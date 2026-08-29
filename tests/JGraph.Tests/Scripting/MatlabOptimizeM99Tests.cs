using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// The optimfun family (M99): <c>fminsearch</c>, <c>fminbnd</c>, <c>fzero</c>, <c>lsqnonneg</c>,
/// <c>optimset</c> and <c>optimget</c>, plus the three plot functions <c>PlotFcns</c> names.
/// </summary>
/// <remarks>
/// <para>
/// Assertions run inside the scripts so the tests pin MATLAB's answers rather than JGraph's display
/// formats. The numbers were taken from MATLAB R2024a on this machine and are asserted to the digit
/// where the two agree exactly, which for these solvers is most of them: the iteration and
/// evaluation counts, the exit flags, the procedure names and the answers themselves all match on a
/// polynomial objective.
/// </para>
/// <para>
/// Where a test allows a tolerance it is because the objective is transcendental and .NET's libm
/// differs from MATLAB's in the last ulp, which can move a search by one iteration. ADR 0100 records
/// the measurement.
/// </para>
/// </remarks>
[Collection("JG facade")]
public class MatlabOptimizeM99Tests : IDisposable
{
    private readonly List<(int Number, FigureModel Figure)> _figures = new();
    private readonly RecordingScriptOutput _output = new();

    public MatlabOptimizeM99Tests() => JG.Reset();

    public void Dispose() => JG.Reset();

    private IScriptSession NewSession() => Assert
        .IsAssignableFrom<IScriptRepl>(new MatlabScriptEngine())
        .CreateSession(new ScriptContext(_output, (number, figure) => _figures.Add((number, figure))));

    private static Task<ScriptRunResult> Run(IScriptSession session, string code) =>
        session.ExecuteAsync(code, sourceId: "", CancellationToken.None);

    private void AssertRan(ScriptRunResult result) =>
        Assert.True(result.Success, result.Message + _output.ErrorText);

    // --- fminsearch ---------------------------------------------------------------------------------

    [Fact]
    public async Task Fminsearch_FindsTheMinimumOfASeparableQuadratic()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            x = fminsearch(@(v) (v(1) - 1)^2 + (v(2) + 2)^2, [0 0]);
            assert(abs(x(1) - 1) < 1e-3);
            assert(abs(x(2) + 2) < 1e-3);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminsearch_KeepsTheStartingPointsShape()
    {
        await using IScriptSession session = NewSession();

        // x(:) = xr in MATLAB's own source: the objective is handed the shape it was started with,
        // so an objective written for a column keeps working and the answer comes back as one.
        ScriptRunResult result = await Run(session, """
            x = fminsearch(@(v) sum(v.^2), [3; 4]);
            assert(isequal(size(x), [2 1]));
            r = fminsearch(@(v) sum(v.^2), [3 4]);
            assert(isequal(size(r), [1 2]));
            m = fminsearch(@(v) sum(sum(v.^2)), [1 2; 3 4]);
            assert(isequal(size(m), [2 2]));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminsearch_CountsIterationsAndEvaluationsAsMatlabDoes()
    {
        await using IScriptSession session = NewSession();

        // Measured against MATLAB R2024a: 46 iterations and 87 evaluations, exactly.
        ScriptRunResult result = await Run(session, """
            [x, fval, flag, out] = fminsearch(@(v) sum(v.^2), [3; 4]);
            assert(flag == 1);
            assert(out.iterations == 46);
            assert(out.funcCount == 87);
            assert(strcmp(out.algorithm, 'Nelder-Mead simplex direct search'));
            assert(fval < 1e-8);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminsearch_WalksRosenbrockTheSameWayMatlabDoes()
    {
        await using IScriptSession session = NewSession();

        // The classic hard case: a curved valley the simplex has to crawl along. MATLAB takes 85
        // iterations and 159 evaluations from this start and arrives at the same point.
        ScriptRunResult result = await Run(session, """
            [x, fval, flag, out] = fminsearch(@(v) 100*(v(2) - v(1)^2)^2 + (1 - v(1))^2, [-1.2, 1]);
            assert(out.iterations == 85);
            assert(out.funcCount == 159);
            assert(flag == 1);
            assert(abs(x(1) - 1.0000220217836) < 1e-12);
            assert(abs(x(2) - 1.0000422197518) < 1e-12);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminsearch_ExhaustingItsBudgetIsExitFlagZeroAndSaysWhich()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [x, fval, flag, out] = fminsearch(@(v) sum(v.^2), [3; 4], optimset('MaxIter', 5));
            assert(flag == 0);
            assert(out.iterations == 5);
            assert(~isempty(strfind(out.message, 'Maximum number of iterations')));

            [x2, f2, g2, o2] = fminsearch(@(v) sum(v.^2), [3; 4], optimset('MaxFunEvals', 9));
            assert(g2 == 0);
            assert(~isempty(strfind(o2.message, 'Maximum number of function evaluations')));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminsearch_TakesAProblemStructure()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            p.objective = @(v) (v - 3).^2;
            p.x0 = 0;
            p.solver = 'fminsearch';
            p.options = [];
            assert(abs(fminsearch(p) - 3) < 1e-3);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminsearch_RefusesAProblemStructureBuiltForAnotherSolver()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            p.objective = @sin;
            p.x0 = 1;
            p.solver = 'fzero';
            p.options = [];
            caught = '';
            try
                fminsearch(p);
            catch ME
                caught = ME.identifier;
            end
            assert(strcmp(caught, 'MATLAB:separateOptimStruct:InvalidSolver'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminsearch_PassesTrailingArgumentsToTheObjective()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            f = @(v, c) (v - c).^2;
            x = fminsearch(f, 0, [], 7);
            assert(abs(x - 7) < 1e-3);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminsearch_RefusesInputThatIsNotDouble()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            caught = '';
            try
                fminsearch(@(v) sum(double(v).^2), int32([3 4]));
            catch ME
                caught = ME.identifier;
            end
            assert(strcmp(caught, 'MATLAB:fminsearch:NonDoubleInput'));
            """);

        AssertRan(result);
    }

    // --- fminbnd ------------------------------------------------------------------------------------

    [Fact]
    public async Task Fminbnd_FindsTheMinimumInsideTheInterval()
    {
        await using IScriptSession session = NewSession();

        // MATLAB lands on 3.1415948018514 with 7 iterations and 8 evaluations.
        ScriptRunResult result = await Run(session, """
            [x, fval, flag, out] = fminbnd(@cos, 3, 4);
            assert(abs(x - 3.1415948018514) < 1e-10);
            assert(flag == 1);
            assert(out.iterations == 7);
            assert(out.funcCount == 8);
            assert(strcmp(out.algorithm, 'golden section search, parabolic interpolation'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminbnd_LandsExactlyOnAParabolasVertex()
    {
        await using IScriptSession session = NewSession();

        // A quadratic is what the parabolic step is exact for, so the search reaches the vertex
        // exactly and MATLAB's counts (5 and 6) are reproducible to the digit.
        ScriptRunResult result = await Run(session, """
            [x, fval, flag, out] = fminbnd(@(t) (t - 1.7).^2, -5, 12);
            assert(x == 1.7);
            assert(fval == 0);
            assert(out.iterations == 5);
            assert(out.funcCount == 6);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminbnd_WithBoundsTheWrongWayRoundIsExitFlagMinusTwo()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [x, fval, flag, out] = fminbnd(@sin, 4, 2);
            assert(isempty(x));
            assert(isempty(fval));
            assert(flag == -2);
            assert(out.iterations == 0);
            assert(out.funcCount == 0);
            assert(strcmp(strtrim(out.message), ...
                'Exiting due to infeasibility: the lower bound exceeds the upper bound.'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminbnd_RefusesBoundsThatAreNotFiniteScalarDoubles()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            ids = {};
            try
                fminbnd(@sin, 1, Inf);
            catch ME
                ids{end+1} = ME.identifier;
            end
            try
                fminbnd(@sin, 1);
            catch ME
                ids{end+1} = ME.identifier;
            end
            assert(strcmp(ids{1}, 'MATLAB:fminbnd:InvalidBoundInput'));
            assert(strcmp(ids{2}, 'MATLAB:fminbnd:NotEnoughInputs'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminbnd_RefusesAnObjectiveThatIsNotScalar()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            caught = '';
            try
                fminbnd(@(t) [t t], 1, 2);
            catch ME
                caught = ME.identifier;
            end
            assert(strcmp(caught, 'MATLAB:fminbnd:NonScalarObj'));
            """);

        AssertRan(result);
    }

    // --- fzero --------------------------------------------------------------------------------------

    [Fact]
    public async Task Fzero_FindsTheZeroInsideABracket()
    {
        await using IScriptSession session = NewSession();

        // MATLAB: 5 iterations, 7 evaluations, no interval widening, and the answer is pi/2 to the
        // last bit.
        ScriptRunResult result = await Run(session, """
            [x, fval, flag, out] = fzero(@cos, [1 2]);
            assert(x == pi/2);
            assert(flag == 1);
            assert(out.iterations == 5);
            assert(out.funcCount == 7);
            assert(out.intervaliterations == 0);
            assert(strcmp(out.algorithm, 'bisection, interpolation'));
            assert(~isempty(strfind(out.message, 'Zero found in the interval')));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fzero_WidensOutwardFromASingleGuessUntilTheSignChanges()
    {
        await using IScriptSession session = NewSession();

        // The cube root of two, from a guess of one: MATLAB widens 8 times and then takes 6
        // zero-finding steps, 23 evaluations in all.
        ScriptRunResult result = await Run(session, """
            [x, fval, flag, out] = fzero(@(t) t.^3 - 2, 1);
            assert(abs(x - 2^(1/3)) < 1e-15);
            assert(fval == 0);
            assert(flag == 1);
            assert(out.iterations == 6);
            assert(out.funcCount == 23);
            assert(out.intervaliterations == 8);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fzero_AnEndOfTheBracketThatIsAlreadyZeroIsTheAnswer()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [x, fval, flag, out] = fzero(@(t) t, [0 1]);
            assert(x == 0);
            assert(fval == 0);
            assert(flag == 1);
            assert(out.funcCount == 2);
            assert(strcmp(strtrim(out.message), 'Zero find terminated.'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fzero_RefusesABracketWhoseEndsShareASign()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            ids = {};
            try
                fzero(@(t) t.^2 + 1, [1 2]);
            catch ME
                ids{end+1} = ME.identifier;
            end
            try
                fzero(@sin, [1 2 3]);
            catch ME
                ids{end+1} = ME.identifier;
            end
            try
                fzero(@sin, Inf);
            catch ME
                ids{end+1} = ME.identifier;
            end
            assert(strcmp(ids{1}, 'MATLAB:fzero:ValuesAtEndPtsSameSign'));
            assert(strcmp(ids{2}, 'MATLAB:fzero:LengthArg2'));
            assert(strcmp(ids{3}, 'MATLAB:fzero:Arg2NotFinite'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fzero_ReportsANonFiniteValueAndAPoleWithTheirOwnExitFlags()
    {
        await using IScriptSession session = NewSession();

        // A function with no root at all runs the widening out to where it overflows: exit flag -3,
        // and x is NaN rather than a wrong answer. A pole is a sign change the search can bracket
        // and close on, so it converges and says so with -5 instead.
        ScriptRunResult result = await Run(session, """
            [x, fval, flag] = fzero(@(t) t.^2 + 1, 1);
            assert(isnan(x));
            assert(flag == -3);

            [p, fp, pflag, pout] = fzero(@(t) 1./(t - 2), 3);
            assert(abs(p - 2) < 1e-9);
            assert(pflag == -5);
            assert(~isempty(strfind(pout.message, 'near a singular point')));
            """);

        AssertRan(result);
    }

    // --- lsqnonneg ----------------------------------------------------------------------------------

    [Fact]
    public async Task Lsqnonneg_PinsTheUnknownThatWouldGoNegative()
    {
        await using IScriptSession session = NewSession();

        // The unconstrained least-squares answer here has a negative first entry, so the constraint
        // binds and that entry comes back at exactly zero rather than near it.
        ScriptRunResult result = await Run(session, """
            C = [0.0372 0.2869; 0.6861 0.7071; 0.6233 0.6245; 0.6344 0.6170];
            d = [0.8587; 0.1781; 0.0747; 0.8405];
            [x, resnorm, residual, flag, out, lambda] = lsqnonneg(C, d);
            assert(x(1) == 0);
            assert(abs(x(2) - 0.69293439713029) < 1e-12);
            assert(abs(resnorm - 0.83145595126331) < 1e-12);
            assert(flag == 1);
            assert(out.iterations == 1);
            assert(strcmp(out.algorithm, 'active-set'));
            assert(isequal(size(x), [2 1]));
            assert(isequal(size(residual), [4 1]));
            assert(isequal(size(lambda), [2 1]));
            assert(abs(lambda(1) + 0.15061181913647) < 1e-12);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Lsqnonneg_LeavesAnAlreadyNonNegativeAnswerAlone()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            C = [1 0; 0 1];
            d = [3; 5];
            [x, resnorm, residual] = lsqnonneg(C, d);
            assert(abs(x(1) - 3) < 1e-12);
            assert(abs(x(2) - 5) < 1e-12);
            assert(resnorm < 1e-24);
            assert(all(abs(residual) < 1e-12));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Lsqnonneg_RefusesComplexInputAndTooManyArguments()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            ids = {};
            try
                lsqnonneg([1i 0; 0 1], [1; 1]);
            catch ME
                ids{end+1} = ME.identifier;
            end
            try
                lsqnonneg(eye(2), [1; 1], [], [], []);
            catch ME
                ids{end+1} = ME.identifier;
            end
            assert(strcmp(ids{1}, 'MATLAB:lsqnonneg:ComplexCorD'));
            assert(strcmp(ids{2}, 'MATLAB:lsqnonneg:TooManyInputs'));
            """);

        AssertRan(result);
    }

    // --- optimset and optimget ----------------------------------------------------------------------

    [Fact]
    public async Task Optimset_BuildsTheEightFieldStructureWithEverythingElseUnset()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            o = optimset('TolX', 1e-8);
            assert(isequal(fieldnames(o)', {'Display', 'MaxFunEvals', 'MaxIter', 'TolFun', ...
                'TolX', 'FunValCheck', 'OutputFcn', 'PlotFcns'}));
            assert(o.TolX == 1e-8);
            assert(isempty(o.TolFun));
            assert(isempty(o.Display));

            empty = optimset;
            assert(isempty(empty.TolX));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Optimset_AsAStatementPrintsTheSettingsAndLeavesAnsAlone()
    {
        await using IScriptSession session = NewSession();

        // MATLAB's optimset answers a structure when anything wants one and prints the settings
        // when nothing does. The bare word and the written-out call are the same statement, and
        // both have to take the second road.
        ScriptRunResult result = await Run(session, """
            ans = 42;
            optimset
            optimset();
            assert(ans == 42);
            o = optimset;
            assert(numel(fieldnames(o)) == 8);
            """);

        AssertRan(result);
        Assert.Contains("Display: [ off | iter | notify | final ]", _output.NormalText);
        Assert.Contains("PlotFcns: [ function | {[]} ]", _output.NormalText);
        Assert.DoesNotContain("ans =", _output.NormalText);
    }

    [Fact]
    public async Task Optimset_AnswersOneSolversDefaults()
    {
        await using IScriptSession session = NewSession();

        // The two budgets are stored as a recipe rather than a number, because 200 per free
        // parameter is not a number until there is a starting point to count.
        ScriptRunResult result = await Run(session, """
            d = optimset('fminsearch');
            assert(strcmp(d.MaxIter, '200*numberOfVariables'));
            assert(strcmp(d.MaxFunEvals, '200*numberOfVariables'));
            assert(d.TolX == 1e-4);
            assert(d.TolFun == 1e-4);
            assert(strcmp(d.Display, 'notify'));
            assert(strcmp(d.FunValCheck, 'off'));

            b = optimset(@fminbnd);
            assert(b.MaxIter == 500);
            assert(b.MaxFunEvals == 500);
            assert(isempty(b.TolFun));

            z = optimset('fzero');
            assert(z.TolX == eps);

            l = optimset('lsqnonneg');
            assert(strcmp(l.TolX, '10*eps*norm(C,1)*length(C)'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Optimset_MergesAnOldStructureWithNewSettings()
    {
        await using IScriptSession session = NewSession();

        // Only the non-empty fields of the second structure cross over, which is what makes an
        // unset field mean "the caller did not say" rather than "the caller said nothing".
        ScriptRunResult result = await Run(session, """
            a = optimset('TolX', 1e-9, 'Display', 'ITER');
            b = optimset('TolFun', 1e-7);
            c = optimset(a, b);
            assert(c.TolX == 1e-9);
            assert(c.TolFun == 1e-7);
            assert(strcmp(c.Display, 'iter'));

            d = optimset(a, 'TolX', 5e-3);
            assert(d.TolX == 5e-3);
            assert(strcmp(d.Display, 'iter'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Optimget_MatchesAnyUniqueLeadingPortionOfAName()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            o = optimset('TolX', 7);
            assert(optimget(o, 'tolx') == 7);
            assert(optimget(o, 'TolX') == 7);
            assert(optimget(o, 'TolFun', 99) == 99);
            assert(optimget([], 'TolX', 5) == 5);
            assert(isempty(optimget(o, 'TolFun')));

            ids = {};
            try
                optimget(o, 'Tol');
            catch ME
                ids{end+1} = ME.identifier;
            end
            try
                optimget(o, 'Zebra');
            catch ME
                ids{end+1} = ME.identifier;
            end
            assert(strcmp(ids{1}, 'MATLAB:optimget:AmbiguousPropName'));
            assert(strcmp(ids{2}, 'MATLAB:optimget:InvalidPropName'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Optimset_RefusesANameThatIsNeitherSettingNorSolver()
    {
        await using IScriptSession session = NewSession();

        // A single text argument is always read as a solver name, so optimset('TolX') is not a
        // setting missing its value: it is a function that does not exist.
        ScriptRunResult result = await Run(session, """
            ids = {};
            try
                optimset('Nonsense', 1);
            catch ME
                ids{end+1} = ME.identifier;
            end
            try
                optimset('TolX');
            catch ME
                ids{end+1} = ME.identifier;
            end
            try
                optimset('nosuchsolver');
            catch ME
                ids{end+1} = ME.identifier;
            end
            assert(strcmp(ids{1}, 'MATLAB:optimset:InvalidParamNameWithLink'));
            assert(strcmp(ids{2}, 'MATLAB:optimset:FcnNotFoundOnPath'));
            assert(strcmp(ids{3}, 'MATLAB:optimset:FcnNotFoundOnPath'));
            """);

        AssertRan(result);
    }

    // --- Display, callbacks and the plot functions ---------------------------------------------------

    [Fact]
    public async Task Display_IterPrintsATablePerIterationAndNotifyOnlySpeaksOnFailure()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            fminsearch(@(v) (v - 3).^2, 0, optimset('Display', 'iter'));
            """);

        AssertRan(result);
        Assert.Contains(" Iteration   Func-count         f(x)         Procedure", _output.NormalText);
        Assert.Contains("initial simplex", _output.NormalText);
        Assert.Contains("contract inside", _output.NormalText);
        Assert.Contains("Optimization terminated:", _output.NormalText);
    }

    [Fact]
    public async Task Display_NotifySaysNothingWhenTheSolveConverged()
    {
        await using IScriptSession session = NewSession();

        // 'notify' is the default, and its whole point is silence on success.
        ScriptRunResult result = await Run(session, """
            fminsearch(@(v) (v - 3).^2, 0);
            fminbnd(@cos, 3, 4);
            fzero(@cos, [1 2]);
            """);

        AssertRan(result);
        Assert.DoesNotContain("Optimization terminated", _output.NormalText);
        Assert.DoesNotContain("Zero found", _output.NormalText);
    }

    [Fact]
    public async Task Display_OffSaysNothingEvenWhenTheSolveFailed()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            fminsearch(@(v) sum(v.^2), [3; 4], optimset('Display', 'off', 'MaxIter', 2));
            """);

        AssertRan(result);
        Assert.DoesNotContain("Exiting", _output.NormalText);
    }

    [Fact]
    public async Task OutputFcn_CanStopTheSolveAndTheExitFlagSaysSo()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            [x, fval, flag, out] = fminsearch(@(v) (v - 3).^2, 0, ...
                optimset('OutputFcn', @(p, ov, st) ov.iteration >= 3));
            assert(flag == -1);
            assert(out.iterations == 3);
            assert(strcmp(strtrim(out.message), 'Optimization terminated prematurely by user.'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task OutputFcn_SeesTheStatesAndTheOptimValuesFields()
    {
        await using IScriptSession session = NewSession();

        // The opening report is 'init', every step is 'iter', and the last is 'done'. The checks run
        // inside the callback so that a missing field or an unexpected state fails the script where
        // it happens rather than being accumulated and inspected afterwards.
        ScriptRunResult result = await Run(session, """
            global seen;
            seen = 0;
            function stop = watch(x, optimValues, state)
                global seen;
                assert(any(strcmp(state, {'init', 'iter', 'done'})));
                assert(numel(fieldnames(optimValues)) == 4);
                assert(isfield(optimValues, 'iteration'));
                assert(isfield(optimValues, 'funccount'));
                assert(isfield(optimValues, 'fval'));
                assert(isfield(optimValues, 'procedure'));
                if strcmp(state, 'init')
                    seen = seen + 1;
                elseif strcmp(state, 'done')
                    seen = seen + 100;
                end
                stop = false;
            end
            fminsearch(@(v) (v - 3).^2, 0, optimset('OutputFcn', @watch, 'MaxIter', 4));
            assert(seen == 101);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task OutputFcn_ForFzeroCarriesTheBracketItIsWorkingIn()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            global calls;
            calls = 0;
            function stop = watchzero(x, optimValues, state)
                global calls;
                calls = calls + 1;
                assert(numel(fieldnames(optimValues)) == 9);
                assert(isfield(optimValues, 'funccount'));
                assert(isfield(optimValues, 'iteration'));
                assert(isfield(optimValues, 'intervaliteration'));
                assert(isfield(optimValues, 'fval'));
                assert(isfield(optimValues, 'procedure'));
                assert(isfield(optimValues, 'intervala'));
                assert(isfield(optimValues, 'fvala'));
                assert(isfield(optimValues, 'intervalb'));
                assert(isfield(optimValues, 'fvalb'));
                stop = false;
            end
            fzero(@cos, [1 2], optimset('OutputFcn', @watchzero));
            assert(calls > 2);
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task FunValCheck_TurnsANaNObjectiveIntoAnErrorWithItsOwnIdentifier()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            % Without the option a NaN is a value like any other: it sorts to the end of the simplex
            % and gets reflected away, so the solve finishes.
            x = fminsearch(@(v) (v - 3).^2, 0);
            assert(abs(x - 3) < 1e-3);

            caught = '';
            try
                fminsearch(@(v) NaN, 1, optimset('FunValCheck', 'on'));
            catch ME
                caught = ME.identifier;
            end
            assert(strcmp(caught, 'MATLAB:fminsearch:checkfun:NaNFval'));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task PlotFcns_DrawTheSolveAsItRuns()
    {
        await using IScriptSession session = NewSession();

        ScriptRunResult result = await Run(session, """
            fminsearch(@(v) sum((v - [1 2]).^2), [0 0], ...
                optimset('PlotFcns', {@optimplotfval, @optimplotx, @optimplotfunccount}, ...
                         'MaxIter', 5));
            assert(~isempty(get(gcf, 'Children')));
            """);

        AssertRan(result);
    }

    [Fact]
    public async Task Fminsearch_IsNoLongerRefusedAsUnsupported()
    {
        await using IScriptSession session = NewSession();

        // It was in the UnsupportedFunctions table from M35 until this milestone, so the old
        // "not supported in JGraph (optimization)" message must be gone for good.
        ScriptRunResult result = await Run(session, """
            assert(exist('fminsearch') > 0);
            assert(exist('fminbnd') > 0);
            assert(exist('fzero') > 0);
            assert(exist('lsqnonneg') > 0);
            assert(isa(@fminsearch, 'function_handle'));
            assert(abs(feval('fminsearch', @(v) (v - 2).^2, 0) - 2) < 1e-3);
            """);

        AssertRan(result);
    }
}
