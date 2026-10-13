namespace JGraph.Scripting.Jgs;

/// <summary>
/// Arrays of objects (open items 65 and 116): what <c>[s s2]</c> makes, what <c>a(2)</c> and
/// <c>b(1, 2)</c> pick, what <c>a(3) = s</c> grows and <c>a(2) = []</c> shrinks, the element
/// <c>a(1).p = v</c> writes, and the comma-separated list <c>a.p</c> names. R2025b's answers were
/// measured on <c>uistyle</c> objects (probe <c>probe_65</c>) and on value and handle classes of
/// the batch 19 scratch (<c>probe_116</c>, <c>probe_116b</c>); the roads are the struct array's
/// (M65) over a <see cref="JgsObjectArray"/> payload.
/// </summary>
/// <remarks>
/// A value class's elements are copied on write through either holder; a handle class's are the
/// objects themselves, so a write through one array is seen through every other and every alias.
/// A piece that is not of the array's class is converted through the class's constructor, as
/// R2025b converts it: <c>[s 5]</c> holds <c>Val116(5)</c>, and <c>[s sub]</c> holds
/// <c>Val116(sub)</c> whatever the two classes are to each other. A gap <c>a(5) = s</c> opens is
/// filled with the constructor's no-argument object, made once for a value class and once a slot
/// for a handle class.
/// </remarks>
internal sealed partial class Interpreter
{
    /// <summary>Whether any piece of a bracket literal is an object, one or an array of them.</summary>
    private static bool AnyObject(IReadOnlyList<JgsValue[]> rows)
    {
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (piece.Type == JgsType.Object)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Concatenates a bracket literal of objects: each row side by side, then the rows stacked. The
    /// class is the first object's; another object or a number, text, cell or struct beside it is
    /// converted through that class's constructor, and an empty <c>[]</c>, <c>{}</c> or <c>''</c> is
    /// left out. A <c>uistyle</c> keeps R2025b's heterogeneous refusal.
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

        var joinedRows = new List<(JgsObject[] Elements, int Rows, int Cols)>();
        foreach (IReadOnlyList<JgsValue> row in rows)
        {
            var across = new List<JgsObject>();
            int height = -1;
            int width = 0;
            foreach (JgsValue piece in row)
            {
                if (IsDroppedInBracket(piece))
                {
                    continue; // [], {} and '' join as nothing, as they do in every other bracket
                }

                JgsValue member = AsMemberOf(piece, definition, at, assigning: false);
                (JgsObject[] elements, int pieceRows, int pieceCols) = ElementsOf(member);
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
                    HoldElement(definition, element); // M2: the piece still holds it
                    stacked[next++] = element;
                }
            }
        }

        return JgsObjectArray.ValueOf(definition, stacked, total, cols);
    }

    /// <summary>An empty <c>[]</c>, <c>{}</c> or <c>''</c>, which a bracket leaves out; a string's empty is converted.</summary>
    private static bool IsDroppedInBracket(JgsValue piece) =>
        (piece.Type == JgsType.Array && piece.ArrayLength == 0 && !piece.IsStringArray && !piece.IsTime)
        || (piece.Type == JgsType.Cell && piece.AsCell.Length == 0)
        || (piece.Type == JgsType.String && piece.AsString.Length == 0);

    /// <summary>
    /// A piece as a member of an array of <paramref name="definition"/>: an object of that class
    /// as it is, anything else through the class's constructor, which is how R2025b converts a
    /// number, a struct, a cell or an object of another class (probe_116b: <c>[s 5]</c> holds
    /// <c>Val116(5)</c>, <c>[Sub116(2) Val116(1)]</c> holds <c>Sub116(Val116(1))</c>). The constructor's
    /// refusal is reported in R2025b's words for the bracket or the assignment.
    /// </summary>
    private JgsValue AsMemberOf(JgsValue piece, JgsClass definition, Node at, bool assigning)
    {
        if (piece.Type == JgsType.Object && ReferenceEquals(piece.ObjectClass, definition))
        {
            return piece;
        }

        if (definition.Name == JgsBuiltinClasses.Style)
        {
            throw piece.Type == JgsType.Object
                ? new JgsRuntimeException(at.Line, at.Column, "MATLAB:class:NoConversionDefined",
                    $"Cannot convert from '{piece.ClassName}' to '{definition.Name}'.")
                : CannotConvertToObjects(piece, definition, at);
        }

        string from = JgsBuiltins.ClassOf(piece, Dialect);
        try
        {
            return definition.Construct([piece], at.Line, at.Column);
        }
        catch (JgsRuntimeException refusal)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:UnableToConvert",
                assigning
                    ? $"Unable to perform assignment because value of type '{from}' is not convertible to '{definition.Name}'."
                    : $"The following error occurred converting from {from} to {definition.Name}: {refusal.Message}");
        }
    }

    /// <summary>R2025b's refusal of a number or text beside a <c>uistyle</c> in a bracket (probe_65, <c>[s 1]</c>).</summary>
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

    /// <summary>A second holder of a value-class element takes a counted share; a handle is the object itself.</summary>
    private static void HoldElement(JgsClass definition, JgsObject element)
    {
        if (!definition.IsHandle)
        {
            JgsHolders.Share(element);
        }
    }

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
        JgsClass definition = callee.ObjectClass;
        var positions = new JgsValue[elements.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            positions[i] = JgsValue.Number(i);
        }

        JgsValue standIn = elements.Length == 1 ? OneElementArray(positions[0]) : JgsMatrix.FromElements(positions, rows, cols);
        JgsValue picked = IndexInto(standIn, call.Arguments, call, env);
        if (picked.Type == JgsType.Number)
        {
            return ElementValue(definition, elements[(int)picked.AsNumber]);
        }

        int count = picked.ArrayLength;
        var chosen = new JgsObject[count];
        for (int i = 0; i < count; i++)
        {
            chosen[i] = elements[(int)picked.ElementAt(i).AsNumber];
            HoldElement(definition, chosen[i]); // the selection is a second holder
        }

        return JgsObjectArray.ValueOf(definition, chosen, picked.Rows, picked.Cols);
    }

    /// <summary>
    /// One element as a value of its own: a share of a value-class instance, so a write through
    /// either side copies first; a handle as itself.
    /// </summary>
    private static JgsValue ElementValue(JgsClass definition, JgsObject element) =>
        definition.IsHandle ? JgsValue.Object(element) : JgsValue.Share(JgsValue.Object(element));

    /// <summary>
    /// The property <paramref name="field"/> read across every element of an array of objects, in
    /// order: the comma-separated list <c>a.p</c> names (open item 116). A deleted handle among them
    /// refuses as a read of its own would.
    /// </summary>
    private JgsValue[] ObjectArrayFieldValues(JgsValue array, string field, MemberExpr member)
    {
        (JgsObject[] elements, int _, int _) = ElementsOf(array);
        JgsClass definition = array.ObjectClass;
        var gathered = new JgsValue[elements.Length];
        for (int i = 0; i < elements.Length; i++)
        {
            gathered[i] = ObjectMember(ElementValue(definition, elements[i]), field, member, autoCall: false);
        }

        return gathered;
    }

    /// <summary>
    /// What a dot names on an array of objects (open item 116): a method of the class is the method
    /// over the whole array, as <c>a.bump()</c> is <c>bump(a)</c>; a property is a comma-separated
    /// list, and where it stands decides its one value as a struct array's field does (open item 98).
    /// </summary>
    private JgsValue ObjectArrayMember(JgsValue target, string field, MemberExpr member, bool autoCall, ListUse use)
    {
        JgsClass definition = target.ObjectClass;
        MethodAnswer answer = FindMethod(definition, field, null, false, out ClassMethod? method);
        if (answer == MethodAnswer.Refused)
        {
            throw JgsClass.Restricted(field, method!.Owner ?? definition, member.Line, member.Column);
        }

        if (answer == MethodAnswer.Found && method is not null && !method.Static)
        {
            IJgsCallable body = field == "delete" && definition.IsHandle
                ? new ArrayDestructorCall(ElementsOf(target).Elements)
                : definition.Callable(method);
            var bound = new BoundMethod(body, target);
            return autoCall ? bound.Call([], member.Line, member.Column) : JgsValue.Function(bound);
        }

        if (definition.IsHandle && field is "delete" or "isvalid")
        {
            IJgsCallable inherited = field == "delete"
                ? new ArrayDestructorCall(ElementsOf(target).Elements)
                : new BuiltinFunction("isvalid", (args, line, col) => JgsBuiltins.IsValidObjects(target));
            var bound = new BoundMethod(inherited, target);
            return autoCall ? bound.Call([], member.Line, member.Column) : JgsValue.Function(bound);
        }

        JgsValue[] listed = ObjectArrayFieldValues(target, field, member);
        return OneOfList(listed.Length, listed.Length > 0 ? listed[0] : null, member, brace: false, use);
    }

    /// <summary>
    /// <c>h == g</c> with an array of handles on either side (open item 116): the one object on both
    /// sides counts as equal, a scalar expands, and a number or a value object is equal to none.
    /// </summary>
    private static JgsValue HandleArrayEquality(bool equal, JgsValue left, JgsValue right, Node at)
    {
        static (JgsObject[] Elements, int Rows, int Cols, int Count) Side(JgsValue value) => value.Type switch
        {
            JgsType.Object => (ElementsOf(value).Elements, ElementsOf(value).Rows, ElementsOf(value).Cols, ElementsOf(value).Elements.Length),
            JgsType.Array or JgsType.Cell => ([], value.Rows, value.Cols, Math.Max(1, value.ArrayLength)),
            _ => ([], 1, 1, 1),
        };
        (JgsObject[] a, int ar, int ac, int leftCount) = Side(left);
        (JgsObject[] b, int br, int bc, int rightCount) = Side(right);
        if (leftCount != 1 && rightCount != 1 && leftCount != rightCount)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:dimagree", "Matrix dimensions must agree.");
        }

        int count = Math.Max(leftCount, rightCount);
        (int rows, int cols) = leftCount >= rightCount ? (ar, ac) : (br, bc);
        if (rows * cols != count)
        {
            (rows, cols) = (1, count);
        }

        var flags = new JgsValue[count];
        for (int i = 0; i < count; i++)
        {
            JgsObject? one = a.Length == 0 ? null : a[leftCount == 1 ? 0 : i];
            JgsObject? other = b.Length == 0 ? null : b[rightCount == 1 ? 0 : i];
            bool same = one is not null && other is not null && ReferenceEquals(one, other);
            flags[i] = JgsValue.Bool(equal ? same : !same);
        }

        return count == 1 ? flags[0] : JgsMatrix.FromElements(flags, rows, cols);
    }

    /// <summary>
    /// <c>delete(h)</c> over an array of handles: each element's destructors in turn, as R2025b
    /// deletes every element (open item 116). An element already gone is passed over.
    /// </summary>
    private sealed class ArrayDestructorCall(JgsObject[] elements) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => "delete";

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column)
        {
            CallMultiple(arguments, 1, line, column);
            return JgsValue.Null;
        }

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
        {
            foreach (JgsObject element in elements)
            {
                new DestructorCall(element).CallMultiple([], 0, line, column);
            }

            return [];
        }
    }

    /// <summary>
    /// <c>a(k) = s</c> and <c>b(r, c) = s</c> replace elements, growing the array to reach a slot
    /// past its end with the constructor's no-argument objects; <c>a(k) = []</c> and
    /// <c>b(r, :) = []</c> delete them. Both rebuild the element list and store it back through the
    /// target's entry, as a struct array's writes do. A right-hand side of another kind is converted
    /// through the class's constructor (open item 116).
    /// </summary>
    private JgsValue AssignIntoObjectArray(
        Expr target, JgsValue existing, IReadOnlyList<Expr> subscripts, JgsValue rhs, Node at, JgsEnvironment env)
    {
        if (subscripts.Count is not (1 or 2))
        {
            throw new JgsRuntimeException(at.Line, at.Column,
                "An element of an array of objects is written with one or two subscripts.");
        }

        bool deleting = (rhs.Type == JgsType.Array && rhs.ArrayLength == 0 && !rhs.IsStringArray)
            || (rhs.Type == JgsType.String && rhs.AsString.Length == 0); // a(2) = '' deletes too (R2025b, probe_116)
        JgsClass definition = existing.Type == JgsType.Object ? existing.ObjectClass : rhs.ObjectClass;
        if (!deleting)
        {
            rhs = AsMemberOf(rhs, definition, at, assigning: true);
        }

        (JgsObject[] elements, int rows, int cols) = existing.Type == JgsType.Object ? ElementsOf(existing) : ([], 0, 0);
        bool shared = existing.Type == JgsType.Object && existing.IsShared;
        JgsValue written = subscripts.Count == 1
            ? AssignLinear(definition, elements, rows, cols, shared, deleting, subscripts[0], rhs, at, env)
            : AssignGrid(definition, elements, rows, cols, shared, deleting, subscripts[0], subscripts[1], rhs, at, env);
        StoreBack(target, written, at, env);
        return rhs;
    }

    /// <summary>The one-subscript write: a row or column is kept, a grid is written through its linear order.</summary>
    private JgsValue AssignLinear(
        JgsClass definition, JgsObject[] elements, int rows, int cols, bool shared, bool deleting,
        Expr subscript, JgsValue rhs, Node at, JgsEnvironment env)
    {
        bool column = cols == 1 && rows > 1;
        bool grid = rows > 1 && cols > 1;
        if (deleting)
        {
            JgsValue? doomedIndex = EvaluateIndexArgument(subscript, elements.Length, env);
            if (doomedIndex is null)
            {
                return JgsValue.ObjectArray(new JgsObjectArray(definition, []), 0, 0); // a(:) = []
            }

            int[] doomed = WritePicks(doomedIndex, elements.Length, at);
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
                        HoldElement(definition, elements[i]);
                    }

                    kept.Add(elements[i]);
                }
            }

            // A grid loses its shape to a linear deletion, as every array does.
            return JgsObjectArray.ValueOf(definition, [.. kept],
                column ? kept.Count : (kept.Count == 0 ? 0 : 1), column ? (kept.Count == 0 ? 0 : 1) : kept.Count);
        }

        int[] picks = WritePicks(EvaluateIndexArgument(subscript, elements.Length, env), elements.Length, at);
        (JgsObject[] source, int _, int _) = ElementsOf(rhs);
        if (source.Length != 1 && source.Length != picks.Length)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsassignnumelmismatch",
                "Unable to perform assignment because the left and right sides have a different number of elements.");
        }

        int needed = Math.Max(elements.Length, Highest(picks) + 1);
        if (grid && needed > elements.Length)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:index:growAmbiguous",
                "Attempt to grow array along ambiguous dimension.");
        }

        JgsObject[] grown = Grown(definition, elements, needed, at);
        var replaced = new bool[needed];
        for (int i = 0; i < picks.Length; i++)
        {
            grown[picks[i]] = source[source.Length == 1 ? 0 : i];
            HoldElement(definition, grown[picks[i]]); // M2: the right-hand side still holds it
            replaced[picks[i]] = true;
        }

        if (shared)
        {
            for (int i = 0; i < elements.Length; i++)
            {
                if (!replaced[i])
                {
                    HoldElement(definition, grown[i]); // in two arrays while the old one is held
                }
            }
        }

        return grid
            ? JgsObjectArray.ValueOf(definition, grown, rows, cols)
            : JgsObjectArray.ValueOf(definition, grown, column ? needed : 1, column ? 1 : needed);
    }

    /// <summary>
    /// The two-subscript write (open item 116): <c>b(r, c) = s</c> on a grid grown to reach the
    /// farthest row and column named, the gaps filled with default objects; <c>b(r, :) = []</c>
    /// deletes rows and <c>b(:, c) = []</c> columns, and anything else is R2025b's refusal.
    /// </summary>
    private JgsValue AssignGrid(
        JgsClass definition, JgsObject[] elements, int rows, int cols, bool shared, bool deleting,
        Expr rowSubscript, Expr colSubscript, JgsValue rhs, Node at, JgsEnvironment env)
    {
        if (elements.Length == 1 && rows == 0)
        {
            rows = 1;
            cols = 1;
        }

        JgsValue? rowIndex = EvaluateIndexArgument(rowSubscript, rows, env);
        JgsValue? colIndex = EvaluateIndexArgument(colSubscript, cols, env);
        if (deleting)
        {
            if (rowIndex is not null && colIndex is not null)
            {
                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsdeldimmismatch",
                    "A null assignment can have only one non-colon index.");
            }

            var dropRows = new HashSet<int>(rowIndex is null ? [] : WritePicks(rowIndex, rows, at));
            var dropCols = new HashSet<int>(colIndex is null ? [] : WritePicks(colIndex, cols, at));
            if (dropRows.Any(r => r >= rows) || dropCols.Any(c => c >= cols))
            {
                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsdeldimmismatch", "Index exceeds array bounds.");
            }

            int keptRows = rows - dropRows.Count;
            int keptCols = cols - dropCols.Count;
            var kept = new List<JgsObject>(keptRows * keptCols);
            for (int c = 0; c < cols; c++)
            {
                if (dropCols.Contains(c))
                {
                    continue;
                }

                for (int r = 0; r < rows; r++)
                {
                    if (dropRows.Contains(r))
                    {
                        continue;
                    }

                    JgsObject element = elements[r + (c * rows)];
                    if (shared)
                    {
                        HoldElement(definition, element);
                    }

                    kept.Add(element);
                }
            }

            return JgsValue.ObjectArray(new JgsObjectArray(definition, [.. kept]), keptRows, keptCols);
        }

        int[] rowPicks = rowIndex is null ? [.. Enumerable.Range(0, Math.Max(rows, 1))] : WritePicks(rowIndex, rows, at);
        int[] colPicks = colIndex is null ? [.. Enumerable.Range(0, Math.Max(cols, 1))] : WritePicks(colIndex, cols, at);
        int newRows = Math.Max(rows, Highest(rowPicks) + 1);
        int newCols = Math.Max(cols, Highest(colPicks) + 1);
        (JgsObject[] source, int _, int _) = ElementsOf(rhs);
        if (source.Length != 1 && source.Length != rowPicks.Length * colPicks.Length)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsassignnumelmismatch",
                "Unable to perform assignment because the left and right sides have a different number of elements.");
        }

        // The grid is laid out anew when it grows, the old elements keeping their row and column.
        var grown = new JgsObject[newRows * newCols];
        var filled = new bool[newRows * newCols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                grown[r + (c * newRows)] = elements[r + (c * rows)];
                filled[r + (c * newRows)] = true;
            }
        }

        JgsObject? filler = null;
        for (int i = 0; i < grown.Length; i++)
        {
            if (!filled[i])
            {
                grown[i] = GapFiller(definition, ref filler, at);
            }
        }

        var replaced = new bool[grown.Length];
        int taken = 0;
        foreach (int c in colPicks)
        {
            foreach (int r in rowPicks)
            {
                int slot = r + (c * newRows);
                grown[slot] = source[source.Length == 1 ? 0 : taken];
                HoldElement(definition, grown[slot]);
                replaced[slot] = true;
                taken++;
            }
        }

        if (shared)
        {
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    int slot = r + (c * newRows);
                    if (!replaced[slot])
                    {
                        HoldElement(definition, grown[slot]);
                    }
                }
            }
        }

        return JgsObjectArray.ValueOf(definition, grown, newRows, newCols);
    }

    /// <summary>The element list grown to <paramref name="needed"/>, the gap filled with default objects.</summary>
    private JgsObject[] Grown(JgsClass definition, JgsObject[] elements, int needed, Node at)
    {
        var grown = new JgsObject[needed];
        Array.Copy(elements, grown, elements.Length);
        JgsObject? filler = null;
        for (int i = elements.Length; i < needed; i++)
        {
            grown[i] = GapFiller(definition, ref filler, at);
        }

        return grown;
    }

    /// <summary>
    /// The object a gap is filled with: the constructor called with no arguments (R2025b calls it
    /// once for every gap of a value class and shares the copies, and once a slot for a handle
    /// class; probe_116). A constructor that needs an argument refuses here as it would anywhere.
    /// </summary>
    private JgsObject GapFiller(JgsClass definition, ref JgsObject? filler, Node at)
    {
        if (definition.IsHandle)
        {
            return definition.Construct([], at.Line, at.Column).AsObject;
        }

        if (filler is null)
        {
            filler = definition.Construct([], at.Line, at.Column).AsObject;
            return filler;
        }

        JgsHolders.Share(filler);
        return filler;
    }

    /// <summary>
    /// The element <c>a(k).p = v</c> writes, made writable inside the array the variable holds, the
    /// array grown with default objects to reach a slot past its end. A handle's element is the
    /// handle itself, so the write lands on the object every alias shares. Null when the variable
    /// holds no object, so the other roads answer.
    /// </summary>
    private JgsValue? ResolveObjectElementForWrite(VariableExpr variable, Expr subscript, Node at, JgsEnvironment env)
    {
        if (!LookUp(variable.Name, env, out JgsValue existing) || existing.Type != JgsType.Object)
        {
            return null;
        }

        (JgsObject[] elements, int rows, int cols) = ElementsOf(existing);
        JgsValue? index = EvaluateIndexArgument(subscript, elements.Length, env);
        if (index is null || index.Type != JgsType.Number || index.AsNumber != Math.Floor(index.AsNumber))
        {
            // a(:).p = v and a(1:2).p = v are R2025b's refusal; [a(:).p] = deal(v) is the road.
            int named = index is null ? elements.Length : index.ArrayLength;
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:index:expected_one_output_for_assignment",
                $"Assigning to {named} elements using a simple assignment statement is not supported. Consider using comma-separated list assignment.");
        }

        int slot = (int)index.AsNumber - Dialect.IndexBase;
        if (slot < 0)
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:badsubscript",
                "Index in position 1 is invalid. Array indices must be positive integers or logical values.");
        }

        JgsClass definition = existing.ObjectClass;
        if (definition.IsHandle && !existing.IsObjectArray && slot == 0)
        {
            return null; // one handle's own dots are its own road
        }

        JgsValue array = existing;
        if (slot >= elements.Length)
        {
            JgsObject[] grown = Grown(definition, elements, slot + 1, at);
            if (existing.IsShared)
            {
                for (int i = 0; i < elements.Length; i++)
                {
                    HoldElement(definition, grown[i]);
                }
            }

            bool column = cols == 1 && rows > 1;
            array = JgsValue.ObjectArray(new JgsObjectArray(definition, grown), column ? grown.Length : 1, column ? 1 : grown.Length);
            Rebind(variable.Name, array, env);
        }

        if (!array.IsObjectArray)
        {
            return array; // one object: the variable's own wrapper, which the property roads detach
        }

        if (definition.IsHandle)
        {
            return JgsValue.Object(array.AsObjectArray.Elements[slot]); // the object itself, for every alias
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
        JgsClass definition = iterable.ObjectClass;
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
                bound = ElementValue(definition, elements[column]);
            }
            else
            {
                var slice = new JgsObject[rows];
                for (int r = 0; r < rows; r++)
                {
                    slice[r] = elements[r + (column * rows)];
                    HoldElement(definition, slice[r]);
                }

                bound = JgsObjectArray.ValueOf(definition, slice, rows, 1);
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
