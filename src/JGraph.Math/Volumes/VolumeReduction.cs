namespace JGraph.Maths.Volumes;

/// <summary>
/// The three ways a field is made smaller or smoother before it is drawn: cutting a box out of it,
/// keeping every n-th reading, and averaging each reading with its neighbours.
/// </summary>
public static class VolumeReduction
{
    /// <summary>
    /// The part of a field inside a box, as a field of its own. A limit that is not a number leaves
    /// that side where it was, which is how <c>subvolume</c> reads a NaN.
    /// </summary>
    public static ScalarField Subvolume(ScalarField field, double[] box)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(box);
        if (box.Length != 6)
        {
            throw new ArgumentException(
                "A box is [xmin xmax ymin ymax zmin zmax].", nameof(box));
        }

        int[] columns = Inside(field.X, box[0], box[1]);
        int[] rows = Inside(field.Y, box[2], box[3]);
        int[] pages = Inside(field.Z, box[4], box[5]);
        if (columns.Length == 0 || rows.Length == 0 || pages.Length == 0)
        {
            throw new ArgumentException(
                "The box leaves no readings at all — it lies outside the grid.", nameof(box));
        }

        return Gather(field, rows, columns, pages);
    }

    /// <summary>
    /// Every n-th reading along each direction, always keeping the first and the last so the field
    /// still spans what it spanned. A factor of one along a direction leaves it alone.
    /// </summary>
    public static ScalarField Reduce(ScalarField field, int alongX, int alongY, int alongZ)
    {
        ArgumentNullException.ThrowIfNull(field);
        int[] columns = EveryNth(field.Columns, alongX);
        int[] rows = EveryNth(field.Rows, alongY);
        int[] pages = EveryNth(field.Pages, alongZ);
        return Gather(field, rows, columns, pages);
    }

    /// <summary>
    /// Each reading replaced by a weighted average of the block around it, over the same grid.
    /// </summary>
    /// <param name="field">The field to smooth.</param>
    /// <param name="sizes">The block size along x, y and z; each is made odd by rounding up.</param>
    /// <param name="gaussian">
    /// Whether the block is weighted by a bell curve rather than evenly. A box average is the default
    /// because it is what <c>smooth3</c> defaults to.
    /// </param>
    /// <param name="deviation">The bell curve's width, when one is used.</param>
    /// <remarks>
    /// A reading with no value stays without one, and it does not spread: neighbours average over
    /// what they can see, so a single gap does not eat a whole block of the field.
    /// </remarks>
    public static ScalarField Smooth(
        ScalarField field, int[] sizes, bool gaussian = false, double deviation = 0.65)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(sizes);
        if (sizes.Length != 3)
        {
            throw new ArgumentException("A block size is three numbers.", nameof(sizes));
        }

        int halfX = Half(sizes[0]);
        int halfY = Half(sizes[1]);
        int halfZ = Half(sizes[2]);

        double[,,] source = field.Values;
        var smoothed = new double[field.Rows, field.Columns, field.Pages];

        for (int r = 0; r < field.Rows; r++)
        {
            for (int c = 0; c < field.Columns; c++)
            {
                for (int p = 0; p < field.Pages; p++)
                {
                    if (!double.IsFinite(source[r, c, p]))
                    {
                        smoothed[r, c, p] = source[r, c, p];
                        continue;
                    }

                    double total = 0;
                    double weightSum = 0;
                    for (int dr = -halfY; dr <= halfY; dr++)
                    {
                        int rr = r + dr;
                        if (rr < 0 || rr >= field.Rows)
                        {
                            continue;
                        }

                        for (int dc = -halfX; dc <= halfX; dc++)
                        {
                            int cc = c + dc;
                            if (cc < 0 || cc >= field.Columns)
                            {
                                continue;
                            }

                            for (int dp = -halfZ; dp <= halfZ; dp++)
                            {
                                int pp = p + dp;
                                if (pp < 0 || pp >= field.Pages)
                                {
                                    continue;
                                }

                                double sample = source[rr, cc, pp];
                                if (!double.IsFinite(sample))
                                {
                                    continue;
                                }

                                double weight = gaussian
                                    ? Bell(dr, deviation) * Bell(dc, deviation) * Bell(dp, deviation)
                                    : 1;
                                total += sample * weight;
                                weightSum += weight;
                            }
                        }
                    }

                    smoothed[r, c, p] = weightSum > 0 ? total / weightSum : source[r, c, p];
                }
            }
        }

        return field.Like(smoothed);
    }

    /// <summary>The smallest box that holds the whole grid, as [xmin xmax ymin ymax zmin zmax].</summary>
    public static double[] Bounds(ScalarField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return
        [
            field.X.Min(), field.X.Max(),
            field.Y.Min(), field.Y.Max(),
            field.Z.Min(), field.Z.Max(),
        ];
    }

    private static ScalarField Gather(ScalarField field, int[] rows, int[] columns, int[] pages)
    {
        var values = new double[rows.Length, columns.Length, pages.Length];
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < columns.Length; c++)
            {
                for (int p = 0; p < pages.Length; p++)
                {
                    values[r, c, p] = field.Values[rows[r], columns[c], pages[p]];
                }
            }
        }

        return new ScalarField(
            [.. columns.Select(i => field.X[i])],
            [.. rows.Select(i => field.Y[i])],
            [.. pages.Select(i => field.Z[i])],
            values);
    }

    private static int[] Inside(double[] positions, double low, double high)
    {
        double lower = double.IsNaN(low) ? double.NegativeInfinity : low;
        double upper = double.IsNaN(high) ? double.PositiveInfinity : high;
        var kept = new List<int>(positions.Length);
        for (int i = 0; i < positions.Length; i++)
        {
            if (positions[i] >= lower && positions[i] <= upper)
            {
                kept.Add(i);
            }
        }

        return [.. kept];
    }

    private static int[] EveryNth(int count, int factor)
    {
        int step = System.Math.Max(1, factor);
        var kept = new List<int>();
        for (int i = 0; i < count; i += step)
        {
            kept.Add(i);
        }

        if (count > 0 && kept[^1] != count - 1)
        {
            kept.Add(count - 1);
        }

        return [.. kept];
    }

    private static int Half(int size) => System.Math.Max(0, (System.Math.Max(1, size) - 1) / 2);

    private static double Bell(int offset, double deviation)
    {
        double spread = deviation <= 0 ? 0.65 : deviation;
        return System.Math.Exp(-(offset * offset) / (2 * spread * spread));
    }
}
