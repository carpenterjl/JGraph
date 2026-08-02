using System.Numerics;
using JGraph.Signal;

namespace JGraph.Imaging;

/// <summary>
/// Two-dimensional FIR filter design, and the point-spread/optical-transfer pair the deblurring
/// family is written in terms of.
/// </summary>
/// <remarks>
/// Designing a 2-D filter is the same problem as designing a 1-D one — say what response you want and
/// find the short kernel that comes closest — but the 1-D answers do not carry over, because a 2-D
/// polynomial does not factor. What is here are the three routes MATLAB offers around that: sample the
/// response you want and transform it back (<c>fsamp2</c>), do the same and taper the result so the
/// truncation does not ring (<c>fwind1</c>/<c>fwind2</c>), or take a 1-D filter you already trust and
/// map its frequency axis onto the plane (<c>ftrans2</c>).
/// </remarks>
public static class FilterDesign
{
    /// <summary>
    /// The frequency samples along one axis of an <paramref name="n"/>-point response, spanning
    /// <c>[-1, 1)</c> where 1 means half the sampling rate.
    /// </summary>
    /// <param name="n">How many samples.</param>
    /// <returns>The <paramref name="n"/> frequencies, increasing.</returns>
    /// <remarks>
    /// An odd count puts a sample exactly on zero and none on the ends; an even count puts one on the
    /// lower end and none on zero. Both spacings are <c>2/n</c> — the grid a length-<paramref name="n"/>
    /// transform actually resolves, which is why a response sampled here can be transformed back
    /// without interpolation.
    /// </remarks>
    public static double[] Axis(int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        var axis = new double[n];
        double start = n % 2 == 1 ? -1.0 + (1.0 / n) : -1.0;
        for (int i = 0; i < n; i++)
        {
            axis[i] = start + (2.0 * i / n);
        }

        return axis;
    }

    /// <summary>
    /// The one-dimensional frequency vector <c>freqspace</c> returns when only one output is asked
    /// for: <paramref name="n"/> points around the unit circle, of which the distinct half is listed.
    /// </summary>
    /// <param name="n">How many points around the circle.</param>
    /// <returns>The frequencies from zero upwards, in units of half the sampling rate.</returns>
    public static double[] HalfAxis(int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        int count = (n + 1) / 2;
        var axis = new double[count];
        for (int i = 0; i < count; i++)
        {
            axis[i] = 2.0 * i / n;
        }

        return axis;
    }

    /// <summary>
    /// The frequency response of a kernel, sampled at every combination of the two given axes.
    /// </summary>
    /// <param name="kernel">The filter taps.</param>
    /// <param name="fx">Frequencies along the columns.</param>
    /// <param name="fy">Frequencies along the rows.</param>
    /// <returns>An <c>fy.Length</c>-by-<c>fx.Length</c> response.</returns>
    /// <remarks>
    /// Evaluated straight from the definition rather than by zero-padding and transforming. It costs
    /// one multiply-add per tap per sample, which for the sizes involved is nothing, and it buys the
    /// form where the caller names the frequencies — a response along one line through the plane, say
    /// — which a transform cannot give at all. The kernel's origin is its middle tap, so a symmetric
    /// kernel answers with a real response and no phase to unwrap.
    /// </remarks>
    public static Complex[,] Response(double[,] kernel, IReadOnlyList<double> fx, IReadOnlyList<double> fy)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(fx);
        ArgumentNullException.ThrowIfNull(fy);

        int rows = kernel.GetLength(0);
        int cols = kernel.GetLength(1);
        int centreRow = rows / 2;
        int centreCol = cols / 2;

        var response = new Complex[fy.Count, fx.Count];
        for (int i = 0; i < fy.Count; i++)
        {
            for (int j = 0; j < fx.Count; j++)
            {
                Complex sum = Complex.Zero;
                for (int m = 0; m < rows; m++)
                {
                    for (int n = 0; n < cols; n++)
                    {
                        if (kernel[m, n] == 0)
                        {
                            continue;
                        }

                        double angle = -Math.PI *
                            ((fx[j] * (n - centreCol)) + (fy[i] * (m - centreRow)));
                        sum += kernel[m, n] * new Complex(Math.Cos(angle), Math.Sin(angle));
                    }
                }

                response[i, j] = sum;
            }
        }

        return response;
    }

    /// <summary>
    /// The filter whose response matches <paramref name="desired"/> at the points
    /// <see cref="Axis"/> lists — frequency sampling, and the same size as what went in.
    /// </summary>
    /// <param name="desired">The wanted response, sampled on the centred grid.</param>
    /// <returns>The kernel, its origin in the middle.</returns>
    /// <remarks>
    /// The response arrives with zero frequency in the middle, so it is unshifted, inverse-transformed
    /// and shifted back. Frequency sampling matches the response exactly at the sample points and says
    /// nothing about what happens between them, which is why the result rings — that is the honest
    /// answer to the question asked, and <see cref="Windowed(double[,], double[,])"/> is the question
    /// asked differently.
    /// </remarks>
    public static double[,] FromSamples(double[,] desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        int rows = desired.GetLength(0);
        int cols = desired.GetLength(1);
        if (rows == 0 || cols == 0)
        {
            throw new ArgumentException("the desired response is empty.", nameof(desired));
        }

        var grid = new Complex[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                grid[(r * cols) + c] = desired[r, c];
            }
        }

        Complex[] unshifted = FourierGrid.Shift(grid, rows, cols, inverse: true);
        FourierGrid.Transform(unshifted, rows, cols, inverse: true);
        Complex[] centred = FourierGrid.Shift(unshifted, rows, cols, inverse: false);

        var kernel = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                kernel[r, c] = centred[(r * cols) + c].Real;
            }
        }

        return kernel;
    }

    /// <summary>
    /// Frequency sampling from a response given at named frequencies rather than on the regular grid.
    /// </summary>
    /// <param name="fx">The column frequency of each sample.</param>
    /// <param name="fy">The row frequency of each sample.</param>
    /// <param name="desired">The wanted response at those points.</param>
    /// <param name="rows">The kernel height.</param>
    /// <param name="cols">The kernel width.</param>
    /// <returns>The kernel, its origin in the middle.</returns>
    /// <remarks>
    /// The inverse transform written as a sum instead of a transform, which is what lets the samples
    /// sit anywhere. Hand it the grid <see cref="Axis"/> produces and it reduces term for term to
    /// <see cref="FromSamples(double[,])"/>; that identity is worth a test, because it is the only
    /// thing keeping the two forms of one function honest with each other.
    /// </remarks>
    public static double[,] FromSamples(
        IReadOnlyList<double> fx, IReadOnlyList<double> fy, double[,] desired, int rows, int cols)
    {
        ArgumentNullException.ThrowIfNull(fx);
        ArgumentNullException.ThrowIfNull(fy);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(cols, 1);

        int sampleRows = desired.GetLength(0);
        int sampleCols = desired.GetLength(1);
        if (fy.Count != sampleRows || fx.Count != sampleCols)
        {
            throw new ArgumentException(
                $"the response is {sampleRows}-by-{sampleCols} but {fy.Count} row and {fx.Count} " +
                "column frequencies were given.", nameof(desired));
        }

        double scale = 1.0 / (sampleRows * sampleCols);
        var kernel = new double[rows, cols];
        for (int i = 0; i < rows; i++)
        {
            double y = i - (rows / 2);
            for (int j = 0; j < cols; j++)
            {
                double x = j - (cols / 2);
                double sum = 0;
                for (int m = 0; m < sampleRows; m++)
                {
                    for (int n = 0; n < sampleCols; n++)
                    {
                        double angle = Math.PI * ((fx[n] * x) + (fy[m] * y));
                        sum += desired[m, n] * Math.Cos(angle);
                    }
                }

                kernel[i, j] = sum * scale;
            }
        }

        return kernel;
    }

    /// <summary>
    /// The transform <c>ftrans2</c> uses when none is given: the McClellan transform, which turns a
    /// 1-D filter into a very nearly circularly symmetric 2-D one.
    /// </summary>
    public static double[,] McClellan => new double[,]
    {
        { 1.0 / 8, 2.0 / 8, 1.0 / 8 },
        { 2.0 / 8, -4.0 / 8, 2.0 / 8 },
        { 1.0 / 8, 2.0 / 8, 1.0 / 8 },
    };

    /// <summary>
    /// Designs a 2-D filter by mapping the frequency axis of a 1-D one onto the plane.
    /// </summary>
    /// <param name="b">A symmetric, odd-length 1-D filter.</param>
    /// <param name="transform">The frequency transformation kernel.</param>
    /// <returns>The 2-D kernel.</returns>
    /// <remarks>
    /// A zero-phase 1-D filter's response is a polynomial in <c>cos ω</c>. Substituting the
    /// transformation's own response for <c>cos ω</c> gives a 2-D response with the same shape read
    /// along whatever contours the transformation draws — so a good 1-D lowpass becomes a good 2-D
    /// lowpass, and the cutoff you designed is the cutoff you get. Working the substitution in
    /// Chebyshev form keeps it to repeated convolution by the transformation kernel, which is why the
    /// answer stays a short FIR filter instead of a rational one.
    /// </remarks>
    public static double[,] FrequencyTransform(double[] b, double[,] transform)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(transform);
        if (b.Length % 2 == 0)
        {
            throw new ArgumentException(
                "the one-dimensional filter must have an odd number of taps so that it has a middle " +
                "one to be symmetric about.", nameof(b));
        }

        int half = (b.Length - 1) / 2;
        int insetRows = (transform.GetLength(0) - 1) / 2;
        int insetCols = (transform.GetLength(1) - 1) / 2;
        if (insetRows < 0 || insetCols < 0)
        {
            throw new ArgumentException("the transformation kernel is empty.", nameof(transform));
        }

        // The zero-phase filter's Chebyshev coefficients: the middle tap once, every other tap twice,
        // because a symmetric pair of taps is one cosine.
        var a = new double[half + 1];
        a[0] = b[half];
        for (int k = 1; k <= half; k++)
        {
            a[k] = 2 * b[half + k];
        }

        // T0 = 1, T1 = t, and T(k+1) = 2·t·T(k) − T(k−1) — the recurrence, run over kernels rather
        // than over numbers.
        double[,] previous = new double[1, 1] { { 1 } };
        double[,] current = (double[,])transform.Clone();
        double[,] h = Scale(current, a.Length > 1 ? a[1] : 0);
        h[insetRows, insetCols] += a[0];

        for (int k = 2; k <= half; k++)
        {
            double[,] next = Scale(Filters.Convolve2(transform, current), 2);
            AddInto(next, previous, 2 * insetRows, 2 * insetCols, -1);

            double[,] grown = Scale(next, a[k]);
            AddInto(grown, h, insetRows, insetCols, 1);

            previous = current;
            current = next;
            h = grown;
        }

        return Rotate180(h);
    }

    /// <summary>
    /// Huang's rotated window: a 1-D window turned about its centre to make a circularly symmetric
    /// 2-D one.
    /// </summary>
    /// <param name="window">The 1-D window.</param>
    /// <returns>A square window of the same side.</returns>
    /// <remarks>
    /// The window is read as a function of radius and sampled at each pixel's distance from the
    /// centre, so what was a taper along a line becomes a taper in every direction at once. Outside
    /// the unit circle there is no window left to read, and the corners are zero.
    /// </remarks>
    public static double[,] RotateWindow(double[] window)
    {
        ArgumentNullException.ThrowIfNull(window);
        int n = window.Length;
        if (n == 0)
        {
            throw new ArgumentException("the window is empty.", nameof(window));
        }

        var rotated = new double[n, n];
        if (n == 1)
        {
            rotated[0, 0] = window[0];
            return rotated;
        }

        var axis = new double[n];
        for (int i = 0; i < n; i++)
        {
            axis[i] = (i - ((n - 1) / 2.0)) * (2.0 / (n - 1));
        }

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                double radius = Math.Sqrt((axis[r] * axis[r]) + (axis[c] * axis[c]));
                rotated[r, c] = radius > axis[n - 1] ? 0 : Interpolate(axis, window, radius);
            }
        }

        return rotated;
    }

    /// <summary>The separable window two 1-D windows make: one along the rows, one along the columns.</summary>
    /// <param name="rowWindow">The window along the rows.</param>
    /// <param name="colWindow">The window along the columns.</param>
    /// <returns>A <c>rowWindow.Length</c>-by-<c>colWindow.Length</c> window.</returns>
    public static double[,] OuterWindow(double[] rowWindow, double[] colWindow)
    {
        ArgumentNullException.ThrowIfNull(rowWindow);
        ArgumentNullException.ThrowIfNull(colWindow);
        var window = new double[rowWindow.Length, colWindow.Length];
        for (int r = 0; r < rowWindow.Length; r++)
        {
            for (int c = 0; c < colWindow.Length; c++)
            {
                window[r, c] = rowWindow[r] * colWindow[c];
            }
        }

        return window;
    }

    /// <summary>
    /// The windowed design: the ideal kernel <paramref name="desired"/> asks for, cut down to the
    /// window's size and tapered by it.
    /// </summary>
    /// <param name="desired">The wanted response, sampled on the centred grid.</param>
    /// <param name="window">The 2-D window, which fixes the answer's size.</param>
    /// <returns>The kernel, the same size as the window.</returns>
    /// <remarks>
    /// Truncating an ideal response is what makes a design ring: a sharp cut in one domain is a
    /// ripple in the other. The window replaces the sharp cut with a gradual one, trading a wider
    /// transition band for a flatter passband — which for pictures is almost always the better trade,
    /// because ripple around an edge is visible and a soft cutoff is not.
    /// </remarks>
    public static double[,] Windowed(double[,] desired, double[,] window)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(window);

        int rows = window.GetLength(0);
        int cols = window.GetLength(1);
        int sampleRows = desired.GetLength(0);
        int sampleCols = desired.GetLength(1);
        if (rows > sampleRows || cols > sampleCols)
        {
            throw new ArgumentException(
                $"the window is {rows}-by-{cols} but the response is only sampled " +
                $"{sampleRows}-by-{sampleCols}; the response cannot say less than the filter must know.",
                nameof(window));
        }

        double[,] ideal = FromSamples(desired);
        int offsetRow = (sampleRows / 2) - (rows / 2);
        int offsetCol = (sampleCols / 2) - (cols / 2);

        var kernel = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                kernel[r, c] = ideal[r + offsetRow, c + offsetCol] * window[r, c];
            }
        }

        return kernel;
    }

    /// <summary>
    /// The windowed design from a response given at named frequencies: the ideal kernel is evaluated
    /// straight at the window's size, so there is no intermediate grid to interpolate through.
    /// </summary>
    /// <param name="fx">The column frequency of each sample.</param>
    /// <param name="fy">The row frequency of each sample.</param>
    /// <param name="desired">The wanted response at those points.</param>
    /// <param name="window">The 2-D window, which fixes the answer's size.</param>
    /// <returns>The kernel, the same size as the window.</returns>
    public static double[,] Windowed(
        IReadOnlyList<double> fx, IReadOnlyList<double> fy, double[,] desired, double[,] window)
    {
        ArgumentNullException.ThrowIfNull(window);
        int rows = window.GetLength(0);
        int cols = window.GetLength(1);
        double[,] ideal = FromSamples(fx, fy, desired, rows, cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                ideal[r, c] *= window[r, c];
            }
        }

        return ideal;
    }

    /// <summary>
    /// The matrix that performs a convolution: multiply it by a picture read out column by column and
    /// the answer, read back the same way, is <c>conv2</c>.
    /// </summary>
    /// <param name="kernel">The filter.</param>
    /// <param name="rows">The height of the pictures it will be applied to.</param>
    /// <param name="cols">Their width.</param>
    /// <returns>An <c>((rows+p−1)·(cols+q−1))</c>-by-<c>(rows·cols)</c> matrix.</returns>
    /// <remarks>
    /// Filtering is linear, so it has a matrix — writing it out is how a filter joins a least-squares
    /// problem or an inverse that has to be solved rather than applied. It is also enormous: the
    /// matrix for a modest 64×64 picture already has sixteen million entries, so the size is checked
    /// before anything is allocated.
    /// </remarks>
    public static double[,] ConvolutionMatrix(double[,] kernel, int rows, int cols)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(cols, 1);

        int kernelRows = kernel.GetLength(0);
        int kernelCols = kernel.GetLength(1);
        int outRows = rows + kernelRows - 1;
        int outCols = cols + kernelCols - 1;
        long height = (long)outRows * outCols;
        long width = (long)rows * cols;
        if (height * width > 64_000_000)
        {
            throw new ArgumentException(
                $"the convolution matrix would be {height}-by-{width}; filter the picture directly " +
                "instead of building the matrix that would do it.", nameof(kernel));
        }

        var matrix = new double[height, width];
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                int column = r + (c * rows);
                for (int kc = 0; kc < kernelCols; kc++)
                {
                    for (int kr = 0; kr < kernelRows; kr++)
                    {
                        matrix[(r + kr) + ((c + kc) * outRows), column] = kernel[kr, kc];
                    }
                }
            }
        }

        return matrix;
    }

    /// <summary>
    /// The optical transfer function of a point spread function: the same blur, said in frequencies.
    /// </summary>
    /// <param name="psf">The point spread function.</param>
    /// <param name="height">The picture height the transfer function is for.</param>
    /// <param name="width">The picture width.</param>
    /// <returns>The transfer function, row-major, zero frequency at the corner.</returns>
    /// <remarks>
    /// The centre tap of the spread function has to land on sample zero, or every deconvolution comes
    /// back displaced by half the kernel. Padding to size and rotating the origin into the corner is
    /// what puts it there.
    /// </remarks>
    public static Complex[] PsfToOtf(double[,] psf, int height, int width)
    {
        ArgumentNullException.ThrowIfNull(psf);
        int rows = psf.GetLength(0);
        int cols = psf.GetLength(1);
        if (rows > height || cols > width)
        {
            throw new ArgumentException(
                $"the {rows}-by-{cols} spread function does not fit in a {height}-by-{width} picture.",
                nameof(psf));
        }

        var grid = new Complex[height * width];
        int shiftRow = rows / 2;
        int shiftCol = cols / 2;
        for (int r = 0; r < rows; r++)
        {
            int target = ((r - shiftRow) % height + height) % height;
            for (int c = 0; c < cols; c++)
            {
                grid[(target * width) + ((((c - shiftCol) % width) + width) % width)] = psf[r, c];
            }
        }

        FourierGrid.Transform(grid, height, width, inverse: false);
        return grid;
    }

    /// <summary>The spread function a transfer function stands for — <see cref="PsfToOtf"/> undone.</summary>
    /// <param name="otf">The transfer function, row-major.</param>
    /// <param name="height">Its height.</param>
    /// <param name="width">Its width.</param>
    /// <param name="rows">The wanted spread-function height.</param>
    /// <param name="cols">The wanted spread-function width.</param>
    /// <returns>The spread function, its centre tap in the middle.</returns>
    public static double[,] OtfToPsf(Complex[] otf, int height, int width, int rows, int cols)
    {
        ArgumentNullException.ThrowIfNull(otf);
        if (otf.Length != height * width)
        {
            throw new ArgumentException("the transfer function does not match the given size.", nameof(otf));
        }

        if (rows > height || cols > width)
        {
            throw new ArgumentException(
                $"a {rows}-by-{cols} spread function cannot be read out of a {height}-by-{width} " +
                "transfer function.", nameof(rows));
        }

        var grid = (Complex[])otf.Clone();
        FourierGrid.Transform(grid, height, width, inverse: true);

        var psf = new double[rows, cols];
        int shiftRow = rows / 2;
        int shiftCol = cols / 2;
        for (int r = 0; r < rows; r++)
        {
            int source = (((r - shiftRow) % height) + height) % height;
            for (int c = 0; c < cols; c++)
            {
                psf[r, c] = grid[(source * width) + ((((c - shiftCol) % width) + width) % width)].Real;
            }
        }

        return psf;
    }

    /// <summary>
    /// Blurs a picture's borders into its own wrapped edges, so that a deconvolution which treats the
    /// picture as periodic has nothing to ring against.
    /// </summary>
    /// <param name="image">The picture.</param>
    /// <param name="psf">The spread function the deblurring will use.</param>
    /// <returns>A picture the same size, its interior untouched.</returns>
    /// <remarks>
    /// Every frequency-domain deconvolution assumes the picture wraps around, and a picture almost
    /// never does: the top row and the bottom row are strangers, and the step between them reads as an
    /// edge running across the whole picture. That false edge is the source of the ripples that show
    /// up along the borders of a deblurred result. Tapering blends the border into a wrapped blur of
    /// itself, so the seam is smooth before the deconvolution ever sees it. How far in the taper
    /// reaches is set by the spread function's own reach — the autocorrelation of its shadow on each
    /// axis — because that is exactly how far the blur carried information across the seam.
    /// </remarks>
    public static double[,] EdgeTaper(double[,] image, double[,] psf)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(psf);
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        int psfRows = psf.GetLength(0);
        int psfCols = psf.GetLength(1);
        if (psfRows > height || psfCols > width)
        {
            throw new ArgumentException(
                "the spread function is larger than the picture.", nameof(psf));
        }

        var rowProjection = new double[psfRows];
        var colProjection = new double[psfCols];
        for (int r = 0; r < psfRows; r++)
        {
            for (int c = 0; c < psfCols; c++)
            {
                rowProjection[r] += psf[r, c];
                colProjection[c] += psf[r, c];
            }
        }

        double[] rowWeight = BorderWeight(rowProjection, height);
        double[] colWeight = BorderWeight(colProjection, width);

        var spectrum = new Complex[height * width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                spectrum[(r * width) + c] = image[r, c];
            }
        }

        FourierGrid.Transform(spectrum, height, width, inverse: false);
        Complex[] transfer = PsfToOtf(psf, height, width);
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] *= transfer[i];
        }

        FourierGrid.Transform(spectrum, height, width, inverse: true);

        var tapered = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                double keep = (1 - rowWeight[r]) * (1 - colWeight[c]);
                tapered[r, c] = (keep * image[r, c]) + ((1 - keep) * spectrum[(r * width) + c].Real);
            }
        }

        return tapered;
    }

    /// <summary>
    /// How much of each position along one axis belongs to the border: one at both ends, falling to
    /// zero as far in as the spread function reaches.
    /// </summary>
    private static double[] BorderWeight(double[] projection, int length)
    {
        var weight = new double[length];
        if (length < 2)
        {
            return weight;
        }

        int order = length - 1;
        var padded = new Complex[order];
        for (int i = 0; i < Math.Min(projection.Length, order); i++)
        {
            padded[i] = projection[i];
        }

        Complex[] spectrum = Fft.Forward(padded);
        for (int i = 0; i < order; i++)
        {
            double magnitude = spectrum[i].Magnitude;
            spectrum[i] = magnitude * magnitude;
        }

        Complex[] correlation = Fft.Inverse(spectrum);
        double peak = 0;
        for (int i = 0; i < order; i++)
        {
            peak = Math.Max(peak, correlation[i].Real);
        }

        if (peak <= 0)
        {
            return weight;
        }

        // The autocorrelation of a wrapped sequence is symmetric about the wrap, so it already runs
        // down from one end and back up at the other: exactly the shape of a border weight, once the
        // last position is closed back onto the first.
        for (int i = 0; i < order; i++)
        {
            weight[i] = Math.Clamp(correlation[i].Real / peak, 0, 1);
        }

        weight[length - 1] = weight[0];
        return weight;
    }

    /// <summary>Linear interpolation of <paramref name="values"/> over an increasing <paramref name="axis"/>.</summary>
    private static double Interpolate(double[] axis, double[] values, double at)
    {
        if (at <= axis[0])
        {
            return values[0];
        }

        for (int i = 1; i < axis.Length; i++)
        {
            if (at > axis[i])
            {
                continue;
            }

            double span = axis[i] - axis[i - 1];
            double weight = span == 0 ? 0 : (at - axis[i - 1]) / span;
            return values[i - 1] + (weight * (values[i] - values[i - 1]));
        }

        return values[^1];
    }

    private static double[,] Scale(double[,] values, double factor)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var scaled = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                scaled[r, c] = values[r, c] * factor;
            }
        }

        return scaled;
    }

    /// <summary>Adds <paramref name="addend"/> into <paramref name="target"/> at the given offset.</summary>
    private static void AddInto(double[,] target, double[,] addend, int offsetRow, int offsetCol, double sign)
    {
        for (int r = 0; r < addend.GetLength(0); r++)
        {
            for (int c = 0; c < addend.GetLength(1); c++)
            {
                target[r + offsetRow, c + offsetCol] += sign * addend[r, c];
            }
        }
    }

    private static double[,] Rotate180(double[,] values)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var turned = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                turned[r, c] = values[rows - 1 - r, cols - 1 - c];
            }
        }

        return turned;
    }
}
