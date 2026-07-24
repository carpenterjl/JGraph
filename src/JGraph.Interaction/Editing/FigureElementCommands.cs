using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Interaction.Editing;

/// <summary>
/// UI-free operations that create or reveal the parts of a figure, so the same logic backs the plot
/// browser's context menu, the "Add" button, and (being plain methods over the model) unit tests.
/// <para>
/// The undo policy matches the rest of the app (ADR 0005): property edits and annotation additions
/// are undoable and take an <see cref="UndoStack"/>; creating or removing an axes is structural and
/// is <em>not</em> undoable — the caller confirms a removal with a dialog instead, exactly as plot
/// removal already works, because tearing down an axes destroys every plot inside it.
/// </para>
/// </summary>
public static class FigureElementCommands
{
    // ---- Show hidden decorations (undoable) ----

    /// <summary>Reveals the legend if it is hidden. No-op when already shown.</summary>
    public static void ShowLegend(AxesModel axes, UndoStack? undo = null) =>
        SetVisible(axes.Legend, undo);

    /// <summary>Reveals the colorbar if it is hidden. No-op when already shown.</summary>
    public static void ShowColorbar(AxesModel axes, UndoStack? undo = null) =>
        SetVisible(axes.Colorbar, undo);

    private static void SetVisible(GraphObject target, UndoStack? undo)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Visible)
        {
            return;
        }

        target.Visible = true;
        undo?.Push(new PropertyChangeAction(target, nameof(GraphObject.Visible), false, true));
    }

    // ---- Titles (undoable) ----

    /// <summary>Gives the axes a title if it has none, so the empty title becomes editable text.</summary>
    public static void SetAxesTitle(AxesModel axes, string text = "Title", UndoStack? undo = null)
    {
        ArgumentNullException.ThrowIfNull(axes);
        if (!string.IsNullOrEmpty(axes.Title))
        {
            return;
        }

        string old = axes.Title;
        axes.Title = text;
        undo?.Push(new PropertyChangeAction(axes, nameof(AxesModel.Title), old, axes.Title));
    }

    /// <summary>Gives the figure a title if it has none.</summary>
    public static void SetFigureTitle(FigureModel figure, string text = "Title", UndoStack? undo = null)
    {
        ArgumentNullException.ThrowIfNull(figure);
        if (!string.IsNullOrEmpty(figure.Title))
        {
            return;
        }

        string old = figure.Title;
        figure.Title = text;
        undo?.Push(new PropertyChangeAction(figure, nameof(FigureModel.Title), old, figure.Title));
    }

    // ---- Axes and subplots (structural, not undoable) ----

    /// <summary>Adds a secondary X axis along the top edge and returns it.</summary>
    public static AxisModel AddSecondaryXAxis(AxesModel axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        return axes.AddXAxis(AxisPosition.Top);
    }

    /// <summary>Adds a secondary Y axis along the right edge and returns it.</summary>
    public static AxisModel AddSecondaryYAxis(AxesModel axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        return axes.AddYAxis(AxisPosition.Right);
    }

    /// <summary>Adds a new full-figure axes and returns it.</summary>
    public static AxesModel AddAxes(FigureModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        return figure.AddAxes();
    }

    /// <summary>
    /// Re-tiles the figure into a <paramref name="rows"/> × <paramref name="cols"/> grid: existing
    /// axes take the first cells in order, then one new axes fills the next free cell (if the grid has
    /// room). Returns the newly added axes, or null when the grid was already full. Structural, so it
    /// is not undoable — it is part of laying out the axes, the same side of the line as creating one.
    /// </summary>
    public static AxesModel? ApplySubplotGrid(FigureModel figure, int rows, int cols)
    {
        ArgumentNullException.ThrowIfNull(figure);
        if (rows < 1 || cols < 1)
        {
            throw new ArgumentOutOfRangeException(rows < 1 ? nameof(rows) : nameof(cols), "Grid dimensions must be positive.");
        }

        int cells = rows * cols;
        int reuse = System.Math.Min(figure.Axes.Count, cells);
        for (int i = 0; i < reuse; i++)
        {
            figure.Axes[i].NormalizedBounds = FigureModel.SubplotBounds(rows, cols, i + 1, i + 1);
        }

        if (figure.Axes.Count >= cells)
        {
            return null;
        }

        return figure.AddSubplot(rows, cols, figure.Axes.Count + 1);
    }

    /// <summary>
    /// Removes an axes from the figure. Structural and not undoable (it destroys the axes' plots), so
    /// the caller must confirm first.
    /// </summary>
    public static void RemoveAxes(FigureModel figure, AxesModel axes)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentNullException.ThrowIfNull(axes);
        figure.Axes.Remove(axes);
    }

    // ---- Annotations (undoable) ----

    /// <summary>Adds a text label at the centre of the axes' current view and returns it.</summary>
    public static TextAnnotation AddText(AxesModel axes, UndoStack? undo = null)
    {
        ArgumentNullException.ThrowIfNull(axes);
        Point2D c = ViewCenter(axes);
        TextAnnotation text = axes.AddText(c.X, c.Y, "Text");
        undo?.Push(new AddAnnotationAction(axes.Annotations, text, "Add text"));
        return text;
    }

    /// <summary>Adds a short arrow near the centre of the axes' current view and returns it.</summary>
    public static ArrowAnnotation AddArrow(AxesModel axes, UndoStack? undo = null)
    {
        ArgumentNullException.ThrowIfNull(axes);
        Point2D c = ViewCenter(axes);
        double dx = SpanFraction(axes.PrimaryXAxis, 0.15);
        double dy = SpanFraction(axes.PrimaryYAxis, 0.15);
        ArrowAnnotation arrow = axes.AddArrow(c.X - dx, c.Y - dy, c.X + dx, c.Y + dy);
        undo?.Push(new AddAnnotationAction(axes.Annotations, arrow, "Add arrow"));
        return arrow;
    }

    /// <summary>Adds a rectangle around the centre of the axes' current view and returns it.</summary>
    public static RectangleAnnotation AddShape(AxesModel axes, UndoStack? undo = null)
    {
        ArgumentNullException.ThrowIfNull(axes);
        Point2D c = ViewCenter(axes);
        double dx = SpanFraction(axes.PrimaryXAxis, 0.2);
        double dy = SpanFraction(axes.PrimaryYAxis, 0.2);
        RectangleAnnotation shape = axes.AddRectangleAnnotation(c.X - dx, c.Y - dy, c.X + dx, c.Y + dy);
        undo?.Push(new AddAnnotationAction(axes.Annotations, shape, "Add shape"));
        return shape;
    }

    // ---- Legend entries (undoable) ----

    /// <summary>Includes a legend row (sets its <see cref="GraphObject.Visible"/> flag).</summary>
    public static void IncludeLegendEntry(LegendEntryModel entry, UndoStack? undo = null) =>
        SetEntryVisible(entry, true, undo);

    /// <summary>Excludes a legend row without removing it, so it can be re-included later.</summary>
    public static void ExcludeLegendEntry(LegendEntryModel entry, UndoStack? undo = null) =>
        SetEntryVisible(entry, false, undo);

    private static void SetEntryVisible(LegendEntryModel entry, bool visible, UndoStack? undo)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Visible == visible)
        {
            return;
        }

        entry.Visible = visible;
        undo?.Push(new PropertyChangeAction(entry, nameof(GraphObject.Visible), !visible, visible));
    }

    /// <summary>
    /// Returns a custom-placed legend to the top-right preset in one undo step (both the position and
    /// the stored location revert together). No-op when the legend is not custom-placed.
    /// </summary>
    public static void ResetLegendPlacement(LegendModel legend, UndoStack? undo = null)
    {
        ArgumentNullException.ThrowIfNull(legend);
        if (legend.Position != LegendPosition.Custom)
        {
            return;
        }

        LegendPosition oldPosition = legend.Position;
        legend.Position = LegendPosition.TopRight;
        undo?.Push(new PropertyChangeAction(legend, nameof(LegendModel.Position), oldPosition, legend.Position));
    }

    /// <summary>Moves a legend row up or down by one place, clamped to the ends.</summary>
    public static void MoveLegendEntry(LegendModel legend, LegendEntryModel entry, int delta, UndoStack? undo = null)
    {
        ArgumentNullException.ThrowIfNull(legend);
        ArgumentNullException.ThrowIfNull(entry);

        int from = legend.Entries.IndexOf(entry);
        if (from < 0)
        {
            return;
        }

        int to = System.Math.Clamp(from + delta, 0, legend.Entries.Count - 1);
        if (to == from)
        {
            return;
        }

        legend.Entries.Move(from, to);
        undo?.Push(new MoveLegendEntryAction(legend, from, to));
    }

    /// <summary>The centre of an axes' current view in data coordinates.</summary>
    private static Point2D ViewCenter(AxesModel axes) =>
        new(axes.PrimaryXAxis.Range.Center, axes.PrimaryYAxis.Range.Center);

    private static double SpanFraction(AxisModel axis, double fraction)
    {
        double span = axis.Range.Max - axis.Range.Min;
        return (span == 0 ? 1 : span) * fraction;
    }
}
