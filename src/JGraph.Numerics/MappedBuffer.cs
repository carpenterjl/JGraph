using System.IO.MemoryMappedFiles;

namespace JGraph.Numerics;

/// <summary>
/// Large-array backend for machines where RAM is scarce: an SSD-backed temp file mapped into the
/// address space, so the OS virtual-memory manager pages it on demand instead of the process
/// committing physical RAM up front. Slower than <see cref="NativeBuffer"/>, but a 16 GB laptop
/// gets a working multi-hundred-MB array instead of an <see cref="OutOfMemoryException"/>.
/// </summary>
/// <remarks>
/// The file is opened with <see cref="FileOptions.DeleteOnClose"/>, so the OS removes it when the
/// last handle closes — including when the process crashes. Orphans from power loss are swept by
/// <see cref="BufferAllocator.SweepOrphans"/> at application startup. Like <see cref="NativeBuffer"/>
/// the buffer registers its size as GC memory pressure (M6, ADR 0162): a mapped file holds disk,
/// handles and address space that no managed figure counts, and an abandoned one must still make
/// a collection more likely rather than less.
/// </remarks>
public sealed unsafe class MappedBuffer : NumericBuffer
{
    private readonly FileStream _stream;
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly Action? _onFreed;
    private byte* _base;
    private bool _disposed;

    /// <summary>File extension for backing files, used by the orphan sweep.</summary>
    public const string FileExtension = ".jgbuf";

    /// <summary>
    /// Creates a zero-filled buffer backed by a fresh temp file in <paramref name="directory"/>.
    /// <paramref name="onFreed"/> lets the allocator track outstanding mapped bytes and files; it
    /// runs exactly once, when the file is actually released.
    /// </summary>
    public MappedBuffer(int length, string directory, Action? onFreed = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        Directory.CreateDirectory(directory);
        long bytes = (long)length * sizeof(double);
        string path = Path.Combine(directory, $"{Environment.ProcessId:x}-{Guid.NewGuid():N}{FileExtension}");

        // FileShare.None: a live mapping holds the file exclusively, which is what lets the
        // orphan sweep distinguish stale files (deletable) from ones another process still maps.
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                                 bufferSize: 1, FileOptions.DeleteOnClose);
        try
        {
            _stream.SetLength(bytes); // unwritten pages read back as zeros
            _mmf = MemoryMappedFile.CreateFromFile(_stream, mapName: null, bytes,
                MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true);
            _accessor = _mmf.CreateViewAccessor(0, bytes, MemoryMappedFileAccess.ReadWrite);
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _base);
        }
        catch
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
            _stream.Dispose();
            throw;
        }

        GC.AddMemoryPressure(bytes);
        Length = length;
        _onFreed = onFreed;
    }

    /// <inheritdoc />
    public override BufferKind Kind => BufferKind.Mapped;

    /// <inheritdoc />
    public override Span<double> AsSpan()
    {
        byte* basePtr = _base;
        return basePtr is null
            ? throw new ObjectDisposedException(nameof(MappedBuffer))
            : new Span<double>(basePtr + _accessor.PointerOffset, Length);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Teardown();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Backstop for abandoned buffers. Finalization order between this object and its SafeHandles
    /// is not guaranteed, so every step is exception-guarded; the OS handles free themselves via
    /// their own critical finalizers regardless, and DeleteOnClose then removes the file.
    /// </summary>
    ~MappedBuffer() => Teardown();

    private void Teardown()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_accessor is null)
        {
            return; // the constructor threw before the mapping existed; the stream disposed itself
        }

        if (_base is not null)
        {
            _base = null;
            try
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            _accessor.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _mmf.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _stream.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        GC.RemoveMemoryPressure((long)Length * sizeof(double));
        _onFreed?.Invoke();
    }
}
