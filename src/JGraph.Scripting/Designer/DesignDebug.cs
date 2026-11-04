using System.IO;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Jgs.Debug;

namespace JGraph.Scripting.Designer;

/// <summary>
/// A breakpoint of an app held by its place in the class rather than its line (App Designer window plan
/// W11): the function it is in, as the debugger names it, and how many lines below the function's
/// signature it sits. When App Designer writes the class again around the user's code - a component
/// added, a callback renamed - the lines move and the anchors give the breakpoints back.
/// </summary>
/// <param name="Function">The function's debugger name (<c>App.ButtonPushed</c>), or "" for a line outside every function.</param>
/// <param name="Offset">Lines below the function's signature (or the line itself, outside every function).</param>
/// <param name="Condition">The breakpoint's condition, "" for none.</param>
/// <param name="Enabled">Whether the breakpoint pauses.</param>
public sealed record DesignBreakpointAnchor(string Function, int Offset, string Condition, bool Enabled);

/// <summary>
/// The debugger's view of an app's class text (App Designer window plan W11, UI-free): which lines a
/// breakpoint can sit on, where one asked for elsewhere lands, the anchors that carry breakpoints across a
/// rewrite of the class, and the map between the code view's lines and the file's. The debugger counts
/// the file's lines - the class text the interpreter runs (W0: breakpoints in an <c>.mlapp</c> count the
/// generated class text's lines) - and the code view shows the class as the designer writes it again
/// around the user's code, the same text line for line once this designer saved the app.
/// </summary>
public static class DesignDebug
{
    /// <summary>The 1-based lines of <paramref name="text"/> a breakpoint can sit on; empty when the text does not parse.</summary>
    public static IReadOnlyList<int> ExecutableLines(string text) => Map(text)?.ExecutableLines ?? [];

    /// <summary>Where a breakpoint asked for on <paramref name="line"/> lands: the line, or the next executable one; null when the text has none at or after it, or does not parse.</summary>
    public static int? Snap(string text, int line) => Map(text)?.Snap(Math.Max(line, 1));

    /// <summary>The anchors of <paramref name="breakpoints"/> in <paramref name="text"/>, in order.</summary>
    public static IReadOnlyList<DesignBreakpointAnchor> Anchor(string text, IReadOnlyList<JgsBreakpoint> breakpoints)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(breakpoints);
        JgsSourceMap? map = Map(text);
        var anchors = new List<DesignBreakpointAnchor>(breakpoints.Count);
        foreach (JgsBreakpoint breakpoint in breakpoints)
        {
            JgsFunctionSpan? function = map?.FunctionAt(breakpoint.Line);
            anchors.Add(function is null
                ? new DesignBreakpointAnchor(string.Empty, breakpoint.Line, breakpoint.Condition, breakpoint.Enabled)
                : new DesignBreakpointAnchor(function.Name, breakpoint.Line - function.Signature, breakpoint.Condition, breakpoint.Enabled));
        }

        return anchors;
    }

    /// <summary>
    /// The breakpoints <paramref name="anchors"/> stand for in the rewritten <paramref name="text"/> of <paramref name="file"/>: each at
    /// its function's signature plus its offset, moved to the next executable line of that function when the line it names has
    /// none; dropped when the function is gone or the offset runs past its end. Two anchors landing on one line keep the first.
    /// </summary>
    public static IReadOnlyList<JgsBreakpoint> Resolve(string text, string file, IReadOnlyList<DesignBreakpointAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(anchors);
        JgsSourceMap? map = Map(text);
        if (map is null)
        {
            return [];
        }

        var resolved = new List<JgsBreakpoint>();
        var taken = new HashSet<int>();
        foreach (DesignBreakpointAnchor anchor in anchors)
        {
            int? line;
            if (anchor.Function.Length == 0)
            {
                line = map.Snap(Math.Max(anchor.Offset, 1));
            }
            else
            {
                JgsFunctionSpan? function = map.Functions.FirstOrDefault(f => string.Equals(f.Name, anchor.Function, StringComparison.Ordinal));
                line = function is null ? null : map.Snap(function.Signature + anchor.Offset) is int landed && landed <= function.End ? landed : null;
            }

            if (line is int at && taken.Add(at))
            {
                resolved.Add(new JgsBreakpoint(file, at, anchor.Condition, anchor.Enabled));
            }
        }

        return resolved;
    }

    /// <summary>The debugger's name of the function holding <paramref name="line"/> (<c>App.ButtonPushed</c>), or null outside every function or when the text does not parse.</summary>
    public static string? FunctionAt(string text, int line) => Map(text)?.FunctionAt(line)?.Name;

    /// <summary>The class text the interpreter runs from the app file at <paramref name="path"/>: an <c>.mlapp</c>'s document part, a plain-text app's whole file; null when it cannot be read.</summary>
    public static string? FileText(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return File.Exists(path) ? JgsMlapp.ReadSource(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// The lines of the code view's <paramref name="viewText"/> against the lines of the file's <paramref name="fileText"/>, which the
    /// debugger counts: the same text line for line after a save; before one, the class as the designer writes it again around the
    /// user's code against the class as R2025b or a hand wrote it, matched by the line comparison of W14 (a line the same or changed
    /// in place maps, an inserted or deleted one does not).
    /// </summary>
    public static DesignLineMap MapLines(string fileText, string viewText)
    {
        ArgumentNullException.ThrowIfNull(fileText);
        ArgumentNullException.ThrowIfNull(viewText);
        IReadOnlyList<string> file = Editing.TextDiff.SplitLines(fileText);
        IReadOnlyList<string> view = Editing.TextDiff.SplitLines(viewText);
        if (file.Count == view.Count && file.SequenceEqual(view, StringComparer.Ordinal))
        {
            return DesignLineMap.Identity;
        }

        var toView = new Dictionary<int, int>();
        var toFile = new Dictionary<int, int>();
        foreach (Editing.DiffRow row in Editing.TextDiff.Compare(file, view))
        {
            if (row.Kind is Editing.DiffRowKind.Same or Editing.DiffRowKind.Changed && row.Left > 0 && row.Right > 0)
            {
                toView[row.Left] = row.Right;
                toFile[row.Right] = row.Left;
            }
        }

        return new DesignLineMap(toView, toFile);
    }

    private static JgsSourceMap? Map(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            return JgsSourceMap.Parse(text, string.Empty, JgsDialect.Matlab);
        }
        catch (JgsException)
        {
            return null;
        }
    }
}

/// <summary>The lines of an app's code view against the lines of its file (<see cref="DesignDebug.MapLines"/>): the same after a save, a mapping before one.</summary>
public sealed class DesignLineMap
{
    private readonly IReadOnlyDictionary<int, int>? _toView;
    private readonly IReadOnlyDictionary<int, int>? _toFile;

    internal DesignLineMap(IReadOnlyDictionary<int, int> toView, IReadOnlyDictionary<int, int> toFile)
    {
        _toView = toView;
        _toFile = toFile;
    }

    private DesignLineMap()
    {
    }

    /// <summary>The map of a file whose text the code view shows line for line.</summary>
    public static DesignLineMap Identity { get; } = new();

    /// <summary>Whether every line is its own.</summary>
    public bool IsIdentity => _toView is null;

    /// <summary>The code view's line for the file's <paramref name="fileLine"/>, or null when the view has no such line.</summary>
    public int? ToView(int fileLine) => _toView is null ? fileLine : _toView.TryGetValue(fileLine, out int line) ? line : null;

    /// <summary>The file's line for the code view's <paramref name="viewLine"/>, or null when the file has no such line.</summary>
    public int? ToFile(int viewLine) => _toFile is null ? viewLine : _toFile.TryGetValue(viewLine, out int line) ? line : null;
}
