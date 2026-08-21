using JGraph.Core.Primitives;

namespace JGraph.Core.Drawing;

/// <summary>
/// One light contributing to a shaded surface, already resolved against the camera. Everything is
/// expressed in the projection's normalized cube space, where the data box spans [-0.5, 0.5] on
/// every axis — which is what makes a light mean the same thing whatever units the data is in.
/// </summary>
/// <param name="Vector">
/// The direction <em>toward</em> the light for an infinite (directional) light, or the light's
/// position for a local one. Need not be unit length.
/// </param>
/// <param name="Color">The light's color; its intensity per channel is the channel over 255.</param>
/// <param name="IsLocal">Whether <paramref name="Vector"/> is a position rather than a direction.</param>
public readonly record struct LightSource(Vector3D Vector, Color Color, bool IsLocal = false);

/// <summary>
/// The reflectance coefficients of a lit surface, matching MATLAB's <c>material</c> presets and the
/// surface properties they set. Shading is Blinn-Phong:
/// <c>ambient + diffuse·max(0, N·L) + specular·max(0, N·H)^exponent</c>, summed over the lights.
/// </summary>
/// <remarks>
/// A figure has no lights until a script asks for one, so none of this runs by default — which is
/// both MATLAB's behavior (a <c>surf</c> is flat colormap color until a light object exists) and
/// what keeps an unlit surface pixel-for-pixel what it was.
/// </remarks>
/// <param name="Ambient">MATLAB <c>AmbientStrength</c>: the fraction of the surface color present with no light on it.</param>
/// <param name="Diffuse">MATLAB <c>DiffuseStrength</c>: the Lambertian coefficient.</param>
/// <param name="Specular">MATLAB <c>SpecularStrength</c>: the highlight coefficient.</param>
/// <param name="SpecularExponent">MATLAB <c>SpecularExponent</c>: higher is a tighter highlight.</param>
/// <param name="SpecularColorReflectance">
/// MATLAB <c>SpecularColorReflectance</c>: 0 tints the highlight with the surface color, 1 leaves it
/// the light's own color. Metals sit in between.
/// </param>
public readonly record struct LightingModel(
    double Ambient,
    double Diffuse,
    double Specular,
    double SpecularExponent,
    double SpecularColorReflectance)
{
    /// <summary>MATLAB <c>material default</c> — [0.3 0.6 0.9 10 1.0].</summary>
    public static LightingModel Default => new(0.3, 0.6, 0.9, 10, 1.0);

    /// <summary>MATLAB <c>material dull</c> — diffuse only, no highlight at all.</summary>
    public static LightingModel Dull => new(0.3, 0.8, 0.0, 10, 1.0);

    /// <summary>MATLAB <c>material shiny</c> — the default with a tighter highlight.</summary>
    public static LightingModel Shiny => new(0.3, 0.6, 0.9, 20, 1.0);

    /// <summary>MATLAB <c>material metal</c> — little diffuse, a strong highlight half in the surface color.</summary>
    public static LightingModel Metal => new(0.3, 0.3, 1.0, 25, 0.5);

    /// <summary>The preset names <see cref="TryGetByName"/> accepts, in MATLAB's documented order.</summary>
    public static IReadOnlyList<string> PresetNames { get; } = ["shiny", "dull", "metal", "default"];

    /// <summary>Looks up a <c>material</c> preset by name (case-insensitive).</summary>
    public static bool TryGetByName(string? name, out LightingModel material)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "shiny":
                material = Shiny;
                return true;
            case "dull":
                material = Dull;
                return true;
            case "metal":
                material = Metal;
                return true;
            case "default":
                material = Default;
                return true;
            default:
                material = Default;
                return false;
        }
    }

    /// <summary>
    /// Lights one point of a surface. With no lights the color is handed straight back, so an unlit
    /// figure costs nothing and cannot drift.
    /// </summary>
    /// <param name="baseColor">The surface's own color at this point (its colormap sample). Alpha is preserved.</param>
    /// <param name="point">The point being shaded, in normalized cube space (only local lights read it).</param>
    /// <param name="normal">The surface normal there, in normalized cube space; need not be unit length.</param>
    /// <param name="view">The unit direction toward the viewer, in normalized cube space.</param>
    /// <param name="lights">The lights illuminating the surface.</param>
    /// <param name="ambientLight">
    /// The axes' one ambient light color (MATLAB <c>AmbientLightColor</c>), multiplied into the
    /// ambient term per channel. Null means white, which multiplies nothing away.
    /// </param>
    public Color Shade(
        Color baseColor,
        Vector3D point,
        Vector3D normal,
        Vector3D view,
        ReadOnlySpan<LightSource> lights,
        Color? ambientLight = null)
    {
        if (lights.IsEmpty)
        {
            return baseColor;
        }

        Vector3D n = normal.Normalized();
        if (n == Vector3D.Zero)
        {
            return baseColor;
        }

        // MATLAB's default BackFaceLighting is 'reverselit': a facet turned away from the camera is
        // lit by its flipped normal rather than going black, which is what keeps the underside of a
        // folded surface readable instead of silhouetting it.
        if (Vector3D.Dot(n, view) < 0)
        {
            n = -n;
        }

        double br = baseColor.R / 255.0;
        double bg = baseColor.G / 255.0;
        double bb = baseColor.B / 255.0;

        Color ambient = ambientLight ?? Colors.White;
        double r = br * Ambient * (ambient.R / 255.0);
        double g = bg * Ambient * (ambient.G / 255.0);
        double b = bb * Ambient * (ambient.B / 255.0);

        foreach (LightSource light in lights)
        {
            Vector3D l = (light.IsLocal ? light.Vector - point : light.Vector).Normalized();
            double ndl = Vector3D.Dot(n, l);
            if (ndl <= 0)
            {
                continue;
            }

            double lr = light.Color.R / 255.0;
            double lg = light.Color.G / 255.0;
            double lb = light.Color.B / 255.0;

            double kd = Diffuse * ndl;
            r += br * kd * lr;
            g += bg * kd * lg;
            b += bb * kd * lb;

            if (Specular <= 0)
            {
                continue;
            }

            // Blinn's halfway vector rather than the reflected ray: one add and a normalize instead
            // of a reflection, and it is what gives the broader, rounder highlight MATLAB shows.
            double ndh = Vector3D.Dot(n, (l + view).Normalized());
            if (ndh <= 0)
            {
                continue;
            }

            double ks = Specular * Falloff(ndh, SpecularExponent);

            // The highlight runs from the surface's own color to the light's, which is the whole
            // difference between plastic (1) and metal (0.5).
            r += ks * lr * (br + ((1 - br) * SpecularColorReflectance));
            g += ks * lg * (bg + ((1 - bg) * SpecularColorReflectance));
            b += ks * lb * (bb + ((1 - bb) * SpecularColorReflectance));
        }

        return Color.FromScRgb(
            System.Math.Clamp(r, 0, 1),
            System.Math.Clamp(g, 0, 1),
            System.Math.Clamp(b, 0, 1),
            baseColor.A / 255.0);
    }

    /// <summary>
    /// The specular falloff. Every MATLAB preset uses a whole-number exponent (10, 20, 25) and this
    /// runs once per facet — a quarter of a million times a frame on a 500x500 grid — so an integral
    /// exponent goes through repeated squaring rather than the general <c>Math.Pow</c>, which was
    /// most of the cost of turning lighting on.
    /// </summary>
    private static double Falloff(double value, double exponent)
    {
        if (exponent is not (>= 1 and <= 128) || exponent != System.Math.Floor(exponent))
        {
            return System.Math.Pow(value, exponent);
        }

        double result = 1;
        double square = value;
        for (int n = (int)exponent; n > 0; n >>= 1)
        {
            if ((n & 1) != 0)
            {
                result *= square;
            }

            square *= square;
        }

        return result;
    }
}
