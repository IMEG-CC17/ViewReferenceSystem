using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WpfColor = System.Windows.Media.Color;

namespace ViewReferenceSystem.Commands
{
    /// <summary>
    /// Help Command - Shows comprehensive user guide for the Typical Details Plugin
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class HelpCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                ShowHelpWindow();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Help Error", $"Error displaying help: {ex.Message}");
                return Result.Failed;
            }
        }

        private void ShowHelpWindow()
        {
            var helpWindow = new Window
            {
                Title = "Typical Details Plugin - User Guide",
                Width = 900,
                Height = 700,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(20)
            };

            var flowDoc = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                LineHeight = 20
            };

            AddTitle(flowDoc, "📘 Typical Details Plugin - Complete User Guide");
            AddSeparator(flowDoc);

            // Section 1: First Time Setup
            AddSectionHeader(flowDoc, "1️⃣ FIRST TIME SETUP - Creating or Joining a Portfolio");
            AddParagraph(flowDoc, "If you're setting up the plugin for the first time or joining an existing portfolio:");

            AddSubHeader(flowDoc, "Option A: Create a NEW Portfolio");
            AddBullet(flowDoc, "Open your Revit project (the one that will contain typical details)");
            AddBullet(flowDoc, "Click the 'Portfolio Manager' button on the cc17-dev ribbon");
            AddBullet(flowDoc, "Click 'Portfolio Setup' in the pane");
            AddBullet(flowDoc, "Enter a Portfolio Name (e.g., 'Campus Project 2025')");
            AddBullet(flowDoc, "Enter a Project Nickname (how this project appears in the list)");
            AddBullet(flowDoc, "Check 'Typical Details Authority' if this project contains the standard details");
            AddBullet(flowDoc, "Choose 'Network Location' and browse to where the portfolio JSON file should be saved");
            AddBullet(flowDoc, "Click 'Create Portfolio' - this creates the shared portfolio file");

            AddSubHeader(flowDoc, "Option B: Join an EXISTING Portfolio");
            AddBullet(flowDoc, "Open your Revit project");
            AddBullet(flowDoc, "Click the 'Portfolio Manager' button on the cc17-dev ribbon");
            AddBullet(flowDoc, "Click 'Portfolio Setup' in the pane");
            AddBullet(flowDoc, "Enter the same Portfolio Name as the existing portfolio");
            AddBullet(flowDoc, "Enter a Project Nickname for YOUR project");
            AddBullet(flowDoc, "Leave 'Typical Details Authority' unchecked (unless you're taking over as authority)");
            AddBullet(flowDoc, "Browse to the EXISTING portfolio JSON file location");
            AddBullet(flowDoc, "Click 'Join Portfolio' - this adds your project to the shared portfolio");

            AddNote(flowDoc, "💡 TIP: All projects in a portfolio share the same portfolio name and JSON file location. The JSON file is typically stored on a network drive like X:\\Projects\\YourPortfolio\\portfolio.json");

            AddSeparator(flowDoc);

            // Section 2: Viewing Available Details
            AddSectionHeader(flowDoc, "2️⃣ VIEWING AVAILABLE DETAILS");
            AddParagraph(flowDoc, "Once your project is part of a portfolio, you can see all available details:");

            AddBullet(flowDoc, "Open your Revit project");
            AddBullet(flowDoc, "Click 'Portfolio Manager' button - the pane opens on the right");
            AddBullet(flowDoc, "The pane shows ALL details from ALL projects in your portfolio");
            AddBullet(flowDoc, "Details are organized by Sheet Number and Detail Number");
            AddBullet(flowDoc, "Use the search box to find details by sheet, detail number, or keywords");
            AddBullet(flowDoc, "Typical details (from the authority project) appear at the top");
            AddBullet(flowDoc, "Each detail shows: Sheet Number, Detail Number, Top Note, and Source Project");

            AddNote(flowDoc, "🔍 SEARCH TIPS: Search by sheet number (S-101), detail number (3), or keywords in the top note. Results are ranked with typical details first.");

            AddSeparator(flowDoc);

            // Section 3: Syncing and Updates
            AddSectionHeader(flowDoc, "3️⃣ SYNCING TO CENTRAL - Automatic Updates");
            AddParagraph(flowDoc, "The plugin automatically keeps everyone synchronized:");

            AddSubHeader(flowDoc, "What Happens During Sync to Central:");
            AddBullet(flowDoc, "Your project's sheet-placed details are automatically pushed to the shared portfolio");
            AddBullet(flowDoc, "Your project pulls the latest details from other projects");
            AddBullet(flowDoc, "The Portfolio Manager pane automatically refreshes with the latest data");
            AddBullet(flowDoc, "All detail references in your project update with the latest information");
            AddBullet(flowDoc, "Top notes from the Typical Details Authority override local changes");

            AddSubHeader(flowDoc, "What Gets Synced:");
            AddBullet(flowDoc, "✅ Views placed on sheets (with viewports)");
            AddBullet(flowDoc, "✅ Sheet numbers and detail numbers");
            AddBullet(flowDoc, "✅ Top notes and view names");
            AddBullet(flowDoc, "❌ NOT synced: Work-in-progress views not on sheets");
            AddBullet(flowDoc, "❌ NOT synced: Deleted views or views removed from sheets");

            AddNote(flowDoc, "⚡ AUTOMATIC: You don't need to do anything special. Just Sync to Central as normal, and the portfolio updates automatically!");

            AddSeparator(flowDoc);

            // Section 4: Editing Top Notes
            AddSectionHeader(flowDoc, "4️⃣ EDITING TOP NOTES");
            AddParagraph(flowDoc, "Top notes can be edited directly in the Portfolio Manager pane:");

            AddBullet(flowDoc, "Find the detail you want to edit in the Portfolio Manager");
            AddBullet(flowDoc, "Click on the detail to select it");
            AddBullet(flowDoc, "Click the 'Edit Top Note' button that appears");
            AddBullet(flowDoc, "Type your new top note in the dialog");
            AddBullet(flowDoc, "Click 'Save' - changes are saved immediately");
            AddBullet(flowDoc, "Sync to Central to share the change with other projects");

            AddNote(flowDoc, "⚠️ AUTHORITY RULE: If you edit a typical detail's top note from a non-authority project, your changes will be overridden by the authority project during the next sync. Only the Typical Details Authority project can permanently change standard detail top notes.");

            AddSeparator(flowDoc);

            // Section 5: Placing Callout References
            AddSectionHeader(flowDoc, "5️⃣ PLACING CALLOUT REFERENCES");
            AddParagraph(flowDoc, "Callouts are rectangular references that show where a detail applies:");

            AddSubHeader(flowDoc, "How to Place a Callout:");
            AddBullet(flowDoc, "Open a view where you want to place the reference (floor plan, section, elevation, etc.)");
            AddBullet(flowDoc, "Find the detail you want to reference in the Portfolio Manager");
            AddBullet(flowDoc, "Click on the detail to select it");
            AddBullet(flowDoc, "Click the '📐 Callout' button");
            AddBullet(flowDoc, "Revit prompts: 'Click to place first corner of callout rectangle'");
            AddBullet(flowDoc, "Click the first corner of where you want the callout");
            AddBullet(flowDoc, "Revit prompts: 'Click to place opposite corner'");
            AddBullet(flowDoc, "Click the opposite corner - the callout appears with the detail number");

            AddSubHeader(flowDoc, "Same-Project vs Cross-Project Callouts:");
            AddBullet(flowDoc, "If the detail is in YOUR project: Creates a native Revit callout - you can double-click to navigate to the view");
            AddBullet(flowDoc, "If the detail is in ANOTHER project: Creates a visual callout that shows the detail info but doesn't navigate");

            AddNote(flowDoc, "💡 WHEN TO USE: Use callouts in plan views, elevations, or sections where you need to reference a detail in a specific rectangular area.");

            AddSeparator(flowDoc);

            // Section 6: Placing Section References
            AddSectionHeader(flowDoc, "6️⃣ PLACING SECTION REFERENCES");
            AddParagraph(flowDoc, "Sections are linear references that show a cut line:");

            AddSubHeader(flowDoc, "How to Place a Section:");
            AddBullet(flowDoc, "Open a view where you want to place the section");
            AddBullet(flowDoc, "Find the detail you want to reference in the Portfolio Manager");
            AddBullet(flowDoc, "Click on the detail to select it");
            AddBullet(flowDoc, "Click the '📏 Section' button");
            AddBullet(flowDoc, "Revit prompts: 'Click to place section start point'");
            AddBullet(flowDoc, "Click where the section line should start");
            AddBullet(flowDoc, "Revit prompts: 'Click to place section end point'");
            AddBullet(flowDoc, "Click where the section line should end - the section appears with the detail bubble");

            AddSubHeader(flowDoc, "Same-Project vs Cross-Project Sections:");
            AddBullet(flowDoc, "If the detail is in YOUR project: Creates a native Revit section - you can double-click to navigate to the view");
            AddBullet(flowDoc, "If the detail is in ANOTHER project: Creates a visual section that shows the detail info but doesn't navigate");

            AddNote(flowDoc, "💡 WHEN TO USE: Use sections when you need to show a cut line or cutting plane that references a detail. Perfect for wall sections, foundation details, etc.");

            AddSeparator(flowDoc);

            // Section 7: Placing Family Symbols
            AddSectionHeader(flowDoc, "7️⃣ PLACING DETAIL FAMILY SYMBOLS");
            AddParagraph(flowDoc, "Detail families are annotation symbols that can be placed anywhere:");

            AddSubHeader(flowDoc, "How to Place a Detail Family:");
            AddBullet(flowDoc, "Open a view where you want to place the annotation");
            AddBullet(flowDoc, "Find the detail you want to reference in the Portfolio Manager");
            AddBullet(flowDoc, "Click on the detail to select it");
            AddBullet(flowDoc, "Click the '📍 Family' button");
            AddBullet(flowDoc, "Revit prompts: 'Click to place detail reference'");
            AddBullet(flowDoc, "Click where you want the detail reference symbol");
            AddBullet(flowDoc, "The family appears showing the sheet and detail number in a bubble");

            AddSubHeader(flowDoc, "What's in the Family Symbol:");
            AddBullet(flowDoc, "Sheet Number (where the detail lives)");
            AddBullet(flowDoc, "Detail Number (the detail number on that sheet)");
            AddBullet(flowDoc, "Reference bubble (typical detail callout appearance)");
            AddBullet(flowDoc, "Metadata parameters storing source project and view information");

            AddNote(flowDoc, "💡 WHEN TO USE: Use detail families when you need a simple annotation reference that doesn't require a callout box or section line. Great for 'typical' or 'see detail' notes.");

            AddSeparator(flowDoc);

            // Section 8: Understanding Project Roles
            AddSectionHeader(flowDoc, "8️⃣ UNDERSTANDING PROJECT ROLES");

            AddSubHeader(flowDoc, "Typical Details Authority Project:");
            AddBullet(flowDoc, "This is the 'master' project that contains standard details");
            AddBullet(flowDoc, "Only ONE project in a portfolio should be the authority");
            AddBullet(flowDoc, "Top notes from this project override all other projects");
            AddBullet(flowDoc, "Sets the standard for typical details across the portfolio");
            AddBullet(flowDoc, "Usually a standalone 'Typical Details' or 'Standards' project");

            AddSubHeader(flowDoc, "Regular Portfolio Projects:");
            AddBullet(flowDoc, "These are building or site-specific projects");
            AddBullet(flowDoc, "Can contribute their own project-specific details to the portfolio");
            AddBullet(flowDoc, "Can reference both typical details and details from other projects");
            AddBullet(flowDoc, "Top notes for typical details will be overridden by the authority project");
            AddBullet(flowDoc, "Can have custom top notes for their own project-specific details");

            AddSeparator(flowDoc);

            // Section 9: Troubleshooting
            AddSectionHeader(flowDoc, "9️⃣ TROUBLESHOOTING");

            AddSubHeader(flowDoc, "Portfolio Manager is Empty:");
            AddBullet(flowDoc, "Make sure you've run Portfolio Setup and joined/created a portfolio");
            AddBullet(flowDoc, "Sync to Central to pull the latest portfolio data");
            AddBullet(flowDoc, "Check that the portfolio JSON file exists at the configured location");
            AddBullet(flowDoc, "Verify network drive access if using a network location");

            AddSubHeader(flowDoc, "My Changes Aren't Showing Up:");
            AddBullet(flowDoc, "Make sure your views are placed on sheets (only sheet-placed views sync)");
            AddBullet(flowDoc, "Sync to Central to push your changes to the portfolio");
            AddBullet(flowDoc, "Other users need to Sync to Central to pull your changes");
            AddBullet(flowDoc, "Check that you have write access to the portfolio JSON file");

            AddSubHeader(flowDoc, "Top Notes Keep Reverting:");
            AddBullet(flowDoc, "If editing a typical detail from a non-authority project, changes will be overridden");
            AddBullet(flowDoc, "Only the Typical Details Authority can permanently change standard detail top notes");
            AddBullet(flowDoc, "Make top note changes in the authority project instead");

            AddSubHeader(flowDoc, "Can't Place References:");
            AddBullet(flowDoc, "Make sure you have a view open (can't place in project browser)");
            AddBullet(flowDoc, "Verify the view type supports the reference type (sections need plan/elevation views)");
            AddBullet(flowDoc, "Check that the detail family is loaded in your project");
            AddBullet(flowDoc, "Try reloading Revit if placement tools aren't responding");

            AddSeparator(flowDoc);

            // Section 10: Best Practices
            AddSectionHeader(flowDoc, "🌟 BEST PRACTICES");

            AddBullet(flowDoc, "✅ Designate ONE project as the Typical Details Authority and keep it consistent");
            AddBullet(flowDoc, "✅ Use clear, descriptive project nicknames so everyone knows which project is which");
            AddBullet(flowDoc, "✅ Sync to Central regularly to keep everyone's portfolio data current");
            AddBullet(flowDoc, "✅ Only put finalized details on sheets - work-in-progress views won't sync");
            AddBullet(flowDoc, "✅ Use the search function to find details quickly instead of scrolling");
            AddBullet(flowDoc, "✅ Make top note changes in the authority project for typical details");
            AddBullet(flowDoc, "✅ Test the portfolio with a small project team before rolling out to everyone");
            AddBullet(flowDoc, "✅ Store the portfolio JSON file on a reliable network location that everyone can access");
            AddBullet(flowDoc, "❌ Don't create multiple authority projects - this causes conflicts");
            AddBullet(flowDoc, "❌ Don't manually edit the portfolio JSON file - let the plugin manage it");
            AddBullet(flowDoc, "❌ Don't delete the portfolio JSON file - you'll lose all shared data");

            AddSeparator(flowDoc);

            // Footer
            AddParagraph(flowDoc, "");
            AddParagraph(flowDoc, "For additional support, contact your BIM coordinator or the plugin administrator.");
            AddParagraph(flowDoc, "Plugin Version: 3.0 | Last Updated: 2025");

            var flowDocReader = new FlowDocumentScrollViewer
            {
                Document = flowDoc,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            helpWindow.Content = flowDocReader;
            helpWindow.ShowDialog();
        }

        private void AddTitle(FlowDocument doc, string text)
        {
            var para = new Paragraph(new Run(text))
            {
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0, 102, 204)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            doc.Blocks.Add(para);
        }

        private void AddSectionHeader(FlowDocument doc, string text)
        {
            var para = new Paragraph(new Run(text))
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(51, 51, 51)),
                Margin = new Thickness(0, 15, 0, 8),
                Background = new SolidColorBrush(WpfColor.FromRgb(240, 240, 240)),
                Padding = new Thickness(8, 4, 8, 4)
            };
            doc.Blocks.Add(para);
        }

        private void AddSubHeader(FlowDocument doc, string text)
        {
            var para = new Paragraph(new Run(text))
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0, 102, 204)),
                Margin = new Thickness(0, 10, 0, 5)
            };
            doc.Blocks.Add(para);
        }

        private void AddParagraph(FlowDocument doc, string text)
        {
            var para = new Paragraph(new Run(text))
            {
                Margin = new Thickness(0, 5, 0, 5),
                TextAlignment = TextAlignment.Left
            };
            doc.Blocks.Add(para);
        }

        private void AddBullet(FlowDocument doc, string text)
        {
            var list = new List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(20, 2, 0, 2)
            };
            list.ListItems.Add(new ListItem(new Paragraph(new Run(text)) { Margin = new Thickness(0) }));
            doc.Blocks.Add(list);
        }

        private void AddNote(FlowDocument doc, string text)
        {
            var para = new Paragraph(new Run(text))
            {
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0, 128, 0)),
                Background = new SolidColorBrush(WpfColor.FromRgb(240, 255, 240)),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 8, 0, 8),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0, 180, 0)),
                BorderThickness = new Thickness(2, 0, 0, 0)
            };
            doc.Blocks.Add(para);
        }

        private void AddSeparator(FlowDocument doc)
        {
            var para = new Paragraph
            {
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 10, 0, 10),
                Padding = new Thickness(0)
            };
            doc.Blocks.Add(para);
        }
    }
}