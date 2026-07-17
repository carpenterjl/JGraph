using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Ticks;

/// <summary>
/// Generates ticks for a category axis: one major tick per category, placed at integer positions
/// 0, 1, 2, … and labeled with the category name. When more categories are visible than the target
/// count, labels are thinned so they stay legible. Category axes have no minor ticks.
/// </summary>
public sealed class CategoryTickGenerator : ITickGenerator
{
    private readonly IReadOnlyList<string> _categories;

    public CategoryTickGenerator(IReadOnlyList<string>? categories) =>
        _categories = categories ?? Array.Empty<string>();

    public AxisScaleType ScaleType => AxisScaleType.Category;

    public TickSet Generate(DataRange range, int targetCount, string? labelFormat = null)
    {
        int n = _categories.Count;
        if (n == 0)
        {
            return TickSet.Empty;
        }

        int stride = System.Math.Max(1, (int)System.Math.Ceiling(n / (double)System.Math.Max(2, targetCount)));
        var majors = new List<Tick>();
        const double epsilon = 1e-9;
        for (int i = 0; i < n; i += stride)
        {
            double position = i;
            // Only label categories whose position lies within the visible range.
            if (position < range.Min - epsilon || position > range.Max + epsilon)
            {
                continue;
            }

            majors.Add(new Tick(position, _categories[i] ?? string.Empty));
        }

        return new TickSet(majors, Array.Empty<double>(), stride);
    }
}
