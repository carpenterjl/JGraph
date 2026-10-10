using System.Text.RegularExpressions;
using Xunit;

namespace JGraph.Tests.Startup;

/// <summary>
/// Every test class that resets or takes over the process's graphics state runs in the
/// <c>JG facade</c> collection, so no two of them run at once.
/// </summary>
/// <remarks>
/// A top-level <c>JgsRunner.Run</c> calls <c>JG.Reset</c> and clears the handle registry, and a live
/// JGS session installs the process's callback dispatcher. Six device test classes ran scripts outside
/// the collection, and one built a session there, so a BatchRunner test lost its figure about one full
/// run in two (open item 85). A class that names <c>JG</c> was always put in the collection by hand;
/// the ones that reach it through the runner were not, which is what this checks.
/// </remarks>
public class FacadeCollectionGuardTests
{
    private static readonly Regex TakesTheFacade = new(
        @"\bJgsRunner\.Run\(|\bJG\.Reset\(|\bJgsHandleRegistry\.Clear\(|\bBatchRunner\.Run\w*\(|ScriptEngine\(\)\.CreateSession\(");

    private static readonly Regex InTheCollection = new(@"\[Collection\(""JG facade""\)\]");

    [Fact]
    public void EveryClassThatResetsTheGraphicsStateRunsInTheFacadeCollection()
    {
        string root = Path.Combine(Repository(), "tests", "JGraph.Tests");
        var outside = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(root, path))
            .Where(path =>
            {
                string text = File.ReadAllText(path);
                return TakesTheFacade.IsMatch(text) && !InTheCollection.IsMatch(text);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.True(outside.Count == 0, "outside the JG facade collection: " + string.Join(", ", outside));
    }

    private static bool IsBuildOutput(string root, string path)
    {
        string first = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)[0];
        return first is "bin" or "obj";
    }

    private static string Repository()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "JGraph.sln")))
        {
            at = at.Parent;
        }

        return at?.FullName
            ?? throw new InvalidOperationException("the repository root is not above the test binaries");
    }
}
