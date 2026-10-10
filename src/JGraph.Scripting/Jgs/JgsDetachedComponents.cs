using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Where a component made with <c>'Parent', []</c> waits for a parent (open item 48). R2025b makes it
/// belong to nothing — its <c>Parent</c> an empty placeholder — until a write to <c>Parent</c> places
/// it, which App Designer's generated code and some custom components do. Here it sits in a panel
/// that no figure holds and nothing draws, so every reading of a component still finds a container
/// above it; its <c>Parent</c> reads <c>[]</c> (this build's placeholders are numbers, as
/// <c>gobjects</c>' are), and a <c>Parent</c> write moves it out as it moves any component.
/// </summary>
internal static class JgsDetachedComponents
{
    /// <summary>The panel detached components wait in; a new run starts with an empty one.</summary>
    public static UiPanelModel Holder { get; private set; } = new();

    /// <summary>
    /// The figure a detached <c>uiaxes</c> waits in, as an axes has to be some figure's: one no
    /// figure number names, which nothing shows.
    /// </summary>
    public static FigureModel HolderFigure { get; private set; } = NewHolderFigure();

    /// <summary>Whether <paramref name="target"/> is where detached objects wait.</summary>
    public static bool IsHolder(GraphObject? target) =>
        target is not null && (ReferenceEquals(target, Holder) || ReferenceEquals(target, HolderFigure));

    /// <summary>Whether a <c>Parent</c> given to a maker is the empty one that means none.</summary>
    public static bool MeansNoParent(JgsValue value) =>
        value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray && !value.IsCharMatrix;

    /// <summary>Forgets every component still waiting (a new run's).</summary>
    public static void Clear()
    {
        Holder = new UiPanelModel();
        HolderFigure = NewHolderFigure();
    }

    private static FigureModel NewHolderFigure() => new() { IsUiFigure = true, IntegerHandle = false };
}
