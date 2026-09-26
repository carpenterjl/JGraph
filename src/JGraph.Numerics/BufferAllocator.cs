using System.Globalization;

namespace JGraph.Numerics;

/// <summary>Machine memory facts the allocation policy reads. Seam for tests.</summary>
public interface IMemoryInfo
{
    /// <summary>Total physical memory available to the process (machine RAM or container limit).</summary>
    long TotalPhysicalBytes { get; }

    /// <summary>Machine-wide committed memory load, so other applications' usage is respected.</summary>
    long MemoryLoadBytes { get; }
}

/// <summary>The real memory source: <see cref="GC.GetGCMemoryInfo()"/>. No P/Invoke.</summary>
public sealed class GcMemoryInfo : IMemoryInfo
{
    /// <inheritdoc />
    public long TotalPhysicalBytes => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    /// <inheritdoc />
    public long MemoryLoadBytes => GC.GetGCMemoryInfo().MemoryLoadBytes;
}

/// <summary>Disk facts the mapped budget reads (M6, ADR 0162). Seam for tests.</summary>
public interface IDiskInfo
{
    /// <summary>Free bytes on the volume holding <paramref name="directory"/>.</summary>
    long AvailableFreeBytes(string directory);
}

/// <summary>The real disk source: <see cref="DriveInfo.AvailableFreeSpace"/> of the directory's root.</summary>
public sealed class DriveDiskInfo : IDiskInfo
{
    /// <inheritdoc />
    public long AvailableFreeBytes(string directory)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(directory));
            return root is null ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return long.MaxValue; // an unreadable volume is not a full one; the file system will say
        }
    }
}

/// <summary>Forces a single backing strategy regardless of size or memory headroom.</summary>
public enum BufferMode
{
    /// <summary>Pick the backend from size and available memory (the default).</summary>
    Automatic,

    /// <summary>Always <see cref="ManagedBuffer"/>.</summary>
    Managed,

    /// <summary>Always <see cref="NativeBuffer"/>.</summary>
    Native,

    /// <summary>Always <see cref="MappedBuffer"/>.</summary>
    Mapped,
}

/// <summary>
/// Chooses the backing strategy for a numeric buffer: small requests stay managed; large requests
/// go to native memory while physical RAM has headroom, and degrade to an SSD-backed mapped file
/// when it does not — big arrays get slower instead of throwing <see cref="OutOfMemoryException"/>,
/// on a 16 GB laptop and a 64 GB workstation alike.
/// </summary>
/// <remarks>
/// <para>
/// Two budgets, not one (M6, ADR 0162). The <b>native</b> budget is RAM headroom, and a request that
/// does not fit goes mapped, as it always has. The <b>mapped</b> budget is the mapped directory's
/// free disk space less a reserve, and a ceiling on live backing files — the resources a mapped
/// buffer holds that no memory figure counts. Only when a request would cross the budget of the
/// backend it is headed for does the allocator ask the runtime to collect and run finalizers,
/// <b>once</b>, and re-check that same budget: the ownership model leaves a payload that others may
/// still read to the finalizer (an exposed one, M6), so a session that allocates and clears large
/// arrays in a loop reclaims them here, at the moment it needs the room, rather than holding disk
/// and handles until the managed heap happens to fill. Nothing is ever disposed by the allocator:
/// a collection frees only what no wrapper can reach. A mapped request still over budget after the
/// collection is refused with <see cref="OutOfMemoryMessage"/>.
/// </para>
/// <para>
/// Environment overrides (read once, when <see cref="Shared"/> is first used):
/// <c>JGRAPH_BUFFER_MODE=managed|native|mapped</c> forces a backend;
/// <c>JGRAPH_BUFFER_MANAGED_MAX</c> (elements) and <c>JGRAPH_BUFFER_NATIVE_FRACTION</c> (0..1)
/// tune the automatic policy.
/// </para>
/// </remarks>
public sealed class BufferAllocator
{
    private readonly IMemoryInfo _memory;
    private readonly IDiskInfo _disk;
    private long _outstandingNativeBytes;
    private long _outstandingMappedBytes;
    private int _liveMappedFiles;
    private long _collections;

    /// <summary>MATLAB's words for a refused allocation.</summary>
    public const string OutOfMemoryMessage = "Out of memory.";

    /// <summary>The process-wide allocator, configured from environment variables.</summary>
    public static BufferAllocator Shared { get; } = CreateFromEnvironment();

    /// <summary>Creates an allocator over an explicit memory source (tests inject a fake).</summary>
    public BufferAllocator(IMemoryInfo memory) : this(memory, new DriveDiskInfo())
    {
    }

    /// <summary>Creates an allocator over explicit memory and disk sources (tests inject fakes).</summary>
    public BufferAllocator(IMemoryInfo memory, IDiskInfo disk)
    {
        _memory = memory;
        _disk = disk;
    }

    /// <summary>Forced backend, or <see cref="BufferMode.Automatic"/>.</summary>
    public BufferMode Mode { get; init; } = BufferMode.Automatic;

    /// <summary>Requests at or below this element count use a plain managed array (default 1M ≈ 8 MB).</summary>
    public int ManagedMaxElements { get; init; } = 1 << 20;

    /// <summary>A native request may use at most this fraction of the current free-RAM headroom.</summary>
    public double NativeHeadroomFraction { get; init; } = 0.5;

    /// <summary>Physical memory always left untouched by the native policy (default 1 GB).</summary>
    public long MinFreeReserveBytes { get; init; } = 1L << 30;

    /// <summary>Directory for mapped-buffer temp files.</summary>
    public string MappedDirectory { get; init; } = DefaultMappedDirectory;

    /// <summary>Disk space always left untouched by the mapped policy (default 1 GB).</summary>
    public long MappedReserveBytes { get; init; } = 1L << 30;

    /// <summary>The most backing files this allocator holds open at once (default 1024).</summary>
    public int MappedMaxFiles { get; init; } = 1024;

    /// <summary>The default mapped-file directory: <c>%TEMP%/JGraph/buffers</c>.</summary>
    public static string DefaultMappedDirectory =>
        Path.Combine(Path.GetTempPath(), "JGraph", "buffers");

    /// <summary>Native bytes currently allocated and not yet freed (policy input and diagnostics).</summary>
    public long OutstandingNativeBytes => Interlocked.Read(ref _outstandingNativeBytes);

    /// <summary>Mapped bytes currently backed by a live file (policy input and diagnostics).</summary>
    public long OutstandingMappedBytes => Interlocked.Read(ref _outstandingMappedBytes);

    /// <summary>Backing files currently open (policy input and diagnostics).</summary>
    public int LiveMappedFiles => Volatile.Read(ref _liveMappedFiles);

    /// <summary>How many times a request over its budget has asked the runtime to collect (diagnostics).</summary>
    public long Collections => Interlocked.Read(ref _collections);

    /// <summary>Allocates a zero-filled buffer of <paramref name="elementCount"/> doubles.</summary>
    public NumericBuffer Allocate(long elementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        if (elementCount > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(elementCount), elementCount,
                $"array of {elementCount} elements exceeds the supported maximum of {int.MaxValue}");
        }

        int count = (int)elementCount;
        return Mode switch
        {
            BufferMode.Managed => new ManagedBuffer(count),
            BufferMode.Native when count > 0 => AllocateNative(count),
            BufferMode.Mapped when count > 0 => AllocateMapped(count),
            _ => AllocateAutomatic(count),
        };
    }

    /// <summary>
    /// Allocates a buffer of <paramref name="elementCount"/> doubles whose contents are
    /// <em>unspecified</em>: for a destination a kernel writes in full before anything can read it
    /// (Z2e, ADR 0173), where zeroing is a whole extra pass over the memory. A managed request skips
    /// the runtime's clearing, a native one the zeroing allocation; a mapped file is zero anyway.
    /// The caller owns the promise that every element is written before it is read.
    /// </summary>
    public NumericBuffer AllocateForOverwrite(long elementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        if (elementCount > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(elementCount), elementCount,
                $"array of {elementCount} elements exceeds the supported maximum of {int.MaxValue}");
        }

        int count = (int)elementCount;
        switch (Mode)
        {
            case BufferMode.Managed:
                return UninitializedManaged(count);
            case BufferMode.Native when count > 0:
                return AllocateNative(count, zeroed: false);
            case BufferMode.Mapped when count > 0:
                return AllocateMapped(count);
        }

        if (count <= ManagedMaxElements)
        {
            return UninitializedManaged(count);
        }

        long bytes = (long)count * sizeof(double);
        if (!FitsNative(bytes))
        {
            CollectOnce();
        }

        return FitsNative(bytes) ? AllocateNative(count, zeroed: false) : AllocateMapped(count);
    }

    private static ManagedBuffer UninitializedManaged(int count) =>
        count == 0 ? new ManagedBuffer(0) : ManagedBuffer.Adopt(GC.AllocateUninitializedArray<double>(count));

    private NumericBuffer AllocateAutomatic(int count)
    {
        if (count <= ManagedMaxElements)
        {
            return new ManagedBuffer(count);
        }

        long bytes = (long)count * sizeof(double);
        if (!FitsNative(bytes))
        {
            // Over the native budget: what is holding it may be payloads nobody can reach any
            // more, waiting on their finalizers. One collection, then the same question again.
            CollectOnce();
        }

        return FitsNative(bytes) ? AllocateNative(count) : AllocateMapped(count);
    }

    private bool FitsNative(long bytes)
    {
        long headroom = _memory.TotalPhysicalBytes - _memory.MemoryLoadBytes
                        - MinFreeReserveBytes - OutstandingNativeBytes;
        return bytes <= NativeHeadroomFraction * headroom;
    }

    private NativeBuffer AllocateNative(int count, bool zeroed = true)
    {
        long bytes = (long)count * sizeof(double);
        Interlocked.Add(ref _outstandingNativeBytes, bytes);
        try
        {
            return new NativeBuffer(count, onFreed: () => Interlocked.Add(ref _outstandingNativeBytes, -bytes), zeroed);
        }
        catch
        {
            Interlocked.Add(ref _outstandingNativeBytes, -bytes);
            throw;
        }
    }

    private MappedBuffer AllocateMapped(int count)
    {
        long bytes = (long)count * sizeof(double);
        if (!FitsMapped(bytes))
        {
            CollectOnce();
            if (!FitsMapped(bytes))
            {
                throw new OutOfMemoryException(OutOfMemoryMessage);
            }
        }

        Interlocked.Add(ref _outstandingMappedBytes, bytes);
        Interlocked.Increment(ref _liveMappedFiles);
        try
        {
            return new MappedBuffer(count, MappedDirectory, onFreed: () =>
            {
                Interlocked.Add(ref _outstandingMappedBytes, -bytes);
                Interlocked.Decrement(ref _liveMappedFiles);
            });
        }
        catch
        {
            Interlocked.Add(ref _outstandingMappedBytes, -bytes);
            Interlocked.Decrement(ref _liveMappedFiles);
            throw;
        }
    }

    /// <summary>
    /// The mapped budget: this allocator's outstanding bytes plus the request against the volume's
    /// free space less the reserve, and the file ceiling. The outstanding count is the process's
    /// own and the free space is the volume's, so on a real disk a live file is counted on both
    /// sides — the budget errs towards refusing a request the disk could just have held, never
    /// towards filling it.
    /// </summary>
    private bool FitsMapped(long bytes) =>
        LiveMappedFiles < MappedMaxFiles
        && OutstandingMappedBytes + bytes <= _disk.AvailableFreeBytes(MappedDirectory) - MappedReserveBytes;

    private void CollectOnce()
    {
        Interlocked.Increment(ref _collections);
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    /// <summary>
    /// Deletes stale mapped-buffer files left behind by power loss (a crash alone never orphans:
    /// the files are opened with delete-on-close). Files still mapped by a live process hold an
    /// exclusive handle, fail the delete, and are skipped.
    /// </summary>
    public static void SweepOrphans(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(directory, "*" + MappedBuffer.FileExtension))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static BufferAllocator CreateFromEnvironment()
    {
        var mode = Environment.GetEnvironmentVariable("JGRAPH_BUFFER_MODE")?.ToLowerInvariant() switch
        {
            "managed" => BufferMode.Managed,
            "native" => BufferMode.Native,
            "mapped" => BufferMode.Mapped,
            _ => BufferMode.Automatic,
        };

        int managedMax = 1 << 20;
        if (int.TryParse(Environment.GetEnvironmentVariable("JGRAPH_BUFFER_MANAGED_MAX"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMax) && parsedMax > 0)
        {
            managedMax = parsedMax;
        }

        double fraction = 0.5;
        if (double.TryParse(Environment.GetEnvironmentVariable("JGRAPH_BUFFER_NATIVE_FRACTION"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedFraction)
            && parsedFraction is > 0 and <= 1)
        {
            fraction = parsedFraction;
        }

        return new BufferAllocator(new GcMemoryInfo())
        {
            Mode = mode,
            ManagedMaxElements = managedMax,
            NativeHeadroomFraction = fraction,
        };
    }
}
