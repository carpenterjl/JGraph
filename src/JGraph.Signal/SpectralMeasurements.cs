namespace JGraph.Signal;

/// <summary>
/// The measurements taken off a power spectrum: how much power lies in a band, where its centre of
/// mass and its median lie, and how wide a band has to be to hold a stated share of the power.
/// </summary>
/// <remarks>
/// All of them share one small piece of arithmetic — the width each estimate stands for, which is
/// the gap to the next bin except at whichever end is missing one — and one habit: the cumulative
/// power is interpolated at the midpoints between bins rather than at the bins, so a band edge that
/// falls between two estimates gets a fraction of each.
/// </remarks>
public static class SpectralMeasurements
{
    /// <summary>MATLAB's <c>specfreqwidth</c>: the frequency width each estimate stands for.</summary>
    public static double[] Widths(double[] f)
    {
        ArgumentNullException.ThrowIfNull(f);
        int n = f.Length;
        var width = new double[n];
        double missing = n > 1 ? (f[n - 1] - f[0]) / (n - 1) : 0;
        bool centered = f[0] != 0;
        for (int i = 0; i < n; i++)
        {
            if (centered)
            {
                width[i] = i == 0 ? missing : f[i] - f[i - 1];
            }
            else
            {
                width[i] = i == n - 1 ? missing : f[i + 1] - f[i];
            }
        }

        return width;
    }

    /// <summary>The cumulative power and the frequencies it is cumulative at (the bin midpoints).</summary>
    public static (double[] Cumulative, double[] Edges) Cumulative(double[] pxx, double[] f)
    {
        double[] width = Widths(f);
        var cumulative = new double[pxx.Length + 1];
        for (int i = 0; i < pxx.Length; i++)
        {
            cumulative[i + 1] = cumulative[i] + (pxx[i] * width[i]);
        }

        var edges = new double[f.Length + 1];
        edges[0] = f[0];
        for (int i = 0; i < f.Length - 1; i++)
        {
            edges[i + 1] = (f[i] + f[i + 1]) / 2;
        }

        edges[f.Length] = f[^1];
        return (cumulative, edges);
    }

    /// <summary>Straight-line interpolation, MATLAB's <c>linterp</c>.</summary>
    public static double Interpolate(double yPre, double yPost, double xPre, double xPost, double x) =>
        yPre + ((yPost - yPre) * (x - xPre) / (xPost - xPre));

    /// <summary>The cumulative power at a frequency between two edges.</summary>
    public static double PowerAt(double[] cumulative, double[] edges, double f)
    {
        int at = -1;
        for (int i = 0; i < edges.Length; i++)
        {
            if (f <= edges[i])
            {
                at = i;
                break;
            }
        }

        if (at < 0)
        {
            return double.NaN;
        }

        return at == 0
            ? Interpolate(cumulative[0], cumulative[1], edges[0], edges[1], f)
            : Interpolate(cumulative[at], cumulative[at - 1], edges[at], edges[at - 1], f);
    }

    /// <summary>The frequency at which the cumulative power first reaches a level.</summary>
    public static double FrequencyAt(double[] cumulative, double[] edges, double level)
    {
        int at = -1;
        for (int i = 0; i < cumulative.Length; i++)
        {
            if (level <= cumulative[i])
            {
                at = i;
                break;
            }
        }

        if (at < 0)
        {
            return double.NaN;
        }

        if (at == 0)
        {
            at = 1;
        }

        return Interpolate(edges[at - 1], edges[at], cumulative[at - 1], cumulative[at], level);
    }

    /// <summary>MATLAB's <c>meanfreq</c>: the first moment of the power over a band.</summary>
    public static (double Frequency, double Power) MeanFrequency(double[] pxx, double[] f, double lo, double hi)
    {
        double[] width = Widths(f);
        double power = 0;
        double moment = 0;
        for (int i = 0; i < pxx.Length; i++)
        {
            if (f[i] < lo || f[i] > hi)
            {
                continue;
            }

            double p = width[i] * pxx[i];
            power += p;
            moment += p * f[i];
        }

        return (moment / power, power);
    }

    /// <summary>MATLAB's <c>medfreq</c>: the frequency that splits the band's power in half.</summary>
    public static (double Frequency, double Power) MedianFrequency(double[] pxx, double[] f, double lo, double hi)
    {
        (double[] cumulative, double[] edges) = Cumulative(pxx, f);
        double low = PowerAt(cumulative, edges, lo);
        double high = PowerAt(cumulative, edges, hi);
        double frequency = FrequencyAt(cumulative, edges, (low + high) / 2);
        return (frequency, high - low);
    }

    /// <summary>MATLAB's <c>obw</c>: the band, centred on the power, that holds a stated percentage of it.</summary>
    public static (double Bandwidth, double Low, double High, double Power) OccupiedBandwidth(
        double[] pxx, double[] f, double lo, double hi, double percent)
    {
        (double[] cumulative, double[] edges) = Cumulative(pxx, f);
        double low = PowerAt(cumulative, edges, lo);
        double high = PowerAt(cumulative, edges, hi);
        double total = high - low;
        double flo = FrequencyAt(cumulative, edges, low + ((100 - percent) / 200 * total));
        double fhi = FrequencyAt(cumulative, edges, low + ((100 + percent) / 200 * total));
        return (fhi - flo, flo, fhi, percent / 100 * total);
    }

    /// <summary>
    /// MATLAB's <c>powerbw</c>: the width of the band over which the density stays within
    /// <paramref name="rolloff"/> decibels of its reference level.
    /// </summary>
    public static (double Bandwidth, double Low, double High, double Power) PowerBandwidth(
        double[] pxx, double[] f, double[]? range, double rolloff, bool hasNyquist, bool fromTime)
    {
        var levels = (double[])pxx.Clone();
        double reference;
        int left;
        int right;
        double[] width = Widths(f);
        if (range is null)
        {
            int centre = 0;
            for (int i = 1; i < levels.Length; i++)
            {
                if (levels[i] > levels[centre])
                {
                    centre = i;
                }
            }

            reference = levels[centre] * System.Math.Pow(10, rolloff / 10);
            left = centre;
            right = centre;
        }
        else
        {
            double total = 0;
            double span = 0;
            for (int i = 0; i < levels.Length; i++)
            {
                if (f[i] >= range[0] && f[i] <= range[1])
                {
                    total += levels[i] * width[i];
                    span += width[i];
                }
            }

            reference = total / span * System.Math.Pow(10, rolloff / 10);
            left = -1;
            right = -1;
            double centre = (range[0] + range[1]) / 2;
            for (int i = 0; i < f.Length; i++)
            {
                if (f[i] < centre)
                {
                    left = i;
                }
            }

            for (int i = 0; i < f.Length; i++)
            {
                if (f[i] > centre)
                {
                    right = i;
                    break;
                }
            }
        }

        // The doubling below is MATLAB's: the reference level is a two-sided density, so the bins
        // that are not mirrored have to be counted twice before they are compared with it.
        if (f[0] == 0)
        {
            levels[0] *= 2;
        }

        if (hasNyquist && fromTime)
        {
            levels[^1] *= 2;
        }

        int belowLeft = -1;
        int belowRight = -1;
        int scanLeft = range is null ? left : right;
        int scanRight = range is null ? right : left;
        if (scanLeft >= 0)
        {
            for (int i = 0; i <= scanLeft && i < levels.Length; i++)
            {
                if (levels[i] <= reference)
                {
                    belowLeft = i;
                }
            }
        }

        if (scanRight >= 0)
        {
            for (int i = scanRight; i < levels.Length; i++)
            {
                if (levels[i] <= reference)
                {
                    belowRight = i;
                    break;
                }
            }
        }

        double flo = Edge(belowLeft, 1, levels, f, reference, right, ascending: true);
        double fhi = Edge(belowRight, -1, levels, f, reference, left, ascending: false);
        (double[] cumulative, double[] edges) = Cumulative(pxx, f);
        double power = PowerAt(cumulative, edges, fhi) - PowerAt(cumulative, edges, flo);
        return (fhi - flo, flo, fhi, power);
    }

    private static double Edge(
        int at, int step, double[] levels, double[] f, double reference, int forbidden, bool ascending)
    {
        if (at < 0)
        {
            return ascending ? f[0] : f[^1];
        }

        if (at == forbidden && forbidden >= 0)
        {
            return double.NaN;
        }

        int other = at + step;
        if (other < 0 || other >= levels.Length)
        {
            return f[at];
        }

        const double tiny = 2.2250738585072014e-308;
        return Interpolate(
            f[at],
            f[other],
            System.Math.Log10(System.Math.Max(levels[at], tiny)),
            System.Math.Log10(System.Math.Max(levels[other], tiny)),
            System.Math.Log10(reference));
    }

    /// <summary>MATLAB's <c>bandpower</c> over a spectrum: the rectangle rule over the band.</summary>
    public static double BandPower(double[] pxx, double[] f, double[]? range)
    {
        double lo = range?[0] ?? f[0];
        double hi = range?[1] ?? f[^1];
        int first = 0;
        for (int i = 0; i < f.Length; i++)
        {
            if (f[i] <= lo)
            {
                first = i;
            }
        }

        int last = f.Length - 1;
        for (int i = 0; i < f.Length; i++)
        {
            if (f[i] >= hi)
            {
                last = i;
                break;
            }
        }

        double[] width;
        if (range is not null)
        {
            // A named range stops at its last estimate rather than counting a width past it.
            width = new double[f.Length];
            for (int i = 0; i < f.Length - 1; i++)
            {
                width[i] = f[i + 1] - f[i];
            }
        }
        else
        {
            width = Widths(f);
        }

        double power = 0;
        for (int i = first; i <= last; i++)
        {
            power += width[i] * pxx[i];
        }

        return power;
    }

    /// <summary>MATLAB's <c>enbw</c>: a window's equivalent noise bandwidth.</summary>
    public static double EquivalentNoiseBandwidth(double[] window, double? fs)
    {
        double energy = 0;
        double sum = 0;
        foreach (double w in window)
        {
            energy += w * w;
            sum += w;
        }

        int n = window.Length;
        double bandwidth = (energy / n) / ((sum / n) * (sum / n));
        return fs is double rate ? bandwidth * rate / n : bandwidth;
    }
}
