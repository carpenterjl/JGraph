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
    private readonly HashSet<int> _shownThisRun = new();
    private long _runStartStamp;
    private string? _runScriptDirectory;
    private int _figuresShown;

    /// <summary>Creates the globals over a run's <paramref name="context"/>.</summary>
    public JGraphScriptGlobals(ScriptContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        BeginRun();
    }

    /// <summary>How many figures the current run has displayed.</summary>
    public int FiguresShown => Volatile.Read(ref _figuresShown);

    /// <summary>
    /// Starts a new run: figure display is tracked per run, so a script that plots into figures it
    /// already opened in an earlier run displays them again — the MATLAB expectation, where running
    /// a script always leaves its figures on screen. A long-lived console session calls this for
    /// every statement and every F5.
    /// </summary>
    /// <param name="scriptDirectory">The folder of the file being run, tried first for its relative
    /// paths, or null for prompt input (which has no file to sit beside).</param>
    internal void BeginRun(string? scriptDirectory = null)
    {
        lock (_shownThisRun)
        {
            _shownThisRun.Clear();
        }

        _runStartStamp = JG.TouchStamp;
        _runScriptDirectory = scriptDirectory;
        Volatile.Write(ref _figuresShown, 0);
    }

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
        lock (_shownThisRun)
        {
            _shownThisRun.Add(number);
        }

        _context.ShowFigure(number, figure);
        Interlocked.Increment(ref _figuresShown);
    }

    /// <summary>
    /// Displays every figure this run touched but has not shown yet — the MATLAB expectation, where
    /// <c>figure; plot(...)</c> opens a window by itself and re-running the script brings the window
    /// back. Called after a run finishes; a figure the run explicitly <c>show()</c>ed is not shown
    /// twice, and figures the run never touched (opened from a file, say) are left alone.
    /// </summary>
    internal void ShowTouchedFigures()
    {
        foreach (int number in JG.FiguresTouchedSince(_runStartStamp))
        {
            bool alreadyShown;
            lock (_shownThisRun)
            {
                alreadyShown = _shownThisRun.Contains(number);
            }

            if (!alreadyShown && JG.TryGetFigure(number, out FigureModel figure))
            {
                Display(number, figure);
            }
        }
    }

    /// <summary>
    /// Closes figure <paramref name="number"/>: drops it from the registry and asks the host to close
    /// the window it opened. Returns whether there was such a figure. A later <c>figure(n)</c> with the
    /// same number therefore builds a fresh figure and opens a fresh window, exactly as in MATLAB.
    /// </summary>
    internal bool CloseFigure(int number)
    {
        bool existed = JG.CloseFigure(number);
        lock (_shownThisRun)
        {
            _shownThisRun.Remove(number);
        }

        if (existed)
        {
            _context.CloseFigure?.Invoke(number);
        }

        return existed;
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

    // --- Ending the session -----------------------------------------------------------------------

    /// <summary>Stops the script and asks the host to exit with code 0.</summary>
    public void exit() => exit(0);

    /// <summary>Stops the script and asks the host to exit with <paramref name="code"/>.</summary>
    public void exit(int code) => throw new ScriptExitException(code);

    /// <summary>An alias for <see cref="exit()"/>.</summary>
    public void quit() => exit(0);

    /// <summary>An alias for <see cref="exit(int)"/>.</summary>
    public void quit(int code) => exit(code);

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
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        // A file run writes beside the script, matching where its reads come from.
        string? baseDirectory = _runScriptDirectory is { Length: > 0 } scriptDir
            ? scriptDir
            : _context.WorkingDirectory;
        return baseDirectory is { Length: > 0 } directory ? Path.Combine(directory, path) : path;
    }

    private IScriptFigureFiles RequireFigureFiles(string operation) =>
        _context.FigureFiles
            ?? throw new InvalidOperationException($"{operation} is not supported by this host.");

    // --- File handles (fopen/fclose and friends) --------------------------------------------------

    private readonly Dictionary<int, FileStream> _openFiles = new();
    private int _nextFileId = 3; // MATLAB reserves 0-2 for stdin/stdout/stderr

    /// <summary>
    /// Opens a file for the <c>fopen</c> builtin and returns its id, or -1 when it cannot be opened —
    /// MATLAB's convention, so scripts can test <c>fid == -1</c>. Modes: r, w, a, r+ (a trailing
    /// 'b'/'t' is accepted and ignored; everything here is binary-exact).
    /// </summary>
    internal int OpenFile(string path, string mode)
    {
        string trimmed = mode.TrimEnd('b', 't');
        try
        {
            FileStream stream = trimmed switch
            {
                "r" => new FileStream(Resolve(path), FileMode.Open, FileAccess.Read),
                "w" => new FileStream(ResolveForWrite(path), FileMode.Create, FileAccess.Write),
                "a" => new FileStream(ResolveForWrite(path), FileMode.Append, FileAccess.Write),
                "r+" => new FileStream(Resolve(path), FileMode.Open, FileAccess.ReadWrite),
                _ => throw new ArgumentException($"fopen does not support the mode '{mode}'."),
            };

            int id = _nextFileId++;
            _openFiles[id] = stream;
            return id;
        }
        catch (IOException)
        {
            return -1;
        }
        catch (UnauthorizedAccessException)
        {
            return -1;
        }
    }

    /// <summary>The stream behind a file id, or null when the id is not open.</summary>
    internal FileStream? FileFor(int id) => _openFiles.TryGetValue(id, out FileStream? stream) ? stream : null;

    /// <summary>Closes one file id; false when it was not open.</summary>
    internal bool CloseFile(int id)
    {
        if (!_openFiles.Remove(id, out FileStream? stream))
        {
            return false;
        }

        stream.Dispose();
        return true;
    }

    /// <summary>Closes every open file — <c>fclose('all')</c>, and the session's own teardown.</summary>
    internal void CloseAllFiles()
    {
        foreach (FileStream stream in _openFiles.Values)
        {
            stream.Dispose();
        }

        _openFiles.Clear();
    }

    /// <summary>Clears the output sink's display — the <c>clc</c> builtin. Sinks without a display ignore it.</summary>
    internal void ClearOutput() => _context.Output.Clear();

    /// <summary>The run's working directory (the workspace root, or the batch start folder), or null
    /// when the host supplied none — what <c>dir</c> and <c>path</c> report against.</summary>
    internal string? WorkingDirectory => _context.WorkingDirectory;

    /// <summary>Resolves a script-supplied path through the context's workspace resolver, falling back
    /// to the working directory. Also used by engine-level file access such as the JGS <c>run()</c>.</summary>
    internal string Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // A file run looks in the script's own folder first: `readcsv('data.csv')` in a script means
        // the copy sitting beside it, not one that happens to share its name at the workspace root.
        if (!Path.IsPathRooted(path) && _runScriptDirectory is { Length: > 0 } scriptDir)
        {
            string beside = Path.Combine(scriptDir, path);
            if (File.Exists(beside))
            {
                return beside;
            }
        }

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
