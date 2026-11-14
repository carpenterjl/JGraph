namespace JGraph.Scripting.Jgs;

/// <summary>
/// ADR 0354 (open items 285, 286 and the engine half of 208): what an index write with two
/// subscripts or more accepts as its right side, a struct array written through a grid, and the
/// indexing R2025b does with a subscript that is not a number.
/// </summary>
/// <remarks>
/// <para>
/// A write with several subscripts selects a block, and R2025b fits the right side to the block by
/// its extents, not by its count: the right side's dimensions that are not 1, in order, must be the
/// block's dimensions that are not 1. <c>A(2:3, 2:3) = 1:4</c> has the count and not the extents, and
/// is refused; <c>A(:, 1) = reshape(1:5, 1, 1, 5)</c> into a 5-by-1 and a 1-by-2-by-3 into a 2-by-3
/// have them both. A block of nothing takes any empty. JGraph checked the count alone and wrote
/// column-major, so a script R2025b refuses ran here and filled the block in another order.
/// </para>
/// <para>
/// One rule, five roads: the numeric two- and N-subscript writes, the cell ones and the char row
/// written with a row and a column. The struct array (open item 286) rides the numeric road whole,
/// through a stand-in of its positions, so its growth, deletion and refusals are an array's.
/// </para>
/// </remarks>
internal sealed partial class Interpreter
{
    /// <summary>R2025b's words for a misfit written into the shapeless 0-by-0, whose block it could not size.</summary>
    private const string IndicesNotCompatible =
        "Unable to perform assignment because the indices on the left side are not compatible with the size of the right side.";

    /// <summary>
    /// The fit test of a write with two subscripts or more (MATLAB dialect): the right side's count
    /// is the block's, and its non-singleton extents are the block's in order unless the block is
    /// empty. A misfit is <c>MATLAB:subsassigndimmismatch</c>, naming both sizes, or the shapeless
    /// target's own sentence when the target was the 0-by-0 (measured: <c>A = []; A(1:2, 1:3) = 1:6</c>).
    /// </summary>
    private static void RefuseMisfit(int[] selection, int[] rhsDims, bool shapelessTarget, Node at)
    {
        long selected = 1;
        foreach (int extent in selection)
        {
            selected *= extent;
        }

        long given = 1;
        foreach (int extent in rhsDims)
        {
            given *= extent;
        }

        if (given == selected
            && (selected == 0 || selection.Where(static d => d != 1).SequenceEqual(rhsDims.Where(static d => d != 1))))
        {
            return;
        }

        throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsassigndimmismatch", shapelessTarget
            ? IndicesNotCompatible
            : "Unable to perform assignment because the size of the left side is " + SizeWords(LeftSideDims(selection))
                + " and the size of the right side is " + SizeWords(rhsDims) + ".");
    }

    /// <summary>The block's size as R2025b names it: trailing ones past the second dimension dropped, zeros kept.</summary>
    private static int[] LeftSideDims(int[] selection)
    {
        int kept = selection.Length;
        while (kept > 2 && selection[kept - 1] == 1)
        {
            kept--;
        }

        return selection[..kept];
    }

    private static string SizeWords(int[] dims) => string.Join("-by-", dims);

    /// <summary>A right side's dimensions: a char row is 1-by-n, anything else its own.</summary>
    private static int[] WrittenDims(JgsValue rhs) => rhs.Type switch
    {
        JgsType.String when !rhs.IsStringArray => [1, rhs.AsString.Length],
        JgsType.Array or JgsType.Cell or JgsType.Struct => JgsMatrix.DimsOf(rhs),
        _ => [1, 1],
    };

    /// <summary>
    /// The rows and columns <c>A(:, :) = v</c> takes on the shapeless 0-by-0 when <c>v</c> has a third
    /// dimension: its extents that are not 1, padded with 1 (R2025b: a 1-by-2-by-3 makes a 2-by-3 and
    /// a 1-by-1-by-3 a 3-by-1). Null for a right side of two dimensions, which gives its own, and for
    /// one with three extents that are not 1, which then meets the refusal.
    /// </summary>
    private static (int Rows, int Cols)? ShapelessColonExtents(JgsValue rhs)
    {
        if (rhs.Type is not (JgsType.Array or JgsType.Cell))
        {
            return null;
        }

        int[] dims = JgsMatrix.DimsOf(rhs);
        if (dims.Length <= 2)
        {
            return null;
        }

        int[] spread = [.. dims.Where(static d => d != 1)];
        return spread.Length switch
        {
            0 => (1, 1),
            1 => (spread[0], 1),
            2 => (spread[0], spread[1]),
            _ => null,
        };
    }

    /// <summary>Whether a value is the shapeless 0-by-0 a write takes its extents from.</summary>
    private static bool IsShapelessTarget(JgsValue target) => target.Dims is [0, 0];

    /// <summary>
    /// R2025b's refusal of a brace (or one field over several elements) written by a plain
    /// assignment: the list has <paramref name="count"/> places and the statement fills one.
    /// </summary>
    private static JgsRuntimeException AssigningToSeveral(int count, Node at) =>
        new(at.Line, at.Column, "MATLAB:index:expected_one_output_for_assignment",
            $"Assigning to {count} elements using a simple assignment statement is not supported. "
            + "Consider using comma-separated list assignment.");

    /// <summary>R2025b's deletion with a subscript past the end (measured: <c>A = zeros(2); A(3, :) = []</c>).</summary>
    private void RefuseDeletionPastEnd(JgsValue index, int extent, Node at)
    {
        if (!Dialect.IsMatlab || IsLogicalIndex(index))
        {
            return;
        }

        JgsValue[] named = index.Type == JgsType.Array ? index.BoxedElements() : [index];
        foreach (JgsValue position in named)
        {
            if (position.Type is JgsType.Number or JgsType.Bool && position.AsNumber > extent)
            {
                throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:subsdeldimmismatch",
                    "Matrix index is out of range for deletion.");
            }
        }
    }

    /// <summary>R2025b's deletion with two subscripts that are not colons.</summary>
    private static JgsRuntimeException NullAssignmentOneNonColon(Node at) =>
        new(at.Line, at.Column, "MATLAB:badnullassign", "A null assignment can have only one non-colon index.");

    // --- subscripts that are not numbers (open item 208) -------------------------------------------

    /// <summary>
    /// A subscript as R2025b reads it in a position (MATLAB dialect): a char is its character codes
    /// (<c>x('a')</c> is <c>x(97)</c>, in its own shape), and a string, a cell, a struct, a function
    /// handle or a time is <c>MATLAB:matrix:unableToUseTypeAsIndex</c>. Everything else is handed
    /// back as it came. A table's subscripts are names and do not come here.
    /// </summary>
    private JgsValue PositionalIndex(JgsValue index, Node at)
    {
        if (index.IsStringArray || index.Type == JgsType.Cell || index.Type == JgsType.Function || index.IsTime
            || (index.Type == JgsType.Struct && index.ClassName is null && !JgsBuiltins.IsKeyedCollection(index)))
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:matrix:unableToUseTypeAsIndex",
                $"Unable to use a value of type {JgsBuiltins.ClassOf(index, Dialect)} as an index.");
        }

        if (index.Type == JgsType.String)
        {
            string text = index.AsString;
            if (text.Length == 1)
            {
                return JgsValue.Number(text[0]);
            }

            var codes = new double[text.Length];
            for (int i = 0; i < codes.Length; i++)
            {
                codes[i] = text[i];
            }

            return text.Length == 0 ? JgsMatrix.FromColumnMajor(codes, 0, 0) : JgsMatrix.FromColumnMajor(codes, 1, codes.Length);
        }

        if (index.IsCharMatrix)
        {
            var codes = new double[index.ArrayLength];
            for (int i = 0; i < codes.Length; i++)
            {
                codes[i] = index.ElementAt(i).AsNumber;
            }

            return JgsMatrix.FromColumnMajorDims(codes, index.Dims);
        }

        return index;
    }

    /// <summary>
    /// <c>T(rows, vars)</c>'s subscripts: a table's are names as well as positions, so they are not
    /// read as character codes.
    /// </summary>
    private JgsValue? EvaluateNamedIndexArgument(Expr argument, int[] extents, int slot, JgsEnvironment env) =>
        EvaluateIndexArgument(argument, new IndexContext(extents, null, slot), env, named: true);

    /// <summary>
    /// Whether <c>x()</c> is the value itself (MATLAB dialect, measured: <c>x()</c> and <c>s()</c> are
    /// <c>x</c> and <c>s</c>, so <c>s().a</c> is <c>s.a</c>): data, not a callable, a keyed collection
    /// or an interpolant, which answer an empty call in their own ways.
    /// </summary>
    private bool IsEmptyCallTheValue(JgsValue callee) =>
        Dialect.IsMatlab
        && (callee.Type is JgsType.Array or JgsType.String or JgsType.Number or JgsType.Bool or JgsType.Complex
                or JgsType.Cell or JgsType.Sparse
            || (callee.Type == JgsType.Struct && callee.ClassName is null
                && !JgsBuiltins.IsKeyedCollection(callee) && !JgsBuiltins.IsInterpolant(callee)));

    /// <summary>
    /// <c>s.a = v</c> on a struct array, with R2025b's identifier: an empty one is
    /// <c>MATLAB:emptyDotAssignment</c> and any other <c>MATLAB:scalarStrucRequired</c> (measured).
    /// </summary>
    private static JgsRuntimeException ScalarStructRequired(JgsValue structs, Node at) =>
        structs.AsStructArray.Length == 0
            ? new(at.Line, at.Column, "MATLAB:emptyDotAssignment",
                "A dot name structure assignment is illegal when the structure is empty.  Use a subscript on the structure.")
            : new(at.Line, at.Column, "MATLAB:scalarStrucRequired", "Scalar structure required for this assignment.");

    /// <summary>A field write's refusal, R2025b's dot-indexing one carrying its identifier (<c>MATLAB:structAssToNonStruct</c>).</summary>
    private static JgsRuntimeException FieldWriteRefusal(Node at, string message) =>
        string.Equals(message, DotNotSupported, StringComparison.Ordinal)
            ? new(at.Line, at.Column, "MATLAB:structAssToNonStruct", message)
            : new(at.Line, at.Column, message);

    // --- struct arrays written through a grid (open item 286) --------------------------------------

    /// <summary>
    /// Whether a write with two subscripts or more is a struct array's (MATLAB dialect): into a plain
    /// struct, or a plain struct into the <c>[]</c> one starts from.
    /// </summary>
    private bool IsStructGridWrite(JgsValue callee, JgsValue rhs, TokenType op) =>
        Dialect.IsMatlab && op == TokenType.Assign
        && ((callee.Type == JgsType.Struct && callee.ClassName is null
                && !JgsBuiltins.IsKeyedCollection(callee) && !JgsBuiltins.IsInterpolant(callee))
            || (rhs.Type == JgsType.Struct && rhs.ClassName is null
                && callee.Type == JgsType.Array && callee.ArrayLength == 0 && !callee.IsStringArray && !callee.IsTime));

    /// <summary>
    /// <c>s(r, c) = t</c>, <c>s(:, 1) = t</c>, <c>s(i, j, k) = t</c> and <c>s(r, :) = []</c> on a struct
    /// array, or on the <c>[]</c> one starts from (open item 286): the write is made on a numeric
    /// stand-in of the positions (1 onward, the right side's after them), so the fit, the growth, the
    /// deletion and every refusal are the numeric road's; a 0 the growth left is an element with the
    /// array's fields, each <c>[]</c>. The fields are held to the array's first (M14), as on the
    /// one-subscript road.
    /// </summary>
    private JgsValue AssignStructByStandIn(
        Expr target, JgsValue existing, IReadOnlyList<Expr> subscripts, JgsValue rhs, Node at, JgsEnvironment env)
    {
        bool deleting = IsDeletion(rhs);
        if (!deleting && !(rhs.Type == JgsType.Struct && rhs.ClassName is null))
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:heterogeneousAssignment",
                "Assignment between unlike types is not allowed.");
        }

        JgsStructArray? source = deleting ? null : rhs.AsStructArray;
        JgsStructArray payload = existing.Type == JgsType.Struct
            ? existing.AsStructArray
            : new JgsStructArray([], source!.FieldNames);
        if (source is not null && !SameFieldSet(payload.FieldNames, source.FieldNames))
        {
            throw new JgsRuntimeException(at.Line, at.Column, "MATLAB:heterogeneousStrucAssignment",
                "Subscripted assignment between dissimilar structures.");
        }

        int count = payload.Length;
        JgsValue standIn = existing.Type == JgsType.Struct
            ? PositionsFrom(existing.Dims, count, 1)
            : JgsMatrix.FromColumnMajor([], 0, 0);

        // An empty struct on the right is not the [] that deletes: it is written as the 1-by-0 of
        // the same count, and a misfit is reported with its own size.
        bool emptySource = source is { Length: 0 } && rhs.Dims is [0, 0];
        JgsValue markers = deleting ? JgsMatrix.FromColumnMajor([], 0, 0)
            : emptySource ? JgsMatrix.FromColumnMajor([], 1, 0)
            : source!.Length == 1 ? JgsValue.Number(count + 1)
            : PositionsFrom(rhs.Dims, source.Length, count + 1);

        var scratch = new JgsEnvironment(env);
        scratch.Declare(LevelSlot, standIn);
        var slot = new VariableExpr(LevelSlot) { Line = at.Line, Column = at.Column };
        var inner = new AssignExpr(new CallExpr(slot, subscripts) { Line = at.Line, Column = at.Column }, TokenType.Assign,
            new PreEvaluated(markers) { Line = at.Line, Column = at.Column })
        {
            Line = at.Line,
            Column = at.Column,
        };

        try
        {
            EvaluateAssign(inner, scratch);
        }
        catch (JgsRuntimeException misfit) when (emptySource && misfit.Identifier == "MATLAB:subsassigndimmismatch")
        {
            throw new JgsRuntimeException(misfit.Line, misfit.Column, misfit.Identifier,
                misfit.Message.Replace("right side is 1-by-0.", "right side is 0-by-0.", StringComparison.Ordinal));
        }

        scratch.TryGet(LevelSlot, out JgsValue written);
        bool many = written.Type == JgsType.Array;
        int total = many ? written.ArrayLength : 1;
        var elements = new Dictionary<string, JgsValue>[total];
        bool shared = existing.Type == JgsType.Struct && existing.IsShared;
        for (int i = 0; i < total; i++)
        {
            int position = (int)(many ? written.ElementAt(i) : written).AsNumber;
            if (position == 0)
            {
                elements[i] = payload.NewElement(); // a gap the growth left: the array's fields, each []
            }
            else if (position <= count)
            {
                elements[i] = payload.Elements[position - 1];
                if (shared)
                {
                    JgsHolders.Share(elements[i]); // in two arrays while the old one is held
                }
            }
            else
            {
                elements[i] = source!.Elements[position - count - 1];
                JgsHolders.Share(elements[i]); // M2: the right-hand side still holds it
            }
        }

        JgsValue result = JgsValue.StructArray(new JgsStructArray(elements, payload.EmptyFields),
            many ? written.Rows : 1, many ? written.Cols : 1);
        if (many && written.IsNd)
        {
            result.ReshapeDims(written.Dims);
        }

        StoreBack(target, result, at, env);
        return rhs;
    }

    /// <summary>
    /// <c>s'</c> on a two-dimensional struct array: the elements in their turned places, each now in
    /// a second array and so taking a share (M2).
    /// </summary>
    private static JgsValue TransposedStructs(JgsValue structs)
    {
        JgsStructArray payload = structs.AsStructArray;
        int rows = structs.Rows;
        int cols = structs.Cols;
        var stood = new Dictionary<string, JgsValue>[payload.Length];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                stood[c + (r * cols)] = payload.Elements[r + (c * rows)];
                JgsHolders.Share(stood[c + (r * cols)]);
            }
        }

        return JgsValue.StructArray(new JgsStructArray(stood, payload.EmptyFields), cols, rows);
    }

    /// <summary><paramref name="offset"/> onward, <paramref name="count"/> of them, laid out in <paramref name="dims"/>.</summary>
    private static JgsValue PositionsFrom(int[] dims, int count, int offset)
    {
        if (count == 1)
        {
            return JgsValue.Number(offset);
        }

        var positions = new double[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = offset + i;
        }

        return JgsMatrix.FromColumnMajorDims(positions, dims);
    }
}
