using System.Text.Json.Serialization;
using JGraph.Core.Drawing;
using JGraph.Core.Model;

namespace JGraph.Serialization.Dto;

/// <summary>
/// The serialized form of an annotation. The concrete type is chosen by the <c>type</c> discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextAnnotationDto), "text")]
[JsonDerivedType(typeof(ArrowAnnotationDto), "arrow")]
[JsonDerivedType(typeof(RectangleAnnotationDto), "rectangle")]
[JsonDerivedType(typeof(EllipseAnnotationDto), "ellipse")]
[JsonDerivedType(typeof(DataTipAnnotationDto), "datatip")]
public abstract class AnnotationDto
{
    public string Name { get; set; } = string.Empty;

    public bool Visible { get; set; } = true;

    public int ZOrder { get; set; }

    public AnnotationSpace Space { get; set; }

    public double Opacity { get; set; } = 1.0;
}

public sealed class TextAnnotationDto : AnnotationDto
{
    public PointDto Position { get; set; } = new(0, 0);

    /// <summary>
    /// The anchor's height in a 3D axes. Zero is the default and what every label written before M45
    /// carries, which is the floor of the box.
    /// </summary>
    public double Z { get; set; }

    public string Text { get; set; } = string.Empty;

    public double FontSize { get; set; } = 12;

    public string FontFamily { get; set; } = "Segoe UI";

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public Color? Color { get; set; }

    public Color? Background { get; set; }

    public Color? BorderColor { get; set; }

    public double Padding { get; set; } = 4;

    /// <summary>How far the label is turned, anticlockwise on the page.</summary>
    public double Rotation { get; set; }

    public double BorderWidth { get; set; } = 1;

    public DashStyle BorderDash { get; set; } = DashStyle.Solid;

    public bool Smoothing { get; set; } = true;

    /// <summary>Off by default, as MATLAB has it: a label just outside the data should still be read.</summary>
    public bool Clipping { get; set; }

    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Bottom;

    /// <summary>An explicit box, or null to size the box to the text — the default and what every label written before M60 carries.</summary>
    public RectDto? Box { get; set; }
}

public sealed class DataTipAnnotationDto : AnnotationDto
{
    public PointDto Pinned { get; set; } = new(0, 0);

    public PointDto LabelPosition { get; set; } = new(0, 0);

    public string Text { get; set; } = string.Empty;

    public string SourceSeries { get; set; } = string.Empty;

    public int PointIndex { get; set; } = -1;

    public double FontSize { get; set; } = 11;

    public Color? Color { get; set; }

    public Color? Background { get; set; }

    public double MarkerSize { get; set; } = 6;
}

public sealed class ArrowAnnotationDto : AnnotationDto
{
    public PointDto Start { get; set; } = new(0, 0);

    public PointDto End { get; set; } = new(0, 0);

    public Color? Color { get; set; }

    public double LineWidth { get; set; } = 1.5;

    public DashStyle DashStyle { get; set; }

    public bool ShowHead { get; set; } = true;

    public bool ShowTailHead { get; set; }

    public double HeadLength { get; set; } = 12;

    public double HeadWidth { get; set; } = 9;

    public string Text { get; set; } = string.Empty;

    public double FontSize { get; set; } = 10;

    public string FontFamily { get; set; } = "Segoe UI";
}

/// <summary>The shared shape (rectangle/ellipse) two-corner annotation body.</summary>
public abstract class ShapeAnnotationDto : AnnotationDto
{
    public PointDto Corner1 { get; set; } = new(0, 0);

    public PointDto Corner2 { get; set; } = new(0, 0);

    public Color? Stroke { get; set; }

    public double LineWidth { get; set; } = 1.5;

    public DashStyle DashStyle { get; set; }

    public Color? Fill { get; set; }
}

public sealed class RectangleAnnotationDto : ShapeAnnotationDto
{
}

public sealed class EllipseAnnotationDto : ShapeAnnotationDto
{
}
