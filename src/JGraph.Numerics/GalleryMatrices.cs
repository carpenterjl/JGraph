using System;
using System.Numerics;

namespace JGraph.Numerics;

/// <summary>
/// The Higham test matrices MATLAB reaches through <c>gallery</c>, as far as they are decided by
/// their arguments alone.
/// </summary>
/// <remarks>
/// <para>
/// Every family here is a formula, and each is written as one: an entry is computed from its own
/// two indices and the family's parameters, into column-major storage. Nothing is assembled from
/// smaller matrices and nothing is factorised, so a family costs one pass over its own entries.
/// </para>
/// <para>
/// The families this class does not hold are the ones whose entries are drawn rather than computed
/// — <c>rando</c>, <c>randsvd</c>, <c>qmult</c> and their kin — and the ones MATLAB answers with a
/// sparse matrix. Both groups are refused by name at the scripting surface rather than approximated
/// here, because a matrix drawn from a different stream is a different matrix and saying otherwise
/// would be worse than saying nothing.
/// </para>
/// </remarks>
public static class GalleryMatrices
{
    /// <summary>The square root of the machine epsilon, which several families default to.</summary>
    public const double RootEpsilon = 1.4901161193847656e-08;

    // ---- families over one size -----------------------------------------------------------------

    /// <summary>
    /// The binomial matrix: integer entries whose square is <c>2^(n−1)</c> times the identity, so
    /// scaling it by <c>2^((1−n)/2)</c> gives a matrix that is its own inverse.
    /// </summary>
    public static double[] Binomial(int n)
    {
        // A(i,j) = sum_k (-1)^k C(i-1,k) C(n-i, j-1-k), which is the coefficient extraction of
        // (1-x)^(i-1) (1+x)^(n-i). Both binomial rows are built once and reused down the column.
        var result = new double[n * n];
        var left = new double[n];
        var right = new double[n];
        for (int i = 0; i < n; i++)
        {
            Row(left, i);
            Row(right, n - 1 - i);
            for (int j = 0; j < n; j++)
            {
                double total = 0;
                for (int k = 0; k <= j && k <= i; k++)
                {
                    double term = left[k] * right[j - k];
                    total += (k & 1) == 0 ? term : -term;
                }

                result[(j * n) + i] = total;
            }
        }

        return result;

        // The m-th row of Pascal's triangle, in place.
        static void Row(double[] into, int m)
        {
            Array.Clear(into);
            into[0] = 1;
            for (int k = 1; k <= m; k++)
            {
                into[k] = into[k - 1] * (m - k + 1) / k;
            }
        }
    }

    /// <summary>
    /// The Cauchy matrix <c>1/(x(i) + y(j))</c>.
    /// </summary>
    public static double[] Cauchy(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        int rows = x.Length;
        int cols = y.Length;
        var result = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                result[(c * rows) + r] = 1.0 / (x[r] + y[c]);
            }
        }

        return result;
    }

    /// <summary>
    /// The Chebyshev spectral differentiation matrix on <paramref name="n"/> points. With
    /// <paramref name="boundary"/> false it is nilpotent and annihilates the constant vector; with
    /// it true the first point is dropped from an <c>(n+1)</c>-point matrix instead, which removes
    /// the null vector and leaves it well conditioned.
    /// </summary>
    public static double[] ChebyshevSpectral(int n, bool boundary)
    {
        // One construction serves both: build the (m+1)-point differentiation matrix and keep either
        // all of it or all but its first row and column.
        int points = boundary ? n + 1 : n;
        int last = points - 1;
        var x = new double[points];
        for (int i = 0; i < points; i++)
        {
            x[i] = CosineOfDegrees(180.0 * i / last);
        }

        // The whole matrix first, then the block that is kept. The closed form is exact once the
        // grid is: with the middle point at zero and the quarter turns exact, -x/(2(1-x²)) lands on
        // the answer rather than near it.
        var full = new double[points * points];
        for (int i = 0; i < points; i++)
        {
            for (int j = 0; j < points; j++)
            {
                double entry;
                if (i != j)
                {
                    double ci = i == 0 || i == last ? 2 : 1;
                    double cj = j == 0 || j == last ? 2 : 1;
                    entry = ci / cj / (x[i] - x[j]);
                    if (((i + j) & 1) != 0)
                    {
                        entry = -entry;
                    }
                }
                else if (i == 0 || i == last)
                {
                    entry = (((2.0 * last * last) + 1) / 6) * (i == 0 ? 1 : -1);
                }
                else
                {
                    entry = -x[i] / (2 * (1 - (x[i] * x[i])));
                }

                full[(j * points) + i] = entry;
            }
        }

        if (!boundary)
        {
            return full;
        }

        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                result[(c * n) + r] = full[((c + 1) * points) + r + 1];
            }
        }

        return result;
    }

    /// <summary>The cosine of an angle in degrees, reduced exactly rather than through π/180.</summary>
    /// <remarks>
    /// <c>cos(k·π/N)</c> computed the obvious way is off by an ulp for over half the grid points a
    /// Chebyshev construction asks for, because <c>k·π/N</c> is already rounded before the cosine
    /// sees it. Splitting π/180 in two and carrying the product's own rounding error alongside it
    /// takes that to under a fifth, and makes the quarter-turns exact.
    /// </remarks>
    public static double CosineOfDegrees(double degrees)
    {
        const double Radian = 0.017453292519943295;          // π/180, rounded
        const double RadianRest = 2.9486522708701687e-19;    // and what rounding left behind

        double a = Math.IEEERemainder(degrees, 360);
        a = Math.Abs(a);
        double sign = 1;
        if (a > 90)
        {
            a = 180 - a;
            sign = -1;
        }

        if (a == 90)
        {
            return 0;
        }

        if (a == 0)
        {
            return sign;
        }

        double scaled = a * Radian;
        double residue = Math.FusedMultiplyAdd(a, Radian, -scaled) + (a * RadianRest);
        return sign * (Math.Cos(scaled) - (Math.Sin(scaled) * residue));
    }

    /// <summary>The sine of an angle in degrees, by the same reduction.</summary>
    public static double SineOfDegrees(double degrees) => CosineOfDegrees(90 - degrees);

    /// <summary>
    /// The <paramref name="k"/>-th power of the <paramref name="n"/>-th root of unity, taken round
    /// the circle in degrees so that the quarter turns are exactly <c>±1</c> and <c>±i</c>.
    /// </summary>
    public static Complex UnitRoot(double k, int n)
    {
        double degrees = 360 * k / n;
        return new Complex(CosineOfDegrees(degrees), SineOfDegrees(degrees));
    }

    /// <summary>
    /// The Chebyshev–Vandermonde matrix: entry (i, j) is the Chebyshev polynomial of degree
    /// <c>i − 1</c> evaluated at <c>points[j]</c>.
    /// </summary>
    public static double[] ChebyshevVandermonde(int rows, ReadOnlySpan<double> points)
    {
        int cols = points.Length;
        var result = new double[rows * cols];

        // The three-term recurrence T(k+1) = 2xT(k) − T(k−1), one column at a time.
        for (int c = 0; c < cols; c++)
        {
            double x = points[c];
            double previous = 1;
            double current = x;
            for (int r = 0; r < rows; r++)
            {
                if (r == 0)
                {
                    result[c * rows] = 1;
                    continue;
                }

                result[(c * rows) + r] = current;
                (previous, current) = (current, (2 * x * current) - previous);
            }
        }

        return result;
    }

    /// <summary>
    /// The Chow matrix: a lower Hessenberg Toeplitz matrix with <c>alpha^(i−j+1)</c> on and below
    /// the superdiagonal, plus <paramref name="delta"/> down the diagonal.
    /// </summary>
    public static double[] Chow(int n, double alpha, double delta)
    {
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                int power = r - c + 1;
                double entry = power >= 0 ? Math.Pow(alpha, power) : 0;
                if (r == c)
                {
                    entry += delta;
                }

                result[(c * n) + r] = entry;
            }
        }

        return result;
    }

    /// <summary>The circulant matrix whose first row is <paramref name="v"/>.</summary>
    public static double[] Circulant(ReadOnlySpan<double> v)
    {
        int n = v.Length;
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                result[(c * n) + r] = v[(((c - r) % n) + n) % n];
            }
        }

        return result;
    }

    /// <summary>
    /// The Clement matrix: tridiagonal with a zero diagonal and eigenvalues that are the integers
    /// <c>±(n−1), ±(n−3), …</c>. <paramref name="symmetric"/> picks the diagonally similar
    /// symmetric form.
    /// </summary>
    public static double[] Clement(int n, bool symmetric)
    {
        var result = new double[n * n];
        for (int i = 0; i + 1 < n; i++)
        {
            double below = n - 1 - i;   // sub-diagonal counts down
            double above = i + 1;       // super-diagonal counts up
            if (symmetric)
            {
                below = above = Math.Sqrt(below * above);
            }

            result[(i * n) + i + 1] = below;
            result[((i + 1) * n) + i] = above;
        }

        return result;
    }

    /// <summary>
    /// The comparison matrix of <paramref name="a"/>: the diagonal keeps its magnitude and
    /// everything off it is made negative. With <paramref name="rowMaximum"/> set, each off-diagonal
    /// entry becomes minus the largest magnitude in its own row instead of its own.
    /// </summary>
    public static double[] Comparison(ReadOnlySpan<double> a, int rows, int cols, bool rowMaximum)
    {
        var result = new double[rows * cols];

        // A triangular matrix stays triangular: the row maximum is only spread over the entries that
        // were already there, so a zero on the wrong side of the diagonal is left alone.
        bool upper = true;
        bool lower = true;
        for (int c = 0; c < cols && (upper || lower); c++)
        {
            for (int r = 0; r < rows; r++)
            {
                if (a[(c * rows) + r] == 0)
                {
                    continue;
                }

                if (r > c)
                {
                    upper = false;
                }
                else if (r < c)
                {
                    lower = false;
                }
            }
        }

        for (int r = 0; r < rows; r++)
        {
            double largest = 0;
            if (rowMaximum)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (c != r)
                    {
                        largest = Math.Max(largest, Math.Abs(a[(c * rows) + r]));
                    }
                }
            }

            for (int c = 0; c < cols; c++)
            {
                if (r == c)
                {
                    result[(c * rows) + r] = Math.Abs(a[(c * rows) + r]);
                    continue;
                }

                bool keep = !rowMaximum
                    || (!upper && !lower)
                    || (upper && c > r)
                    || (lower && c < r);
                double magnitude = rowMaximum ? largest : Math.Abs(a[(c * rows) + r]);
                result[(c * rows) + r] = keep ? -magnitude : 0;
            }
        }

        return result;
    }

    /// <summary>
    /// A counter-example to a matrix condition estimator, of the <paramref name="kind"/> the
    /// estimator it defeats is numbered by, padded out to order <paramref name="n"/> with an
    /// identity.
    /// </summary>
    public static double[] ConditionCounterExample(int n, int kind, double theta)
    {
        var result = new double[n * n];
        for (int i = 0; i < n; i++)
        {
            result[(i * n) + i] = 1; // the padding, overwritten where the family reaches
        }

        void Set(int r, int c, double value)
        {
            if (r < n && c < n)
            {
                result[(c * n) + r] = value;
            }
        }

        switch (kind)
        {
            case 1:
                Set(0, 0, 1); Set(0, 1, -1); Set(0, 2, -2 * theta); Set(0, 3, 0);
                Set(1, 0, 0); Set(1, 1, 1); Set(1, 2, theta); Set(1, 3, -theta);
                Set(2, 0, 0); Set(2, 1, 1); Set(2, 2, theta + 1); Set(2, 3, -(theta + 1));
                Set(3, 0, 0); Set(3, 1, 0); Set(3, 2, 0); Set(3, 3, theta);
                break;
            case 2:
                Set(0, 0, 1); Set(0, 1, 1 - (2 / (theta * theta))); Set(0, 2, -2);
                Set(1, 0, 0); Set(1, 1, 1 / theta); Set(1, 2, -1 / theta);
                Set(2, 0, 0); Set(2, 1, 0); Set(2, 2, 1);
                break;
            default:
                // Lower triangular with a unit diagonal and minus ones below it, save for the last
                // row, which is minus ones throughout. Independent of theta.
                for (int c = 0; c < n; c++)
                {
                    for (int r = 0; r < n; r++)
                    {
                        result[(c * n) + r] = r < c ? 0 : r == c ? 1 : -1;
                    }
                }

                for (int c = 0; c < n; c++)
                {
                    result[(c * n) + n - 1] = -1;
                }

                break;
        }

        return result;
    }

    /// <summary>
    /// A matrix of zeros and ones whose inverse has large integer entries, of the
    /// <paramref name="kind"/> named in the family's documentation: Toeplitz, upper triangular
    /// Toeplitz, or the lower Hessenberg one whose determinant is a Fibonacci number.
    /// </summary>
    public static double[] Dramadah(int n, int kind)
    {
        var column = new double[n];
        var row = new double[n];
        switch (kind)
        {
            case 2:
                // Upper triangular: a single one in the column, and ones at the even places along
                // the row after the first.
                column[0] = 1;
                for (int j = 0; j < n; j++)
                {
                    row[j] = j == 0 || (j & 1) == 1 ? 1 : 0;
                }

                break;
            case 3:
                // Lower Hessenberg: ones at the odd places down the column, one superdiagonal.
                for (int i = 0; i < n; i++)
                {
                    column[i] = (i & 1) == 0 ? 1 : 0;
                }

                row[0] = 1;
                if (n > 1)
                {
                    row[1] = 1;
                }

                break;
            default:
                // The anti-Hadamard Toeplitz matrix: the row carries ones at offsets 0, 1 and 3, and
                // the column repeats with period four.
                for (int j = 0; j < n; j++)
                {
                    row[j] = j is 0 or 1 or 3 ? 1 : 0;
                }

                for (int i = 0; i < n; i++)
                {
                    column[i] = (i + 1) % 4 is 0 or 1 ? 1 : 0;
                }

                break;
        }

        return TestMatrices.Toeplitz(column, row);
    }

    /// <summary>The Fiedler matrix <c>|c(i) − c(j)|</c>.</summary>
    public static double[] Fiedler(ReadOnlySpan<double> c)
    {
        int n = c.Length;
        var result = new double[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                result[(j * n) + i] = Math.Abs(c[i] - c[j]);
            }
        }

        return result;
    }

    /// <summary>
    /// A Jordan block with eigenvalue <paramref name="lambda"/> whose bottom-left corner has been
    /// pushed to <paramref name="alpha"/>, which is enough to spread the eigenvalue into a ring.
    /// </summary>
    public static double[] Forsythe(int n, double alpha, double lambda)
    {
        double[] result = JordanBlock(n, lambda);
        if (n > 0)
        {
            result[n - 1] = alpha;
        }

        return result;
    }

    /// <summary>
    /// The Frank matrix: upper Hessenberg with determinant one and reciprocal pairs of eigenvalues.
    /// <paramref name="reflected"/> flips it about the anti-diagonal.
    /// </summary>
    public static double[] Frank(int n, bool reflected)
    {
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                int i = reflected ? n - 1 - c : r;
                int j = reflected ? n - 1 - r : c;
                result[(c * n) + r] = j >= i - 1 ? n - Math.Max(i, j) : 0;
            }
        }

        return result;
    }

    /// <summary>The matrix of greatest common divisors of the indices.</summary>
    public static double[] GreatestCommonDivisors(int n)
    {
        var result = new double[n * n];
        for (int j = 1; j <= n; j++)
        {
            for (int i = 1; i <= j; i++)
            {
                double d = Gcd(i, j);
                result[((j - 1) * n) + i - 1] = d;
                result[((i - 1) * n) + j - 1] = d;
            }
        }

        return result;

        static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }

            return a;
        }
    }

    /// <summary>
    /// The Gear matrix: ones on both first off-diagonals, and one signed entry in each of the first
    /// and last rows, placed by <paramref name="first"/> and <paramref name="last"/>.
    /// </summary>
    public static double[] Gear(int n, int first, int last)
    {
        var result = new double[n * n];
        for (int i = 0; i + 1 < n; i++)
        {
            result[((i + 1) * n) + i] = 1;
            result[(i * n) + i + 1] = 1;
        }

        if (first != 0)
        {
            result[(Math.Abs(first) - 1) * n] = Math.Sign(first);
        }

        if (last != 0)
        {
            result[((n - Math.Abs(last)) * n) + n - 1] = Math.Sign(last);
        }

        return result;
    }

    /// <summary>
    /// The Grcar matrix: minus ones below the diagonal, ones on it, and <paramref name="bands"/>
    /// superdiagonals of ones. Its eigenvalues move a long way for a small change.
    /// </summary>
    public static double[] Grcar(int n, int bands)
    {
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                int offset = c - r;
                result[(c * n) + r] = offset == -1 ? -1 : offset >= 0 && offset <= bands ? 1 : 0;
            }
        }

        return result;
    }

    /// <summary>
    /// The Hanowa matrix of even order <paramref name="n"/>, whose eigenvalues all sit on the
    /// vertical line through <paramref name="d"/>.
    /// </summary>
    public static double[] Hanowa(int n, double d)
    {
        int half = n / 2;
        var result = new double[n * n];
        // Written as the four products MATLAB writes — d*I, -diag(1:m), diag(1:m), d*I — so that a
        // zero off the diagonal of a negated block is a negative zero, as it is there.
        for (int c = 0; c < half; c++)
        {
            for (int r = 0; r < half; r++)
            {
                double unit = r == c ? 1 : 0;
                double counted = r == c ? r + 1 : 0;
                result[(c * n) + r] = d * unit;
                result[((half + c) * n) + half + r] = d * unit;
                result[((half + c) * n) + r] = -counted;
                result[(c * n) + half + r] = counted;
            }
        }

        return result;
    }

    /// <summary>
    /// The Householder vector, its scale and the multiple of the first axis it sends
    /// <paramref name="x"/> to. <paramref name="sign"/> 0 takes the numerically safe direction,
    /// 1 the other one, and 2 forces a positive result.
    /// </summary>
    public static (double[] V, double Beta, double S) Householder(double[] x, int sign)
    {
        int n = x.Length;
        double norm = 0;
        for (int i = 0; i < n; i++)
        {
            norm += x[i] * x[i];
        }

        norm = Math.Sqrt(norm);
        if (norm == 0)
        {
            return (new double[n], 1, 0);
        }

        double first = x[0];
        double direction = sign switch
        {
            1 => first == 0 ? 1 : Math.Sign(first),
            2 => 1,
            _ => first == 0 ? -1 : -Math.Sign(first),
        };

        double s = direction * norm;
        var v = (double[])x.Clone();

        // x(1) − s cancels whenever the two share a sign, which is exactly what k = 1 and k = 2 ask
        // for. The tail's own norm gives the same difference without the subtraction.
        if (first * s > 0)
        {
            double tail = 0;
            for (int i = 1; i < n; i++)
            {
                tail += x[i] * x[i];
            }

            v[0] = -tail / (first + s);
        }
        else
        {
            v[0] = first - s;
        }

        // v'v is exactly −2·s·v(1) for this v, so the scale needs no second pass and no squares
        // large enough to overflow.
        return v[0] == 0 && AllZero(v) ? (new double[n], 1, first) : (v, -1 / (s * v[0]), s);

        static bool AllZero(double[] values)
        {
            foreach (double value in values)
            {
                if (value != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// A matrix whose inverse is upper Hessenberg: its lower triangle is constant down each column,
    /// and its strict upper triangle constant across each row.
    /// </summary>
    public static double[] InverseHessenberg(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        int n = x.Length;
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                result[(c * n) + r] = r >= c ? x[c] : y[r];
            }
        }

        return result;
    }

    /// <summary>
    /// An involutory matrix — its own inverse — made by scaling the rows and first column of the
    /// Hilbert matrix.
    /// </summary>
    public static double[] Involutory(int n)
    {
        double[] result = Hilbert(n);
        double d = -n;
        for (int r = 0; r < n; r++)
        {
            result[r] *= d;
        }

        for (int i = 1; i < n; i++)
        {
            d = -(n + i) * (n - i) * d / ((double)i * i);
            for (int c = 0; c < n; c++)
            {
                result[(c * n) + i] *= d;
            }
        }

        return result;
    }

    /// <summary>The Hilbert matrix <c>1/(i + j − 1)</c>.</summary>
    public static double[] Hilbert(int n)
    {
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                result[(c * n) + r] = 1.0 / (r + c + 1);
            }
        }

        return result;
    }

    /// <summary>
    /// The Hankel matrix of factorials — <c>(i+j)!</c>, or its reciprocal when
    /// <paramref name="reciprocal"/> is set — together with its determinant, which is known in
    /// closed form and is returned rather than computed from the matrix.
    /// </summary>
    public static (double[] A, double Determinant) FactorialHankel(int n, bool reciprocal)
    {
        var result = new double[n * n];

        // (i+j)! along each anti-diagonal, stepped rather than re-multiplied.
        var factorial = new double[(2 * n) + 1];
        factorial[0] = 1;
        for (int k = 1; k < factorial.Length; k++)
        {
            factorial[k] = factorial[k - 1] * k;
        }

        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                double value = factorial[r + c + 2];
                result[(c * n) + r] = reciprocal ? 1 / value : value;
            }
        }

        // The determinant, by the ratio of consecutive ones: each step's ratio is the previous
        // step's times a square. Multiplying the matrix out would lose the exactness the closed
        // form has.
        double determinant = 1;
        double ratio = reciprocal ? 0.5 : 2;
        for (int k = 1; k <= n; k++)
        {
            if (k > 1)
            {
                double scale = (4.0 * k) - 2;
                ratio = reciprocal ? -ratio / (scale * scale) : ratio * ((double)(k * k) - 1);
            }

            determinant *= ratio;
        }

        return (result, n == 0 ? 1 : determinant);
    }

    /// <summary>A Jordan block of order <paramref name="n"/> with the given eigenvalue.</summary>
    public static double[] JordanBlock(int n, double lambda)
    {
        var result = new double[n * n];
        for (int i = 0; i < n; i++)
        {
            result[(i * n) + i] = lambda;
            if (i + 1 < n)
            {
                result[((i + 1) * n) + i] = 1;
            }
        }

        return result;
    }

    /// <summary>
    /// The Kahan matrix: upper trapezoidal, and a standing counter-example to rank detection by
    /// column-pivoted QR. <paramref name="perturbation"/> tilts the diagonal by a few ulps so that
    /// rounding cannot make the pivoting reorder the columns.
    /// </summary>
    public static double[] Kahan(int rows, int cols, double theta, double perturbation)
    {
        double s = Math.Sin(theta);
        double c = Math.Cos(theta);
        var result = new double[rows * cols];
        double scale = 1;
        for (int r = 0; r < rows; r++)
        {
            for (int j = r; j < cols; j++)
            {
                result[(j * rows) + r] = j == r ? scale : -scale * c;
            }

            scale *= s;
        }

        // Added afterwards, so it perturbs the scaled diagonal and not the pattern it came from.
        double step = perturbation * 2.220446049250313e-16;
        for (int i = 0; i < rows && i < cols; i++)
        {
            result[(i * rows) + i] += step * (cols - i);
        }

        return result;
    }

    /// <summary>
    /// The Kac–Murdock–Szegő Toeplitz matrix <c>rho^|i−j|</c>, conjugated below the diagonal so it
    /// stays Hermitian for a complex ratio.
    /// </summary>
    public static Complex[] KacMurdockSzego(int n, Complex rho)
    {
        // A real ratio takes the power, because a running product of doubles rounds differently and
        // rho^3 is where the two first part company; a complex one takes the product, because that
        // is exact for a ratio on an axis where the power's exp-and-log route is not.
        var powers = new Complex[n];
        for (int k = 0; k < n; k++)
        {
            powers[k] = rho.Imaginary == 0
                ? new Complex(Math.Pow(rho.Real, k), 0)
                : k == 0 ? Complex.One : powers[k - 1] * rho;
        }

        var result = new Complex[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                Complex value = powers[Math.Abs(r - c)];
                result[(c * n) + r] = r > c ? Complex.Conjugate(value) : value;
            }
        }

        return result;
    }

    /// <summary>
    /// The Krylov matrix <c>[x, Ax, A²x, …]</c> with <paramref name="columns"/> columns — the basis
    /// a Krylov solver builds, before it is orthogonalised.
    /// </summary>
    public static double[] Krylov(double[] a, int n, double[] x, int columns)
    {
        var result = new double[n * columns];
        var current = (double[])x.Clone();
        for (int c = 0; c < columns; c++)
        {
            Array.Copy(current, 0, result, c * n, n);
            var next = new double[n];
            for (int r = 0; r < n; r++)
            {
                double total = 0;
                for (int k = 0; k < n; k++)
                {
                    total += a[(k * n) + r] * current[k];
                }

                next[r] = total;
            }

            current = next;
        }

        return result;
    }

    /// <summary>
    /// The Läuchli matrix: a row of ones over <paramref name="mu"/> times the identity. Forming
    /// <c>A'A</c> loses the perturbation entirely, which is the point of it.
    /// </summary>
    public static double[] Lauchli(int n, double mu)
    {
        int rows = n + 1;
        var result = new double[rows * n];
        for (int c = 0; c < n; c++)
        {
            result[c * rows] = 1;
            result[(c * rows) + c + 1] = mu;
        }

        return result;
    }

    /// <summary>The Lehmer matrix <c>min(i,j)/max(i,j)</c>.</summary>
    public static double[] Lehmer(int n)
    {
        var result = new double[n * n];
        for (int j = 1; j <= n; j++)
        {
            for (int i = 1; i <= n; i++)
            {
                result[((j - 1) * n) + i - 1] = (double)Math.Min(i, j) / Math.Max(i, j);
            }
        }

        return result;
    }

    /// <summary>
    /// The Leslie population matrix: birth numbers across the first row and survival rates down the
    /// first subdiagonal.
    /// </summary>
    public static double[] Leslie(ReadOnlySpan<double> births, ReadOnlySpan<double> survival)
    {
        int n = births.Length;
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            result[c * n] = births[c];
        }

        for (int i = 0; i < survival.Length; i++)
        {
            result[(i * n) + i + 1] = survival[i];
        }

        return result;
    }

    /// <summary>
    /// A tridiagonal matrix whose real eigenvalues grow exponentially more sensitive the more
    /// negative they are.
    /// </summary>
    public static double[] Lesp(int n)
    {
        var result = new double[n * n];
        for (int i = 0; i < n; i++)
        {
            result[(i * n) + i] = -((2 * (i + 1)) + 3);
            if (i + 1 < n)
            {
                result[((i + 1) * n) + i] = i + 2;
                result[(i * n) + i + 1] = 1.0 / (i + 2);
            }
        }

        return result;
    }

    /// <summary>The Hilbert matrix with its first row replaced by ones.</summary>
    public static double[] Lotkin(int n)
    {
        double[] result = Hilbert(n);
        for (int c = 0; c < n; c++)
        {
            result[c * n] = 1;
        }

        return result;
    }

    /// <summary>The matrix <c>min(i,j)</c>.</summary>
    public static double[] MinIndex(int n)
    {
        var result = new double[n * n];
        for (int j = 1; j <= n; j++)
        {
            for (int i = 1; i <= n; i++)
            {
                result[((j - 1) * n) + i - 1] = Math.Min(i, j);
            }
        }

        return result;
    }

    /// <summary>
    /// The Moler matrix <c>U'U</c>, where <c>U</c> is the fully filled triangular matrix
    /// <see cref="UpperTriangularWilkinson"/> builds. One eigenvalue is very small.
    /// </summary>
    public static double[] Moler(int n, double alpha)
    {
        double[] u = UpperTriangularWilkinson(n, n, alpha, n - 1);
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r <= c; r++)
            {
                double total = 0;
                for (int k = 0; k < n; k++)
                {
                    total += u[(r * n) + k] * u[(c * n) + k];
                }

                result[(c * n) + r] = total;
                result[(r * n) + c] = total;
            }
        }

        return result;
    }

    /// <summary>
    /// One of the nine orthogonal or nearly orthogonal matrices the family is indexed by. Only
    /// <paramref name="kind"/> 3 is complex; the rest have a zero imaginary part.
    /// </summary>
    public static Complex[] Orthogonal(int n, int kind)
    {
        var result = new Complex[n * n];
        void Set(int r, int c, Complex value) => result[(c * n) + r] = value;

        switch (kind)
        {
            case 2:
                for (int c = 1; c <= n; c++)
                {
                    for (int r = 1; r <= n; r++)
                    {
                        Set(r - 1, c - 1, 2 / Math.Sqrt((2 * n) + 1) *
                            SineOfDegrees(360.0 * r * c / ((2 * n) + 1)));
                    }
                }

                break;
            case 3:
                for (int c = 0; c < n; c++)
                {
                    for (int r = 0; r < n; r++)
                    {
                        Set(r, c, UnitRoot((double)r * c, n) / Math.Sqrt(n));
                    }
                }

                break;
            case 4:
                // The Helmert matrix: a first row of equal weights, then each row balancing the
                // entries before it against the one it reaches.
                for (int c = 0; c < n; c++)
                {
                    Set(0, c, 1 / Math.Sqrt(n));
                }

                for (int r = 1; r < n; r++)
                {
                    double scale = 1 / Math.Sqrt((double)r * (r + 1));
                    for (int c = 0; c < r; c++)
                    {
                        Set(r, c, scale);
                    }

                    Set(r, r, -r * scale);
                }

                break;
            case 5:
                for (int c = 0; c < n; c++)
                {
                    for (int r = 0; r < n; r++)
                    {
                        double angle = 360.0 * r * c / n;
                        Set(r, c, (SineOfDegrees(angle) + CosineOfDegrees(angle)) / Math.Sqrt(n));
                    }
                }

                break;
            case 6:
                for (int c = 1; c <= n; c++)
                {
                    for (int r = 1; r <= n; r++)
                    {
                        Set(r - 1, c - 1, Math.Sqrt(2.0 / n) *
                            CosineOfDegrees((r - 0.5) * (c - 0.5) * 180.0 / n));
                    }
                }

                break;
            case 7:
            {
                // The reflection that sends the vector of ones onto the first axis.
                double root = Math.Sqrt(n);
                var v = new double[n];
                for (int i = 0; i < n; i++)
                {
                    v[i] = 1;
                }

                v[0] -= root;
                double squared = 0;
                for (int i = 0; i < n; i++)
                {
                    squared += v[i] * v[i];
                }

                for (int c = 0; c < n; c++)
                {
                    for (int r = 0; r < n; r++)
                    {
                        double entry = (r == c ? 1 : 0) - (2 * v[r] * v[c] / squared);
                        Set(r, c, entry);
                    }
                }

                break;
            }

            case -1:
                for (int c = 0; c < n; c++)
                {
                    for (int r = 0; r < n; r++)
                    {
                        Set(r, c, CosineOfDegrees(180.0 * r * c / (n - 1)));
                    }
                }

                break;
            case -2:
                for (int c = 1; c <= n; c++)
                {
                    for (int r = 1; r <= n; r++)
                    {
                        Set(r - 1, c - 1, CosineOfDegrees((r - 1) * (c - 0.5) * 180.0 / n));
                    }
                }

                break;
            default:
                for (int c = 1; c <= n; c++)
                {
                    for (int r = 1; r <= n; r++)
                    {
                        Set(r - 1, c - 1, Math.Sqrt(2.0 / (n + 1)) *
                            SineOfDegrees(180.0 * r * c / (n + 1)));
                    }
                }

                break;
        }

        return result;
    }

    /// <summary>The Parter matrix <c>1/(i − j + ½)</c>, whose singular values crowd around π.</summary>
    public static double[] Parter(int n)
    {
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                result[(c * n) + r] = 1 / (r - c + 0.5);
            }
        }

        return result;
    }

    /// <summary>The Pei matrix: <paramref name="alpha"/> down the diagonal, ones everywhere.</summary>
    public static double[] Pei(int n, double alpha)
    {
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            for (int r = 0; r < n; r++)
            {
                result[(c * n) + r] = r == c ? alpha + 1 : 1;
            }
        }

        return result;
    }

    /// <summary>
    /// The prolate matrix: the symmetric Toeplitz matrix whose first row samples the ideal
    /// low-pass filter of bandwidth <paramref name="w"/>.
    /// </summary>
    public static double[] Prolate(int n, double w)
    {
        var first = new double[n];
        if (n > 0)
        {
            first[0] = 2 * w;
        }

        for (int k = 1; k < n; k++)
        {
            first[k] = SineOfDegrees(360.0 * w * k) / (Math.PI * k);
        }

        return TestMatrices.Toeplitz(first, first);
    }

    /// <summary>
    /// Redheffer's matrix of zeros and ones: a one in the first column, and a one wherever the row
    /// index divides the column index. Its determinant is the Mertens function.
    /// </summary>
    public static double[] Redheffer(int n)
    {
        var result = new double[n * n];
        for (int j = 1; j <= n; j++)
        {
            for (int i = 1; i <= n; i++)
            {
                result[((j - 1) * n) + i - 1] = j == 1 || j % i == 0 ? 1 : 0;
            }
        }

        return result;
    }

    /// <summary>
    /// The Riemann matrix, whose determinant's growth is equivalent to the Riemann hypothesis.
    /// </summary>
    public static double[] Riemann(int n)
    {
        var result = new double[n * n];
        for (int j = 2; j <= n + 1; j++)
        {
            for (int i = 2; i <= n + 1; i++)
            {
                result[((j - 2) * n) + i - 2] = j % i == 0 ? i - 1 : -1;
            }
        }

        return result;
    }

    /// <summary>
    /// The Ris matrix: symmetric Hankel, with eigenvalues that gather about <c>±π/2</c>.
    /// </summary>
    public static double[] Ris(int n)
    {
        var result = new double[n * n];
        for (int c = 1; c <= n; c++)
        {
            for (int r = 1; r <= n; r++)
            {
                result[((c - 1) * n) + r - 1] = 0.5 / (n - r - c + 1.5);
            }
        }

        return result;
    }

    /// <summary>
    /// The sampling matrix, whose eigenvalues are exactly <c>0 … n−1</c> and exceedingly sensitive.
    /// </summary>
    public static double[] Sampling(ReadOnlySpan<double> x)
    {
        int n = x.Length;
        var result = new double[n * n];
        for (int c = 0; c < n; c++)
        {
            double diagonal = 0;
            for (int r = 0; r < n; r++)
            {
                if (r == c)
                {
                    continue;
                }

                double entry = x[r] / (x[r] - x[c]);
                result[(c * n) + r] = entry;
                diagonal += entry;
            }

            result[(c * n) + c] = diagonal;
        }

        return result;
    }

    /// <summary>
    /// The smoke matrix: roots of unity down the diagonal, a superdiagonal of ones, and — unless
    /// <paramref name="open"/> is set — a one closing the ring in the bottom-left corner.
    /// </summary>
    public static Complex[] Smoke(int n, bool open)
    {
        var result = new Complex[n * n];
        for (int i = 0; i < n; i++)
        {
            result[(i * n) + i] = UnitRoot(i + 1, n);
            if (i + 1 < n)
            {
                result[((i + 1) * n) + i] = Complex.One;
            }
        }

        if (!open && n > 0)
        {
            result[n - 1] = Complex.One;
        }

        return result;
    }

    /// <summary>
    /// A symmetric positive definite Toeplitz matrix built as a positive combination of
    /// rank-two cosine matrices, with weights <paramref name="w"/> at frequencies
    /// <paramref name="theta"/>.
    /// </summary>
    public static double[] ToeplitzPositiveDefinite(
        int n, ReadOnlySpan<double> w, ReadOnlySpan<double> theta)
    {
        var first = new double[n];
        for (int k = 0; k < n; k++)
        {
            double total = 0;
            for (int m = 0; m < w.Length; m++)
            {
                total += w[m] * CosineOfDegrees(360.0 * theta[m] * k);
            }

            first[k] = total;
        }

        return TestMatrices.Toeplitz(first, first);
    }

    /// <summary>
    /// Wilkinson's upper triangular matrix: ones down the diagonal and <paramref name="alpha"/> on
    /// the first <paramref name="bands"/> superdiagonals.
    /// </summary>
    public static double[] UpperTriangularWilkinson(int rows, int cols, double alpha, int bands)
    {
        var result = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                int offset = c - r;
                result[(c * rows) + r] = offset == 0 ? 1 : offset > 0 && offset <= bands ? alpha : 0;
            }
        }

        return result;
    }
}
