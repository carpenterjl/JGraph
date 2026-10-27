using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using JGraph.Controls.Themes;
using JGraph.Deploy;
using JGraph.Scripting;
using Microsoft.Win32;

namespace JGraph.Application.Scripting;

/// <summary>What a compiler app's run of a script came to: the console's lines while it ran, and its failure if any.</summary>
/// <param name="Success">Whether the script ran to its end.</param>
/// <param name="Message">The error that stopped it, or null.</param>
/// <param name="Log">What it printed.</param>
public sealed record CompilerRunOutcome(bool Success, string? Message, string Log);

/// <summary>
/// A compiler app's window (compiler and SDK plan, stage K19), one per target, in the style of
/// R2025a's target-specific apps: the main file or the exported functions, the application's info,
/// the files it needs (analysed as <c>matlab.codetools.requiredFilesAndProducts</c> finds them),
/// the target's own settings, the installer's, and the output folders; Build and Build and Package
/// in the toolbar run the <c>compiler.build</c> script the task stands for in the MATLAB session,
/// Create Build Script saves it, and Show Code shows it beside the settings as the Code View does.
/// The Production Server Archive Compiler adds a Test Client. The words are R2025b's catalogs'
/// (probe <c>k19_apps</c>); the layout is JGraph's.
/// </summary>
internal sealed class CompilerAppWindow : Window
{
    /// <summary>Builds the task.</summary>
    public static readonly RoutedCommand Build = new(nameof(Build), typeof(CompilerAppWindow));

    /// <summary>Builds the task and packages its installer.</summary>
    public static readonly RoutedCommand BuildAndPackage = new(nameof(BuildAndPackage), typeof(CompilerAppWindow));

    /// <summary>Saves the task's script as a MATLAB file.</summary>
    public static readonly RoutedCommand CreateBuildScript = new(nameof(CreateBuildScript), typeof(CompilerAppWindow));

    /// <summary>Runs the dependency analysis again.</summary>
    public static readonly RoutedCommand Reanalyze = new(nameof(Reanalyze), typeof(CompilerAppWindow));

    /// <summary>Opens the test client (a Production Server archive).</summary>
    public static readonly RoutedCommand TestClient = new(nameof(TestClient), typeof(CompilerAppWindow));

    private static readonly Regex FunctionHeader = new(@"^\s*function\s+(?:(?<outs>[^=]+?)\s*=\s*)?(?<name>[A-Za-z]\w*)\s*(?:\((?<ins>[^)]*)\))?", RegexOptions.Multiline);

    private readonly CompilerAppServices _services;
    private readonly TextBlock _status = new() { Margin = new Thickness(4, 0, 4, 0) };
    private readonly TextBox _code = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas"),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        TextWrapping = TextWrapping.NoWrap,
    };
    private readonly ToggleButton _showCode = new() { Content = "Show Code", ToolTip = "Toggle showing MATLAB code for current selections" };
    private readonly ColumnDefinition _codeColumn = new() { Width = new GridLength(0) };
    private readonly GridSplitter _codeSplitter = new() { Width = 4, HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
    private readonly TextBox _mainFile = new() { IsReadOnly = true };
    private readonly ListBox _exported = new() { MinHeight = 60 };
    private readonly ListBox _required = new() { MinHeight = 80 };
    private readonly TextBlock _requiredNote = new() { Text = "No required files detected", Margin = new Thickness(0, 2, 0, 2) };
    private readonly ListBox _installerFiles = new() { MinHeight = 48 };
    private readonly Dictionary<string, TextBox> _texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _checks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadioButton> _radios = new(StringComparer.Ordinal);
    private TestClientWindow? _testClient;
    private bool _loading;
    private bool _busy;

    /// <summary>A window over a project's task (stage K20): every edit is written back to the project at once.</summary>
    public CompilerAppWindow(JGraph.Deploy.Projects.CompilerTaskConfiguration source, CompilerAppServices services)
        : this(source.Target ?? throw new ArgumentException("The task's target is not one JGraph builds.", nameof(source)), services,
            source.ToTaskModel() ?? throw new ArgumentException("The task's target is not one JGraph builds.", nameof(source)))
    {
        Source = source;
        Title = CompilerTargets.AppTitle(Target) + " - " + source.Name;
        if (Task.IsDeployable)
        {
            Analyze();
        }

        Refresh();
    }

    public CompilerAppWindow(CompilerTarget target, CompilerAppServices services)
        : this(target, services, new CompilerTaskModel(target))
    {
    }

    private CompilerAppWindow(CompilerTarget target, CompilerAppServices services, CompilerTaskModel task)
    {
        ArgumentNullException.ThrowIfNull(services);
        Target = target;
        _services = services;
        Task = task;
        Title = CompilerTargets.AppTitle(target);
        Width = 1040;
        Height = 760;
        ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, ThemeKeys.Window);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        AutomationProperties.SetAutomationId(this, "CompilerApp." + target);

        var toolbar = new ToolBar();
        toolbar.Items.Add(ToolButton("Build", Build, "Build the task", "Build"));
        if (CompilerTargets.CanPackage(target))
        {
            toolbar.Items.Add(ToolButton("Build and Package", BuildAndPackage, "Build and Package compiler application", "BuildAndPackage"));
        }

        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(ToolButton("Create Build Script…", CreateBuildScript, "Save the current compiler task as a MATLAB script", "CreateBuildScript"));
        toolbar.Items.Add(new Separator());
        AutomationProperties.SetAutomationId(_showCode, "ShowCode");
        _showCode.Checked += (_, _) => ShowCode(true);
        _showCode.Unchecked += (_, _) => ShowCode(false);
        toolbar.Items.Add(_showCode);
        toolbar.Items.Add(new Separator());
        toolbar.Items.Add(ToolButton("Reanalyze", Reanalyze, "Find the files the task needs again", "Reanalyze"));
        if (target == CompilerTarget.ProductionServerArchive)
        {
            toolbar.Items.Add(new Separator());
            toolbar.Items.Add(ToolButton("Test Client", TestClient, "Serve the built archive locally and send it requests", "TestClient"));
        }

        foreach (Button button in toolbar.Items.OfType<Button>())
        {
            button.CommandTarget = this;
        }

        var statusBar = new StatusBar();
        AutomationProperties.SetAutomationId(_status, "Status");
        statusBar.Items.Add(new StatusBarItem { Content = _status });

        var sections = new StackPanel { Margin = new Thickness(10) };
        BuildSections(sections);
        var scroll = new ScrollViewer { Content = sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetAutomationId(_code, "Code");
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(_codeColumn);
        Grid.SetColumn(_codeSplitter, 1);
        Grid.SetColumn(_code, 2);
        body.Children.Add(scroll);
        body.Children.Add(_codeSplitter);
        body.Children.Add(_code);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(statusBar);
        root.Children.Add(body);
        Content = root;

        CommandBindings.Add(new CommandBinding(Build, async (_, _) => await RunAsync(package: false), (_, e) => e.CanExecute = Task.IsDeployable && !_busy));
        CommandBindings.Add(new CommandBinding(BuildAndPackage, async (_, _) => await RunAsync(package: true), (_, e) => e.CanExecute = Task.IsDeployable && !_busy));
        CommandBindings.Add(new CommandBinding(CreateBuildScript, (_, _) => SaveScript(), (_, e) => e.CanExecute = Task.IsDeployable));
        CommandBindings.Add(new CommandBinding(Reanalyze, (_, _) => Analyze(), (_, e) => e.CanExecute = Task.IsDeployable));
        CommandBindings.Add(new CommandBinding(TestClient, (_, _) => OpenTestClient(), (_, e) => e.CanExecute = Task.IsDeployable && !_busy));
        Closed += (_, _) => _testClient?.Close();
        Refresh();
    }

    /// <summary>The target this app builds for.</summary>
    public CompilerTarget Target { get; }

    /// <summary>The task as the window holds it.</summary>
    public CompilerTaskModel Task { get; }

    /// <summary>The project task the window is over, or null for a task the window alone holds (stage K19's form).</summary>
    public JGraph.Deploy.Projects.CompilerTaskConfiguration? Source { get; }

    /// <summary>
    /// Runs a script in the MATLAB session, as the Build and Package steps need; null when there is
    /// no session to run it in.
    /// </summary>
    public Func<string, Task<CompilerRunOutcome>>? Runner { get; set; }

    /// <summary>The script the task stands for, as the Code View shows it.</summary>
    public string Script => DeploymentScript.Generate(Task, package: CompilerTargets.CanPackage(Target));

    /// <summary>Takes <paramref name="path"/> as the main file (or the first exported function) and analyses it.</summary>
    public void SetMainFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (CompilerTargets.HasMainFile(Target))
        {
            Task.MainFile = path;
        }
        else if (!Task.ExportedFunctions.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            Task.ExportedFunctions.Add(path);
        }

        if (Task.Name.Length == 0)
        {
            Task.Name = Path.GetFileNameWithoutExtension(path);
        }

        Analyze();
        Refresh();
    }

    /// <summary>Brings the window up.</summary>
    public void Bring()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    // --- The sections -----------------------------------------------------------------------------

    private void BuildSections(StackPanel sections)
    {
        // The main file or the exported functions.
        if (CompilerTargets.HasMainFile(Target))
        {
            AutomationProperties.SetAutomationId(_mainFile, "MainFile");
            var row = new DockPanel();
            var add = new Button { Content = "Add File", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            AutomationProperties.SetAutomationId(add, "AddMainFile");
            add.Click += (_, _) => { if (PickFile("Select the main file", "MATLAB files (*.m;*.mlapp;*.mlx;*.p)|*.m;*.mlapp;*.mlx;*.p") is { } f) { SetMainFile(f); } };
            DockPanel.SetDock(add, Dock.Right);
            row.Children.Add(add);
            row.Children.Add(_mainFile);
            sections.Children.Add(Section(CompilerTargets.MainFileTitle, row));
        }
        else
        {
            AutomationProperties.SetAutomationId(_exported, "ExportedFunctions");
            sections.Children.Add(Section(CompilerTargets.ExportedFunctionsTitle, ListWithButtons(_exported,
                ("Add File", "AddExportedFunction", () => { if (PickFile("Add an exported function", "MATLAB functions (*.m)|*.m") is { } f) { SetMainFile(f); } }),
                ("Remove", "RemoveExportedFunction", () => { if (_exported.SelectedItem is string f) { Task.ExportedFunctions.Remove(f); Analyze(); Refresh(); } }))));
        }

        // The info.
        var info = new StackPanel();
        info.Children.Add(TextRow(CompilerTargets.NameLabel(Target), "Name", () => Task.Name, v => Task.Name = v));
        info.Children.Add(TextRow("Version", "Version", () => Task.Version, v => Task.Version = v));
        info.Children.Add(TextRow("Author Name", "AuthorName", () => Task.AuthorName, v => Task.AuthorName = v));
        info.Children.Add(TextRow("Company Name", "CompanyName", () => Task.CompanyName, v => Task.CompanyName = v));
        info.Children.Add(TextRow("E-mail Address", "Email", () => Task.Email, v => Task.Email = v));
        info.Children.Add(TextRow("Summary", "Summary", () => Task.Summary, v => Task.Summary = v));
        info.Children.Add(TextRow("Description", "Description", () => Task.Description, v => Task.Description = v, lines: 3));
        sections.Children.Add(Section(CompilerTargets.InfoTitle(Target), info));

        // The target's own settings.
        switch (Target)
        {
            case CompilerTarget.StandaloneApplication:
                var executable = new StackPanel();
                executable.Children.Add(TextRow("Executable Name", "ExecutableName", () => Task.ExecutableName, v => Task.ExecutableName = v));
                executable.Children.Add(RadioRow("Application Type", "ApplicationType",
                    ("Standalone Application", "StandaloneApplication", () => !Task.WindowsApplication, () => Task.WindowsApplication = false),
                    ("Standalone Windows Application", "StandaloneWindowsApplication", () => Task.WindowsApplication, () => Task.WindowsApplication = true)));
                executable.Children.Add(RadioRow("Input Type", "InputType",
                    ("Treat inputs to the app as a MATLAB character vector", "InputsAsChar", () => !Task.TreatInputsAsNumeric, () => Task.TreatInputsAsNumeric = false),
                    ("Treat inputs to the app as a numeric MATLAB double", "InputsAsNumeric", () => Task.TreatInputsAsNumeric, () => Task.TreatInputsAsNumeric = true)));
                sections.Children.Add(Section("Executable Details", executable));
                var images = new StackPanel();
                images.Children.Add(FileRow("Icon", "ExecutableIcon", () => Task.ExecutableIcon, v => Task.ExecutableIcon = v));
                images.Children.Add(FileRow("Splash Screen", "ExecutableSplashScreen", () => Task.ExecutableSplashScreen, v => Task.ExecutableSplashScreen = v));
                sections.Children.Add(Section("Images for the Standalone Application", images));
                break;
            case CompilerTarget.DotNetAssembly:
                var dotnet = new StackPanel();
                dotnet.Children.Add(RadioRow(".NET API Selection", "Interface",
                    ("MATLAB Data API", "MatlabDataApi", () => Task.Interface == "matlab-data", () => Task.Interface = "matlab-data"),
                    ("MWArray API", "MwArrayApi", () => Task.Interface == "mwarray", () => Task.Interface = "mwarray")));
                dotnet.Children.Add(LabelRow(".NET Version", ".NET 5.0 or higher (JGraph's assemblies are built for .NET 8)"));
                sections.Children.Add(Section("Assembly Options", dotnet));
                break;
            case CompilerTarget.CppSharedLibrary:
                sections.Children.Add(Section("C++ API Selection", LabelRow("API", "MATLAB Data API (the mwArray API is out of scope, decision KD7)")));
                break;
            case CompilerTarget.ProductionServerArchive:
                var signatures = new StackPanel();
                signatures.Children.Add(new TextBlock { Text = "Add or create a function signature file to help clients use your MATLAB functions", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
                var row = new DockPanel();
                var create = new Button { Content = "Create File", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
                AutomationProperties.SetAutomationId(create, "CreateSignatures");
                create.Click += (_, _) => CreateSignaturesTemplate();
                var existing = new Button { Content = "Add Existing File", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
                AutomationProperties.SetAutomationId(existing, "AddSignatures");
                existing.Click += (_, _) => { if (PickFile("Select the function signature file", "JSON files (*.json)|*.json") is { } f) { Task.FunctionSignatures = f; Refresh(); } };
                DockPanel.SetDock(create, Dock.Right);
                DockPanel.SetDock(existing, Dock.Right);
                var box = new TextBox { IsReadOnly = true };
                AutomationProperties.SetAutomationId(box, "FunctionSignatures");
                _texts["FunctionSignatures"] = box;
                row.Children.Add(create);
                row.Children.Add(existing);
                row.Children.Add(box);
                signatures.Children.Add(row);
                sections.Children.Add(Section("MATLAB Function Signature File", signatures));
                break;
            case CompilerTarget.ExcelAddIn:
                sections.Children.Add(Section("Class Info", TextRow("Class Name", "ClassName", () => Task.ClassName, v => Task.ClassName = v)));
                break;
        }

        // The files the task needs.
        AutomationProperties.SetAutomationId(_required, "RequiredFiles");
        var required = new StackPanel();
        required.Children.Add(_requiredNote);
        required.Children.Add(ListWithButtons(_required,
            ("Add File", "AddRequiredFile", () => { if (PickFile("Add a file the task needs", "All files (*.*)|*.*") is { } f) { AddAdditional(f); } }),
            ("Add Folder", "AddRequiredFolder", () => { if (PickFolder("Add a folder the task needs") is { } f) { AddAdditional(f); } }),
            ("Remove", "RemoveRequiredFile", () => { if (_required.SelectedItem is string f && Task.AdditionalFiles.Remove(f)) { Refresh(); } })));
        var detect = new CheckBox { Content = "Autodetect Data Files", ToolTip = "Automatically include data files that are provided as inputs to functions such as load and fopen", Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetAutomationId(detect, "AutoDetectDataFiles");
        _checks["AutoDetectDataFiles"] = detect;
        detect.Checked += (_, _) => Changed(() => Task.AutoDetectDataFiles = true);
        detect.Unchecked += (_, _) => Changed(() => Task.AutoDetectDataFiles = false);
        required.Children.Add(detect);
        sections.Children.Add(Section(CompilerTargets.RequiredFilesTitle(Target), required));

        // Obfuscation.
        var obfuscate = new CheckBox { Content = "Obfuscate all file names, folder structures, code, and data in build output" };
        AutomationProperties.SetAutomationId(obfuscate, "Obfuscate");
        _checks["Obfuscate"] = obfuscate;
        obfuscate.Checked += (_, _) => Changed(() => Task.Obfuscate = true);
        obfuscate.Unchecked += (_, _) => Changed(() => Task.Obfuscate = false);
        sections.Children.Add(Section("Code Obfuscation", obfuscate));

        // The installer.
        if (CompilerTargets.CanPackage(Target))
        {
            var installer = new StackPanel();
            installer.Children.Add(RadioRow("Package Type", "RuntimeDelivery",
                (CompilerTargets.RuntimeDeliveryText(RuntimeDeliveryChoice.Web), "RuntimeWeb", () => Task.RuntimeDelivery == RuntimeDeliveryChoice.Web, () => Task.RuntimeDelivery = RuntimeDeliveryChoice.Web),
                (CompilerTargets.RuntimeDeliveryText(RuntimeDeliveryChoice.Installer), "RuntimeInstaller", () => Task.RuntimeDelivery == RuntimeDeliveryChoice.Installer, () => Task.RuntimeDelivery = RuntimeDeliveryChoice.Installer),
                (CompilerTargets.RuntimeDeliveryText(RuntimeDeliveryChoice.None), "RuntimeNone", () => Task.RuntimeDelivery == RuntimeDeliveryChoice.None, () => Task.RuntimeDelivery = RuntimeDeliveryChoice.None)));
            installer.Children.Add(TextRow("Installer Name", "InstallerName", () => Task.InstallerName, v => Task.InstallerName = v));
            installer.Children.Add(TextRow("Installer Notes", "InstallerNotes", () => Task.InstallerNotes, v => Task.InstallerNotes = v, lines: 3));
            installer.Children.Add(TextRow("Install Root", "InstallRoot", () => Task.InstallRoot, v => Task.InstallRoot = v));
            installer.Children.Add(TextRow("Install subfolder", "InstallSubfolder", () => Task.InstallSubfolder, v => Task.InstallSubfolder = v));
            installer.Children.Add(FileRow("Icon", "InstallerIcon", () => Task.InstallerIcon, v => Task.InstallerIcon = v));
            installer.Children.Add(FileRow("Splash Screen", "InstallerSplash", () => Task.InstallerSplash, v => Task.InstallerSplash = v));
            installer.Children.Add(FileRow("Sidebar Image", "InstallerLogo", () => Task.InstallerLogo, v => Task.InstallerLogo = v));
            sections.Children.Add(Section("Installer Settings", installer));
            AutomationProperties.SetAutomationId(_installerFiles, "InstallerFiles");
            sections.Children.Add(Section("Files Packaged in the Installer", ListWithButtons(_installerFiles,
                ("Add File", "AddInstallerFile", () => { if (PickFile("Add a file to package in the installer", "All files (*.*)|*.*") is { } f && !Task.InstallerAdditionalFiles.Contains(f, StringComparer.OrdinalIgnoreCase)) { Task.InstallerAdditionalFiles.Add(f); Refresh(); } }),
                ("Remove", "RemoveInstallerFile", () => { if (_installerFiles.SelectedItem is string f && Task.InstallerAdditionalFiles.Remove(f)) { Refresh(); } }))));
        }

        // The output folders.
        var output = new StackPanel();
        output.Children.Add(FolderRow("Output Folder for the Build", "OutputFolder", () => Task.OutputFolder, () => Task.EffectiveOutputFolder, v => Task.OutputFolder = v));
        if (CompilerTargets.CanPackage(Target))
        {
            output.Children.Add(FolderRow("Output Folder for the Installer", "PackageFolder", () => Task.PackageFolder, () => Task.EffectivePackageFolder, v => Task.PackageFolder = v));
        }

        sections.Children.Add(Section("Output Locations", output));
    }

    private static GroupBox Section(string title, UIElement content) =>
        new() { Header = title, Content = content, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 4, 8, 6) };

    private static Button ToolButton(string text, ICommand command, string tip, string id)
    {
        var button = new Button { Content = text, Command = command, ToolTip = tip, Padding = new Thickness(8, 2, 8, 2) };
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    private static Grid Row(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    private Grid TextRow(string label, string id, Func<string> read, Action<string> write, int lines = 1)
    {
        var box = new TextBox { AcceptsReturn = lines > 1, TextWrapping = lines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap, MinLines = lines, VerticalScrollBarVisibility = lines > 1 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled };
        AutomationProperties.SetAutomationId(box, id);
        _texts[id] = box;
        box.Text = read();
        box.TextChanged += (_, _) => Changed(() => write(box.Text));
        return Row(label, box);
    }

    private static Grid LabelRow(string label, string text) => Row(label, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });

    private Grid FileRow(string label, string id, Func<string> read, Action<string> write)
    {
        var box = new TextBox { IsReadOnly = true };
        AutomationProperties.SetAutomationId(box, id);
        _texts[id] = box;
        var panel = new DockPanel();
        var clear = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        clear.Click += (_, _) => Changed(() => write(""));
        var select = new Button { Content = "Select", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        AutomationProperties.SetAutomationId(select, "Select" + id);
        select.Click += (_, _) => { if (PickFile("Select an image", "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.ico)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.ico") is { } f) { Changed(() => write(f)); } };
        DockPanel.SetDock(clear, Dock.Right);
        DockPanel.SetDock(select, Dock.Right);
        panel.Children.Add(clear);
        panel.Children.Add(select);
        panel.Children.Add(box);
        return Row(label, panel);
    }

    private Grid FolderRow(string label, string id, Func<string> read, Func<string> effective, Action<string> write)
    {
        var box = new TextBox();
        AutomationProperties.SetAutomationId(box, id);
        _texts[id] = box;
        box.TextChanged += (_, _) => Changed(() => write(box.Text == effective() ? "" : box.Text));
        var panel = new DockPanel();
        var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        browse.Click += (_, _) => { if (PickFolder(label) is { } f) { Changed(() => write(f)); } };
        DockPanel.SetDock(browse, Dock.Right);
        panel.Children.Add(browse);
        panel.Children.Add(box);
        return Row(label, panel);
    }

    private StackPanel RadioRow(string label, string group, params (string Text, string Id, Func<bool> Read, Action Choose)[] choices)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 2) });
        foreach ((string text, string id, Func<bool> read, Action choose) in choices)
        {
            var radio = new RadioButton { Content = text, GroupName = group + GetHashCode(), Margin = new Thickness(12, 1, 0, 1), IsChecked = read() };
            AutomationProperties.SetAutomationId(radio, id);
            _radios[id] = radio;
            radio.Checked += (_, _) => Changed(choose);
            panel.Children.Add(radio);
        }

        return panel;
    }

    private static DockPanel ListWithButtons(ListBox list, params (string Text, string Id, Action Click)[] buttons)
    {
        var panel = new DockPanel();
        var column = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        foreach ((string text, string id, Action click) in buttons)
        {
            var button = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 0, 4), MinWidth = 90 };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => click();
            column.Children.Add(button);
        }

        DockPanel.SetDock(column, Dock.Right);
        panel.Children.Add(column);
        panel.Children.Add(list);
        return panel;
    }

    // --- The task -----------------------------------------------------------------------------------

    private void Changed(Action change)
    {
        if (_loading)
        {
            return;
        }

        change();
        Refresh();
    }

    private void AddAdditional(string path)
    {
        if (!Task.AdditionalFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            Task.AdditionalFiles.Add(path);
            Refresh();
        }
    }

    /// <summary>Shows the task in every control and the script in the code pane.</summary>
    private void Refresh()
    {
        _loading = true;
        try
        {
            _mainFile.Text = Task.MainFile ?? "";
            _exported.ItemsSource = null;
            _exported.ItemsSource = Task.ExportedFunctions.ToList();
            var required = Task.RequiredFiles.Concat(Task.AdditionalFiles).ToList();
            _required.ItemsSource = required;
            _requiredNote.Visibility = required.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _installerFiles.ItemsSource = Task.InstallerAdditionalFiles.ToList();
            foreach ((string id, TextBox box) in _texts)
            {
                string value = id switch
                {
                    "Name" => Task.Name,
                    "Version" => Task.Version,
                    "AuthorName" => Task.AuthorName,
                    "CompanyName" => Task.CompanyName,
                    "Email" => Task.Email,
                    "Summary" => Task.Summary,
                    "Description" => Task.Description,
                    "ExecutableName" => Task.ExecutableName,
                    "ExecutableIcon" => Task.ExecutableIcon,
                    "ExecutableSplashScreen" => Task.ExecutableSplashScreen,
                    "FunctionSignatures" => Task.FunctionSignatures,
                    "ClassName" => Task.ClassName,
                    "InstallerName" => Task.InstallerName,
                    "InstallerNotes" => Task.InstallerNotes,
                    "InstallRoot" => Task.InstallRoot,
                    "InstallSubfolder" => Task.InstallSubfolder,
                    "InstallerIcon" => Task.InstallerIcon,
                    "InstallerSplash" => Task.InstallerSplash,
                    "InstallerLogo" => Task.InstallerLogo,
                    "OutputFolder" => Task.EffectiveOutputFolder,
                    "PackageFolder" => Task.EffectivePackageFolder,
                    _ => box.Text,
                };
                if (box.Text != value)
                {
                    box.Text = value;
                }
            }

            _checks["AutoDetectDataFiles"].IsChecked = Task.AutoDetectDataFiles;
            _checks["Obfuscate"].IsChecked = Task.Obfuscate;
            foreach ((string id, RadioButton radio) in _radios)
            {
                bool on = id switch
                {
                    "StandaloneApplication" => !Task.WindowsApplication,
                    "StandaloneWindowsApplication" => Task.WindowsApplication,
                    "InputsAsChar" => !Task.TreatInputsAsNumeric,
                    "InputsAsNumeric" => Task.TreatInputsAsNumeric,
                    "MatlabDataApi" => Task.Interface == "matlab-data",
                    "MwArrayApi" => Task.Interface == "mwarray",
                    "RuntimeWeb" => Task.RuntimeDelivery == RuntimeDeliveryChoice.Web,
                    "RuntimeInstaller" => Task.RuntimeDelivery == RuntimeDeliveryChoice.Installer,
                    "RuntimeNone" => Task.RuntimeDelivery == RuntimeDeliveryChoice.None,
                    _ => radio.IsChecked == true,
                };
                if (radio.IsChecked != on)
                {
                    radio.IsChecked = on;
                }
            }

            _code.Text = Task.IsDeployable ? Script : "% " + Task.StatusText;
            _status.Text = Task.StatusText;
            if (Source is { } source)
            {
                // A project's task is saved as it is edited, as R2025b saves one.
                try
                {
                    source.ApplyTaskModel(Task);
                }
                catch (Exception ex) when (ex is JGraph.Deploy.Projects.ProjectException or IOException or UnauthorizedAccessException)
                {
                    _status.Text = "The task could not be saved to the project: " + ex.Message;
                }
            }
        }
        finally
        {
            _loading = false;
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private void Analyze()
    {
        Task.RequiredFiles.Clear();
        if (!Task.IsDeployable)
        {
            return;
        }

        try
        {
            DependencyReport report = _services.Analyze([.. Task.SourceFiles, .. Task.AdditionalFiles]);
            var sources = new HashSet<string>(Task.SourceFiles.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            foreach (DependencyFile file in report.Files)
            {
                if (!sources.Contains(Path.GetFullPath(file.Path)) && !Task.AdditionalFiles.Contains(file.Path, StringComparer.OrdinalIgnoreCase))
                {
                    Task.RequiredFiles.Add(file.Path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _status.Text = "The analysis failed: " + ex.Message;
        }
    }

    private void ShowCode(bool on)
    {
        _codeColumn.Width = on ? new GridLength(420) : new GridLength(0);
        _codeSplitter.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _showCode.Content = on ? "Hide Code" : "Show Code";
    }

    private string? PickFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            InitialDirectory = Task.ProjectRoot ?? _services.CurrentFolder,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = Task.ProjectRoot ?? _services.CurrentFolder };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    // --- Build, package, script and test client ------------------------------------------------------

    private async Task RunAsync(bool package)
    {
        if (Runner is not { } runner)
        {
            MessageBox.Show(this, "There is no MATLAB session to build in.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!CompilerTargets.IsBuilt(Target))
        {
            MessageBox.Show(this, $"JGraph's compiler does not build {CompilerTargets.TypeName(Target)}s yet; the script is shown and can be saved.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        _busy = true;
        CommandManager.InvalidateRequerySuggested();
        var dialog = new CompilerPackagingDialog(Task, package) { Owner = this };
        dialog.Show();
        try
        {
            dialog.StartStep(0);
            CompilerRunOutcome outcome = await runner(DeploymentScript.Generate(Task, package: false));
            dialog.EndStep(0, outcome);
            if (outcome.Success && package)
            {
                dialog.StartStep(1);
                CompilerRunOutcome packaged = await runner(DeploymentScript.GeneratePackageOnly(Task));
                dialog.EndStep(1, packaged);
                outcome = packaged;
            }

            dialog.Finish(outcome, package ? Task.EffectivePackageFolder : Task.EffectiveOutputFolder);
            _status.Text = outcome.Success ? "Process completed successfully" : "An error occurred while deploying";
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void SaveScript()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Create Build Script",
            Filter = "MATLAB script (*.m)|*.m",
            FileName = Task.EffectiveName + "_build.m",
            InitialDirectory = Task.ProjectRoot,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, Script.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
            _status.Text = $"Saved {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Writes a function signature file template for the exported functions beside the first one, as
    /// R2025b's Create File does (its <c>generateFunctionSignaturesTemplate</c>): each function's
    /// inputs and outputs from its header, their types left to fill in.
    /// </summary>
    private void CreateSignaturesTemplate()
    {
        if (Task.ProjectRoot is not { } root)
        {
            return;
        }

        var text = new System.Text.StringBuilder();
        text.Append("// Function Signatures\n// To optionally specify argument types and/or sizes, search for \"type\"\n// and insert the appropriate specifiers inside the brackets. For example:\n//\n//    \"type\": [\"double\", \"size=1,1\"]\n//\n// To modify function or parameter help text, search for \"purpose\" and edit\n// the values.\n//\n// JSON-formatted text below this line.\n{\n    \"_schemaVersion\": \"1.1.0\"");
        foreach (string file in Task.ExportedFunctions)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string[] inputs = [], outputs = [];
            try
            {
                if (FunctionHeader.Match(File.ReadAllText(file)) is { Success: true } header)
                {
                    inputs = Names(header.Groups["ins"].Value);
                    outputs = Names(header.Groups["outs"].Value.Trim('[', ']', ' '));
                }
            }
            catch (IOException)
            {
            }

            text.Append($",\n    \"{name}\": {{\n        \"inputs\": [{string.Join(", ", inputs.Select(Parameter))}],\n        \"outputs\": [{string.Join(", ", outputs.Select(Parameter))}],\n        \"purpose\": \"\"\n    }}");
        }

        text.Append("\n}\n");
        string path = Path.Combine(root, Task.EffectiveName + "Signatures.json");
        for (int n = 1; File.Exists(path); n++)
        {
            path = Path.Combine(root, $"{Task.EffectiveName}Signatures{n}.json");
        }

        File.WriteAllText(path, text.ToString());
        Task.FunctionSignatures = path;
        Refresh();
        _status.Text = $"Wrote {path}";

        static string[] Names(string list) => list.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Where(static n => n != "~").ToArray();
        static string Parameter(string name) => $"{{\"name\": \"{name}\", \"type\": [], \"purpose\": \"\"}}";
    }

    private void OpenTestClient()
    {
        if (_testClient is { } open)
        {
            MessageBox.Show(this, "A test client is already open. Close it before launching a new test client.", "Cannot Open Test Client", MessageBoxButton.OK, MessageBoxImage.Information);
            open.Activate();
            return;
        }

        string archive = Path.Combine(Task.EffectiveOutputFolder, Task.EffectiveName + ".ctf");
        _testClient = new TestClientWindow(archive, Task.ExportedFunctions.Select(Path.GetFileNameWithoutExtension).ToList()!) { Owner = this };
        _testClient.Closed += (_, _) => _testClient = null;
        _testClient.Show();
    }
}
