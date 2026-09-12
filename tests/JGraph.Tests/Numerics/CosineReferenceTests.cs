using System.Globalization;
using System.Numerics;
using System.Text.Json;
using JGraph.Imaging;
using JGraph.Numerics;
using Xunit;

namespace JGraph.Tests.Numerics;

/// <summary>
/// ADR 0153, stage 07b: Makhoul's reordering computes the DCT-II and DCT-III through one length-n
/// transform instead of an even extension of length 2n, and moves the last bits. Forward and
/// inverse are accepted <em>independently</em> — a permutation or sign error can cancel in a round
/// trip — against the 30-digit reference <c>tools/transforms/dct_reference.py</c> wrote
/// (<c>dct_reference.json</c>: every coefficient at the small lengths, selected ones at the
/// production lengths) and against closed-form inputs at production lengths: a constant, one
/// cosine, one impulse. The even-extension road it replaced is kept here as
/// <see cref="EvenExtensionForward"/> and <see cref="EvenExtensionInverse"/> so the ADR can say
/// which road was the more accurate on each length class, from a measurement. In the facade
/// collection because the plan cache is one static, and <see cref="TransformRoadTests"/> asserts
/// on its counts.
/// </summary>
[Collection("JG facade")]
public class CosineReferenceTests
{
    private static readonly Lazy<JsonDocument> Reference = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Numerics", "dct_reference.json"))));

    public static TheoryData<int> FullLengths() => new() { 8, 33, 100, 1000, 4096 };

    public static TheoryData<int> SelectedLengths() => new() { 1 << 20, 4_000_000 };

    public static TheoryData<int> ProductionLengths() => new() { 1 << 20, 4_000_000, 4_000_037 };

    [Theory]
    [MemberData(nameof(FullLengths))]
    public void EveryCoefficientAgreesWithTheReference(int n)
    {
        JsonElement entry = Reference.Value.RootElement.GetProperty("full").GetProperty(n.ToString(CultureInfo.InvariantCulture));
        double[] x = Lcg(n);
        double[] forward = Doubles(entry.GetProperty("forward"));
        double[] inverse = Doubles(entry.GetProperty("inverse"));

        double newForward = WorstRelative(CosineTransforms.Forward(x), forward);
        double oldForward = WorstRelative(EvenExtensionForward(x), forward);
        double newInverse = WorstRelative(CosineTransforms.Inverse(x), inverse);
        double oldInverse = WorstRelative(EvenExtensionInverse(x), inverse);
        Report($"full {n}: forward new {newForward:E2} old {oldForward:E2}; inverse new {newInverse:E2} old {oldInverse:E2}");

        Assert.True(newForward <= 1e-12, $"n = {n}: forward is {newForward:E3} from the reference (even extension: {oldForward:E3})");
        Assert.True(newInverse <= 1e-12, $"n = {n}: inverse is {newInverse:E3} from the reference (even extension: {oldInverse:E3})");
    }

    [Theory]
    [MemberData(nameof(SelectedLengths))]
    public void SelectedCoefficientsAgreeWithTheReferenceAtProductionLength(int n)
    {
        JsonElement entry = Reference.Value.RootElement.GetProperty("selected").GetProperty(n.ToString(CultureInfo.InvariantCulture));
        double[] x = Lcg(n);
        double[] forward = CosineTransforms.Forward(x);
        double[] oldForward = EvenExtensionForward(x);
        double[] inverse = CosineTransforms.Inverse(x);
        double[] oldInverse = EvenExtensionInverse(x);

        double scale = 0;
        foreach (double v in forward)
        {
            scale = Math.Max(scale, Math.Abs(v));
        }

        foreach (JsonProperty p in entry.GetProperty("forward").EnumerateObject())
        {
            int k = int.Parse(p.Name, CultureInfo.InvariantCulture);
            double want = p.Value.GetDouble();
            Report($"selected {n} forward k={k}: new {Math.Abs(forward[k] - want) / scale:E2} old {Math.Abs(oldForward[k] - want) / scale:E2}");
            Assert.True(Math.Abs(forward[k] - want) <= 1e-11 * scale, $"n = {n}, k = {k}: {forward[k]:R} is not {want:R} (scale {scale:E2})");
        }

        double xscale = 0;
        foreach (double v in inverse)
        {
            xscale = Math.Max(xscale, Math.Abs(v));
        }

        foreach (JsonProperty p in entry.GetProperty("inverse").EnumerateObject())
        {
            int j = int.Parse(p.Name, CultureInfo.InvariantCulture);
            double want = p.Value.GetDouble();
            Report($"selected {n} inverse j={j}: new {Math.Abs(inverse[j] - want) / xscale:E2} old {Math.Abs(oldInverse[j] - want) / xscale:E2}");
            Assert.True(Math.Abs(inverse[j] - want) <= 1e-11 * xscale, $"n = {n}, j = {j}: {inverse[j]:R} is not {want:R} (scale {xscale:E2})");
        }
    }

    [Theory]
    [MemberData(nameof(ProductionLengths))]
    public void ClosedFormInputsAnswerTheirClosedFormsAtProductionLength(int n)
    {
        double sqrtN = Math.Sqrt(n);
        double sqrtHalf = Math.Sqrt(n / 2.0);

        // A constant: only the DC coefficient, sqrt(n)·c.
        var x = new double[n];
        Array.Fill(x, 0.75);
        double[] d = CosineTransforms.Forward(x);
        Assert.True(Math.Abs(d[0] - (0.75 * sqrtN)) <= 1e-10 * 0.75 * sqrtN, $"constant: DC {d[0]:R}");
        AssertRestSmall(d, 0, 1e-10 * 0.75 * sqrtN, "constant");

        // One cosine at k0: one coefficient, sqrt(n/2).
        foreach (int k0 in new[] { 1, 7, n / 2 })
        {
            for (int j = 0; j < n; j++)
            {
                x[j] = CosineOf(((2L * j) + 1) * k0, n);
            }

            d = CosineTransforms.Forward(x);
            Assert.True(Math.Abs(d[k0] - sqrtHalf) <= 1e-10 * sqrtHalf, $"cos k0 = {k0}: {d[k0]:R} is not {sqrtHalf:R}");
            AssertRestSmall(d, k0, 1e-10 * sqrtHalf, $"cos k0 = {k0}");

            // And the mirror: one coefficient in, the cosine out.
            var c = new double[n];
            c[k0] = sqrtHalf;
            double[] back = CosineTransforms.Inverse(c);
            double[] oldBack = EvenExtensionInverse(c);
            double worst = 0;
            double worstOld = 0;
            for (int j = 0; j < n; j++)
            {
                worst = Math.Max(worst, Math.Abs(back[j] - x[j]));
                worstOld = Math.Max(worstOld, Math.Abs(oldBack[j] - x[j]));
            }

            Report($"closed {n} inverse of one coefficient k0={k0}: new {worst:E2} old {worstOld:E2}");
            Assert.True(worst <= 1e-10, $"inverse of one coefficient k0 = {k0}: off by {worst:E3} (even extension: {worstOld:E3})");
        }

        // An impulse at j0: coefficient k is w(k)·cos(πk(2j0+1)/2n).
        Array.Clear(x);
        int j0 = 12345;
        x[j0] = 1;
        d = CosineTransforms.Forward(x);
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        double worstImpulse = 0;
        for (int k = 0; k < n; k++)
        {
            double want = (k == 0 ? first : rest) * CosineOf((long)k * ((2L * j0) + 1), n);
            worstImpulse = Math.Max(worstImpulse, Math.Abs(d[k] - want));
        }

        Assert.True(worstImpulse <= 1e-10 * rest, $"impulse: off by {worstImpulse:E3} (scale {rest:E2})");

        // The mirror: a DC-only spectrum comes back constant.
        var dc = new double[n];
        dc[0] = 0.75 * sqrtN;
        double[] flat = CosineTransforms.Inverse(dc);
        double worstFlat = 0;
        foreach (double v in flat)
        {
            worstFlat = Math.Max(worstFlat, Math.Abs(v - 0.75));
        }

        Assert.True(worstFlat <= 1e-10, $"inverse of DC: off by {worstFlat:E3}");
    }

    [Theory]
    [InlineData(1 << 20)]
    [InlineData(4_000_000)]
    [InlineData(3_981_312)]
    [InlineData(4_000_037)]
    [InlineData(4_194_304)]
    public void TheRoundTripHoldsAtProductionLength(int n)
    {
        double[] x = Lcg(n);
        double[] back = CosineTransforms.Inverse(CosineTransforms.Forward(x));
        double worst = 0;
        for (int i = 0; i < n; i++)
        {
            worst = Math.Max(worst, Math.Abs(back[i] - x[i]));
        }

        Report($"roundtrip {n}: {worst:E2}");
        Assert.True(worst <= 1e-9, $"n = {n}: round trip off by {worst:E3}");
    }

    // --- the road that was: the even extension over a length-2n transform ---------------------

    internal static double[] EvenExtensionForward(ReadOnlySpan<double> values)
    {
        int n = values.Length;
        var re = new double[2 * n];
        var im = new double[2 * n];
        for (int i = 0; i < n; i++)
        {
            re[i] = values[i];
            re[(2 * n) - 1 - i] = values[i];
        }

        FftKernels.Transform(re, im, 2 * n, inverse: false, inside: true);
        var result = new double[n];
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        for (int k = 0; k < n; k++)
        {
            double angle = -Math.PI * k / (2.0 * n);
            double half = ((re[k] * Math.Cos(angle)) - (im[k] * Math.Sin(angle))) / 2.0;
            result[k] = half * (k == 0 ? first : rest);
        }

        return result;
    }

    internal static double[] EvenExtensionInverse(ReadOnlySpan<double> coefficients)
    {
        int n = coefficients.Length;
        var re = new double[2 * n];
        var im = new double[2 * n];
        double first = Math.Sqrt(1.0 / n);
        double rest = Math.Sqrt(2.0 / n);
        for (int k = 0; k < n; k++)
        {
            double weight = coefficients[k] * (k == 0 ? first : rest);
            double angle = Math.PI * k / (2.0 * n);
            re[k] = weight * Math.Cos(angle);
            im[k] = weight * Math.Sin(angle);
        }

        FftKernels.Transform(re, im, 2 * n, inverse: true, inside: true);
        var result = new double[n];
        for (int j = 0; j < n; j++)
        {
            result[j] = re[j] * 2 * n;
        }

        return result;
    }

    // --- helpers ------------------------------------------------------------------------------

    /// <summary>The reference script's LCG, step for step: every state is an exact integer.</summary>
    internal static double[] Lcg(int n)
    {
        var x = new double[n];
        long state = 12345;
        for (int i = 0; i < n; i++)
        {
            state = ((1103515245L * state) + 12345L) % (1L << 31);
            x[i] = (state / (double)(1L << 31)) - 0.5;
        }

        return x;
    }

    /// <summary>
    /// cos(π·numerator / 2n) with the numerator reduced modulo 4n first, so the angle is one turn
    /// at most. Unreduced, π·(2j+1)·k0/2n reaches π·n/2 at k0 = n/2, and a double argument that
    /// large carries about 1e-9 of rounding before the cosine is taken — an error of the
    /// expectation, not of the transform, and one both roads reproduced to three digits.
    /// </summary>
    private static double CosineOf(long numerator, int n) => Math.Cos(Math.PI * (numerator % (4L * n)) / (2.0 * n));

    private static double[] Doubles(JsonElement array)
    {
        var values = new double[array.GetArrayLength()];
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            values[i++] = e.GetDouble();
        }

        return values;
    }

    /// <summary>The largest |got − want| over the vector, relative to the largest |want|.</summary>
    private static double WorstRelative(double[] got, double[] want)
    {
        double scale = 0;
        foreach (double v in want)
        {
            scale = Math.Max(scale, Math.Abs(v));
        }

        double worst = 0;
        for (int i = 0; i < want.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(got[i] - want[i]));
        }

        return worst / scale;
    }

    private static void AssertRestSmall(double[] d, int except, double bound, string what)
    {
        double worst = 0;
        int at = -1;
        for (int k = 0; k < d.Length; k++)
        {
            if (k != except && Math.Abs(d[k]) > worst)
            {
                worst = Math.Abs(d[k]);
                at = k;
            }
        }

        Assert.True(worst <= bound, $"{what}: coefficient {at} is {worst:E3}, bound {bound:E3}");
    }

    /// <summary>With JGRAPH_DCT_REPORT set to a path, one line per measurement for the ADR.</summary>
    private static void Report(string line)
    {
        if (Environment.GetEnvironmentVariable("JGRAPH_DCT_REPORT") is { Length: > 0 } path)
        {
            lock (Reference)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
    }
}
