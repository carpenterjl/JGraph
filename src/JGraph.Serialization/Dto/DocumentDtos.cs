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
}

/// <summary>The serialized form of an <see cref="AxesModel"/>.</summary>
public sealed class AxesDto
{
    public string Name { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public TextStyleDto? TitleStyle { get; set; }

    public Color Background { get; set; }

    public RectDto NormalizedBounds { get; set; } = new(0, 0, 1, 1);

    public double AutoScalePadding { get; set; }

    public bool EqualAspect { get; set; }

    public bool FrameVisible { get; set; } = true;

    public bool Visible { get; set; } = true;

    public bool Is3D { get; set; }

    public double Azimuth { get; set; } = -37.5;

    public double Elevation { get; set; } = 30;

    /// <summary>The Z axis of a 3D axes; null in documents written before format version 2.</summary>
    public AxisDto? ZAxis { get; set; }

    /// <summary>The colorbar; null in documents written before format version 2.</summary>
    public ColorbarDto? Colorbar { get; set; }

    public List<AxisDto> XAxes { get; set; } = new();

    public List<AxisDto> YAxes { get; set; } = new();

    public GridDto Grid { get; set; } = new();

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

    public TextStyleDto? LabelStyle { get; set; }

    public TextStyleDto? TickLabelStyle { get; set; }
}

/// <summary>The serialized form of a <see cref="GridModel"/>.</summary>
public sealed class GridDto
{
    public bool Visible { get; set; } = true;

    public bool ShowMajor { get; set; } = true;

    public bool ShowMinor { get; set; }

    public LineStyleDto? MajorLineStyle { get; set; }

    public LineStyleDto? MinorLineStyle { get; set; }
}

/// <summary>The serialized form of a <see cref="ColorbarModel"/>.</summary>
public sealed class ColorbarDto
{
    public bool Visible { get; set; }

    public double Width { get; set; } = 18;

    public string? Label { get; set; }

    public TextStyleDto? TickLabelStyle { get; set; }
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
