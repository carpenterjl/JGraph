using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>MATLAB's six <c>Units</c> words, in the order R2025b lists them.</summary>
public enum UiUnits
{
    Inches,
    Centimeters,
    Characters,
    Normalized,
    Points,
    Pixels,
}

/// <summary>
/// The units engine (app-building plan, section C): a <c>Position</c> among MATLAB's six units,
/// measured on R2025b (probes <c>u0_layout</c>, <c>u2_units</c>).
/// <para>
/// A pixel is 1/96 inch — a DIP — and pixel positions are 1-based: pixel 1 is the parent's edge. Every
/// other unit counts from 0, so pixel x is <c>(x − 1)</c> units along; sizes convert by the factor
/// alone. <c>normalized</c> is a fraction of the parent's inner size (of the screen, for a figure),
/// and a character is 5.6 by 15 pixels, the default system font's cell.
/// </para>
/// </summary>
public static class UiUnitConverter
{
    /// <summary>The width of R2025b's character unit, in pixels.</summary>
    public const double CharacterWidth = 5.6;

    /// <summary>The height of R2025b's character unit, in pixels.</summary>
    public const double CharacterHeight = 15;

    /// <summary>The words, indexed by <see cref="UiUnits"/>.</summary>
    public static readonly IReadOnlyList<string> Words =
        ["inches", "centimeters", "characters", "normalized", "points", "pixels"];

    /// <summary>How many pixels one unit spans along each direction, given the parent's size.</summary>
    public static (double X, double Y) PixelsPer(UiUnits units, Size2D reference) => units switch
    {
        UiUnits.Inches => (96, 96),
        UiUnits.Centimeters => (96 / 2.54, 96 / 2.54),
        UiUnits.Characters => (CharacterWidth, CharacterHeight),
        UiUnits.Normalized => (reference.Width, reference.Height),
        UiUnits.Points => (96.0 / 72, 96.0 / 72),
        _ => (1, 1),
    };

    /// <summary>A rectangle in <paramref name="units"/> as MATLAB's pixel rectangle.</summary>
    public static Rect2D ToPixels(Rect2D value, UiUnits units, Size2D reference)
    {
        if (units == UiUnits.Pixels)
        {
            return value;
        }

        (double fx, double fy) = PixelsPer(units, reference);
        return new Rect2D((value.X * fx) + 1, (value.Y * fy) + 1, value.Width * fx, value.Height * fy);
    }

    /// <summary>MATLAB's pixel rectangle in <paramref name="units"/>.</summary>
    public static Rect2D FromPixels(Rect2D pixels, UiUnits units, Size2D reference)
    {
        if (units == UiUnits.Pixels)
        {
            return pixels;
        }

        (double fx, double fy) = PixelsPer(units, reference);
        return new Rect2D(
            Divide(pixels.X - 1, fx),
            Divide(pixels.Y - 1, fy),
            Divide(pixels.Width, fx),
            Divide(pixels.Height, fy));
    }

    /// <summary>A rectangle moved from one unit to another against the same parent.</summary>
    public static Rect2D Convert(Rect2D value, UiUnits from, UiUnits to, Size2D reference) =>
        from == to ? value : FromPixels(ToPixels(value, from, reference), to, reference);

    private static double Divide(double value, double by) => by == 0 ? 0 : value / by;
}

/// <summary>
/// The screens, as MATLAB's root reports them: one rectangle per monitor in pixels of 1/96 inch,
/// <c>[left bottom width height]</c> with the primary monitor's corner at (1, 1) and Y upward. The
/// host installs the real ones; without a host the primary screen is asked of the system, and a
/// machine with nothing to ask answers one 1920 by 1080 screen.
/// </summary>
public static class UiScreen
{
    private static Func<IReadOnlyList<Rect2D>>? _provider;

    /// <summary>Installs (or, with null, removes) the source of the monitor rectangles.</summary>
    public static void SetProvider(Func<IReadOnlyList<Rect2D>>? provider) => _provider = provider;

    /// <summary>Every monitor, the primary one first.</summary>
    public static IReadOnlyList<Rect2D> Monitors
    {
        get
        {
            IReadOnlyList<Rect2D>? monitors = (_provider ?? SystemScreens.Read)();
            return monitors is { Count: > 0 } ? monitors : [new Rect2D(1, 1, 1920, 1080)];
        }
    }

    /// <summary>MATLAB's <c>ScreenSize</c>: the primary monitor.</summary>
    public static Rect2D Primary => Monitors[0];
}
