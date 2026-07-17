using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Engineering;
using JGraph.Serialization.Dto;

namespace JGraph.Serialization.Mapping;

/// <summary>Maps every concrete plot object to and from its <see cref="PlotDto"/>.</summary>
internal static class PlotMapper
{
    public static PlotDto ToDto(PlotObject plot)
    {
        PlotDto dto = plot switch
        {
            LinePlot p => new LinePlotDto
            {
                Series = DtoConvert.ToDto(p.Data),
                Color = p.Color,
                LineWidth = p.LineWidth,
                DashStyle = p.DashStyle,
                Marker = p.Marker,
                MarkerSize = p.MarkerSize,
                MarkerFill = p.MarkerFill,
            },
            ScatterPlot p => new ScatterPlotDto
            {
                Series = DtoConvert.ToDto(p.Data),
                Color = p.Color,
                Marker = p.Marker,
                MarkerSize = p.MarkerSize,
                Fill = p.Fill,
                EdgeWidth = p.EdgeWidth,
            },
            BarPlot p => new BarPlotDto
            {
                Series = DtoConvert.ToDto(p.Data),
                FillColor = p.FillColor,
                EdgeColor = p.EdgeColor,
                EdgeWidth = p.EdgeWidth,
                BarWidthFraction = p.BarWidthFraction,
                Baseline = p.Baseline,
                Horizontal = p.Horizontal,
            },
            StemPlot p => new StemPlotDto
            {
                Series = DtoConvert.ToDto(p.Data),
                Color = p.Color,
                LineWidth = p.LineWidth,
                Baseline = p.Baseline,
                Marker = p.Marker,
                MarkerSize = p.MarkerSize,
                MarkerFill = p.MarkerFill,
            },
            HistogramPlot p => new HistogramPlotDto
            {
                Values = p.Values.ToArray(),
                BinCount = p.BinCount,
                Normalization = p.Normalization,
                FillColor = p.FillColor,
                EdgeColor = p.EdgeColor,
                EdgeWidth = p.EdgeWidth,
            },
            ErrorBarPlot p => new ErrorBarPlotDto
            {
                Series = DtoConvert.ToDto(p.Data),
                ErrorNeg = p.ErrorNeg.ToArray(),
                ErrorPos = p.ErrorPos.ToArray(),
                Color = p.Color,
                LineWidth = p.LineWidth,
                CapSize = p.CapSize,
                ShowLine = p.ShowLine,
                Marker = p.Marker,
                MarkerSize = p.MarkerSize,
                MarkerFill = p.MarkerFill,
            },
            ImagePlot p => new ImagePlotDto
            {
                Values = ToJagged(p.Values),
                Colormap = DtoConvert.ToDto(p.Colormap),
                XExtent = DtoConvert.ToDto(p.XExtent),
                YExtent = DtoConvert.ToDto(p.YExtent),
                AutoScaleColor = p.AutoScaleColor,
                ColorMin = p.ColorMin,
                ColorMax = p.ColorMax,
                Interpolate = p.Interpolate,
                RowZeroAtTop = p.RowZeroAtTop,
            },
            PolarGrid p => new PolarGridDto
            {
                MaxRadius = p.MaxRadius,
                RadialDivisions = p.RadialDivisions,
                AngularDivisions = p.AngularDivisions,
                GridColor = p.GridColor,
                LabelStyle = DtoConvert.ToDto(p.LabelStyle),
                ShowLabels = p.ShowLabels,
            },
            SmithGrid p => new SmithGridDto
            {
                GridColor = p.GridColor,
                LabelStyle = DtoConvert.ToDto(p.LabelStyle),
                ShowLabels = p.ShowLabels,
            },
            EyeDiagramPlot p => new EyeDiagramPlotDto
            {
                Signal = p.Signal.ToArray(),
                SamplesPerSymbol = p.SamplesPerSymbol,
                SymbolsPerTrace = p.SymbolsPerTrace,
                Color = p.Color,
                LineWidth = p.LineWidth,
            },
            _ => throw new GraphFormatException($"Cannot serialize plot type '{plot.GetType().Name}'."),
        };

        CaptureCommon(plot, dto);
        return dto;
    }

    public static PlotObject ToModel(PlotDto dto)
    {
        PlotObject plot = dto switch
        {
            LinePlotDto d => new LinePlot(DtoConvert.ToSeries(d.Series))
            {
                Color = d.Color,
                LineWidth = d.LineWidth,
                DashStyle = d.DashStyle,
                Marker = d.Marker,
                MarkerSize = d.MarkerSize,
                MarkerFill = d.MarkerFill,
            },
            ScatterPlotDto d => new ScatterPlot(DtoConvert.ToSeries(d.Series))
            {
                Color = d.Color,
                Marker = d.Marker,
                MarkerSize = d.MarkerSize,
                Fill = d.Fill,
                EdgeWidth = d.EdgeWidth,
            },
            BarPlotDto d => new BarPlot(DtoConvert.ToSeries(d.Series))
            {
                FillColor = d.FillColor,
                EdgeColor = d.EdgeColor,
                EdgeWidth = d.EdgeWidth,
                BarWidthFraction = d.BarWidthFraction,
                Baseline = d.Baseline,
                Horizontal = d.Horizontal,
            },
            StemPlotDto d => new StemPlot(DtoConvert.ToSeries(d.Series))
            {
                Color = d.Color,
                LineWidth = d.LineWidth,
                Baseline = d.Baseline,
                Marker = d.Marker,
                MarkerSize = d.MarkerSize,
                MarkerFill = d.MarkerFill,
            },
            HistogramPlotDto d => new HistogramPlot(d.Values)
            {
                BinCount = d.BinCount,
                Normalization = d.Normalization,
                FillColor = d.FillColor,
                EdgeColor = d.EdgeColor,
                EdgeWidth = d.EdgeWidth,
            },
            ErrorBarPlotDto d => new ErrorBarPlot(DtoConvert.ToSeries(d.Series), d.ErrorNeg, d.ErrorPos)
            {
                Color = d.Color,
                LineWidth = d.LineWidth,
                CapSize = d.CapSize,
                ShowLine = d.ShowLine,
                Marker = d.Marker,
                MarkerSize = d.MarkerSize,
                MarkerFill = d.MarkerFill,
            },
            ImagePlotDto d => new ImagePlot(To2D(d.Values))
            {
                Colormap = DtoConvert.ToColormap(d.Colormap),
                XExtent = DtoConvert.ToRange(d.XExtent),
                YExtent = DtoConvert.ToRange(d.YExtent),
                AutoScaleColor = d.AutoScaleColor,
                ColorMin = d.ColorMin,
                ColorMax = d.ColorMax,
                Interpolate = d.Interpolate,
                RowZeroAtTop = d.RowZeroAtTop,
            },
            PolarGridDto d => ApplyGridLabels(new PolarGrid
            {
                MaxRadius = d.MaxRadius,
                RadialDivisions = d.RadialDivisions,
                AngularDivisions = d.AngularDivisions,
                GridColor = d.GridColor,
                ShowLabels = d.ShowLabels,
            }, d.LabelStyle),
            SmithGridDto d => ApplyGridLabels(new SmithGrid
            {
                GridColor = d.GridColor,
                ShowLabels = d.ShowLabels,
            }, d.LabelStyle),
            EyeDiagramPlotDto d => new EyeDiagramPlot(d.Signal, d.SamplesPerSymbol, d.SymbolsPerTrace)
            {
                Color = d.Color,
                LineWidth = d.LineWidth,
            },
            _ => throw new GraphFormatException($"Unknown plot DTO '{dto.GetType().Name}'."),
        };

        ApplyCommon(dto, plot);
        return plot;
    }

    private static PolarGrid ApplyGridLabels(PolarGrid grid, TextStyleDto? style)
    {
        if (style is not null)
        {
            grid.LabelStyle = DtoConvert.ToTextStyle(style);
        }

        return grid;
    }

    private static SmithGrid ApplyGridLabels(SmithGrid grid, TextStyleDto? style)
    {
        if (style is not null)
        {
            grid.LabelStyle = DtoConvert.ToTextStyle(style);
        }

        return grid;
    }

    private static void CaptureCommon(PlotObject plot, PlotDto dto)
    {
        dto.Name = plot.Name;
        dto.DisplayName = plot.DisplayName;
        dto.Visible = plot.Visible;
        dto.ZOrder = plot.ZOrder;
        dto.Opacity = plot.Opacity;
        dto.HitTestVisible = plot.HitTestVisible;
        dto.XAxisIndex = plot.XAxisIndex;
        dto.YAxisIndex = plot.YAxisIndex;
    }

    private static void ApplyCommon(PlotDto dto, PlotObject plot)
    {
        plot.Name = dto.Name;
        plot.DisplayName = dto.DisplayName;
        plot.Visible = dto.Visible;
        plot.ZOrder = dto.ZOrder;
        plot.Opacity = dto.Opacity;
        plot.HitTestVisible = dto.HitTestVisible;
        plot.XAxisIndex = dto.XAxisIndex;
        plot.YAxisIndex = dto.YAxisIndex;
    }

    private static double[][] ToJagged(double[,] values)
    {
        int rows = values.GetLength(0);
        int cols = values.GetLength(1);
        var jagged = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            var row = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                row[c] = values[r, c];
            }

            jagged[r] = row;
        }

        return jagged;
    }

    private static double[,] To2D(double[][] jagged)
    {
        int rows = jagged.Length;
        int cols = rows > 0 ? jagged[0].Length : 0;
        var values = new double[rows, cols];
        for (int r = 0; r < rows; r++)
        {
            double[] row = jagged[r];
            for (int c = 0; c < cols && c < row.Length; c++)
            {
                values[r, c] = row[c];
            }
        }

        return values;
    }
}
