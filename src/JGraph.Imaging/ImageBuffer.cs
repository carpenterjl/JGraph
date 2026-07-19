using JGraph.Numerics;

namespace JGraph.Imaging;

/// <summary>
/// A raster image as a flat, interleaved, row-major buffer of <see cref="double"/> samples in the
/// range [0, 1] (the MATLAB <c>im2double</c> convention). Grayscale images have one channel; colour
/// images have three (R, G, B). Backing storage is a <see cref="NumericBuffer"/> allocated through
/// <see cref="BufferAllocator.Shared"/>, so large images transparently spill from managed memory to
/// native memory to an SSD-backed mapped file instead of throwing <see cref="OutOfMemoryException"/>.
/// </summary>
/// <remarks>
/// Pixel <c>(r, c)</c> channel <c>ch</c> lives at flat index <c>((r * Width) + c) * Channels + ch</c>.
/// The buffer owns unmanaged memory, so callers must <see cref="Dispose"/> images they own; the JGS
/// runtime disposes image values left in a completed run's locals. Because the backend can be
/// unmanaged, any code that holds a <see cref="Pixels"/> span must keep the image reachable until its
/// last use of the span (call <see cref="GC.KeepAlive(object)"/> afterwards in hot loops).
/// </remarks>
public sealed class ImageBuffer : IDisposable
{
    private readonly NumericBuffer _buffer;
    private bool _disposed;

    /// <summary>Allocates a zero-filled image of the given dimensions.</summary>
    /// <param name="height">Row count; must be positive.</param>
    /// <param name="width">Column count; must be positive.</param>
    /// <param name="channels">1 (grayscale) or 3 (RGB).</param>
    public ImageBuffer(int height, int width, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        if (channels is not (1 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels,
                "image channels must be 1 (grayscale) or 3 (RGB)");
        }

        Height = height;
        Width = width;
        Channels = channels;
        _buffer = BufferAllocator.Shared.Allocate((long)height * width * channels);
    }

    private ImageBuffer(int height, int width, int channels, NumericBuffer buffer)
    {
        Height = height;
        Width = width;
        Channels = channels;
        _buffer = buffer;
    }

    /// <summary>Number of rows.</summary>
    public int Height { get; }

    /// <summary>Number of columns.</summary>
    public int Width { get; }

    /// <summary>Number of channels: 1 (grayscale) or 3 (RGB).</summary>
    public int Channels { get; }

    /// <summary>Total sample count, <c>Height * Width * Channels</c>.</summary>
    public long SampleCount => (long)Height * Width * Channels;

    /// <summary>Whether every channel of every pixel is exactly 0 or 1 (a logical/binary image).</summary>
    /// <remarks>Computed on demand; edge/threshold outputs produce true binary images.</remarks>
    public bool IsBinary
    {
        get
        {
            ReadOnlySpan<double> px = Pixels;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i] != 0.0 && px[i] != 1.0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>The backing buffer (flat, length <see cref="SampleCount"/>).</summary>
    public NumericBuffer Buffer => _buffer;

    /// <summary>The whole sample buffer as a writable span. See the class remarks for the lifetime contract.</summary>
    public Span<double> Pixels => _buffer.AsSpan();

    /// <summary>Reads or writes one channel sample, with bounds checking.</summary>
    public double this[int r, int c, int ch]
    {
        get => Pixels[Index(r, c, ch)];
        set => Pixels[Index(r, c, ch)] = value;
    }

    private int Index(int r, int c, int ch)
    {
        if ((uint)r >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(r), r, $"row out of range [0, {Height - 1}]");
        }

        if ((uint)c >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(nameof(c), c, $"column out of range [0, {Width - 1}]");
        }

        if ((uint)ch >= (uint)Channels)
        {
            throw new ArgumentOutOfRangeException(nameof(ch), ch, $"channel out of range [0, {Channels - 1}]");
        }

        return ((r * Width) + c) * Channels + ch;
    }

    /// <summary>Creates an independent copy with its own backing buffer.</summary>
    public ImageBuffer Clone()
    {
        var copy = new ImageBuffer(Height, Width, Channels, BufferAllocator.Shared.Allocate(SampleCount));
        Pixels.CopyTo(copy.Pixels);
        GC.KeepAlive(this);
        return copy;
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
}
