using JGraph.Maths.Contours;

namespace JGraph.Maths.Volumes;

/// <summary>
/// The things done to a triangulated surface after it has been found: making it smaller, shrinking
/// its faces away from each other, and working out which way each vertex points.
/// </summary>
public static class MeshOperations
{
    /// <summary>
    /// A mesh with about the requested share of its faces, kept by clustering vertices onto a coarser
    /// lattice and dropping the triangles that collapse.
    /// </summary>
    /// <param name="mesh">The mesh to reduce.</param>
    /// <param name="keep">The share of faces to aim for, between 0 and 1.</param>
    /// <remarks>
    /// <para>
    /// Vertex clustering rather than edge collapse: every vertex is moved to the centre of the
    /// lattice cell it falls in, vertices sharing a cell become one, and a triangle with two corners
    /// now equal is no longer a triangle and goes. It is a cruder reduction than the error-driven
    /// collapse MATLAB uses, and it moves vertices where MATLAB's keeps a subset of the originals —
    /// a recorded divergence rather than an approximation of the same answer.
    /// </para>
    /// <para>
    /// The lattice is chosen by search, because the face count a given lattice yields cannot be
    /// predicted from the mesh: the reduction doubles the lattice until it reaches the target, then
    /// narrows between the last two to find the coarsest lattice that still does. Stopping at the
    /// first doubling that succeeds would routinely overshoot — a mesh asked for a fifth of its faces
    /// would be handed half of them — so the narrowing is what makes the answer mean what was asked.
    /// It is still an inequality rather than an equality: the answer is the closest lattice at or
    /// above the target, never fewer faces than were asked for.
    /// </para>
    /// </remarks>
    public static IsoMesh Reduce(IsoMesh mesh, double keep)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (!(keep > 0) || keep >= 1 || mesh.Faces.Length == 0)
        {
            return mesh;
        }

        int target = System.Math.Max(1, (int)System.Math.Round(mesh.Faces.Length * keep));

        // Double until the target is reached, keeping the last lattice that fell short.
        int low = 1;
        int high = 0;
        IsoMesh? reached = null;
        for (int divisions = 2; divisions <= 1024; divisions *= 2)
        {
            IsoMesh candidate = Cluster(mesh, divisions);
            if (candidate.Faces.Length >= target)
            {
                high = divisions;
                reached = candidate;
                break;
            }

            low = divisions;
        }

        if (reached is null)
        {
            return mesh;
        }

        // Narrow: the coarsest lattice between them that still reaches the target is the answer.
        while (high - low > 1)
        {
            int middle = low + ((high - low) / 2);
            IsoMesh candidate = Cluster(mesh, middle);
            if (candidate.Faces.Length >= target)
            {
                high = middle;
                reached = candidate;
            }
            else
            {
                low = middle;
            }
        }

        return reached;
    }

    /// <summary>
    /// Every face pulled in towards its own centre, so the faces come apart and each can be seen
    /// separately. A factor of 1 leaves the mesh alone.
    /// </summary>
    /// <remarks>
    /// Shrinking necessarily breaks the sharing of vertices — two faces that met at a corner now have
    /// a corner each — so the answer has one vertex per face corner and always more vertices than it
    /// started with. That is what the operation means, not a loss.
    /// </remarks>
    public static IsoMesh Shrink(IsoMesh mesh, double factor)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var vx = new List<double>();
        var vy = new List<double>();
        var vz = new List<double>();
        var faces = new List<int[]>(mesh.Faces.Length);

        foreach (int[] face in mesh.Faces)
        {
            if (face.Length == 0)
            {
                continue;
            }

            double cx = 0, cy = 0, cz = 0;
            foreach (int v in face)
            {
                cx += mesh.X[v];
                cy += mesh.Y[v];
                cz += mesh.Z[v];
            }

            cx /= face.Length;
            cy /= face.Length;
            cz /= face.Length;

            var corners = new int[face.Length];
            for (int i = 0; i < face.Length; i++)
            {
                int v = face[i];
                vx.Add(cx + ((mesh.X[v] - cx) * factor));
                vy.Add(cy + ((mesh.Y[v] - cy) * factor));
                vz.Add(cz + ((mesh.Z[v] - cz) * factor));
                corners[i] = vx.Count - 1;
            }

            faces.Add(corners);
        }

        return new IsoMesh([.. vx], [.. vy], [.. vz], [.. faces]);
    }

    /// <summary>
    /// The direction each vertex of a surface faces, taken from the field the surface was found in
    /// rather than from the triangles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The obvious way to do this is to average the directions of the triangles meeting at each
    /// vertex, and it does not work here: <see cref="MarchingTetrahedra"/> does not make its winding
    /// consistent, so two triangles sharing a vertex may report opposite directions and cancel rather
    /// than agree. The field has no such ambiguity — a surface sits across the slope of the field it
    /// was cut from, so the slope at a vertex is the direction that vertex faces, and it is the same
    /// answer however the triangles around it happen to be wound.
    /// </para>
    /// <para>
    /// The slope is negated, so the normals point away from the higher readings — outward from a
    /// region enclosed by a field that grows towards it, which is the way round a lighting model
    /// wants them and the convention MATLAB's <c>isonormals</c> uses.
    /// </para>
    /// </remarks>
    public static (double[] X, double[] Y, double[] Z) Normals(ScalarField field, IsoMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(mesh);

        (ScalarField gx, ScalarField gy, ScalarField gz) = field.Gradient();
        var nx = new double[mesh.VertexCount];
        var ny = new double[mesh.VertexCount];
        var nz = new double[mesh.VertexCount];

        for (int i = 0; i < mesh.VertexCount; i++)
        {
            double x = mesh.X[i], y = mesh.Y[i], z = mesh.Z[i];
            double dx = -gx.Sample(x, y, z);
            double dy = -gy.Sample(x, y, z);
            double dz = -gz.Sample(x, y, z);
            double length = System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (length > 0 && double.IsFinite(length))
            {
                nx[i] = dx / length;
                ny[i] = dy / length;
                nz[i] = dz / length;
            }
        }

        return (nx, ny, nz);
    }

    /// <summary>
    /// The reading of a field at every vertex of a mesh — what paints a surface found in one field
    /// with the values of another.
    /// </summary>
    public static double[] SampleAt(ScalarField field, IsoMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(mesh);
        var colors = new double[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            colors[i] = field.Sample(mesh.X[i], mesh.Y[i], mesh.Z[i]);
        }

        return colors;
    }

    /// <summary>
    /// The quadrilaterals of a surface grid, as a mesh — the <c>surf2patch</c> conversion, which is
    /// how a surface becomes something <c>patch</c> can draw.
    /// </summary>
    public static IsoMesh FromSurface(double[,] x, double[,] y, double[,] z)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(z);

        int rows = z.GetLength(0);
        int columns = z.GetLength(1);
        if (x.GetLength(0) != rows || x.GetLength(1) != columns
            || y.GetLength(0) != rows || y.GetLength(1) != columns)
        {
            throw new ArgumentException("The three grids have to be the same size.", nameof(z));
        }

        var vx = new double[rows * columns];
        var vy = new double[rows * columns];
        var vz = new double[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                int v = (c * rows) + r;
                vx[v] = x[r, c];
                vy[v] = y[r, c];
                vz[v] = z[r, c];
            }
        }

        var faces = new List<int[]>();
        for (int r = 0; r + 1 < rows; r++)
        {
            for (int c = 0; c + 1 < columns; c++)
            {
                faces.Add([
                    (c * rows) + r,
                    (c * rows) + r + 1,
                    ((c + 1) * rows) + r + 1,
                    ((c + 1) * rows) + r]);
            }
        }

        return new IsoMesh(vx, vy, vz, [.. faces]);
    }

    private static IsoMesh Cluster(IsoMesh mesh, int divisions)
    {
        (double minX, double spanX) = Extent(mesh.X);
        (double minY, double spanY) = Extent(mesh.Y);
        (double minZ, double spanZ) = Extent(mesh.Z);

        var cellOf = new Dictionary<(int, int, int), int>();
        var vertexFor = new int[mesh.VertexCount];
        var sx = new List<double>();
        var sy = new List<double>();
        var sz = new List<double>();
        var counts = new List<int>();

        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var cell = (
                Bucket(mesh.X[i], minX, spanX, divisions),
                Bucket(mesh.Y[i], minY, spanY, divisions),
                Bucket(mesh.Z[i], minZ, spanZ, divisions));

            if (!cellOf.TryGetValue(cell, out int index))
            {
                index = sx.Count;
                cellOf[cell] = index;
                sx.Add(0);
                sy.Add(0);
                sz.Add(0);
                counts.Add(0);
            }

            sx[index] += mesh.X[i];
            sy[index] += mesh.Y[i];
            sz[index] += mesh.Z[i];
            counts[index]++;
            vertexFor[i] = index;
        }

        for (int i = 0; i < sx.Count; i++)
        {
            sx[i] /= counts[i];
            sy[i] /= counts[i];
            sz[i] /= counts[i];
        }

        var faces = new List<int[]>();
        foreach (int[] face in mesh.Faces)
        {
            var mapped = new int[face.Length];
            for (int i = 0; i < face.Length; i++)
            {
                mapped[i] = vertexFor[face[i]];
            }

            if (mapped.Distinct().Count() == mapped.Length)
            {
                faces.Add(mapped);
            }
        }

        return new IsoMesh([.. sx], [.. sy], [.. sz], [.. faces]);
    }

    private static (double Min, double Span) Extent(double[] values)
    {
        if (values.Length == 0)
        {
            return (0, 1);
        }

        double min = values.Min();
        double span = values.Max() - min;
        return (min, span > 0 ? span : 1);
    }

    private static int Bucket(double value, double min, double span, int divisions) =>
        System.Math.Clamp((int)((value - min) / span * divisions), 0, divisions - 1);
}
