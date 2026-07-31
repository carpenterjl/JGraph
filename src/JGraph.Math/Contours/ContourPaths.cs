using JGraph.Core.Primitives;

namespace JGraph.Maths.Contours;

/// <summary>
/// Joins the loose two-point segments <see cref="MarchingSquares.Lines"/> produces into continuous
/// polylines.
/// </summary>
/// <remarks>
/// Marching squares works one cell at a time, so a single iso-line comes back as a scatter of
/// unordered segments. Anything that wants the line as a line — a contour matrix, a path to stroke,
/// an arc length — has to put them back in order first. Adjacent cells compute the crossing on
/// their shared edge from the same two corner values, so the endpoints coincide to the last bit and
/// matching them is a lookup rather than a search.
/// </remarks>
public static class ContourPaths
{
    /// <summary>
    /// Chains <paramref name="segments"/> end to end. <paramref name="tolerance"/> is the distance
    /// below which two endpoints count as the same point; it should be small against the grid
    /// spacing, since the endpoints being matched are meant to be identical.
    /// </summary>
    public static IReadOnlyList<Point2D[]> Assemble(IReadOnlyList<Point2D[]> segments, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (tolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance must be positive.");
        }

        var used = new bool[segments.Count];
        var atPoint = new Dictionary<(long X, long Y), List<int>>();

        (long, long) Key(Point2D p) => ((long)Math.Round(p.X / tolerance), (long)Math.Round(p.Y / tolerance));

        for (int i = 0; i < segments.Count; i++)
        {
            // A sample sitting exactly on the level makes the cells around it interpolate both of
            // their crossings to that one grid vertex, so marching squares emits a segment from a
            // point to itself. It carries no geometry, it cannot be chained to anything, and left
            // in it comes back as a stray one-point path among the real curves.
            if (segments[i].Length < 2 || (segments[i].Length == 2 && Key(segments[i][0]) == Key(segments[i][1])))
            {
                used[i] = true;
                continue;
            }

            foreach (Point2D end in new[] { segments[i][0], segments[i][^1] })
            {
                (long, long) key = Key(end);
                if (!atPoint.TryGetValue(key, out List<int>? list))
                {
                    atPoint[key] = list = [];
                }

                list.Add(i);
            }
        }

        var paths = new List<Point2D[]>();

        for (int i = 0; i < segments.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            used[i] = true;
            var path = new List<Point2D>(segments[i]);

            // Grow from the tail, then flip and grow from what was the head. Doing it in two passes
            // means an open line is found whole no matter which of its segments came first.
            for (int pass = 0; pass < 2; pass++)
            {
                while (true)
                {
                    Point2D tip = path[^1];
                    int next = -1;
                    if (atPoint.TryGetValue(Key(tip), out List<int>? candidates))
                    {
                        foreach (int candidate in candidates)
                        {
                            if (!used[candidate])
                            {
                                next = candidate;
                                break;
                            }
                        }
                    }

                    if (next < 0)
                    {
                        break;
                    }

                    used[next] = true;
                    Point2D[] segment = segments[next];
                    bool forward = Key(segment[0]) == Key(tip);
                    if (forward)
                    {
                        for (int k = 1; k < segment.Length; k++)
                        {
                            path.Add(segment[k]);
                        }
                    }
                    else
                    {
                        for (int k = segment.Length - 2; k >= 0; k--)
                        {
                            path.Add(segment[k]);
                        }
                    }
                }

                path.Reverse();
            }

            paths.Add([.. path]);
        }

        return paths;
    }
}
