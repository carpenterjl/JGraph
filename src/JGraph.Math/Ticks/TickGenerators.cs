using JGraph.Core.Model;

namespace JGraph.Maths.Ticks;

/// <summary>Resolves the appropriate <see cref="ITickGenerator"/> for an axis or scale type.</summary>
public static class TickGenerators
{
    /// <summary>
    /// Returns a tick generator for a scale type. Category axes need their labels, so this overload
    /// falls back to linear for <see cref="AxisScaleType.Category"/>; prefer <see cref="For(AxisModel)"/>.
    /// </summary>
    public static ITickGenerator For(AxisScaleType scaleType) => scaleType switch
    {
        AxisScaleType.Logarithmic => LogarithmicTickGenerator.Instance,
        AxisScaleType.DateTime => DateTimeTickGenerator.Instance,
        _ => LinearTickGenerator.Instance,
    };

    /// <summary>
    /// Returns a tick generator for an axis, using its scale and (for category axes) its labels. This
    /// is the resolver rendering uses.
    /// </summary>
    public static ITickGenerator For(AxisModel axis)
    {
        ArgumentNullException.ThrowIfNull(axis);
        ITickGenerator automatic = axis.Scale switch
        {
            AxisScaleType.Logarithmic => LogarithmicTickGenerator.Instance,
            AxisScaleType.DateTime => DateTimeTickGenerator.Instance,
            AxisScaleType.Category => new CategoryTickGenerator(axis.Categories),
            _ => LinearTickGenerator.Instance,
        };

        // Ticks the axis names itself win over the ones a scale would choose. Deciding that here means
        // every caller honours it: the renderer generates ticks in five places and none of them has to
        // know that an axis can be told where its ticks go.
        return axis.TickPositions is null && axis.TickLabelOverrides is null
            ? automatic
            : new ManualTickGenerator(automatic, axis.TickPositions, axis.TickLabelOverrides);
    }
}
