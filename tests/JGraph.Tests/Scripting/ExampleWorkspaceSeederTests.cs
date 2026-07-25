using System.IO;
using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

public class ExampleWorkspaceSeederTests
{
    private static readonly string SourceRoot = Path.Combine("C:", "app", "examples");
    private static readonly string TargetRoot = Path.Combine("C:", "users", "me", "JGraph", "Examples");

    private static string Source(params string[] parts) => Path.Combine(SourceRoot, Path.Combine(parts));

    private static string Target(params string[] parts) => Path.Combine(TargetRoot, Path.Combine(parts));

    [Fact]
    public void Plan_PairsEverySourceWithItsTarget()
    {
        IReadOnlyList<ExampleWorkspaceSeeder.SeedFile> plan = ExampleWorkspaceSeeder.Plan(
            [Source("example.jgs"), Source("sample.csv")], SourceRoot, TargetRoot, _ => false);

        Assert.Equal(2, plan.Count);
        Assert.Equal(new ExampleWorkspaceSeeder.SeedFile(Source("example.jgs"), Target("example.jgs")), plan[0]);
        Assert.Equal(new ExampleWorkspaceSeeder.SeedFile(Source("sample.csv"), Target("sample.csv")), plan[1]);
    }

    [Fact]
    public void Plan_PreservesTheFolderStructureBelowTheSourceRoot()
    {
        IReadOnlyList<ExampleWorkspaceSeeder.SeedFile> plan = ExampleWorkspaceSeeder.Plan(
            [Source("data", "sample.s2p")], SourceRoot, TargetRoot, _ => false);

        Assert.Equal(Target("data", "sample.s2p"), Assert.Single(plan).Target);
    }

    [Fact]
    public void Plan_SkipsFilesTheUserAlreadyHas()
    {
        // Re-seeding must never overwrite a copy the user has edited.
        IReadOnlyList<ExampleWorkspaceSeeder.SeedFile> plan = ExampleWorkspaceSeeder.Plan(
            [Source("example.jgs"), Source("sample.csv")],
            SourceRoot,
            TargetRoot,
            path => path.EndsWith("example.jgs", StringComparison.Ordinal));

        Assert.Equal(Target("sample.csv"), Assert.Single(plan).Target);
    }

    [Fact]
    public void Plan_IsIdempotent()
    {
        var copied = new HashSet<string>(StringComparer.Ordinal);
        string[] sources = [Source("example.jgs"), Source("data", "sample.s2p")];

        IReadOnlyList<ExampleWorkspaceSeeder.SeedFile> first =
            ExampleWorkspaceSeeder.Plan(sources, SourceRoot, TargetRoot, copied.Contains);
        foreach (ExampleWorkspaceSeeder.SeedFile file in first)
        {
            copied.Add(file.Target);
        }

        Assert.Equal(2, first.Count);
        Assert.Empty(ExampleWorkspaceSeeder.Plan(sources, SourceRoot, TargetRoot, copied.Contains));
    }

    [Fact]
    public void Plan_IgnoresFilesOutsideTheSourceRoot()
    {
        // A path that escapes the root has no meaningful place under the target; copying it by its
        // bare name would collide unpredictably, so it is left alone.
        IReadOnlyList<ExampleWorkspaceSeeder.SeedFile> plan = ExampleWorkspaceSeeder.Plan(
            [Path.Combine("C:", "app", "secrets.txt")], SourceRoot, TargetRoot, _ => false);

        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_AcceptsAnEmptySource() =>
        Assert.Empty(ExampleWorkspaceSeeder.Plan([], SourceRoot, TargetRoot, _ => false));
}
