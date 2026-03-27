// DimensionTextEditorWindowV3.cs

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

// Aliases to resolve ambiguity between System.Windows.* and Autodesk.Revit.DB.*
using WpfColor = System.Windows.Media.Color;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfCheckBox = System.Windows.Controls.CheckBox;

namespace ViewReferenceSystem.UI
{
    // ─────────────────────────────────────────────────────────────────────────
    // Settings model
    // ─────────────────────────────────────────────────────────────────────────

    public class DimensionTextSettingsV3
    {
        public bool UseCustomText { get; set; }
        public string CustomText { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string Suffix { get; set; } = "";
        public string Above { get; set; } = "";
        public string Below { get; set; } = "";

        public DimensionTextSettingsV3 Clone() => new DimensionTextSettingsV3
        {
            UseCustomText = UseCustomText,
            CustomText = CustomText,
            Prefix = Prefix,
            Suffix = Suffix,
            Above = Above,
            Below = Below
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Window
    // ─────────────────────────────────────────────────────────────────────────

    public class DimensionTextEditorWindowV3 : Window
    {
        private WpfTextBox _txtReplace;
        private WpfTextBox _txtAbove;
        private WpfTextBox _txtPrefix;
        private WpfTextBox _txtSuffix;
        private WpfTextBox _txtBelow;
        private WpfCheckBox _chkUseReplace;
        private Button _btnOk;
        private Button _btnCancel;

        private DimensionTextSettingsV3 _result;
        public DimensionTextSettingsV3 Result => _result;

        private readonly int _dimensionCount;

        public DimensionTextEditorWindowV3(int dimensionCount = 0)
        {
            _dimensionCount = dimensionCount;
            InitializeWindow();
            CreateControls();
            SetupEventHandlers();
        }

        private void InitializeWindow()
        {
            Title = _dimensionCount > 0
                                      ? $"Dimension Text Editor  ({_dimensionCount} dimension{(_dimensionCount == 1 ? "" : "s")} selected)"
                                      : "Dimension Text Editor";
            Width = 460;
            Height = 390;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(WpfColor.FromRgb(45, 45, 48));
            Foreground = Brushes.WhiteSmoke;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            WindowStyle = WindowStyle.ToolWindow;
            ShowInTaskbar = false;
        }

        private void CreateControls()
        {
            var mainGrid = new WpfGrid { Margin = new Thickness(16) };

            for (int i = 0; i < 8; i++)
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int row = 0;

            // Title
            var titleLabel = new TextBlock
            {
                Text = "Configure Dimension Text",
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Foreground = Brushes.WhiteSmoke,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            };
            WpfGrid.SetRow(titleLabel, row++);
            WpfGrid.SetColumnSpan(titleLabel, 2);
            mainGrid.Children.Add(titleLabel);

            // Replace checkbox
            _chkUseReplace = new WpfCheckBox
            {
                Content = "Replace With Text  (overrides dimension value)",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.WhiteSmoke,
                Margin = new Thickness(0, 0, 0, 4)
            };
            WpfGrid.SetRow(_chkUseReplace, row++);
            WpfGrid.SetColumnSpan(_chkUseReplace, 2);
            mainGrid.Children.Add(_chkUseReplace);

            // Replace textbox
            _txtReplace = MakeTextBox(false, "Custom text that replaces the dimension value");
            WpfGrid.SetRow(_txtReplace, row++);
            WpfGrid.SetColumnSpan(_txtReplace, 2);
            _txtReplace.Margin = new Thickness(0, 0, 0, 12);
            mainGrid.Children.Add(_txtReplace);

            // Above / Prefix / Suffix / Below
            AddRow(mainGrid, "Above:", ref row, out _txtAbove, "Text displayed above the dimension line");
            AddRow(mainGrid, "Prefix:", ref row, out _txtPrefix, "Text before the dimension value  e.g. '~'");
            AddRow(mainGrid, "Suffix:", ref row, out _txtSuffix, "Text after the dimension value  e.g. ' TYP'");
            AddRow(mainGrid, "Below:", ref row, out _txtBelow, "Text displayed below the dimension line  e.g. '-TYP'");

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            _btnOk = new Button
            {
                Content = "Apply",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true,
                Background = new SolidColorBrush(WpfColor.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0, 100, 180))
            };

            _btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30,
                IsCancel = true,
                Background = new SolidColorBrush(WpfColor.FromRgb(60, 60, 64)),
                Foreground = Brushes.WhiteSmoke,
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(90, 90, 95))
            };

            buttonPanel.Children.Add(_btnOk);
            buttonPanel.Children.Add(_btnCancel);
            WpfGrid.SetRow(buttonPanel, row);
            WpfGrid.SetColumnSpan(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        private void AddRow(WpfGrid grid, string label, ref int row, out WpfTextBox box, string tooltip)
        {
            var lbl = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(160, 160, 164)),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 10, 4)
            };
            WpfGrid.SetRow(lbl, row);
            WpfGrid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            box = MakeTextBox(true, tooltip);
            WpfGrid.SetRow(box, row);
            WpfGrid.SetColumn(box, 1);
            grid.Children.Add(box);

            row++;
        }

        private WpfTextBox MakeTextBox(bool enabled, string tooltip) => new WpfTextBox
        {
            IsEnabled = enabled,
            Height = 26,
            Margin = new Thickness(0, 4, 0, 4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(WpfColor.FromRgb(30, 30, 30)),
            Foreground = Brushes.WhiteSmoke,
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(90, 90, 95)),
            CaretBrush = Brushes.White,
            ToolTip = tooltip
        };

        private void SetupEventHandlers()
        {
            _chkUseReplace.Checked += (s, e) => { _txtReplace.IsEnabled = true; _txtReplace.Focus(); };
            _chkUseReplace.Unchecked += (s, e) => { _txtReplace.IsEnabled = false; _txtReplace.Text = ""; };

            _btnOk.Click += BtnOk_Click;
            _btnCancel.Click += (s, e) => { DialogResult = false; Close(); };

            KeyDown += (s, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };

            Loaded += (s, e) => _txtPrefix.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = new DimensionTextSettingsV3
                {
                    UseCustomText = _chkUseReplace.IsChecked == true,
                    CustomText = _txtReplace.Text?.Trim() ?? "",
                    Prefix = _txtPrefix.Text?.Trim() ?? "",
                    Suffix = _txtSuffix.Text?.Trim() ?? "",
                    Above = _txtAbove.Text?.Trim() ?? "",
                    Below = _txtBelow.Text?.Trim() ?? ""
                };

                if (!s.UseCustomText &&
                    string.IsNullOrEmpty(s.Prefix) &&
                    string.IsNullOrEmpty(s.Suffix) &&
                    string.IsNullOrEmpty(s.Above) &&
                    string.IsNullOrEmpty(s.Below))
                {
                    MessageBox.Show(
                        "Please enter at least one text setting, or check 'Replace With Text'.",
                        "Nothing to Apply", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (s.UseCustomText && string.IsNullOrEmpty(s.CustomText))
                {
                    var confirm = MessageBox.Show(
                        "'Replace With Text' is checked but no text was entered.\n\n" +
                        "This will blank out the dimension values. Continue?",
                        "Empty Replacement", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirm == MessageBoxResult.No) { _txtReplace.Focus(); return; }
                }

                _result = s;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void SetSettings(DimensionTextSettingsV3 existing)
        {
            if (existing == null) return;
            _chkUseReplace.IsChecked = existing.UseCustomText;
            _txtReplace.Text = existing.CustomText ?? "";
            _txtReplace.IsEnabled = existing.UseCustomText;
            _txtPrefix.Text = existing.Prefix ?? "";
            _txtSuffix.Text = existing.Suffix ?? "";
            _txtAbove.Text = existing.Above ?? "";
            _txtBelow.Text = existing.Below ?? "";
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// External Command
// ─────────────────────────────────────────────────────────────────────────────

namespace ViewReferenceSystem.Commands
{
    using Autodesk.Revit.Attributes;
    using Autodesk.Revit.DB;
    using Autodesk.Revit.UI;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ViewReferenceSystem.UI;

    [Transaction(TransactionMode.Manual)]
    public class DimensionTextEditorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc?.Document;

            if (doc == null)
            {
                TaskDialog.Show("Dimension Text Editor", "No active document.");
                return Result.Failed;
            }

            var selected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Dimension>()
                .ToList();

            if (selected.Count == 0)
            {
                TaskDialog.Show("Dimension Text Editor",
                    "No dimensions selected.\n\nSelect one or more dimensions, then run this tool.");
                return Result.Cancelled;
            }

            var window = new DimensionTextEditorWindowV3(selected.Count);
            bool? dialogResult = window.ShowDialog();

            if (dialogResult != true || window.Result == null)
                return Result.Cancelled;

            DimensionTextSettingsV3 settings = window.Result;

            int applied = 0;
            int skipped = 0;
            var errors = new List<string>();

            using (Transaction trans = new Transaction(doc, "Edit Dimension Text"))
            {
                trans.Start();

                foreach (Dimension dim in selected)
                {
                    try
                    {
                        if (dim.NumberOfSegments > 1)
                        {
                            foreach (DimensionSegment seg in dim.Segments)
                                ApplyToSegment(seg, settings);
                        }
                        else
                        {
                            ApplyToDimension(dim, settings);
                        }
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        errors.Add($"  Element {dim.Id.IntegerValue}: {ex.Message}");
                    }
                }

                trans.Commit();
            }

            string report = $"Applied to {applied} dimension{(applied == 1 ? "" : "s")}.";
            if (skipped > 0)
            {
                report += $"\nSkipped {skipped} (read-only or locked):";
                report += "\n" + string.Join("\n", errors.Take(5));
                if (errors.Count > 5) report += $"\n  ...and {errors.Count - 5} more.";
            }
            TaskDialog.Show("Dimension Text Editor", report);

            return Result.Succeeded;
        }

        private void ApplyToDimension(Dimension dim, DimensionTextSettingsV3 s)
        {
            if (s.UseCustomText)
                dim.ValueOverride = s.CustomText;

            if (!string.IsNullOrEmpty(s.Above)) dim.Above = s.Above;
            if (!string.IsNullOrEmpty(s.Below)) dim.Below = s.Below;
            if (!string.IsNullOrEmpty(s.Prefix)) dim.Prefix = s.Prefix;
            if (!string.IsNullOrEmpty(s.Suffix)) dim.Suffix = s.Suffix;
        }

        private void ApplyToSegment(DimensionSegment seg, DimensionTextSettingsV3 s)
        {
            if (s.UseCustomText)
                seg.ValueOverride = s.CustomText;

            if (!string.IsNullOrEmpty(s.Above)) seg.Above = s.Above;
            if (!string.IsNullOrEmpty(s.Below)) seg.Below = s.Below;
            if (!string.IsNullOrEmpty(s.Prefix)) seg.Prefix = s.Prefix;
            if (!string.IsNullOrEmpty(s.Suffix)) seg.Suffix = s.Suffix;
        }
    }
}