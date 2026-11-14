using System.ComponentModel;

namespace JGraph.Core.Model;

/// <summary>
/// A named collection of drawn objects a script can act on as one: MATLAB's <c>hggroup</c>, and with
/// a matrix, its <c>hgtransform</c>.
/// <para>
/// The members stay in their axes' <see cref="AxesModel.Plots"/>, <see cref="AxesModel.Annotations"/>
/// and <see cref="AxesModel.Lights"/>, which is what draws them and autoscales the axes. The group is
/// kept in its axes' <see cref="AxesModel.Groups"/>, so the renderer, the autoscaler and the document
/// see it too (open item 296, ADR 0360): a group's <see cref="GraphObject.Visible"/> is its own, and a
/// member is drawn only while every group around it is visible; a transform's <see cref="Matrix"/> is
/// applied when the members are drawn and fitted, so a member keeps its own coordinates, as R2025b's
/// does.
/// </para>
/// <para>
/// In the tree a <b>script</b> sees it is a child like any other (open items 144 and 166, ADR 0342):
/// <c>Children</c> of its axes lists it and not its members, <c>findobj</c> and <c>findall</c> reach
/// it and through it the members, a replacing plot or <c>cla</c> deletes it, and <c>delete</c> takes
/// its members with it.
/// </para>
/// </summary>
public sealed class GraphicsGroupModel : GraphObject
{
    private GraphObject[] _members = [];
    private double[,] _matrix = Identity();

    /// <summary>
    /// Makes a group in <paramref name="home"/>, or in <paramref name="outer"/>. With
    /// <paramref name="joinOuter"/> the group becomes the outer group's newest member at once; a
    /// document being read puts the members back in their own order instead.
    /// </summary>
    public GraphicsGroupModel(bool transforms, AxesModel? home = null, GraphicsGroupModel? outer = null, bool joinOuter = true)
    {
        Transforms = transforms;
        Home = home;
        Outer = outer;
        Seat = CreationOrder;
        Name = transforms ? "Transform" : "Group";
        if (joinOuter)
        {
            outer?.Admit(this); // a child of the group it was made in (open item 137)
        }
    }

    /// <summary>The group this one was made in, when it was made in a group rather than an axes.</summary>
    [Browsable(false)]
    public GraphicsGroupModel? Outer { get; }

    /// <summary>Whether this group carries a matrix — that is, whether it is an hgtransform.</summary>
    public bool Transforms { get; }

    /// <summary>
    /// The axes this group was made in, or its outer group's: the axes whose children list it (or its
    /// outermost group), and whose replacing plot or <c>cla</c> deletes it (open item 166).
    /// </summary>
    [Browsable(false)]
    public AxesModel? Home { get; }

    /// <summary>
    /// Where the group stands among its axes' drawn objects: before the first one made after this
    /// number. It is the group's own <see cref="GraphObject.CreationOrder"/> until a document read back
    /// gives it the place it was saved in.
    /// </summary>
    [Browsable(false)]
    public long Seat { get; set; }

    /// <summary>
    /// R2025b's <c>Annotation</c>, whose <c>LegendInformation.IconDisplayStyle</c> a group starts
    /// with <c>'off'</c> (open item 95, probe <c>probe_95</c>).
    /// </summary>
    [Browsable(false)]
    public PlotAnnotationModel Annotation
    {
        get
        {
            if (_annotation is null)
            {
                _annotation = new PlotAnnotationModel();
                _annotation.LegendInformation.IconDisplayStyle = LegendIconDisplay.Off;
            }

            return _annotation;
        }
    }

    private PlotAnnotationModel? _annotation;

    /// <summary>The legend's words for the group (R2025b's <c>DisplayName</c>), empty until given.</summary>
    [Browsable(false)]
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value ?? string.Empty, InvalidationKind.Layout);
    }

    private string _displayName = string.Empty;

    /// <summary>
    /// The members, in the order they joined: drawn objects, text and lights, and the groups made in
    /// this one (open item 137). A snapshot: a member joining or leaving replaces the list, so a frame
    /// being drawn reads one list from start to end.
    /// </summary>
    [Browsable(false)]
    public IReadOnlyList<GraphObject> Members => _members;

    /// <summary>
    /// The transform, as a 4-by-4 matrix in row-major order; the identity until one is set. The array
    /// is never changed in place, so a caller may keep the one it read.
    /// </summary>
    public double[,] Matrix => _matrix;

    /// <summary>Whether <see cref="Matrix"/> moves anything.</summary>
    [Browsable(false)]
    public bool MovesMembers => Transforms && !IsIdentity(_matrix);

    public static double[,] Identity()
    {
        var m = new double[4, 4];
        for (int i = 0; i < 4; i++)
        {
            m[i, i] = 1;
        }

        return m;
    }

    /// <summary>Whether <paramref name="matrix"/> is the 4-by-4 identity.</summary>
    public static bool IsIdentity(double[,] matrix)
    {
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                if (matrix[r, c] != (r == c ? 1 : 0))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Takes <paramref name="member"/> into the group, as its newest member.</summary>
    public void Admit(GraphObject member)
    {
        if (Array.IndexOf(_members, member) >= 0)
        {
            return;
        }

        _members = [.. _members, member];
        Changed();
    }

    /// <summary>
    /// Lets <paramref name="member"/> go when it is given another parent; it keeps its own data, which
    /// the matrix never changed.
    /// </summary>
    public void Release(GraphObject member)
    {
        int at = Array.IndexOf(_members, member);
        if (at < 0)
        {
            return;
        }

        _members = [.. _members[..at], .. _members[(at + 1)..]];
        Changed();
    }

    /// <summary>Sets the transform; the members are drawn where it says from the next frame.</summary>
    public void SetMatrix(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.GetLength(0) != 4 || matrix.GetLength(1) != 4)
        {
            throw new ArgumentException("A transform is a 4-by-4 matrix.", nameof(matrix));
        }

        _matrix = (double[,])matrix.Clone();
        OnPropertyChanged(nameof(Matrix));
        Changed();
    }

    /// <summary>A change to what the group draws: the axes refits and redraws.</summary>
    private void Changed()
    {
        Invalidate(InvalidationKind.Render);
        if (Parent is null)
        {
            Home?.Invalidate(InvalidationKind.Render);
        }
    }

    /// <summary>
    /// <c>outer * inner</c> of two 4-by-4 transforms: a point moved by <paramref name="inner"/> and then
    /// by <paramref name="outer"/>.
    /// </summary>
    public static double[,] Compose(double[,] outer, double[,] inner)
    {
        var m = new double[4, 4];
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += outer[r, k] * inner[k, c];
                }

                m[r, c] = sum;
            }
        }

        return m;
    }

    /// <summary>
    /// Where <paramref name="matrix"/> moves the point (<paramref name="x"/>, <paramref name="y"/>,
    /// <paramref name="z"/>). The fourth row is a perspective term MATLAB's own <c>makehgtform</c>
    /// never produces, so a matrix that carries one is honoured rather than assumed away.
    /// </summary>
    public static (double X, double Y, double Z) Apply(double[,] matrix, double x, double y, double z)
    {
        double w = (matrix[3, 0] * x) + (matrix[3, 1] * y) + (matrix[3, 2] * z) + matrix[3, 3];
        w = w == 0 ? 1 : w;
        return (
            ((matrix[0, 0] * x) + (matrix[0, 1] * y) + (matrix[0, 2] * z) + matrix[0, 3]) / w,
            ((matrix[1, 0] * x) + (matrix[1, 1] * y) + (matrix[1, 2] * z) + matrix[1, 3]) / w,
            ((matrix[2, 0] * x) + (matrix[2, 1] * y) + (matrix[2, 2] * z) + matrix[2, 3]) / w);
    }
}
