using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Maths.Transforms;


namespace JGraph.Objects;

/// <summary>
/// An object that responds to the lights on its axes. MATLAB's <c>lighting</c> and <c>material</c>
/// verbs work every lit object in an axes rather than one class of them, so they ask for this rather
/// than for a surface — which is what let patches join in M72 without either verb learning a second
/// type to look for.
/// </summary>
public interface ILitObject
{
    /// <summary>How this object is shaded (MATLAB <c>FaceLighting</c>).</summary>
    SurfaceLighting FaceLighting { get; set; }

    /// <summary>The five reflectance coefficients (MATLAB <c>material</c>).</summary>
    LightingModel Material { get; set; }
}

/// <summary>
/// Resolving an axes' lights against the camera, which is the same arithmetic for every lit object:
/// a camera-following light is expressed in the projection's own screen frame, a fixed one is taken
/// as written, and an invisible or degenerate one is dropped.
/// </summary>
internal static class SceneLights
{
    /// <summary>
    /// Fills <paramref name="scratch"/> with the axes' visible lights and answers how many there are
    /// through <paramref name="count"/>, or null when nothing would be lit — no light, no axes, or
    /// lighting turned off on the object.
    /// </summary>
    internal static LightSource[]? Resolve(
        GraphObject owner,
        SurfaceLighting lighting,
        Projection3D projection,
        ref LightSource[]? scratch,
        bool exclusive,
        out int count)
    {
        count = 0;
        if (lighting == SurfaceLighting.None || owner.Parent is not AxesModel axes || axes.Lights.Count == 0)
        {
            return null;
        }

        LightSource[] resolved = RenderScratch.Rent(ref scratch, axes.Lights.Count, exclusive);
        Vector3D right = projection.ScreenRight;
        Vector3D up = projection.ScreenUp;
        Vector3D view = projection.ViewDirection;

        foreach (LightModel light in axes.Lights)
        {
            if (!light.Visible)
            {
                continue;
            }

            Vector3D p = light.Position;
            Vector3D v = light.FollowsCamera ? (right * p.X) + (up * p.Y) + (view * p.Z) : p;
            if (v == Vector3D.Zero)
            {
                continue;
            }

            resolved[count++] = new LightSource(v, light.Color, light.Style == LightStyle.Local);
        }

        return count > 0 ? resolved : null;
    }
}
