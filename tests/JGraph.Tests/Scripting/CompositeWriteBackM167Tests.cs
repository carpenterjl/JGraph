using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), ninth sub-stage: composite writes store back. A write whose path passes through
/// a computed level — a graphics property (<c>p.YData(2) = 9</c>, appendix A #95–#99, #132, #133,
/// #150), a <c>datetime</c> component or property (<c>e.Day(2) = 15</c>, #126, #135), a dictionary's
/// cell value under braces (#136) — is get, modify, set; a handle object held in a struct or a cell
/// (#137–#140) and a value object's property reached through a container are written where they are.
/// </summary>
/// <remarks>
/// The parity fixture <c>composite_write_back</c> holds R2025b's answers; these pin the roads the
/// sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class CompositeWriteBackM167Tests : IDisposable
{
    private const string Helpers = """

        function z = bump_prop(p)
        p.YData(1) = 7;
        z = 0;
        end

        function k = bump_k()
        global cw_bumps
        cw_bumps = cw_bumps + 1;
        k = 2;
        end
        """;

    /// <summary>The parity fixtures' helpers folder, for <c>HandleHolder</c> and <c>ValueBox</c>.</summary>
    private static readonly string Helpers_ = Path.Combine(AppContext.BaseDirectory, "MatlabParity", "fixtures", "helpers");

    private readonly RecordingScriptOutput _output = new();

    public CompositeWriteBackM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    /// <summary>Runs the script the way a fixture runs: the helpers folder on the function path.</summary>
    private ScriptRunResult Run(string code)
    {
        var context = new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles());
        return JgsRunner.Run(code + Helpers, context, default, sourceId: "", hook: null, JgsDialect.Matlab, searchFolders: [Helpers_]);
    }

    private string RunAndRead(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    private string RunExpectingError(string code)
    {
        ScriptRunResult result = Run(code);
        Assert.False(result.Success);
        return result.Message ?? "";
    }

    private const string Line = "f = figure('Visible', 'off'); p = plot([1 2 3]); ";

    [Theory]
    [InlineData("p.YData(2) = 9;", "[1 9 3] [1 9 3]")]
    [InlineData("p.YData(1, 3) = 7;", "[1 2 7] [1 2 7]")]
    [InlineData("p.YData(1:2) = [8 9];", "[8 9 3] [8 9 3]")]
    [InlineData("p.YData(logical([1 0 1])) = 0;", "[0 2 0] [0 2 0]")]
    [InlineData("p.YData(end) = 5;", "[1 2 5] [1 2 5]")]
    [InlineData("p.YData(2) = p.YData(2) + 10;", "[1 12 3] [1 12 3]")]
    [InlineData("p.YData([2 3 1]) = p.YData;", "[3 1 2] [3 1 2]")]
    [InlineData("nm = 'YData'; p.(nm)(2) = 9;", "[1 9 3] [1 9 3]")]
    public void AnIndexedWriteIntoAGraphicsPropertyStoresBackThroughItsSetter(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead(
            $"{Line}{statement} fprintf('%s %s\\n', mat2str(p.YData), mat2str(get(p, 'YData'))); close(f);"));
    }

    [Theory]
    [InlineData("p.YData(end + 1) = 4;", "[1 2 3 4] [1 2 3 4]")]
    [InlineData("p.YData(2) = [];", "[1 3] [1 2]")]
    [InlineData("p.YData = [1 2 3 50];", "[1 2 3 50] [1 2 3 4]")]
    public void GrowthAndDeletionThroughYDataCountThePositionsOutAgain(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead(
            $"{Line}{statement} fprintf('%s %s\\n', mat2str(p.YData), mat2str(p.XData)); close(f);"));
    }

    [Fact]
    public void ChosenPositionsAreNotCountedOutAgain()
    {
        string message = RunExpectingError(
            "f = figure('Visible', 'off'); p = plot([10 20 30], [1 2 3]); p.YData(end + 1) = 4;");
        Assert.Contains("YData has 4 values where the series has 3", message);
    }

    [Theory]
    [InlineData("c = {p}; c{1}.YData(1) = 0;", "[0 2 3]")]
    [InlineData("st.h = p; st.h.YData(1) = 0;", "[0 2 3]")]
    [InlineData("ax = gca; ch = ax.Children; ch(1).YData(1) = 42;", "[42 2 3]")]
    [InlineData("r = p.YData + bump_prop(p);", "[7 2 3]")]
    public void TheHandleIsFoundWhereverItIsHeld(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead($"{Line}{statement} disp(mat2str(p.YData)); close(f);"));
    }

    [Fact]
    public void OneHandleOutOfAnArrayIsWrittenAlone()
    {
        string text = RunAndRead("""
            f = figure('Visible', 'off');
            p1 = plot([1 2 3]);
            hold on
            p2 = plot([4 5 6]);
            h = [p1 p2];
            h(2).YData(3) = 60;
            fprintf('%s %s\n', mat2str(p1.YData), mat2str(p2.YData));
            close(f);
            """);
        Assert.Equal("[1 2 3] [4 5 60]", text);
    }

    [Theory]
    [InlineData("ax = gca; ax.XLim = [0 10]; ax.XLim(2) = 5; r = ax.XLim;", "[0 5]")]
    [InlineData("f.Position = [10 20 300 200]; f.Position(3) = 321; r = f.Position(3);", "321")]
    [InlineData("p.Color = [0 0 0]; p.Color(1) = 1; r = p.Color;", "[1 0 0]")]
    [InlineData("p.LineWidth(1) = 3; r = p.LineWidth;", "3")]
    [InlineData("t = text(0, 0, 'ab'); t.String(1) = 'z'; r = double(t.String);", "[122 98]")]
    [InlineData("im = imagesc(magic(3)); im.CData(2, 2) = 0; r = [im.CData(2, 2) size(im.CData)];", "[0 3 3]")]
    public void AxesFigureAndOtherPropertiesTakeTheSameRoad(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead($"{Line}{statement} disp(mat2str(r)); close(f);"));
    }

    [Fact]
    public void TheGetterRunsAfterTheSubscriptsAndTheRightHandSideAndTheSetterOnce()
    {
        string text = RunAndRead("""
            global gp_cw cw_bumps
            cw_bumps = 0;
            f = figure('Visible', 'off');
            gp_cw = plot([1 2 3]);
            gp_cw.YData(bump_k()) = grow_global();
            fprintf('%d %s\n', cw_bumps, mat2str(gp_cw.YData));
            close(f);

            function v = grow_global()
            global gp_cw
            gp_cw.YData = [1 2 3 50];
            v = 9;
            end
            """);
        Assert.Equal("1 [1 9 3 50]", text);
    }

    [Fact]
    public void ARefusedWriteLeavesThePropertyAsItWasAndAnEarlierReadUnchanged()
    {
        string text = RunAndRead("""
            f = figure('Visible', 'off');
            p = plot([1 2 3]);
            y = p.YData;
            try
                p.YData(4:5) = [7 8 9];
            catch
            end
            p.YData(2) = 9;
            fprintf('%s %s\n', mat2str(y), mat2str(p.YData));
            close(f);
            """);
        Assert.Equal("[1 2 3] [1 9 3]", text);
    }

    [Theory]
    [InlineData("e.Day(2) = 15; r = [day(d(2)) day(e(2)) day(e(1))];", "[2 15 1]")]
    [InlineData("e.Year = 2021; r = [year(d(1)) year(e(1))];", "[2020 2021]")]
    [InlineData("e.Year = [2021 2022 2023]; r = year(e);", "[2021 2022 2023]")]
    [InlineData("e.Month(1) = 13; r = [year(e(1)) month(e(1))];", "[2021 1]")]
    [InlineData("e.Hour(2) = 5; e.Minute(2) = 30; e.Second(2) = 15.5; r = [hour(e(2)) minute(e(2)) second(e(2))];", "[5 30 15.5]")]
    [InlineData("e.Day(2) = 15; r = e.Day;", "[1 15 3]")]
    [InlineData("e(2) = NaT; e.Day = 5; r = [sum(isnat(e)) day(e(1))];", "[1 5]")]
    [InlineData("st.t = d; st.t.Year = 1999; r = [year(d(1)) year(st.t(1))];", "[2020 1999]")]
    [InlineData("c = {d}; c{1}.Day(2) = 15; r = [day(c{1}(2)) day(d(2))];", "[15 2]")]
    public void ADatetimeComponentIsWrittenThroughAndTheAliasKeepsItsMoments(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead(
            $"d = datetime(2020, 1, 1) + days(0:2); e = d; {statement} disp(mat2str(r));"));
    }

    [Fact]
    public void AZonedDatetimeKeepsItsZoneThroughAComponentWrite()
    {
        Assert.Equal("5 UTC", RunAndRead(
            "z = datetime(2020, 1, 1, 'TimeZone', 'UTC'); z.Hour = 5; fprintf('%d %s\\n', hour(z), z.TimeZone);"));
    }

    [Fact]
    public void AFormatWrittenThroughACellLeavesTheOtherCellAlone()
    {
        Assert.Equal("dd-MMM-uuuu/yyyy", RunAndRead(
            "c = {datetime(2020, 1, 1)}; d = c; d{1}.Format = 'yyyy'; fprintf('%s/%s\\n', c{1}.Format, d{1}.Format);"));
    }

    [Theory]
    [InlineData("e.Day = [1 2];", "Assignment to the 'Day' property of a datetime array may not change the size of the property.")]
    [InlineData("e.Day(4) = 1;", "Assignment to the 'Day' property of a datetime array may not change the size of the property.")]
    [InlineData("h = hours(1); h.Day = 1;", "Unrecognized property: 'Day'.")]
    public void AComponentWriteThatChangesTheSizeOrNamesNoPropertyIsRefusedInMatlabsWords(string statement, string expected)
    {
        Assert.Contains(expected, RunExpectingError($"e = datetime(2020, 1, 1) + days(0:2); {statement}"));
    }

    [Theory]
    [InlineData("t.h.data(2) = 9; r = st.h.data;", "[1 9 3]")]
    [InlineData("t.h.data = [4 5 6]; r = st.h.data;", "[4 5 6]")]
    [InlineData("t.h.bump(); r = st.h.data;", "[7 2 3]")]
    [InlineData("t.h.data(2) = 9; r = [strcmp(class(t.h), 'HandleHolder') strcmp(class(st.h), 'HandleHolder')];", "[true true]")]
    [InlineData("r = [t.h == st.h, HandleHolder() == HandleHolder(), t.h ~= st.h];", "[true false false]")]
    public void AHandleObjectHeldInAStructIsTheOneObjectEveryCopyHolds(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead(
            $"st.h = HandleHolder(); st.h.data = [1 2 3]; t = st; {statement} disp(mat2str(r));"));
    }

    [Theory]
    [InlineData("c = {HandleHolder()}; c{1}.data = [1 2 3]; d = c; d{1}.data(2) = 9; r = c{1}.data;", "[1 9 3]")]
    [InlineData("st.c = {HandleHolder()}; st.c{1}.data = [1 2 3]; t = st; t.c{1}.data(2) = 9; r = st.c{1}.data;", "[1 9 3]")]
    [InlineData("a = HandleHolder(); b = HandleHolder(); a.data = b; a.data.data = [1 2]; a.data.data(2) = 7; r = b.data;", "[1 7]")]
    public void AHandleObjectIsFoundAlongAnyEntryPath(string statements, string expected)
    {
        Assert.Equal(expected, RunAndRead($"{statements} disp(mat2str(r));"));
    }

    [Theory]
    [InlineData("c = {v}; c{1}.p = 9; r = [v.p c{1}.p];", "[1 2 3 4 5 9]")]
    [InlineData("c = {v}; c{1}.p(2) = 9; r = [v.p c{1}.p];", "[1 2 3 4 5 1 9 3 4 5]")]
    [InlineData("st.f = v; st.f.p = 9; r = [v.p st.f.p strcmp(class(st.f), 'ValueBox')];", "[1 2 3 4 5 9 1]")]
    [InlineData("o = ValueBox(); o.p = struct('f', 1:5); o.p.f = 9; o.p.g = 1; r = [o.p.f o.p.g numel(fieldnames(o.p))];", "[9 1 2]")]
    [InlineData("o = ValueBox(); o.p = struct('f', num2cell(1:5)); o.p(end + 1).f = 9; r = [numel(o.p) o.p(6).f];", "[6 9]")]
    public void AValueObjectsPropertyIsWrittenWhereTheObjectIsHeldAndTheOriginalKeepsItsValue(string statements, string expected)
    {
        Assert.Equal(expected, RunAndRead($"v = ValueBox(); v.p = 1:5; {statements} disp(mat2str(r));"));
    }

    [Theory]
    [InlineData("r = d{\"k\"};", "[1 2]")]
    [InlineData("e = d; e{\"k\"}(1) = 9; r = [d{\"k\"} e{\"k\"}];", "[1 2 9 2]")]
    [InlineData("d{\"k\"} = [7 8 9]; r = [d{\"k\"} strcmp(class(d(\"k\")), 'cell')];", "[7 8 9 1]")]
    [InlineData("d{\"n\"} = 5; r = [d{\"n\"} numEntries(d)];", "[5 2]")]
    [InlineData("d2 = dictionary([\"a\" \"b\"], {1, [2 3]}); r = [d2{\"a\"} d2{\"b\"}];", "[1 2 3]")]
    public void ADictionaryOfCellValuesOpensUnderBraces(string statement, string expected)
    {
        Assert.Equal(expected, RunAndRead($"d = dictionary(\"k\", {{[1 2]}}); {statement} disp(mat2str(r));"));
    }

    [Fact]
    public void BracesOnADictionaryOfPlainValuesAreRefusedInMatlabsWords()
    {
        Assert.Contains(
            "Using curly braces on a dictionary with 'double' value type is not supported. The dictionary value type must be 'cell'.",
            RunExpectingError("d = dictionary(\"k\", 5); x = d{\"k\"};"));
    }
}
