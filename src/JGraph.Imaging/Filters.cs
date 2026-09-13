using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JGraph.Numerics;

namespace JGraph.Imaging;

/// <summary>Spatial-filtering operations: 2-D correlation/convolution and median filtering.</summary>
public static class Filters
{
    /// <summary>How samples beyond the image edge are supplied to a filter.</summary>
    public enum Boundary
    {
        /// <summary>Out-of-range samples are 0 (MATLAB default).</summary>
        Zero,

        /// <summary>Out-of-range samples replicate the nearest edge pixel.</summary>
        Replicate,

        /// <summary>Out-of-range samples mirror across the edge.</summary>
        Symmetric,

        /// <summary>Out-of-range samples wrap around, treating the image as one tile of a periodic plane.</summary>
        Circular,
    }

    /// <summary>
    /// Correlates an image with a kernel (MATLAB <c>imfilter</c> default), producing a same-size result.
    /// Each output channel is filtered independently. The kernel is applied as-is (no flip).
    /// </summary>
    public static ImageBuffer Correlate(ImageBuffer image, double[,] kernel, Boundary boundary = Boundary.Zero) =>
        Filter(image, kernel, boundary);

    /// <summary>
    /// The general spatial filter behind MATLAB <c>imfilter</c>: correlation or convolution, a same-size
    /// or full-size result, and any boundary rule or a constant pad value.
    /// </summary>
    /// <param name="image">The image to filter; each channel is filtered independently.</param>
    /// <param name="kernel">The filter kernel.</param>
    /// <param name="boundary">How samples beyond the edge are supplied.</param>
    /// <param name="padValue">The constant used when <paramref name="boundary"/> is <see cref="Boundary.Zero"/>.</param>
    /// <param name="convolve">Convolve (flip the kernel) rather than correlate.</param>
    /// <param name="full">Return the full (H+kh-1)×(W+kw-1) result rather than the same-size centre.</param>
    /// <remarks>
    /// Correlation puts the kernel origin at <c>(kh-1)/2</c>, which is MATLAB's
    /// <c>floor((size(h)+1)/2)</c> written 0-based. Convolution flips the kernel and keeps the same
    /// anchor — the flip itself is what moves an even kernel's centre — which is exactly what makes
    /// <c>Filter(A, h, convolve: true)</c> and <see cref="Convolve2(double[,], double[,], Conv2Shape)"/> with
    /// <see cref="Conv2Shape.Same"/> agree with each other and with MATLAB, even-sized kernels
    /// included (measured against R2024a in M103, which also moved <c>Same</c>'s crop).
    /// </remarks>
    public static ImageBuffer Filter(
        ImageBuffer image,
        double[,] kernel,
        Boundary boundary = Boundary.Zero,
        double padValue = 0.0,
        bool convolve = false,
        bool full = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(kernel);
        int kh = kernel.GetLength(0);
        int kw = kernel.GetLength(1);
        double[,] applied = convolve ? Rotate180(kernel) : kernel;

        // Flipping the kernel flips which way an even kernel's centre leans, and rotating it here
        // has already spent that flip — so both modes anchor at (k−1)/2, and convolution lands on
        // the same samples MATLAB's conv2(…, 'same') reads (measured; M103).
        int anchorR = (kh - 1) / 2;
        int anchorC = (kw - 1) / 2;

        // 'full' is the same filter evaluated over a window that starts before the image and ends
        // after it, so it is one index shift away from the same-size case rather than a second loop.
        int offsetR = full ? kh - 1 - anchorR : 0;
        int offsetC = full ? kw - 1 - anchorC : 0;
        int height = full ? image.Height + kh - 1 : image.Height;
        int width = full ? image.Width + kw - 1 : image.Width;

        var result = new ImageBuffer(height, width, image.Channels);
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    double acc = 0;
                    for (int kr = 0; kr < kh; kr++)
                    {
                        int sr = r - offsetR + kr - anchorR;
                        for (int kc = 0; kc < kw; kc++)
                        {
                            int sc = c - offsetC + kc - anchorC;
                            acc += applied[kr, kc] * Sample(image, sr, sc, ch, boundary, padValue);
                        }
                    }

                    result[r, c, ch] = acc;
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// Gaussian smoothing with independent row and column standard deviations (MATLAB
    /// <c>imgaussfilt</c>), applied as two 1-D passes.
    /// </summary>
    /// <param name="image">The image to smooth.</param>
    /// <param name="sigmaRows">Standard deviation down the rows.</param>
    /// <param name="sigmaCols">Standard deviation across the columns.</param>
    /// <param name="filterHeight">Row extent of the kernel; odd, and defaulted from sigma when 0.</param>
    /// <param name="filterWidth">Column extent of the kernel; odd, and defaulted from sigma when 0.</param>
    /// <param name="boundary">How samples beyond the edge are supplied.</param>
    /// <remarks>
    /// A 2-D Gaussian is the outer product of two 1-D Gaussians, so filtering rows then columns costs
    /// <c>kh + kw</c> multiplies per pixel instead of <c>kh · kw</c> and is exact rather than an
    /// approximation. Separating it is what makes a sigma of 20 — the kind <c>imgaussfilt</c> is asked
    /// for when it stands in for a background estimate — affordable.
    /// </remarks>
    public static ImageBuffer GaussianBlur(
        ImageBuffer image,
        double sigmaRows,
        double sigmaCols,
        int filterHeight = 0,
        int filterWidth = 0,
        Boundary boundary = Boundary.Replicate)
    {
        ArgumentNullException.ThrowIfNull(image);
        double[] down = Gaussian1D(sigmaRows, filterHeight);
        double[] across = Gaussian1D(sigmaCols, filterWidth);

        var rows = new double[1, across.Length];
        for (int i = 0; i < across.Length; i++)
        {
            rows[0, i] = across[i];
        }

        var cols = new double[down.Length, 1];
        for (int i = 0; i < down.Length; i++)
        {
            cols[i, 0] = down[i];
        }

        using ImageBuffer horizontal = Filter(image, rows, boundary);
        return Filter(horizontal, cols, boundary);
    }

    /// <summary>
    /// A normalized 1-D Gaussian. <paramref name="size"/> of 0 takes MATLAB's default extent,
    /// <c>2·ceil(2σ)+1</c>, which holds better than four sigma of the mass.
    /// </summary>
    public static double[] Gaussian1D(double sigma, int size = 0)
    {
        if (sigma <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sigma), sigma, "gaussian sigma must be positive.");
        }

        if (size <= 0)
        {
            size = (2 * (int)Math.Ceiling(2 * sigma)) + 1;
        }

        var kernel = new double[size];
        double centre = (size - 1) / 2.0;
        double twoSigmaSq = 2 * sigma * sigma;
        double sum = 0;
        for (int i = 0; i < size; i++)
        {
            double d = i - centre;
            kernel[i] = Math.Exp(-(d * d) / twoSigmaSq);
            sum += kernel[i];
        }

        for (int i = 0; i < size; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }

    /// <summary>2-D convolution of two matrices (MATLAB <c>conv2</c>), with 'full', 'same', or 'valid' shape.</summary>
    public static double[,] Convolve2(double[,] a, double[,] b, Conv2Shape shape = Conv2Shape.Full)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        int ah = a.GetLength(0);
        int aw = a.GetLength(1);
        int bh = b.GetLength(0);
        int bw = b.GetLength(1);
        int fullH = ah + bh - 1;
        int fullW = aw + bw - 1;

        var full = new double[fullH, fullW];
        for (int i = 0; i < ah; i++)
        {
            for (int j = 0; j < aw; j++)
            {
                double av = a[i, j];
                if (av == 0)
                {
                    continue;
                }

                for (int m = 0; m < bh; m++)
                {
                    for (int n = 0; n < bw; n++)
                    {
                        full[i + m, j + n] += av * b[m, n];
                    }
                }
            }
        }

        return shape switch
        {
            Conv2Shape.Full => full,
            // The centre of an even kernel leans forward: MATLAB's 'same' starts at floor(k/2),
            // which only differs from floor((k-1)/2) when the kernel has an even side (M103).
            Conv2Shape.Same => Crop(full, bh / 2, bw / 2, ah, aw),
            Conv2Shape.Valid => ah >= bh && aw >= bw
                ? Crop(full, bh - 1, bw - 1, ah - bh + 1, aw - bw + 1)
                : new double[0, 0],
            _ => full,
        };
    }

    /// <summary>
    /// Median filter over an m×n window (MATLAB <c>medfilt2</c>). The default zero padding is MATLAB's
    /// <c>'zeros'</c> <c>padopt</c>; <see cref="Boundary.Symmetric"/> is its <c>'symmetric'</c>.
    /// </summary>
    public static ImageBuffer Median(
        ImageBuffer image, int windowHeight = 3, int windowWidth = 3, Boundary boundary = Boundary.Zero)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowWidth);
        int anchorR = windowHeight / 2;
        int anchorC = windowWidth / 2;
        var window = new double[windowHeight * windowWidth];

        var result = new ImageBuffer(image.Height, image.Width, image.Channels);
        for (int r = 0; r < image.Height; r++)
        {
            for (int c = 0; c < image.Width; c++)
            {
                for (int ch = 0; ch < image.Channels; ch++)
                {
                    int count = 0;
                    for (int wr = 0; wr < windowHeight; wr++)
                    {
                        int sr = r + wr - anchorR;
                        for (int wc = 0; wc < windowWidth; wc++)
                        {
                            int sc = c + wc - anchorC;
                            window[count++] = Sample(image, sr, sc, ch, boundary);
                        }
                    }

                    Array.Sort(window);
                    result[r, c, ch] = window[window.Length / 2];
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>
    /// The local mean over an m×n window (MATLAB <c>imboxfilt</c>), computed with running sums so the
    /// cost is independent of the window size. Averaging is the inner loop of adaptive thresholding,
    /// guided filtering and box filtering alike, so it is worth having once and O(1) per pixel.
    /// </summary>
    public static ImageBuffer BoxMean(
        ImageBuffer image, int windowHeight, int windowWidth, Boundary boundary = Boundary.Replicate)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowWidth);

        int height = image.Height;
        int width = image.Width;
        var result = new ImageBuffer(height, width, image.Channels);
        var horizontal = new double[height * width];
        int anchorR = windowHeight / 2;
        int anchorC = windowWidth / 2;
        double area = (double)windowHeight * windowWidth;

        for (int ch = 0; ch < image.Channels; ch++)
        {
            // Horizontal pass: a prefix sum over each padded row turns a window sum into one subtraction.
            var padded = new double[width + windowWidth];
            for (int r = 0; r < height; r++)
            {
                padded[0] = 0;
                for (int i = 0; i < width + windowWidth - 1; i++)
                {
                    padded[i + 1] = padded[i] + Sample(image, r, i - anchorC, ch, boundary);
                }

                int rowBase = r * width;
                for (int c = 0; c < width; c++)
                {
                    horizontal[rowBase + c] = padded[c + windowWidth] - padded[c];
                }
            }

            // Vertical pass over the row sums, which makes the whole window sum separable.
            var column = new double[height + windowHeight];
            for (int c = 0; c < width; c++)
            {
                column[0] = 0;
                for (int i = 0; i < height + windowHeight - 1; i++)
                {
                    int sr = i - anchorR;
                    double value;
                    if ((uint)sr < (uint)height)
                    {
                        value = horizontal[(sr * width) + c];
                    }
                    else
                    {
                        // Zero padding contributes nothing; the other rules fold the index back inside,
                        // matching what the horizontal pass already did along the other axis.
                        value = boundary == Boundary.Zero
                            ? 0.0
                            : horizontal[(MapIndex(sr, height, boundary) * width) + c];
                    }

                    column[i + 1] = column[i] + value;
                }

                for (int r = 0; r < height; r++)
                {
                    result[r, c, ch] = (column[r + windowHeight] - column[r]) / area;
                }
            }
        }

        GC.KeepAlive(image);
        return result;
    }

    /// <summary>Maps an out-of-range index onto a real one for the given boundary rule.</summary>
    /// <remarks>
    /// <see cref="Boundary.Zero"/> has no in-range answer, so it clamps here and callers that need
    /// true constant padding sample through <see cref="Sample"/> instead. Only the separable pass uses
    /// this, and it pads by replication, mirroring, or wrapping.
    /// </remarks>
    internal static int MapIndex(int index, int length, Boundary boundary) => boundary switch
    {
        Boundary.Symmetric => Mirror(index, length),
        Boundary.Circular => Wrap(index, length),
        _ => Math.Clamp(index, 0, length - 1),
    };

    /// <summary>
    /// One sample, with out-of-range coordinates resolved by the boundary rule.
    /// <paramref name="padValue"/> supplies the constant for <see cref="Boundary.Zero"/>, which is how
    /// <c>imfilter</c>'s numeric pad argument reaches the inner loop.
    /// </summary>
    internal static double Sample(
        ImageBuffer image, int r, int c, int channel, Boundary boundary, double padValue = 0.0)
    {
        switch (boundary)
        {
            case Boundary.Replicate:
                r = Math.Clamp(r, 0, image.Height - 1);
                c = Math.Clamp(c, 0, image.Width - 1);
                return image[r, c, channel];
            case Boundary.Symmetric:
                r = Mirror(r, image.Height);
                c = Mirror(c, image.Width);
                return image[r, c, channel];
            case Boundary.Circular:
                r = Wrap(r, image.Height);
                c = Wrap(c, image.Width);
                return image[r, c, channel];
            default:
                return (uint)r < (uint)image.Height && (uint)c < (uint)image.Width
                    ? image[r, c, channel]
                    : padValue;
        }
    }

    /// <summary>Rotates a kernel by 180°, which turns correlation into convolution.</summary>
    internal static double[,] Rotate180(double[,] kernel)
    {
        int h = kernel.GetLength(0);
        int w = kernel.GetLength(1);
        var result = new double[h, w];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                result[r, c] = kernel[h - 1 - r, w - 1 - c];
            }
        }

        return result;
    }

    private static int Wrap(int index, int length)
    {
        index %= length;
        return index < 0 ? index + length : index;
    }

    private static int Mirror(int index, int length)
    {
        if (length == 1)
        {
            return 0;
        }

        while (index < 0 || index >= length)
        {
            if (index < 0)
            {
                index = -index - 1; // reflect across the leading edge (symmetric: abcba)
            }
            else if (index >= length)
            {
                index = (2 * length) - index - 1;
            }
        }

        return index;
    }

    /// <summary>
    /// The separable form of <see cref="Convolve2(double[,], double[,], Conv2Shape)"/>: <c>conv2(u, v, A)</c>, where the kernel is the
    /// outer product of two vectors and never has to be built. One pass along the rows with
    /// <paramref name="v"/> and one down the columns with <paramref name="u"/> costs
    /// <c>|u| + |v|</c> multiplies per pixel where the built kernel cost <c>|u|·|v|</c> — for the
    /// twenty-one-tap blur of a 2048-square image, a hundred and seventy-six million instead of one
    /// and a half billion.
    /// </summary>
    /// <remarks>
    /// Two passes of sums are not the same rounding as one pass over a materialised kernel, and this
    /// is the milestone's one deliberate divergence in the imaging layer: the products are formed
    /// differently, so the last bits differ. What does not differ is the shape, the anchor or the
    /// crop, and the tests pin the answer to the built-kernel one within its own precision.
    /// Threads take bands of output rows, so a band's inputs stay in one core's cache across all of
    /// <paramref name="u"/>'s taps, and no band can see another's work.
    /// </remarks>
    public static double[,] SeparableConvolve2(
        double[,] a, double[] u, double[] v, Conv2Shape shape = Conv2Shape.Full)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(u);
        ArgumentNullException.ThrowIfNull(v);
        int ah = a.GetLength(0);
        int aw = a.GetLength(1);
        int uh = u.Length;
        int vw = v.Length;
        if (ah == 0 || aw == 0 || uh == 0 || vw == 0)
        {
            return Convolve2(a, OuterProduct(u, v), shape);
        }

        int fullH = ah + uh - 1;
        int fullW = aw + vw - 1;
        var rows = new double[(long)ah * fullW <= int.MaxValue ? ah * fullW : 0];
        if (rows.Length == 0)
        {
            return Convolve2(a, OuterProduct(u, v), shape);
        }

        bool wide = (long)ah * fullW >= ParallelKernels.MemoryBoundThreshold;
        ParallelKernels.ForBlocks(ah, wide, i =>
        {
            Span<double> source = Flatten(a).Slice(i * aw, aw);
            Span<double> line = rows.AsSpan(i * fullW, fullW);
            for (int n = 0; n < vw; n++)
            {
                AddScaled(source, line.Slice(n, aw), v[n]);
            }
        });

        var full = new double[fullH, fullW];

        // Bands rather than single rows: a band's inputs are |u| + band rows of the intermediate,
        // which stays in one core's cache while every tap reads it.
        const int Band = 64;
        int bands = ((fullH - 1) / Band) + 1;
        ParallelKernels.ForBlocks(bands, wide, b =>
        {
            int first = b * Band;
            int last = Math.Min(first + Band, fullH);
            Span<double> target = Flatten(full);
            for (int y = first; y < last; y++)
            {
                Span<double> line = target.Slice(y * fullW, fullW);
                int from = Math.Max(0, y - ah + 1);
                int to = Math.Min(uh - 1, y);
                for (int m = from; m <= to; m++)
                {
                    AddScaled(rows.AsSpan((y - m) * fullW, fullW), line, u[m]);
                }
            }
        });

        return shape switch
        {
            Conv2Shape.Full => full,
            Conv2Shape.Same => Crop(full, uh / 2, vw / 2, ah, aw),
            Conv2Shape.Valid => ah >= uh && aw >= vw
                ? Crop(full, uh - 1, vw - 1, ah - uh + 1, aw - vw + 1)
                : new double[0, 0],
            _ => full,
        };
    }

    /// <summary>
    /// The size of <c>conv2</c>'s answer for an <paramref name="ah"/>×<paramref name="aw"/> image, a
    /// <paramref name="bh"/>×<paramref name="bw"/> kernel and a shape word, all four sides at least one.
    /// </summary>
    public static (int Rows, int Cols) Convolve2Size(int ah, int aw, int bh, int bw, Conv2Shape shape) => shape switch
    {
        Conv2Shape.Same => (ah, aw),
        Conv2Shape.Valid => ah >= bh && aw >= bw ? (ah - bh + 1, aw - bw + 1) : (0, 0),
        _ => (ah + bh - 1, aw + bw - 1),
    };

    /// <summary>Where the answer's top-left corner sits in the full convolution.</summary>
    private static (int Row, int Col) Convolve2Origin(int bh, int bw, Conv2Shape shape) => shape switch
    {
        Conv2Shape.Same => (bh / 2, bw / 2),
        Conv2Shape.Valid => (bh - 1, bw - 1),
        _ => (0, 0),
    };

    /// <summary>
    /// A tile of the answer one block owns: sixty-four rows by sixty-four columns of output, eight
    /// kilobytes of a column-major buffer per column strip, small enough that the tile and the
    /// source window that feeds it stay in one core's cache.
    /// </summary>
    private const int TileSide = 64;

    /// <summary>
    /// <see cref="Convolve2(double[,], double[,], Conv2Shape)"/> for a column-major image and kernel,
    /// answering column-major into <paramref name="output"/>, which the caller sizes with
    /// <see cref="Convolve2Size"/>. All four sides must be at least one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bits are the boxed road's: the scatter there visits the source in ascending row and then
    /// column, skips every source value equal to zero before any multiply, and adds each product to
    /// an accumulator that started at +0. Here every tile of the answer is owned by one block that
    /// visits, in that same order, exactly the source pixels that can reach it, applies the same
    /// skip, and adds the same products in the same order — so each output element sees the same
    /// additions in the same sequence and rounds the same way. The skip is what keeps a zero under an
    /// Inf or NaN tap from becoming a NaN: the scatter never added anything there, and neither does
    /// this (item 08b, ADR 0155).
    /// </para>
    /// <para>
    /// No sparse branch: the zero test is one compare per source pixel per tile that can see it, at
    /// most a few per pixel for any kernel narrower than a tile, and a nonzero list would cost a
    /// strided pass over the whole source to build — more than the test it would save. Blocks are
    /// the tiles, cut by the answer's shape alone, and run in parallel when the answer has at least
    /// <see cref="ParallelKernels.ComputeBoundThreshold"/> elements.
    /// </para>
    /// </remarks>
    public static void Convolve2(
        ReadOnlySpan<double> a, int ah, int aw,
        ReadOnlySpan<double> b, int bh, int bw,
        Conv2Shape shape, Span<double> output)
    {
        RequireSides(ah, aw, bh, bw);
        RequireLength(a.Length, ah, aw, "image");
        RequireLength(b.Length, bh, bw, "kernel");
        (int oh, int ow) = Convolve2Size(ah, aw, bh, bw, shape);
        RequireLength(output.Length, oh, ow, "output");
        if (oh == 0 || ow == 0)
        {
            return;
        }

        (int r0, int c0) = Convolve2Origin(bh, bw, shape);
        int tilesDown = ((oh - 1) / TileSide) + 1;
        int tilesAcross = ((ow - 1) / TileSide) + 1;
        bool parallel = (long)oh * ow >= ParallelKernels.ComputeBoundThreshold;
        unsafe
        {
            fixed (double* pa = a, pb = b, po = output)
            {
                double* fa = pa, fb = pb, fo = po;
                ParallelKernels.ForBlocks(tilesDown * tilesAcross, parallel, tile =>
                {
                    int ty = tile % tilesDown;
                    int tx = tile / tilesDown;
                    GatherTile(
                        fa, ah, aw, fb, bh, bw, fo, oh, r0, c0,
                        ty * TileSide, Math.Min((ty + 1) * TileSide, oh),
                        tx * TileSide, Math.Min((tx + 1) * TileSide, ow));
                });
            }
        }
    }

    /// <summary>
    /// One tile of the answer, rows <c>[y0, y1)</c> and columns <c>[x0, x1)</c> of the output, from
    /// the source pixels that can reach it, in the scatter's own order.
    /// </summary>
    private static unsafe void GatherTile(
        double* a, int ah, int aw, double* b, int bh, int bw, double* output, int oh,
        int r0, int c0, int y0, int y1, int x0, int x1)
    {
        for (int x = x0; x < x1; x++)
        {
            new Span<double>(output + ((long)x * oh) + y0, y1 - y0).Clear();
        }

        // The tile in the full convolution's coordinates, and the source window that reaches it.
        int fullY0 = y0 + r0;
        int fullY1 = y1 + r0;
        int fullX0 = x0 + c0;
        int fullX1 = x1 + c0;
        int iFrom = Math.Max(0, fullY0 - bh + 1);
        int iTo = Math.Min(ah - 1, fullY1 - 1);
        int jFrom = Math.Max(0, fullX0 - bw + 1);
        int jTo = Math.Min(aw - 1, fullX1 - 1);
        for (int i = iFrom; i <= iTo; i++)
        {
            int mFrom = Math.Max(0, fullY0 - i);
            int mTo = Math.Min(bh - 1, fullY1 - 1 - i);
            for (int j = jFrom; j <= jTo; j++)
            {
                double av = a[i + ((long)ah * j)];
                if (av == 0)
                {
                    continue;
                }

                int nFrom = Math.Max(0, fullX0 - j);
                int nTo = Math.Min(bw - 1, fullX1 - 1 - j);
                for (int n = nFrom; n <= nTo; n++)
                {
                    // Output row i + m − r0 of output column j + n − c0; kernel column n.
                    double* column = output + ((long)(j + n - c0) * oh) + (i - r0);
                    double* taps = b + ((long)n * bh);
                    for (int m = mFrom; m <= mTo; m++)
                    {
                        column[m] += av * taps[m];
                    }
                }
            }
        }
    }

    /// <summary>
    /// <see cref="SeparableConvolve2(double[,], double[], double[], Conv2Shape)"/> for a column-major
    /// image, answering column-major into <paramref name="output"/>, which the caller sizes with
    /// <see cref="Convolve2Size"/>; the image's sides and both tap vectors must be at least one, and
    /// the intermediate <c>ah·(aw+|v|−1)</c> must fit an array (see <see cref="SeparableFits"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pass order is the boxed road's exactly: every input row is convolved with
    /// <paramref name="v"/> across its columns into an <c>ah × (aw+|v|−1)</c> intermediate, taps
    /// ascending; then each answer row is the sum over <paramref name="u"/>'s taps, ascending, of
    /// the tap times the intermediate row above it. Each intermediate and answer element therefore
    /// receives the same products in the same order as before, through the same
    /// <see cref="AddScaled"/>, and rounds to the same bits.
    /// </para>
    /// <para>
    /// What changes is which way the loops walk the memory: in column-major storage a row is the
    /// strided direction, so the first pass takes a band of rows at a time and gathers each
    /// intermediate column of that band from the input columns behind it, contiguous stretches both;
    /// and the second pass walks answer columns, each a contiguous stretch of the intermediate
    /// shifted by a tap. Only the columns the shape asks for are computed in the first pass, and the
    /// second writes straight into the cropped answer. Bands and column blocks are cut by the shape
    /// alone and own their outputs, so the thread count changes no bits (item 08b, ADR 0155).
    /// </para>
    /// </remarks>
    public static void SeparableConvolve2(
        ReadOnlySpan<double> a, int ah, int aw,
        ReadOnlySpan<double> u, ReadOnlySpan<double> v,
        Conv2Shape shape, Span<double> output)
    {
        int uh = u.Length;
        int vw = v.Length;
        RequireSides(ah, aw, uh, vw);
        RequireLength(a.Length, ah, aw, "image");
        if (!SeparableFits(ah, aw, vw))
        {
            throw new ArgumentException($"a {ah}x{aw} image with {vw} taps across needs an intermediate too large for one array.");
        }

        (int oh, int ow) = Convolve2Size(ah, aw, uh, vw, shape);
        RequireLength(output.Length, oh, ow, "output");
        if (oh == 0 || ow == 0)
        {
            return;
        }

        (int r0, int c0) = Convolve2Origin(uh, vw, shape);
        int fullW = aw + vw - 1;
        int middle = ah * fullW;
        double[] rented = ArrayPool<double>.Shared.Rent(middle);
        try
        {
            rented.AsSpan(0, middle).Clear();
            bool wide = middle >= ParallelKernels.MemoryBoundThreshold;
            unsafe
            {
                fixed (double* pa = a, pu = u, pv = v, pi = rented, po = output)
                {
                    double* fa = pa, fu = pu, fv = pv, fi = pi, fo = po;

                    // Pass one, by bands of rows: intermediate column j of the band is the sum over
                    // the taps n of v[n] times input column j − n, taps ascending.
                    const int Band = 64;
                    int bands = ((ah - 1) / Band) + 1;
                    ParallelKernels.ForBlocks(bands, wide, band =>
                    {
                        int top = band * Band;
                        int height = Math.Min(top + Band, ah) - top;
                        for (int j = c0; j < c0 + ow; j++)
                        {
                            var line = new Span<double>(fi + ((long)j * ah) + top, height);
                            int nFrom = Math.Max(0, j - aw + 1);
                            int nTo = Math.Min(vw - 1, j);
                            for (int n = nFrom; n <= nTo; n++)
                            {
                                AddScaled(new ReadOnlySpan<double>(fa + ((long)(j - n) * ah) + top, height), line, fv[n]);
                            }
                        }
                    });

                    // Pass two, by blocks of answer columns: answer row y of column x is the sum
                    // over the taps m of u[m] times intermediate row y − m of the same column, taps
                    // ascending; each tap is one contiguous stretch.
                    const int Columns = 16;
                    int blocks = ((ow - 1) / Columns) + 1;
                    ParallelKernels.ForBlocks(blocks, wide, block =>
                    {
                        int first = block * Columns;
                        int last = Math.Min(first + Columns, ow);
                        for (int x = first; x < last; x++)
                        {
                            var answer = new Span<double>(fo + ((long)x * oh), oh);
                            answer.Clear();
                            double* source = fi + ((long)(x + c0) * ah);
                            for (int m = 0; m < uh; m++)
                            {
                                int yFrom = Math.Max(m, r0);
                                int yTo = Math.Min(m + ah, r0 + oh);
                                if (yFrom >= yTo)
                                {
                                    continue;
                                }

                                AddScaled(
                                    new ReadOnlySpan<double>(source + (yFrom - m), yTo - yFrom),
                                    answer.Slice(yFrom - r0, yTo - yFrom),
                                    fu[m]);
                            }
                        }
                    });
                }
            }
        }
        finally
        {
            ArrayPool<double>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Whether the separable road's intermediate, <c>ah·(aw+|v|−1)</c> doubles, fits one array; the
    /// boxed road falls back to the built kernel when it does not, and a caller of the packed one
    /// should do the same.
    /// </summary>
    public static bool SeparableFits(int ah, int aw, int vw) => (long)ah * (aw + vw - 1) <= int.MaxValue;

    private static void RequireSides(int ah, int aw, int bh, int bw)
    {
        if (ah < 1 || aw < 1 || bh < 1 || bw < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ah), $"the packed convolution wants every side at least one, got {ah}x{aw} and {bh}x{bw}.");
        }
    }

    private static void RequireLength(int length, int rows, int cols, string what)
    {
        if (length != (long)rows * cols)
        {
            throw new ArgumentException($"the {what} span holds {length} elements where {rows}x{cols} needs {(long)rows * cols}.");
        }
    }

    /// <summary>The kernel the separable form never builds, for the edge cases that still want it.</summary>
    private static double[,] OuterProduct(double[] u, double[] v)
    {
        var outer = new double[u.Length, v.Length];
        for (int r = 0; r < u.Length; r++)
        {
            for (int c = 0; c < v.Length; c++)
            {
                outer[r, c] = u[r] * v[c];
            }
        }

        return outer;
    }

    /// <summary>A rectangular array's storage as one span; it is already contiguous and row-major.</summary>
    private static Span<double> Flatten(double[,] array) =>
        MemoryMarshal.CreateSpan(
            ref Unsafe.As<byte, double>(ref MemoryMarshal.GetArrayDataReference(array)),
            array.Length);

    /// <summary>
    /// <c>into[i] += scale · from[i]</c>, four at a time. The multiply and the add stay separate
    /// operations: a fused one would round differently on the machines that have it and not on the
    /// ones that do not, and an answer must not depend on which machine ran it.
    /// </summary>
    private static void AddScaled(ReadOnlySpan<double> from, Span<double> into, double scale)
    {
        int width = Vector<double>.Count;
        int i = 0;
        if (from.Length >= width)
        {
            ref double src = ref MemoryMarshal.GetReference(from);
            ref double dst = ref MemoryMarshal.GetReference(into);
            var factor = new Vector<double>(scale);
            for (; i <= from.Length - width; i += width)
            {
                Vector<double> sum = Vector.LoadUnsafe(ref dst, (nuint)i)
                    + (factor * Vector.LoadUnsafe(ref src, (nuint)i));
                Vector.StoreUnsafe(sum, ref dst, (nuint)i);
            }
        }

        for (; i < from.Length; i++)
        {
            into[i] += scale * from[i];
        }
    }

    private static double[,] Crop(double[,] source, int rowOffset, int colOffset, int height, int width)
    {
        var result = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                result[r, c] = source[rowOffset + r, colOffset + c];
            }
        }

        return result;
    }
}

/// <summary>Output-size convention for <see cref="Filters.Convolve2(double[,], double[,], Conv2Shape)"/>.</summary>
public enum Conv2Shape
{
    /// <summary>The full (ah+bh-1)×(aw+bw-1) convolution.</summary>
    Full,

    /// <summary>The central part the same size as the first operand.</summary>
    Same,

    /// <summary>Only the region computed without zero-padding.</summary>
    Valid,
}
