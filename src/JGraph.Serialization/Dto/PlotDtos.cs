using System.Text.Json.Serialization;
using JGraph.Core.Drawing;
using JGraph.Objects;

namespace JGraph.Serialization.Dto;

/// <summary>
/// The serialized form of a plot object. The concrete type is chosen by the <c>type</c> discriminator;
/// common properties live here and per-type data on the derived DTOs. Adding a plot type is a new
/// <see cref="JsonDerivedTypeAttribute"/> line plus a mapper arm.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LinePlotDto), "line")]
[JsonDerivedType(typeof(ScatterPlotDto), "scatter")]
[JsonDerivedType(typeof(BarPlotDto), "bar")]
[JsonDerivedType(typeof(StemPlotDto), "stem")]
[JsonDerivedType(typeof(HistogramPlotDto), "histogram")]
[JsonDerivedType(typeof(ErrorBarPlotDto), "errorbar")]
[JsonDerivedType(typeof(ImagePlotDto), "image")]
[JsonDerivedType(typeof(RgbImagePlotDto), "rgbimage")]
[JsonDerivedType(typeof(SurfacePlotDto), "surface")]
[JsonDerivedType(typeof(ContourPlotDto), "contour")]
[JsonDerivedType(typeof(ConstantLinePlotDto), "constantline")]
[JsonDerivedType(typeof(Line3DPlotDto), "line3d")]
[JsonDerivedType(typeof(Scatter3DPlotDto), "scatter3d")]
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
}

public sealed class LinePlotDto : PlotDto
{
    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1.5;

    public DashStyle DashStyle { get; set; }

    public MarkerType Marker { get; set; }

    public double MarkerSize { get; set; } = 6;

    public Color? MarkerFill { get; set; }
}

public sealed class ScatterPlotDto : PlotDto
{
    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public Color? Color { get; set; }

    public MarkerType Marker { get; set; } = MarkerType.Circle;

    public double MarkerSize { get; set; } = 7;

    public Color? Fill { get; set; }

    public double EdgeWidth { get; set; } = 1.0;
}

public sealed class BarPlotDto : PlotDto
{
    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public Color? FillColor { get; set; }

    public Color? EdgeColor { get; set; }

    public double EdgeWidth { get; set; } = 1.0;

    public double BarWidthFraction { get; set; } = 0.8;

    public double Baseline { get; set; }

    public bool Horizontal { get; set; }
}

public sealed class StemPlotDto : PlotDto
{
    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1.5;

    public double Baseline { get; set; }

    public MarkerType Marker { get; set; } = MarkerType.Circle;

    public double MarkerSize { get; set; } = 6;

    public Color? MarkerFill { get; set; }
}

public sealed class HistogramPlotDto : PlotDto
{
    public double[] Values { get; set; } = Array.Empty<double>();

    public int BinCount { get; set; } = 10;

    public HistogramNormalization Normalization { get; set; }

    public Color? FillColor { get; set; }

    public Color? EdgeColor { get; set; }

    public double EdgeWidth { get; set; } = 1.0;
}

public sealed class ErrorBarPlotDto : PlotDto
{
    public SeriesDto Series { get; set; } = new(Array.Empty<double>(), Array.Empty<double>());

    public double[] ErrorNeg { get; set; } = Array.Empty<double>();

    public double[] ErrorPos { get; set; } = Array.Empty<double>();

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1.5;

    public double CapSize { get; set; } = 6;

    public bool ShowLine { get; set; } = true;

    public MarkerType Marker { get; set; } = MarkerType.Circle;

    public double MarkerSize { get; set; } = 6;

    public Color? MarkerFill { get; set; }
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
    /// A position per vertex for a parametric surface (a sphere, a cylinder). Null on the rectilinear
    /// surfaces every document written before M45 holds, which is what <see cref="X"/>/<see cref="Y"/>
    /// describe on their own.
    /// </summary>
    public double[][]? XGrid { get; set; }

    public double[][]? YGrid { get; set; }

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

    public Color? MarkerFill { get; set; }
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

    public ColormapDto Colormap { get; set; } = new("Parula", Array.Empty<Color>());

    public bool AutoScaleColor { get; set; } = true;

    public double ColorMin { get; set; }

    public double ColorMax { get; set; } = 1;
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
