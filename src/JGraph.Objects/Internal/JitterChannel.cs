using JGraph.Core.Drawing;
using JGraph.Maths;

namespace JGraph.Objects.Internal;

/// <summary>
/// One axis' worth of jitter on a marker chart: which spread it uses and how wide it is allowed to
/// be. Two charts carry these (the flat scatter and the one in space, three of them there), and the
/// rules for reading an unset width and for asking <see cref="Swarm"/> for the offsets are the same
/// on both, so they live here rather than twice over.
/// </summary>
internal sealed class JitterChannel
{
    private JitterStyle _style;
    private double _width;

    /// <summary>Which spread this axis uses, <see cref="JitterStyle.None"/> for none.</summary>
    public JitterStyle Style
    {
        get => _style;
        set => _style = value;
    }

    /// <summary>Whether this axis is spreading its points at all.</summary>
    public bool Spreads => _style != JitterStyle.None;

    /// <summary>
    /// The width that was set, or zero for none. Writing zero or less puts the width back to being
    /// worked out from the data, which is what MATLAB's <c>'auto'</c> means and the only way back.
    /// </summary>
    public double Width
    {
        get => _width;
        set => _width = double.IsFinite(value) && value > 0 ? value : 0;
    }

    /// <summary>The width in force: the one that was set, or the one the readings imply.</summary>
    public double WidthFor(IReadOnlyList<double> positions) =>
        _width > 0 ? _width : Swarm.AutomaticWidth(positions);

    /// <summary>The offset to draw each point at, or an all-zero run when this axis is not spreading.</summary>
    public double[] Offsets(IReadOnlyList<double> positions, IReadOnlyList<double> crowded) =>
        Spreads
            ? Swarm.Offsets(positions, crowded, _style, WidthFor(positions))
            : new double[positions.Count];
}
