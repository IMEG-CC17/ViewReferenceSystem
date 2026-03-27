using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ViewReferenceSystem.Placement
{
    /// <summary>
    /// External event handler for clearing portfolio parameters safely from WPF context
    /// </summary>
    public class ClearParametersHelper : IExternalEventHandler
    {
        private Document _document;
        public bool IsCompleted { get; private set; }
        public bool IsSuccessful { get; private set; }
        public string ErrorMessage { get; private set; }

        public void SetDocument(Document doc)
        {
            _document = doc;
            IsCompleted = false;
            IsSuccessful = false;
            ErrorMessage = "";
        }

        public void Execute(UIApplication app)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🗑️ ClearParametersHelper.Execute() START");

                if (_document == null)
                {
                    ErrorMessage = "No document set";
                    IsCompleted = true;
                    IsSuccessful = false;
                    return;
                }

                using (Transaction trans = new Transaction(_document, "Clear Portfolio Parameters"))
                {
                    trans.Start();

                    try
                    {
                        var projectInfo = _document.ProjectInformation;
                        var parameterNames = new[]
                        {
                            "ViewReference_JsonPath",
                            "ViewReference_PortfolioGuid",
                            "ViewReference_ProjectType",
                            "ViewReference_ProjectGuid",
                            "ViewReference_LastSync",
                            "ViewReference_OfflineMode"
                        };

                        int clearedCount = 0;
                        foreach (string paramName in parameterNames)
                        {
                            Parameter param = projectInfo.LookupParameter(paramName);
                            if (param != null && !param.IsReadOnly)
                            {
                                param.Set("");
                                clearedCount++;
                                System.Diagnostics.Debug.WriteLine($"   ✅ Cleared: {paramName}");
                            }
                        }

                        trans.Commit();
                        System.Diagnostics.Debug.WriteLine($"✅ Cleared {clearedCount} parameters");
                        IsSuccessful = true;
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        ErrorMessage = ex.Message;
                        IsSuccessful = false;
                        System.Diagnostics.Debug.WriteLine($"❌ Error in transaction: {ex.Message}");
                    }
                }

                IsCompleted = true;
                System.Diagnostics.Debug.WriteLine("🗑️ ClearParametersHelper.Execute() END");
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                IsCompleted = true;
                IsSuccessful = false;
                System.Diagnostics.Debug.WriteLine($"❌ ClearParametersHelper exception: {ex.Message}");
            }
        }

        public string GetName()
        {
            return "ClearParametersHelper";
        }
    }
}