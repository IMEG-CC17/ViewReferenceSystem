using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace ViewReferenceSystem.UI
{
    /// <summary>
    /// Dialog for editing portfolio name
    /// </summary>
    public partial class EditPortfolioNameDialog : Window
    {
        private TextBox _nameTextBox;
        private Button _saveButton;
        private Button _cancelButton;
        private TextBlock _instructionText;

        public string PortfolioName { get; private set; }
        public bool WasSaved { get; private set; } = false;

        public EditPortfolioNameDialog(string currentName = "")
        {
            PortfolioName = currentName ?? "";
            InitializeDialog();
        }

        /// <summary>
        /// Initialize dialog layout and controls
        /// </summary>
        private void InitializeDialog()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("✏️ Initializing EditPortfolioNameDialog");

                // Window properties
                Title = "Edit Portfolio Name";
                Width = 400;
                Height = 180;
                MinWidth = 350;
                MinHeight = 160;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ShowInTaskbar = false;
                WindowStyle = WindowStyle.ToolWindow;
                ResizeMode = ResizeMode.CanResize;
                Background = SystemColors.ControlBrush;

                // Main grid
                var mainGrid = new Grid();
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Instructions
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Text box
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Spacer
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons
                mainGrid.Margin = new Thickness(15);

                // Instructions
                _instructionText = new TextBlock
                {
                    Text = "Enter the name for this portfolio:",
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 10),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetRow(_instructionText, 0);
                mainGrid.Children.Add(_instructionText);

                // Text box
                _nameTextBox = new TextBox
                {
                    Text = PortfolioName,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 15),
                    Height = 25
                };
                Grid.SetRow(_nameTextBox, 1);
                mainGrid.Children.Add(_nameTextBox);

                // Button panel
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                _cancelButton = new Button
                {
                    Content = "Cancel",
                    Width = 75,
                    Height = 25,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsCancel = true
                };
                _cancelButton.Click += CancelButton_Click;
                buttonPanel.Children.Add(_cancelButton);

                _saveButton = new Button
                {
                    Content = "Save",
                    Width = 75,
                    Height = 25,
                    IsDefault = true
                };
                _saveButton.Click += SaveButton_Click;
                buttonPanel.Children.Add(_saveButton);

                Grid.SetRow(buttonPanel, 3);
                mainGrid.Children.Add(buttonPanel);

                Content = mainGrid;

                // Event handlers
                KeyDown += EditPortfolioNameDialog_KeyDown;
                Loaded += EditPortfolioNameDialog_Loaded;

                System.Diagnostics.Debug.WriteLine("✅ EditPortfolioNameDialog initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error initializing EditPortfolioNameDialog: {ex.Message}");
            }
        }

        #region Event Handlers

        /// <summary>
        /// Handle dialog load - focus text box and select all
        /// </summary>
        private void EditPortfolioNameDialog_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _nameTextBox.Focus();
                _nameTextBox.SelectAll();
                System.Diagnostics.Debug.WriteLine("✅ Text box focused and text selected");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error focusing text box: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle keyboard shortcuts
        /// </summary>
        private void EditPortfolioNameDialog_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Escape)
                {
                    WasSaved = false;
                    Close();
                }
                else if (e.Key == Key.Enter)
                {
                    SaveAndClose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error handling key down: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle save button click
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveAndClose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in save button click: {ex.Message}");
                MessageBox.Show($"Error saving portfolio name: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Handle cancel button click
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WasSaved = false;
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in cancel button click: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Save the portfolio name and close dialog
        /// </summary>
        private void SaveAndClose()
        {
            try
            {
                string newName = _nameTextBox.Text?.Trim() ?? "";

                // Validate input
                if (string.IsNullOrEmpty(newName))
                {
                    MessageBox.Show("Portfolio name cannot be empty.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _nameTextBox.Focus();
                    return;
                }

                if (newName.Length > 100) // Example limit
                {
                    MessageBox.Show("Portfolio name is too long. Please keep it under 100 characters.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _nameTextBox.Focus();
                    return;
                }

                // Check for invalid characters
                char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
                foreach (char c in invalidChars)
                {
                    if (newName.Contains(c))
                    {
                        MessageBox.Show($"Portfolio name contains invalid character: '{c}'. Please remove it.",
                            "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        _nameTextBox.Focus();
                        return;
                    }
                }

                PortfolioName = newName;
                WasSaved = true;

                System.Diagnostics.Debug.WriteLine($"✅ Portfolio name saved: '{PortfolioName}'");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error saving portfolio name: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Static method to show dialog and get result
        /// </summary>
        public static (bool saved, string name) ShowDialog(Window owner, string currentName = "")
        {
            try
            {
                var dialog = new EditPortfolioNameDialog(currentName);
                if (owner != null)
                {
                    dialog.Owner = owner;
                }

                bool? result = dialog.ShowDialog();

                return (dialog.WasSaved, dialog.PortfolioName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error showing EditPortfolioNameDialog: {ex.Message}");
                return (false, currentName ?? "");
            }
        }
    }
}