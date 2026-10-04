using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Where an axes sits: the plot box, the cell around it, the margin between the two, and which of
/// the first two a placement fixes. Three of the four are answers about a drawing rather than about
/// the document, so they read through <see cref="AxesModel.LastLayout"/> — the report the renderer
/// files after each frame — and fall back to an estimate before the first one.
/// </summary>
/// <remarks>
/// These rectangles are MATLAB's, not the model's: the origin is the bottom-left corner and Y counts
/// upward, where <see cref="AxesModel.NormalizedBounds"/> counts downward from the top. The flip is
/// done here, once, in both directions, so that the model keeps the one convention the renderer,
/// subplot, and the document format have always used.
/// </remarks>
internal static partial class JgsGraphicsProperties
{
    /// <summary>
    /// The same four rectangles for a chart that owns a whole axes in MATLAB but is a plot on one
    /// here — a heatmap. It answers for the axes it is drawn on, which is the shape its Title and its
    /// two labels have answered in since it was written.
    /// </summary>
    private static void AddChartLayout(IDictionary<string, GraphicsProperty> table)
    {
        var host = new Dictionary<string, GraphicsProperty>(StringComparer.OrdinalIgnoreCase);
        AddAxesLayout(host);

        // A chart that fills its axes sits where its axes sits, tile included.
        AddLayoutHandle(table, Owner);

        foreach ((string name, GraphicsProperty property) in host)
        {
            string spelling = name;
            GraphicsProperty over = property;
            Put(table, spelling,
                entry => over.Read(JgsHandleRegistry.EntryFor(Owning(entry, 0, 0))),
                over.Write is null
                    ? null
                    : (entry, value, line, col) => over.Write(
                        JgsHandleRegistry.EntryFor(Owning(entry, line, col)), value, line, col));
        }
    }

    private static void AddAxesLayout(IDictionary<string, GraphicsProperty> table)
    {
        // Position and InnerPosition are two names for the plot box, as they are in MATLAB. Writing
        // either one pins that box and lets the margins grow outward, which is what MATLAB records
        // by moving PositionConstraint to 'innerposition'.
        AddInnerPosition(table, "Position");
        AddInnerPosition(table, "InnerPosition");

        Put(table, "OuterPosition",
            entry => AxesRectValue(Axes(entry), inner: false),
            (entry, value, line, col) =>
                SetAxesRect(Axes(entry), Box("OuterPosition", value, line, col), inner: false));

        // Read-only, because it is a measurement. MATLAB orders it [left bottom right top], which is
        // the anticlockwise order its rectangles use rather than the clockwise one a Thickness uses.
        Put(table, "TightInset",
            entry =>
            {
                Thickness inset = LayoutOf(Axes(entry)).NormalizeInset();
                return Row(inset.Left, inset.Bottom, inset.Right, inset.Top);
            });

        Put(table, "PositionConstraint",
            entry => JgsValue.Str(Axes(entry).PositionConstraint == PositionConstraintType.InnerPosition
                ? "innerposition"
                : "outerposition"),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                string word = JgsBuiltins.StrOf("PositionConstraint", value, line, col);
                switch (word.ToLowerInvariant())
                {
                    case "innerposition":
                        // Taking the box that is drawn now is what makes the constraint act rather
                        // than merely be recorded: from here on the margins move instead of it. An
                        // axes pinned in absolute units is re-pinned by the rectangle it now keeps.
                        if (axes.PixelBounds is not null && axes.InnerTarget is null)
                        {
                            Rect2D inner = AxesPixels(axes, inner: true);
                            axes.InnerTarget = FractionOf(inner, axes.ReferenceSize());
                            axes.PixelBounds = inner;
                        }
                        else if (axes.PixelBounds is null)
                        {
                            axes.InnerTarget = InnerOf(axes);
                        }

                        axes.PositionConstraint = PositionConstraintType.InnerPosition;
                        break;

                    case "outerposition":
                        if (axes.PixelBounds is not null && axes.InnerTarget is not null)
                        {
                            Rect2D outer = AxesPixels(axes, inner: false);
                            axes.NormalizedBounds = FractionOf(outer, axes.ReferenceSize());
                            axes.InnerTarget = null;
                            axes.PixelBounds = outer;
                        }

                        axes.InnerTarget = null;
                        axes.PositionConstraint = PositionConstraintType.OuterPosition;
                        break;

                    default:
                        throw new JgsRuntimeException(line, col,
                            $"PositionConstraint is 'innerposition' or 'outerposition', but got '{word}'.");
                }
            });

        // MATLAB's six units (U2). The model keeps fractions of the area the axes is placed in;
        // in any other unit the rectangle is pinned in pixels and holds them through a resize.
        Put(table, "Units",
            entry => UnitsValue(Axes(entry).Units),
            (entry, value, line, col) => SetAxesUnits(Axes(entry), UnitsWord(entry, value, line, col)));
    }

    private static void AddInnerPosition(IDictionary<string, GraphicsProperty> table, string name) =>
        Put(table, name,
            entry => AxesRectValue(Axes(entry), inner: true),
            (entry, value, line, col) => SetAxesRect(Axes(entry), Box(name, value, line, col), inner: true));

    /// <summary>The plot box, as fractions of the figure with Y still downward.</summary>
    private static Rect2D InnerOf(AxesModel axes)
    {
        if (axes.InnerTarget is { } target) return target;
        AxesLayoutSnapshot layout = LayoutOf(axes);
        return layout.Normalize(layout.PlotAreaPx);
    }

    /// <summary>The cell the axes occupies, as fractions of the figure with Y still downward.</summary>
    private static Rect2D OuterOf(AxesModel axes)
    {
        // While the cell is what was asked for, it is what was asked for exactly — no measurement
        // needed, and none of the estimate's error. It is only a pinned plot box that makes the cell
        // something the renderer worked out.
        if (axes.InnerTarget is null)
        {
            return axes.NormalizedBounds;
        }

        AxesLayoutSnapshot layout = LayoutOf(axes);
        return layout.Normalize(layout.OuterPx);
    }

    /// <summary>
    /// What the renderer measured last frame, or what it would measure if it drew now. An axes that
    /// has never been drawn still has to answer, and an estimate is the only answer there is.
    /// </summary>
    private static AxesLayoutSnapshot LayoutOf(AxesModel axes) =>
        axes.LastLayout ?? AxesLayoutSnapshot.Estimate(
            axes, axes.Parent is FigureModel ? axes.ReferenceSize() : new Size2D(640, 480));

    /// <summary>Reads a four-element rectangle, refusing a width or height that is not positive.</summary>
    private static Rect2D Box(string what, JgsValue value, int line, int col)
    {
        double[] box = Numbers(what, value, 4, line, col);
        if (box[2] <= 0 || box[3] <= 0)
        {
            throw new JgsRuntimeException(line, col, $"{what} needs a positive width and height.");
        }

        return new Rect2D(box[0], box[1], box[2], box[3]);
    }

    /// <summary>Turns a downward-Y rectangle into the upward-Y row MATLAB reports.</summary>
    private static JgsValue FlipRow(Rect2D rect) =>
        Row(rect.X, 1 - rect.Y - rect.Height, rect.Width, rect.Height);

    /// <summary>Turns an upward-Y rectangle from a script into the downward-Y one the model keeps.</summary>
    private static Rect2D FlipRect(Rect2D rect) =>
        new(rect.X, 1 - rect.Y - rect.Height, rect.Width, rect.Height);
}
