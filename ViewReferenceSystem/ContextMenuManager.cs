using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using ViewReferenceSystem.Models;
using ViewReferenceSystem.UI;
using WpfColor = System.Windows.Media.Color;

namespace ViewReferenceSystem
{
    /// <summary>
    /// PHASE 4: Context Menu Manager - Provides right-click context menus for detail family instances
    /// </summary>
    public class ContextMenuManager
    {
        // Events for context menu actions
        public event EventHandler<PreviewRequestedEventArgs> PreviewRequested;
        public event EventHandler<EditTopNoteRequestedEventArgs> EditTopNoteRequested;
        public event EventHandler<FindInPortfolioRequestedEventArgs> FindInPortfolioRequested;

        // Current context menu state
        private ContextMenu _activeContextMenu;
        private FamilyInstance _currentFamilyInstance;
        private ViewInfo _currentViewInfo;

        public ContextMenuManager()
        {
            System.Diagnostics.Debug.WriteLine("📋 PHASE 4: ContextMenuManager initialized");
        }

        /// <summary>
        /// PHASE 4: Show context menu for a detail family instance
        /// </summary>
        public void ShowContextMenuForFamily(FamilyInstance familyInstance, ViewInfo viewInfo)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"📋 PHASE 4: Showing context menu for {viewInfo?.ViewName ?? "Unknown"}");

                if (familyInstance == null || viewInfo == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ PHASE 4: Cannot show context menu - missing family instance or view info");
                    return;
                }

                // Store current context
                _currentFamilyInstance = familyInstance;
                _currentViewInfo = viewInfo;

                // Create context menu
                _activeContextMenu = CreateContextMenu(viewInfo);

                // Show context menu at mouse position
                if (_activeContextMenu != null)
                {
                    _activeContextMenu.IsOpen = true;
                    System.Diagnostics.Debug.WriteLine("✅ PHASE 4: Context menu displayed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error showing context menu: {ex.Message}");
            }
        }

        /// <summary>
        /// PHASE 4: Create context menu for a view
        /// </summary>
        private ContextMenu CreateContextMenu(ViewInfo viewInfo)
        {
            try
            {
                var contextMenu = new ContextMenu();

                // Preview item
                var previewItem = new MenuItem
                {
                    Header = "👁️ Preview Detail",
                    FontSize = 12
                };
                previewItem.Click += (s, e) => HandlePreviewAction();
                contextMenu.Items.Add(previewItem);

                // Edit top note item
                var editItem = new MenuItem
                {
                    Header = "✏️ Edit Top Note",
                    FontSize = 12
                };
                editItem.Click += (s, e) => HandleEditAction();
                contextMenu.Items.Add(editItem);

                // Separator
                contextMenu.Items.Add(new Separator());

                // Find in portfolio item
                var findItem = new MenuItem
                {
                    Header = "🔍 Find in Portfolio",
                    FontSize = 12
                };
                findItem.Click += (s, e) => HandleFindAction();
                contextMenu.Items.Add(findItem);

                // Style the context menu
                contextMenu.Background = new SolidColorBrush(WpfColor.FromRgb(250, 250, 250));
                contextMenu.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(200, 200, 200));
                contextMenu.BorderThickness = new Thickness(1);

                System.Diagnostics.Debug.WriteLine("✅ PHASE 4: Context menu created with 3 items");
                return contextMenu;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error creating context menu: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// PHASE 4: Hide active context menu
        /// </summary>
        public void HideContextMenu()
        {
            try
            {
                if (_activeContextMenu?.IsOpen == true)
                {
                    _activeContextMenu.IsOpen = false;
                    System.Diagnostics.Debug.WriteLine("🔄 PHASE 4: Context menu hidden");
                }

                _activeContextMenu = null;
                _currentFamilyInstance = null;
                _currentViewInfo = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error hiding context menu: {ex.Message}");
            }
        }

        /// <summary>
        /// PHASE 4: Handle preview action
        /// </summary>
        private void HandlePreviewAction()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("👁️ PHASE 4: Handling preview action");

                if (_currentViewInfo != null)
                {
                    var eventArgs = new PreviewRequestedEventArgs(_currentViewInfo, _currentFamilyInstance);
                    PreviewRequested?.Invoke(this, eventArgs);
                    System.Diagnostics.Debug.WriteLine("✅ PHASE 4: Preview event fired");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ PHASE 4: No current view info for preview");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error handling preview action: {ex.Message}");
            }
        }

        /// <summary>
        /// PHASE 4: Handle edit top note action
        /// </summary>
        private void HandleEditAction()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("✏️ PHASE 4: Handling edit action");

                if (_currentViewInfo != null)
                {
                    var eventArgs = new EditTopNoteRequestedEventArgs(_currentViewInfo, _currentFamilyInstance);
                    EditTopNoteRequested?.Invoke(this, eventArgs);
                    System.Diagnostics.Debug.WriteLine("✅ PHASE 4: Edit event fired");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ PHASE 4: No current view info for edit");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error handling edit action: {ex.Message}");
            }
        }

        /// <summary>
        /// PHASE 4: Handle find in portfolio action
        /// </summary>
        private void HandleFindAction()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔍 PHASE 4: Handling find action");

                if (_currentViewInfo != null)
                {
                    var eventArgs = new FindInPortfolioRequestedEventArgs(_currentViewInfo, _currentFamilyInstance);
                    FindInPortfolioRequested?.Invoke(this, eventArgs);
                    System.Diagnostics.Debug.WriteLine("✅ PHASE 4: Find event fired");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ PHASE 4: No current view info for find");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error handling find action: {ex.Message}");
            }
        }

        /// <summary>
        /// PHASE 4: Check if context menu is currently active
        /// </summary>
        public bool IsContextMenuActive
        {
            get
            {
                try
                {
                    return _activeContextMenu?.IsOpen == true;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// PHASE 4: Get current context information
        /// </summary>
        public (FamilyInstance familyInstance, ViewInfo viewInfo) GetCurrentContext()
        {
            return (_currentFamilyInstance, _currentViewInfo);
        }

        /// <summary>
        /// PHASE 4: Cleanup resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                HideContextMenu();
                System.Diagnostics.Debug.WriteLine("🔄 PHASE 4: ContextMenuManager disposed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ PHASE 4: Error disposing ContextMenuManager: {ex.Message}");
            }
        }
    }
}