using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// The standard page sizes a figure's <c>PaperType</c> names, in portrait inches. Naming a type is
/// how a script says a page size without spelling one; <c>'&lt;custom&gt;'</c> is what a figure
/// reports once a size has been set directly instead.
/// </summary>
public static class PaperSizes
{
    /// <summary>The word a figure answers with once its page size stopped coming from this table.</summary>
    public const string CustomName = "<custom>";

    private static readonly Dictionary<string, Size2D> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["usletter"] = new(8.5, 11),
        ["uslegal"] = new(8.5, 14),
        ["tabloid"] = new(11, 17),
        ["a0"] = new(33.1102, 46.8189),
        ["a1"] = new(23.3858, 33.1102),
        ["a2"] = new(16.5354, 23.3858),
        ["a3"] = new(11.6929, 16.5354),
        ["a4"] = new(8.2639, 11.6806),
        ["a5"] = new(5.8264, 8.2639),
        ["b0"] = new(40.5512, 57.3228),
        ["b1"] = new(28.6614, 40.5512),
        ["b2"] = new(20.2756, 28.6614),
        ["b3"] = new(14.3307, 20.2756),
        ["b4"] = new(10.1181, 14.3307),
        ["b5"] = new(7.1653, 10.1181),
        ["arch-a"] = new(9, 12),
        ["arch-b"] = new(12, 18),
        ["arch-c"] = new(18, 24),
        ["arch-d"] = new(24, 36),
        ["arch-e"] = new(36, 48),
        ["a"] = new(8.5, 11),
        ["b"] = new(11, 17),
        ["c"] = new(17, 22),
        ["d"] = new(22, 34),
        ["e"] = new(34, 44),
    };

    /// <summary>Every type name a script may set, in the order MATLAB lists them.</summary>
    public static IReadOnlyList<string> KnownNames { get; } = Table.Keys.ToArray();

    /// <summary>The portrait size of a named page, or null when the name is not one of them.</summary>
    public static Size2D? Find(string? name) =>
        name is not null && Table.TryGetValue(name, out Size2D size) ? size : null;

    /// <summary>The name of the standard page of this size, or null when no standard page matches.</summary>
    public static string? NameOf(Size2D size)
    {
        foreach ((string name, Size2D candidate) in Table)
        {
            if (System.Math.Abs(candidate.Width - size.Width) < 5e-4
                && System.Math.Abs(candidate.Height - size.Height) < 5e-4)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>How many inches one unit of the given kind is worth.</summary>
    public static double InchesPer(PaperUnitType units) => units switch
    {
        PaperUnitType.Centimeters => 1 / 2.54,
        PaperUnitType.Points => 1 / 72.0,
        _ => 1,
    };
}
