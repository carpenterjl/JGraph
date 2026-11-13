using System.Runtime.CompilerServices;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The pane a figure's annotations sit in, R2025b's <c>matlab.graphics.shape.internal.AnnotationPane</c>
/// (open item 143, probes <c>pa_groups</c> and <c>pa_pane</c>). Its handle is hidden, so
/// <c>findobj</c> and a figure's <c>Children</c> pass the annotations by and <c>findall</c> reaches
/// them; an annotation's <c>Parent</c> is the pane and the pane's is the figure.
/// <para>
/// It exists for the MATLAB dialect only, and only as the stable handle a script holds: the
/// annotations stay in <see cref="FigureModel.Annotations"/>, which the renderer and the document
/// read. A figure has one from its first annotation on (R2025b makes it then), it outlives the
/// annotations a script deletes and a plain <c>clf</c>, and <c>delete</c> of it or <c>clf reset</c>
/// takes it and the annotations with it, the next annotation making a new one.
/// </para>
/// </summary>
internal sealed class JgsAnnotationPane : GraphObject
{
    private static readonly ConditionalWeakTable<FigureModel, JgsAnnotationPane> Panes = new();

    private JgsAnnotationPane(FigureModel figure)
    {
        Figure = figure;
        Tag = "scribeOverlay"; // R2025b's (pa_pane)
    }

    /// <summary>The figure whose pane this is.</summary>
    public FigureModel Figure { get; }

    /// <summary>The figure's pane, made the first time it is reached.</summary>
    public static JgsAnnotationPane For(FigureModel figure) => Panes.GetValue(figure, static f => new JgsAnnotationPane(f));

    /// <summary>The figure's pane if it has one, else null.</summary>
    public static JgsAnnotationPane? Existing(FigureModel figure) => Panes.TryGetValue(figure, out JgsAnnotationPane? pane) ? pane : null;

    /// <summary>
    /// The pane a figure lists among its children: its pane once it has annotations or once it
    /// has had a pane; null for a figure that never held one.
    /// </summary>
    public static JgsAnnotationPane? Listed(FigureModel figure) =>
        figure.Annotations.Count > 0 ? For(figure) : Existing(figure);

    /// <summary>Lets the figure's pane go, so that its next annotation makes a new one.</summary>
    public static void Forget(FigureModel figure) => Panes.Remove(figure);
}
