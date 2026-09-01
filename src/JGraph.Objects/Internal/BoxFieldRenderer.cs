using JGraph.Core.Drawing;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;
using JGraph.Rendering;

namespace JGraph.Objects.Internal;

/// <summary>
/// Paints a field of axis-aligned boxes through a camera: the part <see cref="Bar3DPlot"/> and
/// <see cref="Histogram2Plot"/> have in common (M122).
/// </summary>
/// <remarks>
/// <para>
/// It was lifted out of <c>bar3</c> rather than copied into <c>histogram2</c>, because the thing
/// worth sharing is not the drawing but the <em>sort</em>. Every face of every box goes into one
/// depth order, not one order per box: boxes interleave as soon as the camera is off an axis, and
/// sorting them box by box puts a near face behind a far one. That is a mistake a second
/// implementation would have made again, and it is invisible from any angle you would think to check
/// from.
/// </para>
/// <para>
/// The faces are shaded by which way they point — top brightest, sides stepped down — because a box
/// painted one flat colour reads as a hexagon rather than as a solid. The shading is fixed rather
/// than lit: this pipeline has no light model, and a fixed step is honest about being a legibility
/// device rather than a rendering of anything.
/// </para>
/// </remarks>
internal sealed class BoxFieldRenderer
{
    /// <summary>How much of a face's colour survives, per face direction. Top first.</summary>
    public const double TopShade = 1.0;

    private const double BottomShade = 0.5;
    private const double XFaceShade = 0.82;
    private const double YFaceShade = 0.66;

    /// <summary>The corners of each face, as indices into the eight corners of a box.</summary>
    private static readonly int[][] BoxFaces =
    [
        [4, 5, 7, 6],   // top    (z max)
        [0, 1, 3, 2],   // bottom (z min)
        [0, 1, 5, 4],   // front  (y min)
        [2, 3, 7, 6],   // back   (y max)
        [0, 2, 6, 4],   // left   (x min)
        [1, 3, 7, 5],   // right  (x max)
    ];

    /// <summary>How much of the box's colour each of those faces keeps, in the same order.</summary>
    private static readonly double[] FaceShades =
        [TopShade, BottomShade, YFaceShade, YFaceShade, XFaceShade, XFaceShade];

    private double[] _faceDepths = [];
    private int[] _faceOrder = [];
    private readonly Point2D[] _face = new Point2D[4];

    /// <summary>
    /// Draws every box, back to front.
    /// </summary>
    /// <param name="context">Where the polygons go.</param>
    /// <param name="projection">The camera the corners are pushed through.</param>
    /// <param name="state">The render state; its <c>DepthSort</c> is MATLAB's <c>SortMethod</c>.</param>
    /// <param name="boxes">The boxes, in world coordinates.</param>
    /// <param name="colorOf">The unshaded colour of the box at an index.</param>
    /// <param name="stroke">The edge style, or null to draw no edges.</param>
    /// <param name="opacity">What every face's colour is multiplied by.</param>
    public void Render(
        IRenderContext context,
        Projection3D projection,
        RenderState state,
        IReadOnlyList<Bar3DBox> boxes,
        Func<int, Color> colorOf,
        LineStyle? stroke,
        double opacity)
    {
        if (boxes.Count == 0)
        {
            return;
        }

        int faces = boxes.Count * BoxFaces.Length;
        if (_faceDepths.Length < faces)
        {
            _faceDepths = new double[faces];
            _faceOrder = new int[faces];
        }

        var corners = new Point2D[boxes.Count * 8];
        var depths = new double[boxes.Count * 8];
        for (int b = 0; b < boxes.Count; b++)
        {
            Bar3DBox box = boxes[b];
            for (int corner = 0; corner < 8; corner++)
            {
                (double x, double y, double z) = CornerOf(box, corner);
                (corners[(b * 8) + corner], depths[(b * 8) + corner]) = projection.Project(x, y, z);
            }
        }

        for (int f = 0; f < faces; f++)
        {
            int[] indices = BoxFaces[f % BoxFaces.Length];
            int at = (f / BoxFaces.Length) * 8;
            double sum = 0;
            foreach (int corner in indices)
            {
                sum += depths[at + corner];
            }

            _faceDepths[f] = sum / indices.Length;
            _faceOrder[f] = f;
        }

        // SortMethod 'childorder' paints the faces in the order they are held, so the sort is what is
        // skipped: the arrays already carry that order.
        if (state.DepthSort)
        {
            Array.Sort(_faceDepths, _faceOrder, 0, faces);
        }

        for (int i = 0; i < faces; i++)
        {
            int f = _faceOrder[i];
            int which = f % BoxFaces.Length;
            int b = f / BoxFaces.Length;
            int[] indices = BoxFaces[which];

            bool drawable = true;
            for (int v = 0; v < indices.Length; v++)
            {
                _face[v] = corners[(b * 8) + indices[v]];
                drawable &= _face[v].IsFinite;
            }

            if (!drawable)
            {
                continue;
            }

            context.DrawPolygon(
                _face.AsSpan(0, indices.Length),
                stroke,
                Shaded(colorOf(b), FaceShades[which]).WithOpacity(opacity));
        }
    }

    /// <summary>A colour stepped towards black by how much of it a face keeps.</summary>
    public static Color Shaded(Color color, double keep) =>
        keep >= 1 ? color : Color.Lerp(color, Colors.Black, 1 - keep);

    private static (double X, double Y, double Z) CornerOf(Bar3DBox box, int corner) => (
        (corner & 1) == 0 ? box.XMin : box.XMax,
        (corner & 2) == 0 ? box.YMin : box.YMax,
        (corner & 4) == 0 ? box.ZMin : box.ZMax);
}
