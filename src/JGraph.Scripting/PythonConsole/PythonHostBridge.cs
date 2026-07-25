using System.Globalization;
using System.Linq;
using System.Text.Json;
using JGraph.Api;

namespace JGraph.Scripting.PythonConsole;

/// <summary>
/// Executes the plotting calls the Python child asks for. The child cannot touch <see cref="JG"/>
/// directly — it is a separate process — so its <c>jgraph</c> module turns every plotting verb into a
/// <c>call</c> message that lands here, runs against the host's live figure state, and is answered.
/// </summary>
/// <remarks>
/// Only plotting verbs are proxied. Anything that would have to hand a live object back across the
/// process boundary (a <c>Table</c> from <c>readcsv</c>, a figure handle) is deliberately absent for
/// this milestone: Python has its own file readers, and inventing a cross-process handle table is not
/// worth it until something needs one. An unknown verb is reported to the child as an error, so the
/// user sees a Python exception naming the function rather than a silent no-op.
/// </remarks>
internal sealed class PythonHostBridge
{
    private readonly JGraphScriptGlobals _globals;

    /// <summary>Creates a bridge over the session's host globals.</summary>
    public PythonHostBridge(JGraphScriptGlobals globals) => _globals = globals;

    /// <summary>The verbs the child's <c>jgraph</c> module may call, for its own module definition.</summary>
    public static IReadOnlyList<string> FunctionNames { get; } =
    [
        "figure", "subplot", "plot", "scatter", "bar", "stem", "histogram",
        "title", "xlabel", "ylabel", "legend", "grid", "xlim", "ylim", "hold",
        "colorbar", "show",
    ];

    /// <summary>
    /// Runs one <c>call</c> message. Returns the reply to send back — a value for <c>figure()</c>, and
    /// otherwise an acknowledgement, or an error the child re-raises as a Python exception.
    /// </summary>
    public PythonConsoleMessage Invoke(PythonConsoleMessage call)
    {
        string function = call.Fn ?? string.Empty;
        JsonElement[] args = call.Args is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray().ToArray()
            : [];

        try
        {
            return Dispatch(function, args, call.Seq);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException
                                       or IndexOutOfRangeException or NotSupportedException)
        {
            return PythonConsoleCodec.ReturnError(call.Seq, $"{function}: {ex.Message}");
        }
    }

    private PythonConsoleMessage Dispatch(string function, JsonElement[] args, int seq)
    {
        switch (function)
        {
            case "figure":
                int number = args.Length > 0 ? (int)Number(args[0]) : Next();
                JG.Figure(number);
                return Value(seq, number);

            case "subplot":
                Require(function, args, 3);
                JG.Subplot((int)Number(args[0]), (int)Number(args[1]), (int)Number(args[2]));
                return Ack(seq);

            case "plot":
                PlotXy(args);
                return Ack(seq);

            case "scatter":
                Require(function, args, 2);
                JG.Scatter(Numbers(args[0]), Numbers(args[1]));
                return Ack(seq);

            case "bar":
                Require(function, args, 2);
                JG.Bar(Numbers(args[0]), Numbers(args[1]));
                return Ack(seq);

            case "stem":
                Require(function, args, 2);
                JG.Stem(Numbers(args[0]), Numbers(args[1]));
                return Ack(seq);

            case "histogram":
                Require(function, args, 1);
                JG.Histogram(Numbers(args[0]), args.Length > 1 ? (int)Number(args[1]) : 10);
                return Ack(seq);

            case "title":
                JG.Title(Text(function, args));
                return Ack(seq);

            case "xlabel":
                JG.XLabel(Text(function, args));
                return Ack(seq);

            case "ylabel":
                JG.YLabel(Text(function, args));
                return Ack(seq);

            case "legend":
                JG.Legend(args.Select(a => a.GetString() ?? string.Empty).ToArray());
                return Ack(seq);

            case "grid":
                JG.Grid(args.Length == 0 || Flag(args[0]));
                return Ack(seq);

            case "hold":
                JG.Hold(args.Length == 0 || Flag(args[0]));
                return Ack(seq);

            case "colorbar":
                JG.Colorbar(args.Length == 0 || Flag(args[0]));
                return Ack(seq);

            case "xlim":
                Require(function, args, 2);
                JG.XLim(Number(args[0]), Number(args[1]));
                return Ack(seq);

            case "ylim":
                Require(function, args, 2);
                JG.YLim(Number(args[0]), Number(args[1]));
                return Ack(seq);

            case "show":
                if (args.Length > 0)
                {
                    _globals.show((int)Number(args[0]));
                }
                else
                {
                    _globals.show();
                }

                return Ack(seq);

            default:
                return PythonConsoleCodec.ReturnError(seq, $"'{function}' is not a JGraph console function.");
        }
    }

    /// <summary>
    /// <c>plot(y)</c>, <c>plot(x, y)</c> and <c>plot(x, y, spec)</c> — the same overload set the other
    /// languages offer, so the same call reads the same way whichever prompt it is typed at.
    /// </summary>
    private static void PlotXy(JsonElement[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("plot expects at least one sequence.");
        }

        if (args.Length == 1 || args[1].ValueKind == JsonValueKind.String)
        {
            JG.Plot(Numbers(args[0]), args.Length > 1 ? args[1].GetString() : null);
            return;
        }

        JG.Plot(Numbers(args[0]), Numbers(args[1]), args.Length > 2 ? args[2].GetString() : null);
    }

    /// <summary>The lowest figure number not already registered — what a bare <c>figure()</c> means.</summary>
    private static int Next()
    {
        var used = JG.FigureNumbers.ToHashSet();
        int candidate = 1;
        while (used.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
    }

    private static PythonConsoleMessage Ack(int seq) => PythonConsoleCodec.Return(seq);

    private static PythonConsoleMessage Value(int seq, int value) =>
        PythonConsoleCodec.Return(seq, JsonSerializer.SerializeToElement(value));

    private static void Require(string function, JsonElement[] args, int count)
    {
        if (args.Length < count)
        {
            throw new ArgumentException($"{function} expects at least {count} argument(s).");
        }
    }

    private static string Text(string function, JsonElement[] args)
    {
        Require(function, args, 1);
        return args[0].ValueKind == JsonValueKind.String
            ? args[0].GetString() ?? string.Empty
            : args[0].ToString();
    }

    private static bool Flag(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.GetDouble() != 0,
        _ => true,
    };

    private static double Number(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.String when double.TryParse(
            element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => throw new ArgumentException($"expected a number, got {element.ValueKind}."),
    };

    /// <summary>
    /// A numeric sequence. The child has already flattened lists, tuples and numpy arrays to a JSON
    /// array of numbers, so anything else here is a genuine type error worth reporting.
    /// </summary>
    private static double[] Numbers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return [element.GetDouble()];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"expected a sequence of numbers, got {element.ValueKind}.");
        }

        var values = new double[element.GetArrayLength()];
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            values[index++] = Number(item);
        }

        return values;
    }
}
