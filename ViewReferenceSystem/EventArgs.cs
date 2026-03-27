using System;
using Autodesk.Revit.DB;
using ViewReferenceSystem.Models;

namespace ViewReferenceSystem
{
    /// <summary>
    /// PHASE 4: Event arguments for preview requests
    /// </summary>
    public class PreviewRequestedEventArgs : EventArgs
    {
        public ViewInfo ViewInfo { get; }
        public FamilyInstance FamilyInstance { get; }

        public PreviewRequestedEventArgs(ViewInfo viewInfo, FamilyInstance familyInstance = null)
        {
            ViewInfo = viewInfo ?? throw new ArgumentNullException(nameof(viewInfo));
            FamilyInstance = familyInstance;
        }
    }

    /// <summary>
    /// PHASE 4: Event arguments for edit top note requests
    /// </summary>
    public class EditTopNoteRequestedEventArgs : EventArgs
    {
        public ViewInfo ViewInfo { get; }
        public FamilyInstance FamilyInstance { get; }

        public EditTopNoteRequestedEventArgs(ViewInfo viewInfo, FamilyInstance familyInstance = null)
        {
            ViewInfo = viewInfo ?? throw new ArgumentNullException(nameof(viewInfo));
            FamilyInstance = familyInstance;
        }
    }

    /// <summary>
    /// PHASE 4: Event arguments for find in portfolio requests
    /// </summary>
    public class FindInPortfolioRequestedEventArgs : EventArgs
    {
        public ViewInfo ViewInfo { get; }
        public FamilyInstance FamilyInstance { get; }

        public FindInPortfolioRequestedEventArgs(ViewInfo viewInfo, FamilyInstance familyInstance = null)
        {
            ViewInfo = viewInfo ?? throw new ArgumentNullException(nameof(viewInfo));
            FamilyInstance = familyInstance;
        }
    }

    /// <summary>
    /// PHASE 4: Event arguments for detail family selection
    /// </summary>
    public class DetailFamilySelectedEventArgs : EventArgs
    {
        public FamilyInstance FamilyInstance { get; }
        public string ViewName { get; }
        public string DetailNumber { get; }
        public string SheetNumber { get; }
        public ViewInfo ViewInfo { get; set; } // Set by the handler when ViewInfo is resolved

        public DetailFamilySelectedEventArgs(FamilyInstance familyInstance, string viewName, string detailNumber, string sheetNumber)
        {
            FamilyInstance = familyInstance ?? throw new ArgumentNullException(nameof(familyInstance));
            ViewName = viewName ?? "Unknown";
            DetailNumber = detailNumber ?? "";
            SheetNumber = sheetNumber ?? "";
        }
    }
}