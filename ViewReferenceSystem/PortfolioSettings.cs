// PortfolioSettings.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.Core;

namespace ViewReferenceSystem.Core
{
    /// <summary>
    /// Portfolio Settings - Network Location Only
    /// </summary>
    public static class PortfolioSettings
    {
        #region Constants

        public const string PROJECT_TYPE_PARAMETER = "ViewReference_ProjectType";
        public const string JSON_PATH_PARAMETER = "ViewReference_JsonPath";
        public const string PORTFOLIO_GUID_PARAMETER = "ViewReference_PortfolioGuid";
        public const string LAST_SYNC_PARAMETER = "ViewReference_LastSync";
        public const string PROJECT_GUID_PARAMETER = "ViewReference_ProjectGuid";
        public const string PROJECT_TYPE_PORTFOLIO = "Portfolio";

        // Default monitored family
        public const string DEFAULT_MONITORED_FAMILY = "DetailReferenceFamily";
        public const string DEFAULT_MONITORED_FAMILY_FILE = "DetailReferenceFamily.rfa";

        #endregion

        #region File Management

        public static bool SavePortfolioToFile(Portfolio data, string filePath)
        {
            try
            {
                if (data == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Cannot save null portfolio data");
                    return false;
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    System.Diagnostics.Debug.WriteLine("❌ Cannot save to null/empty file path");
                    return false;
                }

                data.LastModified = DateTime.Now;
                data.LastUpdated = DateTime.Now;

                string jsonContent = JsonConvert.SerializeObject(data, Formatting.Indented);

                // ── Firebase path ────────────────────────────────────────────
                if (FirebaseClient.IsFirebasePath(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"💾 Saving portfolio to Firebase: {filePath}");
                    FirebaseClient.WritePortfolio(filePath, jsonContent);
                    System.Diagnostics.Debug.WriteLine($"✅ Portfolio saved to Firebase: {filePath}");
                    return true;
                }

                // ── Local file path ──────────────────────────────────────────
                System.Diagnostics.Debug.WriteLine($"💾 Saving portfolio to: {filePath}");

                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, jsonContent);
                System.Diagnostics.Debug.WriteLine($"✅ Portfolio saved to file: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error saving portfolio to {filePath}: {ex.Message}");
                return false;
            }
        }

        public static Portfolio LoadPortfolioFromFile(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    System.Diagnostics.Debug.WriteLine("❌ Cannot load from null/empty file path");
                    return null;
                }

                // ── Firebase path ────────────────────────────────────────────
                if (FirebaseClient.IsFirebasePath(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"📥 Loading portfolio from Firebase: {filePath}");
                    string firebaseJson = FirebaseClient.ReadPortfolio(filePath);
                    if (string.IsNullOrEmpty(firebaseJson))
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ No data at Firebase path: {filePath}");
                        return null;
                    }
                    return DeserializeAndInitPortfolio(firebaseJson, filePath, isFirebase: true);
                }

                // ── Local file path ──────────────────────────────────────────
                System.Diagnostics.Debug.WriteLine($"📥 Loading portfolio from: {filePath}");

                if (!File.Exists(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Portfolio file not found: {filePath}");
                    return null;
                }

                string jsonContent = File.ReadAllText(filePath);

                if (string.IsNullOrEmpty(jsonContent))
                {
                    System.Diagnostics.Debug.WriteLine("❌ Portfolio file content is empty");
                    return null;
                }

                return DeserializeAndInitPortfolio(jsonContent, filePath, isFirebase: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading portfolio from {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Firebase Realtime Database converts JSON arrays to numeric-keyed objects:
        ///   ["a","b"] becomes {"0":"a","1":"b"}
        /// This recursively converts them back to arrays before deserialization.
        /// </summary>
        private static JToken NormalizeFirebaseArrays(JToken token)
        {
            if (token is JObject obj)
            {
                var keys = obj.Properties().Select(p => p.Name).ToList();
                bool allNumeric = keys.Count > 0 && keys.All(k => int.TryParse(k, out _));
                if (allNumeric)
                {
                    var array = new JArray();
                    foreach (var key in keys.OrderBy(k => int.Parse(k)))
                        array.Add(NormalizeFirebaseArrays(obj[key]));
                    return array;
                }
                foreach (var prop in obj.Properties().ToList())
                    obj[prop.Name] = NormalizeFirebaseArrays(prop.Value);
            }
            else if (token is JArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                    arr[i] = NormalizeFirebaseArrays(arr[i]);
            }
            return token;
        }

        /// <summary>
        /// Deserialize portfolio JSON and initialize any missing defaults.
        /// Saves back if changes were made (via Firebase or local file).
        /// </summary>
        private static Portfolio DeserializeAndInitPortfolio(string jsonContent, string filePath, bool isFirebase)
        {
            // Firebase converts arrays to numeric-keyed objects — normalize before deserializing
            if (isFirebase)
            {
                try
                {
                    var normalized = NormalizeFirebaseArrays(JToken.Parse(jsonContent));
                    jsonContent = normalized.ToString();
                    System.Diagnostics.Debug.WriteLine("✅ Firebase JSON arrays normalized");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Could not normalize Firebase JSON: {ex.Message}");
                }
            }

            var portfolio = JsonConvert.DeserializeObject<Portfolio>(jsonContent);
            if (portfolio == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ Failed to deserialize portfolio data");
                return null;
            }

            bool needsSave = false;

            // Ensure MonitoredFamilies list exists
            if (portfolio.MonitoredFamilies == null)
            {
                portfolio.MonitoredFamilies = new List<MonitoredFamily>();
                needsSave = true;
            }

            // Ensure default monitored family exists
            bool hasDefault = portfolio.MonitoredFamilies.Any(f =>
                string.Equals(f.FamilyName, DEFAULT_MONITORED_FAMILY, StringComparison.OrdinalIgnoreCase));

            if (!hasDefault)
            {
                portfolio.MonitoredFamilies.Add(new MonitoredFamily
                {
                    FamilyName = DEFAULT_MONITORED_FAMILY,
                    FileName = DEFAULT_MONITORED_FAMILY_FILE,
                    LastPublished = null,
                    PublishedByProject = null
                });
                needsSave = true;
                System.Diagnostics.Debug.WriteLine($"📦 Added default monitored family: {DEFAULT_MONITORED_FAMILY}");
            }

            // Clean up any projects marked as "removed" (legacy cleanup)
            if (portfolio.ProjectInfos != null)
            {
                var removedProjects = portfolio.ProjectInfos
                    .Where(p => string.Equals(p.Status, "removed", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var removedProject in removedProjects)
                {
                    portfolio.ProjectInfos.Remove(removedProject);
                    portfolio.Views?.RemoveAll(v =>
                        string.Equals(v.SourceProjectName, removedProject.ProjectName, StringComparison.OrdinalIgnoreCase));
                    System.Diagnostics.Debug.WriteLine($"🗑️ Cleaned up removed project: {removedProject.ProjectName}");
                    needsSave = true;
                }
            }

            // Ensure all projects have FamilyUpdateStatus initialized
            if (portfolio.ProjectInfos != null)
            {
                foreach (var project in portfolio.ProjectInfos)
                {
                    if (project.FamilyUpdateStatus == null)
                    {
                        project.FamilyUpdateStatus = new Dictionary<string, bool>();
                        needsSave = true;
                    }

                    foreach (var family in portfolio.MonitoredFamilies)
                    {
                        if (!project.FamilyUpdateStatus.ContainsKey(family.FamilyName))
                        {
                            project.FamilyUpdateStatus[family.FamilyName] = false;
                            needsSave = true;
                        }
                    }
                }
            }

            // Save back if we made changes
            if (needsSave)
            {
                try
                {
                    string updatedJson = JsonConvert.SerializeObject(portfolio, Formatting.Indented);
                    if (isFirebase)
                    {
                        FirebaseClient.WritePortfolio(filePath, updatedJson);
                    }
                    else
                    {
                        File.WriteAllText(filePath, updatedJson);
                    }
                    System.Diagnostics.Debug.WriteLine("💾 Portfolio auto-updated with missing defaults");
                }
                catch (Exception saveEx)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Could not save portfolio updates: {saveEx.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"✅ Portfolio loaded: {portfolio.PortfolioName}");
            return portfolio;
        }

        public static Portfolio LoadPortfolioFromProject(Document doc)
        {
            string jsonPath = GetJsonPath(doc);
            return string.IsNullOrEmpty(jsonPath) ? null : LoadPortfolioFromFile(jsonPath);
        }

        public static Portfolio LoadPortfolioData(Document doc)
        {
            return LoadPortfolioFromProject(doc) ?? new Portfolio();
        }

        public static bool LoadPortfolioData(string filePath, out Portfolio portfolioData)
        {
            portfolioData = null;
            try
            {
                if (string.IsNullOrEmpty(filePath))
                    return false;

                // For local paths, verify file exists before attempting load
                if (!FirebaseClient.IsFirebasePath(filePath) && !File.Exists(filePath))
                    return false;

                portfolioData = LoadPortfolioFromFile(filePath);
                return portfolioData != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading portfolio from path {filePath}: {ex.Message}");
                portfolioData = null;
                return false;
            }
        }

        public static void SavePortfolioData(Document doc, Portfolio data)
        {
            string jsonPath = GetJsonPath(doc);
            if (string.IsNullOrEmpty(jsonPath))
                throw new Exception("Portfolio path not configured for this project.");

            SavePortfolioToFile(data, jsonPath);
        }

        /// <summary>
        /// Get the Families subfolder path based on the portfolio JSON location.
        /// For Firebase paths, returns the Firebase path itself (FirebaseClient uses it as the storage root).
        /// For local paths, returns the local Families subfolder.
        /// </summary>
        public static string GetFamiliesFolderPath(string portfolioJsonPath)
        {
            if (string.IsNullOrEmpty(portfolioJsonPath))
                return null;

            // Firebase: the portfolio path IS the storage root — FirebaseClient appends /families/ internally
            if (FirebaseClient.IsFirebasePath(portfolioJsonPath))
                return portfolioJsonPath;

            string directory = Path.GetDirectoryName(portfolioJsonPath);
            if (string.IsNullOrEmpty(directory))
                return null;

            return Path.Combine(directory, "Families");
        }

        /// <summary>
        /// Ensure the Families subfolder exists.
        /// For Firebase paths, always returns true (no folder creation needed).
        /// </summary>
        public static bool EnsureFamiliesFolderExists(string portfolioJsonPath)
        {
            try
            {
                // Firebase: no folder creation needed
                if (FirebaseClient.IsFirebasePath(portfolioJsonPath))
                    return true;

                string familiesFolder = GetFamiliesFolderPath(portfolioJsonPath);
                if (string.IsNullOrEmpty(familiesFolder))
                    return false;

                if (!Directory.Exists(familiesFolder))
                {
                    Directory.CreateDirectory(familiesFolder);
                    System.Diagnostics.Debug.WriteLine($"📁 Created Families folder: {familiesFolder}");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creating Families folder: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Project Parameters

        /// <summary>
        /// Clear all ViewReference parameters from the project
        /// </summary>
        public static void ClearProjectParameters(Document doc)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🗑️ Clearing existing ViewReference parameters...");

                ProjectInfo projectInfo = doc.ProjectInformation;
                if (projectInfo == null) return;

                string[] parametersToClear = {
                    PROJECT_TYPE_PARAMETER,
                    JSON_PATH_PARAMETER,
                    PORTFOLIO_GUID_PARAMETER,
                    LAST_SYNC_PARAMETER,
                    PROJECT_GUID_PARAMETER
                };

                int clearedCount = 0;
                foreach (string paramName in parametersToClear)
                {
                    try
                    {
                        Parameter param = projectInfo.LookupParameter(paramName);
                        if (param != null && !param.IsReadOnly)
                        {
                            param.Set("");
                            clearedCount++;
                            System.Diagnostics.Debug.WriteLine($"   ✅ Cleared: {paramName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"   ⚠️ Could not clear {paramName}: {ex.Message}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"✅ Cleared {clearedCount} parameters");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error clearing parameters: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates project parameters with proper error handling
        /// </summary>
        public static bool CreateProjectParameters(Document doc)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔧 Creating project parameters...");

                ProjectInfo projectInfo = doc.ProjectInformation;
                if (projectInfo == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ ProjectInfo is null");
                    return false;
                }

                // Check which parameters already exist
                bool param1Exists = projectInfo.LookupParameter(PROJECT_TYPE_PARAMETER) != null;
                bool param2Exists = projectInfo.LookupParameter(JSON_PATH_PARAMETER) != null;
                bool param3Exists = projectInfo.LookupParameter(PORTFOLIO_GUID_PARAMETER) != null;
                bool param4Exists = projectInfo.LookupParameter(LAST_SYNC_PARAMETER) != null;
                bool param5Exists = projectInfo.LookupParameter(PROJECT_GUID_PARAMETER) != null;

                if (param1Exists && param2Exists && param3Exists && param4Exists && param5Exists)
                {
                    System.Diagnostics.Debug.WriteLine("✅ All parameters already exist");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine("📝 Creating missing parameters...");

                using (Transaction trans = new Transaction(doc, "Create ViewReference Parameters"))
                {
                    trans.Start();

                    try
                    {
                        string tempFile = Path.GetTempFileName();

                        try
                        {
                            doc.Application.SharedParametersFilename = tempFile;
                            DefinitionFile defFile = doc.Application.OpenSharedParameterFile();

                            if (defFile == null)
                            {
                                System.Diagnostics.Debug.WriteLine("❌ Could not create shared parameter file");
                                trans.RollBack();
                                return false;
                            }

                            DefinitionGroup defGroup = defFile.Groups.Create("ViewReferenceParameters");

                            CategorySet catSet = doc.Application.Create.NewCategorySet();
                            Category projectInfoCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_ProjectInformation);
                            catSet.Insert(projectInfoCategory);

                            BindingMap bindingMap = doc.ParameterBindings;
                            int createdCount = 0;

                            if (!param1Exists)
                            {
                                ExternalDefinition def = defGroup.Definitions.Create(
                                    new ExternalDefinitionCreationOptions(PROJECT_TYPE_PARAMETER, SpecTypeId.String.Text)) as ExternalDefinition;
                                InstanceBinding binding = doc.Application.Create.NewInstanceBinding(catSet);
                                bindingMap.Insert(def, binding);
                                createdCount++;
                                System.Diagnostics.Debug.WriteLine($"   ✅ Created: {PROJECT_TYPE_PARAMETER}");
                            }

                            if (!param2Exists)
                            {
                                ExternalDefinition def = defGroup.Definitions.Create(
                                    new ExternalDefinitionCreationOptions(JSON_PATH_PARAMETER, SpecTypeId.String.Text)) as ExternalDefinition;
                                InstanceBinding binding = doc.Application.Create.NewInstanceBinding(catSet);
                                bindingMap.Insert(def, binding);
                                createdCount++;
                                System.Diagnostics.Debug.WriteLine($"   ✅ Created: {JSON_PATH_PARAMETER}");
                            }

                            if (!param3Exists)
                            {
                                ExternalDefinition def = defGroup.Definitions.Create(
                                    new ExternalDefinitionCreationOptions(PORTFOLIO_GUID_PARAMETER, SpecTypeId.String.Text)) as ExternalDefinition;
                                InstanceBinding binding = doc.Application.Create.NewInstanceBinding(catSet);
                                bindingMap.Insert(def, binding);
                                createdCount++;
                                System.Diagnostics.Debug.WriteLine($"   ✅ Created: {PORTFOLIO_GUID_PARAMETER}");
                            }

                            if (!param4Exists)
                            {
                                ExternalDefinition def = defGroup.Definitions.Create(
                                    new ExternalDefinitionCreationOptions(LAST_SYNC_PARAMETER, SpecTypeId.String.Text)) as ExternalDefinition;
                                InstanceBinding binding = doc.Application.Create.NewInstanceBinding(catSet);
                                bindingMap.Insert(def, binding);
                                createdCount++;
                                System.Diagnostics.Debug.WriteLine($"   ✅ Created: {LAST_SYNC_PARAMETER}");
                            }

                            if (!param5Exists)
                            {
                                ExternalDefinition def = defGroup.Definitions.Create(
                                    new ExternalDefinitionCreationOptions(PROJECT_GUID_PARAMETER, SpecTypeId.String.Text)) as ExternalDefinition;
                                InstanceBinding binding = doc.Application.Create.NewInstanceBinding(catSet);
                                bindingMap.Insert(def, binding);
                                createdCount++;
                                System.Diagnostics.Debug.WriteLine($"   ✅ Created: {PROJECT_GUID_PARAMETER}");
                            }

                            trans.Commit();
                            System.Diagnostics.Debug.WriteLine($"✅ Successfully created {createdCount} parameters");
                            return true;
                        }
                        finally
                        {
                            try { File.Delete(tempFile); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        System.Diagnostics.Debug.WriteLine($"❌ Error in transaction: {ex.Message}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creating project parameters: {ex.Message}");
                return false;
            }
        }

        public static bool IsProjectConfigured(Document doc)
        {
            string projectType = GetProjectType(doc);
            string jsonPath = GetJsonPath(doc);
            return !string.IsNullOrEmpty(projectType) && !string.IsNullOrEmpty(jsonPath);
        }

        public static string GetProjectType(Document doc)
        {
            return GetParameterValue(doc, PROJECT_TYPE_PARAMETER) ?? "";
        }

        public static void SetProjectType(Document doc, string value)
        {
            SetParameterValue(doc, PROJECT_TYPE_PARAMETER, value);
        }

        public static string GetJsonPath(Document doc)
        {
            return GetParameterValue(doc, JSON_PATH_PARAMETER) ?? "";
        }

        public static void SetJsonPath(Document doc, string value)
        {
            SetParameterValue(doc, JSON_PATH_PARAMETER, value);
        }

        public static string GetPortfolioGuid(Document doc)
        {
            return GetParameterValue(doc, PORTFOLIO_GUID_PARAMETER) ?? "";
        }

        public static void SetPortfolioGuid(Document doc, string value)
        {
            SetParameterValue(doc, PORTFOLIO_GUID_PARAMETER, value);
        }

        public static DateTime? GetLastSyncTimestamp(Document doc)
        {
            string value = GetParameterValue(doc, LAST_SYNC_PARAMETER);
            return DateTime.TryParse(value, out DateTime result) ? result : (DateTime?)null;
        }

        public static void SetLastSyncTimestamp(Document doc, DateTime value)
        {
            SetParameterValue(doc, LAST_SYNC_PARAMETER, value.ToString("o"));
        }

        public static string GetProjectGuid(Document doc)
        {
            return GetParameterValue(doc, PROJECT_GUID_PARAMETER) ?? "";
        }

        public static void SetProjectGuid(Document doc, string value)
        {
            SetParameterValue(doc, PROJECT_GUID_PARAMETER, value);
        }



        private static string GetParameterValue(Document doc, string parameterName)
        {
            try
            {
                ProjectInfo projectInfo = doc.ProjectInformation;
                if (projectInfo == null)
                    return null;

                Parameter param = projectInfo.LookupParameter(parameterName);
                if (param != null && param.HasValue)
                {
                    return param.AsString();
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error getting parameter {parameterName}: {ex.Message}");
                return null;
            }
        }

        private static void SetParameterValue(Document doc, string parameterName, string value)
        {
            try
            {
                ProjectInfo projectInfo = doc.ProjectInformation;
                if (projectInfo == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ ProjectInfo is null");
                    return;
                }

                Parameter param = projectInfo.LookupParameter(parameterName);
                if (param != null && !param.IsReadOnly)
                {
                    param.Set(value ?? "");
                    System.Diagnostics.Debug.WriteLine($"✅ Set parameter {parameterName} = {value}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Parameter {parameterName} not found or read-only");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error setting parameter {parameterName}: {ex.Message}");
            }
        }

        #endregion

        #region Portfolio Management

        public static bool ConfigureProjectForPortfolio(Document document, string portfolioName,
            string projectNickname = null, bool isAuthority = false)
        {
            try
            {
                string projectName = GetProjectName(document);
                string jsonPath = GenerateNetworkPath(portfolioName);

                Portfolio portfolioData = LoadPortfolioFromFile(jsonPath) ??
                    new Portfolio
                    {
                        PortfolioName = portfolioName,
                        PortfolioGuid = Guid.NewGuid().ToString(),
                        CreatedDate = DateTime.Now,
                        DataVersion = "3.0",
                        ProjectInfos = new List<PortfolioProject>(),
                        Views = new List<ViewInfo>(),
                        MonitoredFamilies = new List<MonitoredFamily>()
                    };

                // Ensure default monitored family exists
                EnsureDefaultMonitoredFamily(portfolioData);

                var existingProject = portfolioData.ProjectInfos?.FirstOrDefault(p =>
                    string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));

                if (existingProject == null)
                {
                    var newProject = new PortfolioProject
                    {
                        ProjectName = projectName,
                        Nickname = projectNickname ?? projectName,
                        IsTypicalDetailsAuthority = isAuthority,
                        LastSync = DateTime.Now,
                        Status = "active",
                        FamilyUpdateStatus = new Dictionary<string, bool>()
                    };

                    // Initialize family update status for all monitored families
                    foreach (var family in portfolioData.MonitoredFamilies)
                    {
                        newProject.FamilyUpdateStatus[family.FamilyName] = false;
                    }

                    portfolioData.ProjectInfos.Add(newProject);
                }
                else
                {
                    existingProject.Nickname = projectNickname ?? existingProject.Nickname;
                    existingProject.IsTypicalDetailsAuthority = isAuthority;
                    existingProject.LastSync = DateTime.Now;

                    // Ensure family update status exists for all monitored families
                    if (existingProject.FamilyUpdateStatus == null)
                    {
                        existingProject.FamilyUpdateStatus = new Dictionary<string, bool>();
                    }

                    foreach (var family in portfolioData.MonitoredFamilies)
                    {
                        if (!existingProject.FamilyUpdateStatus.ContainsKey(family.FamilyName))
                        {
                            existingProject.FamilyUpdateStatus[family.FamilyName] = false;
                        }
                    }
                }

                if (!SavePortfolioToFile(portfolioData, jsonPath))
                    return false;

                CreateProjectParameters(document);

                using (var transaction = new Transaction(document, "Update Portfolio Configuration"))
                {
                    transaction.Start();
                    try
                    {
                        SetProjectType(document, PROJECT_TYPE_PORTFOLIO);
                        SetJsonPath(document, jsonPath);
                        SetPortfolioGuid(document, portfolioData.PortfolioGuid);
                        SetLastSyncTimestamp(document, DateTime.Now);

                        string existingGuid = GetProjectGuid(document);
                        if (string.IsNullOrEmpty(existingGuid))
                        {
                            SetProjectGuid(document, Guid.NewGuid().ToString());
                        }

                        transaction.Commit();
                        System.Diagnostics.Debug.WriteLine("✅ Project parameters set successfully");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        transaction.RollBack();
                        System.Diagnostics.Debug.WriteLine($"❌ Error updating project parameters: {ex.Message}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error configuring project: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Ensure the default DetailReferenceFamily is in the monitored families list
        /// </summary>
        public static void EnsureDefaultMonitoredFamily(Portfolio portfolioData)
        {
            if (portfolioData == null) return;

            if (portfolioData.MonitoredFamilies == null)
            {
                portfolioData.MonitoredFamilies = new List<MonitoredFamily>();
            }

            bool hasDefault = portfolioData.MonitoredFamilies.Any(f =>
                string.Equals(f.FamilyName, DEFAULT_MONITORED_FAMILY, StringComparison.OrdinalIgnoreCase));

            if (!hasDefault)
            {
                portfolioData.MonitoredFamilies.Add(new MonitoredFamily
                {
                    FamilyName = DEFAULT_MONITORED_FAMILY,
                    FileName = DEFAULT_MONITORED_FAMILY_FILE,
                    LastPublished = null,
                    PublishedByProject = null
                });

                System.Diagnostics.Debug.WriteLine($"📦 Added default monitored family: {DEFAULT_MONITORED_FAMILY}");
            }
        }

        public static string GetProjectDisplayName(Document doc)
        {
            return GetProjectName(doc);
        }

        #endregion

        #region Utility Methods

        public static string GenerateNetworkPath(string portfolioName)
        {
            try
            {
                string safePortfolioName = portfolioName.Replace(" ", "_").Replace("\\", "_").Replace("/", "_");
                string fileName = $"{safePortfolioName}_Portfolio.json";
                return $@"X:\Standards\Portfolios\{fileName}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error generating network path: {ex.Message}");
                return "";
            }
        }

        public static PortfolioProject CreateProjectInfo(Document doc, string nickname = null, bool isAuthority = false)
        {
            try
            {
                string projectName = GetProjectName(doc);
                string projectGuid = GetProjectGuid(doc);

                return new PortfolioProject
                {
                    ProjectName = projectName,
                    ProjectGuid = projectGuid,  // ADD THIS
                    Nickname = nickname ?? projectName,
                    IsTypicalDetailsAuthority = isAuthority,
                    LastSync = DateTime.Now,
                    Status = "active",
                    FamilyUpdateStatus = new Dictionary<string, bool>()
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error creating project info: {ex.Message}");
                return null;
            }
        }

        public static string GetProjectName(Document doc)
        {
            try
            {
                if (doc == null)
                    return "Unknown Project";

                string title = doc.Title;
                if (!string.IsNullOrEmpty(title))
                    return title;

                string pathName = doc.PathName;
                if (!string.IsNullOrEmpty(pathName))
                    return Path.GetFileNameWithoutExtension(pathName);

                return "Untitled Project";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error getting project name: {ex.Message}");
                return "Unknown Project";
            }
        }

        /// <summary>
        /// Find a project in the portfolio using multi-strategy matching:
        /// 1. GUID match (most reliable, survives renames)
        /// 2. Exact name match
        /// 3. Fuzzy name match (handles "GMP" → "GMP 3" version suffix changes)
        /// Auto-populates GUID when matched by name/fuzzy so future lookups are stable.
        /// </summary>
        /// <param name="portfolioData">The portfolio to search</param>
        /// <param name="projectName">Current doc.Title</param>
        /// <param name="projectGuid">Current project GUID</param>
        /// <param name="matchMethod">Out: how the match was made ("GUID", "Name", "Fuzzy", or "None")</param>
        /// <returns>The matched PortfolioProject, or null</returns>
        public static PortfolioProject FindProjectInPortfolio(
            Portfolio portfolioData, string projectName, string projectGuid, out string matchMethod)
        {
            matchMethod = "None";

            if (portfolioData?.ProjectInfos == null)
                return null;

            // Strategy 1: GUID match (most reliable — survives file renames)
            if (!string.IsNullOrEmpty(projectGuid))
            {
                var guidMatch = portfolioData.ProjectInfos.FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.ProjectGuid) &&
                    string.Equals(p.ProjectGuid, projectGuid, StringComparison.OrdinalIgnoreCase));

                if (guidMatch != null)
                {
                    matchMethod = "GUID";
                    // Update stored name if it changed (file was renamed/save-as'd)
                    if (!string.Equals(guidMatch.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"   📝 Project renamed: '{guidMatch.ProjectName}' → '{projectName}'");
                        guidMatch.ProjectName = projectName;
                    }
                    return guidMatch;
                }
            }

            // Strategy 2: Exact name match
            var nameMatch = portfolioData.ProjectInfos.FirstOrDefault(p =>
                string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));

            if (nameMatch != null)
            {
                matchMethod = "Name";
                // Auto-populate GUID so future lookups survive renames
                if (string.IsNullOrEmpty(nameMatch.ProjectGuid) && !string.IsNullOrEmpty(projectGuid))
                {
                    nameMatch.ProjectGuid = projectGuid;
                    System.Diagnostics.Debug.WriteLine($"   📝 Auto-populated GUID for '{projectName}'");
                }
                return nameMatch;
            }

            // Strategy 3: Fuzzy match — strip version suffixes and compare base names
            // Handles: "GMP" → "GMP 3", "GMP 1" → "GMP 2", "RVT25 GMP 1 & 2" etc.
            string currentBase = StripVersionSuffix(projectName);

            foreach (var project in portfolioData.ProjectInfos)
            {
                string storedBase = StripVersionSuffix(project.ProjectName);

                if (string.Equals(currentBase, storedBase, StringComparison.OrdinalIgnoreCase))
                {
                    matchMethod = "Fuzzy";
                    System.Diagnostics.Debug.WriteLine(
                        $"   📝 Fuzzy matched: '{project.ProjectName}' → '{projectName}' (base: '{currentBase}')");

                    // Update stored name to current and populate GUID
                    project.ProjectName = projectName;
                    if (string.IsNullOrEmpty(project.ProjectGuid) && !string.IsNullOrEmpty(projectGuid))
                    {
                        project.ProjectGuid = projectGuid;
                    }
                    return project;
                }
            }

            return null;
        }

        /// <summary>
        /// Strip version suffixes from project names for fuzzy matching.
        /// Removes trailing patterns like " GMP 3", " GMP", " GMP 1 & 2", " 1", " 2" etc.
        /// Example: "4220156_S-ControlOps_Rvt25 GMP 3" → "4220156_S-ControlOps_Rvt25"
        /// </summary>
        internal static string StripVersionSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // Remove trailing version patterns:
            //   " GMP 3", " GMP", " GMP 1 & 2", " GMP 1", " 1", " 2"
            // Work from right to left, stripping known patterns
            string result = name.Trim();

            // Strip trailing " N" or " N & N" (digit groups)
            while (result.Length > 0)
            {
                // Try " N & N" pattern
                int ampIdx = result.LastIndexOf(" & ");
                if (ampIdx > 0)
                {
                    string afterAmp = result.Substring(ampIdx + 3).Trim();
                    string beforeAmp = result.Substring(0, ampIdx).Trim();
                    // Check if both sides of & end with digits
                    if (afterAmp.Length > 0 && char.IsDigit(afterAmp[afterAmp.Length - 1]))
                    {
                        // Find where the number before & starts
                        int numStart = beforeAmp.Length - 1;
                        while (numStart > 0 && char.IsDigit(beforeAmp[numStart - 1])) numStart--;
                        if (numStart > 0 && beforeAmp[numStart - 1] == ' ')
                        {
                            result = beforeAmp.Substring(0, numStart - 1).Trim();
                            continue;
                        }
                    }
                }

                // Try trailing " N" (space + digits at end)
                int lastSpace = result.LastIndexOf(' ');
                if (lastSpace > 0)
                {
                    string suffix = result.Substring(lastSpace + 1);
                    if (suffix.Length > 0 && suffix.All(c => char.IsDigit(c)))
                    {
                        result = result.Substring(0, lastSpace).Trim();
                        continue;
                    }
                }

                break;
            }

            // Strip trailing " GMP" if present
            if (result.EndsWith(" GMP", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - 4).Trim();
            }

            return result;
        }

        #endregion

        #region Data Models

        public class Portfolio
        {
            public string PortfolioName { get; set; }
            public string PortfolioGuid { get; set; }
            public DateTime CreatedDate { get; set; }
            public DateTime LastModified { get; set; }
            public DateTime LastUpdated { get; set; }
            public string UpdatedByProject { get; set; }
            public string DataVersion { get; set; }
            public string FilePath { get; set; }

            /// <summary>
            /// Set when this portfolio is migrated to Firebase.
            /// Stored in the LOCAL JSON so other projects can detect migration on their next sync.
            /// Format: "portfolios/project-folder/portfolio-name"
            /// </summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string FirebasePath { get; set; }

            public string StandardsAuthorityProjectName { get; set; }
            public List<PortfolioProject> ProjectInfos { get; set; }
            public List<ViewInfo> Views { get; set; }

            // Excluded sheets for Usage Report
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public List<string> ExcludedTypicalDetailSheets { get; set; }

            // NEW: Monitored families for cross-project synchronization
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public List<MonitoredFamily> MonitoredFamilies { get; set; }

            public Portfolio()
            {
                ProjectInfos = new List<PortfolioProject>();
                Views = new List<ViewInfo>();
                ExcludedTypicalDetailSheets = new List<string>();
                MonitoredFamilies = new List<MonitoredFamily>();
                DataVersion = "3.0";
                UpdatedByProject = "";
                StandardsAuthorityProjectName = "";
            }
        }

        public class PortfolioProject
        {
            public string ProjectName { get; set; }
            public string ProjectGuid { get; set; }
            public string Nickname { get; set; }
            public bool IsTypicalDetailsAuthority { get; set; }
            public DateTime LastSync { get; set; }
            public string Status { get; set; }

            /// <summary>
            /// True once this project has updated its ViewReference_JsonPath to the Firebase path.
            /// Set to false for all sibling projects when a migration is initiated.
            /// Each project sets itself to true automatically on its next Sync to Central.
            /// </summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool IsMigrated { get; set; } = false;

            // List of ViewIds from Typical Details that this project uses
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public List<int> UsesViewIds { get; set; } = new List<int>();

            // NEW: Family update status - key is family name, value is whether this project has the latest version
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public Dictionary<string, bool> FamilyUpdateStatus { get; set; } = new Dictionary<string, bool>();

            public string DisplayNickname => string.IsNullOrEmpty(Nickname) ? ProjectName : Nickname;
        }

        /// <summary>
        /// Represents a family being monitored across the portfolio
        /// </summary>
        public class MonitoredFamily
        {
            public string FamilyName { get; set; }
            public string FileName { get; set; }
            public DateTime? LastPublished { get; set; }
            public string PublishedByProject { get; set; }

            /// <summary>Username of whoever currently has this family checked out for editing. Null if free.</summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public string CheckedOutBy { get; set; }

            /// <summary>When the checkout was acquired.</summary>
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public DateTime? CheckedOutAt { get; set; }

            public bool HasBeenPublished => LastPublished.HasValue;

            /// <summary>True if any user currently has this family checked out.</summary>
            [JsonIgnore]
            public bool IsCheckedOut => !string.IsNullOrEmpty(CheckedOutBy);

            /// <summary>True if the CURRENT machine user has this family checked out.</summary>
            [JsonIgnore]
            public bool IsCheckedOutByCurrentUser =>
                IsCheckedOut &&
                string.Equals(CheckedOutBy, Environment.UserName, StringComparison.OrdinalIgnoreCase);

            /// <summary>Human-readable checkout status string for display in UI.</summary>
            [JsonIgnore]
            public string CheckoutDisplayString =>
                IsCheckedOutByCurrentUser ? $"Checked out by you ({CheckedOutBy})" :
                IsCheckedOut ? $"Checked out by {CheckedOutBy}" :
                "Available";
        }

        #endregion
    }
}