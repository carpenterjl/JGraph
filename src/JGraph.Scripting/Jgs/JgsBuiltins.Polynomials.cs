using System;
using System.Collections.Generic;
using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The polynomial and one-dimensional signal names from MATLAB's <c>polyfun</c>, <c>datafun</c> and
/// <c>elfun</c> folders: <c>roots</c>, <c>poly</c>, <c>polyder</c>, <c>polyint</c>,
/// <c>polyvalm</c>, <c>conv</c>, <c>deconv</c>, <c>convn</c>, <c>nextpow2</c>, <c>unwrap</c>,
/// <c>cplxpair</c>, <c>polyarea</c>, <c>rectint</c> and <c>inpolygon</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a MATLAB library function rather than a kernel builtin — the population
/// <c>docs/matlab-toolbox-coverage.md</c> counts. The arithmetic lives in
/// <see cref="JGraph.Numerics"/>; what is here is the part that is really about MATLAB rather than
/// about mathematics, which turns out to be most of the surface area: which shape an answer takes,
/// which argument decides it, and which identifier a refusal carries.
/// </para>
/// <para>
/// The shape rules are the fiddly part and they are not decorative. <c>conv</c> takes its
/// orientation from whichever operand is longer, and from the first one alone once a shape word is
/// given; <c>roots</c> always answers a column and <c>poly</c> always a row, whatever they were
/// handed. A script that plots one against the other, or feeds one straight back into the other,
/// depends on all of it.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the polynomial and 1-D signal builtins into <paramref name="env"/>.</summary>
    /// <param name="env">The scope to declare into.</param>
    internal static void RegisterPolynomialBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            Define(name, (args, line, col) => both(args, 1, line, col)[0], both);

        Define("roots", PolynomialRoots);
        Define("poly", PolynomialFromRoots);
        DefineBoth("polyder", PolynomialDerivative);
        Define("polyint", PolynomialIntegral);
        Define("polyvalm", PolynomialMatrixValue);

        DefineBoth("residue", Residue);

        Define("conv", Convolve);
        DefineBoth("deconv", Deconvolve);
        Define("convn", ConvolveN);

        Define("nextpow2", (args, line, col) =>
        {
            Arity("nextpow2", args, 1, line, col);
            return MapNumeric("nextpow2", args[0], Polynomials.NextPowerOfTwo, line, col);
        });

        Define("unwrap", UnwrapPhase);
        Define("cplxpair", ComplexPairs);

        Define("polyarea", PolygonArea);
        Define("rectint", RectangleIntersection);
        DefineBoth("inpolygon", PointsInPolygon);
    }

    // --- Polynomials ------------------------------------------------------------------------------

    /// <summary>
    /// <c>r = roots(p)</c>: the roots of the polynomial whose coefficients are <c>p</c>, highest
    /// power first, always as a column.
    /// </summary>
    private static JgsValue PolynomialRoots(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("roots", args, 1, line, col);
        JgsValue given = args[0];
        if (!IsVectorOrEmpty(given))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:roots:NonVectorInput",
                "Input must be a vector.");
        }

        Complex[] coefficients = ComplexArrayOf("roots", given, line, col);
        foreach (Complex c in coefficients)
        {
            if (!Complex.IsFinite(c))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:roots:NonFiniteInput",
                    "Input to ROOTS must not contain NaN or Inf.");
            }
        }

        return ComplexColumn(Polynomials.Roots(coefficients));
    }

    /// <summary>
    /// <c>p = poly(r)</c> for a vector of roots, or <c>p = poly(A)</c> for a square matrix, whose
    /// characteristic polynomial is the one whose roots are its eigenvalues. Always a row.
    /// </summary>
    private static JgsValue PolynomialFromRoots(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("poly", args, 1, line, col);
        JgsValue given = args[0];
        int[] dims = SizeDims(given);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int cols = dims.Length > 1 ? dims[1] : 1;

        Complex[] values;
        if (dims.Length <= 2 && rows == cols)
        {
            // A square matrix — including the 1-by-1 and the 0-by-0 — is read as a matrix whose
            // eigenvalues are the roots, which is why poly(5) is [1 -5] and not [1] as a one-element
            // root vector alone might suggest.
            values = rows == 0
                ? []
                : SquareSpectrum("poly", given, rows, line, col);
        }
        else if (dims.Length <= 2 && (rows == 1 || cols == 1))
        {
            values = ComplexArrayOf("poly", given, line, col);
        }
        else
        {
            throw new JgsRuntimeException(line, col, "MATLAB:poly:InputSize",
                "Argument must be a vector or a square matrix.");
        }

        return ComplexRow(Polynomials.FromRoots(values));
    }

    /// <summary>
    /// <c>polyder(p)</c>, <c>polyder(a, b)</c> for the derivative of a product, and
    /// <c>[q, d] = polyder(b, a)</c> for the derivative of a ratio as a ratio.
    /// </summary>
    private static JgsValue[] PolynomialDerivative(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("polyder", args, 1, 2, line, col);
        double[] u = FlattenColumnMajor("polyder", args[0], line, col);
        double[] v = args.Count >= 2
            ? FlattenColumnMajor("polyder", args[1], line, col)
            : [1.0];

        if (wanted < 2)
        {
            return [Row(Polynomials.Derivative(u, v))];
        }

        (double[] numerator, double[] denominator) = Polynomials.QuotientDerivative(u, v);
        return [Row(numerator), Row(denominator)];
    }

    /// <summary>
    /// <c>polyint(p)</c> and <c>polyint(p, k)</c>: the antiderivative, with <c>k</c> as the constant
    /// of integration.
    /// </summary>
    private static JgsValue PolynomialIntegral(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("polyint", args, 1, 2, line, col);
        JgsValue given = args[0];
        double[] p = FlattenColumnMajor("polyint", given, line, col);

        // MATLAB divides p by the row 1:n and then concatenates the constant on the right. Handed a
        // column, the division broadcasts to a square and the concatenation cannot happen — so a
        // column polynomial is a documented failure rather than the answer a reader would expect.
        int[] dims = SizeDims(given);
        bool oneRow = dims.Length <= 1 || dims[0] == 1;
        if (p.Length > 1 && !oneRow)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:catenate:dimensionMismatch",
                "Dimensions of arrays being concatenated are not consistent.");
        }

        double constant = args.Count >= 2 ? Num("polyint", args, 1, line, col) : 0.0;
        return Row(Polynomials.Antiderivative(p, constant));
    }

    /// <summary>
    /// <c>Y = polyvalm(p, X)</c>: the polynomial in the matrix sense, where each power is a matrix
    /// power and the constant term is that multiple of the identity.
    /// </summary>
    private static JgsValue PolynomialMatrixValue(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("polyvalm", args, 2, line, col);
        if (!IsVectorOrEmpty(args[0]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:polyvalm:InvalidP", "P must be a vector.");
        }

        int[] dims = SizeDims(args[1]);
        int rows = dims.Length > 0 ? dims[0] : 1;
        int cols = dims.Length > 1 ? dims[1] : 1;
        if (dims.Length > 2 || rows != cols)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:polyvalm:NonSquareMatrix",
                "Matrix must be square.");
        }

        double[] p = FlattenColumnMajor("polyvalm", args[0], line, col);
        double[] x = FlattenColumnMajor("polyvalm", args[1], line, col);
        if (p.Length == 0)
        {
            return JgsMatrix.FromColumnMajorDims(new double[rows * rows], [rows, rows]);
        }

        return JgsMatrix.FromColumnMajorDims(Polynomials.MatrixValue(p, x, rows), [rows, rows]);
    }

    // --- Convolution ------------------------------------------------------------------------------

    /// <summary>
    /// <c>w = conv(u, v)</c> and <c>conv(u, v, shape)</c>: the convolution of two sequences, which
    /// is also the product of the two polynomials their coefficients spell.
    /// </summary>
    private static JgsValue Convolve(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("conv", args, 2, 3, line, col);
        if (!IsVector(args[0]) || !IsVector(args[1]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:conv:AorBNotVector",
                "A and B must be vectors.");
        }

        ConvolutionShape shape = args.Count >= 3
            ? ConvolutionShapeOf("conv", args, 2, line, col)
            : ConvolutionShape.Full;

        int lengthA = ElementCount(args[0]);
        int lengthB = ElementCount(args[1]);

        // Full follows whichever operand is longer, and the second one when they tie; a cut shape is
        // measured against the first, so it follows the first.
        bool asRow = shape != ConvolutionShape.Full || lengthA > lengthB
            ? IsRowVector(args[0])
            : IsRowVector(args[1]);

        if (HasComplexPart(args[0]) || HasComplexPart(args[1]))
        {
            Complex[] answer = Convolution.Convolve(
                ComplexArrayOf("conv", args[0], line, col),
                ComplexArrayOf("conv", args[1], line, col),
                shape);
            return asRow ? ComplexRow(answer) : ComplexColumn(answer);
        }

        double[] values = Convolution.Convolve(
            FlattenColumnMajor("conv", args[0], line, col),
            FlattenColumnMajor("conv", args[1], line, col),
            shape);

        return Oriented(values, asRow);
    }

    /// <summary>
    /// <c>[q, r] = deconv(u, v)</c>: long division of one sequence by another, so that
    /// <c>u</c> is <c>conv(v, q) + r</c>.
    /// </summary>
    /// <summary>
    /// <c>[r, p, k] = residue(b, a)</c>, and the same name read backwards as
    /// <c>[b, a] = residue(r, p, k)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One name, two directions, told apart by how many arguments arrive — which is MATLAB's own
    /// rule and not a convenience. Three inputs is always the way back, because the forward
    /// direction has only ever taken two.
    /// </para>
    /// <para>
    /// The poles come back in the order <c>roots</c> gives, so <c>residue</c> and <c>roots</c> agree
    /// about a polynomial with no repeated factor. A repeated one is the case where they part: the
    /// eigenvalue solver hands back a double root as a conjugate pair about 1e-8 off the axis, and
    /// this reads that pair as the one pole it is (see <see cref="PartialFractions"/>).
    /// </para>
    /// </remarks>
    private static JgsValue[] Residue(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("residue", args, 2, 3, line, col);

        if (args.Count == 3)
        {
            (Complex[] numerator, Complex[] denominator) = PartialFractions.Combine(
                ComplexArrayOf("residue", args[0], line, col),
                ComplexArrayOf("residue", args[1], line, col),
                ComplexArrayOf("residue", args[2], line, col));

            return [ComplexRow(numerator), ComplexRow(denominator)];
        }

        PartialFractions.Expansion expansion = PartialFractions.Expand(
            ComplexArrayOf("residue", args[0], line, col),
            ComplexArrayOf("residue", args[1], line, col));

        // r and p are columns and k is a row, whatever shape the call was written with — the same
        // asymmetry roots and poly have, and for the same reason: one of the three is a polynomial.
        return
        [
            ComplexColumn(expansion.Residues),
            ComplexColumn(expansion.Poles),
            ComplexRow(expansion.Direct),
        ];
    }

    private static JgsValue[] Deconvolve(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("deconv", args, 2, line, col);
        bool asRow = IsRowVector(args[0]);

        if (HasComplexPart(args[0]) || HasComplexPart(args[1]))
        {
            Complex[] b = ComplexArrayOf("deconv", args[0], line, col);
            Complex[] a = ComplexArrayOf("deconv", args[1], line, col);
            RefuseZeroDivisor(a.Length > 0 && a[0] == Complex.Zero, line, col);
            (Complex[] q, Complex[] r) = Polynomials.Divide(b, a);
            return Outputs(wanted,
                asRow ? ComplexRow(q) : ComplexColumn(q),
                asRow ? ComplexRow(r) : ComplexColumn(r));
        }

        double[] dividend = FlattenColumnMajor("deconv", args[0], line, col);
        double[] divisor = FlattenColumnMajor("deconv", args[1], line, col);
        RefuseZeroDivisor(divisor.Length > 0 && divisor[0] == 0, line, col);

        (double[] quotient, double[] remainder) = Polynomials.Divide(dividend, divisor);
        return Outputs(wanted, Oriented(quotient, asRow), Oriented(remainder, asRow));
    }

    private static void RefuseZeroDivisor(bool zero, int line, int col)
    {
        if (zero)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:deconv:ZeroCoef1",
                "First coefficient of A must be non-zero when the deconvolution method is "
                + "\"long-division\".");
        }
    }

    /// <summary>
    /// <c>C = convn(A, B)</c> and <c>convn(A, B, shape)</c>: convolution over every dimension at
    /// once, which for two matrices is <c>conv2</c> and for two vectors is <c>conv</c>.
    /// </summary>
    private static JgsValue ConvolveN(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("convn", args, 2, 3, line, col);
        ConvolutionShape shape = args.Count >= 3
            ? ConvolutionShapeOf("convn", args, 2, line, col)
            : ConvolutionShape.Full;

        (double[] values, int[] dims) = Convolution.ConvolveN(
            FlattenColumnMajor("convn", args[0], line, col), SizeDims(args[0]),
            FlattenColumnMajor("convn", args[1], line, col), SizeDims(args[1]),
            shape);

        return JgsMatrix.FromColumnMajorDims(values, dims);
    }

    private static ConvolutionShape ConvolutionShapeOf(
        string name, IReadOnlyList<JgsValue> args, int index, int line, int col) =>
        Str(name, args, index, line, col).ToLowerInvariant() switch
        {
            "full" => ConvolutionShape.Full,
            "same" => ConvolutionShape.Same,
            "valid" => ConvolutionShape.Valid,
            _ => throw new JgsRuntimeException(line, col, "MATLAB:conv2:unknownShapeParameter",
                "SHAPE must be 'full', 'same', or 'valid'."),
        };

    // --- Phase and pairing ------------------------------------------------------------------------

    /// <summary>
    /// <c>unwrap(P)</c>, <c>unwrap(P, tol)</c>, <c>unwrap(P, [], dim)</c> and
    /// <c>unwrap(P, tol, dim)</c>: whole turns added where the phase record jumps.
    /// </summary>
    private static JgsValue UnwrapPhase(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("unwrap", args, 1, 3, line, col);
        double cutoff = args.Count >= 2 && !IsPlaceholderArgument(args[1])
            ? Num("unwrap", args, 1, line, col)
            : PhaseSequences.HalfTurn;

        return AlongDimension("unwrap", args, 2, line, col, slice =>
        {
            PhaseSequences.Unwrap(slice, cutoff);
            return slice;
        });
    }

    /// <summary>
    /// <c>cplxpair(A)</c> and its tolerance and dimension forms: conjugate pairs adjacent and
    /// ascending, the purely real values last.
    /// </summary>
    private static JgsValue ComplexPairs(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("cplxpair", args, 1, 3, line, col);
        JgsValue given = args[0];
        if (ElementCount(given) == 0)
        {
            return given;
        }

        double tolerance = PhaseSequences.PairingTolerance;
        if (args.Count >= 2 && !IsPlaceholderArgument(args[1]))
        {
            tolerance = Num("cplxpair", args, 1, line, col);
            if (!(tolerance >= 0 && tolerance < 1))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:cplxpair:WrongTolerance",
                    "Relative tolerance TOL must be a scalar such that 0<=TOL<1.");
            }
        }

        int[] dims = SizeDims(given);
        int dim = args.Count >= 3
            ? DimensionArgument("cplxpair", args, 2, line, col)
            : JgsMatrix.DefaultDim(dims);

        Complex[] flat = ComplexArrayOf("cplxpair", given, line, col);
        (Complex[][] slices, _) = ComplexSlicesAlong(flat, dims, dim);
        for (int i = 0; i < slices.Length; i++)
        {
            try
            {
                slices[i] = PhaseSequences.ConjugatePairs(slices[i], tolerance);
            }
            catch (ArgumentException)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:cplxpair:ComplexValuesPaired",
                    "Complex numbers can't be paired.");
            }
        }

        return ComplexShaped(JoinComplexAlong(slices, dims, dim), dims);
    }

    // --- Planar geometry --------------------------------------------------------------------------

    /// <summary>
    /// <c>polyarea(x, y)</c> and <c>polyarea(x, y, dim)</c>: the area each column of vertices
    /// encloses.
    /// </summary>
    private static JgsValue PolygonArea(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("polyarea", args, 2, 3, line, col);
        int[] dims = SizeDims(args[0]);
        if (!SameShape(dims, SizeDims(args[1])))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:polyarea:XYSizeMismatch",
                "X and Y must be the same size.");
        }

        int dim = args.Count >= 3
            ? DimensionArgument("polyarea", args, 2, line, col)
            : JgsMatrix.DefaultDim(dims);

        double[] x = FlattenColumnMajor("polyarea", args[0], line, col);
        double[] y = FlattenColumnMajor("polyarea", args[1], line, col);
        (double[][] xs, int[] reduced) = JgsMatrix.SlicesAlong(x, dims, dim);
        (double[][] ys, _) = JgsMatrix.SlicesAlong(y, dims, dim);

        var areas = new double[xs.Length];
        for (int i = 0; i < xs.Length; i++)
        {
            areas[i] = PlanarGeometry.PolygonArea(xs[i], ys[i]);
        }

        return areas.Length == 1
            ? JgsValue.Number(areas[0])
            : JgsMatrix.FromColumnMajorDims(areas, reduced);
    }

    /// <summary>
    /// <c>rectint(A, B)</c>: the area each rectangle of A shares with each rectangle of B, one row
    /// per rectangle of A.
    /// </summary>
    private static JgsValue RectangleIntersection(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("rectint", args, 2, line, col);
        double[] a = FlattenColumnMajor("rectint", args[0], line, col);
        double[] b = FlattenColumnMajor("rectint", args[1], line, col);
        int rows = a.Length / 4;
        int cols = b.Length / 4;

        return JgsMatrix.FromColumnMajorDims(
            PlanarGeometry.RectangleOverlaps(a, b), [rows, cols]);
    }

    /// <summary>
    /// <c>in = inpolygon(xq, yq, xv, yv)</c> and <c>[in, on] = …</c>: which query points the polygon
    /// encloses, and which lie on its edge. Both answers keep the query points' own shape.
    /// </summary>
    private static JgsValue[] PointsInPolygon(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Arity("inpolygon", args, 4, line, col);
        if (!IsVectorOrEmpty(args[2]) || !IsVectorOrEmpty(args[3]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:inpolygon:PolygonVecDef",
                "Polygon must be defined by vectors (XV, YV).");
        }

        int[] shape = SizeDims(args[0]);
        double[] qx = FlattenColumnMajor("inpolygon", args[0], line, col);
        double[] qy = FlattenColumnMajor("inpolygon", args[1], line, col);
        (double[] vx, double[] vy) = ClosedLoops(
            FlattenColumnMajor("inpolygon", args[2], line, col),
            FlattenColumnMajor("inpolygon", args[3], line, col),
            line, col);

        (bool[] inside, bool[] on) = PlanarGeometry.InPolygon(qx, qy, vx, vy);
        return Outputs(wanted, PrepMask(inside, shape), PrepMask(on, shape));
    }

    /// <summary>
    /// The polygon with each of its loops closed: a vertex list that does not return to its start
    /// gets one added, and the NaN-separated loops are each closed on their own.
    /// </summary>
    private static (double[] X, double[] Y) ClosedLoops(
        double[] vx, double[] vy, int line, int col)
    {
        bool anyGap = false;
        for (int i = 0; i < vx.Length; i++)
        {
            bool xGap = double.IsNaN(vx[i]);
            if (xGap != double.IsNaN(vy[i]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:inpolygon:InvalidLoopDef",
                    "NaN separators in the polygon definition must be in the same location for "
                    + "both X and Y.");
            }

            anyGap |= xGap;
        }

        var x = new List<double>(vx.Length + 2);
        var y = new List<double>(vy.Length + 2);
        if (!anyGap)
        {
            x.AddRange(vx);
            y.AddRange(vy);
            if (x.Count == 1)
            {
                // A single vertex is a degenerate loop; doubling it gives an edge to walk.
                x.Add(x[0]);
                y.Add(y[0]);
            }

            if (x.Count > 0 && (x[0] != x[^1] || y[0] != y[^1]))
            {
                x.Add(x[0]);
                y.Add(y[0]);
            }

            return (x.ToArray(), y.ToArray());
        }

        // Each NaN-separated run is closed on its own, and the separators are kept so that the
        // winding count treats the loops as disjoint rather than joining them with a phantom edge.
        int start = 0;
        for (int i = 0; i <= vx.Length; i++)
        {
            bool end = i == vx.Length || double.IsNaN(vx[i]);
            if (!end)
            {
                continue;
            }

            if (i > start)
            {
                for (int j = start; j < i; j++)
                {
                    x.Add(vx[j]);
                    y.Add(vy[j]);
                }

                if (vx[start] != vx[i - 1] || vy[start] != vy[i - 1])
                {
                    x.Add(vx[start]);
                    y.Add(vy[start]);
                }

                x.Add(double.NaN);
                y.Add(double.NaN);
            }

            start = i + 1;
        }

        return (x.ToArray(), y.ToArray());
    }

    // --- Shared shaping ---------------------------------------------------------------------------

    /// <summary>Runs a per-slice transform along a dimension, keeping the value's own shape.</summary>
    private static JgsValue AlongDimension(
        string name, IReadOnlyList<JgsValue> args, int dimIndex, int line, int col,
        Func<double[], double[]> transform)
    {
        int[] dims = SizeDims(args[0]);
        int dim = args.Count > dimIndex
            ? DimensionArgument(name, args, dimIndex, line, col)
            : JgsMatrix.DefaultDim(dims);

        double[] flat = FlattenColumnMajor(name, args[0], line, col);
        (double[][] slices, _) = JgsMatrix.SlicesAlong(flat, dims, dim);
        for (int i = 0; i < slices.Length; i++)
        {
            slices[i] = transform(slices[i]);
        }

        (double[] joined, int[] shape) = JgsMatrix.JoinAlong(slices, dims, dim);
        return joined.Length == 1 && dims.Length <= 1
            ? JgsValue.Number(joined[0])
            : JgsMatrix.FromColumnMajorDims(joined, shape);
    }

    /// <summary>A positive whole dimension number, refused with MATLAB's shared identifier.</summary>
    private static int DimensionArgument(
        string name, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        double given = Num(name, args, index, line, col);
        if (!double.IsFinite(given) || given != Math.Floor(given) || given < 1)
        {
            throw new JgsRuntimeException(line, col,
                "MATLAB:getdimarg:dimensionMustBePositiveInteger",
                "Dimension argument must be a positive integer scalar within indexing range.");
        }

        return (int)given;
    }

    /// <summary>Whether an argument slot holds the <c>[]</c> that means "take the default".</summary>
    private static bool IsPlaceholderArgument(JgsValue value) =>
        value.Type == JgsType.Array && value.ArrayLength == 0;

    /// <summary>MATLAB's <c>isvector</c>: two dimensions, one of them singleton, and not empty.</summary>
    private static bool IsVector(JgsValue value)
    {
        int[] dims = SizeDims(value);
        if (dims.Length > 2)
        {
            return false;
        }

        int rows = dims.Length > 0 ? dims[0] : 1;
        int cols = dims.Length > 1 ? dims[1] : 1;
        return (rows == 1 || cols == 1) && rows * cols > 0;
    }

    /// <summary>A vector, or the empty that several of these names accept in a vector's place.</summary>
    private static bool IsVectorOrEmpty(JgsValue value) =>
        IsVector(value) || ElementCount(value) == 0;

    /// <summary>
    /// How many elements a value holds, read from the shape rather than from <c>ArrayLength</c>,
    /// which answers only for a value that really is an array. A scalar reaching these names is
    /// ordinary — <c>conv(2, p)</c> and <c>unwrap(5)</c> are both legal — so the count has to come
    /// from the one reader that covers every type.
    /// </summary>
    private static int ElementCount(JgsValue value)
    {
        int count = 1;
        foreach (int n in SizeDims(value))
        {
            count *= n;
        }

        return count;
    }

    /// <summary>
    /// MATLAB's <c>isrow</c>: one row and any number of columns, so a scalar counts. The sibling
    /// <c>IsRowShaped</c> answers a narrower question — it excludes the scalar — and the two must
    /// stay apart, because <c>conv</c>'s orientation rule turns on exactly that case.
    /// </summary>
    private static bool IsRowVector(JgsValue value)
    {
        int[] dims = SizeDims(value);
        return dims.Length <= 1 || dims[0] == 1;
    }

    private static bool SameShape(int[] a, int[] b)
    {
        int rank = Math.Max(a.Length, b.Length);
        for (int i = 0; i < rank; i++)
        {
            if ((i < a.Length ? a[i] : 1) != (i < b.Length ? b[i] : 1))
            {
                return false;
            }
        }

        return true;
    }

    private static JgsValue Row(double[] values) => Oriented(values, row: true);

    private static JgsValue ComplexRow(Complex[] values) =>
        ComplexShaped(values, [1, values.Length]);

    private static JgsValue ComplexColumn(Complex[] values) =>
        ComplexShaped(values, [values.Length, 1]);

    /// <summary>Complex elements in a given shape, dropping to plain numbers when all are real.</summary>
    private static JgsValue ComplexShaped(Complex[] values, IReadOnlyList<int> dims)
    {
        bool anyImaginary = false;
        foreach (Complex value in values)
        {
            if (value.Imaginary != 0)
            {
                anyImaginary = true;
                break;
            }
        }

        if (!anyImaginary)
        {
            var reals = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                reals[i] = values[i].Real;
            }

            return JgsMatrix.FromColumnMajorDims(reals, dims);
        }

        var boxed = new JgsValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            boxed[i] = JgsValue.ComplexNum(values[i]);
        }

        return JgsMatrix.FromElementsDims(boxed, dims);
    }

    /// <summary><see cref="JgsMatrix.SlicesAlong"/> over complex storage.</summary>
    private static (Complex[][] Slices, int[] ReducedDims) ComplexSlicesAlong(
        Complex[] flat, IReadOnlyList<int> dims, int dim)
    {
        var real = new double[flat.Length];
        var imaginary = new double[flat.Length];
        for (int i = 0; i < flat.Length; i++)
        {
            real[i] = flat[i].Real;
            imaginary[i] = flat[i].Imaginary;
        }

        (double[][] re, int[] reduced) = JgsMatrix.SlicesAlong(real, dims, dim);
        (double[][] im, _) = JgsMatrix.SlicesAlong(imaginary, dims, dim);

        var slices = new Complex[re.Length][];
        for (int s = 0; s < re.Length; s++)
        {
            slices[s] = new Complex[re[s].Length];
            for (int j = 0; j < re[s].Length; j++)
            {
                slices[s][j] = new Complex(re[s][j], im[s][j]);
            }
        }

        return (slices, reduced);
    }

    /// <summary><see cref="JgsMatrix.JoinAlong"/> over complex storage.</summary>
    private static Complex[] JoinComplexAlong(
        Complex[][] slices, IReadOnlyList<int> dims, int dim)
    {
        var re = new double[slices.Length][];
        var im = new double[slices.Length][];
        for (int s = 0; s < slices.Length; s++)
        {
            re[s] = new double[slices[s].Length];
            im[s] = new double[slices[s].Length];
            for (int j = 0; j < slices[s].Length; j++)
            {
                re[s][j] = slices[s][j].Real;
                im[s][j] = slices[s][j].Imaginary;
            }
        }

        (double[] real, _) = JgsMatrix.JoinAlong(re, dims, dim);
        (double[] imaginary, _) = JgsMatrix.JoinAlong(im, dims, dim);

        var joined = new Complex[real.Length];
        for (int i = 0; i < real.Length; i++)
        {
            joined[i] = new Complex(real[i], imaginary[i]);
        }

        return joined;
    }

    /// <summary>The eigenvalues of a square value, which are the roots of its characteristic polynomial.</summary>
    private static Complex[] SquareSpectrum(string name, JgsValue value, int n, int line, int col)
    {
        if (!HasComplexPart(value))
        {
            return JGraph.Numerics.LinearAlgebra.Eigen.Spectrum(
                FlattenColumnMajor(name, value, line, col), n);
        }

        // A 1-by-1 complex is the common case here: poly(2+3i) reads its one argument as a matrix,
        // because a square matrix and a one-element root vector are the same shape and MATLAB
        // resolves the ambiguity towards the matrix.
        Complex[] flat = ComplexArrayOf(name, value, line, col);
        var square = new Complex[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                square[r, c] = flat[(c * n) + r];
            }
        }

        return JGraph.Numerics.LinearAlgebra.ComplexEigen.Values(square);
    }
}
