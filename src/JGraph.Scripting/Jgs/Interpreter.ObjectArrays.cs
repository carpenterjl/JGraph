namespace JGraph.Scripting.Jgs;

/// <summary>
/// Arrays of value-class objects (open item 65): what <c>[s s2]</c> makes, what <c>a(2)</c> picks,
/// what <c>a(3) = s</c> grows and <c>a(2) = []</c> shrinks, and the element <c>a(1).p = v</c>
/// writes. R2025b's answers were measured on <c>uistyle</c> objects (probe <c>probe_65</c>); the
/// roads are the struct array's (M65) over a <see cref="JgsObjectArray"/> payload.
/// </summary>
internal sealed partial class Interpreter
{
    /// <summary>
    /// Whether any piece of a bracket literal is a value-class object, one or an array. A handle
    /// object keeps the road it had (a boxed array of handles, released in index order).
    /// </summary>
    private static bool AnyObject(IReadOnlyList<JgsValue[]> rows)
    {
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (IsValueObject(piece))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>An object of a value class, one or an array of them.</summary>
    private static bool IsValueObject(JgsValue piece) => piece.Type == JgsType.Object && !piece.ObjectClass.IsHandle;

    /// <summary>
    /// Concatenates a bracket literal of objects: each row side by side, then the rows stacked.
    /// Every piece is an object of the one class or an empty <c>[]</c>, which is left out; anything
    /// else is R2025b's <c>NoConversionDefined</c>. A handle object is refused: only value classes
    /// make arrays here.
    /// </summary>
    private JgsValue ConcatenateObjects(IReadOnlyList<IReadOnlyList<JgsValue>> rows, Node at)
    {
        JgsClass? definition = null;
        foreach (IReadOnlyList<JgsValue> row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (piece.Type == JgsType.Object)
                {
                    definition ??= piece.ObjectClass;
                }
            }
        }

        if (definition is null)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "A bracket of objects needs an object.");
        }

        if (definition.IsHandle)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Arrays of handle objects are not supported yet: an array of '{definition.Name}' cannot be made.");
        }

        var joinedRows = new List<(JgsObject[] Elements, int Rows, int Cols)>();
        foreach (IReadOnlyList<JgsValue> row in rows)
        {
            var across = new List<JgsObject>();
            int height = -1;
            int width = 0;
            foreach (JgsValue piece in row)
            {
                if (piece.Type == JgsType.Array && piece.ArrayLength == 0 && !piece.IsStringArray && !piece.IsTime)
                {
                    continue; // [] joins as nothing, as it does in every other bracket
                }

                if (piece.Type != JgsType.Object)
                {
                    throw CannotConvertToObjects(piece, definition, at);
                }

                if (!ReferenceEquals(piece.ObjectClass, definition))
                {
                    throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:class:NoConversionDefined",
                        $"Cannot convert from '{piece.ClassName}' to '{definition.Name}'.");
                }

                (JgsObject[] elements, int pieceRows, int pieceCols) = ElementsOf(piece);
                if (elements.Length == 0)
                {
                    continue;
                }

                if (height < 0)
                {
                    height = pieceRows;
                }
                else if (pieceRows != height)
                {
                    throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:catenate:dimensionMismatch",
                        "Dimensions of arrays being concatenated are not consistent.");
                }

                across.AddRange(elements);
                width += pieceCols;
            }

            if (height > 0)
            {
                joinedRows.Add(([.. across], height, width));
            }
        }

        // The rows stack: column-major storage interleaves them, the first column of the answer
        // being the first column of every row in turn.
        int cols = -1;
        int total = 0;
        foreach ((JgsObject[] _, int _, int rowCols) in joinedRows)
        {
            if (cols < 0)
            {
                cols = rowCols;
            }
            else if (rowCols != cols)
            {
                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:catenate:dimensionMismatch",
                    "Dimensions of arrays being concatenated are not consistent.");
            }
        }

        foreach ((JgsObject[] _, int rowRows, int _) in joinedRows)
        {
            total += rowRows;
        }

        if (cols < 0)
        {
            return JgsValue.ObjectArray(new JgsObjectArray(definition, []), 0, 0);
        }

        var stacked = new JgsObject[total * cols];
        int next = 0;
        for (int column = 0; column < cols; column++)
        {
            foreach ((JgsObject[] elements, int rowRows, int _) in joinedRows)
            {
                for (int r = 0; r < rowRows; r++)
                {
                    JgsObject element = elements[r + (column * rowRows)];
                    JgsHolders.Share(element); // M2: the piece still holds it
                    stacked[next++] = element;
                }
            }
        }

        return JgsObjectArray.ValueOf(definition, stacked, total, cols);
    }

    /// <summary>R2025b's refusal of a number or text beside an object in a bracket (probe_65, <c>[s 1]</c>).</summary>
    private JgsRuntimeException CannotConvertToObjects(JgsValue piece, JgsClass definition, Node at)
    {
        string from = JgsBuiltins.ClassOf(piece, Dialect);
        string text = definition.Name == JgsBuiltinClasses.Style
            ? $"Cannot convert from '{from}' to heterogeneous class '{definition.Name}'. Either 'matlab.ui.style.internal.ComponentStyle' does not define static method 'convertObject' or its 'convertObject' method does not support converting '{from}' to '{definition.Name}'."
            : $"Cannot convert from '{from}' to '{definition.Name}'.";
        return new JgsRuntimeException(at.Line, at.Column, "MATLAB:class:NoConversionDefined", text);
    }

    /// <summary>An object value's elements and shape: one for a lone object, the array's for an array.</summary>
    private static (JgsObject[] Elements, int Rows, int Cols) ElementsOf(JgsValue value) =>
        value.IsObjectArray
            ? (value.AsObjectArray.Elements, value.Rows, value.Cols)
            : ([value.AsObject], 1, 1);

    /// <summary>
    /// A paren subscript of an object or an array of them. The subscripts are applied to a stand-in
    /// array of the element positions, so <c>end</c>, a colon, a mask and every refusal
    /// (<c>s(2)</c>, <c>s(0)</c>) are exactly what an array of that shape answers; the positions
    /// picked are then the elements, shared out of the array (M2).
    /// </summary>
    private JgsValue IndexObject(JgsValue callee, CallExpr call, JgsEnvironment env)
    {
        if (call.Arguments.Count == 0)
        {
            return callee;
        }

        (JgsObject[] elements, int rows, int cols) = ElementsOf(callee);
        var positions = new JgsValue[elements.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = JgsValue.Number(i);
        }

        JgsValue standIn = elements.Length == 1 ? OneElementArray(positions[0]) : JgsMatrix.FromElements(positions, rows, cols);
        JgsValue picked = IndexInto(standIn, call.Arguments, call, env);
        if (picked.Type == JgsType.Number)
        {
            return ElementValue(elements[(int)picked.AsNumber]);
        }

        int count = picked.ArrayLength;
        var chosen = new JgsObject[count];
        for (int i = 0; i < count; i++)
        {
            chosen[i] = elements[(int)picked.ElementAt(i).AsNumber];
            JgsHolders.Share(chosen[i]); // the selection is a second holder
        }

        return JgsObjectArray.ValueOf(callee.ObjectClass, chosen, picked.Rows, picked.Cols);
    }

    /// <summary>One element as a value of its own: a share of the instance, so a write through either side copies first.</summary>
    private static JgsValue ElementValue(JgsObject element) => JgsValue.Share(JgsValue.Object(element));

    /// <summary>
    /// <c>a(k) = s</c> replaces elements, growing the array to reach a slot past its end with
    /// default objects of the class (R2025b fills the gap with base-class objects; here they are the
    /// class's own defaults); <c>a(k) = []</c> deletes them. Both rebuild the element list and store
    /// it back through the target's entry, as a struct array's writes do.
    /// </summary>
    private JgsValue AssignIntoObjectArray(
        Expr target, JgsValue existing, IReadOnlyList<Expr> subscripts, JgsValue rhs, Node at, JgsEnvironment env)
    {
        if (subscripts.Count != 1)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "An element of an array of objects is written with one subscript; a row and a column are not supported yet.");
        }

        bool deleting = rhs.Type == JgsType.Array && rhs.ArrayLength == 0 && !rhs.IsStringArray;
        JgsClass definition = existing.Type == JgsType.Object ? existing.ObjectClass : rhs.ObjectClass;
        if (!deleting && (rhs.Type != JgsType.Object || !ReferenceEquals(rhs.ObjectClass, definition)))
        {
            throw rhs.Type == JgsType.Object
                ? new JgsRuntimeException(at.Line, at.Column, "MATLAB:class:NoConversionDefined",
                    $"Cannot convert from '{rhs.ClassName}' to '{definition.Name}'.")
                : CannotConvertToObjects(rhs, definition, at);
        }

        if (definition.IsHandle)
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                $"Arrays of handle objects are not supported yet: an array of '{definition.Name}' cannot be made.");
        }

        (JgsObject[] elements, int rows, int cols) = existing.Type == JgsType.Object ? ElementsOf(existing) : ([], 0, 0);
        bool column = cols == 1 && rows > 1;
        bool shared = existing.Type == JgsType.Object && existing.IsShared;
        JgsValue written;
        if (deleting)
        {
            int[] doomed = WritePicks(EvaluateIndexArgument(subscripts[0], elements.Length, env), elements.Length, at);
            var drop = new HashSet<int>(doomed);
            foreach (int slot in drop)
            {
                if (slot >= elements.Length)
                {
                    throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsdeldimmismatch",
                        "Index exceeds array bounds.");
                }
            }

            var kept = new List<JgsObject>(elements.Length);
            for (int i = 0; i < elements.Length; i++)
            {
                if (!drop.Contains(i))
                {
                    if (shared)
                    {
                        JgsHolders.Share(elements[i]);
                    }

                    kept.Add(elements[i]);
                }
            }

            written = JgsObjectArray.ValueOf(definition, [.. kept],
                column ? kept.Count : (kept.Count == 0 ? 0 : 1), column ? (kept.Count == 0 ? 0 : 1) : kept.Count);
        }
        else
        {
            int[] picks = WritePicks(EvaluateIndexArgument(subscripts[0], elements.Length, env), elements.Length, at);
            (JgsObject[] source, int _, int _) = ElementsOf(rhs);
            if (source.Length != 1 && source.Length != picks.Length)
            {
                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsassignnumelmismatch",
                    "Unable to perform assignment because the left and right sides have a different number of elements.");
            }

            int needed = Math.Max(elements.Length, Highest(picks) + 1);
            var grown = new JgsObject[needed];
            Array.Copy(elements, grown, elements.Length);
            for (int i = elements.Length; i < needed; i++)
            {
                grown[i] = definition.NewDefault(at.Line, at.Column);
            }

            var replaced = new bool[needed];
            for (int i = 0; i < picks.Length; i++)
            {
                grown[picks[i]] = source[source.Length == 1 ? 0 : i];
                JgsHolders.Share(grown[picks[i]]); // M2: the right-hand side still holds it
                replaced[picks[i]] = true;
            }

            if (shared)
            {
                for (int i = 0; i < elements.Length; i++)
                {
                    if (!replaced[i])
                    {
                        JgsHolders.Share(grown[i]); // in two arrays while the old one is held
                    }
                }
            }

            written = JgsObjectArray.ValueOf(definition, grown, column ? needed : 1, column ? 1 : needed);
        }

        StoreBack(target, written, at, env);
        return rhs;
    }

    /// <summary>
    /// The element <c>a(k).p = v</c> writes, made writable inside the array the variable holds, the
    /// array grown with default objects to reach a slot past its end. Null when the variable holds
    /// no object, so the other roads answer.
    /// </summary>
    private JgsValue? ResolveObjectElementForWrite(VariableExpr variable, Expr subscript, Node at, JgsEnvironment env)
    {
        if (!LookUp(variable.Name, env, out JgsValue existing) || existing.Type != JgsType.Object)
        {
            return null;
        }

        (JgsObject[] elements, int rows, int cols) = ElementsOf(existing);
        JgsValue index = EvaluateIndexArgument(subscript, elements.Length, env)
            ?? throw new JgsRuntimeException(at.Line, at.Column,
                "An element of an array of objects is named by one whole-number subscript, not ':'.");
        if (index.Type != JgsType.Number || index.AsNumber != Math.Floor(index.AsNumber))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "An element of an array of objects is named by one whole-number subscript.");
        }

        int slot = (int)index.AsNumber - Dialect.IndexBase;
        if (slot < 0)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:badsubscript",
                "Index in position 1 is invalid. Array indices must be positive integers or logical values.");
        }

        JgsClass definition = existing.ObjectClass;
        if (definition.IsHandle)
        {
            return null; // a handle's dots are its own; a handle array is outside this build
        }

        JgsValue array = existing;
        if (slot >= elements.Length)
        {
            var grown = new JgsObject[slot + 1];
            Array.Copy(elements, grown, elements.Length);
            if (existing.IsShared)
            {
                for (int i = 0; i < elements.Length; i++)
                {
                    JgsHolders.Share(grown[i]);
                }
            }

            for (int i = elements.Length; i < grown.Length; i++)
            {
                grown[i] = definition.NewDefault(at.Line, at.Column);
            }

            bool column = cols == 1 && rows > 1;
            array = JgsValue.ObjectArray(new JgsObjectArray(definition, grown), column ? grown.Length : 1, column ? 1 : grown.Length);
            Rebind(variable.Name, array, env);
        }

        if (!array.IsObjectArray)
        {
            return array; // one object: the variable's own wrapper, which the property roads detach
        }

        // M3/M7: the element is detached inside the array it belongs to, so the wrapper handed back
        // writes where the write was aimed rather than into a copy of its own.
        return JgsValue.Object(array.WritableObjectArray().WritableElement(slot));
    }

    /// <summary>
    /// Runs a <c>for</c> over an array of objects: a row element by element, anything taller a
    /// column at a time, as MATLAB walks every array.
    /// </summary>
    private Completion ExecuteForOverObjects(ForStmt statement, JgsValue iterable, JgsEnvironment env)
    {
        (JgsObject[] elements, int rows, int cols) = ElementsOf(iterable);
        if (elements.Length == 0)
        {
            return BindZeroTripVariable(statement, env);
        }

        for (int column = 0; column < cols; column++)
        {
            Tick();
            JgsValue bound;
            if (rows == 1)
            {
                bound = ElementValue(elements[column]);
            }
            else
            {
                var slice = new JgsObject[rows];
                for (int r = 0; r < rows; r++)
                {
                    slice[r] = elements[r + (column * rows)];
                    JgsHolders.Share(slice[r]);
                }

                bound = JgsObjectArray.ValueOf(iterable.ObjectClass, slice, rows, 1);
            }

            JgsEnvironment local = BlockScope(env);
            local.Declare(statement.Variable, bound);
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
}
