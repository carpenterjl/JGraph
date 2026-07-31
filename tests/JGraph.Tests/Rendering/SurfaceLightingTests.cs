using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Rendering;
using JGraph.Tests.TestDoubles;
using Xunit;

namespace JGraph.Tests.Rendering;

/// <summary>
/// M44 wave 4: MATLAB-style lighting. The load-bearing property is that none of it happens until a
/// script asks for a light — a plain <c>surf</c> has to stay exactly the flat colormap color it has
/// always been, which is both what MATLAB does and what keeps every other rendering test honest.
/// </summary>
public class SurfaceLightingTests
{
    /// <summary>A flat sheet at z = 0, so every facet normal is exactly +Z and the arithmetic is checkable.</summary>
    private static SurfacePlot FlatSurface(int n = 3) =>
        new(Ramp(n), Ramp(n), new double[n, n]) { Colormap = new Colormap("gray", Colors.White, Colors.White) };

    private static double[] Ramp(int n)
    {
        var v = new double[n];
        for (int i = 0; i < n; i++)
        {
            v[i] = i;
        }

        return v;
    }

    private static RecordingRenderContext Render(AxesModel axes)
    {
        var figure = new FigureModel();
        figure.Axes.Add(axes);
        var context = new RecordingRenderContext(new Size2D(640, 480));
        new FigureRenderer().Render(figure, context);
        return context;
    }

    private static AxesModel AxesWith(SurfacePlot surface)
    {
        var axes = new AxesModel { Is3D = true };
        axes.Plots.Add(surface);
        return axes;
    }

    /// <summary>
    /// The whole point of the wave: a surface with no light on its axes produces the colormap sample
    /// and nothing else. If this ever fails, every existing surface figure has silently changed.
    /// </summary>
    [Fact]
    public void Lighting_IsOffUntilALightExists()
    {
        var surface = new SurfacePlot(new double[,] { { 0, 1 }, { 2, 3 } });
        AxesModel axes = AxesWith(surface);

        RecordingRenderContext unlit = Render(axes);
        uint[] before = unlit.TriangleColors.ToArray();

        // The mean height of the one cell is 1.5, dead centre of the 0..3 range.
        Assert.Equal(surface.Colormap.Sample(1.5, 0, 3).ToArgb(), before[0]);

        axes.Lights.Add(new LightModel());
        RecordingRenderContext lit = Render(axes);

        Assert.NotEqual(before[0], lit.TriangleColors[0]);
    }

    /// <summary>
    /// <c>lighting none</c> opts a surface out even with a light present, which is the only way a
    /// MATLAB script has of keeping one surface unlit in a lit axes.
    /// </summary>
    [Fact]
    public void FaceLightingNone_IgnoresTheLight()
    {
        var surface = new SurfacePlot(new double[,] { { 0, 1 }, { 2, 3 } }) { FaceLighting = SurfaceLighting.None };
        AxesModel axes = AxesWith(surface);
        axes.Lights.Add(new LightModel());

        Assert.Equal(surface.Colormap.Sample(1.5, 0, 3).ToArgb(), Render(axes).TriangleColors[0]);
    }

    /// <summary>
    /// A light straight down the surface normal gives full diffuse and full specular; the same light
    /// swung to graze the surface gives neither. Both are read off a white flat sheet, where the
    /// expected value is arithmetic rather than a golden number.
    /// </summary>
    [Fact]
    public void HeadOnLight_IsBrighterThanAGrazingOne()
    {
        SurfacePlot surface = FlatSurface();
        surface.Material = LightingModel.Dull; // no highlight, so this reads diffuse alone
        AxesModel axes = AxesWith(surface);
        axes.Lights.Add(new LightModel { Position = new Vector3D(0, 0, 1) });

        byte headOn = Unpack(Render(axes).TriangleColors[0]).R;

        // 0.3 ambient + 0.8 diffuse on a white surface saturates.
        Assert.Equal(255, headOn);

        // Grazing: N.L is 0, so only the ambient term survives.
        axes.Lights[0].Position = new Vector3D(1, 0, 0);
        byte grazing = Unpack(Render(axes).TriangleColors[0]).R;

        Assert.InRange(grazing, 75, 78); // 0.3 * 255
    }

    /// <summary>
    /// Seen from below, a level sheet's +Z normal points away from the camera — and MATLAB's default
    /// <c>BackFaceLighting</c> of 'reverselit' flips it rather than letting the facet go black, so a
    /// light on the camera's side lights it. Without the flip the underside of every folded surface
    /// silhouettes.
    /// </summary>
    [Fact]
    public void SeenFromBelow_AFacetIsLitByItsFlippedNormal()
    {
        SurfacePlot surface = FlatSurface();
        surface.Material = LightingModel.Dull;
        AxesModel axes = AxesWith(surface);
        axes.Elevation = -30; // camera under the sheet
        axes.Lights.Add(new LightModel { Position = new Vector3D(0, 0, -1) });

        Assert.Equal(255, Unpack(Render(axes).TriangleColors[0]).R);

        // From above, the same light is genuinely behind the facet and only ambient survives — the
        // flip follows the camera, not the light, which is what 'reverselit' means.
        axes.Elevation = 30;
        Assert.InRange(Unpack(Render(axes).TriangleColors[0]).R, 75, 78);
    }

    /// <summary>Lights sum, so two of them are brighter than one at the same angle.</summary>
    [Fact]
    public void Lights_Sum()
    {
        SurfacePlot surface = FlatSurface();
        surface.Material = new LightingModel(0.1, 0.3, 0, 10, 1);
        AxesModel axes = AxesWith(surface);
        axes.Lights.Add(new LightModel { Position = new Vector3D(0, 0, 1) });

        byte one = Unpack(Render(axes).TriangleColors[0]).R;
        axes.Lights.Add(new LightModel { Position = new Vector3D(0, 0, 1) });
        byte two = Unpack(Render(axes).TriangleColors[0]).R;

        Assert.InRange(one, 101, 103);  // 0.1 + 0.3
        Assert.InRange(two, 178, 180);  // 0.1 + 0.3 + 0.3
    }

    /// <summary>
    /// A hidden light contributes nothing — the same escape hatch <see cref="GraphObject.Visible"/>
    /// gives every other object in the tree.
    /// </summary>
    [Fact]
    public void AHiddenLight_ContributesNothing()
    {
        var surface = new SurfacePlot(new double[,] { { 0, 1 }, { 2, 3 } });
        AxesModel axes = AxesWith(surface);
        axes.Lights.Add(new LightModel { Visible = false });

        Assert.Equal(surface.Colormap.Sample(1.5, 0, 3).ToArgb(), Render(axes).TriangleColors[0]);
    }

    /// <summary>
    /// Gouraud lighting has to promote the palette to one color per vertex, or there would be nothing
    /// to interpolate and the mode would be indistinguishable from flat. A faceted surface emits six
    /// vertices per cell either way, so the tell is that the four corners of a cell over a sloped
    /// region no longer share a color.
    /// </summary>
    [Fact]
    public void GouraudLighting_ColorsEachVertexSeparately()
    {
        var z = new double[3, 3];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                z[r, c] = c * c; // curved along x, so neighbouring vertices differ in slope
            }
        }

        var surface = new SurfacePlot(Ramp(3), Ramp(3), z)
        {
            Colormap = new Colormap("gray", Colors.White, Colors.White),
            FaceLighting = SurfaceLighting.Gouraud,
        };

        AxesModel axes = AxesWith(surface);
        axes.Lights.Add(new LightModel { Position = new Vector3D(0, 0, 1) });

        List<uint> colors = Render(axes).TriangleColors;
        Assert.True(colors.Count >= 6);
        Assert.True(
            colors.Take(6).Distinct().Count() > 1,
            "gouraud lighting must vary within a facet, but every vertex of the first one matched");

        surface.FaceLighting = SurfaceLighting.Flat;
        List<uint> flat = Render(axes).TriangleColors;
        Assert.Single(flat.Take(6).Distinct());
    }

    /// <summary>
    /// A camera-following light travels with the camera, so the shading of a sloped facet changes as
    /// the figure turns and the highlight stays where the viewer is looking. A fixed light stays put
    /// in the data, so a diffuse-only surface shades identically however it is spun — which is
    /// MATLAB's <c>camlight</c>, whose highlight is left behind on the first drag. Following is the
    /// documented divergence, and <c>FollowsCamera = false</c> is how to get MATLAB's reading back.
    /// </summary>
    [Fact]
    public void ACameraLight_TravelsWithTheCamera_AndAFixedOneStaysPut()
    {
        var z = new double[3, 3];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                z[r, c] = c; // a ramp, so the normal is tilted and the light angle actually matters
            }
        }

        var surface = new SurfacePlot(Ramp(3), Ramp(3), z)
        {
            Colormap = new Colormap("gray", Colors.White, Colors.White),
            Material = LightingModel.Dull, // diffuse only, so nothing varies with the view by itself
        };

        AxesModel axes = AxesWith(surface);
        var light = new LightModel { FollowsCamera = true, Position = new Vector3D(0, 0, 1) };
        axes.Lights.Add(light);

        uint following = Render(axes).TriangleColors[0];
        axes.Azimuth += 90;
        Assert.NotEqual(following, Render(axes).TriangleColors[0]);

        light.FollowsCamera = false;
        light.Position = new Vector3D(1, 0, 1);
        axes.Azimuth = -37.5;
        uint stationary = Render(axes).TriangleColors[0];
        axes.Azimuth += 90;
        Assert.Equal(stationary, Render(axes).TriangleColors[0]);
    }

    /// <summary>
    /// Normals are computed in the projection's normalized cube space, not in data units. A surface
    /// whose Z spans millions and whose X spans ones would otherwise have a normal pointing almost
    /// straight along X everywhere, and would light like a wall rather than like a hill.
    /// </summary>
    [Fact]
    public void Normals_AreScaleIndependent()
    {
        static uint LitColorOf(double zScale)
        {
            var z = new double[3, 3];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    z[r, c] = c * zScale;
                }
            }

            var surface = new SurfacePlot(Ramp(3), Ramp(3), z)
            {
                Colormap = new Colormap("gray", Colors.White, Colors.White),
            };

            var axes = new AxesModel { Is3D = true };
            axes.Plots.Add(surface);
            axes.Lights.Add(new LightModel { Position = new Vector3D(0, 0, 1) });

            var figure = new FigureModel();
            figure.Axes.Add(axes);
            var context = new RecordingRenderContext(new Size2D(640, 480));
            new FigureRenderer().Render(figure, context);
            return context.TriangleColors[0];
        }

        Assert.Equal(LitColorOf(1), LitColorOf(1e6));
    }

    /// <summary>Every <c>material</c> preset is reachable by name, and they really do differ.</summary>
    [Theory]
    [InlineData("shiny")]
    [InlineData("dull")]
    [InlineData("metal")]
    [InlineData("default")]
    public void MaterialPresets_ResolveByName(string name)
    {
        Assert.True(LightingModel.TryGetByName(name, out LightingModel material));
        Assert.InRange(material.Ambient, 0, 1);
        Assert.True(material.SpecularExponent > 0);
    }

    [Fact]
    public void MaterialPresets_DifferInTheWaysMatlabDocuments()
    {
        Assert.Equal(0.0, LightingModel.Dull.Specular);              // no highlight at all
        Assert.True(LightingModel.Shiny.SpecularExponent > LightingModel.Default.SpecularExponent);
        Assert.Equal(0.5, LightingModel.Metal.SpecularColorReflectance); // highlight half the surface color
        Assert.False(LightingModel.TryGetByName("plastic", out LightingModel fallback));
        Assert.Equal(LightingModel.Default, fallback);
    }

    /// <summary>With no lights the shader hands the color straight back — the fast path that matters.</summary>
    [Fact]
    public void Shade_WithNoLights_ReturnsTheColorUnchanged()
    {
        Color red = Colors.Red;
        Assert.Equal(
            red,
            LightingModel.Default.Shade(red, Vector3D.Zero, Vector3D.UnitZ, Vector3D.UnitZ, ReadOnlySpan<LightSource>.Empty));
    }

    /// <summary>A local light is a position, so a point beside it is lit and a point far off-axis is not.</summary>
    [Fact]
    public void ALocalLight_IsAPositionNotADirection()
    {
        LightingModel material = LightingModel.Dull;
        var light = new LightSource(new Vector3D(0, 0, 1), Colors.White, IsLocal: true);
        ReadOnlySpan<LightSource> lights = stackalloc LightSource[] { light };

        // Directly beneath the light: the direction to it is +Z, straight along the normal.
        Color under = material.Shade(Colors.White, Vector3D.Zero, Vector3D.UnitZ, Vector3D.UnitZ, lights);

        // Far to one side, in the surface's own plane: the light is nearly edge-on.
        Color aside = material.Shade(
            Colors.White, new Vector3D(1000, 0, 0), Vector3D.UnitZ, Vector3D.UnitZ, lights);

        Assert.True(under.R > aside.R, $"expected the point under the light to be brighter, got {under} vs {aside}");
    }

    /// <summary>The surface's five reflectance coefficients round-trip through the composite property.</summary>
    [Fact]
    public void Material_RoundTripsThroughTheSurface()
    {
        var surface = new SurfacePlot(new double[2, 2]);
        Assert.Equal(LightingModel.Default, surface.Material);

        surface.Material = LightingModel.Metal;
        Assert.Equal(LightingModel.Metal, surface.Material);
        Assert.Equal(0.5, surface.SpecularColorReflectance);
    }

    private static Color Unpack(uint packed) =>
        Color.FromArgb((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
}
