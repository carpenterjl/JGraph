using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Data;
using JGraph.Objects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;

namespace JGraph.Scripting;

/// <summary>
/// Runs C# scripts with the Roslyn scripting engine. Scripts see the JGraph plotting API as top-level
/// functions: the static <see cref="JG"/> facade is imported statically (so <c>Plot(...)</c>,
/// <c>Title(...)</c>, etc. need no qualifier) and the host helpers (<c>readcsv</c>, <c>print</c>,
/// <c>show</c>) come from the <see cref="JGraphScriptGlobals"/> globals object.
/// </summary>
public sealed class CSharpScriptEngine : IScriptEngine
{
    private static readonly ScriptOptions Options = BuildOptions();

    /// <inheritdoc />
    public string Language => "C#";

    /// <summary>Always true — the C# engine is built in and needs no external runtime.</summary>
    public bool IsAvailable => true;

    /// <inheritdoc />
    public Task<ScriptRunResult> RunAsync(string code, ScriptContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);
        return Task.Run(() => RunCoreAsync(code, context, cancellationToken), cancellationToken);
    }

    private static async Task<ScriptRunResult> RunCoreAsync(string code, ScriptContext context, CancellationToken cancellationToken)
    {
        // Each run starts from a clean current-figure/current-axes state.
        JG.Reset();

        var globals = new JGraphScriptGlobals(context);
        try
        {
            Script<object> script = CSharpScript.Create<object>(code, Options, typeof(JGraphScriptGlobals));

            ImmutableArray<Diagnostic> diagnostics = script.Compile(cancellationToken);
            List<ScriptDiagnostic> errors = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(Map)
                .ToList();

            if (errors.Count > 0)
            {
                foreach (ScriptDiagnostic error in errors)
                {
                    context.Output.WriteError(error.ToString());
                }

                return ScriptRunResult.Failed(
                    $"Compilation failed with {errors.Count} error(s).", errors);
            }

            foreach (Diagnostic warning in diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning))
            {
                context.Output.WriteError(Map(warning).ToString());
            }

            ScriptState<object> state = await script
                .RunAsync(globals, catchException: _ => true, cancellationToken)
                .ConfigureAwait(false);

            if (state.Exception is not null)
            {
                // exit()/quit() unwind through the same channel as a failure, but mean the opposite.
                if (ScriptExitException.Unwrap(state.Exception) is { } exit)
                {
                    return ScriptRunResult.Exited(exit.ExitCode, globals.FiguresShown);
                }

                context.Output.WriteError(state.Exception.ToString());
                return ScriptRunResult.Failed(state.Exception.Message);
            }

            return ScriptRunResult.Ok(globals.FiguresShown, SnapshotVariables(state));
        }
        catch (CompilationErrorException ex)
        {
            List<ScriptDiagnostic> errors = ex.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(Map)
                .ToList();
            foreach (ScriptDiagnostic error in errors)
            {
                context.Output.WriteError(error.ToString());
            }

            return ScriptRunResult.Failed("Compilation failed.", errors);
        }
        catch (OperationCanceledException)
        {
            return ScriptRunResult.Failed("Script run was cancelled.");
        }
    }

    private static IReadOnlyList<ScriptVariable> SnapshotVariables(ScriptState<object> state)
    {
        try
        {
            var variables = new List<ScriptVariable>(state.Variables.Length);
            foreach (ScriptVariable? projected in state.Variables.Select(Project))
            {
                if (projected is not null)
                {
                    variables.Add(projected);
                }
            }

            variables.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return variables;
        }
        catch (Exception)
        {
            // A snapshot must never fail an otherwise-successful run.
            return Array.Empty<ScriptVariable>();
        }
    }

    private static ScriptVariable? Project(Microsoft.CodeAnalysis.Scripting.ScriptVariable variable)
    {
        object? value = variable.Value;
        if (value is Delegate)
        {
            return new ScriptVariable(variable.Name, "function", variable.Type.Name, RawValue: null);
        }

        string display = value switch
        {
            null => "null",
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            double[] array => $"double[{array.Length}]",
            Table table => $"table[{table.RowCount}x{table.ColumnCount}]",
            _ => value.ToString() ?? string.Empty,
        };

        object? raw = value switch
        {
            double or bool or string => value,
            double[] => value,
            Table => value,
            _ => null,
        };

        return new ScriptVariable(
            variable.Name,
            FriendlyTypeName(variable.Type),
            ScriptVariable.Truncate(display),
            raw);
    }

    private static string FriendlyTypeName(Type type) => type switch
    {
        _ when type == typeof(double) || type == typeof(float) || type == typeof(int) || type == typeof(long) => "number",
        _ when type == typeof(bool) => "bool",
        _ when type == typeof(string) => "string",
        _ when type == typeof(double[]) => "array",
        _ when type == typeof(Table) => "table",
        _ => type.Name,
    };

    private static ScriptDiagnostic Map(Diagnostic diagnostic)
    {
        bool inSource = diagnostic.Location.IsInSource;
        LinePosition start = diagnostic.Location.GetLineSpan().StartLinePosition;
        return new ScriptDiagnostic(
            Line: inSource ? start.Line + 1 : 0,
            Column: inSource ? start.Character + 1 : 0,
            Message: diagnostic.GetMessage(CultureInfo.InvariantCulture),
            IsError: diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static ScriptOptions BuildOptions() =>
        ScriptOptions.Default
            .WithReferences(
                typeof(JG).Assembly,                     // JGraph.Api
                typeof(FigureModel).Assembly,            // JGraph.Core
                typeof(LinePlot).Assembly,               // JGraph.Objects
                typeof(Table).Assembly,                  // JGraph.Data
                typeof(Signal.Fft).Assembly,             // JGraph.Signal
                typeof(JGraphScriptGlobals).Assembly)    // JGraph.Scripting (the globals type)
            .WithImports(
                "System",
                "System.Math",
                "System.Linq",
                "System.Collections.Generic",
                "JGraph.Api",
                "JGraph.Api.JG",
                "JGraph.Core.Model",
                "JGraph.Core.Primitives",
                "JGraph.Core.Drawing",
                "JGraph.Objects",
                "JGraph.Data",
                "JGraph.Data.Import",
                "JGraph.Signal");
}
