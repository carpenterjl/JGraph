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
}
