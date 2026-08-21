using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Maths.Volumes;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB's <c>slice</c> (M72): coloured planes cut through a volume.
/// </summary>
/// <remarks>
/// <para>
/// The name is taken twice. JGS has had <c>slice(array, start, stop)</c> — a piece of a list — since
/// long before there were volumes to cut, and that surface is frozen, so this is registered only for
/// the MATLAB dialect, where it shadows the array reading. A JGS script sees the list slicer it
/// always saw; a MATLAB script sees the verb MATLAB documents.
/// </para>
/// <para>
/// Each plane is drawn as a patch rather than a surface because a surface here is a height over the
/// x-y plane and two of the three slice orientations stand vertically, where no such height exists.
/// The patch carries one interpolated reading per vertex and interpolates between them, which is the
/// picture MATLAB draws.
/// </para>
/// <para>
/// Divergence: MATLAB also accepts a slicing <em>surface</em> — <c>slice(X, Y, Z, V, XI, YI, ZI)</c>
/// with three matrices — and rotated planes through <c>surf</c> geometry. Only the axis-aligned plane
/// form is drawn here; the trailing interpolation-method word is accepted and read linearly.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>Declares MATLAB's volume <c>slice</c> over the JGS array slicer.</summary>
    private static void RegisterVolumeSlice(JgsEnvironment env)
    {
        env.Declare("slice", JgsValue.Function(
            new BuiltinFunction("slice", OnNamedAxes((args, line, col) => Slice(args, line, col)))
            {
                BindsAnsAsStatement = false,
            }));
    }

    private static JgsValue Slice(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("slice", args, 4, 8, line, col);

        // A trailing word names the interpolation MATLAB would use between grid points. Every reading
        // here is trilinear, so the word is checked and then does nothing rather than being ignored
        // silently — a script asking for 'cubic' should learn that it did not get it.
        if (args[^1].Type == JgsType.String)
        {
            string how = args[^1].AsString;
            if (!how.Equals("linear", StringComparison.OrdinalIgnoreCase)
                && !how.Equals("nearest", StringComparison.OrdinalIgnoreCase)
                && !how.Equals("cubic", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(
                    line, col, $"slice: unknown interpolation '{how}'. Use linear, nearest, or cubic.");
            }

            args = [.. args.Take(args.Count - 1)];
        }

        // The form is settled by counting, not by the family's usual look-at-the-first-argument rule:
        // a plane list is a scalar or an empty, so slice(V, 6, [], []) has four arguments and would
        // be read by that rule as a grid of three plus one reading. Four means no grid; seven means
        // one. Nothing else is a slice.
        if (args.Count is not (4 or 7))
        {
            throw new JgsRuntimeException(line, col,
                "slice takes V, sx, sy, sz — or the grid X, Y, Z and then V, sx, sy, sz. Any plane "
                + "list may be [] for none.");
        }

        int at = args.Count == 7 ? 3 : 0;
        double[,,] volume = ReadVolume("slice", args[at], at, line, col);
        int rows = volume.GetLength(0), columns = volume.GetLength(1), pages = volume.GetLength(2);
        ScalarField field = at == 0
            ? new ScalarField(Counting(columns), Counting(rows), Counting(pages), volume)
            : new ScalarField(
                GridVector("slice", args[0], 1, columns, line, col),
                GridVector("slice", args[1], 0, rows, line, col),
                GridVector("slice", args[2], 2, pages, line, col),
                volume);

        int next = at + 1;
        double[] sx = PlaneList("slice", args, next, line, col);
        double[] sy = PlaneList("slice", args, next + 1, line, col);
        double[] sz = PlaneList("slice", args, next + 2, line, col);
        if (sx.Length + sy.Length + sz.Length == 0)
        {
            throw new JgsRuntimeException(line, col, "slice: every plane list is empty, so there is nothing to cut.");
        }

        var drawn = new List<JgsValue>();
        foreach (double x in sx)
        {
            drawn.Add(Handle(CutPlane(field, Axis.X, x)));
        }

        foreach (double y in sy)
        {
            drawn.Add(Handle(CutPlane(field, Axis.Y, y)));
        }

        foreach (double z in sz)
        {
            drawn.Add(Handle(CutPlane(field, Axis.Z, z)));
        }

        AxesModel axes = JG.Gca();
        axes.Is3D = true;
        return JgsValue.Array([.. drawn]);
    }

    /// <summary>Which direction a plane is normal to.</summary>
    private enum Axis
    {
        X,
        Y,
        Z,
    }

    /// <summary>
    /// One plane through <paramref name="field"/>, as a patch whose vertices sit on the field's own
    /// grid in the two directions the plane spans. Sampling on that grid rather than on a grid of our
    /// own is what makes the picture agree with the volume everywhere the volume was measured.
    /// </summary>
    private static PatchPlot CutPlane(ScalarField field, Axis normal, double at)
    {
        double[] across = normal == Axis.X ? field.Y : field.X;
        double[] down = normal == Axis.Z ? field.Y : field.Z;

        int wide = across.Length;
        int tall = down.Length;
        var x = new double[wide * tall];
        var y = new double[wide * tall];
        var z = new double[wide * tall];
        var colours = new double[wide * tall];

        for (int r = 0; r < tall; r++)
        {
            for (int c = 0; c < wide; c++)
            {
                int v = (r * wide) + c;
                (x[v], y[v], z[v]) = normal switch
                {
                    Axis.X => (at, across[c], down[r]),
                    Axis.Y => (across[c], at, down[r]),
                    _ => (across[c], down[r], at),
                };

                colours[v] = field.Sample(x[v], y[v], z[v]);
            }
        }

        var faces = new int[(wide - 1) * (tall - 1)][];
        int face = 0;
        for (int r = 0; r < tall - 1; r++)
        {
            for (int c = 0; c < wide - 1; c++)
            {
                int v = (r * wide) + c;
                faces[face++] = [v, v + 1, v + wide + 1, v + wide];
            }
        }

        PatchPlot patch = JG.Patch(x, y, z, faces);
        patch.ColorData = colours;
        patch.Shading = PatchShading.Interp;

        // A slice is a window onto the volume, not a wireframe of one: MATLAB draws the grid lines
        // only when a script asks for them back.
        patch.EdgeColor = null;
        patch.Name = "Slice";
        return patch;
    }

    /// <summary>
    /// One of the three plane lists. An empty value means no planes in that direction, which is how
    /// MATLAB writes a cut that runs only one way — <c>slice(V, [], [], 5)</c>.
    /// </summary>
    private static double[] PlaneList(string verb, IReadOnlyList<JgsValue> args, int index, int line, int col)
    {
        JgsValue value = args[index];
        if (value.Type == JgsType.Array && value.ArrayLength == 0)
        {
            return [];
        }

        return ToDoubles($"{verb}: plane list", value, line, col);
    }
}
