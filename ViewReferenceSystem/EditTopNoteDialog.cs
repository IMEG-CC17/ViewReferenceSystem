using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ViewReferenceSystem.Models;

namespace ViewReferenceSystem.UI
{
    /// <summary>
    /// IMPROVED: Simple dialog for editing top notes - Enter to save, single line only
    /// </summary>
    public class EditTopNoteDialog : Window
    {
        private TextBox _textBox;

        public string TopNote { get; private set; }
        public bool WasSaved { get; private set; } = false;

        public EditTopNoteDialog(string currentTopNote = "")
        {
            TopNote = currentTopNote ?? "";

            Title = "Edit Top Note";
            Width = 400;
            Height = 170;  // Smaller height for single line
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Margin = new Thickness(15);

            // Instructions
            var instructions = new TextBlock
            {
                Text = "Enter the top note text (Press Enter to save, Esc to cancel):",
                Margin = new Thickness(0, 0, 0, 10),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetRow(instructions, 0);
            grid.Children.Add(instructions);

            // Text box - IMPROVED: Single line, no line breaks, Enter to save
            _textBox = new TextBox
            {
                Text = TopNote,
                Margin = new Thickness(0, 0, 0, 15),
                Padding = new Thickness(5),
                FontSize = 14,
                AcceptsReturn = false,  // No line breaks
                AcceptsTab = false,     // No tabs
                TextWrapping = TextWrapping.NoWrap  // No wrapping
            };

            // IMPROVED: Handle Enter and Esc keys directly on textbox
            _textBox.KeyDown += TextBox_KeyDown;
            Grid.SetRow(_textBox, 1);
            grid.Children.Add(_textBox);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelButton = new Button
            {
                Content = "Cancel (Esc)",
                Width = 85,
                Height = 25,
                Margin = new Thickness(0, 0, 10, 0)
            };
            cancelButton.Click += (s, e) => CancelEdit();

            var saveButton = new Button
            {
                Content = "Save (Enter)",
                Width = 85,
                Height = 25,
                IsDefault = true  // Makes Enter work on this button as fallback
            };
            saveButton.Click += (s, e) => SaveEdit();

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(saveButton);
            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            Content = grid;

            // Focus text box when loaded and select all text
            Loaded += (s, e) =>
            {
                _textBox.Focus();
                _textBox.SelectAll();
            };
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Save and close on Enter
                SaveEdit();
                e.Handled = true;  // Prevent the beep sound
            }
            else if (e.Key == Key.Escape)
            {
                // Cancel on Escape
                CancelEdit();
                e.Handled = true;
            }
        }

        private void SaveEdit()
        {
            TopNote = _textBox.Text?.Trim() ?? "";
            WasSaved = true;
            DialogResult = true;  // This will make ShowDialog() return true
            Close();
        }

        private void CancelEdit()
        {
            WasSaved = false;
            DialogResult = false;  // This will make ShowDialog() return false
            Close();
        }

        // COMPATIBILITY: Keep the second constructor for existing code
        public EditTopNoteDialog(ViewInfo viewInfo, object placeholderParameter = null)
            : this(viewInfo?.TopNote ?? "")
        {
            // The second parameter is ignored but allows calls like new EditTopNoteDialog(viewInfo, null)
            // This maintains compatibility with existing code that passes two arguments
        }
    }
}