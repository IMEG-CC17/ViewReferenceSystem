using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Linq;
using ViewReferenceSystem.Utilities;

namespace ViewReferenceSystem.Utilities
{
    /// <summary>
    /// ENHANCED: ProxyViewManager with reliable auto-creation and detail number setting
    /// </summary>
    public static class ProxyViewManager
    {
        // FIXED: Use X naming convention for proxy identification
        private const string PROXY_VIEW_NAME = "X";
        private const string PROXY_SHEET_NAME = "Portfolio Proxy Sheet";
        private const string PROXY_SHEET_NUMBER = "X-XX";
        private const string PROXY_DETAIL_NUMBER = "-";

        /// <summary>
        /// ENHANCED: Ensure complete proxy system exists using reliable step-by-step creation
        /// </summary>
        public static bool EnsureProxyViewExists(Document doc, bool showUserMessages = true)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔍 EnsureProxyViewExists() START - Enhanced Version");

                bool createdSomething = false;
                ViewDrafting proxyView = null;
                ViewSheet proxySheet = null;
                Viewport proxyViewport = null;

                // Step 1: Ensure proxy view exists
                proxyView = FindExistingProxyView(doc);
                if (proxyView == null)
                {
                    System.Diagnostics.Debug.WriteLine("🔧 Creating proxy view...");
                    var viewResult = CreateSimpleDraftingView(doc, PROXY_VIEW_NAME);
                    if (!viewResult.Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Failed to create proxy view: {viewResult.ErrorMessage}");
                        if (showUserMessages)
                        {
                            TaskDialog.Show("Proxy Creation Error", $"Failed to create proxy view: {viewResult.ErrorMessage}");
                        }
                        return false;
                    }
                    proxyView = viewResult.CreatedView;
                    createdSomething = true;
                    System.Diagnostics.Debug.WriteLine($"✅ Proxy view created: {proxyView.Name}");
                }

                // Step 2: Ensure proxy sheet exists
                proxySheet = FindExistingProxySheet(doc);
                if (proxySheet == null)
                {
                    System.Diagnostics.Debug.WriteLine("🔧 Creating proxy sheet...");
                    var sheetResult = CreateSimpleSheet(doc, PROXY_SHEET_NUMBER, PROXY_SHEET_NAME);
                    if (!sheetResult.Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Failed to create proxy sheet: {sheetResult.ErrorMessage}");
                        if (showUserMessages)
                        {
                            TaskDialog.Show("Proxy Creation Error", $"Failed to create proxy sheet: {sheetResult.ErrorMessage}");
                        }
                        return false;
                    }
                    proxySheet = sheetResult.CreatedSheet;
                    createdSomething = true;
                    System.Diagnostics.Debug.WriteLine($"✅ Proxy sheet created: {proxySheet.SheetNumber}");
                }

                // Step 3: Ensure view is placed on sheet
                proxyViewport = FindExistingViewport(doc, proxyView, proxySheet);
                if (proxyViewport == null)
                {
                    System.Diagnostics.Debug.WriteLine("🔧 Placing view on sheet...");
                    var placementResult = PlaceViewOnSheetSimple(doc, proxyView, proxySheet);
                    if (!placementResult.Success)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Failed to place view on sheet: {placementResult.ErrorMessage}");
                        if (showUserMessages)
                        {
                            TaskDialog.Show("Proxy Creation Error", $"Failed to place view on sheet: {placementResult.ErrorMessage}");
                        }
                        return false;
                    }
                    proxyViewport = placementResult.CreatedViewport;
                    createdSomething = true;
                    System.Diagnostics.Debug.WriteLine($"✅ View placed on sheet: Viewport {proxyViewport.Id}");
                }

                // Step 4: Ensure detail number is set to "-"
                if (proxyViewport != null)
                {
                    var detailNumberResult = SetDetailNumber(doc, proxyViewport, PROXY_DETAIL_NUMBER);
                    if (detailNumberResult)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Detail number set to: {PROXY_DETAIL_NUMBER}");
                    }
                }

                // Step 5: Show success message if anything was created
                if (createdSomething && showUserMessages)
                {
                    TaskDialog.Show("Proxy System Ready",
                        $"Created Drafting View X and placed on Sheet X-XX for proxy purposes\n\n" +
                        $"✅ View: {proxyView?.Name} (ID: {proxyView?.Id})\n" +
                        $"✅ Sheet: {proxySheet?.SheetNumber} (ID: {proxySheet?.Id})\n" +
                        $"✅ Detail Number: {PROXY_DETAIL_NUMBER}\n\n" +
                        $"This enables cross-project detail references with native callouts and sections.");
                }

                System.Diagnostics.Debug.WriteLine("✅ Complete proxy system ready");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in EnsureProxyViewExists: {ex.Message}");
                if (showUserMessages)
                {
                    TaskDialog.Show("Proxy System Error", $"Failed to create proxy system: {ex.Message}");
                }
                return false;
            }
        }

        /// <summary>
        /// Find existing proxy view in the document
        /// </summary>
        public static ViewDrafting FindExistingProxyView(Document doc)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                var views = collector.OfClass(typeof(ViewDrafting))
                    .Cast<ViewDrafting>()
                    .Where(v => !v.IsTemplate && v.Name == PROXY_VIEW_NAME)
                    .ToList();

                var proxyView = views.FirstOrDefault();
                if (proxyView != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Found existing proxy view: {proxyView.Name}");
                }

                return proxyView;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding proxy view: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find existing proxy sheet in the document
        /// </summary>
        public static ViewSheet FindExistingProxySheet(Document doc)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                var sheets = collector.OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => s.SheetNumber == PROXY_SHEET_NUMBER || s.Name == PROXY_SHEET_NAME)
                    .ToList();

                var proxySheet = sheets.FirstOrDefault();
                if (proxySheet != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Found existing proxy sheet: {proxySheet.SheetNumber}");
                }

                return proxySheet;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding proxy sheet: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find existing viewport for the proxy view on proxy sheet
        /// </summary>
        public static Viewport FindExistingViewport(Document doc, ViewDrafting view, ViewSheet sheet)
        {
            try
            {
                var viewports = new FilteredElementCollector(doc, sheet.Id)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .Where(vp => vp.ViewId == view.Id)
                    .ToList();

                var viewport = viewports.FirstOrDefault();
                if (viewport != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Found existing viewport: {viewport.Id}");
                }

                return viewport;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding viewport: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the proxy view for creating native callouts/sections
        /// </summary>
        public static View GetProxyViewForReference(Document doc)
        {
            try
            {
                // Ensure complete proxy system exists first
                if (!EnsureProxyViewExists(doc, false))
                {
                    return null;
                }

                // Return the proxy view
                return FindExistingProxyView(doc);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error getting proxy view for reference: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Create a simple drafting view (from our successful test logic)
        /// </summary>
        private static DetailGenerationResult CreateSimpleDraftingView(Document doc, string viewName)
        {
            var result = new DetailGenerationResult();

            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 Creating simple drafting view: {viewName}");

                // Step 1: Validate inputs
                if (doc == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Document is null";
                    return result;
                }

                if (doc.IsReadOnly)
                {
                    result.Success = false;
                    result.ErrorMessage = "Document is read-only";
                    return result;
                }

                // Step 2: Get drafting view type
                var draftingViewTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .Where(vft => vft.ViewFamily == ViewFamily.Drafting)
                    .ToList();

                if (!draftingViewTypes.Any())
                {
                    result.Success = false;
                    result.ErrorMessage = "No drafting view types found in document";
                    return result;
                }

                var draftingViewType = draftingViewTypes.First();

                // Step 3: Create the view
                using (Transaction trans = new Transaction(doc, "Create Proxy Drafting View"))
                {
                    trans.Start();

                    ViewDrafting newView = ViewDrafting.Create(doc, draftingViewType.Id);
                    if (newView == null)
                    {
                        trans.RollBack();
                        result.Success = false;
                        result.ErrorMessage = "ViewDrafting.Create returned null";
                        return result;
                    }

                    // Set the view name
                    try
                    {
                        newView.Name = viewName;
                        System.Diagnostics.Debug.WriteLine($"✅ View name set to: {newView.Name}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Could not set view name: {ex.Message}");
                        // Continue anyway - view was created
                    }

                    // Add proxy content
                    try
                    {
                        var textTypes = new FilteredElementCollector(doc)
                            .OfClass(typeof(TextNoteType))
                            .Cast<TextNoteType>()
                            .ToList();

                        if (textTypes.Any())
                        {
                            var textType = textTypes.First();
                            var options = new TextNoteOptions();
                            options.TypeId = textType.Id;
                            options.HorizontalAlignment = HorizontalTextAlignment.Center;

                            TextNote.Create(doc, newView.Id, XYZ.Zero, "PORTFOLIO PROXY", options);
                            System.Diagnostics.Debug.WriteLine("✅ Added proxy content to view");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Could not add content: {ex.Message}");
                        // Continue anyway - view was created
                    }

                    trans.Commit();

                    result.Success = true;
                    result.CreatedView = newView;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Exception creating view: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ Exception creating simple drafting view: {ex}");
                return result;
            }
        }

        /// <summary>
        /// Create a simple sheet (from our successful test logic)
        /// </summary>
        private static DetailGenerationResult CreateSimpleSheet(Document doc, string sheetNumber, string sheetName)
        {
            var result = new DetailGenerationResult();

            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 Creating simple sheet: {sheetNumber}");

                // Step 1: Validate inputs
                if (doc == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Document is null";
                    return result;
                }

                if (doc.IsReadOnly)
                {
                    result.Success = false;
                    result.ErrorMessage = "Document is read-only";
                    return result;
                }

                // Step 2: Get title block
                var titleBlocks = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilySymbol>()
                    .ToList();

                if (!titleBlocks.Any())
                {
                    result.Success = false;
                    result.ErrorMessage = "No title block families found. Please load a title block family first.";
                    return result;
                }

                var titleBlockType = titleBlocks.First();

                // Ensure title block is active
                if (!titleBlockType.IsActive)
                {
                    titleBlockType.Activate();
                }

                // Step 3: Create the sheet
                using (Transaction trans = new Transaction(doc, "Create Proxy Sheet"))
                {
                    trans.Start();

                    ViewSheet newSheet = ViewSheet.Create(doc, titleBlockType.Id);
                    if (newSheet == null)
                    {
                        trans.RollBack();
                        result.Success = false;
                        result.ErrorMessage = "ViewSheet.Create returned null";
                        return result;
                    }

                    // Set sheet properties
                    try
                    {
                        newSheet.SheetNumber = sheetNumber;
                        System.Diagnostics.Debug.WriteLine($"✅ Sheet number set to: {newSheet.SheetNumber}");
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        result.Success = false;
                        result.ErrorMessage = $"Could not set sheet number: {ex.Message}";
                        return result;
                    }

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(sheetName))
                        {
                            newSheet.Name = sheetName;
                            System.Diagnostics.Debug.WriteLine($"✅ Sheet name set to: {newSheet.Name}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Could not set sheet name: {ex.Message}");
                        // Continue anyway - sheet was created successfully
                    }

                    trans.Commit();

                    result.Success = true;
                    result.CreatedSheet = newSheet;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Exception creating sheet: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ Exception creating simple sheet: {ex}");
                return result;
            }
        }

        /// <summary>
        /// Place view on sheet using simple viewport placement (from our successful test logic)
        /// </summary>
        private static DetailGenerationResult PlaceViewOnSheetSimple(Document doc, ViewDrafting view, ViewSheet sheet)
        {
            var result = new DetailGenerationResult();

            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 Placing view {view.Name} on sheet {sheet.SheetNumber}");

                // Step 1: Check if view is on any other sheet
                var allSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .ToList();

                foreach (var otherSheet in allSheets.Where(s => s.Id != sheet.Id))
                {
                    var placedViews = otherSheet.GetAllPlacedViews();
                    if (placedViews.Contains(view.Id))
                    {
                        result.Success = false;
                        result.ErrorMessage = $"View '{view.Name}' is already placed on sheet '{otherSheet.SheetNumber}'. A view can only be placed on one sheet.";
                        return result;
                    }
                }

                // Step 2: Place the view on the sheet
                using (Transaction trans = new Transaction(doc, "Place Proxy View on Sheet"))
                {
                    trans.Start();

                    try
                    {
                        // Get sheet outline for centering
                        var sheetOutline = sheet.Outline;
                        var centerPoint = new XYZ(
                            (sheetOutline.Min.U + sheetOutline.Max.U) / 2,
                            (sheetOutline.Min.V + sheetOutline.Max.V) / 2,
                            0);

                        // Create viewport
                        var viewport = Viewport.Create(doc, sheet.Id, view.Id, centerPoint);

                        if (viewport == null)
                        {
                            trans.RollBack();
                            result.Success = false;
                            result.ErrorMessage = "Viewport.Create returned null";
                            return result;
                        }

                        trans.Commit();

                        result.Success = true;
                        result.CreatedViewport = viewport;
                        result.CreatedView = view;
                        result.CreatedSheet = sheet;
                        return result;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        result.Success = false;
                        result.ErrorMessage = $"Failed to create viewport: {ex.Message}";
                        System.Diagnostics.Debug.WriteLine($"❌ Viewport creation failed: {ex.Message}");
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Exception placing view: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ Exception placing view on sheet: {ex}");
                return result;
            }
        }

        /// <summary>
        /// Set the detail number parameter on the viewport
        /// </summary>
        private static bool SetDetailNumber(Document doc, Viewport viewport, string detailNumber)
        {
            try
            {
                using (Transaction trans = new Transaction(doc, "Set Proxy Detail Number"))
                {
                    trans.Start();

                    var detailParam = viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                    if (detailParam != null && !detailParam.IsReadOnly)
                    {
                        detailParam.Set(detailNumber);
                        trans.Commit();
                        System.Diagnostics.Debug.WriteLine($"✅ Detail number set to: {detailNumber}");
                        return true;
                    }
                    else
                    {
                        trans.RollBack();
                        System.Diagnostics.Debug.WriteLine("⚠️ Could not access detail number parameter");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Could not set detail number: {ex.Message}");
                return false;
            }
        }
    }
}