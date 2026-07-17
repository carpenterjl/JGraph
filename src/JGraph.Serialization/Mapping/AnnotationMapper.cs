using JGraph.Core.Model;
using JGraph.Objects.Annotations;
using JGraph.Serialization.Dto;

namespace JGraph.Serialization.Mapping;

/// <summary>Maps every concrete annotation to and from its <see cref="AnnotationDto"/>.</summary>
internal static class AnnotationMapper
{
    public static AnnotationDto ToDto(AnnotationObject annotation)
    {
        AnnotationDto dto = annotation switch
        {
            TextAnnotation a => new TextAnnotationDto
            {
                Position = DtoConvert.ToDto(a.Position),
                Text = a.Text,
                FontSize = a.FontSize,
                FontFamily = a.FontFamily,
                Bold = a.Bold,
                Italic = a.Italic,
                Color = a.Color,
                Background = a.Background,
                BorderColor = a.BorderColor,
                Padding = a.Padding,
                HorizontalAlignment = a.HorizontalAlignment,
                VerticalAlignment = a.VerticalAlignment,
            },
            ArrowAnnotation a => new ArrowAnnotationDto
            {
                Start = DtoConvert.ToDto(a.Start),
                End = DtoConvert.ToDto(a.End),
                Color = a.Color,
                LineWidth = a.LineWidth,
                DashStyle = a.DashStyle,
                ShowHead = a.ShowHead,
                HeadLength = a.HeadLength,
                HeadWidth = a.HeadWidth,
            },
            RectangleAnnotation a => FillShape(new RectangleAnnotationDto(), a),
            EllipseAnnotation a => FillShape(new EllipseAnnotationDto(), a),
            _ => throw new GraphFormatException($"Cannot serialize annotation type '{annotation.GetType().Name}'."),
        };

        CaptureCommon(annotation, dto);
        return dto;
    }

    public static AnnotationObject ToModel(AnnotationDto dto)
    {
        AnnotationObject annotation = dto switch
        {
            TextAnnotationDto d => new TextAnnotation
            {
                Position = DtoConvert.ToPoint(d.Position),
                Text = d.Text,
                FontSize = d.FontSize,
                FontFamily = d.FontFamily,
                Bold = d.Bold,
                Italic = d.Italic,
                Color = d.Color,
                Background = d.Background,
                BorderColor = d.BorderColor,
                Padding = d.Padding,
                HorizontalAlignment = d.HorizontalAlignment,
                VerticalAlignment = d.VerticalAlignment,
            },
            ArrowAnnotationDto d => new ArrowAnnotation
            {
                Start = DtoConvert.ToPoint(d.Start),
                End = DtoConvert.ToPoint(d.End),
                Color = d.Color,
                LineWidth = d.LineWidth,
                DashStyle = d.DashStyle,
                ShowHead = d.ShowHead,
                HeadLength = d.HeadLength,
                HeadWidth = d.HeadWidth,
            },
            RectangleAnnotationDto d => FillShape(new RectangleAnnotation(), d),
            EllipseAnnotationDto d => FillShape(new EllipseAnnotation(), d),
            _ => throw new GraphFormatException($"Unknown annotation DTO '{dto.GetType().Name}'."),
        };

        ApplyCommon(dto, annotation);
        return annotation;
    }

    private static ShapeAnnotationDto FillShape(ShapeAnnotationDto dto, ShapeAnnotation shape)
    {
        dto.Corner1 = DtoConvert.ToDto(shape.Corner1);
        dto.Corner2 = DtoConvert.ToDto(shape.Corner2);
        dto.Stroke = shape.Stroke;
        dto.LineWidth = shape.LineWidth;
        dto.DashStyle = shape.DashStyle;
        dto.Fill = shape.Fill;
        return dto;
    }

    private static ShapeAnnotation FillShape(ShapeAnnotation shape, ShapeAnnotationDto dto)
    {
        shape.Corner1 = DtoConvert.ToPoint(dto.Corner1);
        shape.Corner2 = DtoConvert.ToPoint(dto.Corner2);
        shape.Stroke = dto.Stroke;
        shape.LineWidth = dto.LineWidth;
        shape.DashStyle = dto.DashStyle;
        shape.Fill = dto.Fill;
        return shape;
    }

    private static void CaptureCommon(AnnotationObject annotation, AnnotationDto dto)
    {
        dto.Name = annotation.Name;
        dto.Visible = annotation.Visible;
        dto.ZOrder = annotation.ZOrder;
        dto.Space = annotation.Space;
        dto.Opacity = annotation.Opacity;
    }

    private static void ApplyCommon(AnnotationDto dto, AnnotationObject annotation)
    {
        annotation.Name = dto.Name;
        annotation.Visible = dto.Visible;
        annotation.ZOrder = dto.ZOrder;
        annotation.Space = dto.Space;
        annotation.Opacity = dto.Opacity;
    }
}
