using JGraph.Core.Model;

namespace JGraph.Maths.Transforms;

/// <summary>
/// Maps an axis' data values to a linear coordinate space (and back) so that a single linear
/// interpolation can then place them within the plot rectangle. A linear axis uses the identity
/// mapping; a logarithmic axis uses base-10 logarithm.
/// </summary>
public interface IScaleTransform
{
    /// <summary>The scale type this transform implements.</summary>
    AxisScaleType ScaleType { get; }

    /// <summary>Whether a data value is representable on this scale (for example, strictly positive for log).</summary>
    bool IsValidData(double value);

    /// <summary>Maps a data value into linear coordinate space.</summary>
    double Forward(double dataValue);

    /// <summary>Maps a linear coordinate value back to data space.</summary>
    double Inverse(double linearValue);
}
