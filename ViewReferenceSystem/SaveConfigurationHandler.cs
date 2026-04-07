using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ViewReferenceSystem.Core;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.Utilities;

namespace ViewReferenceSystem.UI
{
    /// <summary>
    /// External event handler for saving portfolio configuration
    /// ENHANCED V3: 
    /// - REPLACES entire project section instead of just adding views
    /// - UPDATES all placed Detail Reference Family instances with latest data
    /// - PRESERVES manually edited top notes during rescan
    /// - RELEASES element ownership after modifications
    /// </summary>
    public class SaveConfigurationHandler : IExternalEventHandler
    {
        private PortfolioSettings.Portfolio _portfolioData;
        private string _portfolioPath;
        private Document _currentDocument;
        private volatile bool _saveCompleted = false;
        private volatile bool _isSuccessful = false;
        private string _errorMessage = "";
        private static string _logPath = Path.Combine(Path.GetTempPath(), "TypicalDetailsPlugin_SaveHandler.log");

        public bool IsCompleted => _saveCompleted;
        public bool IsSuccessful => _isSuccessful;
        public string ErrorMessage => _errorMessage;

        private void Log(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(_logPath, $"[{timestamp}] {message}\n");
                System.Diagnostics.Debug.WriteLine(message);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public void ResetStatus()
        {
            _saveCompleted = false;
            _isSuccessful = false;
            _errorMessage = "";
        }

        public void SetData(PortfolioSettings.Portfolio portfolioData, string portfolioPath)
        {
            _portfolioData = portfolioData;
            _portfolioPath = portfolioPath;
            _currentDocument = null;
        }

        public void SetData(PortfolioSettings.Portfolio portfolioData, string portfolioPath, Document document)
        {
            _portfolioData = portfolioData;
            _portfolioPath = portfolioPath;
            _currentDocument = document;
        }

        public void SetDataParametersOnly(PortfolioSettings.Portfolio portfolioData, string portfolioPath, Document document)
        {
            _portfolioData = portfolioData;
            _portfolioPath = portfolioPath;
            _currentDocument = document;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                _saveCompleted = false;
                _isSuccessful = false;
                _errorMessage = "";

                Log("========================================");
                Log("🚀 ===== SAVE CONFIGURATION START =====");

                // Step 1: Validate inputs
                Log("📋 Step 1: Validating inputs");
                if (!ValidateInputs(app, out UIDocument uidoc, out Document doc))
                {
                    return; // Error already set in ValidateInputs
                }
                Log("   ✅ Inputs validated");

                // Step 2: Add current project to portfolio if not already present
                Log("📋 Step 2: Adding current project to portfolio");
                AddCurrentProjectToPortfolio(doc);
                Log("   ✅ Project added to portfolio");

                // Step 3: REPLACE project views (instead of just adding)
                Log("📋 Step 3: Scanning and REPLACING project views");
                ReplaceProjectViews(doc);
                Log("   ✅ Project views replaced");

                // Step 4: Save JSON file
                Log("📋 Step 4: Saving portfolio JSON file");
                if (!SavePortfolioFile())
                {
                    return; // Error already set in SavePortfolioFile
                }
                Log("   ✅ JSON file saved");

                // Step 5: Validate and update all placed Detail Reference Family instances
                Log("📋 Step 5: Validating and updating placed family instances");
                var validationResult = PortfolioValidator.ValidateAllDetailReferences(doc, _portfolioData);
                Log($"   {validationResult.GetSummary()}");

                // Show warning dialog if there are orphans on views
                if (validationResult.HasOrphansOnViews)
                {
                    ShowOrphanedReferencesWarning(validationResult.OrphanedOnViews);
                }
                Log("   ✅ Family instances validated");

                // Step 6: Update project parameters
                Log("📋 Step 6: Updating project parameters");
                try
                {
                    if (!UpdateProjectParameters(doc))
                    {
                        Log($"   ⚠️ Failed to update project parameters: {_errorMessage}");
                        // Don't return here - JSON is saved, parameters will be updated on next sync
                        // Reset error since this is non-critical
                        _errorMessage = "";
                    }
                    else
                    {
                        Log("   ✅ Project parameters updated");
                    }
                }
                catch (Exception ex)
                {
                    // Parameter update is non-critical, log but don't fail
                    Log($"⚠️ Step 6 warning (Update parameters): {ex.Message}");
                }

                // Step 7: Relinquish ownership of borrowed elements
                Log("📋 Step 7: Relinquishing element ownership");
                try
                {
                    RelinquishBorrowedElements(doc);
                    Log("   ✅ Element ownership relinquished");
                }
                catch (Exception ex)
                {
                    // Non-critical - elements will be relinquished on next sync
                    Log($"⚠️ Step 7 warning (Relinquish ownership): {ex.Message}");
                }

                // Success
                _isSuccessful = true;
                _errorMessage = "";

                Log("✅ ===== SAVE CONFIGURATION COMPLETE =====");
            }
            catch (Exception ex)
            {
                _errorMessage = $"Unexpected error in Execute: {ex.Message}";
                _isSuccessful = false;
                Log($"❌ Save configuration failed: {ex.Message}");
                Log($"❌ Stack trace: {ex.StackTrace}");
            }
            finally
            {
                _saveCompleted = true;
                Log($"📊 Execute completed: Success={_isSuccessful}, Error='{_errorMessage}'");
                Log("========================================\n");
            }
        }

        public string GetName()
        {
            return "SaveConfigurationHandler";
        }

        #region Private Methods

        /// <summary>
        /// Relinquish ownership of all borrowed elements after our modifications
        /// This prevents keeping elements checked out unnecessarily
        /// </summary>
        private void RelinquishBorrowedElements(Document doc)
        {
            try
            {
                // Only applies to workshared documents
                if (!doc.IsWorkshared)
                {
                    Log("   Document is not workshared - skipping relinquish");
                    return;
                }

                // Check if we can relinquish (document must not be read-only)
                if (doc.IsReadOnly)
                {
                    Log("   Document is read-only - skipping relinquish");
                    return;
                }

                // Create relinquish options - relinquish everything we borrowed
                var relinquishOptions = new RelinquishOptions(true);
                relinquishOptions.UserWorksets = true;           // Release user-created worksets we modified
                relinquishOptions.FamilyWorksets = true;         // Release family worksets (for type changes)
                relinquishOptions.ViewWorksets = true;           // Release view worksets
                relinquishOptions.StandardWorksets = true;       // Release standard worksets
                relinquishOptions.CheckedOutElements = true;     // Release individual elements we checked out

                // Create transact options - don't save local before relinquishing
                var transactOptions = new TransactWithCentralOptions();

                Log("   Calling WorksharingUtils.RelinquishOwnership...");

                // Perform the relinquish (returns void in some Revit versions)
                WorksharingUtils.RelinquishOwnership(doc, relinquishOptions, transactOptions);

                Log("   ✅ Relinquish complete");
            }
            catch (Autodesk.Revit.Exceptions.CentralModelContentionException ex)
            {
                // Another user has changes pending - this is OK, just log it
                Log($"   ⚠️ Central model contention during relinquish (this is OK): {ex.Message}");
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                // Model may have been modified since last sync - also OK
                Log($"   ⚠️ Invalid operation during relinquish: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Log but don't fail - elements will be relinquished on next sync to central
                Log($"   ⚠️ Error during relinquish (non-critical): {ex.Message}");
            }
        }

        private bool ValidateInputs(UIApplication app, out UIDocument uidoc, out Document doc)
        {
            uidoc = null;
            doc = null;

            if (_portfolioData == null)
            {
                _errorMessage = "Portfolio data is null";
                _saveCompleted = true;
                return false;
            }

            if (string.IsNullOrEmpty(_portfolioPath))
            {
                _errorMessage = "Portfolio path is null or empty";
                _saveCompleted = true;
                return false;
            }

            // Get the active document
            uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                _errorMessage = "No active document";
                _saveCompleted = true;
                return false;
            }

            doc = uidoc.Document;
            if (doc == null)
            {
                _errorMessage = "Document is null";
                _saveCompleted = true;
                return false;
            }

            return true;
        }

        private void AddCurrentProjectToPortfolio(Document doc)
        {
            string projectName = PortfolioSettings.GetProjectName(doc);
            string projectGuid = PortfolioSettings.GetProjectGuid(doc);

            // Generate GUID if it doesn't exist
            if (string.IsNullOrEmpty(projectGuid))
            {
                projectGuid = Guid.NewGuid().ToString();
            }

            // Ensure ProjectInfos list exists
            if (_portfolioData.ProjectInfos == null)
            {
                _portfolioData.ProjectInfos = new List<PortfolioSettings.PortfolioProject>();
            }

            // Match by GUID first (survives ACC project renames)
            var existingProject = _portfolioData.ProjectInfos.FirstOrDefault(p =>
                !string.IsNullOrEmpty(p.ProjectGuid) &&
                string.Equals(p.ProjectGuid, projectGuid, StringComparison.OrdinalIgnoreCase));

            // Fall back to name match for entries that predate GUID support
            if (existingProject == null)
            {
                existingProject = _portfolioData.ProjectInfos.FirstOrDefault(p =>
                    string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));

                // If found by name but no GUID, stamp the GUID on it now
                if (existingProject != null && string.IsNullOrEmpty(existingProject.ProjectGuid))
                {
                    existingProject.ProjectGuid = projectGuid;
                    Log($"      Stamped GUID on existing project: {projectName}");
                }
            }

            if (existingProject == null)
            {
                Log($"      Adding new project: {projectName}");
                _portfolioData.ProjectInfos.Add(new PortfolioSettings.PortfolioProject
                {
                    ProjectName = projectName,
                    ProjectGuid = projectGuid,
                    Nickname = projectName,
                    IsTypicalDetailsAuthority = false,
                    LastSync = DateTime.Now,
                    LastSyncUser = _currentDocument?.Application?.Username,
                    Status = "active"
                });
            }
            else
            {
                // Update name in case it was renamed on ACC — GUID is the stable ID
                if (!string.Equals(existingProject.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"      Project renamed: '{existingProject.ProjectName}' → '{projectName}' (GUID unchanged)");
                    existingProject.ProjectName = projectName;
                }

                existingProject.ProjectGuid = projectGuid;
                existingProject.LastSync = DateTime.Now;
                existingProject.LastSyncUser = _currentDocument?.Application?.Username;
                Log($"      Updated existing project: {projectName}");
            }
        }

        private void ReplaceProjectViews(Document doc)
        {
            string currentProjectName = PortfolioSettings.GetProjectName(doc);
            string currentProjectGuid = PortfolioSettings.GetProjectGuid(doc);

            // Generate GUID now if missing (shouldn't happen, but be safe)
            if (string.IsNullOrEmpty(currentProjectGuid))
            {
                currentProjectGuid = Guid.NewGuid().ToString();
                Log($"   ⚠️ No project GUID found — generated new one: {currentProjectGuid}");
            }

            Log($"   Current project: {currentProjectName} ({currentProjectGuid})");

            // STEP 1: Cache ALL existing views from this project (to preserve TopNotes and Descriptions)
            // Match by GUID (preferred) OR name (legacy support for views before GUID was added)
            var existingViewsCache = new Dictionary<int, ViewInfo>();
            var viewsFromThisProject = _portfolioData.Views
                .Where(v =>
                    (!string.IsNullOrEmpty(currentProjectGuid) && string.Equals(v.SourceProjectGuid, currentProjectGuid, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(v.SourceProjectName, currentProjectName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var view in viewsFromThisProject)
            {
                if (!existingViewsCache.ContainsKey(view.ViewId))
                {
                    existingViewsCache[view.ViewId] = view;
                }
            }

            Log($"   📋 Cached {existingViewsCache.Count} existing views for preservation");

            // STEP 2: REMOVE all views from this project from the portfolio
            // Match by GUID OR name to catch any renamed-project views
            int removedCount = _portfolioData.Views.RemoveAll(v =>
                (!string.IsNullOrEmpty(currentProjectGuid) && string.Equals(v.SourceProjectGuid, currentProjectGuid, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(v.SourceProjectName, currentProjectName, StringComparison.OrdinalIgnoreCase));

            Log($"   🗑️ Removed {removedCount} existing views from project '{currentProjectName}'");

            // STEP 3: Scan and ADD all currently sheet-placed views
            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet)
                .ToList();

            Log($"   📊 Found {allViews.Count} total views to process");

            int viewsAdded = 0;
            int viewsSkipped = 0;
            int templateViews = 0;
            int notOnSheetViews = 0;
            int topNotesPreserved = 0;

            foreach (var view in allViews)
            {
                try
                {
                    if (view.IsTemplate)
                    {
                        templateViews++;
                        continue;
                    }

                    // Check if view is placed on a sheet
                    var viewport = new FilteredElementCollector(doc)
                        .OfClass(typeof(Viewport))
                        .Cast<Viewport>()
                        .FirstOrDefault(vp => vp.ViewId == view.Id);

                    if (viewport == null)
                    {
                        notOnSheetViews++;
                        continue;
                    }

                    // Get sheet info
                    var sheet = doc.GetElement(viewport.SheetId) as ViewSheet;
                    if (sheet == null)
                    {
                        viewsSkipped++;
                        continue;
                    }

                    string sheetNumber = sheet.SheetNumber ?? "";
                    string sheetName = sheet.Name ?? "";
                    string detailNumber = viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() ?? "";

                    // Check if we have existing data to preserve
                    existingViewsCache.TryGetValue(view.Id.IntegerValue, out ViewInfo existingView);

                    // PRIORITY ORDER for TopNote:
                    // 1. Existing JSON value (preserves user edits made in Portfolio Manager)
                    // 2. Revit "Top Note" shared parameter (matches what Ribbon.cs reads)
                    // 3. Empty string
                    string currentTopNote = "";

                    // FIRST: Always preserve existing JSON value if it exists
                    // This prevents other users' syncs from overwriting edits made via Portfolio Manager
                    if (existingView != null && !string.IsNullOrEmpty(existingView.TopNote))
                    {
                        currentTopNote = existingView.TopNote;
                    }
                    else
                    {
                        // SECOND: Try to read from Revit "Top Note" shared parameter
                        // (same parameter that Ribbon.cs uses via GetViewTopNote)
                        try
                        {
                            var topNoteParam = view.LookupParameter("Top Note");
                            if (topNoteParam != null && topNoteParam.HasValue)
                            {
                                currentTopNote = topNoteParam.AsString() ?? "";
                            }
                        }
                        catch { }
                    }

                    // Create ViewInfo
                    var viewInfo = new ViewInfo
                    {
                        ViewId = view.Id.IntegerValue,
                        ViewName = view.Name ?? "",
                        ViewType = view.ViewType.ToString(),
                        DetailNumber = detailNumber,
                        SheetNumber = sheetNumber,
                        SheetName = sheetName,
                        SourceProjectName = currentProjectName,
                        SourceProjectGuid = currentProjectGuid,
                        TopNote = currentTopNote,
                        LastModified = DateTime.Now,

                    };

                    if (!string.IsNullOrEmpty(currentTopNote))
                    {
                        topNotesPreserved++;
                    }

                    _portfolioData.Views.Add(viewInfo);
                    viewsAdded++;

                    Log($"         ✅ Added: Sheet {sheetNumber}, Detail {detailNumber}, View: {view.Name}");
                }
                catch (Exception ex)
                {
                    Log($"      ⚠️ Error processing view '{view?.Name}': {ex.Message}");
                    viewsSkipped++;
                }
            }

            Log($"   📊 SUMMARY: Added {viewsAdded}, Removed {removedCount}, Skipped {viewsSkipped}");
            Log($"      (Templates: {templateViews}, Not on sheets: {notOnSheetViews})");
            Log($"      ✅ Preserved {topNotesPreserved} top notes");
        }

        /// <summary>
        /// Show warning dialog for orphaned references that are placed on views
        /// </summary>
        private void ShowOrphanedReferencesWarning(List<PortfolioValidator.OrphanedReferenceInfo> orphans)
        {
            try
            {
                Log($"   ⚠️ Showing orphan warning dialog for {orphans.Count} references");

                string message = "The following detail references no longer exist in the portfolio:\n\n";

                foreach (var orphan in orphans.Take(20)) // Limit to 20 to avoid huge dialog
                {
                    message += $"  • {orphan.DisplayText}\n";
                }

                if (orphans.Count > 20)
                {
                    message += $"\n  ... and {orphans.Count - 20} more\n";
                }

                message += "\nPlease review and remove these references manually.";

                MessageBox.Show(
                    message,
                    "Orphaned Detail References",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log($"   ⚠️ Error showing orphan warning: {ex.Message}");
            }
        }

        private bool SavePortfolioFile()
        {
            try
            {
                Log($"   Saving to: {_portfolioPath}");

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(_portfolioData, Newtonsoft.Json.Formatting.Indented);

                if (FirebaseClient.IsFirebasePath(_portfolioPath))
                {
                    FirebaseClient.WritePortfolio(_portfolioPath, json);
                }
                else
                {
                    string directory = Path.GetDirectoryName(_portfolioPath);
                    if (!Directory.Exists(directory))
                    {
                        Log($"   Creating directory: {directory}");
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllText(_portfolioPath, json);
                }

                Log($"   ✅ JSON file saved successfully ({json.Length} bytes)");
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                _errorMessage = $"Access denied saving portfolio file: {ex.Message}\n\nCheck file permissions.";
                _saveCompleted = true;
                return false;
            }
            catch (IOException ex)
            {
                _errorMessage = $"File I/O error saving portfolio: {ex.Message}";
                _saveCompleted = true;
                return false;
            }
            catch (Exception ex)
            {
                _errorMessage = $"Error saving portfolio data: {ex.Message}";
                _saveCompleted = true;
                return false;
            }
        }

        private bool UpdateProjectParameters(Document doc)
        {
            try
            {
                Log("   📝 UpdateProjectParameters: Starting...");

                if (doc.IsReadOnly)
                {
                    _errorMessage = "Document is read-only, cannot update parameters";
                    Log($"   ⚠️ {_errorMessage}");
                    return false;
                }

                using (var transaction = new Transaction(doc, "Update Portfolio Parameters"))
                {
                    transaction.Start();
                    Log("      Transaction started");

                    try
                    {
                        Log($"      Setting ProjectType to: {PortfolioSettings.PROJECT_TYPE_PORTFOLIO}");
                        PortfolioSettings.SetProjectType(doc, PortfolioSettings.PROJECT_TYPE_PORTFOLIO);

                        Log($"      Setting JsonPath to: {_portfolioPath}");
                        PortfolioSettings.SetJsonPath(doc, _portfolioPath);

                        Log($"      Setting PortfolioGuid to: {_portfolioData.PortfolioGuid}");
                        PortfolioSettings.SetPortfolioGuid(doc, _portfolioData.PortfolioGuid);

                        Log("      Setting LastSyncTimestamp...");
                        PortfolioSettings.SetLastSyncTimestamp(doc, DateTime.Now);

                        // Also set a unique project GUID if it doesn't exist
                        string existingGuid = PortfolioSettings.GetProjectGuid(doc);
                        if (string.IsNullOrEmpty(existingGuid))
                        {
                            string newGuid = Guid.NewGuid().ToString();
                            Log($"      Setting new ProjectGuid to: {newGuid}");
                            PortfolioSettings.SetProjectGuid(doc, newGuid);
                        }
                        else
                        {
                            Log($"      ProjectGuid already exists: {existingGuid}");
                        }

                        Log("      Committing transaction...");
                        transaction.Commit();

                        Log("   ✅ All project parameters set successfully!");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log($"   ❌ Error in transaction: {ex.Message}");
                        transaction.RollBack();
                        throw; // Re-throw to be caught by outer try-catch
                    }
                }
            }
            catch (Exception ex)
            {
                _errorMessage = $"Error updating project parameters: {ex.Message}";
                Log($"   ❌ Error setting parameters: {ex.Message}");
                Log($"      Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        #endregion
    }
}