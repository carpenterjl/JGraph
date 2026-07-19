using System.Globalization;
using System.IO;
using System.Threading;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Data.Import;

namespace JGraph.Scripting;

/// <summary>
/// The object exposed to a running script. It holds the small set of host-backed helpers a script needs
/// that the static <see cref="JG"/> facade does not provide: reading tables, printing to the output
/// console, and displaying a figure. The C# engine surfaces these as top-level names (the globals type);
/// the Python engine injects the same helpers into the module scope. Everything else a script does — all
/// the plotting — goes through the ordinary <see cref="JG"/> API.
/// </summary>
public sealed class JGraphScriptGlobals
{
    private readonly ScriptContext _context;
    private readonly HashSet<int> _shownNumbers = new();
    private int _figuresShown;

    /// <summary>Creates the globals over a run's <paramref name="context"/>.</summary>
    public JGraphScriptGlobals(ScriptContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>How many figures the script has displayed via <c>show()</c> so far.</summary>
    public int FiguresShown => Volatile.Read(ref _figuresShown);

    // --- Output -----------------------------------------------------------------------------------

    /// <summary>Writes a value followed by a newline to the output console (C# scripts).</summary>
    public void print(object? value = null) =>
        _context.Output.WriteLine(Format(value));

    /// <summary>Writes raw text (no newline) to the output console. Backs Python's redirected stdout.</summary>
    public void WriteOut(string text) => _context.Output.Write(text);

    /// <summary>Writes raw text (no newline) to the error console. Backs Python's redirected stderr.</summary>
    public void WriteErr(string text) => _context.Output.WriteError(text);

    // --- Table readers ----------------------------------------------------------------------------

    /// <summary>Reads a delimited-text (CSV/TSV) table, resolving a relative path against the working directory.</summary>
    public Table readcsv(string path) => Table.ReadCsv(Resolve(path));

    /// <summary>Reads a delimited-text table after discarding <paramref name="skipRows"/> leading lines
    /// (junk preamble — tester names, rig info — above the real header row).</summary>
    public Table readcsv(string path, int skipRows) =>
        Table.ReadCsv(Resolve(path), new ImportOptions { SkipRows = skipRows });

    /// <summary>Reads a table from an Excel <c>.xlsx</c> workbook.</summary>
    public Table readxlsx(string path) => Table.ReadXlsx(Resolve(path));

    /// <summary>Reads an <c>.xlsx</c> table after discarding <paramref name="skipRows"/> leading rows.</summary>
    public Table readxlsx(string path, int skipRows) =>
        Table.ReadXlsx(Resolve(path), new ImportOptions { SkipRows = skipRows });

    /// <summary>Reads a table from a file, using the workbook reader for <c>.xlsx</c> and the text reader otherwise.</summary>
    public Table readtable(string path) => readtable(path, 0);

    /// <summary>Reads a table by extension after discarding <paramref name="skipRows"/> leading rows.</summary>
    public Table readtable(string path, int skipRows)
    {
        string resolved = Resolve(path);
        ImportOptions? options = skipRows > 0 ? new ImportOptions { SkipRows = skipRows } : null;
        return resolved.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? Table.ReadXlsx(resolved, options)
            : Table.ReadCsv(resolved, options);
    }

    // --- Figure display ---------------------------------------------------------------------------

    /// <summary>Displays the current figure (MATLAB <c>show</c>/<c>drawnow</c>).</summary>
    public void show() => Display(JG.CurrentFigureNumber, JG.CurrentFigure);

    /// <summary>Displays a specific figure, registering it (next free number) if it has none yet.</summary>
    public void show(FigureModel figure)
    {
        if (figure is null)
        {
            show();
            return;
        }

        int number = JG.GetFigureNumber(figure);
        if (number == 0)
        {
            number = JG.RegisterFigure(figure);
        }

        Display(number, figure);
    }

    /// <summary>Displays the figure registered under <paramref name="number"/> (JGS <c>show(fig)</c>).</summary>
    public void show(int number)
    {
        if (!JG.TryGetFigure(number, out FigureModel figure))
        {
            throw new ArgumentException($"There is no figure {number}.", nameof(number));
        }

        Display(number, figure);
    }

    private void Display(int number, FigureModel figure)
    {
        lock (_shownNumbers)
        {
            _shownNumbers.Add(number);
        }

        _context.ShowFigure(number, figure);
        Interlocked.Increment(ref _figuresShown);
    }

    /// <summary>
    /// Displays every registered figure the script created but never <c>show()</c>ed — the MATLAB
    /// expectation, where <c>figure; plot(...)</c> opens a window by itself. Called by the JGS
    /// runner after a successful run; explicit <c>show()</c> calls are not repeated.
    /// </summary>
    internal void ShowUnshownFigures()
    {
        foreach (int number in JG.FigureNumbers)
        {
            bool alreadyShown;
            lock (_shownNumbers)
            {
                alreadyShown = _shownNumbers.Contains(number);
            }

            if (!alreadyShown && JG.TryGetFigure(number, out FigureModel figure))
            {
                Display(number, figure);
            }
        }
    }

    // --- Figure files -----------------------------------------------------------------------------

    /// <summary>The host's figure save/load/export services, or null when unavailable.</summary>
    internal IScriptFigureFiles? FigureFiles => _context.FigureFiles;

    /// <summary>Saves the current figure as a <c>.graph</c> document (overwriting silently).</summary>
    public void savefigure(string path) => savefigure(path, JG.CurrentFigure);

    /// <summary>Saves a specific figure as a <c>.graph</c> document.</summary>
    public void savefigure(string path, FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        RequireFigureFiles("savefigure").Save(figure, ResolveForWrite(path));
    }

    /// <summary>Loads a <c>.graph</c> document, registers it as a new numbered figure, makes it
    /// current, and returns it — plot verbs and <c>show()</c> then target it.</summary>
    public FigureModel loadfigure(string path)
    {
        FigureModel figure = RequireFigureFiles("loadfigure").Load(Resolve(path));
        JG.RegisterFigure(figure);
        return figure;
    }

    /// <summary>Exports the current figure as an image; the format follows the extension.</summary>
    public void exportfigure(string path) => exportfigure(path, JG.CurrentFigure);

    /// <summary>Exports a specific figure as an image.</summary>
    public void exportfigure(string path, FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        RequireFigureFiles("exportfigure").Export(figure, ResolveForWrite(path));
    }

    // --- Audio ------------------------------------------------------------------------------------

    /// <summary>Reads a .wav file into normalized mono samples plus its sample rate (MATLAB <c>audioread</c>).</summary>
    public (double[] Samples, int SampleRate) audioread(string path) =>
        JGraph.Signal.WaveFile.Read(Resolve(path));

    /// <summary>Starts non-blocking playback of <paramref name="samples"/> (MATLAB <c>sound</c>).</summary>
    public void sound(double[] samples, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(samples);
        IScriptAudio audio = _context.Audio
            ?? throw new InvalidOperationException("sound is not supported by this host.");
        audio.Play(samples, sampleRate);
    }

    /// <summary>
    /// Resolves a path for *writing*: a relative name lands in the working directory (the script's
    /// folder, or the workspace root). The read resolver (<see cref="Resolve"/>) is wrong for writes —
    /// it probes for an *existing* file and falls back to the bare name (the process directory) for a
    /// file that is only about to be created.
    /// </summary>
    internal string ResolveForWrite(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _context.WorkingDirectory is { Length: > 0 } baseDir && !Path.IsPathRooted(path)
            ? Path.Combine(baseDir, path)
            : path;
    }

    private IScriptFigureFiles RequireFigureFiles(string operation) =>
        _context.FigureFiles
            ?? throw new InvalidOperationException($"{operation} is not supported by this host.");

    /// <summary>Resolves a script-supplied path through the context's workspace resolver, falling back
    /// to the working directory. Also used by engine-level file access such as the JGS <c>run()</c>.</summary>
    internal string Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_context.ResolvePath is { } resolve)
            return resolve(path);
        return _context.WorkingDirectory is { Length: > 0 } baseDir && !Path.IsPathRooted(path)
            ? Path.Combine(baseDir, path)
            : path;
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
