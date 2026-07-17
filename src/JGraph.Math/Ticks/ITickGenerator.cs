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
    /// Generates ticks for <paramref name="range"/>, aiming for approximately
    /// <paramref name="targetCount"/> major ticks.
    /// </summary>
    /// <param name="range">The visible data range.</param>
    /// <param name="targetCount">The desired number of major ticks (a hint, not a guarantee).</param>
    /// <param name="labelFormat">An optional .NET numeric format string overriding automatic formatting.</param>
    TickSet Generate(DataRange range, int targetCount, string? labelFormat = null);
}
