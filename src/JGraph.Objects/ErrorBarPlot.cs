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
    private double[] _errorNeg;
    private double[] _errorPos;
    private double[]? _errorLeft;
    private double[]? _errorRight;
    private Color? _color;
    private double _lineWidth = 1.5;
    private double _capSize = 6;
    private bool _showLine = true;
    private DashStyle _dashStyle = DashStyle.Solid;
    private MarkerType _marker = MarkerType.Circle;
    private double _markerSize = 6;
    private Color? _markerFill;
    private Color? _markerEdge;

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
    public Color? MarkerFaceColor
    {
        get => _markerFill;
        set => SetProperty(ref _markerFill, value, InvalidationKind.Render);
    }

    /// <inheritdoc />
    public string LegendLabel => DisplayName;

    /// <summary>
    /// The per-sample lower error magnitudes — MATLAB's <c>LData</c>. Writable since M77: they were
    /// a constructor argument and nothing else, so a script could draw error bars and never change
    /// them.
    /// </summary>
    [Browsable(false)]
    public double[] ErrorNeg
    {
        get => _errorNeg;
        set => SetProperty(ref _errorNeg, Fitted(value, Data.Count), InvalidationKind.Data);
    }

    /// <summary>The per-sample upper error magnitudes — MATLAB's <c>UData</c>.</summary>
    [Browsable(false)]
    public double[] ErrorPos
    {
        get => _errorPos;
        set => SetProperty(ref _errorPos, Fitted(value, Data.Count), InvalidationKind.Data);
    }

    /// <summary>How far each whisker reaches to the left, or null for none — <c>XNegativeDelta</c>.</summary>
    [Browsable(false)]
    public double[]? ErrorLeft
    {
        get => _errorLeft;
        set => SetProperty(ref _errorLeft, value is null ? null : Fitted(value, Data.Count), InvalidationKind.Data);
    }

    /// <summary>How far each whisker reaches to the right — <c>XPositiveDelta</c>.</summary>
    [Browsable(false)]
    public double[]? ErrorRight
    {
        get => _errorRight;
        set => SetProperty(ref _errorRight, value is null ? null : Fitted(value, Data.Count), InvalidationKind.Data);
    }

    /// <summary>How the connecting line and the whiskers are dashed.</summary>
    [Category("Appearance"), DisplayName("Dash style")]
    public DashStyle DashStyle
    {
        get => _dashStyle;
        set
        {
            SetProperty(ref _dashStyle, value, InvalidationKind.Render);
            LineStyleManual = true;
        }
    }

    /// <summary>Marker outline color, or null to draw it in the series' own colour.</summary>
    [Category("Appearance"), DisplayName("Marker edge")]
    public Color? MarkerEdgeColor
    {
        get => _markerEdge;
        set => SetProperty(ref _markerEdge, value, InvalidationKind.Render);
    }

    /// <summary>Errors padded or trimmed to the samples there are, so the two cannot disagree.</summary>
    private static double[] Fitted(double[] values, int count)
    {
        ArgumentNullException.ThrowIfNull(values);
        var fitted = new double[count];
        Array.Copy(values, fitted, System.Math.Min(values.Length, count));
        return fitted;
    }

    /// <summary>The horizontal extent includes the sideways whiskers, when there are any.</summary>
    public override DataRange GetXDataBounds()
    {
        DataRange bounds = Data.XBounds;
        if (_errorLeft is null && _errorRight is null)
        {
            return bounds;
        }

        for (int i = 0; i < Data.Count; i++)
        {
            double x = Data.GetX(i);
            if (!double.IsFinite(x))
            {
                continue;
            }

            bounds = bounds.Include(x - System.Math.Abs(_errorLeft is null ? 0 : _errorLeft[i]));
            bounds = bounds.Include(x + System.Math.Abs(_errorRight is null ? 0 : _errorRight[i]));
        }

        return bounds;
    }

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
        var lineStyle = new LineStyle(color, _lineWidth, _dashStyle);
        ICoordinateMapper mapper = state.Mapper;

        if (_showLine)
        {
            SeriesRenderer.DrawLine(context, state, Data, lineStyle, ref _dataBuffer, ref _pixelBuffer);
        }

        double halfCap = _capSize / 2.0;
        Span<Point2D> tip = stackalloc Point2D[1];
        Color markerEdge = _markerEdge is { } chosen ? chosen.WithOpacity(Opacity) : color;
        var marker = new MarkerStyle(_marker, _markerSize, _markerFill, markerEdge);

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

            // The sideways pair, when the call gave any. They are the same figure turned a quarter
            // turn: a bar between two reaches, capped at each end.
            if (_errorLeft is not null || _errorRight is not null)
            {
                double left = x - System.Math.Abs(_errorLeft is null ? 0 : _errorLeft[i]);
                double right = x + System.Math.Abs(_errorRight is null ? 0 : _errorRight[i]);
                Point2D pLeft = mapper.DataToPixel(left, y);
                Point2D pRight = mapper.DataToPixel(right, y);
                context.DrawLine(pLeft, pRight, lineStyle);

                if (_capSize > 0)
                {
                    context.DrawLine(
                        new Point2D(pLeft.X, pLeft.Y - halfCap),
                        new Point2D(pLeft.X, pLeft.Y + halfCap),
                        lineStyle);
                    context.DrawLine(
                        new Point2D(pRight.X, pRight.Y - halfCap),
                        new Point2D(pRight.X, pRight.Y + halfCap),
                        lineStyle);
                }
            }

            if (_marker != MarkerType.None)
            {
                tip[0] = mapper.DataToPixel(x, y);
                context.DrawMarkers(tip, marker, markerEdge);
            }
        }
    }

    /// <inheritdoc />
    public LegendKey GetLegendKey(Color seriesColor)
    {
        Color color = _color ?? seriesColor;
        LineStyle? line = _showLine ? new LineStyle(color, _lineWidth, _dashStyle) : null;
        MarkerStyle? marker = _marker != MarkerType.None
            ? new MarkerStyle(_marker, System.Math.Min(_markerSize, 8), _markerFill, _markerEdge ?? color)
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
