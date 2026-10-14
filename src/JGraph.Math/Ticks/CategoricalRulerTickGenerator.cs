using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Maths.Ticks;

/// <summary>
/// The ticks of a MATLAB categorical ruler (open item 120): one per category at its 1-based
/// position, labelled with the category's name, every one shown however many there are (R2025b
/// shows all thirty of thirty categories and turns the labels to fit). No minor ticks.
/// </summary>
public sealed class CategoricalRulerTickGenerator : ITickGenerator
{
    private readonly IReadOnlyList<string> _categories;

    public CategoricalRulerTickGenerator(IReadOnlyList<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        _categories = categories;
    }

    public AxisScaleType ScaleType => AxisScaleType.Linear;

    /// <summary>The name of the category at <paramref name="position"/>, or empty between categories.</summary>
    public string NameAt(double position)
    {
        int at = (int)System.Math.Round(position);
        return System.Math.Abs(position - at) < 1e-9 && at >= 1 && at <= _categories.Count ? _categories[at - 1] : string.Empty;
    }

    public TickSet Generate(DataRange range, TickBudget budget, string? labelFormat = null)
    {
        const double epsilon = 1e-9;
        var majors = new List<Tick>(_categories.Count);
        for (int k = 1; k <= _categories.Count; k++)
        {
            if (k >= range.Min - epsilon && k <= range.Max + epsilon)
            {
                majors.Add(new Tick(k, _categories[k - 1]));
            }
        }

        return new TickSet(majors, Array.Empty<double>(), 1);
    }
}
