using JGraph.Core.Primitives;

namespace JGraph.Maths.Contours;

/// <summary>
/// The assembled iso-lines of a scalar field at several levels at once, flattened into shared
/// buffers and indexed by level.
/// </summary>
/// <remarks>
/// <para>
/// Marching squares emits a scatter of unconnected two-point segments, and drawing them that way
/// costs one call each and — more to the point — restarts the dash pattern on every one, so a dashed
/// contour comes out as an even row of ticks rather than a dashed curve.
/// <see cref="ContourPaths.Assemble"/> already chains the segments back into curves; what was missing
/// was somewhere to keep the result. Nothing here depends on the camera or the axes, only on the
/// data and the levels, so a set survives every pan, zoom, and rotate of the figure that drew it.
/// </para>
/// </remarks>
public sealed class ContourLineSet
{
    private readonly Point2D[] _points;
    private readonly int[] _pathStarts;
    private readonly int[] _levelStarts;
    private readonly double[] _levels;

    private ContourLineSet(double[] levels, Point2D[] points, int[] pathStarts, int[] levelStarts)
    {
        _levels = levels;
        _points = points;
        _pathStarts = pathStarts;
        _levelStarts = levelStarts;

        for (int level = 0; level < levels.Length; level++)
        {
            int paths = _levelStarts[level + 1] - _levelStarts[level];
            MaxLevelPaths = System.Math.Max(MaxLevelPaths, paths);
            MaxLevelVertices = System.Math.Max(
                MaxLevelVertices,
                _pathStarts[_levelStarts[level + 1]] - _pathStarts[_levelStarts[level]]);
        }
    }

    /// <summary>The levels this set was built for, in the order they were given.</summary>
    public ReadOnlySpan<double> Levels => _levels;

    /// <summary>The largest vertex count any single level accounts for — a caller's buffer size.</summary>
    public int MaxLevelVertices { get; }

    /// <summary>The largest polyline count any single level accounts for.</summary>
    public int MaxLevelPaths { get; }

    /// <summary>
    /// Extracts and assembles the iso-lines of <paramref name="z"/> at every level given. The
    /// endpoint-matching tolerance is scaled off the grid extent, since the endpoints being matched
    /// come from the same interpolation on both sides and are meant to be identical.
    /// </summary>
    public static ContourLineSet Build(double[] x, double[] y, double[,] z, double[] levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        MarchingSquares.Validate(x, y, z);

        double span = System.Math.Max(
            System.Math.Max(System.Math.Abs(x[^1] - x[0]), System.Math.Abs(y[^1] - y[0])),
            1e-12);
        double tolerance = span * 1e-10;

        var points = new List<Point2D>();
        var pathStarts = new List<int>();
        var levelStarts = new int[levels.Length + 1];

        for (int level = 0; level < levels.Length; level++)
        {
            levelStarts[level] = pathStarts.Count;
            foreach (Point2D[] path in ContourPaths.Assemble(MarchingSquares.Lines(x, y, z, levels[level]), tolerance))
            {
                if (path.Length < 2)
                {
                    continue;
                }

                pathStarts.Add(points.Count);
                points.AddRange(path);
            }
        }

        levelStarts[levels.Length] = pathStarts.Count;
        pathStarts.Add(points.Count); // one past the end, so every path has an exclusive bound
        return new ContourLineSet([.. levels], [.. points], [.. pathStarts], levelStarts);
    }

    /// <summary>Whether this set was built for exactly <paramref name="levels"/>.</summary>
    public bool Matches(ReadOnlySpan<double> levels) => levels.SequenceEqual(_levels);

    /// <summary>How many polylines make up <paramref name="level"/>.</summary>
    public int PathCount(int level) => _levelStarts[level + 1] - _levelStarts[level];

    /// <summary>The <paramref name="index"/>th polyline of <paramref name="level"/>, in data space.</summary>
    public ReadOnlySpan<Point2D> Path(int level, int index)
    {
        int path = _levelStarts[level] + index;
        int start = _pathStarts[path];
        return _points.AsSpan(start, _pathStarts[path + 1] - start);
    }
}
