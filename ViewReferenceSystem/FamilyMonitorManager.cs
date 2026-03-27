// FamilyMonitorManager.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ViewReferenceSystem.Core;

namespace ViewReferenceSystem.Utilities
{
    /// <summary>
    /// Manages monitored families across the portfolio - publishing, updating, and status tracking
    /// Includes file locking, backup cleanup, and element ownership checks for multi-user safety
    /// </summary>
    public static class FamilyMonitorManager
    {
        #region Constants

        // Default location where the installer puts the family
        private static readonly string[] LOCAL_FAMILY_SEARCH_PATHS = new string[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2024"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2025"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "2026"),
            @"X:\JQProduction\Revit\Program\Program Downloads\Typical Details Addin"
        };

        #endregion

        #region Auto-Publish Suppression

        /// <summary>
        /// When true, family loads are coming from UpdateFamiliesIfNeeded (pulling published versions).
        /// Ribbon's OnDocumentChanged checks this to avoid re-publishing families we're receiving.
        /// </summary>
        public static bool IsSyncingFamilies { get; private set; }

        #endregion

        #region Diagnostic Log

        /// <summary>
        /// Write to the sync diagnostic log file next to the portfolio JSON.
        /// </summary>
        private static void SyncLog(string message, string portfolioPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(message);

                // Skip local log file for Firebase paths — no local folder to write to
                if (string.IsNullOrEmpty(portfolioPath) || FirebaseClient.IsFirebasePath(portfolioPath))
                    return;

                string folder = Path.GetDirectoryName(portfolioPath);
                if (string.IsNullOrEmpty(folder)) return;

                string logPath = Path.Combine(folder, "sync_diagnostic.log");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.AppendAllText(logPath, $"[{timestamp}] [FamilyMgr] {message}\r\n");
            }
            catch { }
        }

        // File access retry settings
        private const int FILE_RETRY_COUNT = 10;
        private const int FILE_RETRY_DELAY_MS = 500;

        #endregion

        #region File Access Utilities

        /// <summary>
        /// Wait for exclusive file access with retry and backoff
        /// Returns true if file is accessible, false if timed out
        /// </summary>
        private static bool WaitForFileAccess(string filePath, int maxRetries = FILE_RETRY_COUNT, int delayMs = FILE_RETRY_DELAY_MS)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        // File is accessible
                        System.Diagnostics.Debug.WriteLine($"   🔓 File accessible on attempt {attempt}: {Path.GetFileName(filePath)}");
                        return true;
                    }
                }
                catch (IOException)
                {
                    if (attempt < maxRetries)
                    {
                        System.Diagnostics.Debug.WriteLine($"   🔒 File locked (attempt {attempt}/{maxRetries}), waiting {delayMs}ms: {Path.GetFileName(filePath)}");
                        Thread.Sleep(delayMs);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"   🚫 Access denied to file: {Path.GetFileName(filePath)}");
                    return false;
                }
            }

            System.Diagnostics.Debug.WriteLine($"   ⏰ File access timed out after {maxRetries * delayMs}ms: {Path.GetFileName(filePath)}");
            return false;
        }

        /// <summary>
        /// Check if a file is currently readable (non-blocking check)
        /// </summary>
        private static bool IsFileReadable(string filePath)
        {
            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Wait for a file to become readable with retry and backoff
        /// Less strict than WaitForFileAccess - only needs read access
        /// </summary>
        private static bool WaitForFileReadable(string filePath, int maxRetries = FILE_RETRY_COUNT, int delayMs = FILE_RETRY_DELAY_MS)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        System.Diagnostics.Debug.WriteLine($"   🔓 File readable on attempt {attempt}: {Path.GetFileName(filePath)}");
                        return true;
                    }
                }
                catch (IOException)
                {
                    if (attempt < maxRetries)
                    {
                        System.Diagnostics.Debug.WriteLine($"   🔒 File locked for read (attempt {attempt}/{maxRetries}), waiting {delayMs}ms: {Path.GetFileName(filePath)}");
                        Thread.Sleep(delayMs);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"   🚫 Access denied to file: {Path.GetFileName(filePath)}");
                    return false;
                }
            }

            System.Diagnostics.Debug.WriteLine($"   ⏰ File read access timed out after {maxRetries * delayMs}ms: {Path.GetFileName(filePath)}");
            return false;
        }

        /// <summary>
        /// Clean up Revit backup files (e.g. family.0001.rfa, family.0002.rfa)
        /// </summary>
        private static void CleanupBackupFiles(string originalFilePath)
        {
            try
            {
                string directory = Path.GetDirectoryName(originalFilePath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalFilePath);
                string extension = Path.GetExtension(originalFilePath);

                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    return;

                // Match patterns like "family.0001.rfa", "family.0002.rfa" etc.
                var backupFiles = Directory.GetFiles(directory, $"{fileNameWithoutExt}.????.{extension.TrimStart('.')}")
                    .Where(f => !string.Equals(f, originalFilePath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Also match patterns like "family.0001.rfa" where extension includes the dot
                var backupFiles2 = Directory.GetFiles(directory, $"{fileNameWithoutExt}.????{extension}")
                    .Where(f => !string.Equals(f, originalFilePath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var allBackups = backupFiles.Union(backupFiles2, StringComparer.OrdinalIgnoreCase).ToList();

                if (allBackups.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"   🧹 Found {allBackups.Count} backup files to clean up");

                    foreach (var backupFile in allBackups)
                    {
                        try
                        {
                            File.Delete(backupFile);
                            System.Diagnostics.Debug.WriteLine($"   🗑️ Deleted backup: {Path.GetFileName(backupFile)}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"   ⚠️ Could not delete backup '{Path.GetFileName(backupFile)}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"   ⚠️ Error during backup cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Safely write JSON to portfolio file with file lock retry
        /// Re-reads the JSON first and applies only our specific changes to minimize race conditions
        /// </summary>
        private static bool SavePortfolioJsonSafe(string portfolioPath, string currentProjectName,
            Action<PortfolioSettings.Portfolio> applyChanges, out string errorMessage)
        {
            errorMessage = "";

            try
            {
                // ── Firebase: no file locking needed — read/modify/write ────
                if (FirebaseClient.IsFirebasePath(portfolioPath))
                {
                    var freshData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);
                    if (freshData == null)
                    {
                        errorMessage = "Could not read portfolio data from Firebase before saving.";
                        return false;
                    }

                    applyChanges(freshData);

                    if (!PortfolioSettings.SavePortfolioToFile(freshData, portfolioPath))
                    {
                        errorMessage = "Could not save portfolio to Firebase.";
                        return false;
                    }

                    return true;
                }

                // ── Local file: use file lock retry ───────────────────────
                if (File.Exists(portfolioPath) && !WaitForFileAccess(portfolioPath))
                {
                    errorMessage = "Portfolio file is locked by another user. Changes will be applied on next sync.";
                    System.Diagnostics.Debug.WriteLine($"   ⚠️ {errorMessage}");
                    return false;
                }

                // RE-READ the JSON right before writing to minimize race window
                var freshLocalData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);
                if (freshLocalData == null)
                {
                    errorMessage = "Could not re-read portfolio data before saving.";
                    return false;
                }

                applyChanges(freshLocalData);

                if (!PortfolioSettings.SavePortfolioToFile(freshLocalData, portfolioPath))
                {
                    errorMessage = "Could not save portfolio file.";
                    return false;
                }

                return true;
            }
            catch (IOException ex)
            {
                errorMessage = $"File I/O error saving portfolio: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"   ❌ {errorMessage}");
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Unexpected error saving portfolio: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"   ❌ {errorMessage}");
                return false;
            }
        }

        #endregion

        #region Element Ownership Utilities

        /// <summary>
        /// Check if an element is owned by another user in a workshared document
        /// Returns the owner's name if owned by someone else, null if available
        /// </summary>
        private static string GetElementOwner(Document doc, ElementId elementId)
        {
            try
            {
                if (!doc.IsWorkshared)
                    return null; // Not workshared = no ownership conflicts

                var checkoutStatus = WorksharingUtils.GetCheckoutStatus(doc, elementId);

                if (checkoutStatus == CheckoutStatus.OwnedByOtherUser)
                {
                    // Get the owner's name
                    string owner = WorksharingUtils.GetWorksharingTooltipInfo(doc, elementId).Owner;
                    return string.IsNullOrEmpty(owner) ? "another user" : owner;
                }

                return null; // Available or owned by us
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"   ⚠️ Error checking element ownership: {ex.Message}");
                return null; // Assume available on error
            }
        }

        /// <summary>
        /// Check if a family element is owned by another user
        /// Shows a TaskDialog warning if it is
        /// Returns true if we can proceed (not owned or we own it), false if blocked
        /// </summary>
        private static bool CheckFamilyOwnership(Document doc, string familyName)
        {
            try
            {
                if (!doc.IsWorkshared)
                    return true; // Not workshared = no conflicts

                Family family = FindFamilyInDocument(doc, familyName);
                if (family == null)
                    return true; // Family doesn't exist yet, loading will create it - no conflict

                string owner = GetElementOwner(doc, family.Id);
                if (owner != null)
                {
                    System.Diagnostics.Debug.WriteLine($"   🚫 Family '{familyName}' is owned by {owner}");

                    TaskDialog.Show("Family Update Skipped",
                        $"Cannot update family '{familyName}' because it is currently checked out by {owner}.\n\n" +
                        $"The update will be retried on your next Sync to Central after {owner} releases the element.");

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"   ⚠️ Error checking family ownership: {ex.Message}");
                return true; // Proceed on error - the transaction will fail if there's a real conflict
            }
        }

        #endregion

        #region Staleness Detection

        /// <summary>
        /// Result of a family staleness check
        /// </summary>
        public class StalenessResult
        {
            public bool IsMonitored { get; set; }
            public bool IsStale { get; set; }
            public bool IsOffline { get; set; }
            public bool HasBeenPublished { get; set; }
            public string PublishedByProject { get; set; }
            public DateTime? LastPublished { get; set; }
            public DateTime ProjectLastSync { get; set; }
            public string Message { get; set; }
        }

        /// <summary>
        /// Check if a family is stale (published by another project after this project last synced).
        /// Called when a family document is opened for editing.
        /// </summary>
        /// <param name="parentProjectDoc">The project document the user was working in before editing the family</param>
        /// <param name="familyName">Name of the family being opened</param>
        public static StalenessResult CheckFamilyStaleness(Document parentProjectDoc, string familyName)
        {
            var result = new StalenessResult { IsMonitored = false, IsStale = false, IsOffline = false };

            try
            {
                if (parentProjectDoc == null || string.IsNullOrEmpty(familyName))
                    return result;

                string portfolioPath = PortfolioSettings.GetJsonPath(parentProjectDoc);
                if (string.IsNullOrEmpty(portfolioPath))
                    return result; // Not a portfolio project

                // Check if we can reach the portfolio
                if (!FirebaseClient.IsFirebasePath(portfolioPath) && !File.Exists(portfolioPath))
                {
                    result.IsOffline = true;
                    result.IsMonitored = true; // Assume it might be — can't verify
                    result.Message = "Cannot verify if this family is up to date — the portfolio file is not accessible. " +
                                     "You may be working offline. Any edits you make may conflict with updates from other users.";
                    return result;
                }

                var portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);
                if (portfolioData == null)
                    return result;

                // Is this family monitored?
                var monitoredFamily = portfolioData.MonitoredFamilies?.FirstOrDefault(f =>
                    string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));

                if (monitoredFamily == null)
                    return result; // Not monitored — no concern

                result.IsMonitored = true;
                result.HasBeenPublished = monitoredFamily.HasBeenPublished;
                result.LastPublished = monitoredFamily.LastPublished;
                result.PublishedByProject = monitoredFamily.PublishedByProject;

                if (!monitoredFamily.HasBeenPublished)
                    return result; // Never published — nothing to be stale against

                // Find the current project in portfolio
                string projectName = PortfolioSettings.GetProjectName(parentProjectDoc);
                string projectGuid = PortfolioSettings.GetProjectGuid(parentProjectDoc);
                string matchMethod;
                var currentProject = PortfolioSettings.FindProjectInPortfolio(
                    portfolioData, projectName, projectGuid, out matchMethod);

                if (currentProject == null)
                    return result; // Project not in portfolio

                result.ProjectLastSync = currentProject.LastSync;

                // Check staleness: was this family published AFTER our last sync?
                if (monitoredFamily.LastPublished.HasValue &&
                    monitoredFamily.LastPublished.Value > currentProject.LastSync)
                {
                    result.IsStale = true;
                    result.Message = $"WARNING: This family was published by '{monitoredFamily.PublishedByProject}' " +
                                     $"on {monitoredFamily.LastPublished.Value:MM/dd/yyyy h:mm tt}, " +
                                     $"but your project last synced on {currentProject.LastSync:MM/dd/yyyy h:mm tt}.\n\n" +
                                     $"Your version may be out of date. Please Sync to Central first to receive the " +
                                     $"latest version, then try editing again.";
                    return result;
                }

                // Also check FamilyUpdateStatus — if false, we haven't loaded the latest
                if (currentProject.FamilyUpdateStatus != null &&
                    currentProject.FamilyUpdateStatus.TryGetValue(familyName, out bool isUpdated) &&
                    !isUpdated)
                {
                    result.IsStale = true;
                    result.Message = $"WARNING: A new version of '{familyName}' is available but hasn't been loaded " +
                                     $"into your project yet.\n\n" +
                                     $"Please Sync to Central first to receive the latest version, then try editing again.";
                    return result;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error checking family staleness: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Check if it's safe to publish (no one published a newer version since our last sync).
        /// Called by PublishFamily before overwriting the .rfa.
        /// Returns true if safe to proceed, false if blocked.
        /// </summary>
        public static bool IsPublishSafe(Document doc, string familyName, out string blockReason)
        {
            blockReason = "";

            try
            {
                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath))
                {
                    blockReason = "Cannot reach the portfolio. Publishing while offline could overwrite newer versions.";
                    return false;
                }

                // Local path: verify file reachable; Firebase: always reachable if auth works
                if (!FirebaseClient.IsFirebasePath(portfolioPath) && !File.Exists(portfolioPath))
                {
                    blockReason = "Cannot reach the portfolio file. You may be offline. " +
                                  "Publishing while offline could overwrite newer versions from other users.";
                    return false;
                }

                var portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);
                if (portfolioData == null)
                    return true; // Can't check — allow

                var monitoredFamily = portfolioData.MonitoredFamilies?.FirstOrDefault(f =>
                    string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));

                if (monitoredFamily == null || !monitoredFamily.HasBeenPublished)
                    return true; // Not published yet — safe to publish

                // Find current project
                string projectName = PortfolioSettings.GetProjectName(doc);
                string projectGuid = PortfolioSettings.GetProjectGuid(doc);
                string matchMethod;
                var currentProject = PortfolioSettings.FindProjectInPortfolio(
                    portfolioData, projectName, projectGuid, out matchMethod);

                if (currentProject == null)
                    return true; // Can't find project — allow

                // Was this family published by SOMEONE ELSE after our last sync?
                if (monitoredFamily.LastPublished.HasValue &&
                    monitoredFamily.LastPublished.Value > currentProject.LastSync &&
                    !string.Equals(monitoredFamily.PublishedByProject, projectName, StringComparison.OrdinalIgnoreCase))
                {
                    blockReason = $"'{familyName}' was published by '{monitoredFamily.PublishedByProject}' " +
                                  $"on {monitoredFamily.LastPublished.Value:MM/dd/yyyy h:mm tt}, " +
                                  $"but your project last synced on {currentProject.LastSync:MM/dd/yyyy h:mm tt}.\n\n" +
                                  $"You may be overwriting a newer version. Sync to Central first to get the latest, " +
                                  $"then make your changes.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error checking publish safety: {ex.Message}");
            }

            return true; // Default: allow
        }

        #endregion

        #region Publish Family

        /// <summary>
        /// Publish a family from the current project to the portfolio's Families folder
        /// Saves a CLEAN family with only the base type (no project-specific types)
        /// Sets all OTHER projects' update status to false
        /// </summary>
        public static bool PublishFamily(Document doc, string familyName, out string errorMessage)
        {
            errorMessage = "";

            try
            {
                System.Diagnostics.Debug.WriteLine($"📤 Publishing family: {familyName}");

                // Get portfolio data
                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath) || (!FirebaseClient.IsFirebasePath(portfolioPath) && !File.Exists(portfolioPath)))
                {
                    errorMessage = "Project is not part of a portfolio.";
                    return false;
                }

                var portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);
                if (portfolioData == null)
                {
                    errorMessage = "Could not load portfolio data.";
                    return false;
                }

                // Find the monitored family
                var monitoredFamily = portfolioData.MonitoredFamilies?.FirstOrDefault(f =>
                    string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));

                if (monitoredFamily == null)
                {
                    errorMessage = $"Family '{familyName}' is not being monitored.";
                    return false;
                }

                // Find the family in the current document
                Family family = FindFamilyInDocument(doc, familyName);
                if (family == null)
                {
                    errorMessage = $"Family '{familyName}' not found in current project.";
                    return false;
                }

                // Ensure Families folder exists
                if (!PortfolioSettings.EnsureFamiliesFolderExists(portfolioPath))
                {
                    errorMessage = "Could not create Families folder.";
                    return false;
                }

                string familiesFolder = PortfolioSettings.GetFamiliesFolderPath(portfolioPath);
                string targetPath = Path.Combine(familiesFolder, monitoredFamily.FileName);

                // SAFETY CHECK: Is it safe to publish? (Did someone publish a newer version since our last sync?)
                string blockReason;
                if (!IsPublishSafe(doc, familyName, out blockReason))
                {
                    errorMessage = blockReason;
                    return false;
                }

                // Check if the target .rfa file is locked by another user (local only)
                if (!FirebaseClient.IsFirebasePath(portfolioPath) &&
                    File.Exists(targetPath) && !WaitForFileAccess(targetPath))
                {
                    errorMessage = $"Family file '{monitoredFamily.FileName}' is locked by another user. Try again shortly.";
                    return false;
                }

                // CRITICAL: EditFamily must be called OUTSIDE of any transaction
                Document familyDoc = null;
                try
                {
                    familyDoc = doc.EditFamily(family);
                    if (familyDoc == null)
                    {
                        errorMessage = "Could not open family for editing.";
                        return false;
                    }

                    // CLEAN THE FAMILY - remove all types except the base type
                    CleanFamilyTypes(familyDoc);

                    // Save family — for Firebase, save to temp then upload; for local, save directly
                    string tempFamilyPath = null;
                    try
                    {
                        if (FirebaseClient.IsFirebasePath(portfolioPath))
                        {
                            // Save to temp location first, then upload to Firebase Storage
                            tempFamilyPath = Path.Combine(Path.GetTempPath(), monitoredFamily.FileName);
                            targetPath = tempFamilyPath;
                        }

                        SaveAsOptions saveOptions = new SaveAsOptions();
                        saveOptions.OverwriteExistingFile = true;
                        saveOptions.MaximumBackups = 1;
                        familyDoc.SaveAs(targetPath, saveOptions);

                        System.Diagnostics.Debug.WriteLine($"   ✅ Clean family saved to: {targetPath}");

                        // Upload to Firebase Storage if needed
                        if (FirebaseClient.IsFirebasePath(portfolioPath) && tempFamilyPath != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"   ⬆️ Uploading {monitoredFamily.FileName} to Firebase Storage...");
                            FirebaseClient.UploadFamily(tempFamilyPath, monitoredFamily.FileName, portfolioPath);
                            System.Diagnostics.Debug.WriteLine($"   ✅ Uploaded to Firebase Storage");
                        }
                    }
                    catch (Exception ex)
                    {
                        errorMessage = $"Error exporting family: {ex.Message}";
                        return false;
                    }
                    finally
                    {
                        // Always close family document WITHOUT saving back to project
                        try { familyDoc?.Close(false); } catch { }

                        // Clean up temp file
                        if (tempFamilyPath != null)
                        {
                            try { File.Delete(tempFamilyPath); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = $"Error exporting family: {ex.Message}";
                    return false;
                }

                // Clean up any backup files (local paths only)
                if (!FirebaseClient.IsFirebasePath(portfolioPath))
                    CleanupBackupFiles(targetPath);

                // Update portfolio data using safe JSON write (re-reads to minimize race condition)
                string currentProjectName = PortfolioSettings.GetProjectName(doc);

                bool jsonSaved = SavePortfolioJsonSafe(portfolioPath, currentProjectName, (freshData) =>
                {
                    // Find the monitored family in the fresh data
                    var freshMonitoredFamily = freshData.MonitoredFamilies?.FirstOrDefault(f =>
                        string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));

                    if (freshMonitoredFamily != null)
                    {
                        freshMonitoredFamily.LastPublished = DateTime.Now;
                        freshMonitoredFamily.PublishedByProject = currentProjectName;
                    }

                    // Set current project to true, all others to false
                    foreach (var project in freshData.ProjectInfos)
                    {
                        if (project.FamilyUpdateStatus == null)
                        {
                            project.FamilyUpdateStatus = new Dictionary<string, bool>();
                        }

                        if (string.Equals(project.ProjectName, currentProjectName, StringComparison.OrdinalIgnoreCase))
                        {
                            project.FamilyUpdateStatus[familyName] = true;
                            System.Diagnostics.Debug.WriteLine($"   ✅ {project.ProjectName}: Set to TRUE (publisher)");
                        }
                        else
                        {
                            project.FamilyUpdateStatus[familyName] = false;
                            System.Diagnostics.Debug.WriteLine($"   ⏳ {project.ProjectName}: Set to FALSE (needs update)");
                        }
                    }
                }, out string jsonError);

                if (!jsonSaved)
                {
                    errorMessage = $"Family exported but could not update portfolio data: {jsonError}";
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Family '{familyName}' published successfully");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Unexpected error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ Error publishing family: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clean a family document by removing all types except one base type
        /// This ensures we only publish the family definition, not project-specific types
        /// </summary>
        private static void CleanFamilyTypes(Document familyDoc)
        {
            try
            {
                if (!familyDoc.IsFamilyDocument)
                {
                    System.Diagnostics.Debug.WriteLine("   ⚠️ Document is not a family document");
                    return;
                }

                FamilyManager famMgr = familyDoc.FamilyManager;
                if (famMgr == null)
                {
                    System.Diagnostics.Debug.WriteLine("   ⚠️ Could not get FamilyManager");
                    return;
                }

                // Get all types
                var allTypes = famMgr.Types.Cast<FamilyType>().ToList();
                int originalCount = allTypes.Count;

                System.Diagnostics.Debug.WriteLine($"   🧹 Family has {originalCount} types, cleaning...");

                if (originalCount <= 1)
                {
                    System.Diagnostics.Debug.WriteLine($"   ✅ Family already clean (1 or fewer types)");
                    return;
                }

                // Find a "base" type to keep - prefer one with a simple name
                FamilyType typeToKeep = allTypes.FirstOrDefault(t =>
                    t.Name.Equals(familyDoc.Title, StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals(familyDoc.Title.Replace(".rfa", ""), StringComparison.OrdinalIgnoreCase))
                    ?? allTypes.First();

                System.Diagnostics.Debug.WriteLine($"   📌 Keeping type: {typeToKeep.Name}");

                // Delete all other types
                using (Transaction trans = new Transaction(familyDoc, "Clean Family Types"))
                {
                    trans.Start();

                    int deletedCount = 0;
                    foreach (var famType in allTypes)
                    {
                        if (famType.Name == typeToKeep.Name)
                            continue;

                        try
                        {
                            // Set as current type first (required before delete)
                            famMgr.CurrentType = famType;
                            famMgr.DeleteCurrentType();
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"   ⚠️ Could not delete type '{famType.Name}': {ex.Message}");
                        }
                    }

                    trans.Commit();
                    System.Diagnostics.Debug.WriteLine($"   ✅ Deleted {deletedCount} types, kept 1");
                }

                // Rename the remaining type to match the family name
                using (Transaction trans = new Transaction(familyDoc, "Rename Base Type"))
                {
                    trans.Start();
                    try
                    {
                        famMgr.CurrentType = typeToKeep;
                        string baseName = familyDoc.Title.Replace(".rfa", "");
                        if (!typeToKeep.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                        {
                            famMgr.RenameCurrentType(baseName);
                            System.Diagnostics.Debug.WriteLine($"   ✅ Renamed type to: {baseName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"   ⚠️ Could not rename type: {ex.Message}");
                    }
                    trans.Commit();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"   ❌ Error cleaning family types: {ex.Message}");
            }
        }

        #endregion

        #region Update Families (called during sync)

        /// <summary>
        /// Check and update any families that need updating for the current project
        /// Called silently during sync-to-central
        /// </summary>
        public static bool UpdateFamiliesIfNeeded(Document doc)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔄 Checking for family updates...");

                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath) || (!FirebaseClient.IsFirebasePath(portfolioPath) && !File.Exists(portfolioPath)))
                {
                    System.Diagnostics.Debug.WriteLine("   ⏭️ No portfolio configured, skipping family check");
                    return false;
                }

                var portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);
                if (portfolioData == null)
                {
                    System.Diagnostics.Debug.WriteLine("   ⏭️ Could not load portfolio, skipping family check");
                    return false;
                }

                string currentProjectName = PortfolioSettings.GetProjectName(doc);
                string currentProjectGuid = PortfolioSettings.GetProjectGuid(doc);
                SyncLog($"  FamilyUpdate: Name='{currentProjectName}', GUID='{currentProjectGuid}'", portfolioPath);

                // Use shared matching logic (GUID → Name → Fuzzy)
                string matchMethod;
                var currentProject = PortfolioSettings.FindProjectInPortfolio(
                    portfolioData, currentProjectName, currentProjectGuid, out matchMethod);

                if (currentProject != null)
                {
                    SyncLog($"  FamilyUpdate: MATCHED by {matchMethod} → '{currentProject.ProjectName}'", portfolioPath);
                }
                else
                {
                    SyncLog($"  FamilyUpdate: ❌ NO MATCH — skipping family check", portfolioPath);
                    SyncLog($"  Projects in JSON:", portfolioPath);
                    foreach (var p in portfolioData.ProjectInfos ?? new List<PortfolioSettings.PortfolioProject>())
                    {
                        SyncLog($"    - Name='{p.ProjectName}', GUID='{p.ProjectGuid}'", portfolioPath);
                    }
                    System.Diagnostics.Debug.WriteLine("   ⏭️ Project not in portfolio, skipping family check");
                    return false;
                }

                if (currentProject.FamilyUpdateStatus == null)
                {
                    currentProject.FamilyUpdateStatus = new Dictionary<string, bool>();
                }

                bool anyUpdated = false;
                var skippedFamilies = new List<string>();
                bool isFirebase = FirebaseClient.IsFirebasePath(portfolioPath);
                string familiesFolder = isFirebase ? portfolioPath : PortfolioSettings.GetFamiliesFolderPath(portfolioPath);

                // Suppress auto-publish while loading families from the network
                // (otherwise DocumentChanged → auto-publish creates an infinite loop)
                IsSyncingFamilies = true;
                try
                {
                    foreach (var monitoredFamily in portfolioData.MonitoredFamilies ?? new List<PortfolioSettings.MonitoredFamily>())
                    {
                        // Check if this project needs the update
                        bool needsUpdate = false;

                        if (!currentProject.FamilyUpdateStatus.TryGetValue(monitoredFamily.FamilyName, out bool isUpdated))
                        {
                            // Not in dictionary = needs update
                            needsUpdate = true;
                        }
                        else
                        {
                            needsUpdate = !isUpdated;
                        }

                        if (!needsUpdate)
                        {
                            System.Diagnostics.Debug.WriteLine($"   ✅ {monitoredFamily.FamilyName}: Already up to date");
                            continue;
                        }

                        // Check if the family has ever been published
                        if (!monitoredFamily.HasBeenPublished)
                        {
                            System.Diagnostics.Debug.WriteLine($"   ⏭️ {monitoredFamily.FamilyName}: Never published, skipping");
                            continue;
                        }

                        // Try to update the family
                        string tempDownloadPath = null;
                        string networkFamilyPath;

                        if (isFirebase)
                        {
                            // Download from Firebase Storage to temp
                            System.Diagnostics.Debug.WriteLine($"   ⬇️ {monitoredFamily.FamilyName}: Downloading from Firebase Storage...");
                            tempDownloadPath = FirebaseClient.DownloadFamily(monitoredFamily.FileName, portfolioPath);
                            if (tempDownloadPath == null)
                            {
                                System.Diagnostics.Debug.WriteLine($"   ⚠️ {monitoredFamily.FamilyName}: Not found in Firebase Storage");
                                continue;
                            }
                            networkFamilyPath = tempDownloadPath;
                        }
                        else
                        {
                            networkFamilyPath = Path.Combine(familiesFolder, monitoredFamily.FileName);

                            if (!File.Exists(networkFamilyPath))
                            {
                                System.Diagnostics.Debug.WriteLine($"   ⚠️ {monitoredFamily.FamilyName}: Network file not found at {networkFamilyPath}");
                                continue;
                            }

                            // Check if the .rfa file is readable (another user might be mid-publish)
                            if (!WaitForFileReadable(networkFamilyPath))
                            {
                                System.Diagnostics.Debug.WriteLine($"   ⚠️ {monitoredFamily.FamilyName}: File locked, skipping until next sync");
                                skippedFamilies.Add($"{monitoredFamily.FamilyName} (file locked by another user)");
                                continue;
                            }
                        }

                        try
                        {
                            // Check element ownership before attempting load
                            if (!CheckFamilyOwnership(doc, monitoredFamily.FamilyName))
                            {
                                skippedFamilies.Add($"{monitoredFamily.FamilyName} (owned by another user)");
                                continue;
                            }

                            // Load the family
                            if (LoadFamilyFromPath(doc, networkFamilyPath, monitoredFamily.FamilyName))
                            {
                                currentProject.FamilyUpdateStatus[monitoredFamily.FamilyName] = true;
                                anyUpdated = true;
                                System.Diagnostics.Debug.WriteLine($"   ✅ {monitoredFamily.FamilyName}: Updated successfully");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"   ⚠️ {monitoredFamily.FamilyName}: Failed to load");
                                skippedFamilies.Add($"{monitoredFamily.FamilyName} (load failed)");
                            }
                        }
                        finally
                        {
                            // Clean up temp download file (Firebase only)
                            if (tempDownloadPath != null)
                            {
                                try { File.Delete(tempDownloadPath); } catch { }
                            }
                        }
                    }

                    // Save portfolio if any updates were made - use safe write
                    if (anyUpdated)
                    {
                        SavePortfolioJsonSafe(portfolioPath, currentProjectName, (freshData) =>
                        {
                            // Find this project in the fresh data — use shared matching
                            string _m;
                            var freshProject = PortfolioSettings.FindProjectInPortfolio(
                                freshData, currentProjectName, currentProjectGuid, out _m);

                            if (freshProject != null)
                            {
                                if (freshProject.FamilyUpdateStatus == null)
                                {
                                    freshProject.FamilyUpdateStatus = new Dictionary<string, bool>();
                                }

                                // Only update the flags for families we successfully loaded
                                foreach (var kvp in currentProject.FamilyUpdateStatus)
                                {
                                    if (kvp.Value) // Only set true flags (we loaded it)
                                    {
                                        freshProject.FamilyUpdateStatus[kvp.Key] = true;
                                    }
                                }
                            }
                        }, out string _);

                        System.Diagnostics.Debug.WriteLine("   💾 Portfolio updated with new family status");
                    }

                    // Show warning for any skipped families
                    if (skippedFamilies.Count > 0)
                    {
                        string message = "The following family updates were skipped:\n\n";
                        foreach (var skipped in skippedFamilies)
                        {
                            message += $"  • {skipped}\n";
                        }
                        message += "\nThese will be retried on your next Sync to Central.";

                        TaskDialog.Show("Family Updates Skipped", message);
                    }

                    System.Diagnostics.Debug.WriteLine("✅ Family update check complete");
                }
                finally
                {
                    IsSyncingFamilies = false;
                }

                return anyUpdated;
            }
            catch (Exception ex)
            {
                IsSyncingFamilies = false;
                System.Diagnostics.Debug.WriteLine($"❌ Error checking family updates: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load a family from a file path, overwriting if it exists
        /// </summary>
        private static bool LoadFamilyFromPath(Document doc, string familyPath, string familyName)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"📦 LoadFamilyFromPath: {familyName} from {familyPath}");

                using (Transaction trans = new Transaction(doc, $"Update {familyName}"))
                {
                    trans.Start();

                    try
                    {
                        Family loadedFamily = null;
                        bool success = doc.LoadFamily(familyPath, new FamilyLoadOptions(), out loadedFamily);

                        System.Diagnostics.Debug.WriteLine($"   LoadFamily result: success={success}, family={(loadedFamily != null ? loadedFamily.Name : "null")}");

                        if (success || loadedFamily != null)
                        {
                            trans.Commit();
                            return true;
                        }

                        // If LoadFamily returned false, it might already exist - try to find it
                        Family existingFamily = FindFamilyInDocument(doc, familyName);

                        if (existingFamily != null)
                        {
                            // Family exists, try reload with overwrite
                            success = doc.LoadFamily(familyPath, new FamilyLoadOptions(), out loadedFamily);
                            System.Diagnostics.Debug.WriteLine($"   Retry result: success={success}");
                            trans.Commit();
                            return true;
                        }

                        trans.RollBack();
                        return false;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"   ❌ LoadFamily exception: {ex.Message}");
                        if (trans.HasStarted() && !trans.HasEnded())
                        {
                            trans.RollBack();
                        }
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in LoadFamilyFromPath: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Add/Remove Monitored Families

        /// <summary>
        /// Add a new family to be monitored across the portfolio
        /// Immediately publishes from current project
        /// </summary>
        public static bool AddMonitoredFamily(Document doc, string familyName, string fileName, out string errorMessage)
        {
            errorMessage = "";

            try
            {
                System.Diagnostics.Debug.WriteLine($"➕ Adding monitored family: {familyName}");

                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath) || (!FirebaseClient.IsFirebasePath(portfolioPath) && !File.Exists(portfolioPath)))
                {
                    errorMessage = "Project is not part of a portfolio.";
                    return false;
                }

                var portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);
                if (portfolioData == null)
                {
                    errorMessage = "Could not load portfolio data.";
                    return false;
                }

                // Check if already monitored
                bool alreadyExists = portfolioData.MonitoredFamilies?.Any(f =>
                    string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)) ?? false;

                if (alreadyExists)
                {
                    errorMessage = $"Family '{familyName}' is already being monitored.";
                    return false;
                }

                // Verify family exists in current document
                Family family = FindFamilyInDocument(doc, familyName);
                if (family == null)
                {
                    errorMessage = $"Family '{familyName}' not found in current project. Load the family first.";
                    return false;
                }

                // Save portfolio with new monitored family using safe JSON write
                bool jsonSaved = SavePortfolioJsonSafe(portfolioPath, PortfolioSettings.GetProjectName(doc), (freshData) =>
                {
                    // Add to monitored list
                    if (freshData.MonitoredFamilies == null)
                    {
                        freshData.MonitoredFamilies = new List<PortfolioSettings.MonitoredFamily>();
                    }

                    // Double-check it wasn't added by another user in the meantime
                    bool existsInFresh = freshData.MonitoredFamilies.Any(f =>
                        string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));

                    if (!existsInFresh)
                    {
                        freshData.MonitoredFamilies.Add(new PortfolioSettings.MonitoredFamily
                        {
                            FamilyName = familyName,
                            FileName = fileName,
                            LastPublished = null,
                            PublishedByProject = null
                        });
                    }

                    // Initialize status for all projects
                    foreach (var project in freshData.ProjectInfos)
                    {
                        if (project.FamilyUpdateStatus == null)
                        {
                            project.FamilyUpdateStatus = new Dictionary<string, bool>();
                        }
                        project.FamilyUpdateStatus[familyName] = false;
                    }
                }, out string jsonError);

                if (!jsonSaved)
                {
                    errorMessage = $"Could not save portfolio data: {jsonError}";
                    return false;
                }

                // Now publish the family
                if (!PublishFamily(doc, familyName, out string publishError))
                {
                    errorMessage = $"Family added to monitoring but publish failed: {publishError}";
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Family '{familyName}' added and published");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Unexpected error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ Error adding monitored family: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Remove a family from monitoring (does not delete the .rfa file)
        /// </summary>
        public static bool RemoveMonitoredFamily(Document doc, string familyName, out string errorMessage)
        {
            errorMessage = "";

            try
            {
                System.Diagnostics.Debug.WriteLine($"➖ Removing monitored family: {familyName}");

                // Prevent removing the default family
                if (string.Equals(familyName, PortfolioSettings.DEFAULT_MONITORED_FAMILY, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = $"Cannot remove '{PortfolioSettings.DEFAULT_MONITORED_FAMILY}' - it is required.";
                    return false;
                }

                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath) || (!FirebaseClient.IsFirebasePath(portfolioPath) && !File.Exists(portfolioPath)))
                {
                    errorMessage = "Project is not part of a portfolio.";
                    return false;
                }

                // Use safe JSON write
                bool jsonSaved = SavePortfolioJsonSafe(portfolioPath, PortfolioSettings.GetProjectName(doc), (freshData) =>
                {
                    var familyToRemove = freshData.MonitoredFamilies?.FirstOrDefault(f =>
                        string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));

                    if (familyToRemove != null)
                    {
                        freshData.MonitoredFamilies.Remove(familyToRemove);
                    }

                    // Remove from all projects' status dictionaries
                    foreach (var project in freshData.ProjectInfos)
                    {
                        project.FamilyUpdateStatus?.Remove(familyName);
                    }
                }, out string jsonError);

                if (!jsonSaved)
                {
                    errorMessage = $"Could not save portfolio data: {jsonError}";
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Family '{familyName}' removed from monitoring");
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Unexpected error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ Error removing monitored family: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Status Reports

        /// <summary>
        /// Get a report of all monitored families and their status across projects
        /// </summary>
        public static FamilyStatusReport GetFamilyStatusReport(Document doc)
        {
            var report = new FamilyStatusReport();

            try
            {
                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath) || (!FirebaseClient.IsFirebasePath(portfolioPath) && !File.Exists(portfolioPath)))
                {
                    report.ErrorMessage = "Project is not part of a portfolio.";
                    return report;
                }

                var portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);
                if (portfolioData == null)
                {
                    report.ErrorMessage = "Could not load portfolio data.";
                    return report;
                }

                report.PortfolioName = portfolioData.PortfolioName;

                foreach (var monitoredFamily in portfolioData.MonitoredFamilies ?? new List<PortfolioSettings.MonitoredFamily>())
                {
                    var familyReport = new FamilyStatusReportItem
                    {
                        FamilyName = monitoredFamily.FamilyName,
                        FileName = monitoredFamily.FileName,
                        LastPublished = monitoredFamily.LastPublished,
                        PublishedByProject = monitoredFamily.PublishedByProject,
                        ProjectStatuses = new List<ProjectFamilyStatus>()
                    };

                    foreach (var project in portfolioData.ProjectInfos ?? new List<PortfolioSettings.PortfolioProject>())
                    {
                        bool isUpdated = false;
                        project.FamilyUpdateStatus?.TryGetValue(monitoredFamily.FamilyName, out isUpdated);

                        familyReport.ProjectStatuses.Add(new ProjectFamilyStatus
                        {
                            ProjectName = project.ProjectName,
                            Nickname = project.DisplayNickname,
                            IsUpdated = isUpdated,
                            LastSync = project.LastSync
                        });
                    }

                    report.Families.Add(familyReport);
                }

                report.Success = true;
            }
            catch (Exception ex)
            {
                report.ErrorMessage = $"Error generating report: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ Error generating family status report: {ex.Message}");
            }

            return report;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Find a family in the document by name
        /// </summary>
        private static Family FindFamilyInDocument(Document doc, string familyName)
        {
            try
            {
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                // Exact match first
                var family = families.FirstOrDefault(f =>
                    string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));

                if (family != null)
                    return family;

                // Try partial match for DetailReferenceFamily variations
                string nameLower = familyName.ToLowerInvariant();
                family = families.FirstOrDefault(f =>
                {
                    string fName = f.Name.ToLowerInvariant();
                    return fName.Contains(nameLower) || nameLower.Contains(fName);
                });

                return family;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding family: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the local path to a family from the addins folder
        /// </summary>
        public static string GetLocalFamilyPath(string fileName)
        {
            foreach (string searchPath in LOCAL_FAMILY_SEARCH_PATHS)
            {
                try
                {
                    if (Directory.Exists(searchPath))
                    {
                        string fullPath = Path.Combine(searchPath, fileName);
                        if (File.Exists(fullPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"   📁 Found local family at: {fullPath}");
                            return fullPath;
                        }
                    }
                }
                catch { }
            }

            System.Diagnostics.Debug.WriteLine($"   ⚠️ Local family not found: {fileName}");
            return null;
        }

        /// <summary>
        /// Get list of families in the current document that could be monitored
        /// </summary>
        public static List<string> GetAvailableFamiliesForMonitoring(Document doc)
        {
            var families = new List<string>();

            try
            {
                var allFamilies = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(f => f.FamilyCategory != null)
                    .OrderBy(f => f.Name)
                    .ToList();

                foreach (var family in allFamilies)
                {
                    families.Add(family.Name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error getting available families: {ex.Message}");
            }

            return families;
        }

        /// <summary>
        /// Revit API category names don't always match the Project Browser display names.
        /// This maps API names to what users see in Revit's UI.
        /// </summary>
        private static readonly Dictionary<string, string> CategoryDisplayNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Generic Annotations", "Annotation Symbols" },
            { "Elevation Marks", "Elevation Symbols" },
            { "Brace in Plan View Symbols", "Brace Symbols" },
            { "Span Direction Symbol", "Span Direction Symbols" },
            { "Spot Elevation Symbols", "Spot Elevations" },
            { "Rebar Shape", "Rebar Shapes" }
        };

        /// <summary>
        /// Get families grouped by category, matching Revit's Project Browser structure.
        /// Category names are remapped to match what users see in Revit's UI.
        /// Returns Dictionary of DisplayCategoryName -> List of FamilyName
        /// </summary>
        public static Dictionary<string, List<string>> GetFamiliesByCategory(Document doc)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var allFamilies = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(f => f.FamilyCategory != null)
                    .OrderBy(f => f.FamilyCategory.Name)
                    .ThenBy(f => f.Name)
                    .ToList();

                foreach (var family in allFamilies)
                {
                    string apiName = family.FamilyCategory.Name;

                    // Remap to Project Browser display name if available
                    string displayName;
                    if (!CategoryDisplayNameMap.TryGetValue(apiName, out displayName))
                    {
                        displayName = apiName;
                    }

                    if (!result.ContainsKey(displayName))
                    {
                        result[displayName] = new List<string>();
                    }

                    result[displayName].Add(family.Name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error getting families by category: {ex.Message}");
            }

            return result;
        }

        #endregion

        #region Report Classes

        public class FamilyStatusReport
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public string PortfolioName { get; set; }
            public List<FamilyStatusReportItem> Families { get; set; } = new List<FamilyStatusReportItem>();

            public int TotalFamilies => Families.Count;
            public int TotalProjects => Families.FirstOrDefault()?.ProjectStatuses?.Count ?? 0;
            public int TotalOutdated => Families.Sum(f => f.ProjectStatuses?.Count(p => !p.IsUpdated) ?? 0);
        }

        public class FamilyStatusReportItem
        {
            public string FamilyName { get; set; }
            public string FileName { get; set; }
            public DateTime? LastPublished { get; set; }
            public string PublishedByProject { get; set; }
            public List<ProjectFamilyStatus> ProjectStatuses { get; set; } = new List<ProjectFamilyStatus>();

            public int UpdatedCount => ProjectStatuses?.Count(p => p.IsUpdated) ?? 0;
            public int OutdatedCount => ProjectStatuses?.Count(p => !p.IsUpdated) ?? 0;
            public bool AllUpdated => OutdatedCount == 0;
        }

        public class ProjectFamilyStatus
        {
            public string ProjectName { get; set; }
            public string Nickname { get; set; }
            public bool IsUpdated { get; set; }
            public DateTime LastSync { get; set; }

            public string StatusDisplay => IsUpdated ? "✅ Current" : "❌ Needs Update";
        }

        #endregion

        #region Family Load Options

        /// <summary>
        /// Options for loading families with overwrite behavior
        /// </summary>
        private class FamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }

        #endregion
    }
}