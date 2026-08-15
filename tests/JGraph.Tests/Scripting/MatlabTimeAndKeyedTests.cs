using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M64 — the time types and the keyed collections. <c>datetime</c> stops being a char row of the
/// current moment and becomes a point in time; <c>duration</c> stops being a count of seconds and
/// becomes a length of one; and <c>containers.Map</c> and <c>dictionary</c> arrive with the two
/// different copy semantics MATLAB gives them.
/// </summary>
/// <remarks>
/// A time here is a numeric array of milliseconds wearing a tag, exactly as a string array is an
/// array of strings wearing one (M63) — so the tests that matter most are the ones that check the
/// tag survives the journey (a copy, a transpose, an index, a reduction) and the ones that check the
/// operators refuse what MATLAB refuses. Milliseconds rather than days is what makes
/// <c>seconds(1) + seconds(2) == seconds(3)</c> exactly true, and that has a test of its own.
/// </remarks>
[Collection("JG facade")]
public class MatlabTimeAndKeyedTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabTimeAndKeyedTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task<string> RunRefusing(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success, "expected a refusal, but the code ran");
        return result.Message + _output.ErrorText;
    }

    // --- The two types --------------------------------------------------------------------------

    [Fact]
    public Task ADatetimeIsAPointInTimeAndADurationIsALengthOfOne() => RunAsserting("""
        t = datetime(2024, 1, 15);
        d = seconds(90);
        assert(isdatetime(t) && ~isduration(t));
        assert(isduration(d) && ~isdatetime(d));
        assert(strcmp(class(t), 'datetime'));
        assert(strcmp(class(d), 'duration'));
        assert(isa(t, 'datetime'));

        % Neither is a number, though both are stored as one: what the milliseconds mean is the
        % value, and the storage is only how it is kept.
        assert(~isnumeric(t) && ~isnumeric(d));
        """);

    [Fact]
    public Task ADatetimeIsOneElementAndAnArrayOfThemKeepsItsShape() => RunAsserting("""
        t = datetime(2024, 1, 15);
        assert(numel(t) == 1 && isscalar(t));
        assert(isequal(size(t), [1 1]));

        span = datetime(2024, 1, 1) + days(0:4);
        assert(numel(span) == 5);
        assert(isequal(size(span), [1 5]));
        assert(isdatetime(span(3)));      % a selection out of a datetime is a datetime
        assert(isdatetime(span(2:4)));
        """);

    /// <summary>
    /// The reason the storage counts milliseconds rather than days. In days, one second plus two
    /// seconds is three sixteenths of a millisecond short of three seconds, and the comparison a
    /// script actually writes comes back false.
    /// </summary>
    [Fact]
    public Task DurationsAddExactly() => RunAsserting("""
        assert(seconds(1) + seconds(2) == seconds(3));
        assert(minutes(1) == seconds(60));
        assert(hours(1) == minutes(60));
        assert(days(1) == hours(24));
        assert(seconds(hours(2)) == 7200);
        """);

    [Fact]
    public Task NaTIsTheMissingMoment() => RunAsserting("""
        assert(isnat(NaT));
        assert(isdatetime(NaT));
        assert(~isnat(datetime(2024, 1, 1)));
        assert(isequal(size(NaT(2, 3)), [2 3]));
        """);

    // --- The tag survives every journey ------------------------------------------------------------

    /// <summary>
    /// A tag that lives on the wrapper is lost by every path that mints a new one, and MATLAB's
    /// value semantics mint a new one constantly. The transpose was the one this milestone found:
    /// <c>timetable(seconds(1:3)', …)</c> stored raw milliseconds because the tag came off at the
    /// apostrophe.
    /// </summary>
    [Fact]
    public Task ATimeStaysOneThroughCopyingPassingTransposingAndContainers() => RunAsserting("""
        d = seconds([1 2 3]);
        copied = d;
        assert(isduration(copied));
        assert(isduration(d'));                  % the transpose kept it
        assert(isequal(size(d'), [3 1]));
        assert(isduration(relay(d)));
        c = {d};
        assert(isduration(c{1}));
        s.span = d;
        assert(isduration(s.span));

        function out = relay(in)
            out = in;
        end
        """);

    [Fact]
    public Task ATransposeKeepsEveryOtherKindTag() => RunAsserting("""
        % Not a time question at all: the same path carries the numeric class and the string array,
        % and carried neither before M64 went looking.
        assert(strcmp(class(uint8([1 2])'), 'uint8'));
        assert(isstring(["a" "b"]'));
        """);

    // --- Arithmetic -------------------------------------------------------------------------------

    [Fact]
    public Task SubtractingTwoDatetimesGivesTheDurationBetweenThem() => RunAsserting("""
        a = datetime(2024, 1, 1);
        b = datetime(2024, 1, 15);
        gap = b - a;
        assert(isduration(gap));
        assert(days(gap) == 14);
        """);

    [Fact]
    public Task ADatetimeMovesByADurationAndStaysADatetime() => RunAsserting("""
        a = datetime(2024, 1, 1);
        assert(isdatetime(a + days(7)));
        assert(isdatetime(days(7) + a));         % either way round
        assert(isdatetime(a - hours(12)));
        assert(days((a + days(7)) - a) == 7);
        """);

    [Fact]
    public Task DurationsCombineAndScale() => RunAsserting("""
        assert(hours(1) + minutes(30) == minutes(90));
        assert(hours(1) - minutes(30) == minutes(30));
        assert(minutes(20) * 3 == hours(1));
        assert(3 * minutes(20) == hours(1));
        assert(hours(1) / 2 == minutes(30));

        % A duration over a duration is a plain number — how many times one goes into the other,
        % which is how a script counts elapsed seconds.
        assert(hours(2) / hours(1) == 2);
        assert(~isduration(hours(2) / hours(1)));
        """);

    [Fact]
    public Task TimesCompareOnTheirOwnOrder() => RunAsserting("""
        a = datetime(2024, 1, 1);
        b = datetime(2024, 1, 15);
        assert(a < b && b > a && a == a && a ~= b);
        assert(seconds(30) < minutes(1));
        assert(isequal([a b] == a, [true false]));
        """);

    /// <summary>
    /// What the operators refuse, and why each refusal is worth having: every one of these would
    /// otherwise answer with a plausible number.
    /// </summary>
    [Fact]
    public async Task TheOperatorsRefuseWhatHasNoMeaning()
    {
        Assert.Contains("no units of its own", await RunRefusing("datetime(2024,1,1) + 1;"), StringComparison.Ordinal);
        Assert.Contains("no units of its own", await RunRefusing("datetime(2024,1,1) * 2;"), StringComparison.Ordinal);
        Assert.Contains("only subtraction", await RunRefusing("datetime(2024,1,1) + datetime(2024,1,2);"), StringComparison.Ordinal);
        Assert.Contains("cannot combine two durations", await RunRefusing("hours(1) * hours(2);"), StringComparison.Ordinal);
        Assert.Contains("make the number a duration", await RunRefusing("hours(1) + 1;"), StringComparison.Ordinal);
        Assert.Contains("not defined on datetimes", await RunRefusing("sum(datetime(2024,1,1));"), StringComparison.Ordinal);
        Assert.Contains("point in time, not a length", await RunRefusing("hours(datetime(2024,1,1));"), StringComparison.Ordinal);
    }

    // --- Reductions -------------------------------------------------------------------------------

    [Fact]
    public Task TheReductionsHandBackWhatTheyWereGiven() => RunAsserting("""
        t = datetime(2024, 1, 1) + days(0:4);
        assert(isdatetime(min(t)) && isdatetime(max(t)));
        assert(isdatetime(sort(t)) && isdatetime(mean(t)));
        assert(min(t) == datetime(2024, 1, 1));
        assert(max(t) == datetime(2024, 1, 5));

        % The gap between moments is a length of time, not a moment.
        assert(isduration(diff(t)));
        assert(numel(diff(t)) == 4);
        assert(days(diff(t)(1)) == 1);

        d = seconds([30 60 90]);
        assert(isduration(sum(d)) && seconds(sum(d)) == 180);
        assert(isduration(max(d)));
        """);

    /// <summary>
    /// Only the first output of a reduction is a time: the second is a position in the input, and
    /// stamping that with a datetime tag would claim the index was a date.
    /// </summary>
    [Fact]
    public Task OnlyTheFirstOutputOfAReductionIsATime() => RunAsserting("""
        t = datetime(2024, 1, 1) + days([2 0 1]);
        [m, i] = min(t);
        assert(isdatetime(m));
        assert(~isdatetime(i) && i == 2);
        """);

    // --- Display and conversion ---------------------------------------------------------------------

    [Fact]
    public Task ATimeShowsItselfAsTextRatherThanAsItsMilliseconds() => RunAsserting("""
        t = datetime(2024, 3, 5, 14, 30, 45);
        assert(strcmp(char(t), '05-Mar-2024 14:30:45'));
        assert(isstring(string(t)));
        assert(strcmp(string(t), '05-Mar-2024 14:30:45'));
        assert(strcmp(datestr(t), '05-Mar-2024 14:30:45'));
        assert(strcmp(datestr(t, 'uuuu/MM/dd'), '2024/03/05'));

        % A date with no time of day says so, which is the format MATLAB's own constructor picks.
        assert(strcmp(char(datetime(2024, 3, 5)), '05-Mar-2024'));
        assert(strcmp(char(seconds(90)), '90 sec'));
        assert(strcmp(char(duration(1, 30, 0)), '01:30:00'));
        """);

    [Fact]
    public Task TheSerialDateSurfaceAndTheNewTypeConvertBothWays() => RunAsserting("""
        t = datetime(2024, 3, 5, 14, 30, 45);
        assert(abs(datenum(t) - datenum(2024, 3, 5, 14, 30, 45)) < 1e-9);

        % A serial date number counts days, so a round trip through one is exact only to the
        % precision a double has left at that magnitude — about a hundredth of a millisecond. It is
        % the reason the storage counts milliseconds and converts, rather than counting days.
        assert(abs(seconds(datetime(datenum(t), 'ConvertFrom', 'datenum') - t)) < 1e-3);
        assert(isequal(datevec(t), [2024 3 5 14 30 45]));
        assert(datetime(datevec(t)) == t);
        assert(posixtime(datetime(1970, 1, 1)) == 0);
        assert(yyyymmdd(t) == 20240305);
        """);

    /// <summary>
    /// <c>x = now</c> used to bind the function rather than the time, so <c>datestr(now)</c> — the
    /// commonest date line anyone writes — failed complaining it had been handed a function. The
    /// probe that opened this milestone found it.
    /// </summary>
    [Fact]
    public Task TheClockReadingsAnswerWithTheirValueOnABareMention() => RunAsserting("""
        x = now;
        assert(isnumeric(x) && ~isa(x, 'function_handle'));
        assert(numel(clock) == 6);
        assert(~isempty(datestr(now)));
        assert(year(now) >= 2024);
        """);

    [Fact]
    public Task TheFieldAccessorsReadAMomentApart() => RunAsserting("""
        t = datetime(2024, 3, 5, 14, 30, 45);
        assert(year(t) == 2024 && month(t) == 3 && day(t) == 5);
        assert(hour(t) == 14 && minute(t) == 30 && second(t) == 45);
        assert(quarter(t) == 1 && weekday(t) == 3);
        [y, m, d] = ymd(t);
        assert(y == 2024 && m == 3 && d == 5);
        [h, mi, s] = hms(t);
        assert(h == 14 && mi == 30 && s == 45);
        assert(isduration(timeofday(t)));
        assert(hours(timeofday(t)) == 14.5125);
        """);

    [Fact]
    public Task DateshiftMovesToABoundaryAndKeepsTheKind() => RunAsserting("""
        t = datetime(2024, 3, 5, 14, 30, 45);
        assert(isdatetime(dateshift(t, 'start', 'day')));
        assert(dateshift(t, 'start', 'day') == datetime(2024, 3, 5));
        assert(dateshift(t, 'start', 'month') == datetime(2024, 3, 1));
        assert(dateshift(t, 'start', 'month', 1) == datetime(2024, 4, 1));
        assert(day(dateshift(t, 'end', 'month')) == 31);
        assert(isbetween(t, datetime(2024, 1, 1), datetime(2024, 12, 31)));
        assert(~isbetween(t, datetime(2025, 1, 1), datetime(2025, 12, 31)));
        """);

    [Fact]
    public Task ADatetimePlotsOnADateRuler() => RunAsserting("""
        span = datetime(2024, 1, 1) + days(0:4);
        figure(1); clf;
        plot(span, [1 2 3 4 5]);
        limits = get(gca, 'XLim');

        % The drawing pipeline works in doubles from end to end, so the type cannot travel through
        % it: the numbers reach the axis and the axis remembers what they mean.
        assert(isnumeric(limits));
        assert(limits(2) - limits(1) >= 4);
        """);

    // --- Keyed collections ---------------------------------------------------------------------------

    [Fact]
    public Task AMapReadsAndWritesByKey() => RunAsserting("""
        m = containers.Map({'alpha', 'beta'}, {1, 2});
        assert(strcmp(class(m), 'containers.Map'));
        assert(m.Count == 2);
        assert(m('alpha') == 1);
        m('gamma') = 3;
        assert(m.Count == 3 && m('gamma') == 3);
        assert(isKey(m, 'alpha') && ~isKey(m, 'delta'));
        assert(iscell(keys(m)) && numel(keys(m)) == 3);
        assert(iscell(values(m)));
        remove(m, 'gamma');
        assert(m.Count == 2 && ~isKey(m, 'gamma'));

        n = containers.Map({1, 2}, {'one', 'two'});
        assert(strcmp(n(2), 'two'));
        """);

    /// <summary>
    /// A bare <c>containers.Map</c> has to build the collection where it is written. Binding the
    /// constructor instead made every later mention of the name call it afresh, so the writes went
    /// into collections nobody kept — and vanished without a word.
    /// </summary>
    [Fact]
    public Task ABareContainersMapBuildsAnEmptyCollection() => RunAsserting("""
        e = containers.Map;
        assert(strcmp(class(e), 'containers.Map'));
        assert(e.Count == 0);
        e('x') = 10;
        assert(e.Count == 1 && e('x') == 10);
        """);

    /// <summary>
    /// The one thing that genuinely separates the two collections: a Map is a handle class, so two
    /// names for it are one collection, and a dictionary is a value class, so they are two.
    /// </summary>
    [Fact]
    public Task AMapIsSharedByEveryNameAndADictionaryIsNot() => RunAsserting("""
        m = containers.Map({'a'}, {1});
        alias = m;
        alias('b') = 2;
        assert(m.Count == 2 && isKey(m, 'b'));

        d = dictionary(["a", "b"], [1 2]);
        copy = d;
        copy("c") = 3;
        assert(numEntries(d) == 2);
        assert(numEntries(copy) == 3);
        """);

    [Fact]
    public Task ADictionaryAnswersTheNewerVerbs() => RunAsserting("""
        d = dictionary(["a", "b"], [1 2]);
        assert(strcmp(class(d), 'dictionary'));
        assert(d("a") == 1);
        assert(numEntries(d) == 2);
        assert(isConfigured(d));
        assert(isstring(keys(d)));
        assert(lookup(d, "zz", 'FallbackValue', -1) == -1);
        insert(d, "c", 3);
        assert(d("c") == 3);
        e = entries(d);
        assert(numel(e) == 3 && strcmp(e{1}.Key, 'a'));
        """);

    [Fact]
    public async Task AMissingKeyIsRefusedByName()
    {
        string message = await RunRefusing("""
            m = containers.Map({'a'}, {1});
            m('nope');
            """);

        Assert.Contains("has no key 'nope'", message, StringComparison.Ordinal);
    }

    // --- The JGS surface is untouched -----------------------------------------------------------------

    /// <summary>
    /// The freeze gate. Every name this milestone adds is new, so for JGS the whole of it is a pure
    /// addition — which its freeze allows. The two exceptions are the names whose <em>meaning</em>
    /// moved: <c>seconds</c> answered with its own argument and <c>datetime</c> with a char row of
    /// the current moment, and a JGS script that called either must go on getting that.
    /// </summary>
    [Fact]
    public async Task JgsKeepsItsOwnMeaningForTheTwoNamesThatAlreadyExisted()
    {
        var engine = new JgsScriptEngine();
        await using IScriptSession session = Assert.IsAssignableFrom<IScriptRepl>(engine)
            .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

        ScriptRunResult result = await session.ExecuteAsync("""
            let s = seconds(90);
            assert(s == 90);
            assert(!isduration(s));
            let d = datetime();
            assert(length(d) > 0);
            """, sourceId: "", CancellationToken.None);

        Assert.True(result.Success, result.Message + _output.ErrorText);
    }
}
