using JGraph.Core.Primitives;
using JGraph.Maths.Geometry;
using Xunit;

namespace JGraph.Tests.Maths;

/// <summary>
/// The Voronoi dual (M57): the diagram is checked by its defining property — every point of a cell
/// is nearer its own site than any other — and by the hand-checkable small cases, because the
/// diagram of a general point set has no unique stored answer worth pinning.
/// </summary>
public class VoronoiTests
{
    [Fact]
    public void SquareCollapsesToOneVertexAndFourRays()
    {
        // Four cocircular points: both triangles share a circumcentre, so the duplicate merges,
        // the finite edge between the two centres vanishes, and only the four rays remain.
        VoronoiDiagram diagram = Voronoi.FromPoints([0, 1, 1, 0], [0, 0, 1, 1]);

        Point2D vertex = Assert.Single(diagram.Vertices);
        Assert.Equal(0.5, vertex.X, 12);
        Assert.Equal(0.5, vertex.Y, 12);
        Assert.Empty(diagram.Edges);
        Assert.Equal(4, diagram.Rays.Count);

        // Every ray leaves the centre along a diagonal-free axis direction, outward.
        foreach ((int start, Point2D direction) in diagram.Rays)
        {
            Assert.Equal(0, start);
            Assert.Equal(1.0, (direction.X * direction.X) + (direction.Y * direction.Y), 12);
            Assert.True(Math.Abs(direction.X) < 1e-9 || Math.Abs(direction.Y) < 1e-9);
        }

        // All four cells are unbounded corner cells: the centre vertex plus the point at infinity.
        Assert.All(diagram.Cells, cell => Assert.Equal(new[] { 0, -1 }, cell.OrderBy(v => -v)));
    }

    [Fact]
    public void InteriorPointGetsAClosedCell()
    {
        // Four compass points around the origin: the bisector against each is one side of the
        // square [-1,1]x[-1,1], so the middle cell's corners are (±1, ±1).
        VoronoiDiagram diagram = Voronoi.FromPoints([0, 2, 0, -2, 0], [0, 0, 2, 0, -2]);

        int[] middle = diagram.Cells[0];
        Assert.Equal(4, middle.Length);
        Assert.DoesNotContain(-1, middle);
        foreach (int v in middle)
        {
            Assert.Equal(1.0, Math.Abs(diagram.Vertices[v].X), 9);
            Assert.Equal(1.0, Math.Abs(diagram.Vertices[v].Y), 9);
        }

        // The outer four are on the hull, so their cells reach infinity.
        for (int p = 1; p <= 4; p++)
        {
            Assert.Contains(-1, diagram.Cells[p]);
        }
    }

    [Fact]
    public void EveryFiniteVertexIsEquidistantFromThreeSites()
    {
        // A Voronoi vertex is where three cells meet: its nearest-site distance is attained at
        // least three times. This is the diagram's defining property, checked on an irregular set.
        double[] x = [0.1, 2.3, 1.7, 3.9, 0.6, 2.8, 4.2, 1.2];
        double[] y = [0.4, 0.2, 2.1, 1.5, 3.3, 3.8, 3.1, 1.9];
        VoronoiDiagram diagram = Voronoi.FromPoints(x, y);

        Assert.NotEmpty(diagram.Vertices);
        foreach (Point2D vertex in diagram.Vertices)
        {
            double nearest = double.PositiveInfinity;
            for (int i = 0; i < x.Length; i++)
            {
                nearest = Math.Min(nearest, Distance(vertex, x[i], y[i]));
            }

            int atNearest = 0;
            for (int i = 0; i < x.Length; i++)
            {
                if (Distance(vertex, x[i], y[i]) <= nearest * (1 + 1e-9))
                {
                    atNearest++;
                }
            }

            Assert.True(atNearest >= 3, $"A Voronoi vertex touches {atNearest} cells; it needs at least 3.");
        }
    }

    [Fact]
    public void CellsWalkCounterClockwise()
    {
        double[] x = [0.1, 2.3, 1.7, 3.9, 0.6, 2.8, 4.2, 1.2];
        double[] y = [0.4, 0.2, 2.1, 1.5, 3.3, 3.8, 3.1, 1.9];
        VoronoiDiagram diagram = Voronoi.FromPoints(x, y);

        for (int p = 0; p < x.Length; p++)
        {
            int[] finite = [.. diagram.Cells[p].Where(v => v >= 0)];
            for (int i = 0; i + 1 < finite.Length; i++)
            {
                double here = Math.Atan2(diagram.Vertices[finite[i]].Y - y[p], diagram.Vertices[finite[i]].X - x[p]);
                double next = Math.Atan2(diagram.Vertices[finite[i + 1]].Y - y[p], diagram.Vertices[finite[i + 1]].X - x[p]);
                Assert.True(next > here, $"Cell {p} lists vertices out of angular order.");
            }
        }
    }

    [Fact]
    public void SegmentsCutTheRaysOffAtABoxRoundThePicture()
    {
        // The square's whole diagram is four rays from one vertex, so every segment starts there and
        // ends on a wall of the box — a ray has no end, and stopping at the picture's edge is the
        // only answer that can be drawn.
        VoronoiDiagram diagram = Voronoi.FromPoints([0, 1, 1, 0], [0, 0, 1, 1]);
        (Point2D From, Point2D To)[] segments = diagram.Segments();

        Assert.Equal(4, segments.Length);
        foreach ((Point2D from, Point2D to) in segments)
        {
            Assert.Equal(0.5, from.X, 12);
            Assert.Equal(0.5, from.Y, 12);

            // The box is the unit square padded by a tenth, so each ray runs 0.6 before it stops.
            Assert.Equal(0.6, Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y), 12);
        }
    }

    [Fact]
    public void SegmentsKeepEveryFiniteEdgeAsItIs()
    {
        double[] x = [0.1, 2.3, 1.7, 3.9, 0.6, 2.8, 4.2, 1.2];
        double[] y = [0.4, 0.2, 2.1, 1.5, 3.3, 3.8, 3.1, 1.9];
        VoronoiDiagram diagram = Voronoi.FromPoints(x, y);

        Assert.Equal(diagram.Edges.Count + diagram.Rays.Count, diagram.Segments().Length);
        for (int e = 0; e < diagram.Edges.Count; e++)
        {
            (int from, int to) = diagram.Edges[e];
            Assert.Equal(diagram.Vertices[from], diagram.Segments()[e].From);
            Assert.Equal(diagram.Vertices[to], diagram.Segments()[e].To);
        }
    }

    [Fact]
    public void ATriangulationGivenByHandProducesTheSameDualAsComputingOne()
    {
        double[] x = [0, 2, 1, 1];
        double[] y = [0, 0, 2, 0.5];

        VoronoiDiagram computed = Voronoi.FromPoints(x, y);
        VoronoiDiagram given = Voronoi.FromTriangulation(x, y, Delaunay.Triangulate(x, y));

        Assert.Equal(computed.Vertices, given.Vertices);
        Assert.Equal(computed.Edges, given.Edges);
        Assert.Equal(computed.Cells, given.Cells);
    }

    [Fact]
    public void ATriangleNamingAPointThatIsNotThereIsRefused()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => Voronoi.FromTriangulation([0, 1, 0], [0, 0, 1], new[,] { { 0, 1, 7 } }));
        Assert.Contains("only 3", failure.Message);
    }

    [Fact]
    public void CollinearPointsAreRefusedWithTheReason()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => Voronoi.FromPoints([0, 1, 2, 3], [0, 1, 2, 3]));
        Assert.Contains("collinear", failure.Message);
    }

    [Fact]
    public void TooFewPointsAreRefused()
    {
        Assert.Throws<ArgumentException>(() => Voronoi.FromPoints([0, 1], [0, 1]));
    }

    [Fact]
    public void MismatchedCoordinatesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => Voronoi.FromPoints([0, 1, 2], [0, 1]));
    }

    private static double Distance(Point2D vertex, double x, double y) =>
        Math.Sqrt(((vertex.X - x) * (vertex.X - x)) + ((vertex.Y - y) * (vertex.Y - y)));
}
