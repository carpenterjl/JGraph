using JGraph.Core.Primitives;

namespace JGraph.Core.Data;

/// <summary>
/// An immutable <see cref="IDataSeries"/> backed by two parallel <see cref="double"/> arrays. Bounds
/// and monotonicity are computed once at construction so that repeated rendering and auto-scaling are
/// cheap even for very large datasets.
/// </summary>
public sealed class ArrayDataSeries : IDataSeries
{
    private readonly double[] _xs;
    private readonly double[] _ys;

    /// <summary>Creates a series from X/Y arrays. The arrays are used directly (not copied).</summary>
    /// <exception cref="ArgumentException">The arrays differ in length.</exception>
    public ArrayDataSeries(double[] xs, double[] ys)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);
        if (xs.Length != ys.Length)
        {
            throw new ArgumentException("X and Y arrays must have the same length.", nameof(ys));
        }

        _xs = xs;
        _ys = ys;
        (XBounds, YBounds, IsXAscending) = Analyze(xs, ys);
    }

    /// <summary>Creates a series with implicit X indices 0, 1, 2, ... for the given Y values.</summary>
    public static ArrayDataSeries FromValues(double[] ys)
    {
        ArgumentNullException.ThrowIfNull(ys);
        var xs = new double[ys.Length];
        for (int i = 0; i < ys.Length; i++)
        {
            xs[i] = i;
        }

        return new ArrayDataSeries(xs, ys);
    }

    public int Count => _xs.Length;

    public bool IsXAscending { get; }

    public DataRange XBounds { get; }

    public DataRange YBounds { get; }

    public double GetX(int index) => _xs[index];

    public double GetY(int index) => _ys[index];

    public bool TryGetSpans(out ReadOnlySpan<double> xs, out ReadOnlySpan<double> ys)
    {
        xs = _xs;
        ys = _ys;
        return true;
    }

    private static (DataRange X, DataRange Y, bool Ascending) Analyze(double[] xs, double[] ys)
    {
        DataRange xRange = DataRange.Empty;
        DataRange yRange = DataRange.Empty;
        bool ascending = true;
        double previousX = double.NegativeInfinity;

        for (int i = 0; i < xs.Length; i++)
        {
            double x = xs[i];
            double y = ys[i];

            if (double.IsFinite(x))
            {
                xRange = xRange.Include(x);
                if (x < previousX)
                {
                    ascending = false;
                }

                previousX = x;
            }

            if (double.IsFinite(y))
            {
                yRange = yRange.Include(y);
            }
        }

        return (xRange, yRange, ascending);
    }
}
