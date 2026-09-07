namespace JGraph.Signal;

/// <summary>
/// Cutting a stream into frames and putting it back: <c>buffer</c>, <c>framesig</c>,
/// <c>datawrap</c> and <c>seqperiod</c> (M132).
/// </summary>
/// <remarks>
/// <para>
/// Framing looks like reshaping and is not, because a stream does not divide evenly and because
/// consecutive frames usually share samples. Three decisions have to be made and MATLAB's two
/// framing functions make them differently, which is why both are written out here rather than one
/// in terms of the other.
/// </para>
/// <para>
/// The first is what happens to the samples at the end that do not fill a frame: <c>buffer</c> pads
/// them with zeros when it has one output and hands them back untouched when it has two, which is
/// what lets it be called in a loop over a stream. The second is where the first frame starts:
/// with overlap, <c>buffer</c> prepends a frame's worth of zeros unless told <c>'nodelay'</c>, so
/// that the first real sample appears at the same position in its frame as every other sample does
/// in its own. The third is what is carried into the next call, which is the last few samples plus
/// however far into the skipped region the stream ran out.
/// </para>
/// </remarks>
public static class SignalFraming
{
    /// <summary>What one call to <c>buffer</c> produced.</summary>
    /// <param name="Frames">The frames, column-major, one frame per column.</param>
    /// <param name="Columns">How many frames there are.</param>
    /// <param name="Leftover">The samples that did not fill a frame; empty when the frames were padded.</param>
    /// <param name="Carry">
    /// What the next call should be handed as its <c>opt</c>: the overlapping samples when frames
    /// overlap, and the number of samples still to skip when they underlap.
    /// </param>
    public readonly record struct Buffered(double[] Frames, int Columns, double[] Leftover, double[] Carry);

    /// <summary>Cuts <paramref name="x"/> into frames of <paramref name="n"/> samples.</summary>
    /// <param name="x">The samples to cut.</param>
    /// <param name="n">The frame length.</param>
    /// <param name="p">Overlap when positive, underlap when negative.</param>
    /// <param name="option">
    /// The initial condition: a vector of <paramref name="p"/> samples when frames overlap, a count
    /// of samples to skip when they underlap, and null for neither.
    /// </param>
    /// <param name="nodelay">Start the first frame at the first sample rather than at the delay.</param>
    /// <param name="keepLeftover">Hand the trailing samples back rather than padding them with zeros.</param>
    public static Buffered Buffer(
        ReadOnlySpan<double> x, int n, int p, double[]? option, bool nodelay, bool keepLeftover)
    {
        if (n <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "buffer needs a frame length of at least one.");
        }

        if (p >= n)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "buffer's overlap is shorter than its frame.");
        }

        return p >= 0
            ? Overlapped(x, n, p, option, nodelay, keepLeftover)
            : Underlapped(x, n, p, option, keepLeftover);
    }

    /// <summary>The overlapping and abutting cases, where frames share samples or touch.</summary>
    private static Buffered Overlapped(
        ReadOnlySpan<double> x, int n, int p, double[]? option, bool nodelay, bool keepLeftover)
    {
        int hop = n - p;
        double[] data;
        if (nodelay || p == 0)
        {
            data = x.ToArray();
        }
        else
        {
            // Without 'nodelay' the stream is preceded by an initial condition of p samples, zeros
            // unless the caller carried some in from a previous call.
            data = new double[p + x.Length];
            if (option is not null)
            {
                if (option.Length != p)
                {
                    throw new ArgumentException(
                        $"buffer's initial condition holds {p} sample(s) when the overlap is {p}.", nameof(option));
                }

                option.CopyTo(data, 0);
            }

            x.CopyTo(data.AsSpan(p));
        }

        int newSamples = data.Length - p;
        int columns = keepLeftover
            ? System.Math.Max(0, newSamples / hop)
            : System.Math.Max(0, (int)System.Math.Ceiling((double)newSamples / hop));

        var frames = new double[(long)columns * n <= int.MaxValue ? columns * n : 0];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < n; r++)
            {
                int at = (c * hop) + r;
                frames[(c * n) + r] = at < data.Length ? data[at] : 0.0;
            }
        }

        if (!keepLeftover)
        {
            return new Buffered(frames, columns, [], []);
        }

        int used = p + (columns * hop);
        var leftover = new double[System.Math.Max(0, data.Length - used)];
        Array.Copy(data, used, leftover, 0, leftover.Length);
        var carry = new double[p];
        for (int i = 0; i < p; i++)
        {
            int at = used - p + i;
            carry[i] = at >= 0 && at < data.Length ? data[at] : 0.0;
        }

        return new Buffered(frames, columns, leftover, carry);
    }

    /// <summary>The underlapping case, where samples between frames are thrown away.</summary>
    private static Buffered Underlapped(
        ReadOnlySpan<double> x, int n, int p, double[]? option, bool keepLeftover)
    {
        int hop = n - p;
        int skip = 0;
        if (option is not null)
        {
            if (option.Length != 1)
            {
                throw new ArgumentException(
                    "buffer's initial condition is a single count of samples to skip when frames underlap.",
                    nameof(option));
            }

            skip = (int)option[0];
            if (skip < 0 || skip > -p)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(option), $"buffer skips between 0 and {-p} sample(s) before its first frame.");
            }
        }

        int length = x.Length;
        int columns = 0;
        while (true)
        {
            int start = skip + (columns * hop);
            bool fits = keepLeftover ? start + n <= length : start < length;
            if (!fits)
            {
                break;
            }

            columns++;
        }

        var frames = new double[columns * n];
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < n; r++)
            {
                int at = skip + (c * hop) + r;
                frames[(c * n) + r] = at < length ? x[at] : 0.0;
            }
        }

        if (!keepLeftover)
        {
            return new Buffered(frames, columns, [], []);
        }

        // The next frame would start past the end of what was used, and the samples in between were
        // going to be skipped anyway; what is left over is whatever comes after that point.
        int next = columns == 0 ? skip : skip + ((columns - 1) * hop) + n + -p;
        int from = System.Math.Min(length, next);
        var leftover = new double[length - from];
        for (int i = 0; i < leftover.Length; i++)
        {
            leftover[i] = x[from + i];
        }

        return new Buffered(frames, columns, leftover, [System.Math.Max(0, next - length)]);
    }

    /// <summary>
    /// <c>datawrap</c>: adds the signal to itself every <paramref name="nfft"/> samples, which is
    /// the time-domain operation that aliases a spectrum down to that many bins.
    /// </summary>
    public static double[] Wrap(ReadOnlySpan<double> x, int nfft)
    {
        Buffered framed = Buffer(x, nfft, 0, null, nodelay: false, keepLeftover: false);
        var y = new double[nfft];
        for (int c = 0; c < framed.Columns; c++)
        {
            for (int r = 0; r < nfft; r++)
            {
                y[r] += framed.Frames[(c * nfft) + r];
            }
        }

        return y;
    }

    /// <summary>
    /// <c>seqperiod</c>: the shortest prefix of <paramref name="x"/> that repeats through the rest
    /// of it, to within <paramref name="tolerance"/>.
    /// </summary>
    public static int Period(ReadOnlySpan<double> x, double tolerance)
    {
        int n = x.Length;
        for (int p = 1; p < n; p++)
        {
            bool repeats = true;
            for (int i = 0; i + p < n; i++)
            {
                if (System.Math.Abs(x[i + p] - x[i]) > tolerance)
                {
                    repeats = false;
                    break;
                }
            }

            if (repeats)
            {
                return p;
            }
        }

        return n;
    }

    /// <summary>What one call to <c>framesig</c> produced.</summary>
    /// <param name="Frames">The frames, column-major within each channel, one frame per column.</param>
    /// <param name="FrameCount">How many frames each channel produced.</param>
    /// <param name="FinalCondition">The samples to carry into the next call, one column per channel.</param>
    /// <param name="FinalConditionLength">How many samples that carry holds.</param>
    /// <param name="FinalIndex">The initial index the next call should be given.</param>
    public readonly record struct Framed(
        double[] Frames, int FrameCount, double[] FinalCondition, int FinalConditionLength, int FinalIndex);

    /// <summary>
    /// <c>framesig</c>: the modern framing verb, which counts overlap the other way round from
    /// <c>buffer</c> and carries a window with it.
    /// </summary>
    /// <param name="x">The samples, column-major, <paramref name="length"/> rows by channels.</param>
    /// <param name="length">How many samples each channel holds.</param>
    /// <param name="channels">How many channels there are.</param>
    /// <param name="frameLength">The frame length.</param>
    /// <param name="lap">Overlap when positive, underlap when negative.</param>
    /// <param name="initialCondition">Samples to place before the stream, column-major, or null.</param>
    /// <param name="initialConditionLength">How many rows that initial condition has.</param>
    /// <param name="initialIndex">The one-based sample to start at.</param>
    /// <param name="window">A taper of <paramref name="frameLength"/> samples, or null.</param>
    /// <param name="zeroPad">Keep the last incomplete frame and pad it, rather than dropping it.</param>
    public static Framed Frame(
        ReadOnlySpan<double> x, int length, int channels, int frameLength, int lap,
        double[]? initialCondition, int initialConditionLength, int initialIndex,
        double[]? window, bool zeroPad)
    {
        if (frameLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameLength), "framesig needs a frame length of at least one.");
        }

        if (lap >= frameLength)
        {
            throw new ArgumentOutOfRangeException(nameof(lap), "framesig's overlap is shorter than its frame.");
        }

        int total = length + initialConditionLength;
        if (frameLength > total)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameLength), $"framesig's frame is longer than the {total} sample(s) it has.");
        }

        int startIndex = initialIndex - 1;
        var stream = new double[total * channels];
        for (int c = 0; c < channels; c++)
        {
            for (int i = 0; i < initialConditionLength; i++)
            {
                stream[(c * total) + i] = initialCondition is null ? 0 : initialCondition[(c * initialConditionLength) + i];
            }

            for (int i = 0; i < length; i++)
            {
                stream[(c * total) + initialConditionLength + i] = x[(c * length) + i];
            }
        }

        int available = System.Math.Max(0, total - startIndex);
        int hop = frameLength - lap;
        int frames;
        int remainder;
        int finalIndex = 1;
        if (lap >= 0)
        {
            int usable = System.Math.Max(0, available - lap);
            frames = usable / hop;
            remainder = usable - (hop * frames);
        }
        else
        {
            frames = available / hop;
            remainder = available - (hop * frames);
            if (remainder >= frameLength)
            {
                frames++;
                remainder -= frameLength;
                if (remainder < -lap)
                {
                    if (!zeroPad)
                    {
                        finalIndex = -lap - remainder + 1;
                    }

                    remainder = 0;
                }
            }
        }

        int extra = remainder > 0 && zeroPad ? 1 : 0;
        frames += extra;

        var framed = new double[(long)frameLength * frames * channels <= int.MaxValue
            ? frameLength * frames * channels
            : 0];
        for (int c = 0; c < channels; c++)
        {
            for (int f = 0; f < frames; f++)
            {
                for (int r = 0; r < frameLength; r++)
                {
                    int at = (f * hop) + r;
                    double sample = at < available ? stream[(c * total) + startIndex + at] : 0.0;
                    framed[(((c * frames) + f) * frameLength) + r] =
                        window is null ? sample : sample * window[r];
                }
            }
        }

        int overlapKept = System.Math.Max(0, frameLength - hop);
        int carryLength = 0;
        double[] carry = [];
        if (!zeroPad && (lap > 0 || remainder > 0))
        {
            carryLength = overlapKept + remainder;
            carry = new double[carryLength * channels];
            int lastFrame = System.Math.Max(0, frames - extra - 1);
            for (int c = 0; c < channels; c++)
            {
                for (int i = 0; i < overlapKept; i++)
                {
                    int at = (lastFrame * hop) + hop + i;
                    carry[(c * carryLength) + i] = at < available
                        ? stream[(c * total) + startIndex + at]
                        : 0.0;
                }

                for (int i = 0; i < remainder; i++)
                {
                    int at = available - remainder + i;
                    carry[(c * carryLength) + overlapKept + i] = stream[(c * total) + startIndex + at];
                }
            }
        }

        return new Framed(framed, frames, carry, carryLength, finalIndex);
    }
}
