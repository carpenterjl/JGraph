using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The Schur decomposition and the things built on it — reading a quasi-triangular matrix's
/// eigenvalues in block order, and moving chosen ones to the top — together with the rank-one
/// updates of a Cholesky or QR factorization.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the Schur family and the rank-one updates (M39).</summary>
    private static void RegisterSchurBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>? multi = null) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body) { MultiOutput = multi }));

        Define("schur",
            (args, line, col) => FromRect(Factorized("schur", args, line, col).T),
            (args, _, line, col) =>
            {
                Schur factored = Factorized("schur", args, line, col);
                return [FromRect(factored.U), FromRect(factored.T)];
            });

        Define("ordeig", (args, line, col) =>
        {
            Arity("ordeig", args, 1, line, col);

            // Unlike eig, this reports the eigenvalues in the order their diagonal blocks appear,
            // which is the whole point: it is how a caller says which ones ordschur should select.
            Complex[] values = Schur.EigenvaluesOf(SquareRect("ordeig", args[0], line, col));
            return JgsValue.Array([.. values.Select(ComplexValue)]);
        }, null);

        Define("ordschur",
            (args, line, col) => FromRect(Reordered(args, line, col).T),
            (args, _, line, col) =>
            {
                Schur reordered = Reordered(args, line, col);
                return [FromRect(reordered.U), FromRect(reordered.T)];
            });

        RegisterRankOneUpdates(Define);
    }

    /// <summary>Factors the argument, checking the optional output-kind word MATLAB allows.</summary>
    private static Schur Factorized(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange(name, args, 1, 2, line, col);

        if (args.Count == 2)
        {
            string kind = Str(name, args, 1, line, col);
            if (kind == "complex")
            {
                // The complex form would need a unitary factor and a triangular one over the
                // complex numbers. JGraph's matrices hold complex entries, but nothing else in the
                // linear-algebra stack works in complex arithmetic, so a complex Schur form would
                // be a factorization no other builtin here could consume.
                throw new JgsRuntimeException(line, col,
                    "schur: only the real Schur form is available; its 2-by-2 blocks hold the conjugate pairs.");
            }

            if (kind != "real")
            {
                throw new JgsRuntimeException(line, col, $"schur: '{kind}' is not 'real' or 'complex'.");
            }
        }

        return Schur.Factor(SquareRect(name, args[0], line, col));
    }

    /// <summary>Reorders a Schur form, reading MATLAB's selection vector or region word.</summary>
    private static Schur Reordered(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("ordschur", args, 3, line, col);
        double[,] u = SquareRect("ordschur", args[0], line, col);
        double[,] t = SquareRect("ordschur", args[1], line, col);
        Complex[] values = Schur.EigenvaluesOf(t);

        // ordschur takes U first and T second; Reorder takes them the other way round.
        return Schur.Reorder(t, u, Selection(args[2], values, line, col));
    }

    /// <summary>
    /// Turns the third argument into one flag per eigenvalue. MATLAB accepts either a logical
    /// vector or one of four region words, and the words are worth having: 'lhp' and 'udi' are how
    /// a stable subspace is asked for, which is most of what ordschur is used for.
    /// </summary>
    private static bool[] Selection(JgsValue value, Complex[] values, int line, int col)
    {
        var select = new bool[values.Length];

        if (value.Type == JgsType.String)
        {
            string region = value.AsString;
            Func<Complex, bool> test = region switch
            {
                "lhp" => static v => v.Real < 0,
                "rhp" => static v => v.Real >= 0,
                "udi" => static v => v.Magnitude < 1,
                "udo" => static v => v.Magnitude >= 1,
                _ => throw new JgsRuntimeException(line, col,
                    $"ordschur: '{region}' is not 'lhp', 'rhp', 'udi', or 'udo'."),
            };

            for (int i = 0; i < values.Length; i++)
            {
                select[i] = test(values[i]);
            }

            return select;
        }

        double[] flags = ToDoubles("ordschur", value, line, col);
        if (flags.Length != values.Length)
        {
            throw new JgsRuntimeException(line, col,
                $"ordschur: the selection needs one entry per eigenvalue ({values.Length}, not {flags.Length}).");
        }

        for (int i = 0; i < flags.Length; i++)
        {
            select[i] = flags[i] != 0;
        }

        return select;
    }

    // --- Rank-one updates -------------------------------------------------------------------------

    private static void RegisterRankOneUpdates(
        Action<string, Func<IReadOnlyList<JgsValue>, int, int, JgsValue>,
            Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]>?> Define)
    {
        Define("cholupdate",
            (args, line, col) =>
            {
                double[,]? updated = Updated(args, line, col);

                // With one output MATLAB raises the error rather than reporting it, because a
                // caller that did not ask for the flag has no way to notice a silent failure.
                return updated is null
                    ? throw new JgsRuntimeException(line, col,
                        "cholupdate: the downdated matrix is not positive definite.")
                    : FromRect(updated);
            },
            (args, wanted, line, col) =>
            {
                double[,]? updated = Updated(args, line, col);
                if (updated is not null)
                {
                    return wanted >= 2 ? [FromRect(updated), JgsValue.Number(0)] : [FromRect(updated)];
                }

                if (wanted < 2)
                {
                    throw new JgsRuntimeException(line, col,
                        "cholupdate: the downdated matrix is not positive definite.");
                }

                // A nonzero flag is MATLAB's way of saying the downdate failed; the factor comes
                // back unchanged so the caller still has something well formed.
                return [FromRect(SquareRect("cholupdate", args[0], line, col)), JgsValue.Number(1)];
            });

        Define("qrupdate",
            (args, line, col) => Update(args, line, col).R is var r ? FromRect(r) : JgsValue.Null,
            (args, _, line, col) =>
            {
                (double[,] q, double[,] r) = Update(args, line, col);
                return [FromRect(q), FromRect(r)];
            });
    }

    /// <summary>
    /// A vector's entries whichever way round it is written. MATLAB's updates take a column, which
    /// in JGraph is a matrix of one-element rows, and the kernels want a plain list of numbers.
    /// </summary>
    private static double[] VectorOf(string name, JgsValue value, int line, int col)
    {
        if (!IsMatrixValue(value))
        {
            return ToDoubles(name, value, line, col);
        }

        double[,] rect = RectOf(name, value, line, col);
        int rows = rect.GetLength(0);
        int columns = rect.GetLength(1);
        if (rows != 1 && columns != 1)
        {
            throw new JgsRuntimeException(line, col, $"{name} needs a vector, but got {rows}x{columns}.");
        }

        var flat = new double[rows * columns];
        int k = 0;
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                flat[k++] = rect[i, j];
            }
        }

        return flat;
    }

    /// <summary>Runs cholupdate's update or downdate, returning null when definiteness is lost.</summary>
    private static double[,]? Updated(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("cholupdate", args, 2, 3, line, col);
        double[,] r = SquareRect("cholupdate", args[0], line, col);
        double[] x = VectorOf("cholupdate", args[1], line, col);

        string sign = args.Count == 3 ? Str("cholupdate", args, 2, line, col) : "+";
        return sign switch
        {
            "+" => RankOneUpdates.CholeskyUpdate(r, x),
            "-" => RankOneUpdates.CholeskyDowndate(r, x),
            _ => throw new JgsRuntimeException(line, col, $"cholupdate: the sign must be '+' or '-', not '{sign}'."),
        };
    }

    /// <summary>Runs qrupdate, which needs the full square Q rather than the economy factor.</summary>
    private static (double[,] Q, double[,] R) Update(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Arity("qrupdate", args, 4, line, col);
        return RankOneUpdates.QrUpdate(
            SquareRect("qrupdate", args[0], line, col),
            RectOf("qrupdate", args[1], line, col),
            VectorOf("qrupdate", args[2], line, col),
            VectorOf("qrupdate", args[3], line, col));
    }
}
