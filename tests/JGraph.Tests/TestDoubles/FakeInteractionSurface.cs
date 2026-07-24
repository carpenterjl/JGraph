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

    public Rect2D? GetLegendBounds(AxesModel axes) =>
        ReferenceEquals(axes, _axes) ? LegendBounds : null;

    public void RequestRender() => RenderRequests++;
}
