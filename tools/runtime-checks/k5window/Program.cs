// Stage K5's window check client (compiler and SDK plan): two deployed components in one process,
// one showing a uifigure app with a button, the other a plotted figure. Prints key<TAB>value lines;
// run-k5window.ps1 waits for "ready", presses the app's button and closes both windows through UI
// Automation, and checks the lines that follow. The button's callback runs while this client makes
// no call (it only asks for the count), as R2025b's runtime runs a component's events between calls.
using System.Diagnostics;
using System.IO;
using JGraph.Deploy;
using JGraph.Runtime;

const string Source = """
    function out = k5app(op, arg)
    global K5N
    switch op
        case 'quiet'
            out = beep;
            beep('off');
        case 'app'
            K5N = 0;
            f = uifigure('Name', arg, 'Position', [200 300 320 160]);
            uibutton(f, 'Text', 'Press', 'Position', [100 60 120 40], 'ButtonPushedFcn', @(~, ~) pressed());
            out = 'shown';
        case 'plot'
            figure('Name', arg, 'Position', [560 300 480 360]);
            plot(1:5, [2 4 3 5 1]);
            out = 'shown';
        case 'count'
            out = mat2str(K5N);
        case 'figures'
            out = mat2str(numel(findall(groot, 'Type', 'figure')));
    end
    end

    function pressed()
    global K5N
    K5N = K5N + 1;
    fprintf('k5app pressed %d\n', K5N);
    end
    """;

static void Line(string key, string value)
{
    Console.Out.Flush();
    Console.WriteLine($"{key}\t{value}");
    Console.Out.Flush();
}

static string Archive(string root, string component)
{
    string source = Directory.CreateDirectory(Path.Combine(root, "src_" + component)).FullName;
    string file = Path.Combine(source, "k5app.m");
    File.WriteAllText(file, Source.ReplaceLineEndings("\n"));
    string archive = Path.Combine(root, component + ".ctf");
    CtfArchiveWriter.Write(archive, new CtfBuild
    {
        ComponentName = component,
        TargetType = CtfTargetType.DotNetAssembly,
        Files = [(file, component + "/k5app.m")],
        MatlabPath = [component],
        PublicFunctions = [new CtfFunction("k5app", component + "/k5app.m", [], [])],
        RuntimeOptions = [],
    });
    return archive;
}

static string Op(Component component, string op, string? arg = null) =>
    ((RuntimeChar)component.Call("k5app", 1, RuntimeValue.Text(op), arg is null ? RuntimeValue.Empty() : RuntimeValue.Text(arg))[0]).Characters;

static int Check(string root)
{
    using Component a = Component.Open(Archive(root, "K5WA"));
    using Component b = Component.Open(Archive(root, "K5WB"));
    Line("quiet", Op(a, "quiet") + " " + Op(b, "quiet"));
    Line("app", Op(a, "app", "k5 app of a"));
    Line("plot", Op(b, "plot", "k5 plot of b"));

    // On a WPF client's own thread the windows open after the calls, and their components after
    // them. UI Automation answers ahead of that queue, and a window it reads before its components
    // arrive keeps showing it empty, so the check waits for the thread to be idle first.
    System.Windows.Application.Current?.Dispatcher.Invoke(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    Line("ready", "");

    // The harness presses the button; this client only asks how many presses its callback has counted.
    var clock = Stopwatch.StartNew();
    string count = "0";
    while (count == "0" && clock.Elapsed < TimeSpan.FromSeconds(60))
    {
        Thread.Sleep(200);
        count = Op(a, "count");
    }

    Line("pressed", count);

    // Then it closes the app's window, and the plot's.
    a.WaitForFiguresToClose();
    Line("a.closed", Op(a, "figures"));
    b.WaitForFiguresToClose();
    Line("b.closed", Op(b, "figures"));
    JgRuntime.Terminate();
    Line("end", "");
    return count == "1" ? 0 : 1;
}

string root = Directory.CreateDirectory(args.Length > 0 && !args[0].StartsWith('-') ? args[0] : Path.Combine(Path.GetTempPath(), "jgraph-k5window")).FullName;
Environment.SetEnvironmentVariable("MCR_CACHE_ROOT", Path.Combine(root, "cache"));
if (!args.Contains("-wpf-host"))
{
    Line("host", "console");
    return Check(root);
}

// A WPF client: its own application on its own thread, which the runtime's windows join.
Line("host", "wpf");
int code = 1;
var ui = new Thread(() =>
{
    var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
    app.Startup += (_, _) => Task.Run(() =>
    {
        try
        {
            code = Check(root);
        }
        finally
        {
            app.Dispatcher.InvokeShutdown();
        }
    });
    app.Run();
});
ui.SetApartmentState(ApartmentState.STA);
ui.Start();
ui.Join();
return code;
