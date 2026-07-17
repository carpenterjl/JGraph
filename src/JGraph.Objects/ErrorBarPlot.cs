using System.ComponentModel;
using JGraph.Core.Data;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects.Internal;
using JGraph.Rendering;

namespace JGraph.Objects;

/// <summary>
/// An error-bar plot (MATLAB <c>errorbar</c>): samples drawn with an optional connecting line and
/// markers, each carrying a vertical error whisker (with caps) spanning y − <c>errorNeg</c> to
/// y + <c>errorPos</c>. Errors may be symmetric or asymmetric.
/// </summary>
public sealed class ErrorBarPlot : XYPlot, IDrawable, ILegendItem
{
    private readonly double[] _errorNeg;
    private readonly double[] _errorPos;
    private Color? _color;
    private double _lineWidth = 1.5;
    private double _capSize = 6;
    private bool _showLine = true;
    private MarkerType _marker = MarkerType.Circle;
    private double _markerSize = 6;
    private Color? _markerFill;

    private Point2D[] _dataBuffer = new Point2D[16];
    private Point2D[] _pixelBuffer = new Point2D[16];

    /// <summary>Creates an error-bar plot with asymmetric lower/upper Y errors per sample.</summary>
    public ErrorBarPlot(IDataSeries data, double[] errorNeg, double[] errorPos)
        : base(data)
    {
        ArgumentNullException.ThrowIfNull(errorNeg);
        ArgumentNullException.ThrowIfNull(errorPos);
        if (errorNeg.Length != data.Count || errorPos.Length != data.Count)
        {
            throw new ArgumentException("Error arrays must match the sample count.");
        }

        _errorNeg = errorNeg;
        _errorPos = errorPos;
        Name = "ErrorBar";
    }

    /// <summary>Creates an error-bar plot with symmetric Y errors per sample.</summary>
    public ErrorBarPlot(double[] xs, double[] ys, double[] error)
        : this(new ArrayDataSeries(xs, ys), error, error)
    {
    }

    /// <summary>Explicit line/whisker color, or null to use the auto series color.</summary>
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

    /// <summary>The width of the whisker end caps, in device-independent units.</summary>
    [Category("Appearance"), DisplayName("Cap size")]
    public double CapSize
    {
        get => _capSize;
        set => SetProperty(ref _capSize, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>Whether the samples are joined by a connecting line.</summary>
    [Category("Appearance"), DisplayName("Show line")]
    public bool ShowLine
    {
        get => _showLine;
        set => SetProperty(ref _showLine, value, InvalidationKind.Render);
    }

    [Category("Appearance")]
    public MarkerType Marker
    {
        get => _marker;
        set => SetProperty(ref _marker, value, InvalidationKind.Render);
    }

    [Category("Appearance"), DisplayName("Marker size")]
    public double MarkerSize
    {
        get => _markerSize;
        set => SetProperty(ref _markerSize, System.Math.Max(0, value), InvalidationKind.Render);
    }

    /// <summary>Marker interior color, or null for open (unfilled) markers.</summary>
    [Category("Appearance"), DisplayName("Marker fill")]
    public Color? MarkerFill
    {
        get => _markerFill;
        set => SetProperty(ref _markerFill, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>The per-sample lower error magnitudes, exposed for serialization.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> ErrorNeg => _errorNeg;

    /// <summary>The per-sample upper error magnitudes, exposed for serialization.</summary>
    [Browsable(false)]
    public IReadOnlyList<double> ErrorPos => _errorPos;

    /// <summary>The vertical extent includes the whiskers, so error bars are never clipped by auto-scaling.</summary>
    public override DataRange GetYDataBounds()
    {
        DataRange bounds = DataRange.Empty;
        for (int i = 0; i < Data.Count; i++)
        {
            double y = Data.GetY(i);
            if (!double.IsFinite(y))
            {
                continue;
            }

            bounds = bounds.Include(y - System.Math.Abs(_errorNeg[i]));
            bounds = bounds.Include(y + System.Math.Abs(_errorPos[i]));
        }

        return bounds;
    }

    /// <inheritdoc />
    public void Render(IRenderContext context, RenderState state)
    {
        Color color = (_color ?? state.SeriesColor).WithOpacity(Opacity);
        var lineStyle = new LineStyle(color, _lineWidth);
        ICoordinateMapper mapper = state.Mapper;

        if (_showLine)
        {
            SeriesRenderer.DrawLine(context, state, Data, lineStyle, ref _dataBuffer, ref _pixelBuffer);
        }

        double halfCap = _capSize / 2.0;
        Span<Point2D> tip = stackalloc Point2D[1];
        var marker = new MarkerStyle(_marker, _markerSize, _markerFill, color);

        for (int i = 0; i < Data.Count; i++)
        {
            double x = Data.GetX(i);
            double y = Data.GetY(i);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            double lo = y - System.Math.Abs(_errorNeg[i]);
            double hi = y + System.Math.Abs(_errorPos[i]);
            Point2D pLo = mapper.DataToPixel(x, lo);
            Point2D pHi = mapper.DataToPixel(x, hi);

            // Vertical whisker.
            context.DrawLine(pLo, pHi, lineStyle);

            // End caps.
            if (_capSize > 0)
            {
                context.DrawLine(new Point2D(pLo.X - halfCap, pLo.Y), new Point2D(pLo.X + halfCap, pLo.Y), lineStyle);
                context.DrawLine(new Point2D(pHi.X - halfCap, pHi.Y), new Point2D(pHi.X + halfCap, pHi.Y), lineStyle);
            }

            if (_marker != MarkerType.None)
            {
                tip[0] = mapper.DataToPixel(x, y);
                context.DrawMarkers(tip, marker, color);
            }
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        LineStyle? line = _showLine ? new LineStyle(color, _lineWidth) : null;
        MarkerStyle? marker = _marker != MarkerType.None
            ? new MarkerStyle(_marker, System.Math.Min(_markerSize, 8), _markerFill, color)
            : null;
        return new LegendKey(line, marker, swatch: null);
    }

    /// <inheritdoc />
    public override PlotHitResult? HitTest(Point2D pixelPoint, ICoordinateMapper mapper, double tolerancePixels)
    {
        if (!HitTestVisible || Data.Count == 0)
        {
            return null;
        }

        double bestDistance = double.MaxValue;
        int bestIndex = -1;
        for (int i = 0; i < Data.Count; i++)
        {
            double x = Data.GetX(i);
            double y = Data.GetY(i);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            double distance = mapper.DataToPixel(x, y).DistanceTo(pixelPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        double pick = System.Math.Max(tolerancePixels, _markerSize);
        if (bestIndex < 0 || bestDistance > pick)
        {
            return null;
        }

        return new PlotHitResult(this, new Point2D(Data.GetX(bestIndex), Data.GetY(bestIndex)), bestDistance, bestIndex);
    }
}
