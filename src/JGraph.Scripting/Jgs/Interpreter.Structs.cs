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
                    // M2 (appendix A #12): the selection is a second holder of the target's
                    // own element dictionaries, so a later write through either detaches.
                    Dictionary<string, JgsValue> picked = payload.Elements[row + (pickedColumn * rows)];
                    JgsHolders.Share(picked);
                    grid[next++] = picked;
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
            JgsHolders.Share(chosen[i]); // M2 (#12): the selection is a second holder
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
    /// <c>S(k) = []</c> deletes elements; <c>S(k) = other</c> replaces them with another struct's,
    /// growing the array to reach a slot past its end. Both rebuild the element list and store it
    /// back through the target's entry.
    /// </summary>
    /// <remarks>
    /// M14 (#76, #86, #87): the struct written in must carry exactly the array's fields, in any
    /// order — R2025b refuses anything else as <c>MATLAB:heterogeneousStrucAssignment</c> before an
    /// element is replaced or a field added, and an empty array with fields (<c>struct('a', {})</c>)
    /// is held to its fields the same way. The count and the fields are checked before the array
    /// is touched, so a refusal leaves it as it was.
    /// </remarks>
    private JgsValue AssignIntoStruct(
        Expr target, JgsValue existing, IReadOnlyList<Expr> subscripts, JgsValue rhs, Node at, JgsEnvironment env)
    {
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
            int[] picks = WritePicks(
                EvaluateIndexArgument(
                    Single(subscripts, at, "A struct-array index"), payload.Length, env),
                payload.Length, at);
            JgsStructArray source = rhs.AsStructArray;
            if (source.Length != 1 && source.Length != picks.Length)
            {
                throw new JgsRuntimeException(at.Line, at.Column,
                    $"Writing {picks.Length} elements needs 1 or {picks.Length} on the right, not {source.Length}.");
            }

            if (!SameFieldSet(payload.FieldNames, source.FieldNames))
            {
                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:heterogeneousStrucAssignment",
                    "Subscripted assignment between dissimilar structures.");
            }

            int needed = Math.Max(payload.Length, Highest(picks) + 1);
            var elements = new Dictionary<string, JgsValue>[needed];
            Array.Copy(payload.Elements, elements, payload.Length);
            for (int i = payload.Length; i < needed; i++)
            {
                elements[i] = payload.NewElement(); // growth fills the gap with the array's fields, each []
            }

            var replaced = new bool[elements.Length];
            for (int i = 0; i < picks.Length; i++)
            {
                elements[picks[i]] = source.Elements[source.Length == 1 ? 0 : i];

                // M2: the right-hand side still holds what was written in, and one element written
                // into several slots is held by each of them.
                JgsHolders.Share(elements[picks[i]]);
                replaced[picks[i]] = true;
            }

            // The elements that stayed are in two arrays only while the old one is still held.
            if (existing.IsShared)
            {
                for (int i = 0; i < payload.Length; i++)
                {
                    if (!replaced[i])
                    {
                        JgsHolders.Share(elements[i]);
                    }
                }
            }

            var rebuilt = new JgsStructArray(elements, payload.EmptyFields);
            bool column = existing.Cols == 1 && existing.Rows > 1;
            written = JgsValue.StructArray(rebuilt,
                column ? needed : (needed == 0 ? 0 : 1), column ? (needed == 0 ? 0 : 1) : needed);
            written.SetClassName(existing.ClassName);
        }

        StoreBack(target, written, at, env);
        return rhs;
    }

    /// <summary>
    /// <c>b(r, c) = s</c> (V6): one struct into the element a row and a column name. A slot past an
    /// edge grows the array to the rectangle that holds it, the gap filled with elements carrying
    /// the array's fields, each <c>[]</c>. The fields are held to the array's as on the
    /// one-subscript road (M14), and an array started from <c>[]</c> takes the fields written in.
    /// </summary>
    private JgsValue AssignIntoStructGrid(
        Expr target, JgsValue existing, IReadOnlyList<Expr> subscripts, JgsValue rhs, Node at, JgsEnvironment env)
    {
        JgsStructArray source = rhs.AsStructArray;
        JgsStructArray payload = existing.Type == JgsType.Struct
            ? existing.AsStructArray
            : new JgsStructArray([], source.FieldNames);
        int rows = existing.Type == JgsType.Struct ? existing.Rows : 0;
        int cols = existing.Type == JgsType.Struct ? existing.Cols : 0;
        int[] extents = [rows, cols];
        if (!TryOneSubscript(subscripts[0], extents, 0, env, out int row)
            || !TryOneSubscript(subscripts[1], extents, 1, env, out int column))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "A struct written by a row and a column names one element; write several with one subscript.");
        }

        if (!SameFieldSet(payload.FieldNames, source.FieldNames))
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:heterogeneousStrucAssignment",
                "Subscripted assignment between dissimilar structures.");
        }

        int newRows = Math.Max(rows, row + 1);
        int newCols = Math.Max(cols, column + 1);
        var elements = new Dictionary<string, JgsValue>[newRows * newCols];
        bool shared = existing.Type == JgsType.Struct && existing.IsShared;
        for (int c = 0; c < newCols; c++)
        {
            for (int r = 0; r < newRows; r++)
            {
                int slot = r + (c * newRows);
                if (r == row && c == column)
                {
                    elements[slot] = source.Elements[0];
                    JgsHolders.Share(elements[slot]); // M2: the right-hand side still holds it
                }
                else if (r < rows && c < cols)
                {
                    elements[slot] = payload.Elements[r + (c * rows)];
                    if (shared)
                    {
                        JgsHolders.Share(elements[slot]); // in two arrays while the old one is held
                    }
                }
                else
                {
                    elements[slot] = payload.NewElement();
                }
            }
        }

        JgsValue written = JgsValue.StructArray(new JgsStructArray(elements, payload.EmptyFields), newRows, newCols);
        StoreBack(target, written, at, env);
        return rhs;
    }

    /// <summary>Whether two field lists name the same fields, in any order.</summary>
    private static bool SameFieldSet(string[] left, string[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        foreach (string field in right)
        {
            if (Array.IndexOf(left, field) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Runs a <c>for</c> whose loop expression is a struct array, element by element.</summary>
    private Completion ExecuteForOverStructs(ForStmt statement, JgsValue iterable, JgsEnvironment env)
    {
        JgsStructArray payload = iterable.AsStructArray;
        if (payload.Length == 0)
        {
            return BindZeroTripVariable(statement, env);
        }

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
            JoinFields(fields, payload.FieldNames, at);

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
            JoinFields(fields, payload.FieldNames, at);

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

    /// <summary>
    /// The fields a bracket's next struct piece brings to the join. MATLAB refuses a piece whose
    /// field set differs from the first's (in any order), and refuses it before anything is built,
    /// so the pieces are left as they were (M8, <c>b_horzcat_failing</c>); JGS unions the fields.
    /// </summary>
    private void JoinFields(List<string> fields, string[] brought, Node at)
    {
        if (Dialect.IsMatlab && fields.Count > 0 && !SameFieldSet([.. fields], brought))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "Names of fields in structure arrays being concatenated do not match. "
                + "Concatenation of structure arrays requires that these arrays have the same set of fields.");
        }

        foreach (string field in brought)
        {
            if (!fields.Contains(field))
            {
                fields.Add(field);
            }
        }
    }

    /// <summary>
    /// The struct array those elements make, with every field present on every one of them. The
    /// pieces' element dictionaries are shared into the answer (M2), and one that lacks a field the
    /// union has is replaced by a shared copy carrying it, so no piece is changed by being joined.
    /// </summary>
    private static JgsValue BuildStructArray(
        List<Dictionary<string, JgsValue>> elements, List<string> fields, int rows, int cols)
    {
        var joined = new Dictionary<string, JgsValue>[elements.Count];
        for (int i = 0; i < joined.Length; i++)
        {
            Dictionary<string, JgsValue> element = elements[i];
            bool complete = true;
            foreach (string field in fields)
            {
                complete &= element.ContainsKey(field);
            }

            if (complete)
            {
                JgsHolders.Share(element);
                joined[i] = element;
                continue;
            }

            Dictionary<string, JgsValue> widened = JgsStructArray.SharedCopy(element);
            foreach (string field in fields)
            {
                widened.TryAdd(field, JgsValue.Array([]));
            }

            joined[i] = widened;
        }

        return JgsValue.StructArray(new JgsStructArray(joined, [.. fields]), rows, cols);
    }
}
