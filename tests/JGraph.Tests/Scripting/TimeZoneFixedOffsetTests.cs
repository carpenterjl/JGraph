using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 209: a fixed-offset <c>TimeZone</c> read and named as R2025b reads and names it, and
/// the zone format letters printed as R2025b prints them. Every expectation was recorded from
/// R2025b (probe209, probe209b, probe209c); the parity fixture oi_timezone_fixed_offsets carries the
/// whole table.
/// </summary>
[Collection("JG facade")]
public class TimeZoneFixedOffsetTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public TimeZoneFixedOffsetTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(
            code, new ScriptContext(_output, static (_, _) => { }), default);

    private static string Text(ScriptRunResult result, string name)
    {
        object? raw = Assert.Single(result.Variables, v => v.Name == name).RawValue;
        return raw switch
        {
            string text => text,
            string[] { Length: 1 } texts => texts[0],
            _ => Assert.IsType<string>(raw),
        };
    }

    [Theory]
    [InlineData("+02:00", "+02:00")]
    [InlineData("+02:00:00", "+02:00")]
    [InlineData("+0200", "+02:00")]
    [InlineData("+020030", "+02:00:30")]
    [InlineData("+020", "+00:20")]
    [InlineData("+02000", "+00:20")]
    [InlineData("+2", "+02:00")]
    [InlineData("-00:00", "+00:00")]
    [InlineData("-00:00:30", "-00:00:30")]
    [InlineData("Z", "+00:00")]
    [InlineData("z", "+00:00")]
    [InlineData("utc", "UTC")]
    [InlineData("GMT", "UTC")]
    [InlineData("gmt", "+00:00")]
    [InlineData("UT", "+00:00")]
    [InlineData("UTC+2", "+02:00")]
    [InlineData("UTC+2:00:30", "+02:00:30")]
    [InlineData("UTC+00:00", "+00:00")]
    [InlineData("UT+0200", "+02:00")]
    [InlineData("GMT+020", "+00:20")]
    [InlineData("GMT+00", "UTC")]
    [InlineData("gmt-0", "UTC")]
    [InlineData("Etc/GMT-2", "+02:00")]
    [InlineData("Etc/GMT+5", "-05:00")]
    [InlineData("Etc/UTC", "+00:00")]
    [InlineData("+23:59:59", "+23:59:59")]
    [InlineData("-023015", "-02:30:15")]
    public void ASpellingIsNamedAsR2025bNamesIt(string given, string name)
    {
        Assert.Equal(ZoneReading.Fixed, JgsTimeZones.Read(given, out string read, out _, out _));
        Assert.Equal(name, read);
    }

    [Theory]
    [InlineData("+2:00")]
    [InlineData("+02:0")]
    [InlineData("+02:00:0")]
    [InlineData("+02:00:00.5")]
    [InlineData(" +02:00")]
    [InlineData("+02:00 ")]
    [InlineData("+24:00")]
    [InlineData("+02:60")]
    [InlineData("+0200000")]
    [InlineData("+096")]
    [InlineData("UTC+0200")]
    [InlineData("UTC+")]
    [InlineData("UTC2")]
    [InlineData("GMT+24")]
    [InlineData("GMT+2:0")]
    [InlineData("Etc/GMT+13")]
    [InlineData("Etc/GMT-02")]
    [InlineData("++2")]
    [InlineData(" ")]
    public void ASpellingR2025bCannotReadIsRefused(string given) =>
        Assert.Equal(ZoneReading.Refused, JgsTimeZones.Read(given, out _, out _, out _));

    [Theory]
    [InlineData("GMT+2", "GMT+02:00")]
    [InlineData("gmt-0530", "GMT-05:30")]
    [InlineData("GMT+0", "GMT+0")]
    [InlineData("Zulu", "Zulu")]
    public void AGmtPrefixOrAnAliasIsWarnedAboutUnderItsName(string given, string alias)
    {
        Assert.Equal(ZoneReading.Fixed, JgsTimeZones.Read(given, out _, out _, out string? warnAs));
        Assert.Equal(alias, warnAs);
    }

    [Fact]
    public void ANamedZoneIsLeftToTheDatabase() =>
        Assert.Equal(ZoneReading.NotFixed, JgsTimeZones.Read("America/New_York", out _, out _, out _));

    [Theory]
    [InlineData(2 * 3600, 0, 1, false, false, "+02")]
    [InlineData(-(5 * 3600 + 1800), 0, 1, false, false, "-0530")]
    [InlineData(-30, 0, 1, false, false, "+00")]
    [InlineData(-30, 1, 2, false, false, "-000030")]
    [InlineData(-30, 0, 1, false, true, "Z")]
    [InlineData(30, 1, 2, true, true, "+00:00:30")]
    [InlineData(2 * 3600 + 30, 1, 2, true, false, "+02:00:30")]
    public void TheIsoFormsDropTrailingZeroFieldsAndAMinusOnZero(
        int seconds, int minField, int maxField, bool colons, bool utcZ, string expected) =>
        Assert.Equal(expected, JgsTimeZones.Iso(TimeSpan.FromSeconds(seconds), minField, maxField, colons, utcZ));

    [Fact]
    public async Task TheConstructorAndTheSetterNameTheZoneAlike()
    {
        ScriptRunResult result = await RunMatlab(
            "a = datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', '+0200').TimeZone; "
            + "t = datetime(2026, 1, 2, 3, 4, 5); t.TimeZone = '+020030'; b = t.TimeZone; "
            + "u = datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', 'UTC'); u.TimeZone = '+020030'; "
            + "u.Format = 'HH:mm:ss'; c = char(u); "
            + "d = datetime(2026, 1, 2, 'TimeZone', hours(-5.5)).TimeZone;");
        Assert.True(result.Success, result.Message);
        Assert.Equal("+02:00", Text(result, "a"));
        Assert.Equal("+02:00:30", Text(result, "b"));
        Assert.Equal("05:04:35", Text(result, "c"));
        Assert.Equal("-05:30", Text(result, "d"));
    }

    [Fact]
    public async Task AnUnreadableSpellingIsRefusedWithR2025bsIdentifier()
    {
        ScriptRunResult result = await RunMatlab(
            "try, datetime(2026, 1, 2, 'TimeZone', '+2:00'); catch e, a = e.identifier; b = e.message; end");
        Assert.True(result.Success, result.Message);
        Assert.Equal("MATLAB:datetime:UnknownTimeZone", Text(result, "a"));
        Assert.StartsWith("Unable to create the specified time zone: '+2:00'.", Text(result, "b"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGmtOffsetWarnsThatItFollowsNoDaylightSaving()
    {
        ScriptRunResult result = await RunMatlab(
            "lastwarn(''); t = datetime(2026, 1, 2, 'TimeZone', 'GMT+2'); [m, id] = lastwarn; a = t.TimeZone;");
        Assert.True(result.Success, result.Message);
        Assert.Equal("+02:00", Text(result, "a"));
        Assert.Equal("MATLAB:datetime:NonstandardTimeZoneFixed", Text(result, "id"));
        Assert.StartsWith("'GMT+02:00' specifies a time zone with a fixed offset from UTC, +02:00.", Text(result, "m"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheZoneLettersPrintIcusFormsAndAStarWhenUnzoned()
    {
        ScriptRunResult result = await RunMatlab(
            "t = datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', '-02:30:15'); "
            + "t.Format = 'x|xx|xxx|xxxx|xxxxx|Z|ZZZZ|ZZZZZ|z'; a = char(t); "
            + "u = datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', 'UTC'); u.Format = 'X|xx|ZZZZ|z'; b = char(u); "
            + "v = datetime(2026, 1, 2, 3, 4, 5, 'Format', 'x|ZZZZ|z'); c = char(v);");
        Assert.True(result.Success, result.Message);
        Assert.Equal("-0230|-0230|-02:30|-023015|-02:30:15|-023015|UTC-02:30:15|-02:30:15|UTC-2:30:15", Text(result, "a"));
        Assert.Equal("Z|+0000|UTC|UTC", Text(result, "b"));
        Assert.Equal("*|****|*", Text(result, "c"));
    }

    [Fact]
    public async Task TzoffsetIsHoursAndMinutesWithItsDaylightPart()
    {
        ScriptRunResult result = await RunMatlab(
            "[a, b] = tzoffset(datetime(2026, 1, 2, 'TimeZone', '+02:00:30')); x = char(a); y = char(b); f = a.Format; "
            + "[c, d] = tzoffset(datetime(2026, 7, 2, 'TimeZone', 'America/New_York')); z = [char(c) ' ' char(d)];");
        Assert.True(result.Success, result.Message);
        Assert.Equal("02:00", Text(result, "x"));
        Assert.Equal("00:00", Text(result, "y"));
        Assert.Equal("hh:mm", Text(result, "f"));
        Assert.Equal("-04:00 01:00", Text(result, "z"));
    }

    [Fact]
    public async Task TheFormatLettersR2025bRefusesAreRefusedWhenTheFormatIsSet()
    {
        ScriptRunResult result = await RunMatlab(
            "t = datetime(2026, 1, 2, 'TimeZone', '+02:00'); "
            + "try, t.Format = 'HH:mm O'; catch e, a = e.identifier; end; "
            + "try, datetime(2026, 1, 2, 'TimeZone', '+02:00', 'Format', 'HH:mm zzzz'); catch e, b = e.identifier; end; "
            + "t.Format = '''O'' x'; c = char(t);");
        Assert.True(result.Success, result.Message);
        Assert.Equal("MATLAB:datetime:UnsupportedSymbol", Text(result, "a"));
        Assert.Equal("MATLAB:datetime:UnsupportedIdentifier", Text(result, "b"));
        Assert.Equal("O +02", Text(result, "c"));
    }
}
