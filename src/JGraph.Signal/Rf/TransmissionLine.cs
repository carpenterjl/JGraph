namespace JGraph.Signal.Rf;

/// <summary>
/// Closed-form analysis and synthesis for the printed transmission lines used on RF PCBs: microstrip
/// (Hammerstad–Jensen) and stripline (Pozar). Analysis returns a characteristic impedance (and, for
/// microstrip, the effective permittivity); synthesis inverts for the conductor width that hits a
/// target impedance. These are the standard quasi-static approximations — accurate to a percent or so
/// for typical geometries, not a substitute for a full-wave solver.
/// </summary>
public static class TransmissionLine
{
    private const double VacuumImpedance = 376.730313668; // η₀, ohms
    private const double SpeedOfLight = 299792458.0;       // c, m/s

    /// <summary>
    /// Analyzes a microstrip line: given trace width, dielectric height, and relative permittivity
    /// (width and height in any consistent unit), returns the characteristic impedance Z₀ in ohms and
    /// the effective permittivity ε_eff.
    /// </summary>
    public static (double Z0, double Eeff) Microstrip(double width, double height, double relativePermittivity)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be positive.");
        }

        if (relativePermittivity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relativePermittivity), "The relative permittivity must be at least 1.");
        }

        double u = width / height;
        double eeff = MicrostripEeff(u, relativePermittivity);

        // Hammerstad's air-microstrip impedance, then scaled down by the effective permittivity.
        double f = 6 + ((2 * System.Math.PI) - 6) * System.Math.Exp(-System.Math.Pow(30.666 / u, 0.7528));
        double z01 = VacuumImpedance / (2 * System.Math.PI) *
            System.Math.Log((f / u) + System.Math.Sqrt(1 + (2 / u * (2 / u))));
        double z0 = z01 / System.Math.Sqrt(eeff);
        return (z0, eeff);
    }

    /// <summary>
    /// Synthesizes the microstrip trace width (in the same unit as <paramref name="height"/>) that
    /// yields the target characteristic impedance for the given substrate.
    /// </summary>
    public static double MicrostripWidth(double targetZ0, double height, double relativePermittivity)
    {
        if (targetZ0 <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetZ0), "The target impedance and height must be positive.");
        }

        if (relativePermittivity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relativePermittivity), "The relative permittivity must be at least 1.");
        }

        // Z₀ falls monotonically as the width ratio u grows, so bisection on u is robust and needs no
        // branch formula. The bracket spans very thin to very wide traces.
        double low = 1e-3, high = 1e3;
        for (int iteration = 0; iteration < 200; iteration++)
        {
            double mid = 0.5 * (low + high);
            (double z0, _) = Microstrip(mid * height, height, relativePermittivity);
            if (z0 > targetZ0)
            {
                low = mid; // impedance too high → trace too narrow → widen
            }
            else
            {
                high = mid;
            }

            if (high - low < 1e-12 * high)
            {
                break;
            }
        }

        return 0.5 * (low + high) * height;
    }

    /// <summary>
    /// Analyzes a stripline: given trace width, ground-plane spacing, and relative permittivity
    /// (width and spacing in any consistent unit), returns the characteristic impedance Z₀ in ohms.
    /// </summary>
    public static double Stripline(double width, double plateSpacing, double relativePermittivity)
    {
        if (width <= 0 || plateSpacing <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width and plate spacing must be positive.");
        }

        if (relativePermittivity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relativePermittivity), "The relative permittivity must be at least 1.");
        }

        double wOverB = width / plateSpacing;
        double effectiveWidthRatio = wOverB >= 0.35
            ? wOverB
            : wOverB - ((0.35 - wOverB) * (0.35 - wOverB));
        return 30 * System.Math.PI / System.Math.Sqrt(relativePermittivity) /
            (effectiveWidthRatio + 0.441);
    }

    /// <summary>
    /// Synthesizes the stripline trace width (in the same unit as <paramref name="plateSpacing"/>)
    /// that yields the target characteristic impedance for the given substrate.
    /// </summary>
    public static double StriplineWidth(double targetZ0, double plateSpacing, double relativePermittivity)
    {
        if (targetZ0 <= 0 || plateSpacing <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetZ0), "The target impedance and plate spacing must be positive.");
        }

        if (relativePermittivity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relativePermittivity), "The relative permittivity must be at least 1.");
        }

        // Pozar's closed-form inverse, branching on the normalized impedance √εr·Z₀.
        double x = (30 * System.Math.PI / (System.Math.Sqrt(relativePermittivity) * targetZ0)) - 0.441;
        double wOverB = System.Math.Sqrt(relativePermittivity) * targetZ0 < 120
            ? x
            : 0.85 - System.Math.Sqrt(0.6 - x);
        return wOverB * plateSpacing;
    }

    /// <summary>The guided wavelength (metres) at a frequency (hertz) in a medium of effective permittivity ε_eff.</summary>
    public static double GuidedWavelength(double frequencyHz, double effectivePermittivity)
    {
        if (frequencyHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz), "The frequency must be positive.");
        }

        if (effectivePermittivity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(effectivePermittivity), "The effective permittivity must be positive.");
        }

        return SpeedOfLight / (frequencyHz * System.Math.Sqrt(effectivePermittivity));
    }

    /// <summary>The Hammerstad–Jensen effective permittivity for a microstrip of width ratio u = W/h.</summary>
    private static double MicrostripEeff(double u, double er)
    {
        double a = 1
            + (System.Math.Log(((u * u * u * u) + ((u / 52) * (u / 52))) / ((u * u * u * u) + 0.432)) / 49)
            + (System.Math.Log(1 + ((u / 18.1) * (u / 18.1) * (u / 18.1))) / 18.7);
        double b = 0.564 * System.Math.Pow((er - 0.9) / (er + 3), 0.053);
        return ((er + 1) / 2) + ((er - 1) / 2 * System.Math.Pow(1 + (10 / u), -a * b));
    }
}
