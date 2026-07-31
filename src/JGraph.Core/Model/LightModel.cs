using System.ComponentModel;
using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>How a <see cref="LightModel"/> illuminates the scene (MATLAB's light <c>Style</c>).</summary>
public enum LightStyle
{
    /// <summary>A directional light infinitely far away: <see cref="LightModel.Position"/> is a direction.</summary>
    Infinite,

    /// <summary>A point light inside the axes box: <see cref="LightModel.Position"/> is a position.</summary>
    Local,
}

/// <summary>
/// A light illuminating the 3D content of an <see cref="AxesModel"/> (MATLAB's <c>light</c> object).
/// Lights sum, and an axes has none until a script adds one — which is why a plain <c>surf</c> shows
/// flat colormap color here exactly as it does in MATLAB.
/// </summary>
/// <remarks>
/// <see cref="Position"/> is in the projection's <em>normalized</em> cube space, where the data box
/// spans [-0.5, 0.5] on every axis, not in data units. That is deliberate: a surface whose Z is in
/// millions and whose X is in units would otherwise light like a vertical wall.
/// </remarks>
public sealed class LightModel : GraphObject
{
    private LightStyle _style = LightStyle.Infinite;
    private Vector3D _position = new(1, 0, 1);
    private Color _color = Colors.White;
    private bool _followsCamera;

    public LightModel()
    {
        Name = "Light";
    }

    /// <summary>Whether this is a directional light or a point light.</summary>
    [Category("Lighting")]
    public LightStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, value, InvalidationKind.Render);
    }

    /// <summary>
    /// The light's direction (<see cref="LightStyle.Infinite"/>) or position (<see cref="LightStyle.Local"/>)
    /// in normalized cube space — or, when <see cref="FollowsCamera"/> is set, its coefficients along
    /// the camera's right, up, and view axes.
    /// </summary>
    [Category("Lighting")]
    public Vector3D Position
    {
        get => _position;
        set => SetProperty(ref _position, value, InvalidationKind.Render);
    }

    /// <summary>The light's color.</summary>
    [Category("Lighting")]
    public Color Color
    {
        get => _color;
        set => SetProperty(ref _color, value, InvalidationKind.Render);
    }

    /// <summary>
    /// When true, <see cref="Position"/> is read in camera axes (right, up, toward the viewer), so the
    /// light travels with the camera and the highlight stays put as the figure is rotated.
    /// </summary>
    /// <remarks>
    /// A documented divergence: MATLAB's <c>camlight</c> resolves a fixed world position when it is
    /// called and does <em>not</em> track the camera, so its highlight is left behind on the first
    /// drag. Following is the useful reading for an interactive figure, so the <c>camlight</c> verb
    /// sets this — clear it to get MATLAB's behavior back.
    /// </remarks>
    [Category("Lighting"), DisplayName("Follows camera")]
    public bool FollowsCamera
    {
        get => _followsCamera;
        set => SetProperty(ref _followsCamera, value, InvalidationKind.Render);
    }
}
