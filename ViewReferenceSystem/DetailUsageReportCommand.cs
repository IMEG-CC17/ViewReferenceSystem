using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ViewReferenceSystem.Core;
using ViewReferenceSystem.Models;
using PortfolioSettings = ViewReferenceSystem.Core.PortfolioSettings;
using WpfGrid = System.Windows.Controls.Grid;
using System.Windows.Media;

namespace ViewReferenceSystem.Commands
{
    /// <summary>
    /// Detail Usage Report Command - Shows which details from Typical Details Authority are referenced vs unreferenced
    /// Can be run from any project in the portfolio
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class DetailUsageReportCommand : IExternalCommand
    {
        private PortfolioSettings.Portfolio _portfolioData;
        private string _portfolioPath;
        private string _authorityProjectName;
        private List<SheetSelectionItem> _allSheets;
        private CheckBox _hideUncheckedCheckBox;
        private StackPanel _sheetListPanel;
        private Window _selectionWindow;

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

                // Get portfolio path
                _portfolioPath = PortfolioSettings.GetJsonPath(doc);
                if (string.IsNullOrEmpty(_portfolioPath) || (!FirebaseClient.IsFirebasePath(_portfolioPath) && !File.Exists(_portfolioPath)))
                {
                    TaskDialog.Show("Not in Portfolio",
                        "This project is not part of a portfolio.\n\n" +
                        "Use Portfolio Setup to configure the portfolio first.");
                    return Result.Cancelled;
                }

                // Load portfolio data (handles both Firebase and local paths)
                _portfolioData = PortfolioSettings.LoadPortfolioFromFile(_portfolioPath);

                if (_portfolioData?.Views == null || !_portfolioData.Views.Any())
                {
                    TaskDialog.Show("No Views", "No views found in the portfolio.");
                    return Result.Cancelled;
                }

                // Find the Typical Details Authority project
                var authorityProject = _portfolioData.ProjectInfos?.FirstOrDefault(p => p.IsTypicalDetailsAuthority);
                if (authorityProject == null)
                {
                    TaskDialog.Show("No Authority",
                        "No Typical Details Authority project is configured in this portfolio.\n\n" +
                        "Use Portfolio Setup to designate a project as the Typical Details Authority.");
                    return Result.Cancelled;
                }

                _authorityProjectName = authorityProject.ProjectName;

                // Get all sheets from the authority project
                var authorityViews = _portfolioData.Views
                    .Where(v => string.Equals(v.SourceProjectName, _authorityProjectName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!authorityViews.Any())
                {
                    TaskDialog.Show("No Views",
                        $"No views from '{_authorityProjectName}' found in the portfolio.\n\n" +
                        "The Typical Details Authority project needs to sync to central.");
                    return Result.Cancelled;
                }

                // Get unique sheets
                var uniqueSheets = authorityViews
                    .Where(v => !string.IsNullOrEmpty(v.SheetNumber))
                    .Select(v => v.SheetNumber)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();

                if (!uniqueSheets.Any())
                {
                    TaskDialog.Show("No Sheets", "No sheets found in the Typical Details Authority project.");
                    return Result.Cancelled;
                }

                // Build sheet selection items
                var excludedSheets = _portfolioData.ExcludedTypicalDetailSheets ?? new List<string>();
                _allSheets = uniqueSheets.Select(sheetNum => new SheetSelectionItem
                {
                    SheetNumber = sheetNum,
                    SheetName = GetSheetName(authorityViews, sheetNum),
                    IsIncluded = !excludedSheets.Contains(sheetNum, StringComparer.OrdinalIgnoreCase),
                    ViewCount = authorityViews.Count(v => v.SheetNumber == sheetNum)
                }).ToList();

                // Show sheet selection dialog
                ShowSheetSelectionDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error generating report: {ex.Message}";
                TaskDialog.Show("Error", message);
                return Result.Failed;
            }
        }

        private string GetSheetName(List<ViewInfo> views, string sheetNumber)
        {
            // Get the actual SheetName from the first view on this sheet
            var view = views.FirstOrDefault(v => v.SheetNumber == sheetNumber);
            if (view != null && !string.IsNullOrEmpty(view.SheetName))
                return view.SheetName;

            return "";
        }

        private void ShowSheetSelectionDialog()
        {
            _selectionWindow = new Window
            {
                Title = $"Select Typical Detail Sheets - {_authorityProjectName}",
                Width = 550,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = SystemColors.ControlBrush,
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            var mainGrid = new WpfGrid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Hide checkbox
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Sheet list
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Header
            var headerText = new TextBlock
            {
                Text = "Select sheets that contain Typical Details:",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(12, 12, 12, 4)
            };
            WpfGrid.SetRow(headerText, 0);
            mainGrid.Children.Add(headerText);

            // Hide unchecked checkbox
            _hideUncheckedCheckBox = new CheckBox
            {
                Content = "Hide unchecked sheets",
                IsChecked = false,
                Margin = new Thickness(12, 4, 12, 8)
            };
            _hideUncheckedCheckBox.Checked += (s, e) => RefreshSheetList();
            _hideUncheckedCheckBox.Unchecked += (s, e) => RefreshSheetList();
            WpfGrid.SetRow(_hideUncheckedCheckBox, 1);
            mainGrid.Children.Add(_hideUncheckedCheckBox);

            // Sheet list with scroll - use a border for visual grouping
            var listBorder = new Border
            {
                BorderBrush = SystemColors.ActiveBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(12, 0, 12, 12),
                Background = Brushes.White
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };

            _sheetListPanel = new StackPanel();
            scrollViewer.Content = _sheetListPanel;
            listBorder.Child = scrollViewer;
            WpfGrid.SetRow(listBorder, 2);
            mainGrid.Children.Add(listBorder);

            RefreshSheetList();

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 0, 12, 12)
            };

            var generateButton = new Button
            {
                Content = "Generate Report",
                Padding = new Thickness(20, 6, 20, 6),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 100
            };
            generateButton.Click += GenerateReport_Click;
            buttonPanel.Children.Add(generateButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(20, 6, 20, 6),
                MinWidth = 80
            };
            cancelButton.Click += (s, e) => _selectionWindow.Close();
            buttonPanel.Children.Add(cancelButton);

            WpfGrid.SetRow(buttonPanel, 3);
            mainGrid.Children.Add(buttonPanel);

            _selectionWindow.Content = mainGrid;
            _selectionWindow.ShowDialog();
        }

        private void RefreshSheetList()
        {
            _sheetListPanel.Children.Clear();

            bool hideUnchecked = _hideUncheckedCheckBox?.IsChecked ?? false;

            foreach (var sheet in _allSheets)
            {
                if (hideUnchecked && !sheet.IsIncluded)
                    continue;

                var sheetPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(2, 2, 2, 2)
                };

                var checkBox = new CheckBox
                {
                    IsChecked = sheet.IsIncluded,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = sheet
                };
                checkBox.Checked += (s, e) =>
                {
                    if (s is CheckBox cb && cb.Tag is SheetSelectionItem item)
                        item.IsIncluded = true;
                };
                checkBox.Unchecked += (s, e) =>
                {
                    if (s is CheckBox cb && cb.Tag is SheetSelectionItem item)
                        item.IsIncluded = false;
                };
                sheetPanel.Children.Add(checkBox);

                var sheetNumberText = new TextBlock
                {
                    Text = sheet.SheetNumber,
                    FontWeight = FontWeights.SemiBold,
                    Width = 80,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                sheetPanel.Children.Add(sheetNumberText);

                var viewCountText = new TextBlock
                {
                    Text = $"({sheet.ViewCount} views)",
                    Foreground = Brushes.Gray,
                    Width = 70,
                    VerticalAlignment = VerticalAlignment.Center
                };
                sheetPanel.Children.Add(viewCountText);

                var sheetNameText = new TextBlock
                {
                    Text = sheet.SheetName,
                    Foreground = Brushes.DimGray,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                sheetPanel.Children.Add(sheetNameText);

                _sheetListPanel.Children.Add(sheetPanel);
            }
        }

        private void GenerateReport_Click(object sender, RoutedEventArgs e)
        {
            // Save excluded sheets to portfolio JSON
            var excludedSheets = _allSheets.Where(s => !s.IsIncluded).Select(s => s.SheetNumber).ToList();
            _portfolioData.ExcludedTypicalDetailSheets = excludedSheets;

            try
            {
                PortfolioSettings.SavePortfolioToFile(_portfolioData, _portfolioPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Could not save excluded sheets: {ex.Message}");
            }

            // Get included sheet numbers
            var includedSheetNumbers = _allSheets.Where(s => s.IsIncluded).Select(s => s.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Get views from authority project on included sheets only
            var authorityViews = _portfolioData.Views
                .Where(v => string.Equals(v.SourceProjectName, _authorityProjectName, StringComparison.OrdinalIgnoreCase))
                .Where(v => includedSheetNumbers.Contains(v.SheetNumber))
                .OrderBy(v => v.SheetNumber)
                .ThenBy(v => v.DetailNumber)
                .ToList();

            if (!authorityViews.Any())
            {
                TaskDialog.Show("No Views", "No views found on the selected sheets.");
                return;
            }

            // Check each view against all projects' UsesViewIds
            var referencedViews = new List<(ViewInfo View, List<string> UsedByProjects)>();
            var unreferencedViews = new List<ViewInfo>();

            foreach (var view in authorityViews)
            {
                var projectsUsingThisView = GetProjectsUsingView(view.ViewId);

                if (projectsUsingThisView.Any())
                {
                    referencedViews.Add((view, projectsUsingThisView));
                }
                else
                {
                    unreferencedViews.Add(view);
                }
            }

            // Build the report
            var report = new StringBuilder();
            report.AppendLine("═══════════════════════════════════════════════════════════════");
            report.AppendLine($"  TYPICAL DETAILS USAGE REPORT");
            report.AppendLine($"  Authority Project: {_authorityProjectName}");
            report.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine("═══════════════════════════════════════════════════════════════");
            report.AppendLine();
            report.AppendLine($"  Sheets Analyzed: {includedSheetNumbers.Count}");
            report.AppendLine($"  Total Details: {authorityViews.Count}");
            report.AppendLine($"  Referenced:    {referencedViews.Count}");
            report.AppendLine($"  Unreferenced:  {unreferencedViews.Count}");
            report.AppendLine();

            // UNREFERENCED FIRST (priority - these are candidates for deletion)
            if (unreferencedViews.Any())
            {
                report.AppendLine("───────────────────────────────────────────────────────────────");
                report.AppendLine("  ⚠️  UNREFERENCED DETAILS (candidates for deletion)");
                report.AppendLine("───────────────────────────────────────────────────────────────");

                foreach (var view in unreferencedViews)
                {
                    string location = $"{view.DetailNumber}/{view.SheetNumber}";
                    report.AppendLine($"  ✗ {location,-12} {view.ViewName}");
                }
                report.AppendLine();
            }

            // Referenced details
            if (referencedViews.Any())
            {
                report.AppendLine("───────────────────────────────────────────────────────────────");
                report.AppendLine("  ✓ REFERENCED DETAILS (in use - do NOT delete)");
                report.AppendLine("───────────────────────────────────────────────────────────────");

                foreach (var (view, usedByProjects) in referencedViews)
                {
                    string location = $"{view.DetailNumber}/{view.SheetNumber}";
                    string projectList = string.Join(", ", usedByProjects.Select(p => GetProjectDisplayName(p)));
                    report.AppendLine($"  ✓ {location,-12} {view.ViewName}");
                    report.AppendLine($"                   → {projectList}");
                }
                report.AppendLine();
            }

            report.AppendLine("═══════════════════════════════════════════════════════════════");
            report.AppendLine("  NOTE: Reference data is updated when projects sync to central.");
            report.AppendLine("  Ensure all projects have synced recently for accurate results.");
            report.AppendLine("═══════════════════════════════════════════════════════════════");

            _selectionWindow.Close();

            // Show report
            ShowScrollableReport("Typical Details Usage Report", report.ToString());
        }

        /// <summary>
        /// Get list of project names that use the specified ViewId
        /// Checks each project's UsesViewIds list
        /// </summary>
        private List<string> GetProjectsUsingView(int viewId)
        {
            var projectNames = new List<string>();

            if (_portfolioData?.ProjectInfos == null)
                return projectNames;

            foreach (var project in _portfolioData.ProjectInfos)
            {
                // Skip the authority project itself
                if (project.IsTypicalDetailsAuthority)
                    continue;

                if (project.UsesViewIds != null && project.UsesViewIds.Contains(viewId))
                {
                    projectNames.Add(project.ProjectName);
                }
            }

            return projectNames;
        }

        /// <summary>
        /// Get display name for a project (nickname if available)
        /// </summary>
        private string GetProjectDisplayName(string projectName)
        {
            var project = _portfolioData?.ProjectInfos?.FirstOrDefault(p =>
                string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));

            if (project != null && !string.IsNullOrEmpty(project.Nickname) &&
                !string.Equals(project.Nickname, project.ProjectName, StringComparison.OrdinalIgnoreCase))
            {
                return project.Nickname;
            }

            return projectName;
        }

        private void ShowScrollableReport(string title, string content)
        {
            var window = new Window
            {
                Title = title,
                Width = 700,
                Height = 550,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = SystemColors.ControlBrush,
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            var grid = new WpfGrid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Scrollable text area with border
            var textBorder = new Border
            {
                BorderBrush = SystemColors.ActiveBorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(12, 12, 12, 8),
                Background = Brushes.White
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8)
            };

            var textBlock = new TextBlock
            {
                Text = content,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                TextWrapping = TextWrapping.NoWrap
            };

            scrollViewer.Content = textBlock;
            textBorder.Child = scrollViewer;
            WpfGrid.SetRow(textBorder, 0);
            grid.Children.Add(textBorder);

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12, 0, 12, 12)
            };

            // Copy button
            var copyButton = new Button
            {
                Content = "Copy to Clipboard",
                Padding = new Thickness(20, 6, 20, 6),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 120
            };
            copyButton.Click += (s, e) =>
            {
                System.Windows.Clipboard.SetText(content);
                TaskDialog.Show("Copied", "Report copied to clipboard.");
            };
            buttonPanel.Children.Add(copyButton);

            // Close button
            var closeButton = new Button
            {
                Content = "Close",
                Padding = new Thickness(20, 6, 20, 6),
                MinWidth = 80
            };
            closeButton.Click += (s, e) => window.Close();
            buttonPanel.Children.Add(closeButton);

            WpfGrid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            window.Content = grid;
            window.ShowDialog();
        }

        /// <summary>
        /// Helper class for sheet selection
        /// </summary>
        private class SheetSelectionItem
        {
            public string SheetNumber { get; set; }
            public string SheetName { get; set; }
            public bool IsIncluded { get; set; }
            public int ViewCount { get; set; }
        }
    }
}