namespace JGraph.Numerics;

/// <summary>
/// Order statistics without a sort: places the elements a caller names at the ranks it names, and
/// leaves the rest of the array in whatever order the partitioning happened to produce.
/// </summary>
/// <remarks>
/// <para>
/// A median is not a sorted array with a value read out of the middle of it — it is one order
/// statistic, and putting a single element in its place costs a linear pass rather than an n log n
/// one. The same is true of a quartile, and of the pair of neighbouring ranks a percentile
/// interpolates between. Ten million samples were costing 0.70 s each for <c>median</c> and for
/// <c>prctile</c> before this existed, of which the sort was the larger half and three copies of
/// the data were the rest.
/// </para>
/// <para>
/// Several ranks at once are answered by one recursion rather than one per rank, which is what
/// makes <c>prctile(x, [25 75])</c> cost less than two medians: the first partition serves every
/// rank that falls on either side of it, and only the ranks still in play descend into a side.
/// </para>
/// <para>
/// The recursion is bounded the way a library sort's is. A partition that keeps choosing badly
/// would be quadratic, so the depth is budgeted, and a range that exhausts its budget is sorted
/// outright — which satisfies every rank left in it at once and puts a floor of n log n under the
/// worst case that no input can get below.
/// </para>
/// </remarks>
public static class SelectKernels
{
    /// <summary>Ranges this short are sorted rather than partitioned again.</summary>
    private const int SmallRange = 24;

    /// <summary>
    /// Places <paramref name="data"/>'s <paramref name="ranks"/>-th smallest elements at those
    /// indices. <paramref name="ranks"/> is sorted in place and may hold repeats.
    /// </summary>
    /// <remarks>
    /// Afterwards <c>data[r]</c> is the r-th smallest for every requested r, and every element
    /// below index r is no greater than it and every element above no less — which is all a
    /// percentile ever reads, and is the whole of what this promises. Nothing else about the
    /// order is defined.
    /// </remarks>
    public static void PartialSort(Span<double> data, Span<int> ranks)
    {
        if (data.Length == 0 || ranks.Length == 0)
        {
            return;
        }

        ranks.Sort();
        int budget = 2 * (64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)data.Length));
        Select(data, 0, data.Length - 1, ranks, budget);
    }

    /// <summary>The single k-th smallest, for the common case of one rank.</summary>
    public static double NthSmallest(Span<double> data, int rank)
    {
        Span<int> one = [rank];
        PartialSort(data, one);
        return data[rank];
    }

    private static void Select(Span<double> data, int low, int high, Span<int> ranks, int budget)
    {
        while (ranks.Length > 0)
        {
            if (high - low < SmallRange)
            {
                InsertionSort(data, low, high);
                return;
            }

            if (budget-- <= 0)
            {
                // The partitions in this range have gone badly often enough to be worth stopping.
                // A sort answers every rank still in it, so there is nothing left to recur into.
                data[low..(high + 1)].Sort();
                return;
            }

            int split = Partition(data, low, high);

            // Everything at or below `split` is no greater than everything above it, so a rank is
            // settled to one side by its index alone — no comparison against a pivot value, which
            // is what lets a run of equal elements divide evenly instead of piling up on one side.
            int taken = 0;
            while (taken < ranks.Length && ranks[taken] <= split)
            {
                taken++;
            }

            if (taken > 0)
            {
                Select(data, low, split, ranks[..taken], budget);
            }

            if (taken == ranks.Length)
            {
                return;
            }

            // The right-hand ranks are carried by the loop rather than by a second call, so a long
            // run of one-sided partitions costs stack depth nothing.
            ranks = ranks[taken..];
            low = split + 1;
        }
    }

    /// <summary>
    /// Hoare's partition about the median of the first, middle and last elements. Returns an index
    /// j with low ≤ j &lt; high such that no element in [low, j] is greater than any in (j, high].
    /// </summary>
    private static int Partition(Span<double> data, int low, int high)
    {
        int middle = low + ((high - low) / 2);
        if (data[middle] < data[low])
        {
            (data[low], data[middle]) = (data[middle], data[low]);
        }

        if (data[high] < data[low])
        {
            (data[low], data[high]) = (data[high], data[low]);
        }

        if (data[high] < data[middle])
        {
            (data[middle], data[high]) = (data[high], data[middle]);
        }

        // The pivot sits at low, which is what keeps the scan below inside the range without a
        // bounds test of its own: it stops the left scan on its first step at the latest.
        double pivot = data[middle];
        (data[low], data[middle]) = (data[middle], data[low]);

        int i = low - 1;
        int j = high + 1;
        while (true)
        {
            do
            {
                i++;
            }
            while (data[i] < pivot);

            do
            {
                j--;
            }
            while (data[j] > pivot);

            if (i >= j)
            {
                return j;
            }

            (data[i], data[j]) = (data[j], data[i]);
        }
    }

    private static void InsertionSort(Span<double> data, int low, int high)
    {
        for (int i = low + 1; i <= high; i++)
        {
            double held = data[i];
            int j = i - 1;
            while (j >= low && data[j] > held)
            {
                data[j + 1] = data[j];
                j--;
            }

            data[j + 1] = held;
        }
    }
}
