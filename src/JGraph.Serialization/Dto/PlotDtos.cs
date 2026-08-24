using System.Text.Json.Serialization;
using JGraph.Core.Drawing;
using JGraph.Objects;

namespace JGraph.Serialization.Dto;

/// <summary>
/// The serialized form of a plot object. The concrete type is chosen by the <c>type</c> discriminator;
/// common properties live here and per-type data on the derived DTOs. Adding a plot type is a new
/// <see cref="JsonDerivedTypeAttribute"/> line plus a mapper arm.
/// </summary>
/// <remarks>
/// <b>The marker colours are named for MATLAB and written under their old keys.</b> M86 renamed the
/// model's <c>MarkerFill</c> and <c>MarkerEdge</c> to the names MATLAB documents, and every one of
/// these carries a <see cref="JsonPropertyNameAttribute"/> putting <c>markerFill</c> and
/// <c>markerEdge</c> back on the wire. A saved figure is a file somebody already has: renaming a
/// property in this assembly must not turn a document written yesterday into one that loads with its
/// markers blank, and the format version is not bumped because nothing about the format changed. The
/// pin is tested rather than trusted — a later tidy-up that "corrects" these keys would be a silent
/// data loss with no error anywhere.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LinePlotDto), "line")]
[JsonDerivedType(typeof(ScatterPlotDto), "scatter")]
[JsonDerivedType(typeof(BarPlotDto), "bar")]
[JsonDerivedType(typeof(AreaPlotDto), "area")]
[JsonDerivedType(typeof(PiePlotDto), "pie")]
[JsonDerivedType(typeof(HeatmapPlotDto), "heatmap")]
[JsonDerivedType(typeof(BinScatterPlotDto), "binscatter")]
[JsonDerivedType(typeof(BoxChartPlotDto), "boxchart")]
[JsonDerivedType(typeof(StemPlotDto), "stem")]
[JsonDerivedType(typeof(HistogramPlotDto), "histogram")]
[JsonDerivedType(typeof(PolarHistogramPlotDto), "polarhistogram")]
[JsonDerivedType(typeof(ErrorBarPlotDto), "errorbar")]
[JsonDerivedType(typeof(ImagePlotDto), "image")]
[JsonDerivedType(typeof(RgbImagePlotDto), "rgbimage")]
[JsonDerivedType(typeof(SurfacePlotDto), "surface")]
[JsonDerivedType(typeof(ContourPlotDto), "contour")]
[JsonDerivedType(typeof(ConstantLinePlotDto), "constantline")]
[JsonDerivedType(typeof(Line3DPlotDto), "line3d")]
[JsonDerivedType(typeof(Scatter3DPlotDto), "scatter3d")]
[JsonDerivedType(typeof(Stem3DPlotDto), "stem3d")]
[JsonDerivedType(typeof(Bar3DPlotDto), "bar3d")]
[JsonDerivedType(typeof(Pie3DPlotDto), "pie3d")]
[JsonDerivedType(typeof(PatchPlotDto), "patch")]
[JsonDerivedType(typeof(QuiverPlotDto), "quiver")]
[JsonDerivedType(typeof(PolarGridDto), "polarGrid")]
[JsonDerivedType(typeof(SmithGridDto), "smithGrid")]
[JsonDerivedType(typeof(EyeDiagramPlotDto), "eyeDiagram")]
public abstract class PlotDto
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Visible { get; set; } = true;

    public int ZOrder { get; set; }

    public double Opacity { get; set; } = 1.0;

    public bool HitTestVisible { get; set; } = true;

    public int XAxisIndex { get; set; }

    public int YAxisIndex { get; set; }

    /// <summary>The seat this plot took in the series cycle, or -1 (the pre-M73 default) for none.</summary>
    public int SeriesIndex { get; set; } = -1;

    /// <summary>Whether the object is trimmed to the plot box (M77). Documents before it clipped.</summary>
    public bool Clipping { get; set; } = true;

    /// <summary>Whether the object takes a legend row — MATLAB's Annotation.LegendInformation.</summary>
    public bool ShowsInLegend { get; set; } = true;
}

/// <summary>
/// The serialized form of the line a bar, stem or area stands on (JGraph.Core.Model.BaseLineModel).
/// Its base value lives on the owning chart, which has always carried one, so only what M77 added
/// is written here.
/// </summary>
public sealed class BaseLineDto
{
    public bool Visible { get; set; } = true;

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 0.5;

    public DashStyle LineStyle { get; set; }
}

public sealed class LinePlotDto : PlotDto
{
    /// <summary>Marker outline colour, or null to draw it in the line's own (M77).</summary>
    [JsonPropertyName("markerEdge")]
    public Color? MarkerEdgeColor { get; set; }

    /// <summary>Which samples carry a marker, or null for all of them (M77).</summary>
    public int[]? MarkerIndices { get; set; }

    public LineJoin LineJoin { get; set; } = LineJoin.Miter;

    public bool AlignVertexCenters { get; set; }

    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1.5;

    public DashStyle DashStyle { get; set; }

    /// <summary>Straight, or a stairstep — how <c>stairs</c> survives a save.</summary>
    public StepMode Steps { get; set; }

    public MarkerType Marker { get; set; }

    public double MarkerSize { get; set; } = 6;

    [JsonPropertyName("markerFill")]
    public Color? MarkerFaceColor { get; set; }
}

public sealed class ScatterPlotDto : PlotDto
{
    /// <summary>A transparency per point, or null for one opacity across the cloud (M77).</summary>
    public double[]? AlphaData { get; set; }

    public AlphaMapping AlphaDataMapping { get; set; } = AlphaMapping.Scaled;

    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public Color? Color { get; set; }

    public MarkerType Marker { get; set; } = MarkerType.Circle;

    public double MarkerSize { get; set; } = 7;

    public Color? Fill { get; set; }

    public double EdgeWidth { get; set; } = 1.0;

    /// <summary>Per-point sizes, or null for a uniform <see cref="MarkerSize"/>.</summary>
    public double[]? SizeData { get; set; }

    /// <summary>Per-point colormapped values, or null for a single-colored cloud.</summary>
    public double[]? ColorData { get; set; }

    /// <summary>
    /// Whether <see cref="SizeData"/> is read as bubble values rather than as marker areas — the one
    /// thing telling a saved <c>bubblechart</c> from a saved <c>scatter</c> carrying sizes.
    /// </summary>
    public bool BubbleSizing { get; set; }

    /// <summary>How points sharing an x are spread sideways — what tells a swarm chart from a scatter.</summary>
    public JitterStyle XJitter { get; set; }

    public JitterStyle YJitter { get; set; }

    /// <summary>
    /// The spread widths that were set, zero for the ones left to follow the data — the widths in
    /// force are worked out again on load rather than frozen into the file.
    /// </summary>
    public double XJitterWidth { get; set; }

    public double YJitterWidth { get; set; }

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    public bool AutoScaleColor { get; set; } = true;

    public double ColorMin { get; set; }

    public double ColorMax { get; set; } = 1;
}

public sealed class BarPlotDto : PlotDto
{
    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public Color? FillColor { get; set; }

    public Color? EdgeColor { get; set; }

    public double EdgeWidth { get; set; } = 1.0;

    public double FaceAlpha { get; set; } = 1.0;

    public DashStyle Dash { get; set; }

    public double BarWidthFraction { get; set; } = 0.8;

    public double Baseline { get; set; }

    public bool Horizontal { get; set; }

    public double EdgeAlpha { get; set; } = 1.0;

    /// <summary>One colour per bar, or null for one colour across the series.</summary>
    public Color[]? ColorData { get; set; }

    /// <summary>The line the bars stand on, or null for a document written before M77.</summary>
    public BaseLineDto? BaseLine { get; set; }

    /// <summary>Which series of a grouped chart this is, and how many share the slot.</summary>
    public int GroupIndex { get; set; }

    public int GroupCount { get; set; } = 1;

    /// <summary>How far the slot is shifted, as a fraction of its width (histc's half shift).</summary>
    public double PositionOffset { get; set; }

    /// <summary>The stacking floor, or null for a series standing on its own baseline.</summary>
    public double[]? LowerEdge { get; set; }
}

public sealed class AreaPlotDto : PlotDto
{
    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public Color? FaceColor { get; set; }

    public Color? EdgeColor { get; set; }

    public double FaceAlpha { get; set; } = 1.0;

    public double LineWidth { get; set; } = 1.0;

    public DashStyle Dash { get; set; }

    public double BaseValue { get; set; }

    public bool ShowBaseLine { get; set; } = true;

    public double EdgeAlpha { get; set; } = 1.0;

    public bool AlignVertexCenters { get; set; }

    /// <summary>The line the band stands on, or null for a document written before M77.</summary>
    public BaseLineDto? BaseLine { get; set; }

    /// <summary>The stacking floor, or null for a band standing on its own base value.</summary>
    public double[]? LowerEdge { get; set; }
}

/// <summary>The serialized form of a <see cref="PiePlot"/>.</summary>
public sealed class PiePlotDto : PlotDto
{
    public double[] Values { get; set; } = Array.Empty<double>();

    /// <summary>How far each wedge is pushed out, or null when none is.</summary>
    public double[]? Explode { get; set; }

    /// <summary>What is written beside each wedge, or null for the automatic percentages.</summary>
    public string[]? Labels { get; set; }

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    public Color? EdgeColor { get; set; }

    public double LineWidth { get; set; } = 1.0;

    public double FaceAlpha { get; set; } = 1.0;

    public double StartAngle { get; set; } = 90;

    public bool Clockwise { get; set; }

    public bool ShowLabels { get; set; } = true;

    public double LabelRadius { get; set; } = 1.2;

    public TextStyleDto? LabelStyle { get; set; }

    /// <summary>
    /// How the wedges are drawn as the patch they are (M79). The geometry is not stored — a pie works
    /// it out from its values every time — so what a document has to carry is only what a script chose
    /// about it. Every one of these is additive, so a figure written before M79 loads with the
    /// defaults, which is what it was drawn with.
    /// </summary>
    public PieWedgeDto? Wedges { get; set; }
}

/// <summary>The patch styling of a pie's wedges: everything a script can choose, and no geometry.</summary>
public sealed class PieWedgeDto
{
    public Color? FaceColor { get; set; }

    public bool FaceVisible { get; set; } = true;

    public double EdgeAlpha { get; set; } = 1;

    public DashStyle EdgeDash { get; set; } = DashStyle.Solid;

    public LineJoin LineJoin { get; set; } = LineJoin.Miter;

    public MarkerType Marker { get; set; } = MarkerType.None;

    public double MarkerSize { get; set; } = 6;

    [JsonPropertyName("markerEdge")]
    public Color? MarkerEdgeColor { get; set; }

    [JsonPropertyName("markerFill")]
    public Color? MarkerFaceColor { get; set; }

    public double[]? ColorData { get; set; }

    public double[]? VertexAlpha { get; set; }

    public AlphaMapping AlphaDataMapping { get; set; } = AlphaMapping.Scaled;

    public ColorMapping CDataMapping { get; set; } = ColorMapping.Scaled;

    public SurfaceLighting FaceLighting { get; set; } = SurfaceLighting.Flat;

    public SurfaceLighting EdgeLighting { get; set; } = SurfaceLighting.None;

    public BackFaceLighting BackFaceLighting { get; set; } = BackFaceLighting.ReverseLit;

    public double AmbientStrength { get; set; } = 0.3;

    public double DiffuseStrength { get; set; } = 0.6;

    public double SpecularStrength { get; set; } = 0.9;

    public double SpecularExponent { get; set; } = 10;

    public double SpecularColorReflectance { get; set; } = 1;

    public bool AlignVertexCenters { get; set; }
}

/// <summary>The serialized form of a <see cref="HeatmapPlot"/>.</summary>
public sealed class HeatmapPlotDto : PlotDto
{
    public double[][] ColorData { get; set; } = Array.Empty<double[]>();

    /// <summary>The column names, or null when they are the numbers one upward.</summary>
    public string[]? XData { get; set; }

    /// <summary>The row names, or null when they are the numbers one upward.</summary>
    public string[]? YData { get; set; }

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    /// <summary>The colour limits that were set, or null when they come from the data.</summary>
    public RangeDto? ColorLimits { get; set; }

    public HeatmapScaling ColorScaling { get; set; } = HeatmapScaling.Scaled;

    public bool ShowCellLabels { get; set; } = true;

    /// <summary>The cell label colour, or null when each cell picks one against its own fill.</summary>
    public Color? CellLabelColor { get; set; }

    public string? CellLabelFormat { get; set; }

    public TextStyleDto? CellLabelStyle { get; set; }

    public bool GridVisible { get; set; } = true;

    public Color GridColor { get; set; } = Colors.White;

    public Color MissingDataColor { get; set; } = Color.FromScRgb(0.15, 0.15, 0.15);

    public string MissingDataLabel { get; set; } = "NaN";

    /// <summary>The text written under the columns, when a script has separated it from their names.</summary>
    public string[]? XDisplayLabels { get; set; }

    public string[]? YDisplayLabels { get; set; }
}

/// <summary>
/// The serialized form of a <see cref="BinScatterPlot"/>. The readings are stored and the bins are
/// counted again on load, the same way a histogram keeps its samples rather than its bars — it is
/// the smaller of the two for the sample sizes this chart is for, and it is the only form that lets
/// the bin count be changed after the fact.
/// </summary>
public sealed class BinScatterPlotDto : PlotDto
{
    public double[] XData { get; set; } = Array.Empty<double>();

    public double[] YData { get; set; } = Array.Empty<double>();

    public int NumBinsX { get; set; } = 1;

    public int NumBinsY { get; set; } = 1;

    /// <summary>The span the bins cover across, or null when it comes from the readings.</summary>
    public RangeDto? XLimits { get; set; }

    /// <summary>The span the bins cover up, or null when it comes from the readings.</summary>
    public RangeDto? YLimits { get; set; }

    public bool ShowEmptyBins { get; set; }

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    /// <summary>The colour limits that were set, or null when they come from the counts.</summary>
    public RangeDto? ColorLimits { get; set; }
}

/// <summary>The serialized form of a <see cref="BoxChartPlot"/>.</summary>
public sealed class BoxChartPlotDto : PlotDto
{
    /// <summary>Which group each observation falls in, or null when they are all in one.</summary>
    public double[]? XData { get; set; }

    /// <summary>Every observation — the boxes are worked out again on load rather than stored.</summary>
    public double[] YData { get; set; } = Array.Empty<double>();

    public Color? BoxFaceColor { get; set; }

    public double BoxFaceAlpha { get; set; } = 0.5;

    public Color BoxEdgeColor { get; set; } = Color.FromScRgb(0.15, 0.15, 0.15);

    public Color BoxMedianLineColor { get; set; } = Color.FromScRgb(0.15, 0.15, 0.15);

    public double BoxWidth { get; set; } = 0.5;

    public double LineWidth { get; set; } = 1.0;

    public Color WhiskerLineColor { get; set; } = Color.FromScRgb(0.15, 0.15, 0.15);

    public DashStyle WhiskerLineStyle { get; set; }

    public MarkerType MarkerStyle { get; set; } = MarkerType.Circle;

    public double MarkerSize { get; set; } = 6;

    public Color? MarkerColor { get; set; }

    public bool Notch { get; set; }

    public bool JitterOutliers { get; set; }

    public bool Horizontal { get; set; }
}

public sealed class StemPlotDto : PlotDto
{
    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1.5;

    public double Baseline { get; set; }

    public DashStyle DashStyle { get; set; }

    public MarkerType Marker { get; set; } = MarkerType.Circle;

    public double MarkerSize { get; set; } = 6;

    [JsonPropertyName("markerFill")]
    public Color? MarkerFaceColor { get; set; }

    [JsonPropertyName("markerEdge")]
    public Color? MarkerEdgeColor { get; set; }

    /// <summary>The line the stems stand on, or null for a document written before M77.</summary>
    public BaseLineDto? BaseLine { get; set; }
}

public sealed class HistogramPlotDto : PlotDto
{
    /// <summary>The readings behind the counts, or empty for the counts-only form.</summary>
    public double[] Values { get; set; } = Array.Empty<double>();

    public int BinCount { get; set; } = 10;

    /// <summary>Where the bins fall. Absent in documents written before M77, which carried a count.</summary>
    public double[]? BinEdges { get; set; }

    /// <summary>The counts, needed only when there are no readings to take them from again.</summary>
    public double[]? BinCounts { get; set; }

    public string BinMethod { get; set; } = "auto";

    /// <summary>The chosen span, or null when the bins simply cover the readings.</summary>
    public double[]? BinLimits { get; set; }

    /// <summary>The names counted, when this is a histogram of names.</summary>
    public string[]? Categories { get; set; }

    public CategoryDisplayOrder DisplayOrder { get; set; }

    public int NumDisplayBins { get; set; }

    public bool ShowOthers { get; set; }

    public HistogramNormalization Normalization { get; set; }

    public HistogramDisplayStyle DisplayStyle { get; set; }

    public HistogramOrientation Orientation { get; set; }

    public Color? FillColor { get; set; }

    public Color? EdgeColor { get; set; }

    public double EdgeWidth { get; set; } = 1.0;

    public double FaceAlpha { get; set; } = 1.0;

    public double EdgeAlpha { get; set; } = 1.0;

    public DashStyle LineStyle { get; set; } = DashStyle.Solid;

    public double BarWidth { get; set; } = 1.0;
}

/// <summary>The serialized form of a <see cref="PolarHistogramPlot"/>.</summary>
public sealed class PolarHistogramPlotDto : PlotDto
{
    /// <summary>The angles behind the counts, in radians, or empty for the counts-only form.</summary>
    public double[] Data { get; set; } = Array.Empty<double>();

    public double[] BinEdges { get; set; } = [0, System.Math.Tau];

    /// <summary>
    /// The counts, saved even when there is data behind them: reading them back is cheaper than
    /// counting again, and a file that says what it drew cannot drift from what it drew.
    /// </summary>
    public double[] BinCounts { get; set; } = Array.Empty<double>();

    public HistogramNormalization Normalization { get; set; }

    public PolarHistogramDisplayStyle DisplayStyle { get; set; }

    public Color? FaceColor { get; set; }

    public Color? EdgeColor { get; set; }

    public double FaceAlpha { get; set; } = 1.0;

    public double EdgeAlpha { get; set; } = 1.0;

    public double LineWidth { get; set; } = 0.5;

    public DashStyle LineStyle { get; set; }
}

public sealed class ErrorBarPlotDto : PlotDto
{
    /// <summary>How far each whisker reaches sideways, or null for an upright-only chart (M77).</summary>
    public double[]? ErrorLeft { get; set; }

    public double[]? ErrorRight { get; set; }

    public DashStyle DashStyle { get; set; }

    [JsonPropertyName("markerEdge")]
    public Color? MarkerEdgeColor { get; set; }

    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public double[] ErrorNeg { get; set; } = Array.Empty<double>();

    public double[] ErrorPos { get; set; } = Array.Empty<double>();

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1.5;

    public double CapSize { get; set; } = 6;

    public bool ShowLine { get; set; } = true;

    public MarkerType Marker { get; set; } = MarkerType.Circle;

    public double MarkerSize { get; set; } = 6;

    [JsonPropertyName("markerFill")]
    public Color? MarkerFaceColor { get; set; }
}

public sealed class ImagePlotDto : PlotDto
{
    public double[][] Values { get; set; } = Array.Empty<double[]>();

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    public RangeDto XExtent { get; set; } = new(0, 1);

    public RangeDto YExtent { get; set; } = new(0, 1);

    public bool AutoScaleColor { get; set; } = true;

    public double ColorMin { get; set; }

    public double ColorMax { get; set; } = 1;

    public bool Interpolate { get; set; }

    public bool RowZeroAtTop { get; set; } = true;

    public AlphaMapping AlphaDataMapping { get; set; } = AlphaMapping.Scaled;

    /// <summary>Direct is an image's default: its numbers are usually colour numbers already.</summary>
    public ColorMapping CDataMapping { get; set; } = ColorMapping.Direct;
}

public sealed class RgbImagePlotDto : PlotDto
{
    /// <summary>Base64 of the little-endian 0xAARRGGBB pixel bytes (row-major, row 0 at top).</summary>
    public string PixelsBase64 { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    public RangeDto XExtent { get; set; } = new(0, 1);

    public RangeDto YExtent { get; set; } = new(0, 1);

    public bool Interpolate { get; set; }
}

public sealed class SurfacePlotDto : PlotDto
{
    public double[] X { get; set; } = Array.Empty<double>();

    public double[] Y { get; set; } = Array.Empty<double>();

    public double[][] Z { get; set; } = Array.Empty<double[]>();

    /// <summary>
    /// The explicit colour grid, when the surface has one. Null — the common case, and every
    /// document written before M70 — means the colour comes from Z, which is what those documents
    /// meant. Adding it needs no format bump for that reason: an absent key reads as the old
    /// behaviour rather than as a missing one.
    /// </summary>
    public double[][]? CData { get; set; }

    /// <summary>
    /// A position per vertex for a parametric surface (a sphere, a cylinder). Null on the rectilinear
    /// surfaces every document written before M45 holds, which is what <see cref="X"/>/<see cref="Y"/>
    /// describe on their own.
    /// </summary>
    public double[][]? XGrid { get; set; }

    public double[][]? YGrid { get; set; }

    /// <summary>
    /// A transparency per grid point, looked up in the axes' alphamap. Null — every document written
    /// before M74 — means the surface is uniformly transparent, which is what those documents meant.
    /// </summary>
    public double[][]? AlphaData { get; set; }

    /// <summary>Whether the faces take their alpha from that grid rather than from FaceAlpha.</summary>
    public bool FaceAlphaFlat { get; set; }

    public SurfaceMeshStyle MeshStyle { get; set; } = SurfaceMeshStyle.Both;

    public DashStyle EdgeDash { get; set; } = DashStyle.Solid;

    public MarkerType Marker { get; set; } = MarkerType.None;

    public double MarkerSize { get; set; } = 6;

    [JsonPropertyName("markerEdge")]
    public Color? MarkerEdgeColor { get; set; }

    [JsonPropertyName("markerFill")]
    public Color? MarkerFaceColor { get; set; }

    public AlphaMapping AlphaDataMapping { get; set; } = AlphaMapping.Scaled;

    public ColorMapping CDataMapping { get; set; } = ColorMapping.Scaled;

    public SurfaceLighting EdgeLighting { get; set; } = SurfaceLighting.None;

    public BackFaceLighting BackFaceLighting { get; set; } = BackFaceLighting.ReverseLit;

    public bool AlignVertexCenters { get; set; }

    /// <summary>Whether the positions were counted out from the grid rather than given.</summary>
    public bool XImplied { get; set; }

    public bool YImplied { get; set; }

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    public SurfaceStyle Style { get; set; } = SurfaceStyle.FilledWithWireframe;

    public SurfaceShading Shading { get; set; } = SurfaceShading.Flat;

    public bool ShowContourBelow { get; set; }

    public int ContourLevels { get; set; } = 8;

    /// <summary>One colour for every face instead of the colormap; null on every figure before M54.</summary>
    public Color? FaceColor { get; set; }

    public Color? EdgeColor { get; set; }

    public double EdgeWidth { get; set; } = 0.75;

    public bool AutoScaleColor { get; set; } = true;

    public double ColorMin { get; set; }

    public double ColorMax { get; set; } = 1;

    /// <summary>
    /// The lighting properties. Their defaults are what a surface has always had, so a document
    /// written before lighting existed reads back unchanged — and unlit, since the axes has no lights.
    /// </summary>
    public SurfaceLighting FaceLighting { get; set; } = SurfaceLighting.Flat;

    public double AmbientStrength { get; set; } = 0.3;

    public double DiffuseStrength { get; set; } = 0.6;

    public double SpecularStrength { get; set; } = 0.9;

    public double SpecularExponent { get; set; } = 10;

    public double SpecularColorReflectance { get; set; } = 1;
}

public sealed class ContourPlotDto : PlotDto
{
    public double[] X { get; set; } = Array.Empty<double>();

    public double[] Y { get; set; } = Array.Empty<double>();

    public double[][] Z { get; set; } = Array.Empty<double[]>();

    public double[]? Levels { get; set; }

    public int LevelCount { get; set; } = 8;

    public bool Filled { get; set; }

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    public double LineWidth { get; set; } = 1.5;

    public bool AutoScaleColor { get; set; } = true;

    public double ColorMin { get; set; }

    public double ColorMax { get; set; } = 1;

    public bool ShowText { get; set; }

    public double[]? LabelLevels { get; set; }

    public TextStyleDto? LabelStyle { get; set; }

    /// <summary>One ink for every curve, or null to colour each by its own level.</summary>
    public Color? LineColor { get; set; }

    public DashStyle LineDash { get; set; } = DashStyle.Solid;

    public double? LevelStep { get; set; }

    public double? TextStep { get; set; }

    public double LabelSpacing { get; set; } = 144;

    public bool ContoursAtZero { get; set; }

    public bool XImplied { get; set; }

    public bool YImplied { get; set; }
}

/// <summary>The serialized form of a <see cref="ConstantLinePlot"/> — an xline or a yline.</summary>
public sealed class ConstantLinePlotDto : PlotDto
{
    public ConstantLineDirection Direction { get; set; }

    public double Value { get; set; }

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1;

    public DashStyle Dash { get; set; } = DashStyle.Dash;

    public string Label { get; set; } = string.Empty;

    public TextStyleDto? LabelStyle { get; set; }

    public HorizontalAlignment LabelHorizontalAlignment { get; set; } = HorizontalAlignment.Right;

    public VerticalAlignment LabelVerticalAlignment { get; set; } = VerticalAlignment.Top;

    /// <summary>Which way the label reads (M79). Additive: an older document has none and reads aligned.</summary>
    public ConstantLineLabelOrientation LabelOrientation { get; set; } = ConstantLineLabelOrientation.Aligned;
}

/// <summary>The serialized form of a <see cref="Line3DPlot"/>.</summary>
public sealed class Line3DPlotDto : PlotDto
{
    public double[] X { get; set; } = Array.Empty<double>();

    public double[] Y { get; set; } = Array.Empty<double>();

    public double[] Z { get; set; } = Array.Empty<double>();

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1.5;

    public DashStyle DashStyle { get; set; }

    public MarkerType Marker { get; set; }

    public double MarkerSize { get; set; } = 6;

    [JsonPropertyName("markerFill")]
    public Color? MarkerFaceColor { get; set; }

    /// <summary>Marker outline colour, or null to draw it in the line's own (M86).</summary>
    [JsonPropertyName("markerEdge")]
    public Color? MarkerEdgeColor { get; set; }
}

/// <summary>The serialized form of a <see cref="Scatter3DPlot"/>.</summary>
public sealed class Scatter3DPlotDto : PlotDto
{
    public double[] X { get; set; } = Array.Empty<double>();

    public double[] Y { get; set; } = Array.Empty<double>();

    public double[] Z { get; set; } = Array.Empty<double>();

    /// <summary>Per-point marker area, or null for a uniform <see cref="MarkerSize"/>.</summary>
    public double[]? SizeData { get; set; }

    /// <summary>Per-point colormapped values, or null for a single-colored cloud.</summary>
    public double[]? ColorData { get; set; }

    public Color? Color { get; set; }

    public MarkerType Marker { get; set; } = MarkerType.Circle;

    public double MarkerSize { get; set; } = 7;

    public bool Filled { get; set; }

    public double EdgeWidth { get; set; } = 1.0;

    /// <summary>Whether the sizes are bubble values rather than marker areas (<c>bubblechart3</c>).</summary>
    public bool BubbleSizing { get; set; }

    /// <summary>How points sharing a coordinate are spread along it (<c>swarmchart3</c>).</summary>
    public JitterStyle XJitter { get; set; }

    public JitterStyle YJitter { get; set; }

    public JitterStyle ZJitter { get; set; }

    /// <summary>The spread widths that were set, zero for the ones still following the data.</summary>
    public double XJitterWidth { get; set; }

    public double YJitterWidth { get; set; }

    public double ZJitterWidth { get; set; }

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    public bool AutoScaleColor { get; set; } = true;

    public double ColorMin { get; set; }

    public double ColorMax { get; set; } = 1;
}

/// <summary>The serialized form of a <see cref="Stem3DPlot"/>.</summary>
public sealed class Stem3DPlotDto : PlotDto
{
    public double[] X { get; set; } = Array.Empty<double>();

    public double[] Y { get; set; } = Array.Empty<double>();

    public double[] Z { get; set; } = Array.Empty<double>();

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1.5;

    public DashStyle Dash { get; set; }

    public double Baseline { get; set; }

    public DashStyle DashStyle { get; set; }

    public MarkerType Marker { get; set; } = MarkerType.Circle;

    public double MarkerSize { get; set; } = 6;

    [JsonPropertyName("markerFill")]
    public Color? MarkerFaceColor { get; set; }

    [JsonPropertyName("markerEdge")]
    public Color? MarkerEdgeColor { get; set; }

    /// <summary>The line the stems stand on, or null for a document written before M77.</summary>
    public BaseLineDto? BaseLine { get; set; }
}

/// <summary>The serialized form of a <see cref="Bar3DPlot"/>.</summary>
public sealed class Bar3DPlotDto : PlotDto
{
    public double[][] ZData { get; set; } = Array.Empty<double[]>();

    /// <summary>Where each row sits, or null when the rows are the counting numbers.</summary>
    public double[]? RowPositions { get; set; }

    public Bar3DStyle Style { get; set; }

    public bool Horizontal { get; set; }

    public double BarWidth { get; set; } = 0.8;

    public double Baseline { get; set; }

    public Color? FaceColor { get; set; }

    /// <summary>The edge colour; null reads as the default black, as it does for a patch.</summary>
    public Color? EdgeColor { get; set; }

    /// <summary>Whether the boxes are outlined at all, which is what records an edge turned off.</summary>
    public bool EdgeVisible { get; set; } = true;

    public double LineWidth { get; set; } = 0.5;

    public double FaceAlpha { get; set; } = 1.0;

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());
}

/// <summary>The serialized form of a <see cref="Pie3DPlot"/>.</summary>
public sealed class Pie3DPlotDto : PlotDto
{
    public double[] Values { get; set; } = Array.Empty<double>();

    /// <summary>How far each wedge is pushed out, or null when none is.</summary>
    public double[]? Explode { get; set; }

    /// <summary>What is written beside each wedge, or null for the automatic percentages.</summary>
    public string[]? Labels { get; set; }

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    /// <summary>The edge colour; null reads as the white outline a pie carries by default.</summary>
    public Color? EdgeColor { get; set; }

    /// <summary>Whether the faces are outlined at all, which is what records an edge turned off.</summary>
    public bool EdgeVisible { get; set; } = true;

    public double LineWidth { get; set; } = 1.0;

    public double FaceAlpha { get; set; } = 1.0;

    public double StartAngle { get; set; } = 90;

    public bool Clockwise { get; set; }

    public double Height { get; set; } = 0.3;

    public bool ShowLabels { get; set; } = true;

    public double LabelRadius { get; set; } = 1.2;

    public TextStyleDto? LabelStyle { get; set; }
}

/// <summary>The serialized form of a <see cref="PatchPlot"/>.</summary>
public sealed class PatchPlotDto : PlotDto
{
    public double[] X { get; set; } = Array.Empty<double>();

    public double[] Y { get; set; } = Array.Empty<double>();

    public double[] Z { get; set; } = Array.Empty<double>();

    /// <summary>The vertex indices of each face.</summary>
    public int[][] Faces { get; set; } = Array.Empty<int[]>();

    /// <summary>Per-face or per-vertex colormapped values, or null for a single-colored patch.</summary>
    public double[]? ColorData { get; set; }

    public Color? FaceColor { get; set; }

    /// <summary>Whether the faces are filled at all — false is MATLAB's <c>'FaceColor', 'none'</c>.</summary>
    public bool FaceVisible { get; set; } = true;

    /// <summary>
    /// The outline color. Null means the black outline a patch carries by default: this format omits
    /// null properties, so "absent" and "default" have to read the same. An outline that was
    /// explicitly turned off is recorded by <see cref="EdgeVisible"/> instead.
    /// </summary>
    public Color? EdgeColor { get; set; }

    /// <summary>Whether the patch is outlined at all — false is MATLAB's <c>'EdgeColor', 'none'</c>.</summary>
    public bool EdgeVisible { get; set; } = true;

    public double EdgeWidth { get; set; } = 0.75;

    public PatchShading Shading { get; set; }

    public DashStyle EdgeDash { get; set; } = DashStyle.Solid;

    public LineJoin LineJoin { get; set; } = LineJoin.Miter;

    public MarkerType Marker { get; set; } = MarkerType.None;

    public double MarkerSize { get; set; } = 6;

    [JsonPropertyName("markerEdge")]
    public Color? MarkerEdgeColor { get; set; }

    [JsonPropertyName("markerFill")]
    public Color? MarkerFaceColor { get; set; }

    /// <summary>A transparency per vertex, or null for one opacity across the patch.</summary>
    public double[]? VertexAlpha { get; set; }

    public AlphaMapping AlphaDataMapping { get; set; } = AlphaMapping.Scaled;

    public ColorMapping CDataMapping { get; set; } = ColorMapping.Scaled;

    public SurfaceLighting EdgeLighting { get; set; } = SurfaceLighting.None;

    public BackFaceLighting BackFaceLighting { get; set; } = BackFaceLighting.ReverseLit;

    public bool AlignVertexCenters { get; set; }

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    public bool AutoScaleColor { get; set; } = true;

    public double ColorMin { get; set; }

    public double ColorMax { get; set; } = 1;
}

/// <summary>A field of arrows (MATLAB <c>quiver</c>/<c>quiver3</c>).</summary>
public sealed class QuiverPlotDto : PlotDto
{
    public double[] X { get; set; } = Array.Empty<double>();

    public double[] Y { get; set; } = Array.Empty<double>();

    public double[] Z { get; set; } = Array.Empty<double>();

    public double[] U { get; set; } = Array.Empty<double>();

    public double[] V { get; set; } = Array.Empty<double>();

    public double[] W { get; set; } = Array.Empty<double>();

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1;

    public bool AutoScale { get; set; } = true;

    public double AutoScaleFactor { get; set; } = 0.9;

    public double Scale { get; set; } = 1;

    public bool ShowArrowHead { get; set; } = true;

    public double MaxHeadSize { get; set; } = 0.2;

    public DashStyle LineDash { get; set; } = DashStyle.Solid;

    public bool LineStyleManual { get; set; }

    public MarkerType Marker { get; set; } = MarkerType.None;

    public bool MarkerManual { get; set; }

    public double MarkerSize { get; set; } = 6;

    [JsonPropertyName("markerEdge")]
    public Color? MarkerEdgeColor { get; set; }

    [JsonPropertyName("markerFill")]
    public Color? MarkerFaceColor { get; set; }

    public bool AlignVertexCenters { get; set; }

    public bool XImplied { get; set; }

    public bool YImplied { get; set; }
}

public sealed class PolarGridDto : PlotDto
{
    public double MaxRadius { get; set; } = 1;

    public int RadialDivisions { get; set; } = 5;

    public int AngularDivisions { get; set; } = 12;

    public Color GridColor { get; set; }

    public TextStyleDto? LabelStyle { get; set; }

    public bool ShowLabels { get; set; } = true;
}

public sealed class SmithGridDto : PlotDto
{
    public Color GridColor { get; set; }

    public TextStyleDto? LabelStyle { get; set; }

    public bool ShowLabels { get; set; } = true;
}

public sealed class EyeDiagramPlotDto : PlotDto
{
    public double[] Signal { get; set; } = Array.Empty<double>();

    public int SamplesPerSymbol { get; set; } = 1;

    public int SymbolsPerTrace { get; set; } = 2;

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1.0;
}
