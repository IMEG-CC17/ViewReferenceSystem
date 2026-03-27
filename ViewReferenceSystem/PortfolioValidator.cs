using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.Core;

namespace ViewReferenceSystem.Utilities
{
    /// <summary>
    /// Portfolio Validator - Validates detail reference instances against portfolio data
    /// Handles orphaned references: warns user, never auto-deletes instances
    /// </summary>
    public static class PortfolioValidator
    {
        /// <summary>
        /// Info about an orphaned reference that's placed on a view
        /// </summary>
        public class OrphanedReferenceInfo
        {
            public string TypeName { get; set; }
            public string ViewName { get; set; }
            public string SheetNumber { get; set; }
            public bool IsOnSheet { get; set; }

            public string DisplayText
            {
                get
                {
                    if (IsOnSheet && !string.IsNullOrEmpty(SheetNumber))
                        return $"Sheet {SheetNumber}: {TypeName}";
                    else if (!string.IsNullOrEmpty(ViewName))
                        return $"View '{ViewName}': {TypeName}";
                    else
                        return TypeName;
                }
            }
        }

        /// <summary>
        /// Validation result class
        /// </summary>
        public class ValidationResult
        {
            public int TotalReferencesFound { get; set; }
            public int ValidReferencesUpdated { get; set; }
            public int OrphanedInstancesDeleted { get; set; }
            public int OrphanedTypesDeleted { get; set; }
            public List<OrphanedReferenceInfo> OrphanedOnViews { get; set; } = new List<OrphanedReferenceInfo>();
            public int Errors { get; set; }
            public List<string> ErrorMessages { get; set; } = new List<string>();

            public bool HasOrphansOnViews => OrphanedOnViews.Count > 0;

            public string GetSummary()
            {
                if (TotalReferencesFound == 0 && OrphanedTypesDeleted == 0)
                    return "No detail references found in project";

                var summary = $"Validated {TotalReferencesFound} detail references:\n" +
                       $"  • {ValidReferencesUpdated} updated with current data\n";

                if (OrphanedTypesDeleted > 0)
                    summary += $"  • {OrphanedTypesDeleted} unused types deleted\n";

                if (OrphanedOnViews.Count > 0)
                    summary += $"  • {OrphanedOnViews.Count} orphaned references (user action required)\n";

                if (Errors > 0)
                    summary += $"  • {Errors} errors occurred";

                return summary;
            }
        }

        /// <summary>
        /// Main validation method - validates all detail reference instances in the document
        /// Updates valid references and warns about orphans (never auto-deletes instances)
        /// </summary>
        public static ValidationResult ValidateAllDetailReferences(Document doc, PortfolioSettings.Portfolio portfolioData)
        {
            var result = new ValidationResult();
            List<FamilyInstance> detailInstances = null;
            List<FamilySymbol> allDetailFamilyTypes = null;

            try
            {
                System.Diagnostics.Debug.WriteLine("🔍 ===== PORTFOLIO VALIDATION START =====");

                // Step 1: Find all detail reference family instances
                detailInstances = FindAllDetailReferenceInstances(doc);
                result.TotalReferencesFound = detailInstances.Count;

                System.Diagnostics.Debug.WriteLine($"📊 Found {detailInstances.Count} detail reference instances");

                // Step 2: Group instances by their FamilySymbol type
                var instancesByType = detailInstances.GroupBy(i => i.Symbol).ToDictionary(g => g.Key, g => g.ToList());

                System.Diagnostics.Debug.WriteLine($"📦 Grouped into {instancesByType.Count} unique types");

                // Step 3: Validate each type and update accordingly
                using (Transaction trans = new Transaction(doc, "Validate Detail References"))
                {
                    trans.Start();

                    // Process types that have instances
                    foreach (var kvp in instancesByType)
                    {
                        FamilySymbol symbol = kvp.Key;
                        List<FamilyInstance> instances = kvp.Value;

                        try
                        {
                            // Get metadata from the type (these are type parameters)
                            // Read Source Project GUID — the stable identifier
                            string sourceProjectGuid = GetTypeParameterValue(symbol, "Source Project GUID");
                            string viewIdStr = GetTypeParameterValue(symbol, "View ID");

                            if (string.IsNullOrEmpty(viewIdStr) || !int.TryParse(viewIdStr, out int viewId))
                            {
                                System.Diagnostics.Debug.WriteLine($"⚠️ Type '{symbol.Name}' has no valid ViewId metadata - skipping");
                                continue;
                            }

                            if (string.IsNullOrEmpty(sourceProjectGuid))
                            {
                                System.Diagnostics.Debug.WriteLine($"⚠️ Type '{symbol.Name}' has no Source Project GUID - skipping (family may need to be re-placed)");
                                continue;
                            }

                            System.Diagnostics.Debug.WriteLine($"🔍 Validating type '{symbol.Name}' (ViewId: {viewId}, GUID: {sourceProjectGuid}, Instances: {instances.Count})");

                            // Check if source view still exists in portfolio
                            ViewInfo sourceView = FindViewInPortfolio(portfolioData, sourceProjectGuid, viewId);

                            if (sourceView != null)
                            {
                                // Valid reference - update type parameters with current data
                                UpdateValidReference(symbol, sourceView);
                                result.ValidReferencesUpdated += instances.Count;
                                System.Diagnostics.Debug.WriteLine($"✅ Updated type '{symbol.Name}' with current data");
                            }
                            else
                            {
                                // Orphaned reference - NEVER auto-delete, just warn user
                                System.Diagnostics.Debug.WriteLine($"⚠️ Type '{symbol.Name}' is orphaned - adding to warnings");

                                // Add one warning per type (not per instance)
                                result.OrphanedOnViews.Add(new OrphanedReferenceInfo
                                {
                                    TypeName = symbol.Name,
                                    ViewName = $"{instances.Count} instance(s)",
                                    SheetNumber = "",
                                    IsOnSheet = false
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Errors++;
                            result.ErrorMessages.Add($"Error validating type '{symbol.Name}': {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"❌ Error validating type '{symbol.Name}': {ex.Message}");
                        }
                    }

                    var typesWithInstanceIds = new HashSet<ElementId>(instancesByType.Keys.Select(s => s.Id));

                    // Clean up types that have ZERO instances (safe to delete)
                    System.Diagnostics.Debug.WriteLine("   🧹 Checking for types with no instances...");
                    allDetailFamilyTypes = FindAllDetailReferenceFamilyTypes(doc);
                    foreach (var symbol in allDetailFamilyTypes)
                    {
                        // Skip if this type has instances
                        if (typesWithInstanceIds.Contains(symbol.Id))
                            continue;

                        // NEVER delete the base family type - we need it to create new types
                        if (symbol.Name == symbol.FamilyName || symbol.Name == "DetailReferenceFamily")
                        {
                            System.Diagnostics.Debug.WriteLine($"   ⏭️ Skipping base type: {symbol.Name}");
                            continue;
                        }

                        // No instances - safe to delete the type
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"   🗑️ Deleting unused type: {symbol.Name}");
                            doc.Delete(symbol.Id);
                            result.OrphanedTypesDeleted++;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"   ⚠️ Could not delete type '{symbol.Name}': {ex.Message}");
                        }
                    }

                    trans.Commit();
                }

                System.Diagnostics.Debug.WriteLine("✅ ===== PORTFOLIO VALIDATION COMPLETE =====");
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.ErrorMessages.Add($"Validation failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Validation failed: {ex.Message}");
            }
            finally
            {
                // Clear references to allow garbage collection
                detailInstances?.Clear();
                allDetailFamilyTypes?.Clear();
            }

            return result;
        }

        /// <summary>
        /// Find all DetailReferenceFamily types in the document
        /// </summary>
        private static List<FamilySymbol> FindAllDetailReferenceFamilyTypes(Document doc)
        {
            try
            {
                using (var collector = new FilteredElementCollector(doc))
                {
                    return collector
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .Where(fs => IsDetailReferenceFamilyType(fs))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding family types: {ex.Message}");
                return new List<FamilySymbol>();
            }
        }

        /// <summary>
        /// Find all DetailReferenceFamily instances in the document
        /// </summary>
        private static List<FamilyInstance> FindAllDetailReferenceInstances(Document doc)
        {
            try
            {
                using (var collector = new FilteredElementCollector(doc))
                {
                    return collector
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .Where(fi => IsDetailReferenceFamily(fi))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding instances: {ex.Message}");
                return new List<FamilyInstance>();
            }
        }

        /// <summary>
        /// Check if a family symbol is a DetailReferenceFamily type
        /// </summary>
        private static bool IsDetailReferenceFamilyType(FamilySymbol symbol)
        {
            try
            {
                string familyName = symbol.FamilyName ?? "";
                string familyNameLower = familyName.ToLowerInvariant();

                return familyNameLower.Contains("detailreference") ||
                       familyNameLower.Contains("detail reference") ||
                       familyName.Equals("DetailReferenceFamily", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a family instance is a DetailReferenceFamily
        /// </summary>
        private static bool IsDetailReferenceFamily(FamilyInstance instance)
        {
            try
            {
                string familyName = instance.Symbol?.FamilyName ?? "";
                string familyNameLower = familyName.ToLowerInvariant();

                return familyNameLower.Contains("detailreference") ||
                       familyNameLower.Contains("detail reference") ||
                       familyName.Equals("DetailReferenceFamily", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Find a view in the portfolio by source project GUID and ViewId.
        /// GUID is the sole stable identifier — immune to ACC project renames.
        /// </summary>
        private static ViewInfo FindViewInPortfolio(PortfolioSettings.Portfolio portfolio, string sourceProjectGuid, int viewId)
        {
            try
            {
                if (portfolio?.Views == null || string.IsNullOrEmpty(sourceProjectGuid))
                    return null;

                return portfolio.Views.FirstOrDefault(v =>
                    v.ViewId == viewId &&
                    string.Equals(v.SourceProjectGuid, sourceProjectGuid, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding view in portfolio: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Update a valid reference with current data from portfolio
        /// </summary>
        private static void UpdateValidReference(FamilySymbol symbol, ViewInfo viewInfo)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔄 Updating type '{symbol.Name}' with: Detail={viewInfo.DetailNumber}, Sheet={viewInfo.SheetNumber}, TopNote={viewInfo.TopNote}");

                SetSymbolParameter(symbol, "Detail Number", viewInfo.DetailNumber ?? "");
                SetSymbolParameter(symbol, "Sheet Number", viewInfo.SheetNumber ?? "");
                SetSymbolParameter(symbol, "Top Note", viewInfo.TopNote ?? "");
                SetSymbolParameter(symbol, "Last Updated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error updating valid reference: {ex.Message}");
            }
        }

        /// <summary>
        /// Get a parameter value from a family symbol (type parameter)
        /// </summary>
        private static string GetTypeParameterValue(FamilySymbol symbol, string parameterName)
        {
            try
            {
                Parameter param = symbol.LookupParameter(parameterName);
                if (param != null && param.HasValue)
                {
                    return param.AsString() ?? "";
                }
                return "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Set a parameter value on a family symbol
        /// </summary>
        private static void SetSymbolParameter(FamilySymbol symbol, string parameterName, string value)
        {
            try
            {
                Parameter param = symbol.LookupParameter(parameterName);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(value);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Failed to set parameter '{parameterName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Clean up deleted views from portfolio data
        /// Removes ViewInfo entries where the ViewId no longer exists in the source project
        /// </summary>
        public static int CleanupDeletedViews(Document doc, PortfolioSettings.Portfolio portfolioData)
        {
            List<View> allViewsList = null;

            try
            {
                if (portfolioData?.Views == null)
                    return 0;

                string currentProjectName = PortfolioSettings.GetProjectName(doc);
                string currentProjectGuid = PortfolioSettings.GetProjectGuid(doc);

                // Get all current view IDs in this project
                var currentViewIds = new HashSet<int>();

                using (var collector = new FilteredElementCollector(doc))
                {
                    allViewsList = collector
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .ToList();
                }

                foreach (var view in allViewsList)
                {
                    currentViewIds.Add(view.Id.IntegerValue);
                }

                // Find views in portfolio belonging to this project that no longer exist
                // Match by GUID first, fall back to name for legacy entries
                var viewsToRemove = portfolioData.Views
                    .Where(v =>
                        ((!string.IsNullOrEmpty(currentProjectGuid) && string.Equals(v.SourceProjectGuid, currentProjectGuid, StringComparison.OrdinalIgnoreCase)) ||
                         string.Equals(v.SourceProjectName, currentProjectName, StringComparison.OrdinalIgnoreCase)) &&
                        !currentViewIds.Contains(v.ViewId))
                    .ToList();

                if (viewsToRemove.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"🗑️ Removing {viewsToRemove.Count} deleted views from portfolio:");
                    foreach (var view in viewsToRemove)
                    {
                        System.Diagnostics.Debug.WriteLine($"   - {view.ViewName} (ViewId: {view.ViewId})");
                        portfolioData.Views.Remove(view);
                    }
                }

                return viewsToRemove.Count;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error cleaning up deleted views: {ex.Message}");
                return 0;
            }
            finally
            {
                // Clear references to allow garbage collection
                allViewsList?.Clear();
            }
        }
    }
}