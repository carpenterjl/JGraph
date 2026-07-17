using JGraph.Core.Model;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Application.Services;

/// <summary>Builds a multi-series sample figure demonstrating the framework on startup.</summary>
public sealed class SampleFigureFactory : IFigureFactory
{
    public FigureModel CreateSample()
    {
        var figure = new FigureModel();
        AxesModel axes = figure.AddAxes();

        double[] x = Linspace(0, 4 * System.Math.PI, 600);
        axes.AddLine(x, Map(x, System.Math.Sin)).DisplayName = "sin(x)";
        axes.AddLine(x, Map(x, System.Math.Cos)).DisplayName = "cos(x)";
        LinePlot damped = axes.AddLine(x, Map(x, v => System.Math.Sin(v) * System.Math.Exp(-v / 10)));
        damped.DisplayName = "damped";
        damped.LineWidth = 2;

        axes.Title = "JGraph — Interactive Figure";
        axes.PrimaryXAxis.Label = "x";
        axes.PrimaryYAxis.Label = "amplitude";
        axes.Grid.ShowMajor = true;
        axes.Legend.Visible = true;

        // A few annotations to edit: select them in Edit mode, drag them, tweak them in the inspector.
        double peak = System.Math.PI / 2;
        axes.AddArrow(peak + 1.7, 0.75, peak + 0.2, 0.97);
        TextAnnotation label = axes.AddText(peak + 1.8, 0.75, "first peak");
        label.Color = Core.Drawing.Colors.Black; // explicit box, so pin the ink too
        label.Background = Core.Drawing.Colors.White.WithOpacity(0.75);
        label.BorderColor = Core.Drawing.Colors.Gray;
        figure.AddText(0.99, 0.995, "annotations are draggable in Edit mode").HorizontalAlignment =
            Core.Drawing.HorizontalAlignment.Right;

        return figure;
    }

    private static double[] Linspace(double a, double b, int n)
    {
        var r = new double[n];
        double step = (b - a) / (n - 1);
        for (int i = 0; i < n; i++)
        {
            r[i] = a + (i * step);
        }

        return r;
    }

    private static double[] Map(double[] x, Func<double, double> f)
    {
        var r = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            r[i] = f(x[i]);
        }

        return r;
    }
}
