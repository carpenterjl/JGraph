using System.Globalization;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>substruct</c>, <c>subsref</c> and <c>subsasgn</c> (compiler and SDK plan, stage K12, whose
/// strongly typed C++ interface reaches a class's properties through them): a subscript structure's
/// levels (<c>type</c> <c>()</c>, <c>{}</c> or <c>.</c>, with <c>subs</c> a cell of indices or a field
/// name) done as the indexing they spell, written out and run by the interpreter in a scope of its
/// own, so every value indexes and assigns exactly as it would in code; a char <c>':'</c> among the
/// indices is the colon, as MATLAB reads it there (a string <c>":"</c> is not).
/// </summary>
internal static partial class JgsBuiltins
{
    private const string SubscriptValue = "jgsubs_value";
    private const string SubscriptRight = "jgsubs_right";
    private const string SubscriptOutput = "jgsubs_out";

    private static void RegisterSubscriptBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        // R2025b's substruct.m: two arguments a level, nothing else checked.
        env.Builtins.Register("substruct", JgsValue.Function(new BuiltinFunction("substruct", (args, line, col) =>
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:substruct:nargin", "SUBSTRUCT takes at least two arguments.");
            }

            if (args.Count % 2 != 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:substruct:narginOdd", "SUBSTRUCT takes an even number of arguments.");
            }

            var levels = new List<Dictionary<string, JgsValue>>();
            for (int i = 0; i < args.Count; i += 2)
            {
                levels.Add(new Dictionary<string, JgsValue>(StringComparer.Ordinal) { ["type"] = args[i], ["subs"] = args[i + 1] });
            }

            return levels.Count == 1 ? JgsValue.Struct(levels[0]) : JgsValue.StructArray([.. levels]);
        })
        {
            KeepsStringArguments = true,
        }));

        JgsValue[] Reference(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            if (args.Count < 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            if (args.Count > 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            var scope = new JgsEnvironment(env);
            string chain = SubscriptChain(interpreter, scope, args[1], assigning: false, line, col);
            scope.Declare(SubscriptValue, interpreter.CopyForBinding(args[0]));
            if (wanted <= 1)
            {
                return [interpreter.EvaluateSource(SubscriptValue + chain, scope, line, col)];
            }

            // A comma-separated list gives as many outputs as it has, and too few is the multiple
            // assignment's own refusal, as R2025b's is ([a, b] = subsref(c, substruct('{}', {':'}))).
            var outputs = new string[wanted];
            for (int i = 0; i < wanted; i++)
            {
                outputs[i] = SubscriptOutput + (i + 1).ToString(CultureInfo.InvariantCulture);
            }

            interpreter.EvaluateSource("[" + string.Join(", ", outputs) + "] = " + SubscriptValue + chain + ";", scope, line, col, asStatement: true);
            var values = new JgsValue[wanted];
            for (int i = 0; i < wanted; i++)
            {
                scope.TryGet(outputs[i], out values[i]);
            }

            return values;
        }

        env.Builtins.Register("subsref", JgsValue.Function(new BuiltinFunction("subsref", (args, line, col) => Reference(args, 1, line, col)[0])
        {
            MultiOutput = Reference,
            KeepsStringArguments = true,
        }));

        JgsValue Assign(IReadOnlyList<JgsValue> args, int line, int col)
        {
            // No value is an assignment of nothing, as R2025b reads it (k12_cpp_shared_library: subsasgn_two_args).
            if (args.Count == 2)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputsDueToMissingBraces", "Unable to perform assignment with 0 elements on the right-hand side.");
            }

            if (args.Count < 3)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            if (args.Count > 3)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
            }

            var scope = new JgsEnvironment(env);
            string chain = SubscriptChain(interpreter, scope, args[1], assigning: true, line, col);

            // Bound as an assignment binds them, so the write leaves the caller's own value alone.
            scope.Declare(SubscriptValue, interpreter.CopyForBinding(args[0]));
            scope.Declare(SubscriptRight, interpreter.CopyForBinding(args[2]));
            interpreter.EvaluateSource(SubscriptValue + chain + " = " + SubscriptRight + ";", scope, line, col, asStatement: true);
            return scope.TryGet(SubscriptValue, out JgsValue changed) ? changed : args[0];
        }

        env.Builtins.Register("subsasgn", JgsValue.Function(new BuiltinFunction("subsasgn", Assign)
        {
            // R2025b refuses a subsasgn whose answer goes nowhere (k12_cpp_shared_library: subsasgn_no_output).
            MultiOutput = (args, wanted, line, col) => wanted == 0
                ? throw new JgsRuntimeException(line, col, "MATLAB:subsasgnMustHaveOutput", "SUBSASGN must be called with an output.")
                : [Assign(args, line, col)],
            TakesOutputCount = true,
            BindsAnsAsStatement = false,
            KeepsStringArguments = true,
        }));
    }

    /// <summary>
    /// The indexing a subscript structure spells, its indices and field names declared in
    /// <paramref name="scope"/>: <c>(jgsubs_1{1}, :)</c>, <c>{jgsubs_2{1}}</c>, <c>.(jgsubs_3)</c>.
    /// Every level is checked before any is indexed, in R2025b's order (probed for stage K12): the
    /// structure's two fields and its elements, then each level's type, that only a <c>.</c> follows a
    /// <c>()</c>, and its cell of indices, of which an assignment's <c>()</c> needs one. A <c>.</c>
    /// level's name goes to the dynamic field reference as given, which refuses one that is not a
    /// name, as R2025b's does; a <c>{}</c> with no index is every element, as <c>c{:}</c> is.
    /// </summary>
    private static string SubscriptChain(Interpreter interpreter, JgsEnvironment scope, JgsValue subscripts, bool assigning, int line, int col)
    {
        if (subscripts.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:subsArgNotStruc", "Subscript argument to SUBSREF and SUBSASGN must be a structure.");
        }

        JgsStructArray levels = subscripts.AsStructArray;
        string[] fields = levels.FieldNames;
        if (fields.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:subsMustHaveTwo", "Subscript argument to SUBSREF and SUBSASGN must have two fields.");
        }

        if (!fields.Contains("type") || !fields.Contains("subs"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:subsMustHaveTypeSubs",
                "Subscript argument to SUBSREF and SUBSASGN must have two fields whose names are \"type\" and \"subs\".");
        }

        if (levels.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:subsArgEmpty", "Subscript argument to SUBSREF and SUBSASGN must not be empty.");
        }

        var chain = new StringBuilder();
        string previous = string.Empty;
        for (int k = 0; k < levels.Length; k++)
        {
            JgsValue type = levels.Elements[k]["type"];
            JgsValue subs = levels.Elements[k]["subs"];
            if (!IsTextScalar(type))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:subsTypeMustBeChar",
                    "The \"type\" field in the indexing structure argument to SUBSREF and SUBSASGN must be a string scalar or character vector.");
            }

            string kind = TextOf(type);
            if (kind is not ("()" or "{}" or "."))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:subsTypeMustBeSquigglyOrSmooth",
                    "The \"type\" field in the indexing structure argument to SUBSREF and SUBSASGN must be a string scalar or character vector of \".\" or \"{}\" or \"()\".");
            }

            if (previous == "()" && kind != ".")
            {
                throw new JgsRuntimeException(line, col, "MATLAB:subsDotMustFollow", "Only a dot field name can follow ()'s.");
            }

            previous = kind;
            string name = "jgsubs_" + (k + 1).ToString(CultureInfo.InvariantCulture);
            if (kind == ".")
            {
                scope.Declare(name, interpreter.CopyForBinding(subs));
                chain.Append(".(").Append(name).Append(')');
                continue;
            }

            if (subs.Type != JgsType.Cell)
            {
                throw kind == "()"
                    ? new JgsRuntimeException(line, col, "MATLAB:subsSmoothTypeSubsMustBeCell", "SUBS field must be a cell array for () TYPE.")
                    : new JgsRuntimeException(line, col, "MATLAB:subsSquigglyTypeSubsMustBeCell", "SUBS field must be a cell array for {} TYPE.");
            }

            JgsValue[] indices = subs.AsCell;
            if (indices.Length == 0 && assigning && kind == "()")
            {
                throw new JgsRuntimeException(line, col, "MATLAB:atLeastOneIndexIsRequired", "At least one index is required.");
            }

            scope.Declare(name, interpreter.CopyForBinding(subs));
            var items = new string[indices.Length];
            for (int j = 0; j < indices.Length; j++)
            {
                items[j] = indices[j].Type == JgsType.String && indices[j].AsString == ":"
                    ? ":"
                    : name + "{" + (j + 1).ToString(CultureInfo.InvariantCulture) + "}";
            }

            chain.Append(kind[0]).Append(kind == "{}" && items.Length == 0 ? ":" : string.Join(", ", items)).Append(kind[1]);
        }

        return chain.ToString();
    }
}
