using System.Text.RegularExpressions;
using JGraph.Api;
using Python.Runtime;

namespace JGraph.Scripting;

/// <summary>
/// Runs Python scripts with a real in-process CPython interpreter via pythonnet. Scripts use the JGraph
/// API through the .NET bridge — <c>from JGraph.Api import JG</c> is set up for them, along with the same
/// host helpers as the C# engine (<c>readcsv</c>, <c>show</c>, and a <c>print</c> that writes to the
/// console). When no CPython runtime is found the engine reports that gracefully instead of failing hard.
/// </summary>
public sealed class PythonScriptEngine : IScriptEngine
{
    /// <summary>The user-facing message shown when no CPython runtime could be found.</summary>
    public const string UnavailableMessage =
        "Python runtime not found. Install CPython 3.x from python.org, or set the PYTHONNET_PYDLL " +
        "environment variable to the full path of a pythonXY.dll.";

    private static readonly object InitLock = new();
    private static bool _initialized;

    private readonly PythonRuntimeInfo? _runtime;

    /// <summary>Creates the engine, probing for a CPython runtime to load on demand.</summary>
    public PythonScriptEngine() => _runtime = PythonLocator.Find();

    /// <inheritdoc />
    public string Language => "Python";

    /// <inheritdoc />
    public bool IsAvailable => _runtime is not null;

    /// <summary>The CPython runtime the engine will load, or null when none was found.</summary>
    public PythonRuntimeInfo? RuntimeInfo => _runtime;

    /// <inheritdoc />
    public Task<ScriptRunResult> RunAsync(string code, ScriptContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);

        if (!IsAvailable)
        {
            return Task.FromResult(ScriptRunResult.Failed(UnavailableMessage));
        }

        return Task.Run(() => RunCore(code, context), cancellationToken);
    }

    private ScriptRunResult RunCore(string code, ScriptContext context)
    {
        // Python scripts drive the same static JG facade; start each run from a clean state.
        JG.Reset();
        var globals = new JGraphScriptGlobals(context);

        try
        {
            EnsureInitialized();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Output.WriteError(ex.Message);
            return ScriptRunResult.Failed($"Failed to initialize the Python runtime: {ex.Message}");
        }

        using (Py.GIL())
        {
            using PyModule scope = Py.CreateScope();
            try
            {
                scope.Set("_ctx", globals);
                scope.Set("_jg_search_paths", _runtime!.SearchPaths.ToArray());
                scope.Exec(Preamble);
                scope.Exec(code);
                return ScriptRunResult.Ok(globals.FiguresShown, SnapshotVariables(scope));
            }
            catch (PythonException ex) when (ScriptExitException.Unwrap(ex) is { } exit)
            {
                // pythonnet wraps the .NET exception exit() threw; the request is inside.
                return ScriptRunResult.Exited(exit.ExitCode, globals.FiguresShown);
            }
            catch (ScriptExitException ex)
            {
                return ScriptRunResult.Exited(ex.ExitCode, globals.FiguresShown);
            }
            catch (PythonException ex)
            {
                context.Output.WriteError(ex.Message);
                string? traceback = ex.StackTrace;
                if (!string.IsNullOrEmpty(traceback))
                {
                    context.Output.WriteError(traceback);
                }

                return ScriptRunResult.Failed(FirstLine(ex.Message), MapTraceback(ex.Message, traceback));
            }
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            Runtime.PythonDLL = _runtime!.Dll;
            if (_runtime.Home is { Length: > 0 } home)
            {
                // Point the embedded interpreter at the probed environment's prefix (sys.prefix, so a
                // venv's own site-packages resolves) — without this, packages installed for the probed
                // Python (numpy, …) are invisible to the embedded runtime.
                PythonEngine.PythonHome = home;
            }

            PythonEngine.Initialize();
            // Release the GIL the initialising thread holds so per-run threads can acquire it via Py.GIL().
            PythonEngine.BeginAllowThreads();
            _initialized = true;
        }
    }

    /// <summary>
    /// Projects the scope's user-defined variables for the workspace variables panel. Dunders, modules,
    /// and callables (functions, classes, CLR types) are skipped. Runs under the GIL. A snapshot must
    /// never fail an otherwise-successful run, so any conversion surprise abandons it.
    /// </summary>
    private static IReadOnlyList<ScriptVariable> SnapshotVariables(PyModule scope)
    {
        try
        {
            var variables = new List<ScriptVariable>();
            using PyDict dict = scope.Variables();
            foreach (PyObject key in dict.Keys())
            {
                using (key)
                {
                    string name = key.As<string>() ?? string.Empty;
                    if (name.Length == 0 || name.StartsWith('_'))
                    {
                        continue;
                    }

                    using PyObject value = dict[key];
                    if (value.IsCallable())
                    {
                        continue;
                    }

                    string pythonType = value.GetPythonType().Name;
                    if (pythonType == "module")
                    {
                        continue;
                    }

                    variables.Add(Project(name, value, pythonType));
                }
            }

            variables.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return variables;
        }
        catch (Exception)
        {
            return Array.Empty<ScriptVariable>();
        }
    }

    private static ScriptVariable Project(string name, PyObject value, string pythonType)
    {
        (string type, object? raw) = pythonType switch
        {
            "bool" => ("bool", (object?)value.As<bool>()),
            "int" or "float" => ("number", value.As<double>()),
            "str" => ("string", value.As<string>()),
            "list" or "tuple" or "ndarray" => ("array", TryNumericArray(value)),
            "Table" => ("table", TryManaged<Data.Table>(value)),
            _ => (pythonType, null),
        };

        string display;
        try
        {
            display = value.ToString() ?? string.Empty;
        }
        catch (PythonException)
        {
            display = $"<{pythonType}>";
        }

        return new ScriptVariable(name, type, ScriptVariable.Truncate(display), raw);
    }

    private static double[]? TryNumericArray(PyObject value)
    {
        try
        {
            return value.As<double[]>();
        }
        catch (Exception ex) when (ex is PythonException or InvalidCastException or FormatException)
        {
            return null;
        }
    }

    private static T? TryManaged<T>(PyObject value)
        where T : class
    {
        try
        {
            return value.As<T>();
        }
        catch (Exception ex) when (ex is PythonException or InvalidCastException)
        {
            return null;
        }
    }

    private static IReadOnlyList<ScriptDiagnostic> MapTraceback(string message, string? traceback)
    {
        int line = 0;
        if (!string.IsNullOrEmpty(traceback))
        {
            foreach (Match match in Regex.Matches(traceback, @"line (\d+)"))
            {
                if (int.TryParse(match.Groups[1].Value, out int parsed))
                {
                    line = parsed;
                }
            }
        }

        return new[] { new ScriptDiagnostic(line, 0, FirstLine(message), IsError: true) };
    }

    private static string FirstLine(string text)
    {
        int newline = text.IndexOf('\n');
        return newline < 0 ? text : text[..newline].TrimEnd('\r');
    }

    /// <summary>
    /// Python setup run once per scope before the user's code: redirect stdout/stderr to the output
    /// console, load the JGraph assemblies, and define the host helper functions. Executed separately
    /// from the user's code so traceback line numbers refer to the script the user actually wrote.
    /// </summary>
    private const string Preamble = """
        import sys
        import clr

        # Belt-and-braces against pythonnet home/path quirks: make sure every search path of the
        # probed interpreter (site-packages included) is importable in the embedded runtime.
        for _p in reversed([str(p) for p in _jg_search_paths]):
            if _p and _p not in sys.path:
                sys.path.insert(0, _p)

        clr.AddReference("JGraph.Api")
        clr.AddReference("JGraph.Core")
        clr.AddReference("JGraph.Objects")
        clr.AddReference("JGraph.Data")
        clr.AddReference("JGraph.Signal")

        from JGraph.Api import JG
        from JGraph.Data import Table


        class _JgWriter:
            def __init__(self, sink):
                self._sink = sink

            def write(self, text):
                if text:
                    self._sink(text)
                return len(text) if text else 0

            def flush(self):
                pass


        sys.stdout = _JgWriter(_ctx.WriteOut)
        sys.stderr = _JgWriter(_ctx.WriteErr)


        def readcsv(path):
            return _ctx.readcsv(path)


        def readxlsx(path):
            return _ctx.readxlsx(path)


        def readtable(path):
            return _ctx.readtable(path)


        def show(figure=None):
            if figure is None:
                _ctx.show()
            else:
                _ctx.show(figure)
        """;
}
