using JGraph.Core.Drawing;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// The settings <c>exportsetupdlg</c> edits and the picture verbs fall back on (M84).
/// </summary>
/// <remarks>
/// <para>
/// MATLAB's export setup is a named style a figure carries and the export verbs read. This is the
/// same idea with one style rather than a library of them: a figure holds at most one preset, and it
/// is consulted <em>only where the caller did not say otherwise</em>. A preset that overrode an
/// explicit <c>'Resolution'</c> would be action at a distance — a script's own argument losing to a
/// dialog someone opened once — and that is the failure mode worth designing against, because it is
/// invisible in the script that suffers it.
/// </para>
/// <para>
/// Every field is nullable and null means "nothing to say", so a figure whose preset has never been
/// opened exports exactly as it did before this existed.
/// </para>
/// </remarks>
public sealed class FigureExportPreset
{
    /// <summary>Dots per inch to render at, or null for the verb's own default.</summary>
    public double? Resolution { get; set; }

    /// <summary>The size to draw at in device-independent units, or null for the figure's own.</summary>
    public Size2D? Size { get; set; }

    /// <summary>The colour to paint behind the figure, or null to keep the figure's own.</summary>
    public Color? Background { get; set; }

    /// <summary>Whether this preset has anything to say at all.</summary>
    public bool IsEmpty => Resolution is null && Size is null && Background is null;

    /// <summary>A copy, so a saved document and a live figure never share one.</summary>
    public FigureExportPreset Clone() =>
        new() { Resolution = Resolution, Size = Size, Background = Background };
}
