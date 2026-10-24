using JGraph.Deploy;
using JGraph.Runtime;
using Xunit;

namespace JGraph.Tests.Runtime;

/// <summary>
/// The runtime options a program is built with or a host starts the runtime with (stage K4), read
/// as R2025b's runtime read them (<c>recordings/k4_ropts.out.txt</c>), and how the JGraph Runtime's
/// folder is found.
/// </summary>
public sealed class RuntimeOptionsTests : IDisposable
{
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jgraph-k4o-" + Guid.NewGuid().ToString("N"))).FullName;

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

    [Fact]
    public void Parse_ReadsEachOption_AndSeveralInOneArgument()
    {
        RuntimeOptions options = RuntimeOptions.Parse(
            ["-startmsg,K4 start,-completemsg,K4 complete", "-logfile,k4r.log", "-singleCompThread", "-nojvm", "-nodisplay"]);
        Assert.Equal("K4 start", options.StartMessage);
        Assert.Equal("K4 complete", options.CompleteMessage);
        Assert.Equal("k4r.log", options.LogFile);
        Assert.True(options.SingleCompThread && options.NoJvm && options.NoDisplay);
        Assert.Empty(options.Unrecognized);
        Assert.Equal(RuntimeOptions.None, RuntimeOptions.Parse([]));

        // A message keeps its own commas, up to the next option.
        Assert.Equal("one, two", RuntimeOptions.Parse(["-startmsg,one, two"]).StartMessage);
    }

    [Theory]
    [InlineData("-startmsg,K4 start", new[] { "Initializing JGraph Runtime version 25.2", "K4 start" })]
    [InlineData("-startmsg", new[] { "Initializing JGraph Runtime version 25.2" })]
    [InlineData("-completemsg,K4 complete", new[] { "K4 complete" })]
    [InlineData("-startmsg,K4 start,-completemsg,K4 complete", new[] { "Initializing JGraph Runtime version 25.2", "K4 start", "K4 complete" })]
    [InlineData("-bogus", new[] { "Warning: Unrecognized command line option: bogus." })]
    [InlineData("-nojvm", new string[0])]
    [InlineData("-singleCompThread", new string[0])]
    [InlineData("-logfile,k4r.log", new string[0])]
    public void StartLines_AreR2025bs_WithJGraphsRuntimeNamed(string option, string[] lines) =>
        Assert.Equal(lines, RuntimeOptions.Parse([option]).StartLines());

    private string Runtime(string name, string version)
    {
        string root = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        File.WriteAllText(Path.Combine(root, RuntimeLayout.MarkerFile), RuntimeLayout.MarkerText(version));
        return root;
    }

    [Fact]
    public void Find_TakesTheEnvironment_ThenTheApplicationsFolder_AndOnlyTheVersionAsked()
    {
        string version = RuntimeLayout.CurrentVersion;
        string named = Runtime("named", version);
        string carried = Runtime("carried", version);
        string older = Runtime("older", "0.0.1");
        string? before = Environment.GetEnvironmentVariable(RuntimeLayout.RootVariable);
        try
        {
            Environment.SetEnvironmentVariable(RuntimeLayout.RootVariable, named);
            RuntimeLayout? found = RuntimeLayout.Find(applicationFolder: carried);
            Assert.NotNull(found);
            Assert.Equal((named, RuntimeLocationSource.Environment, version, "25.2"), (found.Root, found.Source, found.Version, found.Release));
            Assert.Equal(Path.Combine(named, "bin", "win64"), found.BinFolder);

            // A runtime of another version is passed over.
            Environment.SetEnvironmentVariable(RuntimeLayout.RootVariable, older);
            found = RuntimeLayout.Find(applicationFolder: carried);
            Assert.Equal((carried, RuntimeLocationSource.ApplicationFolder), (found?.Root, found?.Source));

            Assert.Equal(older, RuntimeLayout.Find("0.0.1", applicationFolder: carried)?.Root);
            Assert.Null(RuntimeLayout.Find("0.0.2", applicationFolder: _root));
            Assert.Null(RuntimeLayout.Read(Path.Combine(_root, "nothing"), RuntimeLocationSource.ApplicationFolder));
        }
        finally
        {
            Environment.SetEnvironmentVariable(RuntimeLayout.RootVariable, before);
        }
    }
}
