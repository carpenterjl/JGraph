using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The file forms M76 added: what <c>fopen</c> answers about a file it opened, the shapes and
/// precisions the binary readers take, the byte order they can be told to use, where a bounded read
/// leaves the file, and the table <c>textscan</c> reads into columns.
/// </summary>
[Collection("JG facade")]
public class MatlabM76FileFormTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("jgraph-m76-").FullName;

    public MatlabM76FileFormTests() => JG.Reset();

    public void Dispose()
    {
        JG.Reset();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A file the run left open on a slow handle release is not this test's business.
        }
    }

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(
            _output, (number, figure) => _figures.Add((number, figure)), _folder));

    private async Task RunAsserting(string code)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message + _output.ErrorText);
    }

    private async Task RunExpectingError(string code, string fragment)
    {
        await using IScriptSession session = NewSession();
        ScriptRunResult result = await session.ExecuteAsync(code, sourceId: "", CancellationToken.None);
        Assert.False(result.Success, "expected a refusal, got success");
        Assert.Contains(fragment, _output.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Three lines of numbers, which most of these read back one way or another.</summary>
    private const string WriteGrid = """
        fid = fopen('grid.txt', 'w');
        bytes = fprintf(fid, '1 2 3\n4 5 6\n7 8 9\n');
        fclose(fid);
        """;

    [Fact]
    public Task FprintfReportsHowManyBytesItWrote() => RunAsserting(WriteGrid + """
        assert(bytes == 18, 'six characters on each of three lines');
        """);

    [Fact]
    public Task FopenAnswersWhatItOpened() => RunAsserting(WriteGrid + """
        fid = fopen('grid.txt', 'r');
        assert(ischar(fopen(fid)), 'one output is the name it was opened under');

        [name, permission, format, encoding] = fopen(fid);
        assert(~isempty(strfind(name, 'grid.txt')));
        assert(strcmp(permission, 'r'));
        assert(strcmp(format, 'ieee-le'));
        assert(strcmp(encoding, 'UTF-8'));

        assert(isequal(fopen('all'), fid), 'the one open id');
        fclose(fid);
        assert(isempty(fopen('all')));
        """);

    [Fact]
    public Task FopenReportsWhyItCouldNotOpenAFile() => RunAsserting("""
        [fid, message] = fopen('no_such_file_at_all.txt', 'r');
        assert(fid == -1);
        assert(~isempty(message), 'and says why rather than only that');
        """);

    [Fact]
    public Task TheLineReadersAgreeAndFrewindGoesBack() => RunAsserting(WriteGrid + """
        fid = fopen('grid.txt', 'r');
        assert(strcmp(fgetl(fid), '1 2 3'));

        [line, terminator] = fgets(fid);
        assert(strcmp(strtrim(line), '4 5 6'));
        assert(terminator == 1, 'one character of line ending');

        frewind(fid);
        assert(strcmp(fgetl(fid), '1 2 3'), 'frewind puts the file back at its start');

        frewind(fid);
        assert(strcmp(fgets(fid, 3), '1 2'), 'and fgets stops after the characters asked for');
        fclose(fid);
        """);

    [Fact]
    public Task FgetlAtTheEnd_AnswersMinusOne() => RunAsserting(WriteGrid + """
        fid = fopen('grid.txt', 'r');
        fgetl(fid); fgetl(fid); fgetl(fid);
        last = fgetl(fid);
        assert(~ischar(last) && last == -1);
        fclose(fid);
        """);

    /// <summary>
    /// The change that made a bounded read mean anything: the file is left where the scan stopped,
    /// not at the end of it.
    /// </summary>
    [Fact]
    public Task ABoundedFscanf_LeavesTheFileWhereItStopped() => RunAsserting(WriteGrid + """
        fid = fopen('grid.txt', 'r');

        a = fscanf(fid, '%f', 3);
        assert(isequal(a, [1; 2; 3]), 'a count bounds the read and the answer is a column');

        [b, count] = fscanf(fid, '%f', [3 1]);
        assert(count == 3);
        assert(isequal(b, [4; 5; 6]), 'an asked-for shape is given, and reading went on from 4');

        rest = fscanf(fid, '%f');
        assert(isequal(rest, [7; 8; 9]));
        fclose(fid);
        """);

    [Fact]
    public Task TextscanReadsATableIntoColumns() => RunAsserting(WriteGrid + """
        fid = fopen('grid.txt', 'r');
        C = textscan(fid, '%f %f %f');
        fclose(fid);

        assert(numel(C) == 3, 'one cell per conversion');
        assert(isequal(C{1}, [1; 4; 7]));
        assert(isequal(C{2}, [2; 5; 8]));
        assert(isequal(C{3}, [3; 6; 9]));
        """);

    [Fact]
    public Task TextscanReadsFromTextAndCountsItsRepetitions() => RunAsserting("""
        C = textscan('a 1 b 2 c 3', '%s %d', 2);
        assert(numel(C) == 2);
        assert(numel(C{1}) == 2, 'two repetitions, not three');
        assert(strcmp(C{1}{1}, 'a') && strcmp(C{1}{2}, 'b'));
        assert(strcmp(class(C{2}), 'int32'), '%d reads an integer class');
        """);

    [Fact]
    public Task TextscanReadsItsOptions() => RunAsserting("""
        withHeader = textscan(sprintf('name value\n1 2\n3 4'), '%f %f', 'HeaderLines', 1);
        assert(isequal(withHeader{1}, [1; 3]));

        commas = textscan('1,2,3', '%f', 'Delimiter', ',');
        assert(isequal(commas{1}, [1; 2; 3]));

        quoted = textscan('"hello" 5', '%q %f');
        assert(strcmp(quoted{1}{1}, 'hello'));
        assert(quoted{2} == 5);

        together = textscan(sprintf('1 2\n3 4'), '%f %f', 'CollectOutput', true);
        assert(numel(together) == 1 && isequal(size(together{1}), [2 2]));
        """);

    [Fact]
    public Task TextscanReportsWhereItStopped() => RunAsserting(WriteGrid + """
        fid = fopen('grid.txt', 'r');
        [first, position] = textscan(fid, '%f %f %f', 1);
        assert(position > 0);
        assert(isequal(first{1}, 1), 'one repetition read the first row only');

        % The scan stops where the numbers stop, which is before the line ending rather than after
        % it — so the rest of that line is empty and the row after it is the next one.
        assert(isempty(strtrim(fgetl(fid))));
        assert(strcmp(strtrim(fgetl(fid)), '4 5 6'), 'reading goes on from there');
        fclose(fid);
        """);

    [Fact]
    public Task FreadAnswersAColumnAndTheCountItRead() => RunAsserting("""
        fid = fopen('bytes.bin', 'w');
        n = fwrite(fid, [65 66 67 68 69 70]);
        fclose(fid);
        assert(n == 6);

        fid = fopen('bytes.bin', 'r');
        [all, count] = fread(fid);
        fclose(fid);
        assert(count == 6);
        assert(isequal(all, [65; 66; 67; 68; 69; 70]), 'a column, as MATLAB answers');
        """);

    [Fact]
    public Task FreadTakesAShapeAndKeepsAClass() => RunAsserting("""
        fid = fopen('bytes.bin', 'w');
        fwrite(fid, [1 2 3 4 5 6]);
        fclose(fid);

        fid = fopen('bytes.bin', 'r');
        m = fread(fid, [2 Inf], 'uint8=>uint8');
        fclose(fid);
        assert(isequal(size(m), [2 3]), 'filled column by column');
        assert(strcmp(class(m), 'uint8'), 'and kept in the class it was read as');

        fid = fopen('bytes.bin', 'r');
        pair = fread(fid, [3 1]);
        fclose(fid);
        assert(isequal(pair, [1; 2; 3]));
        """);

    [Fact]
    public Task FreadSkipsTheBytesItIsToldTo() => RunAsserting("""
        fid = fopen('bytes.bin', 'w');
        fwrite(fid, [1 9 2 9 3 9]);
        fclose(fid);

        fid = fopen('bytes.bin', 'r');
        kept = fread(fid, 3, 'uint8', 1);
        fclose(fid);
        assert(isequal(kept, [1; 2; 3]), 'every other byte');
        """);

    /// <summary>
    /// The byte order genuinely acts: the same file read the other way round gives the swapped
    /// numbers rather than the same ones.
    /// </summary>
    [Fact]
    public Task TheByteOrderIsRealAndRoundTrips() => RunAsserting("""
        fid = fopen('big.bin', 'w', 'ieee-be');
        fwrite(fid, [1 256], 'uint16');
        fclose(fid);

        fid = fopen('big.bin', 'r', 'ieee-be');
        same = fread(fid, Inf, 'uint16');
        fclose(fid);
        assert(isequal(same, [1; 256]));

        fid = fopen('big.bin', 'r');
        swapped = fread(fid, Inf, 'uint16');
        fclose(fid);
        assert(isequal(swapped, [256; 1]), 'read little-endian the bytes come back the other way');
        """);

    [Fact]
    public Task FwriteWritesTheCharactersOfText() => RunAsserting("""
        fid = fopen('text.bin', 'w');
        n = fwrite(fid, 'AB');
        fclose(fid);
        assert(n == 2);

        fid = fopen('text.bin', 'r');
        back = fread(fid);
        fclose(fid);
        assert(isequal(back, [65; 66]));
        """);

    [Fact]
    public Task FerrorAnswersItsPairOfOutputs() => RunAsserting(WriteGrid + """
        fid = fopen('grid.txt', 'r');
        [message, number] = ferror(fid);
        fclose(fid);
        assert(isempty(message) && number == 0);
        """);

    [Fact]
    public Task ModesBeyondTheOriginalFourAreAccepted() => RunAsserting("""
        fid = fopen('update.txt', 'w+');
        fprintf(fid, 'hello');
        frewind(fid);
        assert(strcmp(fgetl(fid), 'hello'), 'w+ reads what it wrote');
        fclose(fid);
        """);

    [Fact]
    public Task TheBitPrecisionsAreRefusedByName() => RunAsserting("""
        fid = fopen('any.bin', 'w');
        fwrite(fid, [1 2 3]);
        fclose(fid);

        fid = fopen('any.bin', 'r');
        caught = '';
        try
            fread(fid, Inf, 'bit4');
        catch err
            caught = err.message;
        end

        fclose(fid);
        assert(~isempty(strfind(caught, 'counts bits')));
        """);

    [Theory]
    [InlineData("fgetl(99);", "not an open file")]
    [InlineData("textscan('a', '%f', 'Nope', 1);", "is not an option")]
    [InlineData("textscan('a', 'no conversions here');", "no conversions")]
    public Task TheFileVerbsRefuseByName(string code, string fragment) =>
        RunExpectingError(code, fragment);

    [Fact]
    public Task AnUnknownByteOrderOrEncodingIsRefused() => RunAsserting("""
        caught = 0;
        try
            fopen('x.txt', 'r', 'sideways');
        catch
            caught = caught + 1;
        end

        try
            fopen('x.txt', 'r', 'n', 'klingon');
        catch
            caught = caught + 1;
        end

        assert(caught == 2);
        """);
}
