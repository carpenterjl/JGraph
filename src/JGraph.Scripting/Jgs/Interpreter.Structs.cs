namespace JGraph.Scripting.Jgs;

/// <summary>
/// Struct arrays (M65): reading, writing, growing and deleting elements of a
/// <see cref="JgsType.Struct"/> value that is not a 1-by-1.
/// </summary>
/// <remarks>
/// Until M65 a struct array was a cell whose elements happened all to be structs, recognised by
/// scanning it. Everything here exists because that inference could not tell a struct array from a
/// cell a script had built by hand, and could not keep the invariant MATLAB gives the type: every
/// element has every field.
/// </remarks>
internal sealed partial class Interpreter
{
    /// <summary>
    /// <c>S(k)</c>, <c>S(2:3)</c>, <c>S(mask)</c> and <c>S(:)</c> over a struct array. The result is
    /// a struct array of the picked elements — a 1-by-1 one when a single element is named, which is
    /// the value <c>S(k).f</c> then reads its field out of.
    /// </summary>
    private JgsValue IndexStruct(JgsValue target, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        JgsStructArray payload = target.AsStructArray;
        if (subscripts.Count == 2)
        {
            int rows = target.Rows;
            int cols = target.Cols;
            int[] extents = [rows, cols];
            int[] rowPicks = StructPicks(EvaluateIndexArgument(subscripts[0], extents, 0, env), rows, "row", at);
            int[] colPicks = StructPicks(EvaluateIndexArgument(subscripts[1], extents, 1, env), cols, "column", at);
            var grid = new Dictionary<string, JgsValue>[rowPicks.Length * colPicks.Length];
            int next = 0;
            foreach (int pickedColumn in colPicks)
            {
                foreach (int row in rowPicks)
                {
                    grid[next++] = payload.Elements[row + (pickedColumn * rows)];
                }
            }

            return JgsValue.StructArray(
                new JgsStructArray(grid, payload.EmptyFields), rowPicks.Length, colPicks.Length);
        }

        if (subscripts.Count != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Indexing a struct array takes one subscript or two (a row and a column).");
        }

        int[] picks = StructPicks(
            EvaluateIndexArgument(subscripts[0], payload.Length, env), payload.Length, "struct array", at);
        var chosen = new Dictionary<string, JgsValue>[picks.Length];
        for (int i = 0; i < picks.Length; i++)
        {
            chosen[i] = payload.Elements[picks[i]];
        }

        // A selection out of a column stays a column, as it does for every other container.
        bool column = target.Cols == 1 && target.Rows > 1;
        return JgsValue.StructArray(
            new JgsStructArray(chosen, payload.EmptyFields),
            column ? picks.Length : (picks.Length == 0 ? 0 : 1),
            column ? (picks.Length == 0 ? 0 : 1) : picks.Length);
    }

    /// <summary>The slots one subscript names: ':' is all of them, an array gathers or masks.</summary>
    private int[] StructPicks(JgsValue? index, int extent, string what, Node at)
    {
        if (index is null)
        {
            var all = new int[extent];
            for (int i = 0; i < extent; i++)
            {
                all[i] = i;
            }

            return all;
        }

        return index.Type == JgsType.Array
            ? ComputePicks(index, extent, what, at.Line, at.Column)
            : [ToIndex(index, extent, at.Line, at.Column)];
    }

    /// <summary>
    /// One field across every element of a struct array, in order — the comma-separated list
    /// <c>stats.Area</c> names.
    /// </summary>
    /// <remarks>
    /// This is still the one place that knows how a struct array is stored, which is what M61
    /// arranged it for: when the representation changed in M65, only this method's body moved.
    /// </remarks>
    private JgsValue[] StructArrayFieldValues(JgsValue array, string field, Node member)
    {
        JgsStructArray payload = array.AsStructArray;
        var gathered = new JgsValue[payload.Length];
        for (int i = 0; i < gathered.Length; i++)
        {
            if (!payload.Elements[i].TryGetValue(field, out JgsValue? value))
            {
                throw new JgsRuntimeException(member.Line, member.Column,
                    $"Element {i + Dialect.IndexBase} of this struct array has no field '{field}'.");
            }

            gathered[i] = value;
        }

        return gathered;
    }

    /// <summary>
    /// One field read across every element of a struct array, as a single value — <c>stats.Area</c>
    /// where one value is wanted rather than a list.
    /// </summary>
    /// <remarks>
    /// A row array when every field is a number, a cell otherwise. In an argument list or a bracket
    /// the field spreads instead (M61); this is what the same expression means where a list has no
    /// room to go, so <c>x = stats.Area</c> yields the row rather than the first element.
    /// </remarks>
    private JgsValue StructArrayField(JgsValue array, string field, Node member)
    {
        JgsValue[] gathered = StructArrayFieldValues(array, field, member);
        bool allNumbers = true;
        foreach (JgsValue value in gathered)
        {
            allNumbers &= value.Type is JgsType.Number or JgsType.Bool;
        }

        if (!allNumbers)
        {
            return JgsValue.Cell(gathered);
        }

        var numbers = new double[gathered.Length];
        for (int i = 0; i < gathered.Length; i++)
        {
            numbers[i] = gathered[i].AsNumber;
        }

        return NumbersOf(numbers);
    }

    /// <summary>
    /// Deletes the elements a subscript names from a struct array — <c>S(2) = []</c>. The survivors
    /// keep their order, and a row stays a row.
    /// </summary>
    private JgsValue DeleteStructElements(JgsValue target, IReadOnlyList<Expr> subscripts, Node at, JgsEnvironment env)
    {
        JgsStructArray payload = target.AsStructArray;
        if (subscripts.Count != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Deleting from a struct array takes one subscript.");
        }

        int[] doomed = StructPicks(
            EvaluateIndexArgument(subscripts[0], payload.Length, env), payload.Length, "struct array", at);
        var drop = new HashSet<int>(doomed);
        var kept = new List<Dictionary<string, JgsValue>>(payload.Length - drop.Count);
        for (int i = 0; i < payload.Length; i++)
        {
            if (!drop.Contains(i))
            {
                kept.Add(payload.Elements[i]);
            }
        }

        bool column = target.Cols == 1 && target.Rows > 1;
        return JgsValue.StructArray(
            new JgsStructArray([.. kept], payload.EmptyFields),
            column ? kept.Count : (kept.Count == 0 ? 0 : 1),
            column ? (kept.Count == 0 ? 0 : 1) : kept.Count);
    }

    /// <summary>
    /// <c>S(k) = []</c> deletes elements; <c>S(k) = other</c> replaces them with another struct's.
    /// Both rebuild the element list, so the target has to be a plain name to rebind.
    /// </summary>
    private JgsValue AssignIntoStruct(
        Expr target, JgsValue existing, IReadOnlyList<Expr> subscripts, JgsValue rhs, Node at, JgsEnvironment env)
    {
        if (target is not VariableExpr variable)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Writing an element of a struct array needs a plain variable to write back to.");
        }

        bool deleting = rhs.Type == JgsType.Array && rhs.ArrayLength == 0;
        if (!deleting && rhs.Type != JgsType.Struct)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"An element of a struct array takes a struct or [], not a {rhs.TypeName}.");
        }

        JgsValue written;
        if (deleting)
        {
            written = DeleteStructElements(existing, subscripts, at, env);
        }
        else
        {
            JgsStructArray payload = existing.AsStructArray;
            int[] picks = StructPicks(
                EvaluateIndexArgument(
                    Single(subscripts, at, "A struct-array index"), payload.Length, env),
                payload.Length, "struct array", at);
            JgsStructArray source = rhs.AsStructArray;
            if (source.Length != 1 && source.Length != picks.Length)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"Writing {picks.Length} elements needs 1 or {picks.Length} on the right, not {source.Length}.");
            }

            var elements = (Dictionary<string, JgsValue>[])payload.Elements.Clone();
            for (int i = 0; i < picks.Length; i++)
            {
                elements[picks[i]] = source.Elements[source.Length == 1 ? 0 : i];
            }

            var rebuilt = new JgsStructArray(elements, payload.EmptyFields);
            foreach (string field in source.FieldNames)
            {
                rebuilt.EnsureField(field);
            }

            written = JgsValue.StructArray(rebuilt, existing.Rows, existing.Cols);
        }

        Rebind(variable.Name, written, env);
        return rhs;
    }

    /// <summary>Runs a <c>for</c> whose loop expression is a struct array, element by element.</summary>
    private Completion ExecuteForOverStructs(ForStmt statement, JgsValue iterable, JgsEnvironment env)
    {
        JgsStructArray payload = iterable.AsStructArray;
        for (int index = 0; index < payload.Length; index++)
        {
            Tick();
            JgsEnvironment local = BlockScope(env);
            local.Declare(statement.Variable, CopyForBinding(JgsValue.Struct(payload.Elements[index])));
            Completion completion = ExecuteBlock(statement.Body, local);
            if (completion.Kind == CompletionKind.Break)
            {
                break;
            }

            if (completion.Kind == CompletionKind.Return)
            {
                return completion;
            }
        }

        return Completion.Normal;
    }

    /// <summary>Whether any piece of a bracket literal is a struct.</summary>
    private static bool AnyStruct(List<JgsValue[]> rows)
    {
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (piece.Type == JgsType.Struct)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Concatenates a bracket literal of struct values — <c>[s1 s2]</c>, <c>[s1; s2]</c> and the
    /// two together. Each row is joined side by side and the rows are then stacked, which is the
    /// order the brackets themselves say and the only order that gets <c>[S; S]</c> of two 1-by-3
    /// arrays to a 2-by-3 rather than a column of six.
    /// </summary>
    private JgsValue ConcatenateStructs(IReadOnlyList<IReadOnlyList<JgsValue>> rows, Node at)
    {
        var joined = new List<JgsValue>(rows.Count);
        foreach (IReadOnlyList<JgsValue> row in rows)
        {
            joined.Add(JoinStructsAcross(row, at));
        }

        return StackStructRows(joined, at);
    }

    /// <summary>
    /// One row of a bracket: struct values side by side. Every piece must be a struct with the same
    /// number of rows, and the fields are unioned so the result keeps the invariant that every
    /// element has every field. Column-major storage makes this an append — a column of the second
    /// piece follows every column of the first.
    /// </summary>
    private JgsValue JoinStructsAcross(IReadOnlyList<JgsValue> pieces, Node at)
    {
        var elements = new List<Dictionary<string, JgsValue>>();
        var fields = new List<string>();
        int rows = -1;
        int cols = 0;
        foreach (JgsValue piece in pieces)
        {
            if (piece.Type != JgsType.Struct)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"A struct can only be concatenated with another struct, not with a {piece.TypeName}.");
            }

            JgsStructArray payload = piece.AsStructArray;
            foreach (string field in payload.FieldNames)
            {
                if (!fields.Contains(field))
                {
                    fields.Add(field);
                }
            }

            // An empty struct array contributes its fields and no shape, the way [] does in a
            // numeric bracket: [S, struct('a', {})] is S.
            if (payload.Length == 0)
            {
                continue;
            }

            if (rows < 0)
            {
                rows = piece.Rows;
            }
            else if (piece.Rows != rows)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"Struct arrays joined side by side must have the same number of rows, not {rows} and {piece.Rows}.");
            }

            elements.AddRange(payload.Elements);
            cols += piece.Cols;
        }

        return BuildStructArray(elements, fields, rows < 0 ? 0 : rows, rows < 0 ? 0 : cols);
    }

    /// <summary>
    /// The rows of a bracket stacked one above another. Every row must be the same width, and the
    /// elements interleave rather than append, because storage is column-major: the first column of
    /// the answer is the first column of every row in turn.
    /// </summary>
    private JgsValue StackStructRows(IReadOnlyList<JgsValue> rows, Node at)
    {
        var kept = new List<JgsValue>(rows.Count);
        var fields = new List<string>();
        int cols = -1;
        int height = 0;
        foreach (JgsValue row in rows)
        {
            JgsStructArray payload = row.AsStructArray;
            foreach (string field in payload.FieldNames)
            {
                if (!fields.Contains(field))
                {
                    fields.Add(field);
                }
            }

            if (payload.Length == 0)
            {
                continue;
            }

            if (cols < 0)
            {
                cols = row.Cols;
            }
            else if (row.Cols != cols)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"Struct arrays stacked one above another must have the same number of columns, not {cols} and {row.Cols}.");
            }

            kept.Add(row);
            height += row.Rows;
        }

        if (kept.Count == 1)
        {
            return kept[0];
        }

        var elements = new List<Dictionary<string, JgsValue>>(height * System.Math.Max(cols, 0));
        for (int column = 0; column < cols; column++)
        {
            foreach (JgsValue row in kept)
            {
                JgsStructArray payload = row.AsStructArray;
                for (int r = 0; r < row.Rows; r++)
                {
                    elements.Add(payload.Elements[r + (column * row.Rows)]);
                }
            }
        }

        return BuildStructArray(elements, fields, cols < 0 ? 0 : height, cols < 0 ? 0 : cols);
    }

    /// <summary>The struct array those elements make, with every field present on every one of them.</summary>
    private static JgsValue BuildStructArray(
        List<Dictionary<string, JgsValue>> elements, List<string> fields, int rows, int cols)
    {
        var built = new JgsStructArray([.. elements], [.. fields]);
        foreach (string field in fields)
        {
            built.EnsureField(field);
        }

        return JgsValue.StructArray(built, rows, cols);
    }
}
