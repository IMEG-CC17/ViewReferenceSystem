using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using System;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.Utilities;

namespace ViewReferenceSystem.Placement
{
    /// <summary>
    /// Native callout helper - SIMPLIFIED with minimal UI interruptions
    /// ✅ Fixed: Reduced minimum callout size from 0.1 feet to 0.01 feet
    /// </summary>
    public class NativeCalloutHelper : IExternalEventHandler
    {
        private ViewInfo _viewInfo;
        private XYZ _corner1;
        private XYZ _corner2;
        private bool _pointsCollected = false;

        public void SetViewInfo(ViewInfo viewInfo)
        {
            _viewInfo = viewInfo;
            _pointsCollected = false;
            System.Diagnostics.Debug.WriteLine($"🎯 NativeCalloutHelper.SetViewInfo() called with: {viewInfo?.ViewName ?? "NULL"}");
        }

        public void Execute(UIApplication uiApp)
        {
            System.Diagnostics.Debug.WriteLine("🚀 NativeCalloutHelper.Execute() START");

            try
            {
                if (!ValidateInputs(uiApp, out UIDocument uidoc, out Document doc, out View activeView))
                {
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Validation passed - Document: {doc.Title}, Active View: {activeView.Name}");

                if (!_pointsCollected)
                {
                    if (!GetCalloutRectangle(uidoc, activeView))
                    {
                        System.Diagnostics.Debug.WriteLine("ℹ️ User cancelled callout rectangle selection");
                        return;
                    }
                    _pointsCollected = true;
                }

                if (!ProxyViewManager.EnsureProxyViewExists(doc, true))
                {
                    ShowError("Failed to create or find proxy view. Cannot create native callout.");
                    return;
                }

                View proxyView = ProxyViewManager.GetProxyViewForReference(doc);
                if (proxyView == null)
                {
                    ShowError("Could not access proxy view for callout creation.");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Using proxy view: {proxyView.Name}");

                using (Transaction trans = new Transaction(doc, "Create Native Callout"))
                {
                    trans.Start();

                    try
                    {
                        ViewSection.CreateReferenceCallout(doc, activeView.Id, proxyView.Id, _corner1, _corner2);
                        trans.Commit();

                        System.Diagnostics.Debug.WriteLine("✅ Native callout created successfully (referencing proxy)");
                        _pointsCollected = false;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Native callout creation failed: {ex.Message}");
                        trans.RollBack();
                        ShowError($"Failed to create native callout: {ex.Message}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"💥 Exception in NativeCalloutHelper.Execute: {ex.Message}");
                ShowError($"Error creating callout: {ex.Message}");
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("🏁 NativeCalloutHelper.Execute() END");
            }
        }

        private bool ValidateInputs(UIApplication uiApp, out UIDocument uidoc, out Document doc, out View activeView)
        {
            uidoc = null;
            doc = null;
            activeView = null;

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

            if (!CanPlaceCalloutInView(activeView))
            {
                System.Diagnostics.Debug.WriteLine($"❌ Cannot place callout in view type: {activeView.ViewType}");
                ShowError($"Cannot place callouts in {activeView.ViewType} views. Please switch to a Plan, Section, or Elevation view.");
                return false;
            }

            if (doc.IsReadOnly)
            {
                ShowError("Document is read-only and cannot be modified.");
                return false;
            }

            return true;
        }

        private bool CanPlaceCalloutInView(View view)
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
        /// ✅ FIXED: Flatten Z coordinates and use proper distance calculation
        /// </summary>
        private bool GetCalloutRectangle(UIDocument uidoc, View activeView)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("📐 Getting callout rectangle from user...");

                XYZ point1 = uidoc.Selection.PickPoint("Click first corner of callout");
                System.Diagnostics.Debug.WriteLine($"✅ First corner: ({point1.X:F3}, {point1.Y:F3}, {point1.Z:F3})");

                XYZ point2 = uidoc.Selection.PickPoint("Click opposite corner of callout");
                System.Diagnostics.Debug.WriteLine($"✅ Second corner: ({point2.X:F3}, {point2.Y:F3}, {point2.Z:F3})");

                // Flatten both points to the same Z level (use view's origin Z or first point's Z)
                double flatZ = point1.Z;
                _corner1 = new XYZ(point1.X, point1.Y, flatZ);
                _corner2 = new XYZ(point2.X, point2.Y, flatZ);

                // Calculate actual distance (not just X/Y deltas)
                double width = Math.Abs(_corner2.X - _corner1.X);
                double height = Math.Abs(_corner2.Y - _corner1.Y);
                double diagonal = _corner1.DistanceTo(_corner2);

                System.Diagnostics.Debug.WriteLine($"📏 Callout size: {width:F4} x {height:F4} feet, diagonal: {diagonal:F4} feet");

                // Minimum size check - use diagonal distance
                // 0.01 feet = ~1/8 inch, which is very small
                if (diagonal < 0.01)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Callout rectangle too small");
                    ShowError("Callout rectangle is too small. Please try again with a larger area.");
                    return false;
                }

                // Ensure we have SOME width and height (not a line)
                if (width < 0.001 || height < 0.001)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Callout is essentially a line");
                    ShowError("Callout must have both width and height. Please click two diagonal corners.");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine("✅ Callout rectangle coordinates collected successfully");
                return true;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("ℹ️ User cancelled callout placement");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error during callout placement: {ex.Message}");
                ShowError($"Error during callout placement: {ex.Message}");
                return false;
            }
        }

        private void ShowError(string message)
        {
            TaskDialog errorDialog = new TaskDialog("Callout Error");
            errorDialog.MainInstruction = "Failed to create callout";
            errorDialog.MainContent = message;
            errorDialog.CommonButtons = TaskDialogCommonButtons.Ok;
            errorDialog.Show();
        }

        public string GetName()
        {
            return "NativeCalloutHelper";
        }
    }
}