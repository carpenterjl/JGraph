using System.Text;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 350 (ADR 0385), what the parity fixtures cannot say: a host with no console width wraps no report,
/// the wrap rule on its own, and the code page encodings' own round trips.
/// </summary>
[Collection("JG facade")]
public class ReportWrapStaticWorkspaceCodePageTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "jgraph-oi350-" + Guid.NewGuid().ToString("N"));

    public ReportWrapStaticWorkspaceCodePageTests()
    {
        JG.Reset();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        JG.Reset();
        Directory.Delete(_folder, recursive: true);
    }

    private async Task<string> RunMatlab(string source, int? width)
    {
        string path = Path.Combine(_folder, "oi350run.m");
        File.WriteAllText(path, source);
        ScriptContext context = new(_output, static (_, _) => { }, _folder) { ScriptPath = path, ConsoleWidth = width };
        ScriptRunResult result = await new MatlabScriptEngine().RunAsync(source, context, default);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private const string LongReport =
        "try\n    error('t:w', '%s', strtrim(repmat('word ', 1, 12)));\ncatch e\n    fprintf('%s', getReport(e, 'basic'));\nend\n";

    /// <summary>A host that reports no width wraps nothing.</summary>
    [Fact]
    public async Task NoWidth_LeavesTheMessageWhole()
    {
        string text = await RunMatlab(LongReport, width: null);
        Assert.Equal("Error using oi350run (line 2)\n" + string.Join(' ', Enumerable.Repeat("word", 12)), text);
    }

    /// <summary>A 30-wide console holds 29 characters a line.</summary>
    [Fact]
    public async Task Width_WrapsAtTheLastBlankThatFits()
    {
        string text = await RunMatlab(LongReport, width: 30);
        Assert.Equal(
            "Error using oi350run (line 2)\nword word word word word word\nword word word word word word",
            text);
    }

    [Theory]
    [InlineData("aaaa bbb tail", 10, "aaaa bbb\ntail")]
    [InlineData("aaaaaaaaaaaa bb", 10, "aaaaaaaaaaaa\nbb")]
    [InlineData("  ab cd ef gh", 8, "  ab cd\n  ef gh")]
    [InlineData("aaaa  bbbbbb", 7, "aaaa\nbbbbbb")]
    [InlineData("short", 10, "short")]
    public void WrapToConsole_FollowsR2025bsRule(string text, int width, string expected) =>
        Assert.Equal(expected, JgsErrorReport.WrapToConsole(text, width));

    [Theory]
    [InlineData("Shift_JIS", "Shift_JIS")]
    [InlineData("sjis", "Shift_JIS")]
    [InlineData("ISO-8859-11", "iso-8859_11-2001")]
    [InlineData("windows-949", "KSC_5601")]
    [InlineData("KOI8-R", "KOI8-R")]
    public void Named_AnswersR2025bsName(string asked, string named) =>
        Assert.Equal(named, JgsMatlabEncoding.Named(asked)?.MatlabName);

    [Fact]
    public void Named_RefusesAnUnknownPage() => Assert.Null(JgsMatlabEncoding.Named("ISO-8859-16"));

    /// <summary>The bytes R2025b writes for A é Ж 日 € (measured), and what they read back as.</summary>
    [Theory]
    [InlineData("Shift_JIS", new byte[] { 65, 252, 252, 132, 71, 147, 250, 252, 252 })]
    [InlineData("KOI8-R", new byte[] { 65, 26, 246, 26, 26 })]
    [InlineData("EUC-KR", new byte[] { 65, 26, 172, 168, 236, 237, 175, 254 })]
    [InlineData("GB2312", new byte[] { 65, 168, 166, 167, 168, 200, 213, 161, 161 })]
    [InlineData("IBM866", new byte[] { 65, 127, 134, 127, 127 })]
    public void Encode_WritesR2025bsBytes(string page, byte[] expected)
    {
        Encoding encoding = JgsMatlabEncoding.Named(page)!;
        Assert.Equal(expected, encoding.GetBytes("AéЖ日€"));
    }

    [Fact]
    public void SingleByte_RoundTripsEveryDefinedByte()
    {
        Encoding encoding = JgsMatlabEncoding.Named("ISO-8859-7")!;
        byte[] all = [.. Enumerable.Range(0, 256).Select(static b => (byte)b)];
        string text = encoding.GetString(all);
        byte[] defined = [.. all.Where(b => text[b] != '�')];
        Assert.Equal(defined, encoding.GetBytes(text.Replace("�", string.Empty, StringComparison.Ordinal)));
    }
}
