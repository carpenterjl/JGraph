using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>The numeric class an array remembers being asked for: MATLAB's <c>class(x)</c> answer.</summary>
/// <remarks>
/// Storage is always doubles. The class is a tag, not a representation — <c>uint8([1 2 3])</c> holds
/// the same three doubles it would have held anyway, and the tag records that they were rounded and
/// saturated into 0…255 and must stay there. That is enough to answer <c>class</c>, <c>isinteger</c>
/// and <c>isa</c> honestly, and to make arithmetic saturate the way MATLAB's does, without a second
/// numeric kernel for each of the eight integer widths.
/// </remarks>
internal enum JgsNumericClass : byte
{
    /// <summary>Ordinary MATLAB double precision — the class every untagged value has.</summary>
    Double,

    Single,
    Int8,
    Int16,
    Int32,
    Int64,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
}

/// <summary>Names, ranges, conversion and the combining rule for <see cref="JgsNumericClass"/>.</summary>
internal static class JgsNumericClasses
{
    /// <summary>The class's MATLAB name, as <c>class</c> reports it.</summary>
    public static string MatlabName(this JgsNumericClass numericClass) => numericClass switch
    {
        JgsNumericClass.Single => "single",
        JgsNumericClass.Int8 => "int8",
        JgsNumericClass.Int16 => "int16",
        JgsNumericClass.Int32 => "int32",
        JgsNumericClass.Int64 => "int64",
        JgsNumericClass.UInt8 => "uint8",
        JgsNumericClass.UInt16 => "uint16",
        JgsNumericClass.UInt32 => "uint32",
        JgsNumericClass.UInt64 => "uint64",
        _ => "double",
    };

    /// <summary>True for the eight integer classes.</summary>
    public static bool IsInteger(this JgsNumericClass numericClass) => numericClass >= JgsNumericClass.Int8;

    /// <summary>The class a MATLAB class name names, or null when the name is not a numeric class.</summary>
    public static JgsNumericClass? Parse(string name) => name switch
    {
        "double" => JgsNumericClass.Double,
        "single" => JgsNumericClass.Single,
        "int8" => JgsNumericClass.Int8,
        "int16" => JgsNumericClass.Int16,
        "int32" => JgsNumericClass.Int32,
        "int64" => JgsNumericClass.Int64,
        "uint8" => JgsNumericClass.UInt8,
        "uint16" => JgsNumericClass.UInt16,
        "uint32" => JgsNumericClass.UInt32,
        "uint64" => JgsNumericClass.UInt64,
        _ => null,
    };

    /// <summary>The saturation range of an integer class.</summary>
    public static (double Min, double Max) Range(JgsNumericClass numericClass) => numericClass switch
    {
        JgsNumericClass.Int8 => (sbyte.MinValue, sbyte.MaxValue),
        JgsNumericClass.Int16 => (short.MinValue, short.MaxValue),
        JgsNumericClass.Int32 => (int.MinValue, int.MaxValue),
        JgsNumericClass.Int64 => (long.MinValue, long.MaxValue),
        JgsNumericClass.UInt8 => (0, byte.MaxValue),
        JgsNumericClass.UInt16 => (0, ushort.MaxValue),
        JgsNumericClass.UInt32 => (0, uint.MaxValue),
        JgsNumericClass.UInt64 => (0, ulong.MaxValue),
        _ => (double.NegativeInfinity, double.PositiveInfinity),
    };

    /// <summary>One sample through the class: round half away from zero, saturate, NaN to 0.</summary>
    /// <remarks>
    /// Single rounds to float precision instead, which is what makes <c>single(0.1) == 0.1</c> false
    /// in MATLAB and here alike.
    /// </remarks>
    public static double Convert(double x, JgsNumericClass numericClass)
    {
        if (numericClass == JgsNumericClass.Single)
        {
            return (float)x;
        }

        if (!numericClass.IsInteger())
        {
            return x;
        }

        if (double.IsNaN(x))
        {
            return 0;
        }

        (double min, double max) = Range(numericClass);
        return System.Math.Clamp(System.Math.Round(x, MidpointRounding.AwayFromZero), min, max);
    }

    /// <summary>
    /// Converts every sample of <paramref name="value"/> into <paramref name="numericClass"/> and
    /// tags the result with it.
    /// </summary>
    /// <remarks>
    /// The value that comes back is a fresh wrapper whenever any conversion happened, so the packed
    /// single-wrapper invariant holds; a value already in the class is tagged in place, which is what
    /// lets an indexing read stamp its own result without copying the samples twice.
    /// </remarks>
    public static JgsValue Stamp(JgsValue value, JgsNumericClass numericClass)
    {
        if (numericClass == JgsNumericClass.Double)
        {
            return value;
        }

        JgsValue converted = Map(value, numericClass);
        converted.SetNumericClass(numericClass);
        return converted;
    }

    private static JgsValue Map(JgsValue value, JgsNumericClass numericClass)
    {
        switch (value.Type)
        {
            case JgsType.Number or JgsType.Bool:
                return JgsValue.Number(Convert(value.AsNumber, numericClass));

            case JgsType.Array when value.IsPacked:
            {
                NumericBuffer destination = JgsPacking.Allocate(value.ArrayLength);
                PackedMath.Map(value.AsBuffer, destination, x => Convert(x, numericClass));
                return JgsMatrix.Like(value, JgsValue.Packed(destination));
            }

            case JgsType.Array:
            {
                JgsValue[] source = value.AsArray;
                var mapped = new JgsValue[source.Length];
                for (int i = 0; i < mapped.Length; i++)
                {
                    mapped[i] = Map(source[i], numericClass);
                }

                return JgsMatrix.Like(value, JgsValue.Array(mapped));
            }

            default:
                // A string, a cell, a function: nothing to convert, and nothing that could carry the
                // tag either. The caller has already decided this value is numeric, so leaving it
                // alone keeps the tag off values that cannot hold one.
                return value;
        }
    }

    /// <summary>
    /// The class an arithmetic result takes from its two operands, and MATLAB's rule for refusing
    /// the combination outright.
    /// </summary>
    /// <remarks>
    /// An integer class wins over a floating one, because MATLAB keeps arithmetic inside the narrower
    /// type and saturates rather than promoting. Two <em>different</em> integer classes are an error,
    /// as is an integer combined with a non-scalar double: MATLAB's rule is that an integer array
    /// combines only with its own class or with a scalar, so <c>int8([1 2]) + [1 2]</c> is refused
    /// where <c>int8([1 2]) + 1</c> is not. Single beats double; everything else is double.
    /// </remarks>
    public static JgsNumericClass Combine(JgsValue left, JgsValue right, string op, int line, int col)
    {
        JgsNumericClass a = left.NumericClass;
        JgsNumericClass b = right.NumericClass;
        if (a == JgsNumericClass.Double && b == JgsNumericClass.Double)
        {
            return JgsNumericClass.Double;
        }

        if (a.IsInteger() && b.IsInteger())
        {
            if (a != b)
            {
                throw new JgsRuntimeException(line, col,
                    $"'{op}' cannot combine {a.MatlabName()} with {b.MatlabName()}; " +
                    "integers only combine with their own class or with a scalar.");
            }

            return a;
        }

        if (a.IsInteger() || b.IsInteger())
        {
            JgsNumericClass integer = a.IsInteger() ? a : b;
            JgsValue other = a.IsInteger() ? right : left;
            if (!IsScalar(other))
            {
                throw new JgsRuntimeException(line, col,
                    $"'{op}' cannot combine {integer.MatlabName()} with a {other.NumericClass.MatlabName()} array; " +
                    "an integer array combines only with its own class or with a scalar.");
            }

            return integer;
        }

        return JgsNumericClass.Single;
    }

    /// <summary>
    /// The class a concatenation takes from its pieces: an integer class wins, then single, then
    /// double — which is why <c>[int8(1) 300]</c> is an int8 row whose second element saturates.
    /// </summary>
    public static JgsNumericClass CombineForConcat(JgsNumericClass a, JgsNumericClass b, string op, int line, int col)
    {
        if (a == b)
        {
            return a;
        }

        if (a.IsInteger() && b.IsInteger())
        {
            throw new JgsRuntimeException(line, col,
                $"{op} cannot join {a.MatlabName()} with {b.MatlabName()}; " +
                "integers only join with their own class.");
        }

        if (a.IsInteger())
        {
            return a;
        }

        if (b.IsInteger())
        {
            return b;
        }

        return a == JgsNumericClass.Single || b == JgsNumericClass.Single
            ? JgsNumericClass.Single
            : JgsNumericClass.Double;
    }

    private static bool IsScalar(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Bool
        || (value.Type == JgsType.Array && value.ArrayLength == 1);
}
