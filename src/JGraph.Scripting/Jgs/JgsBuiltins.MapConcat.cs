namespace JGraph.Scripting.Jgs;

/// <summary>
/// A bracket holding a <c>containers.Map</c> (open item 317, ADR 0358, probes p317, p317b, p317c):
/// R2025b's Map class has its own <c>horzcat</c>, <c>vertcat</c> and <c>cat</c>. Side by side is
/// <c>MATLAB:Containers:Map:HorzcatNotSupported</c>; stacked is a new map, a copy of the first with
/// every entry of the others written into it in turn, so a later key wins and each key and value is
/// converted to the first map's types or refused as <c>MATLAB:Containers:TypeMismatch</c>. A piece
/// that is not a map after the first is the method's argument validation refusing it; a first piece
/// that is not a map is R2025b's failed conversion of it through the Map constructor. Leading empties
/// are left out, and a map alone after them is that map.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Whether a MATLAB bracket holds a containers.Map anywhere.</summary>
    internal static bool AnyMatlabMap(IReadOnlyList<JgsValue[]> rows)
    {
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                if (IsMatlabMapShape(piece, out _))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary><c>MATLAB:Containers:Map:IncorrectIndexing</c>: what a <c>for</c> loop over a map meets.</summary>
    internal static JgsRuntimeException MapIncorrectIndexing(int line, int col) =>
        new(line, col, "MATLAB:Containers:Map:IncorrectIndexing", "Only one-dimensional indexing is supported.");

    /// <summary>A bracket with a map in it: each row side by side, then the rows stacked.</summary>
    internal static JgsValue JoinMaps(IReadOnlyList<JgsValue[]> rows, int line, int col)
    {
        var stacked = new JgsValue[rows.Count];
        for (int r = 0; r < rows.Count; r++)
        {
            stacked[r] = rows[r].Length == 1 ? rows[r][0] : JoinMapsAlong(rows[r], vertical: false, line, col);
        }

        return stacked.Length == 1 ? stacked[0] : JoinMapsAlong(stacked, vertical: true, line, col);
    }

    /// <summary>
    /// <c>vertcat(m)</c>, <c>cat(1, m)</c> and <c>horzcat(m)</c> of one map: the method runs, so the
    /// stacking verbs answer a new map with the same entries (measured: <c>vertcat(m) == m</c> is false).
    /// </summary>
    internal static JgsValue JoinOneMap(JgsValue map, bool vertical, int line, int col) =>
        vertical ? MergeMaps([map], line, col) : throw HorzcatNotSupported(line, col);

    /// <summary>
    /// <c>cat(dim, …)</c> with a map among the pieces (probe p317e): the method takes every piece as a
    /// map, empties included (<c>cat(3, [], m)</c> refuses argument 2), then refuses a dimension past
    /// the second; along 2 one map is itself and more are refused, along 1 they merge (one is copied).
    /// </summary>
    internal static JgsValue CatMaps(IReadOnlyList<JgsValue> args, int line, int col)
    {
        for (int i = 1; i < args.Count; i++)
        {
            if (!IsMatlabMapShape(args[i], out _))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:RequireClass",
                    $"Invalid input for argument {i + 1} (varargin): Value must be 'containers.Map scalar'.");
            }
        }

        double dim = args[0].Type is JgsType.Number or JgsType.Bool ? args[0].AsNumber
            : args[0].Type == JgsType.Array && args[0].ArrayLength == 1 && !args[0].IsStringArray ? args[0].ElementAt(0).AsNumber
            : double.NaN;
        JgsValue[] maps = [.. args.Skip(1)];
        return dim switch
        {
            1 => MergeMaps(maps, line, col),
            2 => maps.Length == 1 ? maps[0] : throw HorzcatNotSupported(line, col),
            >= 3 when dim == Math.Floor(dim) => throw new JgsRuntimeException(line, col, "MATLAB:Containers:Map:NDCatNotSupported",
                $"Concatenation in dimension {dim} is not supported by a containers.Map."),
            _ => throw new JgsRuntimeException(line, col, "MATLAB:catenate:invalidDimension",
                "Dimension for concatenation must be a positive integer scalar."),
        };
    }

    private static JgsRuntimeException HorzcatNotSupported(int line, int col) =>
        new(line, col, "MATLAB:Containers:Map:HorzcatNotSupported", "Horizontal concatenation is not supported by a containers.Map.");

    private static JgsValue JoinMapsAlong(JgsValue[] pieces, bool vertical, int line, int col)
    {
        // Leading empties leave the bracket before the map's method is asked (measured: [[]; m] and
        // ['', m, []] are m itself, where [m; []] is the method refusing its second argument).
        int lead = 0;
        while (lead < pieces.Length && IsEmptyBracketPiece(pieces[lead]))
        {
            lead++;
        }

        if (lead > 0)
        {
            JgsValue[] rest = [.. pieces.Skip(lead).Where(static p => !IsEmptyBracketPiece(p))];
            if (rest.Length == 1)
            {
                return rest[0];
            }

            pieces = rest;
        }

        JgsValue first = pieces[0];
        if (!IsMatlabMapShape(first, out _))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UnableToConvert",
                $"The following error occurred converting from {ClassOf(first, JgsDialect.Matlab)} to containers.Map:\nIncorrect number of inputs specified.");
        }

        for (int i = 1; i < pieces.Length; i++)
        {
            if (!IsMatlabMapShape(pieces[i], out _))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:class:RequireClass",
                    $"Invalid input for argument {i + 1} (varargin): Value must be 'containers.Map scalar'.");
            }
        }

        return vertical ? MergeMaps(pieces, line, col) : throw HorzcatNotSupported(line, col);
    }

    private static bool IsEmptyBracketPiece(JgsValue piece) =>
        !IsMatlabMapShape(piece, out _)
        && ((piece.Type == JgsType.Array && piece.ArrayLength == 0 && !piece.IsStringArray && !piece.IsTime)
            || (piece.Type == JgsType.Cell && piece.AsCell.Length == 0)
            || (piece.Type == JgsType.String && piece.AsString.Length == 0)
            || (piece.Type == JgsType.Struct && piece.ArrayLength == 0));

    /// <summary>A new map of the first's types holding its entries, then each later map's written in.</summary>
    private static JgsValue MergeMaps(JgsValue[] maps, int line, int col)
    {
        Dictionary<string, JgsValue> source = maps[0].AsStruct;
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["keys"] = JgsValue.Cell([]),
            ["values"] = JgsValue.Cell([]),
            ["KeyType"] = JgsValue.Str(source["KeyType"].AsString),
            ["ValueType"] = JgsValue.Str(source["ValueType"].AsString),
            ["Count"] = CountOf(0),
        };

        JgsValue merged = JgsValue.Struct(fields);
        merged.SetClassName(MapClassName);
        JgsValue[] firstKeys = KeyCell(maps[0]);
        JgsValue[] firstValues = ValueCell(maps[0]);
        for (int i = 0; i < firstKeys.Length; i++)
        {
            Put(merged, firstKeys[i], RetainedForEntry(firstValues[i], sharesOnStore: true), line, col);
        }

        string keyType = fields["KeyType"].AsString;
        string valueType = fields["ValueType"].AsString;
        for (int m = 1; m < maps.Length; m++)
        {
            JgsValue[] keys = KeyCell(maps[m]);
            JgsValue[] values = ValueCell(maps[m]);
            for (int i = 0; i < keys.Length; i++)
            {
                JgsValue key = MapKeyAs(keyType, keys[i], line, col);
                JgsValue value = MapValueAs(valueType, values[i], line, col);
                Put(merged, key, RetainedForEntry(value, sharesOnStore: true), line, col);
            }
        }

        return merged;
    }

    /// <summary>
    /// <c>m(k) = v</c> on a <c>containers.Map</c> in the MATLAB dialect (open item 324, ADR 0371): the
    /// key and the value converted to the map's <c>KeyType</c> and <c>ValueType</c>, or refused, as
    /// the merge converts them (probe p6d: a char into a double map is refused, 5 into a logical map is
    /// true, an int32 into a double map a double, 1.5 into an int32 map 2). The key is read first.
    /// </summary>
    internal static (JgsValue Key, JgsValue Value) MapEntryAs(JgsValue map, JgsValue key, JgsValue value, int line, int col)
    {
        Dictionary<string, JgsValue> fields = map.AsStruct;
        JgsValue convertedKey = MapKeyAs(fields["KeyType"].AsString, key, line, col);
        return (convertedKey, MapValueAs(fields["ValueType"].AsString, value, line, col));
    }

    /// <summary>
    /// A key written into a map of <paramref name="keyType"/>: text into a char map, a real number
    /// converted into a numeric one; a NaN is R2025b's own refusal (probe p6d2).
    /// </summary>
    private static JgsValue MapKeyAs(string keyType, JgsValue key, int line, int col)
    {
        bool text = IsTextScalar(key);
        if (keyType == "char")
        {
            return text ? key : throw MapTypeMismatch("key", line, col);
        }

        if (text || MapScalarNumber(key) is not double number)
        {
            throw MapTypeMismatch("key", line, col);
        }

        return double.IsNaN(number)
            ? throw new JgsRuntimeException(line, col, "MATLAB:Containers:NaNNotAllowed", "NaN-valued key is not allowed for this container.")
            : MapNumberOfClass(number, keyType);
    }

    /// <summary>A real number held as a scalar or a 1-by-1 numeric or logical array; null for anything else.</summary>
    private static double? MapScalarNumber(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Bool ? value.AsNumber
        : value.Type == JgsType.Array && value.ArrayLength == 1 && !value.IsStringArray && !value.IsCharMatrix
            && value.TimeTag is null && !value.IsCategorical && !value.IsPackedComplex ? value.ElementAt(0).AsNumber
        : null;

    /// <summary>
    /// A value written into a map of <paramref name="valueType"/> (probe p317b): anything into 'any',
    /// text into 'char', a real scalar converted into a numeric or logical one (5 into a logical map is
    /// true, an int32 into a double map a double). Probe p6d2: a string scalar into a char map is its
    /// char row and a char matrix the row of its characters in storage order (<c>['ab'; 'cd']</c> is
    /// <c>'acbd'</c>); a logical map takes a whole number only (NaN, Inf, 0.5 are
    /// <c>MATLAB:IntVal</c>; -1 is true).
    /// </summary>
    private static JgsValue MapValueAs(string valueType, JgsValue value, int line, int col)
    {
        if (valueType == "any")
        {
            return value;
        }

        if (valueType == "char")
        {
            if (value.IsCharMatrix)
            {
                return JgsValue.Str(value.CharMatrixText());
            }

            if (value.IsStringArray && value.ArrayLength == 1 && IsTextScalar(value))
            {
                return JgsValue.Str(TextOf(value));
            }

            return value.Type == JgsType.String || IsEmptyCharRow(value) ? value : throw MapTypeMismatch("value", line, col);
        }

        if (MapScalarNumber(value) is not double number)
        {
            throw MapTypeMismatch("value", line, col);
        }

        if (valueType == "logical")
        {
            return double.IsFinite(number) && number == Math.Truncate(number)
                ? JgsValue.Bool(number != 0)
                : throw new JgsRuntimeException(line, col, "MATLAB:IntVal", "Scalar integer value required, but value is not integral");
        }

        return MapNumberOfClass(number, valueType);
    }

    private static JgsValue MapNumberOfClass(double number, string className) =>
        JgsNumericClasses.Parse(className) is { } numeric
            ? JgsNumericClasses.Stamp(JgsValue.Number(number), numeric)
            : JgsValue.Number(number);

    private static JgsRuntimeException MapTypeMismatch(string which, int line, int col) =>
        new(line, col, "MATLAB:Containers:TypeMismatch", $"Specified {which} type does not match the type expected for this container.");
}
