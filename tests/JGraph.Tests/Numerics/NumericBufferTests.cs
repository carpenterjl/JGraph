using System.Runtime.CompilerServices;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>Fake memory facts for exercising the allocation policy deterministically.</summary>
internal sealed class FakeMemoryInfo : IMemoryInfo
{
    public long TotalPhysicalBytes { get; set; }
    public long MemoryLoadBytes { get; set; }
}

public class NumericBufferTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "JGraphTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private static NumericBuffer Create(BufferKind kind, int length, string directory) => kind switch
    {
        BufferKind.Managed => new ManagedBuffer(length),
        BufferKind.Native => new NativeBuffer(length),
        _ => new MappedBuffer(length, directory),
    };

    [Theory]
    [InlineData(BufferKind.Managed)]
    [InlineData(BufferKind.Native)]
    [InlineData(BufferKind.Mapped)]
    public void EveryBackend_RoundTripsWritesAndStartsZeroed(BufferKind kind)
    {
        const int length = 100_000;
        using NumericBuffer buffer = Create(kind, length, _tempDirectory);

        Assert.Equal(kind, buffer.Kind);
        Assert.Equal(length, buffer.Length);

        var span = buffer.AsSpan();
        Assert.Equal(0.0, span[0]);
        Assert.Equal(0.0, span[length - 1]);

        for (int i = 0; i < length; i++)
        {
            span[i] = i * 0.5;
        }

        var reread = buffer.AsSpan();
        Assert.Equal(0.0, reread[0]);
        Assert.Equal(12_345 * 0.5, reread[12_345]);
        Assert.Equal((length - 1) * 0.5, reread[length - 1]);

        var window = buffer.AsSpan(1_000, 10);
        Assert.Equal(1_000 * 0.5, window[0]);
    }

    [Theory]
    [InlineData(BufferKind.Native)]
    [InlineData(BufferKind.Mapped)]
    public void UnmanagedBackends_ThrowAfterDispose_AndDisposeIsIdempotent(BufferKind kind)
    {
        NumericBuffer buffer = Create(kind, 16, _tempDirectory);
        buffer.Dispose();
        buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => buffer.AsSpan());
    }

    [Fact]
    public void ManagedBuffer_Adopt_WrapsTheArrayWithoutCopying()
    {
        double[] array = [1, 2, 3];
        var buffer = ManagedBuffer.Adopt(array);
        buffer.AsSpan()[1] = 42;
        Assert.Equal(42, array[1]);
    }

    [Fact]
    public void MappedBuffer_CreatesItsFileWhileLive_AndTheFileGoesAwayOnDispose()
    {
        var buffer = new MappedBuffer(1_000, _tempDirectory);
        string[] live = Directory.GetFiles(_tempDirectory, "*" + MappedBuffer.FileExtension);
        Assert.Single(live);

        buffer.Dispose();
        Assert.Empty(Directory.GetFiles(_tempDirectory, "*" + MappedBuffer.FileExtension));
    }

    [Fact]
    public void NativeBuffer_FinalizerFreesAbandonedMemory()
    {
        bool freed = false;
        AllocateAndAbandon(() => freed = true);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.True(freed);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateAndAbandon(Action onFreed) => _ = new NativeBuffer(1_000, onFreed);

    [Fact]
    public void Allocator_SmallRequests_StayManaged()
    {
        var allocator = new BufferAllocator(new FakeMemoryInfo
        {
            TotalPhysicalBytes = 16L << 30,
            MemoryLoadBytes = 8L << 30,
        });

        using var buffer = allocator.Allocate(1000);
        Assert.Equal(BufferKind.Managed, buffer.Kind);
    }

    [Fact]
    public void Allocator_LargeRequest_WithPlentifulRam_GoesNative()
    {
        var allocator = new BufferAllocator(new FakeMemoryInfo
        {
            TotalPhysicalBytes = 64L << 30,
            MemoryLoadBytes = 8L << 30,
        });

        using var buffer = allocator.Allocate(2_000_000); // 16 MB, far under half of ~55 GB headroom
        Assert.Equal(BufferKind.Native, buffer.Kind);
        Assert.Equal(16_000_000, allocator.OutstandingNativeBytes);
    }

    [Fact]
    public void Allocator_LargeRequest_WithScarceRam_DegradesToMapped()
    {
        var allocator = new BufferAllocator(new FakeMemoryInfo
        {
            // 16 GB machine with 15.5 GB in use: headroom after the 1 GB reserve is negative.
            TotalPhysicalBytes = 16L << 30,
            MemoryLoadBytes = (15L << 30) + (1L << 29),
        })
        { MappedDirectory = _tempDirectory };

        using var buffer = allocator.Allocate(2_000_000);
        Assert.Equal(BufferKind.Mapped, buffer.Kind);
    }

    [Fact]
    public void Allocator_OutstandingNativeBytes_CountAgainstHeadroom_AndReleaseOnDispose()
    {
        var memory = new FakeMemoryInfo
        {
            // Headroom = 4 GB − 2 GB load − 1 GB reserve = 1 GB; half of that is 512 MB.
            TotalPhysicalBytes = 4L << 30,
            MemoryLoadBytes = 2L << 30,
        };
        var allocator = new BufferAllocator(memory) { MappedDirectory = _tempDirectory };

        // 400 MB fits under the 512 MB budget and books outstanding bytes...
        var first = allocator.Allocate(50_000_000);
        Assert.Equal(BufferKind.Native, first.Kind);

        // ...which shrink the remaining budget to ~56 MB, so an identical request degrades.
        using var second = allocator.Allocate(50_000_000);
        Assert.Equal(BufferKind.Mapped, second.Kind);

        first.Dispose();
        Assert.Equal(0, allocator.OutstandingNativeBytes);

        using var third = allocator.Allocate(50_000_000);
        Assert.Equal(BufferKind.Native, third.Kind);
    }

    [Theory]
    [InlineData(BufferMode.Managed, BufferKind.Managed)]
    [InlineData(BufferMode.Native, BufferKind.Native)]
    [InlineData(BufferMode.Mapped, BufferKind.Mapped)]
    public void Allocator_ForcedModes_OverrideThePolicy(BufferMode mode, BufferKind expected)
    {
        var allocator = new BufferAllocator(new FakeMemoryInfo
        {
            TotalPhysicalBytes = 16L << 30,
            MemoryLoadBytes = 8L << 30,
        })
        { Mode = mode, MappedDirectory = _tempDirectory };

        using var buffer = allocator.Allocate(10_000);
        Assert.Equal(expected, buffer.Kind);
    }

    [Fact]
    public void Allocator_RejectsRequestsBeyondIntRange()
    {
        var allocator = new BufferAllocator(new FakeMemoryInfo
        {
            TotalPhysicalBytes = 16L << 30,
            MemoryLoadBytes = 8L << 30,
        });

        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Allocate((long)int.MaxValue + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Allocate(-1));
    }

    [Fact]
    public void SweepOrphans_DeletesStaleFiles_AndSkipsLiveOnes()
    {
        Directory.CreateDirectory(_tempDirectory);
        string stale = Path.Combine(_tempDirectory, "dead" + MappedBuffer.FileExtension);
        File.WriteAllBytes(stale, new byte[16]);

        using var liveBuffer = new MappedBuffer(100, _tempDirectory);

        BufferAllocator.SweepOrphans(_tempDirectory);

        Assert.False(File.Exists(stale));
        Assert.Single(Directory.GetFiles(_tempDirectory, "*" + MappedBuffer.FileExtension));
        Assert.Equal(100, liveBuffer.AsSpan().Length);
    }

    [Fact]
    public void SweepOrphans_MissingDirectory_IsANoOp() =>
        BufferAllocator.SweepOrphans(Path.Combine(_tempDirectory, "does-not-exist"));
}
