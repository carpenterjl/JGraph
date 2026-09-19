using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), seventh sub-stage: what <c>cellfun</c> and <c>arrayfun</c> hand an
/// <c>'ErrorHandler'</c> (appendix A #54) — the failure's identifier, its message under R2025b's
/// "Error using …" header, the index — and <c>mat2str</c> of a negative zero (#163).
/// </summary>
/// <remarks>
/// The parity fixture <c>errorhandler_records</c> holds R2025b's answers; these pin the roads the
/// sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class ErrorHandlerRecordsM167Tests : IDisposable
{
    private const string Helpers = """

        function y = failing(x)
        error('probe:bad', 'bad %d', x);
        y = x;
        end

        function y = fails_on_even(x)
        if mod(x, 2) == 0
            error('probe:even', 'even %d', x);
        end
        y = x * 10;
        end

        function y = pick(e, x)
        if strcmp(e.identifier, 'probe:even')
            y = -x;
        else
            y = NaN;
        end
        end
        """;

    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public ErrorHandlerRecordsM167Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private string RunAndRead(string code)
    {
        ScriptRunResult result = _engine.RunAsync(
            code + Helpers,
            new ScriptContext(_output, (_, _) => { }, Path.GetTempPath(), resolvePath: null, figureFiles: new TestFigureFiles()),
            CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(result.Success, result.Message + _output.ErrorText);
        return _output.NormalText.Trim();
    }

    [Theory]
    [InlineData("cellfun(@failing, {1}, 'ErrorHandler', @(e, x) strcmp(e.identifier, 'probe:bad'))")]
    [InlineData("arrayfun(@failing, 1, 'ErrorHandler', @(e, x) strcmp(e.identifier, 'probe:bad'))")]
    public void TheRecordCarriesTheIdentifier(string call)
    {
        Assert.Equal("1", RunAndRead($"r = {call}; fprintf('%d\\n', r);"));
    }

    [Fact]
    public void TheMessageSitsUnderTheLineNamingTheFunctionThatFailed()
    {
        string text = RunAndRead("""
            r = cellfun(@failing, {1}, 'UniformOutput', false, 'ErrorHandler', @(e, x) e.message);
            q = cellfun(@(x) failing(x) + 1, {5}, 'UniformOutput', false, 'ErrorHandler', @(e, x) e.message);
            parts = strsplit(r{1}, newline);
            fprintf('%d %d %s|%d %s\n', startsWith(parts{1}, 'Error using '), contains(parts{1}, 'failing (line '), ...
                parts{2}, contains(q{1}, 'failing (line '), q{1}(end - 4:end));
            """);
        Assert.Equal("1 1 bad 1|1 bad 5", text);
    }

    [Fact]
    public void AnAnonymousErrorIsRefusedForTheOutputItCannotGive()
    {
        string text = RunAndRead("""
            r = cellfun(@(x) error('probe:bad', 'bad'), {1}, 'UniformOutput', false, ...
                'ErrorHandler', @(e, x) sprintf('%d#%s', strcmp(e.identifier, 'probe:bad'), strrep(e.message, newline, ' ')));
            disp(r{1});
            """);
        Assert.Equal("0#Error using error Too many output arguments.", text);
    }

    [Fact]
    public void TheHandlerAnswersForTheElementsThatFailedAndOnlyThose()
    {
        string text = RunAndRead("""
            a = arrayfun(@fails_on_even, 1:4, 'ErrorHandler', @pick);
            b = arrayfun(@fails_on_even, [1 2; 3 4], 'ErrorHandler', @(e, x) -e.index);
            c = cellfun(@failing, {7, 8}, 'ErrorHandler', @(e, x) x * 100);
            fprintf('%s %s %s\n', mat2str(a), mat2str(b), mat2str(c));
            """);
        Assert.Equal("[10 -2 30 -4] [10 -3;30 -4] [700 800]", text);
    }

    [Fact]
    public void AHandlerThatFailsOrRethrowsEndsTheCall()
    {
        string text = RunAndRead("""
            try
                cellfun(@failing, {1}, 'ErrorHandler', @(e, x) error('probe:handler', 'handler failed'));
            catch err
                fprintf('%s %s\n', err.identifier, err.message);
            end
            try
                cellfun(@failing, {1}, 'ErrorHandler', @(e, x) rethrow(e));
            catch err
                fprintf('%s %d\n', err.identifier, contains(err.message, 'bad 1'));
            end
            """);
        Assert.Equal("probe:handler handler failed\nprobe:bad 1", text.Replace("\r", ""));
    }

    [Theory]
    [InlineData("mat2str(-0)", "0")]
    [InlineData("mat2str([0 -0 1; -0 2 -3])", "[0 0 1;0 2 -3]")]
    [InlineData("mat2str([complex(-0, 1) complex(2, -0)])", "[0+1i 2]")]
    [InlineData("mat2str([-0 -0.5], 3)", "[0 -0.5]")]
    public void Mat2strWritesANegativeZeroAsTheZeroItReadsBackAs(string call, string expected)
    {
        Assert.Equal(expected, RunAndRead($"disp({call});"));
    }
}
