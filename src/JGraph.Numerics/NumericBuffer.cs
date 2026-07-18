namespace JGraph.Numerics;

/// <summary>The backing strategy of a <see cref="NumericBuffer"/> (diagnostics and tests).</summary>
public enum BufferKind
{
    /// <summary>A plain managed <c>double[]</c> on the GC heap.</summary>
    Managed,

    /// <summary>Unmanaged memory from <see cref="System.Runtime.InteropServices.NativeMemory"/>, invisible to the GC.</summary>
    Native,

    /// <summary>An SSD-backed memory-mapped temp file paged on demand by the OS.</summary>
    Mapped,
}

/// <summary>
/// Flat contiguous storage for doubles, backed by managed, native, or file-mapped memory.
/// Buffers are created through <see cref="BufferAllocator"/>, which picks the backing strategy
/// from the size of the request and the machine's available memory.
/// </summary>
/// <remarks>
/// Lifetime contract: the unmanaged backends free their memory from a finalizer, so any code that
/// captures <see cref="AsSpan()"/> must keep the buffer reachable until its last use of the span —
/// call <see cref="GC.KeepAlive(object)"/> on the buffer afterwards (the kernels in
/// <see cref="PackedMath"/> do this centrally). <see cref="Dispose"/> is idempotent; using a span
/// after disposal is undefined.
/// </remarks>
public abstract class NumericBuffer : IDisposable
{
    /// <summary>Number of doubles in the buffer.</summary>
    public int Length { get; protected init; }

    /// <summary>The backing strategy.</summary>
    public abstract BufferKind Kind { get; }

    /// <summary>The whole buffer as a writable span. See the class remarks for the lifetime contract.</summary>
    public abstract Span<double> AsSpan();

    /// <summary>A writable window of the buffer. See the class remarks for the lifetime contract.</summary>
    public Span<double> AsSpan(int start, int length) => AsSpan().Slice(start, length);

    /// <summary>Releases the backing memory. Idempotent.</summary>
    public abstract void Dispose();
}

/// <summary>
/// Small-array backend: a plain managed <c>double[]</c>. The GC owns the memory, so there is no
/// finalizer and <see cref="Dispose"/> is a no-op.
/// </summary>
public sealed class ManagedBuffer : NumericBuffer
{
    private readonly double[] _array;

    /// <summary>Allocates a zero-filled managed buffer.</summary>
    public ManagedBuffer(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _array = length == 0 ? System.Array.Empty<double>() : new double[length];
        Length = length;
    }

    private ManagedBuffer(double[] array)
    {
        _array = array;
        Length = array.Length;
    }

    /// <summary>
    /// Wraps a caller-built array without copying. The buffer owns the array from here on; the
    /// caller must not keep writing through its own reference.
    /// </summary>
    public static ManagedBuffer Adopt(double[] array)
    {
        ArgumentNullException.ThrowIfNull(array);
        return new ManagedBuffer(array);
    }

    /// <inheritdoc />
    public override BufferKind Kind => BufferKind.Managed;

    /// <inheritdoc />
    public override Span<double> AsSpan() => _array;

    /// <inheritdoc />
    public override void Dispose()
    {
    }
}
