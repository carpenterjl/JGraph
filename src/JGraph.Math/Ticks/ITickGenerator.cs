using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Ticks;

/// <summary>
/// Produces the set of major and minor ticks for a given axis range. Implementations exist for each
/// axis scale type; new scales (date/time, category) add new generators without touching the rest of
/// the framework.
/// </summary>
public interface ITickGenerator
{
    /// <summary>The scale type this generator handles.</summary>
    AxisScaleType ScaleType { get; }

    /// <summary>
    /// Generates ticks for <paramref name="range"/> within <paramref name="budget"/>: R2025b's
    /// bounded count for a linear ruler that was measured, and the classic target count otherwise.
    /// </summary>
    /// <param name="range">The visible data range.</param>
    /// <param name="budget">How many ticks the ruler may carry.</param>
    /// <param name="labelFormat">An optional .NET numeric format string overriding automatic formatting.</param>
    TickSet Generate(DataRange range, TickBudget budget, string? labelFormat = null);
}

/// <summary>The count-only call every generator took before rulers were measured (open item 99).</summary>
public static class TickGeneratorExtensions
{
    /// <summary>Generates ticks aiming for approximately <paramref name="targetCount"/> majors.</summary>
    public static TickSet Generate(this ITickGenerator generator, DataRange range, int targetCount, string? labelFormat = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return generator.Generate(range, TickBudget.Of(targetCount), labelFormat);
    }
}
