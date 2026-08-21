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
    private static void AddAxesLayout(IDictionary<string, GraphicsProperty> table)
    {
        // Position and InnerPosition are two names for the plot box, as they are in MATLAB. Writing
        // either one pins that box and lets the margins grow outward, which is what MATLAB records
        // by moving PositionConstraint to 'innerposition'.
        AddInnerPosition(table, "Position");
        AddInnerPosition(table, "InnerPosition");

        Put(table, "OuterPosition",
            entry => FlipRow(OuterOf(Axes(entry))),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                axes.InnerTarget = null;
                axes.PositionConstraint = PositionConstraintType.OuterPosition;
                axes.NormalizedBounds = FlipRect(Box("OuterPosition", value, line, col));
            });

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
                        // than merely be recorded: from here on the margins move instead of it.
                        axes.InnerTarget = InnerOf(axes);
                        axes.PositionConstraint = PositionConstraintType.InnerPosition;
                        break;

                    case "outerposition":
                        axes.InnerTarget = null;
                        axes.PositionConstraint = PositionConstraintType.OuterPosition;
                        break;

                    default:
                        throw new JgsRuntimeException(line, col,
                            $"PositionConstraint is 'innerposition' or 'outerposition', but got '{word}'.");
                }
            });

        // An axes is placed in fractions of its figure and nothing else here measures in points or
        // pixels, so the one honest answer is 'normalized' and the others are refused by name.
        Put(table, "Units",
            static entry => JgsValue.Str("normalized"),
            (entry, value, line, col) =>
            {
                string word = JgsBuiltins.StrOf("Units", value, line, col);
                if (!word.Equals("normalized", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"Axes units other than 'normalized' are not supported, but got '{word}'.");
                }
            });
    }

    private static void AddInnerPosition(IDictionary<string, GraphicsProperty> table, string name) =>
        Put(table, name,
            entry => FlipRow(InnerOf(Axes(entry))),
            (entry, value, line, col) =>
            {
                AxesModel axes = Axes(entry);
                axes.InnerTarget = FlipRect(Box(name, value, line, col));
                axes.PositionConstraint = PositionConstraintType.InnerPosition;
            });

    /// <summary>The plot box, as fractions of the figure with Y still downward.</summary>
    private static Rect2D InnerOf(AxesModel axes)
    {
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
            axes, axes.Parent is FigureModel figure ? figure.Size : new Size2D(640, 480));

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
