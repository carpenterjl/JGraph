using JGraph.Api;
using JGraph.Deploy.Apps;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Deploy;

/// <summary>
/// <c>visdiff</c> in a MATLAB session (App Designer window plan W14): the comparison objects answered for two texts and
/// two apps, <c>publish</c> writing the report, <c>filter</c>'s refusal, and R2025b's refusals (probe <c>w14_compare</c>) -
/// a missing file, one argument, numbers, a type the files do not take, a bad format or folder for <c>publish</c>, and a
/// comparison without an output in a session with no window.
/// </summary>
[Collection("JG facade")]
public sealed class VisdiffBuiltinTests : IAsyncLifetime
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Deploy", "fixtures");
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jgraph-visdiff-" + Guid.NewGuid().ToString("N"));
    private IScriptSession _session = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        ToolboxPackage.Extract(Path.Combine(Fixtures, "AcceptForm-r2025b.mltbx"), Path.Combine(_folder, "apps"));
        File.Copy(Path.Combine(_folder, "apps", "AcceptForm.mlapp"), Path.Combine(_folder, "Copy.mlapp"));
        File.WriteAllText(Path.Combine(_folder, "prog.m"), "x = 1;\ny = 2;\n");
        File.WriteAllText(Path.Combine(_folder, "prog2.m"), "x = 1;\ny = 3;\n");
        _session = ((IScriptRepl)new MatlabScriptEngine()).CreateSession(new ScriptContext(_output, (_, _) => { }, _folder, null));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        JG.Reset();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Q(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>The identifier and message a statement fails with, or what it printed.</summary>
    private async Task<string> Run(string statement)
    {
        _output.Mark();
        ScriptRunResult result = await _session.ExecuteAsync(
            $"try, {statement}; catch e, disp([e.identifier ' | ' strrep(e.message, newline, ' / ')]), end",
            string.Empty, CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.TextSinceMark.Trim();
    }

    [Fact]
    public async Task TwoTextFilesAnswerATextComparisonWhosePublishWritesTheReport()
    {
        string a = Path.Combine(_folder, "prog.m");
        string b = Path.Combine(_folder, "prog2.m");
        Assert.Equal("comparisons.TextComparison", await Run($"c = visdiff('{Q(a)}', '{Q(b)}'); disp(class(c))"));
        Assert.Equal(a, await Run("disp(c.Left)"));
        Assert.Equal(b, await Run("disp(c.Right)"));
        Assert.Equal("comparisons.TextComparison", await Run("c2 = visdiff('prog.m', 'prog2.m', 'text'); disp(class(c2))"));
        string report = await Run($"r = publish(c, 'Format', 'html', 'OutputFolder', '{Q(_folder)}', 'Name', 'rep'); disp(r)");
        Assert.Equal(Path.Combine(_folder, "rep.html"), report);
        string html = File.ReadAllText(report);
        Assert.Contains("File Comparison Report", html, StringComparison.Ordinal);
        Assert.Contains("vertical-align:middle;\">≠<", html, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(_folder, "prog_prog2.html"), await Run("disp(publish(c))"));
        Assert.Equal("comparisons:FilterNotSupported | Filtering is not supported for this comparison type.", await Run("filter(c, 'unfiltered')"));
    }

    [Fact]
    public async Task TwoAppsAnswerAnMlappComparison()
    {
        string a = Path.Combine(_folder, "apps", "AcceptForm.mlapp");
        string b = Path.Combine(_folder, "Copy.mlapp");
        Assert.Equal("comparisons.MlappComparison", await Run($"c = visdiff('{Q(a)}', '{Q(b)}'); disp(class(c))"));
        string report = await Run($"disp(publish(c, 'OutputFolder', '{Q(_folder)}'))");
        Assert.Equal(Path.Combine(_folder, "AcceptForm_Copy.html"), report);
        Assert.Contains("No changes detected", File.ReadAllText(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRefusalsAreR2025bs()
    {
        string a = Path.Combine(_folder, "prog.m");
        string b = Path.Combine(_folder, "prog2.m");
        Assert.Equal($"comparisons:mldesktop:FileNotFound | Unable to load file '{Path.Combine(_folder, "nowhere.m")}'.", await Run($"c = visdiff('nowhere.m', '{Q(b)}')"));
        Assert.Equal("MATLAB:minrhs | Invalid argument list. Function requires 1 more input(s).", await Run($"c = visdiff('{Q(a)}')"));
        Assert.Equal("comparisons:mldesktop:UnknownComparisonTypeForNoDisplay | Comparison type 'binary' not available for the specified files without opening the Comparison Tool.",
            await Run($"c = visdiff('{Q(a)}', '{Q(b)}', 'binary')"));
        Assert.StartsWith("MATLAB:invalidType | The value of 'filename1' is invalid. Expected input to be one of these types: /  / char, string /  / Instead its type was double.",
            await Run("c = visdiff(1, 2)"), StringComparison.Ordinal);
        Assert.Equal($"comparisons:mldesktop:CannotCompareWithoutDisplay | Unable to compare '{_folder}' and '{Path.Combine(_folder, "apps")}' without opening the Comparison Tool.",
            await Run($"c = visdiff('{Q(_folder)}', '{Q(Path.Combine(_folder, "apps"))}')"));
        // Without an output and without a window.
        Assert.Equal($"comparisons:mldesktop:UnableToCompareWithNoDisplay | Unable to compare '{a}' and '{b}' without a display. Try returning a comparison object and run the comparison again.",
            await Run($"visdiff('{Q(a)}', '{Q(b)}')"));
        Assert.Equal("MATLAB:unrecognizedStringChoice | Expected input to match one of these values: /  / 'html', 'docx', 'pdf' /  / The input, 'nosuch', did not match any of the valid values.",
            await Run($"c = visdiff('{Q(a)}', '{Q(b)}'); publish(c, 'Format', 'nosuch')"));
        Assert.Equal($"MATLAB:validators:mustBeFolder | Invalid argument at position 2. These folders do not exist: '{Path.Combine(_folder, "nowhere")}'.",
            await Run($"publish(c, 'OutputFolder', '{Q(Path.Combine(_folder, "nowhere"))}')"));
        Assert.Equal("JGraph:visdiff:FormatNotSupported | JGraph publishes a comparison to HTML; the 'docx' format is not supported.", await Run("publish(c, 'Format', 'docx')"));
    }
}
