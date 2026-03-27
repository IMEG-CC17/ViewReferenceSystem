using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.ApplicationServices;
using Newtonsoft.Json;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.Core;

namespace ViewReferenceSystem.Placement
{
    [Transaction(TransactionMode.Manual)]
    public class PlaceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc?.Document;

                System.Diagnostics.Debug.WriteLine("🚀 V3 Place Command executed");

                // Check if project is configured
                if (!PortfolioSettings.IsProjectConfigured(doc))
                {
                    MessageBox.Show("This project has not been configured for View Reference System.\n\nPlease use 'Portfolio Setup' first to configure this project.",
                        "Project Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return Result.Cancelled;
                }

                // For testing - this would normally come from the UI pane selection
                MessageBox.Show("PlaceCommand executed successfully!\n\nThis is a placeholder for view reference placement.",
                    "Command Executed", MessageBoxButton.OK, MessageBoxImage.Information);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        #region Public Static Methods for PlacementHelper

        /// <summary>
        /// Create a custom family type for a specific view - PUBLIC for PlacementHelper access
        /// </summary>
        public static FamilySymbol CreateViewFamilyType(Document doc, FamilySymbol baseSymbol, ViewInfo viewInfo)
        {
            try
            {
                string typeName = $"{viewInfo.ViewName}_{DateTime.Now:yyyyMMdd_HHmmss}";

                using (Transaction trans = new Transaction(doc, "Create View Reference Type"))
                {
                    trans.Start();

                    FamilySymbol newSymbol = baseSymbol.Duplicate(typeName) as FamilySymbol;

                    if (newSymbol != null)
                    {
                        SetTypeParameters(newSymbol, viewInfo);
                        trans.Commit();
                        return newSymbol;
                    }
                    else
                    {
                        trans.RollBack();
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creating family type: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Set instance visibility parameters - PUBLIC for PlacementHelper access
        /// </summary>
        public static void SetInstanceVisibilityParameters(FamilyInstance instance)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 SetInstanceVisibilityParameters() START");

                // Set visibility control parameters
                SetParameterValue(instance, "Left Visible", 0);     // Hide left callout
                SetParameterValue(instance, "Callout Reference", 1); // Enable callout reference
                SetParameterValue(instance, "Callout Element", 1);   // Enable callout element

                System.Diagnostics.Debug.WriteLine($"✅ Instance visibility parameters set");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error setting instance visibility parameters: {ex.Message}");
                // Don't throw - this is non-critical
            }
        }

        /// <summary>
        /// Set instance parameters with enhanced metadata - PUBLIC for PlacementHelper access
        /// </summary>
        public static void SetInstanceParametersWithMetadata(FamilyInstance instance, ViewInfo viewInfo, Document doc)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 PHASE 4: SetInstanceParametersWithMetadata() START");

                // Set visibility control parameters first
                SetInstanceVisibilityParameters(instance);

                // PHASE 4: Set metadata parameters at instance level
                string portfolioGuid = GetCurrentPortfolioGuid(doc);
                SetParameterValue(instance, "Portfolio GUID", portfolioGuid);
                SetParameterValue(instance, "Placement Date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                SetParameterValue(instance, "Placed By Project", doc.Title ?? "Unknown");

                // ✅ NOTE: Detail Number, Sheet Number, Top Note, Source Project GUID, and View ID
                // are TYPE parameters, not instance parameters. They're set in SetTypeParameters()
                // when the FamilySymbol is created/updated, not here.
                System.Diagnostics.Debug.WriteLine($"✅ PHASE 4: Instance metadata set - Portfolio: {portfolioGuid}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ PHASE 4: Error setting instance metadata: {ex.Message}");
                // Don't show MessageBox for metadata parameters - they're not critical
            }
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Set type parameters on a family symbol
        /// </summary>
        public static void SetTypeParameters(FamilySymbol symbol, ViewInfo viewInfo)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 PHASE 4: SetTypeParameters() START for ViewInfo: {viewInfo.ViewName}");

                // Set type parameters with validation
                bool detailNumberSet = SetParameterValueOnSymbol(symbol, "Detail Number", viewInfo.DetailNumber ?? "");
                bool sheetNumberSet = SetParameterValueOnSymbol(symbol, "Sheet Number", viewInfo.SheetNumber ?? "");
                bool topNoteSet = SetParameterValueOnSymbol(symbol, "Top Note", viewInfo.TopNote ?? "");

                // Additional metadata
                // NOTE: We store the GUID (not the name) so renaming on ACC doesn't break linking
                SetParameterValueOnSymbol(symbol, "Source Project GUID", viewInfo.SourceProjectGuid ?? "");
                SetParameterValueOnSymbol(symbol, "View ID", viewInfo.ViewId.ToString());
                SetParameterValueOnSymbol(symbol, "Last Updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                System.Diagnostics.Debug.WriteLine($"✅ PHASE 4: SetTypeParameters() COMPLETE - Detail: {detailNumberSet}, Sheet: {sheetNumberSet}, TopNote: {topNoteSet}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error setting type parameters: {ex.Message}");
                // Silently continue if parameter setting fails
            }
        }

        /// <summary>
        /// Set parameter value on a family symbol
        /// </summary>
        private static bool SetParameterValueOnSymbol(FamilySymbol symbol, string parameterName, object value)
        {
            try
            {
                Parameter param = symbol.LookupParameter(parameterName);
                if (param == null || param.IsReadOnly)
                {
                    return false;
                }

                switch (param.StorageType)
                {
                    case StorageType.String:
                        param.Set(value?.ToString() ?? "");
                        return true;

                    case StorageType.Integer:
                        if (value is bool boolValue)
                        {
                            param.Set(boolValue ? 1 : 0);
                            return true;
                        }
                        else if (int.TryParse(value?.ToString(), out int intValue))
                        {
                            param.Set(intValue);
                            return true;
                        }
                        break;

                    case StorageType.Double:
                        if (double.TryParse(value?.ToString(), out double doubleValue))
                        {
                            param.Set(doubleValue);
                            return true;
                        }
                        break;
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Get current portfolio GUID - PRIVATE helper method
        /// </summary>
        private static string GetCurrentPortfolioGuid(Document doc)
        {
            try
            {
                return PortfolioSettings.GetPortfolioGuid(doc) ?? Guid.NewGuid().ToString();
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }

        /// <summary>
        /// Set parameter value on any element
        /// </summary>
        private static void SetParameterValue(Element element, string parameterName, object value)
        {
            try
            {
                Parameter param = element.LookupParameter(parameterName);
                if (param == null || param.IsReadOnly) return;

                switch (param.StorageType)
                {
                    case StorageType.String:
                        param.Set(value?.ToString() ?? "");
                        break;
                    case StorageType.Integer:
                        if (int.TryParse(value?.ToString(), out int intValue))
                            param.Set(intValue);
                        break;
                    case StorageType.Double:
                        if (double.TryParse(value?.ToString(), out double doubleValue))
                            param.Set(doubleValue);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error setting parameter {parameterName}: {ex.Message}");
            }
        }

        #endregion

        #region Legacy Methods for Backward Compatibility

        /// <summary>
        /// Legacy method for backward compatibility
        /// </summary>
        private static ViewInfo GetFreshViewInfoFromPortfolio(Document doc, ViewInfo viewInfo)
        {
            try
            {
                var portfolio = PortfolioSettings.LoadPortfolioFromProject(doc);
                if (portfolio?.Views != null)
                {
                    return portfolio.Views.FirstOrDefault(v => v.ViewId == viewInfo.ViewId &&
                        string.Equals(v.SourceProjectName, viewInfo.SourceProjectName, StringComparison.OrdinalIgnoreCase));
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error getting fresh ViewInfo: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Place view reference family - legacy method
        /// </summary>
        private static bool PlaceViewReferenceFamily(UIDocument uidoc, ViewInfo viewInfo)
        {
            try
            {
                Document doc = uidoc.Document;

                // Get ViewReference families — search by name only (family may be any category)
                var familySymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.FamilyName.Contains("DetailReference") ||
                                fs.FamilyName.Contains("ViewReference"))
                    .ToList();

                if (!familySymbols.Any())
                {
                    MessageBox.Show("DetailReference family not found. Please load the family first.",
                        "Family Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                FamilySymbol symbol = familySymbols.First();

                // Try to create a custom family type
                FamilySymbol viewSymbol = null;
                try
                {
                    viewSymbol = CreateViewFamilyType(doc, symbol, viewInfo);
                }
                catch (Exception)
                {
                    // Continue with base symbol if custom type creation fails
                }

                FamilySymbol symbolToUse = viewSymbol ?? symbol;

                // Ensure symbol is active
                if (!symbolToUse.IsActive)
                {
                    using (Transaction trans = new Transaction(doc, "Activate View Reference Symbol"))
                    {
                        trans.Start();
                        symbolToUse.Activate();
                        trans.Commit();
                    }
                }

                // Prompt user to pick placement point
                try
                {
                    View activeView = uidoc.ActiveView;
                    if (activeView is ViewSheet)
                    {
                        MessageBox.Show("Cannot place view references on sheet views.\n\nPlease switch to a plan, elevation, or section view.",
                            "Invalid View Type", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }

                    if (activeView is View3D && !(activeView as View3D).IsTemplate)
                    {
                        MessageBox.Show("Cannot place view references in 3D views.\n\nPlease switch to a plan, elevation, or section view.",
                            "Invalid View Type", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }

                    System.Threading.Thread.Sleep(100);

                    XYZ placementPoint = uidoc.Selection.PickPoint("Click to place view reference");

                    using (Transaction trans = new Transaction(doc, "Place View Reference"))
                    {
                        trans.Start();

                        EnsureWorkPlaneIsSet(doc, activeView);

                        FamilyInstance instance = doc.Create.NewFamilyInstance(
                            placementPoint,
                            symbolToUse,
                            activeView);

                        try
                        {
                            // PHASE 4: Set instance parameters with enhanced metadata
                            SetInstanceParametersWithMetadata(instance, viewInfo, doc);
                        }
                        catch (Exception)
                        {
                            // Continue anyway
                        }

                        trans.Commit();
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    string exceptionTypeName = ex.GetType().Name;
                    if (exceptionTypeName.Contains("Cancel") ||
                        exceptionTypeName.Contains("Operation"))
                    {
                        return false;
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during placement: {ex.Message}",
                    "Placement Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Ensure work plane is set for the view
        /// </summary>
        private static void EnsureWorkPlaneIsSet(Document doc, View activeView)
        {
            try
            {
                if (activeView.SketchPlane == null)
                {
                    // Create a work plane if none exists
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                    activeView.SketchPlane = sketchPlane;
                    System.Diagnostics.Debug.WriteLine("✅ Work plane created for view");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Could not set work plane: {ex.Message}");
            }
        }

        #endregion
    }
}