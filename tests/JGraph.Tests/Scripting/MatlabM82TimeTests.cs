using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M82: the three time divergences ADR 0064 recorded.
/// <para>
/// A zone is now carried and consulted, the readers stopped throwing away the fraction the storage
/// always had, and <c>calendarDuration</c> has storage of its own — a struct array, which is M64's
/// own rule ("a meaning attached to storage that already knows how to be an array") applied to the
/// one case a count of milliseconds could not cover.
/// </para>
/// <para>
/// The first fixture is the most important one in the file: an unzoned datetime is what almost every
/// script holds, and nothing here may change what one of those does. Every expression was run at the
/// CLI before it was written down.
/// </para>
/// </summary>
[Collection("JG facade")]
public class MatlabM82TimeTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public MatlabM82TimeTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    /// <summary>
    /// One variable's value, unwrapping the one-element array a time expression hands back.
    /// </summary>
    /// <remarks>
    /// A duration is a 1-by-1 array underneath, so <c>hours(a - b)</c> is a <c>double[]</c> of one
    /// where <c>year(t)</c> is a bare <c>double</c> — both are scalars to the script and only one of
    /// them is to the harness. Unwrapping here rather than at each assertion is what stops this file
    /// from being a list of which expressions happen to come back which way.
    /// </remarks>
    private static object Scalar(ScriptRunResult result, string name)
    {
        object? raw = Assert.Single(result.Variables, v => v.Name == name).RawValue;
        return raw switch
        {
            double[] { Length: 1 } numbers => numbers[0],
            bool[] { Length: 1 } flags => flags[0],
            string[] { Length: 1 } texts => texts[0],
            _ => raw!,
        };
    }

    private static double Number(ScriptRunResult result, string name) =>
        Assert.IsType<double>(Scalar(result, name));

    private static string Text(ScriptRunResult result, string name) =>
        Assert.IsType<string>(Scalar(result, name));

    private static bool Flag(ScriptRunResult result, string name) =>
        Assert.IsType<bool>(Scalar(result, name));

    private static void Succeeded(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message);

    /// <summary>
    /// Several refusals in a row, one after another. Deliberately not <c>Task.WhenAll</c>: the facade
    /// these scripts run against is one static figure stack, so two scripts at once are two scripts
    /// editing the same figure — which passes alone and fails beside its neighbours.
    /// </summary>
    private async Task RefusesEach(params (string Code, string Fragment)[] cases)
    {
        foreach ((string code, string fragment) in cases)
        {
            ScriptRunResult result = await RunMatlab(code);
            Assert.False(result.Success, $"expected a refusal from: {code}");
            Assert.Contains(fragment, result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // --- The thing that must not move --------------------------------------------------------------

    /// <summary>
    /// An unzoned datetime is what a measurement log holds, and none of this milestone may change what
    /// one does. This is the fixture the whole wave is checked against.
    /// </summary>
    [Fact]
    public async Task AnUnzonedDatetimeReadsExactlyAsItDid()
    {
        ScriptRunResult result = await RunMatlab(
            "t = datetime(2024, 3, 5, 14, 30, 45); "
            + "a = char(t); b = year(t); c = second(t); d = t.TimeZone; "
            + "e = double(seconds(1) + seconds(2) == seconds(3)); f = double(milliseconds(1500) == seconds(1.5));");
        Succeeded(result);
        Assert.Equal("05-Mar-2024 14:30:45", Text(result, "a"));
        Assert.Equal(2024, Number(result, "b"));
        Assert.Equal(45, Number(result, "c"));
        Assert.Equal(string.Empty, Text(result, "d"));
        Assert.Equal(1, Number(result, "e"));
        Assert.Equal(1, Number(result, "f"));
    }

    // --- Time zones --------------------------------------------------------------------------------

    /// <summary>
    /// The failure the divergence was about: two moments in different zones used to compare and
    /// subtract as though both were wall-clock readings, and to say so about a value each had been
    /// explicitly given a zone for.
    /// </summary>
    [Fact]
    public async Task TwoZonesSubtractAndSortAsTheInstantsTheyAre()
    {
        ScriptRunResult result = await RunMatlab(
            "a = datetime(2024, 3, 5, 12, 0, 0, 'TimeZone', 'UTC'); "
            + "b = datetime(2024, 3, 5, 12, 0, 0, 'TimeZone', 'America/New_York'); "
            + "gap = hours(a - b); earlier = double(a < b); ta = char(a); tb = char(b);");
        Succeeded(result);
        Assert.Equal(-5, Number(result, "gap"), 9);
        Assert.Equal(1, Number(result, "earlier"));

        // And each still reads as the clock in its own zone shows, which is what a zone is for.
        Assert.Equal("05-Mar-2024 12:00:00", Text(result, "ta"));
        Assert.Equal("05-Mar-2024 12:00:00", Text(result, "tb"));
    }

    /// <summary>
    /// Setting <c>TimeZone</c> means two different things and MATLAB means both: attaching to an
    /// unzoned value keeps the reading, and converting a zoned one keeps the instant.
    /// </summary>
    [Fact]
    public async Task AttachKeepsTheClockAndConvertKeepsTheInstant()
    {
        ScriptRunResult result = await RunMatlab(
            "x = datetime(2024, 3, 5, 12, 0, 0); x.TimeZone = 'America/New_York'; a = char(x); "
            + "y = datetime(2024, 3, 5, 12, 0, 0, 'TimeZone', 'UTC'); y.TimeZone = 'America/New_York'; b = char(y); "
            + "z = datetime(2024, 3, 5, 12, 0, 0, 'TimeZone', 'UTC'); z.TimeZone = ''; c = char(z); d = z.TimeZone;");
        Succeeded(result);
        Assert.Equal("05-Mar-2024 12:00:00", Text(result, "a"));
        Assert.Equal("05-Mar-2024 07:00:00", Text(result, "b"));
        Assert.Equal("05-Mar-2024 12:00:00", Text(result, "c"));
        Assert.Equal(string.Empty, Text(result, "d"));
    }

    [Fact]
    public async Task TzoffsetAndIsdstReadTheZoneAtThatMoment()
    {
        ScriptRunResult result = await RunMatlab(
            "w = datetime(2024, 1, 15, 'TimeZone', 'America/New_York'); "
            + "s = datetime(2024, 7, 15, 'TimeZone', 'America/New_York'); "
            + "a = hours(tzoffset(w)); b = hours(tzoffset(s)); c = isdst(w); d = isdst(s); "
            + "e = hours(tzoffset(datetime(2024, 1, 15)));");
        Succeeded(result);
        Assert.Equal(-5, Number(result, "a"), 9);
        Assert.Equal(-4, Number(result, "b"), 9);
        Assert.False(Flag(result, "c"));
        Assert.True(Flag(result, "d"));

        // An unzoned datetime has no offset, and that is missing rather than zero — zero is UTC's
        // answer, and the two must not read alike.
        Assert.True(double.IsNaN(Number(result, "e")));
    }

    /// <summary>
    /// The day a spring-forward falls in is twenty-three hours long, which is the reason ADR 0064's
    /// "caldays is safe because an unzoned datetime has no daylight saving" stopped being true.
    /// </summary>
    [Fact]
    public async Task ADayAcrossASpringForwardIsTwentyThreeHours()
    {
        ScriptRunResult result = await RunMatlab(
            "d1 = datetime(2024, 3, 10, 0, 0, 0, 'TimeZone', 'America/New_York'); "
            + "d2 = datetime(2024, 3, 12, 0, 0, 0, 'TimeZone', 'America/New_York'); "
            + "a = hours(dateshift(d1, 'end', 'day') - d1); "
            + "b = hours(dateshift(d2, 'end', 'day') - d2);");
        Succeeded(result);
        Assert.Equal(23, Number(result, "a"), 5);
        Assert.Equal(24, Number(result, "b"), 5);
    }

    [Fact]
    public async Task TheThreeSpellingsThatAreNotZoneIdsResolve()
    {
        ScriptRunResult result = await RunMatlab(
            "a = hours(tzoffset(datetime(2024, 1, 1, 'TimeZone', '+05:30'))); "
            + "b = hours(tzoffset(datetime(2024, 1, 1, 'TimeZone', 'UTC'))); "
            + "c = numel(timezones) > 50; "
            + "d = char(datetime(2024, 1, 15, 12, 0, 0, 'TimeZone', 'America/New_York', "
            + "'Format', 'uuuu-MM-dd HH:mm:ss Z'));");
        Succeeded(result);
        Assert.Equal(5.5, Number(result, "a"), 9);
        Assert.Equal(0, Number(result, "b"), 9);
        Assert.True(Flag(result, "c"));
        Assert.Equal("2024-01-15 12:00:00 -05:00", Text(result, "d"));
    }

    /// <summary>
    /// A zone this machine cannot find is refused rather than recorded and ignored, which is what it
    /// was until M82 and the worse of the two failures.
    /// </summary>
    [Fact]
    public async Task AnUnknownZoneIsRefusedByName() =>
        await RefusesEach(
            ("t = datetime(2024, 1, 1, 'TimeZone', 'Not/AZone');", "not a time zone"),
            ("t = datetime(2024, 1, 1); t.TimeZone = 'Nowhere';", "not a time zone"),
            ("d = seconds(1); z = d.TimeZone;", "no time zone"));

    // --- Sub-millisecond ---------------------------------------------------------------------------

    /// <summary>
    /// The storage always carried the fraction — <c>ToDateTime</c> rounds to ticks, not to whole
    /// milliseconds. It was thrown away by readers asking a <see cref="DateTime"/> for its
    /// whole-number <c>Millisecond</c>.
    /// </summary>
    [Fact]
    public async Task TheReadersKeepTheFractionTheStorageAlwaysHad()
    {
        ScriptRunResult result = await RunMatlab(
            "p = datetime(2024, 1, 1, 0, 0, 1.0005); a = second(p); "
            + "v = datevec(p); b = v(6); [~, ~, c] = hms(p); "
            + "d = second(datetime(2024, 1, 1, 0, 0, 1.123456));");
        Succeeded(result);
        Assert.Equal(1.0005, Number(result, "a"), 9);
        Assert.Equal(1.0005, Number(result, "b"), 9);
        Assert.Equal(1.0005, Number(result, "c"), 9);
        Assert.Equal(1.123456, Number(result, "d"), 6);
    }

    [Fact]
    public async Task TheDisplayAndTheTwoNewUnitsReachTheFraction()
    {
        ScriptRunResult result = await RunMatlab(
            "a = char(datetime(2024, 1, 1, 0, 0, 1.0005, 'Format', 'ss.SSSSSS')); "
            + "b = char(seconds(1.000001)); "
            + "c = microseconds(microseconds(7)); d = nanoseconds(nanoseconds(250));");
        Succeeded(result);
        Assert.Equal("01.000500", Text(result, "a"));
        Assert.Equal("1.000001 sec", Text(result, "b"));
        Assert.Equal(7, Number(result, "c"), 9);
        Assert.Equal(250, Number(result, "d"), 5);
    }

    /// <summary>
    /// The end of a unit is the largest value the storage can hold inside it, not a fixed step below
    /// the boundary: a double's spacing at a datetime's magnitude is about nine tenths of a
    /// microsecond, so subtracting one tick changed nothing at all and <c>dateshift(t, 'end', 'month')</c>
    /// answered with the first of the next month.
    /// </summary>
    [Fact]
    public async Task TheEndOfAUnitLandsInsideItAtEveryMagnitude()
    {
        ScriptRunResult result = await RunMatlab(
            "e = dateshift(datetime(2024, 3, 5, 12, 0, 0), 'end', 'day'); "
            + "a = hour(e); b = minute(e); c = second(e); "
            + "d = day(dateshift(datetime(2024, 3, 5), 'end', 'month')); "
            + "f = day(dateshift(datetime(2024, 2, 5), 'end', 'month'));");
        Succeeded(result);
        Assert.Equal(23, Number(result, "a"));
        Assert.Equal(59, Number(result, "b"));
        Assert.InRange(Number(result, "c"), 59.99, 60.0);
        Assert.Equal(31, Number(result, "d"));
        Assert.Equal(29, Number(result, "f"));
    }

    // --- calendarDuration --------------------------------------------------------------------------

    [Fact]
    public async Task ACalendarDurationIsATypeWithStorageOfItsOwn()
    {
        ScriptRunResult result = await RunMatlab(
            "a = class(calmonths(3)); b = iscalendarduration(calmonths(3)); "
            + "c = isstruct(calmonths(3)); d = isduration(calmonths(3)); "
            + "e = char(calmonths(3)); f = char(calyears(1)); g = char(caldays(3)); "
            + "h = char(calweeks(2)); i = char(calendarDuration(1, 2, 3));");
        Succeeded(result);
        Assert.Equal("calendarDuration", Text(result, "a"));
        Assert.True(Flag(result, "b"));

        // The storage is a struct array; the type is not a struct. The tagged-value rule M68 wrote
        // for MException, applied to the fourth tagged type.
        Assert.False(Flag(result, "c"));
        Assert.False(Flag(result, "d"));
        Assert.Equal("3mo", Text(result, "e"));
        Assert.Equal("1y", Text(result, "f"));
        Assert.Equal("3d", Text(result, "g"));
        Assert.Equal("14d", Text(result, "h"));
        Assert.Equal("1y 2mo 3d", Text(result, "i"));
    }

    /// <summary>
    /// The components are applied in order and do not collapse into each other, which is the whole
    /// reason a calendar duration cannot be a count of milliseconds.
    /// </summary>
    [Fact]
    public async Task AMonthThenADayIsNotADayThenAMonth()
    {
        ScriptRunResult result = await RunMatlab(
            "a = char(datetime(2024, 1, 31) + calmonths(1)); "
            + "b = char(datetime(2024, 1, 31) + caldays(31)); "
            + "c = char(datetime(2024, 3, 5) - calyears(1)); "
            + "d = char(calmonths(1) + caldays(2)); e = char(calmonths(3) - calmonths(1)); "
            + "f = char(2 * calmonths(3)); g = char(calmonths(1) + hours(5)); "
            + "h = caldays(caldays(3));");
        Succeeded(result);
        Assert.Equal("29-Feb-2024", Text(result, "a"));
        Assert.Equal("02-Mar-2024", Text(result, "b"));
        Assert.Equal("05-Mar-2023", Text(result, "c"));
        Assert.Equal("1mo 2d", Text(result, "d"));
        Assert.Equal("2mo", Text(result, "e"));
        Assert.Equal("6mo", Text(result, "f"));
        Assert.Equal("1mo 05:00:00", Text(result, "g"));
        Assert.Equal(3, Number(result, "h"));
    }

    /// <summary>The measurement adds back to the moment it was measured to, which is the test of it.</summary>
    [Fact]
    public async Task BetweenAndCaldiffMeasureACalendarDifference()
    {
        ScriptRunResult result = await RunMatlab(
            "span = between(datetime(2024, 1, 1), datetime(2025, 3, 15)); "
            + "a = char(span); b = char(datetime(2024, 1, 1) + span); c = split(span, 'months'); "
            + "d = char(caldiff([datetime(2024, 1, 1) datetime(2024, 3, 15)]));");
        Succeeded(result);
        Assert.Equal("1y 2mo 14d", Text(result, "a"));
        Assert.Equal("15-Mar-2025", Text(result, "b"));
        Assert.Equal(14, Number(result, "c"));
        Assert.Equal("2mo 14d", Text(result, "d"));
    }

    /// <summary>
    /// A bracket of times is a time. Concatenation minted a fresh value and dropped the tag, so
    /// <c>[t1 t2]</c> came back as plain milliseconds — the same failure M64 recorded as "a tag a
    /// builtin does not know about is lost", found again because <c>caldiff</c> takes an array and
    /// this is how a script writes one.
    /// </summary>
    [Fact]
    public async Task ABracketOfTimesIsATime()
    {
        ScriptRunResult result = await RunMatlab(
            "a = class([datetime(2024,1,1) datetime(2024,3,15)]); "
            + "b = numel([datetime(2024,1,1) datetime(2024,3,15)]); "
            + "c = class([seconds(1) seconds(2)]); d = class([calmonths(1) calmonths(2)]);");
        Succeeded(result);
        Assert.Equal("datetime", Text(result, "a"));
        Assert.Equal(2, Number(result, "b"));
        Assert.Equal("duration", Text(result, "c"));
        Assert.Equal("calendarDuration", Text(result, "d"));
    }

    /// <summary>
    /// And binding a name to one keeps it a calendar duration. The struct copy carried a class name
    /// and nothing else, which was true for as long as a class name was the only tag a struct had.
    /// </summary>
    [Fact]
    public async Task BindingANameToACalendarDurationKeepsItOne()
    {
        ScriptRunResult result = await RunMatlab(
            "x = calmonths(3); y = x; a = class(y); b = char(y); "
            + "c = char(datetime(2024, 1, 31) + y);");
        Succeeded(result);
        Assert.Equal("calendarDuration", Text(result, "a"));
        Assert.Equal("3mo", Text(result, "b"));

        // Three months on from the 31st of January is the 30th of April: the day clamps to the month
        // it lands in, which is what makes a month a month rather than a number of days.
        Assert.Equal("30-Apr-2024", Text(result, "c"));
    }

    // --- What is still refused ---------------------------------------------------------------------

    /// <summary>
    /// Two calendar lengths have no order without a reference date, so the comparison is refused by
    /// name rather than answered from whichever component the storage happened to compare first.
    /// </summary>
    [Fact]
    public async Task TheCalendarOperatorsRefuseWhatHasNoMeaning()
    {
        await RefusesEach(
            ("x = calmonths(1) < calmonths(2);", "order"),
            ("x = calmonths(1) > caldays(40);", "order"),
            ("x = datetime(2024,1,1) * calmonths(1);", "cannot combine"),
            ("x = calmonths(1) * calmonths(2);", "cannot combine"),
            ("x = calmonths(1) + 3;", "no units of its own"));

        // Adding each to a datetime is the comparison that does have an answer, and the message says so.
        ScriptRunResult result = await RunMatlab(
            "a = double((datetime(2024,1,1) + calmonths(1)) < (datetime(2024,1,1) + caldays(40)));");
        Succeeded(result);
        Assert.Equal(1, Number(result, "a"));
    }
}
