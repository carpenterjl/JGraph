using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M52: the shared option parser, tested directly rather than through whichever builtin happens to
/// declare a spec. It arrived with the imaging surface in M46 and moved to neutral ground here, so
/// these are the first tests it has had of its own — every earlier one reached it through a picture.
/// </summary>
public class MatlabOptionSpecTests
{
    private static readonly JgsBuiltins.OptionSpec Spec = new(
        "demo",
        Flags: ["stable", "sorted", "rows"],
        Names: ["Endpoints", "Width", "Enabled", "Method"]);

    private static JgsBuiltins.ParsedArgs Parse(params JgsValue[] args) => Spec.Parse(args, 2, 1, 1);

    private static JgsValue N(double value) => JgsValue.Number(value);

    private static JgsValue S(string value) => JgsValue.Str(value);

    [Fact]
    public void PositionalsStopAtTheFirstOptionWord()
    {
        JgsBuiltins.ParsedArgs parsed = Parse(N(1), N(2), S("stable"));

        Assert.Equal(2, parsed.Positional.Count);
        Assert.True(parsed.Has("stable"));
    }

    [Fact]
    public void PositionalsStopEarlyWhenAnOptionArrivesEarly()
    {
        JgsBuiltins.ParsedArgs parsed = Parse(N(1), S("rows"));

        Assert.Single(parsed.Positional);
        Assert.True(parsed.Has("rows"));
    }

    [Fact]
    public void OptionWordsAreCaseInsensitive()
    {
        JgsBuiltins.ParsedArgs parsed = Parse(N(1), S("STABLE"), S("width"), N(3));

        Assert.True(parsed.Has("stable"));
        Assert.Equal(3, parsed.Scalar("Width", 0));
    }

    [Fact]
    public void OptionWordsAreNeverMatchedByPrefix()
    {
        // Deliberate: MATLAB accepts unambiguous abbreviations, and copying that would let a future
        // option silently change what an existing script's abbreviation resolves to.
        JgsRuntimeException error = Assert.Throws<JgsRuntimeException>(() => Parse(N(1), S("stab")));

        Assert.Contains("stab", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownOptionNamesTheAlternatives()
    {
        JgsRuntimeException error = Assert.Throws<JgsRuntimeException>(() => Parse(N(1), S("stabel")));

        // The whole point of a declared spec: the misspelling is reported against the real list
        // instead of being swallowed as data and failing later about something else.
        Assert.Contains("'stable'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Endpoints'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameWithNoValueAfterItIsReported()
    {
        JgsRuntimeException error = Assert.Throws<JgsRuntimeException>(() => Parse(N(1), S("Width")));

        Assert.Contains("needs a value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionsMayComeInAnyOrder()
    {
        JgsBuiltins.ParsedArgs parsed = Parse(N(1), S("Width"), N(5), S("stable"), S("Enabled"), JgsValue.Bool(true));

        Assert.Equal(5, parsed.Scalar("Width", 0));
        Assert.True(parsed.Has("stable"));
        Assert.True(parsed.Flag("Enabled", false));
    }

    [Fact]
    public void MissingOptionsFallBack()
    {
        JgsBuiltins.ParsedArgs parsed = Parse(N(1));

        Assert.Equal(7, parsed.Scalar("Width", 7));
        Assert.False(parsed.Flag("Enabled", false));
        Assert.Null(parsed.Text("Method"));
        Assert.Null(parsed.Vector("Width"));
        Assert.Null(parsed.Window("Width"));
    }

    [Fact]
    public void OneOfRefusesTwoOfAMutuallyExclusiveSet()
    {
        JgsBuiltins.ParsedArgs parsed = Parse(N(1), S("stable"), S("sorted"));

        JgsRuntimeException error = Assert.Throws<JgsRuntimeException>(
            () => parsed.OneOf("sorted", "stable", "sorted"));

        Assert.Contains("cannot both be given", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneOfAnswersTheGivenChoiceOrTheFallback()
    {
        Assert.Equal("stable", Parse(N(1), S("stable")).OneOf("sorted", "stable", "sorted"));
        Assert.Equal("sorted", Parse(N(1)).OneOf("sorted", "stable", "sorted"));
    }

    [Fact]
    public void AScalarCountsAsAOneElementVector()
    {
        // MATLAB writes a one-element vector as a bare scalar, so 'Width', 5 and 'Width', [5] are the
        // same call and both have to arrive here as a vector.
        double[] width = Assert.IsType<double[]>(Parse(N(1), S("Width"), N(5)).Vector("Width"));
        Assert.Equal([5.0], width);
    }

    [Fact]
    public void AWindowTakesASizeOrAPair()
    {
        Assert.Equal((3, 3), Parse(N(1), S("Width"), N(3)).Window("Width"));
        Assert.Equal(
            (3, 5),
            Parse(N(1), S("Width"), JgsMatrix.FromColumnMajor([3, 5], 1, 2)).Window("Width"));
    }

    [Fact]
    public void AWindowRefusesThreeValues()
    {
        JgsRuntimeException error = Assert.Throws<JgsRuntimeException>(
            () => Parse(N(1), S("Width"), JgsMatrix.FromColumnMajor([3, 5, 7], 1, 3)).Window("Width"));

        Assert.Contains("[rows, cols]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WholeRefusesAFraction()
    {
        // A count that arrives as 2.5 is a mistake worth naming rather than a number to round quietly.
        JgsRuntimeException error = Assert.Throws<JgsRuntimeException>(
            () => Parse(N(1), S("Width"), N(2.5)).Whole("Width", 1));

        Assert.Contains("whole number", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WholeTakesAnIntegerOrTheFallback()
    {
        Assert.Equal(4, Parse(N(1), S("Width"), N(4)).Whole("Width", 1));
        Assert.Equal(1, Parse(N(1)).Whole("Width", 1));
    }

    [Fact]
    public void WordAcceptsOnlyTheListedSpellings()
    {
        Assert.Equal(
            "shrink",
            Parse(N(1), S("Endpoints"), S("shrink")).Word("Endpoints", "shrink", "shrink", "discard", "fill"));

        JgsRuntimeException error = Assert.Throws<JgsRuntimeException>(
            () => Parse(N(1), S("Endpoints"), S("trim")).Word("Endpoints", "shrink", "shrink", "discard"));

        Assert.Contains("'shrink'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'discard'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WordIsCaseInsensitiveAndAnswersTheCanonicalSpelling()
    {
        Assert.Equal(
            "discard",
            Parse(N(1), S("Endpoints"), S("DISCARD")).Word("Endpoints", "shrink", "shrink", "discard"));
    }

    [Fact]
    public void ATypedOptionRefusesTheWrongType()
    {
        Assert.Contains(
            "takes a number",
            Assert.Throws<JgsRuntimeException>(() => Parse(N(1), S("Width"), S("wide")).Scalar("Width", 0)).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            "takes a word",
            Assert.Throws<JgsRuntimeException>(() => Parse(N(1), S("Method"), N(2)).Text("Method")).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANumericFlagIsOnlyReadWhenTheSpecAllowsOne()
    {
        var padding = new JgsBuiltins.OptionSpec(
            "pad", Flags: ["replicate"], Names: [], AllowNumericFlag: true);

        Assert.Equal(9, padding.Parse([N(1), N(9)], 1, 1, 1).NumericFlag?.AsNumber);

        // Without the allowance a stray number is an error, not a silently ignored argument.
        Assert.Throws<JgsRuntimeException>(() => Parse(N(1), N(2), N(3)));
    }

    [Fact]
    public void StringPositionalsLetALeadingPathThrough()
    {
        // imwrite's filename is positional and looks exactly like an option word; the count says how
        // many leading slots may hold a string before strings start meaning options. This is the real
        // spec's shape: imwrite(A, 'out.png', 'Quality', 90).
        var writing = new JgsBuiltins.OptionSpec(
            "imwrite", Flags: [], Names: ["Quality"], StringPositionals: 2);

        JgsBuiltins.ParsedArgs parsed = writing.Parse([N(1), S("out.png"), S("Quality"), N(90)], 3, 1, 1);

        Assert.Equal(2, parsed.Positional.Count);
        Assert.Equal("out.png", parsed.Positional[1].AsString);
        Assert.Equal(90, parsed.Scalar("Quality", 0));
    }

    [Fact]
    public void PastTheStringPositionalsAStringIsAnOption()
    {
        // The other side of the same rule, and the reason it exists: a misspelling in the option tail
        // must be reported rather than quietly consumed as another positional.
        var writing = new JgsBuiltins.OptionSpec(
            "imwrite", Flags: [], Names: ["Quality"], StringPositionals: 2);

        JgsRuntimeException error = Assert.Throws<JgsRuntimeException>(
            () => writing.Parse([N(1), S("out.png"), S("Qualty"), N(90)], 3, 1, 1));

        Assert.Contains("Qualty", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Quality'", error.Message, StringComparison.Ordinal);
    }
}
