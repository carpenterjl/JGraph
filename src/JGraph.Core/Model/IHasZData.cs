using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// Implemented by plot objects that carry a third (Z) data dimension — surfaces and other 3D plots.
/// The axes layout pass unions these bounds into the Z axis of a 3D <see cref="AxesModel"/> exactly
/// as <see cref="PlotObject.GetXDataBounds"/>/<see cref="PlotObject.GetYDataBounds"/> feed X and Y.
/// </summary>
public interface IHasZData
{
    /// <summary>The extent of the plot's Z data (empty when there is none).</summary>
    DataRange GetZDataBounds();
}
