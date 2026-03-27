using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ViewReferenceSystem.Models
{
    /// <summary>
    /// Tracks which projects reference a view
    /// </summary>
    public class ProjectReference
    {
        public string ProjectGuid { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public DateTime LastSeen { get; set; } = DateTime.Now;

        public ProjectReference() { }

        public ProjectReference(string guid, string name)
        {
            ProjectGuid = guid;
            ProjectName = name;
            LastSeen = DateTime.Now;
        }
    }

    public class ViewInfo
    {
        // ===== 10 ESSENTIAL FIELDS =====

        // 1. Unique view identifier (Revit ElementId as int)
        public int ViewId { get; set; }

        // 2. View name from Revit
        public string ViewName { get; set; } = "";

        // 3. Sheet number where view is placed
        public string SheetNumber { get; set; } = "";

        // 4. Sheet name where view is placed
        public string SheetName { get; set; } = "";

        // 5. Detail number on the sheet (matches Revit VIEWPORT_DETAIL_NUMBER)
        public string DetailNumber { get; set; } = "";

        // 6. View type (DraftingView, Section, EngineeringPlan, Detail, etc.)
        public string ViewType { get; set; } = "";

        // 7. Top note text
        public string TopNote { get; set; } = "";

        // 8. Source project name
        public string SourceProjectName { get; set; } = "";

        // 8.5 Source project GUID (stable even if project is renamed)
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string SourceProjectGuid { get; set; } = "";

        // 9. Which projects reference this view
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<ProjectReference> ReferencedBy { get; set; } = new List<ProjectReference>();

        // 10. Last modified timestamp
        public DateTime LastModified { get; set; } = DateTime.Now;


        // ===== BACKWARD COMPATIBILITY (deserialize from old JSON, never serialize) =====
        // These read old field names from existing JSON and map them to the canonical fields.
        // ShouldSerialize methods prevent them from ever being written back out.

        /// <summary>Old alias — reads "ViewNumber" from old JSON, maps to DetailNumber</summary>
        [JsonProperty("ViewNumber")]
        public string ViewNumber
        {
            get { return null; }
            set { if (string.IsNullOrEmpty(DetailNumber)) DetailNumber = value; }
        }
        public bool ShouldSerializeViewNumber() { return false; }

        /// <summary>Old alias — reads "SourceProject" from old JSON, maps to SourceProjectName</summary>
        [JsonProperty("SourceProject")]
        public string SourceProject
        {
            get { return null; }
            set { if (string.IsNullOrEmpty(SourceProjectName)) SourceProjectName = value; }
        }
        public bool ShouldSerializeSourceProject() { return false; }

        /// <summary>Old field — reads "Notes" from old JSON, extracts SheetName if present</summary>
        [JsonProperty("Notes")]
        public string Notes
        {
            get { return null; }
            set
            {
                if (string.IsNullOrEmpty(SheetName) && value != null && value.StartsWith("Sheet: "))
                    SheetName = value.Substring(7);
            }
        }
        public bool ShouldSerializeNotes() { return false; }

        // Legacy V2 fields — accept from old JSON but never write back
        [JsonProperty("IsPlacedOnSheet")]
        public bool? LegacyIsPlacedOnSheet { get { return null; } set { } }
        public bool ShouldSerializeLegacyIsPlacedOnSheet() { return false; }

        [JsonProperty("IsAvailableForReference")]
        public bool? LegacyIsAvailableForReference { get { return null; } set { } }
        public bool ShouldSerializeLegacyIsAvailableForReference() { return false; }

        [JsonProperty("IsStandardView")]
        public bool? LegacyIsStandardView { get { return null; } set { } }
        public bool ShouldSerializeLegacyIsStandardView() { return false; }

        [JsonProperty("TitleOnPage")]
        public string LegacyTitleOnPage { get { return null; } set { } }
        public bool ShouldSerializeLegacyTitleOnPage() { return false; }

        [JsonProperty("ViewPrefix")]
        public string LegacyViewPrefix { get { return null; } set { } }
        public bool ShouldSerializeLegacyViewPrefix() { return false; }

        [JsonProperty("Description")]
        public string LegacyDescription { get { return null; } set { } }
        public bool ShouldSerializeLegacyDescription() { return false; }

        [JsonProperty("SearchableText")]
        public string LegacySearchableText { get { return null; } set { } }
        public bool ShouldSerializeLegacySearchableText() { return false; }

        [JsonProperty("IsImported")]
        public bool? LegacyIsImported { get { return null; } set { } }
        public bool ShouldSerializeLegacyIsImported() { return false; }

        [JsonProperty("NeedsSheetPlacement")]
        public bool? LegacyNeedsSheetPlacement { get { return null; } set { } }
        public bool ShouldSerializeLegacyNeedsSheetPlacement() { return false; }

        [JsonProperty("ImportedDate")]
        public DateTime? LegacyImportedDate { get { return null; } set { } }
        public bool ShouldSerializeLegacyImportedDate() { return false; }

        [JsonProperty("ImportedBy")]
        public string LegacyImportedBy { get { return null; } set { } }
        public bool ShouldSerializeLegacyImportedBy() { return false; }

        [JsonProperty("RequestedByProjects")]
        public List<string> LegacyRequestedByProjects { get { return null; } set { } }
        public bool ShouldSerializeLegacyRequestedByProjects() { return false; }

        [JsonProperty("TextNotes")]
        public List<string> LegacyTextNotes { get { return null; } set { } }
        public bool ShouldSerializeLegacyTextNotes() { return false; }


        // ===== COMPUTED PROPERTIES (never serialized) =====

        [JsonIgnore]
        public string ViewNameDisplay
        {
            get { return string.IsNullOrEmpty(ViewName) ? "[Unnamed View]" : ViewName; }
        }

        [JsonIgnore]
        public string SheetDisplay
        {
            get { return string.IsNullOrEmpty(SheetNumber) ? "[Not Placed]" : "[" + SheetNumber + "]"; }
        }

        [JsonIgnore]
        public string CompositeViewName
        {
            get { return ViewName + " (" + SourceProjectName + ")"; }
        }

        [JsonIgnore]
        public string FullSearchableText
        {
            get
            {
                var searchTerms = new List<string>
                {
                    ViewName,
                    SheetNumber,
                    SheetName,
                    DetailNumber,
                    TopNote,
                    SourceProjectName
                };

                return string.Join(" ", searchTerms.Where(s => !string.IsNullOrEmpty(s)));
            }
        }

        [JsonIgnore]
        public string EffectiveSourceProjectName
        {
            get
            {
                if (!string.IsNullOrEmpty(SourceProjectName))
                    return SourceProjectName;

                return "Unknown Project";
            }
        }

        [JsonIgnore]
        public bool IsReferenced
        {
            get { return ReferencedBy != null && ReferencedBy.Any(); }
        }

        [JsonIgnore]
        public int ReferenceCount
        {
            get { return ReferencedBy != null ? ReferencedBy.Count : 0; }
        }

        [JsonIgnore]
        public string ReferencedByDisplay
        {
            get
            {
                if (ReferencedBy == null || !ReferencedBy.Any())
                    return "(none)";
                return string.Join(", ", ReferencedBy.Select(r => r.ProjectName));
            }
        }

        [JsonIgnore]
        public bool IsDetailView
        {
            get
            {
                return ViewType == "DraftingView" ||
                       ViewType == "Detail" ||
                       (ViewName != null && ViewName.Contains("Detail")) ||
                       (ViewName != null && ViewName.Contains("DTL"));
            }
        }

        [JsonIgnore]
        public string DisplayText
        {
            get
            {
                string display = ViewName;

                if (!string.IsNullOrEmpty(SheetNumber))
                    display += " (Sheet " + SheetNumber + ")";

                if (!string.IsNullOrEmpty(DetailNumber))
                    display += " Detail " + DetailNumber;

                return display;
            }
        }


        // ===== METHODS =====

        public void AddReference(string projectGuid, string projectName)
        {
            if (ReferencedBy == null)
                ReferencedBy = new List<ProjectReference>();

            var existing = ReferencedBy.FirstOrDefault(r =>
                string.Equals(r.ProjectGuid, projectGuid, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.LastSeen = DateTime.Now;
                existing.ProjectName = projectName;
            }
            else
            {
                ReferencedBy.Add(new ProjectReference(projectGuid, projectName));
            }
        }

        public void RemoveReference(string projectGuid)
        {
            if (ReferencedBy == null)
                return;

            ReferencedBy.RemoveAll(r =>
                string.Equals(r.ProjectGuid, projectGuid, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsReferencedBy(string projectGuid)
        {
            if (ReferencedBy == null) return false;
            return ReferencedBy.Any(r =>
                string.Equals(r.ProjectGuid, projectGuid, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsValidForPortfolio()
        {
            return ViewId > 0 &&
                   !string.IsNullOrEmpty(ViewName) &&
                   !string.IsNullOrEmpty(SourceProjectName);
        }

        public void CopyFrom(ViewInfo other)
        {
            if (other == null) return;

            ViewId = other.ViewId;
            ViewName = other.ViewName;
            SheetNumber = other.SheetNumber;
            SheetName = other.SheetName;
            DetailNumber = other.DetailNumber;
            ViewType = other.ViewType;
            TopNote = other.TopNote;
            SourceProjectName = other.SourceProjectName;
            ReferencedBy = other.ReferencedBy != null
                ? new List<ProjectReference>(other.ReferencedBy)
                : new List<ProjectReference>();
            LastModified = DateTime.Now;
        }

        public static ViewInfo CreateBasic(int viewId, string viewName, string sourceProject)
        {
            return new ViewInfo
            {
                ViewId = viewId,
                ViewName = viewName,
                SourceProjectName = sourceProject,
                ViewType = "DraftingView",
                LastModified = DateTime.Now
            };
        }

        public override string ToString()
        {
            return DisplayText;
        }
    }
}