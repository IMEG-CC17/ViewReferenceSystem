// UpdaterClient.cs
// Handles version checking and update downloading from Firebase.
// Uses the same Firebase infrastructure as the portfolio system.
//
// Firebase structure:
//   Realtime Database:
//     installer/
//       stable/ { version, releaseNotes, updatedAt }
//       beta/   { version, releaseNotes, updatedAt }
//
//   Firebase Storage:
//     installer/
//       stable/
//         ViewReferenceSystem.dll
//         ViewReferenceSystem.addin
//         ViewReferenceSystem.dll.config
//         Newtonsoft.Json.dll
//         DetailReferenceFamily.rfa
//         Microsoft.CodeAnalysis.dll          (Roslyn — AI Family Generator on 2025+)
//         Microsoft.CodeAnalysis.CSharp.dll   (Roslyn — AI Family Generator on 2025+)
//       beta/
//         (same files)

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ViewReferenceSystem.Core;

namespace ViewReferenceSystem.Updater
{
    public static class UpdaterClient
    {
        #region Configuration

        /// <summary>
        /// Files to download during an update — in install order.
        /// Roslyn DLLs are optional — only needed for AI Family Generator on Revit 2025+.
        /// NOTE: Roslyn transitive dependencies (System.Collections.Immutable, System.Reflection.Metadata, etc.)
        /// were removed — they caused assembly version conflicts that crashed Revit at startup.
        /// Revit 2025+ (.NET 8) already has these assemblies in-box.
        /// </summary>
        public static readonly List<string> UpdateFiles = new List<string>
        {
            // Core addin files (required)
            "ViewReferenceSystem.dll",
            "ViewReferenceSystem.addin",
            "ViewReferenceSystem.dll.config",
            "Newtonsoft.Json.dll",
            "DetailReferenceFamily.rfa",

            // Roslyn compiler DLLs (optional — AI Family Generator on Revit 2025+)
            "Microsoft.CodeAnalysis.dll",
            "Microsoft.CodeAnalysis.CSharp.dll"
        };

        /// <summary>
        /// Required files — if any of these fail to download, abort the update.
        /// Roslyn DLLs are NOT required — they're only for AI Family Generator.
        /// </summary>
        private static readonly HashSet<string> RequiredFiles = new HashSet<string>
        {
            "ViewReferenceSystem.dll",
            "ViewReferenceSystem.addin",
            "Newtonsoft.Json.dll",
            "DetailReferenceFamily.rfa"
        };

        private const string RegistryKey = @"Software\IMEG\ViewReferenceSystem";
        private const string RegistryChannelValue = "UpdateChannel";

        /// <summary>
        /// Get the update channel from the registry.
        /// Registry survives reinstalls — App.config gets overwritten by the installer.
        /// 
        /// To set your machine to beta:
        ///   reg add "HKCU\Software\IMEG\ViewReferenceSystem" /v UpdateChannel /t REG_SZ /d beta /f
        /// 
        /// To reset to stable:
        ///   reg add "HKCU\Software\IMEG\ViewReferenceSystem" /v UpdateChannel /t REG_SZ /d stable /f
        /// </summary>
        public static string Channel
        {
            get
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKey))
                    {
                        if (key != null)
                        {
                            string regValue = key.GetValue(RegistryChannelValue) as string;
                            if (!string.IsNullOrWhiteSpace(regValue))
                            {
                                System.Diagnostics.Debug.WriteLine($"🔧 UpdateChannel from registry: {regValue}");
                                return regValue.ToLower().Trim();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Could not read registry channel: {ex.Message}");
                }

                // Registry not set — ensure it's written as "stable" for future reference
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryKey))
                        key?.SetValue(RegistryChannelValue, "stable");
                }
                catch { }

                return "stable";
            }
        }

        /// <summary>
        /// Set the update channel in the registry.
        /// Call this from a developer admin UI to switch channels without regedit.
        /// </summary>
        public static void SetChannel(string channel)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryKey))
                    key?.SetValue(RegistryChannelValue, channel.ToLower().Trim());
                System.Diagnostics.Debug.WriteLine($"✅ UpdateChannel set to: {channel}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Could not set registry channel: {ex.Message}");
            }
        }

        /// <summary>
        /// Current installed version — read directly from the assembly version.
        /// </summary>
        public static Version CurrentVersion
        {
            get
            {
                try
                {
                    return Assembly.GetExecutingAssembly().GetName().Version;
                }
                catch { }
                return new Version(0, 0, 0);
            }
        }

        public static string CurrentVersionString => CurrentVersion.ToString(3);

        #endregion

        #region Version Check

        public class VersionInfo
        {
            public string Version { get; set; }
            public string ReleaseNotes { get; set; }
            public string UpdatedAt { get; set; }

            [JsonIgnore]
            public Version ParsedVersion
            {
                get
                {
                    try { return new Version(Version); }
                    catch { return new Version(0, 0, 0); }
                }
            }
        }

        /// <summary>
        /// Check Firebase for the latest version on the current channel.
        /// Returns null if check fails or no update available.
        /// </summary>
        public static VersionInfo CheckForUpdate()
        {
            try
            {
                string channel = Channel;
                string token = FirebaseClient.GetToken();
                string url = $"{FirebaseClient.DatabaseUrl}/installer/{channel}.json?auth={token}";

                System.Diagnostics.Debug.WriteLine($"🔍 Updater: checking {channel} channel...");

                var http = new HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                string json = http.GetStringAsync(url).Result;

                if (string.IsNullOrEmpty(json) || json == "null")
                {
                    System.Diagnostics.Debug.WriteLine($"   ℹ️ No version info found for {channel}");
                    return null;
                }

                var info = JsonConvert.DeserializeObject<VersionInfo>(json);
                if (info == null || string.IsNullOrEmpty(info.Version))
                    return null;

                System.Diagnostics.Debug.WriteLine($"   📦 Server: {info.Version}, Local: {CurrentVersionString}");

                if (info.ParsedVersion > CurrentVersion)
                {
                    System.Diagnostics.Debug.WriteLine($"   🆕 Update available: {info.Version}");
                    return info;
                }

                System.Diagnostics.Debug.WriteLine($"   ✅ Up to date");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Updater: version check failed: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Download

        public class DownloadResult
        {
            public bool Success { get; set; }
            public string TempFolder { get; set; }
            public List<string> DownloadedFiles { get; set; } = new List<string>();
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// Download all update files from Firebase Storage to a temp folder.
        /// Returns the temp folder path on success.
        /// </summary>
        public static DownloadResult DownloadUpdate(string version, Action<string> progressCallback = null)
        {
            var result = new DownloadResult();

            try
            {
                // Create temp folder for this version
                string tempFolder = Path.Combine(Path.GetTempPath(), $"VRS_Update_{version}");
                if (Directory.Exists(tempFolder))
                    Directory.Delete(tempFolder, true);
                Directory.CreateDirectory(tempFolder);

                result.TempFolder = tempFolder;

                string storagePath = $"installer/stable";

                progressCallback?.Invoke($"Downloading version {version}...");
                System.Diagnostics.Debug.WriteLine($"⬇️ Updater: downloading {UpdateFiles.Count} files from {storagePath}");

                foreach (string fileName in UpdateFiles)
                {
                    progressCallback?.Invoke($"Downloading {fileName}...");
                    try
                    {
                        string localPath = DownloadInstallerFile(fileName, storagePath, tempFolder);

                        if (localPath != null)
                        {
                            result.DownloadedFiles.Add(fileName);
                            System.Diagnostics.Debug.WriteLine($"   ✅ Downloaded: {fileName}");
                        }
                        else
                        {
                            // File not found in Storage
                            if (RequiredFiles.Contains(fileName))
                            {
                                result.ErrorMessage = $"Required file not found in Firebase Storage: {fileName}";
                                return result;
                            }
                            System.Diagnostics.Debug.WriteLine($"   ⏭️ Skipped optional: {fileName}");
                        }
                    }
                    catch (Exception fileEx)
                    {
                        if (RequiredFiles.Contains(fileName))
                        {
                            result.ErrorMessage = $"Failed to download {fileName}: {fileEx.Message}";
                            return result;
                        }
                        System.Diagnostics.Debug.WriteLine($"   ⚠️ Optional file failed: {fileName}: {fileEx.Message}");
                    }
                }

                WriteWaiterScript(tempFolder, version);
                result.Success = true;
                System.Diagnostics.Debug.WriteLine($"✅ Updater: all files downloaded to {tempFolder}");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Download failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ Updater: download failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Download a single installer file from Firebase Storage to the temp folder.
        /// Returns the local file path, or null if the file doesn't exist in Storage.
        /// </summary>
        private static string DownloadInstallerFile(string fileName, string storagePath, string tempFolder)
        {
            string fullStoragePath = $"{storagePath}/{fileName}";
            string encodedPath = Uri.EscapeDataString(fullStoragePath);
            // Build URL as a string — RawHttpsDownload sends this WITHOUT passing through System.Uri
            string url = $"{FirebaseClient.StorageUrl}/o/{encodedPath}?alt=media";

            byte[] bytes = FirebaseClient.RawHttpsDownload(url);
            if (bytes == null || bytes.Length == 0)
                return null;

            string localPath = Path.Combine(tempFolder, fileName);
            File.WriteAllBytes(localPath, bytes);
            return localPath;
        }

        #endregion

        #region Install Script

        /// <summary>
        /// Write a PowerShell script that waits for Revit to close, then copies files.
        /// This avoids file-lock issues during the update.
        /// </summary>
        public static void WriteWaiterScript(string tempFolder, string version)
        {
            string scriptPath = Path.Combine(tempFolder, "install_update.ps1");

            string sourceFolder = tempFolder.Replace("\\", "\\\\");

            string scriptContent = $@"
# VRS Update Installer — waits for Revit to close, then copies files
# Version: {version}

Write-Host ""======================================""
Write-Host ""  VRS Update Installer v{version}""
Write-Host ""======================================""
Write-Host """"
Write-Host ""Waiting for Revit to close...""

$maxWait = 300  # seconds
$elapsed = 0
while ($elapsed -lt $maxWait) {{
    $revitProcs = Get-Process -Name ""Revit"" -ErrorAction SilentlyContinue
    if (-not $revitProcs) {{ break }}
    Start-Sleep -Seconds 2
    $elapsed += 2
    if ($elapsed % 10 -eq 0) {{
        Write-Host ""  Still waiting... ($elapsed seconds)""
    }}
}}

if ($elapsed -ge $maxWait) {{
    Write-Host ""Timeout waiting for Revit to close. Aborting."" -ForegroundColor Red
    Write-Host ""Press any key to close...""
    $null = $Host.UI.RawUI.ReadKey(""NoEcho,IncludeKeyDown"")
    exit 1
}}

Write-Host ""Revit closed. Installing update..."" -ForegroundColor Green
Write-Host """"

$sourceFolder = ""{sourceFolder}""
$maxRetries = 3
$retryDelay = 2
$anySuccess = $false

# Find all Revit addins folders
$revitVersions = @(""2022"", ""2023"", ""2024"", ""2025"")
foreach ($revitVer in $revitVersions) {{
    $addinsPath = ""$env:APPDATA\Autodesk\Revit\Addins\$revitVer""
    if (-not (Test-Path ""$addinsPath\ViewReferenceSystem.dll"")) {{
        continue
    }}

    Write-Host ""Installing to Revit $revitVer..."" -ForegroundColor Cyan

    # Ensure addins folder exists
    if (-not (Test-Path $addinsPath)) {{
        New-Item -ItemType Directory -Path $addinsPath -Force | Out-Null
    }}

    $allFilesOk = $true

    # Install each file with retry logic
    $filesToInstall = Get-ChildItem -Path $sourceFolder -File | Where-Object {{ $_.Name -ne ""install_update.ps1"" }}

    foreach ($file in $filesToInstall) {{
        $destPath = Join-Path $addinsPath $file.Name
        $success = $false

        for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {{
            try {{
                Copy-Item -Path $file.FullName -Destination $destPath -Force -ErrorAction Stop
                Write-Host ""  ✅ $($file.Name)"" -ForegroundColor Green
                $success = $true
                break
            }}
            catch {{
                if ($attempt -lt $maxRetries) {{
                    Write-Host ""  ⏳ $($file.Name) locked, retrying ($attempt/$maxRetries)..."" -ForegroundColor Yellow
                    Start-Sleep -Seconds $retryDelay
                }} else {{
                    Write-Host ""  ❌ $($file.Name) failed after $maxRetries attempts: $($_.Exception.Message)"" -ForegroundColor Red
                    $allFilesOk = $false
                }}
            }}
        }}
    }}

    if ($allFilesOk) {{
        Write-Host ""  ✅ Revit $revitVer complete"" -ForegroundColor Green
        $anySuccess = $true
    }} else {{
        Write-Host ""  ⚠️  Revit $revitVer partially installed"" -ForegroundColor Yellow
    }}

    Write-Host """"
}}

Write-Host ""======================================""
if ($anySuccess) {{
    Write-Host ""  ✅ Installation complete!"" -ForegroundColor Green
    Write-Host ""  You can now restart Revit."" -ForegroundColor Green
}} else {{
    Write-Host ""  ❌ Installation failed."" -ForegroundColor Red
    Write-Host ""  Please run the installer manually."" -ForegroundColor Red
}}
Write-Host ""======================================""
Write-Host """"
Write-Host ""Press any key to close...""
$null = $Host.UI.RawUI.ReadKey(""NoEcho,IncludeKeyDown"")

# Clean up temp folder
try {{ Remove-Item -Path $sourceFolder -Recurse -Force -ErrorAction SilentlyContinue }} catch {{}}
";

            File.WriteAllText(scriptPath, scriptContent);
            System.Diagnostics.Debug.WriteLine($"   📝 Waiter script written to: {scriptPath}");
        }

        /// <summary>
        /// Launch the PowerShell waiter script in a visible console window.
        /// Call this BEFORE telling the user to close Revit.
        /// </summary>
        public static bool LaunchWaiterScript(string tempFolder)
        {
            try
            {
                string scriptPath = Path.Combine(tempFolder, "install_update.ps1");
                if (!File.Exists(scriptPath))
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Updater: waiter script not found at {scriptPath}");
                    return false;
                }

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = true,  // Shows the console window
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
                };

                System.Diagnostics.Process.Start(psi);
                System.Diagnostics.Debug.WriteLine($"✅ Updater: waiter script launched");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Updater: could not launch waiter script: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Upload (Developer Only)

        /// <summary>
        /// Upload a new version to Firebase Storage.
        /// Only used by the developer — called from a hidden admin button.
        /// sourceFolder should contain all the files to upload.
        /// </summary>
        public static void PublishRelease(string sourceFolder, string version, string channel,
            string releaseNotes, Action<string> progressCallback = null)
        {
            progressCallback?.Invoke($"Publishing {version} to {channel} channel...");

            string storagePath = $"installer/{channel}";

            // Upload each file
            foreach (string fileName in UpdateFiles)
            {
                string localPath = Path.Combine(sourceFolder, fileName);
                if (!File.Exists(localPath))
                {
                    if (RequiredFiles.Contains(fileName))
                        throw new Exception($"Required file not found: {localPath}");
                    else
                    {
                        progressCallback?.Invoke($"Skipping optional {fileName} (not found)");
                        continue;
                    }
                }

                progressCallback?.Invoke($"Uploading {fileName}...");
                UploadInstallerFile(localPath, fileName, storagePath);
                System.Diagnostics.Debug.WriteLine($"   ✅ Uploaded: {fileName}");
            }

            // Update version info in Realtime Database
            progressCallback?.Invoke("Updating version info...");

            var versionInfo = new
            {
                version = version,
                releaseNotes = releaseNotes,
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd"),
                publishedBy = Environment.UserName
            };

            string json = JsonConvert.SerializeObject(versionInfo, Formatting.Indented);
            string token = FirebaseClient.GetToken();
            string url = $"{FirebaseClient.DatabaseUrl}/installer/{channel}.json?auth={token}";

            var http = new HttpClient();
            var response = http.PutAsync(url,
                new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")).Result;
            response.EnsureSuccessStatusCode();

            progressCallback?.Invoke($"✅ Version {version} published to {channel}!");
            System.Diagnostics.Debug.WriteLine($"✅ Updater: published {version} to {channel}");
        }

        private static void UploadInstallerFile(string localPath, string fileName, string storagePath)
        {
            byte[] bytes = File.ReadAllBytes(localPath);
            string fullStoragePath = $"{storagePath}/{fileName}";
            string encodedPath = Uri.EscapeDataString(fullStoragePath);
            string url = $"{FirebaseClient.StorageUrl}/o?uploadType=media&name={encodedPath}";

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(300);

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("Authorization", $"Bearer {FirebaseClient.GetToken()}");
                request.Content = new System.Net.Http.ByteArrayContent(bytes);
                request.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                var response = http.SendAsync(request).Result;
                string raw = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Upload failed ({response.StatusCode}): {raw}");
            }
        }

        #endregion
    }
}