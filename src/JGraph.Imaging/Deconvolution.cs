using System.Numerics;

namespace JGraph.Imaging;

/// <summary>
/// The four deblurring methods: Wiener, regularized least squares, Richardson–Lucy, and the blind
/// form that estimates the blur along with the picture.
/// </summary>
/// <remarks>
/// Blurring destroys information, so undoing it is not a matter of dividing it back out. Dividing by
/// the transfer function amplifies whatever sits where the blur left nothing — and what sits there is
/// noise, multiplied by an enormous number. Every method here is a different answer to the same
/// question: how much to trust the data at frequencies the blur has already flattened. Wiener answers
/// with a fixed noise-to-signal ratio, regularization answers by preferring smooth results, and
/// Richardson–Lucy answers by only ever multiplying, which keeps the answer positive and its total
/// brightness intact.
/// </remarks>
public static class Deconvolution
{
    /// <summary>The Laplacian <c>deconvreg</c> penalizes by when no other operator is given.</summary>
    public static double[,] Laplacian => new double[,]
    {
        { 0, -1, 0 },
        { -1, 4, -1 },
        { 0, -1, 0 },
    };

    /// <summary>
    /// Wiener deconvolution: the linear filter that minimizes the expected squared error, given how
    /// much noise there is relative to signal.
    /// </summary>
    /// <param name="image">The blurred picture.</param>
    /// <param name="psf">The blur's point spread function.</param>
    /// <param name="noiseToSignal">
    /// The noise-to-signal power ratio at each frequency, row-major, or null to use
    /// <paramref name="constantRatio"/> everywhere.
    /// </param>
    /// <param name="constantRatio">The ratio to use when no spectrum is given.</param>
    /// <returns>The deblurred picture.</returns>
    /// <remarks>
    /// The filter is <c>conj(H)/(|H|² + NSR)</c>. Where the blur passed the signal through, <c>|H|²</c>
    /// dominates and this is division — the inverse filter. Where the blur killed it, the ratio
    /// dominates and this goes smoothly to zero instead of to infinity. That single added term is the
    /// whole difference between a deblurred picture and a screenful of amplified noise.
    /// </remarks>
    public static double[,] Wiener(
        double[,] image, double[,] psf, IReadOnlyList<double>? noiseToSignal, double constantRatio)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(psf);
        int height = image.GetLength(0);
        int width = image.GetLength(1);

        Complex[] transfer = FilterDesign.PsfToOtf(psf, height, width);
        Complex[] spectrum = Spectrum(image);
        if (noiseToSignal is not null && noiseToSignal.Count != spectrum.Length)
        {
            throw new ArgumentException(
                "the noise-to-signal spectrum does not match the picture.", nameof(noiseToSignal));
        }

        for (int i = 0; i < spectrum.Length; i++)
        {
            double power = (transfer[i] * Complex.Conjugate(transfer[i])).Real;
            double ratio = noiseToSignal is null ? constantRatio : noiseToSignal[i];
            double denominator = power + ratio;
            spectrum[i] = denominator <= 0
                ? Complex.Zero
                : Complex.Conjugate(transfer[i]) * spectrum[i] / denominator;
        }

        return Picture(spectrum, height, width);
    }

    /// <summary>
    /// The power spectrum an autocorrelation stands for, which is the form Wiener deconvolution wants
    /// its noise and signal statistics in.
    /// </summary>
    /// <param name="autocorrelation">The autocorrelation, centred like a spread function.</param>
    /// <param name="height">The picture height.</param>
    /// <param name="width">The picture width.</param>
    /// <returns>The power at each frequency, row-major.</returns>
    public static double[] PowerSpectrum(double[,] autocorrelation, int height, int width)
    {
        Complex[] transform = FilterDesign.PsfToOtf(autocorrelation, height, width);
        var power = new double[transform.Length];
        for (int i = 0; i < power.Length; i++)
        {
            // An autocorrelation's transform is real by construction; the imaginary part that survives
            // rounding is noise, not information.
            power[i] = transform[i].Real;
        }

        return power;
    }

    /// <summary>
    /// Regularized deconvolution: the result that fits the data to within the stated noise while
    /// staying as smooth as that allows.
    /// </summary>
    /// <param name="image">The blurred picture.</param>
    /// <param name="psf">The blur's point spread function.</param>
    /// <param name="noisePower">The noise variance per pixel; zero means fit the data exactly.</param>
    /// <param name="lowerBound">The smallest multiplier to consider.</param>
    /// <param name="upperBound">The largest.</param>
    /// <param name="regularizer">The operator whose output is penalized.</param>
    /// <returns>The deblurred picture and the multiplier that was chosen.</returns>
    /// <remarks>
    /// Two things are traded against each other: how well the answer, re-blurred, reproduces the
    /// picture, and how rough the answer is. The multiplier that balances them is not guessed — it is
    /// solved for, because the roughness that a given multiplier buys corresponds to exactly one
    /// residual, and that relation is monotone. Bisecting it in the log gives the multiplier at which
    /// the residual equals the noise the caller says is there: fit the data, and no further.
    /// </remarks>
    public static (double[,] Image, double Lagrange) Regularized(
        double[,] image, double[,] psf, double noisePower,
        double lowerBound, double upperBound, double[,] regularizer)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(psf);
        ArgumentNullException.ThrowIfNull(regularizer);
        if (lowerBound <= 0 || upperBound < lowerBound)
        {
            throw new ArgumentException(
                "the multiplier range must be positive and increasing.", nameof(lowerBound));
        }

        int height = image.GetLength(0);
        int width = image.GetLength(1);
        int count = height * width;

        Complex[] transfer = FilterDesign.PsfToOtf(psf, height, width);
        Complex[] penalty = FilterDesign.PsfToOtf(regularizer, height, width);
        Complex[] spectrum = Spectrum(image);

        var blurPower = new double[count];
        var penaltyPower = new double[count];
        var signalPower = new double[count];
        for (int i = 0; i < count; i++)
        {
            blurPower[i] = transfer[i].Magnitude * transfer[i].Magnitude;
            penaltyPower[i] = penalty[i].Magnitude * penalty[i].Magnitude;
            signalPower[i] = spectrum[i].Magnitude * spectrum[i].Magnitude;
        }

        // What is left over when the answer for a given multiplier is re-blurred: at each frequency
        // the data is scaled down by the regularizer's share, and Parseval turns the sum of those
        // squares into the residual in the picture itself.
        double Residual(double lambda)
        {
            double total = 0;
            for (int i = 0; i < count; i++)
            {
                double denominator = blurPower[i] + (lambda * penaltyPower[i]);
                if (denominator <= 0)
                {
                    continue;
                }

                double share = lambda * penaltyPower[i] / denominator;
                total += signalPower[i] * share * share;
            }

            return total / count;
        }

        double target = noisePower * count;
        double chosen;
        if (target <= 0 || Residual(lowerBound) >= target)
        {
            chosen = lowerBound;
        }
        else if (Residual(upperBound) <= target)
        {
            chosen = upperBound;
        }
        else
        {
            double low = Math.Log(lowerBound);
            double high = Math.Log(upperBound);
            for (int step = 0; step < 60; step++)
            {
                double middle = 0.5 * (low + high);
                if (Residual(Math.Exp(middle)) < target)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            chosen = Math.Exp(0.5 * (low + high));
        }

        for (int i = 0; i < count; i++)
        {
            double denominator = blurPower[i] + (chosen * penaltyPower[i]);
            spectrum[i] = denominator <= 0
                ? Complex.Zero
                : Complex.Conjugate(transfer[i]) * spectrum[i] / denominator;
        }

        return (Picture(spectrum, height, width), chosen);
    }

    /// <summary>
    /// Richardson–Lucy deconvolution: repeated multiplicative correction, accelerated.
    /// </summary>
    /// <param name="image">The blurred picture.</param>
    /// <param name="psf">The blur's point spread function.</param>
    /// <param name="iterations">How many corrections to apply.</param>
    /// <param name="damping">
    /// The residual, in the same units as the picture, below which a correction is treated as noise
    /// and suppressed. Zero applies every correction in full.
    /// </param>
    /// <param name="weight">How much each pixel is to be believed, or null to believe them all.</param>
    /// <param name="readout">A background level added to both the picture and the estimate.</param>
    /// <returns>The deblurred picture.</returns>
    /// <remarks>
    /// Each step asks what the current estimate would look like blurred, compares that to the picture
    /// as a ratio, and pushes the estimate by that ratio smeared back through the blur. Because it
    /// only ever multiplies, an estimate that starts positive stays positive and the total brightness
    /// is preserved — which is why this is the method for pictures made of counted photons.
    /// <para>
    /// The catch is that it sharpens noise as readily as detail, and keeps going. Damping is the
    /// brake: where the estimate already agrees with the picture to within the noise, the correction
    /// is faded out, so smooth regions stop moving while edges carry on.
    /// </para>
    /// </remarks>
    public static double[,] Lucy(
        double[,] image, double[,] psf, int iterations, double damping, double[,]? weight, double readout)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(psf);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        int height = image.GetLength(0);
        int width = image.GetLength(1);
        Complex[] transfer = FilterDesign.PsfToOtf(Normalized(psf), height, width);

        double[] observed = Flatten(image);
        double[] trust = WeightOf(weight, height, width);
        double[] normalizer = Correlate(trust, transfer, height, width);

        double[] estimate = (double[])observed.Clone();
        var extrapolator = new Extrapolator();

        for (int k = 0; k < iterations; k++)
        {
            double[] start = extrapolator.Start(estimate);
            double[] stepped = LucyStep(
                start, observed, trust, normalizer, transfer, height, width, damping, readout);
            extrapolator.Record(estimate, start, stepped);
            estimate = stepped;
        }

        return Reshape(estimate, height, width);
    }

    /// <summary>
    /// Blind deconvolution: Richardson–Lucy run on the picture and the blur at once, each step
    /// improving one while holding the other.
    /// </summary>
    /// <param name="image">The blurred picture.</param>
    /// <param name="psf">A first guess at the spread function, which also fixes its size.</param>
    /// <param name="iterations">How many rounds.</param>
    /// <param name="damping">As for <see cref="Lucy"/>.</param>
    /// <param name="weight">As for <see cref="Lucy"/>.</param>
    /// <param name="readout">As for <see cref="Lucy"/>.</param>
    /// <returns>The deblurred picture and the spread function found.</returns>
    /// <remarks>
    /// Knowing neither the picture nor the blur sounds hopeless, and taken as one problem it is: any
    /// picture explains any data if the blur is allowed to be anything. What makes it tractable is the
    /// constraints — the blur is small, non-negative and sums to one; the picture is non-negative —
    /// and that each half of the problem is easy once the other is fixed. The initial guess is doing
    /// real work here: its size is the largest blur that can be found, and a guess that is too big
    /// invites the method to explain the picture with a blur instead of with detail.
    /// </remarks>
    public static (double[,] Image, double[,] Psf) Blind(
        double[,] image, double[,] psf, int iterations, double damping, double[,]? weight, double readout)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(psf);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        int height = image.GetLength(0);
        int width = image.GetLength(1);
        int psfRows = psf.GetLength(0);
        int psfCols = psf.GetLength(1);

        double[] observed = Flatten(image);
        double[] trust = WeightOf(weight, height, width);
        double[] estimate = (double[])observed.Clone();
        double[] taps = UnitSum(Flatten(Normalized(psf)));

        var pictureSteps = new Extrapolator();
        var blurSteps = new Extrapolator();
        int shiftRow = psfRows / 2;
        int shiftCol = psfCols / 2;

        for (int k = 0; k < iterations; k++)
        {
            double[] pictureStart = pictureSteps.Start(estimate);
            double[] blurStart = UnitSum(blurSteps.Start(taps));

            Complex[] transfer = FilterDesign.PsfToOtf(Reshape(blurStart, psfRows, psfCols), height, width);
            double[] ratio = LucyRatio(
                pictureStart, observed, trust, transfer, height, width, damping, readout);

            // The spread function's turn: the same ratio, smeared back through the current estimate of
            // the picture instead of through the blur. Convolution does not care which of its two
            // arguments is called the kernel, so this is the same step with the roles swapped, and one
            // ratio serves both.
            double[] correction = Correlate(
                ratio, Transform(pictureStart, height, width, inverse: false), height, width);

            var blurStepped = new double[blurStart.Length];
            for (int r = 0; r < psfRows; r++)
            {
                int row = (((r - shiftRow) % height) + height) % height;
                for (int c = 0; c < psfCols; c++)
                {
                    int column = (((c - shiftCol) % width) + width) % width;
                    blurStepped[(r * psfCols) + c] = Math.Max(
                        0, blurStart[(r * psfCols) + c] * correction[(row * width) + column]);
                }
            }

            blurStepped = UnitSum(blurStepped);

            // And the picture's turn, against the blur this round started from.
            double[] normalizer = Correlate(trust, transfer, height, width);
            double[] pictureStepped = new double[estimate.Length];
            double[] pictureCorrection = Correlate(ratio, transfer, height, width);
            for (int i = 0; i < pictureStepped.Length; i++)
            {
                pictureStepped[i] = normalizer[i] > 1e-12
                    ? Math.Max(0, pictureStart[i] * pictureCorrection[i] / normalizer[i])
                    : pictureStart[i];
            }

            pictureSteps.Record(estimate, pictureStart, pictureStepped);
            blurSteps.Record(taps, blurStart, blurStepped);
            estimate = pictureStepped;
            taps = blurStepped;
        }

        return (Reshape(estimate, height, width), Reshape(taps, psfRows, psfCols));
    }

    /// <summary>One multiplicative correction of the picture estimate.</summary>
    private static double[] LucyStep(
        double[] estimate, double[] observed, double[] trust, double[] normalizer,
        Complex[] transfer, int height, int width, double damping, double readout)
    {
        double[] ratio = LucyRatio(estimate, observed, trust, transfer, height, width, damping, readout);
        double[] correction = Correlate(ratio, transfer, height, width);

        var stepped = new double[estimate.Length];
        for (int i = 0; i < stepped.Length; i++)
        {
            stepped[i] = normalizer[i] > 1e-12
                ? Math.Max(0, estimate[i] * correction[i] / normalizer[i])
                : estimate[i];
        }

        return stepped;
    }

    /// <summary>
    /// How far off the current estimate is, as the ratio a multiplicative correction is built from.
    /// </summary>
    private static double[] LucyRatio(
        double[] estimate, double[] observed, double[] trust,
        Complex[] transfer, int height, int width, double damping, double readout)
    {
        double[] blurred = Convolve(estimate, transfer, height, width);
        var ratio = new double[estimate.Length];
        double threshold = damping > 0 ? 2.0 / (damping * damping) : 0;

        for (int i = 0; i < ratio.Length; i++)
        {
            double measured = observed[i] + readout;
            double predicted = blurred[i] + readout;
            if (predicted <= 1e-12)
            {
                ratio[i] = 0;
                continue;
            }

            double raw = measured / predicted;
            if (damping > 0)
            {
                // How much worse the fit is here than a perfect one, measured as a likelihood and
                // scaled so that one means "as far off as the damping threshold allows". Raising it to
                // a high power makes the brake almost off inside the threshold and almost fully on
                // outside it, rather than fading across the whole range.
                double excess = predicted - measured;
                if (measured > 0)
                {
                    excess += measured * Math.Log(measured / predicted);
                }

                double scaled = Math.Min(Math.Max(threshold * excess, 0), 1);
                double brake = Math.Pow(scaled, 10);
                raw = 1 + (brake * (raw - 1));
            }

            ratio[i] = trust[i] * raw;
        }

        return ratio;
    }

    /// <summary>Circular convolution by a transfer function.</summary>
    private static double[] Convolve(double[] values, Complex[] transfer, int height, int width)
    {
        Complex[] spectrum = Transform(values, height, width, inverse: false);
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] *= transfer[i];
        }

        return RealPart(spectrum, height, width);
    }

    /// <summary>Circular correlation by a transfer function — convolution by its mirror image.</summary>
    private static double[] Correlate(double[] values, Complex[] transfer, int height, int width)
    {
        Complex[] spectrum = Transform(values, height, width, inverse: false);
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] *= Complex.Conjugate(transfer[i]);
        }

        return RealPart(spectrum, height, width);
    }

    private static Complex[] Transform(double[] values, int height, int width, bool inverse)
    {
        var grid = new Complex[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            grid[i] = values[i];
        }

        FourierGrid.Transform(grid, height, width, inverse);
        return grid;
    }

    private static double[] RealPart(Complex[] spectrum, int height, int width)
    {
        FourierGrid.Transform(spectrum, height, width, inverse: true);
        var values = new double[spectrum.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = spectrum[i].Real;
        }

        return values;
    }

    private static Complex[] Spectrum(double[,] image)
    {
        int height = image.GetLength(0);
        int width = image.GetLength(1);
        var grid = new Complex[height * width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                grid[(r * width) + c] = image[r, c];
            }
        }

        FourierGrid.Transform(grid, height, width, inverse: false);
        return grid;
    }

    private static double[,] Picture(Complex[] spectrum, int height, int width)
    {
        FourierGrid.Transform(spectrum, height, width, inverse: true);
        var picture = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                picture[r, c] = spectrum[(r * width) + c].Real;
            }
        }

        return picture;
    }

    private static double[] Flatten(double[,] values)
    {
        int height = values.GetLength(0);
        int width = values.GetLength(1);
        var flat = new double[height * width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                flat[(r * width) + c] = values[r, c];
            }
        }

        return flat;
    }

    private static double[,] Reshape(double[] flat, int height, int width)
    {
        var values = new double[height, width];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                values[r, c] = flat[(r * width) + c];
            }
        }

        return values;
    }

    private static double[] WeightOf(double[,]? weight, int height, int width)
    {
        if (weight is null)
        {
            var all = new double[height * width];
            Array.Fill(all, 1.0);
            return all;
        }

        if (weight.GetLength(0) != height || weight.GetLength(1) != width)
        {
            throw new ArgumentException("the weights are not the same size as the picture.", nameof(weight));
        }

        return Flatten(weight);
    }

    /// <summary>The same scaling over a flattened spread function, with negatives clipped away first.</summary>
    private static double[] UnitSum(double[] taps)
    {
        double total = 0;
        for (int i = 0; i < taps.Length; i++)
        {
            taps[i] = Math.Max(0, taps[i]);
            total += taps[i];
        }

        if (total is 0 or 1)
        {
            return taps;
        }

        for (int i = 0; i < taps.Length; i++)
        {
            taps[i] /= total;
        }

        return taps;
    }

    /// <summary>
    /// Biggs and Andrews' vector extrapolation, which is what makes Richardson–Lucy affordable.
    /// </summary>
    /// <remarks>
    /// Left alone the iteration crawls: each correction is a small multiplicative nudge, and hundreds
    /// of them are needed before an edge looks sharp. But the nudges are not random — successive ones
    /// point much the same way — so the sequence can be read as a direction and stepped along further
    /// than one correction would go. How much further is decided by how well the last two corrections
    /// agreed, which costs two dot products and buys roughly an order of magnitude in iterations. It
    /// changes only how fast the same answer is reached, not what the answer is.
    /// </remarks>
    private sealed class Extrapolator
    {
        private double[]? _previous;
        private double[]? _lastStep;
        private double[]? _stepBefore;

        /// <summary>Where the next correction should start from, given where the last two went.</summary>
        public double[] Start(double[] current)
        {
            if (_previous is null || _lastStep is null || _stepBefore is null)
            {
                return (double[])current.Clone();
            }

            double agreement = 0;
            double magnitude = 0;
            for (int i = 0; i < _lastStep.Length; i++)
            {
                agreement += _lastStep[i] * _stepBefore[i];
                magnitude += _stepBefore[i] * _stepBefore[i];
            }

            double alpha = magnitude > 0 ? Math.Clamp(agreement / magnitude, 0, 1) : 0;
            var start = new double[current.Length];
            for (int i = 0; i < start.Length; i++)
            {
                start[i] = Math.Max(0, current[i] + (alpha * (current[i] - _previous[i])));
            }

            return start;
        }

        /// <summary>Records what one correction did, so the next one knows which way it was going.</summary>
        public void Record(double[] current, double[] start, double[] stepped)
        {
            var step = new double[stepped.Length];
            for (int i = 0; i < step.Length; i++)
            {
                step[i] = stepped[i] - start[i];
            }

            _previous = current;
            _stepBefore = _lastStep;
            _lastStep = step;
        }
    }

    /// <summary>
    /// The spread function scaled to sum to one, because a blur that changes a picture's total
    /// brightness is not a blur.
    /// </summary>
    private static double[,] Normalized(double[,] psf)
    {
        double total = 0;
        foreach (double value in psf)
        {
            total += value;
        }

        if (total is 0 or 1)
        {
            return psf;
        }

        int rows = psf.GetLength(0);
        int cols = psf.GetLength(1);
        var scaled = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                scaled[r, c] = psf[r, c] / total;
            }
        }

        return scaled;
    }
}
