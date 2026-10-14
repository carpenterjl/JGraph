namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>horzcat</c>, <c>vertcat</c> and <c>cat</c> along the first two dimensions as the bracket they
/// spell (open item 124): in MATLAB <c>horzcat(a, b)</c> is <c>[a, b]</c> and <c>vertcat(a, b)</c> is
/// <c>[a; b]</c>, so cells, char rows, structs, strings, objects and categoricals join by the bracket's
/// rules rather than the numeric verb's, which refused anything but numbers.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>
    /// The pieces joined as a bracket would join them: side by side when <paramref name="across"/>,
    /// else stacked one piece per row.
    /// </summary>
    internal JgsValue JoinAsBracket(IReadOnlyList<JgsValue> pieces, bool across, int line, int col)
    {
        JgsValue[] elements = [.. pieces];
        JgsValue built;
        if (across)
        {
            built = BuildArrayLiteral(new ArrayLiteral([]) { Line = line, Column = col }, elements, _globals);
        }
        else
        {
            var rows = new List<JgsValue[]>(elements.Length);
            foreach (JgsValue piece in elements)
            {
                rows.Add([piece]);
            }

            built = elements.Length == 0
                ? EmptyBracket()
                : AssembleMatrix(new MatrixLiteral([]) { Line = line, Column = col }, rows);
        }

        return WithTime(built, elements);
    }

    /// <summary>A grid of pieces joined as <c>[a b; c d]</c> joins them: each row side by side, the rows stacked.</summary>
    internal JgsValue JoinGrid(List<JgsValue[]> rows, int line, int col) =>
        rows.Count == 1
            ? JoinAsBracket(rows[0], across: true, line, col)
            : AssembleMatrix(new MatrixLiteral([]) { Line = line, Column = col }, rows);

    private static JgsValue WithTime(JgsValue built, JgsValue[] elements)
    {
        // A datetime joins as a datetime, as it does in the bracket (M82).
        if (!built.IsTime && elements.Length > 0 && elements[0].TimeTag is { } first
            && Array.TrueForAll(elements, e => e.TimeTag?.Kind == first.Kind))
        {
            built = built.MarkTime(JgsTime.ConcatTag(first, elements));
        }

        return built;
    }
}

internal static partial class JgsBuiltins
{
    /// <summary>
    /// Puts the bracket under <c>horzcat</c>, <c>vertcat</c> and <c>cat(1|2, …)</c> for MATLAB code
    /// (open item 124). Registered after every other wrapper of the three, so it is asked first; JGS
    /// code keeps the numeric verbs it has always had.
    /// </summary>
    internal static void RegisterBracketVerbs(JgsEnvironment env, Interpreter interpreter)
    {
        // cell2mat of a cell holding objects is the bracket over its grid (open item 121, probe_121):
        // {a; a} is a 2-by-3 array of objects, and a foreign piece goes through the constructor.
        if (env.Builtins.TryGet("cell2mat", out JgsValue cellToMat) && cellToMat.Type == JgsType.Function)
        {
            IJgsCallable plain = cellToMat.AsCallable;
            env.Builtins.Register("cell2mat", JgsValue.Function(new BuiltinFunction("cell2mat", (args, line, col) =>
            {
                if (!JgsRunningDialect.ThreadIsMatlab || args.Count != 1 || args[0].Type != JgsType.Cell
                    || !args[0].AsCell.Any(static e => e.Type == JgsType.Object) || args[0].IsNd)
                {
                    return plain.Call(args, line, col);
                }

                JgsValue[] cells = args[0].AsCell;
                int rows = Math.Max(args[0].Rows, 1);
                int cols = cells.Length / rows;
                var grid = new List<JgsValue[]>(rows);
                for (int r = 0; r < rows; r++)
                {
                    var row = new JgsValue[cols];
                    for (int c = 0; c < cols; c++)
                    {
                        row[c] = cells[r + (c * rows)];
                    }

                    grid.Add(row);
                }

                return interpreter.JoinGrid(grid, line, col);
            })));
        }

        foreach (string name in new[] { "horzcat", "vertcat", "cat" })
        {
            if (!env.Builtins.TryGet(name, out JgsValue existing) || existing.Type != JgsType.Function)
            {
                continue;
            }

            IJgsCallable inner = existing.AsCallable;
            JgsValue Join(IReadOnlyList<JgsValue> args, int line, int col)
            {
                if (!JgsRunningDialect.ThreadIsMatlab)
                {
                    return inner.Call(args, line, col);
                }

                switch (name)
                {
                    case "horzcat":
                        return interpreter.JoinAsBracket(args, across: true, line, col);
                    case "vertcat":
                        return interpreter.JoinAsBracket(args, across: false, line, col);
                    default:
                        if (args.Count >= 2 && args[0].Type == JgsType.Number && args[0].AsNumber is 1 or 2)
                        {
                            return interpreter.JoinAsBracket([.. args.Skip(1)], across: args[0].AsNumber == 2, line, col);
                        }

                        return inner.Call(args, line, col);
                }
            }

            var wrapper = new BuiltinFunction(name, Join)
            {
                BindsAnsAsStatement = inner is not BuiltinFunction { BindsAnsAsStatement: false },
                AutoCallsBare = inner is BuiltinFunction { AutoCallsBare: true },
                KnowsWhenDiscarded = inner is BuiltinFunction { KnowsWhenDiscarded: true },

                // The bracket reads a string as a string ("a" beside "b" is a 1-by-2 string); the
                // JGS road hands the arguments to the inner verb, which demotes them as it always did.
                KeepsStringArguments = true,
            };

            env.Builtins.Register(name, JgsValue.Function(wrapper));
        }
    }
}
