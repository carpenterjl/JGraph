using System.Globalization;
using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 316 and 212 (ADR 0359): R2025b's recorded month and weekday names per locale,
/// cellstr with a format, num2str's complex and formatted columns as num2str.m lays them out, and
/// mps.json's 64-bit maxima. Every expectation was recorded from R2025b (the parity fixtures
/// oi_datetime_locale_names, oi_num2str_forms and k14_mps_json carry the rest).
/// </summary>
[Collection("JG facade")]
public class LocaleNamesNum2StrTests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public LocaleNamesNum2StrTests() => JG.Reset();

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

    [Fact]
    public void TheTableHoldsR2025bsNamesWhereDotNetsDiffer()
    {
        JgsLocaleNames.Names? fr = JgsLocaleNames.For(CultureInfo.GetCultureInfo("fr-FR"));
        Assert.NotNull(fr);
        Assert.Equal("janv", fr.MonthsShort[0]);
        Assert.Equal("D", fr.DaysNarrow[0]);

        JgsLocaleNames.Names? ru = JgsLocaleNames.For(CultureInfo.GetCultureInfo("ru-RU"));
        Assert.NotNull(ru);
        Assert.Equal("ноября", ru.MonthsWide[10]);

        JgsLocaleNames.Names? invariant = JgsLocaleNames.For(CultureInfo.InvariantCulture);
        Assert.NotNull(invariant);
        Assert.Equal("N", invariant.MonthsNarrow[10]);
    }

    [Fact]
    public async Task ALocaleFormatsWithR2025bsNamesAndCellstrTakesAFormat()
    {
        ScriptRunResult result = await RunMatlab(
            "t = datetime(2026, 1, 15); a = char(t, 'dd-MMM-uuuu', 'fr_FR'); "
            + "c = cellstr([t t], 'MMMMM', 'de_DE'); b = c{2}; n = char(t, 'M'); y = char(t, 'y');");
        Assert.True(result.Success, result.Message);
        Assert.Equal("15-janv-2026", Text(result, "a"));
        Assert.Equal("J", Text(result, "b"));
        Assert.Equal("1", Text(result, "n"));
        Assert.Equal("2026", Text(result, "y"));
    }

    [Fact]
    public async Task AnUnknownLocaleIsR2025bsRefusal()
    {
        ScriptRunResult result = await RunMatlab(
            "try, string(datetime(2026, 1, 15), 'MMM', 'french'); m = 'none'; catch e, m = e.identifier; end");
        Assert.True(result.Success, result.Message);
        Assert.Equal("MATLAB:datetime:InvalidLocaleString", Text(result, "m"));
    }

    [Fact]
    public async Task Num2strLaysOutComplexAndFormattedColumnsAsNum2strM()
    {
        ScriptRunResult result = await RunMatlab(
            "a = num2str([0.7+0.1i 0.3-0.2i], 17); b = num2str(5, '%10.2f'); c = num2str([1; 10], '%d'); "
            + "c1 = c(1, :); d = num2str(int8([1 -2 3]), 17); e = num2str(1+2i, '%10.5f');");
        Assert.True(result.Success, result.Message);
        Assert.Equal("0.69999999999999996+0.10000000000000001i         0.29999999999999999-0.20000000000000001i", Text(result, "a"));
        Assert.Equal("5.00", Text(result, "b"));
        Assert.Equal(" 1", Text(result, "c1"));
        Assert.Equal("1 -2  3", Text(result, "d"));
        Assert.Equal("1.00000+2.00000i", Text(result, "e"));
    }

    [Fact]
    public async Task MpsJsonWritesTheSixtyFourBitMaximaAsR2025bDoes()
    {
        ScriptRunResult result = await RunMatlab(
            "a = mps.json.encode(intmax('int64')); b = mps.json.encode(intmax('uint64')); "
            + "s = mps.json.decode(['{\"' repmat('a', 1, 2049) '\":1}']); n = numel(fieldnames(s));");
        Assert.True(result.Success, result.Message);
        Assert.Equal("{\"mwdata\":[9223372036854775807],\"mwsize\":[1,1],\"mwtype\":\"int64\"}", Text(result, "a"));
        Assert.Equal("{\"mwdata\":[18446744073709551615],\"mwsize\":[1,1],\"mwtype\":\"uint64\"}", Text(result, "b"));
    }
}
