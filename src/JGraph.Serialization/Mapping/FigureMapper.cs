using JGraph.Core.Model;
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
        };

        foreach (AxesModel axes in figure.Axes)
        {
            dto.Axes.Add(ToDto(axes));
        }

        foreach (AnnotationObject annotation in figure.Annotations)
        {
            dto.Annotations.Add(AnnotationMapper.ToDto(annotation));
        }

        return dto;
    }

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

        foreach (AxesDto axesDto in dto.Axes)
        {
            figure.Axes.Add(ToModel(axesDto));
        }

        foreach (AnnotationDto annotationDto in dto.Annotations)
        {
            figure.Annotations.Add(AnnotationMapper.ToModel(annotationDto));
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
            Background = axes.Background,
            NormalizedBounds = DtoConvert.ToDto(axes.NormalizedBounds),
            AutoScalePadding = axes.AutoScalePadding,
            EqualAspect = axes.EqualAspect,
            FrameVisible = axes.FrameVisible,
            Visible = axes.Visible,
            Is3D = axes.Is3D,
            Azimuth = axes.Azimuth,
            Elevation = axes.Elevation,
            ZAxis = ToDto(axes.ZAxis),
            Colorbar = ToDto(axes.Colorbar),
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

        return dto;
    }

    private static AxesModel ToModel(AxesDto dto)
    {
        var axes = new AxesModel
        {
            Name = dto.Name,
            Title = dto.Title,
            Background = dto.Background,
            NormalizedBounds = DtoConvert.ToRect(dto.NormalizedBounds),
            AutoScalePadding = dto.AutoScalePadding,
            EqualAspect = dto.EqualAspect,
            FrameVisible = dto.FrameVisible,
            Visible = dto.Visible,
            Is3D = dto.Is3D,
            Azimuth = dto.Azimuth,
            Elevation = dto.Elevation,
        };

        // The Z axis instance is owned by the AxesModel; apply the serialized state onto it.
        if (dto.ZAxis is not null)
        {
            ApplyAxis(axes.ZAxis, dto.ZAxis);
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

        if (dto.TitleStyle is not null)
        {
            axes.TitleStyle = DtoConvert.ToTextStyle(dto.TitleStyle);
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

        return axes;
    }

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
        LabelStyle = DtoConvert.ToDto(axis.LabelStyle),
        TickLabelStyle = DtoConvert.ToDto(axis.TickLabelStyle),
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

        if (dto.LabelStyle is not null)
        {
            axis.LabelStyle = DtoConvert.ToTextStyle(dto.LabelStyle);
        }

        if (dto.TickLabelStyle is not null)
        {
            axis.TickLabelStyle = DtoConvert.ToTextStyle(dto.TickLabelStyle);
        }
    }

    private static ColorbarDto ToDto(ColorbarModel colorbar) => new()
    {
        Visible = colorbar.Visible,
        Width = colorbar.Width,
        Label = colorbar.Label,
        TickLabelStyle = DtoConvert.ToDto(colorbar.TickLabelStyle),
    };

    private static GridDto ToDto(GridModel grid) => new()
    {
        Visible = grid.Visible,
        ShowMajor = grid.ShowMajor,
        ShowMinor = grid.ShowMinor,
        MajorLineStyle = DtoConvert.ToDto(grid.MajorLineStyle),
        MinorLineStyle = DtoConvert.ToDto(grid.MinorLineStyle),
    };

    private static void ApplyGrid(GridModel grid, GridDto dto)
    {
        grid.Visible = dto.Visible;
        grid.ShowMajor = dto.ShowMajor;
        grid.ShowMinor = dto.ShowMinor;
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
