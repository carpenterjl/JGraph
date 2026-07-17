using JGraph.Core.Model;
using JGraph.Objects.Annotations;

namespace JGraph.Objects;

/// <summary>
/// Fluent factory helpers for annotations, mirroring <see cref="AxesExtensions"/>. Axes annotations
/// are anchored in data coordinates; figure annotations in normalized [0, 1] figure coordinates
/// ((0, 0) = top-left).
/// </summary>
public static class AnnotationExtensions
{
    /// <summary>Adds a text label at the given data point and returns it.</summary>
    public static TextAnnotation AddText(this AxesModel axes, double x, double y, string text)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var annotation = new TextAnnotation(x, y, text);
        axes.Annotations.Add(annotation);
        return annotation;
    }

    /// <summary>Adds an arrow from (x1, y1) to (x2, y2) in data coordinates and returns it.</summary>
    public static ArrowAnnotation AddArrow(this AxesModel axes, double x1, double y1, double x2, double y2)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var annotation = new ArrowAnnotation(x1, y1, x2, y2);
        axes.Annotations.Add(annotation);
        return annotation;
    }

    /// <summary>Adds a plain line (an arrow without a head) in data coordinates and returns it.</summary>
    public static ArrowAnnotation AddLineAnnotation(this AxesModel axes, double x1, double y1, double x2, double y2)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var annotation = new ArrowAnnotation(x1, y1, x2, y2) { ShowHead = false, Name = "Line" };
        axes.Annotations.Add(annotation);
        return annotation;
    }

    /// <summary>Adds a rectangle annotation spanning the two data-space corners and returns it.</summary>
    public static RectangleAnnotation AddRectangleAnnotation(this AxesModel axes, double x1, double y1, double x2, double y2)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var annotation = new RectangleAnnotation(x1, y1, x2, y2);
        axes.Annotations.Add(annotation);
        return annotation;
    }

    /// <summary>Adds an ellipse annotation inscribed in the two data-space corners and returns it.</summary>
    public static EllipseAnnotation AddEllipseAnnotation(this AxesModel axes, double x1, double y1, double x2, double y2)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var annotation = new EllipseAnnotation(x1, y1, x2, y2);
        axes.Annotations.Add(annotation);
        return annotation;
    }

    /// <summary>Adds a text label at normalized figure coordinates ((0, 0) = top-left) and returns it.</summary>
    public static TextAnnotation AddText(this FigureModel figure, double x, double y, string text)
    {
        ArgumentNullException.ThrowIfNull(figure);
        var annotation = new TextAnnotation(x, y, text) { Space = AnnotationSpace.Figure };
        figure.Annotations.Add(annotation);
        return annotation;
    }

    /// <summary>Adds an arrow in normalized figure coordinates ((0, 0) = top-left) and returns it.</summary>
    public static ArrowAnnotation AddArrow(this FigureModel figure, double x1, double y1, double x2, double y2)
    {
        ArgumentNullException.ThrowIfNull(figure);
        var annotation = new ArrowAnnotation(x1, y1, x2, y2) { Space = AnnotationSpace.Figure };
        figure.Annotations.Add(annotation);
        return annotation;
    }
}
