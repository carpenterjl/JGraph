using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M52: the option surfaces of the text, cell and formatting builtins — <c>strsplit</c>,
/// <c>strjoin</c>, the regular-expression option words, <c>cellfun</c> in every documented shape, and
/// <c>num2str</c> on an array. Expected values are MATLAB's own.
/// </summary>
/// <remarks>
/// Three of these change an answer rather than adding one, and each is pinned here so the change is a
/// decision rather than a drift: a dot spans a newline by default (MATLAB's <c>'dotall'</c>), a
/// zero-length match is not replaced by default (MATLAB's <c>'noemptymatch'</c>), and splitting on
/// whitespace keeps the empty piece a leading or trailing delimiter produces. A fourth lives one
/// layer down — <c>%g</c> now writes C's two-digit exponent, which is what <c>num2str</c> of a small
/// number reads through — and is pinned here beside the builtin that found it.
/// </remarks>
[Collection("JG facade")]
public class MatlabStringOptionTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabStringOptionTests() => JG.Reset();

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

    // --- strsplit ---------------------------------------------------------------------------------

    [Fact]
    public Task SplittingKeepsThePiecesADelimiterLeavesAtTheEnds() => RunAsserting("""
        assert(isequal(strsplit('a,b,c', ','), {'a', 'b', 'c'}));
        % A delimiter at either end leaves an empty piece there, which is what makes strsplit the
        % same function as regexp(..., 'split').
        e = strsplit('  a b  ');
        assert(numel(e) == 4);
        assert(isempty(e{1}));
        assert(isempty(e{4}));
        assert(strcmp(e{2}, 'a'));
        """);

    [Fact]
    public Task RunsOfDelimitersCollapseUnlessToldNotTo() => RunAsserting("""
        assert(isequal(strsplit('a,,b', ','), {'a', 'b'}));
        assert(isequal(strsplit('a,,b', ',', 'CollapseDelimiters', false), {'a', '', 'b'}));
        assert(isequal(strsplit('a,,b', ',', 'CollapseDelimiters', true), {'a', 'b'}));
        """);

    [Fact]
    public Task ACellOfDelimitersCutsOnAnyOfThem() => RunAsserting("""
        assert(isequal(strsplit('a1b2c', {'1', '2'}), {'a', 'b', 'c'}));
        assert(isequal(strsplit('a::b--c', {'::', '--'}), {'a', 'b', 'c'}));
        """);

    /// <summary>
    /// A simple delimiter is literal text but still reads sprintf's escapes, and a regular-expression
    /// delimiter is a pattern — the difference between splitting on the two characters <c>\d</c> and
    /// splitting on any digit.
    /// </summary>
    [Fact]
    public Task TheDelimiterTypeSaysWhetherThePatternIsText() => RunAsserting("""
        assert(isequal(strsplit(sprintf('a\tb'), '\t'), {'a', 'b'}));
        assert(isequal(strsplit('a1b22c', '\d+', 'DelimiterType', 'RegularExpression'), {'a', 'b', 'c'}));
        % '\d' is not one of sprintf's escapes, so a simple delimiter leaves it as two characters
        % and splits on them literally rather than on any digit.
        assert(isequal(strsplit('a\db', '\d'), {'a', 'b'}));
        assert(isequal(strsplit('a1b', '\d'), {'a1b'}));
        """);

    [Fact]
    public Task TheSecondOutputReportsTheDelimitersActuallyCutOn() => RunAsserting("""
        [c, m] = strsplit('a1b22c', '\d+', 'DelimiterType', 'RegularExpression');
        assert(isequal(c, {'a', 'b', 'c'}));
        assert(isequal(m, {'1', '22'}));
        [~, none] = strsplit('abc', ',');
        assert(isempty(none));
        """);

    /// <summary>
    /// The delimiter slot is optional, so a second argument that spells an option name is the option —
    /// otherwise <c>strsplit(s, 'CollapseDelimiters', false)</c> would split on the word.
    /// </summary>
    [Fact]
    public Task AnOptionNameInTheDelimiterSlotIsStillTheOption() => RunAsserting("""
        assert(isequal(strsplit('a b', 'CollapseDelimiters', true), {'a', 'b'}));
        assert(isequal(strsplit('a  b', 'CollapseDelimiters', false), {'a', '', 'b'}));
        """);

    // --- strjoin ----------------------------------------------------------------------------------

    [Fact]
    public Task JoiningTakesOneSeparatorOrOnePerGap() => RunAsserting("""
        assert(strcmp(strjoin({'a', 'b', 'c'}), 'a b c'));
        assert(strcmp(strjoin({'a', 'b', 'c'}, '-'), 'a-b-c'));
        assert(strcmp(strjoin({'a', 'b', 'c'}, {', ', ' and '}), 'a, b and c'));
        assert(strcmp(strjoin({'a', 'b'}, '\t'), sprintf('a\tb')));
        assert(strcmp(strjoin({'only'}, '-'), 'only'));
        """);

    [Fact]
    public Task AWrongNumberOfSeparatorsSaysHowManyItWanted() => RunAsserting("""
        ok = 0;
        try
            strjoin({'a', 'b', 'c'}, {'-'});
        catch err
            ok = ~isempty(strfind(err.message, '2 delimiter'));
        end
        assert(ok == 1);
        """);

    // --- regexprep --------------------------------------------------------------------------------

    [Fact]
    public Task ReplacingStillDoesWhatItAlwaysDid() => RunAsserting("""
        assert(strcmp(regexprep('hello world', 'o', '0'), 'hell0 w0rld'));
        assert(strcmp(regexprep('john smith', '(\w+) (\w+)', '$2 $1'), 'smith john'));
        assert(strcmp(regexprep('aaa', 'a', 'b', 'once'), 'baa'));
        """);

    /// <summary>
    /// MATLAB ignores a zero-length match unless asked; .NET replaces at every position. The default
    /// used to follow .NET by omission, which turned <c>regexprep('abc', 'x*', '-')</c> into
    /// <c>'-a-b-c-'</c>.
    /// </summary>
    [Fact]
    public Task AZeroLengthMatchIsOnlyAMatchWhenAskedFor() => RunAsserting("""
        assert(strcmp(regexprep('abc', 'x*', '-'), 'abc'));
        assert(strcmp(regexprep('abc', 'x*', '-', 'noemptymatch'), 'abc'));
        assert(strcmp(regexprep('abc', 'x*', '-', 'emptymatch'), '-a-b-c-'));
        assert(isempty(regexp('abc', 'x*', 'match')));
        assert(numel(regexp('abc', 'x*', 'match', 'emptymatch')) == 4);
        """);

    /// <summary>MATLAB's dot spans a newline by default; .NET's does not, and .NET's used to win.</summary>
    [Fact]
    public Task TheDotSpansANewlineUnlessToldOtherwise() => RunAsserting("""
        two = sprintf('a\nb');
        assert(strcmp(regexprep(two, 'a.b', 'X'), 'X'));
        assert(strcmp(regexprep(two, 'a.b', 'X', 'dotall'), 'X'));
        assert(strcmp(regexprep(two, 'a.b', 'X', 'dotexceptnewline'), two));
        assert(numel(regexp(two, 'a.b', 'match')) == 1);
        assert(isempty(regexp(two, 'a.b', 'match', 'dotexceptnewline')));
        """);

    [Fact]
    public Task AnchorsAndSpacingFollowTheirOwnWords() => RunAsserting("""
        two = sprintf('a\nb');
        assert(strcmp(regexprep(two, '^b', 'X', 'lineanchors'), sprintf('a\nX')));
        assert(strcmp(regexprep(two, '^b', 'X'), two));
        assert(strcmp(regexprep(two, '^b', 'X', 'lineanchors', 'stringanchors'), two));
        assert(strcmp(regexprep('ab', 'a b', 'X', 'freespacing'), 'X'));
        assert(strcmp(regexprep('ab', 'a b', 'X'), 'ab'));
        """);

    /// <summary>MATLAB's own documented example for <c>'preservecase'</c>, word for word.</summary>
    [Fact]
    public Task PreserveCaseGivesTheReplacementTheCaseItReplaced() => RunAsserting("""
        assert(strcmp(regexprep('Hello HELLO hello', 'hello', 'bye', 'preservecase'), 'Bye BYE bye'));
        assert(strcmp(regexprep('AbC', 'abc', 'xyz', 'ignorecase'), 'xyz'));
        assert(strcmp(regexprep('AbC', 'abc', 'xyz', 'matchcase'), 'AbC'));
        assert(strcmp(regexprep('AbC', 'abc', 'xyz'), 'AbC'));
        """);

    [Fact]
    public Task AMisspelledOptionWordNamesTheRealOnes() => RunAsserting("""
        ok = 0;
        try
            regexprep('a', 'a', 'b', 'ignorcase');
        catch err
            ok = ok + ~isempty(strfind(err.message, 'ignorecase'));
        end
        % An output word belongs to regexp, not to regexprep, so it is unknown here.
        try
            regexprep('a', 'a', 'b', 'tokens');
        catch err
            ok = ok + ~isempty(strfind(err.message, 'unknown option'));
        end
        try
            strsplit('a', 'b', 'DelimiterType', 'Fancy');
        catch err
            ok = ok + ~isempty(strfind(err.message, 'RegularExpression'));
        end
        assert(ok == 3);
        """);

    // --- cellfun ----------------------------------------------------------------------------------

    [Fact]
    public Task ApplyingAcrossSeveralCellsWalksThemTogether() => RunAsserting("""
        assert(isequal(cellfun(@(x) x^2, {1, 2, 3}), [1 4 9]));
        assert(isequal(cellfun(@(a, b) a + b, {1, 2, 3}, {10, 20, 30}), [11 22 33]));
        assert(cellfun(@(a, b, c) a * b + c, {2}, {3}, {4}) == 10);
        """);

    [Fact]
    public Task TheAnswerKeepsTheShapeOfTheCellItCameFrom() => RunAsserting("""
        m = cellfun(@(x) x * 2, {1 2; 3 4});
        assert(isequal(size(m), [2 2]));
        assert(m(2, 1) == 6);
        c = cellfun(@(x) x * 2, {1 2; 3 4}, 'UniformOutput', false);
        assert(isequal(size(c), [2 2]));
        assert(c{2, 1} == 6);
        """);

    /// <summary>
    /// Single characters join into a char row rather than failing the uniform-output rule, which is
    /// what a script asking for every name's initial is after.
    /// </summary>
    [Fact]
    public Task SingleCharacterAnswersJoinIntoAWord() => RunAsserting("""
        assert(strcmp(cellfun(@(s) s(1), {'apple', 'banana', 'cherry'}), 'abc'));
        """);

    [Fact]
    public Task AskingForTwoOutputsAsksEachElementForTwo() => RunAsserting("""
        [lo, hi] = cellfun(@ends, {[3 1 2], [9 7 8]});
        assert(isequal(lo, [1 7]));
        assert(isequal(hi, [3 9]));
        % A handle that only wraps the call passes the count through, so @(v) ends(v) is the same.
        [lo2, hi2] = cellfun(@(v) ends(v), {[3 1 2], [9 7 8]});
        assert(isequal(lo2, lo));
        assert(isequal(hi2, hi));

        function [a, b] = ends(v)
            a = min(v);
            b = max(v);
        end
        """);

    /// <summary>
    /// The names that predate function handles. Each one maps onto a builtin JGraph already has, so
    /// the answers cannot drift from what the spelled-out call gives.
    /// </summary>
    [Fact]
    public Task TheLegacyNamesAnswerTheSameQuestionsTheirBuiltinsDo() => RunAsserting("""
        assert(isequal(cellfun('isempty', {[], 1, '', 'a'}), [true false true false]));
        assert(isequal(cellfun('length', {[1 2 3], 'ab', {}}), [3 2 0]));
        assert(isequal(cellfun('ndims', {1, [1 2; 3 4]}), [2 2]));
        assert(isequal(cellfun('prodofsize', {[1 2 3], [1 2; 3 4]}), [3 4]));
        assert(isequal(cellfun('size', {[1 2 3], [1 2; 3 4]}, 1), [1 2]));
        assert(isequal(cellfun('size', {[1 2 3], [1 2; 3 4]}, 2), [3 2]));
        assert(isequal(cellfun('isclass', {1, 'a', true}, 'char'), [false true false]));
        assert(isequal(cellfun('islogical', {1, true}), [false true]));
        assert(isequal(cellfun('isreal', {1, 1 + 2i}), [true false]));
        """);

    [Fact]
    public Task AnErrorHandlerAnswersForTheElementThatFailed() => RunAsserting("""
        r = cellfun(@(x) 1 / positive(x), {2, -1, 4}, 'ErrorHandler', @(s, x) -1);
        assert(isequal(r, [0.5 -1 0.25]));
        % The handler is given the failure first and then the same inputs, so it can report which
        % element it was standing in for.
        which = cellfun(@(x) positive(x), {-1}, 'ErrorHandler', @(s, x) s.index);
        assert(which == 1);
        told = cellfun(@(x) positive(x), {-1}, 'ErrorHandler', @(s, x) ~isempty(strfind(s.message, 'negative')));
        assert(told == 1);

        function y = positive(x)
            if x < 0
                error('negative input');
            end
            y = x;
        end
        """);

    [Fact]
    public Task TheCellOptionsSayWhatTheyWantedWhenTheyAreWrong() => RunAsserting("""
        ok = 0;
        try
            cellfun(@(x) [x x], {1, 2});
        catch err
            ok = ok + ~isempty(strfind(err.message, 'UniformOutput'));
        end
        try
            cellfun('bogus', {1});
        catch err
            ok = ok + ~isempty(strfind(err.message, 'prodofsize'));
        end
        try
            cellfun(@(a, b) a, {1, 2}, {1});
        catch err
            ok = ok + ~isempty(strfind(err.message, 'same number'));
        end
        try
            cellfun(@(x) x, {1}, 'UniformOutpt', false);
        catch err
            ok = ok + ~isempty(strfind(err.message, 'ErrorHandler'));
        end
        try
            cellfun('size', {1});
        catch err
            ok = ok + ~isempty(strfind(err.message, 'dimension'));
        end
        assert(ok == 5);
        """);

    // --- num2str ----------------------------------------------------------------------------------

    /// <summary>
    /// One number, MATLAB's own significant-digit rule: five for a value near 1, more as the magnitude
    /// grows. <c>num2str(pi)</c> reading <c>3.1416</c> rather than <c>3.14159</c> is that rule.
    /// </summary>
    [Fact]
    public Task OneNumberTakesMatlabsSignificantDigits() => RunAsserting("""
        assert(strcmp(num2str(3.5), '3.5'));
        assert(strcmp(num2str(pi), '3.1416'));
        assert(strcmp(num2str(42), '42'));
        assert(strcmp(num2str(-7), '-7'));
        assert(strcmp(num2str(true), '1'));
        assert(strcmp(num2str(1234.5678), '1234.5678'));
        assert(strcmp(num2str(0.000012345), '1.2345e-05'));
        """);

    [Fact]
    public Task AnArrayIsLaidOutInColumns() => RunAsserting("""
        assert(strcmp(num2str([1 2 3]), '1  2  3'));
        % NaN and Inf are spelled, so the column has to be wide enough for the word.
        assert(strcmp(num2str([1 NaN Inf]), '1  NaN  Inf'));
        assert(strcmp(strtrim(num2str([1.5 2.25])), '1.5        2.25'));
        """);

    /// <summary>
    /// A matrix answers a char matrix in MATLAB. JGraph has no char matrix, so several rows come back
    /// as a cell of strings — the rule <c>dec2bin</c> already follows — with the columns still aligned.
    /// </summary>
    [Fact]
    public Task SeveralRowsComeBackAsACharMatrixWithTheColumnsStillLinedUp() => RunAsserting("""
        % MATLAB answers a char matrix here, not a cell: the columns were lined up so that the rows
        % could stand one above the other, which is the one container that says so (M105).
        rows = num2str([1 20; 300 4]);
        assert(ischar(rows));
        assert(isequal(size(rows), [2 8]));
        assert(strcmp(rows(1,:), '  1   20'));
        assert(strcmp(rows(2,:), '300    4'));
        """);

    [Fact]
    public Task ASecondArgumentIsEitherADigitCountOrAFormat() => RunAsserting("""
        assert(strcmp(num2str(pi, 8), '3.1415927'));
        assert(strcmp(num2str(pi, 4), '3.142'));
        assert(strcmp(num2str(3.14159, '%08.3f'), '0003.142'));
        % Text is handed straight back rather than described.
        assert(strcmp(num2str('text'), 'text'));
        """);

    /// <summary>
    /// The layer <c>num2str</c> reads a small number through. C pads a <c>%g</c> exponent to two
    /// digits and .NET's "G" format does not; the difference showed up as
    /// <c>num2str(0.000012345)</c> reading <c>1.2345e-5</c> where every other language reads
    /// <c>1.2345e-05</c>.
    /// </summary>
    [Fact]
    public Task TheGeneralFormatWritesCsTwoDigitExponent() => RunAsserting("""
        assert(strcmp(sprintf('%g', 0.000012345), '1.2345e-05'));
        assert(strcmp(sprintf('%g', 1e-7), '1e-07'));
        assert(strcmp(sprintf('%g', 1.5e20), '1.5e+20'));
        assert(strcmp(sprintf('%g', 100000), '100000'));
        assert(strcmp(sprintf('%.3g', 1234), '1.23e+03'));
        """);
}
