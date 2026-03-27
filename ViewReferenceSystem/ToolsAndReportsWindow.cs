// ToolsAndReportsWindow.cs

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ViewReferenceSystem.Core;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.Utilities;
using WpfColor = System.Windows.Media.Color;
using WpfGrid = System.Windows.Controls.Grid;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace ViewReferenceSystem.UI
{
    /// <summary>
    /// Tools & Reports Window - Family Management and Portfolio Reports
    /// </summary>
    public class ToolsAndReportsWindow : Window
    {
        #region Fields

        private Document _document;
        private UIApplication _uiApp;
        private PortfolioSettings.Portfolio _portfolioData;
        private string _portfolioPath;

        // UI Elements - Family Management Tab
        private ListBox _familyListBox;
        private Button _publishButton;
        private Button _addFamilyButton;
        private Button _removeFamilyButton;
        private TextBlock _familyStatusText;

        // UI Elements - Reports Tab
        private Button _familyReportButton;
        private Button _unusedDetailsReportButton;
        private Button _migrationStatusButton;
        private FlowDocumentScrollViewer _reportViewer;

        // Colors matching Revit theme
        private static readonly WpfColor RevitBackground = WpfColor.FromRgb(45, 45, 48);
        private static readonly WpfColor RevitPanelBackground = WpfColor.FromRgb(60, 60, 64);
        private static readonly WpfColor RevitText = WpfColor.FromRgb(241, 241, 241);
        private static readonly WpfColor RevitAccent = WpfColor.FromRgb(0, 122, 204);
        private static readonly WpfColor RevitSuccess = WpfColor.FromRgb(76, 175, 80);
        private static readonly WpfColor RevitWarning = WpfColor.FromRgb(255, 152, 0);
        private static readonly WpfColor RevitError = WpfColor.FromRgb(244, 67, 54);

        #endregion

        #region Constructor

        public ToolsAndReportsWindow(Document doc, UIApplication uiApp)
        {
            _document = doc;
            _uiApp = uiApp;

            InitializeWindow();
            LoadPortfolioData();
            RefreshFamilyList();
        }

        #endregion

        #region Window Initialization

        private void InitializeWindow()
        {
            Title = "Tools & Reports";
            Width = 700;
            Height = 550;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(RevitBackground);
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 500;
            MinHeight = 400;

            // Main grid
            var mainGrid = new WpfGrid();
            mainGrid.Margin = new Thickness(10);

            // Create TabControl
            var tabControl = new TabControl();
            tabControl.Background = new SolidColorBrush(RevitPanelBackground);
            tabControl.Foreground = new SolidColorBrush(RevitText);

            // Family Management Tab
            var familyTab = new TabItem();
            familyTab.Header = "Family Management";
            familyTab.Content = CreateFamilyManagementTab();

            // Reports Tab
            var reportsTab = new TabItem();
            reportsTab.Header = "Reports";
            reportsTab.Content = CreateReportsTab();

            tabControl.Items.Add(familyTab);
            tabControl.Items.Add(reportsTab);

            mainGrid.Children.Add(tabControl);
            Content = mainGrid;
        }

        private UIElement CreateFamilyManagementTab()
        {
            var grid = new WpfGrid();
            grid.Margin = new Thickness(10);

            // Define rows
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Header
            var headerText = new TextBlock
            {
                Text = "Monitored Families",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(RevitText),
                Margin = new Thickness(0, 0, 0, 10)
            };
            WpfGrid.SetRow(headerText, 0);
            grid.Children.Add(headerText);

            // Family List
            _familyListBox = new ListBox
            {
                Background = new SolidColorBrush(RevitPanelBackground),
                Foreground = new SolidColorBrush(RevitText),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(80, 80, 84)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 10)
            };
            _familyListBox.SelectionChanged += FamilyListBox_SelectionChanged;
            WpfGrid.SetRow(_familyListBox, 1);
            grid.Children.Add(_familyListBox);

            // Status text
            _familyStatusText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(RevitText),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            WpfGrid.SetRow(_familyStatusText, 2);
            grid.Children.Add(_familyStatusText);

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            _publishButton = CreateButton("Publish Selected", RevitAccent);
            _publishButton.Click += PublishButton_Click;
            _publishButton.IsEnabled = false;
            _publishButton.Margin = new Thickness(0, 0, 10, 0);
            buttonPanel.Children.Add(_publishButton);

            _addFamilyButton = CreateButton("Add Family", RevitSuccess);
            _addFamilyButton.Click += AddFamilyButton_Click;
            _addFamilyButton.Margin = new Thickness(0, 0, 10, 0);
            buttonPanel.Children.Add(_addFamilyButton);

            _removeFamilyButton = CreateButton("Remove Selected", RevitError);
            _removeFamilyButton.Click += RemoveFamilyButton_Click;
            _removeFamilyButton.IsEnabled = false;
            buttonPanel.Children.Add(_removeFamilyButton);

            var refreshButton = CreateButton("Refresh", WpfColor.FromRgb(100, 100, 104));
            refreshButton.Click += (s, e) => { LoadPortfolioData(); RefreshFamilyList(); };
            refreshButton.Margin = new Thickness(20, 0, 0, 0);
            buttonPanel.Children.Add(refreshButton);

            WpfGrid.SetRow(buttonPanel, 3);
            grid.Children.Add(buttonPanel);

            return grid;
        }

        private UIElement CreateReportsTab()
        {
            var grid = new WpfGrid();
            grid.Margin = new Thickness(10);

            // Define rows
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Report content

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };

            _familyReportButton = CreateButton("Family Update Status", RevitAccent);
            _familyReportButton.Click += FamilyReportButton_Click;
            _familyReportButton.Margin = new Thickness(0, 0, 10, 0);
            buttonPanel.Children.Add(_familyReportButton);

            _unusedDetailsReportButton = CreateButton("Unreferenced Details", RevitWarning);
            _unusedDetailsReportButton.Click += UnusedDetailsReportButton_Click;
            _unusedDetailsReportButton.Margin = new Thickness(0, 0, 10, 0);
            buttonPanel.Children.Add(_unusedDetailsReportButton);

            _migrationStatusButton = CreateButton("Migration Status", WpfColor.FromRgb(0, 150, 136));
            _migrationStatusButton.Click += MigrationStatusButton_Click;
            buttonPanel.Children.Add(_migrationStatusButton);

            WpfGrid.SetRow(buttonPanel, 0);
            grid.Children.Add(buttonPanel);

            // Report viewer
            _reportViewer = new FlowDocumentScrollViewer
            {
                Background = new SolidColorBrush(RevitPanelBackground),
                Foreground = new SolidColorBrush(RevitText),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(80, 80, 84)),
                BorderThickness = new Thickness(1),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            // Initialize with placeholder
            var placeholderDoc = new FlowDocument();
            placeholderDoc.Background = new SolidColorBrush(RevitPanelBackground);
            placeholderDoc.Foreground = new SolidColorBrush(RevitText);
            placeholderDoc.Blocks.Add(new Paragraph(new Run("Select a report to generate."))
            {
                Foreground = new SolidColorBrush(WpfColor.FromRgb(150, 150, 150)),
                FontStyle = FontStyles.Italic
            });
            _reportViewer.Document = placeholderDoc;

            WpfGrid.SetRow(_reportViewer, 1);
            grid.Children.Add(_reportViewer);

            return grid;
        }

        private Button CreateButton(string text, WpfColor backgroundColor)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(15, 8, 15, 8),
                Background = new SolidColorBrush(backgroundColor),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            return button;
        }

        #endregion

        #region Data Loading

        private void LoadPortfolioData()
        {
            try
            {
                _portfolioPath = PortfolioSettings.GetJsonPath(_document);

                if (string.IsNullOrEmpty(_portfolioPath) || (!FirebaseClient.IsFirebasePath(_portfolioPath) && !System.IO.File.Exists(_portfolioPath)))
                {
                    _portfolioData = null;
                    UpdateStatus("Project is not part of a portfolio.", RevitWarning);
                    return;
                }

                _portfolioData = PortfolioSettings.LoadPortfolioFromFile(_portfolioPath);

                if (_portfolioData == null)
                {
                    UpdateStatus("Could not load portfolio data.", RevitError);
                    return;
                }

                UpdateStatus($"Portfolio: {_portfolioData.PortfolioName}", RevitSuccess);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}", RevitError);
            }
        }

        private void RefreshFamilyList()
        {
            _familyListBox.Items.Clear();

            if (_portfolioData?.MonitoredFamilies == null || !_portfolioData.MonitoredFamilies.Any())
            {
                _familyListBox.Items.Add(CreateFamilyListItem("No monitored families", null, false));
                return;
            }

            string currentProjectName = PortfolioSettings.GetProjectName(_document);
            var currentProject = _portfolioData.ProjectInfos?.FirstOrDefault(p =>
                string.Equals(p.ProjectName, currentProjectName, StringComparison.OrdinalIgnoreCase));

            foreach (var family in _portfolioData.MonitoredFamilies)
            {
                bool isUpdated = false;
                currentProject?.FamilyUpdateStatus?.TryGetValue(family.FamilyName, out isUpdated);

                var item = CreateFamilyListItem(family.FamilyName, family, isUpdated);
                _familyListBox.Items.Add(item);
            }
        }

        private ListBoxItem CreateFamilyListItem(string displayText, PortfolioSettings.MonitoredFamily family, bool isUpdated)
        {
            var item = new ListBoxItem();
            item.Tag = family;

            var panel = new StackPanel { Orientation = Orientation.Vertical };

            // Top row: status icon + name + publish info
            var topRow = new StackPanel { Orientation = Orientation.Horizontal };

            // Status indicator
            string statusIcon;
            if (family == null)
                statusIcon = "ℹ️";
            else if (family.IsCheckedOut && !family.IsCheckedOutByCurrentUser)
                statusIcon = "🔒";
            else if (family.IsCheckedOutByCurrentUser)
                statusIcon = "✏️";
            else if (isUpdated)
                statusIcon = "✅";
            else
                statusIcon = "⏳";

            topRow.Children.Add(new TextBlock
            {
                Text = statusIcon,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            // Family name
            topRow.Children.Add(new TextBlock
            {
                Text = displayText,
                Foreground = new SolidColorBrush(RevitText),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            // Published info
            if (family != null && family.HasBeenPublished)
            {
                topRow.Children.Add(new TextBlock
                {
                    Text = $"  (Published {family.LastPublished:MM/dd/yyyy HH:mm} by {family.PublishedByProject})",
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(150, 150, 150)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else if (family != null)
            {
                topRow.Children.Add(new TextBlock
                {
                    Text = "  (Never published)",
                    Foreground = new SolidColorBrush(RevitWarning),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            panel.Children.Add(topRow);

            // Checkout row — only shown if checked out
            if (family != null && family.IsCheckedOut)
            {
                var checkoutColor = family.IsCheckedOutByCurrentUser ? RevitAccent : RevitWarning;
                var checkoutMsg = family.IsCheckedOutByCurrentUser
                    ? $"  ✏️ Checked out by you ({Environment.UserName}) — close the family editor when done"
                    : $"  🔒 Checked out by {family.CheckoutDisplayString}";

                panel.Children.Add(new TextBlock
                {
                    Text = checkoutMsg,
                    Foreground = new SolidColorBrush(checkoutColor),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(24, 2, 0, 0)
                });
            }

            item.Content = panel;
            item.Padding = new Thickness(5);

            return item;
        }

        private void UpdateStatus(string message, WpfColor color)
        {
            _familyStatusText.Text = message;
            _familyStatusText.Foreground = new SolidColorBrush(color);
        }

        #endregion

        #region Event Handlers - Family Management

        private void FamilyListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = _familyListBox.SelectedItem as ListBoxItem;
            var family = selectedItem?.Tag as PortfolioSettings.MonitoredFamily;

            _publishButton.IsEnabled = family != null;

            // Show checkout status in status bar
            if (family != null && family.IsCheckedOut && !family.IsCheckedOutByCurrentUser)
                UpdateStatus($"🔒 Checked out by {family.CheckoutDisplayString} — publishing will warn you", RevitWarning);
            else if (family != null && family.IsCheckedOutByCurrentUser)
                UpdateStatus($"✏️ Checked out by you — close the family editor when done", RevitAccent);
            else
                UpdateStatus("", RevitText);

            // Can't remove DetailReferenceFamily
            bool canRemove = family != null &&
                !string.Equals(family.FamilyName, PortfolioSettings.DEFAULT_MONITORED_FAMILY, StringComparison.OrdinalIgnoreCase);
            _removeFamilyButton.IsEnabled = canRemove;
        }

        private void PublishButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = _familyListBox.SelectedItem as ListBoxItem;
            var family = selectedItem?.Tag as PortfolioSettings.MonitoredFamily;

            if (family == null)
            {
                UpdateStatus("No family selected.", RevitWarning);
                return;
            }

            // Check if family exists in document
            var existingFamily = new FilteredElementCollector(_document)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, family.FamilyName, StringComparison.OrdinalIgnoreCase));

            if (existingFamily == null)
            {
                UpdateStatus($"Family '{family.FamilyName}' not found in project. Load it first.", RevitError);
                return;
            }

            // Warn if someone else has it checked out
            if (family.IsCheckedOut && !family.IsCheckedOutByCurrentUser)
            {
                var checkoutWarning = MessageBox.Show(
                    $"Warning: '{family.FamilyName}' is currently checked out by:\n\n" +
                    $"{family.CheckoutDisplayString}\n\n" +
                    $"You are not the person with checkout control. Publishing now will overwrite " +
                    $"any changes they are currently working on.\n\n" +
                    $"Are you sure you want to send this update out to the group?",
                    "Not Your Checkout — Confirm Override",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (checkoutWarning != MessageBoxResult.Yes)
                    return;
            }

            // Standard publish confirmation
            var result = MessageBox.Show(
                $"Publish '{family.FamilyName}' to the portfolio?\n\n" +
                "This will update the network copy and mark all other projects as needing an update.",
                "Confirm Publish",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            if (FamilyMonitorManager.PublishFamily(_document, family.FamilyName, out string errorMessage))
            {
                UpdateStatus($"✅ '{family.FamilyName}' published successfully!", RevitSuccess);
                LoadPortfolioData();
                RefreshFamilyList();
            }
            else
            {
                UpdateStatus($"❌ Publish failed: {errorMessage}", RevitError);
            }
        }

        private void AddFamilyButton_Click(object sender, RoutedEventArgs e)
        {
            // Create dialog to select family
            var dialog = new AddFamilyDialog(_document, _portfolioData);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedFamilyName))
            {
                string fileName = dialog.SelectedFamilyName + ".rfa";

                if (FamilyMonitorManager.AddMonitoredFamily(_document, dialog.SelectedFamilyName, fileName, out string errorMessage))
                {
                    UpdateStatus($"✅ '{dialog.SelectedFamilyName}' added and published!", RevitSuccess);
                    LoadPortfolioData();
                    RefreshFamilyList();
                }
                else
                {
                    UpdateStatus($"❌ Failed: {errorMessage}", RevitError);
                }
            }
        }

        private void RemoveFamilyButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = _familyListBox.SelectedItem as ListBoxItem;
            var family = selectedItem?.Tag as PortfolioSettings.MonitoredFamily;

            if (family == null)
            {
                UpdateStatus("No family selected.", RevitWarning);
                return;
            }

            if (string.Equals(family.FamilyName, PortfolioSettings.DEFAULT_MONITORED_FAMILY, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatus($"Cannot remove '{PortfolioSettings.DEFAULT_MONITORED_FAMILY}' - it is required.", RevitError);
                return;
            }

            var result = MessageBox.Show(
                $"Remove '{family.FamilyName}' from monitoring?\n\n" +
                "The family file will NOT be deleted, only removed from tracking.",
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            if (FamilyMonitorManager.RemoveMonitoredFamily(_document, family.FamilyName, out string errorMessage))
            {
                UpdateStatus($"✅ '{family.FamilyName}' removed from monitoring.", RevitSuccess);
                LoadPortfolioData();
                RefreshFamilyList();
            }
            else
            {
                UpdateStatus($"❌ Remove failed: {errorMessage}", RevitError);
            }
        }

        #endregion

        #region Event Handlers - Reports

        private void FamilyReportButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateFamilyStatusReport();
        }

        private void UnusedDetailsReportButton_Click(object sender, RoutedEventArgs e)
        {
            // Show sheet selection dialog first
            if (_portfolioData == null)
            {
                MessageBox.Show("Portfolio not loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var authorityProject = _portfolioData.ProjectInfos?.FirstOrDefault(p => p.IsTypicalDetailsAuthority);
            if (authorityProject == null)
            {
                MessageBox.Show("No Typical Details Authority project designated.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Get unique sheets from authority project views
            var authorityViews = _portfolioData.Views?
                .Where(v => string.Equals(v.SourceProjectName, authorityProject.ProjectName, StringComparison.OrdinalIgnoreCase))
                .ToList() ?? new List<ViewInfo>();

            if (!authorityViews.Any())
            {
                MessageBox.Show("No views found from the Typical Details Authority project.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Get unique sheets
            var sheets = authorityViews
                .GroupBy(v => v.SheetNumber)
                .Select(g => new SheetInfo
                {
                    SheetNumber = g.Key,
                    SheetName = g.First().SheetName,
                    ViewCount = g.Count()
                })
                .OrderBy(s => s.SheetNumber)
                .ToList();

            // Show sheet selection dialog
            var sheetDialog = new SheetSelectionDialog(sheets, authorityProject.DisplayNickname);
            sheetDialog.Owner = this;

            if (sheetDialog.ShowDialog() == true)
            {
                var selectedSheets = sheetDialog.SelectedSheetNumbers;
                if (selectedSheets.Any())
                {
                    GenerateUnreferencedDetailsReport(selectedSheets);
                }
            }
        }

        private void MigrationStatusButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateMigrationStatusReport();
        }

        private void GenerateMigrationStatusReport()
        {
            var flowDoc = new FlowDocument();
            flowDoc.Background = new SolidColorBrush(RevitPanelBackground);
            flowDoc.Foreground = new SolidColorBrush(RevitText);
            flowDoc.PagePadding = new Thickness(20);

            AddReportHeader(flowDoc, "Portfolio Migration Status");
            AddReportSubheader(flowDoc, $"Generated: {DateTime.Now:MM/dd/yyyy HH:mm}");

            // Show what THIS model's ViewReference_JsonPath is currently set to
            string currentJsonPath = PortfolioSettings.GetJsonPath(_document);
            bool currentIsFirebase = !string.IsNullOrEmpty(currentJsonPath) && FirebaseClient.IsFirebasePath(currentJsonPath);

            AddReportSection(flowDoc, "This Model");
            AddReportParagraph(flowDoc, $"Project Name: {PortfolioSettings.GetProjectName(_document)}");
            AddReportParagraph(flowDoc, $"Project GUID: {PortfolioSettings.GetProjectGuid(_document)}");
            AddReportParagraph(flowDoc, $"ViewReference_JsonPath: {(string.IsNullOrEmpty(currentJsonPath) ? "(empty)" : currentJsonPath)}");
            AddReportParagraph(flowDoc, $"Path Type: {(currentIsFirebase ? "☁️ Firebase" : "📁 Local / Network")}",
                currentIsFirebase ? RevitSuccess : RevitWarning);

            if (_portfolioData == null)
            {
                AddReportParagraph(flowDoc, "Portfolio data not loaded — cannot check other projects.", RevitError);
                _reportViewer.Document = flowDoc;
                return;
            }

            // Portfolio-level info
            AddReportSection(flowDoc, "Portfolio Info");
            AddReportParagraph(flowDoc, $"Portfolio Name: {_portfolioData.PortfolioName}");
            AddReportParagraph(flowDoc, $"FirebasePath in JSON: {(string.IsNullOrEmpty(_portfolioData.FirebasePath) ? "(not set)" : _portfolioData.FirebasePath)}");
            AddReportParagraph(flowDoc, $"Data loaded from: {(string.IsNullOrEmpty(_portfolioPath) ? "(unknown)" : _portfolioPath)}");

            // Project table
            AddReportSection(flowDoc, "All Projects in Portfolio");

            if (_portfolioData.ProjectInfos == null || !_portfolioData.ProjectInfos.Any())
            {
                AddReportParagraph(flowDoc, "No projects found in portfolio.", RevitWarning);
                _reportViewer.Document = flowDoc;
                return;
            }

            var table = new Table();
            table.CellSpacing = 0;
            table.Columns.Add(new TableColumn { Width = new GridLength(180) }); // Name
            table.Columns.Add(new TableColumn { Width = new GridLength(100) }); // Nickname
            table.Columns.Add(new TableColumn { Width = new GridLength(90) });  // IsMigrated
            table.Columns.Add(new TableColumn { Width = new GridLength(140) }); // Last Sync

            var headerGroup = new TableRowGroup();
            var headerRow = new TableRow();
            headerRow.Background = new SolidColorBrush(WpfColor.FromRgb(70, 70, 74));
            headerRow.Cells.Add(CreateTableCell("Project Name", true));
            headerRow.Cells.Add(CreateTableCell("Nickname", true));
            headerRow.Cells.Add(CreateTableCell("Migrated?", true));
            headerRow.Cells.Add(CreateTableCell("Last Sync", true));
            headerGroup.Rows.Add(headerRow);
            table.RowGroups.Add(headerGroup);

            string currentProjectGuid = PortfolioSettings.GetProjectGuid(_document);
            int migratedCount = 0;
            int totalCount = _portfolioData.ProjectInfos.Count;

            var dataGroup = new TableRowGroup();
            foreach (var project in _portfolioData.ProjectInfos.OrderBy(p => p.ProjectName))
            {
                bool isThisModel = !string.IsNullOrEmpty(project.ProjectGuid) &&
                    string.Equals(project.ProjectGuid, currentProjectGuid, StringComparison.OrdinalIgnoreCase);

                string displayName = project.ProjectName + (isThisModel ? " ◀" : "");
                string migratedDisplay = project.IsMigrated ? "✅ Yes" : "❌ No";
                WpfColor migratedColor = project.IsMigrated ? RevitSuccess : RevitWarning;
                string lastSync = project.LastSync > DateTime.MinValue
                    ? project.LastSync.ToString("MM/dd/yyyy HH:mm")
                    : "Never";

                if (project.IsMigrated) migratedCount++;

                var row = new TableRow();
                if (isThisModel)
                    row.Background = new SolidColorBrush(WpfColor.FromRgb(50, 60, 70));
                row.Cells.Add(CreateTableCell(displayName));
                row.Cells.Add(CreateTableCell(project.DisplayNickname));
                row.Cells.Add(CreateTableCell(migratedDisplay, foreground: migratedColor));
                row.Cells.Add(CreateTableCell(lastSync));
                dataGroup.Rows.Add(row);
            }
            table.RowGroups.Add(dataGroup);
            flowDoc.Blocks.Add(table);

            // Summary
            AddReportSection(flowDoc, "Summary");
            AddReportParagraph(flowDoc, $"{migratedCount} of {totalCount} projects migrated to Firebase",
                migratedCount == totalCount ? RevitSuccess : RevitWarning);

            if (migratedCount < totalCount)
            {
                AddReportParagraph(flowDoc,
                    "Projects that show ❌ No still have their ViewReference_JsonPath pointing to the old network path. " +
                    "They should auto-migrate the next time they are opened and the local JSON file contains the FirebasePath breadcrumb.",
                    WpfColor.FromRgb(180, 180, 180));
            }

            _reportViewer.Document = flowDoc;
        }

        private void GenerateFamilyStatusReport()
        {
            var report = FamilyMonitorManager.GetFamilyStatusReport(_document);

            var flowDoc = new FlowDocument();
            flowDoc.Background = new SolidColorBrush(RevitPanelBackground);
            flowDoc.Foreground = new SolidColorBrush(RevitText);
            flowDoc.PagePadding = new Thickness(20);

            // Title
            AddReportHeader(flowDoc, "Family Update Status Report");
            AddReportSubheader(flowDoc, $"Portfolio: {report.PortfolioName}");
            AddReportSubheader(flowDoc, $"Generated: {DateTime.Now:MM/dd/yyyy HH:mm}");

            if (!report.Success)
            {
                AddReportParagraph(flowDoc, $"Error: {report.ErrorMessage}", RevitError);
                _reportViewer.Document = flowDoc;
                return;
            }

            // Summary
            AddReportSection(flowDoc, "Summary");
            AddReportParagraph(flowDoc, $"Total Monitored Families: {report.TotalFamilies}");
            AddReportParagraph(flowDoc, $"Total Projects: {report.TotalProjects}");
            AddReportParagraph(flowDoc, $"Total Outdated Entries: {report.TotalOutdated}",
                report.TotalOutdated > 0 ? RevitWarning : RevitSuccess);

            // Details for each family
            foreach (var familyReport in report.Families)
            {
                AddReportSection(flowDoc, $"📦 {familyReport.FamilyName}");

                if (familyReport.LastPublished.HasValue)
                {
                    AddReportParagraph(flowDoc,
                        $"Last Published: {familyReport.LastPublished:MM/dd/yyyy HH:mm} by {familyReport.PublishedByProject}");
                }
                else
                {
                    AddReportParagraph(flowDoc, "Never published", RevitWarning);
                }

                AddReportParagraph(flowDoc,
                    $"Status: {familyReport.UpdatedCount} current, {familyReport.OutdatedCount} need update");

                // Project status table
                var table = new Table();
                table.CellSpacing = 0;
                table.Columns.Add(new TableColumn { Width = new GridLength(200) });
                table.Columns.Add(new TableColumn { Width = new GridLength(100) });
                table.Columns.Add(new TableColumn { Width = new GridLength(150) });

                var headerGroup = new TableRowGroup();
                var headerRow = new TableRow();
                headerRow.Background = new SolidColorBrush(WpfColor.FromRgb(70, 70, 74));
                headerRow.Cells.Add(CreateTableCell("Project", true));
                headerRow.Cells.Add(CreateTableCell("Status", true));
                headerRow.Cells.Add(CreateTableCell("Last Sync", true));
                headerGroup.Rows.Add(headerRow);
                table.RowGroups.Add(headerGroup);

                var dataGroup = new TableRowGroup();
                foreach (var projectStatus in familyReport.ProjectStatuses)
                {
                    var row = new TableRow();
                    row.Cells.Add(CreateTableCell(projectStatus.Nickname));
                    row.Cells.Add(CreateTableCell(projectStatus.StatusDisplay,
                        foreground: projectStatus.IsUpdated ? RevitSuccess : RevitWarning));
                    row.Cells.Add(CreateTableCell(projectStatus.LastSync.ToString("MM/dd/yyyy HH:mm")));
                    dataGroup.Rows.Add(row);
                }
                table.RowGroups.Add(dataGroup);

                flowDoc.Blocks.Add(table);
            }

            _reportViewer.Document = flowDoc;
        }

        private void GenerateUnreferencedDetailsReport(HashSet<string> selectedSheets)
        {
            var flowDoc = new FlowDocument();
            flowDoc.Background = new SolidColorBrush(RevitPanelBackground);
            flowDoc.Foreground = new SolidColorBrush(RevitText);
            flowDoc.PagePadding = new Thickness(20);
            flowDoc.FontFamily = new FontFamily("Consolas");

            AddReportHeader(flowDoc, "Unreferenced Details Report");
            AddReportSubheader(flowDoc, $"Generated: {DateTime.Now:MM/dd/yyyy HH:mm}");

            // Find the Typical Details Authority project
            var authorityProject = _portfolioData.ProjectInfos?.FirstOrDefault(p => p.IsTypicalDetailsAuthority);
            AddReportSubheader(flowDoc, $"Typical Details Authority: {authorityProject.DisplayNickname}");
            AddReportSubheader(flowDoc, $"Sheets Included: {selectedSheets.Count}");

            // Get all views from the authority project ON SELECTED SHEETS ONLY
            var authorityViews = _portfolioData.Views?
                .Where(v => string.Equals(v.SourceProjectName, authorityProject.ProjectName, StringComparison.OrdinalIgnoreCase))
                .Where(v => selectedSheets.Contains(v.SheetNumber))
                .ToList() ?? new List<ViewInfo>();

            if (!authorityViews.Any())
            {
                AddReportParagraph(flowDoc, "No views found on selected sheets.", RevitWarning);
                _reportViewer.Document = flowDoc;
                return;
            }

            // Get all UsesViewIds from non-authority projects
            var referencedViewIds = new HashSet<int>();
            foreach (var project in _portfolioData.ProjectInfos.Where(p => !p.IsTypicalDetailsAuthority))
            {
                if (project.UsesViewIds != null)
                {
                    foreach (var viewId in project.UsesViewIds)
                    {
                        referencedViewIds.Add(viewId);
                    }
                }
            }

            // Find unreferenced views - sorted by sheet then detail number
            var unreferencedViews = authorityViews
                .Where(v => !referencedViewIds.Contains(v.ViewId))
                .OrderBy(v => v.SheetNumber ?? "")
                .ThenBy(v => v.DetailNumber ?? "")
                .ThenBy(v => v.ViewName ?? "")
                .ToList();

            var referencedViews = authorityViews
                .Where(v => referencedViewIds.Contains(v.ViewId))
                .ToList();

            // Summary line
            AddReportParagraph(flowDoc, "─────────────────────────────────────────────────────────────");
            AddReportParagraph(flowDoc, $"Total: {authorityViews.Count}  |  Referenced: {referencedViews.Count}  |  Unreferenced: {unreferencedViews.Count}",
                unreferencedViews.Count > 0 ? RevitWarning : RevitSuccess);
            AddReportParagraph(flowDoc, "─────────────────────────────────────────────────────────────");

            // Unreferenced details - one line per detail
            if (unreferencedViews.Any())
            {
                AddReportSection(flowDoc, "UNREFERENCED DETAILS");

                foreach (var view in unreferencedViews)
                {
                    string sheet = view.SheetNumber ?? "??";
                    string detail = view.DetailNumber ?? "?";
                    string name = view.ViewName ?? "Unnamed";
                    string topNote = !string.IsNullOrEmpty(view.TopNote) ? $" - {view.TopNote}" : "";

                    AddReportParagraph(flowDoc, $"{sheet}/{detail}  {name}{topNote}");
                }
            }
            else
            {
                AddReportParagraph(flowDoc, "✅ All typical details on selected sheets are referenced by at least one project.", RevitSuccess);
            }

            // Referenced details by project - simple counts
            AddReportParagraph(flowDoc, "");
            AddReportParagraph(flowDoc, "─────────────────────────────────────────────────────────────");
            AddReportSection(flowDoc, "REFERENCED DETAILS BY PROJECT");

            foreach (var project in _portfolioData.ProjectInfos
                .Where(p => !p.IsTypicalDetailsAuthority)
                .OrderBy(p => p.DisplayNickname))
            {
                int refCount = project.UsesViewIds?.Count ?? 0;
                AddReportParagraph(flowDoc, $"{project.DisplayNickname}: {refCount} details referenced");
            }

            _reportViewer.Document = flowDoc;
        }

        #endregion

        #region Report Helpers

        private void AddReportHeader(FlowDocument doc, string text)
        {
            var para = new Paragraph(new Run(text))
            {
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(RevitText),
                Margin = new Thickness(0, 0, 0, 5)
            };
            doc.Blocks.Add(para);
        }

        private void AddReportSubheader(FlowDocument doc, string text)
        {
            var para = new Paragraph(new Run(text))
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(180, 180, 180)),
                Margin = new Thickness(0, 0, 0, 3)
            };
            doc.Blocks.Add(para);
        }

        private void AddReportSection(FlowDocument doc, string text)
        {
            var para = new Paragraph(new Run(text))
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(RevitAccent),
                Margin = new Thickness(0, 15, 0, 5)
            };
            doc.Blocks.Add(para);
        }

        private void AddReportParagraph(FlowDocument doc, string text, WpfColor? foreground = null)
        {
            var para = new Paragraph(new Run(text))
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(foreground ?? RevitText),
                Margin = new Thickness(0, 2, 0, 2)
            };
            doc.Blocks.Add(para);
        }

        private TableCell CreateTableCell(string text, bool isHeader = false, WpfColor? foreground = null)
        {
            var cell = new TableCell(new Paragraph(new Run(text ?? ""))
            {
                Margin = new Thickness(0)
            });
            cell.Padding = new Thickness(8, 4, 8, 4);
            cell.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(80, 80, 84));
            cell.BorderThickness = new Thickness(0, 0, 1, 1);

            if (isHeader)
            {
                cell.FontWeight = FontWeights.Bold;
            }

            if (foreground.HasValue)
            {
                cell.Foreground = new SolidColorBrush(foreground.Value);
            }

            return cell;
        }

        #endregion
    }

    #region Sheet Selection Dialog

    /// <summary>
    /// Helper class for sheet info
    /// </summary>
    public class SheetInfo
    {
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public int ViewCount { get; set; }
        public bool IsSelected { get; set; } = true;
    }

    /// <summary>
    /// Dialog for selecting sheets to include in the report
    /// </summary>
    public class SheetSelectionDialog : Window
    {
        private List<SheetInfo> _sheets;
        private ListBox _sheetListBox;
        private CheckBox _hideUncheckedCheckBox;
        private TextBlock _statusText;

        public HashSet<string> SelectedSheetNumbers { get; private set; } = new HashSet<string>();

        private static readonly WpfColor RevitBackground = WpfColor.FromRgb(45, 45, 48);
        private static readonly WpfColor RevitPanelBackground = WpfColor.FromRgb(60, 60, 64);
        private static readonly WpfColor RevitText = WpfColor.FromRgb(241, 241, 241);
        private static readonly WpfColor RevitAccent = WpfColor.FromRgb(0, 122, 204);

        public SheetSelectionDialog(List<SheetInfo> sheets, string authorityProjectName)
        {
            _sheets = sheets;

            Title = "Select Sheets for Report";
            Width = 600;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(RevitBackground);
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 400;
            MinHeight = 300;

            var mainGrid = new WpfGrid();
            mainGrid.Margin = new Thickness(15);
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filter options
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Header
            var headerText = new TextBlock
            {
                Text = $"Select sheets from {authorityProjectName} to include in report:",
                Foreground = new SolidColorBrush(RevitText),
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            WpfGrid.SetRow(headerText, 0);
            mainGrid.Children.Add(headerText);

            // Filter options
            var filterPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };

            _hideUncheckedCheckBox = new CheckBox
            {
                Content = "Hide unchecked sheets",
                Foreground = new SolidColorBrush(RevitText),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 20, 0)
            };
            _hideUncheckedCheckBox.Checked += HideCheckBox_Changed;
            _hideUncheckedCheckBox.Unchecked += HideCheckBox_Changed;
            filterPanel.Children.Add(_hideUncheckedCheckBox);

            var checkAllButton = new Button
            {
                Content = "Check All",
                Padding = new Thickness(10, 5, 10, 5),
                Background = new SolidColorBrush(WpfColor.FromRgb(80, 80, 84)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 5, 0)
            };
            checkAllButton.Click += CheckAllButton_Click;
            filterPanel.Children.Add(checkAllButton);

            var checkNoneButton = new Button
            {
                Content = "Check None",
                Padding = new Thickness(10, 5, 10, 5),
                Background = new SolidColorBrush(WpfColor.FromRgb(80, 80, 84)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };
            checkNoneButton.Click += CheckNoneButton_Click;
            filterPanel.Children.Add(checkNoneButton);

            WpfGrid.SetRow(filterPanel, 1);
            mainGrid.Children.Add(filterPanel);

            // Sheet list - simple ListBox with manual items
            _sheetListBox = new ListBox
            {
                Background = new SolidColorBrush(RevitPanelBackground),
                Foreground = new SolidColorBrush(RevitText),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(80, 80, 84)),
                BorderThickness = new Thickness(1)
            };

            WpfGrid.SetRow(_sheetListBox, 2);
            mainGrid.Children.Add(_sheetListBox);

            // Status text
            _statusText = new TextBlock
            {
                Foreground = new SolidColorBrush(RevitText),
                Margin = new Thickness(0, 10, 0, 10)
            };
            WpfGrid.SetRow(_statusText, 3);
            mainGrid.Children.Add(_statusText);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var generateButton = new Button
            {
                Content = "Generate Report",
                Padding = new Thickness(15, 8, 15, 8),
                Background = new SolidColorBrush(RevitAccent),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0)
            };
            generateButton.Click += GenerateButton_Click;
            buttonPanel.Children.Add(generateButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(15, 8, 15, 8),
                Background = new SolidColorBrush(WpfColor.FromRgb(100, 100, 104)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(cancelButton);

            WpfGrid.SetRow(buttonPanel, 4);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;

            // Initialize list
            PopulateSheetList();
            UpdateStatus();
        }

        private void HideCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            PopulateSheetList();
        }

        private void PopulateSheetList()
        {
            _sheetListBox.Items.Clear();

            var sheetsToShow = _hideUncheckedCheckBox.IsChecked == true
                ? _sheets.Where(s => s.IsSelected).ToList()
                : _sheets;

            foreach (var sheet in sheetsToShow)
            {
                var item = CreateSheetListItem(sheet);
                _sheetListBox.Items.Add(item);
            }

            UpdateStatus();
        }

        private ListBoxItem CreateSheetListItem(SheetInfo sheet)
        {
            var item = new ListBoxItem();
            item.Tag = sheet;
            item.Padding = new Thickness(5);

            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var checkBox = new CheckBox
            {
                IsChecked = sheet.IsSelected,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            // Store reference to update the sheet object
            checkBox.Tag = sheet;
            checkBox.Checked += SheetCheckBox_Changed;
            checkBox.Unchecked += SheetCheckBox_Changed;
            panel.Children.Add(checkBox);

            var sheetNumText = new TextBlock
            {
                Text = sheet.SheetNumber,
                Width = 100,
                Foreground = new SolidColorBrush(RevitText),
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(sheetNumText);

            var sheetNameText = new TextBlock
            {
                Text = sheet.SheetName,
                Width = 280,
                Foreground = new SolidColorBrush(RevitText),
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(sheetNameText);

            var viewCountText = new TextBlock
            {
                Text = sheet.ViewCount.ToString(),
                Width = 50,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(viewCountText);

            item.Content = panel;
            return item;
        }

        private void SheetCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            var sheet = checkBox?.Tag as SheetInfo;
            if (sheet != null)
            {
                sheet.IsSelected = checkBox.IsChecked == true;
            }
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            int selectedCount = _sheets.Count(s => s.IsSelected);
            int totalViews = _sheets.Where(s => s.IsSelected).Sum(s => s.ViewCount);
            _statusText.Text = $"Selected: {selectedCount} of {_sheets.Count} sheets ({totalViews} views)";
        }

        private void CheckAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var sheet in _sheets)
            {
                sheet.IsSelected = true;
            }
            PopulateSheetList();
        }

        private void CheckNoneButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var sheet in _sheets)
            {
                sheet.IsSelected = false;
            }
            PopulateSheetList();
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedSheetNumbers = new HashSet<string>(
                _sheets.Where(s => s.IsSelected).Select(s => s.SheetNumber)
            );

            if (!SelectedSheetNumbers.Any())
            {
                MessageBox.Show("Please select at least one sheet.", "No Sheets Selected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }

    #endregion

    #region Add Family Dialog

    /// <summary>
    /// Dialog for selecting a family to add to monitoring, organized by category like Revit's Family Browser
    /// </summary>
    public class AddFamilyDialog : Window
    {
        private TreeView _familyTreeView;
        private System.Windows.Controls.TextBox _searchBox;
        private Dictionary<string, List<string>> _familiesByCategory;
        private HashSet<string> _monitoredNames;
        public string SelectedFamilyName { get; private set; }

        private static readonly WpfColor RevitBackground = WpfColor.FromRgb(45, 45, 48);
        private static readonly WpfColor RevitDarker = WpfColor.FromRgb(37, 37, 38);
        private static readonly WpfColor RevitText = WpfColor.FromRgb(241, 241, 241);
        private static readonly WpfColor RevitAccent = WpfColor.FromRgb(0, 122, 204);
        private static readonly WpfColor RevitSubtle = WpfColor.FromRgb(153, 153, 153);

        public AddFamilyDialog(Document doc, PortfolioSettings.Portfolio portfolioData)
        {
            Title = "Add Family to Monitor";
            Width = 420;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(RevitBackground);
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 350;
            MinHeight = 300;

            _monitoredNames = new HashSet<string>(
                portfolioData?.MonitoredFamilies?.Select(f => f.FamilyName) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            _familiesByCategory = FamilyMonitorManager.GetFamiliesByCategory(doc);

            var rootGrid = new WpfGrid();
            rootGrid.Margin = new Thickness(15);
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Search
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Tree
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Selected label
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Buttons

            // Search box
            _searchBox = new System.Windows.Controls.TextBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 8),
                Background = new SolidColorBrush(RevitDarker),
                Foreground = new SolidColorBrush(RevitText),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(67, 67, 70)),
                Padding = new Thickness(5, 3, 5, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            // Placeholder text via GotFocus/LostFocus
            _searchBox.Text = "Search families...";
            _searchBox.Foreground = new SolidColorBrush(RevitSubtle);
            _searchBox.GotFocus += (s, e) =>
            {
                if (_searchBox.Text == "Search families...")
                {
                    _searchBox.Text = "";
                    _searchBox.Foreground = new SolidColorBrush(RevitText);
                }
            };
            _searchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_searchBox.Text))
                {
                    _searchBox.Text = "Search families...";
                    _searchBox.Foreground = new SolidColorBrush(RevitSubtle);
                }
            };
            _searchBox.TextChanged += SearchBox_TextChanged;
            WpfGrid.SetRow(_searchBox, 0);
            rootGrid.Children.Add(_searchBox);

            // Family tree view
            _familyTreeView = new TreeView
            {
                Background = new SolidColorBrush(RevitDarker),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(67, 67, 70)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            _familyTreeView.SelectedItemChanged += FamilyTreeView_SelectedItemChanged;
            WpfGrid.SetRow(_familyTreeView, 1);
            rootGrid.Children.Add(_familyTreeView);

            // Selected family label
            var selectedLabel = new TextBlock
            {
                Name = "SelectedLabel",
                Text = "Select a family from the tree above",
                Foreground = new SolidColorBrush(RevitSubtle),
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 0, 0, 12)
            };
            WpfGrid.SetRow(selectedLabel, 2);
            rootGrid.Children.Add(selectedLabel);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button
            {
                Content = "Add & Publish",
                Padding = new Thickness(15, 8, 15, 8),
                Background = new SolidColorBrush(RevitAccent),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0),
                IsEnabled = false,
                Tag = "OkButton"
            };
            okButton.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(SelectedFamilyName))
                {
                    DialogResult = true;
                    Close();
                }
            };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(15, 8, 15, 8),
                Background = new SolidColorBrush(WpfColor.FromRgb(100, 100, 104)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };
            buttonPanel.Children.Add(cancelButton);

            WpfGrid.SetRow(buttonPanel, 3);
            rootGrid.Children.Add(buttonPanel);

            Content = rootGrid;

            // Populate the tree
            PopulateTree(null);
        }

        private void PopulateTree(string searchFilter)
        {
            _familyTreeView.Items.Clear();
            bool hasFilter = !string.IsNullOrWhiteSpace(searchFilter) && searchFilter != "Search families...";
            string filter = hasFilter ? searchFilter.ToLowerInvariant() : null;

            foreach (var category in _familiesByCategory.OrderBy(c => c.Key))
            {
                // Filter families: exclude already monitored, apply search
                var families = category.Value
                    .Where(f => !_monitoredNames.Contains(f))
                    .Where(f => !hasFilter || f.ToLowerInvariant().Contains(filter) || category.Key.ToLowerInvariant().Contains(filter))
                    .OrderBy(f => f)
                    .ToList();

                if (families.Count == 0) continue;

                var categoryItem = new TreeViewItem
                {
                    Header = $"  {category.Key}  ({families.Count})",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(RevitText),
                    IsExpanded = hasFilter // Auto-expand when searching
                };

                foreach (var familyName in families)
                {
                    var familyItem = new TreeViewItem
                    {
                        Header = $"  {familyName}",
                        Tag = familyName,
                        FontWeight = FontWeights.Normal,
                        Foreground = new SolidColorBrush(RevitText)
                    };
                    categoryItem.Items.Add(familyItem);
                }

                _familyTreeView.Items.Add(categoryItem);
            }

            if (_familyTreeView.Items.Count == 0)
            {
                var emptyItem = new TreeViewItem
                {
                    Header = hasFilter ? "  No matching families" : "  All families are already monitored",
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(RevitSubtle),
                    Focusable = false
                };
                _familyTreeView.Items.Add(emptyItem);
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = _searchBox.Text;
            if (text == "Search families...") return;
            PopulateTree(text);
        }

        private void FamilyTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is string familyName)
            {
                SelectedFamilyName = familyName;

                // Update selected label
                var selectedLabel = FindChild<TextBlock>(this, "SelectedLabel");
                if (selectedLabel != null)
                {
                    selectedLabel.Text = $"Selected: {familyName}";
                    selectedLabel.FontStyle = FontStyles.Normal;
                    selectedLabel.Foreground = new SolidColorBrush(RevitText);
                }

                // Enable OK button
                var okButton = FindChild<Button>(this, "OkButton");
                if (okButton != null) okButton.IsEnabled = true;
            }
            else
            {
                SelectedFamilyName = null;

                var selectedLabel = FindChild<TextBlock>(this, "SelectedLabel");
                if (selectedLabel != null)
                {
                    selectedLabel.Text = "Select a family from the tree above";
                    selectedLabel.FontStyle = FontStyles.Italic;
                    selectedLabel.Foreground = new SolidColorBrush(RevitSubtle);
                }

                var okButton = FindChild<Button>(this, "OkButton");
                if (okButton != null) okButton.IsEnabled = false;
            }
        }

        private static T FindChild<T>(DependencyObject parent, string tag) where T : FrameworkElement
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                {
                    // Match by Name for TextBlock, by Tag for Button
                    if (typedChild is Button btn && btn.Tag?.ToString() == tag)
                        return typedChild;
                    if (typedChild.Name == tag)
                        return typedChild;
                }

                var found = FindChild<T>(child, tag);
                if (found != null) return found;
            }

            return null;
        }
    }

    #endregion
}