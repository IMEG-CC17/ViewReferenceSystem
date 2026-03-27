// AiFamilyGeneratorWindow.cs

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CSharp;
using Newtonsoft.Json;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;

namespace ViewReferenceSystem.UI
{
    public class AiFamilyGeneratorWindow : Window
    {
        #region Fields

        private readonly Document _doc;
        private readonly UIApplication _uiApp;

        // UI Controls
        private WpfTextBox _codeInputBox;
        private WpfTextBox _debugErrorBox;
        private TextBlock _statusText;
        private Button _buildButton;
        private Expander _debugExpander;

        // Theme
        private static readonly SolidColorBrush BrushBg = new SolidColorBrush(WpfColor.FromRgb(45, 45, 48));
        private static readonly SolidColorBrush BrushPanel = new SolidColorBrush(WpfColor.FromRgb(60, 60, 64));
        private static readonly SolidColorBrush BrushInputBg = new SolidColorBrush(WpfColor.FromRgb(30, 30, 30));
        private static readonly SolidColorBrush BrushText = new SolidColorBrush(WpfColor.FromRgb(241, 241, 241));
        private static readonly SolidColorBrush BrushSubText = new SolidColorBrush(WpfColor.FromRgb(160, 160, 164));
        private static readonly SolidColorBrush BrushAccent = new SolidColorBrush(WpfColor.FromRgb(0, 122, 204));
        private static readonly SolidColorBrush BrushSuccess = new SolidColorBrush(WpfColor.FromRgb(76, 175, 80));
        private static readonly SolidColorBrush BrushError = new SolidColorBrush(WpfColor.FromRgb(244, 67, 54));
        private static readonly SolidColorBrush BrushBorder = new SolidColorBrush(WpfColor.FromRgb(90, 90, 95));
        private static readonly SolidColorBrush BrushCodeText = new SolidColorBrush(WpfColor.FromRgb(212, 212, 212));

        #endregion

        #region Constructor

        public AiFamilyGeneratorWindow(Document doc, UIApplication uiApp)
        {
            _doc = doc;
            _uiApp = uiApp;
            BuildUI();
        }

        #endregion

        #region UI

        private void BuildUI()
        {
            Title = "AI Family Generator";
            Width = 780;
            Height = 650;
            MinWidth = 600;
            MinHeight = 450;
            ResizeMode = ResizeMode.CanResize;
            Background = BrushBg;
            Foreground = BrushText;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new WpfGrid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Code input
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // Debug expander
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // Bottom bar

            // ── Header ───────────────────────────────────────────────────────
            var header = new Border
            {
                Background = BrushPanel,
                Padding = new Thickness(16, 12, 16, 12),
                BorderBrush = BrushBorder,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "AI Family Generator",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushText
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "Design your family with any AI \u2192 paste the generated C# code below \u2192 click Build Family",
                FontSize = 11,
                Foreground = BrushSubText,
                Margin = new Thickness(0, 3, 0, 0)
            });
            header.Child = headerStack;
            WpfGrid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Code Input ────────────────────────────────────────────────────
            var codePanel = new WpfGrid { Margin = new Thickness(12, 10, 12, 0) };
            codePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            codePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var codeLabel = new TextBlock
            {
                Text = "C# Family Code",
                Foreground = BrushSubText,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            };
            WpfGrid.SetRow(codeLabel, 0);
            codePanel.Children.Add(codeLabel);

            _codeInputBox = new WpfTextBox
            {
                Background = BrushInputBg,
                Foreground = BrushCodeText,
                BorderBrush = BrushBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = WpfScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVisibility.Auto,
                Text = GetPlaceholderText()
            };
            _codeInputBox.GotFocus += (s, e) =>
            {
                if (_codeInputBox.Text == GetPlaceholderText())
                    _codeInputBox.Text = "";
            };
            WpfGrid.SetRow(_codeInputBox, 1);
            codePanel.Children.Add(_codeInputBox);
            WpfGrid.SetRow(codePanel, 1);
            root.Children.Add(codePanel);

            // ── Debug Expander ────────────────────────────────────────────────
            _debugExpander = new Expander
            {
                Header = "Errors",
                IsExpanded = false,
                Foreground = BrushSubText,
                Background = BrushPanel,
                BorderBrush = BrushBorder,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 8, 0, 0),
                MaxHeight = 260
            };

            var debugGrid = new WpfGrid { Margin = new Thickness(12, 4, 12, 8) };
            debugGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            debugGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var copyErrorBtn = MakeSmallButton("Copy Errors for AI Debug");
            copyErrorBtn.HorizontalAlignment = HorizontalAlignment.Right;
            copyErrorBtn.Margin = new Thickness(4);
            copyErrorBtn.Click += OnCopyErrorsClicked;
            WpfGrid.SetRow(copyErrorBtn, 0);
            debugGrid.Children.Add(copyErrorBtn);

            _debugErrorBox = new WpfTextBox
            {
                Background = BrushInputBg,
                Foreground = BrushError,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = WpfScrollBarVisibility.Auto,
                Height = 180,
                Text = "No errors."
            };
            WpfGrid.SetRow(_debugErrorBox, 1);
            debugGrid.Children.Add(_debugErrorBox);

            _debugExpander.Content = debugGrid;
            WpfGrid.SetRow(_debugExpander, 2);
            root.Children.Add(_debugExpander);

            // ── Bottom Bar ────────────────────────────────────────────────────
            var bottomBar = new Border
            {
                Background = BrushPanel,
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = BrushBorder,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            var bottomGrid = new WpfGrid();
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                Text = "Paste C# family code above and click Build Family.",
                Foreground = BrushSubText,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            WpfGrid.SetColumn(_statusText, 0);

            _buildButton = new Button
            {
                Content = "Build Family",
                Background = BrushAccent,
                Foreground = BrushText,
                BorderBrush = BrushAccent,
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(24, 8, 24, 8),
                Cursor = Cursors.Hand
            };
            _buildButton.Click += OnBuildClicked;
            WpfGrid.SetColumn(_buildButton, 1);

            bottomGrid.Children.Add(_statusText);
            bottomGrid.Children.Add(_buildButton);
            bottomBar.Child = bottomGrid;
            WpfGrid.SetRow(bottomBar, 3);
            root.Children.Add(bottomBar);

            Content = root;
        }

        private Button MakeSmallButton(string text) =>
            new Button
            {
                Content = text,
                Background = BrushPanel,
                Foreground = BrushSubText,
                BorderBrush = BrushBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 11,
                Cursor = Cursors.Hand
            };

        #endregion

        #region Build Flow

        private void OnBuildClicked(object sender, RoutedEventArgs e)
        {
            string code = _codeInputBox.Text.Trim();
            if (string.IsNullOrEmpty(code) || code == GetPlaceholderText())
            {
                SetStatus("Paste C# code first.", true);
                return;
            }

            // Strip markdown fences if pasted from AI chat
            code = StripMarkdownFences(code);

            _buildButton.IsEnabled = false;
            _buildButton.Content = "Compiling...";
            _debugErrorBox.Text = "No errors.";
            _debugExpander.IsExpanded = false;
            SetStatus("Compiling...", false);

            try
            {
                Assembly compiled = CompileCode(code);

                _buildButton.Content = "Running in Revit...";
                SetStatus("Building family in Revit...", false);

                // Hide this window so Revit dialogs/actions are visible
                this.WindowState = WindowState.Minimized;

                ExecuteGeneratedCode(compiled, _uiApp);

                // Bring window back and show success
                this.WindowState = WindowState.Normal;
                this.Activate();
                this.Topmost = true;
                this.Topmost = false;
                SetStatus("Family built \u2014 check your Desktop for the .rfa file.", false);
                _statusText.Foreground = BrushSuccess;
            }
            catch (CompilationException cex)
            {
                this.WindowState = WindowState.Normal;
                this.Activate();
                this.Topmost = true;
                this.Topmost = false;

                _debugErrorBox.Text = cex.Message;
                SetStatus("Compilation failed \u2014 expand Errors below.", true);
                _debugExpander.IsExpanded = true;
            }
            catch (Exception ex)
            {
                this.WindowState = WindowState.Normal;
                this.Activate();
                this.Topmost = true;
                this.Topmost = false;

                _debugErrorBox.Text = ex.ToString();
                SetStatus(ex.Message, true);
                _debugExpander.IsExpanded = true;
            }
            finally
            {
                _buildButton.IsEnabled = true;
                _buildButton.Content = "Build Family";
            }
        }

        #endregion

        #region Copy Errors for AI Debug

        private void OnCopyErrorsClicked(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_debugErrorBox.Text) || _debugErrorBox.Text == "No errors.")
            {
                SetStatus("No errors to copy.", false);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== AI FAMILY GENERATOR \u2014 BUILD ERRORS ===");
            sb.AppendLine();
            sb.AppendLine("The following code failed to compile/run inside the AI Family Generator Revit addin.");
            sb.AppendLine("The addin compiles C# in-memory (CodeDom on Revit 2022-2024, Roslyn on 2025+).");
            sb.AppendLine("Please diagnose the issue and provide the complete corrected code.");
            sb.AppendLine();
            sb.AppendLine("\u2500\u2500 Code \u2500\u2500");
            sb.AppendLine(StripMarkdownFences(_codeInputBox.Text));
            sb.AppendLine();
            sb.AppendLine("\u2500\u2500 Errors \u2500\u2500");
            sb.AppendLine(_debugErrorBox.Text);

            Clipboard.SetText(sb.ToString());
            SetStatus("Copied code + errors to clipboard \u2014 paste into your AI chat to debug.", false);
        }

        #endregion

        #region Compile & Execute

        private Assembly CompileCode(string code)
        {
            // Try CodeDom first (works on .NET Framework 4.8 / Revit 2022-2024)
            // Falls back to Roslyn on .NET 8+ (Revit 2025-2026)
            try
            {
                return CompileWithCodeDom(code);
            }
            catch (PlatformNotSupportedException)
            {
                // .NET 8 — CSharpCodeProvider not supported, use Roslyn
                return CompileWithRoslyn(code);
            }
        }

        private Assembly CompileWithCodeDom(string code)
        {
            var parameters = new CompilerParameters
            {
                GenerateInMemory = true,
                GenerateExecutable = false,
                TreatWarningsAsErrors = false
            };

            parameters.ReferencedAssemblies.Add("mscorlib.dll");
            parameters.ReferencedAssemblies.Add("System.dll");
            parameters.ReferencedAssemblies.Add("System.Core.dll");
            parameters.ReferencedAssemblies.Add("System.Xml.dll");
            parameters.ReferencedAssemblies.Add(typeof(UIApplication).Assembly.Location);
            parameters.ReferencedAssemblies.Add(typeof(Document).Assembly.Location);

            try
            {
                string newtonsoftPath = typeof(JsonConvert).Assembly.Location;
                if (File.Exists(newtonsoftPath))
                    parameters.ReferencedAssemblies.Add(newtonsoftPath);
            }
            catch { }

            using (var provider = new CSharpCodeProvider())
            {
                var results = provider.CompileAssemblyFromSource(parameters, code);

                if (results.Errors.HasErrors)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Compilation failed with " + results.Errors.Count + " error(s):\n");
                    foreach (CompilerError err in results.Errors)
                        if (!err.IsWarning)
                            sb.AppendLine("  Line " + err.Line + ": " + err.ErrorText);
                    throw new CompilationException(sb.ToString());
                }

                return results.CompiledAssembly;
            }
        }

        private Assembly CompileWithRoslyn(string code)
        {
            // Roslyn compilation for .NET 8+ (Revit 2025-2026)
            // 100% reflection — no compile-time dependency on Microsoft.CodeAnalysis
            // DLLs must be in the addins folder (deployed by batch file or Firebase auto-update)

            string addinsFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string roslynCommonPath = Path.Combine(addinsFolder, "Microsoft.CodeAnalysis.dll");
            string roslynCSharpPath = Path.Combine(addinsFolder, "Microsoft.CodeAnalysis.CSharp.dll");

            if (!File.Exists(roslynCommonPath) || !File.Exists(roslynCSharpPath))
                throw new Exception(
                    "Roslyn compiler DLLs not found in addins folder.\n\n" +
                    "AI Family Generator requires Roslyn for Revit 2025+.\n" +
                    "Run the installer batch file or update the add-in to get the required DLLs.\n\n" +
                    "Missing from: " + addinsFolder);

            try
            {
                // Load Roslyn assemblies
                Assembly commonAsm = Assembly.LoadFrom(roslynCommonPath);
                Assembly csharpAsm = Assembly.LoadFrom(roslynCSharpPath);

                // ── Get types ──
                Type metadataRefType = commonAsm.GetType("Microsoft.CodeAnalysis.MetadataReference");
                Type syntaxTreeBaseType = commonAsm.GetType("Microsoft.CodeAnalysis.SyntaxTree");
                Type metadataRefBaseType = commonAsm.GetType("Microsoft.CodeAnalysis.MetadataReference");
                Type outputKindType = commonAsm.GetType("Microsoft.CodeAnalysis.OutputKind");

                Type csharpSyntaxTreeType = csharpAsm.GetType("Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree");
                Type csharpCompilationType = csharpAsm.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilation");
                Type csharpCompOptionsType = csharpAsm.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions");

                // ── MetadataReference.CreateFromFile ──
                MethodInfo createFromFile = null;
                foreach (var m in metadataRefType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name == "CreateFromFile")
                    {
                        createFromFile = m;
                        break;
                    }
                }
                if (createFromFile == null)
                    throw new Exception("Could not find MetadataReference.CreateFromFile method");

                // Helper: call CreateFromFile with ANY number of params
                // Roslyn 4.8.0 has 3 params: (string path, MetadataReferenceProperties properties, DocumentationProvider documentation)
                // We fill in defaults for all params beyond the first.
                var cfParams = createFromFile.GetParameters();
                Func<string, object> makeRef = (path) =>
                {
                    var args = new object[cfParams.Length];
                    args[0] = path;
                    for (int i = 1; i < cfParams.Length; i++)
                    {
                        if (cfParams[i].HasDefaultValue)
                            args[i] = cfParams[i].DefaultValue;
                        else if (cfParams[i].ParameterType.IsValueType)
                            args[i] = Activator.CreateInstance(cfParams[i].ParameterType);
                        else
                            args[i] = null;
                    }
                    return createFromFile.Invoke(null, args);
                };

                // ── Build metadata references ──
                var refList = new List<object>();

                Action<string> addRef = (path) =>
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    try { refList.Add(makeRef(path)); } catch { }
                };

                // Core .NET runtime references
                // .NET 8+ (Revit 2025+): use Trusted Platform Assemblies
                // .NET Framework (Revit 2022-2024): use typeof(object).Assembly.Location
                string tpaList = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
                if (!string.IsNullOrEmpty(tpaList))
                {
                    // .NET 8+ path — add all System.* and core assemblies
                    foreach (string asmPath in tpaList.Split(';'))
                    {
                        string fn = Path.GetFileName(asmPath);
                        if (fn.StartsWith("System.") || fn == "mscorlib.dll" || fn == "netstandard.dll")
                            addRef(asmPath);
                    }
                }
                else
                {
                    // .NET Framework fallback
                    string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
                    foreach (string dll in new[] {
                        "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll",
                        "System.Console.dll", "netstandard.dll", "System.dll",
                        "System.Core.dll", "System.Xml.dll" })
                    {
                        addRef(Path.Combine(runtimeDir, dll));
                    }
                    addRef(typeof(object).Assembly.Location);
                }
                addRef(typeof(UIApplication).Assembly.Location);   // RevitAPIUI
                addRef(typeof(Document).Assembly.Location);        // RevitAPI
                try { addRef(typeof(JsonConvert).Assembly.Location); } catch { } // Newtonsoft

                // ── Parse code ──
                MethodInfo parseMethod = null;
                foreach (var m in csharpSyntaxTreeType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "ParseText") continue;
                    var p = m.GetParameters();
                    if (p.Length >= 1 && p[0].ParameterType == typeof(string))
                    {
                        parseMethod = m;
                        break;
                    }
                }
                if (parseMethod == null)
                    throw new Exception("Could not find CSharpSyntaxTree.ParseText method");

                // Build args with defaults for extra parameters
                var parseParams = parseMethod.GetParameters();
                var parseArgs = new object[parseParams.Length];
                parseArgs[0] = code;
                for (int i = 1; i < parseParams.Length; i++)
                {
                    if (parseParams[i].HasDefaultValue)
                        parseArgs[i] = parseParams[i].DefaultValue;
                    else if (parseParams[i].ParameterType.IsValueType)
                        parseArgs[i] = Activator.CreateInstance(parseParams[i].ParameterType);
                    else
                        parseArgs[i] = null;
                }
                object syntaxTree = parseMethod.Invoke(null, parseArgs);

                // ── Build typed arrays ──
                var treesArray = Array.CreateInstance(syntaxTreeBaseType, 1);
                treesArray.SetValue(syntaxTree, 0);

                var refsArray = Array.CreateInstance(metadataRefBaseType, refList.Count);
                for (int i = 0; i < refList.Count; i++)
                    refsArray.SetValue(refList[i], i);

                // ── CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary) ──
                object outputKindDll = Enum.Parse(outputKindType, "DynamicallyLinkedLibrary");
                var optionsCtor = csharpCompOptionsType.GetConstructors()
                    .OrderBy(c => c.GetParameters().Length).First();
                var ctorParams = optionsCtor.GetParameters();
                var ctorArgs = new object[ctorParams.Length];
                ctorArgs[0] = outputKindDll;
                for (int i = 1; i < ctorParams.Length; i++)
                {
                    if (ctorParams[i].HasDefaultValue)
                        ctorArgs[i] = ctorParams[i].DefaultValue;
                    else if (ctorParams[i].ParameterType.IsValueType)
                        ctorArgs[i] = Activator.CreateInstance(ctorParams[i].ParameterType);
                    else
                        ctorArgs[i] = null;
                }
                object compOptions = optionsCtor.Invoke(ctorArgs);

                // ── CSharpCompilation.Create(name, syntaxTrees, references, options) ──
                MethodInfo createMethod = null;
                foreach (var m in csharpCompilationType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name == "Create" && m.GetParameters().Length == 4)
                    {
                        createMethod = m;
                        break;
                    }
                }
                if (createMethod == null)
                    throw new Exception("Could not find CSharpCompilation.Create method");

                object compilation = createMethod.Invoke(null, new object[] {
                    "AIFamilyGenerator", treesArray, refsArray, compOptions });

                // ── Emit to MemoryStream ──
                using (var ms = new MemoryStream())
                {
                    MethodInfo emitMethod = null;
                    foreach (var m in compilation.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                    {
                        if (m.Name != "Emit") continue;
                        var ep = m.GetParameters();
                        if (ep.Length >= 1 && ep[0].ParameterType == typeof(Stream))
                        {
                            emitMethod = m;
                            break;
                        }
                    }
                    if (emitMethod == null)
                        throw new Exception("Could not find Compilation.Emit method");

                    var emitParams = emitMethod.GetParameters();
                    var emitArgs = new object[emitParams.Length];
                    emitArgs[0] = ms;
                    for (int i = 1; i < emitParams.Length; i++)
                    {
                        if (emitParams[i].HasDefaultValue)
                            emitArgs[i] = emitParams[i].DefaultValue;
                        else if (emitParams[i].ParameterType.IsValueType)
                            emitArgs[i] = Activator.CreateInstance(emitParams[i].ParameterType);
                        else
                            emitArgs[i] = null;
                    }

                    object emitResult = emitMethod.Invoke(compilation, emitArgs);

                    bool success = (bool)emitResult.GetType().GetProperty("Success").GetValue(emitResult);

                    if (!success)
                    {
                        var diagnostics = emitResult.GetType().GetProperty("Diagnostics")
                            .GetValue(emitResult) as System.Collections.IEnumerable;

                        var sb = new StringBuilder();
                        sb.AppendLine("Roslyn compilation failed:\n");
                        foreach (object diag in diagnostics)
                        {
                            string severity = diag.GetType().GetProperty("Severity")
                                .GetValue(diag).ToString();
                            if (severity == "Error")
                                sb.AppendLine("  " + diag.ToString());
                        }
                        throw new CompilationException(sb.ToString());
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    return Assembly.Load(ms.ToArray());
                }
            }
            catch (CompilationException) { throw; }
            catch (Exception ex) when (!(ex is CompilationException))
            {
                throw new Exception(
                    "Roslyn compilation error:\n" + ex.Message + "\n\n" +
                    "Make sure Microsoft.CodeAnalysis.CSharp.dll and Microsoft.CodeAnalysis.dll\n" +
                    "are in the addins folder: " + addinsFolder);
            }
        }

        private void ExecuteGeneratedCode(Assembly assembly, UIApplication uiApp)
        {
            Type genType = assembly.GetType("AIFamilyGenerator.GeneratedFamily");
            if (genType == null)
                throw new Exception("Generated code must define class 'AIFamilyGenerator.GeneratedFamily'.\n\n" +
                    "Expected namespace: AIFamilyGenerator\n" +
                    "Expected class: GeneratedFamily\n" +
                    "Expected method: public static void Execute(UIApplication uiApp)");

            MethodInfo exec = genType.GetMethod("Execute",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(UIApplication) }, null);

            if (exec == null)
                throw new Exception("GeneratedFamily must have a public static Execute(UIApplication) method.");

            exec.Invoke(null, new object[] { uiApp });
        }

        #endregion

        #region Helpers

        private static string StripMarkdownFences(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return code;
            string s = code.Trim();
            if (s.StartsWith("```"))
            {
                int nl = s.IndexOf('\n');
                if (nl > 0) s = s.Substring(nl + 1);
            }
            if (s.TrimEnd().EndsWith("```"))
                s = s.Substring(0, s.TrimEnd().Length - 3).TrimEnd();
            return s;
        }

        private void SetStatus(string msg, bool error)
        {
            _statusText.Text = msg;
            _statusText.Foreground = error ? BrushError : BrushSubText;
        }

        private static string GetPlaceholderText() =>
@"// Paste your AI-generated C# family code here.
//
// The code must follow this structure:
//
//   namespace AIFamilyGenerator
//   {
//       public static class GeneratedFamily
//       {
//           public static void Execute(UIApplication uiApp)
//           {
//               // Build the family here using Revit API
//           }
//       }
//   }
//
// How to use:
//   1. Open any AI chat (Claude, ChatGPT, etc.)
//   2. Describe the Revit family you need
//   3. Ask it to generate Revit API C# code using the structure above
//   4. Paste the code here
//   5. Click Build Family
//
// The .rfa file will be saved to your Desktop.
//
// NOTE: Run this from Revit 2022 for best compatibility.";

        #endregion
    }

    public class CompilationException : Exception
    {
        public CompilationException(string message) : base(message) { }
    }
}

// -----------------------------------------------------------------------
// External Command
// -----------------------------------------------------------------------

namespace ViewReferenceSystem.Commands
{
    using Autodesk.Revit.Attributes;
    using Autodesk.Revit.DB;
    using Autodesk.Revit.UI;
    using ViewReferenceSystem.UI;

    [Transaction(TransactionMode.Manual)]
    public class AiFamilyGeneratorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var window = new AiFamilyGeneratorWindow(
                    commandData.Application.ActiveUIDocument?.Document,
                    commandData.Application);
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
}