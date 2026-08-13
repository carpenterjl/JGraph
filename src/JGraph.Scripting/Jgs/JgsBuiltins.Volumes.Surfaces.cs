using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Maths.Contours;
using JGraph.Maths.Volumes;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M59: the verbs that turn a field into a surface, and the ones that work on the surface afterwards.
/// </summary>
/// <remarks>
/// Every verb here follows MATLAB's rule for this family: asked for nothing it draws, asked for
/// something it answers with the shape and draws nothing. That is what makes
/// <c>fv = isosurface(V, 0.5)</c> followed by <c>patch(fv)</c> the documented way to colour a surface
/// before it goes on screen, and it is why the struct these verbs hand back is the struct
/// <c>patch</c> reads.
/// </remarks>
internal static partial class JgsBuiltins
{
    private static void RegisterIsoSurfaceBuiltins(JgsEnvironment env)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        // These four draw when nobody wanted the shape and answer with it when somebody did, which
        // is a distinction only KnowsWhenDiscarded can make: `isosurface(V, 1)` on its own is a
        // picture and `fv = isosurface(V, 1)` is a struct, and after the call has been evaluated the
        // two look identical.
        void DefineDrawOrShape(
            string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, (args, line, col) => body(args, 1, line, col)[0])
                {
                    BindsAnsAsStatement = false,
                    KnowsWhenDiscarded = true,
                    MultiOutput = body,
                }));

        DefineDrawOrShape("isosurface", IsoSurface);
        DefineDrawOrShape("isocaps", IsoCapsVerb);
        DefineDrawOrShape("reducepatch", ReducePatch);
        DefineDrawOrShape("shrinkfaces", ShrinkFaces);

        Define("isonormals", (args, line, col) => IsoNormals(args, line, col));
        Define("isocolors", (args, line, col) => IsoColors(args, line, col));

        env.Declare("surf2patch", JgsValue.Function(new BuiltinFunction("surf2patch",
            (args, line, col) => Surf2Patch(args, 1, line, col)[0])
        {
            MultiOutput = (args, wanted, line, col) => Surf2Patch(args, wanted, line, col),
        }));

    }

    // --- The face/vertex answer these verbs share -------------------------------------------------

    /// <summary>
    /// A mesh as the answer a script asked for: the struct when it wanted one value, the faces and
    /// vertices separately when it wanted more, and a drawn patch when it wanted nothing at all.
    /// </summary>
    /// <param name="mesh">The shape to answer with or draw.</param>
    /// <param name="colors">A reading at each vertex to paint with, or null for no colouring.</param>
    /// <param name="wanted">
    /// How many outputs the caller asked for; zero means it was called as a statement and should
    /// draw.
    /// </param>
    /// <param name="decorate">Anything else to do to the patch when one is drawn.</param>
    private static JgsValue[] MeshAnswer(
        IsoMesh mesh, double[]? colors, int wanted, Action<PatchPlot>? decorate = null)
    {
        if (wanted == 0)
        {
            PatchPlot patch = JG.Patch(mesh.X, mesh.Y, mesh.Z, mesh.Faces);
            if (colors is not null && colors.Length == mesh.VertexCount)
            {
                patch.ColorData = colors;
                patch.Shading = PatchShading.Interp;
            }

            decorate?.Invoke(patch);
            return [HandlesFor<PatchPlot>([patch])];
        }

        if (wanted == 1)
        {
            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["faces"] = FacesValue(mesh),
                ["vertices"] = VerticesValue(mesh),
            };

            if (colors is not null && colors.Length == mesh.VertexCount)
            {
                fields["facevertexcdata"] = JgsMatrix.FromColumnMajorDims(
                    colors, [colors.Length, 1]);
            }

            return [JgsValue.Struct(fields)];
        }

        return wanted == 2 || colors is null || colors.Length != mesh.VertexCount
            ? [FacesValue(mesh), VerticesValue(mesh)]
            : [
                FacesValue(mesh),
                VerticesValue(mesh),
                JgsMatrix.FromColumnMajorDims(colors, [colors.Length, 1])];
    }

    /// <summary>A mesh's faces, one row each, counting vertices from one and padded with NaN.</summary>
    private static JgsValue FacesValue(IsoMesh mesh)
    {
        int widest = 0;
        foreach (int[] face in mesh.Faces)
        {
            widest = System.Math.Max(widest, face.Length);
        }

        int rows = mesh.Faces.Length;
        var flat = new double[rows * widest];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < widest; c++)
            {
                flat[r + (c * rows)] = c < mesh.Faces[r].Length
                    ? mesh.Faces[r][c] + 1
                    : double.NaN;
            }
        }

        return JgsMatrix.FromColumnMajorDims(flat, [rows, widest]);
    }

    /// <summary>A mesh's vertices as an n-by-3 table.</summary>
    private static JgsValue VerticesValue(IsoMesh mesh)
    {
        int count = mesh.VertexCount;
        var flat = new double[count * 3];
        for (int i = 0; i < count; i++)
        {
            flat[i] = mesh.X[i];
            flat[i + count] = mesh.Y[i];
            flat[i + (2 * count)] = mesh.Z[i];
        }

        return JgsMatrix.FromColumnMajorDims(flat, [count, 3]);
    }

    /// <summary>
    /// A mesh read back out of what a script is holding: a face/vertex struct, a patch handle, or the
    /// two arrays given separately.
    /// </summary>
    private static IsoMesh ReadMesh(
        string verb, IReadOnlyList<JgsValue> args, ref int at, int line, int col)
    {
        if (at >= args.Count)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb} needs a patch handle, a struct of faces and vertices, or the two arrays.");
        }

        JgsValue first = args[at];

        if (first.Type == JgsType.Struct)
        {
            at++;
            Dictionary<string, JgsValue> fields = first.AsStruct;
            JgsValue? faces = FieldNamed(fields, "faces");
            JgsValue? vertices = FieldNamed(fields, "vertices");
            if (faces is null || vertices is null)
            {
                throw new JgsRuntimeException(line, col,
                    $"{verb}: a struct has to hold both 'faces' and 'vertices'.");
            }

            return MeshFrom(verb, faces, vertices, line, col);
        }

        if (PatchOf(first) is PatchPlot patch)
        {
            at++;
            return MeshOfPatch(patch);
        }

        if (at + 1 < args.Count)
        {
            JgsValue faces = args[at];
            JgsValue vertices = args[at + 1];
            at += 2;
            return MeshFrom(verb, faces, vertices, line, col);
        }

        throw new JgsRuntimeException(line, col,
            $"{verb} needs a patch handle, a struct of faces and vertices, or the two arrays.");
    }

    private static JgsValue? FieldNamed(Dictionary<string, JgsValue> fields, string wanted)
    {
        foreach ((string name, JgsValue value) in fields)
        {
            if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>The mesh a patch is drawing.</summary>
    private static IsoMesh MeshOfPatch(PatchPlot patch) => new(
        [.. patch.X],
        [.. patch.Y],
        [.. patch.Z],
        [.. patch.Faces.Select(face => (int[])face.Clone())]);

    /// <summary>A mesh from the face table and vertex table a script wrote, counting from one.</summary>
    private static IsoMesh MeshFrom(
        string verb, JgsValue facesValue, JgsValue verticesValue, int line, int col)
    {
        double[,] vertices = Rectangle($"{verb}: vertices", verticesValue, line, col);
        int count = vertices.GetLength(0);
        int components = vertices.GetLength(1);
        if (components is < 2 or > 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{verb}: vertices need 2 or 3 columns, but this table has {components}.");
        }

        var x = new double[count];
        var y = new double[count];
        var z = new double[count];
        for (int i = 0; i < count; i++)
        {
            x[i] = vertices[i, 0];
            y[i] = vertices[i, 1];
            z[i] = components == 3 ? vertices[i, 2] : 0;
        }

        double[,] rows = Rectangle($"{verb}: faces", facesValue, line, col);
        var faces = new List<int[]>(rows.GetLength(0));
        for (int r = 0; r < rows.GetLength(0); r++)
        {
            var corners = new List<int>(rows.GetLength(1));
            for (int c = 0; c < rows.GetLength(1); c++)
            {
                double raw = rows[r, c];
                if (double.IsNaN(raw))
                {
                    continue;
                }

                int index = (int)System.Math.Round(raw) - 1;
                if (index < 0 || index >= count)
                {
                    throw new JgsRuntimeException(line, col,
                        $"{verb}: face {r + 1} names vertex {raw}, but there are only {count}.");
                }

                corners.Add(index);
            }

            if (corners.Count > 0)
            {
                faces.Add([.. corners]);
            }
        }

        return new IsoMesh(x, y, z, [.. faces]);
    }

    // --- isosurface and isocaps -------------------------------------------------------------------

    /// <summary>
    /// <c>isosurface(X, Y, Z, V, level)</c>: the surface where the readings reach the level, over the
    /// tetrahedra <see cref="MarchingTetrahedra"/> cuts the grid into.
    /// </summary>
    private static JgsValue[] IsoSurface(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("isosurface", args, 1, 7, line, col);
        (ScalarField field, int next) = ReadScalarField("isosurface", args, line, col);

        double? level = null;
        ScalarField? paint = null;
        if (next < args.Count && IsNumericArgument(args[next]))
        {
            // A second volume here is the colours; a single number is the level.
            if (JgsMatrix.DimsOf(args[next]).Length >= 3)
            {
                paint = new ScalarField(
                    field.X, field.Y, field.Z, ReadVolume("isosurface", args[next], next, line, col));
            }
            else
            {
                level = Num("isosurface", args, next, line, col);
            }

            next++;
        }

        if (next < args.Count && IsNumericArgument(args[next]) && paint is null)
        {
            paint = new ScalarField(
                field.X, field.Y, field.Z, ReadVolume("isosurface", args[next], next, line, col));
            next++;
        }

        for (int i = next; i < args.Count; i++)
        {
            string word = Str("isosurface", args, i, line, col);
            if (!word.Equals("noshare", StringComparison.OrdinalIgnoreCase)
                && !word.Equals("verbose", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"isosurface: '{word}' is not a word here; it is 'noshare' or 'verbose'. "
                    + "Both are accepted and change nothing — vertices are always shared here, "
                    + "which is what makes the surface watertight.");
            }
        }

        double at = level ?? MidwayLevel(field);
        IsoMesh mesh = MarchingTetrahedra.Surface(field.X, field.Y, field.Z, field.Values, at);
        double[]? colors = paint is null ? null : MeshOperations.SampleAt(paint, mesh);
        return MeshAnswer(mesh, colors, wanted);
    }

    /// <summary>
    /// <c>isocaps(X, Y, Z, V, level)</c>: the lids that close an isosurface where it runs into the
    /// side of its box.
    /// </summary>
    private static JgsValue[] IsoCapsVerb(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("isocaps", args, 1, 7, line, col);
        (ScalarField field, int next) = ReadScalarField("isocaps", args, line, col);

        double? level = null;
        if (next < args.Count && IsNumericArgument(args[next]))
        {
            level = Num("isocaps", args, next, line, col);
            next++;
        }

        CapSide side = CapSide.Above;
        for (int i = next; i < args.Count; i++)
        {
            string word = Str("isocaps", args, i, line, col);
            if (word.Equals("below", StringComparison.OrdinalIgnoreCase))
            {
                side = CapSide.Below;
            }
            else if (word.Equals("above", StringComparison.OrdinalIgnoreCase))
            {
                side = CapSide.Above;
            }
            else if (!word.Equals("all", StringComparison.OrdinalIgnoreCase)
                && !word.Equals("enclose", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"isocaps: '{word}' is not a word here; it is 'above', 'below' or 'all'.");
            }
        }

        double at = level ?? MidwayLevel(field);
        IsoMesh mesh = IsoCaps.Surface(field, at, side);
        double[] colors = MeshOperations.SampleAt(field, mesh);
        return MeshAnswer(mesh, colors, wanted);
    }

    /// <summary>
    /// <c>isonormals(X, Y, Z, V, vertices)</c> or <c>isonormals(…, patch)</c>: which way each vertex
    /// of a surface faces, worked out from the field rather than from the triangles.
    /// </summary>
    private static JgsValue IsoNormals(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("isonormals", args, 2, 6, line, col);
        (ScalarField field, int next) = ReadScalarField("isonormals", args, line, col);
        if (next >= args.Count)
        {
            throw new JgsRuntimeException(line, col,
                "isonormals needs the vertices to answer for, or the patch holding them.");
        }

        IsoMesh mesh;
        if (PatchOf(args[next]) is PatchPlot patch)
        {
            mesh = MeshOfPatch(patch);
        }
        else
        {
            double[,] table = Rectangle("isonormals: vertices", args[next], line, col);
            if (table.GetLength(1) != 3)
            {
                throw new JgsRuntimeException(line, col,
                    $"isonormals: vertices need 3 columns, but this table has {table.GetLength(1)}.");
            }

            int count = table.GetLength(0);
            var x = new double[count];
            var y = new double[count];
            var z = new double[count];
            for (int i = 0; i < count; i++)
            {
                x[i] = table[i, 0];
                y[i] = table[i, 1];
                z[i] = table[i, 2];
            }

            mesh = new IsoMesh(x, y, z, []);
        }

        (double[] nx, double[] ny, double[] nz) = MeshOperations.Normals(field, mesh);
        var flat = new double[nx.Length * 3];
        for (int i = 0; i < nx.Length; i++)
        {
            flat[i] = nx[i];
            flat[i + nx.Length] = ny[i];
            flat[i + (2 * nx.Length)] = nz[i];
        }

        return JgsMatrix.FromColumnMajorDims(flat, [nx.Length, 3]);
    }

    /// <summary>
    /// <c>isocolors(X, Y, Z, C, vertices)</c>: the reading of a colour field at each vertex of a
    /// surface, which is what paints a shape found in one field with the values of another.
    /// </summary>
    private static JgsValue IsoColors(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("isocolors", args, 2, 6, line, col);
        (ScalarField field, int next) = ReadScalarField("isocolors", args, line, col);
        if (next >= args.Count)
        {
            throw new JgsRuntimeException(line, col,
                "isocolors needs the vertices to colour, or the patch holding them.");
        }

        PatchPlot? patch = PatchOf(args[next]);
        IsoMesh mesh;
        if (patch is not null)
        {
            mesh = MeshOfPatch(patch);
        }
        else
        {
            double[,] table = Rectangle("isocolors: vertices", args[next], line, col);
            int count = table.GetLength(0);
            var x = new double[count];
            var y = new double[count];
            var z = new double[count];
            for (int i = 0; i < count; i++)
            {
                x[i] = table[i, 0];
                y[i] = table[i, 1];
                z[i] = table.GetLength(1) > 2 ? table[i, 2] : 0;
            }

            mesh = new IsoMesh(x, y, z, []);
        }

        double[] colors = MeshOperations.SampleAt(field, mesh);

        // Handed a patch, isocolors paints it as well as answering — which is the whole reason the
        // handle form exists.
        if (patch is not null)
        {
            patch.ColorData = colors;
            patch.Shading = PatchShading.Interp;
        }

        return JgsMatrix.FromColumnMajorDims(colors, [colors.Length, 1]);
    }

    // --- The verbs that work on a surface ---------------------------------------------------------

    /// <summary>
    /// <c>surf2patch(X, Y, Z)</c>: a surface grid as faces and vertices, so a surface can be drawn as
    /// a patch.
    /// </summary>
    private static JgsValue[] Surf2Patch(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("surf2patch", args, 1, 5, line, col);

        bool triangles = args.Count > 0
            && args[^1].Type == JgsType.String
            && args[^1].AsString.Equals("triangles", StringComparison.OrdinalIgnoreCase);
        int given = triangles ? args.Count - 1 : args.Count;

        double[,] x, y, z;
        if (given == 1 && TargetOf(args[0]) is SurfacePlot surface)
        {
            (x, y, z) = GridsOfSurface(surface);
        }
        else if (given == 1)
        {
            z = Matrix("surf2patch", args, 0, line, col);
            (x, y) = CountingGrid(z.GetLength(0), z.GetLength(1));
        }
        else if (given >= 3)
        {
            x = Matrix("surf2patch", args, 0, line, col);
            y = Matrix("surf2patch", args, 1, line, col);
            z = Matrix("surf2patch", args, 2, line, col);
        }
        else
        {
            throw new JgsRuntimeException(line, col,
                "surf2patch takes a surface handle, Z on its own, or X, Y and Z.");
        }

        IsoMesh mesh;
        try
        {
            mesh = MeshOperations.FromSurface(x, y, z);
        }
        catch (ArgumentException ex)
        {
            throw new JgsRuntimeException(line, col, $"surf2patch: {ex.Message}");
        }

        if (triangles)
        {
            mesh = Triangulated(mesh);
        }

        // surf2patch never draws — it is a conversion, and MATLAB documents it as one.
        return MeshAnswer(mesh, null, System.Math.Max(1, wanted));
    }

    /// <summary>
    /// <c>reducepatch(p, r)</c>: about the given share of the faces, by clustering vertices onto a
    /// coarser lattice.
    /// </summary>
    private static JgsValue[] ReducePatch(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("reducepatch", args, 1, 3, line, col);
        int at = 0;
        PatchPlot? patch = PatchOf(args[0]);
        IsoMesh mesh = ReadMesh("reducepatch", args, ref at, line, col);

        double keep = 0.5;
        if (at < args.Count)
        {
            keep = Num("reducepatch", args, at, line, col);

            // A number above one is a face count rather than a share, which is how MATLAB reads it.
            if (keep > 1)
            {
                keep = mesh.Faces.Length > 0 ? keep / mesh.Faces.Length : 1;
            }

            if (!(keep > 0))
            {
                throw new JgsRuntimeException(line, col,
                    "reducepatch: how much to keep is a share above 0, or a number of faces.");
            }
        }

        IsoMesh smaller = MeshOperations.Reduce(mesh, keep);
        return PatchAnswer(patch, smaller, wanted);
    }

    /// <summary>
    /// <c>shrinkfaces(p, sf)</c>: every face pulled in towards its own centre, so the faces come
    /// apart and each can be seen.
    /// </summary>
    private static JgsValue[] ShrinkFaces(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        ArityRange("shrinkfaces", args, 1, 3, line, col);
        int at = 0;
        PatchPlot? patch = PatchOf(args[0]);
        IsoMesh mesh = ReadMesh("shrinkfaces", args, ref at, line, col);

        double factor = 0.3;
        if (at < args.Count)
        {
            factor = Num("shrinkfaces", args, at, line, col);
            if (!(factor > 0))
            {
                throw new JgsRuntimeException(line, col,
                    "shrinkfaces: the shrink factor is a number above 0; 1 leaves the faces alone.");
            }
        }

        return PatchAnswer(patch, MeshOperations.Shrink(mesh, factor), wanted);
    }

    /// <summary>
    /// What <c>reducepatch</c> and <c>shrinkfaces</c> answer. Handed a patch and asked for nothing,
    /// they change that patch in place rather than drawing a second one — which is what MATLAB does
    /// and the only reading under which <c>reducepatch(p, 0.2)</c> as a statement means anything.
    /// </summary>
    private static JgsValue[] PatchAnswer(PatchPlot? patch, IsoMesh mesh, int wanted)
    {
        if (wanted == 0 && patch is not null)
        {
            patch.SetData(mesh.X, mesh.Y, mesh.Z, mesh.Faces);
            return [JgsValue.Null];
        }

        return MeshAnswer(mesh, null, wanted);
    }

    /// <summary>The object a handle names, or null when the value is not a handle at all.</summary>
    private static GraphObject? TargetOf(JgsValue value) =>
        JgsHandleRegistry.TryGet(value, out JgsHandleEntry? entry) ? entry.Target : null;

    private static PatchPlot? PatchOf(JgsValue value) => TargetOf(value) as PatchPlot;

    /// <summary>The three coordinate grids a surface is drawn over, whether or not it is parametric.</summary>
    private static (double[,] X, double[,] Y, double[,] Z) GridsOfSurface(SurfacePlot surface)
    {
        double[,] z = surface.Z;
        int rows = z.GetLength(0);
        int columns = z.GetLength(1);
        if (surface.XGrid is { } xg && surface.YGrid is { } yg)
        {
            return (xg, yg, z);
        }

        var x = new double[rows, columns];
        var y = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                x[r, c] = surface.X[c];
                y[r, c] = surface.Y[r];
            }
        }

        return (x, y, z);
    }

    /// <summary>Every face of a mesh cut into triangles, as a fan from its first corner.</summary>
    private static IsoMesh Triangulated(IsoMesh mesh)
    {
        var faces = new List<int[]>();
        foreach (int[] face in mesh.Faces)
        {
            for (int i = 1; i + 1 < face.Length; i++)
            {
                faces.Add([face[0], face[i], face[i + 1]]);
            }
        }

        return new IsoMesh(mesh.X, mesh.Y, mesh.Z, [.. faces]);
    }

    /// <summary>The level halfway through a field's readings, which is what these verbs default to.</summary>
    private static double MidwayLevel(ScalarField field)
    {
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        for (int r = 0; r < field.Rows; r++)
        {
            for (int c = 0; c < field.Columns; c++)
            {
                for (int p = 0; p < field.Pages; p++)
                {
                    double value = field.Values[r, c, p];
                    if (double.IsFinite(value))
                    {
                        low = System.Math.Min(low, value);
                        high = System.Math.Max(high, value);
                    }
                }
            }
        }

        return double.IsFinite(low) ? (low + high) / 2 : 0;
    }

    private static (double[,] X, double[,] Y) CountingGrid(int rows, int columns)
    {
        var x = new double[rows, columns];
        var y = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                x[r, c] = c + 1;
                y[r, c] = r + 1;
            }
        }

        return (x, y);
    }

    private static bool IsNumericArgument(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Bool or JgsType.Array;
}
