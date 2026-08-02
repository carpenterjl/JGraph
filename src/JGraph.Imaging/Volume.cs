using JGraph.Numerics;

namespace JGraph.Imaging;

/// <summary>
/// A three-dimensional sample field — a stack of slices — held column-major, so sample
/// <c>(r, c, p)</c> lives at <c>r + c·Height + p·Height·Width</c>.
/// </summary>
/// <remarks>
/// <para>
/// A volume is not an image with extra channels, and this deliberately is not an
/// <see cref="ImageBuffer"/> with a third size. An image's channels are different measurements of the
/// same place — red, green and blue at one pixel — and every filter in this project treats them
/// independently for that reason. A volume's planes are the same measurement at different places, so
/// a filter must reach across them exactly as it reaches across rows. Keeping the two types apart is
/// what stops <c>imgaussfilt</c> and <c>imgaussfilt3</c> from quietly meaning the same thing.
/// </para>
/// <para>
/// The layout is column-major because MATLAB's is, and because a volume reaches this project as a
/// plain N-D script array whose storage is already column-major: reading one costs a copy rather than
/// a transposition. Storage comes from <see cref="BufferAllocator.Shared"/>, so a 500³ volume — a
/// gigabyte of samples — spills to native memory or a mapped file instead of throwing.
/// </para>
/// </remarks>
public sealed class Volume : IDisposable
{
    private readonly NumericBuffer _buffer;
    private bool _disposed;

    /// <summary>Allocates a zero-filled volume of the given size.</summary>
    /// <param name="height">Rows; must be positive.</param>
    /// <param name="width">Columns; must be positive.</param>
    /// <param name="depth">Planes; must be positive.</param>
    public Volume(int height, int width, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        Height = height;
        Width = width;
        Depth = depth;
        _buffer = BufferAllocator.Shared.Allocate((long)height * width * depth);
    }

    /// <summary>Rows.</summary>
    public int Height { get; }

    /// <summary>Columns.</summary>
    public int Width { get; }

    /// <summary>Planes.</summary>
    public int Depth { get; }

    /// <summary>Total sample count, <c>Height · Width · Depth</c>.</summary>
    public long SampleCount => (long)Height * Width * Depth;

    /// <summary>How far apart two samples one plane apart sit in the flat buffer.</summary>
    public int PlaneStride => Height * Width;

    /// <summary>The whole field as a writable span; see <see cref="ImageBuffer"/> for the lifetime contract.</summary>
    public Span<double> Samples => _buffer.AsSpan();

    /// <summary>The size as MATLAB reports it.</summary>
    public int[] Size => [Height, Width, Depth];

    /// <summary>Reads or writes one sample, with bounds checking.</summary>
    public double this[int r, int c, int p]
    {
        get => Samples[Index(r, c, p)];
        set => Samples[Index(r, c, p)] = value;
    }

    /// <summary>Adopts already column-major samples as a volume of the given size.</summary>
    public static Volume From(ReadOnlySpan<double> columnMajor, int height, int width, int depth)
    {
        var volume = new Volume(height, width, depth);
        if (columnMajor.Length != volume.SampleCount)
        {
            volume.Dispose();
            throw new ArgumentException(
                $"a {height}x{width}x{depth} volume needs {(long)height * width * depth} samples, " +
                $"but {columnMajor.Length} were given.",
                nameof(columnMajor));
        }

        columnMajor.CopyTo(volume.Samples);
        return volume;
    }

    /// <summary>A zero-filled volume the same size as <paramref name="model"/>.</summary>
    public static Volume Like(Volume model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new Volume(model.Height, model.Width, model.Depth);
    }

    /// <summary>Whether two volumes have the same size.</summary>
    public static bool SameSize(Volume a, Volume b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Height == b.Height && a.Width == b.Width && a.Depth == b.Depth;
    }

    /// <summary>The flat index of a sample, with bounds checking.</summary>
    public int Index(int r, int c, int p)
    {
        if ((uint)r >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(r), r, $"row out of range [0, {Height - 1}]");
        }

        if ((uint)c >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(nameof(c), c, $"column out of range [0, {Width - 1}]");
        }

        if ((uint)p >= (uint)Depth)
        {
            throw new ArgumentOutOfRangeException(nameof(p), p, $"plane out of range [0, {Depth - 1}]");
        }

        return r + (c * Height) + (p * Height * Width);
    }

    /// <summary>
    /// A sample read through a boundary rule, so a filter can ask for a neighbour that does not exist
    /// and get the answer its rule implies rather than a bounds error.
    /// </summary>
    public double At(int r, int c, int p, Filters.Boundary boundary, double padValue = 0.0)
    {
        int rr = Fold(r, Height, boundary);
        int cc = Fold(c, Width, boundary);
        int pp = Fold(p, Depth, boundary);
        if (rr < 0 || cc < 0 || pp < 0)
        {
            return padValue;
        }

        return Samples[rr + (cc * Height) + (pp * Height * Width)];
    }

    /// <summary>Creates an independent copy with its own storage.</summary>
    public Volume Clone()
    {
        var copy = new Volume(Height, Width, Depth);
        Samples.CopyTo(copy.Samples);
        GC.KeepAlive(this);
        return copy;
    }

    /// <summary>One plane of the volume as a single-channel image, for the 2-D code that already exists.</summary>
    public ImageBuffer Slice(int plane)
    {
        var image = new ImageBuffer(Height, Width, 1);
        Span<double> source = Samples;
        Span<double> target = image.Pixels;
        int offset = plane * Height * Width;
        for (int c = 0; c < Width; c++)
        {
            for (int r = 0; r < Height; r++)
            {
                target[(r * Width) + c] = source[offset + r + (c * Height)];
            }
        }

        GC.KeepAlive(this);
        return image;
    }

    /// <summary>Writes a single-channel image back into one plane.</summary>
    public void SetSlice(int plane, ImageBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Height != Height || image.Width != Width)
        {
            throw new ArgumentException("slice size does not match the volume.", nameof(image));
        }

        Span<double> target = Samples;
        ReadOnlySpan<double> source = image.Pixels;
        int offset = plane * Height * Width;
        int channels = image.Channels;
        for (int c = 0; c < Width; c++)
        {
            for (int r = 0; r < Height; r++)
            {
                target[offset + r + (c * Height)] = source[((r * Width) + c) * channels];
            }
        }

        GC.KeepAlive(this);
    }

    /// <summary>Releases the backing memory. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _buffer.Dispose();
    }

    // A coordinate folded back inside the volume by the boundary rule; -1 means "there is nothing
    // there", which only the constant rule can answer.
    private static int Fold(int i, int extent, Filters.Boundary boundary)
    {
        if (i >= 0 && i < extent)
        {
            return i;
        }

        switch (boundary)
        {
            case Filters.Boundary.Replicate:
                return Math.Clamp(i, 0, extent - 1);

            case Filters.Boundary.Symmetric:
            {
                // Mirror on the sample, repeatedly: a pad wider than the volume is legal and has to
                // keep folding rather than land outside on the second bounce.
                int period = 2 * extent;
                int m = ((i % period) + period) % period;
                return m < extent ? m : period - 1 - m;
            }

            case Filters.Boundary.Circular:
                return ((i % extent) + extent) % extent;

            default:
                return -1;
        }
    }
}
