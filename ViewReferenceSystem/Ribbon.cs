// Ribbon.cs

using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ViewReferenceSystem.Commands;
using ViewReferenceSystem.Core;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.Placement;
using ViewReferenceSystem.UI;
using ViewReferenceSystem.Utilities;
using ViewReferenceSystem.Updater;

namespace ViewReferenceSystem
{
    [Transaction(TransactionMode.Manual)]
    public class Ribbon : IExternalApplication
    {
        #region Fields
        private static Document _lastActiveDocument = null;
        private static Document _lastProjectDocument = null; // Tracks last non-family doc for staleness checks
        private static string _syncLogPath = null;

        // Auto-publish queue: family names detected as loaded/modified that need publishing
        private static readonly Queue<string> _autoPublishQueue = new Queue<string>();
        private static readonly object _queueLock = new object();
        private static bool _idlingSubscribed = false;
        private static UIControlledApplication _uiControlledApp = null;

        // Cache monitored family names per portfolio path — only refreshed when a Family element changes
        private static HashSet<string> _cachedMonitoredNames = null;
        private static string _cachedMonitoredNamesPortfolioPath = null;

        // Project-open update: queue documents that need family/topnote updates on open
        private static Document _pendingOpenUpdateDoc = null;
        private static Document _pendingAutoMigrateDoc = null;
        private static bool _openUpdateIdlingSubscribed = false;
        private static Autodesk.Revit.UI.PushButton _updateButton = null;
        private static UpdaterClient.VersionInfo _pendingUpdate = null;
        private static volatile bool _isShuttingDown = false;

        // Post-sync relinquish: deferred to Idling because RelinquishOwnership
        // cannot run inside DocumentSynchronizedWithCentral (Revit internal error)
        private static Document _pendingPostSyncRelinquishDoc = null;
        #endregion

        #region Sync Diagnostic Log

        /// <summary>
        /// Write to a visible log file next to the portfolio JSON.
        /// Use this instead of Debug.WriteLine for critical sync diagnostics.
        /// </summary>
        private static void SyncLog(string message, string portfolioPath = null)
        {
            try
            {
                // Also write to debug output
                System.Diagnostics.Debug.WriteLine(message);

                // Resolve log path
                if (_syncLogPath == null && !string.IsNullOrEmpty(portfolioPath))
                {
                    string folder = Path.GetDirectoryName(portfolioPath);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        _syncLogPath = Path.Combine(folder, "sync_diagnostic.log");
                    }
                }

                if (_syncLogPath == null) return;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.AppendAllText(_syncLogPath, $"[{timestamp}] {message}\r\n");
            }
            catch { } // Never let logging break sync
        }

        #endregion

        #region Startup & Initialization
        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                // Force TLS 1.2 — required for Firebase. Older Revit versions (2023) don't default to it.
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;

                System.Diagnostics.Debug.WriteLine("🚀 Starting View Reference System Plugin...");

                string tabName = "cc17-dev";

                app.CreateRibbonTab(tabName);
                RibbonPanel panel = app.CreateRibbonPanel(tabName, "Portfolio");

                var saveHandler = new SaveConfigurationHandler();
                var saveEvent = ExternalEvent.Create(saveHandler);

                var placementHelper = new PlacementHelper();
                var placementEvent = ExternalEvent.Create(placementHelper);

                var nativeCalloutHelper = new NativeCalloutHelper();
                var nativeCalloutEvent = ExternalEvent.Create(nativeCalloutHelper);

                var nativeSectionHelper = new NativeSectionHelper();
                var nativeSectionEvent = ExternalEvent.Create(nativeSectionHelper);

                var clearParametersHelper = new ClearParametersHelper();
                var clearParametersEvent = ExternalEvent.Create(clearParametersHelper);

                PortfolioManagePane.InitializeExternalEvents(
                   saveEvent, saveHandler,
                   placementEvent, placementHelper,
                   nativeCalloutEvent, nativeCalloutHelper,
                   nativeSectionEvent, nativeSectionHelper);

                System.Diagnostics.Debug.WriteLine("✅ External events initialized");

                RegisterDockablePane(app);

                // Portfolio Manager Button
                PushButtonData buttonData = new PushButtonData(
                    "PortfolioManagerButton",
                    "Portfolio\nManager",
                    typeof(Ribbon).Assembly.Location,
                    typeof(PortfolioManagerCommand).FullName);

                buttonData.ToolTip = "Open the Portfolio Manager to view and place detail references";
                buttonData.LargeImage = LoadPushButtonImage("ViewReferenceSystem.Resources.DetailIcon32.png");


                PushButton managerButton = panel.AddItem(buttonData) as PushButton;

                // Portfolio Setup Button (hidden - only called programmatically from pane)
                PushButtonData setupButtonData = new PushButtonData(
                    "PortfolioSetupButton",
                    "Portfolio\nSetup",
                    typeof(Ribbon).Assembly.Location,
                    typeof(PortfolioSetupCommand).FullName);

                setupButtonData.ToolTip = "Configure portfolio settings";
                setupButtonData.LargeImage = LoadPushButtonImage("ViewReferenceSystem.Resources.DetailIcon32.png");

                PushButton setupButton = panel.AddItem(setupButtonData) as PushButton;
                // Make it invisible - we only trigger it programmatically

                // Tools & Reports Button (NEW - replaces Usage Report)
                PushButtonData toolsButtonData = new PushButtonData(
                    "ToolsReportsButton",
                    "Tools &\nReports",
                    typeof(Ribbon).Assembly.Location,
                    typeof(ToolsAndReportsCommand).FullName);

                toolsButtonData.ToolTip = "Family management and portfolio reports";
                toolsButtonData.LargeImage = LoadPushButtonImage("ViewReferenceSystem.Resources.DetailIcon32.png");

                PushButton toolsButton = panel.AddItem(toolsButtonData) as PushButton;

                // Help Button
                PushButtonData helpButtonData = new PushButtonData(
                    "HelpButton",
                    "Help Me",
                    typeof(Ribbon).Assembly.Location,
                    typeof(HelpCommand).FullName);

                helpButtonData.ToolTip = "Complete user guide for the Typical Details Plugin";
                helpButtonData.LargeImage = LoadPushButtonImage("ViewReferenceSystem.Resources.HelpIcon32.png");

                PushButton helpButton = panel.AddItem(helpButtonData) as PushButton;

                // Update Add-in Button (always available — downloads latest from Firebase)
                PushButtonData updateButtonData = new PushButtonData(
                    "InstallUpdateButton",
                    "Update\nAdd-in",
                    typeof(Ribbon).Assembly.Location,
                    typeof(ViewReferenceSystem.Commands.InstallUpdateCommand).FullName);
                updateButtonData.ToolTip = "Download and install the latest version of the plugin from Firebase";
                updateButtonData.LargeImage = LoadPushButtonImage("ViewReferenceSystem.Resources.DetailIcon32.png");
                _updateButton = panel.AddItem(updateButtonData) as PushButton;
                _updateButton.Enabled = true;

                // AI Family Generator Button
                PushButtonData aiFamilyButtonData = new PushButtonData(
                    "AiFamilyGeneratorButton",
                    "AI Family\nGenerator",
                    typeof(Ribbon).Assembly.Location,
                    typeof(ViewReferenceSystem.Commands.AiFamilyGeneratorCommand).FullName);
                aiFamilyButtonData.ToolTip = "Generate Revit families from a JSON spec using Claude Sonnet AI";
                aiFamilyButtonData.LargeImage = LoadPushButtonImage("ViewReferenceSystem.Resources.DetailIcon32.png");
                panel.AddItem(aiFamilyButtonData);

                // ── Drafting Tools Panel ──────────────────────────────────────────
                RibbonPanel draftingPanel = app.CreateRibbonPanel(tabName, "Drafting Tools");

                PushButtonData dimEditorButtonData = new PushButtonData(
                    "DimensionTextEditorButton",
                    "Dimension\nText Editor",
                    typeof(Ribbon).Assembly.Location,
                    typeof(ViewReferenceSystem.Commands.DimensionTextEditorCommand).FullName);
                dimEditorButtonData.ToolTip = "Bulk-edit prefix, suffix, above, below, and override text on selected dimensions";
                dimEditorButtonData.LargeImage = LoadPushButtonImage("ViewReferenceSystem.Resources.DetailIcon32.png");
                draftingPanel.AddItem(dimEditorButtonData);

                app.ControlledApplication.DocumentOpened += OnDocumentOpened;
                app.ControlledApplication.DocumentClosing += OnDocumentClosing;
                app.ControlledApplication.DocumentClosed += OnDocumentClosed;
                app.ViewActivated += OnViewActivated;
                app.ControlledApplication.DocumentSynchronizingWithCentral += OnDocumentSynchronizingWithCentral;
                app.ControlledApplication.DocumentSynchronizedWithCentral += OnDocumentSynchronizedWithCentral;
                app.ControlledApplication.DocumentChanged += OnDocumentChanged;

                _uiControlledApp = app;
                _uiControlledApp.Idling += OnIdling_ProjectOpenUpdate;
                _openUpdateIdlingSubscribed = true;

                // Check for updates in background — don't block Revit startup
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        System.Threading.Thread.Sleep(8000); // Wait for Revit to finish loading
                        string localVer = UpdaterClient.CurrentVersionString;
                        var latest = UpdaterClient.CheckForUpdate();
                        string firebaseVer = latest?.Version ?? "null";
                        bool updateAvailable = latest != null;

                        System.Diagnostics.Debug.WriteLine($"🔄 Update check: local={localVer}, firebase={firebaseVer}, available={updateAvailable}");

                        if (updateAvailable)
                        {
                            System.Diagnostics.Debug.WriteLine($"🆕 Update available: {latest.Version}");
                            _pendingUpdate = latest;
                        }
                    }
                    catch (Exception updateEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Update check failed: {updateEx.Message}");
                    }
                });


                System.Diagnostics.Debug.WriteLine("✅ View Reference System initialized successfully on cc17-dev panel");
                System.Diagnostics.Debug.WriteLine("✅ Sync-to-Central event handlers registered (pre and post)");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Fatal error during startup: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                TaskDialog.Show("Startup Error", $"Failed to initialize View Reference System:\n{ex.Message}");
                return Result.Failed;
            }
        }

        private System.Windows.Media.ImageSource LoadPushButtonImage(string resourceName)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream != null)
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    return bitmap;
                }

                System.Diagnostics.Debug.WriteLine($"⚠️ Could not load image resource: {resourceName}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading image: {ex.Message}");
                return null;
            }
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            _isShuttingDown = true;
            try
            {
                System.Diagnostics.Debug.WriteLine("🔄 Ribbon OnShutdown() called");

                app.ControlledApplication.DocumentOpened -= OnDocumentOpened;
                app.ControlledApplication.DocumentClosing -= OnDocumentClosing;
                app.ControlledApplication.DocumentClosed -= OnDocumentClosed;
                app.ViewActivated -= OnViewActivated;
                app.ControlledApplication.DocumentSynchronizingWithCentral -= OnDocumentSynchronizingWithCentral;
                app.ControlledApplication.DocumentSynchronizedWithCentral -= OnDocumentSynchronizedWithCentral;
                app.ControlledApplication.DocumentChanged -= OnDocumentChanged;

                if (_idlingSubscribed)
                {
                    app.Idling -= OnIdling_AutoPublish;
                    _idlingSubscribed = false;
                }

                if (_openUpdateIdlingSubscribed)
                {
                    app.Idling -= OnIdling_ProjectOpenUpdate;
                    _openUpdateIdlingSubscribed = false;
                }

                System.Diagnostics.Debug.WriteLine("✅ Shutdown completed successfully");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error during shutdown: {ex.Message}");
                return Result.Failed;
            }
        }

        private void RegisterDockablePane(UIControlledApplication app)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔧 Registering dockable pane...");

                var portfolioPaneProvider = new PortfolioManagePane();

                app.RegisterDockablePane(
                    PortfolioManagePane.GetDockablePaneId(),
                    "Portfolio Manager",
                    portfolioPaneProvider);

                System.Diagnostics.Debug.WriteLine("✅ Dockable pane registered successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error registering dockable pane: {ex.Message}");
                throw;
            }
        }
        #endregion

        #region Enhanced Document Event Handlers

        private void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            if (_isShuttingDown) return;
            try
            {
                Document doc = e.Document;
                if (doc == null || !doc.IsValidObject) return;
                System.Diagnostics.Debug.WriteLine($"📄 Document opened: {doc.Title}");

                // Check if this is a family document being opened for editing
                if (doc.IsFamilyDocument)
                {
                    string familyName = doc.Title;

                    // Strip ".rfa" suffix if present in title
                    if (familyName.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
                        familyName = familyName.Substring(0, familyName.Length - 4);

                    // Use the last known project document as the portfolio context
                    if (_lastProjectDocument != null && _lastProjectDocument.IsValidObject)
                    {
                        // STEP 1: Check for stale version before checkout
                        var staleness = FamilyMonitorManager.CheckFamilyStaleness(_lastProjectDocument, familyName);

                        if (staleness.IsOffline)
                        {
                            TaskDialog td = new TaskDialog("Family Edit — Offline Warning");
                            td.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
                            td.MainInstruction = $"Cannot verify if '{familyName}' is up to date";
                            td.MainContent = staleness.Message;
                            td.CommonButtons = TaskDialogCommonButtons.Ok;
                            td.Show();
                        }
                        else if (staleness.IsStale)
                        {
                            TaskDialog td = new TaskDialog("Family Edit — Stale Version Detected");
                            td.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
                            td.MainInstruction = $"'{familyName}' may be out of date!";
                            td.MainContent = staleness.Message;
                            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                                "Close and Sync First (Recommended)",
                                "Close this family editor and Sync to Central to get the latest version.");
                            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                                "Continue Anyway",
                                "Edit the current version. Your changes may overwrite a newer version when published.");

                            TaskDialogResult result = td.Show();

                            if (result == TaskDialogResult.CommandLink1)
                            {
                                try { doc.Close(false); }
                                catch (Exception closeEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"⚠️ Could not auto-close family doc: {closeEx.Message}");
                                }
                                return;
                            }
                        }

                        // STEP 2: Proceed with staleness check (checkout tracking not yet implemented)
                        bool checkoutOk = true;
                        string blockedBy = null;

                        if (!checkoutOk)
                        {
                            // Someone else has it — warn but let the user decide
                            TaskDialog td = new TaskDialog("Family Already Checked Out");
                            td.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
                            td.MainInstruction = $"'{familyName}' is currently being edited by another user";
                            td.MainContent =
                                $"Checked out by: {blockedBy}\n\n" +
                                $"Opening this family while another user is editing it could result in conflicting changes. " +
                                $"Whoever publishes last will overwrite the other's changes.\n\n" +
                                $"It is strongly recommended you contact {blockedBy.Split(' ')[0]} before proceeding.";
                            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                                "Open Anyway",
                                "I understand the risk. I will coordinate with the other user.");
                            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                                "Cancel",
                                "Close the family editor and coordinate first.");

                            TaskDialogResult tdResult = td.Show();

                            if (tdResult == TaskDialogResult.CommandLink2)
                            {
                                try { doc.Close(false); }
                                catch (Exception closeEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"⚠️ Could not auto-close family doc: {closeEx.Message}");
                                }
                                return;
                            }

                            // User chose to open anyway — log it but don't take over the checkout
                            System.Diagnostics.Debug.WriteLine($"   ⚠️ '{familyName}' opened despite checkout by {blockedBy} — user accepted risk");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"   🔒 '{familyName}' checked out by {Environment.UserName}");
                        }
                    }
                }
                else
                {
                    // This is a project document — track it for future family staleness checks
                    _lastProjectDocument = doc;

                    // Queue family and top note updates for when idle cycle is available
                    // (transactions aren't allowed in DocumentOpened, but are in Idling)
                    string portfolioPath = PortfolioSettings.GetJsonPath(doc);

                    // Queue family/top note updates if portfolio is on Firebase
                    bool portfolioAccessible = !string.IsNullOrEmpty(portfolioPath) &&
                        FirebaseClient.IsFirebasePath(portfolioPath);

                    if (portfolioAccessible)
                    {
                        _pendingOpenUpdateDoc = doc;
                        if (!_openUpdateIdlingSubscribed && _uiControlledApp != null)
                        {
                            _uiControlledApp.Idling += OnIdling_ProjectOpenUpdate;
                            _openUpdateIdlingSubscribed = true;
                            System.Diagnostics.Debug.WriteLine("   📋 Queued project-open update (families + top notes)");
                        }
                    }
                }

                _lastActiveDocument = doc;
                ViewReferenceSystem.UI.PortfolioManagePane.OnDocumentChanged(doc);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error handling document opened: {ex.Message}");
            }
        }

        private void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            try
            {
                Document doc = e.Document;
                if (doc == null) return;

                // Clear any pending references to this document to prevent stale access
                if (_pendingOpenUpdateDoc != null && _pendingOpenUpdateDoc.Equals(doc))
                    _pendingOpenUpdateDoc = null;
                if (_pendingAutoMigrateDoc != null && _pendingAutoMigrateDoc.Equals(doc))
                    _pendingAutoMigrateDoc = null;
                if (_lastProjectDocument != null && _lastProjectDocument.Equals(doc))
                    _lastProjectDocument = null;
                if (_lastActiveDocument != null && _lastActiveDocument.Equals(doc))
                    _lastActiveDocument = null;
                if (_pendingPostSyncRelinquishDoc != null && _pendingPostSyncRelinquishDoc.Equals(doc))
                    _pendingPostSyncRelinquishDoc = null;

                if (doc.IsFamilyDocument)
                {
                    // Family editor closing — release the checkout
                    string familyName = doc.Title;
                    if (familyName.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
                        familyName = familyName.Substring(0, familyName.Length - 4);

                    // Use last known project doc as the portfolio context
                    if (_lastProjectDocument != null && _lastProjectDocument.IsValidObject)
                    {
                        System.Diagnostics.Debug.WriteLine($"   🔓 Family doc closed: '{familyName}'");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"   🔒 Project doc closing: '{doc.Title}'");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in OnDocumentClosing: {ex.Message}");
            }
        }

        private void OnDocumentClosed(object sender, DocumentClosedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"📄 Document closed");
                _lastActiveDocument = null;
                ViewReferenceSystem.UI.PortfolioManagePane.OnDocumentChanged(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error handling document closed: {ex.Message}");
            }
        }

        private void OnViewActivated(object sender, ViewActivatedEventArgs e)
        {
            if (_isShuttingDown) return;
            try
            {
                Document currentDocument = e.Document;
                if (currentDocument == null || !currentDocument.IsValidObject) return;
                View currentView = e.CurrentActiveView;

                if (sender is UIApplication uiApp)
                {
                    ViewReferenceSystem.UI.PortfolioManagePane.SetUIApplication(uiApp);
                }

                if (currentDocument != _lastActiveDocument)
                {
                    System.Diagnostics.Debug.WriteLine($"📄 Document switched to: {currentDocument?.Title ?? "null"}");
                    _lastActiveDocument = currentDocument;

                    // Track non-family docs for staleness checks
                    if (currentDocument != null && !currentDocument.IsFamilyDocument)
                    {
                        _lastProjectDocument = currentDocument;
                    }

                    ViewReferenceSystem.UI.PortfolioManagePane.OnDocumentChanged(currentDocument);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"👁️ View activated: {currentView?.Name ?? "null"} (Type: {currentView?.ViewType.ToString() ?? "null"})");
                    ViewReferenceSystem.UI.PortfolioManagePane.OnViewChanged(currentView);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error handling view activated: {ex.Message}");
            }
        }

        private void OnDocumentSynchronizingWithCentral(object sender, DocumentSynchronizingWithCentralEventArgs e)
        {
            if (_isShuttingDown) return;
            var startTime = DateTime.Now;

            try
            {
                Document doc = e.Document;
                if (doc == null || !doc.IsValidObject) return;
                System.Diagnostics.Debug.WriteLine($"🔄 Sync to Central triggered for: {doc.Title}");

                if (ViewReferenceSystem.UI.PortfolioManagePane.IsProjectOffline(doc))
                {
                    System.Diagnostics.Debug.WriteLine("   ⏭️ Project is offline - skipping portfolio update");
                    return;
                }

                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath))
                {
                    System.Diagnostics.Debug.WriteLine("   ⏭️ Not a portfolio project - skipping portfolio update");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"   📂 Portfolio path: {portfolioPath}");
                _syncLogPath = null;
                SyncLog("═══════════════════════════════════════════", portfolioPath);
                SyncLog($"PRE-SYNC: doc.Title = '{doc.Title}'", portfolioPath);

                PortfolioSettings.Portfolio portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);

                if (portfolioData == null)
                {
                    HandleBreadcrumbState(doc, portfolioPath);
                    System.Diagnostics.Debug.WriteLine("   ⚠️ Portfolio data is null - skipping");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("   ✅ Portfolio loaded, updating directly...");

                // NOTE: Family updates moved to OnDocumentSynchronizedWithCentral (post-sync)
                // because transactions are not allowed during the pre-sync event

                UpdatePortfolioDirect(doc, portfolioData, portfolioPath);

                var elapsed = DateTime.Now - startTime;
                System.Diagnostics.Debug.WriteLine($"   ✅ Portfolio updated in {elapsed.TotalMilliseconds:F0}ms");

                try
                {
                    System.Diagnostics.Debug.WriteLine("   🔄 Refreshing Portfolio Manager pane...");
                    ViewReferenceSystem.UI.PortfolioManagePane.OnDocumentChanged(doc);
                    System.Diagnostics.Debug.WriteLine("   ✅ Pane refresh triggered");
                }
                catch (Exception paneEx)
                {
                    System.Diagnostics.Debug.WriteLine($"   ⚠️ Could not refresh pane: {paneEx.Message}");
                }

                System.Diagnostics.Debug.WriteLine("   🔄 Proceeding with Revit's sync-to-central...");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error updating portfolio during sync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   Stack trace: {ex.StackTrace}");
            }
        }

        private void OnDocumentSynchronizedWithCentral(object sender, DocumentSynchronizedWithCentralEventArgs e)
        {
            if (_isShuttingDown) return;
            try
            {
                Document doc = e.Document;
                if (doc == null || !doc.IsValidObject) return;

                System.Diagnostics.Debug.WriteLine($"✅ Sync completed for: {doc.Title}");

                // Invalidate monitored-family name cache so next document change re-reads fresh data
                _cachedMonitoredNames = null;
                _cachedMonitoredNamesPortfolioPath = null;

                // Check if this is a portfolio project
                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath))
                {
                    System.Diagnostics.Debug.WriteLine("   ⏭️ Not a portfolio project - skipping post-sync work");
                    return;
                }

                bool isFirebase = FirebaseClient.IsFirebasePath(portfolioPath);

                // For local paths, verify file exists
                if (!isFirebase && !File.Exists(portfolioPath))
                {
                    System.Diagnostics.Debug.WriteLine("   ⏭️ Portfolio file not found - skipping post-sync work");
                    return;
                }

                // ── Auto-migrate check: if local path but portfolio has been migrated to Firebase ──
                if (!isFirebase)
                {
                    CheckAndAutoMigrate(doc, portfolioPath);
                    // Re-read in case it changed
                    portfolioPath = PortfolioSettings.GetJsonPath(doc);
                    isFirebase = FirebaseClient.IsFirebasePath(portfolioPath);
                }

                SyncLog("POST-SYNC: Starting post-sync processing...", portfolioPath);

                // STEP 1: Update sync timestamp in Revit project parameters
                try
                {
                    using (Transaction trans = new Transaction(doc, "Update Portfolio Sync Time"))
                    {
                        trans.Start();
                        PortfolioSettings.SetLastSyncTimestamp(doc, DateTime.Now);
                        trans.Commit();
                    }
                    System.Diagnostics.Debug.WriteLine("   ✅ Sync timestamp updated");
                }
                catch (Exception tsEx)
                {
                    System.Diagnostics.Debug.WriteLine($"   ⚠️ Could not update sync timestamp: {tsEx.Message}");
                }

                // STEP 2: Load any pending family updates
                SyncLog("POST-SYNC: Checking family updates...", portfolioPath);
                FamilyMonitorManager.UpdateFamiliesIfNeeded(doc);

                // STEP 3: Push top notes from JSON to Revit shared parameters
                PushTopNotesToRevit(doc, portfolioPath);

                // STEP 4: Queue relinquish for next Idling cycle.
                // RelinquishOwnership CANNOT run inside DocumentSynchronizedWithCentral —
                // Revit is still finishing the sync pipeline and throws "internal error" if we
                // try to talk to central again here. Deferring to Idling lets Revit fully
                // complete the sync first, then we relinquish cleanly.
                if (doc.IsWorkshared)
                {
                    _pendingPostSyncRelinquishDoc = doc;
                    SyncLog("POST-SYNC: Queued relinquish for next Idling cycle", portfolioPath);
                    System.Diagnostics.Debug.WriteLine("   📋 Queued relinquish for next Idling cycle");
                }

                // STEP 5: Refresh the portfolio pane
                PortfolioManagePane.RefreshCurrentPane();

                SyncLog("POST-SYNC: Complete (relinquish pending)", portfolioPath);
                System.Diagnostics.Debug.WriteLine("   ✅ Post-sync processing complete (relinquish pending)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in post-sync handler: {ex.Message}");
            }
        }

        /// <summary>
        /// If the portfolio has been migrated to Firebase by another project,
        /// auto-update this project's ViewReference_JsonPath and show a one-time notification.
        /// </summary>
        private void CheckAndAutoMigrate(Document doc, string localPortfolioPath)
        {
            try
            {
                if (!File.Exists(localPortfolioPath)) return;

                var portfolioData = PortfolioSettings.LoadPortfolioFromFile(localPortfolioPath);
                if (portfolioData == null) return;

                // If no Firebase path stored in portfolio, no migration has happened
                string firebasePath = portfolioData.FirebasePath;
                if (string.IsNullOrEmpty(firebasePath) || !FirebaseClient.IsFirebasePath(firebasePath)) return;

                string currentProjectName = PortfolioSettings.GetProjectName(doc);
                string currentProjectGuid = PortfolioSettings.GetProjectGuid(doc);
                string matchMethod;
                var currentProject = PortfolioSettings.FindProjectInPortfolio(
                    portfolioData, currentProjectName, currentProjectGuid, out matchMethod);

                // If already migrated, nothing to do
                if (currentProject == null || currentProject.IsMigrated) return;

                // Update this project's JsonPath to Firebase
                using (Transaction trans = new Transaction(doc, "Auto-Migrate to Firebase"))
                {
                    trans.Start();
                    PortfolioSettings.SetJsonPath(doc, firebasePath);
                    trans.Commit();
                }

                // Mark this project as migrated in Firebase
                try
                {
                    var freshData = PortfolioSettings.LoadPortfolioFromFile(firebasePath);
                    if (freshData != null)
                    {
                        string _m;
                        var p = PortfolioSettings.FindProjectInPortfolio(freshData, currentProjectName, currentProjectGuid, out _m);
                        if (p != null)
                        {
                            p.IsMigrated = true;
                            PortfolioSettings.SavePortfolioToFile(freshData, firebasePath);
                        }
                    }
                }
                catch (Exception migrateEx)
                {
                    System.Diagnostics.Debug.WriteLine($"   ⚠️ Could not mark IsMigrated in Firebase: {migrateEx.Message}");
                }

                // Show one-time notification
                TaskDialog.Show("Portfolio Migrated to Firebase",
                    $"This portfolio has been migrated to Firebase by another team member.\n\n" +
                    $"Firebase path: {firebasePath}\n\n" +
                    $"Your project has been automatically updated — no action needed.");

                System.Diagnostics.Debug.WriteLine($"✅ Auto-migrated to Firebase: {firebasePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Auto-migrate check failed: {ex.Message}");
            }
        }

        #endregion

        #region Auto-Publish on Family Load

        /// <summary>
        /// Fires on any document change. Detects when a monitored family is loaded/reloaded
        /// and queues it for auto-publish on the next idle cycle.
        /// </summary>
        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (_isShuttingDown) return;
            try
            {
                Document doc = e.GetDocument();
                if (doc == null || !doc.IsValidObject) return;

                // Don't auto-publish if we're currently pulling family updates from the network
                // (UpdateFamiliesIfNeeded loads families → triggers DocumentChanged → would re-publish)
                if (FamilyMonitorManager.IsSyncingFamilies) return;

                // Quick check: is this a portfolio project with monitored families?
                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath))
                    return;

                // For local paths, verify file exists
                if (!FirebaseClient.IsFirebasePath(portfolioPath) && !File.Exists(portfolioPath))
                    return;

                // PERFORMANCE: Check for Family elements BEFORE reading any JSON.
                // OnDocumentChanged fires on every keystroke/parameter edit.
                // We only care about changes that involve a Family element being loaded or modified.
                // If there are no Family elements in this change set, skip entirely — no file I/O needed.
                var changedIds = e.GetAddedElementIds()
                    .Concat(e.GetModifiedElementIds());

                var changedFamilies = new List<Family>();
                foreach (ElementId id in changedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem is Family family)
                        changedFamilies.Add(family);
                }

                if (changedFamilies.Count == 0)
                    return; // No families changed — skip JSON read entirely

                // A family changed — now check if any are monitored
                // Use cached names if available; otherwise read JSON (this is now rare)
                HashSet<string> monitoredNames;
                if (_cachedMonitoredNames != null &&
                    string.Equals(_cachedMonitoredNamesPortfolioPath, portfolioPath, StringComparison.OrdinalIgnoreCase))
                {
                    monitoredNames = _cachedMonitoredNames;
                }
                else
                {
                    var portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);
                    if (portfolioData?.MonitoredFamilies == null || portfolioData.MonitoredFamilies.Count == 0)
                        return;

                    monitoredNames = new HashSet<string>(
                        portfolioData.MonitoredFamilies.Select(m => m.FamilyName),
                        StringComparer.OrdinalIgnoreCase);

                    _cachedMonitoredNames = monitoredNames;
                    _cachedMonitoredNamesPortfolioPath = portfolioPath;
                }

                // Check the already-collected family elements against monitored names
                foreach (Family family in changedFamilies)
                {
                    if (monitoredNames.Contains(family.Name))
                    {
                        lock (_queueLock)
                        {
                            // Avoid duplicate entries in the queue
                            if (!_autoPublishQueue.Contains(family.Name))
                            {
                                _autoPublishQueue.Enqueue(family.Name);
                                System.Diagnostics.Debug.WriteLine(
                                    $"📋 Auto-publish queued: '{family.Name}' (detected family load/reload)");
                            }
                        }

                        // Subscribe to Idling if not already
                        if (!_idlingSubscribed && _uiControlledApp != null)
                        {
                            _uiControlledApp.Idling += OnIdling_AutoPublish;
                            _idlingSubscribed = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error in OnDocumentChanged (auto-publish): {ex.Message}");
            }
        }

        /// <summary>
        /// Fires on idle after a monitored family was loaded. Processes the auto-publish queue.
        /// Runs outside any transaction context, so PublishFamily can use EditFamily/SaveAs freely.
        /// </summary>
        private void OnIdling_AutoPublish(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            if (_isShuttingDown) return;
            try
            {
                string familyToPublish = null;

                lock (_queueLock)
                {
                    if (_autoPublishQueue.Count == 0)
                    {
                        // Nothing left — unsubscribe to stop Idling overhead
                        if (_uiControlledApp != null)
                        {
                            _uiControlledApp.Idling -= OnIdling_AutoPublish;
                            _idlingSubscribed = false;
                        }
                        return;
                    }

                    familyToPublish = _autoPublishQueue.Dequeue();
                }

                if (string.IsNullOrEmpty(familyToPublish)) return;

                // Get the active document
                UIApplication uiApp = sender as UIApplication;
                Document doc = uiApp?.ActiveUIDocument?.Document;
                if (doc == null) return;

                System.Diagnostics.Debug.WriteLine($"🔄 Auto-publishing family: '{familyToPublish}'");

                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath)) return;

                // Call the existing PublishFamily method
                string errorMessage;
                bool success = FamilyMonitorManager.PublishFamily(doc, familyToPublish, out errorMessage);

                if (success)
                {
                    SyncLog($"  AUTO-PUBLISH: '{familyToPublish}' published successfully", portfolioPath);

                    // Brief non-blocking notification via status bar
                    if (uiApp != null)
                    {
                        try
                        {
                            uiApp.ActiveUIDocument?.RefreshActiveView();
                        }
                        catch { }
                    }

                    TaskDialog.Show("Family Auto-Published",
                        $"'{familyToPublish}' was automatically published to the portfolio " +
                        $"because it was loaded/reloaded and is a monitored family.\n\n" +
                        $"Other projects will receive the update on their next Sync to Central.");
                }
                else
                {
                    SyncLog($"  AUTO-PUBLISH BLOCKED: '{familyToPublish}' — {errorMessage}", portfolioPath);
                    System.Diagnostics.Debug.WriteLine($"⚠️ Auto-publish blocked for '{familyToPublish}': {errorMessage}");

                    // Show the block reason to the user so they know what happened
                    TaskDialog td = new TaskDialog("Family Auto-Publish Blocked");
                    td.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
                    td.MainInstruction = $"Could not auto-publish '{familyToPublish}'";
                    td.MainContent = errorMessage;
                    td.CommonButtons = TaskDialogCommonButtons.Ok;
                    td.Show();
                }

                // If more items in queue, keep Idling subscribed for next cycle
                lock (_queueLock)
                {
                    if (_autoPublishQueue.Count == 0 && _uiControlledApp != null)
                    {
                        _uiControlledApp.Idling -= OnIdling_AutoPublish;
                        _idlingSubscribed = false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in OnIdling_AutoPublish: {ex.Message}");

                // Unsubscribe on error to prevent infinite error loops
                lock (_queueLock)
                {
                    _autoPublishQueue.Clear();
                    if (_uiControlledApp != null)
                    {
                        try { _uiControlledApp.Idling -= OnIdling_AutoPublish; } catch { }
                        _idlingSubscribed = false;
                    }
                }
            }
        }

        #endregion

        #region Project-Open Updates

        /// <summary>
        /// Fires on idle after a portfolio project is opened.
        /// Loads any pending family updates and pushes JSON top notes to Revit parameters.
        /// This ensures you get updates immediately on open, not just on sync.
        /// </summary>
        private void OnIdling_ProjectOpenUpdate(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            if (_isShuttingDown) return;
            try
            {
                // ── Handle deferred post-sync relinquish ──
                // RelinquishOwnership cannot run inside DocumentSynchronizedWithCentral,
                // so we queue it here where Revit has fully completed the sync pipeline.
                Document relinquishDoc = _pendingPostSyncRelinquishDoc;
                _pendingPostSyncRelinquishDoc = null;
                if (relinquishDoc != null && relinquishDoc.IsValidObject && relinquishDoc.IsWorkshared)
                {
                    try
                    {
                        var relinquishOptions = new RelinquishOptions(true);
                        relinquishOptions.StandardWorksets = true;
                        relinquishOptions.FamilyWorksets = true;
                        relinquishOptions.ViewWorksets = true;
                        relinquishOptions.UserWorksets = true;
                        WorksharingUtils.RelinquishOwnership(relinquishDoc, relinquishOptions, new TransactWithCentralOptions());
                        System.Diagnostics.Debug.WriteLine("   🔓 Deferred relinquish completed (Idling)");

                        // Verify: check if Project Information is still owned by us
                        try
                        {
                            var projInfo = relinquishDoc.ProjectInformation;
                            if (projInfo != null)
                            {
                                var status = WorksharingUtils.GetCheckoutStatus(relinquishDoc, projInfo.Id);
                                if (status == CheckoutStatus.OwnedByCurrentUser)
                                {
                                    SyncLog("POST-SYNC: ⚠️ Project Information STILL owned after relinquish!");
                                    TaskDialog.Show("VRS — Element Still Checked Out",
                                        "RelinquishOwnership ran, but Project Information is still owned by you.\n\n" +
                                        "This can happen if there are unsaved local changes.\n" +
                                        "Try using Collaborate → Relinquish All Mine manually.");
                                }
                                else
                                {
                                    SyncLog("POST-SYNC: ✅ Project Information released successfully");
                                }
                            }
                        }
                        catch { } // Verification is best-effort
                    }
                    catch (Exception relEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"   ⚠️ Deferred relinquish failed: {relEx.Message}");
                        SyncLog($"POST-SYNC: ❌ RELINQUISH FAILED: {relEx.Message}");

                        TaskDialog.Show("VRS — Relinquish Failed",
                            $"Could not release element ownership after sync:\n\n{relEx.Message}\n\n" +
                            "Try using Collaborate → Relinquish All Mine manually.");
                    }
                }

                // Check if update notification is pending (set by background thread)
                if (_pendingUpdate != null && _updateButton != null)
                {
                    var latest = _pendingUpdate;
                    _pendingUpdate = null;

                    _updateButton.ItemText = "⬆ Update\nAvailable";
                    _updateButton.ToolTip =
                        $"Version {latest.Version} is available!\n\n" +
                        $"Current: {UpdaterClient.CurrentVersionString}\n" +
                        $"New: {latest.Version}\n\n" +
                        $"{latest.ReleaseNotes}\n\nClick to download and install.";

                    // Show startup notification with Update Now option
                    var td = new TaskDialog("Update Available");
                    td.MainIcon = TaskDialogIcon.TaskDialogIconInformation;
                    td.MainInstruction = $"View Reference System {latest.Version} is available!";
                    td.MainContent =
                        $"Current version: {UpdaterClient.CurrentVersionString}\n" +
                        $"New version: {latest.Version}\n\n" +
                        (string.IsNullOrEmpty(latest.ReleaseNotes) ? "" : $"{latest.ReleaseNotes}\n\n") +
                        "You can update now or use the ribbon button later.";
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "Update Now",
                        "Downloads and installs automatically when you close Revit.");
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Later",
                        "Use the ribbon button to update when you're ready.");
                    td.DefaultButton = TaskDialogResult.CommandLink1;

                    if (td.Show() == TaskDialogResult.CommandLink1)
                    {
                        // Run the same download flow as InstallUpdateCommand
                        var progressWindow = new UpdateProgressWindow(latest.Version);
                        progressWindow.Show();

                        string capturedVersion = latest.Version;
                        var thread = new System.Threading.Thread(() =>
                        {
                            UpdaterClient.DownloadResult result = null;
                            Exception error = null;

                            try
                            {
                                result = UpdaterClient.DownloadUpdate(capturedVersion, msg =>
                                {
                                    if (!progressWindow.CancellationSource.IsCancellationRequested)
                                        progressWindow.Dispatcher.Invoke(() => progressWindow.SetStatus(msg));
                                });
                            }
                            catch (Exception ex) { error = ex; }

                            progressWindow.Dispatcher.Invoke(() =>
                            {
                                if (!progressWindow.IsVisible) return;
                                progressWindow.Close();

                                if (progressWindow.WasCancelled) return;

                                if (error != null || result == null || !result.Success)
                                {
                                    string err = error?.Message ?? result?.ErrorMessage ?? "Unknown error";
                                    TaskDialog.Show("Update",
                                        $"Could not download the update:\n\n{err}\n\nYou can install manually.");
                                    return;
                                }

                                bool launched = UpdaterClient.LaunchWaiterScript(result.TempFolder);

                                if (!launched)
                                {
                                    TaskDialog.Show("Update Downloaded",
                                        $"Files downloaded to:\n{result.TempFolder}\n\n" +
                                        "Could not launch auto-installer. Close Revit and run install_update.ps1 manually.");
                                    return;
                                }

                                var finalTd = new TaskDialog("Ready to Install");
                                finalTd.MainIcon = TaskDialogIcon.TaskDialogIconInformation;
                                finalTd.MainInstruction = "Update ready — close Revit to install";
                                finalTd.MainContent =
                                    $"Version {capturedVersion} has been downloaded.\n\n" +
                                    "A small installer window is running in the background. " +
                                    "When you close Revit, it will automatically install the update.\n\n" +
                                    "You can save your work and close Revit at any time.";
                                finalTd.CommonButtons = TaskDialogCommonButtons.Ok;
                                finalTd.Show();
                            });
                        });
                        thread.IsBackground = true;
                        thread.Start();
                    }

                    System.Diagnostics.Debug.WriteLine($"✅ Update notification shown — newer version available");

                    // Re-sync pane with active document in case the dialog blocked the initial notification
                    if (_lastProjectDocument != null && _lastProjectDocument.IsValidObject)
                        PortfolioManagePane.OnDocumentChanged(_lastProjectDocument);
                }

                Document doc = _pendingOpenUpdateDoc;
                _pendingOpenUpdateDoc = null;

                if (doc == null || !doc.IsValidObject) return;

                string portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(portfolioPath)) return;
                if (!FirebaseClient.IsFirebasePath(portfolioPath))
                {
                    System.Diagnostics.Debug.WriteLine("   ⏭️ Non-Firebase path — skipping project-open update");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"📂 Project-open update for: {doc.Title}");
                SyncLog("═══════════════════════════════════════════", portfolioPath);
                SyncLog($"PROJECT-OPEN: Checking for updates... ({doc.Title})", portfolioPath);

                // 1. Load any pending family updates
                FamilyMonitorManager.UpdateFamiliesIfNeeded(doc);

                // 2. Push top notes from JSON to Revit parameters
                PushTopNotesToRevit(doc, portfolioPath);

                // 3. Relinquish any elements we checked out
                try
                {
                    if (doc.IsWorkshared)
                    {
                        var relinquishOptions = new RelinquishOptions(true);
                        relinquishOptions.StandardWorksets = true;
                        relinquishOptions.FamilyWorksets = true;
                        relinquishOptions.ViewWorksets = true;
                        relinquishOptions.UserWorksets = true;
                        var transactOptions = new TransactWithCentralOptions();
                        WorksharingUtils.RelinquishOwnership(doc, relinquishOptions, transactOptions);
                        System.Diagnostics.Debug.WriteLine("   🔓 Relinquished element ownership (project-open)");
                    }
                }
                catch (Exception relEx)
                {
                    System.Diagnostics.Debug.WriteLine($"   ⚠️ Could not relinquish ownership: {relEx.Message}");
                }

                // 4. Refresh the pane
                PortfolioManagePane.RefreshCurrentPane();

                SyncLog("PROJECT-OPEN: Update complete", portfolioPath);
                System.Diagnostics.Debug.WriteLine("   ✅ Project-open update complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in OnIdling_ProjectOpenUpdate: {ex.Message}");

                // Clean up
                if (_uiControlledApp != null)
                {
                    try { _uiControlledApp.Idling -= OnIdling_ProjectOpenUpdate; } catch { }
                    _openUpdateIdlingSubscribed = false;
                }
            }
        }

        #endregion

        #region Breadcrumb Handling

        /// <summary>
        /// Called after a null portfolio load.
        /// Checks FirebaseClient pending flags set by LoadPortfolioFromFile and acts on them:
        ///   - PendingPathUpdate: silently updates stored path and shows one-time toast
        ///   - PendingArchivedNotice: shows a one-time TaskDialog and disables sync
        /// </summary>
        private static void HandleBreadcrumbState(Document doc, string currentPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(FirebaseClient.PendingPathUpdate))
                {
                    string newPath = FirebaseClient.PendingPathUpdate;
                    FirebaseClient.PendingPathUpdate = null;

                    System.Diagnostics.Debug.WriteLine($"🔀 Auto-healing path: {currentPath} → {newPath}");

                    try
                    {
                        using (Transaction trans = new Transaction(doc, "Update Portfolio Path"))
                        {
                            trans.Start();
                            PortfolioSettings.SetJsonPath(doc, newPath);
                            trans.Commit();
                        }

                        TaskDialog td = new TaskDialog("VRS — Portfolio Path Updated");
                        td.MainIcon = TaskDialogIcon.TaskDialogIconInformation;
                        td.MainInstruction = "Portfolio path updated automatically";
                        td.MainContent = $"This portfolio was moved to a new location.\n\nNew path: {newPath}\n\nThe path has been updated in this project. No action required.";
                        td.CommonButtons = TaskDialogCommonButtons.Ok;
                        td.Show();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Could not auto-update path: {ex.Message}");
                    }
                }
                else if (FirebaseClient.PendingArchivedNotice)
                {
                    FirebaseClient.PendingArchivedNotice = false;

                    System.Diagnostics.Debug.WriteLine("📦 Showing archived portfolio notice");

                    TaskDialog td = new TaskDialog("VRS — Portfolio Archived");
                    td.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
                    td.MainInstruction = "This portfolio has been archived";
                    td.MainContent = "Detail references are read-only. Portfolio sync is paused until the portfolio is restored.\n\nContact: scott.thoreson@imegcorp.com";
                    td.CommonButtons = TaskDialogCommonButtons.Ok;
                    td.Show();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ HandleBreadcrumbState error: {ex.Message}");
            }
        }

        #endregion

        #region Direct Portfolio Update

        private void UpdatePortfolioDirect(Document doc, PortfolioSettings.Portfolio portfolioData, string portfolioPath)
        {
            try
            {
                string currentProjectName = PortfolioSettings.GetProjectName(doc);
                string currentProjectGuid = PortfolioSettings.GetProjectGuid(doc);
                SyncLog($"  GetProjectName = '{currentProjectName}'");
                SyncLog($"  GetProjectGuid = '{currentProjectGuid}'");
                System.Diagnostics.Debug.WriteLine($"   📋 Current project: {currentProjectName} (GUID: {currentProjectGuid})");

                // ========== MATCH PROJECT FIRST (before view processing) ==========
                // This handles renamed files (e.g., "GMP" → "GMP 3") via fuzzy matching
                string matchMethod;
                var currentProject = PortfolioSettings.FindProjectInPortfolio(
                    portfolioData, currentProjectName, currentProjectGuid, out matchMethod);

                if (currentProject != null)
                {
                    SyncLog($"  MATCHED by {matchMethod}: stored='{currentProject.ProjectName}' nickname='{currentProject.Nickname}'");
                }
                else
                {
                    SyncLog($"  ❌ NO MATCH FOUND!");
                    SyncLog($"  Projects in portfolio JSON:");
                    foreach (var p in portfolioData.ProjectInfos ?? new List<PortfolioSettings.PortfolioProject>())
                    {
                        SyncLog($"    - Name='{p.ProjectName}', Nickname='{p.Nickname}', GUID='{p.ProjectGuid}'");
                    }
                }

                // Determine the name views are stored under in the JSON
                // After fuzzy match, ProjectName has been updated to current name,
                // but views in JSON still use the OLD name. We need to find views by ANY name
                // that could belong to this project.
                // Use GUID-matched project's original name, or current name for new projects.
                string viewSourceName = currentProjectName;

                // Find views belonging to this project — check both current name and any name
                // associated with this GUID (handles renames)
                var existingProjectViews = new List<ViewInfo>();
                if (portfolioData.Views != null)
                {
                    // Collect all possible names this project might have views under
                    var possibleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentProjectName };
                    if (currentProject != null && !string.IsNullOrEmpty(currentProject.ProjectName))
                    {
                        possibleNames.Add(currentProject.ProjectName);
                    }

                    // Also check for fuzzy matches on view source names
                    string currentBase = null;
                    foreach (var v in portfolioData.Views)
                    {
                        if (possibleNames.Contains(v.SourceProjectName))
                        {
                            existingProjectViews.Add(v);
                        }
                        else
                        {
                            // Lazy-init the base name for fuzzy view matching
                            if (currentBase == null)
                            {
                                // Use same stripping logic — strip version suffixes
                                currentBase = PortfolioSettings.StripVersionSuffix(currentProjectName);
                            }
                            string viewBase = PortfolioSettings.StripVersionSuffix(v.SourceProjectName);
                            if (!string.IsNullOrEmpty(currentBase) &&
                                string.Equals(currentBase, viewBase, StringComparison.OrdinalIgnoreCase))
                            {
                                existingProjectViews.Add(v);
                            }
                        }
                    }
                }
                // ==================================================================

                var viewsOnSheets = GetSheetPlacedViews(doc, currentProjectGuid);
                System.Diagnostics.Debug.WriteLine($"   📊 Found {viewsOnSheets.Count} views on sheets");

                // ========== PRESERVE EXISTING DATA (TopNote, ReferencedBy) ==========
                // Build lookup of existing data to preserve - ALWAYS keep JSON values
                // NOTE: Use a loop instead of ToDictionary to safely handle duplicate ViewIds in JSON
                var preservedData = new Dictionary<int, (string TopNote, List<ViewReferenceSystem.Models.ProjectReference> ReferencedBy)>();
                int duplicatesSkipped = 0;
                foreach (var v in existingProjectViews)
                {
                    if (!preservedData.ContainsKey(v.ViewId))
                        preservedData[v.ViewId] = (v.TopNote, v.ReferencedBy);
                    else
                        duplicatesSkipped++;
                }
                if (duplicatesSkipped > 0)
                    SyncLog($"  WARNING: Skipped {duplicatesSkipped} duplicate ViewIds in existing JSON data");
                System.Diagnostics.Debug.WriteLine($"   💾 Preserving data for {preservedData.Count} existing views ({duplicatesSkipped} duplicates skipped)");
                SyncLog($"  Found {existingProjectViews.Count} existing views, {viewsOnSheets.Count} current views on sheets");
                // ==================================================================================

                System.Diagnostics.Debug.WriteLine($"   🗑️ Removing {existingProjectViews.Count} existing views from this project");

                foreach (var view in existingProjectViews)
                {
                    portfolioData.Views.Remove(view);
                }

                System.Diagnostics.Debug.WriteLine($"   ➕ Adding {viewsOnSheets.Count} current views");

                int topNotesPreserved = 0;
                int referencesPreserved = 0;

                foreach (var viewInfo in viewsOnSheets)
                {
                    // ========== ALWAYS PRESERVE EXISTING JSON DATA ==========
                    if (preservedData.TryGetValue(viewInfo.ViewId, out var preserved))
                    {
                        // ALWAYS keep the JSON TopNote if it exists (don't let Revit overwrite it)
                        if (!string.IsNullOrEmpty(preserved.TopNote))
                        {
                            viewInfo.TopNote = preserved.TopNote;
                            topNotesPreserved++;
                        }

                        // Always preserve ReferencedBy list
                        if (preserved.ReferencedBy != null && preserved.ReferencedBy.Any())
                        {
                            viewInfo.ReferencedBy = preserved.ReferencedBy;
                            referencesPreserved++;
                        }
                    }
                    // ========================================================

                    portfolioData.Views.Add(viewInfo);
                }

                System.Diagnostics.Debug.WriteLine($"   📝 Preserved {topNotesPreserved} TopNotes, {referencesPreserved} ReferencedBy lists");

                // ========== Update reference tracking (UsesViewIds) ==========
                UpdateReferenceTracking(doc, portfolioData, currentProjectName);
                // ==============================================================

                // NOTE: ValidateAllDetailReferences is intentionally NOT called here.
                // This runs during pre-sync (OnDocumentSynchronizingWithCentral) where transactions
                // are illegal in Revit 2025. Validation happens in SaveConfigurationHandler (manual sync button).

                if (currentProject != null)
                {
                    currentProject.LastSync = DateTime.Now;
                    SyncLog($"  Updated LastSync for '{currentProject.ProjectName}'");
                    System.Diagnostics.Debug.WriteLine($"   🕐 Updated last sync time for {currentProjectName}");
                }

                portfolioData.LastUpdated = DateTime.Now;

                PortfolioSettings.SavePortfolioToFile(portfolioData, portfolioPath);

                System.Diagnostics.Debug.WriteLine($"   💾 Portfolio file saved: {portfolioPath}");
                // NOTE: "Update Portfolio Sync Time" transaction and RelinquishOwnership removed from here.
                // Transactions during pre-sync crash Revit 2025. Sync timestamp and relinquish
                // are handled in OnDocumentSynchronizedWithCentral (post-sync) where transactions are safe.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in UpdatePortfolioDirect: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   Stack: {ex.StackTrace}");
                SyncLog($"  ❌ ERROR in UpdatePortfolioDirect: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Update reference tracking - stores list of ViewIds this project uses in UsesViewIds
        /// </summary>
        private void UpdateReferenceTracking(Document doc, PortfolioSettings.Portfolio portfolioData, string currentProjectName)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("   🔗 Updating reference tracking (UsesViewIds)...");

                // Get all ViewIds that this project references via DetailReferenceFamily types
                var usedViewIds = GetUsedViewIds(doc);
                System.Diagnostics.Debug.WriteLine($"   📊 This project uses {usedViewIds.Count} detail views");

                // Find the current project in portfolio and update its UsesViewIds
                var currentProject = portfolioData.ProjectInfos?.FirstOrDefault(p =>
                    string.Equals(p.ProjectName, currentProjectName, StringComparison.OrdinalIgnoreCase));

                if (currentProject != null)
                {
                    currentProject.UsesViewIds = usedViewIds;
                    System.Diagnostics.Debug.WriteLine($"   ✅ Updated UsesViewIds for {currentProjectName}: [{string.Join(", ", usedViewIds)}]");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"   ⚠️ Project '{currentProjectName}' not found in portfolio ProjectInfos");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"   ⚠️ Error updating reference tracking: {ex.Message}");
                // Don't throw - reference tracking is not critical
            }
        }

        /// <summary>
        /// Get all ViewIds that are referenced by DetailReferenceFamily types in this document
        /// Returns a list of ViewId integers
        /// </summary>
        private List<int> GetUsedViewIds(Document doc)
        {
            var viewIds = new HashSet<int>();
            List<FamilySymbol> familySymbols = null;
            Dictionary<ElementId, int> instanceCountBySymbol = null;

            try
            {
                // Step 1: Collect all DetailReferenceFamily types ONCE
                using (var symbolCollector = new FilteredElementCollector(doc))
                {
                    familySymbols = symbolCollector
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>()
                        .Where(fs => IsDetailReferenceFamilySymbol(fs))
                        .ToList();
                }

                System.Diagnostics.Debug.WriteLine($"      🔍 Found {familySymbols.Count} DetailReferenceFamily types");

                // Step 2: Collect all FamilyInstances ONCE and count by Symbol.Id
                using (var instanceCollector = new FilteredElementCollector(doc))
                {
                    instanceCountBySymbol = instanceCollector
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .Where(fi => fi.Symbol != null)
                        .GroupBy(fi => fi.Symbol.Id)
                        .ToDictionary(g => g.Key, g => g.Count());
                }

                // Step 3: Process each symbol type
                foreach (var symbol in familySymbols)
                {
                    try
                    {
                        // Check if this type has any instances placed (using pre-collected data)
                        if (!instanceCountBySymbol.TryGetValue(symbol.Id, out int instanceCount) || instanceCount == 0)
                        {
                            // No instances - skip this type
                            continue;
                        }

                        // Skip orphaned references (marked with XXXX)
                        string sheetNumber = GetTypeParameterValue(symbol, "Sheet Number");
                        if (sheetNumber == "XXXX")
                        {
                            System.Diagnostics.Debug.WriteLine($"      ⏭️ Skipping orphaned type: {symbol.Name}");
                            continue;
                        }

                        // Try to get ViewId from type parameter first
                        string viewIdStr = GetTypeParameterValue(symbol, "View ID");
                        if (!string.IsNullOrEmpty(viewIdStr) && int.TryParse(viewIdStr, out int viewId))
                        {
                            viewIds.Add(viewId);
                            System.Diagnostics.Debug.WriteLine($"      ✅ Type '{symbol.Name}' -> ViewId {viewId} (from parameter)");
                            continue;
                        }

                        // Fallback: Try to parse ViewId from type name (format: "ProjectName_ViewId")
                        string typeName = symbol.Name;
                        int lastUnderscore = typeName.LastIndexOf('_');
                        if (lastUnderscore > 0 && lastUnderscore < typeName.Length - 1)
                        {
                            string possibleViewId = typeName.Substring(lastUnderscore + 1);
                            if (int.TryParse(possibleViewId, out int parsedViewId))
                            {
                                viewIds.Add(parsedViewId);
                                System.Diagnostics.Debug.WriteLine($"      ✅ Type '{symbol.Name}' -> ViewId {parsedViewId} (from type name)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"      ⚠️ Error processing type '{symbol.Name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"   ⚠️ Error getting used view IDs: {ex.Message}");
            }
            finally
            {
                // Clear references to allow garbage collection
                familySymbols?.Clear();
                instanceCountBySymbol?.Clear();
            }

            return viewIds.ToList();
        }

        /// <summary>
        /// Check if a FamilySymbol is a DetailReferenceFamily type
        /// </summary>
        private bool IsDetailReferenceFamilySymbol(FamilySymbol symbol)
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
        /// Get a type parameter value from a FamilySymbol
        /// </summary>
        private string GetTypeParameterValue(FamilySymbol symbol, string parameterName)
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
        /// Check if a family instance is a DetailReferenceFamily
        /// </summary>
        private bool IsDetailReferenceFamily(FamilyInstance instance)
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
        /// Get a parameter value from a family instance (checks both instance and type parameters)
        /// </summary>
        private string GetParameterValue(FamilyInstance instance, string parameterName)
        {
            try
            {
                // Try instance parameter first
                Parameter param = instance.LookupParameter(parameterName);
                if (param != null && param.HasValue)
                {
                    return param.AsString() ?? "";
                }

                // Try type parameter
                param = instance.Symbol?.LookupParameter(parameterName);
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

        private List<ViewInfo> GetSheetPlacedViews(Document doc, string projectGuid = "")
        {
            var viewInfoList = new List<ViewInfo>();
            List<ViewSheet> sheets = null;

            try
            {
                using (var sheetCollector = new FilteredElementCollector(doc))
                {
                    sheets = sheetCollector
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .ToList();
                }

                System.Diagnostics.Debug.WriteLine($"   📄 Scanning {sheets.Count} sheets...");

                string projectName = PortfolioSettings.GetProjectName(doc);

                foreach (ViewSheet sheet in sheets)
                {
                    var viewports = sheet.GetAllViewports();

                    foreach (ElementId viewportId in viewports)
                    {
                        Viewport viewport = doc.GetElement(viewportId) as Viewport;
                        if (viewport == null) continue;

                        View view = doc.GetElement(viewport.ViewId) as View;
                        if (view == null) continue;

                        string topNote = GetViewTopNote(view);
                        string detailNumber = viewport.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() ?? "";

                        var viewInfo = new ViewInfo
                        {
                            ViewId = view.Id.IntegerValue,
                            ViewName = view.Name ?? "",
                            ViewType = view.ViewType.ToString(),
                            SheetNumber = sheet.SheetNumber ?? "",
                            SheetName = sheet.Name ?? "",
                            DetailNumber = detailNumber,
                            TopNote = topNote,
                            SourceProjectName = projectName,
                            SourceProjectGuid = projectGuid ?? "",
                            LastModified = DateTime.Now
                        };

                        viewInfoList.Add(viewInfo);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"   ✅ Collected {viewInfoList.Count} view records");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error collecting views: {ex.Message}");
            }
            finally
            {
                // Clear references to allow garbage collection
                sheets?.Clear();
            }

            return viewInfoList;
        }

        private string GetViewTopNote(View view)
        {
            try
            {
                Parameter topNoteParam = view.LookupParameter("Top Note");
                if (topNoteParam != null && topNoteParam.HasValue)
                {
                    return topNoteParam.AsString() ?? "";
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Push top notes from the portfolio JSON (source of truth) to Revit's "Top Note" shared parameter.
        /// Called post-sync so transactions are allowed. Only updates views that belong to the current project.
        /// Also pulls top notes from OTHER projects' views and updates any placed detail family instances.
        /// </summary>
        private void PushTopNotesToRevit(Document doc, string portfolioPath)
        {
            try
            {
                var portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioPath);
                if (portfolioData?.Views == null) return;

                string currentProjectName = PortfolioSettings.GetProjectName(doc);

                // Build lookup: ViewId → TopNote for views belonging to this project
                var topNoteLookup = new Dictionary<int, string>();
                foreach (var view in portfolioData.Views)
                {
                    if (!string.IsNullOrEmpty(view.TopNote))
                    {
                        topNoteLookup[view.ViewId] = view.TopNote;
                    }
                }

                if (topNoteLookup.Count == 0) return;

                int updated = 0;
                int skipped = 0;

                using (Transaction trans = new Transaction(doc, "Update Top Notes from Portfolio"))
                {
                    trans.Start();

                    // Update views in the current project that have matching ViewIds
                    List<View> views;
                    using (var collector = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .WhereElementIsNotElementType())
                    {
                        views = collector.Cast<View>().ToList();
                    }

                    foreach (View view in views)
                    {
                        if (view.IsTemplate) continue;

                        int viewId = view.Id.IntegerValue;
                        if (!topNoteLookup.TryGetValue(viewId, out string jsonTopNote))
                            continue;

                        // Check current value — only write if different
                        Parameter topNoteParam = view.LookupParameter("Top Note");
                        if (topNoteParam == null || topNoteParam.IsReadOnly) continue;

                        string currentValue = topNoteParam.AsString() ?? "";
                        if (string.Equals(currentValue, jsonTopNote, StringComparison.Ordinal))
                            continue; // Already matches

                        try
                        {
                            topNoteParam.Set(jsonTopNote);
                            updated++;
                        }
                        catch
                        {
                            skipped++; // Might be owned by another user
                        }
                    }

                    if (updated > 0)
                    {
                        trans.Commit();
                        SyncLog($"  TOP NOTES: Pushed {updated} top notes to Revit parameters (skipped {skipped})");
                    }
                    else
                    {
                        trans.RollBack();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error pushing top notes to Revit: {ex.Message}");
                // Non-critical — don't fail the sync
            }
        }

        #endregion
    }

    #region Tools & Reports Command

    /// <summary>
    /// Command to open the Tools & Reports window
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ToolsAndReportsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                Document doc = uiApp.ActiveUIDocument?.Document;

                if (doc == null)
                {
                    TaskDialog.Show("Error", "No active document found.");
                    return Result.Failed;
                }

                var window = new ToolsAndReportsWindow(doc, uiApp);
                window.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion
}