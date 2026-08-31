using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M105 — the char matrix. <c>char('a', 'bcd')</c> and <c>['ab'; 'cd']</c> stop being a column of
/// char rows and become what MATLAB means by one: a 2-D array of characters.
/// </summary>
/// <remarks>
/// <para>
/// The old representation was an N-by-1 array whose elements were whole char rows, and it answered
/// every question about itself for the container rather than for the characters: <c>class</c> said
/// <c>double</c>, <c>size</c> said N-by-1, <c>numel</c> counted rows, and <c>A(:, 2)</c> raised an
/// index error because there was only ever one column to ask for.
/// </para>
/// <para>
/// What replaces it is an ordinary numeric array of code points wearing a tag, which is the same
/// shape of answer M47 gave the integer classes and M63 gave the string array. It is the reason
/// almost nothing below needed a line of its own: indexing, <c>size</c>, <c>numel</c>, the
/// transpose, <c>double</c> and the flatten are the array machinery that was already there, reading
/// a real 2-D shape. Every value here is MATLAB R2024a's own.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabCharMatrixTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabCharMatrixTests() => JG.Reset();

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

    // --- what it is -------------------------------------------------------------------------------

    /// <summary>The bug this milestone was opened for: the class and the size were both the container's.</summary>
    [Fact]
    public Task AStackOfCharRowsIsCharAndMeasuresItsCharacters() => RunAsserting("""
        A = char('a', 'bcd');
        assert(strcmp(class(A), 'char'));
        assert(isequal(size(A), [2 3]));
        assert(size(A, 1) == 2 && size(A, 2) == 3);
        assert(ndims(A) == 2);
        % numel counts characters and length is the longest dimension — 2 and 2 before M105.
        assert(numel(A) == 6);
        assert(length(A) == 3);
        assert(ischar(A));
        assert(~isnumeric(A) && ~isstring(A) && ~iscellstr(A));
        assert(~isempty(A) && ~isrow(A));
        """);

    /// <summary>Every builder of a char matrix answers the same kind of value, not just <c>char</c>.</summary>
    [Fact]
    public Task EveryCharMatrixBuilderAnswersACharMatrix() => RunAsserting("""
        assert(isequal(size(char('a', 'bcd')), [2 3]));
        assert(isequal(size(strvcat('a', 'bcd')), [2 3]));
        assert(isequal(size(str2mat('a', 'bcd')), [2 3]));
        assert(isequal(size(strjust(char('a', 'bcd'), 'right')), [2 3]));
        assert(isequal(size(char(["a"; "bbb"])), [2 3]));
        assert(isequal(size(cellstr(char('a', 'bcd'))), [2 1]));

        % num2str lines its columns up so the rows can stand one above the other, which makes it a
        % char matrix and not the cell it used to answer.
        n = num2str([1 20; 300 4]);
        assert(ischar(n) && isequal(size(n), [2 8]));
        assert(strcmp(n(1,:), '  1   20'));
        """);

    // --- reading it -------------------------------------------------------------------------------

    /// <summary>
    /// Indexing needed one line, because a char matrix is a numeric array of code points: the gather,
    /// the bounds and the shape were already right, and only the characters had to be given back.
    /// </summary>
    [Fact]
    public Task IndexingReadsCharactersAndKeepsThemChar() => RunAsserting("""
        A = char('a', 'bcd');
        assert(strcmp(A(1,:), 'a  '));
        assert(strcmp(A(2,:), 'bcd'));
        assert(strcmp(A(end,:), 'bcd'));

        % A whole column: two characters, one above the other — an index error before M105.
        c = A(:,2);
        assert(ischar(c) && isequal(size(c), [2 1]));
        assert(isequal(double(c), [32; 99]));

        % One element is a one-character char row, by either kind of subscript.
        assert(strcmp(A(2,2), 'c'));
        assert(strcmp(A(1,2), ' '));
        assert(strcmp(A(3), ' '));

        % A(:) flattens down the columns, which is the order the characters are stored in.
        flat = A(:);
        assert(ischar(flat) && isequal(size(flat), [6 1]));
        assert(strcmp(reshape(flat, 1, []), 'ab c d'));
        """);

    /// <summary>A transpose carries the tag, so <c>A'</c> is char rather than the codes underneath.</summary>
    [Fact]
    public Task ATransposeIsStillCharAndSwapsTheTwoDimensions() => RunAsserting("""
        A = char('a', 'bcd');
        T = A';
        assert(ischar(T));
        assert(isequal(size(T), [3 2]));
        assert(strcmp(T(1,:), 'ab'));
        assert(strcmp(reshape(T, 1, []), 'a  bcd'));
        """);

    /// <summary><c>double</c> reads the code points, which is the other half of what char means.</summary>
    [Fact]
    public Task TheCodePointsAreReadableAsNumbers() => RunAsserting("""
        A = char('a', 'bcd');
        assert(isequal(double(A), [97 32 32; 98 99 100]));
        assert(strcmp(class(double(A)), 'double'));

        % And char of a char matrix is that char matrix, not its code points run together — which is
        % what char(char('a','bcd')) answered before, a 1-by-6.
        assert(isequal(size(char(A)), [2 3]));
        """);

    // --- building it ------------------------------------------------------------------------------

    /// <summary>
    /// A bracket stacks char rows the way MATLAB's concatenation does — and pads nothing, which is
    /// the difference between a bracket and <c>char</c>.
    /// </summary>
    [Fact]
    public Task ABracketStacksCharRowsIntoACharMatrix() => RunAsserting("""
        B = ['ab'; 'cd'];
        assert(strcmp(class(B), 'char'));
        assert(isequal(size(B), [2 2]));
        assert(strcmp(B(2,:), 'cd'));

        A = char('a', 'bcd');
        assert(isequal(size([A; 'xy ']), [3 3]));
        assert(isequal(size([A, A]), [2 6]));
        assert(strcmp(reshape([A, A]', 1, []), 'a  a  bcdbcd'));

        % A single row of char rows is still one char row, and an empty contributes nothing.
        assert(strcmp(['ab' 'cd'], 'abcd'));
        assert(strcmp(['ab'; []], 'ab'));
        """);

    /// <summary>
    /// A bracket pads nothing, so rows that do not line up are refused with MATLAB's own message —
    /// <c>char</c> and <c>strvcat</c> are the two verbs that pad.
    /// </summary>
    [Fact]
    public Task ABracketRefusesRowsThatDoNotLineUp() => RunAsserting("""
        threw = false;
        try
            B = ['a'; 'bcd'];
        catch err
            threw = true;
            assert(strcmp(err.identifier, 'MATLAB:catenate:dimensionMismatch'));
        end
        assert(threw);
        """);

    // --- text that came out of one ----------------------------------------------------------------

    /// <summary>
    /// The verbs that take a container of text answer in the container they were handed (M104), and a
    /// char matrix is one of the three — so a row read out of one is an ordinary char row again.
    /// </summary>
    [Fact]
    public Task TextVerbsAnswerACharMatrixWithACharMatrix() => RunAsserting("""
        A = char('a', 'bcd');

        % cellstr takes the padding back off, which is what makes it the way out of a char matrix.
        cs = cellstr(A);
        assert(iscell(cs) && isequal(size(cs), [2 1]));
        assert(strcmp(cs{1}, 'a') && strcmp(cs{2}, 'bcd'));

        % strjust moves the padding to the other side and keeps the shape.
        r = strjust(A, 'right');
        assert(ischar(r) && isequal(size(r), [2 3]));
        assert(strcmp(r(1,:), '  a'));

        assert(strmatch('bc', A) == 2);

        % %s reads the characters in storage order, which is MATLAB's column-major run.
        assert(strcmp(sprintf('%s', A), 'ab c d'));
        """);

    // --- code points keep their shape (M119) ------------------------------------------------------

    /// <summary>
    /// <c>char</c> of a matrix of code points reads each of its rows as a row of characters. It used
    /// to read the storage order end to end and answer one long row, which lost the shape at
    /// construction — so the answer had no second row for a subscript to ask about, and the error
    /// text named row-and-column indexing as supported while refusing it.
    /// </summary>
    [Fact]
    public Task CharOfANumericMatrixKeepsTheMatrixsShape() => RunAsserting("""
        M = [72 73 74; 75 76 77];
        A = char(M);
        assert(isequal(size(A), [2 3]));          % 1-by-6 before M119
        assert(strcmp(class(A), 'char'));
        assert(isequal(A, ['HIJ'; 'KLM']));
        assert(strcmp(A(2, :), 'KLM'));
        assert(strcmp(A(:, 2)', 'IL'));
        """);

    /// <summary>
    /// A column of code points is a column of characters, for the same reason. The row vector is the
    /// one shape that was already right, and it stays a plain char row rather than becoming a 1-by-N
    /// matrix — which is what MATLAB answers and what the rest of the text machinery expects.
    /// </summary>
    [Fact]
    public Task CharOfAVectorFollowsTheVectorsOrientation() => RunAsserting("""
        assert(isequal(size(char([72 73 74])), [1 3]));
        assert(strcmp(char([72 73 74]), 'HIJ'));

        down = char([72; 73; 74]);
        assert(isequal(size(down), [3 1]));
        assert(strcmp(class(down), 'char'));
        assert(isequal(down, ['H'; 'I'; 'J']));
        """);

    /// <summary>
    /// Round-tripping is what the shape is for: the code points of a char matrix are the matrix that
    /// built it, in the same places.
    /// </summary>
    [Fact]
    public Task TheCodePointsOfACharMatrixAreTheMatrixItWasBuiltFrom() => RunAsserting("""
        M = reshape(65:70, 2, 3);
        assert(isequal(double(char(M)), M));
        """);

    // --- the wrapper ------------------------------------------------------------------------------

    /// <summary>
    /// The tag is minted with the value and carried by the paths that mint a new wrapper, which is
    /// the trap every earlier tag fell into: a value that is char must not stop being char because it
    /// was copied.
    /// </summary>
    [Fact]
    public void TheTagIsCarriedByTheValueAndReadsItsRowsBack()
    {
        JgsValue matrix = JgsValue.CharMatrix(["a", "bcd"]);

        Assert.True(matrix.IsCharMatrix);
        Assert.Equal(2, matrix.Rows);
        Assert.Equal(3, matrix.Cols);
        Assert.Equal(6, matrix.ArrayLength);

        // Padded to the longest row, which is the only way a stack of unequal rows is rectangular.
        Assert.Equal(["a  ", "bcd"], matrix.CharMatrixRows());

        // Storage order is column-major, exactly as it is for every other array here.
        Assert.Equal("ab c d", matrix.CharMatrixText());
    }
}
