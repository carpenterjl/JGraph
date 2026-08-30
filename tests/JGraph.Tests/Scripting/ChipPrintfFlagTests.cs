using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The printf flags of <c>sprintf</c> and <c>fprintf</c> — <c>-</c>, <c>+</c>, a space, <c>0</c> and
/// <c>#</c> — which the formatter used to refuse outright, reading the flag itself as the conversion
/// character and answering "does not support the specifier". Every expectation below was measured
/// against MATLAB R2024a rather than reasoned out of C's rules, because MATLAB's printf is close to
/// C's without being it: its integer conversions hand a value they cannot hold over to <c>%e</c>,
/// keeping the flags, and the sign flags then apply to conversions that had ignored them.
/// </summary>
[Collection("JG facade")]
public class ChipPrintfFlagTests : IDisposable
{
    private readonly JgsScriptEngine _engine = new();
    private readonly List<FigureModel> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public ChipPrintfFlagTests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private ScriptRunResult RunMatlab(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Matlab);
    }

    private ScriptRunResult RunJgs(string code)
    {
        var context = new ScriptContext(_output, (_, figure) => _figures.Add(figure), null);
        return JgsRunner.Run(code, context, default, sourceId: "", hook: null, JgsDialect.Jgs);
    }

    /// <summary>What <c>sprintf(<paramref name="call"/>)</c> makes, to the character — trailing spaces included.</summary>
    private string Sprintf(string call)
    {
        _output.Normal.Clear();
        ScriptRunResult result = RunMatlab($"fprintf('%s', sprintf({call}));");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText;
    }

    [Theory]
    // Each flag on its own, and the combinations the report named.
    [InlineData("'%+d', 3", "+3")]
    [InlineData("'%+d', -3", "-3")]
    [InlineData("'%+g', 3", "+3")]
    [InlineData("'%-8.3f|', 1.5", "1.500   |")]
    [InlineData("'% d', 7", " 7")]
    [InlineData("'% d', -7", "-7")]
    [InlineData("'%#o', 8", "010")]
    [InlineData("'%#x', 255", "0xff")]
    [InlineData("'%#X', 255", "0XFF")]
    [InlineData("'%05.1f', 3.14159", "003.1")]
    [InlineData("'%+.3e', pi", "+3.142e+00")]
    [InlineData("'%-+8d|', 42", "+42     |")]

    // The order of the flags is free, and each pair has a winner: '-' beats '0' and '+' beats a space.
    [InlineData("'%+-8d|', 42", "+42     |")]
    [InlineData("'%-08d|', 42", "42      |")]
    [InlineData("'%0-8d|', 42", "42      |")]
    [InlineData("'%-08.2f|', 1.5", "1.50    |")]
    [InlineData("'%+ d', 7", "+7")]
    [InlineData("'% +d', 7", "+7")]
    [InlineData("'% +d', -7", "-7")]
    [InlineData("'%+05d', -42", "-0042")]

    // Zero padding slots in behind the sign, and behind the 0x that '#' writes.
    [InlineData("'%+05d', 42", "+0042")]
    [InlineData("'% 05d', 42", " 0042")]
    [InlineData("'%+010.2f|', -1.5", "-000001.50|")]
    [InlineData("'% 08.2f|', 1.5", " 0001.50|")]
    [InlineData("'%#08x|', 255", "0x0000ff|")]
    [InlineData("'%08.2f', 3.14159", "00003.14")]

    // The sign flags belong to the signed conversions only: %u, %o, %x and %X ignore them outright,
    // and so do %s and %c.
    [InlineData("'%+u', 7", "7")]
    [InlineData("'%+x', 255", "ff")]
    [InlineData("'%+X', 255", "FF")]
    [InlineData("'% x', 255", "ff")]
    [InlineData("'%+o', 8", "10")]
    [InlineData("'%#+o', 8", "010")]
    [InlineData("'%+s', 'ab'", "ab")]
    [InlineData("'%#s', 'ab'", "ab")]
    [InlineData("'% s', 'ab'", "ab")]
    [InlineData("'%+c', 65", "A")]
    [InlineData("'%#d', 5", "5")]

    // Text takes the zero flag, on whichever side it aligns — which is a MATLAB-ism, not C's.
    [InlineData("'%06s|', 'ab'", "0000ab|")]
    [InlineData("'%08s|', 'abc'", "00000abc|")]
    [InlineData("'%-08s|', 'abc'", "abc00000|")]
    [InlineData("'%05c|', 65", "0000A|")]
    [InlineData("'%-5c|', 65", "A    |")]

    // '#' on the base conversions: a leading zero for octal, a 0x for hex, and neither for a zero.
    [InlineData("'%#o', 0", "0")]
    [InlineData("'%#o', 1", "01")]
    [InlineData("'%#o', 7", "07")]
    [InlineData("'%#o', 64", "0100")]
    [InlineData("'%#x', 0", "0")]
    [InlineData("'%#x', 16", "0x10")]
    [InlineData("'%#.4x', 255", "0x00ff")]
    [InlineData("'%#.5x', 255", "0x000ff")]
    [InlineData("'%#.4o', 8", "0010")]
    [InlineData("'%#5.3o|', 8", "  010|")]
    [InlineData("'%#-5.2o|', 8", "010  |")]
    [InlineData("'%#-08x|', 255", "0xff    |")]

    // '#' on a fixed or scientific conversion writes the point even where no decimals follow it.
    [InlineData("'%#f', 1", "1.000000")]
    [InlineData("'%#.0f', 1", "1.")]
    [InlineData("'%#.3f', 1", "1.000")]
    [InlineData("'%#.0e', 1", "1.e+00")]
    [InlineData("'%#.0E', 1", "1.E+00")]
    [InlineData("'%+#.0f', 1", "+1.")]
    [InlineData("'%#08.0f|', 1", "0000001.|")]

    // '#' on %g keeps the trailing zeros as well, and chooses fixed or scientific by C's own rule:
    // fixed while the exponent sits in [-4, significant digits).
    [InlineData("'%#g', 1", "1.00000")]
    [InlineData("'%#g', 1.5", "1.50000")]
    [InlineData("'%#.3g', 1", "1.00")]
    [InlineData("'%#.5g', 1.5", "1.5000")]
    [InlineData("'%#g', 100000", "100000.")]
    [InlineData("'%#g', 1e6", "1.00000e+06")]
    [InlineData("'%#g', 1e-4", "0.000100000")]
    [InlineData("'%#g', 1e-5", "1.00000e-05")]
    [InlineData("'%#G', 1e-5", "1.00000E-05")]
    [InlineData("'%#g', 0", "0.00000")]
    [InlineData("'%+#g', 0", "+0.00000")]
    [InlineData("'%#.3g', 1234", "1.23e+03")]
    [InlineData("'%#.3g', 0.0001", "0.000100")]
    [InlineData("'%#.10g', pi", "3.141592654")]
    [InlineData("'%#.10g', 100", "100.0000000")]
    [InlineData("'%#.0g', 1.5", "2.")]
    [InlineData("'%#.0g', 100", "1.e+02")]
    [InlineData("'%#020.10g|', pi", "0000000003.141592654|")]
    [InlineData("'%-#8.3g|', 1.5", "1.50    |")]

    // A precision on an integer is a minimum digit count, and it turns the zero flag off because it
    // has already said how many digits there are to be.
    [InlineData("'%.3d|', 7", "007|")]
    [InlineData("'%+.3d|', 7", "+007|")]
    [InlineData("'%.10d|', -7", "-0000000007|")]
    [InlineData("'%08.3d|', 7", "     007|")]
    [InlineData("'%+05.2d|', 7", "  +07|")]
    [InlineData("'%.0d|', 0", "|")]
    [InlineData("'%.0d|', 5", "5|")]
    [InlineData("'%#.0o|', 0", "0|")]

    // MATLAB hands a value its integer conversion cannot hold over to %e, keeping every flag, the
    // width and the precision — so '%+u' gains the sign that %u itself had ignored, and a negative
    // reaching an unsigned conversion goes the same way.
    [InlineData("'%d', 2.5", "2.500000e+00")]
    [InlineData("'%+d', 2.5", "+2.500000e+00")]
    [InlineData("'%.2d|', 2.5", "2.50e+00|")]
    [InlineData("'%+8d|', 2.5", "+2.500000e+00|")]
    [InlineData("'%015d', 2.5", "0002.500000e+00")]
    [InlineData("'%+015.2d', 2.5", "+0000002.50e+00")]
    [InlineData("'%-12d|', 2.5", "2.500000e+00|")]
    [InlineData("'%+u', 2.5", "+2.500000e+00")]
    [InlineData("'%#.0x|', 2.4", "2.e+00|")]
    [InlineData("'%+#.0u|', 2.4", "+2.e+00|")]
    [InlineData("'%+x', -255", "-2.550000e+02")]
    [InlineData("'%o', -8", "-8.000000e+00")]
    [InlineData("'%u', -7", "-7.000000e+00")]
    [InlineData("'%d %d', 1.5, 2", "1.500000e+00 2")]
    [InlineData("'%d', 2^63", "9.223372e+18")]
    [InlineData("'%d', -9223372036854775808", "-9223372036854775808")]
    [InlineData("'%x', 2^63", "8000000000000000")]

    // The infinities take a sign flag and NaN takes none, and neither is ever padded with zeros —
    // a leading zero would read as a digit.
    [InlineData("'%+d', Inf", "+Inf")]
    [InlineData("'% d', Inf", " Inf")]
    [InlineData("'%+d', NaN", "NaN")]
    [InlineData("'%+.2f', -Inf", "-Inf")]
    [InlineData("'%05d', Inf", "  Inf")]
    [InlineData("'%05.1f', NaN", "  NaN")]

    // A negative zero keeps its minus through the float conversions and loses it through the
    // integer ones, because the integer conversion has cast it to a whole number first.
    [InlineData("'%+g', -0", "-0")]
    [InlineData("'%+.2f', -0", "-0.00")]
    [InlineData("'%+f', -0.0", "-0.000000")]
    [InlineData("'%+d', -0", "+0")]

    // The rest of the measured table: flags with widths, precisions and several specifiers at once.
    [InlineData("'%+5.1f|', 1.5", " +1.5|")]
    [InlineData("'% .3f', 1.5", " 1.500")]
    [InlineData("'%+f', 0", "+0.000000")]
    [InlineData("'%+e', 0", "+0.000000e+00")]
    [InlineData("'%+.0f', 2.5", "+2")]
    [InlineData("'%+.0f', 0.5", "+0")]
    [InlineData("'%+g', 1e-5", "+1e-05")]
    [InlineData("'%+.3g', 1234567", "+1.23e+06")]
    [InlineData("'%+.15g', 0.1", "+0.1")]
    [InlineData("'%+i', 5", "+5")]
    [InlineData("'%+d %+d', 1, -1", "+1 -1")]
    [InlineData("'%+d and %-4d|', 7, 8", "+7 and 8   |")]
    [InlineData("'%-+.3e|', pi", "+3.142e+00|")]
    [InlineData("'%+8.2f|', -1.5", "   -1.50|")]
    [InlineData("'% 8.2f|', 1.5", "    1.50|")]
    [InlineData("'% -8.2f|', 1.5", " 1.50   |")]
    [InlineData("'%-+05.1f|', 2.5", "+2.5 |")]
    [InlineData("'%+03d', -5", "-05")]
    [InlineData("'%+3d|', 42", "+42|")]
    [InlineData("'%0d', 5", "5")]
    [InlineData("'%-d|', 5", "5|")]
    [InlineData("'%+d%%|', 5", "+5%|")]
    [InlineData("'%#x %#o %+d % d', 255, 8, 3, 3", "0xff 010 +3  3")]
    public void FlagsMatchMatlab(string call, string expected) => Assert.Equal(expected, Sprintf(call));

    [Fact]
    public void FlagsSurviveTheFormatRecycling()
    {
        // A format shorter than the argument list is reapplied until the values run out, and the
        // flags come round with it. The matrix goes in column by column, as MATLAB reads it.
        Assert.Equal("+1,+2,+3,", Sprintf("'%+d,', 1:3"));
        Assert.Equal(" +1.5| -2.2|", Sprintf("'%+5.1f|', [1.5 -2.25]"));
        Assert.Equal("+1.500 -1.500", Sprintf("'%+.3f %+.3f', [1.5 -1.5]"));
        Assert.Equal(" +1.50| +3.25| -2.50| -4.75|", Sprintf("'%+6.2f|%+6.2f|', [1.5 -2.5; 3.25 -4.75]"));
    }

    [Fact]
    public void FprintfTakesTheSameFlags()
    {
        // fprintf shares the formatter, so one fix serves both names.
        _output.Normal.Clear();
        ScriptRunResult result = RunMatlab("fprintf('[%+d][%-8.3f][% d][%#x][%08.2f]', 3, 1.5, 7, 255, 3.14159);");

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("[+3][1.500   ][ 7][0xff][00003.14]", _output.NormalText);
    }

    [Fact]
    public void StarTakesAWidthOrAPrecisionBesideTheFlags()
    {
        // The '*' forms were already here; what is new is that a flag may stand in front of one.
        Assert.Equal("    42|", Sprintf("'%*d|', 6, 42"));
        Assert.Equal("42    |", Sprintf("'%-*d|', 6, 42"));
        Assert.Equal("3.142|", Sprintf("'%.*f|', 3, pi"));
        Assert.Equal("   +42|", Sprintf("'%+*d|', 6, 42"));
        Assert.Equal("+42   |", Sprintf("'%+-*d|', 6, 42"));
    }

    [Fact]
    public void ARepeatedFlagIsAnError()
    {
        // MATLAB abandons the rest of the format without a word here. This formatter says what is
        // wrong instead, which is what it does for every other misuse of a specifier.
        foreach (string bad in new[] { "'%++d', 5", "'%##d', 5", "'%  d', 5", "'%--d', 5", "'%00d', 5" })
        {
            _output.Normal.Clear();
            ScriptRunResult result = RunMatlab($"x = sprintf({bad});");

            Assert.False(result.Success);
            Assert.Contains("repeats the", Assert.Single(result.Diagnostics).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AFlagAfterTheWidthIsStillAnError()
    {
        // The flags come before the width, so '%5+d' names no conversion this formatter has. The
        // message quotes the specifier as it was written rather than the one character it stopped on.
        _output.Normal.Clear();
        ScriptRunResult result = RunMatlab("x = sprintf('%5+d', 5);");

        Assert.False(result.Success);
        string message = Assert.Single(result.Diagnostics).Message;
        Assert.Contains("does not support the specifier \"%5+\"", message, StringComparison.Ordinal);
        Assert.Contains("%c %d %i %e %E %f %g %G %o %s %u %x %X %%", message, StringComparison.Ordinal);
        Assert.Contains("- + 0 # and space", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownConversionIsStillAnError()
    {
        _output.Normal.Clear();
        ScriptRunResult result = RunMatlab("x = sprintf('%+q', 5);");

        Assert.False(result.Success);
        Assert.Contains("does not support the specifier", Assert.Single(result.Diagnostics).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheJgsDialectTakesTheFlagsButKeepsItsOwnIntegerReading()
    {
        // The flags are C's and belong to both dialects. The conversion MATLAB overrides on a value
        // its integer specifier cannot hold is MATLAB's alone: JGS goes on rounding, as it always has.
        _output.Normal.Clear();
        ScriptRunResult result = RunJgs("print(sprintf(\"%+d|%-6.2f|%#x|% d\", 3, 1.5, 255, 7));");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("+3|1.50  |0xff| 7\n", _output.NormalText);

        _output.Normal.Clear();
        result = RunJgs("print(sprintf(\"%+i\", 2.6));");
        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Equal("+3\n", _output.NormalText);
    }
}
