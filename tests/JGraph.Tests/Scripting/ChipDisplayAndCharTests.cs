using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Three defects about a value written or repeated in the wrong form: an infinity spelled with
/// .NET's word instead of MATLAB's, a negative zero keeping its sign through <c>num2str</c>, and
/// <c>repmat</c> tiling a piece of text as an element rather than repeating its characters.
/// </summary>
/// <remarks>
/// <para>
/// The three share a shape. In each, a value that already knew what it was lost that knowledge at
/// the one place it was turned into something else: a double became display text through a helper
/// that had never been told the MATLAB spellings, and a char row or a string array became a tiled
/// array through a builder that mints a fresh wrapper and so leaves the tag saying what it is
/// behind. That is the same failure M105 found in nineteen shape verbs, and <c>repmat</c> is on the
/// list of names retrofitted there — but only for a char <em>matrix</em>, which is the one text
/// container it was never handed.
/// </para>
/// <para>
/// Every expected value below was measured against MATLAB R2024a rather than remembered. Where the
/// layout differs — JGraph writes an array as <c>[1, Inf, NaN]</c> where MATLAB lays out columns —
/// the assertion is on the token and not on the line, because that layout is a deliberate
/// simplification and not what any of this is about.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class ChipDisplayAndCharTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public ChipDisplayAndCharTests() => JG.Reset();

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

    /// <summary>Runs <paramref name="code"/> and hands back the lines it printed.</summary>
    private async Task<IReadOnlyList<string>> RunPrinting(string code)
    {
        await RunAsserting(code);
        return _output.NormalLines;
    }

    // --- an infinity is spelled Inf -----------------------------------------------------------

    /// <summary>
    /// The defect itself: .NET's <c>double.ToString</c> answers "Infinity", and every display mode
    /// handed that spelling through, so one program wrote an infinity two ways.
    /// </summary>
    [Fact]
    public async Task AnInfinityDisplaysAsInfAndNotAsDotNetsWord()
    {
        IReadOnlyList<string> lines = await RunPrinting("""
            disp(1/0)
            disp(-1/0)
            disp(NaN)
            """);

        Assert.Equal(["Inf", "-Inf", "NaN"], lines);
    }

    /// <summary>The statement echo is a second display path, and it had the same spelling.</summary>
    [Fact]
    public async Task TheEchoOfAnInfinitySpellsItInf()
    {
        IReadOnlyList<string> lines = await RunPrinting("""
            x = Inf
            y = -Inf
            q = NaN
            """);

        Assert.Equal(["x = Inf", "y = -Inf", "q = NaN"], lines);
    }

    /// <summary>
    /// A matrix formats element by element rather than through the scalar path, so it is worth its
    /// own case: an infinity inside an array had to be reached separately from a bare one.
    /// </summary>
    [Fact]
    public async Task AnInfinityInsideAnArrayIsSpelledInfToo()
    {
        IReadOnlyList<string> lines = await RunPrinting("""
            disp([1 Inf NaN])
            z = [1 Inf NaN]
            m = [Inf -Inf; NaN 1]
            """);

        // The bracketed layout is JGraph's own and is not what the fix was about; the token is.
        Assert.Equal(["[1, Inf, NaN]", "z = [1, Inf, NaN]", "m = [Inf, -Inf; NaN, 1]"], lines);
    }

    /// <summary>
    /// A cell, a struct field and a sparse entry each print their elements through the same funnel,
    /// which is why naming the values in one place was enough for all of them.
    /// </summary>
    [Fact]
    public async Task EveryContainerThatShowsItsElementsShowsInf()
    {
        IReadOnlyList<string> lines = await RunPrinting("""
            c = {Inf, -Inf, NaN}
            s.a = Inf
            disp(sparse([1 2], [1 2], [Inf NaN], 2, 2))
            """);

        Assert.Equal("c = {Inf, -Inf, NaN}", lines[0]);
        Assert.Equal("s = struct(a: Inf)", lines[1]);
        Assert.Contains(lines, static line => line.Contains("(1,1)  Inf", StringComparison.Ordinal));
        Assert.Contains(lines, static line => line.Contains("(2,2)  NaN", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every precision <c>format</c> selects, because a custom numeric format string answers the
    /// culture's symbol for a non-finite double rather than laying out digits — so <c>shortE</c> and
    /// <c>longE</c> leaked the same word <c>long</c> did, and a finite number must still change.
    /// </summary>
    [Fact]
    public async Task EveryFormatModeSpellsAnInfinityTheSameWay()
    {
        IReadOnlyList<string> lines = await RunPrinting("""
            format short
            disp(Inf); disp(-Inf); disp(NaN); disp(1/3)
            format shortE
            disp(Inf); disp(-Inf); disp(1/3)
            format longE
            disp(Inf); disp(-Inf)
            format long
            disp(Inf); disp(1/3)
            """);

        Assert.Equal(
            [
                "Inf", "-Inf", "NaN", "0.33333",
                "Inf", "-Inf", "3.3333e-01",
                "Inf", "-Inf",
                "Inf", "0.3333333333333333",
            ],
            lines);
    }

    /// <summary>
    /// Both halves of a complex number go through the scalar formatter, and MATLAB writes the
    /// imaginary one exactly this way: <c>complex(1, Inf)</c> shows <c>Infi</c>, not <c>Infinityi</c>.
    /// </summary>
    [Fact]
    public async Task BothHalvesOfAComplexNumberSpellAnInfinityInf()
    {
        IReadOnlyList<string> lines = await RunPrinting("""
            z = complex(1, Inf)
            w = Inf + 2i
            v = complex(Inf, -Inf)
            """);

        Assert.Equal(["z = 1+Infi", "w = Inf+2i", "v = Inf-Infi"], lines);
    }

    /// <summary>
    /// The names that already wrote the MATLAB spelling must not have moved, and the two that reach
    /// for the display as a last resort now agree with them instead of contradicting them.
    /// </summary>
    [Fact]
    public Task TheFormattingBuiltinsStillWriteInfAndNowAgreeWithTheDisplay() => RunAsserting("""
        assert(strcmp(num2str(Inf), 'Inf'));
        assert(strcmp(num2str(-Inf), '-Inf'));
        assert(strcmp(mat2str([1 Inf -Inf NaN]), '[1 Inf -Inf NaN]'));
        assert(strcmp(sprintf('%g %f %d', Inf, -Inf, NaN), 'Inf -Inf NaN'));

        % %s and string() have no conversion of their own and fall through to the display.
        assert(strcmp(sprintf('%s', Inf), 'Inf'));
        assert(strcmp(string(Inf), "Inf"));
        """);

    /// <summary>
    /// The one thing the spelling must not reach. <c>writematrix</c> shares the precision helper
    /// underneath the display to write a CSV, and this program's own <c>readmatrix</c> parses
    /// .NET's "Infinity" and not "Inf" — so naming the values down there would have written a file
    /// JGraph could no longer read back. It writes what it always wrote, and reads it.
    /// </summary>
    [Fact]
    public async Task WritingAMatrixStillRoundTripsAnInfinityThroughAFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jgraph-inf-{Guid.NewGuid():N}.csv");
        try
        {
            await RunAsserting($"""
                writematrix([1 Inf; -Inf NaN], '{path.Replace("\\", "\\\\", StringComparison.Ordinal)}');
                b = readmatrix('{path.Replace("\\", "\\\\", StringComparison.Ordinal)}');
                assert(isequal(size(b), [2 2]));
                assert(b(1, 1) == 1);
                assert(isinf(b(1, 2)) && b(1, 2) > 0);
                assert(isinf(b(2, 1)) && b(2, 1) < 0);
                assert(isnan(b(2, 2)));
                """);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // --- num2str drops a negative zero's sign -------------------------------------------------

    /// <summary>
    /// MATLAB keeps the sign in the value and never shows it here. Measured against R2024a: every
    /// <c>num2str</c> form drops it, including the ones handed an explicit format, while
    /// <c>sprintf('%g', -0)</c> in the same session keeps it — so this is the builtin normalising
    /// and not the formatter underneath it.
    /// </summary>
    [Fact]
    public Task NumToStrDropsTheSignOfANegativeZeroInEveryForm() => RunAsserting("""
        assert(strcmp(num2str(-0), '0'));
        assert(strcmp(num2str(-0.0), '0'));
        assert(strcmp(num2str(-0, '%d'), '0'));
        assert(strcmp(num2str(-0, '%g'), '0'));
        assert(strcmp(num2str(-0, 8), '0'));
        assert(strcmp(num2str(single(-0)), '0'));

        % An underflow reaches the same value by another road.
        assert(strcmp(num2str(-1e-400), '0'));
        """);

    /// <summary>
    /// Done on the value and not on the finished text, because the column width is measured from the
    /// strings: stripping the sign afterwards would have left <c>num2str([-0 1])</c> a character
    /// wider than <c>num2str([0 1])</c>, where MATLAB writes the two identically.
    /// </summary>
    [Fact]
    public Task ANegativeZeroInAnArrayTakesNoMoreRoomThanAPositiveOne() => RunAsserting("""
        assert(strcmp(num2str([-0 1]), num2str([0 1])));
        assert(strcmp(num2str([-0 1]), '0  1'));
        assert(strcmp(num2str([1 -0 2]), '1  0  2'));
        """);

    /// <summary>
    /// The neighbours MATLAB does <em>not</em> treat alike, pinned so the normalisation cannot spread
    /// into them: <c>sprintf</c> keeps the sign under <c>%g</c> and drops it under <c>%d</c>, and the
    /// value itself is still a negative zero after <c>num2str</c> has described it as a positive one.
    /// </summary>
    [Fact]
    public Task TheSignSurvivesEverywhereMatlabKeepsIt() => RunAsserting("""
        assert(strcmp(sprintf('%g', -0), '-0'));
        assert(strcmp(sprintf('%d', -0), '0'));

        % The stored value is untouched: 1/-0 is still the negative infinity.
        x = -0;
        assert(x == 0);
        num2str(x);
        assert(isinf(1 / x) && 1 / x < 0);
        """);

    // --- repmat of text -----------------------------------------------------------------------

    /// <summary>
    /// The defect: a char row is one value here and 1-by-n characters in MATLAB, so tiling it as an
    /// element built a grid of separate one-character pieces. <c>repmat('ab', 1, 3)</c> answered a
    /// 1-by-3 double where MATLAB answers the 1-by-6 char <c>'ababab'</c>.
    /// </summary>
    [Fact]
    public Task RepmatOfACharRowRepeatsItsCharacters() => RunAsserting("""
        x = repmat('a', 1, 5);
        assert(strcmp(class(x), 'char'));
        assert(isequal(size(x), [1 5]));
        assert(strcmp(x, 'aaaaa'));

        y = repmat('ab', 1, 3);
        assert(strcmp(class(y), 'char'));
        assert(isequal(size(y), [1 6]));
        assert(strcmp(y, 'ababab'));
        """);

    /// <summary>
    /// Repeated downwards a char row becomes a char matrix, which is the container M105 built and the
    /// one <c>repmat</c> was already retrofitted for — it just never reached it from a char row.
    /// </summary>
    [Fact]
    public Task RepmatOfACharRowDownwardsAnswersACharMatrix() => RunAsserting("""
        z = repmat('ab', 2, 3);
        assert(strcmp(class(z), 'char'));
        assert(isequal(size(z), [2 6]));
        assert(strcmp(z(1, :), 'ababab'));
        assert(strcmp(z(2, :), 'ababab'));

        e = repmat('a', 2, 2);
        assert(strcmp(class(e), 'char') && isequal(size(e), [2 2]));
        assert(strcmp(e(1, :), 'aa'));
        """);

    /// <summary>
    /// A char column and a char matrix already worked, because both are char matrices and
    /// <c>repmat</c> is on M105's list of names that keep the tag. They are here so the new arms
    /// cannot take the old road away from them.
    /// </summary>
    [Fact]
    public Task RepmatOfACharColumnAndOfACharMatrixStillTileTheirCharacters() => RunAsserting("""
        c = repmat(['a'; 'b'], 2, 1);
        assert(strcmp(class(c), 'char') && isequal(size(c), [4 1]));
        assert(strcmp(c', 'abab'));

        m = repmat(char('a', 'bcd'), 1, 2);
        assert(strcmp(class(m), 'char') && isequal(size(m), [2 6]));
        assert(strcmp(m(1, :), 'a  a  '));
        assert(strcmp(m(2, :), 'bcdbcd'));

        f = repmat(['ab'; 'cd'], 1, 1);
        assert(strcmp(class(f), 'char') && isequal(size(f), [2 2]));
        """);

    /// <summary>
    /// The one-count form is <c>n</c> by <c>n</c> and the size-vector form is a pair, exactly as they
    /// are for a number — the text arms must take both roads, not only the two-count one.
    /// </summary>
    [Fact]
    public Task RepmatOfTextTakesOneCountAndASizeVectorAsWellAsTwoCounts() => RunAsserting("""
        o = repmat('q', 3);
        assert(strcmp(class(o), 'char') && isequal(size(o), [3 3]));
        assert(strcmp(o(2, :), 'qqq'));

        p = repmat('abc', [2 2]);
        assert(strcmp(class(p), 'char') && isequal(size(p), [2 6]));
        assert(strcmp(p(1, :), 'abcabc'));

        d = repmat('abc', 1, 1);
        assert(strcmp(class(d), 'char') && isequal(size(d), [1 3]) && strcmp(d, 'abc'));
        """);

    /// <summary>
    /// The empty corners, all three measured against MATLAB. They fall out of repmat's own
    /// arithmetic — the answer's size is the source's size multiplied — which is why a zero count
    /// leaves one dimension empty while the other keeps what it was multiplied to.
    /// </summary>
    [Fact]
    public Task RepmatOfTextMultipliesTheSizeEvenWhenTheAnswerIsEmpty() => RunAsserting("""
        % 1-by-3 characters, no copies down: 0-by-6, and not 0-by-0.
        a = repmat('abc', 0, 2);
        assert(strcmp(class(a), 'char') && isequal(size(a), [0 6]) && isempty(a));

        b = repmat('abc', 2, 0);
        assert(strcmp(class(b), 'char') && isequal(size(b), [2 0]) && isempty(b));

        % '' is 0-by-0 here as in MATLAB, so repeating it is still nothing however large the counts.
        assert(isequal(size(''), [0 0]));
        c = repmat('', 2, 3);
        assert(strcmp(class(c), 'char') && isequal(size(c), [0 0]) && isempty(c));
        """);

    /// <summary>
    /// A string array must go on tiling as elements: <c>repmat("a", 1, 3)</c> is three strings, where
    /// <c>repmat('a', 1, 3)</c> is one longer piece of text. The tag saying which lives on the
    /// wrapper the tiling replaces, so it had to be put back — and a string <em>scalar</em> also had
    /// to stop being demoted to the char row it stands for on the way in, or it took the char road.
    /// </summary>
    [Fact]
    public Task RepmatOfAStringArrayTilesItsElementsAndStaysAString() => RunAsserting("""
        s = repmat("a", 1, 3);
        assert(strcmp(class(s), 'string'));
        assert(isequal(size(s), [1 3]));
        assert(numel(s) == 3);
        assert(s(1) == "a" && s(3) == "a");

        g = repmat("hi", 2, 2);
        assert(strcmp(class(g), 'string') && isequal(size(g), [2 2]));
        assert(g(2, 2) == "hi");

        h = repmat(["a" "b"], 2, 1);
        assert(strcmp(class(h), 'string') && isequal(size(h), [2 2]));
        assert(h(1, 1) == "a" && h(1, 2) == "b" && h(2, 1) == "a");
        """);

    /// <summary>
    /// The arms the text cases must not have disturbed: a number, a logical and a matrix all tile the
    /// way they did, and <c>repmat</c> of a number is still not text.
    /// </summary>
    [Fact]
    public Task RepmatOfANumberIsUnchanged() => RunAsserting("""
        assert(isequal(repmat(7, 2, 3), 7 * ones(2, 3)));
        assert(strcmp(class(repmat(7, 2, 3)), 'double'));
        assert(isequal(repmat([1 2; 3 4], 2, 1), [1 2; 3 4; 1 2; 3 4]));
        assert(isequal(repmat([1 2], 1, 2), [1 2 1 2]));
        assert(strcmp(class(repmat(true, 1, 3)), 'logical'));
        assert(isequal(repmat(true, 1, 3), [true true true]));
        assert(isequal(size(repmat(0, 0, 3)), [0 3]));
        """);

    /// <summary>
    /// What the whole thing was found through: <c>fprintf</c> reads a char row as one argument and a
    /// grid of one-character pieces as many, so the tiled text used to swallow the arguments after it
    /// and print a line that no longer said what it was asked to say.
    /// </summary>
    [Fact]
    public async Task ARepeatedCharRowPrintsAsOnePieceOfText()
    {
        IReadOnlyList<string> lines = await RunPrinting("""
            x = repmat('ab', 1, 3);
            fprintf('class=%s size=%s val=[%s]\n', class(x), mat2str(size(x)), x);
            """);

        Assert.Equal(["class=char size=[1 6] val=[ababab]"], lines);
    }
}
