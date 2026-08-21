using JGraph.Core.Drawing;
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
                Steps = p.Steps,
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
                SizeData = p.SizeData?.ToArray(),
                ColorData = p.ColorData?.ToArray(),
                BubbleSizing = p.BubbleSizing,
                XJitter = p.XJitter,
                YJitter = p.YJitter,
                XJitterWidth = p.XJitterWidthOverride,
                YJitterWidth = p.YJitterWidthOverride,
                Colormap = DtoConvert.ToDto(p.Colormap),
                AutoScaleColor = p.AutoScaleColor,
                ColorMin = p.ColorMin,
                ColorMax = p.ColorMax,
            },
            BarPlot p => new BarPlotDto
            {
                Series = DtoConvert.ToDto(p.Data),
                FillColor = p.FillColor,
                EdgeColor = p.EdgeColor,
                EdgeWidth = p.EdgeWidth,
                FaceAlpha = p.FaceAlpha,
                Dash = p.Dash,
                BarWidthFraction = p.BarWidthFraction,
                Baseline = p.Baseline,
                Horizontal = p.Horizontal,
                GroupIndex = p.GroupIndex,
                GroupCount = p.GroupCount,
                PositionOffset = p.PositionOffset,
                LowerEdge = p.LowerEdge,
            },
            AreaPlot p => new AreaPlotDto
            {
                Series = DtoConvert.ToDto(p.Data),
                FaceColor = p.FaceColor,
                EdgeColor = p.EdgeColor,
                FaceAlpha = p.FaceAlpha,
                LineWidth = p.LineWidth,
                Dash = p.Dash,
                BaseValue = p.BaseValue,
                ShowBaseLine = p.ShowBaseLine,
                LowerEdge = p.LowerEdge,
            },
            PiePlot p => new PiePlotDto
            {
                Values = p.Values.ToArray(),
                Explode = p.Explode,
                Labels = p.Labels,
                Colormap = DtoConvert.ToDto(p.Colormap),
                EdgeColor = p.EdgeColor,
                LineWidth = p.LineWidth,
                FaceAlpha = p.FaceAlpha,
                StartAngle = p.StartAngle,
                Clockwise = p.Clockwise,
                ShowLabels = p.ShowLabels,
                LabelRadius = p.LabelRadius,
                LabelStyle = p.LabelStyle is { } pieLabel ? DtoConvert.ToDto(pieLabel) : null,
            },
            HeatmapPlot p => new HeatmapPlotDto
            {
                ColorData = ToJagged(p.ColorData),
                XData = p.XData,
                YData = p.YData,
                Colormap = DtoConvert.ToDto(p.Colormap),
                ColorLimits = p.ColorLimits is { } limits ? DtoConvert.ToDto(limits) : null,
                ColorScaling = p.ColorScaling,
                ShowCellLabels = p.ShowCellLabels,
                CellLabelColor = p.CellLabelColor,
                CellLabelFormat = p.CellLabelFormat,
                CellLabelStyle = DtoConvert.ToDto(p.CellLabelStyle),
                GridVisible = p.GridVisible,
                GridColor = p.GridColor,
                MissingDataColor = p.MissingDataColor,
                MissingDataLabel = p.MissingDataLabel,
            },
            BinScatterPlot p => new BinScatterPlotDto
            {
                XData = p.X.ToArray(),
                YData = p.Y.ToArray(),
                NumBinsX = p.NumBinsX,
                NumBinsY = p.NumBinsY,
                XLimits = p.XLimits is { } xSpan ? DtoConvert.ToDto(xSpan) : null,
                YLimits = p.YLimits is { } ySpan ? DtoConvert.ToDto(ySpan) : null,
                ShowEmptyBins = p.ShowEmptyBins,
                Colormap = DtoConvert.ToDto(p.Colormap),
                ColorLimits = p.ColorLimits is { } counts ? DtoConvert.ToDto(counts) : null,
            },
            BoxChartPlot p => new BoxChartPlotDto
            {
                XData = p.XData,
                YData = p.YData,
                BoxFaceColor = p.BoxFaceColor,
                BoxFaceAlpha = p.BoxFaceAlpha,
                BoxEdgeColor = p.BoxEdgeColor,
                BoxMedianLineColor = p.BoxMedianLineColor,
                BoxWidth = p.BoxWidth,
                LineWidth = p.LineWidth,
                WhiskerLineColor = p.WhiskerLineColor,
                WhiskerLineStyle = p.WhiskerLineStyle,
                MarkerStyle = p.MarkerStyle,
                MarkerSize = p.MarkerSize,
                MarkerColor = p.MarkerColor,
                Notch = p.Notch,
                JitterOutliers = p.JitterOutliers,
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
            PolarHistogramPlot p => new PolarHistogramPlotDto
            {
                Data = p.Data,
                BinEdges = p.BinEdges,
                BinCounts = p.BinCounts,
                Normalization = p.Normalization,
                DisplayStyle = p.DisplayStyle,
                FaceColor = p.FaceColor,
                EdgeColor = p.EdgeColor,
                FaceAlpha = p.FaceAlpha,
                EdgeAlpha = p.EdgeAlpha,
                LineWidth = p.LineWidth,
                LineStyle = p.LineStyle,
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
            RgbImagePlot p => new RgbImagePlotDto
            {
                PixelsBase64 = PixelsToBase64(p.Pixels),
                Width = p.Width,
                Height = p.Height,
                XExtent = DtoConvert.ToDto(p.XExtent),
                YExtent = DtoConvert.ToDto(p.YExtent),
                Interpolate = p.Interpolate,
            },
            SurfacePlot p => new SurfacePlotDto
            {
                X = p.X.ToArray(),
                Y = p.Y.ToArray(),
                Z = ToJagged(p.Z),
                CData = p.CData is { } cd ? ToJagged(cd) : null,
                XGrid = p.XGrid is { } xg ? ToJagged(xg) : null,
                YGrid = p.YGrid is { } yg ? ToJagged(yg) : null,
                AlphaData = p.AlphaData is { } ad ? ToJagged(ad) : null,
                FaceAlphaFlat = p.FaceAlphaFlat,
                Colormap = DtoConvert.ToDto(p.Colormap),
                Style = p.Style,
                Shading = p.Shading,
                ShowContourBelow = p.ShowContourBelow,
                ContourLevels = p.ContourLevels,
                FaceColor = p.FaceColor,
                EdgeColor = p.EdgeColor,
                EdgeWidth = p.EdgeWidth,
                AutoScaleColor = p.AutoScaleColor,
                ColorMin = p.ColorMin,
                ColorMax = p.ColorMax,
                FaceLighting = p.FaceLighting,
                AmbientStrength = p.AmbientStrength,
                DiffuseStrength = p.DiffuseStrength,
                SpecularStrength = p.SpecularStrength,
                SpecularExponent = p.SpecularExponent,
                SpecularColorReflectance = p.SpecularColorReflectance,
            },
            ContourPlot p => new ContourPlotDto
            {
                X = p.X.ToArray(),
                Y = p.Y.ToArray(),
                Z = ToJagged(p.Z),
                Levels = p.Levels?.ToArray(),
                LevelCount = p.LevelCount,
                Filled = p.Filled,
                Colormap = DtoConvert.ToDto(p.Colormap),
                LineWidth = p.LineWidth,
                AutoScaleColor = p.AutoScaleColor,
                ColorMin = p.ColorMin,
                ColorMax = p.ColorMax,
                ShowText = p.ShowText,
                LabelLevels = p.LabelLevels?.ToArray(),
                LabelStyle = p.LabelStyle is { } contourLabel ? DtoConvert.ToDto(contourLabel) : null,
            },
            ConstantLinePlot p => new ConstantLinePlotDto
            {
                Direction = p.Direction,
                Value = p.Value,
                Color = p.Color,
                LineWidth = p.LineWidth,
                Dash = p.Dash,
                Label = p.Label,
                LabelStyle = p.LabelStyle is { } lineLabel ? DtoConvert.ToDto(lineLabel) : null,
                LabelHorizontalAlignment = p.LabelHorizontalAlignment,
                LabelVerticalAlignment = p.LabelVerticalAlignment,
            },
            Line3DPlot p => new Line3DPlotDto
            {
                X = p.X.ToArray(),
                Y = p.Y.ToArray(),
                Z = p.Z.ToArray(),
                Color = p.Color,
                LineWidth = p.LineWidth,
                DashStyle = p.DashStyle,
                Marker = p.Marker,
                MarkerSize = p.MarkerSize,
                MarkerFill = p.MarkerFill,
            },
            Scatter3DPlot p => new Scatter3DPlotDto
            {
                X = p.X.ToArray(),
                Y = p.Y.ToArray(),
                Z = p.Z.ToArray(),
                SizeData = p.SizeData?.ToArray(),
                ColorData = p.ColorData?.ToArray(),
                Color = p.Color,
                Marker = p.Marker,
                MarkerSize = p.MarkerSize,
                Filled = p.Filled,
                EdgeWidth = p.EdgeWidth,
                BubbleSizing = p.BubbleSizing,
                XJitter = p.XJitter,
                YJitter = p.YJitter,
                ZJitter = p.ZJitter,
                XJitterWidth = p.XJitterWidthOverride,
                YJitterWidth = p.YJitterWidthOverride,
                ZJitterWidth = p.ZJitterWidthOverride,
                Colormap = DtoConvert.ToDto(p.Colormap),
                AutoScaleColor = p.AutoScaleColor,
                ColorMin = p.ColorMin,
                ColorMax = p.ColorMax,
            },
            Stem3DPlot p => new Stem3DPlotDto
            {
                X = p.X.ToArray(),
                Y = p.Y.ToArray(),
                Z = p.Z.ToArray(),
                Color = p.Color,
                LineWidth = p.LineWidth,
                Dash = p.DashStyle,
                Baseline = p.Baseline,
                Marker = p.Marker,
                MarkerSize = p.MarkerSize,
                MarkerFill = p.MarkerFill,
            },
            Bar3DPlot p => new Bar3DPlotDto
            {
                ZData = ToJagged(p.ZData),
                RowPositions = p.RowPositions,
                Style = p.Style,
                Horizontal = p.Horizontal,
                BarWidth = p.BarWidth,
                Baseline = p.Baseline,
                FaceColor = p.FaceColor,
                EdgeColor = p.EdgeColor,
                EdgeVisible = p.EdgeColor is not null,
                LineWidth = p.LineWidth,
                FaceAlpha = p.FaceAlpha,
                Colormap = DtoConvert.ToDto(p.Colormap),
            },
            Pie3DPlot p => new Pie3DPlotDto
            {
                Values = p.Values.ToArray(),
                Explode = p.Explode,
                Labels = p.Labels,
                Colormap = DtoConvert.ToDto(p.Colormap),
                EdgeColor = p.EdgeColor,
                EdgeVisible = p.EdgeColor is not null,
                LineWidth = p.LineWidth,
                FaceAlpha = p.FaceAlpha,
                StartAngle = p.StartAngle,
                Clockwise = p.Clockwise,
                Height = p.Height,
                ShowLabels = p.ShowLabels,
                LabelRadius = p.LabelRadius,
                LabelStyle = p.LabelStyle is { } pie3Label ? DtoConvert.ToDto(pie3Label) : null,
            },
            PatchPlot p => new PatchPlotDto
            {
                X = p.X.ToArray(),
                Y = p.Y.ToArray(),
                Z = p.Z.ToArray(),
                Faces = p.Faces.Select(f => f.ToArray()).ToArray(),
                ColorData = p.ColorData?.ToArray(),
                FaceColor = p.FaceColor,
                FaceVisible = p.FaceVisible,
                EdgeColor = p.EdgeColor,
                EdgeVisible = p.EdgeColor is not null,
                EdgeWidth = p.EdgeWidth,
                Shading = p.Shading,
                Colormap = DtoConvert.ToDto(p.Colormap),
                AutoScaleColor = p.AutoScaleColor,
                ColorMin = p.ColorMin,
                ColorMax = p.ColorMax,
            },
            QuiverPlot p => new QuiverPlotDto
            {
                X = p.X.ToArray(),
                Y = p.Y.ToArray(),
                Z = p.Z.ToArray(),
                U = p.U.ToArray(),
                V = p.V.ToArray(),
                W = p.W.ToArray(),
                Color = p.Color,
                LineWidth = p.LineWidth,
                AutoScale = p.AutoScale,
                AutoScaleFactor = p.AutoScaleFactor,
                Scale = p.Scale,
                ShowArrowHead = p.ShowArrowHead,
                MaxHeadSize = p.MaxHeadSize,
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
                Steps = d.Steps,
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
                SizeData = d.SizeData,
                ColorData = d.ColorData,
                BubbleSizing = d.BubbleSizing,
                XJitter = d.XJitter,
                YJitter = d.YJitter,
                XJitterWidthOverride = d.XJitterWidth,
                YJitterWidthOverride = d.YJitterWidth,
                Colormap = DtoConvert.ToColormap(d.Colormap),
                AutoScaleColor = d.AutoScaleColor,
                ColorMin = d.ColorMin,
                ColorMax = d.ColorMax,
            },
            BarPlotDto d => new BarPlot(DtoConvert.ToSeries(d.Series))
            {
                FillColor = d.FillColor,
                EdgeColor = d.EdgeColor,
                EdgeWidth = d.EdgeWidth,
                FaceAlpha = d.FaceAlpha,
                Dash = d.Dash,
                BarWidthFraction = d.BarWidthFraction,
                Baseline = d.Baseline,
                Horizontal = d.Horizontal,
                GroupIndex = d.GroupIndex,
                GroupCount = d.GroupCount,
                PositionOffset = d.PositionOffset,
                LowerEdge = d.LowerEdge,
            },
            AreaPlotDto d => new AreaPlot(DtoConvert.ToSeries(d.Series))
            {
                FaceColor = d.FaceColor,
                EdgeColor = d.EdgeColor,
                FaceAlpha = d.FaceAlpha,
                LineWidth = d.LineWidth,
                Dash = d.Dash,
                BaseValue = d.BaseValue,
                ShowBaseLine = d.ShowBaseLine,
                LowerEdge = d.LowerEdge,
            },
            PiePlotDto d => new PiePlot(d.Values)
            {
                Explode = d.Explode,
                Labels = d.Labels,
                Colormap = DtoConvert.ToColormap(d.Colormap),
                EdgeColor = d.EdgeColor,
                LineWidth = d.LineWidth,
                FaceAlpha = d.FaceAlpha,
                StartAngle = d.StartAngle,
                Clockwise = d.Clockwise,
                ShowLabels = d.ShowLabels,
                LabelRadius = d.LabelRadius,
                LabelStyle = d.LabelStyle is { } pieLabel ? DtoConvert.ToTextStyle(pieLabel) : null,
            },
            HeatmapPlotDto d => new HeatmapPlot(To2D(d.ColorData))
            {
                XData = d.XData,
                YData = d.YData,
                Colormap = DtoConvert.ToColormap(d.Colormap),
                ColorLimits = d.ColorLimits is { } limits ? DtoConvert.ToRange(limits) : null,
                ColorScaling = d.ColorScaling,
                ShowCellLabels = d.ShowCellLabels,
                CellLabelColor = d.CellLabelColor,
                CellLabelFormat = d.CellLabelFormat,
                CellLabelStyle = d.CellLabelStyle is { } cellLabel
                    ? DtoConvert.ToTextStyle(cellLabel)
                    : new TextStyle(Colors.Black, 10),
                GridVisible = d.GridVisible,
                GridColor = d.GridColor,
                MissingDataColor = d.MissingDataColor,
                MissingDataLabel = d.MissingDataLabel,
            },
            BinScatterPlotDto d => new BinScatterPlot(d.XData, d.YData)
            {
                NumBinsX = d.NumBinsX,
                NumBinsY = d.NumBinsY,
                XLimits = d.XLimits is { } xSpan ? DtoConvert.ToRange(xSpan) : null,
                YLimits = d.YLimits is { } ySpan ? DtoConvert.ToRange(ySpan) : null,
                ShowEmptyBins = d.ShowEmptyBins,
                Colormap = DtoConvert.ToColormap(d.Colormap),
                ColorLimits = d.ColorLimits is { } counts ? DtoConvert.ToRange(counts) : null,
            },
            BoxChartPlotDto d => new BoxChartPlot(d.XData, d.YData)
            {
                BoxFaceColor = d.BoxFaceColor,
                BoxFaceAlpha = d.BoxFaceAlpha,
                BoxEdgeColor = d.BoxEdgeColor,
                BoxMedianLineColor = d.BoxMedianLineColor,
                BoxWidth = d.BoxWidth,
                LineWidth = d.LineWidth,
                WhiskerLineColor = d.WhiskerLineColor,
                WhiskerLineStyle = d.WhiskerLineStyle,
                MarkerStyle = d.MarkerStyle,
                MarkerSize = d.MarkerSize,
                MarkerColor = d.MarkerColor,
                Notch = d.Notch,
                JitterOutliers = d.JitterOutliers,
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
            PolarHistogramPlotDto d => ToPolarHistogram(d),
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
            RgbImagePlotDto d => new RgbImagePlot(PixelsFromBase64(d.PixelsBase64, d.Width, d.Height), d.Width, d.Height)
            {
                XExtent = DtoConvert.ToRange(d.XExtent),
                YExtent = DtoConvert.ToRange(d.YExtent),
                Interpolate = d.Interpolate,
            },
            SurfacePlotDto d => ToSurface(d),
            ContourPlotDto d => new ContourPlot(d.X, d.Y, To2D(d.Z))
            {
                Levels = d.Levels,
                LevelCount = d.LevelCount,
                Filled = d.Filled,
                Colormap = DtoConvert.ToColormap(d.Colormap),
                LineWidth = d.LineWidth,
                AutoScaleColor = d.AutoScaleColor,
                ColorMin = d.ColorMin,
                ColorMax = d.ColorMax,
                ShowText = d.ShowText,
                LabelLevels = d.LabelLevels,
                LabelStyle = d.LabelStyle is { } contourLabel ? DtoConvert.ToTextStyle(contourLabel) : null,
            },
            ConstantLinePlotDto d => new ConstantLinePlot(d.Direction, d.Value)
            {
                Color = d.Color,
                LineWidth = d.LineWidth,
                Dash = d.Dash,
                Label = d.Label,
                LabelStyle = d.LabelStyle is { } lineLabel ? DtoConvert.ToTextStyle(lineLabel) : null,
                LabelHorizontalAlignment = d.LabelHorizontalAlignment,
                LabelVerticalAlignment = d.LabelVerticalAlignment,
            },
            Line3DPlotDto d => new Line3DPlot(d.X, d.Y, d.Z)
            {
                Color = d.Color,
                LineWidth = d.LineWidth,
                DashStyle = d.DashStyle,
                Marker = d.Marker,
                MarkerSize = d.MarkerSize,
                MarkerFill = d.MarkerFill,
            },
            Scatter3DPlotDto d => new Scatter3DPlot(d.X, d.Y, d.Z)
            {
                SizeData = d.SizeData,
                ColorData = d.ColorData,
                Color = d.Color,
                Marker = d.Marker,
                MarkerSize = d.MarkerSize,
                Filled = d.Filled,
                EdgeWidth = d.EdgeWidth,
                BubbleSizing = d.BubbleSizing,
                XJitter = d.XJitter,
                YJitter = d.YJitter,
                ZJitter = d.ZJitter,
                XJitterWidthOverride = d.XJitterWidth,
                YJitterWidthOverride = d.YJitterWidth,
                ZJitterWidthOverride = d.ZJitterWidth,
                Colormap = DtoConvert.ToColormap(d.Colormap),
                AutoScaleColor = d.AutoScaleColor,
                ColorMin = d.ColorMin,
                ColorMax = d.ColorMax,
            },
            Stem3DPlotDto d => new Stem3DPlot(d.X, d.Y, d.Z)
            {
                Color = d.Color,
                LineWidth = d.LineWidth,
                DashStyle = d.Dash,
                Baseline = d.Baseline,
                Marker = d.Marker,
                MarkerSize = d.MarkerSize,
                MarkerFill = d.MarkerFill,
            },
            Bar3DPlotDto d => new Bar3DPlot(To2D(d.ZData))
            {
                RowPositions = d.RowPositions,
                Style = d.Style,
                Horizontal = d.Horizontal,
                BarWidth = d.BarWidth,
                Baseline = d.Baseline,
                FaceColor = d.FaceColor,
                EdgeColor = d.EdgeVisible ? d.EdgeColor ?? JGraph.Core.Drawing.Colors.Black : null,
                LineWidth = d.LineWidth,
                FaceAlpha = d.FaceAlpha,
                Colormap = DtoConvert.ToColormap(d.Colormap),
            },
            Pie3DPlotDto d => new Pie3DPlot(d.Values)
            {
                Explode = d.Explode,
                Labels = d.Labels,
                Colormap = DtoConvert.ToColormap(d.Colormap),
                EdgeColor = d.EdgeVisible ? d.EdgeColor ?? JGraph.Core.Drawing.Colors.White : null,
                LineWidth = d.LineWidth,
                FaceAlpha = d.FaceAlpha,
                StartAngle = d.StartAngle,
                Clockwise = d.Clockwise,
                Height = d.Height,
                ShowLabels = d.ShowLabels,
                LabelRadius = d.LabelRadius,
                LabelStyle = d.LabelStyle is { } pie3Label ? DtoConvert.ToTextStyle(pie3Label) : null,
            },
            PatchPlotDto d => new PatchPlot(d.X, d.Y, d.Z, d.Faces)
            {
                ColorData = d.ColorData,
                FaceColor = d.FaceColor,
                FaceVisible = d.FaceVisible,
                EdgeColor = d.EdgeVisible ? d.EdgeColor ?? JGraph.Core.Drawing.Colors.Black : null,
                EdgeWidth = d.EdgeWidth,
                Shading = d.Shading,
                Colormap = DtoConvert.ToColormap(d.Colormap),
                AutoScaleColor = d.AutoScaleColor,
                ColorMin = d.ColorMin,
                ColorMax = d.ColorMax,
            },
            QuiverPlotDto d => new QuiverPlot(d.X, d.Y, d.Z, d.U, d.V, d.W)
            {
                Color = d.Color,
                LineWidth = d.LineWidth,
                AutoScale = d.AutoScale,
                AutoScaleFactor = d.AutoScaleFactor,
                Scale = d.Scale,
                ShowArrowHead = d.ShowArrowHead,
                MaxHeadSize = d.MaxHeadSize,
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
        dto.SeriesIndex = plot.SeriesIndex;
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
        plot.SeriesIndex = dto.SeriesIndex;
    }

    /// <summary>
    /// Rebuilds a surface. The two grid forms need different constructors, and an object initializer
    /// cannot follow a conditional, so this is a method rather than a switch arm.
    /// </summary>
    private static SurfacePlot ToSurface(SurfacePlotDto d)
    {
        SurfacePlot surface = d.XGrid is not null && d.YGrid is not null
            ? new SurfacePlot(To2D(d.XGrid), To2D(d.YGrid), To2D(d.Z))
            : new SurfacePlot(d.X, d.Y, To2D(d.Z));

        surface.CData = d.CData is null ? null : To2D(d.CData);
        surface.Colormap = DtoConvert.ToColormap(d.Colormap);
        surface.Style = d.Style;
        surface.Shading = d.Shading;
        surface.ShowContourBelow = d.ShowContourBelow;
        surface.ContourLevels = d.ContourLevels;
        surface.FaceColor = d.FaceColor;
        surface.EdgeColor = d.EdgeColor;
        surface.EdgeWidth = d.EdgeWidth;
        surface.AutoScaleColor = d.AutoScaleColor;
        surface.ColorMin = d.ColorMin;
        surface.ColorMax = d.ColorMax;
        surface.FaceLighting = d.FaceLighting;
        surface.AmbientStrength = d.AmbientStrength;
        surface.DiffuseStrength = d.DiffuseStrength;
        surface.SpecularStrength = d.SpecularStrength;
        surface.SpecularExponent = d.SpecularExponent;
        surface.SpecularColorReflectance = d.SpecularColorReflectance;

        // The alpha data has to land after Z, since the surface checks it against the grid it is for.
        surface.AlphaData = d.AlphaData is null ? null : To2D(d.AlphaData);
        surface.FaceAlphaFlat = d.FaceAlphaFlat;
        return surface;
    }

    /// <summary>
    /// A polar histogram from its saved form. The counts are read back rather than counted again when
    /// there is no data behind them, which is the whole of the difference between the two ways one can
    /// be made — a histogram given its counts has nothing left to count.
    /// </summary>
    private static PolarHistogramPlot ToPolarHistogram(PolarHistogramPlotDto d)
    {
        PolarHistogramPlot plot = d.Data.Length > 0
            ? new PolarHistogramPlot(d.Data, d.BinEdges)
            : PolarHistogramPlot.FromCounts(d.BinEdges, d.BinCounts);

        plot.Normalization = d.Normalization;
        plot.DisplayStyle = d.DisplayStyle;
        plot.FaceColor = d.FaceColor;
        plot.EdgeColor = d.EdgeColor;
        plot.FaceAlpha = d.FaceAlpha;
        plot.EdgeAlpha = d.EdgeAlpha;
        plot.LineWidth = d.LineWidth;
        plot.LineStyle = d.LineStyle;
        return plot;
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

    private static string PixelsToBase64(uint[] pixels)
    {
        var bytes = new byte[pixels.Length * sizeof(uint)];
        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static uint[] PixelsFromBase64(string base64, int width, int height)
    {
        byte[] bytes = Convert.FromBase64String(base64 ?? string.Empty);
        long expected = (long)width * height;
        var pixels = new uint[expected];
        int available = Math.Min(bytes.Length / sizeof(uint), (int)expected);
        Buffer.BlockCopy(bytes, 0, pixels, 0, available * sizeof(uint));
        return pixels;
    }
}
