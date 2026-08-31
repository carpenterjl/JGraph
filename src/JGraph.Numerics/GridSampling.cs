namespace JGraph.Numerics;

/// <summary>How a <see cref="GridSampler"/> reads between the samples it was given.</summary>
public enum GridMethod
{
    /// <summary>The nearest sample, with a point exactly between two taking the later one.</summary>
    Nearest = 0,

    /// <summary>A straight line along each direction in turn.</summary>
    Linear = 1,

    /// <summary>Keys' cubic convolution, which needs the samples evenly spaced along every direction.</summary>
    Cubic = 2,

    /// <summary>The not-a-knot cubic spline along each direction in turn.</summary>
    Spline = 3,
}

/// <summary>
/// Reads a plaid grid of samples at points that need not be on it, in any number of dimensions —
/// the engine behind <c>interp2</c>, <c>interp3</c> and <c>interpn</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every method here is <em>separable</em>: the answer is what you get by interpolating along one
/// direction, then along the next, and so on, and the order does not matter. That is what lets one
/// class serve two, three and n dimensions without a case for each, and it is also the whole of
/// what MATLAB means by a gridded interpolant — a grid that is not plaid needs a triangulation
/// instead, which is a different name and a different milestone.
/// </para>
/// <para>
/// The three local methods are evaluated as a product of per-direction weights, so a query costs
/// the size of its stencil and nothing more. The spline cannot be, because a spline's slope at one
/// knot depends on every sample along that direction; instead the slopes are taken once, along
/// each direction and along each combination of directions, and the query is then a tensor
/// Hermite over the cell it lands in. That costs 2^n arrays of the grid's own size, which is the
/// price of not walking the whole grid once per query point.
/// </para>
/// </remarks>
public sealed class GridSampler
{
    private readonly double[][] _grids;
    private readonly double[] _values;
    private readonly int[] _dims;
    private readonly int[] _strides;
    private readonly GridMethod _method;
    private readonly int _rank;

    /// <summary>Slopes along every combination of directions; null for the local methods.</summary>
    private readonly double[][]? _derivatives;

    // Scratch, reused across queries: one sampler serves one loop, not several threads.
    private readonly int[] _cells;
    private readonly int[] _starts;
    private readonly int[] _counts;
    private readonly double[] _weights;
    private readonly double[] _basis;

    /// <summary>Builds a sampler over one grid of samples.</summary>
    /// <param name="grids">The coordinates along each direction, each strictly increasing.</param>
    /// <param name="values">The samples, column-major over <paramref name="dims"/>.</param>
    /// <param name="dims">The length of the grid along each direction.</param>
    /// <param name="method">How to read between the samples.</param>
    /// <exception cref="ArgumentException">A direction holds fewer than two samples.</exception>
    public GridSampler(double[][] grids, double[] values, int[] dims, GridMethod method)
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
            if (dims[d] < 2)
            {
                throw new ArgumentException(
                    "Interpolation requires at least two sample points for each grid dimension.",
                    nameof(dims));
            }

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

    /// <summary>Shares one sampler's grid and slopes, with scratch of this one's own.</summary>
    private GridSampler(GridSampler shared)
    {
        _grids = shared._grids;
        _values = shared._values;
        _dims = shared._dims;
        _strides = shared._strides;
        _method = shared._method;
        _rank = shared._rank;
        _derivatives = shared._derivatives;

        _cells = new int[_rank];
        _starts = new int[_rank];
        _counts = new int[_rank];
        _weights = new double[_rank * 4];
        _basis = new double[_rank * 4];
    }

    /// <summary>
    /// A second sampler over the same grid, safe to use while this one is in use.
    /// </summary>
    /// <remarks>
    /// The scratch above is what makes one sampler serve one loop and not several threads, and it
    /// is five small arrays; the grid, the samples and the spline slopes are none of those things
    /// and are read only. Building a whole second sampler to split a query set across cores would
    /// re-solve every tridiagonal system in <see cref="BuildDerivatives"/> once per thread, so what
    /// is copied here is exactly the scratch and nothing else (M120).
    /// </remarks>
    public GridSampler ForAnotherThread() => new(this);

    /// <summary>The value at one point.</summary>
    /// <param name="point">One coordinate per direction.</param>
    /// <param name="extrapolate">Whether to continue the end piece past the grid.</param>
    /// <param name="outside">What to answer at a point outside the grid when not extrapolating.</param>
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

    // --- The local methods --------------------------------------------------------------------

    /// <summary>
    /// The answer as a product of per-direction weights: each direction contributes a short run of
    /// samples and a weight for each, and the value is the sum over every combination of them.
    /// </summary>
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
                    // Halfway takes the later sample, which is what rounding the fractional index
                    // away from zero comes to and what MATLAB's own breakpoints say.
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

    /// <summary>
    /// The four cubic-convolution weights along one direction, with anything the kernel reaches
    /// beyond an end folded back on to the three samples that invent it.
    /// </summary>
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
                // The sample one step before the first is 3y0 − 3y1 + y2, so its weight is spent on
                // those three rather than on a place that does not exist. The window always holds
                // them: a cell that reaches past an end is the end cell, so the window starts there.
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

    // --- The spline ---------------------------------------------------------------------------

    /// <summary>
    /// The slopes along every combination of directions: element 0 is the samples themselves,
    /// element with bit <c>d</c> set is that array differentiated along direction <c>d</c>.
    /// </summary>
    /// <remarks>
    /// A not-a-knot spline's slopes are a linear function of the samples, so differentiating along
    /// one direction and then another gives the same array whichever order it is done in. That is
    /// what makes this table well defined, and it is also why the same trick cannot be played with
    /// <c>pchip</c> or <c>makima</c>, whose slopes are not linear in the samples.
    /// </remarks>
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

    /// <summary>One spline's slopes along direction <paramref name="d"/>, for every line of samples.</summary>
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

    /// <summary>
    /// The tensor Hermite over the cell the point lands in: each direction contributes four basis
    /// values — two for the samples at the cell's ends and two for the slopes there — and the
    /// answer is the sum of every product of them against the matching slope array.
    /// </summary>
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

            _basis[d * 4] = (2 * t3) - (3 * t2) + 1;             // the sample at the cell's left
            _basis[(d * 4) + 1] = width * (t3 - (2 * t2) + t);   // the slope there
            _basis[(d * 4) + 2] = (-2 * t3) + (3 * t2);          // the sample at its right
            _basis[(d * 4) + 3] = width * (t3 - t2);             // the slope there
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

    /// <summary>Which cell of an increasing axis a point falls in, clamped so an outside point continues the end cell.</summary>
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
