using System.Globalization;
using System.Linq;
using JGraph.Api;
using JGraph.Data;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace JGraph.Scripting;

/// <summary>
/// A live C# session over Roslyn's <see cref="ScriptState{T}"/>. Each statement is compiled as a
/// continuation of the last with <c>ContinueWithAsync</c> — the scripting API's own REPL primitive —
/// so <c>var x = 3;</c> typed at the prompt is still in scope three statements later.
/// </summary>
/// <remarks>
/// Two properties fall out of how Roslyn works and are worth knowing. Compilation is per statement, so
/// the first one pays a one-off warm-up of roughly a second while the compiler and the reference set
/// load. And cancellation only takes effect between statements: a C# <c>while (true) {}</c> at the
/// prompt cannot be interrupted, because the running code is ordinary IL with no cooperative check in
/// it — unlike JGS, whose interpreter we own.
/// </remarks>
internal sealed class CSharpReplSession : IScriptSession
{
    private readonly ScriptContext _context;
    private JGraphScriptGlobals _globals;
    private ScriptState<object>? _state;
    private int _figuresReported;
    private bool _disposed;

    /// <summary>Creates a session. Resets the figure registry, as a new workspace should.</summary>
    public CSharpReplSession(ScriptContext context, string language)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        Language = language ?? throw new ArgumentNullException(nameof(language));
        JG.Reset();
        _globals = new JGraphScriptGlobals(_context);
    }

    /// <inheritdoc />
    public string Language { get; }

    /// <inheritdoc />
    public Task<ScriptRunResult> ExecuteAsync(string code, string sourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Not passed to Task.Run: ExecuteCoreAsync maps cancellation to a failed result, and an
        // already-cancelled token would otherwise fault the task rather than report it.
        return Task.Run(() => ExecuteCoreAsync(code, cancellationToken), CancellationToken.None);
    }

    /// <inheritdoc />
    public IReadOnlyList<ScriptVariable> GetVariables() =>
        _state is null ? Array.Empty<ScriptVariable>() : CSharpScriptEngine.SnapshotVariables(_state);

    /// <inheritdoc />
    public void Clear()
    {
        // Dropping the state drops every variable it held; the next statement starts a new chain.
        _state = null;
        JG.Reset();
        _figuresReported = 0;
        _globals = new JGraphScriptGlobals(_context);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _state = null;
        return ValueTask.CompletedTask;
    }

    private async Task<ScriptRunResult> ExecuteCoreAsync(string code, CancellationToken cancellationToken)
    {
        try
        {
            _state = _state is null
                ? await CSharpScript
                    .Create<object>(code, CSharpScriptEngine.Options, typeof(JGraphScriptGlobals))
                    .RunAsync(_globals, catchException: _ => true, cancellationToken)
                    .ConfigureAwait(false)
                : await _state
                    .ContinueWithAsync<object>(code, CSharpScriptEngine.Options, catchException: _ => true, cancellationToken)
                    .ConfigureAwait(false);

            if (_state.Exception is not null)
            {
                if (ScriptExitException.Unwrap(_state.Exception) is { } exit)
                {
                    _globals.ShowUnshownFigures();
                    return ScriptRunResult.Exited(exit.ExitCode, TakeFiguresShown(), GetVariables());
                }

                _context.Output.WriteError(_state.Exception.ToString());
                _globals.ShowUnshownFigures();
                return ScriptRunResult.Failed(_state.Exception.Message);
            }

            EchoReturnValue(_state.ReturnValue);
            _globals.ShowUnshownFigures();
            return ScriptRunResult.Ok(TakeFiguresShown(), GetVariables());
        }
        catch (CompilationErrorException ex)
        {
            // The state is untouched by a statement that never compiled, so the workspace survives a typo.
            List<ScriptDiagnostic> errors = ex.Diagnostics
                .Where(static d => d.Severity == DiagnosticSeverity.Error)
                .Select(CSharpScriptEngine.Map)
                .ToList();
            foreach (ScriptDiagnostic error in errors)
            {
                _context.Output.WriteError(error.ToString());
            }

            return ScriptRunResult.Failed("Compilation failed.", errors);
        }
        catch (OperationCanceledException)
        {
            return ScriptRunResult.Failed("Statement was cancelled.");
        }
    }

    /// <summary>
    /// Prints the value of a statement that was an expression — <c>1 + 1</c> at the prompt should show
    /// <c>2</c>. Roslyn leaves <see cref="ScriptState{T}.ReturnValue"/> null for a statement that
    /// produced nothing, which is exactly the case where nothing should be printed.
    /// </summary>
    private void EchoReturnValue(object? value)
    {
        if (value is null)
        {
            return;
        }

        _context.Output.WriteLine(value switch
        {
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            double[] array => $"double[{array.Length}]",
            Table table => $"table[{table.RowCount}x{table.ColumnCount}]",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        });
    }

    /// <summary>Figures displayed by the statement that just ran — see the note in <c>JgsReplSession</c>.</summary>
    private int TakeFiguresShown()
    {
        int total = _globals.FiguresShown;
        int delta = total - _figuresReported;
        _figuresReported = total;
        return delta < 0 ? 0 : delta;
    }
}
