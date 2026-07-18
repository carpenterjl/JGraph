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
/// Environment overrides (read once, when <see cref="Shared"/> is first used):
/// <c>JGRAPH_BUFFER_MODE=managed|native|mapped</c> forces a backend;
/// <c>JGRAPH_BUFFER_MANAGED_MAX</c> (elements) and <c>JGRAPH_BUFFER_NATIVE_FRACTION</c> (0..1)
/// tune the automatic policy.
/// </remarks>
public sealed class BufferAllocator
{
    private readonly IMemoryInfo _memory;
    private long _outstandingNativeBytes;

    /// <summary>The process-wide allocator, configured from environment variables.</summary>
    public static BufferAllocator Shared { get; } = CreateFromEnvironment();

    /// <summary>Creates an allocator over an explicit memory source (tests inject a fake).</summary>
    public BufferAllocator(IMemoryInfo memory) => _memory = memory;

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

    /// <summary>The default mapped-file directory: <c>%TEMP%/JGraph/buffers</c>.</summary>
    public static string DefaultMappedDirectory =>
        Path.Combine(Path.GetTempPath(), "JGraph", "buffers");

    /// <summary>Native bytes currently allocated and not yet freed (policy input and diagnostics).</summary>
    public long OutstandingNativeBytes => Interlocked.Read(ref _outstandingNativeBytes);

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
            BufferMode.Mapped when count > 0 => new MappedBuffer(count, MappedDirectory),
            _ => AllocateAutomatic(count),
        };
    }

    private NumericBuffer AllocateAutomatic(int count)
    {
        if (count <= ManagedMaxElements)
        {
            return new ManagedBuffer(count);
        }

        long bytes = (long)count * sizeof(double);
        long headroom = _memory.TotalPhysicalBytes - _memory.MemoryLoadBytes
                        - MinFreeReserveBytes - OutstandingNativeBytes;
        return bytes <= NativeHeadroomFraction * headroom
            ? AllocateNative(count)
            : new MappedBuffer(count, MappedDirectory);
    }

    private NativeBuffer AllocateNative(int count)
    {
        long bytes = (long)count * sizeof(double);
        Interlocked.Add(ref _outstandingNativeBytes, bytes);
        try
        {
            return new NativeBuffer(count, onFreed: () => Interlocked.Add(ref _outstandingNativeBytes, -bytes));
        }
        catch
        {
            Interlocked.Add(ref _outstandingNativeBytes, -bytes);
            throw;
        }
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
