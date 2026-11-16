using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// What an axes' groups do to its members when they are drawn and fitted (open item 296, ADR 0360):
/// a member is shown only while every group around it is visible, and is drawn where the matrices of
/// the transforms around it put it. Built once per pass over an axes that has groups; an axes without
/// any has no index (<see cref="For"/> answers null), which is the common case and costs nothing.
/// </summary>
public sealed class GraphicsGroupIndex
{
    private readonly Dictionary<GraphObject, GraphicsGroupModel> _owner = new(ReferenceEqualityComparer.Instance);

    private GraphicsGroupIndex(IReadOnlyList<GraphicsGroupModel> groups)
    {
        foreach (GraphicsGroupModel group in groups)
        {
            foreach (GraphObject member in group.Members)
            {
                _owner[member] = group;
            }
        }
    }

    /// <summary>The index of <paramref name="axes"/>' groups, or null when it has none.</summary>
    public static GraphicsGroupIndex? For(AxesModel axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        IReadOnlyList<GraphicsGroupModel> groups = axes.Groups;
        return groups.Count == 0 ? null : new GraphicsGroupIndex(groups);
    }

    /// <summary>The group <paramref name="member"/> is in, or null.</summary>
    public GraphicsGroupModel? OwnerOf(GraphObject member) =>
        _owner.TryGetValue(member, out GraphicsGroupModel? group) ? group : null;

    /// <summary>
    /// Whether every group around <paramref name="member"/> is visible. R2025b leaves a member's own
    /// <c>Visible</c> as it was when its group is hidden, and does not draw it (probe p296).
    /// </summary>
    public bool Shown(GraphObject member)
    {
        for (GraphicsGroupModel? group = OwnerOf(member); group is not null; group = group.Outer ?? OwnerOf(group))
        {
            if (!group.Visible)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The matrix the transforms around <paramref name="member"/> put it through, outermost last, or
    /// null when none of them moves anything.
    /// </summary>
    public double[,]? MatrixFor(GraphObject member)
    {
        double[,]? total = null;
        for (GraphicsGroupModel? group = OwnerOf(member); group is not null; group = group.Outer ?? OwnerOf(group))
        {
            if (group.MovesMembers)
            {
                total = total is null ? group.Matrix : GraphicsGroupModel.Compose(group.Matrix, total);
            }
        }

        return total;
    }

    /// <summary>
    /// The box <paramref name="matrix"/> moves the box (<paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="z"/>) to: the extents of its eight corners moved. Exact for the translations and
    /// scalings <c>makehgtform</c> builds, and a box around a rotated one.
    /// </summary>
    public static (DataRange X, DataRange Y, DataRange Z) Move(double[,] matrix, DataRange x, DataRange y, DataRange z)
    {
        if (x.IsEmpty || y.IsEmpty)
        {
            return (x, y, z);
        }

        matrix = WithoutRotationResidue(matrix);
        DataRange zz = z.IsEmpty ? new DataRange(0, 0) : z;
        double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        double zMin = double.PositiveInfinity, zMax = double.NegativeInfinity;
        foreach (double cx in (double[])[x.Min, x.Max])
        {
            foreach (double cy in (double[])[y.Min, y.Max])
            {
                foreach (double cz in (double[])[zz.Min, zz.Max])
                {
                    (double mx, double my, double mz) = GraphicsGroupModel.Apply(matrix, cx, cy, cz);
                    xMin = System.Math.Min(xMin, mx);
                    xMax = System.Math.Max(xMax, mx);
                    yMin = System.Math.Min(yMin, my);
                    yMax = System.Math.Max(yMax, my);
                    zMin = System.Math.Min(zMin, mz);
                    zMax = System.Math.Max(zMax, mz);
                }
            }
        }

        return (new DataRange(xMin, xMax), new DataRange(yMin, yMax), z.IsEmpty ? z : new DataRange(zMin, zMax));
    }

    /// <summary>
    /// The box around <paramref name="plot"/> moved by <paramref name="matrix"/>, as R2025b fits it: the
    /// extents of its points moved, where the object gives its points (lines, marker charts, 3-D lines
    /// and patches), else of its data box's eight corners (<see cref="Move"/>). Probe p8ec: a diagonal
    /// line turned by <c>pi/4</c> fits <c>XLim [0 1.2e-16]</c> there where the box's corners gave
    /// <c>[-0.8 0.8]</c>; a triangle, a scatter, a line with a gap all fit their points (open item 348,
    /// ADR 0382).
    /// </summary>
    public static (DataRange X, DataRange Y, DataRange Z) MovePlot(double[,] matrix, PlotObject plot, DataRange z)
    {
        ArgumentNullException.ThrowIfNull(plot);
        if (plot.TransformPoints() is { } points)
        {
            double[,] cleaned = WithoutRotationResidue(matrix);
            double xMin = double.PositiveInfinity, xMax = double.NegativeInfinity;
            double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
            double zMin = double.PositiveInfinity, zMax = double.NegativeInfinity;
            bool any = false;
            foreach ((double px, double py, double pz) in points)
            {
                if (!double.IsFinite(px) || !double.IsFinite(py) || !double.IsFinite(pz))
                {
                    continue;
                }

                (double mx, double my, double mz) = GraphicsGroupModel.Apply(cleaned, px, py, pz);
                xMin = System.Math.Min(xMin, mx);
                xMax = System.Math.Max(xMax, mx);
                yMin = System.Math.Min(yMin, my);
                yMax = System.Math.Max(yMax, my);
                zMin = System.Math.Min(zMin, mz);
                zMax = System.Math.Max(zMax, mz);
                any = true;
            }

            if (any)
            {
                return (new DataRange(xMin, xMax), new DataRange(yMin, yMax), z.IsEmpty ? z : new DataRange(zMin, zMax));
            }
        }

        return Move(matrix, plot.GetXDataBounds(), plot.GetYDataBounds(), z);
    }

    /// <summary>
    /// A matrix whose 3-by-3 part is exactly orthogonal - a rotation, <c>M'M</c> the identity to the
    /// bit - with that part's entries under one eps written as zero, as R2025b fits them: a rotation by
    /// <c>pi/2</c> leaves <c>cos(pi/2) = 6e-17</c>, which R2025b fits as a zero span (<c>[-1 1]</c>)
    /// where a shear or a scaling of the same size keeps its span, and so does a rotation's own
    /// cancellation (<c>pi/4</c>: <c>[0 1.1e-16]</c>) and an entry of <c>1e-15</c> (open item 343, ADR
    /// 0376; probe p7fe, 25 matrices). The translation is left as it is.
    /// </summary>
    private static double[,] WithoutRotationResidue(double[,] matrix)
    {
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                double dot = 0;
                for (int k = 0; k < 3; k++)
                {
                    dot += matrix[k, i] * matrix[k, j];
                }

                if (dot != (i == j ? 1.0 : 0.0))
                {
                    return matrix;
                }
            }
        }

        double[,]? cleaned = null;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (matrix[i, j] != 0 && System.Math.Abs(matrix[i, j]) < 2.220446049250313e-16)
                {
                    cleaned ??= (double[,])matrix.Clone();
                    cleaned[i, j] = 0;
                }
            }
        }

        return cleaned ?? matrix;
    }
}
