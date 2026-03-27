using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ViewReferenceSystem.Core;
using ViewReferenceSystem.Models;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ViewReferenceSystem.UI
{
    public class EditProjectDialog : Window
    {
        private WpfTextBox projectNicknameTextBox;
        private CheckBox typicalDetailsAuthorityCheckBox;
        private Button okButton;
        private Button cancelButton;
        private TextBlock validationMessageText;

        // Dialog result properties for caller to access
        public string ProjectNickname { get; private set; } = "";
        public bool IsTypicalDetailsAuthority { get; private set; } = false;

        // Private fields
        private readonly PortfolioSettings.PortfolioProject _portfolioProject;
        private readonly List<PortfolioSettings.PortfolioProject> _allProjects;

        public EditProjectDialog(PortfolioSettings.PortfolioProject portfolioProject, List<PortfolioSettings.PortfolioProject> allProjects)
        {
            _portfolioProject = portfolioProject ?? throw new ArgumentNullException(nameof(portfolioProject));
            _allProjects = allProjects ?? new List<PortfolioSettings.PortfolioProject>();

            // Initialize properties from the portfolio project
            ProjectNickname = portfolioProject.Nickname ?? portfolioProject.ProjectName ?? "";
            IsTypicalDetailsAuthority = portfolioProject.IsTypicalDetailsAuthority;

            System.Diagnostics.Debug.WriteLine($"EditProjectDialog constructor - Input nickname: '{portfolioProject.Nickname}', Set ProjectNickname: '{ProjectNickname}'");

            InitializeDialog();
        }

        private void InitializeDialog()
        {
            Title = "Edit Project Settings";
            Height = 380;
            Width = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            // Create main grid
            WpfGrid mainGrid = new WpfGrid();
            mainGrid.Margin = new Thickness(20);

            // Define rows
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Project name display
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Nickname label
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Nickname textbox
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) }); // Spacer before checkbox
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // TD Authority checkbox
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // TD Authority explanation
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) }); // Validation message space
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) }); // Spacer
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            int currentRow = 0;

            // Project name display (read-only) - REMOVED ABILITY TO EDIT
            TextBlock projectNameLabel = new TextBlock
            {
                Text = "Project Name (Read-Only):",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            WpfGrid.SetRow(projectNameLabel, currentRow++);
            mainGrid.Children.Add(projectNameLabel);

            // Display project name as read-only text
            TextBlock projectNameDisplay = new TextBlock
            {
                Text = _portfolioProject.ProjectName ?? "Unknown Project",
                FontSize = 12,

                Foreground = Brushes.DarkGray,
                TextWrapping = TextWrapping.Wrap,
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0)
            };
            WpfGrid.SetRow(projectNameDisplay, currentRow++);
            mainGrid.Children.Add(projectNameDisplay);

            currentRow++; // Spacer

            // Nickname label
            TextBlock nicknameLabel = new TextBlock
            {
                Text = "Project Nickname (Display Name):",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            WpfGrid.SetRow(nicknameLabel, currentRow++);
            mainGrid.Children.Add(nicknameLabel);

            // Nickname textbox - THIS IS THE ONLY EDITABLE FIELD
            projectNicknameTextBox = new WpfTextBox
            {
                Text = ProjectNickname,
                FontSize = 12,
                Height = 32,
                Padding = new Thickness(8, 6, 8, 6)
            };
            projectNicknameTextBox.TextChanged += ProjectNicknameTextBox_TextChanged;
            projectNicknameTextBox.KeyDown += ProjectNicknameTextBox_KeyDown;
            WpfGrid.SetRow(projectNicknameTextBox, currentRow++);
            mainGrid.Children.Add(projectNicknameTextBox);

            System.Diagnostics.Debug.WriteLine($"TextBox initialized with text: '{projectNicknameTextBox.Text}'");

            currentRow++; // Spacer

            // Typical Details Authority checkbox - ONLY OTHER EDITABLE FIELD
            typicalDetailsAuthorityCheckBox = new CheckBox
            {
                Content = "Typical Details Authority",
                IsChecked = IsTypicalDetailsAuthority,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1f, 0x4e, 0x79))
            };
            typicalDetailsAuthorityCheckBox.Checked += TypicalDetailsAuthorityCheckBox_Changed;
            typicalDetailsAuthorityCheckBox.Unchecked += TypicalDetailsAuthorityCheckBox_Changed;
            WpfGrid.SetRow(typicalDetailsAuthorityCheckBox, currentRow++);
            mainGrid.Children.Add(typicalDetailsAuthorityCheckBox);

            // Add explanation text
            TextBlock explanationText = new TextBlock
            {
                Text = "Contains standard details for portfolio search priority",
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 2, 0, 0)
            };
            WpfGrid.SetRow(explanationText, currentRow++);
            mainGrid.Children.Add(explanationText);

            currentRow++; // Spacer

            // Validation message
            validationMessageText = new TextBlock
            {
                Foreground = Brushes.Red,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Top,
                MaxHeight = 50
            };
            WpfGrid.SetRow(validationMessageText, currentRow++);
            mainGrid.Children.Add(validationMessageText);

            currentRow++; // Spacer

            // Button panel
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            WpfGrid.SetRow(buttonPanel, currentRow++);

            // OK button
            okButton = new Button
            {
                Content = "OK",
                Width = 70,
                Height = 28,
                IsDefault = true,
                Margin = new Thickness(0, 0, 10, 0)
            };
            okButton.Click += OkButton_Click;
            buttonPanel.Children.Add(okButton);

            // Cancel button
            cancelButton = new Button
            {
                Content = "Cancel",
                Width = 70,
                Height = 28,
                IsCancel = true
            };
            cancelButton.Click += CancelButton_Click;
            buttonPanel.Children.Add(cancelButton);

            mainGrid.Children.Add(buttonPanel);
            Content = mainGrid;

            // Focus the nickname textbox
            Loaded += (s, e) =>
            {
                projectNicknameTextBox.Focus();
                projectNicknameTextBox.SelectAll();
            };

            ValidateInput();
        }

        private void ProjectNicknameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateInput();
        }

        private void ProjectNicknameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && okButton != null && okButton.IsEnabled)
            {
                SaveAndClose();
            }
        }

        private void TypicalDetailsAuthorityCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ValidateInput();
        }

        private void ValidateInput()
        {
            bool isValid = true;
            string validationMessage = "";

            // Check if nickname is empty
            if (string.IsNullOrWhiteSpace(projectNicknameTextBox?.Text))
            {
                isValid = false;
                validationMessage = "Project nickname cannot be empty.";
            }

            // Check for TD authority conflicts
            bool wantsToBeAuthority = typicalDetailsAuthorityCheckBox?.IsChecked == true;
            if (wantsToBeAuthority && !_portfolioProject.IsTypicalDetailsAuthority)
            {
                // Check if another project is already authority using ProjectName
                if (_allProjects != null)
                {
                    var currentAuthority = _allProjects.FirstOrDefault(p =>
                        p.IsTypicalDetailsAuthority &&
                        !string.IsNullOrEmpty(p.ProjectName) &&
                        !string.IsNullOrEmpty(_portfolioProject.ProjectName) &&
                        p.ProjectName != _portfolioProject.ProjectName);

                    if (currentAuthority != null)
                    {
                        validationMessage = $"Note: {currentAuthority.ProjectName} is currently the Typical Details authority. This will be changed.";
                        // This is just a warning, not an error
                    }
                }
            }

            // Update UI
            if (okButton != null)
            {
                okButton.IsEnabled = isValid;
            }

            if (validationMessageText != null)
            {
                if (!string.IsNullOrEmpty(validationMessage))
                {
                    validationMessageText.Text = validationMessage;
                    validationMessageText.Foreground = isValid ? Brushes.Orange : Brushes.Red;
                    validationMessageText.Visibility = Visibility.Visible;
                }
                else
                {
                    validationMessageText.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SaveAndClose();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        private void SaveAndClose()
        {
            string newNickname = projectNicknameTextBox?.Text?.Trim() ?? "";
            bool newAuthority = typicalDetailsAuthorityCheckBox?.IsChecked == true;

            System.Diagnostics.Debug.WriteLine($"SaveAndClose() - newNickname: '{newNickname}', newAuthority: {newAuthority}");

            if (string.IsNullOrEmpty(newNickname))
            {
                MessageBox.Show("Project nickname cannot be empty.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                projectNicknameTextBox?.Focus();
                return;
            }

            // Check for TD authority change confirmation
            if (newAuthority && !_portfolioProject.IsTypicalDetailsAuthority)
            {
                // Find current authority using ProjectName with null check
                if (_allProjects != null)
                {
                    var currentAuthority = _allProjects.FirstOrDefault(p =>
                        p.IsTypicalDetailsAuthority &&
                        !string.IsNullOrEmpty(p.ProjectName) &&
                        !string.IsNullOrEmpty(_portfolioProject.ProjectName) &&
                        p.ProjectName != _portfolioProject.ProjectName);

                    if (currentAuthority != null)
                    {
                        var result = MessageBox.Show(
                            $"{currentAuthority.ProjectName} is currently the Typical Details authority.\n\nChange to {_portfolioProject.ProjectName} instead?",
                            "Change Typical Details Authority",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.No)
                        {
                            return;
                        }
                    }
                }
            }

            // Set the properties that the caller will read
            ProjectNickname = newNickname;
            IsTypicalDetailsAuthority = newAuthority;

            System.Diagnostics.Debug.WriteLine($"Dialog properties set - ProjectNickname: '{ProjectNickname}', IsTypicalDetailsAuthority: {IsTypicalDetailsAuthority}");

            this.DialogResult = true;
            Close();
        }
    }
}