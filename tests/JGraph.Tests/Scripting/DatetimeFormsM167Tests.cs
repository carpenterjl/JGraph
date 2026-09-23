using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), datetime and duration forms (appendix A #127, #128, #129): a one-element time
/// fills a mask selection, text is written into a datetime through its format and into a duration
/// through the timer formats, a number is a count of days in a duration and refused in a
/// datetime, an element's component is set (<c>d(2).Year = 2000</c>), and the default display
/// format follows what the array holds.
/// </summary>
/// <remarks>
/// The parity fixture <c>datetime_forms</c> holds R2025b's answers for every form; these pin the
/// roads the sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class DatetimeFormsM167Tests : IDisposable
{
    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public DatetimeFormsM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim().Replace("\r", "");
    }

    private string RunForError(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code,
            new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(result.Success);
        return result.Message ?? string.Empty;
    }

    private const string Three = "d = datetime(2020, 1, 1) + days(0:2);";

    private const string Show = "fprintf('%s %s %s\\n', class(d), strjoin(cellstr(char(d(:)))', ','), d.Format);";

    // ---- scalar expansion and text ----------------------------------------------------------------

    [Theory]
    [InlineData("e = d; e(day(e) > 1) = NaT; d = e;", "datetime 01-Jan-2020,NaT,NaT dd-MMM-uuuu")]
    [InlineData("d(day(d) > 1) = datetime(2022, 1, 1);", "datetime 01-Jan-2020,01-Jan-2022,01-Jan-2022 dd-MMM-uuuu")]
    [InlineData("d(2) = '2021-05-05';", "datetime 01-Jan-2020,05-May-2021,03-Jan-2020 dd-MMM-uuuu")]
    [InlineData("d(2) = \"2021-05-05\";", "datetime 01-Jan-2020,05-May-2021,03-Jan-2020 dd-MMM-uuuu")]
    [InlineData("d(1:2) = [\"2021-01-01\" \"2021-01-02\"];", "datetime 01-Jan-2021,02-Jan-2021,03-Jan-2020 dd-MMM-uuuu")]
    [InlineData("d(1:2) = {'2021-01-01', '2021-01-02'};", "datetime 01-Jan-2021,02-Jan-2021,03-Jan-2020 dd-MMM-uuuu")]
    [InlineData("d(1:2) = \"2021-05-05\";", "datetime 05-May-2021,05-May-2021,03-Jan-2020 dd-MMM-uuuu")]
    [InlineData("d(2) = 'NaT';", "datetime 01-Jan-2020,NaT,03-Jan-2020 dd-MMM-uuuu")]
    [InlineData("d(2) = '';", "datetime 01-Jan-2020,NaT,03-Jan-2020 dd-MMM-uuuu")]
    [InlineData("d(end + 1) = '2022-01-01';", "datetime 01-Jan-2020,02-Jan-2020,03-Jan-2020,01-Jan-2022 dd-MMM-uuuu")]
    [InlineData("d(5) = '2022-01-01';", "datetime 01-Jan-2020,02-Jan-2020,03-Jan-2020,NaT,01-Jan-2022 dd-MMM-uuuu")]
    [InlineData("d(2) = datetime(2022, 1, 1, 'Format', 'yyyy');", "datetime 01-Jan-2020,01-Jan-2022,03-Jan-2020 dd-MMM-uuuu")]
    public void AOneElementTimeOrATextFillsTheSelection(string write, string expected)
    {
        Assert.Equal(expected, RunAndRead($"{Three}\n{write}\n{Show}"));
    }

    [Fact]
    public void TextIsReadThroughTheTargetsOwnFormatFirst()
    {
        string text = RunAndRead($"{Three}\nd.Format = 'yyyy/MM/dd';\nd(2) = '2021/05/05';\nd(3) = '2021-06-06';\n{Show}");
        Assert.Equal("datetime 2020/01/01,2021/05/05,2021/06/06 yyyy/MM/dd", text);
    }

    [Fact]
    public void TextIntoAZonedDatetimeIsTheWallClockInItsZone()
    {
        string text = RunAndRead(
            "z = datetime(2020, 1, 1, 12, 0, 0, 'TimeZone', 'UTC') + days(0:1);\nz(2) = '2020-01-02 03:00:00';\n"
            + "fprintf('%d %s\\n', hour(z(2)), z.TimeZone);");
        Assert.Equal("3 UTC", text);
    }

    [Fact]
    public void AWriteThroughAStructFieldAndACellLeavesTheSourceAlone()
    {
        string text = RunAndRead(
            $"{Three}\nst.t = d; st.t(2) = '2021-05-05';\nc = {{d}}; c{{1}}(3) = '2022-06-06';\n"
            + "fprintf('%d %d %d %d\\n', year(d(2)), year(st.t(2)), year(d(3)), year(c{1}(3)));");
        Assert.Equal("2020 2021 2020 2022", text);
    }

    // ---- refusals, in R2025b's words --------------------------------------------------------------

    [Theory]
    [InlineData("d(2) = 5;", "You cannot assign numeric values to a datetime array.")]
    [InlineData("d(2) = NaN;", "You cannot assign numeric values to a datetime array.")]
    [InlineData("d(2) = int8(5);", "You cannot assign numeric values to a datetime array.")]
    [InlineData("d(2) = true;", "Right hand side of an assignment must be a datetime array or text representing dates and times.")]
    [InlineData("d(2) = seconds(5);", "Right hand side of an assignment must be a datetime array or text representing dates and times.")]
    [InlineData("d(2) = {datetime(2021, 1, 1)};", "Right hand side of an assignment must be a datetime array or text representing dates and times.")]
    [InlineData("d(1:2) = ['2021-05-05'; '2021-06-06'];", "Right hand side of an assignment must be a datetime array or text representing dates and times.")]
    [InlineData("d(2) = 'garbage';", "Unable to convert the text 'garbage' to a datetime value because its format was not recognized.")]
    [InlineData("d(2) = datetime(2020, 1, 5, 'TimeZone', 'UTC');", "Cannot combine or compare a datetime array with a time zone with one without a time zone.")]
    [InlineData("d(2) = {'2021-05-05', '2021-06-06'};", "Unable to perform assignment because the left and right sides have a different number of elements.")]
    public void ADatetimeRefusesWhatR2025bRefuses(string write, string expected)
    {
        Assert.StartsWith(expected, RunForError($"{Three}\n{write}"));
    }

    [Theory]
    [InlineData("x = []; x(2) = datetime(2020, 1, 1);", "Unable to perform assignment because value of type 'datetime' is not convertible to 'double'.")]
    [InlineData("x = zeros(1, 2); x(2) = datetime(2020, 1, 1);", "Unable to perform assignment because value of type 'datetime' is not convertible to 'double'.")]
    [InlineData("x = 'abc'; x(2) = datetime(2020, 1, 1);", "Unable to perform assignment because the left and right sides have a different number of elements.")]
    public void ADatetimeHasNoPlaceInANumericOrCharArray(string write, string expected)
    {
        Assert.StartsWith(expected, RunForError(write));
    }

    // ---- durations --------------------------------------------------------------------------------

    [Theory]
    [InlineData("u(2) = '00:30:00';", "[1 0.5 3]")]
    [InlineData("u(1:2) = [\"01:00:00\" \"02:30:00\"];", "[1 2.5 3]")]
    [InlineData("u(2) = 5;", "[1 120 3]")]
    [InlineData("u(2) = NaN;", "[1 NaN 3]")]
    [InlineData("u(u > hours(1)) = seconds(5);", "[3600 5 5]")]
    [InlineData("u(5) = minutes(5);", "[3600 7200 10800 0 300]")]
    public void ADurationTakesTextADayCountAndAOneElementDuration(string write, string expected)
    {
        // Hours for the first four, whole seconds for the two whose hours are not short decimals.
        string unit = expected.Contains("3600") ? "seconds" : "hours";
        string text = RunAndRead($"u = hours(1:3);\n{write}\nfprintf('%s %s\\n', class(u), mat2str({unit}(u)));");
        Assert.Equal("duration " + expected, text);
    }

    [Theory]
    [InlineData("u(2) = datetime(2020, 1, 1);")]
    [InlineData("u(2) = 'garbage';")]
    [InlineData("u(2) = {1};")]
    public void ADurationRefusesTheRestInOneSentence(string write)
    {
        Assert.StartsWith(
            "Right hand side of an assignment must be a duration array, numeric array representing days, or text representing durations in timer formats (e.g. 'hh:mm:ss').",
            RunForError($"u = hours(1:3);\n{write}"));
    }

    // ---- an element's component ------------------------------------------------------------------

    [Theory]
    [InlineData("e = d; e(2).Year = 2000;", "2020 2020 2000")]
    [InlineData("e = d; e(1:2).Year = 2000;", "2020 2000 2000")]
    [InlineData("st.t = d; st.t(2).Year = 1999; e = st.t;", "2020 2020 1999")]
    public void AnElementsComponentIsSetOnTheElementsAlone(string write, string expected)
    {
        string text = RunAndRead($"{Three}\n{write}\nfprintf('%d %d %d\\n', year(d(2)), year(e(1)), year(e(2)));");
        Assert.Equal(expected, text);
    }

    // ---- the display format follows the values ----------------------------------------------------

    [Fact]
    public void TheDefaultFormatFollowsWhatTheArrayHolds()
    {
        string text = RunAndRead(
            $"{Three}\na = d.Format; d(2) = datetime(2020, 1, 2, 10, 0, 0); b = d.Format;\n"
            + "d(2) = datetime(2020, 1, 2); c = d.Format; d(:) = NaT; e = d.Format;\n"
            + "fprintf('%s|%s|%s|%s\\n', a, b, c, e);");
        Assert.Equal("dd-MMM-uuuu|dd-MMM-uuuu HH:mm:ss|dd-MMM-uuuu|dd-MMM-uuuu HH:mm:ss", text);
    }

    [Fact]
    public void AFormatAScriptSetStaysAsItWasSet()
    {
        string text = RunAndRead($"{Three}\nd.Format = 'yyyy'; d(2) = datetime(2020, 1, 2, 10, 0, 0);\nfprintf('%s\\n', d.Format);");
        Assert.Equal("yyyy", text);
    }

    [Fact]
    public void AReshapedDatetimeIsStillADatetime()
    {
        string text = RunAndRead(
            "m = reshape(datetime(2020, 1, 1) + days(0:3), 2, 2); m(1, :) = NaT;\n"
            + "fprintf('%s %d %d\\n', class(m), sum(isnat(m(:))), day(m(2, 2)));");
        Assert.Equal("datetime 2 4", text);
    }

    [Fact]
    public void CharOfADurationArrayIsRightAligned()
    {
        string text = RunAndRead("c = char(hours([1 0.5])); fprintf('[%s][%s]\\n', c(1, :), c(2, :));");
        Assert.Equal("[  1 hr][0.5 hr]", text);
    }
}
