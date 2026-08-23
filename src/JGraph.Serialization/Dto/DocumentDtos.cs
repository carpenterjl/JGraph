using JGraph.Core.Drawing;
using JGraph.Core.Model;

namespace JGraph.Serialization.Dto;

/// <summary>The root of a ".graph" document: a format tag, a schema version, and the figure.</summary>
public sealed class DocumentDto
{
    public string Format { get; set; } = "jgraph";

    public int FormatVersion { get; set; }

    public FigureDto Figure { get; set; } = new();
}

/// <summary>The serialized form of a <see cref="FigureModel"/>.</summary>
public sealed class FigureDto
{
    public string Name { get; set; } = string.Empty;

    public Color Background { get; set; }

    public SizeDto Size { get; set; } = new(640, 480);

    public string Title { get; set; } = string.Empty;

    public TextStyleDto? TitleStyle { get; set; }

    public List<AxesDto> Axes { get; set; } = new();

    public List<AnnotationDto> Annotations { get; set; } = new();

    /// <summary>
    /// The tiled layout this figure's tiles are laid in, or null when it has none (M80). Additive:
    /// a document written before M80 has no layout and loads as the plain figure it was.
    /// </summary>
    public TiledLayoutDto? TiledLayout { get; set; }

    /// <summary>
    /// Script-defined right-click menus (MATLAB <c>uicontextmenu</c>). Empty in every document
    /// written before M71, which is what makes this safe to add without a version bump. The menu
    /// structure is saved; the callbacks are script-side state and are not.
    /// </summary>
    public List<ContextMenuDto> ContextMenus { get; set; } = new();

    /// <summary>Everything below is null or defaulted in documents written before M75.</summary>
    public ColormapDto? Colormap { get; set; }

    public double[]? Alphamap { get; set; }

    /// <summary>Null means 'add', which is what every figure written before M75 was.</summary>
    public string? NextPlot { get; set; }

    public bool NumberTitle { get; set; } = true;

    public string FileName { get; set; } = string.Empty;

    public bool InvertHardcopy { get; set; }

    public bool GraphicsSmoothing { get; set; } = true;

    public string? Pointer { get; set; }

    public bool Resizable { get; set; } = true;

    public string? ToolBar { get; set; }

    public string? WindowState { get; set; }

    /// <summary>Where the window was placed, or null while nothing has placed it.</summary>
    public PointDto? Position { get; set; }

    public string? PaperUnits { get; set; }

    public string PaperType { get; set; } = "usletter";

    /// <summary>The portrait page size in inches when one was set directly, else null.</summary>
    public SizeDto? PaperSize { get; set; }

    public string? PaperOrientation { get; set; }

    /// <summary>The print rectangle in inches. Only consulted while the mode is manual.</summary>
    public RectDto PaperPosition { get; set; } = new(0.25, 2.5, 8, 6);

    public bool PaperPositionAuto { get; set; } = true;
}

/// <summary>The serialized form of a <see cref="ContextMenuModel"/>.</summary>
public sealed class ContextMenuDto
{
    public string Name { get; set; } = string.Empty;

    public List<MenuItemDto> Items { get; set; } = new();
}

/// <summary>The serialized form of a <see cref="MenuItemModel"/>.</summary>
public sealed class MenuItemDto
{
    public string Text { get; set; } = string.Empty;

    public bool Checked { get; set; }

    public bool Enable { get; set; } = true;

    public bool Separator { get; set; }

    public string Accelerator { get; set; } = string.Empty;

    public string Tooltip { get; set; } = string.Empty;

    public Color ForegroundColor { get; set; }

    public List<MenuItemDto> Items { get; set; } = new();
}

/// <summary>
/// The serialized form of a <see cref="TiledLayoutModel"/> (M80). The tiles
/// themselves are the figure's axes, each carrying the cell it holds, so what is stored here is the
/// grid and the words written over it — never the placements, which are worked out from both.
/// </summary>
public sealed class TiledLayoutDto
{
    public int Rows { get; set; } = 1;

    public int Columns { get; set; } = 1;

    public bool Flow { get; set; }

    public TileSpacingMode TileSpacing { get; set; } = TileSpacingMode.Loose;

    public TilePaddingMode Padding { get; set; } = TilePaddingMode.Loose;

    public TileIndexingMode TileIndexing { get; set; } = TileIndexingMode.RowMajor;

    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public string XLabel { get; set; } = string.Empty;

    public string YLabel { get; set; } = string.Empty;

    public TextStyleDto? TitleStyle { get; set; }

    public TextStyleDto? SubtitleStyle { get; set; }

    public TextStyleDto? XLabelStyle { get; set; }

    public TextStyleDto? YLabelStyle { get; set; }

    public RectDto Bounds { get; set; } = new(0, 0, 1, 1);

    public bool Visible { get; set; } = true;
}

/// <summary>The serialized form of an <see cref="AxesModel"/>.</summary>
public sealed class AxesDto
{
    public string Name { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public TextStyleDto? TitleStyle { get; set; }

    public string Subtitle { get; set; } = string.Empty;

    public TextStyleDto? SubtitleStyle { get; set; }

    public Color Background { get; set; }

    public RectDto NormalizedBounds { get; set; } = new(0, 0, 1, 1);

    /// <summary>Which cell of the figure's tiled layout this axes holds, or null when it is in none.</summary>
    public int? LayoutTile { get; set; }

    /// <summary>How many rows of that grid the tile covers.</summary>
    public int LayoutRowSpan { get; set; } = 1;

    /// <inheritdoc cref="LayoutRowSpan" />
    public int LayoutColumnSpan { get; set; } = 1;

    /// <summary>
    /// Whether the hovering toolbar is shown over this axes (M80). Only the switch is stored: the
    /// buttons are the default set unless a script asked for others, and the callbacks a script
    /// gave them are never serialized — no callback in this build ever is.
    /// </summary>
    public bool ToolbarVisible { get; set; } = true;

    public double AutoScalePadding { get; set; }

    public bool EqualAspect { get; set; }

    public bool FrameVisible { get; set; } = true;

    public bool Visible { get; set; } = true;

    public bool Is3D { get; set; }

    public double Azimuth { get; set; } = -37.5;

    public double Elevation { get; set; } = 30;

    /// <summary>The camera roll in degrees. Zero on every figure written before M54.</summary>
    public double Roll { get; set; }

    /// <summary>
    /// The 3D plot box's relative side lengths (MATLAB <c>pbaspect</c>). The default cube is what
    /// every document written before M45 holds.
    /// </summary>
    public Point3Dto PlotBoxAspect { get; set; } = new(1, 1, 1);

    /// <summary>The Z axis of a 3D axes; null in documents written before format version 2.</summary>
    public AxisDto? ZAxis { get; set; }

    /// <summary>
    /// Whether this axes is a circle. False in every document written before M56, which is what makes
    /// the whole polar block below safe to add without a version bump.
    /// </summary>
    public bool IsPolar { get; set; }

    /// <summary>Where θ = 0 sits, named rather than numbered so the file stays readable.</summary>
    public string ThetaZeroLocation { get; set; } = "Right";

    /// <summary>Which way θ grows.</summary>
    public string ThetaDirection { get; set; } = "CounterClockwise";

    /// <summary>The unit angles cross the script boundary in.</summary>
    public string ThetaAxisUnits { get; set; } = "Degrees";

    /// <summary>The angle in degrees the r tick labels are written along.</summary>
    public double RAxisLocation { get; set; } = 80;

    /// <summary>Whether that angle was chosen rather than left at the default one.</summary>
    public bool RAxisLocationManual { get; set; }

    /// <summary>The radial ruler; null in documents written before M56.</summary>
    public AxisDto? RAxis { get; set; }

    /// <summary>The angular ruler, in degrees; null in documents written before M56.</summary>
    public AxisDto? ThetaAxis { get; set; }

    /// <summary>The colorbar; null in documents written before format version 2.</summary>
    public ColorbarDto? Colorbar { get; set; }

    /// <summary>The bubble legend; null in every document written before format version 6.</summary>
    public BubbleLegendDto? BubbleLegend { get; set; }

    /// <summary>
    /// The smallest and largest bubble diameter in points (MATLAB <c>bubblesize</c>). Written as a
    /// pair so a document saved before bubbles existed reads back as the default range.
    /// </summary>
    public double BubbleSizeMin { get; set; } = 6;

    public double BubbleSizeMax { get; set; } = 40;

    /// <summary>
    /// The fixed bubble value limits (MATLAB <c>bubblelim</c>), or null — the usual case — to take
    /// them from the data on load, which is what keeps a reloaded chart scaled as it was drawn.
    /// </summary>
    public double[]? BubbleSizeLimits { get; set; }

    /// <summary>
    /// The lights on this axes. Empty in every document written before lighting existed, which reads
    /// back as the unlit surface those documents were saved with.
    /// </summary>
    public List<LightDto> Lights { get; set; } = new();

    /// <summary>
    /// The per-axes color cycle (MATLAB <c>colororder</c>). Null — the default, and what every
    /// document written before M45 holds — leaves the theme in charge.
    /// </summary>
    public List<Color>? ColorOrder { get; set; }

    public List<AxisDto> XAxes { get; set; } = new();

    public List<AxisDto> YAxes { get; set; } = new();

    public GridDto Grid { get; set; } = new();

    /// <summary>Everything below is null or defaulted in documents written before M73.</summary>
    public string? Layer { get; set; }

    public double LineWidth { get; set; } = 1.0;

    public string? BoxStyle { get; set; }

    public Color? AmbientLightColor { get; set; }

    public double TitleFontSizeMultiplier { get; set; } = 1.1;

    public double LabelFontSizeMultiplier { get; set; } = 1.1;

    public string? TitleHorizontalAlignment { get; set; }

    public string? ColorScale { get; set; }

    /// <summary>The axes-level colormap new color-mapped plots are seeded from, or null for automatic.</summary>
    public ColormapDto? Colormap { get; set; }

    /// <summary>The axes-level color limits as a pair, or null when each plot scales itself.</summary>
    public double[]? ColorLimits { get; set; }

    /// <summary>The data aspect ratio (MATLAB <c>daspect</c>), or null to fit freely.</summary>
    public Point3Dto? DataAspectRatio { get; set; }

    /// <summary>The line-style cycle, or null for the default single solid entry.</summary>
    public List<SeriesLineStyleDto>? LineStyleOrder { get; set; }

    /// <summary>Everything below is null or defaulted in documents written before M74.</summary>
    public Point3Dto? CameraPosition { get; set; }

    public Point3Dto? CameraTarget { get; set; }

    public Point3Dto? CameraUpVector { get; set; }

    public double? CameraViewAngle { get; set; }

    /// <summary>Null means orthographic, the projection every 3D axes drew with before M74.</summary>
    public string? Projection { get; set; }

    /// <summary>Null means depth, which is how every 3D object has always ordered its faces.</summary>
    public string? SortMethod { get; set; }

    public bool Clipping { get; set; } = true;

    /// <summary>The alpha limits as a pair, or null while each plot spreads its own alpha data.</summary>
    public double[]? AlphaLimits { get; set; }

    /// <summary>The alphamap, or null for the even ramp from clear to opaque.</summary>
    public double[]? Alphamap { get; set; }

    public string? AlphaScale { get; set; }

    /// <summary>
    /// The plot box a script pinned, in the same downward-Y fractions as the bounds, or null while
    /// the cell is what was asked for. Null in every document written before M75.
    /// </summary>
    public RectDto? InnerTarget { get; set; }

    /// <summary>Null means the outer rectangle is what a placement fixes, as it always was.</summary>
    public string? PositionConstraint { get; set; }

    public LegendDto Legend { get; set; } = new();

    public List<PlotDto> Plots { get; set; } = new();

    public List<AnnotationDto> Annotations { get; set; } = new();
}

/// <summary>The serialized form of an <see cref="AxisModel"/>.</summary>
public sealed class AxisDto
{
    public AxisOrientation Orientation { get; set; }

    public AxisPosition Position { get; set; }

    public AxisScaleType Scale { get; set; }

    public RangeDto Range { get; set; } = new(0, 1);

    public bool AutoScale { get; set; } = true;

    public bool Inverted { get; set; }

    public string Label { get; set; } = string.Empty;

    public bool ShowMajorTicks { get; set; } = true;

    public bool ShowMinorTicks { get; set; }

    public bool ShowTickLabels { get; set; } = true;

    public int TargetMajorTickCount { get; set; } = 5;

    public string? TickLabelFormat { get; set; }

    public string[]? Categories { get; set; }

    /// <summary>Manual tick placement, or null for automatic. Defaulted, so v5 documents still load.</summary>
    public double[]? TickPositions { get; set; }

    /// <summary>Manual tick label text, or null to label each tick with its value.</summary>
    public string[]? TickLabelOverrides { get; set; }

    public double TickLabelAngle { get; set; }

    public TextStyleDto? LabelStyle { get; set; }

    public TextStyleDto? TickLabelStyle { get; set; }

    /// <summary>Which side of the axis line the ticks grow from, or null for automatic (outward).</summary>
    public string? TickDirection { get; set; }

    /// <summary>The [2D 3D] tick length fractions (MATLAB <c>TickLength</c>), or null for automatic.</summary>
    public double[]? TickLength { get; set; }

    /// <summary>The ruler's own ink (MATLAB <c>XColor</c>/<c>YColor</c>), or null for the theme's.</summary>
    public Color? RulerColor { get; set; }

    /// <summary>How auto-scaling fits the limits; null in every document written before M73.</summary>
    public string? LimitMethod { get; set; }
}

/// <summary>The serialized form of a <see cref="GridModel"/>.</summary>
public sealed class GridDto
{
    public bool Visible { get; set; } = true;

    public bool ShowMajor { get; set; } = true;

    public bool ShowMinor { get; set; }

    public LineStyleDto? MajorLineStyle { get; set; }

    public LineStyleDto? MinorLineStyle { get; set; }

    /// <summary>
    /// Per-direction visibility (MATLAB <c>XGrid</c>/<c>YGrid</c>/<c>ZGrid</c> and the minor
    /// three). Null — every document written before M73 — defers to the aggregates above.
    /// </summary>
    public bool? ShowMajorX { get; set; }

    public bool? ShowMajorY { get; set; }

    public bool? ShowMajorZ { get; set; }

    public bool? ShowMajorR { get; set; }

    public bool? ShowMajorTheta { get; set; }

    public bool? ShowMinorR { get; set; }

    public bool? ShowMinorTheta { get; set; }

    public bool? ShowMinorX { get; set; }

    public bool? ShowMinorY { get; set; }

    public bool? ShowMinorZ { get; set; }

    /// <summary>Whether a script chose the grid colors/alphas, so a theme pass leaves them alone.</summary>
    public bool MajorColorManual { get; set; }

    public bool MinorColorManual { get; set; }

    public bool MajorAlphaManual { get; set; }

    public bool MinorAlphaManual { get; set; }
}

/// <summary>The serialized form of a <see cref="ColorbarModel"/>.</summary>
public sealed class ColorbarDto
{
    public bool Visible { get; set; }

    public double Width { get; set; } = 18;

    public string? Label { get; set; }

    public TextStyleDto? TickLabelStyle { get; set; }

    public ColorbarLocation Location { get; set; } = ColorbarLocation.EastOutside;

    public RectDto? FigureBox { get; set; }

    public RangeDto? Limits { get; set; }

    public double[]? TickValues { get; set; }

    public string[]? TickLabelOverrides { get; set; }

    public TickDirection TickDirection { get; set; } = TickDirection.Out;

    public double TickLength { get; set; } = 0.01;

    public bool Inverted { get; set; }

    public bool LabelsInside { get; set; }

    public bool BoxVisible { get; set; } = true;

    public Color? Ink { get; set; }

    public double LineWidth { get; set; } = 0.5;
}

/// <summary>The serialized form of a <see cref="BubbleLegendModel"/>.</summary>
public sealed class BubbleLegendDto
{
    public bool Visible { get; set; }

    public LegendPosition Position { get; set; }

    public BubbleLegendStyle Style { get; set; }

    public int NumBubbles { get; set; } = 3;

    public bool LimitLabels { get; set; }

    public Color Background { get; set; }

    public Color BorderColor { get; set; }

    public bool ShowBorder { get; set; } = true;

    public TextStyleDto? TextStyle { get; set; }

    public string? Title { get; set; }

    /// <summary>The custom placement, as a fraction of the plot area. Used only when <see cref="Position"/> is Custom.</summary>
    public double LocationX { get; set; } = 0.75;

    public double LocationY { get; set; } = 0.05;

    public double BorderWidth { get; set; } = 1;

    /// <summary>Whether the biggest bubble is listed first.</summary>
    public bool Descending { get; set; }

    public RectDto? FigureBox { get; set; }
}

/// <summary>The serialized form of a <see cref="LightModel"/>.</summary>
public sealed class LightDto
{
    public string Name { get; set; } = string.Empty;

    public bool Visible { get; set; } = true;

    public LightStyle Style { get; set; }

    /// <summary>The direction or position, in the projection's normalized cube space.</summary>
    public Point3Dto Position { get; set; } = new(1, 0, 1);

    public Color Color { get; set; }

    public bool FollowsCamera { get; set; }
}

/// <summary>The serialized form of a <see cref="LegendModel"/>.</summary>
public sealed class LegendDto
{
    public bool Visible { get; set; }

    public LegendPosition Position { get; set; }

    public Color Background { get; set; }

    public Color BorderColor { get; set; }

    public bool ShowBorder { get; set; } = true;

    public TextStyleDto? TextStyle { get; set; }

    public string? Title { get; set; }

    /// <summary>The custom placement, as a fraction of the plot area. Used only when <see cref="Position"/> is Custom.</summary>
    public double BorderWidth { get; set; } = 1;

    public LegendOrientation Orientation { get; set; } = LegendOrientation.Vertical;

    public int? Columns { get; set; }

    public bool AutoUpdate { get; set; } = true;

    public RectDto? FigureBox { get; set; }

    public double LocationX { get; set; } = 0.6;

    public double LocationY { get; set; } = 0.05;

    /// <summary>
    /// The legend rows. Absent in documents written before legends had editable rows; the renderer's
    /// sync pass rebuilds them from the plots on first paint, which is the pre-M26 behavior.
    /// </summary>
    public List<LegendEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// The serialized form of a <see cref="LegendEntryModel"/>. The series is referenced by its index
/// within the owning axes' plot list rather than by id: plots carry no id in this format, and the
/// index is stable for the lifetime of a document. An index that no longer resolves is dropped, and
/// the sync pass re-creates a default row for that plot.
/// </summary>
public sealed class LegendEntryDto
{
    public int PlotIndex { get; set; }

    public string? Label { get; set; }

    public bool Visible { get; set; } = true;
}
