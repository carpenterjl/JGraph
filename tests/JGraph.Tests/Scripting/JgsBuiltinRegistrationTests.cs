using System.Collections.Concurrent;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M94: building script environments on two threads at once must not break the process.
/// <para>
/// Registration used to fill in a process-wide dictionary — <c>TimeFieldReaders</c>, the table the
/// dotted spelling <c>t.Year</c> reads through — once per environment built. Two threads inside
/// <c>CreateGlobals</c> together could catch that <c>Dictionary</c> mid-insert, and a corrupted one
/// stays corrupted for the life of the process: every script afterwards died with "Internal error:
/// InvalidOperationException", which is how a suite of 5,585 tests came back with 2,831 failures
/// instead of the expected 57.
/// </para>
/// <para>
/// Nothing in the suite had ever built two environments at once, which is why this ran for six
/// milestones as an unexplained one-off — M88's notes record a run of exactly this shape and asked
/// that a recurrence be treated as a signal. This file is the test that was missing.
/// </para>
/// <para>
/// Deliberately not going through <c>JgsRunner</c>: a run resets the static figure stack, so two
/// runs at once are two scripts editing one figure. An environment is not — it is a scope with
/// names in it — so building environments in parallel is exactly the thing that is meant to be
/// safe, and exactly the thing that was not.
/// </para>
/// <para>
/// In the "JG facade" collection for a scheduling reason rather than a facade one: nothing here
/// touches <c>JG</c>, but the storm below puts every core to work, and that collection holds tests
/// which break into a running script and give up after ten seconds. In a collection of its own this
/// file runs beside those and starves them. Half a second of serialization is the whole price.
/// </para>
/// </summary>
[Collection("JG facade")]
public class JgsBuiltinRegistrationTests
{
    /// <summary>How many threads build environments at once, and how many each builds.</summary>
    /// <remarks>
    /// The race is a narrow one — two inserts overlapping inside one dictionary — so the storm wins
    /// by repetition rather than by timing, and it wins reliably only while the table is still
    /// filling, which is the first environment a process builds. Against the pre-M94 code these
    /// numbers caught it on most runs of this file; the third test below is the one that catches it
    /// on all of them. The whole fixture costs about half a second with the fix in place.
    /// </remarks>
    private const int Threads = 8;
    private const int Rounds = 20;

    [Fact]
    public void BuildingGlobals_OnManyThreadsAtOnce_NeverThrowsAndAlwaysDeclaresTheSameNames()
    {
        (IReadOnlyList<Exception> failures, IReadOnlyList<string[]> answers) =
            Storm(static () => Globals().Locals.Keys.ToArray());

        Assert.True(failures.Count == 0, Report(failures));
        Assert.Equal(Threads * Rounds, answers.Count);

        // A count is not a set: a half-written table can still be the right size, and the corruption
        // this guards against showed up as a missing name long before it showed up as a throw.
        string[] first = answers[0];
        Assert.Contains("year", first);
        Assert.Contains("second", first);
        foreach (string[] answer in answers)
        {
            Assert.Equal(first, answer);
        }
    }

    [Fact]
    public void BuildingGlobals_OnManyThreadsAtOnce_LeavesEveryEnvironmentAbleToReadATimeField()
    {
        // The stronger claim: not just that the environments were built, but that each one works —
        // both spellings of a calendar field, on an environment built while seven others were being
        // built beside it. year(t) is the declared builtin; t.Year is the dotted property, and it is
        // the one that goes through the shared table.
        (IReadOnlyList<Exception> failures, IReadOnlyList<(double Called, double Dotted)> answers) =
            Storm(static () =>
            {
                JgsEnvironment env = Globals();
                JgsValue moment = Evaluate(env, "datetime(2024, 3, 5, 6, 7, 8)");
                return (
                    Called: Scalar(Evaluate(env, "year(datetime(2024, 3, 5, 6, 7, 8))")),
                    Dotted: Scalar(JgsBuiltins.GetTimeProperty(moment, "Year", 0, 0)));
            });

        Assert.True(failures.Count == 0, Report(failures));
        Assert.Equal(Threads * Rounds, answers.Count);
        Assert.All(answers, answer =>
        {
            Assert.Equal(2024d, answer.Called);
            Assert.Equal(2024d, answer.Dotted);
        });
    }

    [Fact]
    public void BuildingAnEnvironment_DoesNotWriteTheSharedFieldReaderTable()
    {
        // The invariant the two tests above lean on, asserted on one thread where nothing is left to
        // chance. This is the one that fails every run rather than only on a losing interleaving: the
        // race itself is only reachable while the table is still filling, so a test that waits for the
        // exception is a test that catches the bug on the first environment of a process and never
        // again. Registration writing the table at all is what must not be true, and it is checkable
        // at any moment — the old code put a freshly closed-over delegate under every key on every
        // build, so the values here changed identity each time an environment was made.
        Dictionary<string, Func<JgsValue, int, int, JgsValue>> before =
            JgsBuiltins.TimeFieldReaders.ToDictionary(
                static entry => entry.Key, static entry => entry.Value, StringComparer.Ordinal);

        Assert.NotEmpty(before);

        object table = JgsBuiltins.TimeFieldReaders;
        _ = Globals();

        Assert.Same(table, JgsBuiltins.TimeFieldReaders);
        Assert.Equal(before.Count, JgsBuiltins.TimeFieldReaders.Count);
        foreach ((string name, Func<JgsValue, int, int, JgsValue> reader) in before)
        {
            Assert.Same(reader, JgsBuiltins.TimeFieldReaders[name]);
        }
    }

    /// <summary>A fresh set of MATLAB globals, which is what <c>CreateGlobals</c> is called for.</summary>
    private static JgsEnvironment Globals() => JgsBuiltins.CreateGlobals(
        new JGraphScriptGlobals(new ScriptContext(new RecordingScriptOutput(), static (_, _) => { })),
        default,
        JgsDialect.Matlab);

    /// <summary>
    /// A one-element answer as a number, whether it came back scalar or as a 1-by-1 array — the call
    /// and the dotted property do not agree about which, and this file is not about that.
    /// </summary>
    private static double Scalar(JgsValue value) =>
        value.Type == JgsType.Number ? value.AsNumber : value.ElementAt(0).AsNumber;

    /// <summary>One expression, run against <paramref name="env"/>.</summary>
    private static JgsValue Evaluate(JgsEnvironment env, string expression)
    {
        new Interpreter(env, default, dialect: JgsDialect.Matlab)
            .Run(Parser.Parse($"answer__ = {expression};", dialect: JgsDialect.Matlab));
        return env.Locals["answer__"];
    }

    /// <summary>
    /// Runs <paramref name="work"/> on <see cref="Threads"/> real threads, <see cref="Rounds"/> times
    /// each, all released together by a barrier — dedicated threads rather than the pool, because a
    /// barrier across pool work items deadlocks on a starved pool.
    /// </summary>
    private static (IReadOnlyList<Exception> Failures, IReadOnlyList<T> Answers) Storm<T>(Func<T> work)
    {
        var gate = new Barrier(Threads);
        var failures = new ConcurrentQueue<Exception>();
        var answers = new ConcurrentQueue<T>();

        var workers = new Thread[Threads];
        for (int worker = 0; worker < Threads; worker++)
        {
            workers[worker] = new Thread(() =>
            {
                try
                {
                    gate.SignalAndWait();
                    for (int round = 0; round < Rounds; round++)
                    {
                        answers.Enqueue(work());
                    }
                }
                catch (Exception error)
                {
                    failures.Enqueue(error);
                }
            })
            {
                IsBackground = true,
            };

            workers[worker].Start();
        }

        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        return ([.. failures], [.. answers]);
    }

    /// <summary>The failures written out, because the type alone does not say which insert tore.</summary>
    private static string Report(IReadOnlyList<Exception> failures) =>
        string.Join(Environment.NewLine, failures.Select(static failure => failure.ToString()));
}
