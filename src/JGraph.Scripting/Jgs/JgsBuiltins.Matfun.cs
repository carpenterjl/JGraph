using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The first thirteen of M107's <c>matfun</c> names: the elimination (<c>rref</c>), the plane
/// rotation and the two factorization updates built on it (<c>planerot</c>, <c>qrinsert</c>,
/// <c>qrdelete</c>), the two conversions between real and complex factorizations (<c>cdf2rdf</c>,
/// <c>rsf2csf</c>), the eigenvalue conditioning (<c>condeig</c>), the three norm and condition
/// estimators (<c>normest</c>, <c>normest1</c>, <c>condest</c>), the Sylvester equation, and the
/// two least-squares solvers (<c>lsqminnorm</c>, <c>lscov</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is written over the factorizations that were already here rather than over
/// arithmetic of its own. <c>condeig</c> is <c>eig</c> and a ratio of lengths; <c>rsf2csf</c> is one
/// rotation per conjugate pair; <c>condest</c> is an LU and a norm estimate. That is not laziness —
/// it is the only way a build can promise that <c>condeig</c>'s eigenvectors are <c>eig</c>'s, or
/// that <c>rref</c> declares a column negligible at the same threshold <c>rank</c> would.
/// </para>
/// <para>
/// Three of them are estimators and say so. <c>normest</c> and <c>normest1</c> answer a norm to
/// within a tolerance rather than exactly, and <c>normest1</c> — and so <c>condest</c> — starts from
/// a block of random signs, which means two runs of it need not agree. Below five rows it does not
/// iterate at all and the answer is exact.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Registers the thirteen names into <paramref name="env"/>.</summary>
    internal static void RegisterMatfunBuiltins(JgsEnvironment env, JGraphScriptGlobals host, Random random)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineBoth(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> both) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => both(args, 1, line, col)[0])
            {
                MultiOutput = both,
            }));

        DefineBoth("rref", (args, wanted, line, col) => ReducedRowEchelon(args, wanted, host, line, col));
        DefineBoth("planerot", PlaneRotation);
        DefineBoth("qrinsert", (args, wanted, line, col) => QrInsert(args, line, col));
        DefineBoth("qrdelete", (args, wanted, line, col) => QrDelete(args, line, col));
        DefineBoth("cdf2rdf", (args, wanted, line, col) => ComplexToRealDiagonal(args, line, col));
        DefineBoth("rsf2csf", (args, wanted, line, col) => RealToComplexSchur(args, line, col));
        DefineBoth("condeig", EigenvalueConditioning);
        DefineBoth("normest", (args, wanted, line, col) => TwoNormEstimate(args, host, random, line, col));
        DefineBoth("normest1", (args, wanted, line, col) => OneNormEstimate(args, random, line, col));
        DefineBoth("condest", (args, wanted, line, col) => ConditionEstimate(args, random, line, col));
        Define("sylvester", SylvesterSolve);
        Define("lsqminnorm", (args, line, col) => MinimumNormLeastSquares(args, host, line, col));
        DefineBoth("lscov", (args, wanted, line, col) => CovarianceLeastSquares(args, wanted, host, line, col));
    }

    // --- rref ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>R = rref(A)</c>, <c>rref(A, tol)</c> and <c>[R, jb] = rref(A)</c>: Gauss-Jordan
    /// elimination to reduced row echelon form, with the pivot columns as a second output.
    /// </summary>
    /// <remarks>
    /// A matrix every entry of which is a ratio of small whole numbers has its answer re-expressed
    /// as such ratios at the end, so that the textbook example comes back as the textbook answer
    /// rather than as a third of a third of a third. That is a real dependency on <c>rat</c>, which
    /// this build only acquired last milestone: before M106 this rounding pass had nothing to run.
    /// </remarks>
    private static JgsValue[] ReducedRowEchelon(
        IReadOnlyList<JgsValue> args, int wanted, JGraphScriptGlobals host, int line, int col)
    {
        _ = host;
        ArityRange("rref", args, 1, 2, line, col);
        Complex[,] a = MatBlock("rref", args[0], line, col);
        double tolerance = args.Count > 1
            ? Num("rref", args, 1, line, col)
            : RowEchelon.DefaultTolerance(a);

        bool rational = LooksRational(a, out double[] flatReal);
        int[] pivots = RowEchelon.Reduce(a, tolerance);
        if (rational)
        {
            Rationalize(a);
        }

        _ = flatReal;
        var jb = new double[pivots.Length];
        for (int i = 0; i < pivots.Length; i++)
        {
            jb[i] = pivots[i];
        }

        return Outputs(wanted, MatValue(a), JgsMatrix.FromColumnMajorDims(jb, [1, pivots.Length]));
    }

    /// <summary>Whether every entry is exactly the ratio its default rational approximation gives.</summary>
    private static bool LooksRational(Complex[,] a, out double[] real)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        real = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                if (a[r, c].Imaginary != 0)
                {
                    return false;
                }

                real[(c * rows) + r] = a[r, c].Real;
            }
        }

        if (real.Length == 0)
        {
            return false;
        }

        (double[] numerators, double[] denominators) = Ratios(real, 1e-6 * FiniteNorm(real));
        for (int i = 0; i < real.Length; i++)
        {
            if (numerators[i] / denominators[i] != real[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Replaces every entry with its own rational approximation, in place.</summary>
    private static void Rationalize(Complex[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var flat = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[(c * rows) + r] = a[r, c].Real;
            }
        }

        (double[] numerators, double[] denominators) = Ratios(flat, 1e-6 * FiniteNorm(flat));
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                int at = (c * rows) + r;
                a[r, c] = new Complex(numerators[at] / denominators[at], 0.0);
            }
        }
    }

    // --- planerot ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>[G, y] = planerot(x)</c>: the rotation that puts a two-element column on its first axis.
    /// </summary>
    private static JgsValue[] PlaneRotation(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("planerot", args, 1, 1, line, col);
        Complex[,] x = MatBlock("planerot", args[0], line, col);
        if (x.GetLength(1) != 1 || x.GetLength(0) != 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:planerot:InputSizeInvalid",
                "First input must be a column vector of length 2.");
        }

        (Complex[,] g, Complex first, Complex second) = PlaneRotations.Plane(x[0, 0], x[1, 0]);
        var y = new Complex[2, 1];
        y[0, 0] = first;
        y[1, 0] = second;
        return Outputs(wanted, MatValue(g), MatValue(y));
    }

    // --- qrinsert / qrdelete ------------------------------------------------------------------------

    /// <summary>
    /// <c>[Q1, R1] = qrinsert(Q, R, j, x)</c> and its <c>'col'</c>/<c>'row'</c> forms: the
    /// factorization of the matrix with one more column or row than the one Q and R came from.
    /// </summary>
    private static JgsValue[] QrInsert(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 4)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrinsert:NotEnoughInputs", "Not enough inputs.");
        }

        ArityRange("qrinsert", args, 4, 5, line, col);
        int j = UpdateIndex("qrinsert", args, 2, line, col);
        bool byRow = Orientation("qrinsert", args, 4, "InvalidInput5", line, col);
        Complex[,] q = MatBlock("qrinsert", args[0], line, col);
        Complex[,] r = MatBlock("qrinsert", args[1], line, col);
        Complex[,] x = MatBlock("qrinsert", args[3], line, col);

        int mq = q.GetLength(0);
        int nq = q.GetLength(1);
        int mr = r.GetLength(0);
        int nr = r.GetLength(1);

        // Nothing to update: the factorization of the inserted piece alone is the whole answer.
        if ((!byRow && nr == 0) || (byRow && mr == 0))
        {
            HouseholderQr fresh = HouseholderQr.Factor(x, pivot: false);
            return [MatValue(fresh.Q(full: true)), MatValue(fresh.R(full: true))];
        }

        if (mq != nq)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrinsert:QNotSquare",
                "The first input matrix must be square.");
        }

        if (nq != mr)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrinsert:InnerDimQRfactors",
                "Inner matrix dimensions must agree. The number of columns of the first input matrix "
                + "must match the number of rows of the second input matrix.");
        }

        if (j <= 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrinsert:NegInsertionIndex",
                "Insertion index must be positive.");
        }

        int mx = x.GetLength(0);
        int nx = x.GetLength(1);
        if (!byRow)
        {
            if (j > nr + 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:qrinsert:InvalidInsertionIndex",
                    "Insertion index exceeds matrix dimensions.");
            }

            if (mx != mq || nx != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:qrinsert:WrongSizeInsertedCol",
                    "Inserted column has incorrect dimensions.");
            }

            (Complex[,] qq, Complex[,] rr) = PlaneRotations.InsertColumn(q, r, j, ColumnFlat(x));
            return [MatValue(qq), MatValue(rr)];
        }

        if (j > mr + 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrinsert:InvalidInsertionIndex",
                "Insertion index exceeds matrix dimensions.");
        }

        if (mx != 1 || nx != nr)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrinsert:WrongSizeInsertedRow",
                "Inserted row has incorrect dimensions.");
        }

        (Complex[,] q2, Complex[,] r2) = PlaneRotations.InsertRow(q, r, j, ColumnFlat(x));
        return [MatValue(q2), MatValue(r2)];
    }

    /// <summary>
    /// <c>[Q1, R1] = qrdelete(Q, R, j)</c> and its <c>'col'</c>/<c>'row'</c> forms: the
    /// factorization of the matrix with one column or row taken out.
    /// </summary>
    private static JgsValue[] QrDelete(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrdelete:NotEnoughInputs", "Not enough inputs.");
        }

        ArityRange("qrdelete", args, 3, 4, line, col);
        int j = UpdateIndex("qrdelete", args, 2, line, col);
        bool byRow = Orientation("qrdelete", args, 3, "InvalidInput4", line, col);
        Complex[,] q = MatBlock("qrdelete", args[0], line, col);
        Complex[,] r = MatBlock("qrdelete", args[1], line, col);

        int mq = q.GetLength(0);
        int nq = q.GetLength(1);
        int m = r.GetLength(0);
        int n = r.GetLength(1);

        if (byRow && mq != nq)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrdelete:QNotSquare",
                "To delete a row, the first input matrix must be square.");
        }

        if (nq != m)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrdelete:InnerDimQRfactors",
                "Inner matrix dimensions must agree. The number of columns of the first input matrix "
                + "must match the number of rows of the second input matrix.");
        }

        if (j <= 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrdelete:NegDeletionIndex",
                "Deletion index must be positive.");
        }

        if (j > (byRow ? m : n))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:qrdelete:InvalidDelIndex",
                "Deletion index exceeds matrix dimensions.");
        }

        (Complex[,] qq, Complex[,] rr) = byRow
            ? PlaneRotations.DeleteRow(q, r, j)
            : PlaneRotations.DeleteColumn(q, r, j);
        return [MatValue(qq), MatValue(rr)];
    }

    /// <summary>The insertion or deletion index, which must be a finite real whole number.</summary>
    private static int UpdateIndex(string name, IReadOnlyList<JgsValue> args, int at, int line, int col)
    {
        JgsValue value = args[at];
        bool scalar = value.Type is JgsType.Number or JgsType.Bool;
        if (!scalar || !double.IsFinite(value.AsNumber) || value.AsNumber != Math.Floor(value.AsNumber))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:InvalidJ",
                "Third input must be a positive integer that is finite and real.");
        }

        return (int)value.AsNumber;
    }

    /// <summary>The trailing <c>'row'</c>/<c>'col'</c> word; anything else is refused by name.</summary>
    private static bool Orientation(
        string name, IReadOnlyList<JgsValue> args, int at, string key, int line, int col)
    {
        if (args.Count <= at)
        {
            return false;
        }

        string word = Str(name, args, at, line, col);
        if (word.Length > 0 && "col".StartsWith(word, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (word.Length > 0 && "row".StartsWith(word, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new JgsRuntimeException(line, col, $"MATLAB:{name}:{key}",
            $"{(key == "InvalidInput5" ? "Fifth" : "Fourth")} input must be 'row' or 'col'.");
    }

    // --- cdf2rdf / rsf2csf --------------------------------------------------------------------------

    /// <summary><c>[V, D] = cdf2rdf(V, D)</c>: a complex diagonal form written as a real block one.</summary>
    private static JgsValue[] ComplexToRealDiagonal(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("cdf2rdf", args, 2, 2, line, col);
        Complex[,] v = MatBlock("cdf2rdf", args[0], line, col);
        Complex[,] d = MatBlock("cdf2rdf", args[1], line, col);
        (Complex[,] V, Complex[,] D)? answer = SchurConversion.ComplexToReal(v, d);
        if (answer is null)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:cdf2rdf:invalidDiagonal",
                "The diagonal of D must be a collection of real eigenvalues and complex conjugate pairs "
                + "(like the output of EIG(X) when X is a real matrix).");
        }

        return [MatValue(answer.Value.V), MatValue(answer.Value.D)];
    }

    /// <summary><c>[U, T] = rsf2csf(U, T)</c>: a real Schur form written as a complex triangular one.</summary>
    private static JgsValue[] RealToComplexSchur(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("rsf2csf", args, 2, 2, line, col);
        Complex[,] u = MatBlock("rsf2csf", args[0], line, col);
        Complex[,] t = MatBlock("rsf2csf", args[1], line, col);
        (Complex[,] uu, Complex[,] tt) = SchurConversion.RealToComplex(u, t);
        return [MatValue(uu), MatValue(tt)];
    }

    // --- condeig ---------------------------------------------------------------------------------

    /// <summary>
    /// <c>s = condeig(A)</c> and <c>[V, D, s] = condeig(A)</c>: how sensitive each eigenvalue is,
    /// measured as the reciprocal of the cosine of the angle between its left and right vectors.
    /// </summary>
    private static JgsValue[] EigenvalueConditioning(
        IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("condeig", args, 1, 1, line, col);

        // Every part of the answer comes from eig itself, so condeig's vectors are eig's vectors and
        // its numbers cannot drift away from what eig would have said about the same matrix.
        JgsValue[] factors = SingleEigen(args[0], 3, asVector: false, line, col);
        Complex[,] x = MatBlock("condeig", factors[0], line, col);
        Complex[,] y = MatBlock("condeig", factors[2], line, col);
        int n = x.GetLength(1);

        var s = new double[n];
        for (int i = 0; i < n; i++)
        {
            double left = 0.0;
            double right = 0.0;
            Complex inner = Complex.Zero;
            for (int r = 0; r < x.GetLength(0); r++)
            {
                left += y[r, i].Real * y[r, i].Real + (y[r, i].Imaginary * y[r, i].Imaginary);
                right += (x[r, i].Real * x[r, i].Real) + (x[r, i].Imaginary * x[r, i].Imaginary);
                inner += Complex.Conjugate(y[r, i]) * x[r, i];
            }

            s[i] = Math.Sqrt(left) * Math.Sqrt(right) / inner.Magnitude;
        }

        JgsValue column = ShapedNumbers(s, [n, 1]);
        return wanted < 2 ? [column] : [factors[0], factors[1], column];
    }

    // --- normest / normest1 / condest ----------------------------------------------------------------

    /// <summary><c>[e, cnt] = normest(S)</c> and <c>normest(S, tol)</c>: the two-norm, estimated.</summary>
    private static JgsValue[] TwoNormEstimate(
        IReadOnlyList<JgsValue> args, JGraphScriptGlobals host, Random random, int line, int col)
    {
        ArityRange("normest", args, 1, 2, line, col);
        Complex[,] s = MatBlock("normest", args[0], line, col);
        double tolerance = args.Count > 1 ? Num("normest", args, 1, line, col) : 1e-6;
        (double estimate, int count, bool stalled) = NormEstimators.TwoNorm(s, tolerance, random);
        if (stalled)
        {
            host.WriteErr("Warning: NORMEST did not converge for 100 iterations with tolerance "
                + tolerance.ToString("G", CultureInfo.InvariantCulture) + "");
        }

        return [JgsValue.Number(estimate), JgsValue.Number(count)];
    }

    /// <summary>
    /// <c>normest1(A)</c>, <c>normest1(A, t)</c>: the one-norm of a matrix or of an operator given
    /// as a function handle, estimated by the block algorithm.
    /// </summary>
    private static JgsValue[] OneNormEstimate(
        IReadOnlyList<JgsValue> args, Random random, int line, int col)
    {
        ArityRange("normest1", args, 1, 3, line, col);
        INormOperand operand = args[0].Type == JgsType.Function
            ? new HandleOperand(args[0], args, line, col)
            : args[0].Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Array
                ? new NormEstimators.MatrixOperand(MatBlock("normest1", args[0], line, col))
                : throw new JgsRuntimeException(line, col, "MATLAB:normest1:ANotMatrixOrFunction",
                    "A must be a matrix or function.");

        int n = operand.Dimension;
        int t = args.Count > 1 && args[1].Type != JgsType.Null ? (int)Num("normest1", args, 1, line, col) : 2;
        t = Math.Abs(t);
        if (t < 1 || t > Math.Max(n, 2))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:normest1:TOutOfRange",
                "T must be an integer between 1 and N = MAX(SIZE(A)).");
        }

        (double estimate, Complex[] v, Complex[] w, int iterations, int products) =
            NormEstimators.OneNorm(operand, t, random);

        // The counts come back as a row when the estimator read the norm off directly and as a
        // column when it iterated. That is not tidy and it is what MATLAB answers: its two exits
        // write `[0 1]` and `[it; nmv]`, one comma apart from each other in the same file.
        int[] shape = iterations == 0 ? [1, 2] : [2, 1];
        return
        [
            JgsValue.Number(estimate),
            MatValue(AsColumn(v)),
            MatValue(AsColumn(w)),
            ShapedNumbers([iterations, products], shape),
        ];
    }

    /// <summary>
    /// <c>c = condest(A)</c>, <c>condest(A, t)</c> and <c>[c, v] = condest(A)</c>: the one-norm
    /// condition number, estimated from an LU factorization and the block one-norm estimator.
    /// </summary>
    private static JgsValue[] ConditionEstimate(
        IReadOnlyList<JgsValue> args, Random random, int line, int col)
    {
        ArityRange("condest", args, 1, 2, line, col);
        Complex[,] a = MatBlock("condest", args[0], line, col);
        int n = a.GetLength(0);
        if (n != a.GetLength(1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:condest:NonSquareMatrix", "Matrix must be square.");
        }

        if (n == 0)
        {
            return [JgsValue.Number(0.0), JgsEmpty.Shaped(0, 1)];
        }

        (Complex[,] lower, Complex[,] upper) = UnitLowerUpper(a, n);
        int singular = -1;
        for (int i = 0; i < n; i++)
        {
            if (upper[i, i] == Complex.Zero)
            {
                singular = i;
                break;
            }
        }

        double condition;
        Complex[] witness;
        if (singular >= 0)
        {
            // A zero pivot settles it: the matrix is singular, and the witness is the combination of
            // the leading columns that the zero column repeats.
            condition = double.PositiveInfinity;
            witness = new Complex[n];
            witness[singular] = Complex.One;
            if (singular > 0)
            {
                var head = new Complex[singular, 1];
                for (int i = 0; i < singular; i++)
                {
                    head[i, 0] = upper[i, singular];
                }

                HouseholderQr.SolveUpper(upper, singular, head);
                for (int i = 0; i < singular; i++)
                {
                    witness[i] = -head[i, 0];
                }
            }
        }
        else
        {
            int t = args.Count > 1 && args[1].Type != JgsType.Null
                ? Math.Abs((int)Num("condest", args, 1, line, col))
                : 2;
            // The witness is the estimator's third answer and not its second: what condest reports
            // is the column of the inverse that attained the norm, not the unit vector that produced
            // it. Reading the wrong one gives a vector that is unit and plausible and wrong.
            (double inverseNorm, _, Complex[] w, _, _) =
                NormEstimators.OneNorm(new InverseOperand(lower, upper), Math.Min(Math.Max(t, 1), Math.Max(n, 2)), random);
            condition = inverseNorm * NormEstimators.OneNormOf(a);
            witness = w;
        }

        double scale = 0.0;
        foreach (Complex value in witness)
        {
            scale += value.Magnitude;
        }

        if (scale != 0)
        {
            for (int i = 0; i < witness.Length; i++)
            {
                witness[i] = new Complex(witness[i].Real / scale, witness[i].Imaginary / scale);
            }
        }

        return [JgsValue.Number(condition), MatValue(AsColumn(witness))];
    }

    /// <summary>An operand that applies the inverse implied by an LU pair, without forming it.</summary>
    private sealed class InverseOperand(Complex[,] lower, Complex[,] upper) : INormOperand
    {
        /// <inheritdoc/>
        public int Dimension => lower.GetLength(0);

        /// <inheritdoc/>
        public bool IsReal
        {
            get
            {
                foreach (Complex value in lower)
                {
                    if (value.Imaginary != 0)
                    {
                        return false;
                    }
                }

                foreach (Complex value in upper)
                {
                    if (value.Imaginary != 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <inheritdoc/>
        public Complex[,] Apply(Complex[,] x)
        {
            var y = (Complex[,])x.Clone();
            SolveLowerUnit(lower, y, transposed: false);
            HouseholderQr.SolveUpper(upper, upper.GetLength(0), y);
            return y;
        }

        /// <inheritdoc/>
        public Complex[,] ApplyConjugateTranspose(Complex[,] x)
        {
            var y = (Complex[,])x.Clone();
            SolveUpperConjugate(upper, y);
            SolveLowerUnit(lower, y, transposed: true);
            return y;
        }
    }

    /// <summary>An operand that reaches a script's own function handle for each product.</summary>
    private sealed class HandleOperand : INormOperand
    {
        private readonly JgsValue _handle;
        private readonly int _line;
        private readonly int _column;

        public HandleOperand(JgsValue handle, IReadOnlyList<JgsValue> args, int line, int col)
        {
            _handle = handle;
            _line = line;
            _column = col;
            _ = args;
            Dimension = (int)Ask("dim", JgsValue.Number(0)).Real;
            IsReal = Ask("real", JgsValue.Number(0)).Real != 0;
        }

        /// <inheritdoc/>
        public int Dimension { get; }

        /// <inheritdoc/>
        public bool IsReal { get; }

        /// <inheritdoc/>
        public Complex[,] Apply(Complex[,] x) => Block("notransp", x);

        /// <inheritdoc/>
        public Complex[,] ApplyConjugateTranspose(Complex[,] x) => Block("transp", x);

        private Complex Ask(string flag, JgsValue argument)
        {
            JgsValue answer = _handle.AsCallable.Call(
                [JgsValue.Str(flag), argument], _line, _column);
            return answer.Type == JgsType.Complex ? answer.AsComplex : new Complex(answer.AsNumber, 0);
        }

        private Complex[,] Block(string flag, Complex[,] x)
        {
            JgsValue answer = _handle.AsCallable.Call(
                [JgsValue.Str(flag), MatValue(x)], _line, _column);
            return MatBlock("normest1", answer, _line, _column);
        }
    }

    // --- sylvester -------------------------------------------------------------------------------

    /// <summary><c>X = sylvester(A, B, C)</c>: the unique X with <c>A·X + X·B = C</c>.</summary>
    private static JgsValue SylvesterSolve(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("sylvester", args, 3, 3, line, col);
        Complex[,] a = MatBlock("sylvester", args[0], line, col);
        Complex[,] b = MatBlock("sylvester", args[1], line, col);
        Complex[,] c = MatBlock("sylvester", args[2], line, col);

        if (a.GetLength(0) != a.GetLength(1) || b.GetLength(0) != b.GetLength(1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sylvester:inputMustBeSquare",
                "First and second inputs must be square.");
        }

        if (a.GetLength(0) != c.GetLength(0) || b.GetLength(0) != c.GetLength(1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:sylvester:inputMustBeCompatibleSize",
                "Inputs must have compatible size.");
        }

        foreach (Complex[,] block in new[] { a, b })
        {
            foreach (Complex value in block)
            {
                if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:sylvester:inputWithNaNInf",
                        "First and second inputs must not contain NaN or Inf.");
                }
            }
        }

        bool anyComplex = IsComplexBlock(a) || IsComplexBlock(b) || IsComplexBlock(c);
        (Complex[,] qa, Complex[,] ta) = Triangularize(a, anyComplex);
        (Complex[,] qb, Complex[,] tb) = Triangularize(b, anyComplex);

        Complex[,] cc = NormEstimators.Product(
            NormEstimators.Product(qa, c, conjugateTranspose: true), qb, conjugateTranspose: false);
        Complex[,] x = SylvesterEquation.SolveTriangular(ta, tb, cc);
        return MatValue(NormEstimators.Product(qa, RightConjugate(x, qb), conjugateTranspose: false));
    }

    /// <summary>
    /// The Schur form a Sylvester solve needs: real and quasi-triangular when everything in sight
    /// is real, complex and strictly triangular when anything is not.
    /// </summary>
    private static (Complex[,] Q, Complex[,] T) Triangularize(Complex[,] a, bool complex)
    {
        int n = a.GetLength(0);
        if (n == 0)
        {
            return (a, a);
        }

        if (IsComplexBlock(a))
        {
            return ComplexEigen.Schur(a);
        }

        var real = new double[n, n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                real[r, c] = a[r, c].Real;
            }
        }

        Schur schur = Schur.Factor(real);
        (Complex[,] q, Complex[,] t) = (Widen(schur.U), Widen(schur.T));
        return complex ? SchurConversion.RealToComplex(q, t) : (q, t);
    }

    // --- lsqminnorm ------------------------------------------------------------------------------

    /// <summary>
    /// <c>X = lsqminnorm(A, B)</c>, <c>lsqminnorm(A, B, tol)</c> and the trailing
    /// <c>'warn'</c>/<c>'nowarn'</c>: the least-squares solution of smallest length.
    /// </summary>
    private static JgsValue MinimumNormLeastSquares(
        IReadOnlyList<JgsValue> args, JGraphScriptGlobals host, int line, int col)
    {
        ArityRange("lsqminnorm", args, 2, 4, line, col);
        Complex[,] a = MatBlock("lsqminnorm", args[0], line, col);
        Complex[,] b = MatBlock("lsqminnorm", args[1], line, col);

        double tolerance = -1.0;
        bool announce = false;
        int at = 2;
        if (at < args.Count && !IsTextScalar(args[at]))
        {
            tolerance = Num("lsqminnorm", args, at, line, col);
            if (!(tolerance >= 0))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:lsqminnorm:InvalidTol",
                    "Tolerance must be a nonnegative scalar number of type double or single.");
            }

            at++;
        }

        if (at < args.Count)
        {
            string word = Str("lsqminnorm", args, at, line, col);
            announce = word.Length > 0 && "warn".StartsWith(word, StringComparison.OrdinalIgnoreCase);
            if (!announce && !(word.Length > 0 && "nowarn".StartsWith(word, StringComparison.OrdinalIgnoreCase)))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:lsqminnorm:InvalidWarn",
                    "Option string must be \"warn\" or \"nowarn\".");
            }

            at++;
        }

        if (at < args.Count)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:lsqminnorm:InvalidOption",
                "Too many input arguments. No additional inputs accepted after option string "
                + "\"warn\" or \"nowarn\".");
        }

        if (b.GetLength(0) != a.GetLength(0))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:decomposition:mldivide",
                "Matrix dimensions must agree.");
        }

        Complex[,] x = HouseholderQr.MinimumNormSolution(a, b, tolerance, out int rank);
        if (announce && rank < Math.Min(a.GetLength(0), a.GetLength(1)))
        {
            double reported = tolerance >= 0
                ? tolerance
                : HouseholderQr.Factor(a, pivot: true).DefaultRankTolerance();
            host.WriteErr("Warning: Rank deficient, rank = "
                + rank.ToString(CultureInfo.InvariantCulture) + ", tol = "
                + reported.ToString("0.000000e+00", CultureInfo.InvariantCulture) + ".");
        }

        return MatValue(x);
    }

    // --- shared marshalling ------------------------------------------------------------------------

    /// <summary>A two-dimensional block of complex numbers, empties and scalars included.</summary>
    private static Complex[,] MatBlock(string name, JgsValue value, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(value);
        if (dims.Length > 2)
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:{name}:inputMustBe2D", "Inputs must be 2-D.");
        }

        // A scalar carries no dimensions of its own — Dims answers the pair it was never given —
        // so its shape is stated here rather than read off. Getting this wrong made lsqminnorm of a
        // scalar right-hand side answer a matrix with no columns.
        bool scalar = value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex;
        int rows = scalar ? 1 : dims.Length > 0 ? dims[0] : 1;
        int cols = scalar ? 1 : dims.Length > 1 ? dims[1] : 1;
        Complex[] flat = ComplexElements(name, value, line, col);
        var a = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                a[r, c] = flat[(c * rows) + r];
            }
        }

        return a;
    }

    /// <summary>A block as a script value, real when every imaginary part is nought.</summary>
    private static JgsValue MatValue(Complex[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        bool complex = IsComplexBlock(a);
        if (!complex)
        {
            var real = new double[rows * cols];
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    real[(c * rows) + r] = a[r, c].Real;
                }
            }

            return real.Length == 1 ? JgsValue.Number(real[0]) : JgsMatrix.FromColumnMajorDims(real, [rows, cols]);
        }

        var flat = new Complex[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[(c * rows) + r] = a[r, c];
            }
        }

        return flat.Length == 1 ? JgsValue.ComplexNum(flat[0]) : ShapedFlatComplex(flat, [rows, cols]);
    }

    /// <summary>Whether any entry has an imaginary part.</summary>
    private static bool IsComplexBlock(Complex[,] a)
    {
        foreach (Complex value in a)
        {
            if (value.Imaginary != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A real matrix widened into complex storage.</summary>
    private static Complex[,] Widen(double[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var wide = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                wide[r, c] = new Complex(a[r, c], 0.0);
            }
        }

        return wide;
    }

    /// <summary>The product <c>M·Qᴴ</c>, which the left-handed product helper cannot express.</summary>
    private static Complex[,] RightConjugate(Complex[,] m, Complex[,] q)
    {
        int rows = m.GetLength(0);
        int inner = m.GetLength(1);
        int cols = q.GetLength(0);
        var y = new Complex[rows, cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                Complex sum = Complex.Zero;
                for (int k = 0; k < inner; k++)
                {
                    sum += m[r, k] * Complex.Conjugate(q[c, k]);
                }

                y[r, c] = sum;
            }
        }

        return y;
    }

    /// <summary>The transpose of a block, without conjugation.</summary>
    private static Complex[,] Transposed(Complex[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var t = new Complex[cols, rows];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                t[c, r] = a[r, c];
            }
        }

        return t;
    }

    /// <summary>A vector as a one-column block.</summary>
    private static Complex[,] AsColumn(Complex[] v)
    {
        var column = new Complex[v.Length, 1];
        for (int i = 0; i < v.Length; i++)
        {
            column[i, 0] = v[i];
        }

        return column;
    }

    /// <summary>Every entry of a block, read down its columns.</summary>
    private static Complex[] ColumnFlat(Complex[,] a)
    {
        int rows = a.GetLength(0);
        int cols = a.GetLength(1);
        var flat = new Complex[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                flat[(c * rows) + r] = a[r, c];
            }
        }

        return flat;
    }

    /// <summary>
    /// The unit lower and upper triangles of an LU factorization with partial pivoting, with the
    /// permutation folded away — which is all a norm estimate of the inverse needs.
    /// </summary>
    private static (Complex[,] Lower, Complex[,] Upper) UnitLowerUpper(Complex[,] a, int n)
    {
        var work = (Complex[,])a.Clone();
        for (int k = 0; k < n; k++)
        {
            int best = k;
            for (int i = k + 1; i < n; i++)
            {
                if (work[i, k].Magnitude > work[best, k].Magnitude)
                {
                    best = i;
                }
            }

            if (best != k)
            {
                for (int c = 0; c < n; c++)
                {
                    (work[k, c], work[best, c]) = (work[best, c], work[k, c]);
                }
            }

            if (work[k, k] == Complex.Zero)
            {
                continue;
            }

            for (int i = k + 1; i < n; i++)
            {
                Complex factor = work[i, k] / work[k, k];
                work[i, k] = factor;
                for (int c = k + 1; c < n; c++)
                {
                    work[i, c] -= factor * work[k, c];
                }
            }
        }

        var lower = new Complex[n, n];
        var upper = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            lower[r, r] = Complex.One;
            for (int c = 0; c < n; c++)
            {
                if (c < r)
                {
                    lower[r, c] = work[r, c];
                }
                else
                {
                    upper[r, c] = work[r, c];
                }
            }
        }

        return (lower, upper);
    }

    /// <summary>Solves <c>L·X = B</c> (or <c>Lᴴ·X = B</c>) in place for a unit lower triangle.</summary>
    private static void SolveLowerUnit(Complex[,] l, Complex[,] b, bool transposed)
    {
        int n = l.GetLength(0);
        int rhs = b.GetLength(1);
        for (int c = 0; c < rhs; c++)
        {
            if (!transposed)
            {
                for (int i = 0; i < n; i++)
                {
                    Complex sum = b[i, c];
                    for (int j = 0; j < i; j++)
                    {
                        sum -= l[i, j] * b[j, c];
                    }

                    b[i, c] = sum;
                }

                continue;
            }

            for (int i = n - 1; i >= 0; i--)
            {
                Complex sum = b[i, c];
                for (int j = i + 1; j < n; j++)
                {
                    sum -= Complex.Conjugate(l[j, i]) * b[j, c];
                }

                b[i, c] = sum;
            }
        }
    }

    /// <summary>Solves <c>Uᴴ·X = B</c> in place for an upper triangle.</summary>
    private static void SolveUpperConjugate(Complex[,] u, Complex[,] b)
    {
        int n = u.GetLength(0);
        int rhs = b.GetLength(1);
        for (int c = 0; c < rhs; c++)
        {
            for (int i = 0; i < n; i++)
            {
                Complex sum = b[i, c];
                for (int j = 0; j < i; j++)
                {
                    sum -= Complex.Conjugate(u[j, i]) * b[j, c];
                }

                b[i, c] = sum / Complex.Conjugate(u[i, i]);
            }
        }
    }
}
