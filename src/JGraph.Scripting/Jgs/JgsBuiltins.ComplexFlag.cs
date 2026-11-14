using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The complex flag of a value whose imaginary parts are all zero (open item 298, ADR 0364).
/// </summary>
/// <remarks>
/// <para>
/// R2025b keeps <c>complex(1, 0)</c> complex: <c>isreal</c> is false, and the value displays,
/// saves and becomes sparse as a complex one. Here the flag is <see cref="JgsPackedComplex.PreserveComplex"/>,
/// the one place it lives: <c>complex</c> builds a planar array with it set, a scalar included
/// (the one-element array), and every reading of an element normalizes, so an indexed read is real
/// as R2025b's is.
/// </para>
/// <para>
/// Measured (probes p5c and p5e) which operations keep it and which drop it. Kept: a copy (a binding, a
/// field, a cell, a function's return), concatenation with a real value, <c>'</c> and <c>.'</c>,
/// <c>reshape</c>, <c>permute</c>, <c>squeeze</c>, <c>full</c>, a MAT-file round trip and
/// <c>sparse</c> of a dense value. Dropped: any indexing, any indexed assignment, unary minus,
/// arithmetic, <c>double</c>, <c>single</c>, an integer class, <c>repmat</c>, the flips and shifts,
/// <c>real</c>, <c>imag</c>, <c>conj</c> and <c>abs</c>.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Whether a value is a planar array that keeps its complex flag with no imaginary part.</summary>
    internal static bool IsZeroImagFlagged(JgsValue value) =>
        value.Type == JgsType.Array && value.IsPackedComplex && value.AsPackedComplex.PreserveComplex
            && !value.AsPackedComplex.Im.AsSpan().ContainsAnyExcept(0.0);

    /// <summary>
    /// An element-wise map over a planar array (unary minus, open item 298). The answer keeps no
    /// flag, so it is real when every imaginary part comes out zero: R2025b's <c>-complex([1 2], 0)</c>
    /// is real.
    /// </summary>
    internal static JgsValue MapPlanesDroppingFlag(JgsPackedComplex planes, Func<Complex, Complex> map)
    {
        int length = planes.Length;
        NumericBuffer re = JgsPacking.Allocate(length);
        NumericBuffer im = JgsPacking.Allocate(length);
        ReadOnlySpan<double> sourceRe = planes.Re.AsSpan();
        ReadOnlySpan<double> sourceIm = planes.Im.AsSpan();
        Span<double> targetRe = re.AsSpan();
        Span<double> targetIm = im.AsSpan();
        bool anyImaginary = false;
        for (int i = 0; i < length; i++)
        {
            Complex mapped = map(new Complex(sourceRe[i], sourceIm[i]));
            targetRe[i] = mapped.Real;
            targetIm[i] = mapped.Imaginary;
            anyImaginary |= mapped.Imaginary != 0;
        }

        GC.KeepAlive(planes);
        if (anyImaginary)
        {
            return JgsValue.PackedComplexArray(new JgsPackedComplex(re, im));
        }

        im.Dispose();
        return JgsValue.Packed(re);
    }

    /// <summary>
    /// A bracket's answer keeps the complex flag of a piece that had one (open item 298):
    /// R2025b's <c>[complex(1, 0) 2]</c> is complex, as concatenation is complex when any piece is.
    /// Only a real double or single answer is turned; one that already holds an imaginary part says
    /// so itself.
    /// </summary>
    internal static JgsValue JoinKeepsComplexFlag(JgsValue assembled, List<JgsValue[]> rows)
    {
        bool flagged = false;
        foreach (JgsValue[] row in rows)
        {
            foreach (JgsValue piece in row)
            {
                flagged |= IsZeroImagFlagged(piece);
            }
        }

        if (!flagged || !IsRealDoubleOrSingle(assembled))
        {
            return assembled;
        }

        double[] values = assembled.Type == JgsType.Number
            ? [assembled.AsNumber]
            : FlattenColumnMajor("concatenation", assembled, 0, 0);
        JgsValue kept = ComplexStorage(
            Array.ConvertAll(values, static v => new Complex(v, 0)), SizeDims(assembled), preserveComplex: true);
        kept.SetNumericClass(assembled.NumericClass);
        return kept;
    }

    /// <summary>
    /// R2025b's checks on <c>complex(a, b)</c> (probe p5i), and its one sparse answer: each part must be
    /// a real number, logical or char array (<c>MATLAB:complex:realPartInputNonNumericOrNonReal</c>,
    /// <c>...imagPartInputNonNumericOrNonReal</c>), two arrays must be one size
    /// (<c>MATLAB:complex:inputSizeMismatch</c>), and two sparse parts make a sparse answer, which is
    /// the only one: a sparse part beside a dense one is read full. Null when the answer is dense.
    /// </summary>
    private static JgsValue? ComplexOfParts(JgsValue real, JgsValue imaginary, int line, int col)
    {
        static bool IsRealPart(JgsValue part) =>
            !HasComplexPart(part) && !part.IsCategorical && part.TimeTag is null && !part.IsStringArray
            && part.Type switch
            {
                JgsType.Number or JgsType.Bool or JgsType.String => true,
                JgsType.Sparse => true,
                JgsType.Array => part.ArrayLength == 0 || part.IsPacked || part.IsPackedComplex || part.IsCharMatrix
                    || Array.TrueForAll(part.AsArray, static e => e.Type is JgsType.Number or JgsType.Bool),
                _ => false,
            };

        if (!IsRealPart(real))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:complex:realPartInputNonNumericOrNonReal",
                "Input for real part must be a real numeric value.");
        }

        if (!IsRealPart(imaginary))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:complex:imagPartInputNonNumericOrNonReal",
                "Input for imaginary part must be a real numeric value.");
        }

        int[] realDims = SizeDims(real), imaginaryDims = SizeDims(imaginary);
        bool realScalar = realDims.Aggregate(1L, static (p, d) => p * d) == 1;
        bool imaginaryScalar = imaginaryDims.Aggregate(1L, static (p, d) => p * d) == 1;
        if (!realScalar && !imaginaryScalar && !realDims.AsSpan().SequenceEqual(imaginaryDims))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:complex:inputSizeMismatch", "Input arrays must have the same size.");
        }

        if (real.Type != JgsType.Sparse || imaginary.Type != JgsType.Sparse)
        {
            return null;
        }

        if (!realDims.AsSpan().SequenceEqual(imaginaryDims))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:complex:inputSizeMismatch", "Input arrays must have the same size.");
        }

        JgsComplexSparse parts = JgsComplexSparse.FromReal(real.AsSparse)
            .Add(JgsComplexSparse.FromReal(imaginary.AsSparse).Map(static v => v * Complex.ImaginaryOne), 1);
        return JgsValue.ComplexSparse(parts);
    }

    /// <summary>One part of <c>complex(a, b)</c> as numbers: a sparse part full, a char part its codes.</summary>
    private static JgsValue ComplexPart(JgsValue part, int line, int col) =>
        part.Type == JgsType.Sparse ? SparseDense(part)
        : part.Type == JgsType.String || part.IsCharMatrix ? ToNumericClass("complex", JgsNumericClass.Double, part, line, col)
        : part;

    /// <summary>
    /// The arguments with a first one that has no imaginary part read as its real plane:
    /// R2025b's <c>num2str</c> adds 0 to its argument and <c>mat2str</c> leaves out a zero imaginary
    /// part, so both write <c>complex(1, 0)</c> as <c>1</c>.
    /// </summary>
    internal static IReadOnlyList<JgsValue> FirstWithoutZeroImag(IReadOnlyList<JgsValue> args)
    {
        if (args.Count == 0 || !IsZeroImagFlagged(args[0]))
        {
            return args;
        }

        var copy = new JgsValue[args.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = args[i];
        }

        copy[0] = RealPlaneOfZeroImag(args[0]);
        return copy;
    }

    /// <summary>
    /// The complex array with these real parts, every imaginary part zero and its flag set, in the
    /// given shape and class: what a MAT-file's complex array with a zero imaginary plane loads as.
    /// </summary>
    internal static JgsValue ZeroImagFlaggedOf(double[] real, int[] dims, JgsNumericClass numericClass)
    {
        JgsValue flagged = ComplexStorage(Array.ConvertAll(real, static v => new Complex(v, 0)), dims, preserveComplex: true);
        flagged.SetNumericClass(numericClass);
        return flagged;
    }

    /// <summary>A real double or single number or numeric array: no text, no logical, no complex part.</summary>
    private static bool IsRealDoubleOrSingle(JgsValue value)
    {
        if (value.NumericClass is not (JgsNumericClass.Double or JgsNumericClass.Single))
        {
            return false;
        }

        if (value.Type == JgsType.Number)
        {
            return true;
        }

        if (value.Type != JgsType.Array || value.IsPackedComplex || value.IsCharMatrix || value.IsStringArray
            || value.IsCategorical || value.TimeTag is not null)
        {
            return false;
        }

        return value.IsPacked
            ? value.PackedKind == JgsPackedKind.Number
            : Array.TrueForAll(value.AsArray, static e => e.Type == JgsType.Number);
    }

    /// <summary>
    /// A planar array with no imaginary part as its real plane, for a conversion that drops the flag
    /// (an integer class: R2025b's <c>int8(complex(1, 0))</c> is a real int8). Anything else is
    /// handed back as it came.
    /// </summary>
    internal static JgsValue RealPlaneOfZeroImag(JgsValue value)
    {
        if (value.Type != JgsType.Array || !value.IsPackedComplex
            || value.AsPackedComplex.Im.AsSpan().ContainsAnyExcept(0.0))
        {
            return value;
        }

        JgsPackedComplex planes = value.AsPackedComplex;
        NumericBuffer re = JgsPacking.Allocate(planes.Length);
        planes.Re.AsSpan().CopyTo(re.AsSpan());
        GC.KeepAlive(planes);
        JgsValue real = JgsMatrix.Like(value, JgsValue.Packed(re));
        real.SetNumericClass(value.NumericClass);
        return real;
    }
}
