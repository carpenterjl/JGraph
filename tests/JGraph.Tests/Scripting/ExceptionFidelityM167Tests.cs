using JGraph.Api;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// V6 (ADR 0167), sixth sub-stage: exception fidelity (appendix A #59, #66, #67). An MException is
/// a value with causes; <c>throw</c>, <c>rethrow</c> and <c>throwAsCaller</c> carry the whole of it
/// and <c>catch</c> hands that value back; the stack goes on past the catch, <c>rethrow</c> keeps
/// it and <c>throwAsCaller</c> leaves out the frame that called it.
/// </summary>
/// <remarks>
/// The parity fixture <c>mexception_values</c> holds R2025b's answers; these pin the roads the
/// sub-stage touched, one assertion a road, so a regression names itself.
/// </remarks>
[Collection("JG facade")]
public class ExceptionFidelityM167Tests : IDisposable
{
    private const string Helpers = """

        function thrower()
        error('probe:bad', 'bad %d', 7);
        end

        function middle()
        thrower();
        end

        function throw_built(e)
        throw(e);
        end

        function as_caller()
        try
            thrower();
        catch inner
            throwAsCaller(inner);
        end
        end

        function calls_as_caller()
        as_caller();
        end
        """;

    private readonly MatlabScriptEngine _engine = new();
    private readonly RecordingScriptOutput _output = new();

    public ExceptionFidelityM167Tests() => JG.Reset();

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

    [Fact]
    public void ANewExceptionHasNoCausesAndNoStackAndAddCauseAnswersANewOne()
    {
        string text = RunAndRead("""
            e = MException('a:b', 'outer');
            f = addCause(e, MException('c:d', 'one'));
            f = addCause(f, MException('e:f', 'two'));
            fprintf('%s %s %d|%s %s|%d %s %s %s\n', class(e.cause), mat2str(size(e.cause)), numel(e.cause), ...
                class(e.stack), mat2str(size(e.stack)), numel(f.cause), mat2str(size(f.cause)), ...
                f.cause{1}.message, f.cause{2}.identifier);
            """);
        Assert.Equal("cell [0 0] 0|struct [0 1]|2 [2 1] one e:f", text);
    }

    [Fact]
    public void AddCauseRefusesWhatIsNotAnException()
    {
        string text = RunAndRead("""
            try
                addCause(MException('a:b', 'outer'), struct('message', 'm', 'identifier', 'x:y'));
            catch err
                disp(err.message);
            end
            """);
        Assert.Equal("Invalid input for argument 2 (rhs2): Value must be 'MException scalar'.", text);
    }

    [Theory]
    [InlineData("throw(e);")]
    [InlineData("try, throw(e); catch first, rethrow(first); end")]
    public void AThrownExceptionArrivesWithItsCausesAllTheWayDown(string raise)
    {
        string text = RunAndRead($$"""
            inner = addCause(MException('p:mid', 'mid'), MException('p:leaf', 'leaf'));
            e = addCause(MException('p:top', 'top'), inner);
            try
                {{raise}}
            catch c
                fprintf('%s %s>%s>%s %s\n', class(c), c.identifier, c.cause{1}.identifier, ...
                    c.cause{1}.cause{1}.identifier, c.message);
            end
            """);
        Assert.Equal("MException p:top>p:mid>p:leaf top", text);
    }

    [Fact]
    public void WhatACatchAddsToItsExceptionStaysOutOfTheOneThatWasThrown()
    {
        string text = RunAndRead("""
            e = addCause(MException('probe:outer', 'outer'), MException('probe:inner', 'inner'));
            try
                throw(e);
            catch c
                c = addCause(c, MException('probe:more', 'more'));
            end
            fprintf('%d %d\n', numel(e.cause), numel(c.cause));
            """);
        Assert.Equal("1 2", text);
    }

    [Fact]
    public void TheStackGoesOnPastTheCatchAndNamesEachFrameInOrder()
    {
        string text = RunAndRead("""
            report(@middle);

            function report(f)
            try
                f();
            catch e
                fprintf('%s %s %s %d %s %s\n', e.stack(1).name, e.stack(2).name, e.stack(3).name, ...
                    e.stack(1).line > 0, strjoin(fieldnames(e.stack)', ','), class(e.stack(1).line));
            end
            end
            """);
        Assert.Equal("thrower middle report 1 file,name,line double", text);
    }

    [Fact]
    public void RethrowKeepsTheStackAndThrowAsCallerLeavesOutItsOwnFrame()
    {
        string text = RunAndRead("""
            try
                try
                    middle();
                catch e1
                    rethrow(e1);
                end
            catch e2
                fprintf('%s %s %d\n', e2.stack(1).name, e2.stack(2).name, numel(e2.stack) == numel(e1.stack));
            end
            try
                calls_as_caller();
            catch e3
                fprintf('%s %s %s\n', e3.stack(1).name, e3.identifier, e3.message);
            end
            """);
        Assert.Equal("thrower middle 1\ncalls_as_caller probe:bad bad 7", text.Replace("\r", ""));
    }

    [Fact]
    public void ABuiltExceptionTakesTheStackOfWhereItWasThrownFrom()
    {
        string text = RunAndRead("""
            e = MException('a:b', 'built');
            try
                throw_built(e);
            catch c
                fprintf('%s %d\n', c.stack(1).name, numel(e.stack));
            end
            """);
        Assert.Equal("throw_built 0", text);
    }

    [Fact]
    public void ErrorRefusesAnExceptionAndRethrowTakesAnErrorStruct()
    {
        string text = RunAndRead("""
            try
                error(MException('a:b', 'outer'));
            catch c
                disp(c.identifier);
            end
            try
                rethrow(struct('message', 'from a struct', 'identifier', 'x:y'));
            catch d
                fprintf('%s %s %s\n', class(d), d.identifier, d.message);
            end
            """);
        Assert.Equal("MATLAB:error:invalidMessageType\nMException x:y from a struct", text.Replace("\r", ""));
    }

    [Fact]
    public void GetReportSaysTheMessageUnderWhereItWasThrown()
    {
        string text = RunAndRead("""
            e = MException('a:b', 'outer');
            try
                thrower();
            catch c
                r = getReport(c);
                fprintf('%s|%d %d\n', getReport(e, 'basic'), contains(r, 'bad 7'), contains(r, 'thrower'));
            end
            """);
        Assert.Equal("outer|1 1", text);
    }
}
