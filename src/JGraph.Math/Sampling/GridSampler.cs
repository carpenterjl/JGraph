namespace JGraph.Maths.Sampling;

/// <summary>
/// The two-parameter half of the function plotters: where to read a function of x and y, and what to
/// do about the places it runs away.
/// </summary>
/// <remarks>
/// <para>
/// There is no refinement here, and that is a decision rather than an omission. A surface in this
/// build is a grid — a rectangle of readings with rows and columns — so there is nowhere to put an
/// extra reading that belongs to one part of the picture and not to the rest of its row. Density is
/// therefore the whole of the control a caller has, which is why the verbs pass it straight through
/// as MATLAB's <c>MeshDensity</c>.
/// </para>
/// <para>
/// The runaway rule is the curve sampler's, applied to a rectangle: a reading further from the middle
/// of the readings than <c>poleFactor</c> spreads is the surface leaving rather than a height on it,
/// and drawing it would flatten the rest of the surface into the floor of a spike.
/// </para>
/// </remarks>
public static class GridSampler
{
    /// <summary><paramref name="count"/> readings evenly spaced from one end to the other.</summary>
    public static double[] EvenlySpaced(double low, double high, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 2);

        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = low + ((high - low) * i / (count - 1));
        }

        values[^1] = high;
        return values;
    }

    /// <summary>
    /// Replaces every reading that has run away — and every one that was never finite — with a gap,
    /// and answers with how many the surface lost that way.
    /// </summary>
    public static int BreakRunaways(double[,] values, double poleFactor)
    {
        ArgumentNullException.ThrowIfNull(values);

        int rows = values.GetLength(0);
        int columns = values.GetLength(1);
        var all = new List<double>(rows * columns);
        foreach (double value in values)
        {
            all.Add(value);
        }

        (double spread, double centre) = ReadingSpread.Of(all);

        int lost = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                double value = values[r, c];
                if (ReadingSpread.RanAway(value, centre, spread, poleFactor))
                {
                    values[r, c] = double.NaN;
                    lost++;
                }
                else if (!double.IsFinite(value))
                {
                    values[r, c] = double.NaN;
                }
            }
        }

        return lost;
    }
}
