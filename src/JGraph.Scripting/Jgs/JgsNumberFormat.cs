using System.Globalization;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The console's numeric display precision — what MATLAB's <c>format</c> command switches. The
/// default shows full round-trip precision (MATLAB's <c>format long</c>, and what JGraph always
/// printed); <c>format short</c> trims to 5 significant digits, and the E modes force exponent
/// notation. One process-wide state, like the figure registry: the app has one console, and
/// creating a session resets it.
/// </summary>
internal static class JgsNumberFormat
{
    /// <summary>The display precision modes <c>format</c> can select.</summary>
    internal enum Mode
    {
        /// <summary>Full round-trip precision (<c>format long</c>, the default).</summary>
        Long,

        /// <summary>5 significant digits; integers still print in full (<c>format short</c>).</summary>
        Short,

        /// <summary>Exponent notation with 4 decimals (<c>format shortE</c>).</summary>
        ShortE,

        /// <summary>Exponent notation with 15 decimals (<c>format longE</c>).</summary>
        LongE,
    }

    /// <summary>The mode numeric display currently uses.</summary>
    private static Mode _current = Mode.Long;
    private static bool _explicit;
    internal static Mode Current { get => _current; set { _current = value; _explicit = true; } }

    /// <summary>MATLAB temporal text defaults to short precision, independently of JGraph's numeric console default.</summary>
    internal static Mode Temporal => _explicit ? _current : Mode.Short;

    /// <summary>Back to the default, as a fresh session (or bare <c>format</c>) asks.</summary>
    internal static void Reset() { _current = Mode.Long; _explicit = false; }

    /// <summary>Formats <paramref name="value"/> in the current mode.</summary>
    internal static string Format(double value)
    {
        switch (Current)
        {
            case Mode.Short:
                // MATLAB's short keeps whole numbers exact and trims fractions to 5 significant digits.
                if (double.IsFinite(value) && value == Math.Floor(value) && Math.Abs(value) < 1e15)
                {
                    return value.ToString("R", CultureInfo.InvariantCulture);
                }

                return value.ToString("G5", CultureInfo.InvariantCulture);
            case Mode.ShortE:
                return value.ToString("0.0000e+00", CultureInfo.InvariantCulture);
            case Mode.LongE:
                return value.ToString("0.000000000000000e+00", CultureInfo.InvariantCulture);
            default:
                return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
