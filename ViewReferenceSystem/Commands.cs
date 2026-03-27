using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ViewReferenceSystem.Core;
using ViewReferenceSystem.UI;
using PortfolioSettings = ViewReferenceSystem.Core.PortfolioSettings;

namespace ViewReferenceSystem.Commands
{
    /// <summary>
    /// Portfolio Setup Command - Opens the Portfolio Setup window
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PortfolioSetupCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uidoc = uiApp.ActiveUIDocument;
                Document doc = uidoc?.Document;

                if (doc == null)
                {
                    TaskDialog.Show("No Document", "No active Revit document found.");
                    return Result.Cancelled;
                }

                if (doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Family Document", "Portfolio Setup is not available for family documents.");
                    return Result.Cancelled;
                }

                // Show setup window
                var setupWindow = new PortfolioSetupWindow(doc, null);
                setupWindow.ShowDialog();

                // After window closes, if setup was completed, configure parameters
                if (setupWindow.WasSetupCompleted && setupWindow.WasSetupSuccessful)
                {
                    System.Diagnostics.Debug.WriteLine("📋 Commands.cs: Setup completed successfully, configuring parameters...");

                    // Get the portfolio path that was just saved
                    string portfolioPath = setupWindow.GetPortfolioFilePath();

                    bool pathValid = !string.IsNullOrEmpty(portfolioPath) &&
                        (FirebaseClient.IsFirebasePath(portfolioPath) || File.Exists(portfolioPath));

                    if (!pathValid)
                    {
                        TaskDialog.Show("Configuration Error", $"Portfolio file path not found: {portfolioPath}");
                        return Result.Failed;
                    }

                    System.Diagnostics.Debug.WriteLine($"📋 Portfolio path: {portfolioPath}");

                    // Load portfolio data (handles both Firebase and local paths)
                    var portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);

                    if (portfolioData == null)
                    {
                        TaskDialog.Show("Configuration Error", "Failed to load portfolio data.");
                        return Result.Failed;
                    }

                    System.Diagnostics.Debug.WriteLine($"📋 Portfolio loaded: {portfolioData.PortfolioName}");

                    string projectName = PortfolioSettings.GetProjectName(doc);
                    var currentProject = portfolioData.ProjectInfos?.FirstOrDefault(p =>
                        string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));

                    // ✅ FIX: Create parameters FIRST (this was missing!)
                    System.Diagnostics.Debug.WriteLine("📋 Creating project parameters...");
                    bool parametersCreated = PortfolioSettings.CreateProjectParameters(doc);

                    if (!parametersCreated)
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ Warning: CreateProjectParameters returned false, attempting to set anyway...");
                    }

                    // Configure parameters
                    using (Transaction trans = new Transaction(doc, "Configure Portfolio Parameters"))
                    {
                        trans.Start();

                        try
                        {
                            System.Diagnostics.Debug.WriteLine("📋 Setting parameter values...");

                            PortfolioSettings.SetProjectType(doc, PortfolioSettings.PROJECT_TYPE_PORTFOLIO);
                            System.Diagnostics.Debug.WriteLine($"   ✅ Set ProjectType = {PortfolioSettings.PROJECT_TYPE_PORTFOLIO}");

                            PortfolioSettings.SetJsonPath(doc, portfolioPath);
                            System.Diagnostics.Debug.WriteLine($"   ✅ Set JsonPath = {portfolioPath}");

                            PortfolioSettings.SetPortfolioGuid(doc, portfolioData.PortfolioGuid);
                            System.Diagnostics.Debug.WriteLine($"   ✅ Set PortfolioGuid = {portfolioData.PortfolioGuid}");

                            PortfolioSettings.SetLastSyncTimestamp(doc, DateTime.Now);
                            System.Diagnostics.Debug.WriteLine($"   ✅ Set LastSync = {DateTime.Now}");

                            string existingGuid = PortfolioSettings.GetProjectGuid(doc);
                            if (string.IsNullOrEmpty(existingGuid))
                            {
                                string newGuid = Guid.NewGuid().ToString();
                                PortfolioSettings.SetProjectGuid(doc, newGuid);
                                System.Diagnostics.Debug.WriteLine($"   ✅ Set ProjectGuid = {newGuid}");
                            }

                            trans.Commit();
                            System.Diagnostics.Debug.WriteLine("📋 ✅ Parameters configured successfully!");
                        }
                        catch (Exception ex)
                        {
                            trans.RollBack();
                            System.Diagnostics.Debug.WriteLine($"❌ Error setting parameters: {ex.Message}");
                            TaskDialog.Show("Configuration Error", $"Error setting parameters:\n\n{ex.Message}");
                            return Result.Failed;
                        }
                    }

                    // Call SaveConfigurationHandler to scan views
                    var saveHandler = PortfolioManagePane.GetSaveHandler();
                    var saveEvent = PortfolioManagePane.GetSaveExternalEvent();

                    if (saveHandler != null && saveEvent != null)
                    {
                        System.Diagnostics.Debug.WriteLine("📋 Raising save event to scan views...");
                        saveHandler.SetData(portfolioData, portfolioPath, doc);
                        saveHandler.ResetStatus();
                        saveEvent.Raise();
                    }

                    // Refresh the portfolio pane
                    PortfolioManagePane.RefreshCurrentPane();

                    // Reopen the window to show the result
                    var confirmWindow = new PortfolioSetupWindow(doc, null);
                    confirmWindow.ShowDialog();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"📋 Setup not completed or not successful. Completed={setupWindow.WasSetupCompleted}, Successful={setupWindow.WasSetupSuccessful}");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Portfolio Setup Error: {ex.Message}\n{ex.StackTrace}");
                TaskDialog.Show("Error", $"Portfolio Setup Error:\n\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Portfolio Manager Command - Opens the Portfolio Manager dockable pane
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PortfolioManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uidoc = uiApp.ActiveUIDocument;
                Document doc = uidoc?.Document;

                if (doc == null)
                {
                    TaskDialog.Show("No Document", "No active Revit document found.");
                    return Result.Cancelled;
                }

                if (doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Family Document", "Portfolio Manager is not available for family documents.");
                    return Result.Cancelled;
                }

                // Show the dockable pane
                DockablePaneId paneId = PortfolioManagePane.GetDockablePaneId();
                DockablePane pane = uiApp.GetDockablePane(paneId);

                if (pane != null)
                {
                    pane.Show();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Portfolio Manager Error:\n\n{ex.Message}");
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Load Family Command - Loads/reloads the DetailReferenceFamily from the addins folder
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class LoadFamilyCommand : IExternalCommand
    {
        private static readonly string[] FAMILY_NAMES_TO_CHECK = {
            "DetailReferenceFamily",
            "Detail Reference Family",
            "DetailReference"
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uidoc = uiApp.ActiveUIDocument;
                Document doc = uidoc?.Document;

                if (doc == null)
                {
                    TaskDialog.Show("No Document", "No active Revit document found.");
                    return Result.Cancelled;
                }

                if (doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Family Document", "Cannot load families into a family document.");
                    return Result.Cancelled;
                }

                // Find the family file in the addins folder
                string addinPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string familyPath = Path.Combine(addinPath, "DetailReferenceFamily.rfa");

                System.Diagnostics.Debug.WriteLine($"🔍 Looking for family at: {familyPath}");

                if (!File.Exists(familyPath))
                {
                    TaskDialog.Show("Family Not Found",
                        $"DetailReferenceFamily.rfa was not found in the addins folder.\n\n" +
                        $"Expected location:\n{familyPath}\n\n" +
                        "Please run the installer again to ensure the family file is properly installed.");
                    return Result.Failed;
                }

                // Check if family already exists in the project
                Family existingFamily = FindExistingFamily(doc);
                string actionDescription = existingFamily != null ? "Reload" : "Load";

                System.Diagnostics.Debug.WriteLine($"📋 {actionDescription}ing DetailReferenceFamily...");

                // Load/reload the family
                using (Transaction trans = new Transaction(doc, $"{actionDescription} DetailReferenceFamily"))
                {
                    trans.Start();

                    try
                    {
                        Family loadedFamily = null;
                        bool success = false;

                        if (existingFamily != null)
                        {
                            // Family exists - reload it with overwrite
                            System.Diagnostics.Debug.WriteLine("   Existing family found, reloading with overwrite...");
                            success = doc.LoadFamily(familyPath, new FamilyLoadOptions(), out loadedFamily);

                            // If reload returned false, use existing family reference
                            if (!success || loadedFamily == null)
                            {
                                loadedFamily = existingFamily;
                                success = true;
                            }
                        }
                        else
                        {
                            // Family doesn't exist - load it fresh
                            System.Diagnostics.Debug.WriteLine("   No existing family, loading fresh...");
                            success = doc.LoadFamily(familyPath, out loadedFamily);
                        }

                        if (success && loadedFamily != null)
                        {
                            // Ensure base type exists
                            bool baseTypeCreated = EnsureBaseTypeExists(doc, loadedFamily);

                            trans.Commit();
                            System.Diagnostics.Debug.WriteLine($"✅ Successfully {actionDescription.ToLower()}ed DetailReferenceFamily");

                            string baseTypeMsg = baseTypeCreated
                                ? "Base type 'DetailReferenceFamily' was created."
                                : "Base type already exists.";

                            TaskDialog.Show("Family Loaded",
                                $"DetailReferenceFamily has been {actionDescription.ToLower()}ed successfully.\n\n" +
                                $"Family name: {loadedFamily.Name}\n" +
                                $"{baseTypeMsg}\n\n" +
                                $"Source: {familyPath}");

                            return Result.Succeeded;
                        }
                        else
                        {
                            trans.RollBack();
                            TaskDialog.Show("Load Failed", "Failed to load the family. Please try again.");
                            return Result.Failed;
                        }
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        System.Diagnostics.Debug.WriteLine($"❌ Error loading family: {ex.Message}");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                message = $"Error loading family: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ LoadFamilyCommand Error: {ex.Message}\n{ex.StackTrace}");
                TaskDialog.Show("Error", $"Failed to load DetailReferenceFamily:\n\n{ex.Message}");
                return Result.Failed;
            }
        }

        private Family FindExistingFamily(Document doc)
        {
            try
            {
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                // Check exact matches first
                foreach (var name in FAMILY_NAMES_TO_CHECK)
                {
                    var family = families.FirstOrDefault(f =>
                        f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (family != null)
                        return family;
                }

                // Check partial matches
                var partialMatch = families.FirstOrDefault(f =>
                {
                    string nameLower = f.Name.ToLowerInvariant();
                    return nameLower.Contains("detailreference") || nameLower.Contains("detail reference");
                });

                return partialMatch;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding existing family: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Ensures a base type "DetailReferenceFamily" exists for placement operations.
        /// Returns true if a new type was created, false if it already existed.
        /// </summary>
        private bool EnsureBaseTypeExists(Document doc, Family family)
        {
            if (family == null) return false;

            try
            {
                var typeIds = family.GetFamilySymbolIds();

                // Check if base type already exists
                foreach (ElementId typeId in typeIds)
                {
                    FamilySymbol symbol = doc.GetElement(typeId) as FamilySymbol;
                    if (symbol != null && symbol.Name == "DetailReferenceFamily")
                    {
                        // Base type exists - make sure it's activated
                        if (!symbol.IsActive)
                        {
                            symbol.Activate();
                            System.Diagnostics.Debug.WriteLine("   ✅ Activated existing base type");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("   ✅ Base type already exists and is active");
                        }
                        return false; // Already existed
                    }
                }

                // No base type found - need to create one
                if (typeIds.Count > 0)
                {
                    // Duplicate first existing type and rename
                    ElementId firstTypeId = typeIds.First();
                    FamilySymbol firstSymbol = doc.GetElement(firstTypeId) as FamilySymbol;

                    if (firstSymbol != null)
                    {
                        FamilySymbol newSymbol = firstSymbol.Duplicate("DetailReferenceFamily") as FamilySymbol;

                        if (newSymbol != null)
                        {
                            if (!newSymbol.IsActive)
                            {
                                newSymbol.Activate();
                            }
                            System.Diagnostics.Debug.WriteLine("   ✅ Created and activated base type 'DetailReferenceFamily'");
                            return true; // New type created
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("   ⚠️ No existing types to duplicate - family may be empty");
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"   ❌ Error ensuring base type: {ex.Message}");
                return false;
            }
        }
    }

    public class FamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            // Always overwrite the existing family
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            // Use the new shared family version
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }

    /// <summary>
    /// Clear Parameters Command - Clears all project parameters for testing
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ClearParametersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uidoc = uiApp.ActiveUIDocument;
                Document doc = uidoc?.Document;

                if (doc == null)
                {
                    TaskDialog.Show("Error", "No active document found.");
                    return Result.Failed;
                }

                if (doc.IsFamilyDocument)
                {
                    TaskDialog.Show("Error", "Cannot clear parameters from family documents.");
                    return Result.Failed;
                }

                TaskDialog confirmDialog = new TaskDialog("Clear Project Parameters");
                confirmDialog.MainInstruction = "Clear All Project Parameters?";
                confirmDialog.MainContent = "This will remove ALL ViewReference_ parameters from the current project.\n\n" +
                                          "This action cannot be undone.\n\n" +
                                          "Are you sure you want to continue?";
                confirmDialog.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                confirmDialog.DefaultButton = TaskDialogResult.No;

                if (confirmDialog.Show() != TaskDialogResult.Yes)
                {
                    return Result.Cancelled;
                }

                using (Transaction trans = new Transaction(doc, "Clear Project Parameters"))
                {
                    trans.Start();

                    int clearedCount = 0;
                    var projectInfo = doc.ProjectInformation;

                    string[] parametersToClear = {
                        "ViewReference_ProjectType",
                        "ViewReference_JsonPath",
                        "ViewReference_PortfolioGuid",
                        "ViewReference_LastSync",
                        "ViewReference_ProjectGuid"
                    };

                    foreach (string paramName in parametersToClear)
                    {
                        try
                        {
                            Parameter param = projectInfo.LookupParameter(paramName);
                            if (param != null && !param.IsReadOnly)
                            {
                                param.Set("");
                                clearedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error clearing parameter {paramName}: {ex.Message}");
                        }
                    }

                    trans.Commit();

                    TaskDialog.Show("Parameters Cleared",
                        $"Successfully cleared {clearedCount} project parameters.\n\n" +
                        "The project now has no portfolio configuration.");

                    return Result.Succeeded;
                }
            }
            catch (Exception ex)
            {
                message = $"Error clearing parameters: {ex.Message}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Debug command to show the current document's file path
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ShowFilePathCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument?.Document;

                if (doc == null)
                {
                    TaskDialog.Show("No Document", "No active document found.");
                    return Result.Cancelled;
                }

                string pathName = doc.PathName;
                string title = doc.Title;
                bool isWorkshared = doc.IsWorkshared;
                string centralPath = "";

                if (isWorkshared)
                {
                    try
                    {
                        ModelPath centralModelPath = doc.GetWorksharingCentralModelPath();
                        centralPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralModelPath);
                    }
                    catch { }
                }

                string info = $"Document Title: {title}\n\n" +
                              $"PathName: {pathName}\n\n" +
                              $"Is Workshared: {isWorkshared}\n\n" +
                              $"Central Path: {centralPath}";

                TaskDialog td = new TaskDialog("File Path Info");
                td.MainInstruction = "Current Document Location";
                td.MainContent = info;
                td.CommonButtons = TaskDialogCommonButtons.Ok;
                td.Show();

                // Also write to debug
                System.Diagnostics.Debug.WriteLine("=== FILE PATH INFO ===");
                System.Diagnostics.Debug.WriteLine($"Title: {title}");
                System.Diagnostics.Debug.WriteLine($"PathName: {pathName}");
                System.Diagnostics.Debug.WriteLine($"IsWorkshared: {isWorkshared}");
                System.Diagnostics.Debug.WriteLine($"CentralPath: {centralPath}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Error getting file path:\n\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}