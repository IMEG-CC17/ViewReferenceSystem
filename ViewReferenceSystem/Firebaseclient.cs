// FirebaseClient.cs
// Central Firebase client for the Typical Details Plugin.
// ALL Firebase communication goes through this class — nothing else talks to Firebase directly.
//
// Firebase structure:
//   Realtime Database:
//     portfolios/{projectFolder}/{portfolioName}/portfolio    ← portfolio JSON
//
//   Firebase Storage:
//     portfolios/{projectFolder}/{portfolioName}/families/{FamilyName}.rfa
//
// ViewReference_JsonPath stores:  "portfolios/4220156-control-ops/package-1"
// IsFirebasePath() detects this vs a legacy local file path

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace ViewReferenceSystem.Core
{
    public static class FirebaseClient
    {
        #region Configuration
        // These come from App.config in production — hardcoded here for now
        // Will be moved to App.config when we wire this into PortfolioSettings
        public static string DatabaseUrl = "https://view-reference-default-rtdb.firebaseio.com";
        // Firebase Storage bucket — check Firebase Console → Storage for the exact bucket name.
        // Newer projects: view-reference.firebasestorage.app
        // Older projects: view-reference.appspot.com
        public static string StorageUrl = "https://firebasestorage.googleapis.com/v0/b/view-reference.firebasestorage.app";
        public static string ApiKey = "AIzaSyBUrlLM0hyeIqU8KgkPV2jdgJbZuZSaaDM";
        public static string Email = "revit-plugin@imeg-internal.com";
        public static string Password = "Welcom32IM@87";
        #endregion

        #region Auth — token cached and auto-refreshed
        static readonly HttpClient _http = new HttpClient();
        static string _idToken = null;
        static DateTime _tokenExpiry = DateTime.MinValue;
        static readonly object _authLock = new object();

        /// <summary>
        /// Returns a valid auth token, signing in or refreshing as needed.
        /// Thread-safe — safe to call from any context.
        /// </summary>
        public static string GetToken()
        {
            lock (_authLock)
            {
                if (_idToken != null && DateTime.Now < _tokenExpiry)
                    return _idToken;

                System.Diagnostics.Debug.WriteLine("🔑 Firebase: signing in...");

                string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={ApiKey}";

                var body = JsonConvert.SerializeObject(new
                {
                    email = Email,
                    password = Password,
                    returnSecureToken = true
                });

                var response = _http.PostAsync(url,
                    new StringContent(body, Encoding.UTF8, "application/json")).Result;

                string raw = response.Content.ReadAsStringAsync().Result;

                if (!response.IsSuccessStatusCode)
                    throw new FirebaseException($"Auth failed ({response.StatusCode}): {raw}");

                var parsed = JObject.Parse(raw);
                _idToken = parsed["idToken"].ToString();
                _tokenExpiry = DateTime.Now.AddMinutes(55); // token valid 60 min, refresh at 55

                System.Diagnostics.Debug.WriteLine("✅ Firebase: auth token obtained");
                return _idToken;
            }
        }

        /// <summary>
        /// Call this to pre-warm the auth token (e.g. on Revit startup or project open)
        /// so the first sync doesn't pay the auth latency cost.
        /// </summary>
        public static void PreWarmAuth()
        {
            try { GetToken(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Firebase: pre-warm auth failed: {ex.Message}");
            }
        }
        #endregion

        #region Path Utilities

        /// <summary>
        /// Returns true if the stored path is a Firebase node path rather than a local file path.
        /// Firebase paths always start with "portfolios/"
        /// </summary>
        public static bool IsFirebasePath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   path.TrimStart().StartsWith("portfolios/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parse a Firebase path into its project folder and portfolio name components.
        /// "portfolios/4220156-control-ops/package-1" → ("4220156-control-ops", "package-1")
        /// </summary>
        public static bool TryParsePath(string firebasePath, out string projectFolder, out string portfolioName)
        {
            projectFolder = null;
            portfolioName = null;

            if (!IsFirebasePath(firebasePath)) return false;

            // Strip leading "portfolios/"
            string remainder = firebasePath.Substring("portfolios/".Length);
            int slash = remainder.IndexOf('/');

            if (slash < 0) return false; // need both folder and name

            projectFolder = remainder.Substring(0, slash);
            portfolioName = remainder.Substring(slash + 1);

            return !string.IsNullOrEmpty(projectFolder) && !string.IsNullOrEmpty(portfolioName);
        }

        /// <summary>
        /// Build a Firebase path from folder + portfolio name.
        /// </summary>
        public static string BuildPath(string projectFolder, string portfolioName)
        {
            return $"portfolios/{projectFolder}/{portfolioName}";
        }

        /// <summary>
        /// Sanitize a user-entered name for use as a Firebase node key.
        /// Replaces spaces and invalid chars with hyphens, lowercases.
        /// </summary>
        public static string SanitizeName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var sb = new StringBuilder();
            foreach (char c in input.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '-')
                    sb.Append(c);
                else if (c == ' ' || c == '_')
                    sb.Append('-');
                // strip everything else
            }

            // Collapse multiple hyphens
            string result = sb.ToString();
            while (result.Contains("--"))
                result = result.Replace("--", "-");

            return result.Trim('-');
        }

        /// <summary>
        /// Check if a Firebase path already exists (has data).
        /// Use before creating a new portfolio to prevent duplicates.
        /// </summary>
        public static bool PathExists(string firebasePath)
        {
            try
            {
                string raw = GetJson(firebasePath + "/portfolio");
                return !string.IsNullOrEmpty(raw) && raw != "null";
            }
            catch { return false; }
        }

        #endregion

        #region Realtime Database — Portfolio JSON

        /// <summary>
        /// Read the portfolio JSON from Firebase.
        /// firebasePath = "portfolios/4220156-control-ops/package-1"
        /// </summary>
        public static string ReadPortfolio(string firebasePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"📥 Firebase: reading portfolio from {firebasePath}");
                string raw = GetJson(firebasePath + "/portfolio");

                if (string.IsNullOrEmpty(raw) || raw == "null")
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Firebase: no data at {firebasePath}/portfolio");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Firebase: portfolio read ({raw.Length:N0} chars)");
                return raw;
            }
            catch (Exception ex)
            {
                throw new FirebaseException($"Failed to read portfolio from {firebasePath}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Write the portfolio JSON to Firebase.
        /// Overwrites the entire portfolio node.
        /// </summary>
        public static void WritePortfolio(string firebasePath, string json)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"💾 Firebase: writing portfolio to {firebasePath} ({json.Length:N0} chars)");
                PutJson(firebasePath + "/portfolio", json);
                System.Diagnostics.Debug.WriteLine($"✅ Firebase: portfolio written");
            }
            catch (Exception ex)
            {
                throw new FirebaseException($"Failed to write portfolio to {firebasePath}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Delete the entire portfolio node from Firebase.
        /// Used for archiving — caller is responsible for saving data locally first.
        /// </summary>
        public static void DeletePortfolio(string firebasePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🗑️ Firebase: deleting portfolio at {firebasePath}");
                DeleteJson(firebasePath);
                System.Diagnostics.Debug.WriteLine($"✅ Firebase: portfolio deleted");
            }
            catch (Exception ex)
            {
                throw new FirebaseException($"Failed to delete portfolio at {firebasePath}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Rename a portfolio by copying to new path and deleting old.
        /// oldPath / newPath = "portfolios/folder/name"
        /// </summary>
        public static void RenamePortfolio(string oldPath, string newPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"✏️ Firebase: renaming {oldPath} → {newPath}");

                // Read old data
                string json = ReadPortfolio(oldPath);
                if (json == null)
                    throw new FirebaseException($"No data found at {oldPath} to rename");

                // Check new path doesn't already exist
                if (PathExists(newPath))
                    throw new FirebaseException($"A portfolio already exists at {newPath}");

                // Write to new path
                WritePortfolio(newPath, json);

                // Copy any storage files
                CopyFamiliesInStorage(oldPath, newPath);

                // Delete old path
                DeletePortfolio(oldPath);

                System.Diagnostics.Debug.WriteLine($"✅ Firebase: rename complete");
            }
            catch (FirebaseException) { throw; }
            catch (Exception ex)
            {
                throw new FirebaseException($"Failed to rename portfolio: {ex.Message}", ex);
            }
        }

        #endregion

        #region Firebase Storage — RFA Family Files

        /// <summary>
        /// Upload an RFA file to Firebase Storage.
        /// familyFileName = "ViewReference.rfa"
        /// firebasePath   = "portfolios/4220156-control-ops/package-1"
        /// </summary>
        public static void UploadFamily(string localRfaPath, string familyFileName, string firebasePath)
        {
            try
            {
                if (!File.Exists(localRfaPath))
                    throw new FileNotFoundException($"RFA file not found: {localRfaPath}");

                byte[] bytes = File.ReadAllBytes(localRfaPath);
                string storagePath = $"{firebasePath}/families/{familyFileName}";
                string encodedPath = Uri.EscapeDataString(storagePath);

                // Use name= query param instead of path — avoids .NET Framework %2F unescaping (Revit 2023)
                string url = $"{StorageUrl}/o?uploadType=media&name={encodedPath}";

                System.Diagnostics.Debug.WriteLine($"⬆️ Firebase Storage: uploading {familyFileName} ({bytes.Length:N0} bytes)");

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(300);
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Headers.Add("Authorization", $"Bearer {GetToken()}");
                    request.Content = new ByteArrayContent(bytes);
                    request.Content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                    var response = http.SendAsync(request).Result;
                    string raw = response.Content.ReadAsStringAsync().Result;

                    if (!response.IsSuccessStatusCode)
                        throw new FirebaseException($"Storage upload failed ({response.StatusCode}): {raw}");
                }

                System.Diagnostics.Debug.WriteLine($"✅ Firebase Storage: {familyFileName} uploaded");
            }
            catch (FirebaseException) { throw; }
            catch (Exception ex)
            {
                throw new FirebaseException($"Failed to upload {familyFileName}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Download an RFA file from Firebase Storage to a local temp path.
        /// Uses the list API to get a mediaLink (avoids .NET Framework %2F path encoding issues).
        /// Returns the local temp file path, or null if the file doesn't exist in Storage.
        /// Caller is responsible for deleting the temp file when done.
        /// </summary>
        public static string DownloadFamily(string familyFileName, string firebasePath)
        {
            try
            {
                string storagePath = $"{firebasePath}/families/{familyFileName}";
                string encodedPath = Uri.EscapeDataString(storagePath);
                // Build URL as a string — RawHttpsDownload sends this WITHOUT passing through System.Uri
                string url = $"{StorageUrl}/o/{encodedPath}?alt=media";

                System.Diagnostics.Debug.WriteLine($"⬇️ Firebase Storage: downloading {familyFileName}");

                byte[] bytes = RawHttpsDownload(url);

                if (bytes == null)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Firebase Storage: {familyFileName} not found");
                    return null;
                }

                string tempPath = Path.Combine(Path.GetTempPath(), familyFileName);
                File.WriteAllBytes(tempPath, bytes);

                System.Diagnostics.Debug.WriteLine($"✅ Firebase Storage: {familyFileName} downloaded to {tempPath}");
                return tempPath;
            }
            catch (FirebaseException) { throw; }
            catch (Exception ex)
            {
                throw new FirebaseException($"Failed to download {familyFileName}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Get the last-modified timestamp of an RFA in Firebase Storage.
        /// Used for staleness detection — compare against locally loaded family.
        /// Returns DateTime.MinValue if the file doesn't exist.
        /// </summary>
        public static DateTime GetFamilyLastModified(string familyFileName, string firebasePath)
        {
            try
            {
                // Use list API with exact prefix — avoids %2F path encoding issues on .NET Framework
                string storagePath = $"{firebasePath}/families/{familyFileName}";
                string prefix = Uri.EscapeDataString(storagePath);
                string url = $"{StorageUrl}/o?prefix={prefix}&maxResults=1";

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(15);
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Authorization", $"Bearer {GetToken()}");

                    var response = http.SendAsync(request).Result;

                    if (!response.IsSuccessStatusCode)
                        return DateTime.MinValue;

                    string raw = response.Content.ReadAsStringAsync().Result;
                    var obj = JObject.Parse(raw);
                    var items = obj["items"] as JArray;
                    if (items == null || items.Count == 0)
                        return DateTime.MinValue;

                    string updated = items[0]["updated"]?.ToString();
                    if (DateTime.TryParse(updated, out DateTime dt))
                        return dt.ToLocalTime();
                }

                return DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Delete an RFA file from Firebase Storage.
        /// Note: Uses path-based URL which may fail on .NET Framework 4.8 (Revit 2022-2024)
        /// due to %2F unescaping. Failures are silently swallowed — upload overwrites anyway.
        /// </summary>
        public static void DeleteFamily(string familyFileName, string firebasePath)
        {
            try
            {
                string storagePath = $"{firebasePath}/families/{familyFileName}";
                string encodedPath = Uri.EscapeDataString(storagePath);
                string url = $"{StorageUrl}/o/{encodedPath}";

                RawHttpsDelete(url);

                System.Diagnostics.Debug.WriteLine($"✅ Firebase Storage: {familyFileName} deleted");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Firebase Storage: could not delete {familyFileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// List all RFA files stored for a portfolio.
        /// Returns list of family file names (e.g. "ViewReference.rfa")
        /// </summary>
        public static List<string> ListFamilies(string firebasePath)
        {
            var result = new List<string>();
            try
            {
                string prefix = Uri.EscapeDataString($"{firebasePath}/families/");
                string url = $"{StorageUrl}/o?prefix={prefix}";

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(15);
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Authorization", $"Bearer {GetToken()}");

                    var response = http.SendAsync(request).Result;
                    if (!response.IsSuccessStatusCode) return result;

                    string raw = response.Content.ReadAsStringAsync().Result;
                    var obj = JObject.Parse(raw);
                    var items = obj["items"] as JArray;
                    if (items == null) return result;

                    foreach (var item in items)
                    {
                        string name = item["name"]?.ToString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            result.Add(Path.GetFileName(name));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Firebase Storage: could not list families: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Get the mediaLink URL for a file in Firebase Storage using the list API.
        /// Uses only query parameters — avoids .NET Framework 4.8 %2F path unescaping.
        /// Returns null if the file doesn't exist.
        /// </summary>
        public static string GetMediaLink(string storagePath)
        {
            string prefix = Uri.EscapeDataString(storagePath);
            string url = $"{StorageUrl}/o?prefix={prefix}&maxResults=1";

            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(30);
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", $"Bearer {GetToken()}");

                var response = http.SendAsync(request).Result;
                if (!response.IsSuccessStatusCode) return null;

                string raw = response.Content.ReadAsStringAsync().Result;
                var obj = JObject.Parse(raw);
                var items = obj["items"] as JArray;
                if (items == null || items.Count == 0) return null;

                // Verify exact match (prefix search could return partial matches)
                string name = items[0]["name"]?.ToString();
                if (name != storagePath) return null;

                return items[0]["mediaLink"]?.ToString();
            }
        }

        /// <summary>
        /// Download bytes from a URL using raw HTTPS, completely bypassing System.Uri.
        /// This avoids .NET Framework 4.8's automatic unescaping of %2F in URL paths,
        /// which breaks Firebase/GCS download URLs.
        /// Returns byte[] on success, null if 404 Not Found.
        /// </summary>
        public static byte[] RawHttpsDownload(string url, string authToken = null)
        {
            if (authToken == null) authToken = GetToken();

            // Parse URL manually — do NOT use System.Uri
            int schemeEnd = url.IndexOf("://");
            if (schemeEnd < 0) throw new FirebaseException("Invalid URL: " + url);
            schemeEnd += 3;

            int pathStart = url.IndexOf('/', schemeEnd);
            if (pathStart < 0) throw new FirebaseException("Invalid URL (no path): " + url);

            string host = url.Substring(schemeEnd, pathStart - schemeEnd);
            string pathAndQuery = url.Substring(pathStart);

            System.Diagnostics.Debug.WriteLine($"🔌 Raw HTTPS GET: {host} {pathAndQuery.Substring(0, Math.Min(80, pathAndQuery.Length))}...");

            using (var tcp = new System.Net.Sockets.TcpClient())
            {
                tcp.Connect(host, 443);
                tcp.SendTimeout = 30000;
                tcp.ReceiveTimeout = 120000;

                using (var ssl = new System.Net.Security.SslStream(tcp.GetStream(), false))
                {
                    ssl.AuthenticateAsClient(host);

                    // Build raw HTTP request — %2F stays exactly as-is
                    string httpReq =
                        $"GET {pathAndQuery} HTTP/1.1\r\n" +
                        $"Host: {host}\r\n" +
                        $"Authorization: Bearer {authToken}\r\n" +
                        $"Accept: */*\r\n" +
                        $"Connection: close\r\n" +
                        $"\r\n";

                    byte[] reqBytes = Encoding.ASCII.GetBytes(httpReq);
                    ssl.Write(reqBytes, 0, reqBytes.Length);
                    ssl.Flush();

                    // Read entire response (server closes connection due to Connection: close)
                    using (var ms = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192];
                        int read;
                        while ((read = ssl.Read(buffer, 0, buffer.Length)) > 0)
                            ms.Write(buffer, 0, read);

                        byte[] all = ms.ToArray();

                        // Find header/body boundary (\r\n\r\n)
                        int bodyStart = -1;
                        for (int i = 0; i < all.Length - 3; i++)
                        {
                            if (all[i] == 0x0D && all[i + 1] == 0x0A &&
                                all[i + 2] == 0x0D && all[i + 3] == 0x0A)
                            {
                                bodyStart = i + 4;
                                break;
                            }
                        }
                        if (bodyStart < 0)
                            throw new FirebaseException("Invalid HTTP response (no header boundary)");

                        string headers = Encoding.ASCII.GetString(all, 0, bodyStart);
                        string statusLine = headers.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];

                        System.Diagnostics.Debug.WriteLine($"   Response: {statusLine}");

                        // 404 = file doesn't exist
                        if (statusLine.Contains(" 404"))
                            return null;

                        // Any non-200 = error
                        if (!statusLine.Contains(" 200"))
                            throw new FirebaseException($"Raw download failed: {statusLine}");

                        // Extract body
                        byte[] body = new byte[all.Length - bodyStart];
                        Buffer.BlockCopy(all, bodyStart, body, 0, body.Length);

                        System.Diagnostics.Debug.WriteLine($"   Downloaded {body.Length:N0} bytes");
                        return body;
                    }
                }
            }
        }

        /// <summary>
        /// Send a raw HTTPS DELETE request, bypassing System.Uri.
        /// Returns true if deleted (204) or already gone (404).
        /// </summary>
        public static bool RawHttpsDelete(string url, string authToken = null)
        {
            if (authToken == null) authToken = GetToken();

            int schemeEnd = url.IndexOf("://") + 3;
            int pathStart = url.IndexOf('/', schemeEnd);
            string host = url.Substring(schemeEnd, pathStart - schemeEnd);
            string pathAndQuery = url.Substring(pathStart);

            using (var tcp = new System.Net.Sockets.TcpClient())
            {
                tcp.Connect(host, 443);
                using (var ssl = new System.Net.Security.SslStream(tcp.GetStream(), false))
                {
                    ssl.AuthenticateAsClient(host);

                    string httpReq =
                        $"DELETE {pathAndQuery} HTTP/1.1\r\n" +
                        $"Host: {host}\r\n" +
                        $"Authorization: Bearer {authToken}\r\n" +
                        $"Connection: close\r\n" +
                        $"\r\n";

                    byte[] reqBytes = Encoding.ASCII.GetBytes(httpReq);
                    ssl.Write(reqBytes, 0, reqBytes.Length);
                    ssl.Flush();

                    using (var ms = new MemoryStream())
                    {
                        byte[] buffer = new byte[4096];
                        int read;
                        while ((read = ssl.Read(buffer, 0, buffer.Length)) > 0)
                            ms.Write(buffer, 0, read);

                        string response = Encoding.ASCII.GetString(ms.ToArray());
                        string statusLine = response.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];

                        return statusLine.Contains(" 204") || statusLine.Contains(" 404") || statusLine.Contains(" 200");
                    }
                }
            }
        }

        /// <summary>
        /// Copy all RFA files from one portfolio path to another.
        /// Used during portfolio rename.
        /// </summary>
        private static void CopyFamiliesInStorage(string sourcePath, string destPath)
        {
            try
            {
                var families = ListFamilies(sourcePath);
                System.Diagnostics.Debug.WriteLine($"   Copying {families.Count} RFA files...");

                foreach (string familyFile in families)
                {
                    string tempPath = DownloadFamily(familyFile, sourcePath);
                    if (tempPath != null)
                    {
                        try
                        {
                            UploadFamily(tempPath, familyFile, destPath);
                        }
                        finally
                        {
                            try { File.Delete(tempPath); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Could not copy families during rename: {ex.Message}");
                // Non-fatal — portfolio JSON was already copied
            }
        }

        #endregion

        #region Archive / Restore

        /// <summary>
        /// Archive a portfolio to a local folder and remove it from Firebase.
        /// localArchiveFolder = full path to destination folder (will be created)
        /// Returns path to saved portfolio.json
        /// </summary>
        public static string ArchivePortfolio(string firebasePath, string localArchiveFolder)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"📦 Archiving {firebasePath} → {localArchiveFolder}");

                Directory.CreateDirectory(localArchiveFolder);
                string familiesFolder = Path.Combine(localArchiveFolder, "families");
                Directory.CreateDirectory(familiesFolder);

                // 1. Save portfolio JSON
                string json = ReadPortfolio(firebasePath);
                if (json == null)
                    throw new FirebaseException($"No portfolio data found at {firebasePath}");

                string jsonPath = Path.Combine(localArchiveFolder, "portfolio.json");
                File.WriteAllText(jsonPath, json);

                // 2. Save all RFA files
                var families = ListFamilies(firebasePath);
                foreach (string familyFile in families)
                {
                    string tempPath = DownloadFamily(familyFile, firebasePath);
                    if (tempPath != null)
                    {
                        try
                        {
                            File.Copy(tempPath, Path.Combine(familiesFolder, familyFile), overwrite: true);
                        }
                        finally
                        {
                            try { File.Delete(tempPath); } catch { }
                        }
                    }
                }

                // 3. Write archive manifest
                var manifest = new
                {
                    OriginalFirebasePath = firebasePath,
                    ArchivedOn = DateTime.Now.ToString("o"),
                    ArchivedBy = Environment.UserName,
                    Machine = Environment.MachineName,
                    FamilyCount = families.Count
                };
                File.WriteAllText(
                    Path.Combine(localArchiveFolder, "archive_manifest.json"),
                    JsonConvert.SerializeObject(manifest, Formatting.Indented));

                // 4. Delete from Firebase
                DeletePortfolio(firebasePath);

                System.Diagnostics.Debug.WriteLine($"✅ Archive complete: {localArchiveFolder}");
                return jsonPath;
            }
            catch (FirebaseException) { throw; }
            catch (Exception ex)
            {
                throw new FirebaseException($"Archive failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Restore an archived portfolio from a local folder back to Firebase.
        /// Reads archive_manifest.json to get the original path (or override with newFirebasePath).
        /// </summary>
        public static void RestorePortfolio(string localArchiveFolder, string newFirebasePath = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"📦 Restoring archive from {localArchiveFolder}");

                // Read manifest to find original path
                string manifestPath = Path.Combine(localArchiveFolder, "archive_manifest.json");
                string firebasePath = newFirebasePath;

                if (firebasePath == null && File.Exists(manifestPath))
                {
                    var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                    firebasePath = manifest["OriginalFirebasePath"]?.ToString();
                }

                if (string.IsNullOrEmpty(firebasePath))
                    throw new FirebaseException("Could not determine Firebase path for restore. Provide a path explicitly.");

                if (PathExists(firebasePath))
                    throw new FirebaseException($"A portfolio already exists at {firebasePath}. Delete it first or choose a different path.");

                // 1. Restore portfolio JSON
                string jsonPath = Path.Combine(localArchiveFolder, "portfolio.json");
                if (!File.Exists(jsonPath))
                    throw new FirebaseException($"No portfolio.json found in archive folder: {localArchiveFolder}");

                string json = File.ReadAllText(jsonPath);
                WritePortfolio(firebasePath, json);

                // 2. Restore RFA files
                string familiesFolder = Path.Combine(localArchiveFolder, "families");
                if (Directory.Exists(familiesFolder))
                {
                    foreach (string rfaPath in Directory.GetFiles(familiesFolder, "*.rfa"))
                    {
                        UploadFamily(rfaPath, Path.GetFileName(rfaPath), firebasePath);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"✅ Restore complete: {firebasePath}");
            }
            catch (FirebaseException) { throw; }
            catch (Exception ex)
            {
                throw new FirebaseException($"Restore failed: {ex.Message}", ex);
            }
        }

        #endregion

        #region Portfolio Discovery

        /// <summary>
        /// List all project folders under portfolios/ in Firebase.
        /// Returns list of folder names (e.g. "4220156-control-ops")
        /// </summary>
        public static List<string> ListProjectFolders()
        {
            try
            {
                string token = GetToken();
                string url = $"{DatabaseUrl}/portfolios.json?auth={token}&shallow=true";
                var response = _http.GetAsync(url).Result;
                response.EnsureSuccessStatusCode();
                string raw = response.Content.ReadAsStringAsync().Result;

                if (string.IsNullOrEmpty(raw) || raw == "null")
                    return new List<string>();

                var obj = JObject.Parse(raw);
                return obj.Properties().Select(p => p.Name).OrderBy(n => n).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Firebase: could not list project folders: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// List all portfolio names under a specific project folder.
        /// e.g. ListPortfoliosInFolder("4220156-control-ops") → ["package-1", "package-2"]
        /// </summary>
        public static List<string> ListPortfoliosInFolder(string projectFolder)
        {
            try
            {
                string token = GetToken();
                string url = $"{DatabaseUrl}/portfolios/{projectFolder}.json?auth={token}&shallow=true";
                var response = _http.GetAsync(url).Result;
                response.EnsureSuccessStatusCode();
                string raw = response.Content.ReadAsStringAsync().Result;

                if (string.IsNullOrEmpty(raw) || raw == "null")
                    return new List<string>();

                var obj = JObject.Parse(raw);
                return obj.Properties().Select(p => p.Name).OrderBy(n => n).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Firebase: could not list portfolios in {projectFolder}: {ex.Message}");
                return new List<string>();
            }
        }

        #endregion

        #region Low-level HTTP helpers

        static string DbNodeUrl(string node) =>
            $"{DatabaseUrl}/{node}.json?auth={GetToken()}";

        static string GetJson(string node)
        {
            var response = _http.GetAsync(DbNodeUrl(node)).Result;
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().Result;
        }

        static void PutJson(string node, string json)
        {
            var response = _http.PutAsync(DbNodeUrl(node),
                new StringContent(json, Encoding.UTF8, "application/json")).Result;
            response.EnsureSuccessStatusCode();
        }

        static void PatchJson(string node, string json)
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), DbNodeUrl(node))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            _http.SendAsync(request).Result.EnsureSuccessStatusCode();
        }

        static void DeleteJson(string node)
        {
            var response = _http.DeleteAsync(DbNodeUrl(node)).Result;
            response.EnsureSuccessStatusCode();
        }

        #endregion
    }

    #region Firebase Exception

    public class FirebaseException : Exception
    {
        public FirebaseException(string message) : base(message) { }
        public FirebaseException(string message, Exception inner) : base(message, inner) { }
    }

    #endregion
}