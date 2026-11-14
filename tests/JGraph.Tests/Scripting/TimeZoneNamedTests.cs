using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open item 311: a named <c>TimeZone</c> matched with its case, stored under R2025b's canonical name
/// (with its warning for an alias), abbreviated by its <c>z</c> letter as R2025b abbreviates it, and
/// <c>'local'</c> stored as the system's zone. Every expectation was recorded from R2025b (probe311,
/// tools/timezones/record_zone_names.m); the parity fixture oi_timezone_named carries the table.
/// </summary>
[Collection("JG facade")]
public class TimeZoneNamedTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public TimeZoneNamedTests() => JG.Reset();

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
    [InlineData("America/New_York", "America/New_York", false)]
    [InlineData("US/Eastern", "America/New_York", true)]
    [InlineData("EST", "America/Panama", true)]
    [InlineData("GB", "Europe/London", true)]
    [InlineData("CET", "Europe/Brussels", true)]
    [InlineData("Asia/Kolkata", "Asia/Calcutta", false)]
    [InlineData("Europe/Kyiv", "Europe/Kiev", false)]
    [InlineData("EST5EDT", "America/New_York", false)]
    public void ANameIsStoredUnderR2025bsName(string given, string stored, bool warns)
    {
        Assert.True(JgsTimeZoneNames.TryLookup(given, out string read, out bool warned));
        Assert.Equal(stored, read);
        Assert.Equal(warns, warned);
    }

    [Theory]
    [InlineData("america/new_york")]
    [InlineData("AMERICA/NEW_YORK")]
    [InlineData("us/eastern")]
    [InlineData("Eastern Standard Time")]
    [InlineData("Factory")]
    public void ANameIsMatchedWithItsCaseAndAWindowsIdIsNotOne(string given) =>
        Assert.False(JgsTimeZoneNames.TryLookup(given, out _, out _));

    /// <summary>
    /// Every stored name resolves to a zone on this machine, save the few newer than the zone data
    /// Windows carries (ADR 0350's divergence); a newer Windows may know them too.
    /// </summary>
    [Fact]
    public void EveryStoredNameResolvesSaveTheKnownFew()
    {
        string[] known = ["America/Ciudad_Juarez", "Antarctica/Troll", "Antarctica/Vostok", "Asia/Urumqi"];
        string[] unresolved = [.. JgsTimeZoneNames.StoredNames.Where(n => !JgsTime.TryResolveZone(n, out _))];
        Assert.All(unresolved, n => Assert.Contains(n, known));
    }

    [Fact]
    public async Task TheZLetterIsTheAbbreviationOrTheOffset()
    {
        ScriptRunResult result = await RunMatlab(
            "a = char(datetime(2026, 1, 15, 12, 0, 0, 'TimeZone', 'America/New_York'), 'z'); "
            + "b = char(datetime(2026, 7, 15, 12, 0, 0, 'TimeZone', 'America/New_York'), 'zzz'); "
            + "c = char(datetime(2026, 1, 15, 'TimeZone', 'Asia/Kolkata'), 'z'); "
            + "d = char(datetime(2026, 1, 15, 'TimeZone', 'Europe/Lisbon'), 'z'); "
            + "e = char(datetime(2026, 7, 15, 'TimeZone', 'Europe/London'), 'z'); "
            + "f = char(datetime(2026, 1, 15, 'TimeZone', 'Europe/London'), 'z');");
        Assert.True(result.Success, result.Message);
        Assert.Equal("EST", Text(result, "a"));
        Assert.Equal("EDT", Text(result, "b"));
        Assert.Equal("UTC+5:30", Text(result, "c"));
        Assert.Equal("UTC", Text(result, "d"));
        Assert.Equal("UTC+1", Text(result, "e"));
        Assert.Equal("GMT", Text(result, "f"));
    }

    [Fact]
    public async Task AnAliasWarnsAndALowerCaseNameIsRefused()
    {
        ScriptRunResult result = await RunMatlab(
            "lastwarn(''); t = datetime(2026, 1, 15, 'TimeZone', 'US/Eastern'); [m, id] = lastwarn; a = t.TimeZone; "
            + "try, datetime(2026, 1, 15, 'TimeZone', 'america/new_york'); catch e, b = e.identifier; end");
        Assert.True(result.Success, result.Message);
        Assert.Equal("America/New_York", Text(result, "a"));
        Assert.Equal("MATLAB:datetime:NonstandardTimeZone", Text(result, "id"));
        Assert.StartsWith("'US/Eastern' does not specify a single time zone unambiguously. It will be treated as 'America/New_York'.", Text(result, "m"), StringComparison.Ordinal);
        Assert.Equal("MATLAB:datetime:UnknownTimeZone", Text(result, "b"));
    }

    [Fact]
    public async Task LocalIsStoredAsTheSystemsZone()
    {
        ScriptRunResult result = await RunMatlab("a = datetime(2026, 1, 15, 'TimeZone', 'local').TimeZone;");
        Assert.True(result.Success, result.Message);
        Assert.Equal(JgsTimeZoneNames.LocalName(), Text(result, "a"));
        Assert.NotEqual("local", Text(result, "a"));
    }

    [Fact]
    public async Task StringAndCharTakeAFormatAndRefuseItsLetters()
    {
        ScriptRunResult result = await RunMatlab(
            "t = datetime(2026, 1, 15, 12, 30, 45, 'TimeZone', 'America/New_York'); "
            + "a = char(string(t, 'yyyy-MM-dd z')); b = char(string(t, 'MMMM', 'fr_FR')); "
            + "try, string(t, 'O'); catch e, c = e.identifier; end; "
            + "try, char(t, 'zzzz'); catch e, d = e.identifier; end; "
            + "try, string(t, 5); catch e, f = e.identifier; end");
        Assert.True(result.Success, result.Message);
        Assert.Equal("2026-01-15 EST", Text(result, "a"));
        Assert.Equal("janvier", Text(result, "b"));
        Assert.Equal("MATLAB:datetime:UnsupportedSymbol", Text(result, "c"));
        Assert.Equal("MATLAB:datetime:UnsupportedIdentifier", Text(result, "d"));
        Assert.Equal("MATLAB:datetime:InvalidFormat", Text(result, "f"));
    }
}
