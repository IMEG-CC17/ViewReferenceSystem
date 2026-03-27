using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ViewReferenceSystem.Utilities
{
    public class AlwaysOverwriteLoadOptions : IFamilyLoadOptions
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

    public static class ViewReferenceFamilyManager
    {
        private static readonly string[] ACCEPTABLE_FAMILY_NAMES = {
            "DetailReferenceFamily",
            "Detail Reference Family",
            "DetailReference"
        };

        private static readonly string[] REQUIRED_PARAMETERS = {
            "Detail Number",
            "Sheet Number",
            "Top Note"
        };

        /// <summary>
        /// Check if family is loaded and usable (has at least one symbol)
        /// </summary>
        public static bool EnsureViewReferenceFamilyIsLoaded(Document doc, bool showUserMessages = true)
        {
            try
            {
                var symbols = GetViewReferenceFamilySymbols(doc);
                if (symbols.Any())
                    return true;

                if (showUserMessages)
                    ShowFamilyNotFoundDialog(doc);

                return false;
            }
            catch (Exception ex)
            {
                if (showUserMessages)
                    TaskDialog.Show("Family Check Error", $"Error checking for DetailReferenceFamily:\n\n{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Find DetailReferenceFamily object in the document
        /// </summary>
        public static Family FindCorrectViewReferenceFamily(Document doc)
        {
            try
            {
                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                // Exact match first
                foreach (var acceptableName in ACCEPTABLE_FAMILY_NAMES)
                {
                    var family = families.FirstOrDefault(f =>
                        f.Name.Equals(acceptableName, StringComparison.OrdinalIgnoreCase));
                    if (family != null)
                        return family;
                }

                // Partial match
                return families.FirstOrDefault(f =>
                {
                    string nameLower = f.Name.ToLowerInvariant();
                    return nameLower.Contains("detailreference") || nameLower.Contains("detail reference");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding family: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get all DetailReferenceFamily symbols. If family exists but has 0 types,
        /// reloads the .rfa from the addins folder to force Revit to create the default type.
        /// </summary>
        public static List<FamilySymbol> GetViewReferenceFamilySymbols(Document doc)
        {
            try
            {
                // STEP 1: Find the Family object
                Family family = FindCorrectViewReferenceFamily(doc);

                if (family != null)
                {
                    // STEP 2: Get symbols from Family
                    var symbolIds = family.GetFamilySymbolIds();

                    if (symbolIds.Count > 0)
                    {
                        var validSymbols = new List<FamilySymbol>();
                        foreach (var symbolId in symbolIds)
                        {
                            var symbol = doc.GetElement(symbolId) as FamilySymbol;
                            if (symbol != null)
                                validSymbols.Add(symbol);
                        }

                        if (validSymbols.Any())
                            return validSymbols;
                    }

                    // STEP 3: Family exists but has 0 types - create default type via Family Editor
                    System.Diagnostics.Debug.WriteLine($"⚠️ Family '{family.Name}' has 0 types - creating default type");
                    var createdSymbols = CreateDefaultTypeViaFamilyEditor(doc, family);
                    if (createdSymbols.Any())
                        return createdSymbols;
                }

                // FALLBACK: Name-based search across all FamilySymbol objects
                return GetViewReferenceFamilySymbolsByNameSearch(doc);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error getting symbols: {ex.Message}");
                return new List<FamilySymbol>();
            }
        }

        /// <summary>
        /// Family has 0 types - open family doc, create a default type via FamilyManager,
        /// then load back into the project. This is how Revit API creates types when
        /// none exist (can't Duplicate from nothing).
        /// </summary>
        private static List<FamilySymbol> CreateDefaultTypeViaFamilyEditor(Document doc, Family family)
        {
            Document familyDoc = null;
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 Opening family '{family.Name}' in background to create default type...");

                // Open the family document in the background
                familyDoc = doc.EditFamily(family);
                if (familyDoc == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ EditFamily returned null");
                    return new List<FamilySymbol>();
                }

                // Create a new type via FamilyManager
                FamilyManager mgr = familyDoc.FamilyManager;
                System.Diagnostics.Debug.WriteLine($"   Current types in family doc: {mgr.Types.Size}");

                using (Transaction famTrans = new Transaction(familyDoc, "Create Default Type"))
                {
                    famTrans.Start();
                    mgr.NewType("DetailReferenceFamily");
                    famTrans.Commit();
                }

                System.Diagnostics.Debug.WriteLine($"   After creating type: {mgr.Types.Size}");

                // Load the modified family back into the project (overwrites existing)
                Family reloadedFamily = familyDoc.LoadFamily(doc, new AlwaysOverwriteLoadOptions());

                // Close the family doc without saving to disk
                familyDoc.Close(false);
                familyDoc = null;

                if (reloadedFamily == null)
                    reloadedFamily = FindCorrectViewReferenceFamily(doc);

                if (reloadedFamily == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Family not found after reload");
                    return new List<FamilySymbol>();
                }

                // Now get the symbols
                var symbolIds = reloadedFamily.GetFamilySymbolIds();
                System.Diagnostics.Debug.WriteLine($"   After reload: {symbolIds.Count} symbol(s)");

                var symbols = new List<FamilySymbol>();
                foreach (var sid in symbolIds)
                {
                    var sym = doc.GetElement(sid) as FamilySymbol;
                    if (sym != null)
                    {
                        // Activate in a transaction if needed
                        if (!sym.IsActive)
                        {
                            using (Transaction actTrans = new Transaction(doc, "Activate Symbol"))
                            {
                                actTrans.Start();
                                sym.Activate();
                                actTrans.Commit();
                            }
                        }
                        symbols.Add(sym);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"✅ Created default type - family now has {symbols.Count} usable symbol(s)");
                return symbols;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CreateDefaultType error: {ex.Message}");
                return new List<FamilySymbol>();
            }
            finally
            {
                // Safety: make sure family doc gets closed
                try
                {
                    if (familyDoc != null && familyDoc.IsValidObject)
                        familyDoc.Close(false);
                }
                catch { }
            }
        }

        /// <summary>
        /// Fallback: Search for symbols by name across all FamilySymbol objects
        /// </summary>
        private static List<FamilySymbol> GetViewReferenceFamilySymbolsByNameSearch(Document doc)
        {
            try
            {
                var allSymbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .ToList();

                var validSymbols = new List<FamilySymbol>();

                foreach (var acceptableName in ACCEPTABLE_FAMILY_NAMES)
                {
                    validSymbols.AddRange(allSymbols.Where(fs =>
                        fs.FamilyName.Equals(acceptableName, StringComparison.OrdinalIgnoreCase)));
                }

                if (!validSymbols.Any())
                {
                    validSymbols.AddRange(allSymbols.Where(fs =>
                    {
                        string nameLower = fs.FamilyName.ToLowerInvariant();
                        return nameLower.Contains("detailreference") || nameLower.Contains("detail reference");
                    }));
                }

                return validSymbols;
            }
            catch
            {
                return new List<FamilySymbol>();
            }
        }

        /// <summary>
        /// Get the best available symbol for placement
        /// </summary>
        public static FamilySymbol GetBestViewReferenceSymbol(Document doc)
        {
            return GetViewReferenceFamilySymbols(doc).FirstOrDefault();
        }

        /// <summary>
        /// Activate a family symbol if it's not already active
        /// </summary>
        public static void EnsureSymbolIsActive(Document doc, FamilySymbol symbol)
        {
            if (symbol == null)
                throw new ArgumentNullException(nameof(symbol));

            if (!symbol.IsActive)
            {
                using (Transaction trans = new Transaction(doc, "Activate DetailReferenceFamily Symbol"))
                {
                    trans.Start();
                    symbol.Activate();
                    trans.Commit();
                }
            }
        }

        private static void ShowFamilyNotFoundDialog(Document doc)
        {
            TaskDialog errorDialog = new TaskDialog("DetailReferenceFamily Not Found");
            errorDialog.MainInstruction = "DetailReferenceFamily is not loaded in this project";

            string diagnosticInfo = GetDiagnosticInfo(doc);

            errorDialog.MainContent =
                "The View Reference System requires DetailReferenceFamily.\n\n" +
                "To resolve this:\n" +
                "1. Use the 'Load Family' button in the ribbon\n" +
                "2. Or: Insert tab → Load Family → select DetailReferenceFamily.rfa\n\n" +
                "Click 'Show More' below for diagnostics.";

            errorDialog.ExpandedContent = diagnosticInfo;
            errorDialog.CommonButtons = TaskDialogCommonButtons.Ok;
            errorDialog.Show();
        }

        private static string GetDiagnosticInfo(Document doc)
        {
            try
            {
                if (doc == null) return "No document";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Project: {doc.Title}");

                var families = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .ToList();

                sb.AppendLine($"Total families: {families.Count}");

                var relevant = families.Where(f =>
                {
                    string name = f.Name.ToLowerInvariant();
                    return name.Contains("detail") || name.Contains("reference");
                }).ToList();

                if (relevant.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine("Relevant families:");
                    foreach (var fam in relevant.OrderBy(f => f.Name))
                    {
                        int cnt = 0;
                        try { cnt = fam.GetFamilySymbolIds().Count; } catch { }
                        sb.AppendLine($"  '{fam.Name}' | Cat: {fam.FamilyCategory?.Name ?? "None"} | Types: {cnt}");
                    }
                }

                string addinPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string rfaPath = Path.Combine(addinPath, "DetailReferenceFamily.rfa");
                sb.AppendLine();
                sb.AppendLine($".rfa exists: {File.Exists(rfaPath)}");
                sb.AppendLine($"Path: {rfaPath}");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}