using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Model;

/// <summary>
/// Open item 361: a window painted a figure on the UI thread while the script thread was still adding
/// to it, and met "Collection was modified". The graphics gate orders the two. The rules are tested on
/// a gate of each test's own, because the process's gate is held by whatever other tests' scripts run
/// alongside; the last two tests use the process's gate only in ways no other script can disturb.
/// Item 359 rides along: a Run is no longer ended at 50 million statements.
/// </summary>
[Collection("JG facade")]
public class GraphicsGateTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>A thread that enters the gate as a script and holds it until told to stop.</summary>
    private sealed class ScriptHolder : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;

        public ScriptHolder(GraphicsGateState gate)
        {
            _thread = new Thread(() =>
            {
                using (gate.EnterScript())
                {
                    _entered.Set();
                    _release.Wait();
                }
            }) { IsBackground = true };
            _thread.Start();
            Assert.True(_entered.Wait(Patience));
        }

        public void Dispose()
        {
            _release.Set();
            Assert.True(_thread.Join(Patience));
        }
    }

    [Fact]
    public void TheDisplayEntersWhenNoScriptHoldsTheGate()
    {
        var gate = new GraphicsGateState();
        Assert.False(gate.ScriptsBusy);
        Assert.True(gate.TryEnterDisplay());
        gate.ExitDisplay();
    }

    [Fact]
    public void AScriptTurnsTheDisplayAway_AndWhatWaitsRunsWhenItEnds()
    {
        var gate = new GraphicsGateState();
        var ran = new ManualResetEventSlim();
        var holder = new ScriptHolder(gate);
        Assert.True(gate.ScriptsBusy);
        Assert.False(gate.TryEnterDisplay());

        gate.WhenFree(ran.Set);
        Assert.False(ran.IsSet);

        holder.Dispose();
        Assert.True(ran.Wait(Patience));
        Assert.False(gate.ScriptsBusy);
        Assert.True(gate.TryEnterDisplay());
        gate.ExitDisplay();
    }

    [Fact]
    public void WhenFreeRunsAtOnceWhenNoScriptHoldsTheGate()
    {
        var gate = new GraphicsGateState();
        bool ran = false;
        gate.WhenFree(() => ran = true);
        Assert.True(ran);
    }

    [Fact]
    public void AParkLetsTheDisplayIn_AndRunsWhatWaited()
    {
        var gate = new GraphicsGateState();
        var parked = new ManualResetEventSlim();
        var resume = new ManualResetEventSlim();
        var ran = new ManualResetEventSlim();
        var script = new Thread(() =>
        {
            using (gate.EnterScript())
            {
                gate.WhenFree(ran.Set); // queued: this script holds the gate
                using (gate.Park())
                {
                    parked.Set();
                    resume.Wait();
                }
            }
        }) { IsBackground = true };
        script.Start();

        Assert.True(parked.Wait(Patience));
        Assert.True(ran.Wait(Patience)); // the park opened the gate, so what waited ran
        bool displayDuringPark = gate.TryEnterDisplay();
        if (displayDuringPark)
        {
            gate.ExitDisplay();
        }

        resume.Set();
        Assert.True(script.Join(Patience));
        Assert.True(displayDuringPark);
    }

    [Fact]
    public void TakingTheGateBackWaitsForAPaintUnderWay()
    {
        var gate = new GraphicsGateState();
        var parked = new ManualResetEventSlim();
        var endPark = new ManualResetEventSlim();
        var backInside = new ManualResetEventSlim();
        var script = new Thread(() =>
        {
            using (gate.EnterScript())
            {
                using (gate.Park())
                {
                    parked.Set();
                    endPark.Wait();
                }

                backInside.Set();
            }
        }) { IsBackground = true };
        script.Start();
        Assert.True(parked.Wait(Patience));

        Assert.True(gate.TryEnterDisplay()); // a paint begins while the script is parked
        endPark.Set();
        Assert.False(backInside.Wait(TimeSpan.FromMilliseconds(300))); // the script waits for it

        gate.ExitDisplay();
        Assert.True(backInside.Wait(Patience));
        Assert.True(script.Join(Patience));
    }

    [Fact]
    public void ScriptsDoNotWaitForEachOther()
    {
        var gate = new GraphicsGateState();
        using var first = new ScriptHolder(gate);
        using var second = new ScriptHolder(gate); // would time out in its constructor if it waited
        Assert.True(gate.ScriptsBusy);
    }

    [Fact]
    public void AScriptReadsWhatItIsChangingOnItsOwnThread()
    {
        var gate = new GraphicsGateState();
        bool own = false;
        var script = new Thread(() =>
        {
            using (gate.EnterScript())
            {
                own = gate.TryEnterDisplay();
                if (own)
                {
                    gate.ExitDisplay();
                }
            }
        });
        script.Start();
        Assert.True(script.Join(Patience));
        Assert.True(own);
    }

    [Fact]
    public void NestedEntriesAndParksOpenAndCloseTheGateOnce()
    {
        var gate = new GraphicsGateState();
        var results = new List<bool>();
        var script = new Thread(() =>
        {
            using (gate.EnterScript())
            {
                using (gate.EnterScript())
                {
                    results.Add(gate.ScriptsBusy);                 // held
                    using (gate.Park())
                    {
                        using (gate.Park())
                        {
                            results.Add(gate.ScriptsBusy);         // open
                        }

                        results.Add(gate.ScriptsBusy);             // still open: the outer park
                    }

                    results.Add(gate.ScriptsBusy);                 // held again
                }

                results.Add(gate.ScriptsBusy);                     // still held: the outer entry
            }

            results.Add(gate.ScriptsBusy);                         // free
        });
        script.Start();
        Assert.True(script.Join(Patience));
        Assert.Equal([true, false, false, true, true, false], results);
    }

    [Fact]
    public void AParkOffAScriptThreadDoesNothing()
    {
        var gate = new GraphicsGateState();
        using (gate.Park())
        {
            Assert.False(gate.ScriptsBusy);
        }

        Assert.False(gate.ScriptsBusy);
        Assert.True(gate.TryEnterDisplay());
        gate.ExitDisplay();
    }

    [Fact]
    public async Task AScriptThreadHoldsTheProcessGateWhileItRuns()
    {
        // Whatever else is running, this script is: another thread's display read is turned away.
        bool refused = await ScriptThread.Run(() =>
        {
            bool entered = Task.Run(GraphicsGate.TryEnterDisplay).GetAwaiter().GetResult();
            if (entered)
            {
                GraphicsGate.ExitDisplay();
            }

            return !entered;
        });
        Assert.True(refused);
    }

    [Fact]
    public async Task ARunPastFiftyMillionStatementsIsNotStopped()
    {
        Assert.Equal(long.MaxValue, Interpreter.MaxSteps);
        JG.Reset();
        var output = new RecordingScriptOutput();
        await using var session = (JgsReplSession)new MatlabScriptEngine().CreateSession(
            new ScriptContext(output, (_, _) => { }, null));
        ScriptRunResult result = await session.ExecuteAsync(
            "n = 0;\nwhile n < 60000000\n    n = n + 1;\nend\nfprintf('%d\\n', n);", sourceId: "", CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Contains("60000000", output.TextSinceMark);
        JG.Reset();
    }
}
