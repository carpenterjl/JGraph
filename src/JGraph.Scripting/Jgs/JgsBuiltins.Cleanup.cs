namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>onCleanup</c> (V10, ADR 0171, appendix A #29): a handle whose destructor runs the task it
/// was given, so <c>c = onCleanup(@() fclose(fid))</c> runs at the frame's exit, at <c>clear c</c>,
/// at an error's unwinding — wherever the last holder of <c>c</c> goes.
/// </summary>
/// <remarks>
/// It is the class MATLAB's own <c>onCleanup.m</c> is: a <c>classdef … &lt; handle</c> with a
/// <c>task</c> property and a <c>delete</c> that calls it, defined from source the first time
/// the name is used. Being an ordinary user class, everything the lifetime model does for a
/// handle with a destructor — the exact count, the check after the dropping statement, the
/// <c>MATLAB:class:DestructorError</c> warning when the task fails, <c>isvalid</c>, an explicit
/// <c>delete(c)</c> — it does for this one with no special road.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The class, as MATLAB writes it.</summary>
    private const string OnCleanupSource = """
        classdef onCleanup < handle
            properties
                task = @() 1
            end
            methods
                function obj = onCleanup(functionHandle)
                    obj.task = functionHandle;
                end
                function delete(obj)
                    obj.task();
                end
            end
        end
        """;

    /// <summary>Registers <c>onCleanup</c>: the constructor of the class above, defined on first use.</summary>
    internal static void RegisterCleanupBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        JgsClass? definition = null;
        env.Builtins.Register("onCleanup", JgsValue.Function(new BuiltinFunction("onCleanup", (args, line, col) =>
        {
            if (args.Count != 1)
            {
                throw new JgsRuntimeException(line, col, args.Count == 0 ? "Not enough input arguments." : "Too many input arguments.");
            }

            if (args[0].Type != JgsType.Function)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:onCleanup:invalidInput",
                    "Input must be a function handle.");
            }

            definition ??= DefineOnCleanup(interpreter);
            return definition.Construct(args, line, col);
        })));
    }

    private static JgsClass DefineOnCleanup(Interpreter interpreter)
    {
        IReadOnlyList<Stmt> program = Parser.Parse(OnCleanupSource, "onCleanup.m", JgsDialect.Matlab);
        return interpreter.DefineClass((ClassdefStmt)program[0], interpreter.NewFileScope());
    }
}
