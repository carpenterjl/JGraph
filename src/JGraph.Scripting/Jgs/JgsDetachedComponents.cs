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

    /// <summary>
    /// The toolbar a detached push or toggle tool waits in (open item 94), made when first needed in
    /// <see cref="HolderFigure"/>, whose menus, context menus and toolbars are the detached ones.
    /// </summary>
    public static UiToolbarModel HolderToolbar
    {
        get
        {
            if (_holderToolbar is null || !ReferenceEquals(_holderToolbar.Parent, HolderFigure))
            {
                _holderToolbar = new UiToolbarModel();
                using (GraphObjectLifecycle.SuppressNotifications())
                {
                    HolderFigure.Toolbars.Add(_holderToolbar);
                }
            }

            return _holderToolbar;
        }
    }

    private static UiToolbarModel? _holderToolbar;

    /// <summary>Whether <paramref name="target"/> is where detached objects wait.</summary>
    public static bool IsHolder(GraphObject? target) =>
        target is not null && (ReferenceEquals(target, Holder) || ReferenceEquals(target, HolderFigure)
            || ReferenceEquals(target, _holderToolbar));

    /// <summary>
    /// The holder a maker that <paramref name="accepts"/> some parents makes a parentless object in:
    /// the panel for a component, the figure for a menu, a context menu or a toolbar, the toolbar for
    /// a tool; null for a maker none of them suits (a tab, whose holder would be a tab group).
    /// </summary>
    public static GraphObject? HolderAccepted(Func<GraphObject, bool> accepts) =>
        accepts(Holder) ? Holder
        : accepts(HolderFigure) ? HolderFigure
        : accepts(HolderToolbar) ? HolderToolbar
        : null;

    /// <summary>Whether a <c>Parent</c> given to a maker is the empty one that means none.</summary>
    public static bool MeansNoParent(JgsValue value) =>
        value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray && !value.IsCharMatrix;

    /// <summary>Forgets every component still waiting (a new run's).</summary>
    public static void Clear()
    {
        Holder = new UiPanelModel();
        HolderFigure = NewHolderFigure();
        _holderToolbar = null;
    }

    private static FigureModel NewHolderFigure() => new() { IsUiFigure = true, IntegerHandle = false };
}
