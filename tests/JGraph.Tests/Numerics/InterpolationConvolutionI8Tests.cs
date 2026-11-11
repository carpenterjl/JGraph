using System.Numerics.Tensors;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// The every-row plan's I8 in the numerics layer: the blocked, threaded convolution, the knot
/// search, the spline slopes taken for many lines over one axis, and the grid sampler's written-out
/// two-direction readers. Each is held to the algorithm it replaced, kept verbatim here, to the bit.
/// </summary>
public class InterpolationConvolutionI8Tests
{
    private const double Phi = 0.618033988749895;

    private static double[] Golden(int n, double scale = 1, double shift = 0)
    {
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = (((i + 1) * Phi % 1) * scale) + shift;
        }

        return values;
    }

    private static void AssertBits(ReadOnlySpan<double> expected, ReadOnlySpan<double> actual, string what)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            if (BitConverter.DoubleToInt64Bits(expected[i]) != BitConverter.DoubleToInt64Bits(actual[i]))
            {
                Assert.Fail($"{what}: element {i} is {actual[i]:R}, the old road gave {expected[i]:R}");
            }
        }
    }

    // --- Convolution ---------------------------------------------------------------------------------

    /// <summary>The whole-answer tap loop as it stood before I8.</summary>
    private static double[] ConvolveAsItWas(ReadOnlySpan<double> u, ReadOnlySpan<double> v)
    {
        if (u.Length == 0 || v.Length == 0)
        {
            return [];
        }

        var result = new double[u.Length + v.Length - 1];
        ReadOnlySpan<double> taps = u.Length <= v.Length ? u : v;
        ReadOnlySpan<double> signal = u.Length <= v.Length ? v : u;
        for (int t = 0; t < taps.Length; t++)
        {
            double tap = taps[t];
            if (tap == 0)
            {
                continue;
            }

            Span<double> into = result.AsSpan(t, signal.Length);
            if (tap == 1)
            {
                TensorPrimitives.Add<double>(into, signal, into);
                continue;
            }

            TensorPrimitives.MultiplyAdd<double>(signal, tap, into, into);
        }

        return result;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 3)]
    [InlineData(3, 5)]
    [InlineData(2047, 2)]
    [InlineData(2048, 17)]
    [InlineData(2049, 64)]
    [InlineData(64, 200000)]
    [InlineData(200000, 64)]
    [InlineData(70000, 1500)]
    [InlineData(4096, 4096)]
    public void TheBlockedConvolutionIsTheTapLoopsToTheBit(int lengthU, int lengthV)
    {
        double[] u = Golden(lengthU, 2, -1);
        double[] v = Golden(lengthV, 3, -0.5);
        AssertBits(ConvolveAsItWas(u, v), Convolution.Convolve(u, v), $"conv {lengthU} by {lengthV}");
    }

    [Fact]
    public void ZeroAndUnitTapsAndInfinitiesStayAsTheyWere()
    {
        double[] signal = Golden(9000, 4, -2);
        signal[10] = double.PositiveInfinity;
        signal[5000] = double.NegativeInfinity;
        signal[7000] = double.NaN;
        signal[100] = -0.0;
        double[] taps = [0, 1, -1, 0.5, 0, 1, 3e-300, -0.0, 2];
        AssertBits(ConvolveAsItWas(signal, taps), Convolution.Convolve(signal, taps), "special taps");
        AssertBits(ConvolveAsItWas(taps, signal), Convolution.Convolve(taps, signal), "special taps, swapped");

        double[] zeros = [-0.0, -0.0];
        double[] negatives = [-0.0, -0.0, -0.0];
        AssertBits(ConvolveAsItWas(zeros, negatives), Convolution.Convolve(zeros, negatives), "negative zeros");
    }

    [Theory]
    [InlineData(ConvolutionShape.Same)]
    [InlineData(ConvolutionShape.Valid)]
    public void ARangeOfTheConvolutionIsThatPartOfTheWhole(ConvolutionShape shape)
    {
        double[] u = Golden(30000, 1, 0);
        double[] v = Golden(77, 1, -0.25);
        double[] full = ConvolveAsItWas(u, v);
        (int offset, int length) = Convolution.Window(u.Length, v.Length, shape);
        var part = new double[length];
        part.AsSpan().Fill(double.NaN); // nothing need be cleared first
        Convolution.ConvolveInto(u, v, offset, part);
        AssertBits(full.AsSpan(offset, length), part, shape.ToString());
        AssertBits(full.AsSpan(offset, length), Convolution.Convolve(u, v, shape), shape + " by the cut");
    }

    [Fact]
    public void ARangeOutsideTheConvolutionIsRefused()
    {
        double[] u = [1, 2, 3];
        double[] v = [1, 1];
        Assert.Throws<ArgumentOutOfRangeException>(() => Convolution.ConvolveInto(u, v, 2, new double[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Convolution.ConvolveInto(u, v, -1, new double[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Convolution.ConvolveInto(u, [], 0, new double[1]));
    }

    // --- The knot search -------------------------------------------------------------------------------

    private static double[] Linspace(double from, double to, int n)
    {
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            values[i] = from + ((to - from) * i / (n - 1));
        }

        return values;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(1200)]
    [InlineData(20000)]
    public void TheCellIsTheBisectionsOnAnEvenAxis(int n)
    {
        double[] axis = Linspace(-3, 3, n);
        var search = KnotSearch.For(axis);
        Assert.True(search.IsEven);
        foreach (double at in Points(axis))
        {
            Assert.Equal(KnotSearch.Bisect(axis, at), search.Cell(at));
        }
    }

    [Fact]
    public void TheCellIsTheBisectionsOnUnevenAndNearlyEvenAxes()
    {
        double[] uneven = new double[500];
        for (int i = 0; i < uneven.Length; i++)
        {
            uneven[i] = i * i * 0.01;
        }

        double[] nearly = Linspace(0, 1, 800);
        nearly[400] += 5e-14; // inside the tolerance (1.25e-13 here): still estimated, settled against the knots

        double[] stepped = Linspace(0, 1, 300);
        stepped[150] += 1e-6; // outside it: bisected

        Assert.False(KnotSearch.For(uneven).IsEven);
        Assert.True(KnotSearch.For(nearly).IsEven);
        Assert.False(KnotSearch.For(stepped).IsEven);
        foreach (double[] axis in new[] { uneven, nearly, stepped })
        {
            var search = KnotSearch.For(axis);
            foreach (double at in Points(axis))
            {
                Assert.Equal(KnotSearch.Bisect(axis, at), search.Cell(at));
            }
        }
    }

    [Fact]
    public void AnAxisWithANaNKnotIsNotEstimated()
    {
        double[] axis = Linspace(0, 1, 10);
        axis[4] = double.NaN;
        Assert.False(KnotSearch.For(axis).IsEven);
    }

    /// <summary>Every knot, its neighbours either side, the midpoints, a golden scatter and the far outside.</summary>
    private static IEnumerable<double> Points(double[] axis)
    {
        foreach (double knot in axis)
        {
            yield return knot;
            yield return Math.BitDecrement(knot);
            yield return Math.BitIncrement(knot);
        }

        for (int i = 0; i + 1 < axis.Length; i++)
        {
            yield return (axis[i] + axis[i + 1]) / 2;
        }

        double lo = axis[0];
        double span = axis[^1] - axis[0];
        foreach (double g in Golden(5000))
        {
            yield return lo + (((1.2 * g) - 0.1) * span);
        }

        yield return double.PositiveInfinity;
        yield return double.NegativeInfinity;
        yield return double.MaxValue;
        yield return -double.MaxValue;
    }

    // --- Spline slopes for many lines ------------------------------------------------------------------

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(37)]
    [InlineData(1200)]
    public void APlansSlopesAreTheOneLineSolvesToTheBit(int n)
    {
        double[] x = new double[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = (i * 0.37) + (0.05 * Math.Sin(i)); // uneven, increasing
        }

        Interpolation.SplineLinePlan plan = Interpolation.SplineLinePlan.For(x)!;
        const int lines = 70;

        // Contiguous lines (stride one, one at a time) and lines across storage (stride = lines).
        double[] contiguous = Golden(n * lines, 5, -2);
        var contiguousAnswer = new double[contiguous.Length];
        for (int j = 0; j < lines; j++)
        {
            plan.Solve(contiguous, contiguousAnswer, j * n, 1, 1);
        }

        double[] across = Golden(n * lines, 5, -2);
        var acrossAnswer = new double[across.Length];
        plan.Solve(across, acrossAnswer, 0, lines, 64);
        plan.Solve(across, acrossAnswer, 64, lines, lines - 64);

        for (int j = 0; j < lines; j++)
        {
            double[] line = contiguous.AsSpan(j * n, n).ToArray();
            AssertBits(Interpolation.SplineSlopes(x, line), contiguousAnswer.AsSpan(j * n, n), $"contiguous line {j}");

            var strided = new double[n];
            var stridedAnswer = new double[n];
            for (int k = 0; k < n; k++)
            {
                strided[k] = across[j + (k * lines)];
                stridedAnswer[k] = acrossAnswer[j + (k * lines)];
            }

            AssertBits(Interpolation.SplineSlopes(x, strided), stridedAnswer, $"strided line {j}");
        }
    }

    [Fact]
    public void AShortAxisHasNoPlan()
    {
        Assert.Null(Interpolation.SplineLinePlan.For([0, 1, 2]));
        Assert.NotNull(Interpolation.SplineLinePlan.For([0, 1, 2, 3]));
    }

    // --- The grid sampler ------------------------------------------------------------------------------

    public static TheoryData<GridMethod, int, int> GridCases => new()
    {
        { GridMethod.Linear, 1200, 1100 },
        { GridMethod.Linear, 3, 2 },
        { GridMethod.Spline, 300, 260 },
        { GridMethod.Spline, 4, 5 },
        { GridMethod.Spline, 3, 7 },
        { GridMethod.Nearest, 50, 40 },
        { GridMethod.Cubic, 60, 45 },
    };

    [Theory]
    [MemberData(nameof(GridCases))]
    public void TheTwoDirectionSamplerIsTheGeneralOnesToTheBit(GridMethod method, int rows, int cols)
    {
        double[] axis0 = Linspace(-3, 3, rows);
        double[] axis1 = cols > 3 && method != GridMethod.Cubic ? Uneven(cols) : Linspace(-2, 5, cols);
        var values = new double[rows * cols];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                values[r + (c * rows)] = (Math.Sin(axis0[r]) * Math.Cos(axis1[c])) + (0.2 * axis0[r]);
            }
        }

        int[] dims = [rows, cols];
        var sampler = new GridSampler([axis0, axis1], values, dims, method);
        var old = new GridSamplerAsItWas([axis0, axis1], values, dims, method);
        double[] g0 = Golden(20000);
        double[] g1 = Golden(20000, 1, 0.31);
        Span<double> point = stackalloc double[2];
        foreach (bool extrapolate in new[] { false, true })
        {
            for (int q = 0; q < g0.Length; q++)
            {
                point[0] = axis0[0] + (((1.1 * g0[q]) - 0.05) * (axis0[^1] - axis0[0]));
                point[1] = axis1[0] + (((1.1 * (g1[q] % 1)) - 0.05) * (axis1[^1] - axis1[0]));
                if (q % 997 == 0)
                {
                    point[0] = axis0[q % rows]; // on a knot
                }

                if (q % 1999 == 0)
                {
                    point[1] = double.NaN;
                }

                double expected = old.Sample(point, extrapolate, -7);
                double actual = sampler.Sample(point, extrapolate, -7);
                Assert.True(
                    BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual),
                    $"{method} at ({point[0]:R}, {point[1]:R}): {actual:R}, the old sampler gave {expected:R}");
            }
        }
    }

    [Fact]
    public void AThreeDirectionSplineIsTheGeneralOnesToTheBit()
    {
        int[] dims = [9, 7, 6];
        double[][] axes = [Linspace(0, 1, 9), Uneven(7), Linspace(-1, 2, 6)];
        double[] values = Golden(9 * 7 * 6, 2, -1);
        var sampler = new GridSampler(axes, values, dims, GridMethod.Spline);
        var old = new GridSamplerAsItWas(axes, values, dims, GridMethod.Spline);
        double[] g = Golden(3000);
        Span<double> point = stackalloc double[3];
        for (int q = 0; q + 2 < g.Length; q += 3)
        {
            for (int d = 0; d < 3; d++)
            {
                point[d] = axes[d][0] + (g[q + d] * (axes[d][^1] - axes[d][0]));
            }

            Assert.Equal(
                BitConverter.DoubleToInt64Bits(old.Sample(point, true, 0)),
                BitConverter.DoubleToInt64Bits(sampler.Sample(point, true, 0)));
        }
    }

    private static double[] Uneven(int n)
    {
        var axis = new double[n];
        for (int i = 0; i < n; i++)
        {
            axis[i] = (i * 0.5) + (0.1 * Math.Sin(3 * i));
        }

        return axis;
    }

    /// <summary>The grid sampler as it stood before I8: bisection, the general loops, slopes a line at a time.</summary>
    private sealed class GridSamplerAsItWas
    {
        private readonly double[][] _grids;
        private readonly double[] _values;
        private readonly int[] _dims;
        private readonly int[] _strides;
        private readonly GridMethod _method;
        private readonly int _rank;
        private readonly double[][]? _derivatives;
        private readonly int[] _cells;
        private readonly int[] _starts;
        private readonly int[] _counts;
        private readonly double[] _weights;
        private readonly double[] _basis;

        public GridSamplerAsItWas(double[][] grids, double[] values, int[] dims, GridMethod method)
        {
            _grids = grids;
            _values = values;
            _dims = dims;
            _method = method;
            _rank = dims.Length;
            _strides = new int[_rank];
            int stride = 1;
            for (int d = 0; d < _rank; d++)
            {
                _strides[d] = stride;
                stride *= dims[d];
            }

            _cells = new int[_rank];
            _starts = new int[_rank];
            _counts = new int[_rank];
            _weights = new double[_rank * 4];
            _basis = new double[_rank * 4];
            _derivatives = method == GridMethod.Spline ? BuildDerivatives() : null;
        }

        public double Sample(ReadOnlySpan<double> point, bool extrapolate, double outside)
        {
            for (int d = 0; d < _rank; d++)
            {
                double at = point[d];
                if (double.IsNaN(at))
                {
                    return double.NaN;
                }

                double[] axis = _grids[d];
                if (!extrapolate && (at < axis[0] || at > axis[^1]))
                {
                    return outside;
                }

                _cells[d] = Bracket(axis, at);
            }

            return _method == GridMethod.Spline ? TensorHermite(point) : TensorWeights(point);
        }

        private double TensorWeights(ReadOnlySpan<double> point)
        {
            int combinations = 1;
            for (int d = 0; d < _rank; d++)
            {
                int cell = _cells[d];
                double[] axis = _grids[d];
                double width = axis[cell + 1] - axis[cell];
                double t = (point[d] - axis[cell]) / width;
                switch (_method)
                {
                    case GridMethod.Nearest:
                        _starts[d] = t >= 0.5 ? Math.Min(cell + 1, _dims[d] - 1) : Math.Max(cell, 0);
                        _counts[d] = 1;
                        _weights[d * 4] = 1;
                        break;
                    case GridMethod.Linear:
                        _starts[d] = cell;
                        _counts[d] = 2;
                        _weights[d * 4] = 1 - t;
                        _weights[(d * 4) + 1] = t;
                        break;
                    default:
                        CubicWeights(d, cell, t);
                        break;
                }

                combinations *= _counts[d];
            }

            double total = 0;
            for (int k = 0; k < combinations; k++)
            {
                int rest = k;
                int offset = 0;
                double weight = 1;
                for (int d = 0; d < _rank; d++)
                {
                    int step = rest % _counts[d];
                    rest /= _counts[d];
                    offset += (_starts[d] + step) * _strides[d];
                    weight *= _weights[(d * 4) + step];
                }

                total += weight * _values[offset];
            }

            return total;
        }

        private void CubicWeights(int d, int cell, double t)
        {
            Span<double> kernel = stackalloc double[4];
            Interpolation.KeysWeights(t, kernel);
            int last = _dims[d] - 1;
            int start = Math.Clamp(cell - 1, 0, Math.Max(last - 3, 0));
            int count = Math.Min(4, _dims[d]);
            _starts[d] = start;
            _counts[d] = count;
            for (int i = 0; i < count; i++)
            {
                _weights[(d * 4) + i] = 0;
            }

            for (int k = 0; k < 4; k++)
            {
                int index = cell - 1 + k;
                double weight = kernel[k];
                if (index < 0)
                {
                    _weights[d * 4] += 3 * weight;
                    _weights[(d * 4) + 1] -= 3 * weight;
                    _weights[(d * 4) + 2] += weight;
                }
                else if (index > last)
                {
                    _weights[(d * 4) + count - 1] += 3 * weight;
                    _weights[(d * 4) + count - 2] -= 3 * weight;
                    _weights[(d * 4) + count - 3] += weight;
                }
                else
                {
                    _weights[(d * 4) + index - start] += weight;
                }
            }
        }

        private double[][] BuildDerivatives()
        {
            var table = new double[1 << _rank][];
            table[0] = _values;
            for (int mask = 1; mask < table.Length; mask++)
            {
                int d = System.Numerics.BitOperations.TrailingZeroCount(mask);
                table[mask] = SlopesAlong(d, table[mask & ~(1 << d)]);
            }

            return table;
        }

        private double[] SlopesAlong(int d, double[] source)
        {
            int length = _dims[d];
            int stride = _strides[d];
            int inner = stride;
            int outer = _values.Length / (length * stride);
            var answer = new double[source.Length];
            double[] axis = _grids[d];
            var fibre = new double[length];
            for (int o = 0; o < outer; o++)
            {
                for (int i = 0; i < inner; i++)
                {
                    int at = (o * length * stride) + i;
                    for (int k = 0; k < length; k++)
                    {
                        fibre[k] = source[at + (k * stride)];
                    }

                    double[] slopes = Interpolation.SplineSlopes(axis, fibre);
                    for (int k = 0; k < length; k++)
                    {
                        answer[at + (k * stride)] = slopes[k];
                    }
                }
            }

            return answer;
        }

        private double TensorHermite(ReadOnlySpan<double> point)
        {
            for (int d = 0; d < _rank; d++)
            {
                int cell = _cells[d];
                double[] axis = _grids[d];
                double width = axis[cell + 1] - axis[cell];
                double t = (point[d] - axis[cell]) / width;
                double t2 = t * t;
                double t3 = t2 * t;
                _basis[d * 4] = (2 * t3) - (3 * t2) + 1;
                _basis[(d * 4) + 1] = width * (t3 - (2 * t2) + t);
                _basis[(d * 4) + 2] = (-2 * t3) + (3 * t2);
                _basis[(d * 4) + 3] = width * (t3 - t2);
            }

            double total = 0;
            int combinations = 1 << (2 * _rank);
            for (int k = 0; k < combinations; k++)
            {
                double weight = 1;
                int offset = 0;
                int mask = 0;
                for (int d = 0; d < _rank; d++)
                {
                    int digit = (k >> (2 * d)) & 3;
                    weight *= _basis[(d * 4) + digit];
                    offset += (_cells[d] + (digit >> 1)) * _strides[d];
                    mask |= (digit & 1) << d;
                }

                if (weight != 0)
                {
                    total += weight * _derivatives![mask][offset];
                }
            }

            return total;
        }

        private static int Bracket(double[] axis, double at)
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
    }
}
