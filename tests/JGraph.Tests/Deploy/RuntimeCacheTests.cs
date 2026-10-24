using System.Globalization;
using JGraph.Deploy;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// The runtime cache (compiler and SDK plan, stage K3) against what <c>record-k3-cache.ps1</c>
/// measured R2025b's MATLAB Runtime doing with its own (<c>recordings/k3_cache.out.txt</c>), and the
/// divergences ADR 0258 records where R2025b was wrong.
/// </summary>
public sealed class RuntimeCacheTests : IDisposable
{
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k3c-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    private string Cache => Path.Combine(_root, "cache", "R2025b");

    /// <summary>An archive of one main and one data file of <paramref name="size"/> bytes.</summary>
    private string Archive(string name, int size = 100)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, "src", name)).FullName;
        string main = Path.Combine(folder, name + ".m");
        File.WriteAllText(main, $"function {name}\ndisp(1)\nend\n");
        string data = Path.Combine(folder, "data.bin");
        File.WriteAllBytes(data, new byte[size]);
        var report = new DependencyReport(
            [new DependencyFile(main, DependencyReason.Root, null), new DependencyFile(data, DependencyReason.Data, main)], [], [], []);
        string archive = Path.Combine(_root, name + ".ctf");
        CtfArchiveWriter.Write(archive, CtfLayout.FromReport(name, main, report));
        return archive;
    }

    private CacheEntry Open(string archive, string? size = null)
    {
        using CtfArchiveReader reader = CtfArchiveReader.Open(archive);
        return new RuntimeCache(Cache).Open(reader, size);
    }

    private string[] Index() => File.ReadAllText(Path.Combine(Cache, RuntimeCache.IndexFile)).Split("\r\n");

    [Fact]
    public void FirstStart_ExtractsIntoTheUuidsFirstSixCharactersAndZero_AndWritesR2025bsFiles()
    {
        string archive = Archive("k0_env");
        using CacheEntry entry = Open(archive);
        Assert.True(entry.Extracted);
        Assert.Equal(Path.Combine(Cache, "k0_env0"), entry.Folder);
        Assert.True(File.Exists(Path.Combine(Cache, RuntimeCache.LockFile)));
        Assert.Equal("1073741824", File.ReadAllText(Path.Combine(Cache, RuntimeCache.MaxSizeFile)));
        Assert.True(File.Exists(Path.Combine(entry.Folder, RuntimeCache.CreatedFile)));
        Assert.True(File.Exists(Path.Combine(entry.Folder, RuntimeCache.LockFile)));
        Assert.True(File.Exists(Path.Combine(entry.Folder, "k0_env", "k0_env.m")));
        Assert.True(Directory.Exists(Path.Combine(entry.Folder, ".matlab")));

        // Four CR LF lines per entry: the uuid, the folder, the size, the last use in R2025b's form.
        string[] index = Index();
        Assert.Equal(5, index.Length);
        Assert.StartsWith("k0_env_", index[0]);
        Assert.Equal("k0_env0", index[1]);
        Assert.True(long.Parse(index[2], CultureInfo.InvariantCulture) > 100);
        Assert.Matches(@"^\d{4}-[A-Z][a-z]{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{6}$", index[3]);
        Assert.Equal("", index[4]);
    }

    [Fact]
    public void SecondStart_ReusesTheEntry_AndAnotherBuildOfTheNameGetsTheNextNumber()
    {
        string archive = Archive("hello");
        using (CacheEntry first = Open(archive))
        {
            Assert.True(first.Extracted);
        }

        using (CacheEntry second = Open(archive))
        {
            Assert.False(second.Extracted);
            Assert.Equal(Path.Combine(Cache, "hello_0"), second.Folder);
        }

        // A rebuild has a new uuid, so a new entry: hello_1, as R2025b numbered hello_0, hello_1, hello_2.
        string rebuilt = Archive("hello");
        using CacheEntry third = Open(rebuilt);
        Assert.True(third.Extracted);
        Assert.Equal(Path.Combine(Cache, "hello_1"), third.Folder);
        Assert.Equal(9, Index().Length);
    }

    [Fact]
    public void AnEntryWithoutItsMarker_OrWithoutItsFolder_IsExtractedAgainIntoTheSameFolder()
    {
        string archive = Archive("k0_env");
        string folder;
        using (CacheEntry first = Open(archive))
        {
            folder = first.Folder;
        }

        File.Delete(Path.Combine(folder, RuntimeCache.CreatedFile));
        File.Delete(Path.Combine(folder, "k0_env", "k0_env.m"));
        using (CacheEntry again = Open(archive))
        {
            Assert.True(again.Extracted);
            Assert.Equal(folder, again.Folder);
            Assert.True(File.Exists(Path.Combine(folder, "k0_env", "k0_env.m")));
        }

        Directory.Delete(folder, recursive: true);
        using CacheEntry restored = Open(archive);
        Assert.True(restored.Extracted);
        Assert.Equal(folder, restored.Folder);
    }

    [Fact]
    public void ADeletedOrChangedFile_IsExtractedAgain_WhereR2025bRunsWithoutItOrFails()
    {
        string archive = Archive("k0_env");
        string folder;
        using (CacheEntry first = Open(archive))
        {
            folder = first.Folder;
        }

        string main = Path.Combine(folder, "k0_env", "k0_env.m");
        string original = File.ReadAllText(main);
        File.Delete(main);
        using (CacheEntry again = Open(archive))
        {
            Assert.True(again.Extracted);
            Assert.Equal(original, File.ReadAllText(main));
        }

        File.AppendAllText(main, "tampered");
        using (CacheEntry again = Open(archive))
        {
            Assert.True(again.Extracted);
            Assert.Equal(original, File.ReadAllText(main));
        }
    }

    [Fact]
    public void WithoutAnIndex_TheArchiveGetsANewFolder_AndTheOldOneIsListedForEviction()
    {
        string archive = Archive("k0_env");
        using (Open(archive))
        {
        }

        File.Delete(Path.Combine(Cache, RuntimeCache.IndexFile));
        using CacheEntry entry = Open(archive);
        Assert.Equal(Path.Combine(Cache, "k0_env1"), entry.Folder);
        string[] index = Index();
        Assert.Equal("", index[0]);
        Assert.Equal("k0_env0", index[1]);
        Assert.Equal("k0_env1", index[5]);
    }

    [Fact]
    public void OverTheLimit_TheLeastRecentlyUsedOthersAreEvicted_NeverTheOneStarting()
    {
        string first = Archive("k0_env", 300_000);
        string second = Archive("k0_path", 200_000);
        using (Open(first, "400000"))
        {
        }

        using (CacheEntry path = Open(second, "400000"))
        {
            Assert.False(Directory.Exists(Path.Combine(Cache, "k0_env0")));
            Assert.True(Directory.Exists(path.Folder));
        }

        // The evicted archive comes back into its old folder name, and the other one goes.
        using CacheEntry env = Open(first, "400000");
        Assert.True(Directory.Exists(env.Folder));
        Assert.False(Directory.Exists(Path.Combine(Cache, "k0_pat0")));
    }

    [Fact]
    public void AnEntryARunningProgramHolds_IsNotEvicted()
    {
        string first = Archive("k0_env", 300_000);
        string second = Archive("k0_path", 200_000);
        using CacheEntry running = Open(first, "400000");
        using CacheEntry other = Open(second, "400000");
        Assert.True(Directory.Exists(running.Folder));
        Assert.True(Directory.Exists(other.Folder));
    }

    [Fact]
    public void TheLimitsUnitCounts_WhereR2025bDropsIt()
    {
        string first = Archive("k0_env", 300_000);
        string second = Archive("k0_path", 200_000);
        using (Open(first, "1000K"))
        {
        }

        // R2025b wrote 1000 and evicted; 1000K is 1,024,000 bytes and holds both.
        Assert.Equal("1024000", File.ReadAllText(Path.Combine(Cache, RuntimeCache.MaxSizeFile)));
        using (Open(second, "1000K"))
        {
            Assert.True(Directory.Exists(Path.Combine(Cache, "k0_env0")));
        }

        // A later start without the variable keeps the limit; one with it replaces it.
        using (Open(second))
        {
            Assert.Equal("1024000", File.ReadAllText(Path.Combine(Cache, RuntimeCache.MaxSizeFile)));
        }

        using (Open(second, "2M"))
        {
            Assert.Equal("2097152", File.ReadAllText(Path.Combine(Cache, RuntimeCache.MaxSizeFile)));
        }
    }

    [Theory]
    [InlineData("150M", 150L * 1024 * 1024)]
    [InlineData("150m", 150L * 1024 * 1024)]
    [InlineData("2G", 2L * 1024 * 1024 * 1024)]
    [InlineData("64K", 64L * 1024)]
    [InlineData("500000", 500000L)]
    [InlineData("0", 0L)]
    [InlineData("10MB", 10L * 1024 * 1024)]
    [InlineData(" 20M", 20L * 1024 * 1024)]
    [InlineData("1.5M", 1572864L)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData("12Q", null)]
    [InlineData(null, null)]
    public void ParseSize_ReadsTheFormsR2025bWasGiven(string? text, long? expected) =>
        Assert.Equal(expected, RuntimeCache.ParseSize(text));
}
