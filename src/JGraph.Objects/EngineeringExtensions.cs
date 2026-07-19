using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects.Engineering;
using JGraph.Signal;

namespace JGraph.Objects;

/// <summary>
/// Fluent factory helpers for the engineering plot types (polar, Smith, eye diagram, Bode, Nyquist,
/// spectrogram). They compose the existing plot objects and the <see cref="JGraph.Signal"/> services,
/// configuring the host axes as needed (equal aspect, log frequency, hidden Cartesian chrome).
/// </summary>
public static class EngineeringExtensions
{
    // ---- Polar ----

    /// <summary>
    /// Adds a polar line for angle/radius data (θ in radians), converting each sample to Cartesian and
    /// configuring the axes as a polar chart (equal aspect, circular grid, no Cartesian frame). Returns
    /// the underlying line so its color/width/markers can be styled.
    /// </summary>
    public static LinePlot AddPolar(this AxesModel axes, double[] thetaRadians, double[] r)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(thetaRadians);
        ArgumentNullException.ThrowIfNull(r);
        if (thetaRadians.Length != r.Length)
        {
            throw new ArgumentException("Angle and radius arrays must have the same length.", nameof(r));
        }

        double dataMax = 0;
        foreach (double v in r)
        {
            dataMax = System.Math.Max(dataMax, System.Math.Abs(v));
        }

        EnsurePolarGrid(axes, dataMax);

        var x = new double[r.Length];
        var y = new double[r.Length];
        for (int i = 0; i < r.Length; i++)
        {
            x[i] = r[i] * System.Math.Cos(thetaRadians[i]);
            y[i] = r[i] * System.Math.Sin(thetaRadians[i]);
        }

        var line = new LinePlot(x, y);
        axes.Plots.Add(line);
        return line;
    }

    // ---- Smith chart ----

    /// <summary>
    /// Adds a Smith-chart trace of a normalized-impedance locus (z = real + j·imag), converting each
    /// point to its reflection coefficient Γ = (z − 1)/(z + 1) and configuring the axes as a Smith
    /// chart. Returns the underlying line.
    /// </summary>
    public static LinePlot AddSmith(this AxesModel axes, double[] impedanceReal, double[] impedanceImag)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(impedanceReal);
        ArgumentNullException.ThrowIfNull(impedanceImag);
        if (impedanceReal.Length != impedanceImag.Length)
        {
            throw new ArgumentException("Impedance real/imag arrays must have the same length.", nameof(impedanceImag));
        }

        EnsureSmithGrid(axes);

        var gammaRe = new double[impedanceReal.Length];
        var gammaIm = new double[impedanceReal.Length];
        for (int i = 0; i < impedanceReal.Length; i++)
        {
            // Γ = (z − 1)/(z + 1) with z = zr + j·zi.
            double zr = impedanceReal[i];
            double zi = impedanceImag[i];
            double denomRe = zr + 1.0;
            double denomSq = (denomRe * denomRe) + (zi * zi);
            double numRe = zr - 1.0;
            gammaRe[i] = ((numRe * denomRe) + (zi * zi)) / denomSq;
            gammaIm[i] = ((zi * denomRe) - (numRe * zi)) / denomSq;
        }

        var line = new LinePlot(gammaRe, gammaIm);
        axes.Plots.Add(line);
        return line;
    }

    /// <summary>
    /// Adds a Smith-chart trace of a reflection-coefficient locus given directly as Γ = real + j·imag
    /// (the form S-parameter data provides), configuring the axes as a Smith chart. Unlike
    /// <see cref="AddSmith"/> this plots Γ as-is, avoiding the z→Γ mapping that is singular as z→∞.
    /// Returns the underlying line.
    /// </summary>
    public static LinePlot AddSmithReflection(this AxesModel axes, double[] gammaReal, double[] gammaImag)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(gammaReal);
        ArgumentNullException.ThrowIfNull(gammaImag);
        if (gammaReal.Length != gammaImag.Length)
        {
            throw new ArgumentException("Reflection real/imag arrays must have the same length.", nameof(gammaImag));
        }

        EnsureSmithGrid(axes);
        var line = new LinePlot(gammaReal, gammaImag);
        axes.Plots.Add(line);
        return line;
    }

    // ---- Eye diagram ----

    /// <summary>Adds an eye diagram of a signal sampled at <paramref name="samplesPerSymbol"/> samples per symbol.</summary>
    public static EyeDiagramPlot AddEyeDiagram(this AxesModel axes, double[] signal, int samplesPerSymbol, int symbolsPerTrace = 2)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var plot = new EyeDiagramPlot(signal, samplesPerSymbol, symbolsPerTrace);
        axes.Plots.Add(plot);
        return plot;
    }

    // ---- Nyquist ----

    /// <summary>Adds a Nyquist plot of a transfer function's H(jω) locus with the critical (−1, 0) point marked.</summary>
    public static LinePlot AddNyquist(this AxesModel axes, TransferFunction system, double omegaMin, double omegaMax, int points = 400)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(system);

        double[] omega = TransferFunction.LogSpace(omegaMin, omegaMax, points);
        int n = omega.Length;
        var re = new double[2 * n];
        var im = new double[2 * n];

        // Negative-ω branch (conjugate), swept from −ωmax up to −ωmin, then the positive branch.
        for (int i = 0; i < n; i++)
        {
            System.Numerics.Complex h = system.Evaluate(omega[n - 1 - i]);
            re[i] = h.Real;
            im[i] = -h.Imaginary;
        }

        for (int i = 0; i < n; i++)
        {
            System.Numerics.Complex h = system.Evaluate(omega[i]);
            re[n + i] = h.Real;
            im[n + i] = h.Imaginary;
        }

        var locus = new LinePlot(re, im);
        axes.Plots.Add(locus);

        var critical = new ScatterPlot(new[] { -1.0 }, new[] { 0.0 })
        {
            Marker = MarkerType.Cross,
            MarkerSize = 10,
            Color = Colors.Red,
            DisplayName = "−1",
            HitTestVisible = false,
        };
        axes.Plots.Add(critical);

        axes.EqualAspect = true;
        return locus;
    }

    /// <summary>Adds a Nyquist plot for a transfer function given by its numerator/denominator coefficients.</summary>
    public static LinePlot AddNyquist(this AxesModel axes, double[] numerator, double[] denominator, double omegaMin, double omegaMax, int points = 400) =>
        axes.AddNyquist(new TransferFunction(numerator, denominator), omegaMin, omegaMax, points);

    // ---- Spectrogram ----

    /// <summary>
    /// Adds a spectrogram (STFT magnitude in dB) of a real signal as a colormapped image spanning
    /// time (x) and frequency (y). Returns the image so its colormap can be changed.
    /// </summary>
    public static ImagePlot AddSpectrogram(
        this AxesModel axes,
        double[] signal,
        double sampleRate,
        int windowSize = 256,
        int overlap = 128,
        WindowType window = WindowType.Hann)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(signal);

        SpectrogramResult result = Spectrogram.Compute(signal, sampleRate, windowSize, overlap, window);
        var image = new ImagePlot(result.MagnitudeDb)
        {
            XExtent = new DataRange(result.TimeMin, result.TimeMax),
            YExtent = new DataRange(result.FrequencyMin, result.FrequencyMax),
            RowZeroAtTop = false, // frequency bin 0 (DC) at the bottom
            Colormap = Colormap.Viridis,
            Interpolate = true,
            DisplayName = "spectrogram",
        };
        axes.Plots.Add(image);
        return image;
    }

    // ---- Bode ----

    /// <summary>
    /// Builds a Bode plot for a transfer function on the figure: a magnitude (dB) panel over a phase
    /// (degrees) panel, both against a shared logarithmic frequency axis. Returns both panels.
    /// </summary>
    public static BodeChart AddBode(this FigureModel figure, TransferFunction system, double omegaMin, double omegaMax, int points = 300)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentNullException.ThrowIfNull(system);

        double[] omega = TransferFunction.LogSpace(omegaMin, omegaMax, points);
        FrequencyResponse response = system.Response(omega);
        var frequencyRange = new DataRange(omegaMin, omegaMax);

        AxesModel magnitude = figure.AddSubplot(2, 1, 1);
        magnitude.AddLine(omega, response.MagnitudeDb).DisplayName = "magnitude";
        ConfigureLogFrequencyAxis(magnitude.PrimaryXAxis, frequencyRange);
        magnitude.PrimaryYAxis.Label = "Magnitude (dB)";
        magnitude.Grid.ShowMajor = true;
        magnitude.Grid.Visible = true;

        AxesModel phase = figure.AddSubplot(2, 1, 2);
        phase.AddLine(omega, response.PhaseDegrees).DisplayName = "phase";
        ConfigureLogFrequencyAxis(phase.PrimaryXAxis, frequencyRange);
        phase.PrimaryXAxis.Label = "Frequency (rad/s)";
        phase.PrimaryYAxis.Label = "Phase (deg)";
        phase.Grid.ShowMajor = true;
        phase.Grid.Visible = true;

        return new BodeChart(magnitude, phase);
    }

    /// <summary>Builds a Bode plot for a transfer function given by its numerator/denominator coefficients.</summary>
    public static BodeChart AddBode(this FigureModel figure, double[] numerator, double[] denominator, double omegaMin, double omegaMax, int points = 300) =>
        figure.AddBode(new TransferFunction(numerator, denominator), omegaMin, omegaMax, points);

    // ---- helpers ----

    /// <summary>Puts an axis on a logarithmic scale pinned to an explicit frequency range (both Bode panels share it).</summary>
    private static void ConfigureLogFrequencyAxis(AxisModel axis, DataRange frequencyRange)
    {
        axis.Scale = AxisScaleType.Logarithmic;
        axis.AutoScale = false;
        axis.Range = frequencyRange;
    }

    private static PolarGrid EnsurePolarGrid(AxesModel axes, double dataMaxRadius)
    {
        PolarGrid? grid = null;
        foreach (PlotObject plot in axes.Plots)
        {
            if (plot is PolarGrid found)
            {
                grid = found;
                break;
            }
        }

        double niceMax = NiceCeiling(System.Math.Max(dataMaxRadius, grid?.MaxRadius ?? 0));
        if (grid is null)
        {
            grid = new PolarGrid { MaxRadius = niceMax };
            axes.Plots.Add(grid); // added before the data series, so it draws behind
        }
        else
        {
            grid.MaxRadius = niceMax;
        }

        ConfigureRadialAxes(axes, grid.MaxRadius * 1.15);
        return grid;
    }

    private static void EnsureSmithGrid(AxesModel axes)
    {
        bool hasGrid = false;
        foreach (PlotObject plot in axes.Plots)
        {
            if (plot is SmithGrid)
            {
                hasGrid = true;
                break;
            }
        }

        if (!hasGrid)
        {
            axes.Plots.Add(new SmithGrid());
        }

        ConfigureRadialAxes(axes, 1.15);
    }

    /// <summary>Configures an axes for a circular (polar/Smith) chart: equal aspect, symmetric limits, no Cartesian chrome.</summary>
    private static void ConfigureRadialAxes(AxesModel axes, double limit)
    {
        axes.EqualAspect = true;
        axes.FrameVisible = false;
        axes.Grid.Visible = false;
        ConfigureHiddenAxis(axes.PrimaryXAxis, limit);
        ConfigureHiddenAxis(axes.PrimaryYAxis, limit);
    }

    private static void ConfigureHiddenAxis(AxisModel axis, double limit)
    {
        axis.ShowMajorTicks = false;
        axis.ShowMinorTicks = false;
        axis.ShowTickLabels = false;
        axis.AutoScale = false;
        axis.Range = new DataRange(-limit, limit);
    }

    /// <summary>Rounds up to a "nice" number (1, 2, or 5 × 10^k) for a tidy outer radius.</summary>
    private static double NiceCeiling(double value)
    {
        if (value <= 0)
        {
            return 1;
        }

        double exponent = System.Math.Floor(System.Math.Log10(value));
        double power = System.Math.Pow(10, exponent);
        double fraction = value / power; // in [1, 10)
        double nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return nice * power;
    }
}
