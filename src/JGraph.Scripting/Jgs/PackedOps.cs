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
    /// <remarks>
    /// <paramref name="into"/> is the numeric class the answer is owed, applied inside the same
    /// sweep that computed each element (M97). It is <see cref="PackedMath.Rounding.None"/> for the
    /// ordinary double case, which is what makes this the same kernel it has always been; when it is
    /// not, the caller must not convert the answer a second time, because it is already converted.
    /// </remarks>
    public static bool TryArithmetic(PackedMath.BinaryOp op, string symbol, JgsValue left, JgsValue right,
                                     PackedMath.Rounding into, Action? cancelCheck, int line, int column,
                                     out JgsValue result)
    {
        // A negative base raised to a fractional power leaves the reals, and this kernel writes
        // doubles (M81). Declining the fast path is exactly what the class contract is for: the boxed
        // path below promotes the pair to complex, and the answer does not depend on which ran.
        if (op == PackedMath.BinaryOp.Power && PowerWouldGoComplex(left, right))
        {
            result = JgsValue.Null;
            return false;
        }

        if (IsPackedArray(left) && IsPackedArray(right))
        {
            RequireSameLengths(symbol, left.ArrayLength, right.ArrayLength, line, column);
            NumericBuffer dest = JgsPacking.Allocate(left.ArrayLength);
            PackedMath.Binary(op, left.AsBuffer, right.AsBuffer, dest, into, cancelCheck);
            result = KeepShape(JgsValue.Packed(dest), left, right);
            return true;
        }

        if (IsPackedArray(left) && IsNumericScalar(right))
        {
            NumericBuffer dest = JgsPacking.Allocate(left.ArrayLength);
            PackedMath.BinaryScalarRight(op, left.AsBuffer, right.AsNumber, dest, into, cancelCheck);
            result = KeepShape(JgsValue.Packed(dest), left, right);
            return true;
        }

        if (IsPackedArray(right) && IsNumericScalar(left))
        {
            NumericBuffer dest = JgsPacking.Allocate(right.ArrayLength);
            PackedMath.BinaryScalarLeft(op, left.AsNumber, right.AsBuffer, dest, into, cancelCheck);
            result = KeepShape(JgsValue.Packed(dest), left, right);
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
            result = KeepShape(JgsValue.Packed(dest, JgsPackedKind.Bool), left, right);
            return true;
        }

        if (IsPackedArray(left) && IsNumericScalar(right))
        {
            NumericBuffer dest = JgsPacking.Allocate(left.ArrayLength);
            PackedMath.CompareScalar(op, left.AsBuffer, right.AsNumber, dest, scalarOnLeft: false, cancelCheck);
            result = KeepShape(JgsValue.Packed(dest, JgsPackedKind.Bool), left, right);
            return true;
        }

        if (IsPackedArray(right) && IsNumericScalar(left))
        {
            NumericBuffer dest = JgsPacking.Allocate(right.ArrayLength);
            PackedMath.CompareScalar(op, right.AsBuffer, left.AsNumber, dest, scalarOnLeft: true, cancelCheck);
            result = KeepShape(JgsValue.Packed(dest, JgsPackedKind.Bool), left, right);
            return true;
        }

        result = JgsValue.Null;
        return false;
    }

    /// <summary>
    /// Elementwise <c>==</c>/<c>!=</c> over packed operands, honoring boxed equality semantics:
    /// numbers and logicals compare by value, so a mask meets <c>[1 0]</c> as MATLAB expects; a
    /// non-numeric scalar is unequal to every element (never an error), giving a constant mask.
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

            // Logicals are stored as 0.0/1.0, so a number/logical mix compares by value with no
            // conversion — the same answer the boxed path now gives.
            NumericBuffer dest = JgsPacking.Allocate(left.ArrayLength);
            PackedMath.Compare(op, left.AsBuffer, right.AsBuffer, dest, cancelCheck);
            result = KeepShape(JgsValue.Packed(dest, JgsPackedKind.Bool), left, right);
            return true;
        }

        (JgsValue packed, JgsValue scalar) = IsPackedArray(left) ? (left, right) : (right, left);
        if (!IsPackedArray(packed) || scalar.Type == JgsType.Array)
        {
            result = JgsValue.Null;
            return false; // packed-vs-boxed-array mixes fall back to the boxed path
        }

        NumericBuffer mask = JgsPacking.Allocate(packed.ArrayLength);
        bool comparable = scalar.Type is JgsType.Number or JgsType.Bool;
        if (comparable)
        {
            PackedMath.CompareScalar(op, packed.AsBuffer, scalar.AsNumber, mask, scalarOnLeft: false, cancelCheck);
        }
        else
        {
            PackedMath.FillConstant(mask, negate ? 1.0 : 0.0, cancelCheck);
        }

        result = KeepShape(JgsValue.Packed(mask, JgsPackedKind.Bool), left, right);
        return true;
    }

    /// <summary>
    /// An elementwise result is the same shape as the operand it was computed over. Shape lives on
    /// the wrapper, so a freshly allocated buffer starts out a flat row and has to be told.
    /// </summary>
    private static JgsValue KeepShape(JgsValue result, JgsValue left, JgsValue right)
    {
        JgsValue model = left.Type == JgsType.Array && (left.IsShaped || left.IsNd) ? left
            : right.Type == JgsType.Array && (right.IsShaped || right.IsNd) ? right
            : JgsValue.Null;
        if (model.Type == JgsType.Array)
        {
            result.TakeShapeOf(model);
        }

        return result;
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

    /// <summary>
    /// The one indexing arrangement worth not building a list of positions for: a packed array read
    /// through a packed logical mask of its own length (M92). Those positions would be one int per
    /// match — over a hundred megabytes for a fifty-million-element mask, written once and read once
    /// — where the elements they name can be copied across in the pass that finds them.
    /// </summary>
    /// <remarks>
    /// Anything else answers false, including a mask whose length does not match: the caller's
    /// ordinary road raises that as the error it is, and this one is not the place to say so.
    /// </remarks>
    public static bool TryMaskGather(JgsValue target, JgsValue selector, out JgsValue result)
    {
        if (!IsPackedArray(target) || !IsPackedArray(selector)
            || selector.PackedKind != JgsPackedKind.Bool
            || selector.ArrayLength != target.ArrayLength)
        {
            result = JgsValue.Null;
            return false;
        }

        NumericBuffer mask = selector.AsBuffer;
        NumericBuffer dest = JgsPacking.Allocate(PackedMath.CountNonZero(mask));
        PackedMath.Compact(target.AsBuffer, mask, dest);
        result = JgsValue.Packed(dest, target.PackedKind);
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

        int[] picks;
        if (selector.PackedKind == JgsPackedKind.Bool)
        {
            if (span.Length != targetLength)
            {
                throw new JgsRuntimeException(line, column,
                    $"A mask must match the {targetName} length (mask {span.Length}, {targetName} {targetLength}).");
            }

            // Counted first, then filled (M92): a List of a few million matches spends most of its
            // time doubling and then copies the lot once more on the way out, where the count is one
            // vector pass over storage that is about to be read again anyway.
            picks = new int[PackedMath.CountNonZero(buffer)];
            int next = 0;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] != 0)
                {
                    picks[next++] = i;
                }
            }
        }
        else
        {
            picks = new int[span.Length];
            for (int i = 0; i < span.Length; i++)
            {
                picks[i] = ToIndex(span[i], targetLength, indexBase, line, column);
            }
        }

        GC.KeepAlive(buffer);
        return picks;
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

    /// <summary>
    /// Whether any pair in <paramref name="left"/> raised to <paramref name="right"/> leaves the
    /// reals. One scan of the flat buffers, ahead of a kernel that would otherwise write NaN — the
    /// same shape of pre-scan <c>MapComplexProducing</c> makes for the unary family, and cheap beside
    /// the <c>Math.Pow</c> it precedes. Shapes outside the three fast-path arrangements answer false
    /// and let the caller's own arms decline.
    /// </summary>
    private static bool PowerWouldGoComplex(JgsValue left, JgsValue right)
    {
        if (IsPackedArray(left) && IsPackedArray(right) && left.ArrayLength == right.ArrayLength)
        {
            ReadOnlySpan<double> bases = left.AsBuffer.AsSpan();
            ReadOnlySpan<double> powers = right.AsBuffer.AsSpan();
            for (int i = 0; i < bases.Length; i++)
            {
                if (!JgsBuiltins.PowerStaysReal(bases[i], powers[i]))
                {
                    return true;
                }
            }

            return false;
        }

        if (IsPackedArray(left) && IsNumericScalar(right))
        {
            double power = right.AsNumber;
            foreach (double value in left.AsBuffer.AsSpan())
            {
                if (!JgsBuiltins.PowerStaysReal(value, power))
                {
                    return true;
                }
            }

            return false;
        }

        if (IsPackedArray(right) && IsNumericScalar(left))
        {
            double bottom = left.AsNumber;
            foreach (double power in right.AsBuffer.AsSpan())
            {
                if (!JgsBuiltins.PowerStaysReal(bottom, power))
                {
                    return true;
                }
            }
        }

        return false;
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
