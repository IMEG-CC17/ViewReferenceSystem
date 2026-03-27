using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ViewReferenceSystem.Utilities
{
    /// <summary>
    /// FIXED DetailViewGenerator with proper sheet creation and duplicate detection
    /// </summary>
    public static class DetailViewGenerator
    {
        // FIXED: Use X-XX naming pattern as requested
        private const string PROXY_VIEW_PREFIX = "X";
        private const string PROXY_SHEET_NUMBER = "X-XX";
        private const string PROXY_SHEET_NAME = "Portfolio Proxy Sheet";

        /// <summary>
        /// FIXED: Create proxy view AND sheet with duplicate detection
        /// </summary>
        public static DetailGenerationResult GenerateProxyDetailView(
            Document doc,
            string baseViewName = "X")
        {
            var result = new DetailGenerationResult();

            try
            {
                System.Diagnostics.Debug.WriteLine("🚀 === GenerateProxyDetailView() START ===");
                System.Diagnostics.Debug.WriteLine($"📋 Parameters: baseViewName='{baseViewName}', document='{doc?.Title}'");

                // Step 1: Validate inputs
                if (!ValidateInputs(doc, out string validationError))
                {
                    result.Success = false;
                    result.ErrorMessage = validationError;
                    System.Diagnostics.Debug.WriteLine($"❌ Input validation failed: {validationError}");
                    return result;
                }

                // Step 2: CRITICAL FIX - Check for existing proxy view and sheet
                if (ProxySystemAlreadyExists(doc))
                {
                    result.Success = true;
                    result.CreatedView = FindExistingProxyView(doc);
                    result.CreatedSheet = FindExistingProxySheet(doc);
                    System.Diagnostics.Debug.WriteLine("✅ Proxy system already exists - returning existing elements");
                    return result;
                }

                // Step 3: Get required types
                if (!GetRequiredTypesEnhanced(doc, out ViewFamilyType draftingViewType, out FamilySymbol titleBlockType, out string typeError))
                {
                    result.Success = false;
                    result.ErrorMessage = typeError;
                    System.Diagnostics.Debug.WriteLine($"❌ Type selection failed: {typeError}");
                    return result;
                }

                // Step 4: Create both view and sheet in single transaction
                using (Transaction trans = new Transaction(doc, "Create Proxy View and Sheet"))
                {
                    trans.Start();

                    try
                    {
                        // STEP 4A: Create the proxy drafting view
                        System.Diagnostics.Debug.WriteLine("🔧 Creating proxy drafting view...");
                        ViewDrafting proxyView = CreateProxyDraftingView(doc, draftingViewType, baseViewName);
                        if (proxyView == null)
                        {
                            trans.RollBack();
                            result.Success = false;
                            result.ErrorMessage = "Failed to create proxy drafting view";
                            return result;
                        }

                        // STEP 4B: Create the proxy sheet
                        System.Diagnostics.Debug.WriteLine("🔧 Creating proxy sheet...");
                        ViewSheet proxySheet = CreateProxySheet(doc, titleBlockType);
                        if (proxySheet == null)
                        {
                            trans.RollBack();
                            result.Success = false;
                            result.ErrorMessage = "Failed to create proxy sheet";
                            return result;
                        }

                        // STEP 4C: Place the view on the sheet
                        System.Diagnostics.Debug.WriteLine("🔧 Placing view on sheet...");
                        Viewport proxyViewport = PlaceViewOnSheet(doc, proxyView, proxySheet);
                        if (proxyViewport == null)
                        {
                            trans.RollBack();
                            result.Success = false;
                            result.ErrorMessage = "Failed to place view on sheet";
                            return result;
                        }

                        // STEP 4D: Add content to the proxy view
                        AddProxyViewContent(doc, proxyView);

                        trans.Commit();

                        result.Success = true;
                        result.CreatedView = proxyView;
                        result.CreatedSheet = proxySheet;
                        result.CreatedViewport = proxyViewport;

                        System.Diagnostics.Debug.WriteLine("✅ Complete proxy system created successfully");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        result.Success = false;
                        result.ErrorMessage = $"Error creating proxy system: {ex.Message}";
                        System.Diagnostics.Debug.WriteLine($"❌ Proxy system creation failed: {ex.Message}");
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Unexpected error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"💥 EXCEPTION: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// NEW: Check if proxy system (view AND sheet) already exists
        /// </summary>
        private static bool ProxySystemAlreadyExists(Document doc)
        {
            try
            {
                ViewDrafting existingView = FindExistingProxyView(doc);
                ViewSheet existingSheet = FindExistingProxySheet(doc);

                bool viewExists = existingView != null;
                bool sheetExists = existingSheet != null;

                System.Diagnostics.Debug.WriteLine($"🔍 Proxy system check: View exists={viewExists}, Sheet exists={sheetExists}");

                // Both should exist for a complete proxy system
                if (viewExists && sheetExists)
                {
                    System.Diagnostics.Debug.WriteLine("✅ Complete proxy system already exists");
                    return true;
                }
                else if (viewExists || sheetExists)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Partial proxy system exists - will recreate");
                    // Could add logic here to clean up partial systems if needed
                    return false;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("🔍 No proxy system exists - will create");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error checking proxy system: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// NEW: Find existing proxy view by exact name match
        /// </summary>
        private static ViewDrafting FindExistingProxyView(Document doc)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                var views = collector.OfClass(typeof(ViewDrafting))
                    .Cast<ViewDrafting>()
                    .Where(v => !v.IsTemplate && v.Name == PROXY_VIEW_PREFIX)
                    .ToList();

                return views.FirstOrDefault();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding proxy view: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// NEW: Find existing proxy sheet by number or name
        /// </summary>
        private static ViewSheet FindExistingProxySheet(Document doc)
        {
            try
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                var sheets = collector.OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => s.SheetNumber == PROXY_SHEET_NUMBER || s.Name == PROXY_SHEET_NAME)
                    .ToList();

                return sheets.FirstOrDefault();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding proxy sheet: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// FIXED: Create proxy sheet with proper configuration
        /// </summary>
        private static ViewSheet CreateProxySheet(Document doc, FamilySymbol titleBlockType)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 === PROXY SHEET CREATION START ===");
                System.Diagnostics.Debug.WriteLine($"📋 Title Block Details:");
                System.Diagnostics.Debug.WriteLine($"    Family: {titleBlockType.FamilyName}");
                System.Diagnostics.Debug.WriteLine($"    Type: {titleBlockType.Name}");
                System.Diagnostics.Debug.WriteLine($"    Active: {titleBlockType.IsActive}");

                // Ensure title block is active
                if (!titleBlockType.IsActive)
                {
                    System.Diagnostics.Debug.WriteLine("🔧 Activating title block...");
                    titleBlockType.Activate();
                    System.Diagnostics.Debug.WriteLine($"✅ Title block activated");
                }

                // Create the sheet
                System.Diagnostics.Debug.WriteLine($"🔧 Creating sheet with title block ID: {titleBlockType.Id}");
                ViewSheet newSheet = ViewSheet.Create(doc, titleBlockType.Id);

                if (newSheet == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ ViewSheet.Create returned null");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Sheet created with ID: {newSheet.Id}");

                // Set sheet properties
                try
                {
                    System.Diagnostics.Debug.WriteLine($"🔧 Setting sheet number to: {PROXY_SHEET_NUMBER}");
                    newSheet.SheetNumber = PROXY_SHEET_NUMBER;
                    System.Diagnostics.Debug.WriteLine($"✅ Sheet number set: '{newSheet.SheetNumber}'");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Could not set sheet number: {ex.Message}");
                    return null; // Sheet number is critical for proxy sheets
                }

                try
                {
                    System.Diagnostics.Debug.WriteLine($"🔧 Setting sheet name to: {PROXY_SHEET_NAME}");
                    newSheet.Name = PROXY_SHEET_NAME;
                    System.Diagnostics.Debug.WriteLine($"✅ Sheet name set: '{newSheet.Name}'");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Could not set sheet name: {ex.Message}");
                    // Continue anyway - name is less critical than number
                }

                System.Diagnostics.Debug.WriteLine($"🔧 === PROXY SHEET CREATION SUCCESS ===");
                return newSheet;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ === PROXY SHEET CREATION FAILED ===");
                System.Diagnostics.Debug.WriteLine($"❌ Exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// FIXED: Place proxy view on proxy sheet with proper viewport configuration
        /// </summary>
        private static Viewport PlaceViewOnSheet(Document doc, ViewDrafting view, ViewSheet sheet)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔧 === VIEWPORT PLACEMENT START ===");

                // Get sheet outline for positioning
                BoundingBoxUV outline = sheet.Outline;
                if (outline == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Sheet outline is null");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"📊 Sheet outline: Min({outline.Min.U:F2}, {outline.Min.V:F2}) Max({outline.Max.U:F2}, {outline.Max.V:F2})");

                // Check if view can be placed
                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                {
                    System.Diagnostics.Debug.WriteLine("❌ Cannot add view to sheet");
                    return null;
                }

                // Calculate center position
                XYZ position = new XYZ(
                    (outline.Min.U + outline.Max.U) / 2,
                    (outline.Min.V + outline.Max.V) / 2,
                    0);

                System.Diagnostics.Debug.WriteLine($"📍 Placing viewport at position: ({position.X:F2}, {position.Y:F2}, {position.Z:F2})");

                // Create viewport
                Viewport viewport = Viewport.Create(doc, sheet.Id, view.Id, position);

                if (viewport == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Viewport.Create returned null");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Viewport created with ID: {viewport.Id}");

                // Set detail number to "-" for proxy
                try
                {
                    Parameter detailParam = viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                    if (detailParam != null && !detailParam.IsReadOnly)
                    {
                        detailParam.Set("-");
                        System.Diagnostics.Debug.WriteLine("✅ Detail number set to '-'");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Could not set detail number: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("🔧 === VIEWPORT PLACEMENT SUCCESS ===");
                return viewport;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Viewport placement error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ENHANCED: Create sheet with comprehensive debugging and validation
        /// </summary>
        private static ViewSheet CreateSheetEnhanced(Document doc, FamilySymbol titleBlockType, string sheetNumber, string sheetName)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 === SHEET CREATION START ===");
                System.Diagnostics.Debug.WriteLine($"📋 Title Block Details:");
                System.Diagnostics.Debug.WriteLine($"    Family: {titleBlockType.FamilyName}");
                System.Diagnostics.Debug.WriteLine($"    Type: {titleBlockType.Name}");
                System.Diagnostics.Debug.WriteLine($"    ID: {titleBlockType.Id}");
                System.Diagnostics.Debug.WriteLine($"    Active: {titleBlockType.IsActive}");

                // Ensure title block is active before using it
                if (!titleBlockType.IsActive)
                {
                    System.Diagnostics.Debug.WriteLine("🔧 Activating title block...");
                    try
                    {
                        titleBlockType.Activate();
                        System.Diagnostics.Debug.WriteLine($"✅ Title block activated successfully");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Failed to activate title block: {ex.Message}");
                        return null;
                    }
                }

                // Attempt sheet creation
                System.Diagnostics.Debug.WriteLine($"🔧 Calling ViewSheet.Create...");
                ViewSheet newSheet = null;
                try
                {
                    newSheet = ViewSheet.Create(doc, titleBlockType.Id);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ ViewSheet.Create threw exception: {ex.Message}");

                    // Try to provide more specific error information
                    if (ex.Message.ToLower().Contains("title block"))
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Title block issue - may be incompatible or corrupt");
                    }
                    return null;
                }

                if (newSheet == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ ViewSheet.Create returned null");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Sheet created with ID: {newSheet.Id}");

                // Test basic sheet functionality
                try
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 Testing sheet basic properties...");
                    var testOutline = newSheet.Outline;
                    if (testOutline == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Sheet outline is null - title block problem");
                        return null;
                    }
                    System.Diagnostics.Debug.WriteLine($"✅ Sheet outline OK: {testOutline.Min.U:F2},{testOutline.Min.V:F2} to {testOutline.Max.U:F2},{testOutline.Max.V:F2}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Sheet outline test failed: {ex.Message}");
                    return null;
                }

                // Set sheet properties with validation
                try
                {
                    System.Diagnostics.Debug.WriteLine($"🔧 Setting sheet number to: {sheetNumber}");
                    newSheet.SheetNumber = sheetNumber;
                    System.Diagnostics.Debug.WriteLine($"✅ Sheet number set: '{newSheet.SheetNumber}'");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Could not set sheet number: {ex.Message}");
                    // Continue anyway - sheet was created
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(sheetName))
                    {
                        System.Diagnostics.Debug.WriteLine($"🔧 Setting sheet name to: {sheetName}");
                        newSheet.Name = sheetName;
                        System.Diagnostics.Debug.WriteLine($"✅ Sheet name set: '{newSheet.Name}'");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ℹ️ No sheet name provided, using default: '{newSheet.Name}'");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Could not set sheet name: {ex.Message}");
                    // Continue anyway - sheet was created
                }

                System.Diagnostics.Debug.WriteLine($"🔧 === SHEET CREATION SUCCESS ===");
                return newSheet;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ === SHEET CREATION FAILED ===");
                System.Diagnostics.Debug.WriteLine($"❌ Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// ENHANCED: Create view and sheet with improved title block selection and viewport handling
        /// </summary>
        public static DetailGenerationResult GenerateDetailViewAndSheet(
            Document doc,
            string viewName,
            string sheetNumber,
            string sheetName = "",
            string detailNumber = "1")
        {
            var result = new DetailGenerationResult();

            try
            {
                System.Diagnostics.Debug.WriteLine("🚀 === GenerateDetailViewAndSheet() START ===");
                System.Diagnostics.Debug.WriteLine($"📋 Parameters: view='{viewName}', sheet='{sheetNumber}', name='{sheetName}', detail='{detailNumber}'");

                // Enhanced validation
                if (!ValidateDetailInputs(doc, viewName, sheetNumber, out string validationError))
                {
                    result.Success = false;
                    result.ErrorMessage = validationError;
                    System.Diagnostics.Debug.WriteLine($"❌ Validation failed: {validationError}");
                    return result;
                }

                // ENHANCED: Get required types with better title block selection
                if (!GetRequiredTypesEnhanced(doc, out ViewFamilyType draftingViewType, out FamilySymbol titleBlockType, out string typeError))
                {
                    result.Success = false;
                    result.ErrorMessage = typeError;
                    System.Diagnostics.Debug.WriteLine($"❌ Type selection failed: {typeError}");
                    return result;
                }

                // Create view and sheet in single transaction for better reliability
                using (Transaction trans = new Transaction(doc, "Create Detail View and Sheet"))
                {
                    trans.Start();
                    System.Diagnostics.Debug.WriteLine("✅ Transaction started");

                    try
                    {
                        // Create drafting view
                        System.Diagnostics.Debug.WriteLine($"🔧 STEP 1: Creating drafting view '{viewName}'...");
                        var createdView = CreateDetailView(doc, draftingViewType, viewName);
                        if (createdView == null)
                        {
                            trans.RollBack();
                            result.Success = false;
                            result.ErrorMessage = "Failed to create detail view";
                            System.Diagnostics.Debug.WriteLine($"❌ STEP 1 FAILED: View creation returned null");
                            return result;
                        }
                        System.Diagnostics.Debug.WriteLine($"✅ STEP 1 SUCCESS: View created - {createdView.Name} (ID: {createdView.Id})");

                        // Create sheet with enhanced title block handling and validation
                        System.Diagnostics.Debug.WriteLine($"🔧 STEP 2: Creating sheet '{sheetNumber}'...");
                        var createdSheet = CreateSheetEnhanced(doc, titleBlockType, sheetNumber, sheetName);
                        if (createdSheet == null)
                        {
                            trans.RollBack();
                            result.Success = false;
                            result.ErrorMessage = "Failed to create sheet - CreateSheetEnhanced returned null";
                            System.Diagnostics.Debug.WriteLine($"❌ STEP 2 FAILED: Sheet creation returned null");
                            return result;
                        }
                        System.Diagnostics.Debug.WriteLine($"✅ STEP 2 SUCCESS: Sheet created - {createdSheet.SheetNumber} (ID: {createdSheet.Id})");

                        // CRITICAL: Validate the sheet was actually created properly
                        System.Diagnostics.Debug.WriteLine($"🔍 STEP 3: Validating created sheet...");
                        System.Diagnostics.Debug.WriteLine($"    Sheet ID: {createdSheet.Id}");
                        System.Diagnostics.Debug.WriteLine($"    Sheet Number: '{createdSheet.SheetNumber}'");
                        System.Diagnostics.Debug.WriteLine($"    Sheet Name: '{createdSheet.Name}'");

                        try
                        {
                            var sheetOutline = createdSheet.Outline;
                            if (sheetOutline == null)
                            {
                                trans.RollBack();
                                result.Success = false;
                                result.ErrorMessage = "Sheet created but has no outline - title block issue";
                                System.Diagnostics.Debug.WriteLine($"❌ STEP 3 FAILED: Sheet outline is null");
                                return result;
                            }
                            System.Diagnostics.Debug.WriteLine($"✅ STEP 3 SUCCESS: Sheet outline: Min({sheetOutline.Min.U:F2}, {sheetOutline.Min.V:F2}) Max({sheetOutline.Max.U:F2}, {sheetOutline.Max.V:F2})");
                        }
                        catch (Exception ex)
                        {
                            trans.RollBack();
                            result.Success = false;
                            result.ErrorMessage = $"Sheet created but outline access failed: {ex.Message}";
                            System.Diagnostics.Debug.WriteLine($"❌ STEP 3 FAILED: Outline access exception: {ex.Message}");
                            return result;
                        }

                        // ENHANCED: Viewport creation with comprehensive debugging
                        System.Diagnostics.Debug.WriteLine($"🔧 STEP 4: Creating viewport...");
                        var createdViewport = CreateViewportWithExtensiveDebugging(doc, createdView, createdSheet, detailNumber);
                        if (createdViewport == null)
                        {
                            trans.RollBack();
                            result.Success = false;
                            result.ErrorMessage = "Failed to create viewport - see debug output for details";
                            System.Diagnostics.Debug.WriteLine($"❌ STEP 4 FAILED: Viewport creation returned null");
                            return result;
                        }
                        System.Diagnostics.Debug.WriteLine($"✅ STEP 4 SUCCESS: Viewport created (ID: {createdViewport.Id})");

                        trans.Commit();
                        System.Diagnostics.Debug.WriteLine("✅ Transaction committed successfully");

                        result.Success = true;
                        result.CreatedView = createdView;
                        result.CreatedSheet = createdSheet;
                        result.CreatedViewport = createdViewport;

                        System.Diagnostics.Debug.WriteLine("✅ === GenerateDetailViewAndSheet() COMPLETE SUCCESS ===");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        result.Success = false;
                        result.ErrorMessage = $"Transaction failed: {ex.Message}";
                        System.Diagnostics.Debug.WriteLine($"❌ Transaction exception: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Unexpected error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"💥 OUTER EXCEPTION: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"💥 Stack trace: {ex.StackTrace}");
                return result;
            }
        }

        // Keep all the existing helper methods...
        private static bool ValidateInputs(Document doc, out string error)
        {
            error = "";

            if (doc == null)
            {
                error = "Document is null";
                return false;
            }

            if (doc.IsReadOnly)
            {
                error = "Document is read-only";
                return false;
            }

            return true;
        }

        private static bool ValidateDetailInputs(Document doc, string viewName, string sheetNumber, out string error)
        {
            error = "";

            if (!ValidateInputs(doc, out error))
                return false;

            if (string.IsNullOrWhiteSpace(viewName))
            {
                error = "View name cannot be empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sheetNumber))
            {
                error = "Sheet number cannot be empty";
                return false;
            }

            // Check for existing view name
            var existingView = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .FirstOrDefault(v => v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));

            if (existingView != null)
            {
                error = $"View '{viewName}' already exists";
                return false;
            }

            // Check for existing sheet number
            var existingSheet = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(s => s.SheetNumber.Equals(sheetNumber, StringComparison.OrdinalIgnoreCase));

            if (existingSheet != null)
            {
                error = $"Sheet '{sheetNumber}' already exists";
                return false;
            }

            return true;
        }

        private static bool GetDraftingViewType(Document doc, out ViewFamilyType draftingViewType, out string error)
        {
            draftingViewType = null;
            error = "";

            try
            {
                var viewTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .Where(vft => vft.ViewFamily == ViewFamily.Drafting)
                    .ToList();

                if (!viewTypes.Any())
                {
                    error = "No drafting view types found in document";
                    return false;
                }

                draftingViewType = viewTypes.First();
                System.Diagnostics.Debug.WriteLine($"✅ Found drafting view type: {draftingViewType.Name}");
                return true;
            }
            catch (Exception ex)
            {
                error = $"Error finding drafting view type: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// ENHANCED: Get required types with improved title block selection and better error handling
        /// </summary>
        private static bool GetRequiredTypesEnhanced(Document doc, out ViewFamilyType draftingViewType, out FamilySymbol titleBlockType, out string error)
        {
            draftingViewType = null;
            titleBlockType = null;
            error = "";

            // Get drafting view type
            if (!GetDraftingViewType(doc, out draftingViewType, out error))
                return false;

            // ENHANCED: Get title block type with better selection logic and error handling
            try
            {
                System.Diagnostics.Debug.WriteLine("🔍 Enhanced title block selection...");

                var allTitleBlocks = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilySymbol>()
                    .ToList();

                if (!allTitleBlocks.Any())
                {
                    error = "No title block families found in document. Please load a title block family (File → Load Family) and try again.";
                    System.Diagnostics.Debug.WriteLine("❌ No title blocks found");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"📋 Found {allTitleBlocks.Count} title blocks:");
                foreach (var tb in allTitleBlocks)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {tb.FamilyName}: {tb.Name} (Active: {tb.IsActive})");
                }

                // SELECTION STRATEGY: Multiple fallback approaches
                titleBlockType = null;

                // Strategy 1: Find an active, usable title block
                titleBlockType = allTitleBlocks.FirstOrDefault(tb =>
                    tb.IsActive &&
                    !tb.Name.ToLower().Contains("none") &&
                    !tb.Name.ToLower().Contains("sample") &&
                    !tb.Name.ToLower().Contains("no title block"));

                // Strategy 2: Any active title block
                if (titleBlockType == null)
                {
                    titleBlockType = allTitleBlocks.FirstOrDefault(tb => tb.IsActive);
                    System.Diagnostics.Debug.WriteLine("🔄 Using any active title block");
                }

                // Strategy 3: Activate a good candidate
                if (titleBlockType == null)
                {
                    var candidateBlock = allTitleBlocks.FirstOrDefault(tb =>
                        !tb.Name.ToLower().Contains("none") &&
                        !tb.Name.ToLower().Contains("sample") &&
                        !tb.Name.ToLower().Contains("no title block"));

                    if (candidateBlock != null)
                    {
                        try
                        {
                            candidateBlock.Activate();
                            titleBlockType = candidateBlock;
                            System.Diagnostics.Debug.WriteLine($"✅ Activated candidate title block: {titleBlockType.FamilyName}: {titleBlockType.Name}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ Failed to activate candidate: {ex.Message}");
                        }
                    }
                }

                // Strategy 4: Try any title block as last resort
                if (titleBlockType == null)
                {
                    var lastResort = allTitleBlocks.FirstOrDefault();
                    if (lastResort != null)
                    {
                        try
                        {
                            if (!lastResort.IsActive)
                            {
                                lastResort.Activate();
                            }
                            titleBlockType = lastResort;
                            System.Diagnostics.Debug.WriteLine($"⚠️ Using last resort title block: {titleBlockType.FamilyName}: {titleBlockType.Name}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Last resort activation failed: {ex.Message}");
                        }
                    }
                }

                // Final check
                if (titleBlockType == null)
                {
                    error = "Could not find or activate any usable title block. Available title blocks may be corrupted or incompatible.";
                    System.Diagnostics.Debug.WriteLine("❌ All title block strategies failed");
                    return false;
                }

                // Verify the selected title block
                try
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 Verifying selected title block: {titleBlockType.FamilyName}: {titleBlockType.Name}");

                    // Test if we can access basic properties
                    var testId = titleBlockType.Id;
                    var testActive = titleBlockType.IsActive;
                    var testFamily = titleBlockType.FamilyName;

                    System.Diagnostics.Debug.WriteLine($"✅ Title block verification passed");
                    System.Diagnostics.Debug.WriteLine($"✅ Selected title block: {titleBlockType.FamilyName}: {titleBlockType.Name} (Active: {titleBlockType.IsActive})");
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Selected title block appears to be corrupted: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine($"❌ Title block verification failed: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = $"Error during title block selection: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ Title block selection error: {ex.Message}");
                return false;
            }
        }

        private static ViewDrafting CreateProxyDraftingView(Document doc, ViewFamilyType draftingViewType, string baseName)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔧 Creating drafting view with type: {draftingViewType.Name}");

                ViewDrafting newView = ViewDrafting.Create(doc, draftingViewType.Id);
                if (newView == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ ViewDrafting.Create returned null");
                    return null;
                }

                try
                {
                    newView.Name = baseName;
                    System.Diagnostics.Debug.WriteLine($"✅ View name set to: {newView.Name}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Could not set view name to '{baseName}': {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"📝 Using default name: {newView.Name}");
                }

                return newView;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creating drafting view: {ex.Message}");
                return null;
            }
        }

        private static ViewDrafting CreateDetailView(Document doc, ViewFamilyType draftingViewType, string viewName)
        {
            try
            {
                var newView = ViewDrafting.Create(doc, draftingViewType.Id);
                if (newView == null) return null;

                newView.Name = viewName;
                System.Diagnostics.Debug.WriteLine($"✅ Detail view created: {newView.Name}");
                return newView;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creating detail view: {ex.Message}");
                return null;
            }
        }

        private static void AddProxyViewContent(Document doc, ViewDrafting view)
        {
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

                    TextNote.Create(doc, view.Id, XYZ.Zero, "PORTFOLIO PROXY", options);
                    System.Diagnostics.Debug.WriteLine("✅ Added proxy view content");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Could not add proxy content: {ex.Message}");
            }
        }

        /// <summary>
        /// ENHANCED: Viewport creation with comprehensive debugging and multiple strategies
        /// </summary>
        private static Viewport CreateViewportWithExtensiveDebugging(Document doc, ViewDrafting view, ViewSheet sheet, string detailNumber)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔧 === ENHANCED VIEWPORT CREATION START ===");
                System.Diagnostics.Debug.WriteLine($"📋 View Details:");
                System.Diagnostics.Debug.WriteLine($"    View ID: {view.Id}");
                System.Diagnostics.Debug.WriteLine($"    View Name: '{view.Name}'");
                System.Diagnostics.Debug.WriteLine($"    View Type: {view.ViewType}");
                System.Diagnostics.Debug.WriteLine($"    Can Be Printed: {view.CanBePrinted}");
                System.Diagnostics.Debug.WriteLine($"    Is Template: {view.IsTemplate}");

                System.Diagnostics.Debug.WriteLine($"📋 Sheet Details:");
                System.Diagnostics.Debug.WriteLine($"    Sheet ID: {sheet.Id}");
                System.Diagnostics.Debug.WriteLine($"    Sheet Number: '{sheet.SheetNumber}'");
                System.Diagnostics.Debug.WriteLine($"    Sheet Name: '{sheet.Name}'");

                // CRITICAL TEST: Can we add this view to this sheet?
                System.Diagnostics.Debug.WriteLine("🔍 Testing Viewport.CanAddViewToSheet...");
                bool canAddView = false;
                try
                {
                    canAddView = Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id);
                    System.Diagnostics.Debug.WriteLine($"✅ CanAddViewToSheet result: {canAddView}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ CanAddViewToSheet threw exception: {ex.Message}");
                    return null;
                }

                if (!canAddView)
                {
                    System.Diagnostics.Debug.WriteLine("❌ CanAddViewToSheet returned false - investigating reasons...");

                    // Check if view is already placed
                    bool isAlreadyPlaced = IsViewAlreadyOnAnySheet(doc, view);
                    System.Diagnostics.Debug.WriteLine($"    View already placed: {isAlreadyPlaced}");

                    // Check view type compatibility
                    bool supportsPlacement = SupportsViewportPlacement(view);
                    System.Diagnostics.Debug.WriteLine($"    Supports placement: {supportsPlacement}");

                    return null;
                }

                // Get sheet outline for positioning
                System.Diagnostics.Debug.WriteLine("🔍 Getting sheet outline...");
                BoundingBoxUV outline = null;
                try
                {
                    outline = sheet.Outline;
                    if (outline != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Sheet outline: Min({outline.Min.U:F2}, {outline.Min.V:F2}) Max({outline.Max.U:F2}, {outline.Max.V:F2})");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("❌ Sheet outline is null");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error getting sheet outline: {ex.Message}");
                    return null;
                }

                // STRATEGY: Try multiple viewport creation approaches
                System.Diagnostics.Debug.WriteLine("🔧 Attempting viewport creation with multiple strategies...");

                XYZ[] positions = {
                    new XYZ((outline.Min.U + outline.Max.U) / 2, (outline.Min.V + outline.Max.V) / 2, 0), // Center
                    new XYZ(outline.Min.U + 0.5, outline.Min.V + 0.5, 0), // Near corner with margin
                    new XYZ(outline.Max.U - 0.5, outline.Max.V - 0.5, 0), // Far corner with margin
                    new XYZ(outline.Min.U + 1.0, outline.Min.V + 1.0, 0), // Larger margin
                    new XYZ(1, 1, 0), // Simple position
                    new XYZ(0.5, 0.5, 0), // Even simpler
                    new XYZ(0, 0, 0) // Origin
                };

                Viewport viewport = null;
                for (int i = 0; i < positions.Length; i++)
                {
                    var position = positions[i];
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"🔧 Strategy {i + 1}: Position ({position.X:F2}, {position.Y:F2}, {position.Z:F2})");

                        // Double-check before each attempt
                        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Strategy {i + 1}: CanAddViewToSheet became false");
                            continue;
                        }

                        viewport = Viewport.Create(doc, sheet.Id, view.Id, position);

                        if (viewport != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"✅ Strategy {i + 1} SUCCESS: Viewport created with ID {viewport.Id}");
                            break;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Strategy {i + 1}: Create returned null");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Strategy {i + 1} exception: {ex.Message}");

                        // If it's a placement issue, try next position
                        if (ex.Message.ToLower().Contains("placement") || ex.Message.ToLower().Contains("position"))
                        {
                            continue;
                        }
                        else
                        {
                            // Other errors might be fatal
                            System.Diagnostics.Debug.WriteLine($"❌ Fatal error, stopping attempts: {ex.Message}");
                            break;
                        }
                    }
                }

                if (viewport == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ ALL viewport creation strategies failed");
                    return null;
                }

                // Set detail number
                try
                {
                    var detailParam = viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);
                    if (detailParam != null && !detailParam.IsReadOnly)
                    {
                        detailParam.Set(detailNumber);
                        System.Diagnostics.Debug.WriteLine($"✅ Detail number set to: {detailNumber}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ Could not access detail number parameter");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Could not set detail number: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("🔧 === ENHANCED VIEWPORT CREATION SUCCESS ===");
                return viewport;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Viewport creation error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private static bool IsViewAlreadyOnAnySheet(Document doc, View view)
        {
            try
            {
                var allSheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>();

                foreach (var sheet in allSheets)
                {
                    var placedViews = sheet.GetAllPlacedViews();
                    if (placedViews.Contains(view.Id))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool SupportsViewportPlacement(View view)
        {
            try
            {
                return view.ViewType == ViewType.DraftingView ||
                       view.ViewType == ViewType.FloorPlan ||
                       view.ViewType == ViewType.CeilingPlan ||
                       view.ViewType == ViewType.Section ||
                       view.ViewType == ViewType.Elevation ||
                       view.ViewType == ViewType.Detail ||
                       view.ViewType == ViewType.ThreeD ||
                       view.ViewType == ViewType.Schedule;
            }
            catch
            {
                return false;
            }
        }

        public static void TestProxyGeneration(Document doc)
        {
            System.Diagnostics.Debug.WriteLine("🧪 === Testing Proxy View Generation ===");

            // FIRST: Run diagnostic check
            System.Diagnostics.Debug.WriteLine("🔍 === DIAGNOSTIC CHECK START ===");
            string diagnosticResult = RunProxyDiagnostics(doc);
            System.Diagnostics.Debug.WriteLine(diagnosticResult);
            System.Diagnostics.Debug.WriteLine("🔍 === DIAGNOSTIC CHECK END ===");

            var result = GenerateProxyDetailView(doc, "X");

            if (result.Success)
            {
                TaskDialog.Show("Proxy Test Success",
                    $"Complete proxy system created successfully!\n\n" +
                    $"✅ View: {result.CreatedView?.Name} (ID: {result.CreatedView?.Id})\n" +
                    $"✅ Sheet: {result.CreatedSheet?.SheetNumber} (ID: {result.CreatedSheet?.Id})\n" +
                    $"✅ Viewport: ID {result.CreatedViewport?.Id}\n\n" +
                    $"This proxy system enables cross-project detail references with native callouts and sections.");
            }
            else
            {
                TaskDialog errorDialog = new TaskDialog("Proxy Test Failed");
                errorDialog.MainInstruction = "Failed to create proxy system";
                errorDialog.MainContent = $"Error: {result.ErrorMessage}";
                errorDialog.ExpandedContent = $"Diagnostic Information:\n{diagnosticResult}\n\nThis information can help identify what's missing in your project template.";
                errorDialog.Show();
            }
        }

        /// <summary>
        /// PUBLIC: Comprehensive diagnostic check for proxy creation requirements
        /// </summary>
        public static string RunProxyDiagnostics(Document doc)
        {
            var diagnostics = "=== PROXY CREATION DIAGNOSTICS ===\n\n";

            try
            {
                // Document basic info
                diagnostics += $"📋 Document: {doc.Title}\n";
                diagnostics += $"📋 Is Read-Only: {doc.IsReadOnly}\n";
                diagnostics += $"📋 Is Workshared: {doc.IsWorkshared}\n\n";

                // Check drafting view types
                var draftingViewTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .Where(vft => vft.ViewFamily == ViewFamily.Drafting)
                    .ToList();

                diagnostics += $"📊 Drafting View Types: {draftingViewTypes.Count}\n";
                foreach (var vt in draftingViewTypes)
                {
                    diagnostics += $"   • {vt.Name} (ID: {vt.Id})\n";
                }
                diagnostics += "\n";

                // Check title blocks
                var titleBlocks = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .Cast<FamilySymbol>()
                    .ToList();

                diagnostics += $"📊 Title Blocks: {titleBlocks.Count}\n";
                foreach (var tb in titleBlocks)
                {
                    diagnostics += $"   • {tb.FamilyName}: {tb.Name} (Active: {tb.IsActive}, ID: {tb.Id})\n";
                }
                diagnostics += "\n";

                // Check existing proxy elements
                var existingProxyViews = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewDrafting))
                    .Cast<ViewDrafting>()
                    .Where(v => v.Name == "X")
                    .ToList();

                diagnostics += $"📊 Existing Proxy Views: {existingProxyViews.Count}\n";
                foreach (var view in existingProxyViews)
                {
                    diagnostics += $"   • {view.Name} (ID: {view.Id})\n";
                }

                var existingProxySheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => s.SheetNumber == "X-XX")
                    .ToList();

                diagnostics += $"📊 Existing Proxy Sheets: {existingProxySheets.Count}\n";
                foreach (var sheet in existingProxySheets)
                {
                    diagnostics += $"   • {sheet.SheetNumber}: {sheet.Name} (ID: {sheet.Id})\n";
                }
                diagnostics += "\n";

                // Check text note types (for proxy content)
                var textTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .ToList();

                diagnostics += $"📊 Text Note Types: {textTypes.Count}\n";

                // Overall assessment
                diagnostics += "=== ASSESSMENT ===\n";

                if (draftingViewTypes.Count == 0)
                {
                    diagnostics += "❌ CRITICAL: No drafting view types found\n";
                }
                else
                {
                    diagnostics += "✅ Drafting view types available\n";
                }

                if (titleBlocks.Count == 0)
                {
                    diagnostics += "❌ CRITICAL: No title blocks found - this will prevent sheet creation\n";
                    diagnostics += "   SOLUTION: Load a title block family (File → Load Family)\n";
                }
                else
                {
                    var activeTitleBlocks = titleBlocks.Where(tb => tb.IsActive).ToList();
                    if (activeTitleBlocks.Count == 0)
                    {
                        diagnostics += "⚠️ WARNING: No active title blocks - will attempt to activate one\n";
                    }
                    else
                    {
                        diagnostics += "✅ Active title blocks available\n";
                    }
                }

                if (doc.IsReadOnly)
                {
                    diagnostics += "❌ CRITICAL: Document is read-only\n";
                }
                else
                {
                    diagnostics += "✅ Document is writable\n";
                }

                if (existingProxyViews.Count > 0 || existingProxySheets.Count > 0)
                {
                    diagnostics += "ℹ️ INFO: Existing proxy elements found - will reuse if complete\n";
                }

            }
            catch (Exception ex)
            {
                diagnostics += $"❌ ERROR during diagnostics: {ex.Message}\n";
            }

            return diagnostics;
        }
    }

    public class DetailGenerationResult
    {
        public bool Success { get; set; } = false;
        public string ErrorMessage { get; set; } = "";
        public ViewDrafting CreatedView { get; set; } = null;
        public ViewSheet CreatedSheet { get; set; } = null;
        public Viewport CreatedViewport { get; set; } = null;

        public override string ToString()
        {
            if (Success)
            {
                string result = $"Success: View '{CreatedView?.Name}'";
                if (CreatedSheet != null)
                    result += $" on Sheet '{CreatedSheet.SheetNumber}'";
                if (CreatedViewport != null)
                    result += $" with Viewport ID {CreatedViewport.Id}";
                return result;
            }
            else
            {
                return $"Failed: {ErrorMessage}";
            }
        }
    }
}