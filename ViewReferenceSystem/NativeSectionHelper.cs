using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using System;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.Utilities;

namespace ViewReferenceSystem.Placement
{
    /// <summary>
    /// Native section helper - SIMPLIFIED with minimal UI interruptions
    /// </summary>
    public class NativeSectionHelper : IExternalEventHandler
    {
        private ViewInfo _viewInfo;
        private XYZ _startPoint;
        private XYZ _endPoint;
        private bool _pointsCollected = false;

        public void SetViewInfo(ViewInfo viewInfo)
        {
            _viewInfo = viewInfo;
            _pointsCollected = false;
            System.Diagnostics.Debug.WriteLine($"🎯 NativeSectionHelper.SetViewInfo() called with: {viewInfo?.ViewName ?? "NULL"}");
        }

        public void Execute(UIApplication uiApp)
        {
            System.Diagnostics.Debug.WriteLine("🚀 NativeSectionHelper.Execute() START");

            try
            {
                // Step 1: Validate inputs and get UI elements
                if (!ValidateInputs(uiApp, out UIDocument uidoc, out Document doc, out View activeView))
                {
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Validation passed - Document: {doc.Title}, Active View: {activeView.Name}");

                // Step 2: If we haven't collected points yet, do it now
                if (!_pointsCollected)
                {
                    if (!GetSectionLineFromUser(uidoc, activeView))
                    {
                        System.Diagnostics.Debug.WriteLine("ℹ️ User cancelled section line selection");
                        return;
                    }
                    _pointsCollected = true;
                }

                // Step 3: Ensure proxy view exists
                if (!ProxyViewManager.EnsureProxyViewExists(doc, true))
                {
                    ShowError("Failed to create or find proxy view. Cannot create native section.");
                    return;
                }

                // Step 4: Get the proxy view for reference
                View proxyView = ProxyViewManager.GetProxyViewForReference(doc);
                if (proxyView == null)
                {
                    ShowError("Could not access proxy view for section creation.");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Using proxy view: {proxyView.Name}");

                // Step 5: Ensure work plane is set for the active view (CRITICAL for sections)
                if (!EnsureWorkPlane(doc, activeView))
                {
                    ShowError("Could not set work plane for section placement. Try placing the section in a plan view.");
                    return;
                }

                // Step 6: Create the native section in a proper transaction
                using (Transaction trans = new Transaction(doc, "Create Native Section"))
                {
                    trans.Start();

                    try
                    {
                        // ✅ FIXED: CreateReferenceSection requires headPoint and tailPoint (not a Line)
                        // headPoint = start of section line
                        // tailPoint = end of section line (opposite direction from viewing)
                        ViewSection.CreateReferenceSection(doc, activeView.Id, proxyView.Id, _startPoint, _endPoint);
                        trans.Commit();

                        System.Diagnostics.Debug.WriteLine("✅ Native section created successfully (referencing proxy)");

                        // Reset for next use
                        _pointsCollected = false;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Native section creation failed: {ex.Message}");
                        trans.RollBack();
                        ShowError($"Failed to create native section: {ex.Message}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"💥 Exception in NativeSectionHelper.Execute: {ex.Message}");
                ShowError($"Error creating section: {ex.Message}");
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("🏁 NativeSectionHelper.Execute() END");
            }
        }

        /// <summary>
        /// Ensure work plane is set - CRITICAL for section placement
        /// </summary>
        private bool EnsureWorkPlane(Document doc, View activeView)
        {
            try
            {
                // Check if we're in a view that needs a work plane
                if (activeView.SketchPlane != null)
                {
                    System.Diagnostics.Debug.WriteLine("✅ Work plane already set");
                    return true;
                }

                // For plan views, try to get the level and create a work plane
                if (activeView is ViewPlan viewPlan)
                {
                    Level level = doc.GetElement(viewPlan.GenLevel.Id) as Level;
                    if (level != null)
                    {
                        Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, level.ProjectElevation * XYZ.BasisZ);

                        using (Transaction trans = new Transaction(doc, "Set Work Plane"))
                        {
                            trans.Start();
                            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                            activeView.SketchPlane = sketchPlane;
                            trans.Commit();
                        }

                        System.Diagnostics.Debug.WriteLine("✅ Work plane created and set");
                        return true;
                    }
                }

                System.Diagnostics.Debug.WriteLine("⚠️ Could not set work plane automatically");
                return true; // Continue anyway - Revit might handle it
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error setting work plane: {ex.Message}");
                return true; // Continue anyway - Revit might handle it
            }
        }

        private bool ValidateInputs(UIApplication uiApp, out UIDocument uidoc, out Document doc, out View activeView)
        {
            uidoc = null;
            doc = null;
            activeView = null;

            // ✅ REMOVED: Don't check for _viewInfo - sections don't need it
            // They just reference the proxy view, not a specific detail

            uidoc = uiApp?.ActiveUIDocument;
            if (uidoc == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ No active UI document");
                ShowError("No active document found.");
                return false;
            }

            doc = uidoc.Document;
            if (doc == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ No document");
                ShowError("No document found.");
                return false;
            }

            activeView = doc.ActiveView;
            if (activeView == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ No active view");
                ShowError("No active view found.");
                return false;
            }

            if (!CanPlaceSectionInView(activeView))
            {
                System.Diagnostics.Debug.WriteLine($"❌ Cannot place section in view type: {activeView.ViewType}");
                ShowError($"Cannot place sections in {activeView.ViewType} views. Please switch to a Plan, Section, or Elevation view.");
                return false;
            }

            if (doc.IsReadOnly)
            {
                ShowError("Document is read-only and cannot be modified.");
                return false;
            }

            return true;
        }

        private bool CanPlaceSectionInView(View view)
        {
            return view.ViewType == ViewType.FloorPlan ||
                   view.ViewType == ViewType.CeilingPlan ||
                   view.ViewType == ViewType.Section ||
                   view.ViewType == ViewType.Elevation ||
                   view.ViewType == ViewType.DraftingView ||
                   view.ViewType == ViewType.Detail ||
                   view.ViewType == ViewType.EngineeringPlan ||
                   view.ViewType == ViewType.AreaPlan;
        }

        /// <summary>
        /// ✅ SIMPLIFIED: Get section line with minimal instructions - just status bar messages
        /// </summary>
        private bool GetSectionLineFromUser(UIDocument uidoc, View activeView)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("📐 Getting section line from user...");

                // ✅ SIMPLIFIED: Just use status bar messages, matching callout style
                _startPoint = uidoc.Selection.PickPoint("Click first point of section (use SNAPS for precision - TAB cycles options)");
                System.Diagnostics.Debug.WriteLine($"✅ First point: ({_startPoint.X:F2}, {_startPoint.Y:F2}, {_startPoint.Z:F2})");

                _endPoint = uidoc.Selection.PickPoint("Click second point of section");
                System.Diagnostics.Debug.WriteLine($"✅ Second point: ({_endPoint.X:F2}, {_endPoint.Y:F2}, {_endPoint.Z:F2})");

                // Validate line length
                double lineLength = _startPoint.DistanceTo(_endPoint);
                System.Diagnostics.Debug.WriteLine($"📏 Section line length: {lineLength:F2} feet");

                if (lineLength < 0.1)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Section line too short");
                    ShowError("Section line is too short. Please try again with points that are further apart.");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine("✅ Section line coordinates collected successfully");
                return true;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("ℹ️ User cancelled section placement");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error during section placement: {ex.Message}");
                ShowError($"Error during section placement: {ex.Message}");
                return false;
            }
        }

        private void ShowError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Showing error: {message}");
            TaskDialog errorDialog = new TaskDialog("Section Error");
            errorDialog.MainInstruction = "Failed to create section";
            errorDialog.MainContent = message;
            errorDialog.MainIcon = TaskDialogIcon.TaskDialogIconError;
            errorDialog.Show();
        }

        public string GetName()
        {
            return "NativeSectionHelper";
        }
    }
}