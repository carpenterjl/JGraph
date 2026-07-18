using System.Runtime.InteropServices;

namespace JGraph.Numerics;

/// <summary>
/// Large-array backend for machines with RAM to spare: zeroed unmanaged memory from
/// <see cref="NativeMemory"/>. The block is invisible to the GC (never scanned, never moved), but
/// <see cref="GC.AddMemoryPressure"/> is registered so that abandoned buffers — script variables
/// that went out of scope without an explicit clear — still trigger timely collection and
/// finalization instead of piling up unnoticed.
/// </summary>
public sealed unsafe class NativeBuffer : NumericBuffer
{
    private void* _ptr;
    private readonly Action? _onFreed;

    /// <summary>
    /// Allocates a zero-filled native buffer. <paramref name="onFreed"/> lets the allocator track
    /// outstanding native bytes; it runs exactly once, when the memory is actually freed.
    /// </summary>
    public NativeBuffer(int length, Action? onFreed = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        _ptr = NativeMemory.AllocZeroed((nuint)length, sizeof(double));
        GC.AddMemoryPressure((long)length * sizeof(double));
        Length = length;
        _onFreed = onFreed;
    }

    /// <inheritdoc />
    public override BufferKind Kind => BufferKind.Native;

    /// <inheritdoc />
    public override Span<double> AsSpan()
    {
        void* ptr = _ptr;
        return ptr is null
            ? throw new ObjectDisposedException(nameof(NativeBuffer))
            : new Span<double>(ptr, Length);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Free();
        GC.SuppressFinalize(this);
    }

    /// <summary>Backstop for abandoned buffers; <see cref="Dispose"/> is the deterministic path.</summary>
    ~NativeBuffer() => Free();

    private void Free()
    {
        void* ptr = _ptr;
        if (ptr is null)
        {
            return;
        }

        _ptr = null;
        NativeMemory.Free(ptr);
        GC.RemoveMemoryPressure((long)Length * sizeof(double));
        _onFreed?.Invoke();
    }
}
