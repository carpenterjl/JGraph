using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// The root of a figure: a canvas that hosts one or more <see cref="AxesModel"/> regions. This is the
/// object a rendering surface binds to and observes (via the bubbling <see cref="GraphObject.Invalidated"/>
/// event) to know when to repaint.
/// </summary>
public sealed class FigureModel : GraphObject
{
    private Color _background = Colors.White;
    private Size2D _size = new(640, 480);
    private string _title = string.Empty;
    private TextStyle _titleStyle = new(Colors.Black, 16, bold: true);

    public FigureModel()
    {
        Name = "Figure";
        Axes = new GraphObjectCollection<AxesModel>(this);
        Annotations = new GraphObjectCollection<AnnotationObject>(this);
    }

    /// <summary>The axes (coordinate regions) contained in this figure.</summary>
    public GraphObjectCollection<AxesModel> Axes { get; }

    /// <summary>
    /// Annotations drawn on top of the whole figure in normalized [0, 1] figure coordinates
    /// ((0, 0) = top-left). They stay put regardless of axis navigation. Annotations added here should
    /// have <see cref="AnnotationObject.Space"/> set to <see cref="AnnotationSpace.Figure"/>.
    /// </summary>
    public GraphObjectCollection<AnnotationObject> Annotations { get; }

    /// <summary>The figure background color.</summary>
    [Category("Appearance")]
    public Color Background
    {
        get => _background;
        set => SetProperty(ref _background, value, InvalidationKind.Render);
    }

    /// <summary>The nominal figure size in device-independent units (used for export and defaults).</summary>
    [Category("Appearance")]
    public Size2D Size
    {
        get => _size;
        set => SetProperty(ref _size, value, InvalidationKind.Layout);
    }

    /// <summary>An optional figure-wide title drawn above all axes.</summary>
    [Category("General")]
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty, InvalidationKind.Layout);
    }

    /// <summary>How the figure title is drawn (font, size, weight, color).</summary>
    [Category("General"), DisplayName("Title style")]
    public TextStyle TitleStyle
    {
        get => _titleStyle;
        set => SetProperty(ref _titleStyle, value, InvalidationKind.Layout);
    }

    /// <summary>The fraction of each subplot cell reserved as a gutter, split across its sides.</summary>
    private const double SubplotGutter = 0.12;

    /// <summary>Creates a new axes, adds it to the figure, and returns it.</summary>
    public AxesModel AddAxes()
    {
        var axes = new AxesModel();
        Axes.Add(axes);
        return axes;
    }

    /// <summary>
    /// Creates an axes occupying cell <paramref name="index"/> of a <paramref name="rows"/> ×
    /// <paramref name="cols"/> grid (MATLAB <c>subplot</c>: 1-based, counted left-to-right then
    /// top-to-bottom), adds it to the figure, and returns it.
    /// </summary>
    public AxesModel AddSubplot(int rows, int cols, int index) => AddSubplot(rows, cols, index, index);

    /// <summary>
    /// Creates an axes spanning cells <paramref name="firstIndex"/>..<paramref name="lastIndex"/> of a
    /// <paramref name="rows"/> × <paramref name="cols"/> grid (the cells must form a rectangular block),
    /// adds it to the figure, and returns it.
    /// </summary>
    public AxesModel AddSubplot(int rows, int cols, int firstIndex, int lastIndex)
    {
        var axes = new AxesModel { NormalizedBounds = SubplotBounds(rows, cols, firstIndex, lastIndex) };
        Axes.Add(axes);
        return axes;
    }

    /// <summary>
    /// Computes the normalized figure bounds for a rectangular block of cells in a
    /// <paramref name="rows"/> × <paramref name="cols"/> subplot grid, with a gutter between cells.
    /// </summary>
    public static Rect2D SubplotBounds(int rows, int cols, int firstIndex, int lastIndex)
    {
        if (rows < 1 || cols < 1)
        {
            throw new ArgumentOutOfRangeException(rows < 1 ? nameof(rows) : nameof(cols), "Grid dimensions must be positive.");
        }

        int cellCount = rows * cols;
        if (firstIndex < 1 || firstIndex > cellCount || lastIndex < 1 || lastIndex > cellCount)
        {
            throw new ArgumentOutOfRangeException(nameof(firstIndex), $"Cell indices must be in [1, {cellCount}].");
        }

        int r0 = (firstIndex - 1) / cols;
        int c0 = (firstIndex - 1) % cols;
        int r1 = (lastIndex - 1) / cols;
        int c1 = (lastIndex - 1) % cols;
        int rowStart = System.Math.Min(r0, r1);
        int rowEnd = System.Math.Max(r0, r1);
        int colStart = System.Math.Min(c0, c1);
        int colEnd = System.Math.Max(c0, c1);

        double cellW = 1.0 / cols;
        double cellH = 1.0 / rows;
        double marginX = SubplotGutter * cellW * 0.5;
        double marginY = SubplotGutter * cellH * 0.5;

        double x = (colStart * cellW) + marginX;
        double y = (rowStart * cellH) + marginY;
        double width = ((colEnd - colStart + 1) * cellW) - (2 * marginX);
        double height = ((rowEnd - rowStart + 1) * cellH) - (2 * marginY);
        return new Rect2D(x, y, width, height);
    }

    /// <summary>Recomputes the data bounds and auto-scaled ranges of every axes in the figure.</summary>
    public void RecomputeDataBounds()
    {
        foreach (AxesModel axes in Axes)
        {
            axes.RecomputeDataBounds();
        }
    }
}
