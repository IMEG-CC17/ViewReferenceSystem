// UpdateNotificationCommand.cs
// Handles the "Install Update" button and the publish release dialog (developer only).

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using ViewReferenceSystem.Updater;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ViewReferenceSystem.Commands
{
    [Transaction(TransactionMode.ReadOnly)]
    public class InstallUpdateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Always fetch latest version info from Firebase
                var latest = UpdaterClient.CheckForUpdate();

                if (latest == null)
                {
                    TaskDialog.Show("Update Check Failed",
                        "Could not reach Firebase to check for updates.\n\n" +
                        "Check your internet connection and try again.");
                    return Result.Failed;
                }

                string currentVer = UpdaterClient.CurrentVersionString;
                bool isNewer = latest.ParsedVersion > UpdaterClient.CurrentVersion;

                // Confirm with user
                var td = new TaskDialog("Update Add-in");
                td.MainIcon = TaskDialogIcon.TaskDialogIconInformation;
                td.MainInstruction = isNewer
                    ? $"Version {latest.Version} is available!"
                    : $"You're on version {currentVer} (latest: {latest.Version})";
                td.MainContent =
                    $"Current version: {currentVer}\n" +
                    $"Firebase version: {latest.Version}\n\n" +
                    (string.IsNullOrEmpty(latest.ReleaseNotes) ? "" : $"Release notes:\n{latest.ReleaseNotes}\n\n") +
                    $"The update will download now. You'll be prompted to close Revit to complete installation.";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    isNewer ? "Download and Install" : "Re-download and Install",
                    "Downloads the latest version and installs automatically when you close Revit.");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Cancel",
                    "");
                td.DefaultButton = TaskDialogResult.CommandLink1;

                if (td.Show() != TaskDialogResult.CommandLink1)
                    return Result.Succeeded;

                // Show progress window and download
                var progressWindow = new UpdateProgressWindow(latest.Version);
                progressWindow.Show();

                // Run download on background thread — do NOT Join() as it deadlocks the UI
                string capturedVersion = latest.Version;
                var thread = new Thread(() =>
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
                        if (!progressWindow.IsVisible) return; // already closed by cancel/timeout
                        progressWindow.Close();

                        if (progressWindow.WasCancelled) return; // user cancelled — silent exit

                        if (error != null || result == null || !result.Success)
                        {
                            string err = error?.Message ?? result?.ErrorMessage ?? "Unknown error";
                            TaskDialog.Show("Update Failed",
                                $"Could not download the update:\n\n{err}\n\nYou can install manually.");
                            return;
                        }

                        bool launched = UpdaterClient.LaunchWaiterScript(result.TempFolder);

                        if (!launched)
                        {
                            TaskDialog.Show("Update Downloaded",
                                $"Files downloaded to:\n{result.TempFolder}\n\n" +
                                $"Could not launch auto-installer. Close Revit and run install_update.ps1 manually.");
                            return;
                        }

                        var finalTd = new TaskDialog("Ready to Install");
                        finalTd.MainIcon = TaskDialogIcon.TaskDialogIconInformation;
                        finalTd.MainInstruction = "Update ready — close Revit to install";
                        finalTd.MainContent =
                            $"Version {capturedVersion} has been downloaded.\n\n" +
                            $"A small installer window is running in the background. " +
                            $"When you close Revit, it will automatically install the update.\n\n" +
                            $"You can save your work and close Revit at any time.";
                        finalTd.CommonButtons = TaskDialogCommonButtons.Ok;
                        finalTd.Show();
                    });
                });
                thread.IsBackground = true;
                thread.Start();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Developer-only: Publish Release Dialog
    // ─────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    public class PublishReleaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var window = new PublishReleaseWindow();
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

    // ─────────────────────────────────────────────────────────────
    // Progress window shown during download
    // ─────────────────────────────────────────────────────────────

    public class UpdateProgressWindow : Window
    {
        private TextBlock _statusText;
        public bool WasCancelled { get; private set; } = false;
        public System.Threading.CancellationTokenSource CancellationSource { get; }
            = new System.Threading.CancellationTokenSource();

        public UpdateProgressWindow(string version)
        {
            Title = "Downloading Update";
            Width = 380;
            Height = 175;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(WpfColor.FromRgb(37, 37, 38));

            // Auto-cancel after 60 seconds
            CancellationSource.CancelAfter(TimeSpan.FromSeconds(60));
            CancellationSource.Token.Register(() =>
                Dispatcher.InvokeAsync(() => { if (IsVisible) { WasCancelled = true; Close(); } }));

            var stack = new StackPanel { Margin = new Thickness(20) };

            stack.Children.Add(new TextBlock
            {
                Text = $"Downloading version {version}...",
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            _statusText = new TextBlock
            {
                Text = "Connecting to Firebase...",
                Foreground = new SolidColorBrush(WpfColor.FromRgb(180, 180, 180)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(_statusText);

            stack.Children.Add(new TextBlock
            {
                Text = "Times out automatically after 60 seconds.",
                Foreground = new SolidColorBrush(WpfColor.FromRgb(100, 100, 100)),
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(WpfColor.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(80, 80, 80)),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            cancelBtn.Click += (s, e) => { WasCancelled = true; CancellationSource.Cancel(); Close(); };
            stack.Children.Add(cancelBtn);

            Content = stack;
        }

        public void SetStatus(string status) => _statusText.Text = status;
    }

    // ─────────────────────────────────────────────────────────────
    // Developer publish window
    // ─────────────────────────────────────────────────────────────

    public class PublishReleaseWindow : Window
    {
        private static readonly WpfColor RevitBg = WpfColor.FromRgb(37, 37, 38);
        private static readonly WpfColor RevitText = WpfColor.FromRgb(241, 241, 241);
        private static readonly WpfColor RevitBorder = WpfColor.FromRgb(63, 63, 70);
        private static readonly WpfColor RevitMuted = WpfColor.FromRgb(150, 150, 150);
        private static readonly WpfColor RevitAccent = WpfColor.FromRgb(0, 122, 204);

        private WpfTextBox _sourceFolderBox;
        private WpfTextBox _versionBox;
        private WpfTextBox _notesBox;
        private TextBlock _statusText;

        public PublishReleaseWindow()
        {
            Title = "Publish Release — Developer Only";
            Width = 480;
            Height = 420;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(RevitBg);

            var stack = new StackPanel { Margin = new Thickness(20) };

            // Warning header
            stack.Children.Add(new TextBlock
            {
                Text = "⚠️  DEVELOPER ONLY — Publishes to Firebase for all users",
                Foreground = new SolidColorBrush(WpfColor.FromRgb(255, 200, 0)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 16)
            });

            // Source folder
            AddLabel(stack, "Source Folder (contains DLL, RFAs, etc.):");
            var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            _sourceFolderBox = MakeTextBox(GetDefaultSourceFolder(), 300);
            var browseBtn = MakeButton("Browse...", 80);
            browseBtn.Click += (s, e) =>
            {
                using (var d = new System.Windows.Forms.FolderBrowserDialog())
                {
                    d.SelectedPath = _sourceFolderBox.Text;
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        _sourceFolderBox.Text = d.SelectedPath;
                }
            };
            folderRow.Children.Add(_sourceFolderBox);
            folderRow.Children.Add(new StackPanel { Width = 6 });
            folderRow.Children.Add(browseBtn);
            stack.Children.Add(folderRow);

            // Version — auto-suggest next version from Firebase
            AddLabel(stack, "Version:");
            var versionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            _versionBox = MakeTextBox("Loading...", 150);
            versionRow.Children.Add(_versionBox);
            var _currentVersionLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(RevitMuted),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            versionRow.Children.Add(_currentVersionLabel);
            var refreshBtn = MakeButton("↺", 30);
            refreshBtn.Margin = new Thickness(6, 0, 0, 0);
            refreshBtn.ToolTip = "Re-read version from Firebase";
            versionRow.Children.Add(refreshBtn);
            stack.Children.Add(versionRow);
            stack.Children.Add(new TextBlock
            {
                Text = "Auto-incremented from current Firebase version.",
                FontSize = 10,
                Foreground = new SolidColorBrush(RevitMuted),
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Reusable action to load version from Firebase
            var capturedLabel = _currentVersionLabel;
            var capturedBox = _versionBox;
            Action loadFirebaseVersion = () =>
            {
                capturedLabel.Text = "Reading Firebase...";
                capturedBox.Text = "...";
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        // Hit Firebase directly — same as test button
                        string token = ViewReferenceSystem.Core.FirebaseClient.GetToken();
                        string url = $"{ViewReferenceSystem.Core.FirebaseClient.DatabaseUrl}/installer/stable.json?auth={token}";
                        var http = new System.Net.Http.HttpClient();
                        http.Timeout = TimeSpan.FromSeconds(10);
                        var response = http.GetAsync(url).Result;
                        string raw = response.Content.ReadAsStringAsync().Result;

                        // Parse version directly from JSON
                        var json = Newtonsoft.Json.Linq.JObject.Parse(raw);
                        string firebaseVer = json["version"]?.ToString();

                        if (string.IsNullOrEmpty(firebaseVer))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                capturedBox.Text = "";
                                capturedLabel.Text = $"❌ No version field in response: {raw}";
                            });
                            return;
                        }

                        string nextVer = IncrementVersion(firebaseVer);
                        Dispatcher.Invoke(() =>
                        {
                            capturedBox.Text = nextVer;
                            capturedLabel.Text = $"(Firebase: {firebaseVer})";
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            capturedBox.Text = "";
                            capturedLabel.Text = $"❌ {ex.Message}";
                        });
                    }
                });
            };

            refreshBtn.Click += (s, e) => loadFirebaseVersion();
            loadFirebaseVersion(); // Load on open


            // Channel — always stable
            AddLabel(stack, "Channel:");
            stack.Children.Add(new TextBlock
            {
                Text = "stable",
                Foreground = new SolidColorBrush(WpfColor.FromRgb(78, 201, 176)),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Release notes
            AddLabel(stack, "Release Notes:");
            _notesBox = new WpfTextBox
            {
                Height = 60,
                Padding = new Thickness(4),
                Background = new SolidColorBrush(RevitBg),
                Foreground = new SolidColorBrush(RevitText),
                BorderBrush = new SolidColorBrush(RevitBorder),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Margin = new Thickness(0, 0, 0, 16)
            };
            stack.Children.Add(_notesBox);

            // Status
            _statusText = new TextBlock
            {
                Foreground = new SolidColorBrush(RevitMuted),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(_statusText);

            // Buttons
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = MakeButton("Cancel", 80);
            cancelBtn.Click += (s, e) => Close();
            var publishBtn = MakeButton("Publish", 100);
            publishBtn.Background = new SolidColorBrush(RevitAccent);
            publishBtn.Click += PublishButton_Click;
            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(new StackPanel { Width = 8 });
            btnRow.Children.Add(publishBtn);
            stack.Children.Add(btnRow);

            Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private void PublishButton_Click(object sender, RoutedEventArgs e)
        {
            string sourceFolder = _sourceFolderBox.Text.Trim();
            string version = _versionBox.Text.Trim();
            string channel = "stable";
            string notes = _notesBox.Text.Trim();

            if (!Directory.Exists(sourceFolder)) { _statusText.Text = "❌ Source folder not found."; return; }
            if (string.IsNullOrEmpty(version)) { _statusText.Text = "❌ Enter a version number."; return; }

            // Confirm
            var result = MessageBox.Show(
                $"Publish version {version} to the '{channel}' channel?\n\nThis will overwrite existing files for all users on this channel.",
                "Confirm Publish", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _statusText.Text = "Publishing...";

            var thread = new Thread(() =>
            {
                try
                {
                    UpdaterClient.PublishRelease(sourceFolder, version, channel, notes, msg =>
                        Dispatcher.Invoke(() => _statusText.Text = msg));

                    Dispatcher.Invoke(() =>
                    {
                        _statusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(78, 201, 176));
                        _statusText.Text = $"✅ Version {version} published to {channel} successfully!";
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        _statusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(220, 80, 80));
                        _statusText.Text = $"❌ Publish failed: {ex.Message}";
                    });
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private string GetDefaultSourceFolder()
        {
            // Check X: drive first — that's where releases are published from
            string[] candidates = new[]
            {
                @"X:\JQProduction\Revit\Program\Program Downloads\View Reference System",
                @"X:\JQProduction\Revit\Program\Program Downloads\View Reference System3.0.0.0 Development",
            };

            // Find the most recent v3.x.x.x folder on X: drive
            foreach (string candidate in candidates)
            {
                try
                {
                    if (!Directory.Exists(candidate)) continue;
                    // Look for latest versioned subfolder
                    var versionFolders = Directory.GetDirectories(candidate, "v3.*")
                        .OrderByDescending(d => d)
                        .ToArray();
                    if (versionFolders.Length > 0) return versionFolders[0];
                    if (File.Exists(Path.Combine(candidate, "ViewReferenceSystem.dll")))
                        return candidate;
                }
                catch { }
            }

            // Fallback to AppData (current install location)
            try { return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { return Environment.GetFolderPath(Environment.SpecialFolder.Desktop); }
        }

        private string IncrementVersion(string version)
        {
            try { var v = new Version(version); return $"{v.Major}.{v.Minor}.{v.Build + 1}"; }
            catch { return version; }
        }

        private void AddLabel(StackPanel parent, string text)
        {
            parent.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(RevitText),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        private WpfTextBox MakeTextBox(string text, double width)
        {
            return new WpfTextBox
            {
                Text = text,
                Width = width,
                Height = 26,
                Padding = new Thickness(4, 2, 4, 2),
                Background = new SolidColorBrush(RevitBg),
                Foreground = new SolidColorBrush(RevitText),
                BorderBrush = new SolidColorBrush(RevitBorder)
            };
        }

        private WpfButton MakeButton(string content, double width)
        {
            return new WpfButton
            {
                Content = content,
                Width = width,
                Height = 26,
                Padding = new Thickness(4, 2, 4, 2),
                Background = new SolidColorBrush(WpfColor.FromRgb(60, 60, 60)),
                Foreground = new SolidColorBrush(RevitText),
                BorderBrush = new SolidColorBrush(RevitBorder),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }
    }
}