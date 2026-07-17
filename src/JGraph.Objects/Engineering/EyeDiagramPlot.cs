using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering;

namespace JGraph.Objects.Engineering;

/// <summary>
/// An eye diagram: a signal is sliced into short traces (a couple of symbol periods each) that are
/// overlaid, one stepping by a symbol from the next, so accumulated intersymbol interference forms the
/// characteristic "eye". The horizontal axis is time in symbol periods, centered on zero; the vertical
/// axis is amplitude. It maps through the ordinary coordinate mapper like any Cartesian plot.
/// </summary>
public sealed class EyeDiagramPlot : PlotObject, IDrawable, ILegendItem
{
    private double[] _signal;
    private int _samplesPerSymbol;
    private int _symbolsPerTrace;
    private Color? _color;
    private double _lineWidth = 1.0;

    /// <summary>Creates an eye diagram from a real signal sampled at <paramref name="samplesPerSymbol"/> samples per symbol.</summary>
    public EyeDiagramPlot(double[] signal, int samplesPerSymbol, int symbolsPerTrace = 2)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (samplesPerSymbol < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(samplesPerSymbol), "Samples per symbol must be positive.");
        }

        if (symbolsPerTrace < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(symbolsPerTrace), "Symbols per trace must be positive.");
        }

        _signal = signal;
        _samplesPerSymbol = samplesPerSymbol;
        _symbolsPerTrace = symbolsPerTrace;
        Name = "EyeDiagram";
    }

    /// <summary>Explicit trace color, or null to use the auto series color.</summary>
    [Category("Appearance")]
    public Color? Color
    {
        get => _color;
        set => SetProperty(ref _color, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Line width")]
    public double LineWidth
    {
        get => _lineWidth;
        set => SetProperty(ref _lineWidth, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>The number of samples per symbol period.</summary>
    [Category("Behavior"), DisplayName("Samples per symbol")]
    public int SamplesPerSymbol
    {
        get => _samplesPerSymbol;
        set => SetProperty(ref _samplesPerSymbol, System.Math.Max(1, value), InvalidationKind.Layout);
    }

    /// <summary>The number of symbol periods spanned by each overlaid trace (usually 2).</summary>
    [Category("Behavior"), DisplayName("Symbols per trace")]
    public int SymbolsPerTrace
    {
        get => _symbolsPerTrace;
        set => SetProperty(ref _symbolsPerTrace, System.Math.Max(1, value), InvalidationKind.Layout);
    }

    /// <summary>The signal being sliced into traces.</summary>
    [Browsable(false)]
    public double[] Signal
    {
        get => _signal;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetProperty(ref _signal, value, InvalidationKind.Data);
        }
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <inheritdoc />
    public override DataRange GetXDataBounds() => new(-_symbolsPerTrace / 2.0, _symbolsPerTrace / 2.0);

    /// <inheritdoc />
    public override DataRange GetYDataBounds()
    {
        DataRange bounds = DataRange.Empty;
        foreach (double v in _signal)
        {
            bounds = bounds.Include(v);
        }

        return bounds.IsEmpty ? DataRange.Unit : bounds;
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        int traceSamples = _samplesPerSymbol * _symbolsPerTrace;
        if (_signal.Length < traceSamples || traceSamples < 2)
        {
            return;
        }

        Color color = (_color ?? state.SeriesColor).WithOpacity(Opacity);
        var style = new LineStyle(color, _lineWidth);
        ICoordinateMapper mapper = state.Mapper;
        double halfSpan = _symbolsPerTrace / 2.0;

        var trace = new Point2D[traceSamples];
        for (int start = 0; start + traceSamples <= _signal.Length; start += _samplesPerSymbol)
        {
            for (int i = 0; i < traceSamples; i++)
            {
                double t = -halfSpan + ((double)i / _samplesPerSymbol);
                trace[i] = mapper.DataToPixel(t, _signal[start + i]);
            }

            context.DrawPolyline(trace, style);
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        return new LegendKey(new LineStyle(color, _lineWidth), marker: null, swatch: null);
    }
}
