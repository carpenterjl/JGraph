using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The dense linear-algebra trio M52 planned and never reached (M66 wave D): <c>balance</c>, and the
/// generalized Schur pair <c>qz</c> and <c>ordqz</c>.
/// </summary>
/// <remarks>
/// <para>
/// The generalized Schur form is assembled here from two factorizations that already existed rather
/// than from a QZ iteration of its own. For a nonsingular <c>B</c> the pencil's invariant subspaces
/// are those of <c>B⁻¹A</c>: take its real Schur basis <c>Z</c>, factor <c>B·Z = Q'·R</c>, and then
/// <c>Q·B·Z</c> is <c>R</c> — upper triangular by construction — while <c>Q·A·Z</c> is <c>R·T</c>,
/// which is quasi-upper-triangular because an upper triangular matrix times a quasi-upper-triangular
/// one is. Two existing routines, no new iteration, and every relation holds exactly rather than to
/// within a convergence tolerance.
/// </para>
/// <para>
/// What that construction cannot do is a singular <c>B</c>, where the pencil has infinite eigenvalues
/// and only a genuine QZ iteration will reduce it. That case is refused by name rather than answered
/// with the nearest thing, because a factorization of the wrong pencil is worse than no factorization.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    private static void RegisterGeneralizedBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        JgsValue[] Balanced(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            ArityRange("balance", args, 1, 2, line, col);
            double[,] a = SquareOf("balance", args[0], line, col);
            bool permute = args.Count < 2
                || OneWord("balance", args, 1, line, col, "noperm", "perm") != "noperm";

            (double[] scale, double[,] balanced) = Balance(a, permute);
            var diagonal = new double[scale.Length, scale.Length];
            for (int i = 0; i < scale.Length; i++)
            {
                diagonal[i, i] = scale[i];
            }

            // One output is the balanced matrix; two are the similarity and the matrix, in that order,
            // so that T \ A * T reproduces B.
            return wanted <= 1
                ? [FromRect(balanced)]
                : [FromRect(diagonal), FromRect(balanced)];
        }

        Define("balance", (args, line, col) => Balanced(args, 1, line, col)[0], Balanced);

        JgsValue[] GeneralizedSchur(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            ArityRange("qz", args, 2, 3, line, col);
            if (args.Count == 3 && OneWord("qz", args, 2, line, col, "real", "complex") == "complex")
            {
                throw new JgsRuntimeException(line, col,
                    "qz: the complex form triangularizes a conjugate pair into two complex diagonal entries, " +
                    "which JGraph does not do — the real form keeps the pair as a 2-by-2 block.");
            }

            double[,] a = SquareOf("qz", args[0], line, col);
            double[,] b = SquareOf("qz", args[1], line, col);
            if (a.GetLength(0) != b.GetLength(0))
            {
                throw new JgsRuntimeException(line, col,
                    $"qz: the two matrices must be the same size, but got {a.GetLength(0)}x{a.GetLength(1)} " +
                    $"and {b.GetLength(0)}x{b.GetLength(1)}.");
            }

            (double[,] aa, double[,] bb, double[,] q, double[,] z) = Qz(a, b, null, line, col);
            return Outputs(wanted, FromRect(aa), FromRect(bb), FromRect(q), FromRect(z));
        }

        Define("qz", (args, line, col) => GeneralizedSchur(args, 1, line, col)[0], GeneralizedSchur);

        JgsValue[] ReorderedGeneralized(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            Arity("ordqz", args, 5, line, col);
            double[,] aa = SquareOf("ordqz", args[0], line, col);
            double[,] bb = SquareOf("ordqz", args[1], line, col);
            double[,] q = SquareOf("ordqz", args[2], line, col);
            double[,] z = SquareOf("ordqz", args[3], line, col);

            if (IsTextScalar(args[4]))
            {
                throw new JgsRuntimeException(line, col,
                    "ordqz: a region word like 'lhp' or 'udi' names a half-plane or a disc, which JGraph does not " +
                    "read — pass a logical vector saying which eigenvalues to move to the front.");
            }

            // The pencil the caller was handed, put back together: Q·A·Z = AA means A = Qᵀ·AA·Zᵀ.
            double[,] a = Linear.Multiply(Linear.Transpose(q), Linear.Multiply(aa, Linear.Transpose(z)));
            double[,] b = Linear.Multiply(Linear.Transpose(q), Linear.Multiply(bb, Linear.Transpose(z)));

            bool[] select = SelectionFlags("ordqz", args[4], aa.GetLength(0), line, col);
            (double[,] ra, double[,] rb, double[,] rq, double[,] rz) = Qz(a, b, select, line, col);
            return Outputs(wanted, FromRect(ra), FromRect(rb), FromRect(rq), FromRect(rz));
        }

        Define("ordqz", (args, line, col) => ReorderedGeneralized(args, 1, line, col)[0], ReorderedGeneralized);
    }

    private static double[,] SquareOf(string name, JgsValue value, int line, int col)
    {
        double[,] matrix = RectOf(name, value, line, col);
        if (matrix.GetLength(0) != matrix.GetLength(1))
        {
            throw new JgsRuntimeException(line, col,
                $"{name} needs a square matrix, but got {matrix.GetLength(0)}x{matrix.GetLength(1)}.");
        }

        return matrix;
    }

    private static bool[] SelectionFlags(string name, JgsValue value, int n, int line, int col)
    {
        double[] raw = FlattenColumnMajor(name, value, line, col);
        if (raw.Length != n)
        {
            throw new JgsRuntimeException(line, col,
                $"{name}: the selection needs one entry per eigenvalue ({n}), but got {raw.Length}.");
        }

        return Array.ConvertAll(raw, static v => v != 0);
    }

    // --- balance ------------------------------------------------------------------------------

    /// <summary>
    /// Parlett–Reinsch balancing: scale each row and its matching column by the same power of two
    /// until neither dominates. Powers of two are exact in floating point, so the similarity
    /// <c>T⁻¹·A·T</c> introduces no rounding of its own — which is the whole reason a balancing
    /// factor is a power of the radix rather than the square root that would balance perfectly.
    /// </summary>
    /// <remarks>
    /// Only the scaling half is done. MATLAB's <c>balance</c> also permutes rows and columns to push
    /// any already-isolated eigenvalues into a triangular corner, so <c>balance(A)</c> here is what
    /// MATLAB spells <c>balance(A, 'noperm')</c>. The similarity is valid either way — the answer is
    /// a correct <c>T</c> and <c>B</c>, just not the same <c>T</c> and <c>B</c>.
    /// </remarks>
    private static (double[] Scale, double[,] Balanced) Balance(double[,] a, bool permute)
    {
        _ = permute;
        int n = a.GetLength(0);
        var b = (double[,])a.Clone();
        var scale = new double[n];
        Array.Fill(scale, 1.0);

        const double Radix = 2.0;
        const double Squared = Radix * Radix;

        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 100)
        {
            changed = false;
            for (int i = 0; i < n; i++)
            {
                double row = 0;
                double column = 0;
                for (int j = 0; j < n; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    row += Math.Abs(b[i, j]);
                    column += Math.Abs(b[j, i]);
                }

                if (row == 0 || column == 0)
                {
                    continue;
                }

                double factor = 1;
                double scaled = column;
                double before = column + row;

                while (scaled < row / Radix)
                {
                    factor *= Radix;
                    scaled *= Squared;
                }

                while (scaled >= row * Radix)
                {
                    factor /= Radix;
                    scaled /= Squared;
                }

                // Accept only a scaling that genuinely shrinks the pair of norms. Without the margin
                // the loop can trade one imbalance for an equal one and never settle.
                if ((scaled + (row / factor)) >= 0.95 * before)
                {
                    continue;
                }

                changed = true;
                scale[i] *= factor;
                for (int j = 0; j < n; j++)
                {
                    b[i, j] /= factor;
                    b[j, i] *= factor;
                }
            }
        }

        return (scale, b);
    }

    // --- qz -----------------------------------------------------------------------------------

    /// <summary>
    /// The generalized Schur form of a pencil, optionally with chosen eigenvalues moved to the
    /// front. The work is the QZ iteration in <see cref="GeneralizedSchur"/>; what is left here is
    /// the empty case and the shape the callers want their answer in.
    /// </summary>
    /// <remarks>
    /// Until M76 this was assembled from the ordinary Schur form of <c>B⁻¹A</c>, which is exact and
    /// cheap and cannot be done at all when <c>B</c> is singular — the case it refused by name. A
    /// real iteration answers for that pencil too, and the refusal is gone with it.
    /// </remarks>
    private static (double[,] AA, double[,] BB, double[,] Q, double[,] Z) Qz(
        double[,] a, double[,] b, bool[]? select, int line, int col)
    {
        _ = line;
        _ = col;
        if (a.GetLength(0) == 0)
        {
            return (new double[0, 0], new double[0, 0], new double[0, 0], new double[0, 0]);
        }

        GeneralizedSchur qz = GeneralizedSchur.Factor(a, b);
        if (select is not null)
        {
            qz = qz.Reordered(select);
        }

        return (qz.AA, qz.BB, qz.Q, qz.Z);
    }
}
