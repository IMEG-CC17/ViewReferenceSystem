using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ViewReferenceSystem.UI
{
    /// <summary>
    /// PHASE 4: Monitor selection changes to detect detail family instances
    /// FIXED: Compatible with Revit 2022-2025+ using Idling event instead of SelectionChanged
    /// </summary>
    public class SelectionMonitor
    {
        private UIApplication _uiApp;
        private Document _currentDoc;
        private bool _isMonitoring = false;
        private List<ElementId> _lastSelection = new List<ElementId>();

        // Events
        public event EventHandler<DetailFamilySelectedEventArgs> DetailFamilySelected;
        public event EventHandler SelectionCleared;

        public SelectionMonitor(UIApplication uiApp, Document doc)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _currentDoc = doc ?? throw new ArgumentNullException(nameof(doc));

            System.Diagnostics.Debug.WriteLine("🔍 PHASE 4: SelectionMonitor created");
        }

        /// <summary>
        /// PHASE 4: Start monitoring selection changes
        /// FIXED: Uses Idling event for Revit 2022-2024 compatibility
        /// </summary>
        public void StartMonitoring()
        {
            try
            {
                if (_isMonitoring)
                    return;

                // FIXED: Use Idling event instead of SelectionChanged for compatibility
                _uiApp.Idling += OnIdling;
                _isMonitoring = true;
                _lastSelection.Clear();

                System.Diagnostics.Debug.WriteLine("🔄 PHASE 4: Selection monitoring started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error starting selection monitoring: {ex.Message}");
            }
        }

        /// <summary>
        /// PHASE 4: Stop monitoring selection changes
        /// </summary>
        public void StopMonitoring()
        {
            try
            {
                if (!_isMonitoring)
                    return;

                _uiApp.Idling -= OnIdling;
                _isMonitoring = false;
                _lastSelection.Clear();

                System.Diagnostics.Debug.WriteLine("🔄 PHASE 4: Selection monitoring stopped");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error stopping selection monitoring: {ex.Message}");
            }
        }

        /// <summary>
        /// FIXED: Use Idling event to check selection (compatible with Revit 2022-2024)
        /// This is called repeatedly during Revit's idle time
        /// </summary>
        private void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            try
            {
                if (_currentDoc == null || _uiApp.ActiveUIDocument?.Document != _currentDoc)
                {
                    _currentDoc = _uiApp.ActiveUIDocument?.Document;
                    if (_currentDoc == null)
                        return;
                }

                var currentSelection = _uiApp.ActiveUIDocument.Selection.GetElementIds();

                // Check if selection actually changed
                if (SelectionsAreEqual(_lastSelection, currentSelection))
                    return;

                _lastSelection = new List<ElementId>(currentSelection);

                System.Diagnostics.Debug.WriteLine($"👁️ PHASE 4: Selection changed - {currentSelection.Count} elements selected");

                if (currentSelection.Count == 0)
                {
                    // Selection cleared
                    FireSelectionCleared();
                    return;
                }

                // Check for detail family instances
                CheckForDetailFamilySelection(currentSelection);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error handling selection change: {ex.Message}");
            }
        }

        /// <summary>
        /// PHASE 4: Check if any selected elements are detail family instances
        /// </summary>
        private void CheckForDetailFamilySelection(ICollection<ElementId> selectedIds)
        {
            try
            {
                foreach (ElementId elementId in selectedIds)
                {
                    Element element = _currentDoc.GetElement(elementId);
                    if (element is FamilyInstance familyInstance)
                    {
                        if (IsDetailFamily(familyInstance))
                        {
                            System.Diagnostics.Debug.WriteLine($"📋 PHASE 4: Detail family selected: {familyInstance.Symbol?.Name ?? "Unknown"}");
                            FireDetailFamilySelected(familyInstance);
                            return; // Only fire for the first detail family found
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error checking for detail family selection: {ex.Message}");
            }
        }

        /// <summary>
        /// PHASE 4: Check if family instance is a detail family
        /// </summary>
        private bool IsDetailFamily(FamilyInstance familyInstance)
        {
            try
            {
                if (familyInstance?.Symbol?.Family == null)
                    return false;

                // Check if family name contains typical detail indicators
                string familyName = familyInstance.Symbol.Family.Name;
                if (string.IsNullOrEmpty(familyName))
                    return false;

                // Check for common detail family naming patterns
                var detailPrefixes = new[]
                {
                    "CTD-", "SCT-", "SCW-", "SC-", "SBR-", "PEMB-", "CMUTD-"
                };

                return detailPrefixes.Any(prefix =>
                    familyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error checking if family is detail family: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// PHASE 4: Compare two selection collections
        /// </summary>
        private bool SelectionsAreEqual(IList<ElementId> selection1, ICollection<ElementId> selection2)
        {
            try
            {
                if (selection1.Count != selection2.Count)
                    return false;

                return selection1.All(id => selection2.Contains(id));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error comparing selections: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// PHASE 4: Fire detail family selected event
        /// </summary>
        private void FireDetailFamilySelected(FamilyInstance familyInstance)
        {
            try
            {
                var eventArgs = new DetailFamilySelectedEventArgs(familyInstance);
                DetailFamilySelected?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error firing detail family selected event: {ex.Message}");
            }
        }

        /// <summary>
        /// PHASE 4: Fire selection cleared event
        /// </summary>
        private void FireSelectionCleared()
        {
            try
            {
                SelectionCleared?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error firing selection cleared event: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// PHASE 4: Event arguments for detail family selection
    /// </summary>
    public class DetailFamilySelectedEventArgs : EventArgs
    {
        public FamilyInstance FamilyInstance { get; }

        public DetailFamilySelectedEventArgs(FamilyInstance familyInstance)
        {
            FamilyInstance = familyInstance ?? throw new ArgumentNullException(nameof(familyInstance));
        }
    }

    /// <summary>
    /// PHASE 4: Event arguments for find in portfolio request
    /// </summary>
    public class FindInPortfolioRequestedEventArgs : EventArgs
    {
        public ViewReferenceSystem.Models.ViewInfo ViewInfo { get; }

        public FindInPortfolioRequestedEventArgs(ViewReferenceSystem.Models.ViewInfo viewInfo)
        {
            ViewInfo = viewInfo ?? throw new ArgumentNullException(nameof(viewInfo));
        }
    }
}