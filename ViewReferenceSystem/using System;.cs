using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ViewReferenceSystem.Core;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.Utilities;

namespace ViewReferenceSystem.Placement
{
    /// <summary>
    /// PlacementHelper - Handles detail family placement with ViewId-based type management
    /// One unique FamilySymbol type per unique detail (SourceProject + ViewId)
    /// ViewName is irrelevant - ViewId is the stable unique identifier
    /// </summary>
    public class PlacementHelper : IExternalEventHandler
    {
        private ViewInfo _viewInfo;

        public void SetViewInfo(ViewInfo viewInfo)
        {
            _viewInfo = viewInfo;
            System.Diagnostics.Debug.WriteLine($"🎯 PlacementHelper - Set view info for: {viewInfo?.ViewName}");
        }

        public void Execute(UIApplication app)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🚀 PlacementHelper.Execute() START");

                if (_viewInfo == null)
                {
                    TaskDialog.Show("Error", "No view information available.");
                    return;
                }

                // STEP 1: Get UIDocument and Document
                UIDocument uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    TaskDialog.Show("Error", "No active document.");
                    return;
                }

                Document doc = uidoc.Document;
                View activeView = uidoc.ActiveView;

                System.Diagnostics.Debug.WriteLine($"✅ Document: {doc.Title}, Active View: {activeView.Name}");
                System.Diagnostics.Debug.WriteLine($"📋 ViewInfo - ViewId: {_viewInfo.ViewId}, ViewName: {_viewInfo.ViewName}, Number: {_viewInfo.DetailNumber}, Sheet: {_viewInfo.SheetNumber}");

                // STEP 2: Check view type
                if (activeView is ViewSheet)
                {
                    TaskDialog.Show("Invalid View", "Cannot place in sheet views. Switch to a plan/section view.");
                    return;
                }

                // STEP 3: Find DetailReferenceFamily base
                System.Diagnostics.Debug.WriteLine("🔍 Looking for DetailReferenceFamily...");
                var viewFamilySymbols = ViewReferenceFamilyManager.GetViewReferenceFamilySymbols(doc);

                if (!viewFamilySymbols.Any())
                {
                    TaskDialog.Show("Family Not Found",
                        "DetailReferenceFamily not found in project.\n\n" +
                        "Please load the family first.");
                    return;
                }

                FamilySymbol baseSymbol = viewFamilySymbols.First();
                System.Diagnostics.Debug.WriteLine($"✅ Found family: {baseSymbol.FamilyName}");

                // STEP 4: Find or create unique type for this detail (based on ViewId)
                FamilySymbol detailSymbol = FindOrCreateDetailType(doc, baseSymbol, _viewInfo);

                if (detailSymbol == null)
                {
                    TaskDialog.Show("Error", "Failed to create or find detail type.");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Using detail type: {detailSymbol.Name}");

                // STEP 5: Activate symbol if needed
                if (!detailSymbol.IsActive)
                {
                    using (Transaction trans = new Transaction(doc, "Activate Symbol"))
                    {
                        trans.Start();
                        detailSymbol.Activate();
                        trans.Commit();
                    }
                    System.Diagnostics.Debug.WriteLine("✅ Symbol activated");
                }

                // STEP 6: For section views, set up work plane BEFORE getting placement point
                if (activeView is ViewSection)
                {
                    System.Diagnostics.Debug.WriteLine("📐 Section view detected - setting up work plane first");
                    if (!EnsureWorkPlaneForSectionView(doc, activeView as ViewSection))
                    {
                        TaskDialog.Show("Work Plane Error",
                            "Could not create work plane for section view.\n\n" +
                            "Detail family placement requires a work plane.");
                        return;
                    }
                }

                // STEP 7: Get placement point from user
                System.Diagnostics.Debug.WriteLine("🖱️ Prompting for placement point...");
                XYZ placementPoint;

                try
                {
                    placementPoint = uidoc.Selection.PickPoint("Click to place detail reference");
                    System.Diagnostics.Debug.WriteLine($"✅ Point selected: {placementPoint}");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ User cancelled");
                    return;
                }

                // STEP 8: Create the family instance
                System.Diagnostics.Debug.WriteLine("🏗️ Creating family instance...");

                using (Transaction trans = new Transaction(doc, "Place Detail Reference"))
                {
                    trans.Start();

                    try
                    {
                        FamilyInstance instance = null;

                        // Place the family instance - work plane is already set for section views
                        if (activeView is ViewSection)
                        {
                            System.Diagnostics.Debug.WriteLine("📐 Placing in section view (work plane already set)");
                            instance = doc.Create.NewFamilyInstance(
                                placementPoint,
                                detailSymbol,
                                activeView);
                        }
                        else if (activeView.ViewType == ViewType.DraftingView)
                        {
                            System.Diagnostics.Debug.WriteLine("📝 Placing in drafting view");
                            instance = doc.Create.NewFamilyInstance(
                                placementPoint,
                                detailSymbol,
                                activeView);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("🗺️ Placing in plan/elevation view");
                            EnsureWorkPlaneIsSet(doc, activeView);
                            instance = doc.Create.NewFamilyInstance(
                                placementPoint,
                                detailSymbol,
                                activeView);
                        }

                        System.Diagnostics.Debug.WriteLine($"✅ Instance created: ID {instance.Id}");

                        // STEP 9: Set instance-level parameters (metadata for validation)
                        System.Diagnostics.Debug.WriteLine("🏷️ Setting instance metadata parameters...");

                        try
                        {
                            PlaceCommand.SetInstanceParametersWithMetadata(instance, _viewInfo, doc);
                            System.Diagnostics.Debug.WriteLine("✅ Instance metadata parameters set");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ Instance parameter setting failed: {ex.Message}");
                            // Non-critical - continue
                        }

                        trans.Commit();
                        System.Diagnostics.Debug.WriteLine("✅ Transaction committed - PLACEMENT COMPLETE");
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        System.Diagnostics.Debug.WriteLine($"❌ Placement failed: {ex.Message}");
                        TaskDialog.Show("Placement Error",
                            $"Failed to place detail reference:\n\n{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Execute error: {ex.Message}");
                TaskDialog.Show("Error", $"Unexpected error:\n\n{ex.Message}");
            }
        }

        public string GetName()
        {
            return "PlacementHelper";
        }

        /// <summary>
        /// Find or create a unique FamilySymbol type for this specific detail
        /// CRITICAL: Uses ViewId as the stable unique identifier (NOT ViewName)
        /// Type name format: "ProjectName_ViewId" (e.g., "TypicalDetails_12345")
        /// </summary>
        private FamilySymbol FindOrCreateDetailType(Document doc, FamilySymbol baseSymbol, ViewInfo viewInfo)
        {
            try
            {
                // Get source project name
                string sourceProject = string.IsNullOrEmpty(viewInfo.SourceProjectName)
                    ? doc.Title
                    : viewInfo.SourceProjectName;

                // Clean project name (remove .rvt extension and invalid characters)
                sourceProject = System.IO.Path.GetFileNameWithoutExtension(sourceProject);
                sourceProject = sourceProject.Replace(" ", "_").Replace("-", "_");

                // CRITICAL: Create unique type name based on ViewId (NOT ViewName)
                // ViewId is stable and never changes, even if view is renamed
                string uniqueTypeName = $"{sourceProject}_{viewInfo.ViewId}";

                System.Diagnostics.Debug.WriteLine($"🔍 Looking for existing type: '{uniqueTypeName}' (ViewId: {viewInfo.ViewId})");

                // Check if this type already exists
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                var existingSymbol = collector
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs =>
                        fs.FamilyName.Equals(baseSymbol.FamilyName, StringComparison.OrdinalIgnoreCase) &&
                        fs.Name.Equals(uniqueTypeName, StringComparison.OrdinalIgnoreCase));

                if (existingSymbol != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Found existing type: '{existingSymbol.Name}'");

                    // Update type parameters in case they've changed in the JSON
                    UpdateTypeParameters(doc, existingSymbol, viewInfo);

                    return existingSymbol;
                }

                // Type doesn't exist - create it
                System.Diagnostics.Debug.WriteLine($"🆕 Creating new type: '{uniqueTypeName}'");

                using (Transaction trans = new Transaction(doc, "Create Detail Type"))
                {
                    trans.Start();

                    FamilySymbol newSymbol = baseSymbol.Duplicate(uniqueTypeName) as FamilySymbol;

                    if (newSymbol != null)
                    {
                        // Set type parameters
                        PlaceCommand.SetTypeParameters(newSymbol, viewInfo);
                        trans.Commit();

                        System.Diagnostics.Debug.WriteLine($"✅ Created new type: '{newSymbol.Name}'");
                        return newSymbol;
                    }
                    else
                    {
                        trans.RollBack();
                        System.Diagnostics.Debug.WriteLine($"❌ Failed to duplicate symbol");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in FindOrCreateDetailType: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Update type parameters on existing symbol (in case JSON data changed)
        /// </summary>
        private void UpdateTypeParameters(Document doc, FamilySymbol symbol, ViewInfo viewInfo)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔄 Updating type parameters for: '{symbol.Name}'");

                using (Transaction trans = new Transaction(doc, "Update Detail Type"))
                {
                    trans.Start();
                    PlaceCommand.SetTypeParameters(symbol, viewInfo);
                    trans.Commit();

                    System.Diagnostics.Debug.WriteLine($"✅ Type parameters updated");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Failed to update type parameters: {ex.Message}");
                // Non-critical - continue with existing parameters
            }
        }

        /// <summary>
        /// Ensure work plane is set for section views - MUST be called in separate transaction
        /// </summary>
        private bool EnsureWorkPlaneForSectionView(Document doc, ViewSection section)
        {
            try
            {
                // Check if work plane already exists
                if (section.SketchPlane != null)
                {
                    System.Diagnostics.Debug.WriteLine("✅ Work plane already exists in section view");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine("🔧 Creating work plane for section view...");

                // Create work plane in a separate transaction
                using (Transaction trans = new Transaction(doc, "Create Work Plane"))
                {
                    trans.Start();

                    // Get the section view's origin point (center of the view)
                    BoundingBoxXYZ bbox = section.CropBox;
                    XYZ origin = (bbox.Min + bbox.Max) / 2.0;

                    // Create a plane parallel to the section view
                    XYZ viewDirection = section.ViewDirection;
                    XYZ upDirection = section.UpDirection;
                    XYZ rightDirection = upDirection.CrossProduct(viewDirection).Normalize();

                    // Create plane perpendicular to view direction (parallel to section cut)
                    Plane plane = Plane.CreateByOriginAndBasis(origin, rightDirection, upDirection);
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

                    // Set it on the view
                    section.SketchPlane = sketchPlane;

                    trans.Commit();
                    System.Diagnostics.Debug.WriteLine("✅ Work plane created and set on section view");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to create work plane for section: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ensure work plane is set - compatible with all view types including sections
        /// </summary>
        private static void EnsureWorkPlaneIsSet(Document doc, View activeView)
        {
            try
            {
                // Check if we need to set a work plane
                if (activeView.SketchPlane != null)
                {
                    System.Diagnostics.Debug.WriteLine("✅ Work plane already exists, no action needed");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"⚠️ No work plane in view: {activeView.Name} (Type: {activeView.ViewType})");

                // For plan views and other views, try to use the level
                Level level = null;

                // Try to get the view's associated level
                Parameter levelParam = activeView.get_Parameter(BuiltInParameter.PLAN_VIEW_LEVEL);
                if (levelParam != null && levelParam.AsElementId() != ElementId.InvalidElementId)
                {
                    level = doc.GetElement(levelParam.AsElementId()) as Level;
                }

                if (level == null)
                {
                    // Get any level in the project as fallback
                    FilteredElementCollector levelCollector = new FilteredElementCollector(doc);
                    level = levelCollector.OfClass(typeof(Level)).Cast<Level>().FirstOrDefault();
                }

                if (level != null)
                {
                    // Create a sketch plane on the level
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation));
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                    activeView.SketchPlane = sketchPlane;

                    System.Diagnostics.Debug.WriteLine($"✅ Work plane set using level {level.Name}");
                }
                else
                {
                    // Create a work plane at origin as last resort
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
                    SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                    activeView.SketchPlane = sketchPlane;

                    System.Diagnostics.Debug.WriteLine("✅ Default work plane set");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Could not set work plane: {ex.Message}");
                // Don't throw - let placement attempt to continue
            }
        }
    }
}