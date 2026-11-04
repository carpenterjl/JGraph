using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JGraph.Controls.Themes;
using JGraph.Deploy;
using Microsoft.Win32;

namespace JGraph.Application.Scripting;

/// <summary>Which of App Designer's Share windows (R2025b's <c>AppShareWindow</c>'s appType).</summary>
internal enum AppShareKind
{
    /// <summary>Share &gt; Standalone Desktop App: <c>StandalonePreviewWindow</c>.</summary>
    Standalone,
}

/// <summary>
/// App Designer's "Configure Standalone App for Sharing" (App Designer window plan W12): R2025b's
/// <c>compiler.internal.ui.StandalonePreviewWindow</c>, whose page is the share window bundle's preview widgets
/// with <c>compiler_ui_common</c>'s words. App Appearance holds the app's name and version from App Details and
/// the images to be included (the splash screen and the icon, each with Browse and Remove); the files required
/// for the app to run are the analysis's (<c>runFileAnalysisWithProgressDialog</c>, here the session's
/// <c>requiredFilesAndProducts</c>) and the user's own files and folders; Package runs the script
/// <c>deployAppDesignerProject</c> writes (<see cref="AppShareScript"/>) in the MATLAB session, its two steps
/// under the packaging dialog with Show Log and Open Output Folder. JGraph adds the package type (the runtime
/// delivery of compiler plan K18) and Show Code, and leaves out R2025b's "Manage App Sharing with a MATLAB
/// Project" pane.
/// </summary>
internal sealed class AppShareWindow : Window
{
    private const string PackagingResources = "packagingResources";

    private readonly AppShareKind _kind;
    private readonly string _appPath;
    private readonly AppShareDetails _details;
    private readonly List<string> _analysed = [];
    private readonly List<string> _custom = [];
    private readonly ListBox _files = new() { MinHeight = 110, MaxHeight = 240 };
    private readonly TextBlock _placeholder = new() { Text = "No custom files currently added", FontStyle = FontStyles.Italic, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _package = new() { Content = "Package", Padding = new Thickness(16, 4, 16, 4), MinWidth = 110 };
    private readonly Button _removeFile = new() { Content = "Remove", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 0, 4), MinWidth = 90, IsEnabled = false };
    private readonly ToggleButton _showCode = new() { Content = "Show Code", ToolTip = "Toggle showing MATLAB code for current selections", Padding = new Thickness(10, 3, 10, 3) };
    private readonly TextBox _code = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas"),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        TextWrapping = TextWrapping.NoWrap,
        Height = 220,
        Margin = new Thickness(0, 8, 0, 0),
        Visibility = Visibility.Collapsed,
    };
    private readonly Dictionary<RuntimeDeliveryChoice, RadioButton> _delivery = [];
    private readonly ImageChoice _splash;
    private readonly ImageChoice _icon;
    private bool _busy;

    public AppShareWindow(AppShareKind kind, string appPath, AppShareDetails details)
    {
        ArgumentException.ThrowIfNullOrEmpty(appPath);
        ArgumentNullException.ThrowIfNull(details);
        _kind = kind;
        _appPath = Path.GetFullPath(appPath);
        _details = details;
        Title = "Configure Standalone App for Sharing";
        // getCenteredPosition(675, 880, 0.8): the size, held to 80% of the screen.
        Width = Math.Min(SystemParameters.PrimaryScreenWidth * 0.8, 675);
        Height = Math.Min(SystemParameters.PrimaryScreenHeight * 0.8, 880);
        MinWidth = 500;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, ThemeKeys.Window);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        AutomationProperties.SetAutomationId(this, "ShareWindow." + kind);

        _splash = new ImageChoice(this, "Splash Screen", "SplashScreen", "default_splash.png");
        _icon = new ImageChoice(this, "Icon", "Icon", "default_icon_48.png");
        Content = BuildContent();
        Loaded += async (_, _) => await AnalyzeAsync();
    }

    /// <summary>The dependency analysis the files required come from; null with no session.</summary>
    public Func<IReadOnlyList<string>, bool, Task<DependencyReport>>? Analyzer { get; set; }

    /// <summary>Runs a script in the MATLAB session, as Package needs; null with no session.</summary>
    public Func<string, Task<CompilerRunOutcome>>? Runner { get; set; }

    /// <summary>How the installer brings the runtime (JGraph's own choice on this window; R2025b's script takes the default).</summary>
    public RuntimeDeliveryChoice RuntimeDelivery { get; private set; } = RuntimeDeliveryChoice.Web;

    /// <summary>The files required for the app to run as the window lists them: the analysis's, then the user's own.</summary>
    public IReadOnlyList<string> Files => [.. _analysed, .. _custom];

    /// <summary>The whole script Package runs, as Show Code shows it.</summary>
    public string Script => AppShareScript.Standalone(_appPath, Files, _splash.File, _icon.File, _details, RuntimeDelivery);

    // --- The page --------------------------------------------------------------------------------------

    private UIElement BuildContent()
    {
        var page = new StackPanel { Margin = new Thickness(15) };

        // App Appearance: the name and version from App Details, the images to be included in the app.
        var appearance = new StackPanel();
        var heads = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        heads.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heads.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        heads.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heads.Children.Add(Property("App Name", "Share.AppName", _details.Name, 0));
        heads.Children.Add(Property("Version", "Share.Version", _details.Version, 2));
        appearance.Children.Add(heads);
        appearance.Children.Add(new TextBlock { Text = "Images to be included in the app", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 7) });
        var images = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
        images.Children.Add(_splash.Panel);
        _icon.Panel.Margin = new Thickness(50, 0, 0, 0);
        images.Children.Add(_icon.Panel);
        appearance.Children.Add(images);
        page.Children.Add(Pane("App Appearance", appearance));

        // The files required for the app to run.
        var required = new StackPanel();
        required.Children.Add(new TextBlock { Text = "Most files will be automatically packaged with your app. Add additional custom files or folders.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) });
        AutomationProperties.SetAutomationId(_placeholder, "Share.NoFiles");
        _placeholder.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        required.Children.Add(_placeholder);
        AutomationProperties.SetAutomationId(_files, "Share.Files");
        _files.SelectionChanged += (_, _) => _removeFile.IsEnabled = _files.SelectedItem is string f && _custom.Contains(f, StringComparer.OrdinalIgnoreCase);
        var list = new DockPanel();
        var buttons = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        buttons.Children.Add(ListButton("Add File", "Share.AddFile", "Add files required for your app to run", AddFile));
        buttons.Children.Add(ListButton("Add Folder", "Share.AddFolder", "Add folders with files required for your app to run", AddFolder));
        AutomationProperties.SetAutomationId(_removeFile, "Share.RemoveFile");
        _removeFile.Click += (_, _) => RemoveFile();
        buttons.Children.Add(_removeFile);
        DockPanel.SetDock(buttons, Dock.Right);
        list.Children.Add(buttons);
        list.Children.Add(_files);
        required.Children.Add(list);
        page.Children.Add(Pane("Files required for the app to run", required));

        // JGraph's own: the package type (R2025b's page takes the installer options' default).
        var delivery = new StackPanel();
        foreach (RuntimeDeliveryChoice choice in new[] { RuntimeDeliveryChoice.Web, RuntimeDeliveryChoice.Installer, RuntimeDeliveryChoice.None })
        {
            var radio = new RadioButton { Content = CompilerTargets.RuntimeDeliveryText(choice), GroupName = "Share.Runtime", Margin = new Thickness(4, 2, 0, 2), IsChecked = choice == RuntimeDelivery };
            AutomationProperties.SetAutomationId(radio, "Share.Runtime" + choice);
            radio.Checked += (_, _) =>
            {
                RuntimeDelivery = choice;
                RefreshCode();
            };
            _delivery[choice] = radio;
            delivery.Children.Add(radio);
        }

        page.Children.Add(Pane("Package Type", delivery));

        // The bar: the status, Show Code, Package; the code under it.
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        AutomationProperties.SetAutomationId(_package, "Share.Package");
        _package.Click += async (_, _) => await PackageAsync();
        DockPanel.SetDock(_package, Dock.Right);
        bar.Children.Add(_package);
        AutomationProperties.SetAutomationId(_showCode, "Share.ShowCode");
        _showCode.Margin = new Thickness(0, 0, 8, 0);
        _showCode.Checked += (_, _) => ShowCode(true);
        _showCode.Unchecked += (_, _) => ShowCode(false);
        DockPanel.SetDock(_showCode, Dock.Right);
        bar.Children.Add(_showCode);
        AutomationProperties.SetAutomationId(_status, "Share.Status");
        bar.Children.Add(_status);
        page.Children.Add(bar);
        AutomationProperties.SetAutomationId(_code, "Share.Code");
        page.Children.Add(_code);

        Refresh();
        return new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>A titled pane, as R2025b's <c>mw-titlepane</c> opens: the title, its content under it.</summary>
    private static Expander Pane(string title, UIElement content)
    {
        var expander = new Expander { Header = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }, Content = content, IsExpanded = true, Margin = new Thickness(0, 0, 0, 14), Padding = new Thickness(8, 4, 8, 4) };
        AutomationProperties.SetName(expander, title);
        return expander;
    }

    private static StackPanel Property(string label, string id, string value, int column)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
        AutomationProperties.SetAutomationId(text, id);
        panel.Children.Add(text);
        Grid.SetColumn(panel, column);
        return panel;
    }

    private static Button ListButton(string text, string id, string tip, Action click)
    {
        var button = new Button { Content = text, ToolTip = tip, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 0, 4), MinWidth = 90 };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    // --- The files -------------------------------------------------------------------------------------

    private async Task AnalyzeAsync()
    {
        if (Analyzer is not { } analyze)
        {
            _status.Text = "There is no MATLAB session to analyse the app's files in.";
            return;
        }

        _status.Text = "Analyzing the files required for the app to run…";
        try
        {
            DependencyReport report = await analyze([_appPath], false);
            _analysed.Clear();
            foreach (DependencyFile file in report.Files)
            {
                string full = Path.GetFullPath(file.Path);
                if (!string.Equals(full, _appPath, StringComparison.OrdinalIgnoreCase) && !_analysed.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    _analysed.Add(full);
                }
            }

            _status.Text = string.Empty;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _status.Text = "The analysis failed: " + ex.Message;
        }

        Refresh();
    }

    private void AddFile()
    {
        var dialog = new OpenFileDialog { Title = "Add File", Filter = "All files (*.*)|*.*", InitialDirectory = Path.GetDirectoryName(_appPath), Multiselect = true };
        if (dialog.ShowDialog(this) == true)
        {
            AddCustom(dialog.FileNames);
        }
    }

    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select Folder", InitialDirectory = Path.GetDirectoryName(_appPath) };
        if (dialog.ShowDialog(this) == true)
        {
            AddCustom([dialog.FolderName]);
        }
    }

    /// <summary>Puts <paramref name="paths"/> among the custom files, each once, as the page's file widget takes them.</summary>
    public void AddCustom(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (string path in paths)
        {
            string full = Path.GetFullPath(path);
            if (!Files.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                _custom.Add(full);
            }
        }

        Refresh();
    }

    private void RemoveFile()
    {
        if (_files.SelectedItem is string file && _custom.RemoveAll(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        IReadOnlyList<string> files = Files;
        _files.ItemsSource = files;
        _placeholder.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _removeFile.IsEnabled = false;
        RefreshCode();
    }

    private void RefreshCode() => _code.Text = Script;

    private void ShowCode(bool on)
    {
        _code.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _showCode.Content = on ? "Hide Code" : "Show Code";
    }

    // --- Package ---------------------------------------------------------------------------------------

    /// <summary>
    /// Package (<c>_onPackageClick</c>, then <c>deployAppDesignerProject</c>): the button rests, the packaging dialog opens, the
    /// build runs and then the installer, each its own run so that each step reports on its own, and the dialog ends with the
    /// result and Open Output Folder on the app's release folder.
    /// </summary>
    private async Task PackageAsync()
    {
        if (_busy)
        {
            return;
        }

        if (Runner is not { } runner)
        {
            MessageBox.Show(this, "There is no MATLAB session to build in.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _busy = true;
        _package.IsEnabled = false;
        var dialog = new CompilerPackagingDialog("Standalone App Packaging Progress", _details.Name, _details.Version,
            [("Building Standalone App", "Step.Build"), ("Creating Installer", "Step.Package")]) { Owner = this };
        dialog.Show();
        try
        {
            dialog.StartStep(0);
            CompilerRunOutcome outcome = await runner(AppShareScript.StandaloneBuild(_appPath, Files, _splash.File, _icon.File, _details));
            dialog.EndStep(0, outcome);
            if (outcome.Success)
            {
                dialog.StartStep(1);
                CompilerRunOutcome packaged = await runner(AppShareScript.StandalonePackage(_appPath, _details, RuntimeDelivery));
                dialog.EndStep(1, packaged);
                outcome = packaged;
            }

            dialog.Finish(outcome, AppShareScript.OutputFolder(_appPath));
            _status.Text = outcome.Success ? "Process completed successfully" : "An error occurred while deploying";
        }
        finally
        {
            _busy = false;
            _package.IsEnabled = true;
        }
    }

    // --- The images ------------------------------------------------------------------------------------

    /// <summary>
    /// One of the page's image selectors (the bundle's <c>ImageSelector</c>): the picture, the default's until one is chosen, the
    /// type's words under it and Browse or Remove; an image of another type is refused in the selector's words.
    /// </summary>
    private sealed class ImageChoice
    {
        private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".gif", ".bmp", ".jpg", ".jpeg" };

        private readonly AppShareWindow _owner;
        private readonly string _defaultFile;
        private readonly Image _picture = new() { Stretch = Stretch.Uniform, MaxWidth = 120, MaxHeight = 90 };
        private readonly TextBlock _file = new() { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150, Margin = new Thickness(0, 4, 0, 0) };
        private readonly Button _action = new() { Content = "Browse", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };

        public ImageChoice(AppShareWindow owner, string words, string id, string defaultFile)
        {
            _owner = owner;
            _defaultFile = defaultFile;
            var frame = new Border { BorderThickness = new Thickness(1), Width = 150, Height = 100, Child = _picture };
            frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);
            Panel.Children.Add(frame);
            Panel.Children.Add(new TextBlock { Text = words, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            AutomationProperties.SetAutomationId(_file, "Share." + id + "File");
            Panel.Children.Add(_file);
            AutomationProperties.SetAutomationId(_action, "Share." + id + "Action");
            _action.Click += (_, _) => Act();
            Panel.Children.Add(_action);
            Show();
        }

        /// <summary>The panel on the page.</summary>
        public StackPanel Panel { get; } = new() { Width = 150 };

        /// <summary>The chosen image's path, or empty for the default.</summary>
        public string File { get; private set; } = string.Empty;

        private void Act()
        {
            if (File.Length > 0)
            {
                File = string.Empty;
            }
            else
            {
                var dialog = new OpenFileDialog { Title = "Select Image", Filter = "Images (*.png;*.gif;*.bmp;*.jpg;*.jpeg)|*.png;*.gif;*.bmp;*.jpg;*.jpeg|All files (*.*)|*.*" };
                if (dialog.ShowDialog(_owner) != true)
                {
                    return;
                }

                if (!Extensions.Contains(Path.GetExtension(dialog.FileName)))
                {
                    MessageBox.Show(_owner, "File must be of type .PNG, .GIF, .BMP, .JPG, or .JPEG", "Invalid Image File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                File = dialog.FileName;
            }

            Show();
            _owner.RefreshCode();
        }

        private void Show()
        {
            string shown = File.Length > 0 ? File : Path.Combine(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), "toolbox", "compiler", PackagingResources, _defaultFile);
            _picture.Source = Picture(shown);
            _file.Text = File.Length > 0 ? Path.GetFileName(File) : "Default";
            _file.ToolTip = File.Length > 0 ? File : null;
            _action.Content = File.Length > 0 ? "Remove" : "Browse";
        }

        private static BitmapImage? Picture(string path)
        {
            if (!System.IO.File.Exists(path))
            {
                return null;
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
