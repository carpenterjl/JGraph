namespace JGraph.Numerics;

/// <summary>
/// Piecewise-cubic interpolation through a set of samples: the not-a-knot cubic spline MATLAB's
/// <c>'spline'</c> names, the shape-preserving Hermite cubic behind <c>'pchip'</c>, the modified
/// Akima cubic behind <c>'makima'</c>, and the cubic convolution behind <c>'cubic'</c>.
/// </summary>
/// <remarks>
/// <para>
/// The first three answer the same question — what slope does the curve take at each sample — and
/// the evaluation is the same Hermite cubic once they have. That is why they share a file and a
/// signature: <c>spline</c> chooses the slopes that make the second derivative continuous,
/// <c>pchip</c> chooses the slopes that stop the curve overshooting between samples, and
/// <c>makima</c> chooses slopes local enough not to ring but weighted enough not to flatten.
/// Everything here works on strictly increasing sample positions; ordering is the caller's job.
/// </para>
/// <para>
/// <see cref="KeysWeights"/> is the odd one out and deliberately so: cubic convolution never asks
/// for a slope, it reads four samples and weights them, which is why it needs an evenly spaced
/// grid where the other three do not.
/// </para>
/// </remarks>
public static class Interpolation
{
    /// <summary>
    /// The slope at each sample of the not-a-knot cubic spline through them. Three samples fit one
    /// parabola and two fit a line, which is what the end condition degenerates to.
    /// </summary>
    /// <exception cref="ArgumentException">Fewer than two samples were given.</exception>
    public static double[] SplineSlopes(double[] x, double[] y)
    {
        int n = Check(x, y);
        var slopes = new double[n];
        if (n == 2)
        {
            slopes[0] = slopes[1] = (y[1] - y[0]) / (x[1] - x[0]);
            return slopes;
        }

        double[] h = Widths(x);
        double[] s = Secants(x, y);

        if (n == 3)
        {
            // One parabola through three points; its slope at each of them is the answer, which is
            // also what the not-a-knot condition means when there is only one interior knot.
            double curvature = (s[1] - s[0]) / (x[2] - x[0]);
            for (int i = 0; i < 3; i++)
            {
                slopes[i] = s[0] + (curvature * ((2 * x[i]) - x[0] - x[1]));
            }

            return slopes;
        }

        // The tridiagonal system for the slopes: continuity of the second derivative at every
        // interior knot, closed at both ends by not-a-knot — the third derivative is continuous
        // across the second and second-to-last knot too, so the first two intervals are one cubic.
        var lower = new double[n];
        var middle = new double[n];
        var upper = new double[n];
        var rhs = new double[n];

        middle[0] = h[1];
        upper[0] = h[0] + h[1];
        rhs[0] = (((h[0] + (2 * (h[0] + h[1]))) * h[1] * s[0]) + (h[0] * h[0] * s[1])) / (h[0] + h[1]);

        for (int i = 1; i < n - 1; i++)
        {
            lower[i] = h[i];
            middle[i] = 2 * (h[i - 1] + h[i]);
            upper[i] = h[i - 1];
            rhs[i] = 3 * ((h[i] * s[i - 1]) + (h[i - 1] * s[i]));
        }

        lower[n - 1] = h[n - 2] + h[n - 3];
        middle[n - 1] = h[n - 3];
        rhs[n - 1] = ((h[n - 2] * h[n - 2] * s[n - 3])
            + ((((2 * (h[n - 3] + h[n - 2])) + h[n - 2]) * h[n - 3]) * s[n - 2])) / (h[n - 3] + h[n - 2]);

        return SolveTridiagonal(lower, middle, upper, rhs);
    }

    /// <summary>
    /// The slope at each sample of the shape-preserving cubic through them (Fritsch–Carlson). A
    /// slope is zeroed wherever the data turn, so the curve never overshoots a sample the way a
    /// spline can.
    /// </summary>
    /// <exception cref="ArgumentException">Fewer than two samples were given.</exception>
    public static double[] PchipSlopes(double[] x, double[] y)
    {
        int n = Check(x, y);
        var slopes = new double[n];
        double[] h = Widths(x);
        double[] s = Secants(x, y);

        if (n == 2)
        {
            slopes[0] = slopes[1] = s[0];
            return slopes;
        }

        for (int i = 1; i < n - 1; i++)
        {
            if (s[i - 1] * s[i] <= 0)
            {
                continue; // the data turn here, so the curve is flat and cannot overshoot
            }

            // A weighted harmonic mean of the two secants: closer to the smaller one, which is what
            // keeps the interpolant inside the interval the samples bracket.
            double left = (2 * h[i]) + h[i - 1];
            double right = h[i] + (2 * h[i - 1]);
            slopes[i] = (left + right) / ((left / s[i - 1]) + (right / s[i]));
        }

        slopes[0] = EndSlope(h[0], h[1], s[0], s[1]);
        slopes[n - 1] = EndSlope(h[n - 2], h[n - 3], s[n - 2], s[n - 3]);
        return slopes;
    }

    /// <summary>
    /// The slope at each sample of the cubic spline whose slope at each end is given rather than
    /// chosen — MATLAB's clamped end condition, which it selects by being handed two more values
    /// than there are sample positions.
    /// </summary>
    /// <exception cref="ArgumentException">Fewer than two samples were given.</exception>
    public static double[] SplineSlopes(double[] x, double[] y, double leftSlope, double rightSlope)
    {
        int n = Check(x, y);
        var slopes = new double[n];
        if (n == 2)
        {
            slopes[0] = leftSlope;
            slopes[1] = rightSlope;
            return slopes;
        }

        double[] h = Widths(x);
        double[] s = Secants(x, y);

        // The interior rows are the ones not-a-knot writes too — continuity of the second
        // derivative — and only the two end rows change: each says "the slope here is this".
        var lower = new double[n];
        var middle = new double[n];
        var upper = new double[n];
        var rhs = new double[n];

        middle[0] = 1;
        rhs[0] = leftSlope;
        for (int i = 1; i < n - 1; i++)
        {
            lower[i] = h[i];
            middle[i] = 2 * (h[i - 1] + h[i]);
            upper[i] = h[i - 1];
            rhs[i] = 3 * ((h[i] * s[i - 1]) + (h[i - 1] * s[i]));
        }

        middle[n - 1] = 1;
        rhs[n - 1] = rightSlope;
        return SolveTridiagonal(lower, middle, upper, rhs);
    }

    /// <summary>
    /// The slope at each sample of the modified Akima cubic through them — MATLAB's
    /// <c>'makima'</c>.
    /// </summary>
    /// <remarks>
    /// Akima's rule weights each secant by how sharply the data turn beyond it, so a slope is set
    /// by its immediate neighbourhood and one wild sample cannot ring across the whole curve. Plain
    /// Akima flattens wherever three samples happen to be collinear, because the weight it uses
    /// goes to zero there; the modification adds half the magnitude of the two secants to each
    /// weight, which breaks that tie and changes nothing else. The two secants beyond each end are
    /// invented by continuing the nearest two linearly, which is what lets the same rule run at a
    /// boundary where there is nothing further out to weigh against.
    /// </remarks>
    /// <exception cref="ArgumentException">Fewer than two samples were given.</exception>
    public static double[] MakimaSlopes(double[] x, double[] y)
    {
        int n = Check(x, y);
        var slopes = new double[n];
        double[] s = Secants(x, y);
        if (n == 2)
        {
            slopes[0] = slopes[1] = s[0];
            return slopes;
        }

        // The secants, with two invented at each end so every sample has two on either side of it.
        // Element i + 2 of this is the secant of interval i.
        var e = new double[n + 3];
        s.CopyTo(e, 2);
        e[1] = (2 * s[0]) - s[1];
        e[0] = (2 * e[1]) - s[0];
        e[n + 1] = (2 * s[n - 2]) - s[n - 3];
        e[n + 2] = (2 * e[n + 1]) - s[n - 2];

        for (int i = 0; i < n; i++)
        {
            double ahead = Weight(e[i + 3], e[i + 2]);
            double behind = Weight(e[i + 1], e[i]);
            double total = ahead + behind;
            slopes[i] = total == 0
                ? 0 // the data are flat on both sides, so there is no direction to prefer
                : ((ahead * e[i + 1]) + (behind * e[i + 2])) / total;
        }

        return slopes;

        static double Weight(double far, double near) =>
            Math.Abs(far - near) + (Math.Abs(far + near) / 2);
    }

    /// <summary>
    /// The four coefficients of each piece, highest power first and in the local variable
    /// <c>x - breaks(i)</c> — the layout MATLAB's <c>pp.coefs</c> uses.
    /// </summary>
    /// <param name="x">The sample positions, strictly increasing.</param>
    /// <param name="y">The values at them.</param>
    /// <param name="slopes">The slope at each sample, from one of the rules above.</param>
    /// <returns>One row of four per piece, laid out row after row.</returns>
    public static double[] PieceCoefficients(double[] x, double[] y, double[] slopes)
    {
        int pieces = x.Length - 1;
        var coefficients = new double[pieces * 4];
        for (int i = 0; i < pieces; i++)
        {
            double h = x[i + 1] - x[i];
            double secant = (y[i + 1] - y[i]) / h;
            coefficients[i * 4] = (slopes[i] + slopes[i + 1] - (2 * secant)) / (h * h);
            coefficients[(i * 4) + 1] = ((3 * secant) - (2 * slopes[i]) - slopes[i + 1]) / h;
            coefficients[(i * 4) + 2] = slopes[i];
            coefficients[(i * 4) + 3] = y[i];
        }

        return coefficients;
    }

    /// <summary>
    /// The four cubic-convolution weights for a point a fraction <paramref name="t"/> of the way
    /// through a cell, to be applied to the samples one before it, either side of it, and one after.
    /// </summary>
    /// <param name="t">Where in the cell the point falls: 0 at its left, 1 at its right.</param>
    /// <param name="weights">Receives four weights, for the offsets −1, 0, +1 and +2.</param>
    /// <remarks>
    /// This is Keys' kernel at a = −½, which MATLAB has called <c>'v5cubic'</c> since version 5
    /// and calls <c>'cubic'</c> now as well. It reproduces a quadratic exactly and is continuous in
    /// its first derivative, and unlike the three slope rules above it needs the samples evenly
    /// spaced, because the kernel is written in cell widths rather than in positions.
    /// </remarks>
    public static void KeysWeights(double t, Span<double> weights)
    {
        weights[0] = Kernel(1 + t);
        weights[1] = Kernel(t);
        weights[2] = Kernel(1 - t);
        weights[3] = Kernel(2 - t);

        static double Kernel(double at)
        {
            double d = Math.Abs(at);
            if (d < 1)
            {
                return (1.5 * d * d * d) - (2.5 * d * d) + 1;
            }

            return d < 2 ? (-0.5 * d * d * d) + (2.5 * d * d) - (4 * d) + 2 : 0;
        }
    }

    /// <summary>
    /// The sample cubic convolution needs one step beyond an end, continued from the three nearest
    /// so that a parabola through them carries on unbroken.
    /// </summary>
    /// <param name="near">The sample at the end.</param>
    /// <param name="middle">The next one in.</param>
    /// <param name="far">The one after that.</param>
    /// <returns>The invented sample one step outside the data.</returns>
    public static double KeysEdgeSample(double near, double middle, double far) =>
        (3 * near) - (3 * middle) + far;

    /// <summary>
    /// The cubic through <paramref name="left"/> and <paramref name="right"/> with the given end
    /// slopes, at <paramref name="at"/>. Outside the interval this extrapolates that same cubic,
    /// which is what both methods do past their last sample.
    /// </summary>
    public static double Hermite(
        double leftX, double rightX, double left, double right, double leftSlope, double rightSlope, double at)
    {
        double width = rightX - leftX;
        double t = (at - leftX) / width;
        double t2 = t * t;
        double t3 = t2 * t;

        return (left * ((2 * t3) - (3 * t2) + 1))
            + (width * leftSlope * (t3 - (2 * t2) + t))
            + (right * ((-2 * t3) + (3 * t2)))
            + (width * rightSlope * (t3 - t2));
    }

    /// <summary>
    /// The end slope of a shape-preserving cubic: a one-sided parabola through the last three
    /// samples, held back when it would either reverse the data's direction or overshoot it.
    /// </summary>
    private static double EndSlope(double near, double far, double nearSecant, double farSecant)
    {
        double slope = (((2 * near) + far) * nearSecant - (near * farSecant)) / (near + far);
        if (Math.Sign(slope) != Math.Sign(nearSecant))
        {
            return 0;
        }

        if (Math.Sign(nearSecant) != Math.Sign(farSecant) && Math.Abs(slope) > Math.Abs(3 * nearSecant))
        {
            return 3 * nearSecant;
        }

        return slope;
    }

    private static int Check(double[] x, double[] y)
    {
        if (x.Length != y.Length)
        {
            throw new ArgumentException("The sample positions and values must be the same length.", nameof(y));
        }

        if (x.Length < 2)
        {
            throw new ArgumentException("Interpolation needs at least two samples.", nameof(x));
        }

        return x.Length;
    }

    private static double[] Widths(double[] x)
    {
        var h = new double[x.Length - 1];
        for (int i = 0; i < h.Length; i++)
        {
            h[i] = x[i + 1] - x[i];
        }

        return h;
    }

    private static double[] Secants(double[] x, double[] y)
    {
        var s = new double[x.Length - 1];
        for (int i = 0; i < s.Length; i++)
        {
            s[i] = (y[i + 1] - y[i]) / (x[i + 1] - x[i]);
        }

        return s;
    }

    /// <summary>The Thomas algorithm: forward elimination then back substitution, in place.</summary>
    private static double[] SolveTridiagonal(double[] lower, double[] middle, double[] upper, double[] rhs)
    {
        int n = middle.Length;
        for (int i = 1; i < n; i++)
        {
            double factor = lower[i] / middle[i - 1];
            middle[i] -= factor * upper[i - 1];
            rhs[i] -= factor * rhs[i - 1];
        }

        var solution = new double[n];
        solution[n - 1] = rhs[n - 1] / middle[n - 1];
        for (int i = n - 2; i >= 0; i--)
        {
            solution[i] = (rhs[i] - (upper[i] * solution[i + 1])) / middle[i];
        }

        return solution;
    }
}
