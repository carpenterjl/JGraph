// Stage W1's window check host (App Designer window plan): the toolstrip control showing R2025b's App
// Designer toolstrip as stage W0 recorded it (w0_toolstrip.json), over a document text box, with
// buttons that switch the context the way App Designer's two views do. Every command, invocation and
// check is a key<TAB>value line in the -log file (a windowed program has no console).
//   ToolstripSample.exe -log <file> [-layout <w0_toolstrip.json>] [-dark] [-width <px>] [-selftest]
// -selftest runs the checks that need WPF's real layout - fitting the CANVAS tab at nine widths, the
// commands sent to the window with a text box focused, key tips, contexts, a button group, the
// spinner's limits - writes "selftest<TAB>pass" or "fail" and exits 0 or 1.
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using JGraph.Controls.Toolstrip;
using JGraph.Scripting.Toolstrip;

namespace ToolstripSample;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? Arg(string name) => Array.IndexOf(args, name) is int i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        string log = Arg("-log") ?? Path.Combine(Path.GetTempPath(), "w1toolstrip.log");
        string layoutPath = Arg("-layout") ?? FindRecording();
        File.WriteAllText(log, "");
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) => SampleWindow.Append(log, "crash\t" + e.Exception.ToString().Replace(Environment.NewLine, " | "));
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/JGraph.Controls;component/Themes/Shared.xaml") });
        string theme = args.Contains("-dark") ? "Dark" : "Light";
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/JGraph.Controls;component/Themes/{theme}.xaml") });
        using JsonDocument recording = JsonDocument.Parse(File.ReadAllText(layoutPath));
        var window = new SampleWindow(recording.RootElement, log, args.Contains("-selftest"));
        if (double.TryParse(Arg("-width"), out double width))
        {
            window.Width = width;
        }

        return app.Run(window);
    }

    // The recording from the repository, found from the program's folder upwards.
    private static string FindRecording()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, "tools", "matlab-checklist", "ui-probes", "w0", "w0_toolstrip.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("w0_toolstrip.json not found; pass -layout.");
    }
}

internal sealed class SampleWindow : Window
{
    private const string Designer = "appDesignerToolstrip.designerTab";
    private const string Canvas = "appDesignerToolstrip.layoutTab";
    private const string Editor = "appDesignerToolstrip.editorTab";
    private readonly string _log;
    private readonly Toolstrip _strip = new();
    private readonly TextBox _document = new() { AcceptsReturn = true, Margin = new Thickness(8) };
    private readonly TextBlock _status = new() { Margin = new Thickness(4, 0, 4, 0) };
    private readonly Dictionary<string, RoutedUICommand> _commands = new(StringComparer.Ordinal);
    private readonly string[] _designGroups;
    private readonly string[] _codeGroups;
    private readonly List<string> _failures = [];
    private bool _debugging;

    public SampleWindow(JsonElement recording, string log, bool selfTest)
    {
        _log = log;
        Title = "Toolstrip sample";
        Width = 1280;
        Height = 420;
        SetResourceReference(BackgroundProperty, JGraph.Controls.Themes.ThemeKeys.Window);
        JsonElement contexts = recording.GetProperty("documentTypes")[0].GetProperty("properties").GetProperty("contexts");
        _designGroups = Groups(contexts, "AD_APP_DESIGN_VIEW_CONTEXT");
        _codeGroups = Groups(contexts, "AD_APP_CODE_VIEW_CONTEXT");

        // The layout as recorded, with the grid interval's limits the action gives (10 to 50) and its value.
        ToolstripLayout layout = ToolstripConfigReader.ReadRecording(recording).MapItems(item =>
            item.Tag == "appDesignerToolstrip.layoutTab.view.intervalSpinner" ? item with { Minimum = 10, Maximum = 50, Value = "10" } : item);
        _strip.CommandResolver = Command;
        _strip.CommandTarget = this;
        _strip.TextFormatter = s => s.Replace("${0}", "App", StringComparison.Ordinal);
        _strip.PopupPanelProvider = tag => tag.EndsWith("gotoPopUpList.panel", StringComparison.Ordinal)
            ? [new ToolstripPopupEntry { Kind = ToolstripPopupEntryKind.Item, Tag = "function.startupFcn", Text = "startupFcn" },
               new ToolstripPopupEntry { Kind = ToolstripPopupEntryKind.Item, Tag = "function.ButtonPushed", Text = "ButtonPushed" }]
            : null;
        _strip.Layout = layout;
        _strip.SetTabVisible("appDesignerToolstrip.simulinkTab", false);
        // As R2025b's execution controls leave the RUN section with no app running: Run alone in
        // its column, Step In and Step Out out of theirs.
        _strip.SetVisible("appDesignerToolstrip.editorTab.run.pause", false);
        _strip.SetVisible("appDesignerToolstrip.editorTab.run.continue", false);
        _strip.SetVisible("appDesignerToolstrip.editorTab.run.stepin", false);
        _strip.SetVisible("appDesignerToolstrip.editorTab.run.stepout", false);
        _strip.SetContext(_designGroups);
        _strip.Invoked += (_, e) => Log($"invoked\t{e.Tag}\t{e.ActionId}\t{e.Value}");
        _strip.SelectedTabChanged += (_, _) => Log($"tab\t{_strip.SelectedTab}");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_document, "Document");
        System.Windows.Automation.AutomationProperties.SetAutomationId(_status, "Status");

        var views = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 0) };
        views.Children.Add(HostButton("Design View", "DesignView", () => _strip.SetContext(_designGroups)));
        views.Children.Add(HostButton("Code View", "CodeView", () => _strip.SetContext(_codeGroups)));
        // While debugging, App Designer's RUN column shows Continue in Run's place, and Step In and Step Out.
        views.Children.Add(HostButton("Debugging", "Debugging", () =>
        {
            _debugging = !_debugging;
            _strip.SetVisible("appDesignerToolstrip.editorTab.run.run", !_debugging);
            _strip.SetVisible("appDesignerToolstrip.editorTab.run.continue", _debugging);
            _strip.SetVisible("appDesignerToolstrip.editorTab.run.stepin", _debugging);
            _strip.SetVisible("appDesignerToolstrip.editorTab.run.stepout", _debugging);
        }));

        var statusBar = new StatusBar();
        statusBar.Items.Add(new StatusBarItem { Content = _status });
        var root = new DockPanel();
        DockPanel.SetDock(_strip, Dock.Top);
        DockPanel.SetDock(views, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(_strip);
        root.Children.Add(views);
        root.Children.Add(statusBar);
        root.Children.Add(_document);
        Content = root;

        // Undo and redo are the document's own commands: the window's handlers prove where they went.
        foreach (RoutedCommand command in new[] { ApplicationCommands.Undo, ApplicationCommands.Redo, ApplicationCommands.Cut, ApplicationCommands.Copy, ApplicationCommands.Paste })
        {
            CommandBindings.Add(new CommandBinding(command, (_, e) => Log($"command\t{((RoutedCommand)e.Command).Name}\twindow"), (_, e) => e.CanExecute = true));
        }

        Loaded += (_, _) =>
        {
            _document.Focus();
            Log("ready\t1");
        };
        if (selfTest)
        {
            ContentRendered += async (_, _) => await SelfTest();
        }
    }

    private static string[] Groups(JsonElement contexts, string id) =>
        [.. contexts.EnumerateArray().First(c => c.GetProperty("contextId").GetString() == id).GetProperty("tabGroupTags").EnumerateArray().Select(e => e.GetString()!)];

    private ICommand Command(string action)
    {
        switch (action)
        {
            case "AD_UNDO": return ApplicationCommands.Undo;
            case "AD_REDO": return ApplicationCommands.Redo;
            case "AD_CUT": return ApplicationCommands.Cut;
            case "AD_COPY": return ApplicationCommands.Copy;
            case "AD_PASTE": return ApplicationCommands.Paste;
        }

        if (!_commands.TryGetValue(action, out RoutedUICommand? command))
        {
            _commands[action] = command = new RoutedUICommand(action, action, typeof(SampleWindow));
            CommandBindings.Add(new CommandBinding(command, (_, e) => Log($"command\t{action}\t{(e.Source == this ? "window" : e.Source?.GetType().Name)}")));
        }

        return command;
    }

    private Button HostButton(string text, string id, Action act)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 2, 8, 2) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => act();
        return button;
    }

    private void Log(string line)
    {
        _status.Text = line.Replace('\t', ' ');
        Append(_log, line);
    }

    // The check script reads the log while the sample writes it: both open it shared.
    public static void Append(string path, string line)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(line);
    }

    private string Read()
    {
        using var stream = new FileStream(_log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void Check(string name, bool ok, string detail = "")
    {
        Log($"check\t{name}\t{(ok ? "ok" : "FAIL")}\t{detail}");
        if (!ok)
        {
            _failures.Add(name);
        }
    }

    private static async Task Idle() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

    private async Task SelfTest()
    {
        try
        {
            await Contexts();
            await Fitting();
            await Routing();
            await KeyTips();
            await Group();
            await Spinner();
        }
        catch (Exception ex)
        {
            Check("exception", false, ex.GetType().Name + ": " + ex.Message.Replace('\n', ' '));
        }

        Log($"selftest\t{(_failures.Count == 0 ? "pass" : "fail")}\t{string.Join(",", _failures)}");
        System.Windows.Application.Current.Shutdown(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Contexts()
    {
        _strip.SetContext(_designGroups);
        await Idle();
        Check("design view tabs", _strip.VisibleTabs.SequenceEqual([Designer, Canvas, "appDesignerToolstrip.designviewTab"]), string.Join(",", _strip.VisibleTabs));
        _strip.SelectedTab = Canvas;
        _strip.SetContext(_codeGroups);
        await Idle();
        Check("code view tabs", _strip.VisibleTabs.SequenceEqual([Designer, Editor, "appDesignerToolstrip.viewTab"]), string.Join(",", _strip.VisibleTabs));
        Check("canvas gives way to editor", _strip.SelectedTab == Editor, _strip.SelectedTab ?? "");
        _strip.SelectedTab = Designer;
        _strip.SetContext(_designGroups);
        await Idle();
        Check("designer kept across views", _strip.SelectedTab == Designer, _strip.SelectedTab ?? "");
    }

    // The CANVAS tab fitted at widths narrowing and widening again: labels go before sections, and
    // sections collapse from the last; widening undoes it all.
    private async Task Fitting()
    {
        _strip.SelectedTab = Canvas;
        double[] widths = [1500, 1100, 900, 760, 640, 520, 400, 300, 1500];
        int previous = -1;
        double previousWidth = double.PositiveInfinity;
        foreach (double w in widths)
        {
            Width = w;
            await Idle();
            _strip.Refit();
            await Idle();
            IReadOnlyList<(string Section, bool Collapsed, bool LabelsShown)> states = _strip.SectionStates(Canvas);
            (double available, double needed) = _strip.FitWidths();
            string shown = string.Join(",", states.Select(s => $"{s.Section}:{(s.Collapsed ? "c" : "e")}{(s.LabelsShown ? "+" : "-")}"));
            Log($"fit\t{w}\t{available:0}\t{needed:0}\t{shown}");
            int collapsed = states.Count(s => s.Collapsed);
            bool suffix = states.SkipWhile(s => !s.Collapsed).All(s => s.Collapsed);
            Check($"fit {w} collapses from the last", suffix, shown);
            Check($"fit {w} fits", needed <= available + 0.5 || collapsed == states.Count, $"{needed:0} in {available:0}");
            if (w < previousWidth)
            {
                Check($"fit {w} narrower collapses no fewer", collapsed >= previous, $"{collapsed} after {previous}");
            }

            previous = collapsed;
            previousWidth = w;
        }

        IReadOnlyList<(string Section, bool Collapsed, bool LabelsShown)> wide = _strip.SectionStates(Canvas);
        Check("widened again: all expanded with labels", wide.All(s => !s.Collapsed && s.LabelsShown), string.Join(",", wide.Select(s => s.Section)));
        Width = 1280;
        await Idle();
    }

    // Undo from the quick access bar with the document focused goes to the window, as CommandTarget
    // says; without it the focused text box's own Undo would answer.
    private async Task Routing()
    {
        _strip.SelectedTab = Designer;
        _document.Clear();
        _document.Focus();
        await Idle();
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, _document, "abc"));
        await Idle();
        Check("typed into the document", _document.Text == "abc", _document.Text);
        var undo = (ButtonBase)_strip.Elements("appDesignerToolstrip.qab.undoButton")[0];
        Invoke(undo);
        await Idle();
        Check("undo went to the window", _document.Text == "abc" && Read().Contains("command\tUndo\twindow"), _document.Text);
        _strip.CommandTarget = null;
        _document.Focus();
        Invoke(undo);
        await Idle();
        Check("without a target the text box answers", _document.Text == "", _document.Text);
        _strip.CommandTarget = this;

        var save = (ButtonBase)_strip.Elements("appDesignerToolstrip.designerTab.file.saveSplitButton")[0];
        Invoke(save);
        await Idle();
        Check("save runs its action's command", Read().Contains("command\tAD_SAVE\twindow"));
        Check("save raises Invoked", Read().Contains("invoked\tappDesignerToolstrip.designerTab.file.saveSplitButton\tAD_SAVE"));
    }

    private async Task KeyTips()
    {
        _strip.SelectedTab = Designer;
        await Idle();
        _strip.ShowKeyTips();
        IReadOnlyList<(string Tip, string Target)> tabs = _strip.ShownKeyTips;
        Log("tips\ttabs\t" + string.Join(",", tabs.Select(t => $"{t.Tip}={t.Target}")));
        Check("tab tips", tabs.Any(t => t.Target == Designer) && tabs.Any(t => t.Target == Canvas), string.Join(",", tabs.Select(t => t.Target)));
        Check("quick access tips 1 to 8", Enumerable.Range(1, 8).All(n => tabs.Any(t => t.Tip == n.ToString())));
        string designerTip = tabs.First(t => t.Target == Designer).Tip;
        foreach (char c in designerTip)
        {
            _strip.TypeKeyTip(c);
        }

        await Idle();
        await Idle();
        IReadOnlyList<(string Tip, string Target)> controls = _strip.ShownKeyTips;
        Log("tips\tdesigner\t" + string.Join(",", controls.Select(t => $"{t.Tip}={t.Target}")));
        Check("controls' tips shown", _strip.KeyTipMode == ToolstripKeyTipMode.Controls && controls.Count >= 6, controls.Count.ToString());
        Check("run keeps R2025b's R", controls.Any(t => t.Tip == "R" && t.Target == "appDesignerToolstrip.editorTab.run.run"));
        Check("step keeps R2025b's DD", controls.Any(t => t.Tip == "DD" && t.Target == "appDesignerToolstrip.editorTab.run.step"));
        Check("tips prefix-free", controls.All(a => controls.All(b => a == b || !b.Tip.StartsWith(a.Tip, StringComparison.Ordinal))));
        _strip.TypeKeyTip('R');
        await Idle();
        Check("R runs Run", Read().Contains("invoked\tappDesignerToolstrip.editorTab.run.run\tAD_RUN"));
        Check("tips hidden after", _strip.KeyTipMode == ToolstripKeyTipMode.Off);
    }

    // SPLIT DOCUMENT's three toggles are a group: one is always pressed.
    private async Task Group()
    {
        _strip.SetContext(_codeGroups);
        _strip.SelectedTab = "appDesignerToolstrip.viewTab";
        await Idle();
        var none = (ToggleButton)_strip.Elements("appDesignerToolstrip.codeViewTab.split.none")[0];
        var left = (ToggleButton)_strip.Elements("appDesignerToolstrip.codeViewTab.split.leftRight")[0];
        Toggle(left);
        await Idle();
        Toggle(none);
        await Idle();
        Check("group: pressing one releases the other", none.IsChecked == true && left.IsChecked == false);
        Toggle(none);
        await Idle();
        Check("group: the pressed one stays pressed", none.IsChecked == true);
        _strip.SetContext(_designGroups);
    }

    private async Task Spinner()
    {
        _strip.SelectedTab = "appDesignerToolstrip.designviewTab";
        await Idle();
        var box = (TextBox)_strip.Elements("appDesignerToolstrip.layoutTab.view.intervalSpinner")[0];
        box.Focus();
        box.Text = "5";
        _document.Focus();
        await Idle();
        Check("spinner refuses 5", box.ToolTip as string == "Value must be between 10 and 50", box.ToolTip as string ?? "");
        box.Focus();
        box.Text = "20";
        _document.Focus();
        await Idle();
        Check("spinner takes 20", Read().Contains("invoked\tappDesignerToolstrip.layoutTab.view.intervalSpinner\tAD_DESIGN_GRID_INTERVAL\t20"));
    }

    private static void Invoke(ButtonBase button) =>
        ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static void Toggle(ToggleButton button) =>
        ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Toggle)).Toggle();
}
