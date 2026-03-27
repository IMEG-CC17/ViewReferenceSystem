// FamilyPublishHelper.cs

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ViewReferenceSystem.Core;
using ViewReferenceSystem.Utilities;

namespace ViewReferenceSystem.Placement
{
    /// <summary>
    /// External event handler for publishing families.
    /// Required because doc.EditFamily() cannot be called from inside a modal dialog.
    /// </summary>
    public class FamilyPublishHelper : IExternalEventHandler
    {
        public enum OperationType
        {
            Publish,
            AddAndPublish
        }

        private Document _document;
        private string _familyName;
        private string _fileName;
        private OperationType _operation;

        /// <summary>
        /// Set up for a publish-only operation
        /// </summary>
        public void SetPublishData(Document doc, string familyName)
        {
            _document = doc;
            _familyName = familyName;
            _fileName = null;
            _operation = OperationType.Publish;
        }

        /// <summary>
        /// Set up for an add-and-publish operation
        /// </summary>
        public void SetAddAndPublishData(Document doc, string familyName, string fileName)
        {
            _document = doc;
            _familyName = familyName;
            _fileName = fileName;
            _operation = OperationType.AddAndPublish;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"📤 FamilyPublishHelper.Execute() - Operation: {_operation}, Family: {_familyName}");

                if (_document == null || string.IsNullOrEmpty(_familyName))
                {
                    System.Diagnostics.Debug.WriteLine("❌ No document or family name set");
                    TaskDialog.Show("Error", "No document or family name specified.");
                    return;
                }

                bool success;
                string errorMessage;

                if (_operation == OperationType.AddAndPublish)
                {
                    // Add to portfolio first, then publish
                    success = FamilyMonitorManager.AddMonitoredFamily(_document, _familyName, _fileName, out errorMessage);

                    if (success)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Family '{_familyName}' added and published successfully");
                        TaskDialog.Show("Add Family Complete", $"Family '{_familyName}' added to monitoring and published!\n\nAll projects will receive the family on their next Sync to Central.");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Add/Publish failed: {errorMessage}");
                        TaskDialog.Show("Add Family Failed", $"Failed to add family:\n\n{errorMessage}");
                    }
                }
                else
                {
                    // Publish only
                    success = FamilyMonitorManager.PublishFamily(_document, _familyName, out errorMessage);

                    if (success)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Family '{_familyName}' published successfully");
                        TaskDialog.Show("Publish Complete", $"Family '{_familyName}' published successfully!\n\nAll other projects will receive the update on their next Sync to Central.");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Publish failed: {errorMessage}");
                        TaskDialog.Show("Publish Failed", $"Failed to publish family:\n\n{errorMessage}");
                    }
                }

                // Refresh the Portfolio Manager pane
                try
                {
                    ViewReferenceSystem.UI.PortfolioManagePane.RefreshCurrentPane();
                }
                catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ FamilyPublishHelper error: {ex.Message}");
                TaskDialog.Show("Error", $"Error during family operation:\n\n{ex.Message}");
            }
            finally
            {
                // Clear the data
                _document = null;
                _familyName = null;
                _fileName = null;
            }
        }

        public string GetName()
        {
            return "FamilyPublishHelper";
        }
    }
}