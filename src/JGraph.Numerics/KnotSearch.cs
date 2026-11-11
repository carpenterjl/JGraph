using System.Runtime.CompilerServices;

namespace JGraph.Numerics;

/// <summary>
/// Which cell of an increasing axis a point falls in: the largest <c>i</c> in <c>[0, n − 2]</c>
/// with <c>axis[i] ≤ at</c>, clamped at both ends so that a point outside the axis continues the
/// end cell — exactly what bisecting the axis answers, found by arithmetic when the knots are
/// evenly spaced (the every-row plan's I8).
/// </summary>
/// <remarks>
/// <para>
/// The estimate is only a starting place. It is corrected against the knots themselves — a query
/// on a knot, a step that rounded the wrong way — and when a couple of steps do not settle it the
/// search bisects, so the cell answered is the bisection's to the index on every axis, even or not.
/// Evenness decides only whether the estimate is worth making.
/// </para>
/// <para>
/// A NaN query is the caller's to refuse before asking: its cell means nothing either way.
/// </para>
/// </remarks>
public readonly struct KnotSearch
{
    private readonly double[] _axis;
    private readonly bool _even;
    private readonly double _origin;
    private readonly double _perUnit;

    private KnotSearch(double[] axis, bool even, double origin, double perUnit)
    {
        _axis = axis;
        _even = even;
        _origin = origin;
        _perUnit = perUnit;
    }

    /// <summary>The search over one axis, at least two knots long and increasing.</summary>
    /// <param name="axis">The knots; read, never written, and kept by reference.</param>
    /// <returns>The search.</returns>
    public static KnotSearch For(double[] axis)
    {
        ArgumentNullException.ThrowIfNull(axis);
        int n = axis.Length;
        if (n < 2)
        {
            return new KnotSearch(axis, false, 0, 0);
        }

        double span = axis[n - 1] - axis[0];
        double perUnit = (n - 1) / span;
        bool even = span > 0 && double.IsFinite(span) && double.IsFinite(perUnit) && perUnit > 0
            && StepsAreEven(axis, span / (n - 1));
        return new KnotSearch(axis, even, axis[0], perUnit);
    }

    /// <summary>Whether the arithmetic estimate is used (the knots are evenly spaced).</summary>
    public bool IsEven => _even;

    /// <summary>The cell <paramref name="at"/> falls in, clamped to <c>[0, n − 2]</c>.</summary>
    /// <param name="at">The point; not NaN.</param>
    /// <returns>The index of the cell's left knot.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Cell(double at)
    {
        double[] x = _axis;
        int last = x.Length - 2;
        if (!_even)
        {
            return Bisect(x, at);
        }

        // Clamped as a double before the cast: a point far outside the axis would otherwise convert
        // to a value that means nothing.
        double estimate = (at - _origin) * _perUnit;
        int i = estimate <= 0 ? 0 : estimate >= last ? last : (int)estimate;
        if (i > 0 && at < x[i])
        {
            i--;
            if (i > 0 && at < x[i])
            {
                return Bisect(x, at);
            }
        }
        else if (i < last && at >= x[i + 1])
        {
            i++;
            if (i < last && at >= x[i + 1])
            {
                return Bisect(x, at);
            }
        }

        return i;
    }

    /// <summary>The bisection the estimate stands in for.</summary>
    public static int Bisect(double[] axis, double at)
    {
        int low = 0;
        int high = axis.Length - 1;
        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (at < axis[mid])
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return low;
    }

    /// <summary>Every step within a ten-billionth of the mean step (the grid readers' tolerance).</summary>
    private static bool StepsAreEven(double[] axis, double step)
    {
        double tolerance = Math.Abs(step) * 1e-10;
        for (int i = 1; i < axis.Length; i++)
        {
            // Written so that a NaN knot fails: the estimate is only for an axis that is all numbers.
            if (!(Math.Abs(axis[i] - axis[i - 1] - step) <= tolerance))
            {
                return false;
            }
        }

        return true;
    }
}
