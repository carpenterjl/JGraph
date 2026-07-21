using System.IO;
using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Data;
using JGraph.Objects;
using JGraph.Objects.Annotations;
using JGraph.Objects.Engineering;

namespace JGraph.Demo;

/// <summary>Builds the set of gallery examples exercising both the object-oriented and functional APIs.</summary>
internal static class ExampleCatalog
{
    public static IReadOnlyList<GalleryExample> Build() => new List<GalleryExample>
    {
        new("Line (OO API)", "Basic", LinePlot),
        new("Multi-series + legend", "Basic", MultiSeries),
        new("Line with markers", "Basic", MarkersLine),
        new("Scatter", "Basic", Scatter),
        new("Bar chart", "Basic", Bar),
        new("Horizontal bar", "Basic", HorizontalBar),
        new("Stem", "Basic", Stem),
        new("Histogram", "Basic", Histogram),
        new("Error bars", "Basic", ErrorBars),
        new("Heatmap", "Basic", Heatmap),
        new("Dashed line styles", "Styling", DashStyles),
        new("Annotations", "Styling", Annotations),
        new("Subplots (2×2)", "Layout", Subplots),
        new("Log Y axis (functional API)", "Scales", SemilogY),
        new("Category bars", "Scales", CategoryBars),
        new("Date/time axis", "Scales", DateTimeAxisExample),
        new("CSV import (Table API)", "Data", CsvImport),
        new("Bode plot", "Engineering", Bode),
        new("Nyquist", "Engineering", Nyquist),
        new("Polar (rose)", "Engineering", Polar),
        new("Smith chart", "Engineering", Smith),
        new("Spectrogram (chirp)", "Engineering", Spectrogram),
        new("Eye diagram", "Engineering", Eye),
        new("1,000,000 points", "Performance", MillionPoints),
        new("Functional API", "API", FunctionalApi),
    };

    private static FigureModel LinePlot()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] x = DemoData.Linspace(0, 4 * System.Math.PI, 500);
        double[] y = DemoData.Map(x, System.Math.Sin);

        LinePlot line = axes.AddLine(x, y);
        line.LineWidth = 2;
        line.DisplayName = "sin(x)";

        axes.Title = "Line Plot";
        axes.PrimaryXAxis.Label = "x";
        axes.PrimaryYAxis.Label = "sin(x)";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static FigureModel MultiSeries()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] x = DemoData.Linspace(0, 4 * System.Math.PI, 400);

        axes.AddLine(x, DemoData.Map(x, System.Math.Sin)).DisplayName = "sin(x)";
        axes.AddLine(x, DemoData.Map(x, System.Math.Cos)).DisplayName = "cos(x)";
        axes.AddLine(x, DemoData.Map(x, v => System.Math.Sin(v) * System.Math.Exp(-v / 8))).DisplayName = "damped";

        axes.Title = "Multiple Series";
        axes.PrimaryXAxis.Label = "x";
        axes.PrimaryYAxis.Label = "y";
        axes.Grid.ShowMajor = true;
        axes.Legend.Visible = true;
        return figure;
    }

    private static FigureModel MarkersLine()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] x = DemoData.Linspace(0, 10, 25);
        double[] y = DemoData.Map(x, v => System.Math.Sqrt(v));

        LinePlot line = axes.AddLine(x, y);
        line.Marker = MarkerType.Circle;
        line.MarkerSize = 7;
        line.LineWidth = 1.5;
        line.DisplayName = "sqrt(x)";

        axes.Title = "Line with Markers";
        axes.Grid.ShowMajor = true;
        axes.Legend.Visible = true;
        return figure;
    }

    private static FigureModel Scatter()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        (double[] xa, double[] ya) = DemoData.Cluster(200, 3, 3, 3, seed: 1);
        (double[] xb, double[] yb) = DemoData.Cluster(200, 6, 6, 3, seed: 2);

        ScatterPlot a = axes.AddScatter(xa, ya);
        a.DisplayName = "Group A";
        ScatterPlot b = axes.AddScatter(xb, yb);
        b.Marker = MarkerType.Diamond;
        b.DisplayName = "Group B";

        axes.Title = "Scatter";
        axes.PrimaryXAxis.Label = "x";
        axes.PrimaryYAxis.Label = "y";
        axes.Legend.Visible = true;
        return figure;
    }

    private static FigureModel Bar()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] positions = { 1, 2, 3, 4, 5, 6 };
        double[] values = { 4, 7, 2, 9, 5, 6 };

        axes.AddBar(positions, values).DisplayName = "Measurements";
        axes.Title = "Bar Chart";
        axes.PrimaryXAxis.Label = "Category";
        axes.PrimaryYAxis.Label = "Value";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static FigureModel HorizontalBar()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] positions = { 1, 2, 3, 4, 5 };
        double[] values = { 12, 19, 7, 15, 9 };

        BarPlot bar = axes.AddBar(positions, values);
        bar.Horizontal = true;
        bar.DisplayName = "Score";

        axes.Title = "Horizontal Bar";
        axes.PrimaryXAxis.Label = "Value";
        axes.PrimaryYAxis.Label = "Category";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static FigureModel Stem()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] n = DemoData.Linspace(0, 20, 21);
        double[] y = DemoData.Map(n, v => System.Math.Sin(v * 0.6) * System.Math.Exp(-v / 15));

        StemPlot stem = axes.AddStem(n, y);
        stem.DisplayName = "impulse response";

        axes.Title = "Stem Plot";
        axes.PrimaryXAxis.Label = "n";
        axes.PrimaryYAxis.Label = "h[n]";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static FigureModel Histogram()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] samples = DemoData.Gaussian(2000, mean: 0, standardDeviation: 1, seed: 11);

        HistogramPlot hist = axes.AddHistogram(samples, binCount: 30);
        hist.DisplayName = "N(0, 1)";

        axes.Title = "Histogram (2,000 samples)";
        axes.PrimaryXAxis.Label = "value";
        axes.PrimaryYAxis.Label = "count";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static FigureModel ErrorBars()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] x = DemoData.Linspace(1, 10, 10);
        double[] y = DemoData.Map(x, v => System.Math.Log(v) * 3);
        double[] err = DemoData.Map(x, v => 0.3 + (0.1 * v));

        ErrorBarPlot eb = axes.AddErrorBar(x, y, err);
        eb.DisplayName = "measurement ± σ";

        axes.Title = "Error Bars";
        axes.PrimaryXAxis.Label = "dose";
        axes.PrimaryYAxis.Label = "response";
        axes.Grid.ShowMajor = true;
        axes.Legend.Visible = true;
        return figure;
    }

    private static FigureModel Heatmap()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        // A radial ripple: sinc-like field over [-6, 6]².
        double[,] field = DemoData.Field(120, 120, -6, 6, -6, 6, (x, y) =>
        {
            double r = System.Math.Sqrt((x * x) + (y * y)) + 1e-6;
            return System.Math.Sin(r) / r;
        });

        ImagePlot image = axes.AddImage(field, new DataRange(-6, 6), new DataRange(-6, 6));
        image.Colormap = Colormap.Viridis;
        image.Interpolate = true;
        image.DisplayName = "sinc";

        axes.Title = "Heatmap (imagesc)";
        axes.PrimaryXAxis.Label = "x";
        axes.PrimaryYAxis.Label = "y";
        return figure;
    }

    private static FigureModel Subplots()
    {
        var figure = new FigureModel();
        figure.Title = "Subplot Grid";

        double[] x = DemoData.Linspace(0, 4 * System.Math.PI, 300);
        figure.AddSubplot(2, 2, 1).AddLine(x, DemoData.Map(x, System.Math.Sin)).DisplayName = "sin";
        figure.Axes[0].Title = "sin";

        AxesModel cos = figure.AddSubplot(2, 2, 2);
        cos.AddLine(x, DemoData.Map(x, System.Math.Cos)).Color = Colors.Orange;
        cos.Title = "cos";

        AxesModel bars = figure.AddSubplot(2, 2, 3);
        bars.AddBar(new double[] { 1, 2, 3, 4, 5 }, new double[] { 3, 6, 2, 8, 5 });
        bars.Title = "bar";

        AxesModel heat = figure.AddSubplot(2, 2, 4);
        heat.AddImage(DemoData.Field(60, 60, -3, 3, -3, 3, (a, b) => System.Math.Sin(a) * System.Math.Cos(b)));
        heat.Title = "field";

        return figure;
    }

    private static FigureModel CategoryBars()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        string[] categories = { "North", "South", "East", "West", "Central" };
        double[] values = { 42, 30, 55, 18, 47 };

        axes.AddBar(categories, values).DisplayName = "sales";

        axes.Title = "Category Axis";
        axes.PrimaryYAxis.Label = "units (k)";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static FigureModel DateTimeAxisExample()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        var start = new DateTime(2026, 1, 1);
        var times = new DateTime[120];
        var values = new double[times.Length];
        var random = new Random(3);
        double level = 100;
        for (int i = 0; i < times.Length; i++)
        {
            times[i] = start.AddDays(i);
            level += random.NextDouble() - 0.45;
            values[i] = level;
        }

        axes.AddLine(times, values).DisplayName = "daily index";

        axes.Title = "Date/Time Axis";
        axes.PrimaryXAxis.Label = "date";
        axes.PrimaryYAxis.Label = "index";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static FigureModel CsvImport()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        // Read the bundled measurement file: a time column plus two numeric channels. The time column
        // is inferred as date/time, so the X axis is configured as a date axis automatically.
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "sample-measurement.csv");
        Table table = Table.ReadCsv(path);

        axes.AddLine(table, "time", "voltage").LineWidth = 1.5;
        axes.AddLine(table, "time", "current").Color = Colors.Orange;

        axes.Title = "CSV Import (readtable)";
        axes.Grid.ShowMajor = true;
        axes.Legend.Visible = true;
        return figure;
    }

    private static FigureModel DashStyles()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] x = DemoData.Linspace(0, 10, 200);

        LinePlot solid = axes.AddLine(x, DemoData.Map(x, v => System.Math.Sin(v)));
        solid.DashStyle = DashStyle.Solid;
        solid.DisplayName = "Solid";

        LinePlot dash = axes.AddLine(x, DemoData.Map(x, v => System.Math.Sin(v) + 1));
        dash.DashStyle = DashStyle.Dash;
        dash.DisplayName = "Dash";

        LinePlot dot = axes.AddLine(x, DemoData.Map(x, v => System.Math.Sin(v) + 2));
        dot.DashStyle = DashStyle.Dot;
        dot.DisplayName = "Dot";

        LinePlot dashDot = axes.AddLine(x, DemoData.Map(x, v => System.Math.Sin(v) + 3));
        dashDot.DashStyle = DashStyle.DashDot;
        dashDot.DisplayName = "DashDot";

        axes.Title = "Dashed Line Styles";
        axes.Legend.Visible = true;
        return figure;
    }

    private static FigureModel Annotations()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] x = DemoData.Linspace(0, 4 * System.Math.PI, 400);
        double[] y = DemoData.Map(x, v => System.Math.Sin(v) * System.Math.Exp(-v / 8));
        axes.AddLine(x, y).DisplayName = "damped oscillation";

        // Data-space annotations: they follow zoom and pan.
        double peakX = System.Math.PI / 2;
        double peakY = System.Math.Sin(peakX) * System.Math.Exp(-peakX / 8);
        axes.AddArrow(peakX + 2.2, peakY - 0.25, peakX + 0.2, peakY - 0.04);
        TextAnnotation peak = axes.AddText(peakX + 2.3, peakY - 0.25, "global maximum");
        peak.Color = Colors.Black; // explicit box, so pin the ink too
        peak.Background = Colors.White.WithOpacity(0.8);
        peak.BorderColor = Colors.Gray;

        RectangleAnnotation window = axes.AddRectangleAnnotation(6.0, -0.35, 9.5, 0.35);
        window.DashStyle = DashStyle.Dash;
        window.Fill = Color.FromArgb(24, 0x1E, 0x88, 0xE5);
        axes.AddText(6.1, 0.38, "settling window").FontSize = 11;

        EllipseAnnotation ring = axes.AddEllipseAnnotation(peakX - 0.6, peakY - 0.12, peakX + 0.6, peakY + 0.12);
        ring.Stroke = Colors.Red;

        // A figure-space annotation: anchored to the window, not the data.
        TextAnnotation note = figure.AddText(0.99, 0.99, "figure-space note (stays put when you zoom)");
        note.HorizontalAlignment = HorizontalAlignment.Right;
        note.VerticalAlignment = VerticalAlignment.Bottom;
        note.FontSize = 11;

        axes.Title = "Annotations";
        axes.PrimaryXAxis.Label = "t (s)";
        axes.PrimaryYAxis.Label = "response";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static FigureModel SemilogY()
    {
        JG.Reset();
        JG.Figure();
        double[] x = DemoData.Linspace(0, 10, 200);
        double[] y = DemoData.Map(x, v => System.Math.Exp(v * 0.8));
        JG.SemilogY(x, y);
        JG.Title("Semilog Y (exp growth)");
        JG.XLabel("x");
        JG.YLabel("exp(0.8 x)");
        JG.Grid(true);
        return JG.CurrentFigure;
    }

    private static FigureModel Bode()
    {
        var figure = new FigureModel { Title = "Bode Plot" };
        // Second-order low-pass: wn = 10 rad/s, zeta = 0.5 → 100 / (s² + 10s + 100).
        figure.AddBode(new double[] { 100 }, new double[] { 1, 10, 100 }, 0.1, 1000);
        return figure;
    }

    private static FigureModel Nyquist()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        axes.AddNyquist(new double[] { 1 }, new double[] { 1, 1, 1 }, 0.01, 100); // 1/(s²+s+1)
        axes.Title = "Nyquist";
        axes.PrimaryXAxis.Label = "Real";
        axes.PrimaryYAxis.Label = "Imaginary";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static FigureModel Polar()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] theta = DemoData.Linspace(0, 2 * System.Math.PI, 720);
        double[] r = DemoData.Map(theta, t => 2 * System.Math.Abs(System.Math.Cos(3 * t)));

        LinePlot line = axes.AddPolar(theta, r);
        line.LineWidth = 2;
        line.Color = Colors.Purple;
        line.DisplayName = "r = 2|cos 3θ|";

        axes.Title = "Polar (rose)";
        return figure;
    }

    private static FigureModel Smith()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        // A constant-resistance (R = 0.5) frequency sweep traces a constant-R circle.
        const int n = 200;
        var zr = new double[n];
        var zi = new double[n];
        for (int i = 0; i < n; i++)
        {
            zr[i] = 0.5;
            zi[i] = 5.0 * i / (n - 1);
        }

        LinePlot line = axes.AddSmith(zr, zi);
        line.LineWidth = 2;
        line.Color = Colors.Red;
        line.DisplayName = "R = 0.5 + jX";

        axes.Title = "Smith Chart";
        return figure;
    }

    private static FigureModel Spectrogram()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] signal = DemoData.Chirp(4000, sampleRate: 2000, startHz: 100, endHz: 700);

        axes.AddSpectrogram(signal, sampleRate: 2000, windowSize: 256, overlap: 200);

        axes.Title = "Spectrogram (chirp)";
        axes.PrimaryXAxis.Label = "Time (s)";
        axes.PrimaryYAxis.Label = "Frequency (Hz)";
        return figure;
    }

    private static FigureModel Eye()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        double[] signal = DemoData.NrzEye(symbols: 200, samplesPerSymbol: 32, noise: 0.15, seed: 7);

        EyeDiagramPlot eye = axes.AddEyeDiagram(signal, samplesPerSymbol: 32);
        eye.LineWidth = 0.8;
        eye.Opacity = 0.25;
        eye.Color = Colors.Blue;

        axes.Title = "Eye Diagram";
        axes.PrimaryXAxis.Label = "Symbol periods";
        axes.PrimaryYAxis.Label = "Amplitude";
        return figure;
    }

    private static FigureModel MillionPoints()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();
        (double[] xs, double[] ys) = DemoData.RandomWalk(1_000_000);

        LinePlot line = axes.AddLine(xs, ys);
        line.LineWidth = 1;
        line.DisplayName = "1M-point random walk";

        axes.Title = "1,000,000 Points (auto-decimated)";
        axes.PrimaryXAxis.Label = "sample";
        axes.PrimaryYAxis.Label = "value";
        axes.Grid.ShowMajor = true;
        return figure;
    }

    private static FigureModel FunctionalApi()
    {
        JG.Reset();
        JG.Figure();
        double[] x = DemoData.Linspace(0, 2 * System.Math.PI, 60);

        JG.Hold(true);
        JG.Plot(x, DemoData.Map(x, System.Math.Sin), "r-");
        JG.Plot(x, DemoData.Map(x, System.Math.Cos), "b--o");
        JG.Title("Voltage");
        JG.XLabel("time (s)");
        JG.YLabel("amplitude");
        JG.Grid(true);
        JG.Legend("sin", "cos");
        return JG.CurrentFigure;
    }
}
