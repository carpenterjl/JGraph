using JGraph.Numerics;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// M1's saturation rule (ADR 0162): a count that has climbed to <see cref="JgsHolders.Saturated"/>
/// never moves again, and the payload is shared for ever.
/// </summary>
/// <remarks>
/// A dead wrapper never decrements (M3), so a payload shared again and again in a long session
/// accumulates a count with no bound. An <c>int</c> that wrapped would come out negative and pass
/// the shared test as unshared, which is a shared write — the one error the model may not make.
/// Saturation is what a session would have to run for days to reach, so the count is seeded here
/// one below the sentinel and every road that moves it is asked what it does there.
/// </remarks>
public class HolderSaturationM162Tests
{
    private static NumericBuffer Payload() => new ManagedBuffer(4);

    [Fact]
    public void SharingAtTheSentinelLeavesItThere()
    {
        NumericBuffer payload = Payload();
        JgsHolders.Seed(payload, JgsHolders.Saturated - 1);

        JgsHolders.Share(payload);
        Assert.Equal(JgsHolders.Saturated, JgsHolders.Of(payload));

        JgsHolders.Share(payload);
        Assert.Equal(JgsHolders.Saturated, JgsHolders.Of(payload));
    }

    [Fact]
    public void ReleasingASaturatedPayloadNeverBringsItBack()
    {
        NumericBuffer payload = Payload();
        JgsHolders.Seed(payload, JgsHolders.Saturated);

        for (int i = 0; i < 1000; i++)
        {
            JgsHolders.Release(payload);
        }

        Assert.Equal(JgsHolders.Saturated, JgsHolders.Of(payload));
        Assert.True(JgsHolders.IsShared(payload)); // so every write detaches, and disposal skips it
    }

    [Fact]
    public void ReleasingNeverGoesBelowOneHolder()
    {
        NumericBuffer payload = Payload();
        JgsHolders.Share(payload);
        Assert.Equal(2, JgsHolders.Of(payload));

        JgsHolders.Release(payload);
        JgsHolders.Release(payload);
        JgsHolders.Release(payload);
        Assert.Equal(1, JgsHolders.Of(payload));
        Assert.False(JgsHolders.IsShared(payload));
    }

    [Fact]
    public void AnUntouchedPayloadHasOneHolder()
    {
        Assert.Equal(1, JgsHolders.Of(Payload()));
        Assert.Equal(1, JgsHolders.Of(new JgsValue[3]));           // the table's absence means one
        Assert.Equal(1, JgsHolders.Of(new Dictionary<string, JgsValue>()));
        Assert.False(JgsHolders.IsShared(Payload()));
    }

    [Fact]
    public void ConcurrentSharesNeitherLoseNorWrapACount()
    {
        NumericBuffer payload = Payload();
        const int threads = 8;
        const int each = 2000;

        Parallel.For(0, threads, _ =>
        {
            for (int i = 0; i < each; i++)
            {
                JgsHolders.Share(payload);
            }
        });

        // Zero means one, so the first share adds two and every other adds one.
        Assert.Equal((threads * each) + 1, JgsHolders.Of(payload));
    }

    [Fact]
    public void ConcurrentSharesAtTheSentinelStayThere()
    {
        NumericBuffer payload = Payload();
        JgsHolders.Seed(payload, JgsHolders.Saturated - 4);

        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 1000; i++)
            {
                JgsHolders.Share(payload);
            }
        });

        Assert.Equal(JgsHolders.Saturated, JgsHolders.Of(payload));
    }

    [Fact]
    public void ATableKeyedPayloadSaturatesTheSameWay()
    {
        var slots = new JgsValue[2];
        JgsHolders.Seed(slots, JgsHolders.Saturated - 1);

        JgsHolders.Share(slots);
        JgsHolders.Share(slots);
        JgsHolders.Release(slots);

        Assert.Equal(JgsHolders.Saturated, JgsHolders.Of(slots));
    }
}
