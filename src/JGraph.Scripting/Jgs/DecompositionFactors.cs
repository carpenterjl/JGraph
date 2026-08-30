using System;
using System.Numerics;
using JGraph.Numerics.LinearAlgebra;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// One matrix, factored, together with everything a later solve needs to know about it.
/// </summary>
/// <remarks>
/// <para>
/// Eleven type names, but far fewer factorizations behind them: three of the names — banded,
/// Hessenberg and permuted triangular — describe a sparsity pattern that a general LU exploits
/// automatically and answers identically for, so they share its code and differ only in what they
/// refuse. What is not shared is the refusing: asking for <c>'chol'</c> of a matrix that is not
/// positive definite has to fail rather than quietly give the LU answer, because the whole reason a
/// caller names a type is to assert something about the matrix.
/// </para>
/// <para>
/// Every solve here takes a flag saying whether it is the conjugate-transposed problem that is
/// wanted. That is what makes <c>dA'\b</c> free: the factors of A are the factors of Aᴴ read the
/// other way round, and nothing needs taking again.
/// </para>
/// </remarks>
internal sealed class DecompositionFactors
{
    private const double Spacing = 2.220446049250313e-16;

    private readonly Complex[,] _matrix;
    private Complex[,]? _lower;
    private Complex[,]? _upper;
    private int[]? _permutation;
    private double[]? _diagonal;
    private HouseholderQr? _qr;
    private double _condition = double.NaN;

    private DecompositionFactors(Complex[,] matrix, string type)
    {
        _matrix = matrix;
        Type = type;
        Rows = matrix.GetLength(0);
        Columns = matrix.GetLength(1);
    }

    /// <summary>Which of the eleven names this factorization answers to.</summary>
    public string Type { get; }

    /// <summary>The rows of the matrix that was factored.</summary>
    public int Rows { get; }

    /// <summary>The columns of the matrix that was factored.</summary>
    public int Columns { get; }

    /// <summary>The numerical rank, which only the two orthogonal types report.</summary>
    public int Rank { get; private set; }

    /// <summary>The reciprocal condition number in the one-norm.</summary>
    public double ReciprocalCondition
    {
        get
        {
            if (!double.IsNaN(_condition))
            {
                return _condition;
            }

            _condition = Reciprocal();
            return _condition;
        }
    }

    /// <summary>Factors <paramref name="a"/> under the named type, refusing what the type forbids.</summary>
    public static DecompositionFactors Take(Complex[,] a, string type, int line, int col)
    {
        int m = a.GetLength(0);
        int n = a.GetLength(1);
        bool square = m == n;

        if (type == "auto")
        {
            type = !square ? "qr" : IsTriangular(a, n) ? "triangular" : "lu";
        }

        var factors = new DecompositionFactors(a, type);
        switch (type)
        {
            case "lu" or "banded" or "hessenberg":
                if (!square)
                {
                    throw Refuse(type switch
                    {
                        "banded" => ("InvalidAForBanded",
                            "Matrix must be square double for decomposition type 'banded'."),
                        "hessenberg" => ("InvalidAForHessenberg",
                            "Hessenberg decomposition requires a dense input matrix that is upper "
                            + "triangular with one lower subdiagonal."),
                        _ => ("InvalidAForLU", "LU decomposition requires the input matrix to be square."),
                    }, line, col);
                }

                if (type == "hessenberg" && !IsHessenbergForm(a, n))
                {
                    throw Refuse(("InvalidAForHessenberg",
                        "Hessenberg decomposition requires a dense input matrix that is upper triangular "
                        + "with one lower subdiagonal."), line, col);
                }

                factors.TakeLu();
                break;

            case "triangular":
                if (!square)
                {
                    throw Refuse(("InvalidAForTriangSquare",
                        "Input matrix must be square for decomposition type 'triangular'."), line, col);
                }

                if (!IsTriangular(a, n))
                {
                    throw Refuse(("InvalidAForTriang",
                        "Input matrix must be triangular. To ignore part of the input matrix, use options "
                        + "'upper' or 'lower' ."), line, col);
                }

                break;

            case "diagonal":
                if (!square || !IsDiagonalForm(a, n))
                {
                    throw Refuse(("InvalidAForDiagonal", "Input matrix must be square diagonal."), line, col);
                }

                break;

            case "permutedTriangular":
                if (!square || !IsPermutedTriangular(a, n))
                {
                    throw Refuse(("InvalidAForPermTriang",
                        "Input matrix must be a permutation of a square triangular matrix."), line, col);
                }

                factors.TakeLu();
                break;

            case "chol":
                if (!square)
                {
                    throw Refuse(("InvalidAForCholSquare",
                        "Cholesky decomposition requires the input matrix to be square, hermitian, and "
                        + "positive definite."), line, col);
                }

                if (!IsHermitianForm(a, n))
                {
                    throw Refuse(("InvalidAForChol",
                        "Cholesky decomposition requires the input matrix to be hermitian and positive "
                        + "definite. Use option 'upper' or 'lower' to ignore part of the input matrix."),
                        line, col);
                }

                if (!factors.TakeCholesky())
                {
                    throw Refuse(("InvalidAForCholSPD",
                        "Cholesky decomposition requires the input matrix to be positive definite."),
                        line, col);
                }

                break;

            case "ldl":
                if (!square)
                {
                    throw Refuse(("InvalidAForLDLSquare",
                        "LDL decomposition requires the input matrix to be square and hermitian."), line, col);
                }

                if (!IsHermitianForm(a, n))
                {
                    throw Refuse(("InvalidAForLDL",
                        "LDL decomposition requires the input matrix to be hermitian. Alternatively, use "
                        + "option 'upper' or 'lower' to ignore part of the input matrix."), line, col);
                }

                if (!factors.TakeLdl())
                {
                    // A hermitian matrix with a nought where a pivot has to go is still solvable; it
                    // is only this factorization that cannot express it, so the general one is used
                    // and the type it was asked for is still what the object reports.
                    factors.TakeLu();
                }

                break;

            case "qr":
            case "cod":
                factors.TakeQr();
                break;

            default:
                throw Refuse(("InvalidA", "Arguments must be matrices of type double or single."), line, col);
        }

        return factors;
    }

    /// <summary>Solves the system, or its conjugate transpose, for every column of <paramref name="b"/>.</summary>
    public Complex[,] Solve(Complex[,] b, bool transposed)
    {
        switch (Type)
        {
            case "triangular":
                return SolveTriangle(_matrix, b, IsUpper(_matrix, Rows), transposed);

            case "diagonal":
            {
                var x = new Complex[Rows, b.GetLength(1)];
                for (int i = 0; i < Rows; i++)
                {
                    Complex pivot = transposed ? Complex.Conjugate(_matrix[i, i]) : _matrix[i, i];
                    for (int c = 0; c < b.GetLength(1); c++)
                    {
                        x[i, c] = b[i, c] / pivot;
                    }
                }

                return x;
            }

            case "chol":
            {
                var y = (Complex[,])b.Clone();
                SolveLowerInPlace(_lower!, y, conjugate: false);
                SolveLowerConjugateInPlace(_lower!, y);
                return y;
            }

            case "ldl" when _diagonal is not null:
            {
                var y = (Complex[,])b.Clone();
                SolveUnitLowerInPlace(_lower!, y, transposed: false);
                for (int i = 0; i < Rows; i++)
                {
                    for (int c = 0; c < y.GetLength(1); c++)
                    {
                        y[i, c] /= _diagonal[i];
                    }
                }

                SolveUnitLowerInPlace(_lower!, y, transposed: true);
                return y;
            }

            case "qr" or "cod":
                return SolveOrthogonal(b, transposed);

            default:
                return SolveLu(b, transposed);
        }
    }

    private Complex[,] SolveLu(Complex[,] b, bool transposed)
    {
        int n = Rows;
        int rhs = b.GetLength(1);
        var y = new Complex[n, rhs];
        if (!transposed)
        {
            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < rhs; c++)
                {
                    y[i, c] = b[_permutation![i], c];
                }
            }

            SolveUnitLowerInPlace(_lower!, y, transposed: false);
            HouseholderQr.SolveUpper(_upper!, n, y);
            return y;
        }

        var z = (Complex[,])b.Clone();
        SolveUpperConjugateInPlace(_upper!, z);
        SolveUnitLowerInPlace(_lower!, z, transposed: true);
        for (int i = 0; i < n; i++)
        {
            for (int c = 0; c < rhs; c++)
            {
                y[_permutation![i], c] = z[i, c];
            }
        }

        return y;
    }

    private Complex[,] SolveOrthogonal(Complex[,] b, bool transposed)
    {
        Complex[,] target = _matrix;
        if (transposed)
        {
            var t = new Complex[Columns, Rows];
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    t[column, row] = Complex.Conjugate(_matrix[row, column]);
                }
            }

            target = t;
        }

        if (Type == "cod")
        {
            return HouseholderQr.MinimumNormSolution(target, b, -1.0, out _);
        }

        // The unpivoted type promises a full-rank matrix, so the leading triangle is the whole
        // answer and no second factorization is needed to square it off.
        HouseholderQr qr = transposed ? HouseholderQr.Factor(target, pivot: false) : _qr!;
        int order = Math.Min(target.GetLength(0), target.GetLength(1));
        Complex[,] applied = qr.ApplyConjugateTranspose(b);
        var head = new Complex[order, b.GetLength(1)];
        for (int i = 0; i < order; i++)
        {
            for (int c = 0; c < b.GetLength(1); c++)
            {
                head[i, c] = applied[i, c];
            }
        }

        Complex[,] r = qr.R(full: false);
        HouseholderQr.SolveUpper(r, order, head);
        if (order == target.GetLength(1))
        {
            return head;
        }

        var x = new Complex[target.GetLength(1), b.GetLength(1)];
        for (int i = 0; i < order; i++)
        {
            for (int c = 0; c < b.GetLength(1); c++)
            {
                x[i, c] = head[i, c];
            }
        }

        return x;
    }

    private void TakeLu()
    {
        int n = Rows;
        var work = (Complex[,])_matrix.Clone();
        var perm = new int[n];
        for (int i = 0; i < n; i++)
        {
            perm[i] = i;
        }

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

                (perm[k], perm[best]) = (perm[best], perm[k]);
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

        _lower = new Complex[n, n];
        _upper = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            _lower[r, r] = Complex.One;
            for (int c = 0; c < n; c++)
            {
                if (c < r)
                {
                    _lower[r, c] = work[r, c];
                }
                else
                {
                    _upper[r, c] = work[r, c];
                }
            }
        }

        _permutation = perm;
    }

    private bool TakeCholesky()
    {
        int n = Rows;
        var l = new Complex[n, n];
        for (int j = 0; j < n; j++)
        {
            Complex pivot = _matrix[j, j];
            for (int k = 0; k < j; k++)
            {
                pivot -= l[j, k] * Complex.Conjugate(l[j, k]);
            }

            if (!(pivot.Real > 0))
            {
                return false;
            }

            double root = Math.Sqrt(pivot.Real);
            l[j, j] = new Complex(root, 0.0);
            for (int i = j + 1; i < n; i++)
            {
                Complex sum = _matrix[i, j];
                for (int k = 0; k < j; k++)
                {
                    sum -= l[i, k] * Complex.Conjugate(l[j, k]);
                }

                l[i, j] = new Complex(sum.Real / root, sum.Imaginary / root);
            }
        }

        _lower = l;
        return true;
    }

    private bool TakeLdl()
    {
        int n = Rows;
        var l = new Complex[n, n];
        var d = new double[n];
        for (int j = 0; j < n; j++)
        {
            Complex pivot = _matrix[j, j];
            for (int k = 0; k < j; k++)
            {
                pivot -= l[j, k] * Complex.Conjugate(l[j, k]) * d[k];
            }

            if (Math.Abs(pivot.Real) <= Spacing * ScaleOf())
            {
                return false;
            }

            d[j] = pivot.Real;
            l[j, j] = Complex.One;
            for (int i = j + 1; i < n; i++)
            {
                Complex sum = _matrix[i, j];
                for (int k = 0; k < j; k++)
                {
                    sum -= l[i, k] * Complex.Conjugate(l[j, k]) * d[k];
                }

                l[i, j] = sum / d[j];
            }
        }

        _lower = l;
        _diagonal = d;
        return true;
    }

    private void TakeQr()
    {
        _qr = HouseholderQr.Factor(_matrix, pivot: Type == "cod");
        double[] diagonal = _qr.DiagonalMagnitudes;
        double largest = 0.0;
        foreach (double value in diagonal)
        {
            largest = Math.Max(largest, value);
        }

        double cut = Math.Max(Rows, Columns) * Spacing * largest;
        Rank = 0;
        foreach (double value in diagonal)
        {
            if (value > cut)
            {
                Rank++;
            }
        }
    }

    /// <summary>
    /// The reciprocal condition number, taken the way <c>rcond</c> itself takes it so that the two
    /// agree — an LU estimate for a real matrix, and the exact ratio for a complex one.
    /// </summary>
    private double Reciprocal()
    {
        int n = Rows;
        if (n == 0)
        {
            return double.PositiveInfinity;
        }

        bool real = true;
        foreach (Complex value in _matrix)
        {
            real &= value.Imaginary == 0;
        }

        if (real)
        {
            var flat = new double[n * n];
            for (int c = 0; c < n; c++)
            {
                for (int r = 0; r < n; r++)
                {
                    flat[(c * n) + r] = _matrix[r, c].Real;
                }
            }

            double anorm = DenseLinalg.OneNorm(n, n, flat, n);
            return LuDecomposition.FactorAdopting(flat, n).ReciprocalCondition(anorm);
        }

        var identity = new Complex[n, n];
        for (int i = 0; i < n; i++)
        {
            identity[i, i] = Complex.One;
        }

        Complex[,] inverse = HouseholderQr.MinimumNormSolution(_matrix, identity, -1.0, out int rank);
        return rank < n
            ? 0.0
            : 1.0 / (NormEstimators.OneNormOf(_matrix) * NormEstimators.OneNormOf(inverse));
    }

    private double ScaleOf()
    {
        double worst = 0.0;
        foreach (Complex value in _matrix)
        {
            worst = Math.Max(worst, value.Magnitude);
        }

        return worst == 0 ? 1.0 : worst;
    }

    private static JgsRuntimeException Refuse((string Key, string Message) what, int line, int col) =>
        new(line, col, $"MATLAB:decomposition:{what.Key}", what.Message);

    private static bool IsTriangular(Complex[,] a, int n) => IsUpper(a, n) || IsLower(a, n);

    private static bool IsUpper(Complex[,] a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            for (int r = c + 1; r < n; r++)
            {
                if (a[r, c] != Complex.Zero)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsLower(Complex[,] a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < c; r++)
            {
                if (a[r, c] != Complex.Zero)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsDiagonalForm(Complex[,] a, int n) => IsUpper(a, n) && IsLower(a, n);

    private static bool IsHessenbergForm(Complex[,] a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            for (int r = c + 2; r < n; r++)
            {
                if (a[r, c] != Complex.Zero)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsHermitianForm(Complex[,] a, int n)
    {
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (a[r, c] != Complex.Conjugate(a[c, r]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the rows can be permuted to leave a triangle: every step some row has exactly one
    /// entry left among the columns not yet spoken for.
    /// </summary>
    private static bool IsPermutedTriangular(Complex[,] a, int n)
    {
        var rowUsed = new bool[n];
        var colUsed = new bool[n];
        for (int step = 0; step < n; step++)
        {
            int foundRow = -1;
            int foundCol = -1;
            for (int r = 0; r < n && foundRow < 0; r++)
            {
                if (rowUsed[r])
                {
                    continue;
                }

                int count = 0;
                int at = -1;
                for (int c = 0; c < n; c++)
                {
                    if (!colUsed[c] && a[r, c] != Complex.Zero)
                    {
                        count++;
                        at = c;
                    }
                }

                if (count == 1)
                {
                    foundRow = r;
                    foundCol = at;
                }
            }

            if (foundRow < 0)
            {
                return false;
            }

            rowUsed[foundRow] = true;
            colUsed[foundCol] = true;
        }

        return true;
    }

    private static Complex[,] SolveTriangle(Complex[,] a, Complex[,] b, bool upper, bool transposed)
    {
        var x = (Complex[,])b.Clone();
        bool goingUp = upper != transposed;
        int n = a.GetLength(0);
        for (int c = 0; c < x.GetLength(1); c++)
        {
            for (int step = 0; step < n; step++)
            {
                int i = goingUp ? n - 1 - step : step;
                Complex sum = x[i, c];
                for (int j = 0; j < n; j++)
                {
                    if ((goingUp && j <= i) || (!goingUp && j >= i))
                    {
                        continue;
                    }

                    Complex entry = transposed ? Complex.Conjugate(a[j, i]) : a[i, j];
                    sum -= entry * x[j, c];
                }

                Complex pivot = transposed ? Complex.Conjugate(a[i, i]) : a[i, i];
                x[i, c] = sum / pivot;
            }
        }

        return x;
    }

    private static void SolveLowerInPlace(Complex[,] l, Complex[,] b, bool conjugate)
    {
        int n = l.GetLength(0);
        for (int c = 0; c < b.GetLength(1); c++)
        {
            for (int i = 0; i < n; i++)
            {
                Complex sum = b[i, c];
                for (int j = 0; j < i; j++)
                {
                    sum -= (conjugate ? Complex.Conjugate(l[i, j]) : l[i, j]) * b[j, c];
                }

                b[i, c] = sum / l[i, i];
            }
        }
    }

    private static void SolveLowerConjugateInPlace(Complex[,] l, Complex[,] b)
    {
        int n = l.GetLength(0);
        for (int c = 0; c < b.GetLength(1); c++)
        {
            for (int i = n - 1; i >= 0; i--)
            {
                Complex sum = b[i, c];
                for (int j = i + 1; j < n; j++)
                {
                    sum -= Complex.Conjugate(l[j, i]) * b[j, c];
                }

                b[i, c] = sum / Complex.Conjugate(l[i, i]);
            }
        }
    }

    private static void SolveUnitLowerInPlace(Complex[,] l, Complex[,] b, bool transposed)
    {
        int n = l.GetLength(0);
        for (int c = 0; c < b.GetLength(1); c++)
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

    private static void SolveUpperConjugateInPlace(Complex[,] u, Complex[,] b)
    {
        int n = u.GetLength(0);
        for (int c = 0; c < b.GetLength(1); c++)
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
