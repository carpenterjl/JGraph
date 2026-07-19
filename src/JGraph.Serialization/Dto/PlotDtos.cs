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

    public ColormapDto Colormap { get; set; } = new("Viridis", Array.Empty<Color>());

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

    public ColormapDto Colormap { get; set; } = new("Viridis", Array.Empty<Color>());

    public SurfaceStyle Style { get; set; } = SurfaceStyle.FilledWithWireframe;

    public bool ShowContourBelow { get; set; }

    public Color? EdgeColor { get; set; }

    public double EdgeWidth { get; set; } = 0.75;

    public bool AutoScaleColor { get; set; } = true;

    public double ColorMin { get; set; }

    public double ColorMax { get; set; } = 1;
}

public sealed class ContourPlotDto : PlotDto
{
    public double[] X { get; set; } = Array.Empty<double>();

    public double[] Y { get; set; } = Array.Empty<double>();

    public double[][] Z { get; set; } = Array.Empty<double[]>();

    public double[]? Levels { get; set; }

    public int LevelCount { get; set; } = 8;

    public bool Filled { get; set; }

    public ColormapDto Colormap { get; set; } = new("Viridis", Array.Empty<Color>());

    public double LineWidth { get; set; } = 1.5;
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
