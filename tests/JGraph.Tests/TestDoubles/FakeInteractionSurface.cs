using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Core.Undo;
using JGraph.Interaction;
using JGraph.Maths.Transforms;

namespace JGraph.Tests.TestDoubles;

/// <summary>
/// A headless <see cref="IInteractionSurface"/> over a single axes with a fixed plot rectangle. The
/// coordinate mapper is rebuilt from the axes' current ranges on each query, mirroring how the real
/// control exposes the latest paint geometry.
/// </summary>
internal sealed class FakeInteractionSurface : IInteractionSurface
{
    private readonly AxesModel _axes;
    private readonly Rect2D _plotArea;

    public FakeInteractionSurface(AxesModel axes, Rect2D plotArea)
    {
        _axes = axes;
        _plotArea = plotArea;
    }

    public UndoStack UndoStack { get; } = new();

    public int RenderRequests { get; private set; }

    public AxesModel? DefaultAxes => _axes;

    /// <summary>Settable so tests can exercise figure-space annotations; null by default.</summary>
    public ICoordinateMapper? FigureMapper { get; set; }

    public bool TryGetAxesAt(Point2D pixel, out AxesModel axes, out ICoordinateMapper mapper, out Rect2D plotArea)
    {
        if (_plotArea.Contains(pixel))
        {
            axes = _axes;
            mapper = AxisTransform.Create(_plotArea, _axes.PrimaryXAxis, _axes.PrimaryYAxis);
            plotArea = _plotArea;
            return true;
        }

        axes = null!;
        mapper = null!;
        plotArea = Rect2D.Empty;
        return false;
    }

    public ICoordinateMapper? GetMapper(AxesModel axes) =>
        AxisTransform.Create(_plotArea, _axes.PrimaryXAxis, _axes.PrimaryYAxis);

    /// <summary>Settable so tests can place a legend box without running a real paint.</summary>
    public Rect2D? LegendBounds { get; set; }

    public bool TryGetLegendAt(Point2D pixel, out AxesModel axes, out Rect2D plotArea)
    {
        // Deliberately not gated on the plot area: a long legend hangs outside it and must still hit.
        if (_axes.Legend.Visible && LegendBounds is { } box && box.Contains(pixel))
        {
            axes = _axes;
            plotArea = _plotArea;
            return true;
        }

        axes = null!;
        plotArea = Rect2D.Empty;
        return false;
    }

    public Rect2D? GetLegendBounds(AxesModel axes) =>
        ReferenceEquals(axes, _axes) ? LegendBounds : null;

    /// <summary>Settable so tests can place one clickable legend row without running a real paint.</summary>
    public (PlotObject Plot, Rect2D Bounds)? LegendRow { get; set; }

    /// <summary>The rows a test clicked, in order.</summary>
    public List<(AxesModel Axes, PlotObject Plot)> LegendRowClicks { get; } = new();

    public PlotObject? GetLegendRowAt(AxesModel axes, Point2D pixel) =>
        ReferenceEquals(axes, _axes) && LegendRow is { } row && row.Bounds.Contains(pixel)
            ? row.Plot
            : null;

    public void OnLegendRowClicked(AxesModel axes, PlotObject plot) => LegendRowClicks.Add((axes, plot));

    public void RequestRender() => RenderRequests++;
}
