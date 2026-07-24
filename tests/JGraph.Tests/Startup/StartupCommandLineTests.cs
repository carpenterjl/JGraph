using JGraph.Scripting.Startup;
using Xunit;

namespace JGraph.Tests.Startup;

/// <summary>
/// Covers the startup-option parser. Both executables parse the same list, and the launcher forwards
/// arguments verbatim to the application, so a disagreement here would be invisible until runtime.
/// </summary>
public class StartupCommandLineTests
{
    [Fact]
    public void NoArguments_IsInteractive()
    {
        StartupOptions options = StartupCommandLine.Parse(Array.Empty<string>());

        Assert.Equal(StartupMode.Interactive, options.Mode);
        Assert.False(options.HasUsageError);
    }

    [Fact]
    public void Batch_TakesTheFollowingStatement()
    {
        StartupOptions options = StartupCommandLine.Parse(new[] { "-batch", "disp(1)" });

        Assert.Equal(StartupMode.Batch, options.Mode);
        Assert.Equal("disp(1)", options.Statement);
        Assert.False(options.ShowFigures);
    }

    [Theory]
    [InlineData("-r")]
    [InlineData("--r")]
    [InlineData("-R")]
    public void Run_AcceptsBothDashFormsAndAnyCase(string flag)
    {
        StartupOptions options = StartupCommandLine.Parse(new[] { flag, "plot(1:10)" });

        Assert.Equal(StartupMode.Run, options.Mode);
        Assert.Equal("plot(1:10)", options.Statement);
    }

    [Fact]
    public void LogFileAndStartDirectory_AreCarriedAlongsideTheMode()
    {
        StartupOptions options = StartupCommandLine.Parse(
            new[] { "-batch", "disp(1)", "-logfile", "run.log", "-sd", @"C:\work" });

        Assert.Equal(StartupMode.Batch, options.Mode);
        Assert.Equal("run.log", options.LogFile);
        Assert.Equal(@"C:\work", options.StartDirectory);
    }

    [Fact]
    public void LogFile_WithoutAMode_StillOpensTheInteractiveApplication()
    {
        StartupOptions options = StartupCommandLine.Parse(new[] { "-logfile", "session.log" });

        Assert.Equal(StartupMode.Interactive, options.Mode);
        Assert.Equal("session.log", options.LogFile);
        Assert.False(options.HasUsageError);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("-help")]
    [InlineData("--HELP")]
    [InlineData("-?")]
    public void Help_IsRecognisedInEveryForm(string flag)
    {
        Assert.Equal(StartupMode.Help, StartupCommandLine.Parse(new[] { flag }).Mode);
    }

    [Fact]
    public void Help_WinsOverAnythingElseOnTheLine()
    {
        // Someone asking what the options are should be told, not have a half-understood line run.
        StartupOptions options = StartupCommandLine.Parse(new[] { "-batch", "disp(1)", "-help" });

        Assert.Equal(StartupMode.Help, options.Mode);
    }

    [Fact]
    public void ShowFigures_AppliesToBatch()
    {
        StartupOptions options = StartupCommandLine.Parse(new[] { "-batch", "plot(1:10)", "-showfigures" });

        Assert.Equal(StartupMode.Batch, options.Mode);
        Assert.True(options.ShowFigures);
    }

    [Fact]
    public void ShowFigures_WithoutBatch_IsRejected()
    {
        StartupOptions options = StartupCommandLine.Parse(new[] { "-r", "plot(1:10)", "-showfigures" });

        Assert.True(options.HasUsageError);
        Assert.Contains("-showfigures", options.UsageError);
    }

    [Fact]
    public void BatchAndRun_CannotBeCombined()
    {
        StartupOptions options = StartupCommandLine.Parse(new[] { "-batch", "x=1", "-r", "y=2" });

        Assert.True(options.HasUsageError);
        Assert.Contains("-batch and -r", options.UsageError);
    }

    [Fact]
    public void RepeatedFlag_IsRejected()
    {
        Assert.True(StartupCommandLine.Parse(new[] { "-batch", "x=1", "-batch", "y=2" }).HasUsageError);
        Assert.True(StartupCommandLine.Parse(new[] { "-logfile", "a", "-logfile", "b" }).HasUsageError);
    }

    [Theory]
    [InlineData("-batch")]
    [InlineData("-r")]
    [InlineData("-logfile")]
    [InlineData("-sd")]
    public void FlagWithoutItsValue_IsRejected(string flag)
    {
        StartupOptions options = StartupCommandLine.Parse(new[] { flag });

        Assert.True(options.HasUsageError);
        Assert.Contains(flag, options.UsageError);
    }

    [Fact]
    public void UnknownFlag_IsRejectedRatherThanIgnored()
    {
        StartupOptions options = StartupCommandLine.Parse(new[] { "-nodesktop" });

        Assert.True(options.HasUsageError);
        Assert.Contains("-nodesktop", options.UsageError);
    }

    [Fact]
    public void BarePositionalArgument_IsRejected()
    {
        StartupOptions options = StartupCommandLine.Parse(new[] { "analysis.jgs" });

        Assert.True(options.HasUsageError);
        Assert.Contains("analysis.jgs", options.UsageError);
    }

    [Fact]
    public void StatementBeginningWithADash_IsStillTakenAsTheValue()
    {
        StartupOptions options = StartupCommandLine.Parse(new[] { "-batch", "-x" });

        Assert.Equal(StartupMode.Batch, options.Mode);
        Assert.Equal("-x", options.Statement);
    }
}
