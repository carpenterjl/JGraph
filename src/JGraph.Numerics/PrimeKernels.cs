using System.Runtime.CompilerServices;

namespace JGraph.Numerics;

/// <summary>
/// A sieve of the odd numbers, cut into pieces that fit the first-level cache (the every-row
/// plan's I5, ADR 0328): <c>primes(n)</c> reads its answer off it, and <c>isprime</c> over an
/// array whose values are dense in their own range looks each one up instead of dividing it by
/// every odd number up to its root.
/// </summary>
public static class PrimeKernels
{
    /// <summary>Odd numbers per piece of the sieve: 32 KB of flags.</summary>
    private const int Piece = 1 << 15;

    /// <summary>
    /// The sieve up to <paramref name="limit"/> (at least 3): element <c>i</c> is nonzero when
    /// <c>2i + 1</c> is composite. Element 0 stands for 1 and is marked.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static byte[] OddComposites(long limit)
    {
        int odd = (int)((limit + 1) / 2); // 1, 3, 5, ... up to limit
        var composite = new byte[odd];
        composite[0] = 1;

        // The primes up to the root, found by the plain sieve over their own short range.
        int root = (int)Math.Sqrt(limit);
        while ((long)root * root > limit)
        {
            root--;
        }

        while ((long)(root + 1) * (root + 1) <= limit)
        {
            root++;
        }

        int small = (root + 1) / 2;
        for (int i = 1; i < small; i++)
        {
            if (composite[i] != 0)
            {
                continue;
            }

            int p = (2 * i) + 1;
            for (long j = ((long)p * p) / 2; j < small; j += p)
            {
                composite[j] = 1;
            }
        }

        int count = 0;
        for (int i = 1; i < small; i++)
        {
            if (composite[i] == 0)
            {
                count++;
            }
        }

        var basePrimes = new int[count];
        var next = new long[count]; // each prime's next multiple, as an index into the odd numbers
        count = 0;
        for (int i = 1; i < small; i++)
        {
            if (composite[i] == 0)
            {
                int p = (2 * i) + 1;
                basePrimes[count] = p;
                long first = ((long)p * p) / 2;
                if (first < small)
                {
                    first += ((small - first + p - 1) / p) * p;
                }

                next[count++] = first;
            }
        }

        for (long low = small; low < odd; low += Piece)
        {
            long high = Math.Min(low + Piece, odd);
            for (int k = 0; k < count; k++)
            {
                long j = next[k];
                if (j >= high)
                {
                    continue;
                }

                int p = basePrimes[k];
                for (; j < high; j += p)
                {
                    composite[j] = 1;
                }

                next[k] = j;
            }
        }

        return composite;
    }

    /// <summary>Every prime up to <paramref name="limit"/>, ascending, as doubles.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static double[] PrimesUpTo(int limit)
    {
        if (limit < 2)
        {
            return [];
        }

        if (limit == 2)
        {
            return [2];
        }

        byte[] composite = OddComposites(limit);
        int count = 1;
        for (int i = 1; i < composite.Length; i++)
        {
            count += composite[i] == 0 ? 1 : 0;
        }

        var primes = new double[count];
        primes[0] = 2;
        int at = 1;
        for (int i = 1; i < composite.Length; i++)
        {
            if (composite[i] == 0)
            {
                primes[at++] = (2.0 * i) + 1;
            }
        }

        return primes;
    }

    /// <summary>
    /// <c>dest[i] = 1</c> where <c>x[i]</c> is a prime no greater than the sieve reaches, 0
    /// elsewhere: a value below 2, a fraction, an even number other than 2 and a composite are all
    /// not prime. The caller has checked that no value exceeds <c>2·composite.Length</c>.
    /// </summary>
    public static void LookUp(NumericBuffer x, byte[] composite, NumericBuffer dest)
    {
        ParallelKernels.For(dest.Length, ParallelKernels.ComputeBoundThreshold, null, (start, len) =>
            LookUpChunk(x.AsSpan(start, len), composite, dest.AsSpan(start, len)));
        GC.KeepAlive(x);
        GC.KeepAlive(dest);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void LookUpChunk(ReadOnlySpan<double> x, byte[] composite, Span<double> d)
    {
        for (int i = 0; i < d.Length; i++)
        {
            double v = x[i];
            bool prime = false;
            if (v >= 2 && v == Math.Floor(v))
            {
                long n = (long)v;
                prime = n == 2 || ((n & 1) == 1 && composite[n >> 1] == 0);
            }

            d[i] = prime ? 1.0 : 0.0;
        }
    }
}
