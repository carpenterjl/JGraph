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
    private Colormap? _colormap;
    private IReadOnlyList<double>? _alphamap;
    private FigureNextPlot _nextPlot = FigureNextPlot.Add;
    private bool _numberTitle = true;
    private string _fileName = string.Empty;
    private bool _invertHardcopy;
    private bool _graphicsSmoothing = true;
    private PointerShape _pointer = PointerShape.Arrow;
    private bool _resizable = true;
    private FigureToolBarMode _toolBar = FigureToolBarMode.Auto;
    private FigureWindowState _windowState = FigureWindowState.Normal;
    private Point2D _position;
    private bool _positionSpecified;
    private PaperUnitType _paperUnits = PaperUnitType.Inches;
    private string _paperType = "usletter";
    private Size2D? _paperSize;
    private PaperOrientationType _paperOrientation = PaperOrientationType.Portrait;
    private Rect2D _paperPosition = new(0.25, 2.5, 8, 6);
    private bool _paperPositionAuto = true;
    private TiledLayoutModel? _tiledLayout;

    public FigureModel()
    {
        Name = "Figure";
        Axes = new GraphObjectCollection<AxesModel>(this);
        Annotations = new GraphObjectCollection<AnnotationObject>(this);
        ContextMenus = new GraphObjectCollection<ContextMenuModel>(this);
    }

    /// <summary>The axes (coordinate regions) contained in this figure.</summary>
    public GraphObjectCollection<AxesModel> Axes { get; }

    /// <summary>Script-defined right-click menus owned by this figure (MATLAB <c>uicontextmenu</c>).
    /// Nothing here draws; objects point at an entry through their <c>ContextMenu</c> property.</summary>
    public GraphObjectCollection<ContextMenuModel> ContextMenus { get; }

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

    /// <summary>
    /// The colormap this figure's axes fall back on (MATLAB figure <c>Colormap</c>), or null for
    /// parula. An axes that has chosen its own overrides it; one that has not reads through to here,
    /// which is what makes <c>colormap(fig, map)</c> reach every axes at once.
    /// </summary>
    [Browsable(false)]
    public Colormap? Colormap
    {
        get => _colormap;
        set => SetProperty(ref _colormap, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The transparencies this figure's axes fall back on (MATLAB figure <c>Alphamap</c>), or null
    /// for the even ramp. The same inheritance as <see cref="Colormap"/>, for the same reason.
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<double>? Alphamap
    {
        get => _alphamap;
        set => SetProperty(ref _alphamap, value, InvalidationKind.Render);
    }

    /// <summary>What plotting into this figure again does (MATLAB <c>NextPlot</c>).</summary>
    [Browsable(false)]
    public FigureNextPlot NextPlot
    {
        get => _nextPlot;
        set => SetProperty(ref _nextPlot, value, InvalidationKind.None);
    }

    /// <summary>Whether the window title carries the figure's number (MATLAB <c>NumberTitle</c>).</summary>
    [Browsable(false)]
    public bool NumberTitle
    {
        get => _numberTitle;
        set => SetProperty(ref _numberTitle, value, InvalidationKind.None);
    }

    /// <summary>The file this figure was last saved to or opened from (MATLAB <c>FileName</c>).</summary>
    [Browsable(false)]
    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value ?? string.Empty, InvalidationKind.None);
    }

    /// <summary>
    /// Whether a saved or printed copy gets a white background regardless of the figure's own colour
    /// (MATLAB <c>InvertHardcopy</c>). Off here, where MATLAB has it on, so that what is exported is
    /// what was on screen.
    /// </summary>
    [Browsable(false)]
    public bool InvertHardcopy
    {
        get => _invertHardcopy;
        set => SetProperty(ref _invertHardcopy, value, InvalidationKind.None);
    }

    /// <summary>Whether lines and text are drawn with smoothed edges (MATLAB <c>GraphicsSmoothing</c>).</summary>
    [Browsable(false)]
    public bool GraphicsSmoothing
    {
        get => _graphicsSmoothing;
        set => SetProperty(ref _graphicsSmoothing, value, InvalidationKind.Render);
    }

    /// <summary>Which pointer the window shows over this figure (MATLAB <c>Pointer</c>).</summary>
    [Browsable(false)]
    public PointerShape Pointer
    {
        get => _pointer;
        set => SetProperty(ref _pointer, value, InvalidationKind.None);
    }

    /// <summary>Whether the window may be resized by dragging its edge (MATLAB <c>Resize</c>).</summary>
    [Browsable(false)]
    public bool Resizable
    {
        get => _resizable;
        set => SetProperty(ref _resizable, value, InvalidationKind.None);
    }

    /// <summary>Whether the window shows its toolbar (MATLAB <c>ToolBar</c>).</summary>
    [Browsable(false)]
    public FigureToolBarMode ToolBar
    {
        get => _toolBar;
        set => SetProperty(ref _toolBar, value, InvalidationKind.None);
    }

    /// <summary>How the window is displayed (MATLAB <c>WindowState</c>).</summary>
    [Browsable(false)]
    public FigureWindowState WindowState
    {
        get => _windowState;
        set => SetProperty(ref _windowState, value, InvalidationKind.None);
    }

    /// <summary>
    /// Where the window's drawable area sits on the screen, in pixels from the top-left, once
    /// something has said (see <see cref="PositionSpecified"/>). Until then the window places itself.
    /// </summary>
    [Browsable(false)]
    public Point2D Position
    {
        get => _position;
        set
        {
            _positionSpecified = true;
            SetProperty(ref _position, value, InvalidationKind.None);
        }
    }

    /// <summary>True once something has placed this figure rather than letting the window choose.</summary>
    [Browsable(false)]
    public bool PositionSpecified
    {
        get => _positionSpecified;
        set => SetProperty(ref _positionSpecified, value, InvalidationKind.None);
    }

    /// <summary>The units a script reads and writes the paper properties in (MATLAB <c>PaperUnits</c>).</summary>
    [Browsable(false)]
    public PaperUnitType PaperUnits
    {
        get => _paperUnits;
        set => SetProperty(ref _paperUnits, value, InvalidationKind.None);
    }

    /// <summary>
    /// The name of the page size (MATLAB <c>PaperType</c>), or the custom word once a size was set
    /// that no standard page has. Naming a type releases any size that was set directly.
    /// </summary>
    [Browsable(false)]
    public string PaperType
    {
        get => _paperType;
        set
        {
            string name = value ?? string.Empty;
            if (PaperSizes.Find(name) is null && !string.Equals(name, PaperSizes.CustomName, StringComparison.Ordinal))
            {
                throw new ArgumentOutOfRangeException(nameof(value), name, "Unknown paper type.");
            }

            _paperSize = null;
            SetProperty(ref _paperType, name, InvalidationKind.None);
        }
    }

    /// <summary>The page size in portrait inches when it was set directly, or null to take the type's.</summary>
    [Browsable(false)]
    public Size2D? PaperSize
    {
        get => _paperSize;
        set
        {
            if (value is { } chosen)
            {
                _paperType = PaperSizes.NameOf(chosen) ?? PaperSizes.CustomName;
            }

            SetProperty(ref _paperSize, value, InvalidationKind.None);
        }
    }

    /// <summary>Which way round the page is (MATLAB <c>PaperOrientation</c>).</summary>
    [Browsable(false)]
    public PaperOrientationType PaperOrientation
    {
        get => _paperOrientation;
        set => SetProperty(ref _paperOrientation, value, InvalidationKind.None);
    }

    /// <summary>
    /// Where on the page the figure is printed, in inches from the bottom-left of the page
    /// (MATLAB <c>PaperPosition</c>). Only consulted while <see cref="PaperPositionAuto"/> is false.
    /// </summary>
    [Browsable(false)]
    public Rect2D PaperPosition
    {
        get => _paperPosition;
        set => SetProperty(ref _paperPosition, value, InvalidationKind.None);
    }

    /// <summary>
    /// Whether a printed copy is the size the figure is on screen (MATLAB <c>PaperPositionMode</c>
    /// of automatic) rather than the size <see cref="PaperPosition"/> asks for.
    /// </summary>
    [Browsable(false)]
    public bool PaperPositionAuto
    {
        get => _paperPositionAuto;
        set => SetProperty(ref _paperPositionAuto, value, InvalidationKind.None);
    }

    /// <summary>The page size in inches, the way round the orientation puts it.</summary>
    public Size2D EffectivePaperSize()
    {
        Size2D portrait = _paperSize ?? PaperSizes.Find(_paperType) ?? new Size2D(8.5, 11);
        return _paperOrientation == PaperOrientationType.Landscape
            ? new Size2D(portrait.Height, portrait.Width)
            : portrait;
    }

    /// <summary>The last character typed into this figure (MATLAB <c>CurrentCharacter</c>).</summary>
    /// <remarks>Interaction state: never serialized, silent, and empty until a key is pressed.</remarks>
    [Browsable(false)]
    public string CurrentCharacter { get; set; } = string.Empty;

    /// <summary>Which gesture last selected something here (MATLAB <c>SelectionType</c>).</summary>
    [Browsable(false)]
    public SelectionKind SelectionType { get; set; } = SelectionKind.Normal;

    /// <summary>
    /// Where the pointer last was over this figure, in pixels from the top-left of the canvas, or
    /// null while the pointer has never been over it. The script surface flips it to MATLAB's
    /// bottom-left origin when it is read, and answers the origin for a figure nobody has pointed at.
    /// </summary>
    [Browsable(false)]
    public Point2D? CurrentPointPx { get; set; }

    /// <summary>The fraction of each subplot cell reserved as a gutter, split across its sides.</summary>
    private const double SubplotGutter = 0.12;

    /// <summary>
    /// The tiled layout this figure's tiles are laid in, or null until <c>tiledlayout</c> makes one.
    /// A figure has at most one: MATLAB nests them and this build does not, which is recorded.
    /// </summary>
    [Browsable(false)]
    public TiledLayoutModel? TiledLayout
    {
        get => _tiledLayout;
        set
        {
            _tiledLayout = value;
            Adopt(value);
            Invalidate(InvalidationKind.Layout);
        }
    }

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
