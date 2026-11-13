using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>pcolor</c> in the MATLAB dialect (open item 167): R2025b's is a flat <c>surface</c> at height
/// zero, its <c>CData</c> the colour matrix, where this build had drawn an <c>image</c>.
/// <para>
/// R2025b's <c>pcolor.m</c> is <c>newplot</c>, then <c>surface(x, y, zeros(size(c)), c)</c> (or
/// <c>surface(zeros(size(c)), c)</c> for <c>pcolor(C)</c>) with <c>AlignVertexCenters</c> on, then -
/// only when the axes is not held - the view flat, the box on and both limits set to the data's
/// span (probe <c>pc1</c>). Its object reads as a surface's: <c>FaceColor</c> 'flat', the surface's
/// dark grey edges a half point wide, <c>XData</c> and <c>YData</c> as given (a matrix stays a
/// matrix), the counted <c>YData</c> of <c>pcolor(C)</c> a column. A flat face takes its colour from
/// its first corner, so the last row and column of <c>C</c> colour nothing, as in R2025b.
/// </para>
/// <para>
/// A JGS script keeps its image: <c>pcolor</c> there is the heatmap over the given extents that
/// ADR 0022 made it, and nothing in that dialect asks for a surface.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>R2025b's surface edge colour, [0.1294 0.1294 0.1294] (probe <c>pc1</c>).</summary>
    private static readonly Color PcolorEdgeColor = new(33, 33, 33);

    private static JgsValue MatlabPcolor(IReadOnlyList<JgsValue> args, int line, int col)
    {
        // pcolor.m's own checks, in its order.
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (IsMatlabVector(args[^1]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:pcolor:NonMatrixColorInput", "Color data input must be a matrix.");
        }

        if (args.Count == 3 && PcolorSizesDisagree(args[0], args[1], args[2]))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:pcolor:InputSizeMismatch", "Matrix dimensions must agree.");
        }

        if (args.Any(IsComplexData))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:pcolor:NonRealInputs", "Data inputs must be real.");
        }

        // Two inputs reach pcolor.m's deal(args{1:3}).
        if (args.Count == 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:badsubscript",
                "Index exceeds the number of array elements. Index must not exceed 2.");
        }

        AxesModel axes = JG.Gca();
        bool held = JG.IsHolding;
        bool was3D = axes.Is3D;
        bool grid = axes.Grid.ShowMajor;

        SurfacePlot surface;
        double[,] colors;
        (double Min, double Max) xSpan, ySpan;
        bool counted = args.Count == 1;
        if (counted)
        {
            colors = Matrix("pcolor", args, 0, line, col);
            int rows = colors.GetLength(0), cols = colors.GetLength(1);
            surface = JG.Surf(Ramp1(cols), Ramp1(rows), new double[rows, cols]);
            surface.YColumn = true;
            xSpan = (1, cols);
            ySpan = (1, rows);
        }
        else
        {
            colors = Matrix("pcolor", args, 2, line, col);
            int rows = colors.GetLength(0), cols = colors.GetLength(1);
            var heights = new double[rows, cols];
            if (IsMatlabVector(args[0]) && IsMatlabVector(args[1]))
            {
                double[] xs = DoubleArray("pcolor", args, 0, line, col);
                double[] ys = DoubleArray("pcolor", args, 1, line, col);
                surface = JG.Surf(xs, ys, heights);
                surface.YColumn = ys.Length > 1 && JgsMatrix.DimsOf(args[1]) is [_, 1];
                xSpan = Span(xs);
                ySpan = Span(ys);
            }
            else
            {
                // A matrix stays the matrix it was given; a vector beside one is spread over the grid.
                double[,] xg = PcolorGrid(args[0], rows, cols, alongX: true, line, col);
                double[,] yg = PcolorGrid(args[1], rows, cols, alongX: false, line, col);
                surface = JG.Surf(xg, yg, heights);
                xSpan = Span(xg.Cast<double>());
                ySpan = Span(yg.Cast<double>());
            }
        }

        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(surface);
        JgsGraphicsProperties.Set(entry, "CData", JgsMatrix.Build(colors.GetLength(0), colors.GetLength(1), (r, c) => colors[r, c]), line, col);
        surface.Style = SurfaceStyle.FilledWithWireframe;
        surface.Shading = SurfaceShading.Flat;
        surface.FaceColor = null;
        surface.EdgeColor = PcolorEdgeColor;
        surface.ColormapEdges = false;
        surface.EdgeWidth = 0.5;
        surface.AlignVertexCenters = true;

        axes.Grid.ShowMajor = held && grid;
        if (held)
        {
            axes.Is3D = was3D;
            return JgsHandleRegistry.For(surface);
        }

        // Not held: R2025b's pcolor.m lays the axes flat, boxes it and sets both limits.
        axes.Is3D = false;
        JgsHandleEntry axesEntry = JgsHandleRegistry.EntryFor(axes);
        JgsGraphicsProperties.Set(axesEntry, "Box", JgsValue.Str("on"), line, col);
        JgsGraphicsProperties.Set(axesEntry, "XLim", Row2(Widened(xSpan)), line, col);
        JgsGraphicsProperties.Set(axesEntry, "YLim", Row2(Widened(ySpan)), line, col);
        return JgsHandleRegistry.For(surface);

        static (double Min, double Max) Span(IEnumerable<double> values)
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (double v in values)
            {
                if (double.IsNaN(v))
                {
                    continue;
                }

                min = System.Math.Min(min, v);
                max = System.Math.Max(max, v);
            }

            return (min, max);
        }

        // pcolor.m: a span that is no span is opened to one unit.
        static (double Min, double Max) Widened((double Min, double Max) span) =>
            span.Max <= span.Min ? (span.Min, span.Min + 1) : span;

        static JgsValue Row2((double Min, double Max) span) =>
            JgsMatrix.FromColumnMajor([span.Min, span.Max], 1, 2);
    }

    /// <summary>MATLAB's <c>isvector</c>: two dimensions, one of them one, at least one element.</summary>
    private static bool IsMatlabVector(JgsValue value) =>
        JgsMatrix.DimsOf(value) is [int rows, int cols] && rows * cols >= 1 && (rows == 1 || cols == 1);

    /// <summary>pcolor.m's <c>LdimMismatch</c>, word for word.</summary>
    private static bool PcolorSizesDisagree(JgsValue x, JgsValue y, JgsValue z)
    {
        static (int Rows, int Cols) Size(JgsValue value) =>
            JgsMatrix.DimsOf(value) is [int rows, int cols, ..] ? (rows, cols) : (1, 1);

        (int xm, int xn) = Size(x);
        (int ym, int yn) = Size(y);
        (int zm, int zn) = Size(z);
        return (xm == 1 && xn != zn) || (xn == 1 && xm != zn) || (xm != 1 && xn != 1 && (xm != zm || xn != zn))
            || (ym == 1 && yn != zm) || (yn == 1 && ym != zm) || (ym != 1 && yn != 1 && (ym != zm || yn != zn));
    }

    /// <summary>One of pcolor's two position arguments as a full grid: a matrix as it is, a vector spread along its axis.</summary>
    private static double[,] PcolorGrid(JgsValue value, int rows, int cols, bool alongX, int line, int col)
    {
        if (!IsMatlabVector(value))
        {
            return Matrix("pcolor", [value], 0, line, col);
        }

        double[] v = DoubleArray("pcolor", [value], 0, line, col);
        var grid = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                grid[r, c] = alongX ? v[c] : v[r];
            }
        }

        return grid;
    }
}
