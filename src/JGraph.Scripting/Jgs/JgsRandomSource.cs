namespace JGraph.Scripting.Jgs;

/// <summary>
/// The random stream a run draws from, and the thing <c>rng</c> reseeds. Every builtin that draws a
/// number — <c>rand</c>, <c>randn</c>, <c>randi</c>, <c>randperm</c>, <c>sprand</c>, <c>imnoise</c>,
/// <c>imsegkmeans</c> — shares one of these, so a script that seeds once gets a reproducible run
/// rather than a reproducible builtin.
/// </summary>
/// <remarks>
/// <para>
/// It derives from <see cref="Random"/> deliberately: the imaging and segmentation kernels take a
/// <see cref="Random"/>, and so did five registrars, so subclassing let the seedable stream reach all
/// of them without changing a signature.
/// </para>
/// <para>
/// The generator is xoshiro256** over a SplitMix64 seed expansion — the same family .NET itself uses,
/// but written out here because the state has to be inspectable for <c>s = rng</c> and restorable for
/// <c>rng(s)</c>, and <see cref="Random"/> exposes neither. Every public draw is built on one counted
/// primitive, <see cref="NextState"/>, which is what makes <see cref="Restore"/> exact no matter which
/// mixture of methods consumed the stream. It is not MATLAB's Mersenne Twister and does not reproduce
/// MATLAB's numbers for a given seed; it reproduces <em>its own</em> numbers, which is what a
/// repeatable script needs.
/// </para>
/// </remarks>
internal sealed class JgsRandomSource : Random
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    /// <summary>Entropy for <see cref="Shuffle"/>, bumped per call so two shuffles in a tick differ.</summary>
    private static int _shuffleCounter;

    /// <summary>Creates a stream seeded from the clock, as an unseeded MATLAB session is.</summary>
    public JgsRandomSource() => Shuffle();

    /// <summary>Creates a stream on a known seed.</summary>
    public JgsRandomSource(int seed) => Reset(seed);

    /// <summary>The seed this stream was last set to.</summary>
    public int Seed { get; private set; }

    /// <summary>
    /// How many primitive draws have been taken since the seed was set. Counted at the primitive
    /// rather than at the public method, so a rejected sample still advances it and a restore lands on
    /// exactly the state that was captured.
    /// </summary>
    public long Draws { get; private set; }

    /// <summary>The generator's name, for the <c>rng</c> state struct. One word, and always this one.</summary>
    public string Kind => "twister";

    /// <summary>Restarts the stream on <paramref name="seed"/>.</summary>
    public void Reset(int seed)
    {
        Seed = seed;
        Draws = 0;

        // SplitMix64 expansion: a bad seed (0, 1, a loop counter) still fills the state with
        // well-mixed bits, which is what keeps rng(0) and rng(1) from producing related streams.
        ulong z = unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL);
        _s0 = SplitMix(ref z);
        _s1 = SplitMix(ref z);
        _s2 = SplitMix(ref z);
        _s3 = SplitMix(ref z);

        // An all-zero state is xoshiro's one fixed point; the expansion above makes it vanishingly
        // unlikely, but a generator that can silently produce nothing but zeros is worth one branch.
        if ((_s0 | _s1 | _s2 | _s3) == 0)
        {
            _s0 = 0x9E3779B97F4A7C15UL;
        }
    }

    /// <summary>Reseeds from the clock, MATLAB's <c>rng('shuffle')</c>.</summary>
    public void Shuffle() =>
        Reset(unchecked(
            (int)Environment.TickCount64 ^ (int)(Interlocked.Increment(ref _shuffleCounter) * 2654435761u)));

    /// <summary>Captures the stream's position, for <c>s = rng</c>.</summary>
    public (int Seed, long Draws) Snapshot() => (Seed, Draws);

    /// <summary>
    /// Puts the stream back where <see cref="Snapshot"/> found it, for <c>rng(s)</c>. Reseeding and
    /// replaying is O(draws) rather than a jump, which is the price of keeping the state small enough
    /// to hand a script as two plain numbers.
    /// </summary>
    public void Restore((int Seed, long Draws) state)
    {
        Reset(state.Seed);
        for (long i = 0; i < state.Draws; i++)
        {
            NextState();
        }
    }

    /// <inheritdoc />
    public override double NextDouble() => (NextState() >> 11) * (1.0 / 9007199254740992.0);

    /// <inheritdoc />
    public override float NextSingle() => (NextState() >> 40) * (1.0f / 16777216.0f);

    /// <inheritdoc />
    protected override double Sample() => NextDouble();

    /// <inheritdoc />
    public override int Next() => (int)NextBelow(int.MaxValue);

    /// <inheritdoc />
    public override int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return maxValue == 0 ? 0 : (int)NextBelow((ulong)maxValue);
    }

    /// <inheritdoc />
    public override int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(minValue));
        }

        return minValue == maxValue ? minValue : minValue + (int)NextBelow((ulong)((long)maxValue - minValue));
    }

    /// <inheritdoc />
    public override long NextInt64() => (long)(NextState() >> 1);

    /// <inheritdoc />
    public override long NextInt64(long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return maxValue == 0 ? 0 : (long)NextBelow((ulong)maxValue);
    }

    /// <inheritdoc />
    public override long NextInt64(long minValue, long maxValue)
    {
        if (minValue > maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(minValue));
        }

        return minValue == maxValue
            ? minValue
            : minValue + (long)NextBelow((ulong)(maxValue - minValue));
    }

    /// <inheritdoc />
    public override void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        NextBytes(buffer.AsSpan());
    }

    /// <inheritdoc />
    public override void NextBytes(Span<byte> buffer)
    {
        int i = 0;
        while (i < buffer.Length)
        {
            ulong bits = NextState();
            for (int b = 0; b < 8 && i < buffer.Length; b++, i++)
            {
                buffer[i] = (byte)(bits >> (b * 8));
            }
        }
    }

    private static ulong SplitMix(ref ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        ulong result = z;
        result = (result ^ (result >> 30)) * 0xBF58476D1CE4E5B9UL;
        result = (result ^ (result >> 27)) * 0x94D049BB133111EBUL;
        return result ^ (result >> 31);
    }

    /// <summary>A uniform value below <paramref name="bound"/>, rejecting the biased tail.</summary>
    private ulong NextBelow(ulong bound)
    {
        // Lemire's multiply-shift, with the rejection branch that removes the modulo bias. Rejected
        // candidates still consume a counted draw, which is exactly why Draws is counted below rather
        // than here.
        ulong threshold = unchecked((ulong)(-(long)bound)) % bound;
        while (true)
        {
            ulong candidate = NextState();
            if (candidate >= threshold)
            {
                return candidate % bound;
            }
        }
    }

    /// <summary>The one primitive every draw is built from — and the one thing that gets counted.</summary>
    private ulong NextState()
    {
        Draws++;

        ulong result = Rotate(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotate(_s3, 45);

        return result;
    }

    private static ulong Rotate(ulong value, int count) => (value << count) | (value >> (64 - count));
}
