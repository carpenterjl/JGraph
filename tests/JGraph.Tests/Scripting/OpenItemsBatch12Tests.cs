using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Ticks;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Serialization;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Open items 96, 97 and 98 (ADR 0225), where the parity fixture cannot reach: the colour a spec
/// letter makes in every dialect, the tick formatter and exponent under the model, a saved .NET tick
/// format, the exponent's place in a saved figure, and the comma-separated lists the fixture's
/// eval-wrapped statements cannot write as statements.
/// </summary>
[Collection("JG facade")]
public class OpenItemsBatch12Tests : IDisposable
{
    private readonly RecordingScriptOutput _output = new();

    public OpenItemsBatch12Tests() => JG.Reset();

    public void Dispose()
    {
        JgsHandleRegistry.Clear();
        JG.Reset();
    }

    private Task<ScriptRunResult> RunMatlab(string code) =>
        new MatlabScriptEngine().RunAsync(code, new ScriptContext(_output, static (_, _) => { }), default);

    private async Task Runs(string code)
    {
        ScriptRunResult result = await RunMatlab(code);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    [Fact]
    public void TheLetterGIsFullGreenInEveryDialect()
    {
        Assert.Equal(Color.FromRgb(0, 255, 0), LineSpec.Parse("g").Color);
        Assert.Equal(Color.FromRgb(0, 255, 0), LineSpec.Parse("--go").Color);

        // The model's own named colour is still the web's, for the code that asks for it by name.
        Assert.Equal(Color.FromRgb(0, 128, 0), Colors.Green);
    }

    [Theory]
    [InlineData(1500.0, "$%,.2f", "$1,500.00")]
    [InlineData(2.0, "%+g", "+2")]
    [InlineData(2.0, "%5.1f", "  2.0")]
    [InlineData(2.0, "%-5.1f|", "2.0  |")]
    [InlineData(2.0, "%05.1f", "002.0")]
    [InlineData(3.0, "%g%%", "3%")]
    [InlineData(45.0, "%g\\x00B0", "45°")]
    [InlineData(1500.0, "%.1e", "1.5\\times10^{3}")]
    [InlineData(0.00025, "%.2e", "2.50\\times10^{-4}")]
    [InlineData(1234567.0, "%g", "1.23457e+06")]
    public void APrintfTickFormatIsWrittenAsR2025bWritesIt(double value, string format, string written)
    {
        Assert.True(TickFormats.IsPrintf(format));
        Assert.Equal(written, TickFormats.Printf(value, format));
    }

    [Theory]
    [InlineData("0.00")]
    [InlineData("0.####%")]
    [InlineData("$#,##0.00")]
    public void ASavedDotNetFormatIsStillHonoured(string format)
    {
        Assert.False(TickFormats.IsPrintf(format));
        Assert.Equal(0.5.ToString(format, System.Globalization.CultureInfo.CurrentCulture), TickFormats.Apply(0.5, format));
    }

    [Theory]
    [InlineData("%g", TickFormatProblem.None)]
    [InlineData("x = %.2f m", TickFormatProblem.None)]
    [InlineData("%s", TickFormatProblem.UnsupportedType)]
    [InlineData("hello", TickFormatProblem.Invalid)]
    [InlineData("%", TickFormatProblem.Invalid)]
    [InlineData("%g and %g", TickFormatProblem.Invalid)]
    public void AFormatIsCheckedAsR2025bChecksIt(string format, TickFormatProblem problem)
    {
        Assert.Equal(problem, TickFormats.Check(format));
    }

    [Fact]
    public void AutomaticLabelsTakeFourDecimalsOrAsManyAsTellTheTicksApart()
    {
        Assert.Equal(["0", "0.3333", "2"], TickFormats.Labels([0, 1.0 / 3, 2], null));
        Assert.Equal(["1.23456", "1.234565", "1.23457"], TickFormats.Labels([1.23456, 1.234565, 1.23457], null));
        Assert.Equal(["-1", "0", "1"], TickFormats.Labels([-1, -0.0, 1], null));
    }

    [Fact]
    public void ALargeRulerIsLabelledOverItsPowerOfTenAndSaysWhich()
    {
        var axis = new AxisModel(AxisOrientation.Horizontal, AxisPosition.Bottom) { Range = new DataRange(0, 50000) };
        TickSet ticks = TickGenerators.For(axis).Generate(axis.Range, axis.TargetMajorTickCount, axis.TickLabelFormat);
        Assert.Equal(4, ticks.Exponent);
        Assert.Equal("5", ticks.MajorTicks[^1].Label);

        axis.Exponent = 0;
        ticks = TickGenerators.For(axis).Generate(axis.Range, axis.TargetMajorTickCount, axis.TickLabelFormat);
        Assert.Equal(0, ticks.Exponent);
        Assert.Equal("50000", ticks.MajorTicks[^1].Label);

        // A log ruler writes decades as powers instead, and labels it was given are its own.
        var log = new AxisModel(AxisOrientation.Vertical, AxisPosition.Left) { Scale = AxisScaleType.Logarithmic, Range = new DataRange(1, 1e5) };
        TickSet decades = TickGenerators.For(log).Generate(log.Range, 6, null);
        Assert.Equal(0, decades.Exponent);
        Assert.Equal("10^{5}", decades.MajorTicks[^1].Label);
    }

    [Fact]
    public void AChosenExponentIsKeptWithTheFigure()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.PrimaryXAxis.Exponent = 3;
        axes.PrimaryXAxis.TickLabelFormat = "%.1f";

        AxisModel back = GraphFormat.Deserialize(GraphFormat.Serialize(figure)).Axes[0].PrimaryXAxis;
        Assert.Equal(3, back.Exponent);
        Assert.Equal("%.1f", back.TickLabelFormat);
        Assert.Null(GraphFormat.Deserialize(GraphFormat.Serialize(figure)).Axes[0].PrimaryYAxis.Exponent);
    }

    [Fact]
    public async Task AListOfNoneWrittenAsAStatementShowsNothingAndKeepsAns()
    {
        await Runs("""
            s = struct('f', {});
            ans = 7;
            s.f
            c = {};
            c{:}
            assert(ans == 7);
            u = struct('f', {1, 2});
            u.f;
            assert(ans == 2);
            """);
        Assert.DoesNotContain("ans = 7", _output.NormalLines);
    }

    [Fact]
    public async Task ATwoSubscriptWriteThroughAListStillGrowsTheArray()
    {
        await Runs("""
            idx = {3, 4};
            A = zeros(2);
            A(idx{:}) = 9;
            assert(isequal(size(A), [3 4]));
            assert(A(3, 4) == 9);
            B = magic(3);
            B(idx{1}, :) = 1;
            assert(isequal(B(3, :), [1 1 1]));
            """);
    }
}
