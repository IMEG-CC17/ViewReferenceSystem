// PortfolioManagePane.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ViewReferenceSystem.Core;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.Placement;
using Newtonsoft.Json;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfVisibility = System.Windows.Visibility;

namespace ViewReferenceSystem.UI
{
    public class PortfolioManagePane : IDockablePaneProvider
    {
        private static ExternalEvent _saveExternalEvent = null;
        private static SaveConfigurationHandler _saveHandler = null;
        private static ExternalEvent _placementEvent = null;
        private static PlacementHelper _placementHelper = null;
        private static ExternalEvent _nativeCalloutEvent = null;
        private static NativeCalloutHelper _nativeCalloutHelper = null;
        private static ExternalEvent _nativeSectionEvent = null;
        private static NativeSectionHelper _nativeSectionHelper = null;
        private static ExternalEvent _clearParametersEvent = null;
        private static ClearParametersHelper _clearParametersHelper = null;
        private static DockablePaneId _dockablePaneId = new DockablePaneId(new Guid("A7C4F0E3-1234-5678-90AB-CDEF12345678"));
        private static UIApplication _uiApplication;
        private static PortfolioManagePane _currentInstance;
        private PortfolioManagerControl _portfolioControl;

        public static DockablePaneId GetDockablePaneId() => _dockablePaneId;
        public static void SetUIApplication(UIApplication uiApp) => _uiApplication = uiApp;
        public static UIApplication GetUIApplication() => _uiApplication;
        public static SaveConfigurationHandler GetSaveHandler() => _saveHandler;
        public static ExternalEvent GetSaveExternalEvent() => _saveExternalEvent;
        public static PlacementHelper GetPlacementHelper() => _placementHelper;
        public static ExternalEvent GetPlacementEvent() => _placementEvent;
        public static NativeCalloutHelper GetNativeCalloutHelper() => _nativeCalloutHelper;
        public static ExternalEvent GetNativeCalloutEvent() => _nativeCalloutEvent;
        public static NativeSectionHelper GetNativeSectionHelper() => _nativeSectionHelper;
        public static ExternalEvent GetNativeSectionEvent() => _nativeSectionEvent;
        public static ClearParametersHelper GetClearParametersHelper() => _clearParametersHelper;
        public static ExternalEvent GetClearParametersEvent() => _clearParametersEvent;
        public static PortfolioManagePane GetCurrentInstance() => _currentInstance;

        public static bool IsProjectOffline(Document doc)
        {
            string portfolioPath = PortfolioSettings.GetJsonPath(doc);

            if (string.IsNullOrEmpty(portfolioPath))
            {
                return false;
            }

            if (FirebaseClient.IsFirebasePath(portfolioPath))
            {
                return false; // Firebase is always reachable from a file perspective
            }

            if (!File.Exists(portfolioPath))
            {
                return true;
            }

            return false;
        }

        public static void ShowPane()
        {
            try
            {
                DockablePane dockablePane = _uiApplication?.GetDockablePane(_dockablePaneId);
                if (dockablePane != null)
                {
                    if (!dockablePane.IsShown()) dockablePane.Show();
                    Document activeDoc = _uiApplication?.ActiveUIDocument?.Document;
                    if (activeDoc != null && _currentInstance?._portfolioControl != null)
                    {
                        _currentInstance._portfolioControl.SetCurrentDocument(activeDoc);
                        _currentInstance._portfolioControl.RefreshPortfolioData();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error showing pane: {ex.Message}");
            }
        }

        public static void ShowPane(UIApplication uiApp)
        {
            SetUIApplication(uiApp);
            ShowPane();
        }

        public static void OnDocumentChanged(Document doc)
        {
            try
            {
                if (_currentInstance?._portfolioControl != null)
                {
                    _currentInstance._portfolioControl.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _currentInstance._portfolioControl.SetCurrentDocument(doc);
                        _currentInstance._portfolioControl.RefreshPortfolioData();
                    }), DispatcherPriority.Normal);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in OnDocumentChanged: {ex.Message}");
            }
        }

        public static void OnViewChanged(View view)
        {
            try
            {
                if (_currentInstance?._portfolioControl != null)
                {
                    _currentInstance._portfolioControl.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _currentInstance._portfolioControl.UpdateButtonStates();
                    }), DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in OnViewChanged: {ex.Message}");
            }
        }

        public static void RefreshCurrentPane()
        {
            try
            {
                if (_currentInstance?._portfolioControl != null)
                {
                    _currentInstance._portfolioControl.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _currentInstance._portfolioControl.RefreshPortfolioData();
                        System.Diagnostics.Debug.WriteLine("✅ Portfolio pane refreshed from external call");
                    }), DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error refreshing pane: {ex.Message}");
            }
        }

        public static void InitializeExternalEvents(
            ExternalEvent saveEvent, SaveConfigurationHandler saveHandler,
            ExternalEvent placementEvent, PlacementHelper placementHelper,
            ExternalEvent nativeCalloutEvent, NativeCalloutHelper nativeCalloutHelper,
            ExternalEvent nativeSectionEvent, NativeSectionHelper nativeSectionHelper)
        {
            _saveExternalEvent = saveEvent;
            _saveHandler = saveHandler;
            _placementEvent = placementEvent;
            _placementHelper = placementHelper;
            _nativeCalloutEvent = nativeCalloutEvent;
            _nativeCalloutHelper = nativeCalloutHelper;
            _nativeSectionEvent = nativeSectionEvent;
            _nativeSectionHelper = nativeSectionHelper;

            if (_clearParametersHelper == null)
            {
                _clearParametersHelper = new ClearParametersHelper();
                _clearParametersEvent = ExternalEvent.Create(_clearParametersHelper);
            }
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            _portfolioControl = new PortfolioManagerControl();
            data.FrameworkElement = _portfolioControl;
            data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
            _currentInstance = this;
        }
    }

    public class PortfolioManagerControl : System.Windows.Controls.UserControl
    {
        private Document _currentDocument;
        private PortfolioSettings.Portfolio _portfolioData;
        private string _portfolioJsonPath;
        private bool _isInitialized = false;
        private bool _isOfflineMode = false;
        private TextBlock _statusText;
        private WpfTextBox _searchTextBox;
        private TreeView _viewsTreeView;
        private Button _refreshButton;
        private Button _createCalloutButton;
        private Button _createSectionButton;
        private Button _placeFamilyButton;
        private ViewInfo _selectedView = null;
        private HashSet<string> _expandedNodes = new HashSet<string>();

        public PortfolioManagerControl()
        {
            this.Background = new SolidColorBrush(WpfColor.FromRgb(239, 239, 242));
            InitializeComponent();
            _isInitialized = true;
        }

        private void InitializeComponent()
        {
            WpfGrid rootGrid = new WpfGrid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "No document loaded",
                Padding = new Thickness(10),
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            };
            WpfGrid.SetRow(_statusText, 0);
            rootGrid.Children.Add(_statusText);

            WpfGrid searchGrid = new WpfGrid { Margin = new Thickness(10, 5, 10, 5) };
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _searchTextBox = new WpfTextBox
            {
                Height = 30,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _searchTextBox.TextChanged += SearchTextBox_TextChanged;
            WpfGrid.SetColumn(_searchTextBox, 0);
            searchGrid.Children.Add(_searchTextBox);

            _refreshButton = new Button
            {
                Content = "Refresh",
                Width = 70,
                Height = 30,
                Margin = new Thickness(5, 0, 0, 0)
            };
            _refreshButton.Click += RefreshButton_Click;
            WpfGrid.SetColumn(_refreshButton, 2);
            searchGrid.Children.Add(_refreshButton);

            WpfGrid.SetRow(searchGrid, 1);
            rootGrid.Children.Add(searchGrid);

            _viewsTreeView = new TreeView
            {
                Margin = new Thickness(10, 5, 10, 5)
            };
            _viewsTreeView.SelectedItemChanged += ViewsTreeView_SelectedItemChanged;
            WpfGrid.SetRow(_viewsTreeView, 2);
            rootGrid.Children.Add(_viewsTreeView);

            WpfGrid buttonGrid = new WpfGrid { Margin = new Thickness(10, 5, 10, 10) };
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _createCalloutButton = new Button
            {
                Content = "Create Callout",
                Height = 35,
                Margin = new Thickness(0, 0, 5, 0),
                IsEnabled = false
            };
            _createCalloutButton.Click += CreateCalloutButton_Click;
            WpfGrid.SetColumn(_createCalloutButton, 0);
            buttonGrid.Children.Add(_createCalloutButton);

            _createSectionButton = new Button
            {
                Content = "Create Section",
                Height = 35,
                Margin = new Thickness(5, 0, 5, 0),
                IsEnabled = false
            };
            _createSectionButton.Click += CreateSectionButton_Click;
            WpfGrid.SetColumn(_createSectionButton, 1);
            buttonGrid.Children.Add(_createSectionButton);

            _placeFamilyButton = new Button
            {
                Content = "Place Family",
                Height = 35,
                Margin = new Thickness(5, 0, 0, 0),
                IsEnabled = false
            };
            _placeFamilyButton.Click += PlaceFamilyButton_Click;
            WpfGrid.SetColumn(_placeFamilyButton, 2);
            buttonGrid.Children.Add(_placeFamilyButton);

            WpfGrid.SetRow(buttonGrid, 3);
            rootGrid.Children.Add(buttonGrid);

            this.Content = rootGrid;
        }

        public void SetCurrentDocument(Document doc)
        {
            _currentDocument = doc;

            if (_currentDocument == null)
            {
                UpdateStatus("No document loaded");
                ClearViewsDisplay();
                UpdateButtonStates();
                return;
            }

            UpdateStatus($"📄 {_currentDocument.Title}");
        }

        public void RefreshPortfolioData()
        {
            if (!_isInitialized || _currentDocument == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ Skipping refresh - not initialized or no document");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"🔄 Refreshing portfolio data for: {_currentDocument.Title}");

                _portfolioJsonPath = PortfolioSettings.GetJsonPath(_currentDocument);

                if (string.IsNullOrEmpty(_portfolioJsonPath))
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ No portfolio path configured");
                    UpdateStatus("⚠️ No portfolio configured - Use 'Portfolio Setup' button on the ribbon");

                    _portfolioData = null;
                    _portfolioJsonPath = null;

                    ClearViewsDisplay();
                    _isOfflineMode = false;
                    UpdateButtonStates();
                    return;
                }

                if (!FirebaseClient.IsFirebasePath(_portfolioJsonPath) && !File.Exists(_portfolioJsonPath))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Portfolio file not found: {_portfolioJsonPath}");
                    EnterOfflineMode();
                    return;
                }

                _isOfflineMode = false;

                _portfolioData = PortfolioSettings.LoadPortfolioFromFile(_portfolioJsonPath);

                string currentProjectName = PortfolioSettings.GetProjectName(_currentDocument);
                var currentProjectInfo = _portfolioData?.ProjectInfos?.FirstOrDefault(p =>
                    string.Equals(p.ProjectName, currentProjectName, StringComparison.OrdinalIgnoreCase));

                if (currentProjectInfo != null &&
                    string.Equals(currentProjectInfo.Status, "removed", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ This project has been removed from the portfolio - clearing parameters");

                    using (Transaction trans = new Transaction(_currentDocument, "Clear Portfolio Parameters"))
                    {
                        trans.Start();
                        PortfolioSettings.SetProjectType(_currentDocument, "");
                        PortfolioSettings.SetJsonPath(_currentDocument, "");
                        PortfolioSettings.SetPortfolioGuid(_currentDocument, "");
                        trans.Commit();
                    }

                    _portfolioData = null;
                    _portfolioJsonPath = null;
                    UpdateStatus("Project removed from portfolio");
                    ClearViewsDisplay();
                    UpdateButtonStates();
                    return;
                }
                if (_portfolioData?.Views == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Portfolio data is null or empty");
                    UpdateStatus("⚠️ Portfolio file is empty or invalid");
                    ClearViewsDisplay();
                    UpdateButtonStates();
                    return;
                }

                var activeProjects = (_portfolioData.ProjectInfos ?? new List<PortfolioSettings.PortfolioProject>())
                    .Where(p => !string.Equals(p.Status, "removed", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var activeProjectNames = activeProjects
                    .Select(p => p.ProjectName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                int viewCount = _portfolioData.Views?
                    .Count(v => activeProjectNames.Contains(v.SourceProjectName ?? "")) ?? 0;
                int projectCount = activeProjects.Count;
                string portfolioName = _portfolioData.PortfolioName ?? "Unnamed Portfolio";
                string dataVersion = _portfolioData.DataVersion ?? "Unknown";

                UpdateStatus($"📊 {portfolioName} | {viewCount} views | {projectCount} projects | v{dataVersion}");

                // Force UI update on dispatcher thread
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    DisplayViews();
                    UpdateButtonStates();
                    _viewsTreeView.UpdateLayout();
                }), DispatcherPriority.Render);

                System.Diagnostics.Debug.WriteLine($"✅ Portfolio loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Unexpected error loading portfolio: {ex.Message}");
                UpdateStatus($"Error loading portfolio: {ex.Message}");

                _portfolioData = null;

                ClearViewsDisplay();
            }
        }

        private void EnterOfflineMode()
        {
            _isOfflineMode = true;
            UpdateStatus("⚠️ OFFLINE MODE - Portfolio file not accessible");
            ClearViewsDisplay();
            UpdateButtonStates();

            // Show offline info with actionable buttons in the tree view area
            var offlinePanel = new StackPanel
            {
                Margin = new Thickness(10),
                Orientation = Orientation.Vertical
            };

            // Offline icon and message
            offlinePanel.Children.Add(new TextBlock
            {
                Text = "⚠️ Portfolio Offline",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            offlinePanel.Children.Add(new TextBlock
            {
                Text = "The portfolio file cannot be found. This usually means the network drive is not connected.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Show the path that's not accessible
            if (!string.IsNullOrEmpty(_portfolioJsonPath))
            {
                offlinePanel.Children.Add(new TextBlock
                {
                    Text = "Path:",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 2)
                });

                offlinePanel.Children.Add(new TextBlock
                {
                    Text = _portfolioJsonPath,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(WpfColors.Gray),
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 15)
                });
            }

            // Refresh button
            var refreshButton = new Button
            {
                Content = "🔄  Refresh Connection",
                Height = 35,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            refreshButton.Click += (s, e) =>
            {
                _isOfflineMode = false;
                RefreshPortfolioData();
            };
            offlinePanel.Children.Add(refreshButton);

            // Locate file button
            var locateButton = new Button
            {
                Content = "📂  Locate File Manually",
                Height = 35,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            locateButton.Click += (s, e) =>
            {
                LocatePortfolioFile();
            };
            offlinePanel.Children.Add(locateButton);

            // Remove connection button
            var removeButton = new Button
            {
                Content = "❌  Remove Connection",
                Height = 35,
                Margin = new Thickness(0, 0, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            removeButton.Click += (s, e) =>
            {
                RemovePortfolioConnection();
            };
            offlinePanel.Children.Add(removeButton);

            // Add the panel as a tree view item
            TreeViewItem offlineItem = new TreeViewItem
            {
                Header = offlinePanel,
                IsExpanded = true,
                Focusable = false
            };
            _viewsTreeView.Items.Add(offlineItem);
        }

        private void LocatePortfolioFile()
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Locate Portfolio JSON File",
                    Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    InitialDirectory = Path.GetDirectoryName(_portfolioJsonPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                bool? result = openFileDialog.ShowDialog();

                if (result == true)
                {
                    string newPath = openFileDialog.FileName;
                    System.Diagnostics.Debug.WriteLine($"✅ User selected new portfolio path: {newPath}");

                    if (UpdatePortfolioPath(newPath))
                    {
                        _portfolioJsonPath = newPath;
                        RefreshPortfolioData();
                    }
                    else
                    {
                        TaskDialog.Show("Error", "Failed to update portfolio path in project parameters.");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ User cancelled file selection");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error locating file: {ex.Message}");
                TaskDialog.Show("Error", $"Error locating portfolio file:\n\n{ex.Message}");
            }
        }

        private bool UpdatePortfolioPath(string newPath)
        {
            if (_currentDocument == null) return false;

            try
            {
                using (Transaction trans = new Transaction(_currentDocument, "Update Portfolio Path"))
                {
                    trans.Start();

                    Parameter param = _currentDocument.ProjectInformation.LookupParameter("ViewReference_JsonPath");
                    if (param != null && !param.IsReadOnly)
                    {
                        param.Set(newPath);
                        System.Diagnostics.Debug.WriteLine($"✅ Portfolio path updated to: {newPath}");
                        trans.Commit();
                        return true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("❌ JsonPath parameter not found or read-only");
                        trans.RollBack();
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error updating portfolio path: {ex.Message}");
                return false;
            }
        }

        private void RemovePortfolioConnection()
        {
            try
            {
                TaskDialog confirmDialog = new TaskDialog("Remove Portfolio Connection");
                confirmDialog.MainInstruction = "Remove this project from the portfolio?";
                confirmDialog.MainContent = "This will clear all portfolio parameters from this project.\n\nThis action cannot be undone.";
                confirmDialog.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                confirmDialog.DefaultButton = TaskDialogResult.No;

                if (confirmDialog.Show() == TaskDialogResult.Yes)
                {
                    using (Transaction trans = new Transaction(_currentDocument, "Clear Portfolio Parameters"))
                    {
                        trans.Start();
                        PortfolioSettings.ClearProjectParameters(_currentDocument);
                        trans.Commit();
                    }

                    UpdateStatus("Portfolio connection removed");
                    ClearViewsDisplay();
                    _isOfflineMode = false;
                    _portfolioJsonPath = null;
                    _portfolioData = null;
                    UpdateButtonStates();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error removing portfolio connection: {ex.Message}");
                TaskDialog.Show("Error", $"Error removing portfolio connection:\n\n{ex.Message}");
            }
        }

        private string GetProjectDisplayName(string projectName)
        {
            try
            {
                if (_portfolioData?.ProjectInfos == null)
                    return projectName;

                var projectInfo = _portfolioData.ProjectInfos.FirstOrDefault(p =>
                    string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));

                if (projectInfo != null && !string.IsNullOrEmpty(projectInfo.Nickname))
                {
                    return projectInfo.Nickname;
                }

                return projectName;
            }
            catch
            {
                return projectName;
            }
        }

        private int GetProjectSortOrder(string projectName, string currentProjectName)
        {
            var projectInfo = _portfolioData?.ProjectInfos?.FirstOrDefault(p =>
                string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));

            if (projectInfo?.IsTypicalDetailsAuthority == true)
                return 0;

            if (string.Equals(projectName, currentProjectName, StringComparison.OrdinalIgnoreCase))
                return 1;

            return 2;
        }

        private bool IsTypicalDetailsAuthority(string projectName)
        {
            var projectInfo = _portfolioData?.ProjectInfos?.FirstOrDefault(p =>
                string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
            return projectInfo?.IsTypicalDetailsAuthority == true;
        }

        private void SaveExpandedState()
        {
            _expandedNodes.Clear();
            foreach (TreeViewItem item in _viewsTreeView.Items.OfType<TreeViewItem>())
            {
                SaveExpandedStateRecursive(item, "");
            }
        }

        private void SaveExpandedStateRecursive(TreeViewItem item, string path)
        {
            string currentPath = path + "|" + item.Header?.ToString();

            if (item.IsExpanded)
            {
                _expandedNodes.Add(currentPath);
            }

            foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            {
                SaveExpandedStateRecursive(child, currentPath);
            }
        }

        private void RestoreExpandedState()
        {
            foreach (TreeViewItem item in _viewsTreeView.Items.OfType<TreeViewItem>())
            {
                RestoreExpandedStateRecursive(item, "");
            }
        }

        private void RestoreExpandedStateRecursive(TreeViewItem item, string path)
        {
            string currentPath = path + "|" + item.Header?.ToString();

            if (_expandedNodes.Contains(currentPath))
            {
                item.IsExpanded = true;
            }

            foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
            {
                RestoreExpandedStateRecursive(child, currentPath);
            }
        }

        private void DisplayViews()
        {
            SaveExpandedState();

            _viewsTreeView.Items.Clear();

            if (_portfolioData?.Views == null || !_portfolioData.Views.Any())
            {
                TreeViewItem noViewsItem = new TreeViewItem
                {
                    Header = "No views available",
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(WpfColors.Gray)
                };
                _viewsTreeView.Items.Add(noViewsItem);
                return;
            }

            string currentProjectName = PortfolioSettings.GetProjectName(_currentDocument);

            var activeProjectNames = _portfolioData.ProjectInfos?
                .Where(p => !string.Equals(p.Status, "removed", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.ProjectName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var groupedViews = _portfolioData.Views
                .Where(v => activeProjectNames.Contains(v.SourceProjectName ?? ""))
                .GroupBy(v => v.SourceProjectName ?? "Unknown Project")
                .OrderBy(g => GetProjectSortOrder(g.Key, currentProjectName))
                .ThenBy(g => GetProjectDisplayName(g.Key));

            foreach (var projectGroup in groupedViews)
            {
                bool isCurrentProject = string.Equals(projectGroup.Key, currentProjectName, StringComparison.OrdinalIgnoreCase);
                bool isAuthority = IsTypicalDetailsAuthority(projectGroup.Key);

                string header;
                if (isAuthority)
                    header = $"⭐ {GetProjectDisplayName(projectGroup.Key)} (Typical Details)";
                else if (isCurrentProject)
                    header = $"📘 {GetProjectDisplayName(projectGroup.Key)} (Current Project)";
                else
                    header = $"📗 {GetProjectDisplayName(projectGroup.Key)}";

                TreeViewItem projectItem = new TreeViewItem
                {
                    Header = header,
                    FontWeight = (isCurrentProject || isAuthority) ? FontWeights.Bold : FontWeights.Normal,
                    IsExpanded = isAuthority
                };

                var sheetGroups = projectGroup
                    .GroupBy(v => v.SheetNumber ?? "No Sheet")
                    .OrderBy(g => g.Key);

                foreach (var sheetGroup in sheetGroups)
                {
                    TreeViewItem sheetItem = new TreeViewItem
                    {
                        Header = $"📄 Sheet {sheetGroup.Key}",
                        IsExpanded = false
                    };

                    var sortedViews = sheetGroup.OrderBy(v => v.DetailNumber ?? "");

                    foreach (var view in sortedViews)
                    {
                        string viewHeader = $"{view.DetailNumber ?? "?"} - {view.ViewName ?? "Unnamed"}";
                        if (!string.IsNullOrEmpty(view.TopNote))
                        {
                            viewHeader += $" ({view.TopNote})";
                        }

                        TreeViewItem viewItem = new TreeViewItem
                        {
                            Header = viewHeader,
                            Tag = view
                        };

                        viewItem.MouseDoubleClick += ViewItem_MouseDoubleClick;
                        sheetItem.Items.Add(viewItem);
                    }

                    projectItem.Items.Add(sheetItem);
                }

                _viewsTreeView.Items.Add(projectItem);
            }

            RestoreExpandedState();
        }

        private void ClearViewsDisplay()
        {
            _viewsTreeView.Items.Clear();
            _selectedView = null;
        }

        private void UpdateStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.Text = message;
            }
        }

        public void UpdateButtonStates()
        {
            bool hasDocument = _currentDocument != null;
            bool hasSelection = _selectedView != null;
            bool canPlace = hasDocument && hasSelection && !_isOfflineMode;

            if (_createCalloutButton != null) _createCalloutButton.IsEnabled = canPlace;
            if (_createSectionButton != null) _createSectionButton.IsEnabled = canPlace;
            if (_placeFamilyButton != null) _placeFamilyButton.IsEnabled = canPlace;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = _searchTextBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                DisplayViews();
                return;
            }

            _viewsTreeView.Items.Clear();

            if (_portfolioData?.Views == null || !_portfolioData.Views.Any())
            {
                return;
            }

            var filteredViews = _portfolioData.Views.Where(v =>
                (v.ViewName?.ToLower().Contains(searchText) ?? false) ||
                (v.SheetNumber?.ToLower().Contains(searchText) ?? false) ||
                (v.DetailNumber?.ToLower().Contains(searchText) ?? false) ||
                (v.TopNote?.ToLower().Contains(searchText) ?? false) ||
                (v.SourceProjectName?.ToLower().Contains(searchText) ?? false)
            ).ToList();

            if (!filteredViews.Any())
            {
                TreeViewItem noResultsItem = new TreeViewItem
                {
                    Header = $"No results for '{_searchTextBox.Text}'",
                    FontStyle = FontStyles.Italic,
                    Foreground = new SolidColorBrush(WpfColors.Gray)
                };
                _viewsTreeView.Items.Add(noResultsItem);
                return;
            }

            string currentProjectName = PortfolioSettings.GetProjectName(_currentDocument);

            var groupedViews = filteredViews
                .GroupBy(v => v.SourceProjectName ?? "Unknown Project")
                .OrderBy(g => GetProjectSortOrder(g.Key, currentProjectName))
                .ThenBy(g => GetProjectDisplayName(g.Key));

            foreach (var projectGroup in groupedViews)
            {
                bool isCurrentProject = string.Equals(projectGroup.Key, currentProjectName, StringComparison.OrdinalIgnoreCase);
                bool isAuthority = IsTypicalDetailsAuthority(projectGroup.Key);

                string header;
                if (isAuthority)
                    header = $"⭐ {GetProjectDisplayName(projectGroup.Key)} (Typical Details)";
                else if (isCurrentProject)
                    header = $"📘 {GetProjectDisplayName(projectGroup.Key)} (Current Project)";
                else
                    header = $"📗 {GetProjectDisplayName(projectGroup.Key)}";

                TreeViewItem projectItem = new TreeViewItem
                {
                    Header = header,
                    FontWeight = (isCurrentProject || isAuthority) ? FontWeights.Bold : FontWeights.Normal,
                    IsExpanded = true
                };

                var sheetGroups = projectGroup
                    .GroupBy(v => v.SheetNumber ?? "No Sheet")
                    .OrderBy(g => g.Key);

                foreach (var sheetGroup in sheetGroups)
                {
                    TreeViewItem sheetItem = new TreeViewItem
                    {
                        Header = $"📄 Sheet {sheetGroup.Key}",
                        IsExpanded = true
                    };

                    var sortedViews = sheetGroup.OrderBy(v => v.DetailNumber ?? "");

                    foreach (var view in sortedViews)
                    {
                        string viewHeader = $"{view.DetailNumber ?? "?"} - {view.ViewName ?? "Unnamed"}";
                        if (!string.IsNullOrEmpty(view.TopNote))
                        {
                            viewHeader += $" ({view.TopNote})";
                        }

                        TreeViewItem viewItem = new TreeViewItem
                        {
                            Header = viewHeader,
                            Tag = view
                        };

                        viewItem.MouseDoubleClick += ViewItem_MouseDoubleClick;
                        sheetItem.Items.Add(viewItem);
                    }

                    projectItem.Items.Add(sheetItem);
                }

                _viewsTreeView.Items.Add(projectItem);
            }
        }

        private void ViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem treeViewItem && treeViewItem.Tag is ViewInfo viewInfo)
            {
                e.Handled = true;

                EditTopNoteDialog dialog = new EditTopNoteDialog(viewInfo.TopNote);
                bool? result = dialog.ShowDialog();

                if (result == true && dialog.WasSaved)
                {
                    viewInfo.TopNote = dialog.TopNote;
                    SaveTopNoteToPortfolio(viewInfo);
                    RefreshPortfolioData();
                    System.Diagnostics.Debug.WriteLine($"✅ Top note updated: {viewInfo.ViewName} = '{dialog.TopNote}'");
                }
            }
        }

        private void SaveTopNoteToPortfolio(ViewInfo viewInfo)
        {
            try
            {
                if (string.IsNullOrEmpty(_portfolioJsonPath))
                    return;

                // Re-read fresh JSON to minimize race conditions with other users
                if (!FirebaseClient.IsFirebasePath(_portfolioJsonPath) && !File.Exists(_portfolioJsonPath))
                {
                    TaskDialog.Show("Error", "Portfolio file not found. You may be offline.");
                    return;
                }

                var freshData = PortfolioSettings.LoadPortfolioFromFile(_portfolioJsonPath);

                if (freshData?.Views == null)
                    return;

                var existingView = freshData.Views.FirstOrDefault(v =>
                    v.ViewId == viewInfo.ViewId &&
                    string.Equals(v.SourceProjectName, viewInfo.SourceProjectName, StringComparison.OrdinalIgnoreCase));

                if (existingView != null)
                {
                    existingView.TopNote = viewInfo.TopNote;
                    PortfolioSettings.SavePortfolioToFile(freshData, _portfolioJsonPath);

                    // Also update local cache so pane stays in sync
                    _portfolioData = freshData;

                    System.Diagnostics.Debug.WriteLine($"✅ Portfolio JSON updated with new top note");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ View {viewInfo.ViewId} not found in fresh JSON — may need to sync first");
                    TaskDialog.Show("Top Note", "This view was not found in the portfolio data.\nTry syncing to central first.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error saving top note to portfolio: {ex.Message}");
                TaskDialog.Show("Error", $"Error saving top note:\n\n{ex.Message}");
            }
        }

        private void ViewsTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem selectedItem && selectedItem.Tag is ViewInfo viewInfo)
            {
                _selectedView = viewInfo;
                System.Diagnostics.Debug.WriteLine($"✅ Selected view: {viewInfo.ViewName} from {viewInfo.SourceProjectName}");
            }
            else
            {
                _selectedView = null;
            }

            UpdateButtonStates();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Fallback: if no document set, try to pick up the active one
                if (_currentDocument == null || !_currentDocument.IsValidObject)
                {
                    var uiApp = PortfolioManagePane.GetUIApplication();
                    var activeDoc = uiApp?.ActiveUIDocument?.Document;
                    if (activeDoc != null && !activeDoc.IsFamilyDocument)
                    {
                        SetCurrentDocument(activeDoc);
                    }
                }

                RefreshPortfolioData();
                System.Diagnostics.Debug.WriteLine("✅ Manual refresh completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error refreshing portfolio: {ex.Message}");
                TaskDialog.Show("Error", $"Error refreshing portfolio:\n\n{ex.Message}");
            }
        }

        private void CreateCalloutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isOfflineMode)
            {
                TaskDialog.Show("Offline Mode", "Cannot place references while in offline mode.\n\nPlease restore portfolio connection first.");
                return;
            }

            CreateNativeCalloutReference("Callout");
        }

        private void CreateSectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isOfflineMode)
            {
                TaskDialog.Show("Offline Mode", "Cannot place references while in offline mode.\n\nPlease restore portfolio connection first.");
                return;
            }

            CreateNativeCalloutReference("Section");
        }

        private void PlaceFamilyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isOfflineMode)
            {
                TaskDialog.Show("Offline Mode", "Cannot place references while in offline mode.\n\nPlease restore portfolio connection first.");
                return;
            }

            CreateNativeCalloutReference("Family");
        }

        private void CreateNativeCalloutReference(string referenceType)
        {
            if (_selectedView == null)
            {
                TaskDialog.Show("Error", "No view selected.\n\nPlease select a view from the list first.");
                return;
            }

            if (referenceType == "Callout")
            {
                var helper = PortfolioManagePane.GetNativeCalloutHelper();
                var externalEvent = PortfolioManagePane.GetNativeCalloutEvent();
                if (_selectedView != null)
                {
                    helper.SetViewInfo(_selectedView);
                }
                externalEvent.Raise();
            }
            else if (referenceType == "Section")
            {
                var helper = PortfolioManagePane.GetNativeSectionHelper();
                var externalEvent = PortfolioManagePane.GetNativeSectionEvent();
                if (_selectedView != null)
                {
                    helper.SetViewInfo(_selectedView);
                }
                externalEvent.Raise();
            }
            else if (referenceType == "Family")
            {
                var helper = PortfolioManagePane.GetPlacementHelper();
                var externalEvent = PortfolioManagePane.GetPlacementEvent();
                helper.SetViewInfo(_selectedView);
                externalEvent.Raise();
            }
        }
    }
}