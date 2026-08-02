namespace JGraph.Imaging;

/// <summary>
/// Circular Hough detection (MATLAB <c>imfindcircles</c>): finds circles of a given radius range and
/// reports their centres, radii and strengths.
/// </summary>
/// <remarks>
/// <para>
/// The two-stage method. Stage one accumulates centres only: every edge pixel votes along its own
/// gradient direction, at every radius in range, for the point that would be the centre if the pixel
/// were on a circle of that radius. Voting along the gradient rather than round a full circle is what
/// collapses the accumulator from three dimensions to two — a circle's edge points all aim at its
/// centre, so the centre is where the votes pile up regardless of radius.
/// </para>
/// <para>
/// Stage two then asks, for each centre found, which radius best explains the edge pixels around it,
/// by histogramming their distances. That is why the radius is accurate even though the centre
/// accumulator threw the radius away.
/// </para>
/// </remarks>
public static class CircleDetection
{
    /// <summary>Which side of the circle is brighter.</summary>
    public enum Polarity
    {
        /// <summary>A bright disc on a dark background.</summary>
        Bright,

        /// <summary>A dark disc on a bright background.</summary>
        Dark,
    }

    /// <summary>One detected circle, with a 0-based centre.</summary>
    public readonly record struct Circle(double CenterX, double CenterY, double Radius, double Strength);

    /// <summary>
    /// Finds circles with radii in <c>[minRadius, maxRadius]</c>.
    /// </summary>
    /// <param name="image">The picture to search.</param>
    /// <param name="minRadius">Smallest radius to consider, in pixels.</param>
    /// <param name="maxRadius">Largest radius.</param>
    /// <param name="polarity">Whether the discs are bright or dark.</param>
    /// <param name="sensitivity">
    /// How readily a peak counts as a circle, 0 to 1. Higher finds more, including more spurious ones
    /// — it lowers the fraction of the strongest peak a candidate must reach.
    /// </param>
    /// <param name="edgeThreshold">
    /// Gradient magnitude, relative to the largest in the picture, below which a pixel does not vote.
    /// Null picks it from the gradient distribution.
    /// </param>
    public static Circle[] Find(
        ImageBuffer image, double minRadius, double maxRadius,
        Polarity polarity = Polarity.Bright, double sensitivity = 0.85, double? edgeThreshold = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (minRadius <= 0 || maxRadius < minRadius)
        {
            throw new ArgumentOutOfRangeException(nameof(minRadius),
                "the radius range must be positive and increasing.");
        }

        int h = image.Height;
        int w = image.Width;

        using ImageBuffer gray = image.Channels == 1 ? image.Clone() : PointOps.ToGray(image);
        using ImageBuffer smoothed = Filters.GaussianBlur(gray, 1.0, 1.0);

        // Central differences rather than Sobel: the vote direction has to be the true gradient
        // angle, and Sobel's smoothing across the perpendicular biases it on a small circle.
        var gx = new double[h * w];
        var gy = new double[h * w];
        var magnitude = new double[h * w];
        double largest = 0;
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                double right = smoothed[r, Math.Min(w - 1, c + 1), 0];
                double left = smoothed[r, Math.Max(0, c - 1), 0];
                double down = smoothed[Math.Min(h - 1, r + 1), c, 0];
                double up = smoothed[Math.Max(0, r - 1), c, 0];
                int p = (r * w) + c;
                gx[p] = (right - left) / 2.0;
                gy[p] = (down - up) / 2.0;
                magnitude[p] = Math.Sqrt((gx[p] * gx[p]) + (gy[p] * gy[p]));
                largest = Math.Max(largest, magnitude[p]);
            }
        }

        if (largest <= 0)
        {
            return [];
        }

        double cut = (edgeThreshold ?? 0.1) * largest;

        // At the left edge of a bright disc the intensity rises to the right, so the gradient points
        // at the centre: the centre lies along +∇, a radius away. A dark disc is the other way
        // about. Getting this backwards puts every vote a full diameter out on the far side, which
        // is a ring of four phantom centres rather than nothing at all — it looks like a result.
        double sign = polarity == Polarity.Bright ? 1.0 : -1.0;

        var accumulator = new double[h * w];
        int radiusSteps = Math.Max(1, (int)Math.Round(maxRadius - minRadius) + 1);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int p = (r * w) + c;
                if (magnitude[p] < cut)
                {
                    continue;
                }

                double ux = sign * gx[p] / magnitude[p];
                double uy = sign * gy[p] / magnitude[p];
                for (int k = 0; k < radiusSteps; k++)
                {
                    double radius = minRadius + (k * (maxRadius - minRadius) / Math.Max(1, radiusSteps - 1));
                    int cx = (int)Math.Round(c + (ux * radius));
                    int cy = (int)Math.Round(r + (uy * radius));
                    if ((uint)cy < (uint)h && (uint)cx < (uint)w)
                    {
                        accumulator[(cy * w) + cx] += 1.0;
                    }
                }
            }
        }

        // Blur the accumulator so a centre spread over a few cells by rounding counts once.
        using var votes = new ImageBuffer(h, w, 1);
        MorphologicalReconstruction.WritePlane(votes, accumulator, 0);
        using ImageBuffer peaksField = Filters.GaussianBlur(votes, 1.5, 1.5);

        double best = 0;
        for (int i = 0; i < accumulator.Length; i++)
        {
            best = Math.Max(best, peaksField.Pixels[i]);
        }

        GC.KeepAlive(peaksField);
        if (best <= 0)
        {
            return [];
        }

        double level = best * (1.0 - Math.Clamp(sensitivity, 0.0, 1.0));
        int suppression = Math.Max(1, (int)Math.Round(minRadius));

        // Only genuine local maxima are candidates. Taking the global maximum, suppressing a disc
        // around it and repeating sounds equivalent, but it is not: the shoulder of a strong peak
        // outranks a weaker circle's summit, so the list fills with re-detections of the same circle
        // and a real one further away is never reached.
        var candidates = new List<(double Peak, int At)>();
        ReadOnlySpan<double> votes2 = peaksField.Pixels;
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int p = (r * w) + c;
                double here = votes2[p];
                if (here < level || here <= 0)
                {
                    continue;
                }

                bool tallest = true;
                for (int dr = -suppression; dr <= suppression && tallest; dr++)
                {
                    for (int dc = -suppression; dc <= suppression; dc++)
                    {
                        int nr = r + dr;
                        int nc = c + dc;
                        if ((uint)nr >= (uint)h || (uint)nc >= (uint)w)
                        {
                            continue;
                        }

                        if (votes2[(nr * w) + nc] > here)
                        {
                            tallest = false;
                            break;
                        }
                    }
                }

                if (tallest)
                {
                    candidates.Add((here, p));
                }
            }
        }

        GC.KeepAlive(peaksField);
        candidates.Sort((a, b) => b.Peak.CompareTo(a.Peak));

        var found = new List<Circle>();
        var taken = new bool[h * w];
        foreach ((double peak, int at) in candidates)
        {
            if (taken[at])
            {
                continue;
            }

            int cy = at / w;
            int cx = at % w;
            for (int dr = -suppression; dr <= suppression; dr++)
            {
                for (int dc = -suppression; dc <= suppression; dc++)
                {
                    int nr = cy + dr;
                    int nc = cx + dc;
                    if ((uint)nr < (uint)h && (uint)nc < (uint)w)
                    {
                        taken[(nr * w) + nc] = true;
                    }
                }
            }

            double radius = BestRadius(magnitude, cut, h, w, cx, cy, minRadius, maxRadius);
            if (radius > 0)
            {
                found.Add(new Circle(cx, cy, radius, peak / best));
            }
        }

        return [.. found];
    }

    /// <summary>
    /// The radius that best explains the edge pixels around a candidate centre: histogram their
    /// distances and take the fullest bin, refined by the weighted mean of its neighbours.
    /// </summary>
    private static double BestRadius(
        double[] magnitude, double cut, int h, int w, int cx, int cy, double minRadius, double maxRadius)
    {
        int bins = Math.Max(1, (int)Math.Ceiling(maxRadius - minRadius) + 1);
        var histogram = new double[bins];
        int from = (int)Math.Floor(maxRadius) + 2;
        for (int r = Math.Max(0, cy - from); r <= Math.Min(h - 1, cy + from); r++)
        {
            for (int c = Math.Max(0, cx - from); c <= Math.Min(w - 1, cx + from); c++)
            {
                int p = (r * w) + c;
                if (magnitude[p] < cut)
                {
                    continue;
                }

                double distance = Math.Sqrt(((r - (double)cy) * (r - (double)cy)) +
                                            ((c - (double)cx) * (c - (double)cx)));
                if (distance < minRadius - 0.5 || distance > maxRadius + 0.5)
                {
                    continue;
                }

                int bin = Math.Clamp((int)Math.Round(distance - minRadius), 0, bins - 1);
                histogram[bin] += magnitude[p];
            }
        }

        int bestBin = -1;
        double bestValue = 0;
        for (int i = 0; i < bins; i++)
        {
            if (histogram[i] > bestValue)
            {
                bestValue = histogram[i];
                bestBin = i;
            }
        }

        if (bestBin < 0)
        {
            return 0;
        }

        double weighted = 0;
        double total = 0;
        for (int i = Math.Max(0, bestBin - 1); i <= Math.Min(bins - 1, bestBin + 1); i++)
        {
            weighted += (minRadius + i) * histogram[i];
            total += histogram[i];
        }

        return total > 0 ? weighted / total : minRadius + bestBin;
    }
}
