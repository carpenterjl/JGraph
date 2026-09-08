namespace JGraph.Signal;

/// <summary>
/// The distortion measurements: the ones that find a tone in a spectrum, decide which bins belong
/// to it, and compare what is under those bins with what is left over.
/// </summary>
/// <remarks>
/// Every one of them is built on the same reading of a peak. A tone is not one bin: it is the run of
/// bins around a maximum over which the density is still falling away on both sides, and its power
/// is the area under that run. Once a tone has been measured it is zeroed out, so the next search
/// finds the next one. Which of them a name reports — the harmonics, the largest leftover, or all of
/// it — is the only thing that differs.
/// </remarks>
public static class HarmonicMeasurements
{
    /// <summary>A tone found in a spectrum: its power, its centre, and the bins it occupies.</summary>
    public readonly record struct Tone(double Power, double Frequency, int Peak, int Left, int Right)
    {
        /// <summary>True when a tone was actually found rather than searched for out of range.</summary>
        public bool Found => !double.IsNaN(Power);
    }

    /// <summary>A tone that was looked for and is not there.</summary>
    public static Tone Missing => new(double.NaN, double.NaN, -1, -1, -1);

    /// <summary>
    /// MATLAB's <c>getToneFromPSD</c>: the largest peak, or the peak nearest a named frequency, with
    /// the run of bins it covers and the power under them.
    /// </summary>
    public static Tone FindTone(double[] pxx, double[] f, double rbw, double? near = null)
    {
        int peak;
        if (near is double target)
        {
            if (target < f[0] || target > f[^1])
            {
                return Missing;
            }

            int nearest = 0;
            double best = System.Math.Abs(f[0] - target);
            for (int i = 1; i < f.Length; i++)
            {
                double distance = System.Math.Abs(f[i] - target);
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }

            int low = System.Math.Max(0, nearest - 1);
            int high = System.Math.Min(nearest + 1, pxx.Length - 1);
            peak = low;
            for (int i = low; i <= high; i++)
            {
                if (pxx[i] > pxx[peak])
                {
                    peak = i;
                }
            }
        }
        else
        {
            peak = 0;
            for (int i = 1; i < pxx.Length; i++)
            {
                if (pxx[i] > pxx[peak])
                {
                    peak = i;
                }
            }
        }

        int left = peak - 1;
        while (left >= 0 && pxx[left] <= pxx[left + 1])
        {
            left--;
        }

        int right = peak + 1;
        while (right < pxx.Length && pxx[right - 1] >= pxx[right])
        {
            right++;
        }

        left++;
        right--;

        double moment = 0;
        double sum = 0;
        for (int i = left; i <= right; i++)
        {
            moment += f[i] * pxx[i];
            sum += pxx[i];
        }

        double frequency = moment / sum;
        double power;
        if (left < right)
        {
            var band = new double[right - left + 1];
            var grid = new double[right - left + 1];
            Array.Copy(pxx, left, band, 0, band.Length);
            Array.Copy(f, left, grid, 0, grid.Length);
            power = SpectralMeasurements.BandPower(band, grid, null);
        }
        else if (right > 0 && right < pxx.Length - 1)
        {
            power = pxx[right] * (f[right + 1] - f[right - 1]) / 2;
        }
        else
        {
            double step = 0;
            for (int i = 1; i < f.Length; i++)
            {
                step += f[i] - f[i - 1];
            }

            power = pxx[right] * step / (f.Length - 1);
        }

        if (power < rbw * pxx[peak])
        {
            power = rbw * pxx[peak];
            frequency = f[peak];
        }

        return new Tone(power, frequency, peak, left, right);
    }

    /// <summary>A harmonic's frequency once it has been folded back below Nyquist.</summary>
    public static double Alias(double f, double fs)
    {
        double wrapped = f % fs;
        return wrapped > fs / 2 ? fs - wrapped : wrapped;
    }

    /// <summary>Zeroes the bins a tone occupies, so the next search finds something else.</summary>
    public static void Clear(double[] pxx, Tone tone)
    {
        if (tone.Left < 0 || tone.Right < 0)
        {
            return;
        }

        for (int i = tone.Left; i <= tone.Right && i < pxx.Length; i++)
        {
            pxx[i] = 0;
        }
    }

    /// <summary>What the harmonic measurements have in common: the DC bin cleared and the fundamental found.</summary>
    public static (double[] Working, Tone Fundamental) Prepare(double[] pxx, double[] f, double rbw)
    {
        var working = (double[])pxx.Clone();
        working[0] *= 2;
        Tone dc = FindTone(working, f, rbw, 0);
        Clear(working, dc);
        Tone fundamental = FindTone(working, f, rbw);
        return (working, fundamental);
    }

    /// <summary>MATLAB's <c>thd</c>: the harmonics' total power against the fundamental's.</summary>
    public static (double Ratio, double[] Powers, double[] Frequencies) TotalHarmonicDistortion(
        double[] pxx, double[] f, double rbw, int harmonics, double? aliasFs)
    {
        (double[] working, Tone fundamental) = Prepare(pxx, f, rbw);
        var powers = new double[harmonics];
        var frequencies = new double[harmonics];
        Array.Fill(powers, double.NaN);
        Array.Fill(frequencies, double.NaN);
        powers[0] = fundamental.Power;
        frequencies[0] = fundamental.Frequency;
        double sum = 0;
        for (int i = 2; i <= harmonics; i++)
        {
            double where = i * fundamental.Frequency;
            if (aliasFs is double fs)
            {
                where = Alias(where, fs);
            }

            Tone tone = FindTone(working, f, rbw, where);
            powers[i - 1] = tone.Power;
            frequencies[i - 1] = tone.Frequency;
            if (tone.Found)
            {
                sum += tone.Power;
            }
        }

        var decibels = new double[harmonics];
        for (int i = 0; i < harmonics; i++)
        {
            decibels[i] = 10 * System.Math.Log10(powers[i]);
        }

        return (10 * System.Math.Log10(sum / powers[0]), decibels, frequencies);
    }

    /// <summary>MATLAB's <c>snr</c>: the fundamental against everything but it and its harmonics.</summary>
    public static (double Ratio, double NoisePower) SignalToNoise(
        double[] pxx, double[] f, double rbw, int harmonics, double? aliasFs)
    {
        (double[] working, Tone fundamental) = Prepare(pxx, f, rbw);
        Clear(working, fundamental);
        for (int i = 2; i <= harmonics; i++)
        {
            double where = i * fundamental.Frequency;
            if (aliasFs is double fs)
            {
                where = Alias(where, fs);
            }

            Tone tone = FindTone(working, f, rbw, where);
            if (tone.Found)
            {
                Clear(working, tone);
            }
        }

        double noise = FloorAndTotal(working, pxx, f);
        return (10 * System.Math.Log10(fundamental.Power / noise), 10 * System.Math.Log10(noise));
    }

    /// <summary>MATLAB's <c>sinad</c>: the fundamental against everything else, harmonics included.</summary>
    public static (double Ratio, double NoisePower) SignalToNoiseAndDistortion(
        double[] pxx, double[] f, double rbw)
    {
        (double[] working, Tone fundamental) = Prepare(pxx, f, rbw);
        Clear(working, fundamental);
        double noise = FloorAndTotal(working, pxx, f);
        return (10 * System.Math.Log10(fundamental.Power / noise), 10 * System.Math.Log10(noise));
    }

    /// <summary>
    /// The noise floor filled into the holes the tones left, capped by the original spectrum so the
    /// estimate can never invent power a low peak did not have.
    /// </summary>
    private static double FloorAndTotal(double[] working, double[] original, double[] f)
    {
        var positive = new List<double>();
        foreach (double value in working)
        {
            if (value > 0)
            {
                positive.Add(value);
            }
        }

        positive.Sort();
        double median = positive.Count == 0
            ? 0
            : positive.Count % 2 == 1
                ? positive[positive.Count / 2]
                : (positive[(positive.Count / 2) - 1] + positive[positive.Count / 2]) / 2;
        for (int i = 0; i < working.Length; i++)
        {
            if (working[i] == 0)
            {
                working[i] = median;
            }

            working[i] = System.Math.Min(working[i], original[i]);
        }

        return SpectralMeasurements.BandPower(working, f, null);
    }

    /// <summary>MATLAB's <c>sfdr</c>: the fundamental against the largest thing that is not it.</summary>
    public static (double Ratio, double SpurPower, double SpurFrequency) SpuriousFreeRange(
        double[] pxx, double[] f, double rbw, double separation)
    {
        (double[] working, Tone fundamental) = Prepare(pxx, f, rbw);
        Clear(working, fundamental);
        for (int i = 0; i < working.Length; i++)
        {
            if (System.Math.Abs(f[i] - fundamental.Frequency) < separation)
            {
                working[i] = 0;
            }
        }

        int largest = 0;
        for (int i = 1; i < working.Length; i++)
        {
            if (working[i] > working[largest])
            {
                largest = i;
            }
        }

        Tone spur = FindTone(working, f, rbw, f[largest]);
        return (
            10 * System.Math.Log10(fundamental.Power / spur.Power),
            10 * System.Math.Log10(spur.Power),
            spur.Frequency);
    }

    /// <summary>MATLAB's <c>toi</c>: the third-order intercept of a two-tone measurement.</summary>
    public static (double Intercept, double[] FundamentalPowers, double[] FundamentalFrequencies,
        double[] IntermodulationPowers, double[] IntermodulationFrequencies) ThirdOrderIntercept(
        double[] pxx, double[] f, double rbw)
    {
        var working = (double[])pxx.Clone();
        working[0] *= 2;
        Tone dc = FindTone(working, f, rbw, 0);
        Clear(working, dc);

        Tone first = FindTone(working, f, rbw);
        double[]? saved = null;
        if (first.Left >= 0)
        {
            saved = new double[first.Right - first.Left + 1];
            Array.Copy(working, first.Left, saved, 0, saved.Length);
            Clear(working, first);
        }

        Tone second = FindTone(working, f, rbw);
        if (saved is not null)
        {
            Array.Copy(saved, 0, working, first.Left, saved.Length);
        }

        double[] powers = [first.Power, second.Power];
        double[] frequencies = [first.Frequency, second.Frequency];
        if (frequencies[1] < frequencies[0])
        {
            (powers[0], powers[1]) = (powers[1], powers[0]);
            (frequencies[0], frequencies[1]) = (frequencies[1], frequencies[0]);
        }

        Tone lower = FindTone(working, f, rbw, (2 * frequencies[0]) - frequencies[1]);
        Tone upper = FindTone(working, f, rbw, (2 * frequencies[1]) - frequencies[0]);
        double[] modulation = [lower.Power, upper.Power];
        double[] modulationFrequencies = [lower.Frequency, upper.Frequency];
        if (dc.Right >= 0
            && (double.IsNaN(modulationFrequencies[0]) || modulationFrequencies[0] <= f[dc.Right]))
        {
            modulation[0] = double.NaN;
            modulationFrequencies[0] = double.NaN;
        }

        var fundamentalDb = new double[2];
        var modulationDb = new double[2];
        for (int i = 0; i < 2; i++)
        {
            fundamentalDb[i] = 10 * System.Math.Log10(powers[i]);
            modulationDb[i] = 10 * System.Math.Log10(modulation[i]);
        }

        double carrier = (fundamentalDb[0] + fundamentalDb[1]) / 2;
        double suppression = carrier - ((modulationDb[0] + modulationDb[1]) / 2);
        return (carrier + (suppression / 2), fundamentalDb, frequencies, modulationDb, modulationFrequencies);
    }
}
