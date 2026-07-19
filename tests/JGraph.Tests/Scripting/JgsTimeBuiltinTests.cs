using System.Globalization;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The time and date builtins: the tic/toc stopwatch and the MATLAB-style date functions
/// (clock, now, datenum, datestr, datetime, date, time). Date functions use MATLAB serial
/// date numbers, which are .NET OLE Automation dates offset so that datenum(1970, 1, 1) == 719529.
/// </summary>
[Collection("JG facade")]
public class JgsTimeBuiltinTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public JgsTimeBuiltinTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> Run(string code) =>
        _engine.RunAsync(code, new ScriptContext(_output, (_, figure) => _figures.Add(figure), null), default);

    private async Task<string> Eval(string expression)
    {
        ScriptRunResult result = await Run($"print({expression})");
        Assert.True(result.Success, result.Message);
        return _output.NormalText.Trim();
    }

    // --- Stopwatch -----------------------------------------------------------------------------

    [Fact]
    public async Task Toc_WithHandle_IsNonNegative() =>
        // tic() returns a handle; toc(handle) reads its elapsed seconds — a named/multiple-timer path.
        Assert.Equal("true", await Eval("toc(tic()) >= 0"));

    [Fact]
    public async Task Toc_MeasuresElapsedTime_AndIsMonotonic()
    {
        // Bare `tic;` starts the default stopwatch; two later toc() reads are non-negative and ordered.
        ScriptRunResult result = await Run("""
            tic;
            let first = toc();
            let second = toc();
            print(second >= first)
            print(first >= 0)
            """);

        Assert.True(result.Success, result.Message);
        Assert.Equal("true\ntrue", _output.NormalText.Trim().ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Toc_BeforeAnyTic_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print(toc())");

        Assert.False(result.Success);
        Assert.Contains("tic", Assert.Single(result.Diagnostics).Message);
    }

    // --- clock / now / time --------------------------------------------------------------------

    [Fact]
    public async Task Clock_IsASixElementVector() =>
        Assert.Equal("6", await Eval("length(clock())"));

    [Fact]
    public async Task Clock_YearIsThePlausibleFirstElement()
    {
        ScriptRunResult result = await Run("""
            let c = clock();
            print(c(1))
            """);

        Assert.True(result.Success, result.Message);
        Assert.True(int.Parse(_output.NormalText.Trim(), CultureInfo.InvariantCulture) >= 2024);
    }

    [Fact]
    public async Task Now_IsAfterAKnownPastDate()
    {
        // datenum(2020, 1, 1) == 737791; "now" must be later than that.
        double serial = double.Parse(await Eval("now()"), CultureInfo.InvariantCulture);
        Assert.True(serial > 737791.0, $"now() = {serial}");
    }

    [Fact]
    public async Task Time_IsPlausibleUnixEpochSeconds()
    {
        // Later than 2023-01-01 (1.672e9); this pins the epoch-seconds convention.
        double epoch = double.Parse(await Eval("time()"), CultureInfo.InvariantCulture);
        Assert.True(epoch > 1_672_531_200.0, $"time() = {epoch}");
    }

    // --- datenum / datestr ---------------------------------------------------------------------

    [Fact]
    public async Task Datenum_FromComponents_MatchesMatlabSerial() =>
        Assert.Equal("737791", await Eval("datenum(2020, 1, 1)"));

    [Fact]
    public async Task Datenum_FromVector_MatchesComponents() =>
        Assert.Equal("737791", await Eval("datenum([2020, 1, 1])"));

    [Fact]
    public async Task Datenum_WithTimeOfDay_AddsFractionalDay() =>
        // Noon is half a day past midnight.
        Assert.Equal("737791.5", await Eval("datenum(2020, 1, 1, 12, 0, 0)"));

    [Fact]
    public async Task Datenum_WrongVectorLength_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print(datenum([2020, 1]))");

        Assert.False(result.Success);
        Assert.Contains("3", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public async Task Datestr_DefaultFormat_RoundTripsFromDatenum() =>
        Assert.Equal("01-Jan-2020 00:00:00", await Eval("datestr(datenum(2020, 1, 1))"));

    [Fact]
    public async Task Datestr_CustomFormat_UsesDotNetTokens() =>
        Assert.Equal("2020-01-01", await Eval("datestr(737791, 'yyyy-MM-dd')"));

    [Fact]
    public async Task Datestr_OutOfRangeSerial_IsRuntimeError()
    {
        ScriptRunResult result = await Run("print(datestr(-999999999))");

        Assert.False(result.Success);
        Assert.Contains("out of range", Assert.Single(result.Diagnostics).Message);
    }

    // --- datetime / date (current-time strings) ------------------------------------------------

    [Fact]
    public async Task Datetime_And_Date_ReturnDashSeparatedStrings()
    {
        // Both read the wall clock; assert only the shape (a dash-separated date), not the value.
        Assert.Contains("-", await Eval("datetime()"));
        Assert.Contains("-", await Eval("date()"));
    }
}
