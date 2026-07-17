using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

public class ScriptWorkspaceTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"jgraph_ws_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Open_MissingFolder_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => ScriptWorkspace.Open(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}")));
    }

    [Fact]
    public void Open_Subfolder_RerootsResolution()
    {
        // M17: browsing into a folder re-opens the workspace there; bare names then resolve
        // against the new root.
        string root = CreateTempDir();
        try
        {
            string sub = Path.Combine(root, "measurements");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "data.csv"), "x\n1");

            using var rerooted = ScriptWorkspace.Open(sub);

            Assert.Equal(Path.GetFullPath(sub), rerooted.RootPath);
            Assert.Equal(Path.Combine(sub, "data.csv"), rerooted.Resolve("data.csv", null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_PrefersScriptDirectory_ThenRoot_ThenOriginal()
    {
        string root = CreateTempDir();
        try
        {
            string sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(root, "data.csv"), "x\n1");
            File.WriteAllText(Path.Combine(sub, "data.csv"), "x\n2");
            File.WriteAllText(Path.Combine(root, "only-root.csv"), "x\n3");

            using ScriptWorkspace workspace = ScriptWorkspace.Open(root);

            // The running script's own folder wins over the root.
            Assert.Equal(Path.Combine(sub, "data.csv"), workspace.Resolve("data.csv", sub));

            // Falls back to the workspace root when the script's folder has no such file.
            Assert.Equal(Path.Combine(root, "only-root.csv"), workspace.Resolve("only-root.csv", sub));

            // No script directory: the root is probed directly.
            Assert.Equal(Path.Combine(root, "data.csv"), workspace.Resolve("data.csv", null));

            // An absolute path passes through untouched.
            string absolute = Path.Combine(root, "data.csv");
            Assert.Equal(absolute, workspace.Resolve(absolute, sub));

            // Not found anywhere: the original comes back so the file open fails naturally.
            Assert.Equal("nowhere.csv", workspace.Resolve("nowhere.csv", sub));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateScripts_FindsScriptFilesRecursively_AndSkipsDataFiles()
    {
        string root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "lib"));
            File.WriteAllText(Path.Combine(root, "main.jgs"), "# main");
            File.WriteAllText(Path.Combine(root, "lib", "helpers.py"), "# helpers");
            File.WriteAllText(Path.Combine(root, "data.csv"), "x\n1");

            using ScriptWorkspace workspace = ScriptWorkspace.Open(root);
            IReadOnlyList<WorkspaceEntry> scripts = workspace.EnumerateScripts();

            Assert.Equal(2, scripts.Count);
            Assert.Contains(scripts, s => s.RelativePath == Path.Combine("lib", "helpers.py"));
            Assert.Contains(scripts, s => s.RelativePath == "main.jgs");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateAll_BuildsTree_DirectoriesFirst()
    {
        string root = CreateTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "zdir"));
            File.WriteAllText(Path.Combine(root, "a.jgs"), "# a");
            File.WriteAllText(Path.Combine(root, "zdir", "nested.csv"), "x\n1");

            using ScriptWorkspace workspace = ScriptWorkspace.Open(root);
            IReadOnlyList<WorkspaceEntry> entries = workspace.EnumerateAll();

            Assert.Equal(2, entries.Count);
            Assert.True(entries[0].IsDirectory);              // directories sort before files
            Assert.Equal("zdir", entries[0].Name);
            Assert.Equal("nested.csv", Assert.Single(entries[0].Children).Name);
            Assert.False(entries[1].IsDirectory);
            Assert.Equal("a.jgs", entries[1].Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Changed_FiresOnExternalFileCreation()
    {
        string root = CreateTempDir();
        try
        {
            using ScriptWorkspace workspace = ScriptWorkspace.Open(root);
            using var fired = new ManualResetEventSlim(false);
            workspace.Changed += (_, _) => fired.Set();

            File.WriteAllText(Path.Combine(root, "new.jgs"), "# new");

            Assert.True(fired.Wait(TimeSpan.FromSeconds(5)), "Changed did not fire within the timeout.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
