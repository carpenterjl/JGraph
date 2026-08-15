using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M63 — string arrays. Double quotes stop meaning a char row and start meaning a string, which is
/// the widest single change to what a MATLAB script's text does since the dialect landed.
/// </summary>
/// <remarks>
/// The three questions these tests keep separate are the three the type exists to answer: what a
/// piece of text <em>is</em> (<c>class</c>, <c>isstring</c>, <c>ischar</c>), how many of them there
/// are (<c>numel("abc")</c> is 1 and <c>numel('abc')</c> is 3), and what happens when text meets an
/// operator (<c>"a" + "b"</c> joins where <c>'a' + 'b'</c> adds). Everything else in the milestone —
/// the editing family, the elementwise retrofit — follows from those.
/// </remarks>
[Collection("JG facade")]
public class MatlabStringArrayTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabStringArrayTests() => JG.Reset();

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

    // --- The type ---------------------------------------------------------------------------------

    [Fact]
    public Task ADoubleQuotedLiteralIsAStringAndASingleQuotedOneIsAChar() => RunAsserting("""
        s = "hello";
        c = 'hello';
        assert(isstring(s) && ~ischar(s));
        assert(ischar(c) && ~isstring(c));
        assert(strcmp(class(s), 'string'));
        assert(strcmp(class(c), 'char'));
        assert(isa(s, 'string'));
        """);

    /// <summary>
    /// The one place the two representations genuinely disagree, and the reason a string scalar is
    /// stored as a 1-by-1 array rather than as a char row wearing a label.
    /// </summary>
    [Fact]
    public Task AStringScalarIsOneElementWhereACharRowIsSeveral() => RunAsserting("""
        assert(numel("abc") == 1);
        assert(numel('abc') == 3);
        assert(length("abc") == 1);
        assert(isequal(size("abc"), [1 1]));
        assert(isscalar("abc"));
        assert(~isempty(""));            % a 1-by-1 string array, empty text and all
        assert(isempty(''));
        assert(strlength("abc") == 3);   % strlength is the question numel used to answer
        """);

    [Fact]
    public Task BracketsBuildAStringArrayWhenAnyPieceIsAString() => RunAsserting("""
        pair = ["a", "b"];
        assert(isstring(pair) && numel(pair) == 2);
        assert(isequal(size(pair), [1 2]));

        % A char row joining strings becomes one element rather than being spliced in letter by
        % letter, which is the whole difference between the two kinds of quote inside a bracket.
        mixed = ["a", 'bc'];
        assert(isstring(mixed) && numel(mixed) == 2);
        assert(strcmp(mixed(2), 'bc'));

        % Single quotes alone still join, which is how a label has always been built.
        label = ['SN:' 'A1'];
        assert(ischar(label) && strcmp(label, 'SN:A1'));
        """);

    [Fact]
    public Task IndexingGivesAStringBackAndBracesGiveTheCharInside() => RunAsserting("""
        s = ["one", "two", "three"];
        assert(isstring(s(2)) && numel(s(2)) == 1);
        assert(ischar(s{2}));
        assert(strcmp(s{2}, 'two'));
        assert(isstring(s(2:3)) && numel(s(2:3)) == 2);
        assert(isstring(s([true false true])));
        """);

    [Fact]
    public Task AStringSurvivesBeingPassedAroundAndAssigned() => RunAsserting("""
        % Value semantics copy on every binding, so a tag that the copy did not carry would be lost
        % the first time a script named the thing twice.
        a = ["x", "y"];
        b = a;
        assert(isstring(b));
        assert(isstring(relay(a)));
        c = {a};
        assert(isstring(c{1}));

        function out = relay(in)
            out = in;
        end
        """);

    // --- Operators --------------------------------------------------------------------------------

    [Fact]
    public Task PlusJoinsStringsAndStillAddsChars() => RunAsserting("""
        assert(strcmp("a" + "b", 'ab'));
        assert(isstring("a" + "b"));
        assert(strcmp("n = " + 5, 'n = 5'));

        % A scalar on one side spreads over the other, which is MATLAB's implicit expansion.
        labelled = "p" + ["1" "2"];
        assert(numel(labelled) == 2 && strcmp(labelled(2), 'p2'));
        """);

    [Fact]
    public Task ComparingStringsAnswersElementwise() => RunAsserting("""
        assert("a" == "a");
        assert(~("a" == "b"));
        assert(isequal(strcmp(["a" "b" "a"], "a"), [true false true]));
        assert(isequal(contains(["abc" "xyz"], "b"), [true false]));
        assert(isequal(startsWith(["abc" "xbc"], "a"), [true false]));
        """);

    // --- Conversions ------------------------------------------------------------------------------

    [Fact]
    public Task StringAndCharAndCellstrConvertBothWays() => RunAsserting("""
        assert(isstring(string('abc')) && numel(string('abc')) == 1);
        assert(strcmp(char("abc"), 'abc'));
        assert(ischar(char("abc")));

        cs = cellstr(["one", "two"]);
        assert(iscell(cs) && numel(cs) == 2 && strcmp(cs{2}, 'two'));

        back = string(cs);
        assert(isstring(back) && strcmp(back(1), 'one'));

        % char of several strings pads them to a common width and stacks them, which is what
        % MATLAB's char matrix is — and the reason cellstr is usually what a script actually wants.
        % JGraph writes a stack of char rows the way ['ab'; 'cd'] has always written one: as rows
        % rather than as a single char array, which is a recorded divergence.
        m = char(["a", "bbb"]);
        assert(isequal(size(m), [2 1]));
        assert(strcmp(m(1), 'a  '));

        assert(isstring(strings(2)) && numel(strings(2)) == 4);
        assert(strcmp(char(65), 'A'));
        assert(strcmp(char([72 105]), 'Hi'));
        """);

    [Fact]
    public Task MissingIsAStringWithNothingInIt() => RunAsserting("""
        arr = ["x", missing, "y"];
        assert(isequal(ismissing(arr), [false true false]));
        assert(ismissing(arr(2)));
        assert(~ismissing(arr(1)));
        """);

    // --- The editing family -----------------------------------------------------------------------

    [Fact]
    public Task TheEditingFamilyCutsAndJoinsText() => RunAsserting("""
        assert(strcmp(erase("hello world", "o"), 'hell wrld'));
        assert(strcmp(insertAfter("abc", "a", "-"), 'a-bc'));
        assert(strcmp(insertBefore("abc", "c", "-"), 'ab-c'));
        assert(strcmp(extractAfter("abcdef", 3), 'def'));
        assert(strcmp(extractBefore("abcdef", 3), 'ab'));
        assert(strcmp(extractBetween("a[b]c", "[", "]"), 'b'));
        assert(strcmp(strip("  ab  "), 'ab'));
        assert(strcmp(strip("  ab  ", 'left'), 'ab  '));
        assert(strcmp(pad("ab", 4), 'ab  '));
        assert(strcmp(reverse("abc"), 'cba'));
        """);

    [Fact]
    public Task AMarkerThatIsNotThereChangesNothing() => RunAsserting("""
        assert(strcmp(insertAfter("abc", "z", "-"), 'abc'));
        assert(strlength(extractAfter("abc", "z")) == 0);
        assert(strlength(extractBetween("abc", "[", "]")) == 0);
        """);

    /// <summary>
    /// The retrofit: the text builtins that were written against one char row now answer once per
    /// element when handed several, keeping the container they came in.
    /// </summary>
    [Fact]
    public Task TextFunctionsAnswerOncePerElement() => RunAsserting("""
        assert(isequal(upper(["ab" "cd"]), ["AB" "CD"]));
        assert(isstring(upper(["ab" "cd"])));
        assert(isequal(strlength(["a" "bb"]), [1 2]));
        assert(isequal(str2double(["1.5" "2.5"]), [1.5 2.5]));

        % A cell of char keeps being a cell, which is what the family did before strings existed.
        trimmed = strtrim({' a ', ' b '});
        assert(iscell(trimmed) && strcmp(trimmed{1}, 'a'));
        assert(isequal(strrep({'aa', 'ba'}, 'a', 'z'), {'zz', 'bz'}));

        % A string scalar maps too, and comes back a string rather than the char it went down as.
        assert(isstring(upper("ab")));
        """);

    [Fact]
    public Task JoinAndSplitAndSortKeepTheKindOfTextTheyWereGiven() => RunAsserting("""
        assert(strcmp(join(["a" "b"], "-"), 'a-b'));
        assert(isstring(join(["a" "b"], "-")));

        parts = split("a,b,c", ",");
        assert(isstring(parts) && numel(parts) == 3 && strcmp(parts(2), 'b'));

        assert(isequal(sort(["c" "a" "b"]), ["a" "b" "c"]));
        assert(isequal(unique(["b" "a" "b"]), ["a" "b"]));
        assert(isstring(sort(["c" "a"])));

        % Handing the same functions char rows leaves them answering in char, as they always did.
        assert(ischar(strjoin({'a', 'b'}, '-')));
        """);

    [Fact]
    public Task Str2numEvaluatesAndStr2doubleDoesNot() => RunAsserting("""
        assert(isequal(str2num('[1 2 3]'), [1 2 3]));
        assert(str2num('1+1') == 2);
        assert(isempty(str2num('not an expression at all $$')));
        assert(isnan(str2double('1+1')));   % str2double reads a number, and this is not one
        """);

    // --- Formatting -------------------------------------------------------------------------------

    [Fact]
    public Task SprintfKnowsTheRestOfTheCSpecifiers() => RunAsserting("""
        assert(strcmp(sprintf('%c', 65), 'A'));
        assert(strcmp(sprintf('%u', 42), '42'));
        assert(strcmp(sprintf('%X', 255), 'FF'));
        assert(strcmp(sprintf('%x', 255), 'ff'));
        assert(strcmp(sprintf('%E', 1234.5), '1.234500E+03'));
        assert(~isempty(strfind(sprintf('%G', 0.00001234), 'E')));
        """);

    [Fact]
    public Task AStarTakesTheWidthFromTheArgumentList() => RunAsserting("""
        assert(strcmp(sprintf('%*.*f', 8, 2, pi), '    3.14'));
        assert(strcmp(sprintf('%*d', 4, 7), '   7'));
        assert(strcmp(sprintf('%-*d|', 4, 7), '7   |'));
        """);

    [Fact]
    public Task SprintfAndComposeTakeStringsToo() => RunAsserting("""
        assert(strcmp(sprintf("%d apples", 3), '3 apples'));
        assert(strcmp(compose("%d-%d", [1 2]), '1-2'));

        % One specifier still means one answer per value, which is what compose is usually for.
        many = compose('%0.1f', [1.25; 2.5]);
        assert(numel(many) == 2);
        """);

    // --- The frozen dialect -----------------------------------------------------------------------

    /// <summary>
    /// JGS never had a string type for double quotes to mean, and its surface is frozen, so the
    /// flip is MATLAB's alone. This is the test that fails if the gate ever stops being a gate.
    /// </summary>
    [Fact]
    public async Task JgsKeepsItsOwnMeaningForDoubleQuotes()
    {
        await using IScriptSession session = Assert
            .IsAssignableFrom<IScriptRepl>(new JgsScriptEngine())
            .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

        ScriptRunResult result = await session.ExecuteAsync(
            """
            let s = "hello"
            print(length(s))
            """,
            sourceId: "",
            CancellationToken.None);

        Assert.True(result.Success, result.Message + _output.ErrorText);
        Assert.Contains("5", _output.NormalText, StringComparison.Ordinal);
    }
}
