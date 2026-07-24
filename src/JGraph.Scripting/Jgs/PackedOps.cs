using System.Globalization;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The interpreter's bridge to <see cref="PackedMath"/>: fast paths for elementwise arithmetic,
/// comparison, equality, range materialization, literal packing, and slice reads over packed
/// arrays. Every Try method returns false when an operand shape is outside its fast path — the
/// interpreter then falls back to the classic boxed code (materializing packed operands via
/// <see cref="JgsValue.BoxedElements"/>), so semantics never depend on which path ran. Error
/// messages replicate the boxed paths' text exactly.
/// </summary>
internal static class PackedOps
{
    /// <summary>The <see cref="PackedMath.BinaryOp"/> for an arithmetic token, or null.</summary>
    public static PackedMath.BinaryOp? MapArithmetic(TokenType op) => op switch
    {
        TokenType.Plus => PackedMath.BinaryOp.Add,
        TokenType.Minus => PackedMath.BinaryOp.Subtract,
        TokenType.Star => PackedMath.BinaryOp.Multiply,
        TokenType.Slash => PackedMath.BinaryOp.Divide,
        TokenType.Percent => PackedMath.BinaryOp.Remainder,
        TokenType.Caret => PackedMath.BinaryOp.Power,
        _ => null,
    };

    /// <summary>
    /// Elementwise arithmetic when either operand is packed and the other is packed or a numeric
    /// scalar (bools read as 0/1, as in the boxed paths). Results are packed number arrays.
    /// </summary>
    public static bool TryArithmetic(PackedMath.BinaryOp op, string symbol, JgsValue left, JgsValue right,
                                     Action? cancelCheck, int line, int column, out JgsValue result)
    {
        if (IsPackedArray(left) && IsPackedArray(right))
        {
            RequireSameLengths(symbol, left.ArrayLength, right.ArrayLength, line, column);
            NumericBuffer dest = JgsPacking.Allocate(left.ArrayLength);
            PackedMath.Binary(op, left.AsBuffer, right.AsBuffer, dest, cancelCheck);
            result = JgsValue.Packed(dest);
            return true;
        }

        if (IsPackedArray(left) && IsNumericScalar(right))
        {
            NumericBuffer dest = JgsPacking.Allocate(left.ArrayLength);
            PackedMath.BinaryScalarRight(op, left.AsBuffer, right.AsNumber, dest, cancelCheck);
            result = JgsValue.Packed(dest);
            return true;
        }

        if (IsPackedArray(right) && IsNumericScalar(left))
        {
            NumericBuffer dest = JgsPacking.Allocate(right.ArrayLength);
            PackedMath.BinaryScalarLeft(op, left.AsNumber, right.AsBuffer, dest, cancelCheck);
            result = JgsValue.Packed(dest);
            return true;
        }

        result = JgsValue.Null;
        return false;
    }

    /// <summary>The <see cref="PackedMath.CompareOp"/> for an ordering token, or null.</summary>
    public static PackedMath.CompareOp? MapComparison(TokenType op) => op switch
    {
        TokenType.Less => PackedMath.CompareOp.Less,
        TokenType.LessEqual => PackedMath.CompareOp.LessEqual,
        TokenType.Greater => PackedMath.CompareOp.Greater,
        TokenType.GreaterEqual => PackedMath.CompareOp.GreaterEqual,
        _ => null,
    };

    /// <summary>Elementwise ordering comparison producing a packed logical mask.</summary>
    public static bool TryCompare(PackedMath.CompareOp op, string symbol, JgsValue left, JgsValue right,
                                  Action? cancelCheck, int line, int column, out JgsValue result)
    {
        if (IsPackedArray(left) && IsPackedArray(right))
        {
            RequireSameLengths(symbol, left.ArrayLength, right.ArrayLength, line, column);
            NumericBuffer dest = JgsPacking.Allocate(left.ArrayLength);
            PackedMath.Compare(op, left.AsBuffer, right.AsBuffer, dest, cancelCheck);
            result = JgsValue.Packed(dest, JgsPackedKind.Bool);
            return true;
        }

        if (IsPackedArray(left) && IsNumericScalar(right))
        {
            NumericBuffer dest = JgsPacking.Allocate(left.ArrayLength);
            PackedMath.CompareScalar(op, left.AsBuffer, right.AsNumber, dest, scalarOnLeft: false, cancelCheck);
            result = JgsValue.Packed(dest, JgsPackedKind.Bool);
            return true;
        }

        if (IsPackedArray(right) && IsNumericScalar(left))
        {
            NumericBuffer dest = JgsPacking.Allocate(right.ArrayLength);
            PackedMath.CompareScalar(op, right.AsBuffer, left.AsNumber, dest, scalarOnLeft: true, cancelCheck);
            result = JgsValue.Packed(dest, JgsPackedKind.Bool);
            return true;
        }

        result = JgsValue.Null;
        return false;
    }

    /// <summary>
    /// Elementwise <c>==</c>/<c>!=</c> over packed operands, honoring boxed equality semantics:
    /// numbers compare to numbers and bools to bools; mismatched element types compare unequal
    /// (never an error), so a packed-number array against a string scalar is a constant mask.
    /// </summary>
    public static bool TryEquality(JgsValue left, JgsValue right, bool negate,
                                   Action? cancelCheck, int line, int column, out JgsValue result)
    {
        var op = negate ? PackedMath.CompareOp.NotEqual : PackedMath.CompareOp.Equal;

        if (IsPackedArray(left) && IsPackedArray(right))
        {
            if (left.ArrayLength != right.ArrayLength)
            {
                throw new JgsRuntimeException(line, column,
                    $"Cannot apply '{(negate ? "!=" : "==")}' to arrays of different lengths ({left.ArrayLength} and {right.ArrayLength}).");
            }

            NumericBuffer dest = JgsPacking.Allocate(left.ArrayLength);
            if (left.PackedKind == right.PackedKind)
            {
                PackedMath.Compare(op, left.AsBuffer, right.AsBuffer, dest, cancelCheck);
            }
            else
            {
                // Number elements never equal bool elements in boxed semantics.
                PackedMath.FillConstant(dest, negate ? 1.0 : 0.0, cancelCheck);
            }

            result = JgsValue.Packed(dest, JgsPackedKind.Bool);
            return true;
        }

        (JgsValue packed, JgsValue scalar) = IsPackedArray(left) ? (left, right) : (right, left);
        if (!IsPackedArray(packed) || scalar.Type == JgsType.Array)
        {
            result = JgsValue.Null;
            return false; // packed-vs-boxed-array mixes fall back to the boxed path
        }

        NumericBuffer mask = JgsPacking.Allocate(packed.ArrayLength);
        bool comparable = (packed.PackedKind, scalar.Type) is
            (JgsPackedKind.Number, JgsType.Number) or (JgsPackedKind.Bool, JgsType.Bool);
        if (comparable)
        {
            PackedMath.CompareScalar(op, packed.AsBuffer, scalar.AsNumber, mask, scalarOnLeft: false, cancelCheck);
        }
        else
        {
            PackedMath.FillConstant(mask, negate ? 1.0 : 0.0, cancelCheck);
        }

        result = JgsValue.Packed(mask, JgsPackedKind.Bool);
        return true;
    }

    /// <summary>Elementwise negation of a packed array (bools negate to numbers, as when boxed).</summary>
    public static JgsValue Negate(JgsValue packed, Action? cancelCheck)
    {
        NumericBuffer dest = JgsPacking.Allocate(packed.ArrayLength);
        PackedMath.Unary(PackedMath.UnaryOp.Negate, packed.AsBuffer, dest, cancelCheck);
        return JgsValue.Packed(dest);
    }

    /// <summary>Materializes a colon range directly into a packed buffer.</summary>
    public static JgsValue CreateRange(double start, double step, long count, Action? cancelCheck)
    {
        NumericBuffer dest = JgsPacking.Allocate(count);
        PackedMath.Fill(dest, start, step, cancelCheck);
        return JgsValue.Packed(dest);
    }

    /// <summary>
    /// Packs an evaluated literal's elements when they are homogeneous scalars (all numbers or all
    /// bools). Mixed or non-scalar element lists stay boxed.
    /// </summary>
    public static bool TryPackElements(JgsValue[] elements, out JgsValue packed)
    {
        bool allNumbers = true;
        bool allBools = elements.Length > 0;
        foreach (JgsValue element in elements)
        {
            allNumbers &= element.Type == JgsType.Number;
            allBools &= element.Type == JgsType.Bool;
        }

        if (!allNumbers && !allBools)
        {
            packed = JgsValue.Null;
            return false;
        }

        NumericBuffer buffer = JgsPacking.Allocate(elements.Length);
        Span<double> span = buffer.AsSpan();
        for (int i = 0; i < elements.Length; i++)
        {
            span[i] = elements[i].AsNumber;
        }

        GC.KeepAlive(buffer);
        packed = JgsValue.Packed(buffer, allNumbers ? JgsPackedKind.Number : JgsPackedKind.Bool);
        return true;
    }

    /// <summary>
    /// Vertically concatenates matrix-literal rows into one packed array when every leaf is a plain
    /// number (packed number arrays, number scalars, and nested all-number boxed arrays qualify).
    /// </summary>
    public static bool TryFlattenNumeric(List<JgsValue[]> rows, Action? cancelCheck, out JgsValue result)
    {
        long total = 0;
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue value in row)
            {
                long count = CountNumberLeaves(value);
                if (count < 0)
                {
                    result = JgsValue.Null;
                    return false;
                }

                total += count;
            }
        }

        NumericBuffer buffer = JgsPacking.Allocate(total);
        int offset = 0;
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue value in row)
            {
                CopyLeaves(value, buffer, ref offset);
            }

            cancelCheck?.Invoke();
        }

        GC.KeepAlive(buffer);
        result = JgsValue.Packed(buffer);
        return true;
    }

    /// <summary>Gathers picked elements of a packed array into a new packed array of the same kind.</summary>
    public static JgsValue Gather(JgsValue packed, int[] picks)
    {
        NumericBuffer dest = JgsPacking.Allocate(picks.Length);
        PackedMath.Gather(packed.AsBuffer, picks, dest);
        return JgsValue.Packed(dest, packed.PackedKind);
    }

    /// <summary>A full copy of a packed array (the <c>x(:)</c> read).</summary>
    public static JgsValue Clone(JgsValue packed, Action? cancelCheck)
    {
        NumericBuffer dest = JgsPacking.Allocate(packed.ArrayLength);
        PackedMath.Copy(packed.AsBuffer, dest, cancelCheck);
        return JgsValue.Packed(dest, packed.PackedKind);
    }

    /// <summary>Gathers picked elements of a packed complex array (both planes).</summary>
    public static JgsValue GatherComplex(JgsValue packed, int[] picks)
    {
        JgsPackedComplex source = packed.AsPackedComplex;
        NumericBuffer re = JgsPacking.Allocate(picks.Length);
        NumericBuffer im = JgsPacking.Allocate(picks.Length);
        PackedMath.Gather(source.Re, picks, re);
        PackedMath.Gather(source.Im, picks, im);
        return JgsValue.PackedComplexArray(new JgsPackedComplex(re, im));
    }

    /// <summary>A full copy of a packed complex array (the <c>x(:)</c> read).</summary>
    public static JgsValue CloneComplex(JgsValue packed, Action? cancelCheck)
    {
        JgsPackedComplex source = packed.AsPackedComplex;
        NumericBuffer re = JgsPacking.Allocate(source.Length);
        NumericBuffer im = JgsPacking.Allocate(source.Length);
        PackedMath.Copy(source.Re, re, cancelCheck);
        PackedMath.Copy(source.Im, im, cancelCheck);
        return JgsValue.PackedComplexArray(new JgsPackedComplex(re, im));
    }

    /// <summary>
    /// Resolves a packed selector to 0-based picks: a packed logical is a mask (length-checked), a
    /// packed number array is an index list. Mirrors the boxed selector rules and messages.
    /// </summary>
    public static int[] PicksFromPacked(JgsValue selector, int targetLength,
                                        string targetName, int indexBase, int line, int column)
    {
        NumericBuffer buffer = selector.AsBuffer;
        Span<double> span = buffer.AsSpan();
        if (span.Length == 0)
        {
            return System.Array.Empty<int>(); // an empty selector picks nothing, mask or not
        }

        var picks = new List<int>();
        if (selector.PackedKind == JgsPackedKind.Bool)
        {
            if (span.Length != targetLength)
            {
                throw new JgsRuntimeException(line, column,
                    $"A mask must match the {targetName} length (mask {span.Length}, {targetName} {targetLength}).");
            }

            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] != 0)
                {
                    picks.Add(i);
                }
            }
        }
        else
        {
            for (int i = 0; i < span.Length; i++)
            {
                picks.Add(ToIndex(span[i], targetLength, indexBase, line, column));
            }
        }

        GC.KeepAlive(buffer);
        return picks.ToArray();
    }

    /// <summary>
    /// A packed complex selector: legal only when every element has zero imaginary part (those read
    /// as plain numbers, so the boxed form would be an all-number index list); otherwise the boxed
    /// mixed-type selector error.
    /// </summary>
    public static int[] PicksFromPackedComplex(JgsValue selector, int targetLength, int indexBase, int line, int column)
    {
        JgsPackedComplex planes = selector.AsPackedComplex;
        Span<double> im = planes.Im.AsSpan();
        foreach (double v in im)
        {
            if (v != 0)
            {
                throw new JgsRuntimeException(line, column,
                    "An index array must be all numbers (indices) or all bools (a mask).");
            }
        }

        Span<double> re = planes.Re.AsSpan();
        var picks = new int[re.Length];
        for (int i = 0; i < re.Length; i++)
        {
            picks[i] = ToIndex(re[i], targetLength, indexBase, line, column);
        }

        GC.KeepAlive(planes);
        return picks;
    }

    /// <summary>
    /// A raw double as an element position, counted from <paramref name="indexBase"/> (0 in JGS, 1 in
    /// MATLAB), with the boxed paths' exact messages.
    /// </summary>
    public static int ToIndex(double raw, int length, int indexBase, int line, int column)
    {
        if (raw != Math.Floor(raw) || double.IsNaN(raw) || double.IsInfinity(raw))
        {
            throw new JgsRuntimeException(line, column,
                $"An index must be a whole number, but got {raw.ToString("R", CultureInfo.InvariantCulture)}.");
        }

        int i = (int)raw - indexBase;
        if (i < 0 || i >= length)
        {
            throw new JgsRuntimeException(line, column,
                $"Index {(int)raw} is out of range for length {length} (indexing is {indexBase}-based).");
        }

        return i;
    }

    private static bool IsPackedArray(JgsValue value) => value.Type == JgsType.Array && value.IsPacked;

    private static bool IsNumericScalar(JgsValue value) => value.Type is JgsType.Number or JgsType.Bool;

    private static void RequireSameLengths(string symbol, int a, int b, int line, int column)
    {
        if (a != b)
        {
            throw new JgsRuntimeException(line, column,
                $"Cannot apply '{symbol}' to arrays of different lengths ({a} and {b}).");
        }
    }

    /// <summary>Number leaves under a value, or -1 when any leaf is not a plain number.</summary>
    private static long CountNumberLeaves(JgsValue value)
    {
        if (value.Type == JgsType.Number)
        {
            return 1;
        }

        if (value.Type != JgsType.Array)
        {
            return -1;
        }

        if (value.IsPacked)
        {
            return value.PackedKind == JgsPackedKind.Number ? value.ArrayLength : -1;
        }

        long total = 0;
        foreach (JgsValue element in value.AsArray)
        {
            long count = CountNumberLeaves(element);
            if (count < 0)
            {
                return -1;
            }

            total += count;
        }

        return total;
    }

    private static void CopyLeaves(JgsValue value, NumericBuffer buffer, ref int offset)
    {
        if (value.Type == JgsType.Number)
        {
            buffer.AsSpan()[offset++] = value.AsNumber;
            return;
        }

        if (value.IsPacked)
        {
            Span<double> source = value.AsBuffer.AsSpan();
            source.CopyTo(buffer.AsSpan(offset, source.Length));
            offset += source.Length;
            return;
        }

        foreach (JgsValue element in value.AsArray)
        {
            CopyLeaves(element, buffer, ref offset);
        }
    }
}
