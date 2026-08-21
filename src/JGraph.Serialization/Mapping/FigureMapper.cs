using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Serialization.Dto;

namespace JGraph.Serialization.Mapping;

/// <summary>
/// Maps the figure object tree to and from its document DTO. The plot and annotation subtrees are
/// delegated to <see cref="PlotMapper"/> and <see cref="AnnotationMapper"/>.
/// </summary>
internal static class FigureMapper
{
    public static FigureDto ToDto(FigureModel figure)
    {
        var dto = new FigureDto
        {
            Name = figure.Name,
            Background = figure.Background,
            Size = DtoConvert.ToDto(figure.Size),
            Title = figure.Title,
            TitleStyle = DtoConvert.ToDto(figure.TitleStyle),
            Colormap = figure.Colormap is { } map ? DtoConvert.ToDto(map) : null,
            Alphamap = figure.Alphamap?.ToArray(),
            NextPlot = figure.NextPlot.ToString(),
            NumberTitle = figure.NumberTitle,
            FileName = figure.FileName,
            InvertHardcopy = figure.InvertHardcopy,
            GraphicsSmoothing = figure.GraphicsSmoothing,
            Pointer = figure.Pointer.ToString(),
            Resizable = figure.Resizable,
            ToolBar = figure.ToolBar.ToString(),
            WindowState = figure.WindowState.ToString(),
            Position = figure.PositionSpecified
                ? new PointDto(figure.Position.X, figure.Position.Y)
                : null,
            PaperUnits = figure.PaperUnits.ToString(),
            PaperType = figure.PaperType,
            PaperSize = figure.PaperSize is { } page ? DtoConvert.ToDto(page) : null,
            PaperOrientation = figure.PaperOrientation.ToString(),
            PaperPosition = DtoConvert.ToDto(figure.PaperPosition),
            PaperPositionAuto = figure.PaperPositionAuto,
        };

        foreach (AxesModel axes in figure.Axes)
        {
            dto.Axes.Add(ToDto(axes));
        }

        foreach (AnnotationObject annotation in figure.Annotations)
        {
            dto.Annotations.Add(AnnotationMapper.ToDto(annotation));
        }

        foreach (ContextMenuModel menu in figure.ContextMenus)
        {
            dto.ContextMenus.Add(new ContextMenuDto
            {
                Name = menu.Name,
                Items = menu.Items.Select(ToDto).ToList(),
            });
        }

        return dto;
    }

    private static MenuItemDto ToDto(MenuItemModel item) => new()
    {
        Text = item.Text,
        Checked = item.Checked,
        Enable = item.Enable,
        Separator = item.Separator,
        Accelerator = item.Accelerator,
        Tooltip = item.Tooltip,
        ForegroundColor = item.ForegroundColor,
        Items = item.Items.Select(ToDto).ToList(),
    };

    private static MenuItemModel ToModel(MenuItemDto dto)
    {
        var item = new MenuItemModel
        {
            Text = dto.Text,
            Checked = dto.Checked,
            Enable = dto.Enable,
            Separator = dto.Separator,
            Accelerator = dto.Accelerator,
            Tooltip = dto.Tooltip,
            ForegroundColor = dto.ForegroundColor,
        };
        foreach (MenuItemDto child in dto.Items)
        {
            item.Items.Add(ToModel(child));
        }

        return item;
    }

    /// <summary>
    /// A word from a document read back as its enum, or the fallback when the word is absent (an
    /// older document) or unknown (a newer one, read by an older build). Never an error: a document
    /// that names something this build has no word for still opens, minus that one choice.
    /// </summary>
    private static TEnum ParseOr<TEnum>(string? word, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse(word, ignoreCase: true, out TEnum parsed) ? parsed : fallback;

    public static FigureModel ToModel(FigureDto dto)
    {
        var figure = new FigureModel
        {
            Name = dto.Name,
            Background = dto.Background,
            Size = DtoConvert.ToSize(dto.Size),
            Title = dto.Title,
        };

        if (dto.TitleStyle is not null)
        {
            figure.TitleStyle = DtoConvert.ToTextStyle(dto.TitleStyle);
        }

        // Every one of these is absent from a document written before M75, and every one of them
        // falls back to what a figure was then, so an old file loads as the figure it was saved as.
        figure.Colormap = dto.Colormap is { } mapDto ? DtoConvert.ToColormap(mapDto) : null;
        figure.Alphamap = dto.Alphamap;
        figure.NextPlot = ParseOr(dto.NextPlot, FigureNextPlot.Add);
        figure.NumberTitle = dto.NumberTitle;
        figure.FileName = dto.FileName;
        figure.InvertHardcopy = dto.InvertHardcopy;
        figure.GraphicsSmoothing = dto.GraphicsSmoothing;
        figure.Pointer = ParseOr(dto.Pointer, PointerShape.Arrow);
        figure.Resizable = dto.Resizable;
        figure.ToolBar = ParseOr(dto.ToolBar, FigureToolBarMode.Auto);
        figure.WindowState = ParseOr(dto.WindowState, FigureWindowState.Normal);
        figure.PaperUnits = ParseOr(dto.PaperUnits, PaperUnitType.Inches);
        figure.PaperType = string.IsNullOrEmpty(dto.PaperType) ? "usletter" : dto.PaperType;
        figure.PaperSize = dto.PaperSize is { } paper ? DtoConvert.ToSize(paper) : null;
        figure.PaperOrientation = ParseOr(dto.PaperOrientation, PaperOrientationType.Portrait);
        figure.PaperPosition = DtoConvert.ToRect(dto.PaperPosition);
        figure.PaperPositionAuto = dto.PaperPositionAuto;

        // Position last, because writing it is what says a figure has been placed at all.
        if (dto.Position is { } placed)
        {
            figure.Position = new Point2D(placed.X, placed.Y);
        }

        foreach (AxesDto axesDto in dto.Axes)
        {
            figure.Axes.Add(ToModel(axesDto));
        }

        foreach (AnnotationDto annotationDto in dto.Annotations)
        {
            figure.Annotations.Add(AnnotationMapper.ToModel(annotationDto));
        }

        foreach (ContextMenuDto menuDto in dto.ContextMenus)
        {
            var menu = new ContextMenuModel { Name = menuDto.Name };
            foreach (MenuItemDto itemDto in menuDto.Items)
            {
                menu.Items.Add(ToModel(itemDto));
            }

            figure.ContextMenus.Add(menu);
        }

        return figure;
    }

    private static AxesDto ToDto(AxesModel axes)
    {
        var dto = new AxesDto
        {
            Name = axes.Name,
            Title = axes.Title,
            TitleStyle = DtoConvert.ToDto(axes.TitleStyle),
            Subtitle = axes.Subtitle,
            SubtitleStyle = DtoConvert.ToDto(axes.SubtitleStyle),
            Background = axes.Background,
            NormalizedBounds = DtoConvert.ToDto(axes.NormalizedBounds),
            AutoScalePadding = axes.AutoScalePadding,
            EqualAspect = axes.EqualAspect,
            FrameVisible = axes.FrameVisible,
            Visible = axes.Visible,
            Is3D = axes.Is3D,
            Azimuth = axes.Azimuth,
            Elevation = axes.Elevation,
            Roll = axes.Roll,
            ZAxis = ToDto(axes.ZAxis),
            IsPolar = axes.IsPolar,
            ThetaZeroLocation = axes.ThetaZeroLocation.ToString(),
            ThetaDirection = axes.ThetaDirection.ToString(),
            ThetaAxisUnits = axes.ThetaAxisUnits.ToString(),
            RAxisLocation = axes.RAxisLocation,
            RAxis = ToDto(axes.RAxis),
            ThetaAxis = ToDto(axes.ThetaAxis),
            Colorbar = ToDto(axes.Colorbar),
            BubbleLegend = ToDto(axes.BubbleLegend),
            BubbleSizeMin = axes.BubbleSizeRange.Min,
            BubbleSizeMax = axes.BubbleSizeRange.Max,
            BubbleSizeLimits = axes.BubbleSizeLimits is { } limits ? [limits.Min, limits.Max] : null,
            Grid = ToDto(axes.Grid),
            Legend = ToLegendDto(axes),
        };

        foreach (AxisModel axis in axes.XAxes)
        {
            dto.XAxes.Add(ToDto(axis));
        }

        foreach (AxisModel axis in axes.YAxes)
        {
            dto.YAxes.Add(ToDto(axis));
        }

        foreach (PlotObject plot in axes.Plots)
        {
            dto.Plots.Add(PlotMapper.ToDto(plot));
        }

        foreach (AnnotationObject annotation in axes.Annotations)
        {
            dto.Annotations.Add(AnnotationMapper.ToDto(annotation));
        }

        foreach (LightModel light in axes.Lights)
        {
            dto.Lights.Add(ToDto(light));
        }

        dto.ColorOrder = axes.ColorOrder is { } order ? [.. order] : null;
        dto.PlotBoxAspect = new Point3Dto(
            axes.PlotBoxAspect.X, axes.PlotBoxAspect.Y, axes.PlotBoxAspect.Z);
        dto.Layer = axes.Layer == AxesLayer.Bottom ? null : axes.Layer.ToString();
        dto.LineWidth = axes.LineWidth;
        dto.BoxStyle = axes.BoxStyle == Box3DStyle.Back ? null : axes.BoxStyle.ToString();
        dto.AmbientLightColor = axes.AmbientLightColor == Colors.White ? null : axes.AmbientLightColor;
        dto.TitleFontSizeMultiplier = axes.TitleFontSizeMultiplier;
        dto.LabelFontSizeMultiplier = axes.LabelFontSizeMultiplier;
        dto.TitleHorizontalAlignment = axes.TitleHorizontalAlignment == TitleHorizontalAlignment.Center
            ? null
            : axes.TitleHorizontalAlignment.ToString();
        dto.ColorScale = axes.ColorScale == ColorScaleType.Linear ? null : axes.ColorScale.ToString();
        dto.Colormap = axes.Colormap is { } map ? DtoConvert.ToDto(map) : null;
        dto.ColorLimits = axes.ColorLimits is { } colorLimits ? [colorLimits.Min, colorLimits.Max] : null;
        dto.DataAspectRatio = axes.DataAspectRatio is { } aspect
            ? new Point3Dto(aspect.X, aspect.Y, aspect.Z)
            : null;

        // M74: the camera the picture is drawn from, and what turns data into transparency. Every
        // one of these is null when the axes has not been told otherwise, so a document written
        // before this wave loads as the axes it described.
        dto.CameraPosition = axes.CameraPosition is { } eye
            ? new Point3Dto(eye.X, eye.Y, eye.Z)
            : null;
        dto.CameraTarget = axes.CameraTarget is { } aim
            ? new Point3Dto(aim.X, aim.Y, aim.Z)
            : null;
        dto.CameraUpVector = axes.CameraUpVector is { } up
            ? new Point3Dto(up.X, up.Y, up.Z)
            : null;
        dto.CameraViewAngle = axes.CameraViewAngle;
        dto.Projection = axes.Projection == ProjectionType.Orthographic ? null : axes.Projection.ToString();
        dto.SortMethod = axes.SortMethod == SortMethodType.Depth ? null : axes.SortMethod.ToString();
        dto.Clipping = axes.Clipping;
        dto.AlphaLimits = axes.AlphaLimits is { } alphaLimits ? [alphaLimits.Min, alphaLimits.Max] : null;
        dto.Alphamap = axes.Alphamap is { } alphamap ? [.. alphamap] : null;
        dto.AlphaScale = axes.AlphaScale == ColorScaleType.Linear ? null : axes.AlphaScale.ToString();
        dto.InnerTarget = axes.InnerTarget is { } inner ? DtoConvert.ToDto(inner) : null;
        dto.PositionConstraint = axes.PositionConstraint == PositionConstraintType.OuterPosition
            ? null
            : axes.PositionConstraint.ToString();
        dto.LineStyleOrder = axes.LineStyleOrder is { } styles
            ? styles.Select(static entry => new SeriesLineStyleDto(entry.Dash, entry.Marker)).ToList()
            : null;
        return dto;
    }

    private static AxesModel ToModel(AxesDto dto)
    {
        var axes = new AxesModel
        {
            Name = dto.Name,
            Title = dto.Title,
            Subtitle = dto.Subtitle,
            Background = dto.Background,
            NormalizedBounds = DtoConvert.ToRect(dto.NormalizedBounds),
            AutoScalePadding = dto.AutoScalePadding,
            EqualAspect = dto.EqualAspect,
            FrameVisible = dto.FrameVisible,
            Visible = dto.Visible,
            Is3D = dto.Is3D,
            Azimuth = dto.Azimuth,
            Elevation = dto.Elevation,
            Roll = dto.Roll,
        };

        // The Z axis instance is owned by the AxesModel; apply the serialized state onto it.
        if (dto.ZAxis is not null)
        {
            ApplyAxis(axes.ZAxis, dto.ZAxis);
        }

        // The angular rulers are owned the same way. An unreadable name falls back to the default
        // rather than throwing: a figure with a strange θ origin is still a figure worth opening.
        axes.IsPolar = dto.IsPolar;
        axes.RAxisLocation = dto.RAxisLocation;
        if (Enum.TryParse(dto.ThetaZeroLocation, out ThetaZeroLocation zero))
        {
            axes.ThetaZeroLocation = zero;
        }

        if (Enum.TryParse(dto.ThetaDirection, out ThetaDirection direction))
        {
            axes.ThetaDirection = direction;
        }

        if (Enum.TryParse(dto.ThetaAxisUnits, out AngleUnits units))
        {
            axes.ThetaAxisUnits = units;
        }

        if (dto.RAxis is not null)
        {
            ApplyAxis(axes.RAxis, dto.RAxis);
        }

        if (dto.ThetaAxis is not null)
        {
            ApplyAxis(axes.ThetaAxis, dto.ThetaAxis);
        }

        if (dto.Colorbar is not null)
        {
            axes.Colorbar.Visible = dto.Colorbar.Visible;
            axes.Colorbar.Width = dto.Colorbar.Width;
            axes.Colorbar.Label = dto.Colorbar.Label;
            if (dto.Colorbar.TickLabelStyle is not null)
            {
                axes.Colorbar.TickLabelStyle = DtoConvert.ToTextStyle(dto.Colorbar.TickLabelStyle);
            }
        }

        axes.BubbleSizeRange = new DataRange(dto.BubbleSizeMin, dto.BubbleSizeMax);
        axes.BubbleSizeLimits = dto.BubbleSizeLimits is { Length: 2 } bubbleLimits
            ? new DataRange(bubbleLimits[0], bubbleLimits[1])
            : null;

        if (dto.BubbleLegend is { } bubbleLegend)
        {
            axes.BubbleLegend.Visible = bubbleLegend.Visible;
            axes.BubbleLegend.Position = bubbleLegend.Position;
            axes.BubbleLegend.Location = new Point2D(bubbleLegend.LocationX, bubbleLegend.LocationY);
            axes.BubbleLegend.Style = bubbleLegend.Style;
            axes.BubbleLegend.NumBubbles = bubbleLegend.NumBubbles;
            axes.BubbleLegend.LimitLabels = bubbleLegend.LimitLabels;
            axes.BubbleLegend.Background = bubbleLegend.Background;
            axes.BubbleLegend.BorderColor = bubbleLegend.BorderColor;
            axes.BubbleLegend.ShowBorder = bubbleLegend.ShowBorder;
            axes.BubbleLegend.Title = bubbleLegend.Title;
            if (bubbleLegend.TextStyle is not null)
            {
                axes.BubbleLegend.TextStyle = DtoConvert.ToTextStyle(bubbleLegend.TextStyle);
            }
        }

        if (dto.TitleStyle is not null)
        {
            axes.TitleStyle = DtoConvert.ToTextStyle(dto.TitleStyle);
        }

        if (dto.SubtitleStyle is not null)
        {
            axes.SubtitleStyle = DtoConvert.ToTextStyle(dto.SubtitleStyle);
        }

        // Replace the axes created by the AxesModel constructor with the serialized ones (guarding
        // against an empty document, which would leave the axes with no primary axis).
        if (dto.XAxes.Count > 0)
        {
            axes.XAxes.Clear();
            foreach (AxisDto axisDto in dto.XAxes)
            {
                axes.XAxes.Add(ToModel(axisDto));
            }
        }

        if (dto.YAxes.Count > 0)
        {
            axes.YAxes.Clear();
            foreach (AxisDto axisDto in dto.YAxes)
            {
                axes.YAxes.Add(ToModel(axisDto));
            }
        }

        ApplyGrid(axes.Grid, dto.Grid);
        ApplyLegend(axes.Legend, dto.Legend);

        foreach (PlotDto plotDto in dto.Plots)
        {
            axes.Plots.Add(PlotMapper.ToModel(plotDto));
        }

        // After the plots, so each row's plot index resolves.
        ApplyLegendEntries(axes, dto.Legend);

        foreach (AnnotationDto annotationDto in dto.Annotations)
        {
            axes.Annotations.Add(AnnotationMapper.ToModel(annotationDto));
        }

        foreach (LightDto lightDto in dto.Lights)
        {
            axes.Lights.Add(ToModel(lightDto));
        }

        axes.ColorOrder = dto.ColorOrder is { Count: > 0 } order ? [.. order] : null;
        axes.PlotBoxAspect = new Vector3D(
            dto.PlotBoxAspect.X, dto.PlotBoxAspect.Y, dto.PlotBoxAspect.Z);

        if (Enum.TryParse(dto.Layer, out AxesLayer layer))
        {
            axes.Layer = layer;
        }

        axes.LineWidth = dto.LineWidth;
        if (Enum.TryParse(dto.BoxStyle, out Box3DStyle boxStyle))
        {
            axes.BoxStyle = boxStyle;
        }

        if (dto.AmbientLightColor is { } ambient)
        {
            axes.AmbientLightColor = ambient;
        }

        axes.TitleFontSizeMultiplier = dto.TitleFontSizeMultiplier;
        axes.LabelFontSizeMultiplier = dto.LabelFontSizeMultiplier;
        if (Enum.TryParse(dto.TitleHorizontalAlignment, out TitleHorizontalAlignment titleAlignment))
        {
            axes.TitleHorizontalAlignment = titleAlignment;
        }

        if (Enum.TryParse(dto.ColorScale, out ColorScaleType colorScale))
        {
            axes.ColorScale = colorScale;
        }

        axes.Colormap = dto.Colormap is { } mapDto ? DtoConvert.ToColormap(mapDto) : null;
        axes.ColorLimits = dto.ColorLimits is { Length: 2 } colorLimits
            ? new DataRange(colorLimits[0], colorLimits[1])
            : null;
        if (dto.DataAspectRatio is { } aspectDto)
        {
            axes.DataAspectRatio = new Vector3D(aspectDto.X, aspectDto.Y, aspectDto.Z);
        }

        axes.CameraPosition = dto.CameraPosition is { } eyeDto
            ? new Vector3D(eyeDto.X, eyeDto.Y, eyeDto.Z)
            : null;
        axes.CameraTarget = dto.CameraTarget is { } aimDto
            ? new Vector3D(aimDto.X, aimDto.Y, aimDto.Z)
            : null;
        axes.CameraUpVector = dto.CameraUpVector is { } upDto
            ? new Vector3D(upDto.X, upDto.Y, upDto.Z)
            : null;
        axes.CameraViewAngle = dto.CameraViewAngle;

        if (Enum.TryParse(dto.Projection, out ProjectionType projection))
        {
            axes.Projection = projection;
        }

        if (Enum.TryParse(dto.SortMethod, out SortMethodType sortMethod))
        {
            axes.SortMethod = sortMethod;
        }

        axes.Clipping = dto.Clipping;
        axes.AlphaLimits = dto.AlphaLimits is { Length: 2 } alphaLimits
            ? new DataRange(alphaLimits[0], alphaLimits[1])
            : null;
        axes.Alphamap = dto.Alphamap is { Length: > 0 } alphamap ? alphamap : null;

        axes.InnerTarget = dto.InnerTarget is { } inner ? DtoConvert.ToRect(inner) : null;
        axes.PositionConstraint = ParseOr(dto.PositionConstraint, PositionConstraintType.OuterPosition);

        if (Enum.TryParse(dto.AlphaScale, out ColorScaleType alphaScale))
        {
            axes.AlphaScale = alphaScale;
        }

        axes.LineStyleOrder = dto.LineStyleOrder is { Count: > 0 } styleDtos
            ? styleDtos.Select(static entry => new SeriesLineStyle(entry.Dash, entry.Marker)).ToArray()
            : null;

        // A held figure keeps cycling from where it stopped: the next seat is one past the highest
        // seat any reloaded plot holds, and at least the plot count for documents without seats.
        int nextSeat = axes.Plots.Count;
        foreach (PlotObject plot in axes.Plots)
        {
            nextSeat = System.Math.Max(nextSeat, plot.SeriesIndex + 1);
        }

        axes.NextSeriesIndex = nextSeat;
        return axes;
    }

    private static LightDto ToDto(LightModel light) => new()
    {
        Name = light.Name,
        Visible = light.Visible,
        Style = light.Style,
        Position = new Point3Dto(light.Position.X, light.Position.Y, light.Position.Z),
        Color = light.Color,
        FollowsCamera = light.FollowsCamera,
    };

    private static LightModel ToModel(LightDto dto) => new()
    {
        Name = dto.Name,
        Visible = dto.Visible,
        Style = dto.Style,
        Position = new Vector3D(dto.Position.X, dto.Position.Y, dto.Position.Z),
        Color = dto.Color,
        FollowsCamera = dto.FollowsCamera,
    };

    private static AxisDto ToDto(AxisModel axis) => new()
    {
        Orientation = axis.Orientation,
        Position = axis.Position,
        Scale = axis.Scale,
        Range = DtoConvert.ToDto(axis.Range),
        AutoScale = axis.AutoScale,
        Inverted = axis.Inverted,
        Label = axis.Label,
        ShowMajorTicks = axis.ShowMajorTicks,
        ShowMinorTicks = axis.ShowMinorTicks,
        ShowTickLabels = axis.ShowTickLabels,
        TargetMajorTickCount = axis.TargetMajorTickCount,
        TickLabelFormat = axis.TickLabelFormat,
        Categories = axis.Categories?.ToArray(),
        TickPositions = axis.TickPositions?.ToArray(),
        TickLabelOverrides = axis.TickLabelOverrides?.ToArray(),
        TickLabelAngle = axis.TickLabelAngle,
        LabelStyle = DtoConvert.ToDto(axis.LabelStyle),
        TickLabelStyle = DtoConvert.ToDto(axis.TickLabelStyle),
        TickDirection = axis.TickDirection?.ToString(),
        TickLength = axis.TickLength is { } tickLength ? [tickLength.X, tickLength.Y] : null,
        RulerColor = axis.RulerColor,
        LimitMethod = axis.LimitMethod == LimitMethod.Padded ? null : axis.LimitMethod.ToString(),
    };

    private static AxisModel ToModel(AxisDto dto)
    {
        var axis = new AxisModel(dto.Orientation, dto.Position);
        ApplyAxis(axis, dto);
        return axis;
    }

    /// <summary>Applies serialized axis state onto an existing axis (used for the owned Z axis too).</summary>
    private static void ApplyAxis(AxisModel axis, AxisDto dto)
    {
        axis.Scale = dto.Scale;
        axis.Range = DtoConvert.ToRange(dto.Range);
        axis.AutoScale = dto.AutoScale;
        axis.Inverted = dto.Inverted;
        axis.Label = dto.Label;
        axis.ShowMajorTicks = dto.ShowMajorTicks;
        axis.ShowMinorTicks = dto.ShowMinorTicks;
        axis.ShowTickLabels = dto.ShowTickLabels;
        axis.TargetMajorTickCount = dto.TargetMajorTickCount;
        axis.TickLabelFormat = dto.TickLabelFormat;

        if (dto.Categories is not null)
        {
            axis.Categories = dto.Categories;
        }

        // Null means automatic, which is also what a document written before these fields existed
        // says by omitting them — so a v5 figure loads with the ticks it always had.
        axis.TickPositions = dto.TickPositions;
        axis.TickLabelOverrides = dto.TickLabelOverrides;
        axis.TickLabelAngle = dto.TickLabelAngle;

        if (dto.LabelStyle is not null)
        {
            axis.LabelStyle = DtoConvert.ToTextStyle(dto.LabelStyle);
        }

        if (dto.TickLabelStyle is not null)
        {
            axis.TickLabelStyle = DtoConvert.ToTextStyle(dto.TickLabelStyle);
        }

        // Null means automatic for all four, which is also what an older document says by omission.
        axis.TickDirection = Enum.TryParse(dto.TickDirection, out TickDirection tickDirection)
            ? tickDirection
            : null;
        axis.TickLength = dto.TickLength is { Length: 2 } tickLength
            ? new Vector2D(tickLength[0], tickLength[1])
            : null;
        axis.RulerColor = dto.RulerColor;
        axis.LimitMethod = Enum.TryParse(dto.LimitMethod, out LimitMethod limitMethod)
            ? limitMethod
            : LimitMethod.Padded;
    }

    private static ColorbarDto ToDto(ColorbarModel colorbar) => new()
    {
        Visible = colorbar.Visible,
        Width = colorbar.Width,
        Label = colorbar.Label,
        TickLabelStyle = DtoConvert.ToDto(colorbar.TickLabelStyle),
    };

    private static BubbleLegendDto ToDto(BubbleLegendModel legend) => new()
    {
        Visible = legend.Visible,
        Position = legend.Position,
        Style = legend.Style,
        NumBubbles = legend.NumBubbles,
        LimitLabels = legend.LimitLabels,
        Background = legend.Background,
        BorderColor = legend.BorderColor,
        ShowBorder = legend.ShowBorder,
        TextStyle = DtoConvert.ToDto(legend.TextStyle),
        Title = legend.Title,
        LocationX = legend.Location.X,
        LocationY = legend.Location.Y,
    };

    private static GridDto ToDto(GridModel grid) => new()
    {
        Visible = grid.Visible,
        ShowMajor = grid.ShowMajor,
        ShowMinor = grid.ShowMinor,
        ShowMajorX = grid.ShowMajorX,
        ShowMajorY = grid.ShowMajorY,
        ShowMajorZ = grid.ShowMajorZ,
        ShowMinorX = grid.ShowMinorX,
        ShowMinorY = grid.ShowMinorY,
        ShowMinorZ = grid.ShowMinorZ,
        MajorColorManual = grid.MajorColorManual,
        MinorColorManual = grid.MinorColorManual,
        MajorAlphaManual = grid.MajorAlphaManual,
        MinorAlphaManual = grid.MinorAlphaManual,
        MajorLineStyle = DtoConvert.ToDto(grid.MajorLineStyle),
        MinorLineStyle = DtoConvert.ToDto(grid.MinorLineStyle),
    };

    private static void ApplyGrid(GridModel grid, GridDto dto)
    {
        grid.Visible = dto.Visible;

        // A document from before the per-direction flags speaks only through the aggregates.
        grid.ShowMajorX = dto.ShowMajorX ?? dto.ShowMajor;
        grid.ShowMajorY = dto.ShowMajorY ?? dto.ShowMajor;
        grid.ShowMajorZ = dto.ShowMajorZ ?? dto.ShowMajor;
        grid.ShowMinorX = dto.ShowMinorX ?? dto.ShowMinor;
        grid.ShowMinorY = dto.ShowMinorY ?? dto.ShowMinor;
        grid.ShowMinorZ = dto.ShowMinorZ ?? dto.ShowMinor;
        grid.MajorColorManual = dto.MajorColorManual;
        grid.MinorColorManual = dto.MinorColorManual;
        grid.MajorAlphaManual = dto.MajorAlphaManual;
        grid.MinorAlphaManual = dto.MinorAlphaManual;
        if (dto.MajorLineStyle is not null)
        {
            grid.MajorLineStyle = DtoConvert.ToLineStyle(dto.MajorLineStyle);
        }

        if (dto.MinorLineStyle is not null)
        {
            grid.MinorLineStyle = DtoConvert.ToLineStyle(dto.MinorLineStyle);
        }
    }

    private static LegendDto ToLegendDto(AxesModel axes)
    {
        LegendModel legend = axes.Legend;
        var dto = new LegendDto
        {
            Visible = legend.Visible,
            Position = legend.Position,
            Background = legend.Background,
            BorderColor = legend.BorderColor,
            ShowBorder = legend.ShowBorder,
            TextStyle = DtoConvert.ToDto(legend.TextStyle),
            Title = legend.Title,
            LocationX = legend.Location.X,
            LocationY = legend.Location.Y,
        };

        foreach (LegendEntryModel entry in legend.Entries)
        {
            int index = entry.Plot is null ? -1 : axes.Plots.IndexOf(entry.Plot);
            if (index < 0)
            {
                continue;
            }

            dto.Entries.Add(new LegendEntryDto
            {
                PlotIndex = index,
                Label = entry.Label,
                Visible = entry.Visible,
            });
        }

        return dto;
    }

    private static void ApplyLegend(LegendModel legend, LegendDto dto)
    {
        legend.Visible = dto.Visible;
        legend.Position = dto.Position;
        legend.Background = dto.Background;
        legend.BorderColor = dto.BorderColor;
        legend.ShowBorder = dto.ShowBorder;
        if (dto.TextStyle is not null)
        {
            legend.TextStyle = DtoConvert.ToTextStyle(dto.TextStyle);
        }

        legend.Title = dto.Title;
        legend.Location = new Core.Primitives.Point2D(dto.LocationX, dto.LocationY);
    }

    /// <summary>
    /// Rebuilds the legend rows once the plots exist, resolving each row's plot index. Rows whose
    /// index no longer resolves are skipped; the renderer's sync pass then supplies a default row for
    /// any plot left without one, which is also what happens for a document written before M26.
    /// </summary>
    private static void ApplyLegendEntries(AxesModel axes, LegendDto dto)
    {
        axes.Legend.Entries.Clear();
        foreach (LegendEntryDto entryDto in dto.Entries)
        {
            if (entryDto.PlotIndex < 0 || entryDto.PlotIndex >= axes.Plots.Count)
            {
                continue;
            }

            axes.Legend.Entries.Add(new LegendEntryModel
            {
                Plot = axes.Plots[entryDto.PlotIndex],
                Label = entryDto.Label,
                Visible = entryDto.Visible,
            });
        }
    }
}
