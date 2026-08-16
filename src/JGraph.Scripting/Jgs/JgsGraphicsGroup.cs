using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A named collection of drawn objects a script can act on as one — MATLAB's <c>hggroup</c>, and with
/// a matrix, its <c>hgtransform</c>.
/// <para>
/// It is deliberately <b>beside</b> the render tree rather than in it. A group in the tree would have
/// to be a plot object of its own: rendered, hit-tested, autoscaled, listed in the plot browser, and
/// written into a saved figure — five surfaces to get right for a container that draws nothing. What a
/// script actually does with a group is turn its members on and off together and move them together,
/// and both of those are things the members already know how to do. So a group is a book of who
/// belongs to it, and its verbs reach through to the members.
/// </para>
/// <para>
/// A transform therefore moves the members' own coordinates. To keep that from compounding, the group
/// remembers what each member looked like when it joined and re-derives from that every time the
/// matrix is set — which is what makes <c>set(t, 'Matrix', …)</c> in a loop an animation rather than a
/// drift. The recorded limits: a group does not clip or z-order its members as a unit, a member reads
/// back its transformed data rather than its original, and a saved figure keeps the members and
/// forgets the grouping.
/// </para>
/// </summary>
internal sealed class JgsGraphicsGroup : GraphObject
{
    /// <summary>What one member looked like before any matrix was applied to it.</summary>
    private readonly Dictionary<PlotObject, (double[] X, double[] Y, double[] Z)> _original = new();

    private readonly List<PlotObject> _members = new();

    public JgsGraphicsGroup(bool transforms)
    {
        Transforms = transforms;
        Name = transforms ? "Transform" : "Group";
    }

    /// <summary>Whether this group carries a matrix — that is, whether it is an hgtransform.</summary>
    public bool Transforms { get; }

    /// <summary>The members, in the order they joined.</summary>
    public IReadOnlyList<PlotObject> Members => _members;

    /// <summary>The transform, as a 4-by-4 matrix in row-major order; the identity until one is set.</summary>
    public double[,] Matrix { get; private set; } = Identity();

    public static double[,] Identity()
    {
        var m = new double[4, 4];
        for (int i = 0; i < 4; i++)
        {
            m[i, i] = 1;
        }

        return m;
    }

    /// <summary>Takes <paramref name="member"/> into the group, remembering where it started.</summary>
    public void Adopt(PlotObject member)
    {
        if (_original.ContainsKey(member))
        {
            return;
        }

        _members.Add(member);
        _original[member] = ReadCoordinates(member);
        Apply(member);
    }

    /// <summary>Sets the transform and moves every member to where it says.</summary>
    public void SetMatrix(double[,] matrix)
    {
        Matrix = matrix;
        foreach (PlotObject member in _members)
        {
            Apply(member);
        }
    }

    /// <summary>Shows or hides every member together.</summary>
    public void ShowMembers(bool visible)
    {
        Visible = visible;
        foreach (PlotObject member in _members)
        {
            member.Visible = visible;
        }
    }

    /// <summary>Moves one member from where it started to where the matrix puts it.</summary>
    private void Apply(PlotObject member)
    {
        if (!Transforms || !_original.TryGetValue(member, out (double[] X, double[] Y, double[] Z) start))
        {
            return;
        }

        var x = new double[start.X.Length];
        var y = new double[start.X.Length];
        var z = new double[start.X.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double sx = start.X[i];
            double sy = start.Y.Length > i ? start.Y[i] : 0;
            double sz = start.Z.Length > i ? start.Z[i] : 0;

            // The fourth row is a perspective term MATLAB's own makehgtform never produces, so a
            // matrix that carries one is honoured rather than assumed away.
            double w = (Matrix[3, 0] * sx) + (Matrix[3, 1] * sy) + (Matrix[3, 2] * sz) + Matrix[3, 3];
            w = w == 0 ? 1 : w;
            x[i] = ((Matrix[0, 0] * sx) + (Matrix[0, 1] * sy) + (Matrix[0, 2] * sz) + Matrix[0, 3]) / w;
            y[i] = ((Matrix[1, 0] * sx) + (Matrix[1, 1] * sy) + (Matrix[1, 2] * sz) + Matrix[1, 3]) / w;
            z[i] = ((Matrix[2, 0] * sx) + (Matrix[2, 1] * sy) + (Matrix[2, 2] * sz) + Matrix[2, 3]) / w;
        }

        WriteCoordinates(member, x, y, z);
    }

    private static (double[] X, double[] Y, double[] Z) ReadCoordinates(PlotObject member)
    {
        switch (member)
        {
            case Line3DPlot spatial:
                return ([.. spatial.X], [.. spatial.Y], [.. spatial.Z]);
            case Scatter3DPlot cloud:
                return ([.. cloud.X], [.. cloud.Y], [.. cloud.Z]);
            case PatchPlot patch:
                return ([.. patch.X], [.. patch.Y], [.. patch.Z]);
            case XYPlot flat:
                var x = new double[flat.Data.Count];
                var y = new double[flat.Data.Count];
                for (int i = 0; i < x.Length; i++)
                {
                    x[i] = flat.Data.GetX(i);
                    y[i] = flat.Data.GetY(i);
                }

                return (x, y, new double[x.Length]);
            default:
                return ([], [], []);
        }
    }

    private static void WriteCoordinates(PlotObject member, double[] x, double[] y, double[] z)
    {
        switch (member)
        {
            case Line3DPlot spatial:
                spatial.SetData(x, y, z);
                break;
            case Scatter3DPlot cloud:
                cloud.SetData(x, y, z);
                break;
            case PatchPlot patch:
                patch.SetData(x, y, z, [.. patch.Faces]);
                break;
            case XYPlot flat:
                flat.SetData(x, y);
                break;
        }
    }
}
