using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// Strings and validators (M104): <c>append</c>, the between-verbs, <c>extract</c>,
/// <c>splitlines</c>, <c>strtok</c>, <c>strjust</c>, <c>strvcat</c>/<c>str2mat</c>,
/// <c>strmatch</c>, <c>isStringScalar</c>, <c>hex2num</c>/<c>num2hex</c>, <c>isvarname</c>, and
/// the four <c>mustBe…</c> names the validators folder still lacked.
/// </summary>
/// <remarks>
/// <para>
/// Assertions run inside the scripts, so what is pinned is MATLAB's answer rather than JGraph's
/// display format. Every answer was read off MATLAB R2024a on this machine. Where an answer turns
/// on this build's char-matrix model — whose <c>class</c> is <c>double</c> and whose <c>size</c> is
/// rows-by-one — the test pins the characters and not the class, and the difference is recorded in
/// ADR 0105.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabStringsM104Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabStringsM104Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private static Task<ScriptRunResult> Run(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private void AssertRan(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message + _output.ErrorText);

    private async Task Asserts(string code)
    {
        await using IScriptSession session = NewSession();
        AssertRan(await Run(session, code));
    }

    private async Task Refuses(string code, string identifier)
    {
        await using IScriptSession session = NewSession();
        AssertRan(await Run(session, $"""
            caught = '';
            try
                {code}
            catch err
                caught = err.identifier;
            end
            assert(strcmp(caught, '{identifier}'), ['got: ' caught]);
            """));
    }

    // --- append -------------------------------------------------------------------------------

    [Fact]
    public Task AppendKeepsTheWhitespaceStrcatTrims() => Asserts("""
        assert(strcmp(append('a  ', 'b'), 'a  b'));
        assert(strcmp(strcat('a  ', 'b'), 'ab'));
        assert(strcmp(append('a', 'b', 'c', 'd'), 'abcd'));
        assert(strcmp(append('ab', ''), 'ab'));
        assert(strcmp(append('a'), 'a'));
        """);

    [Fact]
    public Task AppendAnswersAStringIfAnyArgumentWasOne() => Asserts("""
        assert(ischar(append('ab', 'cd')));
        assert(isstring(append("ab", "cd")));
        assert(isstring(append('a', "b")));
        assert(iscell(append('a', {'b'})));
        assert(isstring(append('a', {'b'}, "c")));
        """);

    [Fact]
    public Task AppendExpandsItsArgumentsAgainstEachOther() => Asserts("""
        s = append(["a";"b"], "-x");
        assert(isequal(size(s), [2 1]) && s(1) == "a-x" && s(2) == "b-x");
        g = append(["a" "b"], ["x";"y"]);
        assert(isequal(size(g), [2 2]));
        assert(g(1,1) == "ax" && g(1,2) == "bx" && g(2,1) == "ay" && g(2,2) == "by");
        c = append({'a','b'}, {'x','y'});
        assert(strcmp(c{1}, 'ax') && strcmp(c{2}, 'by'));
        """);

    [Fact]
    public Task AppendRefusesWhatIsNotText() => Refuses(
        "append(1, 'a');", "MATLAB:string:MustBeCharCellArrayOrString");

    [Fact]
    public Task AppendOfNothingIsNotEnoughInputs() => Refuses(
        "append();", "MATLAB:narginchk:notEnoughInputs");

    // --- eraseBetween / replaceBetween --------------------------------------------------------

    [Fact]
    public Task MarkersBoundTheSpanExclusivelyAndPositionsInclusively() => Asserts("""
        assert(strcmp(eraseBetween('abcdefg', 'b', 'f'), 'abfg'));
        assert(strcmp(eraseBetween('abcdefg', 3, 5), 'abfg'));
        assert(strcmp(eraseBetween('abcdefg', 'b', 'f', 'Boundaries', 'inclusive'), 'ag'));
        assert(strcmp(eraseBetween('abcdefg', 3, 5, 'Boundaries', 'exclusive'), 'abcefg'));
        assert(strcmp(replaceBetween('abcdefg', 3, 5, '--'), 'ab--fg'));
        assert(strcmp(replaceBetween('abcdefg', 'b', 'f', '--', 'Boundaries', 'inclusive'), 'a--g'));
        """);

    [Fact]
    public Task ASpanNeverNestsBecauseTheScanResumesPastTheEndMarker() => Asserts("""
        assert(strcmp(eraseBetween('aXbXcXd', 'X', 'X'), 'aXXcXd'));
        assert(strcmp(replaceBetween('aXbXcXd', 'X', 'X', '-'), 'aX-XcXd'));
        assert(strcmp(eraseBetween('a1b a2b', 'a', 'b'), 'ab ab'));
        """);

    [Fact]
    public Task AMarkerThatIsNotThereChangesNothing() => Asserts("""
        assert(strcmp(eraseBetween('abc', 'x', 'y'), 'abc'));
        assert(strcmp(eraseBetween('abcdef', 'd', 'b'), 'abcdef'));
        assert(strcmp(eraseBetween('abc', '', ''), 'abc'));
        assert(strcmp(replaceBetween('abc', 'x', 'y', '-'), 'abc'));
        """);

    [Fact]
    public Task AnEmptySpanIsAllowedAtEitherEnd() => Asserts("""
        assert(strcmp(eraseBetween('abc', 1, 0), 'abc'));
        assert(strcmp(replaceBetween('abc', 1, 0, 'X'), 'Xabc'));
        assert(strcmp(replaceBetween('abc', 4, 3, 'X'), 'abcX'));
        assert(isempty(eraseBetween('abcd', 1, 4)));
        """);

    [Fact]
    public Task TheBetweenVerbsKeepTheContainerAndReadPositionsPerElement() => Asserts("""
        c = eraseBetween({'abcdef','xbcdy'}, 'b', 'd');
        assert(iscell(c) && strcmp(c{1}, 'abdef') && strcmp(c{2}, 'xbdy'));
        s = eraseBetween(["abcd";"wxyz"], [1;2], [2;3]);
        assert(isstring(s) && s(1) == "cd" && s(2) == "wz");
        r = replaceBetween(["abcd";"wxyz"], 2, 3, ["--";"++"]);
        assert(r(1) == "a--d" && r(2) == "w++z");
        assert(ischar(replaceBetween('abcdefg', 3, 5, "--")));
        """);

    [Fact]
    public Task APositionOutsideTheTextIsRefusedByName() => Refuses(
        "eraseBetween('abc', 2, 9);", "MATLAB:string:PositionOutOfRange");

    [Fact]
    public Task APositionBelowOneIsRefusedByName() => Refuses(
        "eraseBetween('abc', 0, 2);", "MATLAB:string:PositionMustBePositiveInteger");

    [Fact]
    public Task OnlyBoundariesNamesAnOptionHere() => Refuses(
        "eraseBetween('abc', 1, 2, 'Bounds', 'inclusive');",
        "MATLAB:string:UnrecognizedParameterName");

    [Fact]
    public Task BoundariesTakesOnlyItsTwoWords() => Refuses(
        "eraseBetween('abc', 1, 2, 'Boundaries', 'both');",
        "MATLAB:string:UnrecognizedParameterValue");

    // --- extract ------------------------------------------------------------------------------

    [Fact]
    public Task ExtractAnswersEveryNonOverlappingOccurrence() => Asserts("""
        c = extract('hello world', 'o');
        assert(iscell(c) && isequal(size(c), [2 1]));
        assert(strcmp(c{1}, 'o') && strcmp(c{2}, 'o'));
        assert(numel(extract('aaaa', 'aa')) == 2);
        assert(numel(extract('abc', '')) == 4);
        assert(isequal(size(extract('abc', 'z')), [0 1]));
        assert(isequal(size(extract('ab', 'abc')), [0 1]));
        """);

    [Fact]
    public Task ExtractAnswersTextOfTheKindItWasHandedExceptForAChar() => Asserts("""
        assert(iscell(extract('hello world', 'o')));
        assert(isstring(extract("hello world", "o")));
        assert(iscell(extract({'hello','world'}, 'o')));
        """);

    [Fact]
    public Task ExtractByPositionTakesOneCharacterPerElement() => Asserts("""
        c = extract('abcdef', 2);
        assert(strcmp(c{1}, 'b'));
        s = extract(["abc";"def"], [1;2]);
        assert(s(1) == "a" && s(2) == "e");
        """);

    [Fact]
    public Task ExtractSpreadsItsMatchesAlongANewDimension() => Asserts("""
        s = extract(["hello";"world"], "o");
        assert(isequal(size(s), [2 1]));
        g = extract(["oo";"yoo"], "o");
        assert(isequal(size(g), [2 2]));
        """);

    [Fact]
    public Task EveryElementMustYieldTheSameNumberOfMatches() => Refuses(
        "extract([\"hello\",\"xoxo\"], \"o\");", "MATLAB:string:MustHaveSameNumberOf");

    [Fact]
    public Task APositionArgumentMustMatchTheTextOrBeOne() => Refuses(
        "extract('abcdef', 2:4);", "MATLAB:string:InvalidArgumentSize");

    // --- splitlines ---------------------------------------------------------------------------

    [Fact]
    public Task SplitlinesBreaksOnTheCarriageReturnFamilyAndNothingElse() => Asserts("""
        assert(numel(splitlines(sprintf('a\nb'))) == 2);
        assert(numel(splitlines(sprintf('a\r\nb'))) == 2);
        assert(numel(splitlines(sprintf('a\rb'))) == 2);
        assert(numel(splitlines(sprintf('a\n\nb'))) == 3);
        assert(numel(splitlines(sprintf('a\vb'))) == 1);
        assert(numel(splitlines(sprintf('a\fb'))) == 1);
        assert(numel(splitlines(['a' char(8232) 'b'])) == 1);
        """);

    [Fact]
    public Task SplitlinesKeepsTheEmptyPieceAfterATrailingBreak() => Asserts("""
        c = splitlines(sprintf('a\n'));
        assert(numel(c) == 2 && strcmp(c{1}, 'a') && isempty(c{2}));
        """);

    [Fact]
    public Task SplitlinesAnswersACellForACharAndAStringForAString() => Asserts("""
        assert(iscell(splitlines(sprintf('a\nb'))));
        assert(isequal(size(splitlines(sprintf('a\nb'))), [2 1]));
        assert(isstring(splitlines("")) && isequal(size(splitlines("")), [1 1]));
        s = splitlines([string(sprintf('a\nb')); string(sprintf('c\nd'))]);
        assert(isequal(size(s), [2 2]) && s(1,1) == "a" && s(2,2) == "d");
        """);

    [Fact]
    public Task SplitlinesWillNotSplitACharMatrix() => Refuses(
        "splitlines(['ab';'cd']);", "MATLAB:string:MustBeCharCellArrayOrString");

    // --- strtok -------------------------------------------------------------------------------

    [Fact]
    public Task StrtokSkipsLeadingDelimitersAndLeavesTheOneThatStopped() => Asserts("""
        [t, r] = strtok('  hello world  ');
        assert(strcmp(t, 'hello') && strcmp(r, ' world  '));
        [t2, r2] = strtok(',,a,b', ',');
        assert(strcmp(t2, 'a') && strcmp(r2, ',b'));
        [t3, r3] = strtok('a1b2c', '12');
        assert(strcmp(t3, 'a') && strcmp(r3, '1b2c'));
        """);

    [Fact]
    public Task StrtokOfNothingAnswersNothingTwice() => Asserts("""
        [t, r] = strtok('');
        assert(isempty(t) && isempty(r) && ischar(t));
        assert(isempty(strtok('  ')));
        [t2, r2] = strtok('abc', '');
        assert(strcmp(t2, 'abc') && isempty(r2));
        """);

    [Fact]
    public Task StrtokMapsOverAContainerAndKeepsIt() => Asserts("""
        [t, r] = strtok(["a b";"c d"]);
        assert(isstring(t) && isequal(size(t), [2 1]) && t(1) == "a" && t(2) == "c");
        [c, ~] = strtok({'a b','c d'});
        assert(iscell(c) && strcmp(c{1}, 'a') && strcmp(c{2}, 'c'));
        """);

    // --- strjust ------------------------------------------------------------------------------

    [Fact]
    public Task StrjustMovesEachRowsCharactersWithoutChangingItsWidth() => Asserts("""
        A = ['  ab'; 'c   '; ' de '];
        R = strjust(A);
        assert(strcmp(R(1,:), '  ab') && strcmp(R(2,:), '   c') && strcmp(R(3,:), '  de'));
        L = strjust(A, 'left');
        assert(strcmp(L(1,:), 'ab  ') && strcmp(L(2,:), 'c   ') && strcmp(L(3,:), 'de  '));
        C = strjust(A, 'center');
        assert(strcmp(C(1,:), ' ab ') && strcmp(C(2,:), ' c  ') && strcmp(C(3,:), ' de '));
        """);

    [Fact]
    public Task StrjustCountsANullAsABlank() => Asserts("""
        assert(isequal(double(strjust([char(0) 'a  '], 'right')), [32 32 32 97]));
        """);

    [Fact]
    public Task StrjustKeepsTheContainerItWasHanded() => Asserts("""
        c = strjust({'  a','b  '});
        assert(iscell(c) && strcmp(c{1}, '  a') && strcmp(c{2}, '  b'));
        s = strjust(["  a";"b  "]);
        assert(isstring(s) && s(1) == "  a" && s(2) == "  b");
        """);

    [Fact]
    public Task StrjustTakesOnlyItsThreeSides() => Refuses(
        "strjust('ab', 'middle');", "MATLAB:strjust:UnknownParameter");

    // --- strvcat / str2mat --------------------------------------------------------------------

    [Fact]
    public Task StrvcatLeavesOutABlankArgumentAndStr2matKeepsIt() => Asserts("""
        A = strvcat('a', '', 'b');
        assert(size(A, 1) == 2 && strcmp(A(1,:), 'a') && strcmp(A(2,:), 'b'));
        B = str2mat('a', '', 'b');
        assert(size(B, 1) == 3 && strcmp(B(1,:), 'a') && strcmp(B(2,:), ' ') && strcmp(B(3,:), 'b'));
        """);

    [Fact]
    public Task StrvcatPadsToTheLongestRow() => Asserts("""
        A = strvcat('a', 'bcd');
        assert(strcmp(A(1,:), 'a  ') && strcmp(A(2,:), 'bcd'));
        C = strvcat({'a','bcd'});
        assert(strcmp(C(1,:), 'a  ') && strcmp(C(2,:), 'bcd'));
        S = strvcat(["a";"bcd"]);
        assert(strcmp(S(1,:), 'a  ') && strcmp(S(2,:), 'bcd'));
        """);

    [Fact]
    public Task StrvcatReadsANumberAsItsCodePointAndAMatrixAsItsRows() => Asserts("""
        assert(strcmp(strvcat(65), 'A'));
        A = strvcat('ab', [67 68]);
        assert(strcmp(A(1,:), 'ab') && strcmp(A(2,:), 'CD'));
        B = strvcat(['ab';'cd'], 'x');
        assert(size(B, 1) == 3 && strcmp(B(3,:), 'x '));
        assert(isempty(strvcat('')));
        assert(strcmp(strvcat({'a',''}), 'a'));
        """);

    // --- strmatch -----------------------------------------------------------------------------

    [Fact]
    public Task StrmatchFindsTheRowsThatBeginWithTheTextSought() => Asserts("""
        assert(isequal(strmatch('max', {'max','minimax','maximum'}), [1;3]));
        assert(isequal(strmatch('max', {'max','minimax','maximum'}, 'exact'), 1));
        assert(isequal(strmatch('ap', ['apple ';'answer';'apply ']), [1;3]));
        assert(isequal(strmatch('', {'a','b'}), [1;2]));
        assert(isequal(strmatch("max", ["max";"maximum"]), [1;2]));
        """);

    [Fact]
    public Task StrmatchPadsBothSidesToTheListsWidth() => Asserts("""
        assert(isequal(strmatch('ap ', {'apple','ap'}), 2));
        assert(isequal(strmatch('ap ', {'apple','ap'}, 'exact'), 2));
        assert(isempty(strmatch('apple', {'ap'})));
        assert(isequal(size(strmatch('zz', {'a','b'})), [0 0]));
        """);

    // --- isStringScalar -----------------------------------------------------------------------

    [Fact]
    public Task IsStringScalarIsTrueOnlyForAOneElementStringArray() => Asserts("""
        assert(isStringScalar("a"));
        assert(isStringScalar(""));
        assert(~isStringScalar('a'));
        assert(~isStringScalar(["a" "b"]));
        assert(~isStringScalar({'a'}));
        assert(~isStringScalar(1));
        assert(~isStringScalar(strings(0,0)));
        assert(islogical(isStringScalar("a")));
        """);

    // --- num2hex / hex2num --------------------------------------------------------------------

    [Fact]
    public Task Num2hexSpellsTheBitsOfADouble() => Asserts("""
        assert(strcmp(num2hex(1), '3ff0000000000000'));
        assert(strcmp(num2hex(pi), '400921fb54442d18'));
        assert(strcmp(num2hex(-1), 'bff0000000000000'));
        assert(strcmp(num2hex(0), '0000000000000000'));
        assert(strcmp(num2hex(-0), '8000000000000000'));
        assert(strcmp(num2hex(Inf), '7ff0000000000000'));
        assert(strcmp(num2hex(-Inf), 'fff0000000000000'));
        assert(strcmp(num2hex(NaN), 'fff8000000000000'));
        assert(strcmp(num2hex(eps), '3cb0000000000000'));
        assert(strcmp(num2hex(realmin), '0010000000000000'));
        """);

    [Fact]
    public Task Num2hexSpellsASingleInEightDigitsAndAnArrayOneRowAtATime() => Asserts("""
        assert(strcmp(num2hex(single(1)), '3f800000'));
        assert(strcmp(num2hex(single(pi)), '40490fdb'));
        A = num2hex([1 2; 3 4]);
        assert(size(A, 1) == 4);
        assert(strcmp(A(1,:), '3ff0000000000000') && strcmp(A(2,:), '4008000000000000'));
        assert(strcmp(A(3,:), '4000000000000000') && strcmp(A(4,:), '4010000000000000'));
        """);

    [Fact]
    public Task Num2hexRefusesWhatIsNotFloatingPoint() => Refuses(
        "num2hex(int8(1));", "MATLAB:num2hex:floatpointInput");

    [Fact]
    public Task Num2hexRefusesAComplexNumber() => Refuses(
        "num2hex(1 + 2i);", "MATLAB:num2hex:realInput");

    [Fact]
    public Task Hex2numPadsAShortSpellingOnTheRight() => Asserts("""
        assert(hex2num('3ff0000000000000') == 1);
        assert(hex2num('3ff') == 1);
        assert(hex2num('4') == 2);
        assert(hex2num('3FF0000000000000') == 1);
        assert(hex2num('400921fb54442d18') == pi);
        assert(hex2num('bff0000000000000') == -1);
        assert(isnan(hex2num('fff8000000000000')));
        assert(isinf(hex2num('7ff0000000000000')));
        assert(hex2num('3ff00000000000001') == 1);
        assert(abs(hex2num('40490fdb') - 50.123870849609375) < 1e-12);
        """);

    [Fact]
    public Task Hex2numAnswersAColumnForSeveralSpellings() => Asserts("""
        assert(isequal(hex2num(['3ff0000000000000';'4000000000000000']), [1;2]));
        assert(isequal(hex2num({'3ff0000000000000','4000000000000000'}), [1;2]));
        assert(isequal(hex2num(["3ff0000000000000";"4000000000000000"]), [1;2]));
        assert(isequal(hex2num({'3ff','4000000000000000'}), [1;2]));
        assert(isequal(size(hex2num('')), [0 0]));
        assert(hex2num(num2hex(pi)) == pi);
        """);

    [Fact]
    public Task Hex2numRefusesADigitItCannotRead() => Refuses(
        "hex2num('zz');", "MATLAB:hex2num:OutOfRange");

    [Fact]
    public Task Hex2numRefusesWhatIsNotText() => Refuses(
        "hex2num(1);", "MATLAB:hex2num:InputMustBeString");

    // --- isvarname ----------------------------------------------------------------------------

    [Fact]
    public Task IsvarnameAsksTheThreeQuestionsMatlabAsks() => Asserts("""
        assert(isvarname('x'));
        assert(isvarname('x_1'));
        assert(~isvarname('1x'));
        assert(~isvarname('_x'));
        assert(~isvarname('x y'));
        assert(~isvarname('for'));
        assert(~isvarname(''));
        """);

    [Fact]
    public Task ANameMayBeSixtyThreeCharactersAndNoMore() => Asserts("""
        sixtyThree = repmat('a', 1, 63);
        sixtyFour = [sixtyThree 'a'];
        assert(numel(sixtyThree) == 63 && numel(sixtyFour) == 64);
        assert(isvarname(char(sixtyThree)));
        assert(~isvarname(char(sixtyFour)));
        assert(namelengthmax == 63);
        """);

    // --- the four validators ------------------------------------------------------------------

    [Fact]
    public Task MustBeNonsparseAcceptsEverythingThatIsNotSparse() => Asserts("""
        mustBeNonsparse(1);
        mustBeNonsparse([1 2]);
        mustBeNonsparse('a');
        mustBeNonsparse("a");
        mustBeNonsparse({1});
        mustBeNonsparse([]);
        mustBeNonsparse(true);
        """);

    [Fact]
    public Task MustBeNonsparseRefusesASparseMatrix() => Refuses(
        "mustBeNonsparse(sparse(1));", "MATLAB:validators:mustBeNonsparse");

    [Fact]
    public Task MustBeValidVariableNameAcceptsEveryNameAVariableCouldHave() => Asserts("""
        mustBeValidVariableName('x');
        mustBeValidVariableName("x");
        mustBeValidVariableName({'x'});
        mustBeValidVariableName(["a" "b"]);
        """);

    [Fact]
    public Task MustBeValidVariableNameNamesEveryNameThatFailed() => Asserts("""
        caught = '';
        try
            mustBeValidVariableName({'1x','2y','3z'});
        catch err
            caught = err.message;
        end
        assert(strcmp(caught, ...
            'The following are not valid variable names: ''1x'', ''2y'', and ''3z''.'), caught);
        caught2 = '';
        try
            mustBeValidVariableName({'1x','2y'});
        catch err
            caught2 = err.message;
        end
        assert(strcmp(caught2, ...
            'The following are not valid variable names: ''1x'' and ''2y''.'), caught2);
        """);

    [Fact]
    public Task MustBeValidVariableNameRefusesAKeyword() => Refuses(
        "mustBeValidVariableName('for');", "MATLAB:validators:mustBeValidVariableName");

    [Fact]
    public Task TextWithNoCharactersFailsTheEarlierCheck() => Refuses(
        "mustBeValidVariableName('');", "MATLAB:validators:mustBeNonzeroLengthText");

    [Fact]
    public Task SoDoesAValueThatIsNotTextAtAll() => Refuses(
        "mustBeValidVariableName(1);", "MATLAB:validators:mustBeNonzeroLengthText");

    [Fact]
    public Task MustBeFileAndMustBeFolderRefuseWhatIsNotThere() => Task.WhenAll(
        Refuses("mustBeFile('no_such_file_xyz.txt');", "MATLAB:validators:mustBeFile"),
        Refuses("mustBeFolder('no_such_dir_xyz');", "MATLAB:validators:mustBeFolder"),
        Refuses("mustBeFolder(1);", "MATLAB:validators:mustBeNonzeroLengthText"));

    [Fact]
    public Task MustBeFolderAcceptsAFolderThatIsThere() => Asserts("""
        mustBeFolder(pwd);
        mustBeFolder('.');
        """);

    [Fact]
    public Task MustBeFileRefusesAFolderBecauseItIsNotAFile() => Refuses(
        "mustBeFile(pwd);", "MATLAB:validators:mustBeFile");

    // --- the family the four joined -----------------------------------------------------------

    [Fact]
    public Task EveryValidatorNowCarriesMatlabsOwnIdentifier() => Task.WhenAll(
        Refuses("mustBePositive(-1);", "MATLAB:validators:mustBePositive"),
        Refuses("mustBeNonempty([]);", "MATLAB:validators:mustBeNonempty"),
        Refuses("mustBeInteger(1.5);", "MATLAB:validators:mustBeInteger"),
        Refuses("mustBeGreaterThan(1, 2);", "MATLAB:validators:mustBeGreaterThan"),
        Refuses("mustBeInRange(5, 1, 3);", "MATLAB:validators:mustBeInRange"),
        Refuses("mustBeA(1, 'char');", "MATLAB:validators:mustBeA"),
        Refuses("mustBeFloat(int8(1));", "MATLAB:validators:mustBeFloat"));

    [Fact]
    public Task AValidatorsSentenceIsMatlabsOwn() => Asserts("""
        function said = why(f)
            said = '';
            try
                f();
            catch err
                said = err.message;
            end
        end
        assert(strcmp(why(@() mustBeNonempty([])), 'Value must not be empty.'));
        assert(strcmp(why(@() mustBeNonzero(0)), 'Value must not be zero.'));
        assert(strcmp(why(@() mustBeInteger(1.5)), 'Value must be integer.'));
        assert(strcmp(why(@() mustBeTextScalar(1)), ...
            'Value must be a character vector or string scalar.'));
        assert(strcmp(why(@() mustBeVector([1 2; 3 4])), ...
            'Value must be a 1-by-n vector or an n-by-1 vector.'));
        assert(strcmp(why(@() mustBeInRange(5, 1, 3)), ...
            'Value must be greater than or equal to 1, and less than or equal to 3.'));
        assert(strcmp(why(@() mustBeA(1, 'char')), ...
            'Value must be one of the following types: ''char''.'));
        """);

    // --- the defect this milestone found ------------------------------------------------------

    [Fact]
    public Task ASemicolonInABracketOfStringsStillMakesAStringArray() => Asserts("""
        v = ["a"; "b"];
        assert(isstring(v) && isequal(size(v), [2 1]) && v(2) == "b");
        m = ["ab" "cd"; "ef" "gh"];
        assert(isstring(m) && isequal(size(m), [2 2]) && m(2,1) == "ef" && m(1,2) == "cd");
        stacked = [["a";"b"]; "c"];
        assert(isstring(stacked) && isequal(size(stacked), [3 1]) && stacked(3) == "c");
        wide = [["a" "b"]; ["c" "d"]];
        assert(isstring(wide) && isequal(size(wide), [2 2]) && wide(2,2) == "d");
        mixed = ["a"; 'b'];
        assert(isstring(mixed) && isequal(size(mixed), [2 1]) && mixed(2) == "b");
        """);
}
