using JGraph.Core.Data;
using JGraph.Core.Drawing;

namespace JGraph.Objects;

/// <summary>
/// Turns a series into the stairstep path that draws it. The same expansion serves two callers: the
/// renderer, which walks the path, and MATLAB's <c>[xb, yb] = stairs(...)</c>, which hands the path
/// back to the script instead of drawing it — so both are guaranteed to describe the same shape.
/// </summary>
public static class StairSteps
{
    /// <summary>
    /// The stairstep path through <paramref name="xs"/> and <paramref name="ys"/>, as two arrays of
    /// twice the sample count. Each sample contributes a tread and the riser onto it; the last one
    /// has no tread to stand on, which is why its pair repeats the final x — MATLAB's own output.
    /// </summary>
    public static (double[] X, double[] Y) Build(double[] xs, double[] ys, StepMode mode)
    {
        int count = System.Math.Min(xs.Length, ys.Length);
        var stepX = new double[count * 2];
        var stepY = new double[count * 2];

        for (int i = 0; i < count; i++)
        {
            double previousX = xs[i == 0 ? 0 : i - 1];
            double nextX = xs[i == count - 1 ? count - 1 : i + 1];

            switch (mode)
            {
                case StepMode.Pre:
                    // The riser stands at this sample, so the tread reaching it carries the value
                    // before it — the mirror image of Post.
                    stepX[(i * 2) + 0] = xs[i];
                    stepY[(i * 2) + 0] = ys[i == 0 ? 0 : i - 1];
                    stepX[(i * 2) + 1] = xs[i];
                    stepY[(i * 2) + 1] = ys[i];
                    break;

                case StepMode.Mid:
                    stepX[(i * 2) + 0] = i == 0 ? xs[i] : Midpoint(previousX, xs[i]);
                    stepY[(i * 2) + 0] = ys[i];
                    stepX[(i * 2) + 1] = i == count - 1 ? xs[i] : Midpoint(xs[i], nextX);
                    stepY[(i * 2) + 1] = ys[i];
                    break;

                default:
                    stepX[(i * 2) + 0] = xs[i];
                    stepY[(i * 2) + 0] = ys[i];
                    stepX[(i * 2) + 1] = nextX;
                    stepY[(i * 2) + 1] = ys[i];
                    break;
            }
        }

        return (stepX, stepY);
    }

    /// <summary>The stairstep path through a series.</summary>
    public static (double[] X, double[] Y) Build(IDataSeries data, StepMode mode)
    {
        var xs = new double[data.Count];
        var ys = new double[data.Count];
        for (int i = 0; i < data.Count; i++)
        {
            xs[i] = data.GetX(i);
            ys[i] = data.GetY(i);
        }

        return Build(xs, ys, mode);
    }

    private static double Midpoint(double a, double b) =>
        double.IsFinite(a) && double.IsFinite(b) ? 0.5 * (a + b) : b;
}
