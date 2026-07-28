using JGraph.Api;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A live interactive session for the JGS interpreter, in either dialect. Everything
/// <see cref="JgsRunner.Run"/> builds per run — the host globals, the built-in scope, the interpreter,
/// the <c>run()</c> include builtin — is built <em>once</em> here and reused for every statement, which
/// is what makes variables, functions and figures survive from one prompt to the next.
/// </summary>
/// <remarks>
/// Three things a one-shot run does are deliberately absent between statements, and only happen in
/// <see cref="Clear"/> or <see cref="DisposeAsync"/>: <c>JG.Reset()</c> (figures must stay open, and
/// <c>figure(1)</c> at the prompt must keep meaning the window it already opened), releasing packed
/// buffers (live variables still point at them), and rebuilding the built-in scope. See ADR 0035.
/// </remarks>
internal sealed class JgsReplSession : IScriptSession
{
    private readonly ScriptContext _context;
    private readonly JgsDialect _dialect;

    private JGraphScriptGlobals _globals = null!;
    private JgsEnvironment _environment = null!;
    private Interpreter _interpreter = null!;
    private Dictionary<string, JgsValue> _pristine = null!;
    private bool _disposed;

    /// <summary>Creates a session and its first workspace. Resets the figure registry: a new session
    /// is a new workspace, and its figure numbers start from a known state.</summary>
    /// <param name="context">The host services for the session's whole lifetime.</param>
    /// <param name="dialect">The language variant every statement is parsed in.</param>
    /// <param name="language">The engine language name this session reports.</param>
    public JgsReplSession(ScriptContext context, JgsDialect dialect, string language)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        Language = language ?? throw new ArgumentNullException(nameof(language));
        JG.Reset();
        Build();
    }

    /// <inheritdoc />
    public string Language { get; }

    /// <inheritdoc />
    public Task<ScriptRunResult> ExecuteAsync(string code, string sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(sourceId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        // The token is deliberately not passed to Task.Run: Execute maps cancellation to a failed
        // result, and a token already cancelled at call time would otherwise fault the task instead.
        return Task.Run(() => Execute(code, sourceId, cancellationToken), CancellationToken.None);
    }

    /// <inheritdoc />
    public Task<ScriptRunResult> ExecuteFileAsync(string code, string sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(sourceId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() => Execute(code, sourceId, cancellationToken, asFile: true), CancellationToken.None);
    }

    /// <inheritdoc />
    public IReadOnlyList<ScriptVariable> GetVariables() =>
        _disposed ? Array.Empty<ScriptVariable>() : JgsRunner.SnapshotGlobals(_environment, _pristine);

    /// <inheritdoc />
    public void Clear()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseWorkspace();
        JG.Reset();
        Build();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            ReleaseWorkspace();
        }

        return ValueTask.CompletedTask;
    }

    private ScriptRunResult Execute(string code, string sourceId, CancellationToken cancellationToken, bool asFile = false)
    {
        _globals.BeginRun(asFile ? ScriptDirectoryOf(sourceId) : null, asFile ? sourceId : null);
        _interpreter.BeginStatement(cancellationToken);
        try
        {
            IReadOnlyList<Stmt> program = Parser.Parse(code, sourceId, _dialect);
            _interpreter.Run(program);
            if (asFile)
            {
                // A file that is nothing but function definitions is a MATLAB function file: running
                // it means calling its main function. Prompt input never takes this branch.
                JgsRunner.InvokeMainIfFunctionFile(program, _environment);
            }

            _globals.ShowTouchedFigures();
            return ScriptRunResult.Ok(_globals.FiguresShown, GetVariables());
        }
        catch (Exception ex) when (ScriptExitException.Unwrap(ex) is { } exit)
        {
            _globals.ShowTouchedFigures();
            return ScriptRunResult.Exited(exit.ExitCode, _globals.FiguresShown, GetVariables());
        }
        catch (JgsException ex)
        {
            // A failed statement leaves the workspace as it stands — that is the point of a prompt.
            // Whatever ran before the error keeps its effect, exactly as MATLAB does.
            var diagnostic = new ScriptDiagnostic(ex.Line, ex.Column, ex.Message, IsError: true);
            _context.Output.WriteError(diagnostic.ToString());
            _globals.ShowTouchedFigures();
            return ScriptRunResult.Failed(ex.Message, new[] { diagnostic });
        }
        catch (OperationCanceledException)
        {
            _globals.ShowTouchedFigures();
            return ScriptRunResult.Failed("Statement was cancelled.");
        }
    }

    /// <summary>
    /// The folder a file run's relative paths are tried against. The source id is the document's path
    /// when it has one; an unsaved document passes an empty id and simply has no folder of its own.
    /// </summary>
    private static string? ScriptDirectoryOf(string sourceId) =>
        sourceId.Length > 0 && System.IO.Path.IsPathRooted(sourceId)
            ? System.IO.Path.GetDirectoryName(sourceId)
            : null;

    private void Build()
    {
        _globals = new JGraphScriptGlobals(_context);
        _environment = JgsBuiltins.CreateGlobals(_globals, CancellationToken.None, _dialect);

        // The interpreter's token is replaced per statement by BeginStatement; the one it is
        // constructed with only covers the (impossible) window before the first statement.
        _interpreter = new Interpreter(_environment, CancellationToken.None, hook: null,
            echo: line => _context.Output.WriteLine(line), _dialect);
        JgsRunner.DefineRunBuiltin(_environment, _interpreter, _globals, _dialect);
        JgsBuiltins.RegisterEvalBuiltins(_environment, _interpreter, _globals, _dialect);
        JgsBuiltins.RegisterSessionBuiltins(_environment, _globals);
        DefineClearBuiltin(_environment);
        DefineWhosBuiltin(_environment);
        JgsWorkspaceIo.DefineSaveLoad(_environment, _globals, () => UserVariables());

        _pristine = _environment.Locals.ToDictionary(
            static p => p.Key, static p => p.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Defines <c>clear</c>: the in-statement half of <see cref="Clear"/>. It drops the user's variables
    /// and reverts any rebound built-in, but does not touch figures or rebuild the scope — a live
    /// interpreter and every closure it has made hold a reference to this environment, so the scope has
    /// to be emptied in place rather than replaced.
    /// </summary>
    private void DefineClearBuiltin(JgsEnvironment environment) =>
        environment.Declare("clear", JgsValue.Function(new BuiltinFunction("clear", (args, line, column) =>
        {
            if (args.Count != 0)
            {
                throw new JgsRuntimeException(line, column, "clear takes no arguments.");
            }

            JgsRunner.DisposeBuffers(UserVariables().Select(static pair => pair.Value));
            environment.RetainOnly(_pristine);
            return JgsValue.Null;
        })));

    /// <summary>
    /// Defines <c>whos</c>: an aligned name / size / class listing of the user's variables — the
    /// console twin of the Workspace pane, and session-owned for the same reason <c>clear</c> is
    /// (only the session knows which names the user created).
    /// </summary>
    private void DefineWhosBuiltin(JgsEnvironment environment) =>
        environment.Declare("whos", JgsValue.Function(new BuiltinFunction("whos", (args, line, column) =>
        {
            if (args.Count != 0)
            {
                throw new JgsRuntimeException(line, column, "whos takes no arguments.");
            }

            List<(string Name, string Size, string Kind)> rows = UserVariables()
                .OrderBy(static pair => pair.Name, StringComparer.Ordinal)
                .Select(pair => (pair.Name, SizeOf(pair.Value), KindOf(pair.Value)))
                .ToList();
            if (rows.Count == 0)
            {
                return JgsValue.Null;
            }

            int nameWidth = Math.Max("Name".Length, rows.Max(static r => r.Name.Length));
            int sizeWidth = Math.Max("Size".Length, rows.Max(static r => r.Size.Length));
            _context.Output.WriteLine($"  {"Name".PadRight(nameWidth)}  {"Size".PadRight(sizeWidth)}  Class");
            foreach ((string name, string size, string kind) in rows)
            {
                _context.Output.WriteLine($"  {name.PadRight(nameWidth)}  {size.PadRight(sizeWidth)}  {kind}");
            }

            return JgsValue.Null;
        })));

    private static string SizeOf(JgsValue value) => value.Type switch
    {
        JgsType.Array => $"{JgsMatrix.RowCount(value)}x{JgsMatrix.ColCount(value)}",
        JgsType.String => $"1x{value.AsString.Length}",
        JgsType.Cell => $"1x{value.AsCell.Length}",
        JgsType.Table => $"{value.AsTable.RowCount}x{value.AsTable.ColumnCount}",
        JgsType.Image => $"{value.AsImage.Height}x{value.AsImage.Width}x{value.AsImage.Channels}",
        _ => "1x1",
    };

    private static string KindOf(JgsValue value) => value.Type switch
    {
        JgsType.Number or JgsType.Array => "double",
        JgsType.Complex => "complex",
        JgsType.Bool => "logical",
        JgsType.String => "char",
        JgsType.Cell => "cell",
        JgsType.Struct => "struct",
        JgsType.Table => "table",
        JgsType.Image => "image",
        JgsType.Function => "function_handle",
        _ => value.TypeName,
    };

    /// <summary>The user-created (or rebound) names and their values, the set <c>whos</c> lists.</summary>
    private IEnumerable<(string Name, JgsValue Value)> UserVariables()
    {
        foreach ((string name, JgsValue value) in _environment.Locals)
        {
            if (!_pristine.TryGetValue(name, out JgsValue? original) || !ReferenceEquals(original, value))
            {
                yield return (name, value);
            }
        }
    }

    private void ReleaseWorkspace()
    {
        _globals.CloseAllFiles();
        JgsRunner.DisposeBuffers(UserVariables().Select(static pair => pair.Value));
    }

}
