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

    // A uiaxes' Position is its outer rectangle (U5); its InnerPosition is still the plot box.
    private static void AddInnerPosition(IDictionary<string, GraphicsProperty> table, string name) =>
        Put(table, name,
            entry => AxesRectValue(Axes(entry), inner: !(name == "Position" && Axes(entry).PositionIsOuter)),
            (entry, value, line, col) => SetAxesRect(
                Axes(entry), Box(name, value, line, col), inner: !(name == "Position" && Axes(entry).PositionIsOuter)));

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
    private static AxesLayoutSnapshot LayoutOf(AxesModel axes)
    {
        if (axes.LastLayout is { } measured)
        {
            return measured;
        }

        AxesLayoutSnapshot estimate = AxesLayoutSnapshot.Estimate(
            axes, axes.Parent is FigureModel ? axes.ReferenceSize() : new Size2D(640, 480));
        return JgsRunningDialect.ThreadIsMatlab && axes.KeepsPlotBoxMargins && !axes.Is3D && !axes.IsPolar
            ? R2025bEstimate(axes, estimate)
            : estimate;
    }

    /// <summary>
    /// The tight inset and plot box R2025b reads before anything is drawn (open item 133, probe_box over
    /// category counts, label lengths, angles, fonts and labels): below the box, the tick labels'
    /// extent at their angle — the widest rounded up to a pixel times |sin|, a label's height times
    /// |cos| — and 5.33 pixels, then an xlabel's height and 1.33 more; left of it, the widest y label
    /// rounded up and 5.33, then a ylabel's height and 4 more; above it, half a y label's height (the
    /// top label's overhang), or a title's height and 3.67; and the first and last x labels' overhang
    /// past the box ends, if more. The box keeps its default margins until the inset outgrows one,
    /// and then that edge moves to the inset and the default margin's fraction of a pixel.
    /// </summary>
    private static AxesLayoutSnapshot R2025bEstimate(AxesModel axes, AxesLayoutSnapshot estimate)
    {
        AxisModel x = axes.PrimaryXAxis;
        AxisModel y = axes.PrimaryYAxis;
        bool automatic = !axes.FontSizeManual;
        static IReadOnlyList<string> Labels(AxisModel ruler) =>
            ruler.TickLabelOverrides ?? JgsRulerTicks.Current(ruler).Select(static t => t.Label).ToArray();
        // An axis label and a title are measured at R2025b's sizes: its axes font times the multiplier, the title bold.
        double basePoints = x.TickLabelStyle.FontSize * (automatic ? 10.0 / 11 : 1);
        double Height(string text, JGraph.Core.Drawing.TextStyle style, double multiplier, bool bold) =>
            TickLabelTurn.Measure([text], style.WithSize(basePoints * multiplier).WithBold(bold), automaticFont: false).Height;

        double bottom = 0;
        double turnedLeft = 0;
        double right = 0;
        if (x.ShowTickLabels && Labels(x) is { Count: > 0 } xLabels)
        {
            (double widest, double height, _) = TickLabelTurn.Measure(xLabels, x.TickLabelStyle, automatic);
            double radians = JgsRulerTicks.ReadAngle(x).AsNumber * Math.PI / 180;
            double sin = Math.Abs(Math.Sin(radians));
            double cos = Math.Abs(Math.Cos(radians));
            bottom = (Math.Ceiling(widest) * sin) + (height * cos) + (16.0 / 3);

            // How far the first and last labels stand past the box: a level label is centred on its
            // tick, a turned one hangs down-left from it, its width (rounded) times |cos| to the left
            // and its height times |sin| to the right (probe_box: 0, 15, 30 and 45 degrees).
            double[] shown = [.. (x.TickPositions ?? JgsRulerTicks.Current(x).Select(static t => t.Value))
                .Where(t => t >= Math.Min(x.Range.Min, x.Range.Max) - 1e-9 && t <= Math.Max(x.Range.Min, x.Range.Max) + 1e-9)];
            if (shown.Length > 0 && x.Range.Max != x.Range.Min && x.Scale != AxisScaleType.Logarithmic)
            {
                double width = estimate.PlotAreaPx.Width;
                double firstIn = Math.Abs(shown.Min() - x.Range.Min) / Math.Abs(x.Range.Max - x.Range.Min) * width;
                double lastIn = Math.Abs(x.Range.Max - shown.Max()) / Math.Abs(x.Range.Max - x.Range.Min) * width;
                double rounded = Math.Round(widest, MidpointRounding.AwayFromZero);
                bool level = sin < 1e-9;
                turnedLeft = (level ? rounded / 2 : rounded * cos) - firstIn;
                // Upright (90 degrees), a label is centred across its tick, half its height either side.
                right = Math.Max(0, (level ? rounded / 2 : sin > 1 - 1e-9 ? height / 2 : height * sin) - lastIn);
            }
        }

        if (!string.IsNullOrEmpty(x.Label))
        {
            bottom += Height(x.Label, x.LabelStyle, axes.LabelFontSizeMultiplier, bold: false) + (4.0 / 3);
        }

        double left = 0;
        double top = 0;
        if (y.ShowTickLabels && Labels(y) is { Count: > 0 } yLabels)
        {
            (double widest, double height, _) = TickLabelTurn.Measure(yLabels, y.TickLabelStyle, automatic);
            left = Math.Ceiling(widest) + (16.0 / 3);
            top = height / 2;
        }

        if (!string.IsNullOrEmpty(y.Label))
        {
            left += Height(y.Label, y.LabelStyle, axes.LabelFontSizeMultiplier, bold: false) + 4;
        }

        if (!string.IsNullOrEmpty(axes.Title))
        {
            top = Math.Max(top, Height(axes.Title, axes.TitleStyle, axes.TitleFontSizeMultiplier, bold: true) + (11.0 / 3));
        }

        var inset = new Thickness(Math.Max(left, turnedLeft), top, right, bottom);
        Rect2D outer = estimate.OuterPx;
        Rect2D plot = estimate.PlotAreaPx;
        static double Edge(double margin, double needed) => needed > margin ? needed + (margin - Math.Floor(margin)) : margin;
        double marginLeft = Edge(plot.X - outer.X, inset.Left);
        double marginTop = Edge(plot.Y - outer.Y, inset.Top);
        double marginRight = Edge(outer.Right - plot.Right, inset.Right);
        double marginBottom = Edge(outer.Bottom - plot.Bottom, inset.Bottom);
        var box = new Rect2D(
            outer.X + marginLeft,
            outer.Y + marginTop,
            Math.Max(1, outer.Width - marginLeft - marginRight),
            Math.Max(1, outer.Height - marginTop - marginBottom));
        return new AxesLayoutSnapshot(outer, box, inset, estimate.CanvasPx);
    }

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
