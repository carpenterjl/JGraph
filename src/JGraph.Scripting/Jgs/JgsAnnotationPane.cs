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

    private JgsAnnotationPane(FigureModel figure, GraphObject? owner = null)
    {
        Figure = figure;
        Owner = owner ?? figure;
        Tag = "scribeOverlay"; // R2025b's (pa_pane)
    }

    /// <summary>The figure whose pane this is, or the figure of the panel or tab whose pane it is.</summary>
    public FigureModel Figure { get; }

    /// <summary>
    /// What the pane belongs to: its figure, or a panel or tab holding an axes, which R2025b gives a pane
    /// of its own (open item 343, ADR 0376; u2_tree). A container's pane holds nothing here.
    /// </summary>
    public GraphObject Owner { get; }

    /// <summary>Whether this is a panel's or a tab's pane rather than its figure's.</summary>
    public bool InContainer => Owner is not FigureModel;

    /// <summary>The figure's pane, made the first time it is reached.</summary>
    public static JgsAnnotationPane For(FigureModel figure) => Panes.GetValue(figure, static f => new JgsAnnotationPane(f));

    /// <summary>The figure's pane if it has one, else null.</summary>
    public static JgsAnnotationPane? Existing(FigureModel figure) => Panes.TryGetValue(figure, out JgsAnnotationPane? pane) ? pane : null;

    /// <summary>
    /// The pane a figure lists among its children: its pane once it has annotations or once it
    /// has had a pane; null for a figure that never held one.
    /// </summary>
    public static JgsAnnotationPane? Listed(FigureModel figure)
    {
        // In a MATLAB-dialect script a figure has its pane from its first axes on, as R2025b's has
        // (open item 326, ADR 0372; probe p6b), and keeps one after the axes go and after clf reset.
        // A pane a script deleted is made again by the next axes, not by the ones it stood beside (open
        // item 343, ADR 0376; p7fc's pane_deleted_reset).
        long deletedAt = DeletedAt.TryGetValue(figure, out StrongBox<long>? stamp) ? stamp.Value : 0;
        if (JgsRunningDialect.ThreadIsMatlab && !HadAxes.TryGetValue(figure, out _)
            && figure.TopLevelAxes().Any(a => a.CreationOrder > deletedAt))
        {
            HadAxes.AddOrUpdate(figure, figure);
        }

        if (figure.Annotations.Count > 0)
        {
            return For(figure);
        }

        if (JgsRunningDialect.ThreadIsMatlab && HadAxes.TryGetValue(figure, out _))
        {
            // A pane first made here, with no annotation to make it, came with the figure's axes.
            if (Existing(figure) is null)
            {
                AxesMade.AddOrUpdate(figure, figure);
            }

            return For(figure);
        }

        return Existing(figure);
    }

    /// <summary>The figures a MATLAB-dialect listing found holding axes (open item 326).</summary>
    private static readonly ConditionalWeakTable<FigureModel, FigureModel> HadAxes = new();

    /// <summary>
    /// Whether the figure's pane came with its axes rather than with an annotation: R2025b's
    /// <c>clf reset</c> lists such a pane again at once and takes one an annotation made (open item 343,
    /// ADR 0376; probe p7fc).
    /// </summary>
    public static bool CameWithAxes(FigureModel figure) => AxesMade.TryGetValue(figure, out _) && Existing(figure) is not null;

    /// <summary>The figures whose pane was made with their axes, not by an annotation (ADR 0376).</summary>
    private static readonly ConditionalWeakTable<FigureModel, FigureModel> AxesMade = new();

    private static readonly ConditionalWeakTable<UiContainerModel, JgsAnnotationPane> ContainerPanes = new();

    /// <summary>
    /// The pane a panel or a tab of a classic figure lists in a MATLAB-dialect script: R2025b's, made
    /// with the first axes placed in it and kept after the axes go (open item 343, ADR 0376; probe
    /// p7fc). Null for a container that never held one, and outside the MATLAB dialect.
    /// </summary>
    public static JgsAnnotationPane? ListedIn(UiContainerModel container)
    {
        if (!JgsRunningDialect.ThreadIsMatlab || container.Figure is not { IsUiFigure: false } figure)
        {
            return null;
        }

        if (ContainerPanes.TryGetValue(container, out JgsAnnotationPane? pane))
        {
            return pane;
        }

        return container.ContainedAxes().Any()
            ? ContainerPanes.GetValue(container, c => new JgsAnnotationPane(figure, c))
            : null;
    }

    /// <summary>A container's pane if it has one, else null.</summary>
    public static JgsAnnotationPane? ExistingIn(UiContainerModel container) =>
        ContainerPanes.TryGetValue(container, out JgsAnnotationPane? pane) ? pane : null;

    /// <summary>Lets go of a container's pane a script deleted; its next axes makes a new one.</summary>
    public static void ForgetDeleted(UiContainerModel container) => ContainerPanes.Remove(container);

    /// <summary>Lets the figure's pane go, so that its next annotation makes a new one.</summary>
    public static void Forget(FigureModel figure)
    {
        Panes.Remove(figure);
        AxesMade.Remove(figure);
    }

    /// <summary>
    /// Lets go of a pane a script deleted: R2025b makes no new one until the next annotation or axes
    /// (u8_bars: <c>delete(allchild(f))</c> leaves no pane), where <c>clf reset</c>'s is made again
    /// (open item 326, ADR 0372).
    /// </summary>
    public static void ForgetDeleted(FigureModel figure)
    {
        Panes.Remove(figure);
        HadAxes.Remove(figure);
        AxesMade.Remove(figure);
        DeletedAt.AddOrUpdate(figure, new StrongBox<long>(NextOrder()));
    }

    /// <summary>When each figure's pane was last deleted, on the creation clock (ADR 0376).</summary>
    private static readonly ConditionalWeakTable<FigureModel, StrongBox<long>> DeletedAt = new();
}
