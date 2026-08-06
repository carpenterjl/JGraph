namespace JGraph.Scripting.Jgs;

/// <summary>
/// The four two-set operations — <c>union</c>, <c>intersect</c>, <c>setdiff</c>, <c>setxor</c> — plus
/// the outputs and option words <c>ismember</c> was missing, found by M52 wave E's audit of the
/// catalog against MATLAB's documented signatures.
/// </summary>
/// <remarks>
/// None of the four existed at all, which the coverage tables could not show: MATLAB documents them
/// with kind <c>function</c> rather than <c>builtin</c>, and the two tables that file keeps track
/// builtins and graphics functions. They are the natural siblings of <c>unique</c>, so they answer
/// the same four questions it does — what counts as the same value, whether whole rows are compared,
/// what order the answer comes back in, and how the index outputs are numbered — through the same
/// code, and cannot drift from it.
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the set operations and the fuller <c>ismember</c> into <paramref name="env"/>.</summary>
    private static void RegisterSetOperations(JgsEnvironment env, JgsDialect dialect)
    {
        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0]) { MultiOutput = both }));

        foreach (string name in new[] { "union", "intersect", "setdiff", "setxor" })
        {
            string self = name;
            DefineBoth(self, (args, outputs, line, col) => SetOperation(self, args, dialect, outputs, line, col));
        }

        DefineBoth("ismember", (args, outputs, line, col) => Membership(args, dialect, outputs, line, col));
    }

    // --- One side of a comparison -----------------------------------------------------------------

    /// <summary>
    /// One of the two sets, read as a list of keys. A key is a single value, or a whole row under
    /// <c>'rows'</c> — which is the only difference between the two forms, and the reason there is
    /// one implementation rather than two.
    /// </summary>
    private readonly record struct SetSide(
        JgsValue Source, JgsValue[][] Keys, JgsValue[] Elements, bool Cells, int Columns);

    private static SetSide SideOf(string name, JgsValue value, bool rows, int line, int col)
    {
        if (rows)
        {
            if (value.Type != JgsType.Array || value.IsNd)
            {
                throw new JgsRuntimeException(line, col,
                    $"{name}: 'rows' needs a matrix, which is the only shape that has rows to compare.");
            }

            int height = JgsMatrix.RowCount(value);
            int width = JgsMatrix.ColCount(value);
            var byRow = new JgsValue[height][];
            for (int r = 0; r < height; r++)
            {
                var key = new JgsValue[width];
                for (int c = 0; c < width; c++)
                {
                    key[c] = JgsMatrix.At(value, r, c);
                }

                byRow[r] = key;
            }

            return new SetSide(value, byRow, [], Cells: false, Columns: width);
        }

        // A cell of char is how a table hands over a text variable, so it is a set like any other.
        bool cells = value.Type == JgsType.Cell;
        JgsValue[] elements = cells ? value.AsCell : Arr(name, [value], 0, line, col);
        var single = new JgsValue[elements.Length][];
        for (int i = 0; i < elements.Length; i++)
        {
            single[i] = [elements[i]];
        }

        return new SetSide(value, single, elements, cells, Columns: -1);
    }

    /// <summary>
    /// Whether a key is in a side, and where: the earliest index holding it, or −1. A key holding a
    /// missing reading is in nothing, itself included, which is what keeps NaN out of every answer.
    /// </summary>
    private static Func<JgsValue[], int> Lookup(
        string name, SetSide side, int[] candidates, int line, int col)
    {
        int[] byKey = [.. candidates];

        // The index tiebreak is what makes "the earliest" a real answer rather than whichever equal
        // key the sort happened to leave in front — which is the index ismember has to report.
        Array.Sort(byKey, (a, b) =>
        {
            int order = CompareKeys(name, side.Keys[a], side.Keys[b], line, col);
            return order != 0 ? order : a.CompareTo(b);
        });

        return key =>
        {
            if (HasMissing(key))
            {
                return -1;
            }

            int low = 0;
            int high = byKey.Length;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (CompareKeys(name, side.Keys[byKey[mid]], key, line, col) < 0)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low < byKey.Length && CompareKeys(name, side.Keys[byKey[low]], key, line, col) == 0
                ? byKey[low]
                : -1;
        };
    }

    // --- The four operations ----------------------------------------------------------------------

    /// <summary>One value of the answer: its key, and which index it has on each side.</summary>
    private readonly record struct SetPick(JgsValue[] Key, int Left, int Right);

    /// <summary>
    /// <c>[C, ia, ib] = union|intersect|setdiff|setxor(A, B, …)</c>. The four differ only in which
    /// groups they keep; everything around that is shared.
    /// </summary>
    private static JgsValue[] SetOperation(
        string name, IReadOnlyList<JgsValue> args, JgsDialect dialect, int outputs, int line, int col)
    {
        ParsedArgs parsed = new OptionSpec(
            name, Flags: ["rows", "sorted", "stable"], Names: [], StringPositionals: 2)
            .Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, $"{name} compares two sets: {name}(A, B).");
        }

        bool stable = parsed.OneOf("sorted", "sorted", "stable") == "stable";
        bool rows = parsed.Has("rows");
        SetSide left = SideOf(name, parsed.Positional[0], rows, line, col);
        SetSide right = SideOf(name, parsed.Positional[1], rows, line, col);

        if (left.Cells != right.Cells)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: both sets must be the same kind — two arrays or two cells.");
        }

        if (rows && left.Columns != right.Columns)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: 'rows' compares whole rows, so both matrices need the same number of columns.");
        }

        (int[] leftGroups, _) = GroupDistinct(name, left.Keys, stable: true, last: false, line, col);
        (int[] rightGroups, _) = GroupDistinct(name, right.Keys, stable: true, last: false, line, col);
        Func<JgsValue[], int> inLeft = Lookup(name, left, leftGroups, line, col);
        Func<JgsValue[], int> inRight = Lookup(name, right, rightGroups, line, col);

        var picks = new List<SetPick>();
        foreach (int i in leftGroups)
        {
            int partner = inRight(left.Keys[i]);
            bool keep = name switch
            {
                "intersect" => partner >= 0,
                "setdiff" or "setxor" => partner < 0,
                _ => true, // union keeps everything A has
            };

            if (keep)
            {
                picks.Add(new SetPick(left.Keys[i], i, name == "intersect" ? partner : -1));
            }
        }

        if (name is "union" or "setxor")
        {
            foreach (int j in rightGroups)
            {
                if (inLeft(right.Keys[j]) < 0)
                {
                    picks.Add(new SetPick(right.Keys[j], -1, j));
                }
            }
        }

        // 'stable' means "in the order they appeared", which for the two operations that draw from
        // both sides is A's order and then B's — the order the picks were made in. Sorted is the
        // default, and is a sort of the answer rather than of either input.
        if (!stable)
        {
            picks.Sort((a, b) => CompareKeys(name, a.Key, b.Key, line, col));
        }

        JgsValue values = rows
            ? RowsOfPicks(name, picks, left.Columns, line, col)
            : ElementsOfPicks(picks, left, right);

        var fromLeft = new List<int>();
        var fromRight = new List<int>();
        foreach (SetPick pick in picks)
        {
            // intersect names both sides for every value; the others name whichever side it came from.
            if (pick.Left >= 0)
            {
                fromLeft.Add(pick.Left);
            }

            if (pick.Right >= 0)
            {
                fromRight.Add(pick.Right);
            }
        }

        return Outputs(
            outputs,
            values,
            IndexColumn([.. fromLeft], dialect),
            IndexColumn([.. fromRight], dialect));
    }

    private static JgsValue RowsOfPicks(string name, List<SetPick> picks, int columns, int line, int col)
    {
        var flat = new double[picks.Count * columns];
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < picks.Count; i++)
            {
                JgsValue element = picks[i].Key[c];
                flat[(c * picks.Count) + i] = element.Type is JgsType.Number or JgsType.Bool
                    ? element.AsNumber
                    : throw new JgsRuntimeException(line, col, $"{name}: 'rows' needs a numeric matrix.");
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, [picks.Count, columns]);
    }

    /// <summary>
    /// The picked values as an array or a cell. MATLAB hands back a row only when both sets were
    /// rows, and a column otherwise — which is why the answer's shape depends on both inputs rather
    /// than on the one a value happened to come from.
    /// </summary>
    private static JgsValue ElementsOfPicks(List<SetPick> picks, SetSide left, SetSide right)
    {
        var chosen = new JgsValue[picks.Count];
        for (int i = 0; i < picks.Count; i++)
        {
            chosen[i] = picks[i].Left >= 0 ? left.Elements[picks[i].Left] : right.Elements[picks[i].Right];
        }

        JgsValue values = left.Cells ? JgsValue.Cell(chosen) : JgsValue.Array(chosen);
        bool bothRows = !left.Cells
            && JgsMatrix.RowCount(left.Source) <= 1
            && JgsMatrix.RowCount(right.Source) <= 1;
        if (chosen.Length > 1 && !bothRows)
        {
            values.Reshape(chosen.Length, 1);
        }

        return values;
    }

    // --- ismember ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>[tf, loc] = ismember(A, B, 'rows')</c>: whether each value of A is in B, and where. The
    /// location output is the whole reason a script calls this rather than writing a loop, and it
    /// was the output that did not exist.
    /// </summary>
    private static JgsValue[] Membership(
        IReadOnlyList<JgsValue> args, JgsDialect dialect, int outputs, int line, int col)
    {
        ParsedArgs parsed = new OptionSpec(
            "ismember", Flags: ["rows"], Names: [], StringPositionals: 2)
            .Parse(args, 2, line, col);
        if (parsed.Positional.Count != 2)
        {
            throw new JgsRuntimeException(line, col, "ismember asks whether each value of A is in B: ismember(A, B).");
        }

        bool rows = parsed.Has("rows");

        // Without 'rows' this is elementwise over any shape, which the older implementation already
        // did for one output — including for an image and for a scalar — so it still answers that.
        if (!rows && outputs <= 1)
        {
            return [Ismember(parsed.Positional[0], parsed.Positional[1], line, col)];
        }

        SetSide subject = SideOf("ismember", parsed.Positional[0], rows, line, col);
        SetSide set = SideOf("ismember", parsed.Positional[1], rows, line, col);
        if (rows && subject.Columns != set.Columns)
        {
            throw new JgsRuntimeException(line, col,
                "ismember: 'rows' compares whole rows, so both matrices need the same number of columns.");
        }

        var all = new int[set.Keys.Length];
        for (int i = 0; i < all.Length; i++)
        {
            all[i] = i;
        }

        // The first occurrence is the one MATLAB reports, so the lookup runs over every row of B
        // rather than over its distinct groups.
        Func<JgsValue[], int> find = Lookup("ismember", set, all, line, col);

        var found = new double[subject.Keys.Length];
        var where = new double[subject.Keys.Length];
        for (int i = 0; i < subject.Keys.Length; i++)
        {
            int at = find(subject.Keys[i]);
            found[i] = at >= 0 ? 1 : 0;
            where[i] = at >= 0 ? at + dialect.IndexBase : dialect.IndexBase - 1;
        }

        int[] shape = rows ? [subject.Keys.Length, 1] : SizeDims(parsed.Positional[0]);
        return Outputs(
            outputs,
            MaskOf(found, shape),
            JgsMatrix.FromColumnMajorDims(where, shape));
    }

    /// <summary>The membership answers as logicals in the shape they were asked in.</summary>
    private static JgsValue MaskOf(double[] found, IReadOnlyList<int> dims)
    {
        var flags = new JgsValue[found.Length];
        for (int i = 0; i < found.Length; i++)
        {
            flags[i] = JgsValue.Bool(found[i] != 0);
        }

        return found.Length == 1 ? flags[0] : JgsMatrix.FromElementsDims(flags, dims);
    }
}
