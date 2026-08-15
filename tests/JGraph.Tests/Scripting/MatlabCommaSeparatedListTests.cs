using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M61: the comma-separated list — <c>c{:}</c> and a struct array's field spreading into an argument
/// list, a bracket, a cell literal, and a multiple assignment — and <c>varargout</c>, which is the
/// same idea pointing the other way.
/// </summary>
/// <remarks>
/// A list is deliberately not a value: it exists for as long as it takes the caller to spread it and
/// cannot be stored. The tests that matter most here are therefore the refusals, which are what say
/// the list did not quietly become one — <see cref="AListIsRefusedWhereOneValueIsWanted"/> and
/// <see cref="AListCannotBeAssignedToAName"/>. Expected values are MATLAB's own.
/// </remarks>
[Collection("JG facade")]
public class MatlabCommaSeparatedListTests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabCommaSeparatedListTests() => JG.Reset();

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

    // --- Spreading into an argument list ---------------------------------------------------------

    [Fact]
    public Task AColonSpreadsEveryElementIntoAnArgumentList() => RunAsserting("""
        c = {3, 7};
        assert(plus(c{:}) == 10);

        % The count is what proves it spread rather than passing one cell: nargin sees two.
        assert(counts(c{:}) == 2);
        assert(counts(c{1}) == 1);

        function n = counts(varargin)
            n = nargin;
        end
        """);

    [Fact]
    public Task ASpreadJoinsTheArgumentsAroundIt() => RunAsserting("""
        c = {2, 3};
        assert(counts(1, c{:}, 4) == 4);

        function n = counts(varargin)
            n = nargin;
        end
        """);

    [Fact]
    public Task AVarargInIsForwardedBySpreadingIt() => RunAsserting("""
        % The forwarding idiom: a wrapper hands its own arguments on without knowing how many.
        assert(wrapper(4, 9) == 9);
        assert(wrapper(4, 9, 2) == 9);

        function m = wrapper(varargin)
            m = max(varargin{:});
        end
        """);

    [Fact]
    public Task ARangeAndAMaskBothNameSeveral() => RunAsserting("""
        c = {1, 2, 3, 4};
        assert(counts(c{2:3}) == 2);
        assert(counts(c{[true false true false]}) == 2);
        assert(counts(c{[1 4]}) == 2);

        function n = counts(varargin)
            n = nargin;
        end
        """);

    [Fact]
    public Task AnEmptySpreadContributesNoArgumentsAtAll() => RunAsserting("""
        c = {};
        assert(counts(c{:}) == 0);
        assert(counts(1, c{:}) == 1);

        function n = counts(varargin)
            n = nargin;
        end
        """);

    // --- Spreading into brackets and cell literals ------------------------------------------------

    [Fact]
    public Task ABracketCollectsASpreadIntoOneRow() => RunAsserting("""
        c = {1, 2, 3};
        assert(isequal([c{:}], [1 2 3]));
        assert(isequal([0, c{:}, 4], [0 1 2 3 4]));
        assert(isequal([c{2:3}], [2 3]));
        """);

    [Fact]
    public Task ACellLiteralRebuildsTheCellFromASpread() => RunAsserting("""
        c = {1, 'two', 3};
        d = {c{:}};
        assert(numel(d) == 3);
        assert(strcmp(d{2}, 'two'));

        % A row's width is not known until it is evaluated, which is what makes this legal at all.
        e = {0, c{:}};
        assert(numel(e) == 4);
        """);

    // --- Spreading into a multiple assignment ------------------------------------------------------

    [Fact]
    public Task AMultipleAssignmentTakesOneTargetPerElement() => RunAsserting("""
        c = {10, 20, 30};
        [a, b] = c{1:2};
        assert(a == 10 && b == 20);

        [p, q, r] = c{:};
        assert(p == 10 && q == 20 && r == 30);

        % '~' drops one the same way it does from a call.
        [~, s] = c{1:2};
        assert(s == 20);
        """);

    [Fact]
    public Task AskingForMoreTargetsThanTheListHoldsIsRefused() => RunAsserting("""
        caught = '';
        try
            c = {1, 2};
            [a, b, d] = c{:};
        catch err
            caught = err.message;
        end
        assert(~isempty(caught));
        """);

    // --- The refusals: a list is not a value -------------------------------------------------------

    [Fact]
    public Task AListIsRefusedWhereOneValueIsWanted() => RunAsserting("""
        % This is the test that fails if a comma-separated list ever becomes a storable value.
        c = {1, 2, 3};
        caught = '';
        try
            x = c{:};
        catch err
            caught = err.message;
        end
        assert(contains(caught, 'where one value is wanted'));
        """);

    [Fact]
    public Task AListCannotBeAssignedToAName() => RunAsserting("""
        c = {1, 2};
        caught = '';
        try
            x = c{[1 2]};
        catch err
            caught = err.message;
        end
        assert(contains(caught, '2 elements'));
        """);

    [Fact]
    public Task ABraceAssignmentStillWritesExactlyOneElement() => RunAsserting("""
        c = cell(2, 2);
        caught = '';
        try
            c{1, :} = 5;
        catch err
            caught = err.message;
        end
        assert(contains(caught, 'writes one'));
        """);

    [Fact]
    public Task ABraceNamingOneElementIsStillAPlainRead() => RunAsserting("""
        % The single-element cases must not have become lists: c{2} is a value, not a list of one.
        c = {'a', 'b'};
        assert(strcmp(c{2}, 'b'));
        assert(strcmp(c{1, 2}, 'b'));

        c{2} = 'z';
        assert(strcmp(c{2}, 'z'));

        % Growth by brace assignment is the accumulation idiom and still works.
        c{4} = 'q';
        assert(numel(c) == 4);
        """);

    // --- A struct array's field --------------------------------------------------------------------

    [Fact]
    public Task AStructArrayFieldSpreadsAcrossTheElements() => RunAsserting("""
        s(1).a = 10;
        s(2).a = 20;
        s(3).a = 30;

        assert(counts(s.a) == 3);
        assert(isequal([s.a], [10 20 30]));

        function n = counts(varargin)
            n = nargin;
        end
        """);

    [Fact]
    public Task AStructArrayFieldStillCollectsWhereOneValueIsWanted() => RunAsserting("""
        % Where a list has no room to go the field is the collected row, which is what M41 built and
        % what a script reading a regionprops result relies on.
        s(1).a = 10;
        s(2).a = 20;
        x = s.a;
        assert(isequal(x, [10 20]));

        % A cell when the values are not all numbers.
        t(1).name = 'first';
        t(2).name = 'second';
        y = t.name;
        assert(iscell(y));
        assert(strcmp(y{2}, 'second'));

        % iscell(t.name) is not this: there the field spreads and iscell hears two arguments, which
        % is MATLAB's answer too. Reading a list where one value belongs is the caller's mistake.
        caught = '';
        try
            iscell(t.name);
        catch err
            caught = err.message;
        end
        assert(~isempty(caught));
        """);

    [Fact]
    public Task AScalarStructFieldIsUnaffected() => RunAsserting("""
        s.a = 7;
        assert(s.a == 7);
        assert(counts(s.a) == 1);

        function n = counts(varargin)
            n = nargin;
        end
        """);

    // --- varargout ---------------------------------------------------------------------------------

    [Fact]
    public Task AVarargOutHandsBackAsManyAsWereAskedFor() => RunAsserting("""
        [a, b] = spread();
        assert(a == 1 && b == 2);

        [p, q, r] = spread();
        assert(r == 3);

        one = spread();
        assert(one == 1);

        function varargout = spread()
            varargout{1} = 1;
            varargout{2} = 2;
            varargout{3} = 3;
        end
        """);

    [Fact]
    public Task AVarargOutFollowsTheNamedOutputsBeforeIt() => RunAsserting("""
        [first, second, third] = mixed();
        assert(first == 100 && second == 1 && third == 2);

        % Asking for only the named output must not need varargout filled at all.
        just = mixed();
        assert(just == 100);

        function [named, varargout] = mixed()
            named = 100;
            varargout{1} = 1;
            varargout{2} = 2;
        end
        """);

    [Fact]
    public Task AVarargOutSeesTheOutputCountThroughNargout() => RunAsserting("""
        % nargout is how a variadic function knows how many to make, so this is the pairing that
        % makes varargout worth having rather than a fixed list under another name.
        [a, b, c] = counted();
        assert(a == 1 && b == 2 && c == 3);
        assert(numel(counted()) == 1);

        function varargout = counted()
            for k = 1:nargout
                varargout{k} = k;
            end
        end
        """);

    [Fact]
    public Task AVarargInAndVarargOutTogetherForwardBothWays() => RunAsserting("""
        % The relay: varargin{:} spreads in, [varargout{1:nargout}] takes as many back out. It needs
        % every piece of M61 at once, which is why it is the one test that proves the whole wave.
        [a, b] = relay(@size, zeros(3, 5));
        assert(a == 3 && b == 5);

        % Asked for one, the relay asks size for one, and size's one output is the whole shape.
        one = relay(@size, zeros(3, 5));
        assert(isequal(one, [3 5]));

        function varargout = relay(f, varargin)
            [varargout{1:nargout}] = f(varargin{:});
        end
        """);

    // --- arrayfun: the M52 deferral this wave closes ----------------------------------------------

    [Fact]
    public Task ArrayFunAsksEachElementForSeveralOutputs() => RunAsserting("""
        [r, c] = arrayfun(@(k) size(zeros(k, k + 1)), [1 2]);
        assert(isequal(r, [1 2]));
        assert(isequal(c, [2 3]));
        """);

    [Fact]
    public Task ArrayFunRefusesAMisspeltOptionAndNamesTheRealOnes() => RunAsserting("""
        % M52 recorded that arrayfun scanned for 'UniformOutput' and ignored the rest of its tail, so
        % this misspelling used to be accepted in silence and the option simply did not happen.
        caught = '';
        try
            arrayfun(@(k) k, [1 2], 'UnifromOutput', false);
        catch err
            caught = err.message;
        end
        assert(contains(caught, 'UniformOutput'));
        assert(contains(caught, 'ErrorHandler'));
        """);

    [Fact]
    public Task ArrayFunErrorHandlerAnswersForTheElementThatFailed() => RunAsserting("""
        r = arrayfun(@(k) fussy(k), [1 2 3], 'ErrorHandler', @(s, k) -1);
        assert(isequal(r, [1 -1 3]));

        % The handler is handed a record of the failure and then the element's own inputs.
        msgs = arrayfun(@(k) fussy(k), [2], 'ErrorHandler', @(s, k) numel(s.message), ...
            'UniformOutput', true);
        assert(msgs > 0);

        function y = fussy(k)
            if k == 2
                error('boom:it', 'boom');
            end
            y = k;
        end
        """);
}
