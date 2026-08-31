using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// M119 — ode45 answers the shape MATLAB's does. The method was already Dormand–Prince 5(4) and
/// already correct; what it lacked was the pair's continuous extension, and so it reported one point
/// per accepted step where MATLAB reports four.
/// </summary>
/// <remarks>
/// The difference is not cosmetic. A step is a coarse thing — over sixty time units of the Lorenz
/// attractor the method takes some eight hundred of them — and a trajectory drawn corner to corner
/// through eight hundred points is a polygon. It also made every count that crossed the two engines
/// disagree by a factor of about four, which is the whole of what the head-to-head suite was
/// reporting as a divergence.
/// </remarks>
public class OdeSolversM119Tests
{
    /// <summary>dy/dt = y, whose answer is e^t — steep enough that a chord is a poor stand-in for it.</summary>
    private static double[] Exponential(double t, double[] y) => [y[0]];

    /// <summary>The circle as a system: y'' = -y from [1; 0], whose first component is cos t.</summary>
    private static double[] Circle(double t, double[] y) => [y[1], -y[0]];

    [Fact]
    public void RefineReportsFourPointsPerAcceptedStep()
    {
        // Refine changes what is reported and nothing about the integration, so the two runs take the
        // same steps and one answers exactly four points for each point the other answers.
        List<OdeSolvers.OdePoint> coarse =
            OdeSolvers.DormandPrince(Exponential, [0, 5], [1], refine: 1);
        List<OdeSolvers.OdePoint> refined =
            OdeSolvers.DormandPrince(Exponential, [0, 5], [1]);

        Assert.True(coarse.Count > 2, "the problem should need more than one step");
        Assert.Equal(4 * (coarse.Count - 1), refined.Count - 1);
        Assert.Equal(OdeSolvers.DefaultRefine, 4);

        // Every step's own endpoint is still in the answer, and the last one is the end of the span.
        for (int i = 0; i < coarse.Count; i++)
        {
            Assert.Equal(coarse[i].Time, refined[i * 4].Time, 12);
        }

        Assert.Equal(5, refined[^1].Time, 12);
    }

    [Fact]
    public void ThePointsInsideAStepAreOnTheCurveAndNotOnItsChord()
    {
        // The whole value of the continuous extension: the three extra points per step are read off a
        // quartic that agrees with the method at both ends, not interpolated along the straight line
        // between them. On e^t a chord at these step sizes is wrong in the third decimal, so a chord
        // would fail this by two orders of magnitude.
        List<OdeSolvers.OdePoint> answer = OdeSolvers.DormandPrince(Exponential, [0, 5], [1]);

        double worst = 0;
        foreach (OdeSolvers.OdePoint point in answer)
        {
            double exact = System.Math.Exp(point.Time);
            worst = System.Math.Max(worst, System.Math.Abs(point.State[0] - exact) / exact);
        }

        Assert.True(worst < 1e-4, $"relative error {worst:g3} over the whole reported answer");
    }

    [Fact]
    public void TheStepEndpointsAreTheOnesTheChordWouldMiss()
    {
        // Named so the previous test cannot pass by accident on a problem too gentle to tell the two
        // apart: here the chord through each step is measured directly and shown to be far worse.
        List<OdeSolvers.OdePoint> answer = OdeSolvers.DormandPrince(Exponential, [0, 5], [1]);

        double worstChord = 0;
        for (int step = 0; step + 4 < answer.Count; step += 4)
        {
            OdeSolvers.OdePoint from = answer[step];
            OdeSolvers.OdePoint to = answer[step + 4];
            OdeSolvers.OdePoint middle = answer[step + 2];

            double along = (middle.Time - from.Time) / (to.Time - from.Time);
            double chord = from.State[0] + (along * (to.State[0] - from.State[0]));
            double exact = System.Math.Exp(middle.Time);
            worstChord = System.Math.Max(worstChord, System.Math.Abs(chord - exact) / exact);
        }

        Assert.True(worstChord > 1e-3, $"the chord is only {worstChord:g3} off — too gentle to tell");
    }

    [Fact]
    public void NamedTimesAreTheOnlyOnesReportedAndAreHitExactly()
    {
        double[] wanted = [0, 0.25, 1.0, 2.0, 3.5, 4.0];
        List<OdeSolvers.OdePoint> answer = OdeSolvers.DormandPrince(Exponential, wanted, [1]);

        Assert.Equal(wanted.Length, answer.Count);
        for (int i = 0; i < wanted.Length; i++)
        {
            Assert.Equal(wanted[i], answer[i].Time, 12);

            // The tolerance the solver was given is a relative one, so this is measured the same way:
            // e^4 is fifty-odd, and four decimal places of it is a far tighter demand than 1e-3.
            double exact = System.Math.Exp(wanted[i]);
            Assert.True(
                System.Math.Abs(answer[i].State[0] - exact) / exact < 1e-4,
                $"e^{wanted[i]}: {answer[i].State[0]:g10} against {exact:g10}");
        }
    }

    [Fact]
    public void ANamedTimeIsReadOffTheStepAndDoesNotCutIt()
    {
        // A caller who names times is not asking the method to land on them. Clipping every step to
        // the next request makes the integration follow the request rather than the equation, which
        // shows up as a step count that answers to the sampling: asking for a thousand points must
        // not cost a thousand steps.
        var closelySpaced = new double[1001];
        for (int i = 0; i < closelySpaced.Length; i++)
        {
            closelySpaced[i] = i * 5.0 / (closelySpaced.Length - 1);
        }

        int evaluations = 0;
        double[] Counted(double t, double[] y)
        {
            evaluations++;
            return [y[0]];
        }

        List<OdeSolvers.OdePoint> answer = OdeSolvers.DormandPrince(Counted, closelySpaced, [1]);

        Assert.Equal(closelySpaced.Length, answer.Count);

        // Six stages a step, plus the one before the loop. A step per requested point would be six
        // thousand; the equation needs some tens.
        Assert.True(evaluations < 600, $"{evaluations} derivative evaluations for 1001 sample points");
    }

    [Fact]
    public void TheAnswerStillTracksTheClosedFormItAlwaysDid()
    {
        List<OdeSolvers.OdePoint> answer =
            OdeSolvers.DormandPrince(Circle, [0, System.Math.PI], [1, 0]);

        Assert.Equal(System.Math.PI, answer[^1].Time, 12);

        double worst = 0;
        foreach (OdeSolvers.OdePoint point in answer)
        {
            worst = System.Math.Max(worst, System.Math.Abs(point.State[0] - System.Math.Cos(point.Time)));
            worst = System.Math.Max(worst, System.Math.Abs(point.State[1] + System.Math.Sin(point.Time)));
        }

        Assert.True(worst < 1e-4, $"worst departure from the circle was {worst:g3}");
        Assert.True(System.Math.Abs(answer[^1].State[0] + 1) < 1e-4);
    }

    [Fact]
    public void ATighterToleranceIsAnsweredWithMoreStepsAndABetterAnswer()
    {
        List<OdeSolvers.OdePoint> loose =
            OdeSolvers.DormandPrince(Exponential, [0, 5], [1], 1e-3, 1e-6);
        List<OdeSolvers.OdePoint> tight =
            OdeSolvers.DormandPrince(Exponential, [0, 5], [1], 1e-9, 1e-12);

        Assert.True(tight.Count > loose.Count);

        double looseError = System.Math.Abs(loose[^1].State[0] - System.Math.Exp(5));
        double tightError = System.Math.Abs(tight[^1].State[0] - System.Math.Exp(5));
        Assert.True(tightError < looseError);
        Assert.True(tightError / System.Math.Exp(5) < 1e-9);
    }

    [Fact]
    public void BackwardsInTimeIsTheSameMethodWithTheSignTurnedRound()
    {
        List<OdeSolvers.OdePoint> answer =
            OdeSolvers.DormandPrince(Exponential, [5, 0], [System.Math.Exp(5)]);

        Assert.Equal(0, answer[^1].Time, 12);
        Assert.True(System.Math.Abs(answer[^1].State[0] - 1) < 1e-3, $"came back to {answer[^1].State[0]:g10}");
        Assert.Equal(0, (answer.Count - 1) % 4);
    }
}
