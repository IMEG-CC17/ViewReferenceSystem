// PortfolioSetupWindow.cs

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ViewReferenceSystem.Core;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.UI;
using RevitView = Autodesk.Revit.DB.View;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ViewReferenceSystem.UI
{
    public class PortfolioSetupWindow : Window
    {
        #region Private Fields

        private Document _currentDocument;
        private PortfolioSettings.Portfolio _portfolioData;
        private string _portfolioFilePath;
        private Action _onPortfolioChanged;

        private WpfTextBox _portfolioNameTextBox;
        private TextBlock _portfolioFilePathDisplay;
        private Button _createNewButton;
        private Button _joinExistingButton;
        private Button _loadTopNotesButton;
        private StackPanel _projectsPanel;
        private Button _closeButton;
        private TextBlock _statusText;

        public bool WasSetupCompleted { get; private set; } = false;
        public bool WasSetupSuccessful { get; private set; } = false;

        // Revit-style colors
        private static readonly WpfColor RevitBackground = WpfColor.FromRgb(241, 241, 241);
        private static readonly WpfColor RevitBorder = WpfColor.FromRgb(180, 180, 180);
        private static readonly WpfColor RevitButtonFace = WpfColor.FromRgb(225, 225, 225);
        private static readonly WpfColor RevitButtonBorder = WpfColor.FromRgb(150, 150, 150);
        private static readonly WpfColor RevitText = WpfColor.FromRgb(30, 30, 30);
        private static readonly WpfColor RevitTextMuted = WpfColor.FromRgb(100, 100, 100);
        private static readonly WpfColor RevitRowHover = WpfColor.FromRgb(229, 243, 255);
        private static readonly WpfColor RevitAuthorityText = WpfColor.FromRgb(80, 80, 80);

        #endregion

        public string GetPortfolioFilePath()
        {
            return _portfolioFilePath;
        }

        #region Constructor & Initialization

        public PortfolioSetupWindow(Document currentDocument, Action onPortfolioChanged = null)
        {
            _currentDocument = currentDocument ?? throw new ArgumentNullException(nameof(currentDocument));
            _onPortfolioChanged = onPortfolioChanged;

            InitializeComponent();
            LoadExistingPortfolioData();
        }

        private void InitializeComponent()
        {
            Title = "Portfolio Setup";
            Width = 550;
            Height = 450;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 450;
            MinHeight = 350;
            Background = new SolidColorBrush(RevitBackground);

            var mainGrid = new WpfGrid
            {
                Margin = new Thickness(10)
            };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Portfolio name
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // File path
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Projects list
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status + Close

            // Portfolio Name Row
            var namePanel = new WpfGrid { Margin = new Thickness(0, 0, 0, 6) };
            namePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            namePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var portfolioNameLabel = new TextBlock
            {
                Text = "Portfolio:",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(RevitText)
            };
            WpfGrid.SetColumn(portfolioNameLabel, 0);
            namePanel.Children.Add(portfolioNameLabel);

            _portfolioNameTextBox = new WpfTextBox
            {
                IsReadOnly = true,
                Background = new SolidColorBrush(WpfColors.White),
                BorderBrush = new SolidColorBrush(RevitBorder),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            WpfGrid.SetColumn(_portfolioNameTextBox, 1);
            namePanel.Children.Add(_portfolioNameTextBox);

            WpfGrid.SetRow(namePanel, 0);
            mainGrid.Children.Add(namePanel);

            // File Path Row
            var pathPanel = new WpfGrid { Margin = new Thickness(0, 0, 0, 8) };
            pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var filePathLabel = new TextBlock
            {
                Text = "Location:",
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = new SolidColorBrush(RevitText)
            };
            WpfGrid.SetColumn(filePathLabel, 0);
            pathPanel.Children.Add(filePathLabel);

            _portfolioFilePathDisplay = new TextBlock
            {
                Text = "Not configured",
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(RevitTextMuted),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            WpfGrid.SetColumn(_portfolioFilePathDisplay, 1);
            pathPanel.Children.Add(_portfolioFilePathDisplay);

            WpfGrid.SetRow(pathPanel, 1);
            mainGrid.Children.Add(pathPanel);

            // Action Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };

            _createNewButton = CreateRevitButton("Create New Portfolio");
            _createNewButton.Click += CreateNewButton_Click;
            _createNewButton.Margin = new Thickness(0, 0, 6, 0);
            buttonPanel.Children.Add(_createNewButton);

            _joinExistingButton = CreateRevitButton("Join Existing Portfolio");
            _joinExistingButton.Click += JoinExistingButton_Click;
            _joinExistingButton.Margin = new Thickness(0, 0, 6, 0);
            buttonPanel.Children.Add(_joinExistingButton);

            _loadTopNotesButton = CreateRevitButton("Load Top Notes");
            _loadTopNotesButton.Click += LoadTopNotesButton_Click;
            _loadTopNotesButton.ToolTip = "Load Top Notes from current Revit project into portfolio (one-time import)";
            _loadTopNotesButton.Margin = new Thickness(0, 0, 6, 0);
            buttonPanel.Children.Add(_loadTopNotesButton);



            WpfGrid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            // Projects List with header
            var projectsContainer = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };

            var projectsHeader = new TextBlock
            {
                Text = "Projects in Portfolio:",
                Foreground = new SolidColorBrush(RevitText),
                Margin = new Thickness(0, 0, 0, 4)
            };
            DockPanel.SetDock(projectsHeader, Dock.Top);
            projectsContainer.Children.Add(projectsHeader);

            var projectsBorder = new Border
            {
                BorderBrush = new SolidColorBrush(RevitBorder),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(WpfColors.White)
            };

            var projectsScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0)
            };

            _projectsPanel = new StackPanel();
            projectsScroll.Content = _projectsPanel;
            projectsBorder.Child = projectsScroll;
            projectsContainer.Children.Add(projectsBorder);

            WpfGrid.SetRow(projectsContainer, 3);
            mainGrid.Children.Add(projectsContainer);

            // Bottom Row: Status + Close
            var bottomPanel = new WpfGrid { Margin = new Thickness(0, 8, 0, 0) };
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(RevitTextMuted),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            WpfGrid.SetColumn(_statusText, 0);
            bottomPanel.Children.Add(_statusText);

            _closeButton = CreateRevitButton("Close");
            _closeButton.Click += CloseButton_Click;
            _closeButton.MinWidth = 75;
            WpfGrid.SetColumn(_closeButton, 1);
            bottomPanel.Children.Add(_closeButton);

            WpfGrid.SetRow(bottomPanel, 4);
            mainGrid.Children.Add(bottomPanel);

            Content = mainGrid;
        }

        private Button CreateRevitButton(string text)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(12, 4, 12, 4),
                Background = new SolidColorBrush(RevitButtonFace),
                BorderBrush = new SolidColorBrush(RevitButtonBorder),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(RevitText),
                Cursor = Cursors.Hand
            };
            return button;
        }

        private Button CreateSmallRevitButton(string text)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(8, 2, 8, 2),
                Background = new SolidColorBrush(RevitButtonFace),
                BorderBrush = new SolidColorBrush(RevitButtonBorder),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(RevitText),
                Cursor = Cursors.Hand,
                FontSize = 11
            };
            return button;
        }

        #endregion

        #region Load Existing Data

        private void LoadExistingPortfolioData()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔍 LoadExistingPortfolioData: Starting...");

                string jsonPath = PortfolioSettings.GetJsonPath(_currentDocument);

                if (string.IsNullOrEmpty(jsonPath))
                {
                    System.Diagnostics.Debug.WriteLine("   No existing portfolio found");
                    UpdateStatus("No portfolio configured");
                    return;
                }

                // Firebase path
                if (FirebaseClient.IsFirebasePath(jsonPath))
                {
                    System.Diagnostics.Debug.WriteLine($"   Found Firebase portfolio at: {jsonPath}");
                    _portfolioData = PortfolioSettings.LoadPortfolioFromFile(jsonPath);

                    if (_portfolioData != null)
                    {
                        _portfolioFilePath = jsonPath;
                        _portfolioNameTextBox.Text = _portfolioData.PortfolioName;
                        _portfolioFilePathDisplay.Text = $"Firebase: {jsonPath}";
                        _portfolioFilePathDisplay.FontStyle = FontStyles.Normal;
                        _portfolioFilePathDisplay.Foreground = new SolidColorBrush(RevitText);

                        DisplayProjects();
                        UpdateStatus("Loaded existing Firebase portfolio");
                        System.Diagnostics.Debug.WriteLine($"   ✅ Firebase portfolio loaded: {_portfolioData.PortfolioName}");
                    }
                    return;
                }

                // Local file path
                if (File.Exists(jsonPath))
                {
                    System.Diagnostics.Debug.WriteLine($"   Found existing portfolio at: {jsonPath}");

                    string json = File.ReadAllText(jsonPath);
                    _portfolioData = JsonConvert.DeserializeObject<PortfolioSettings.Portfolio>(json);

                    if (_portfolioData != null)
                    {
                        _portfolioFilePath = jsonPath;

                        _portfolioNameTextBox.Text = _portfolioData.PortfolioName;
                        _portfolioFilePathDisplay.Text = _portfolioFilePath;
                        _portfolioFilePathDisplay.FontStyle = FontStyles.Normal;
                        _portfolioFilePathDisplay.Foreground = new SolidColorBrush(RevitText);

                        DisplayProjects();
                        UpdateStatus("Loaded existing portfolio configuration");
                        System.Diagnostics.Debug.WriteLine($"   ✅ Portfolio loaded: {_portfolioData.PortfolioName}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("   No existing portfolio found");
                    UpdateStatus("No portfolio configured");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"   ⚠️ Error loading portfolio: {ex.Message}");
                UpdateStatus("Error loading portfolio configuration");
            }
        }

        #endregion

        #region Portfolio Operations

        private void CreateNewPortfolio()
        {
            try
            {
                // Show Firebase create dialog
                var dialog = new FirebasePortfolioNameDialog(isCreate: true);
                if (dialog.ShowDialog() != true)
                    return;

                string firebasePath = dialog.FirebasePath;
                string portfolioName = firebasePath.Split('/').Last();

                // Check if path already exists
                UpdateStatus("Checking Firebase...");
                if (FirebaseClient.PathExists(firebasePath))
                {
                    ShowError("Portfolio Already Exists",
                        $"A portfolio already exists at:\n{firebasePath}\n\nChoose a different portfolio name.");
                    return;
                }

                string currentProjectName = PortfolioSettings.GetProjectName(_currentDocument);
                string currentProjectGuid = PortfolioSettings.GetProjectGuid(_currentDocument);

                // Upload seed family to Firebase Storage
                string addinFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string seedFamilyPath = Path.Combine(addinFolder, PortfolioSettings.DEFAULT_MONITORED_FAMILY_FILE);
                DateTime? seedPublishedDate = null;

                if (File.Exists(seedFamilyPath))
                {
                    UpdateStatus("Uploading seed family to Firebase...");
                    FirebaseClient.UploadFamily(seedFamilyPath, PortfolioSettings.DEFAULT_MONITORED_FAMILY_FILE, firebasePath);
                    seedPublishedDate = DateTime.Now;
                    System.Diagnostics.Debug.WriteLine($"📦 Seed family uploaded to Firebase Storage");

                    // Also load the seed family into the current project
                    UpdateStatus("Loading family into project...");
                    LoadFamilyIntoProject(seedFamilyPath);
                    System.Diagnostics.Debug.WriteLine($"📦 Seed family loaded into current project");
                }

                // Create portfolio data
                _portfolioData = new PortfolioSettings.Portfolio
                {
                    PortfolioGuid = Guid.NewGuid().ToString(),
                    PortfolioName = portfolioName,
                    DataVersion = "3.0",
                    CreatedDate = DateTime.Now,
                    LastUpdated = DateTime.Now,
                    MonitoredFamilies = new List<PortfolioSettings.MonitoredFamily>
                    {
                        new PortfolioSettings.MonitoredFamily
                        {
                            FamilyName = PortfolioSettings.DEFAULT_MONITORED_FAMILY,
                            FileName = PortfolioSettings.DEFAULT_MONITORED_FAMILY_FILE,
                            LastPublished = seedPublishedDate,
                            PublishedByProject = seedPublishedDate.HasValue ? currentProjectName : null
                        }
                    },
                    ProjectInfos = new List<PortfolioSettings.PortfolioProject>
                    {
                        new PortfolioSettings.PortfolioProject
                        {
                            ProjectName = currentProjectName,
                            ProjectGuid = currentProjectGuid,
                            Nickname = currentProjectName,
                            IsTypicalDetailsAuthority = true,
                            LastSync = DateTime.Now,
                            Status = "active",
                            IsMigrated = true,
                            FamilyUpdateStatus = new Dictionary<string, bool>
                            {
                                { PortfolioSettings.DEFAULT_MONITORED_FAMILY, true }
                            }
                        }
                    },
                    Views = new List<ViewInfo>()
                };

                _portfolioFilePath = firebasePath;
                SavePortfolioAndCloseWindow();
            }
            catch (Exception ex)
            {
                ShowError("Error creating portfolio", ex.Message);
            }
        }

        private void JoinExistingPortfolio(string portfolioFilePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🤝 Joining existing portfolio: {portfolioFilePath}");

                bool isFirebase = FirebaseClient.IsFirebasePath(portfolioFilePath);

                // Validate path exists
                if (!isFirebase && !File.Exists(portfolioFilePath))
                {
                    ShowError("File Not Found", $"Portfolio file not found:\n{portfolioFilePath}");
                    return;
                }

                _portfolioData = PortfolioSettings.LoadPortfolioFromFile(portfolioFilePath);

                if (_portfolioData == null)
                {
                    ShowError("Invalid Portfolio", "The selected portfolio could not be loaded.");
                    return;
                }

                // Ensure MonitoredFamilies exists
                if (_portfolioData.MonitoredFamilies == null)
                    _portfolioData.MonitoredFamilies = new List<PortfolioSettings.MonitoredFamily>();

                string projectName = PortfolioSettings.GetProjectName(_currentDocument);
                string projectGuid = PortfolioSettings.GetProjectGuid(_currentDocument);
                int familiesLoaded = 0;
                var familyUpdateStatus = new Dictionary<string, bool>();

                if (isFirebase)
                {
                    // Download and load families from Firebase Storage
                    var familyFiles = FirebaseClient.ListFamilies(portfolioFilePath);
                    System.Diagnostics.Debug.WriteLine($"📁 Found {familyFiles.Count} families in Firebase Storage");

                    foreach (var familyFileName in familyFiles)
                    {
                        try
                        {
                            string familyName = Path.GetFileNameWithoutExtension(familyFileName);
                            UpdateStatus($"Downloading {familyFileName}...");

                            string tempPath = FirebaseClient.DownloadFamily(familyFileName, portfolioFilePath);
                            if (tempPath == null) continue;

                            try
                            {
                                bool loaded = LoadFamilyIntoProject(tempPath);
                                if (loaded)
                                {
                                    familiesLoaded++;
                                    familyUpdateStatus[familyName] = true;
                                    System.Diagnostics.Debug.WriteLine($"   ✅ Loaded family: {familyName}");

                                    if (!_portfolioData.MonitoredFamilies.Any(f =>
                                        string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        _portfolioData.MonitoredFamilies.Add(new PortfolioSettings.MonitoredFamily
                                        {
                                            FamilyName = familyName,
                                            FileName = familyFileName
                                        });
                                    }
                                }
                                else
                                {
                                    familyUpdateStatus[familyName] = false;
                                }
                            }
                            finally
                            {
                                try { File.Delete(tempPath); } catch { }
                            }
                        }
                        catch (Exception familyEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"   ❌ Error loading family {familyFileName}: {familyEx.Message}");
                        }
                    }
                }
                else
                {
                    // Local file: load families from Families subfolder
                    string portfolioFolder = Path.GetDirectoryName(portfolioFilePath);
                    string familiesFolder = Path.Combine(portfolioFolder, "Families");

                    if (Directory.Exists(familiesFolder))
                    {
                        var familyFiles = Directory.GetFiles(familiesFolder, "*.rfa");
                        System.Diagnostics.Debug.WriteLine($"📁 Found {familyFiles.Length} families in portfolio folder");

                        foreach (var familyPath in familyFiles)
                        {
                            try
                            {
                                string familyFileName = Path.GetFileName(familyPath);
                                string familyName = Path.GetFileNameWithoutExtension(familyPath);

                                bool loaded = LoadFamilyIntoProject(familyPath);
                                if (loaded)
                                {
                                    familiesLoaded++;
                                    familyUpdateStatus[familyName] = true;
                                    System.Diagnostics.Debug.WriteLine($"   ✅ Loaded family: {familyName}");

                                    if (!_portfolioData.MonitoredFamilies.Any(f =>
                                        string.Equals(f.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        _portfolioData.MonitoredFamilies.Add(new PortfolioSettings.MonitoredFamily
                                        {
                                            FamilyName = familyName,
                                            FileName = familyFileName
                                        });
                                    }
                                }
                                else
                                {
                                    familyUpdateStatus[familyName] = false;
                                }
                            }
                            catch (Exception familyEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"   ❌ Error loading family {familyPath}: {familyEx.Message}");
                            }
                        }
                    }
                }

                // Ensure FamilyUpdateStatus has entries for ALL monitored families, not just ones in Storage
                if (_portfolioData.MonitoredFamilies != null)
                {
                    foreach (var mf in _portfolioData.MonitoredFamilies)
                    {
                        if (!familyUpdateStatus.ContainsKey(mf.FamilyName))
                            familyUpdateStatus[mf.FamilyName] = false; // not yet loaded
                    }
                }

                // Check if project already exists in portfolio
                string matchMethod;
                var existingProject = PortfolioSettings.FindProjectInPortfolio(
                    _portfolioData, projectName, projectGuid, out matchMethod);

                if (existingProject == null)
                {
                    _portfolioData.ProjectInfos.Add(new PortfolioSettings.PortfolioProject
                    {
                        ProjectName = projectName,
                        ProjectGuid = projectGuid,
                        Nickname = projectName,
                        IsTypicalDetailsAuthority = false,
                        LastSync = DateTime.Now,
                        Status = "active",
                        IsMigrated = isFirebase,
                        FamilyUpdateStatus = familyUpdateStatus
                    });
                }
                else
                {
                    existingProject.Status = "active";
                    existingProject.LastSync = DateTime.Now;
                    existingProject.FamilyUpdateStatus = familyUpdateStatus;
                    if (isFirebase) existingProject.IsMigrated = true;
                }

                _portfolioFilePath = portfolioFilePath;

                if (familiesLoaded > 0)
                    UpdateStatus($"Joined portfolio - loaded {familiesLoaded} families");

                SavePortfolioAndCloseWindow();
            }
            catch (Exception ex)
            {
                ShowError("Error joining portfolio", ex.Message);
            }
        }

        /// <summary>
        /// Load a family file into the current project with overwrite
        /// </summary>
        private bool LoadFamilyIntoProject(string familyPath)
        {
            try
            {
                if (_currentDocument == null || string.IsNullOrEmpty(familyPath))
                    return false;

                if (!File.Exists(familyPath))
                {
                    System.Diagnostics.Debug.WriteLine($"   ❌ Family file not found: {familyPath}");
                    return false;
                }

                Family loadedFamily = null;
                var loadOptions = new FamilyLoadOptions();

                using (Transaction trans = new Transaction(_currentDocument, "Load Portfolio Family"))
                {
                    trans.Start();

                    bool success = _currentDocument.LoadFamily(familyPath, loadOptions, out loadedFamily);

                    if (success || loadedFamily != null)
                    {
                        trans.Commit();
                        return true;
                    }
                    else
                    {
                        trans.RollBack();
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"   ❌ Error loading family: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Family load options that always overwrite existing families
        /// </summary>
        private class FamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true; // Always load/overwrite
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true; // Always load/overwrite
            }
        }

        private void SavePortfolioAndCloseWindow()
        {
            try
            {
                if (_portfolioData == null || string.IsNullOrEmpty(_portfolioFilePath))
                {
                    ShowError("Error", "Portfolio data or file path is missing");
                    WasSetupCompleted = true;
                    WasSetupSuccessful = false;
                    return;
                }

                _portfolioData.LastUpdated = DateTime.Now;

                if (!PortfolioSettings.SavePortfolioToFile(_portfolioData, _portfolioFilePath))
                {
                    ShowError("Error", "Failed to save portfolio data.");
                    WasSetupCompleted = true;
                    WasSetupSuccessful = false;
                    return;
                }

                WasSetupCompleted = true;
                WasSetupSuccessful = true;

                try { PortfolioManagePane.RefreshCurrentPane(); }
                catch (Exception) { }

                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { this.Close(); }
                    catch (Exception) { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ERROR IN SAVE", $"Exception:\n{ex.Message}\n\n{ex.StackTrace}");
                WasSetupCompleted = true;
                WasSetupSuccessful = false;
                UpdateStatus($"Error: {ex.Message}");
            }
        }

        #endregion

        #region UI Updates

        private void DisplayProjects()
        {
            try
            {
                _projectsPanel.Children.Clear();

                if (_portfolioData?.ProjectInfos == null || !_portfolioData.ProjectInfos.Any())
                {
                    var emptyText = new TextBlock
                    {
                        Text = "No projects in portfolio",
                        FontStyle = FontStyles.Italic,
                        Foreground = new SolidColorBrush(RevitTextMuted),
                        Margin = new Thickness(8, 8, 8, 8)
                    };
                    _projectsPanel.Children.Add(emptyText);
                    return;
                }

                bool isFirst = true;
                foreach (var project in _portfolioData.ProjectInfos
                    .Where(p => p.Status != "removed")
                    .OrderByDescending(p => p.IsTypicalDetailsAuthority)
                    .ThenBy(p => p.Nickname ?? p.ProjectName))
                {
                    var projectRow = CreateCompactProjectRow(project, isFirst);
                    _projectsPanel.Children.Add(projectRow);
                    isFirst = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error displaying projects: {ex.Message}");
            }
        }

        private Border CreateCompactProjectRow(PortfolioSettings.PortfolioProject project, bool isFirst)
        {
            var rowBorder = new Border
            {
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0, isFirst ? 0 : 1, 0, 0),
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(WpfColors.Transparent)
            };

            // Hover effect
            rowBorder.MouseEnter += (s, e) => rowBorder.Background = new SolidColorBrush(RevitRowHover);
            rowBorder.MouseLeave += (s, e) => rowBorder.Background = new SolidColorBrush(WpfColors.Transparent);

            var rowGrid = new WpfGrid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Info column - stacked horizontally for compactness
            var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            // First line: Nickname + Authority indicator
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };

            var nameBlock = new TextBlock
            {
                Text = project.Nickname ?? project.ProjectName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = new SolidColorBrush(RevitText),
                VerticalAlignment = VerticalAlignment.Center
            };
            titlePanel.Children.Add(nameBlock);

            if (project.IsTypicalDetailsAuthority)
            {
                var authorityBadge = new TextBlock
                {
                    Text = " (Authority)",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(RevitAuthorityText),
                    FontStyle = FontStyles.Italic,
                    VerticalAlignment = VerticalAlignment.Center
                };
                titlePanel.Children.Add(authorityBadge);
            }
            infoPanel.Children.Add(titlePanel);

            // Second line: Project name + Last sync (compact)
            var detailsBlock = new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(RevitTextMuted),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            detailsBlock.Inlines.Add(new System.Windows.Documents.Run(project.ProjectName));
            detailsBlock.Inlines.Add(new System.Windows.Documents.Run($"  •  {project.LastSync:M/d/yyyy h:mm tt}"));
            infoPanel.Children.Add(detailsBlock);

            WpfGrid.SetColumn(infoPanel, 0);
            rowGrid.Children.Add(infoPanel);

            // Edit button
            var editButton = CreateSmallRevitButton("Edit");
            editButton.Tag = project;
            editButton.Click += EditProject_Click;
            editButton.Margin = new Thickness(4, 0, 0, 0);
            editButton.VerticalAlignment = VerticalAlignment.Center;
            WpfGrid.SetColumn(editButton, 1);
            rowGrid.Children.Add(editButton);

            // Remove button
            var removeButton = CreateSmallRevitButton("Remove");
            removeButton.Tag = project;
            removeButton.Click += RemoveProject_Click;
            removeButton.Margin = new Thickness(4, 0, 0, 0);
            removeButton.VerticalAlignment = VerticalAlignment.Center;
            WpfGrid.SetColumn(removeButton, 2);
            rowGrid.Children.Add(removeButton);

            rowBorder.Child = rowGrid;
            return rowBorder;
        }

        private void UpdateStatus(string message)
        {
            _statusText.Text = message;
            System.Diagnostics.Debug.WriteLine($"📊 Status: {message}");
        }

        private void ShowError(string title, string message)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error: {title} - {message}");
            UpdateStatus($"Error: {message}");
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        #endregion

        #region Event Handlers

        private void CreateNewButton_Click(object sender, RoutedEventArgs e)
        {
            CreateNewPortfolio();
        }

        private void JoinExistingButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show Firebase portfolio browser
                UpdateStatus("Loading Firebase portfolios...");
                var dialog = new FirebasePortfolioNameDialog(isCreate: false);
                if (dialog.ShowDialog() == true)
                {
                    JoinExistingPortfolio(dialog.FirebasePath);
                }
                else if (dialog.BrowseLocalFile)
                {
                    // Fallback: browse for local file
                    var openDialog = new OpenFileDialog
                    {
                        Title = "Select Existing Portfolio File",
                        Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                        DefaultExt = "json"
                    };

                    if (openDialog.ShowDialog() == true)
                    {
                        JoinExistingPortfolio(openDialog.FileName);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError("Error joining existing portfolio", ex.Message);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WasSetupCompleted = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error closing window: {ex.Message}");
            }
        }

        private void LoadTopNotesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_portfolioData == null || string.IsNullOrEmpty(_portfolioFilePath))
                {
                    MessageBox.Show("No portfolio is configured. Create or join a portfolio first.",
                        "No Portfolio", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_currentDocument == null)
                {
                    MessageBox.Show("No Revit document is open.",
                        "No Document", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string currentProjectName = PortfolioSettings.GetProjectName(_currentDocument);

                // Confirm with user
                var result = MessageBox.Show(
                    $"This will load Top Notes from the 'Top Note' parameter in the current Revit project:\n\n" +
                    $"  {currentProjectName}\n\n" +
                    $"This will OVERWRITE any existing Top Notes in the portfolio for views from this project.\n\n" +
                    $"Continue?",
                    "Load Top Notes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                int updatedCount = LoadTopNotesFromCurrentProject();

                if (updatedCount > 0)
                {
                    // Save the updated portfolio
                    PortfolioSettings.SavePortfolioToFile(_portfolioData, _portfolioFilePath);

                    UpdateStatus($"Loaded {updatedCount} Top Notes from {currentProjectName}");
                    MessageBox.Show($"Successfully loaded {updatedCount} Top Notes from the current project.",
                        "Top Notes Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    UpdateStatus("No Top Notes found to load");
                    MessageBox.Show("No views with 'Top Note' parameters were found in the current project.",
                        "No Top Notes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ShowError("Error loading Top Notes", ex.Message);
            }
        }

        /// <summary>
        /// Reads Top Note parameters from all views in the current Revit project
        /// and updates matching views in the portfolio
        /// </summary>
        private int LoadTopNotesFromCurrentProject()
        {
            int updatedCount = 0;
            string currentProjectName = PortfolioSettings.GetProjectName(_currentDocument);
            List<RevitView> allViews = null;

            System.Diagnostics.Debug.WriteLine($"📝 Loading Top Notes from: {currentProjectName}");

            try
            {
                // Get all views from the current document
                using (var collector = new FilteredElementCollector(_currentDocument))
                {
                    allViews = collector
                        .OfClass(typeof(RevitView))
                        .Cast<RevitView>()
                        .Where(v => !v.IsTemplate)
                        .ToList();
                }

                System.Diagnostics.Debug.WriteLine($"   Found {allViews.Count} views in project");

                foreach (var view in allViews)
                {
                    try
                    {
                        // Try to get the Top Note parameter
                        Parameter topNoteParam = view.LookupParameter("Top Note");
                        if (topNoteParam == null || !topNoteParam.HasValue)
                            continue;

                        string topNote = topNoteParam.AsString();
                        if (string.IsNullOrEmpty(topNote))
                            continue;

                        int viewId = view.Id.IntegerValue;

                        // Find matching view in portfolio (by ViewId AND SourceProjectName)
                        var portfolioView = _portfolioData.Views?.FirstOrDefault(v =>
                            v.ViewId == viewId &&
                            string.Equals(v.SourceProjectName, currentProjectName, StringComparison.OrdinalIgnoreCase));

                        if (portfolioView != null)
                        {
                            portfolioView.TopNote = topNote;
                            updatedCount++;
                            System.Diagnostics.Debug.WriteLine($"   ✅ Updated TopNote for '{view.Name}': {topNote}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"   ⚠️ Error processing view '{view.Name}': {ex.Message}");
                    }
                }
            }
            finally
            {
                // Clear references to allow garbage collection
                allViews?.Clear();
            }

            System.Diagnostics.Debug.WriteLine($"📝 Updated {updatedCount} Top Notes");
            return updatedCount;
        }

        private void EditProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var project = button?.Tag as PortfolioSettings.PortfolioProject;

                if (project == null || _portfolioData == null)
                    return;

                var dialog = new EditProjectDialog(project, _portfolioData.ProjectInfos);
                if (dialog.ShowDialog() == true)
                {
                    project.Nickname = dialog.ProjectNickname;
                    project.IsTypicalDetailsAuthority = dialog.IsTypicalDetailsAuthority;

                    _portfolioData.LastUpdated = DateTime.Now;
                    PortfolioSettings.SavePortfolioToFile(_portfolioData, _portfolioFilePath);

                    DisplayProjects();
                    UpdateStatus($"Updated project: {project.Nickname}");
                }
            }
            catch (Exception ex)
            {
                ShowError("Error editing project", ex.Message);
            }
        }

        private void RemoveProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var project = button?.Tag as PortfolioSettings.PortfolioProject;
                if (project == null || _portfolioData == null)
                    return;

                // Check if this is the current project
                string currentProjectName = PortfolioSettings.GetProjectName(_currentDocument);
                bool isCurrentProject = string.Equals(project.ProjectName, currentProjectName, StringComparison.OrdinalIgnoreCase);

                string warningText = isCurrentProject
                    ? $"Remove '{project.Nickname}' from the portfolio?\n\n" +
                      "This will remove the project, ALL of its views, and clear all portfolio settings from this project.\n" +
                      "This action cannot be undone."
                    : $"Remove '{project.Nickname}' from the portfolio?\n\n" +
                      "This will remove the project and ALL of its views from the portfolio.\n" +
                      "This action cannot be undone.\n\n" +
                      "NOTE: Since this is not the currently open project, you should also open that project and use 'Leave Portfolio' to clear its settings.";

                var result = MessageBox.Show(
                    warningText,
                    "Remove Project",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    string projectName = project.ProjectName;

                    // Remove the project from ProjectInfos
                    _portfolioData.ProjectInfos.Remove(project);

                    // Remove all views from this project
                    int viewsRemoved = _portfolioData.Views?.RemoveAll(v =>
                        string.Equals(v.SourceProjectName, projectName, StringComparison.OrdinalIgnoreCase)) ?? 0;

                    System.Diagnostics.Debug.WriteLine($"🗑️ Removed project '{projectName}' and {viewsRemoved} views");

                    _portfolioData.LastUpdated = DateTime.Now;
                    PortfolioSettings.SavePortfolioToFile(_portfolioData, _portfolioFilePath);

                    // If this is the current project, clear all portfolio parameters
                    if (isCurrentProject && _currentDocument != null)
                    {
                        ClearProjectPortfolioParameters(_currentDocument);
                    }

                    DisplayProjects();
                    UpdateStatus($"Removed project: {project.Nickname} ({viewsRemoved} views)");

                    // Refresh the Portfolio Manager pane
                    try
                    {
                        PortfolioManagePane.RefreshCurrentPane();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                ShowError("Error removing project", ex.Message);
            }
        }

        /// <summary>
        /// Clear all portfolio-related parameters from a Revit document
        /// </summary>
        private void ClearProjectPortfolioParameters(Document doc)
        {
            try
            {
                using (Transaction trans = new Transaction(doc, "Clear Portfolio Parameters"))
                {
                    trans.Start();

                    // Clear all portfolio parameters
                    PortfolioSettings.SetJsonPath(doc, "");
                    PortfolioSettings.SetPortfolioGuid(doc, "");
                    PortfolioSettings.SetProjectGuid(doc, "");
                    PortfolioSettings.SetProjectType(doc, "");
                    PortfolioSettings.SetLastSyncTimestamp(doc, DateTime.MinValue);

                    // Also clear the TypicalDetails_ProjectType if it exists
                    try
                    {
                        ProjectInfo projectInfo = doc.ProjectInformation;
                        Parameter projectTypeParam = projectInfo?.LookupParameter("TypicalDetails_ProjectType");
                        if (projectTypeParam != null && !projectTypeParam.IsReadOnly)
                        {
                            projectTypeParam.Set("");
                        }
                    }
                    catch { }

                    trans.Commit();
                    System.Diagnostics.Debug.WriteLine("✅ Cleared all portfolio parameters from project");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error clearing portfolio parameters: {ex.Message}");
            }
        }

        #endregion
    }

    // =====================================================================
    // Firebase Portfolio Name Dialog
    // Shown when creating a new portfolio or joining an existing one.
    // Create mode: single Portfolio Name text box — path stored as portfolios/{name}
    // Join mode:   dropdown populated from ALL Firebase portfolios (flat and two-level)
    // =====================================================================
    public class FirebasePortfolioNameDialog : Window
    {
        /// <summary>Full Firebase path, e.g. "portfolios/my-portfolio" or "portfolios/folder/name".</summary>
        public string FirebasePath { get; private set; }
        public bool BrowseLocalFile { get; private set; } = false;

        private WpfTextBox _nameBox;         // Create mode
        private WpfComboBox _nameCombo;      // Join mode
        private TextBlock _previewText;
        private TextBlock _errorText;
        private TextBlock _nameHintText;
        private Button _confirmButton;
        private readonly bool _isCreate;

        private static readonly WpfColor RevitBackground = WpfColor.FromRgb(241, 241, 241);
        private static readonly WpfColor RevitBorder = WpfColor.FromRgb(180, 180, 180);
        private static readonly WpfColor RevitButtonFace = WpfColor.FromRgb(225, 225, 225);
        private static readonly WpfColor RevitText = WpfColor.FromRgb(30, 30, 30);
        private static readonly WpfColor RevitMuted = WpfColor.FromRgb(100, 100, 100);
        private static readonly WpfColor RevitError = WpfColor.FromRgb(180, 0, 0);

        public FirebasePortfolioNameDialog(bool isCreate, string defaultName = "package-1")
        {
            _isCreate = isCreate;
            Title = isCreate ? "Create New Portfolio in Firebase" : "Join Existing Firebase Portfolio";
            Width = 480;
            Height = isCreate ? 280 : 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(RevitBackground);

            BuildUI(defaultName);
        }

        private void BuildUI(string defaultName)
        {
            var stack = new StackPanel { Margin = new Thickness(16) };

            // Description
            stack.Children.Add(new TextBlock
            {
                Text = _isCreate
                    ? "Enter a name for the new portfolio. It will be stored at portfolios/{name} in Firebase."
                    : "Select the Firebase portfolio to join.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(RevitMuted),
                Margin = new Thickness(0, 0, 0, 12)
            });

            // ─── Portfolio Name ───
            stack.Children.Add(new TextBlock
            {
                Text = "Portfolio Name:",
                Foreground = new SolidColorBrush(RevitText),
                Margin = new Thickness(0, 0, 0, 4)
            });

            if (_isCreate)
            {
                _nameBox = new WpfTextBox
                {
                    Text = defaultName,
                    Height = 26,
                    Padding = new Thickness(4, 2, 4, 2),
                    BorderBrush = new SolidColorBrush(RevitBorder),
                    Margin = new Thickness(0, 0, 0, 2)
                };
                _nameBox.TextChanged += (s, e) => UpdatePreview();
                stack.Children.Add(_nameBox);

                stack.Children.Add(new TextBlock
                {
                    Text = "e.g. package-1, gmp-3, permit-set  (no spaces)",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(RevitMuted),
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }
            else
            {
                // JOIN MODE: Dropdown populated from ALL Firebase portfolios
                _nameCombo = new WpfComboBox
                {
                    Height = 26,
                    Margin = new Thickness(0, 0, 0, 2),
                    BorderBrush = new SolidColorBrush(RevitBorder),
                    IsEditable = true
                };

                try
                {
                    var allPortfolios = FirebaseClient.ListAllPortfolios();
                    foreach (var p in allPortfolios) _nameCombo.Items.Add(p);

                    if (allPortfolios.Count == 0)
                    {
                        _nameCombo.Text = "(no portfolios found)";
                        _nameCombo.IsEnabled = false;
                    }
                    else
                    {
                        _nameCombo.SelectedIndex = 0;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Could not list Firebase portfolios: {ex.Message}");
                    _nameCombo.Text = "(error loading portfolios)";
                    _nameCombo.IsEnabled = false;
                }

                _nameCombo.SelectionChanged += (s, e) => UpdatePreview();
                _nameCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                    new TextChangedEventHandler((s, e) => UpdatePreview()));

                stack.Children.Add(_nameCombo);

                _nameHintText = new TextBlock
                {
                    Text = _nameCombo.Items.Count > 0
                        ? $"{_nameCombo.Items.Count} portfolio{(_nameCombo.Items.Count == 1 ? "" : "s")} found"
                        : "No portfolios found in Firebase",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(RevitMuted),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                stack.Children.Add(_nameHintText);
            }

            // ─── Preview ───
            stack.Children.Add(new TextBlock
            {
                Text = "Firebase path:",
                Foreground = new SolidColorBrush(RevitMuted),
                FontSize = 10
            });
            _previewText = new TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                Foreground = new SolidColorBrush(RevitText),
                Margin = new Thickness(0, 2, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(_previewText);

            _errorText = new TextBlock
            {
                Foreground = new SolidColorBrush(RevitError),
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = System.Windows.Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(_errorText);

            // ─── Buttons ───
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            if (!_isCreate)
            {
                var localBtn = new Button
                {
                    Content = "Browse Local File",
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(RevitButtonFace),
                    BorderBrush = new SolidColorBrush(RevitBorder),
                    Foreground = new SolidColorBrush(RevitText),
                    Cursor = Cursors.Hand
                };
                localBtn.Click += (s, e) => { BrowseLocalFile = true; DialogResult = false; };
                btnPanel.Children.Add(localBtn);
            }

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(RevitButtonFace),
                BorderBrush = new SolidColorBrush(RevitBorder),
                Foreground = new SolidColorBrush(RevitText),
                Cursor = Cursors.Hand
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; };
            btnPanel.Children.Add(cancelBtn);

            _confirmButton = new Button
            {
                Content = _isCreate ? "Create" : "Join",
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(WpfColor.FromRgb(0, 120, 212)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0, 90, 158)),
                Cursor = Cursors.Hand
            };
            _confirmButton.Click += ConfirmButton_Click;
            btnPanel.Children.Add(_confirmButton);

            stack.Children.Add(btnPanel);

            Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            UpdatePreview();
        }

        private string GetCurrentInput()
        {
            if (_isCreate)
                return _nameBox?.Text ?? "";
            return _nameCombo?.SelectedItem?.ToString() ?? _nameCombo?.Text ?? "";
        }

        private void UpdatePreview()
        {
            if (_isCreate)
            {
                string sanitized = FirebaseClient.SanitizeName(GetCurrentInput());
                if (string.IsNullOrEmpty(sanitized))
                {
                    _previewText.Text = "portfolios/ ...";
                    _confirmButton.IsEnabled = false;
                    return;
                }
                _previewText.Text = $"portfolios/{sanitized}";
                _confirmButton.IsEnabled = true;
            }
            else
            {
                string selected = GetCurrentInput();
                if (string.IsNullOrEmpty(selected))
                {
                    _previewText.Text = "portfolios/ ...";
                    _confirmButton.IsEnabled = false;
                    return;
                }
                // Join mode items are already full paths (e.g. "portfolios/name")
                _previewText.Text = selected;
                _confirmButton.IsEnabled = true;
            }

            _errorText.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCreate)
            {
                string sanitized = FirebaseClient.SanitizeName(GetCurrentInput());
                if (string.IsNullOrEmpty(sanitized))
                {
                    _errorText.Text = "Portfolio name is required.";
                    _errorText.Visibility = System.Windows.Visibility.Visible;
                    return;
                }
                FirebasePath = $"portfolios/{sanitized}";
            }
            else
            {
                string selected = GetCurrentInput();
                if (string.IsNullOrEmpty(selected))
                {
                    _errorText.Text = "Select a portfolio to join.";
                    _errorText.Visibility = System.Windows.Visibility.Visible;
                    return;
                }
                FirebasePath = selected;
            }

            DialogResult = true;
        }
    }
}