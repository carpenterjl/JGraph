using JGraph.Core.Primitives;

namespace JGraph.Objects.Internal;

/// <summary>
/// The two things every plot built from a list of 3D points needs: the argument check that keeps the
/// three coordinate arrays the same length, and the finite extent of one of them. Shared by
/// <see cref="Line3DPlot"/>, <see cref="Scatter3DPlot"/> and <see cref="PatchPlot"/> so that the
/// wording of the error and the treatment of NaN cannot drift apart between them.
/// </summary>
internal static class Vertices3D
{
    /// <summary>
    /// Checks that three coordinate arrays are present and of equal length, and returns that length.
    /// </summary>
    public static int Validate(string what, double[] x, double[] y, double[] z)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);
        if (x.Length != y.Length || x.Length != z.Length)
        {
            throw new ArgumentException(
                $"{what} needs x, y and z of the same length, but got {x.Length}, {y.Length} and {z.Length}.",
                nameof(x));
        }

        return x.Length;
    }

    /// <summary>
    /// The extent of a coordinate array, skipping NaN and infinity. A NaN is a break in the data
    /// rather than a position, so it must not drag the axis limits with it.
    /// </summary>
    public static DataRange Bounds(double[] values)
    {
        DataRange bounds = DataRange.Empty;
        foreach (double v in values)
        {
            if (double.IsFinite(v))
            {
                bounds = bounds.Include(v);
            }
        }

        return bounds;
    }
}
