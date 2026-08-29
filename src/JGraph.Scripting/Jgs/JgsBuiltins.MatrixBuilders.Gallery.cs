using System;
using System.Collections.Generic;
using System.Numerics;
using JGraph.Numerics;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>gallery</c> — the Higham test matrices, dispatched by name (M102).
/// </summary>
/// <remarks>
/// <para>
/// Forty-two families are answered here. Every one of them is decided by its arguments alone, so
/// the same call gives the same matrix on any machine and in any session, and each is a formula
/// <see cref="GalleryMatrices"/> writes straight into column-major storage.
/// </para>
/// <para>
/// Sixteen more are refused by name. Ten of them draw their entries from a random stream —
/// <c>rando</c>, <c>randsvd</c>, <c>qmult</c> and their kin — and a matrix drawn from a stream
/// other than MATLAB's is a different matrix, whatever it is called; five answer with a sparse
/// matrix, which is a shape this dispatcher does not build; and <c>condex</c>'s fourth kind rests
/// on an orthonormal basis of a span this milestone did not settle. Each refusal says which of
/// those it is, because a wrong matrix under the right name is worse than no matrix at all.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Families whose entries are drawn rather than computed.</summary>
    private static readonly Dictionary<string, string> DrawnFamilies = new(StringComparer.Ordinal)
    {
        ["cycol"] = "its columns are filled from a random stream",
        ["integerdata"] = "its entries are drawn from a random stream",
        ["normaldata"] = "its entries are drawn from a random stream",
        ["qmult"] = "it multiplies by a random orthogonal matrix",
        ["randcolu"] = "it is built from a random orthogonal factor",
        ["randcorr"] = "it is built from random rotations",
        ["randhess"] = "its rotation angles are drawn from a random stream",
        ["randjorth"] = "it is built from random hyperbolic rotations",
        ["rando"] = "its entries are drawn from a random stream",
        ["randsvd"] = "its singular vectors are drawn from a random stream",
        ["uniformdata"] = "its entries are drawn from a random stream",
        ["wathen"] = "its element densities are drawn from a random stream",
    };

    /// <summary>Families MATLAB answers with a sparse matrix.</summary>
    private static readonly string[] SparseFamilies =
        ["dorr", "neumann", "poisson", "toeppen", "tridiag"];

    /// <summary>
    /// <c>gallery(matrixname, …)</c> and the two numbered matrices, <c>gallery(3)</c> and
    /// <c>gallery(5)</c>.
    /// </summary>
    private static JgsValue[] TestMatrix(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:gallery:invalidMatName",
                "Invalid matrix name.");
        }

        if (!IsTextScalar(args[0]))
        {
            int which = Count("gallery", args, 0, line, col);
            return which switch
            {
                3 => [ShapedReal([-149, 537, -27, -50, 180, -9, -154, 546, -25], 3, 3)],
                5 => [ShapedReal(
                    [-9, 70, -575, 3891, 1024,
                     11, -69, 575, -3891, -1024,
                     -21, 141, -1149, 7782, 2048,
                     63, -421, 3451, -23345, -6144,
                     -252, 1684, -13801, 93365, 24572], 5, 5)],
                _ => throw new JgsRuntimeException(line, col, "MATLAB:gallery:invalidN",
                    "Invalid N in GALLERY(N)."),
            };
        }

        string family = TextOf(args[0]);
        if (DrawnFamilies.TryGetValue(family, out string? why))
        {
            throw new JgsRuntimeException(line, col,
                $"gallery('{family}') is not available here: {why}, and a matrix drawn from a stream"
                + " other than MATLAB's is a different matrix under the same name.");
        }

        if (Array.IndexOf(SparseFamilies, family) >= 0)
        {
            throw new JgsRuntimeException(line, col,
                $"gallery('{family}') is not available here: MATLAB answers it with a sparse matrix,"
                + " which this family builder does not construct.");
        }

        // The class name, when one is given, is the last argument and is never a size or an option.
        (IReadOnlyList<JgsValue> tail, JgsNumericClass? numericClass) =
            GalleryClassTail(args, line, col);
        var p = new List<JgsValue>(tail.Count - 1);
        for (int i = 1; i < tail.Count; i++)
        {
            p.Add(tail[i]);
        }

        JgsValue[] built = Family(family, p, wanted, line, col);
        if (numericClass is { } named)
        {
            for (int i = 0; i < built.Length; i++)
            {
                built[i] = JgsNumericClasses.Stamp(built[i], named);
            }
        }

        return built;
    }

    /// <summary>
    /// Splits a trailing <c>'single'</c> or <c>'double'</c> off a gallery call. Only those two are
    /// classes here; every other trailing word is one of the family's own options.
    /// </summary>
    private static (IReadOnlyList<JgsValue> Remaining, JgsNumericClass? Class) GalleryClassTail(
        IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2 || !IsTextScalar(args[^1]))
        {
            return (args, null);
        }

        string word = TextOf(args[^1]);
        if (Array.IndexOf(FloatClasses, word) < 0)
        {
            return (args, null);
        }

        var rest = new List<JgsValue>(args.Count - 1);
        for (int i = 0; i < args.Count - 1; i++)
        {
            rest.Add(args[i]);
        }

        _ = line;
        _ = col;
        return (rest, JgsNumericClasses.Parse(word));
    }

    /// <summary>One family, its parameters already stripped of the name and the class.</summary>
    private static JgsValue[] Family(
        string family, IReadOnlyList<JgsValue> p, int wanted, int line, int col)
    {
        // Every family reads its parameters through these four, so a missing one always means the
        // documented default and never an argument-count error.
        double Number(int index, double fallback) =>
            index < p.Count ? Num($"gallery('{family}')", p, index, line, col) : fallback;
        int Size(int index, int fallback) =>
            index < p.Count ? Count($"gallery('{family}')", p, index, line, col) : fallback;
        double[] Vector(int index) =>
            NumericVector($"gallery('{family}')", p, index, line, col);
        double[] Spread(int index)
        {
            // A scalar size stands for 1:n wherever a family takes a vector of points.
            double[] raw = Vector(index);
            if (raw.Length != 1)
            {
                return raw;
            }

            var counted = new double[Math.Max((int)raw[0], 0)];
            for (int i = 0; i < counted.Length; i++)
            {
                counted[i] = i + 1;
            }

            return counted;
        }

        void Needs(int count)
        {
            if (p.Count < count)
            {
                throw new JgsRuntimeException(line, col,
                    $"gallery('{family}') needs {count} argument{(count == 1 ? string.Empty : "s")}.");
            }
        }

        switch (family)
        {
            case "binomial":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Binomial(n), n, n)];
            }

            case "cauchy":
            {
                Needs(1);
                double[] x = Spread(0);
                double[] y = p.Count > 1 ? Spread(1) : x;
                return [ShapedReal(GalleryMatrices.Cauchy(x, y), x.Length, y.Length)];
            }

            case "chebspec":
            {
                int n = Size(0, 0);
                bool boundary = Number(1, 0) != 0;
                return [ShapedReal(GalleryMatrices.ChebyshevSpectral(n, boundary), n, n)];
            }

            case "chebvand":
            {
                Needs(1);
                double[] points = p.Count > 1 ? Spread(1) : Spread(0);
                if (p.Count == 1 && Vector(0).Length == 1)
                {
                    points = Spaced(Size(0, 0));
                }
                else if (p.Count > 1 && Vector(1).Length == 1)
                {
                    points = Spaced(Size(1, 0));
                }

                int rows = p.Count > 1 ? Size(0, points.Length) : points.Length;
                return [ShapedReal(
                    GalleryMatrices.ChebyshevVandermonde(rows, points), rows, points.Length)];
            }

            case "chow":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Chow(n, Number(1, 1), Number(2, 0)), n, n)];
            }

            case "circul":
            {
                Needs(1);
                double[] v = Spread(0);
                return [ShapedReal(GalleryMatrices.Circulant(v), v.Length, v.Length)];
            }

            case "clement":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Clement(n, Number(1, 0) != 0), n, n)];
            }

            case "compar":
            {
                Needs(1);
                int[] dims = SizeDims(p[0]);
                double[] a = ToDoubles($"gallery('{family}')", p[0], line, col);
                return [ShapedReal(
                    GalleryMatrices.Comparison(a, dims[0], dims[1], Number(1, 0) != 0),
                    dims[0], dims[1])];
            }

            case "condex":
            {
                int kind = Size(1, 4);
                if (kind == 4)
                {
                    throw new JgsRuntimeException(line, col,
                        "gallery('condex', n, 4) is not available here: it rests on an orthonormal"
                        + " basis of a span this engine has not settled, and a different basis is a"
                        + " different counter-example. Kinds 1, 2 and 3 are available.");
                }

                int n = Size(0, 0);
                return [ShapedReal(
                    GalleryMatrices.ConditionCounterExample(n, kind, Number(2, 100)), n, n)];
            }

            case "dramadah":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Dramadah(n, Size(1, 1)), n, n)];
            }

            case "fiedler":
            {
                Needs(1);
                double[] c = Spread(0);
                return [ShapedReal(GalleryMatrices.Fiedler(c), c.Length, c.Length)];
            }

            case "forsythe":
            {
                int n = Size(0, 0);
                return [ShapedReal(
                    GalleryMatrices.Forsythe(n, Number(1, GalleryMatrices.RootEpsilon), Number(2, 0)),
                    n, n)];
            }

            case "frank":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Frank(n, Number(1, 0) != 0), n, n)];
            }

            case "gcdmat":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.GreatestCommonDivisors(n), n, n)];
            }

            case "gearmat":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Gear(n, Size(1, n), Size(2, -n)), n, n)];
            }

            case "grcar":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Grcar(n, Size(1, 3)), n, n)];
            }

            case "hanowa":
            {
                int n = Size(0, 0);
                if ((n & 1) != 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hanowa:OddN", "N must be even.");
                }

                return [ShapedReal(GalleryMatrices.Hanowa(n, Number(1, -1)), n, n)];
            }

            case "house":
            {
                Needs(1);
                double[] x = Vector(0);
                (double[] v, double beta, double s) = GalleryMatrices.Householder(x, Size(1, 0));
                return Outputs(
                    wanted, ShapedReal(v, v.Length, 1), JgsValue.Number(beta), JgsValue.Number(s));
            }

            case "invhess":
            {
                Needs(1);
                double[] x = Spread(0);
                double[] y;
                if (p.Count > 1)
                {
                    y = Vector(1);
                }
                else
                {
                    y = new double[Math.Max(x.Length - 1, 0)];
                    for (int i = 0; i < y.Length; i++)
                    {
                        y[i] = -x[i];
                    }
                }

                return [ShapedReal(GalleryMatrices.InverseHessenberg(x, y), x.Length, x.Length)];
            }

            case "invol":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Involutory(n), n, n)];
            }

            case "ipjfact":
            {
                int n = Size(0, 0);
                (double[] a, double determinant) =
                    GalleryMatrices.FactorialHankel(n, Number(1, 0) != 0);
                return Outputs(wanted, ShapedReal(a, n, n), JgsValue.Number(determinant));
            }

            case "jordbloc":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.JordanBlock(n, Number(1, 1)), n, n)];
            }

            case "kahan":
            {
                Needs(1);
                double[] shape = Vector(0);
                int rows = (int)shape[0];
                int cols = shape.Length > 1 ? (int)shape[1] : rows;
                return [ShapedReal(
                    GalleryMatrices.Kahan(rows, cols, Number(1, 1.2), Number(2, 1e3)), rows, cols)];
            }

            case "kms":
            {
                int n = Size(0, 0);
                Complex rho = p.Count > 1
                    ? ComplexElements($"gallery('{family}')", p[1], line, col)[0]
                    : new Complex(0.5, 0);
                return [ShapedComplex(GalleryMatrices.KacMurdockSzego(n, rho), n, n)];
            }

            case "krylov":
            {
                if (p.Count < 2)
                {
                    throw new JgsRuntimeException(line, col,
                        "gallery('krylov') without a starting vector is not available here: MATLAB"
                        + " draws one from a random stream. Pass A and x to build it.");
                }

                int[] dims = SizeDims(p[0]);
                if (dims.Length != 2 || dims[0] != dims[1])
                {
                    throw new JgsRuntimeException(line, col,
                        "gallery('krylov') needs a square matrix.");
                }

                double[] a = ToDoubles($"gallery('{family}')", p[0], line, col);
                double[] x = Vector(1);
                int columns = Size(2, dims[0]);
                return [ShapedReal(
                    GalleryMatrices.Krylov(a, dims[0], x, columns), dims[0], columns)];
            }

            case "lauchli":
            {
                int n = Size(0, 0);
                return [ShapedReal(
                    GalleryMatrices.Lauchli(n, Number(1, GalleryMatrices.RootEpsilon)), n + 1, n)];
            }

            case "lehmer":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Lehmer(n), n, n)];
            }

            case "leslie":
            {
                Needs(1);
                double[] births;
                double[] survival;
                if (p.Count > 1)
                {
                    births = Vector(0);
                    survival = Vector(1);
                }
                else
                {
                    int n = Size(0, 0);
                    births = new double[Math.Max(n, 0)];
                    survival = new double[Math.Max(n - 1, 0)];
                    Array.Fill(births, 1);
                    Array.Fill(survival, 1);
                }

                return [ShapedReal(
                    GalleryMatrices.Leslie(births, survival), births.Length, births.Length)];
            }

            case "lesp":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Lesp(n), n, n)];
            }

            case "lotkin":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Lotkin(n), n, n)];
            }

            case "minij":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.MinIndex(n), n, n)];
            }

            case "moler":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Moler(n, Number(1, -1)), n, n)];
            }

            case "orthog":
            {
                int n = Size(0, 0);
                return [ShapedComplex(GalleryMatrices.Orthogonal(n, Size(1, 1)), n, n)];
            }

            case "parter":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Parter(n), n, n)];
            }

            case "pei":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Pei(n, Number(1, 1)), n, n)];
            }

            case "prolate":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Prolate(n, Number(1, 0.25)), n, n)];
            }

            case "redheff":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Redheffer(n), n, n)];
            }

            case "riemann":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Riemann(n), n, n)];
            }

            case "ris":
            {
                int n = Size(0, 0);
                return [ShapedReal(GalleryMatrices.Ris(n), n, n)];
            }

            case "sampling":
            {
                Needs(1);
                double[] x = Spread(0);
                return [ShapedReal(GalleryMatrices.Sampling(x), x.Length, x.Length)];
            }

            case "smoke":
            {
                int n = Size(0, 0);
                return [ShapedComplex(GalleryMatrices.Smoke(n, Number(1, 0) != 0), n, n)];
            }

            case "toeppd":
            {
                if (p.Count < 4)
                {
                    throw new JgsRuntimeException(line, col,
                        "gallery('toeppd') without weights and angles is not available here: they"
                        + " are drawn from a random stream. Pass n, m, w and theta to build it.");
                }

                int n = Size(0, 0);
                return [ShapedReal(
                    GalleryMatrices.ToeplitzPositiveDefinite(n, Vector(2), Vector(3)), n, n)];
            }

            case "triw":
            {
                Needs(1);
                double[] shape = Vector(0);
                int rows = (int)shape[0];
                int cols = shape.Length > 1 ? (int)shape[1] : rows;
                int bands = Size(2, Math.Max(cols - 1, 0));
                return [ShapedReal(
                    GalleryMatrices.UpperTriangularWilkinson(rows, cols, Number(1, -1), bands),
                    rows, cols)];
            }

            case "wilk":
                return Wilk(Size(0, 0), wanted, line, col);

            default:
                throw new JgsRuntimeException(line, col, "MATLAB:gallery:invalidMatName",
                    "Invalid matrix name.");
        }

        // p equally spaced points on the unit interval, which is what a scalar means to chebvand.
        static double[] Spaced(int count)
        {
            var points = new double[Math.Max(count, 0)];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = points.Length == 1 ? 0 : (double)i / (points.Length - 1);
            }

            return points;
        }
    }

    /// <summary>
    /// The four systems Wilkinson devised, each one a specific matrix rather than a family: two
    /// triangular systems that solve badly, a positive definite one cut out of a Hilbert matrix,
    /// and the tridiagonal eigenvalue problem <c>W21+</c>.
    /// </summary>
    private static JgsValue[] Wilk(int which, int wanted, int line, int col)
    {
        switch (which)
        {
            case 3:
                return Outputs(
                    wanted,
                    ShapedReal([1e-10, 0, 0, 0.9, 0.9, 0, -0.4, -0.4, 1e-10], 3, 3),
                    ShapedReal([0, 0, 1], 3, 1));
            case 4:
                return Outputs(
                    wanted,
                    ShapedReal(
                        [0.9143e-4, 0.8762, 0.7943, 0.8017,
                         0, 0.7156e-4, 0.8143, 0.6123,
                         0, 0, 0.9504e-4, 0.7165,
                         0, 0, 0, 0.7123e-4], 4, 4),
                    ShapedReal([0.6524, 0.3127, 0.4186, 0.7853], 4, 1));
            case 5:
            {
                // The 5-by-5 block of the order-6 Hilbert matrix one column to the right, scaled so
                // that its condition number is the one Wilkinson quotes.
                var block = new double[25];
                for (int c = 0; c < 5; c++)
                {
                    for (int r = 0; r < 5; r++)
                    {
                        // The Hilbert entry first and the scale second, which is two roundings
                        // and not one: 1.8144/5 and (1/5)*1.8144 are different doubles.
                        block[(c * 5) + r] = 1.0 / (r + c + 2) * 1.8144;
                    }
                }

                return [ShapedReal(block, 5, 5)];
            }

            case 21:
                return [ShapedReal(TestMatrices.Wilkinson(21), 21, 21)];
            default:
                throw new JgsRuntimeException(line, col,
                    "gallery('wilk', n) is defined for n = 3, 4, 5 and 21.");
        }
    }
}
