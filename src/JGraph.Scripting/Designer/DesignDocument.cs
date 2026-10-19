using System.Text.RegularExpressions;
using JGraph.Core.Primitives;
using JGraph.Scripting.Jgs;
using JGraph.Scripting.Workspace;

namespace JGraph.Scripting.Designer;

/// <summary>
/// One app open in the visual designer (designer plan DS2): its design, the runtime's components
/// made from it for the canvas, what is selected, and the edits that can be undone. UI-free, so that
/// every rule the canvas follows is tested without a window.
/// </summary>
/// <remarks>
/// <para>
/// An edit changes the design and the runtime's component together, through the component's own
/// set; what the component keeps is read back into the design. Each edit is one step to undo, as App
/// Designer keeps them: up to <see cref="DesignGeometry.UndoDepth"/>, and a new edit clears what
/// could be redone.
/// </para>
/// <para>
/// A gesture - a drag, a resize, a run of arrow keys - shows on the runtime's components as it goes
/// (<see cref="Preview"/>) and becomes one edit when it ends (<see cref="EndGesture(bool, DesignPlacement?)"/>).
/// </para>
/// </remarks>
public sealed partial class DesignDocument : IDisposable
{
    private readonly List<Edit[]> _undo = [];
    private readonly List<Edit[]> _redo = [];
    private IReadOnlyList<DesignComponent> _selection = [];
    private IReadOnlyList<DesignBox>? _boxes;
    private Gesture? _gesture;
    private int _version;
    private int _savedVersion;

    private DesignDocument(string text, string? path, DesignReadResult read)
    {
        Text = text;
        Path = path;
        Style = read.Style;
        Design = read.Design!;
        ReadProblems = read.Problems;
        Runtime = DesignRuntime.Build(Design, path is null ? null : System.IO.Path.GetDirectoryName(path), Style.Theme);
        LayOutRegions();
    }

    /// <summary>Raised after the design, the selection or the saved state changed: the canvas draws again.</summary>
    public event EventHandler? Changed;

    /// <summary>The app's class, as it was read or last saved.</summary>
    public string Text { get; private set; }

    /// <summary>The <c>.mlapp</c> the app is in; null for one that is not saved.</summary>
    public string? Path { get; }

    /// <summary>How the app's code is laid out, which the save keeps, and the theme it is written under (the figure's).</summary>
    public DesignCodeStyle Style { get; private set; }

    /// <summary>The theme the app's code is written under: its figure's <c>Theme</c>, else light.</summary>
    public DesignTheme Theme => Style.Theme;

    /// <summary>The component library the app's components are added from.</summary>
    public DesignPalette Palette { get; } = DesignPalette.R2025b;

    /// <summary>The design.</summary>
    public AppDesign Design { get; }

    /// <summary>The runtime's components, which the canvas draws.</summary>
    public DesignRuntime Runtime { get; }

    /// <summary>
    /// The lines of <c>createComponents</c> the reader could not place. A design read with any is
    /// missing what they do, so it is shown but not saved: a save would lose them.
    /// </summary>
    public IReadOnlyList<string> ReadProblems { get; }

    /// <summary>Whether the design can be saved: it holds everything its code does.</summary>
    public bool CanSave => ReadProblems.Count == 0 && Path is not null;

    /// <summary>Whether there are edits not saved.</summary>
    public bool IsDirty => _version != _savedVersion;

    /// <summary>The selected components, all in one container; the figure is selected alone.</summary>
    public IReadOnlyList<DesignComponent> Selection => _selection;

    /// <summary>Whether there is an edit to undo.</summary>
    public bool CanUndo => _undo.Count > 0 && _gesture is null && !_regionPreview;

    /// <summary>Whether there is an edit to redo.</summary>
    public bool CanRedo => _redo.Count > 0 && _gesture is null && !_regionPreview;

    /// <summary>Opens the App Designer file <paramref name="path"/>.</summary>
    /// <exception cref="System.IO.InvalidDataException">The file is not an app App Designer lays out.</exception>
    public static DesignDocument Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return FromText(JgsMlapp.ReadCode(path), path);
    }

    /// <summary>Opens an app's class text; <paramref name="path"/> is the <c>.mlapp</c> it saves into, if any.</summary>
    /// <exception cref="System.IO.InvalidDataException">The text is not an app App Designer lays out.</exception>
    public static DesignDocument FromText(string text, string? path)
    {
        ArgumentNullException.ThrowIfNull(text);
        DesignReadResult read = DesignCodeReader.Read(text);
        if (read.Design is null)
        {
            throw new System.IO.InvalidDataException(
                $"The app's components cannot be read: {string.Join(" ", read.Problems)}");
        }

        return new DesignDocument(text, path, read);
    }

    /// <summary>The boxes the canvas shows, back to front; worked out again after every change.</summary>
    public IReadOnlyList<DesignBox> Boxes => _boxes ??= Runtime.Boxes();

    /// <summary>The box of <paramref name="component"/>, or null when the canvas does not show it.</summary>
    public DesignBox? BoxOf(DesignComponent component)
    {
        foreach (DesignBox box in Boxes)
        {
            if (ReferenceEquals(box.Component, component))
            {
                return box;
            }
        }

        return null;
    }

    /// <summary>The front component under <paramref name="point"/> on the surface, or the figure when there is none.</summary>
    public DesignComponent ComponentAt(Point2D point)
    {
        IReadOnlyList<DesignBox> boxes = Boxes;
        for (int i = boxes.Count - 1; i >= 0; i--)
        {
            // A grid's child is hit anywhere in its cells, App Designer's wrapper of it (DS4).
            // A responsive app's grid is never hit: its regions cover it, and App Designer keeps it from the user (DS4b).
            DesignBox box = boxes[i];
            Rect2D hit = box.Component.IsInGrid ? CellBoxOf(box.Component) ?? box.Box : box.Box;
            if (box.Shown && hit.Contains(point) && box.Clip.Contains(point) && !IsResponsiveGrid(box.Component))
            {
                return box.Component;
            }
        }

        return Design.Figure;
    }

    /// <summary>
    /// Shows <paramref name="tab"/> in its group on the canvas, as a click on its heading does; the
    /// boxes - and the cells of grids in the tabs - are worked out again.
    /// </summary>
    public void ShowTab(DesignComponent tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        Runtime.ShowTab(tab);
        _boxes = null;
        OnChanged();
    }

    /// <summary>Selects <paramref name="components"/>; an empty selection selects the figure, as App Designer's does.</summary>
    public void Select(IReadOnlyList<DesignComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        IReadOnlyList<DesignComponent> selection = components.Count == 0 ? [Design.Figure] : components.ToArray();
        if (!selection.SequenceEqual(_selection, ReferenceEqualityComparer.Instance))
        {
            _selection = selection;
            OnChanged();
        }
    }

    /// <summary>
    /// Whether <paramref name="component"/> is placed by its <c>Position</c>, so that the canvas can
    /// move and resize it by pixels: not the figure, a tab, a grid, or a grid's child, which a drag
    /// moves to other cells and a handle spans over more (DS4, <see cref="CanDrag"/>).
    /// </summary>
    public bool IsMovable(DesignComponent component) =>
        component.Parent is not null && !component.IsInGrid && component.Type != "matlab.ui.container.Tab"
        && Runtime.PositionOf(component) is not null;

    /// <summary>
    /// The containers a component can be dropped into: the figure, a panel, a button group, a tab -
    /// any container that places its children by their positions. A radio button and a toggle button
    /// belong in a button group only.
    /// </summary>
    public bool CanHold(DesignComponent container, DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(component);
        for (DesignComponent? up = container; up is not null; up = up.Parent)
        {
            if (ReferenceEquals(up, component))
            {
                return false;
            }
        }

        bool needsGroup = component.Type is "matlab.ui.control.RadioButton" or "matlab.ui.control.ToggleButton";
        return container.Type switch
        {
            "matlab.ui.container.ButtonGroup" => true,
            "matlab.ui.Figure" or "matlab.ui.container.Panel" or "matlab.ui.container.Tab" => !needsGroup,
            _ => false,
        };
    }

    /// <summary>
    /// The container a box dropped where it is would go into: the front one whose inner area holds
    /// the box's centre and that can hold every one of <paramref name="moving"/>; else their own.
    /// </summary>
    public DesignComponent DropTarget(IReadOnlyList<DesignComponent> moving, Rect2D box)
    {
        ArgumentNullException.ThrowIfNull(moving);
        var centre = new Point2D(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        IReadOnlyList<DesignBox> boxes = Boxes;
        for (int i = boxes.Count - 1; i >= 0; i--)
        {
            DesignBox candidate = boxes[i];
            if (!candidate.Shown || moving.Any(m => IsWithin(candidate.Component, m))
                || !moving.All(m => CanHold(candidate.Component, m)))
            {
                continue;
            }

            Rect2D inner = Runtime.InnerOnSurface(candidate.Component);
            if (inner.Contains(centre) && candidate.Clip.Contains(centre))
            {
                return candidate.Component;
            }
        }

        return moving.All(m => CanHold(Design.Figure, m)) ? Design.Figure : moving[0].Parent!;
    }

    private static bool IsWithin(DesignComponent component, DesignComponent ancestor)
    {
        for (DesignComponent? up = component; up is not null; up = up.Parent)
        {
            if (ReferenceEquals(up, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What <paramref name="component"/>'s shape allows.</summary>
    public DesignShape ShapeOf(DesignComponent component) => Runtime.ShapeOf(component);

    // --- gestures --------------------------------------------------------------------------------

    /// <summary>
    /// Starts a gesture on <paramref name="components"/>: their boxes as they stand are what
    /// <see cref="Preview"/> moves them from, and what <see cref="EndGesture(bool, DesignPlacement?)"/> puts back when the
    /// gesture is cancelled.
    /// </summary>
    public void BeginGesture(IReadOnlyList<DesignComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        EndGesture(commit: false);
        var start = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance);
        foreach (DesignComponent component in components)
        {
            if (CanDrag(component) && BoxOf(component) is { } box)
            {
                // A grid's child moves by its cells' box, App Designer's wrapper of it (DS4).
                start[component] = CellBoxOf(component) ?? box.Box;
            }
        }

        _gesture = new Gesture(start);
    }

    /// <summary>The boxes the gesture started from.</summary>
    public IReadOnlyDictionary<DesignComponent, Rect2D> GestureStart =>
        _gesture?.Start ?? (IReadOnlyDictionary<DesignComponent, Rect2D>)new Dictionary<DesignComponent, Rect2D>();

    /// <summary>
    /// The boxes of the gesture's grid children where the drag has them (DS4): a grid places its
    /// children by their cells, so they stay where they are while the canvas draws these.
    /// </summary>
    public IReadOnlyDictionary<DesignComponent, Rect2D> GestureGhosts =>
        new Dictionary<DesignComponent, Rect2D>(
            _gesture is { } gesture ? gesture.Last.Where(p => IsGridChild(p.Key)) : [],
            ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Shows the gesture's components at <paramref name="boxes"/> (on the surface), on the runtime's
    /// components only: nothing is in the design until the gesture ends. A grid's child is not moved
    /// but drawn where it would be (<see cref="GestureGhosts"/>).
    /// </summary>
    public void Preview(IReadOnlyDictionary<DesignComponent, Rect2D> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        if (_gesture is null)
        {
            return;
        }

        foreach ((DesignComponent component, Rect2D box) in boxes)
        {
            if (_gesture.Start.ContainsKey(component) && component.Parent is { } parent)
            {
                if (!IsGridChild(component))
                {
                    Rect2D position = Runtime.ToPosition(parent, box);
                    Runtime.Set(component, "Position", Row(position));
                }

                _gesture.Last[component] = box;
            }
        }

        _boxes = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ends the gesture. Committed, its components' last boxes become one edit - each moved into
    /// <paramref name="parent"/> first when one is given; cancelled, they go back to where they were.
    /// </summary>
    public void EndGesture(bool commit, DesignComponent? parent = null) =>
        EndGesture(commit, parent is null ? null : new DesignPlacement(parent, CanDrop: true));

    /// <summary>
    /// Ends the gesture with what it carries dropped at <paramref name="placement"/> (DS4). Into a
    /// grid, each component takes the cells the placement gives it - and nothing changes when any of
    /// them is in its cells already, as App Designer's move does nothing then; out of a grid, each is
    /// placed where it was dropped at the size it had; a placement that refuses the drop changes
    /// nothing. Cancelled, everything goes back to where it was.
    /// </summary>
    public void EndGesture(bool commit, DesignPlacement? placement)
    {
        if (_gesture is not { } gesture)
        {
            return;
        }

        _gesture = null;

        // Put back first, so that an edit begins from where each component stood.
        var startPositions = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance);
        foreach ((DesignComponent component, Rect2D start) in gesture.Start)
        {
            if (!IsGridChild(component))
            {
                Rect2D startPosition = Runtime.ToPosition(component.Parent!, start);
                startPositions[component] = startPosition;
                Runtime.Set(component, "Position", Row(startPosition));
            }
        }

        List<Edit> edits = commit && placement is not { CanDrop: false } ? GestureEdits(gesture, placement, startPositions) : [];
        if (edits.Count > 0)
        {
            Do([.. edits]);
        }
        else
        {
            _boxes = null;
            OnChanged();
        }
    }

    private List<Edit> GestureEdits(Gesture gesture, DesignPlacement? placement, Dictionary<DesignComponent, Rect2D> startPositions)
    {
        var edits = new List<Edit>();
        DesignComponent[] moving = [.. gesture.Start.Keys];
        if (placement is { Container: { } grid, Spans: { } spans } && IsGrid(grid) && spans.Count == moving.Length)
        {
            // App Designer's GridLayoutCommandCreator: no move at all when one keeps its cells in the same grid.
            for (int i = 0; i < moving.Length; i++)
            {
                if (ReferenceEquals(moving[i].Parent, grid) && SpanOf(moving[i]) == spans[i])
                {
                    return [];
                }
            }

            for (int i = 0; i < moving.Length; i++)
            {
                edits.AddRange(IntoGrid(moving[i], grid, spans[i]));
            }

            return edits;
        }

        DesignComponent? target = placement?.Container;
        foreach (DesignComponent component in moving)
        {
            DesignComponent from = component.Parent!;
            if (!gesture.Last.TryGetValue(component, out Rect2D box))
            {
                continue;
            }

            DesignComponent into = target is not null && !ReferenceEquals(target, from) && !IsGrid(target) && CanHold(target, component) ? target : from;
            if (IsGridChild(component))
            {
                if (!ReferenceEquals(into, from))
                {
                    // Placed where its drawn self was dragged - a slider or gauge is drawn smaller than its cells.
                    Rect2D start = gesture.Start[component];
                    Rect2D drawn = BoxOf(component)?.Box ?? start;
                    edits.AddRange(OutOfGrid(component, into, new Rect2D(drawn.X + box.X - start.X, drawn.Y + box.Y - start.Y, drawn.Width, drawn.Height)));
                }

                continue;
            }

            Rect2D startPosition = startPositions[component];
            if (!ReferenceEquals(into, from))
            {
                edits.Add(new ParentEdit(component, from, IndexIn(from, component), into));
            }

            Rect2D position = Runtime.ToPosition(into, box);
            if (position != startPosition || !ReferenceEquals(into, from))
            {
                edits.Add(new PositionEdit(component, component.Get("Position"), Row(position), Row(startPosition)));
            }
        }

        return edits;
    }

    /// <summary>
    /// Moves <paramref name="components"/> by (<paramref name="dx"/>, <paramref name="dy"/>) on the
    /// surface, as one edit: the arrow keys' move, and any move that is not a drag.
    /// </summary>
    public void Move(IReadOnlyList<DesignComponent> components, double dx, double dy)
    {
        ArgumentNullException.ThrowIfNull(components);
        BeginGesture(components);
        var moved = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance);
        foreach ((DesignComponent component, Rect2D box) in GestureStart)
        {
            moved[component] = new Rect2D(box.X + dx, box.Y + dy, box.Width, box.Height);
        }

        Preview(moved);
        EndGesture(commit: true);
    }

    // --- undo ------------------------------------------------------------------------------------

    /// <summary>Undoes the last edit.</summary>
    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        Edit[] step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        for (int i = step.Length - 1; i >= 0; i--)
        {
            step[i].Undo(this);
        }

        _redo.Add(step);
        _version--;
        Settled(step);
    }

    /// <summary>Does again the last edit undone.</summary>
    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        Edit[] step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        foreach (Edit edit in step)
        {
            edit.Do(this);
        }

        _undo.Add(step);
        _version++;
        Settled(step);
    }

    private void Do(Edit[] step)
    {
        foreach (Edit edit in step)
        {
            edit.Do(this);
        }

        if (_undo.Count == DesignGeometry.UndoDepth)
        {
            _undo.RemoveAt(0);
            _savedVersion = int.MinValue; // the saved state can no longer be undone back to
        }

        _undo.Add(step);
        _redo.Clear();
        if (_version < _savedVersion)
        {
            _savedVersion = int.MinValue; // nor reached again, once the edits after it are gone
        }

        _version++;
        Settled(step);
    }

    /// <summary>
    /// After an edit: the components it touched and still in the design are selected (the figure when
    /// none is), the theme follows the figure's, and the canvas draws again.
    /// </summary>
    private void Settled(Edit[] step)
    {
        _boxes = null;
        DesignComponent[] touched = step.Where(static e => e.Selects).Select(static e => e.Component)
            .Where(c => IsInDesign(c)).Distinct(ReferenceEqualityComparer.Instance).Cast<DesignComponent>().ToArray();
        _selection = touched.Length > 0 ? touched : [Design.Figure];
        DesignTheme theme = DesignCodeStyle.ThemeOf(Design);
        if (theme != Style.Theme)
        {
            Style = Style with { Theme = theme };
            Runtime.SetTheme(theme);
        }

        OnChanged();
    }

    /// <summary>Whether <paramref name="component"/> is in the design: the figure, or inside it.</summary>
    private bool IsInDesign(DesignComponent component)
    {
        DesignComponent at = component;
        while (at.Parent is { } up)
        {
            at = up;
        }

        return ReferenceEquals(at, Design.Figure);
    }

    // --- adding and deleting (DS3) ----------------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="container"/> can hold a new component of <paramref name="type"/>: a
    /// container that places its children by their positions, a radio or toggle button only in a
    /// button group. Grids wait for the designer's grid editing (DS4).
    /// </summary>
    public static bool CanHoldNew(DesignComponent container, string type)
    {
        ArgumentNullException.ThrowIfNull(container);
        bool needsGroup = type is "matlab.ui.control.RadioButton" or "matlab.ui.control.ToggleButton";
        return container.Type switch
        {
            "matlab.ui.container.ButtonGroup" => true,
            "matlab.ui.Figure" or "matlab.ui.container.Panel" or "matlab.ui.container.Tab" => !needsGroup,
            _ => false,
        };
    }

    /// <summary>
    /// The container a new component of <paramref name="entry"/> dropped on <paramref name="target"/>
    /// goes into: the figure for the figure tools, else <paramref name="target"/> or the nearest
    /// container around it that can hold it.
    /// </summary>
    public DesignComponent ContainerFor(DesignPaletteEntry entry, DesignComponent target)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(target);
        if (entry.FigureOnly || entry.Type == "matlab.ui.container.Menu")
        {
            return Design.Figure;
        }

        for (DesignComponent? at = target; at is not null; at = at.Parent)
        {
            if (CanHoldNew(at, entry.Type))
            {
                return at;
            }
        }

        return Design.Figure;
    }

    /// <summary>
    /// Adds what <paramref name="entry"/> makes, as one edit (designer plan DS3): into the container
    /// <see cref="ContainerFor"/> gives for <paramref name="target"/>, the top-left corner of its box at
    /// <paramref name="topLeft"/> on the surface, in whole pixels, kept inside a container that is not
    /// the figure; a drawn <paramref name="size"/> is the component's own. Named and made as App
    /// Designer's palette names and makes them; what is added is selected.
    /// </summary>
    /// <returns>The components added to the container.</returns>
    /// <exception cref="InvalidOperationException">The entry is one the designer does not make yet.</exception>
    public IReadOnlyList<DesignComponent> Add(DesignPaletteEntry entry, DesignComponent target, Point2D topLeft, Size2D? size = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(target);
        EndGesture(commit: false);
        if (entry.Type == GridType)
        {
            // On a grid, a grid in the cells its avatar covers (GridifyCommand._addGridIntoGrid, DS4).
            DesignComponent? grid = IsGrid(target) ? target : IsGridChild(target) ? target.Parent : null;
            if (grid is not null && CellsOf(grid) is { } cells
                && DesignGridRules.SpanOfBounds(new Rect2D(topLeft.X, topLeft.Y, GridAvatar.Width, GridAvatar.Height), cells) is { } span)
            {
                return [AddGridInGrid(grid, span)];
            }

            // Elsewhere it lays out the container it is added to (GridifyCommand).
            DesignComponent container = target;
            while (!CanGridify(container) && container.Parent is { } up)
            {
                container = up;
            }

            return CanGridify(container) ? [Gridify(container)] : [];
        }

        DesignComponent parent = ContainerFor(entry, target);
        IReadOnlyList<(DesignComponent Component, int? Index)> made = Palette.Make(entry, Design, parent, new Point2D(0, 0), size);
        Place(parent, made.Select(static m => m.Component).ToArray(), topLeft);

        Edit[] step = made.Select(m => (Edit)new AddEdit(m.Component, parent, m.Index)).ToArray();
        Do(step);
        return made.Select(static m => m.Component).ToArray();
    }

    /// <summary>Moves new components, as one box, so that the box's top-left corner is at <paramref name="topLeft"/> on the surface.</summary>
    private void Place(DesignComponent parent, IReadOnlyList<DesignComponent> made, Point2D topLeft)
    {
        var placed = made.Select(c => (Component: c, Box: c.Get("Position") as NumericValue))
            .Where(static p => p.Box is { Data.Count: 4 })
            .ToArray();
        if (placed.Length == 0)
        {
            return;
        }

        double left = placed.Min(static p => p.Box!.Data[0]);
        double bottom = placed.Min(static p => p.Box!.Data[1]);
        double right = placed.Max(static p => p.Box!.Data[0] + p.Box!.Data[2]);
        double top = placed.Max(static p => p.Box!.Data[1] + p.Box!.Data[3]);

        // The corner in the container's pixels: from its inner area's bottom-left, which is pixel 1.
        Rect2D inner = Runtime.InnerOnSurface(parent);
        double wantLeft = System.Math.Round(topLeft.X - inner.X + 1, MidpointRounding.AwayFromZero);
        double wantTop = System.Math.Round(inner.Y + inner.Height - topLeft.Y + 1, MidpointRounding.AwayFromZero);
        double dx = wantLeft - left;
        double dy = wantTop - top;
        if (!ReferenceEquals(parent, Design.Figure))
        {
            // Kept inside the container, as App Designer clamps a drop to its parent.
            double width = right - left;
            double height = top - bottom;
            dx = System.Math.Max(1 - left, System.Math.Min(dx, inner.Width + 1 - width - left));
            dy = System.Math.Max(1 - bottom, System.Math.Min(dy, inner.Height + 1 - height - bottom));
        }

        foreach ((DesignComponent component, NumericValue? box) in placed)
        {
            component.Set("Position", NumericValue.Row(box!.Data[0] + dx, box.Data[1] + dy, box.Data[2], box.Data[3]));
        }
    }

    /// <summary>Whether <paramref name="component"/> can be deleted: anything but the figure.</summary>
    public bool CanDelete(DesignComponent component) =>
        component.Parent is not null && IsInDesign(component) && !IsRegion(component) && !IsResponsiveGrid(component);

    /// <summary>
    /// Deletes <paramref name="components"/> and everything inside them, as one edit; the figure is
    /// never deleted. A context menu deleted is taken off the components it was assigned to, and a
    /// deleted tree node out of its tree's checked nodes. Callbacks the components named stay in the
    /// app's code, which is the user's.
    /// </summary>
    public void Delete(IReadOnlyList<DesignComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        EndGesture(commit: false);
        List<Edit> edits = DeleteEdits(components);
        if (edits.Count > 0)
        {
            Do([.. edits]);
        }
    }

    /// <summary>The edits that delete <paramref name="components"/>: references to them taken off, then each deleted.</summary>
    private List<Edit> DeleteEdits(IReadOnlyList<DesignComponent> components)
    {
        DesignComponent[] deletable = components.Where(CanDelete).Distinct(ReferenceEqualityComparer.Instance).Cast<DesignComponent>().ToArray();
        DesignComponent[] gone = deletable.Where(c => !deletable.Any(other => !ReferenceEquals(other, c) && IsWithin(c, other))).ToArray();
        if (gone.Length == 0)
        {
            return [];
        }

        var names = new HashSet<string>(gone.SelectMany(static g => Walk(g)).Select(static c => c.CodeName), StringComparer.Ordinal);
        var edits = new List<Edit>();
        foreach (DesignComponent other in Design.Components)
        {
            if (gone.Any(g => IsWithin(other, g)))
            {
                continue;
            }

            foreach ((string name, DesignValue value) in other.Properties.ToArray())
            {
                if (value is ComponentsValue refs && refs.CodeNames.Any(names.Contains))
                {
                    string[] kept = refs.CodeNames.Where(n => !names.Contains(n)).ToArray();
                    DesignValue? after = kept.Length == 0 ? null : new ComponentsValue(kept, refs.Bracketed);
                    edits.Add(new PropertyEdit(other, name, value, after, RuntimeBefore: value, RuntimeAfter: null) { Selects = false });
                }
            }
        }

        foreach (DesignComponent component in gone)
        {
            DesignComponent parent = component.Parent!;
            edits.Add(new DeleteEdit(component, parent, IndexIn(parent, component)));
        }

        return edits;
    }

    private static IEnumerable<DesignComponent> Walk(DesignComponent component)
    {
        yield return component;
        foreach (DesignComponent child in component.Children)
        {
            foreach (DesignComponent c in Walk(child))
            {
                yield return c;
            }
        }
    }

    // --- properties (DS3) ------------------------------------------------------------------------

    /// <summary>
    /// What <paramref name="component"/>'s <paramref name="name"/> is, for the inspector: what its
    /// component holds, read back; else what the design holds, else the property's default.
    /// </summary>
    public DesignValue? ValueOf(DesignComponent component, string name)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(name);
        DesignValue? held = component.Get(name);
        DesignValue? fallback = held ?? component.Class?.Property(name)?.DefaultFor(Theme);
        return Runtime.Read(component, name, fallback) ?? fallback;
    }

    /// <summary>
    /// Sets <paramref name="name"/> on <paramref name="component"/> to <paramref name="value"/> - its
    /// default when null - as one edit (designer plan DS3). The value goes through the component's own
    /// set, and the design keeps what the component kept of it: read back, and unset when that is the
    /// property's default under the app's theme. What follows from it is kept too: the component's
    /// <c>Value</c> where its items or limits moved it, a button group's other buttons' selection, and
    /// - when the text of a component named from its text changed - its code name, and a labelled
    /// component's with its label's, as App Designer renames them. A null <c>ContextMenu</c> takes
    /// the component's context menu off.
    /// </summary>
    /// <returns>Null when the component took the value; else what it said, and nothing changed.</returns>
    public string? SetProperty(DesignComponent component, string name, DesignValue? value)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(name);
        EndGesture(commit: false);
        if (name == "ContextMenu" && value is null)
        {
            // A context menu taken off: the component's ContextMenu emptied, and its assignment gone.
            if (component.Get(name) is not { } assigned)
            {
                return null;
            }

            Do([new PropertyEdit(component, name, assigned, null, assigned, new NumericValue(0, 0, []))]);
            return null;
        }

        DesignProperty? property = component.Class?.Property(name);
        DesignValue? defaultValue = property?.DefaultFor(Theme);
        DesignValue? toSet = value ?? defaultValue;
        if (toSet is null)
        {
            return $"{name} has no default to go back to.";
        }

        DesignValue? held = component.Get(name);

        // A grid's tracks read back with the implicit '1x' rows its children reach; undo sets the ones the design holds (DS4).
        DesignValue? before = component.Type == GridType && name is "RowHeight" or "ColumnWidth"
            ? held ?? defaultValue
            : Runtime.Read(component, name, held ?? defaultValue ?? toSet) ?? held ?? defaultValue;
        if (before is OpaqueValue)
        {
            before = null; // a recorded default the designer cannot write back (an object): undo leaves the component
        }
        var linked = Linked(component, name).Select(l => (l.Component, l.Name, Before: Runtime.Read(l.Component, l.Name, LikeOf(l.Component, l.Name))))
            .ToArray();
        string? oldText = TextOf(component);

        if (Runtime.Set(component, name, toSet) is { } refused)
        {
            return refused;
        }

        DesignValue kept = Runtime.Read(component, name, toSet) ?? toSet;
        var edits = new List<Edit>();

        // Linked properties first: undoing the property they followed puts it back before them.
        foreach ((DesignComponent other, string otherName, DesignValue? otherBefore) in linked)
        {
            if (Runtime.Read(other, otherName, LikeOf(other, otherName)) is not { } after)
            {
                continue;
            }

            DesignValue? designAfter = IsDefaultValue(other, otherName, after) ? null : after;
            DesignValue? designBefore = other.Get(otherName);
            if (!SameValue(designAfter, designBefore))
            {
                edits.Add(new PropertyEdit(other, otherName, designBefore, designAfter, otherBefore, RuntimeAfter: null) { Selects = ReferenceEquals(other, component) });
            }
        }

        DesignValue? designValue = property is not null && IsDefaultValue(component, name, kept) ? null : kept;
        edits.Add(new PropertyEdit(component, name, held, designValue, before, toSet));

        // A component named from its text follows its text, as App Designer renames it.
        if (oldText is not null && DesignNames.Of(component.Type)?.TextProperty == name && DesignPalette.Text(kept) is { } newText)
        {
            edits.AddRange(Renames(component, oldText, newText));
        }

        Do([.. edits]);
        return null;
    }

    /// <summary>The properties that follow <paramref name="name"/> on <paramref name="component"/>: its value, and a button group's selection.</summary>
    private static IEnumerable<(DesignComponent Component, string Name)> Linked(DesignComponent component, string name)
    {
        if (name != "Value" && component.Class?.Property("Value") is not null)
        {
            yield return (component, "Value");
        }

        if (component.Type is "matlab.ui.control.RadioButton" or "matlab.ui.control.ToggleButton" && component.Parent is { } group)
        {
            foreach (DesignComponent sibling in group.Children)
            {
                if (!ReferenceEquals(sibling, component) && sibling.Type == component.Type)
                {
                    yield return (sibling, "Value");
                }
            }
        }
    }

    private DesignValue? LikeOf(DesignComponent component, string name) =>
        component.Get(name) ?? component.Class?.Property(name)?.DefaultFor(Theme);

    private bool IsDefaultValue(DesignComponent component, string name, DesignValue value) =>
        component.Class?.Property(name) is { } property && DesignCodeWriter.IsDefault(component, property, value, Theme);

    private static bool SameValue(DesignValue? a, DesignValue? b) =>
        a is null ? b is null : b is not null && DesignValueText.IsEqual(a, b);

    /// <summary>The text <paramref name="component"/> shows, in the property its class shows it in; null when it shows none.</summary>
    private string? TextOf(DesignComponent component) =>
        DesignNames.Of(component.Type)?.TextProperty is { } property && LikeOf(component, property) is { } value
            ? DesignPalette.Text(value)
            : null;

    // --- names (DS3) -----------------------------------------------------------------------------

    /// <summary>
    /// The names a component's code name may not take: every component's, and
    /// every function, property and the class the app's code declares.
    /// </summary>
    public IReadOnlyList<string> NamesInUse()
    {
        var names = new List<string>(Design.Components.Select(static c => c.CodeName));
        names.AddRange(Design.Components.SelectMany(static c => c.Properties).Select(static p => p.Value).OfType<CallbackValue>().Select(static c => c.Method));
        bool inProperties = false;
        foreach (string raw in Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = raw.Trim();
            if (ClassLine().Match(line) is { Success: true } cls)
            {
                names.Add(cls.Groups[1].Value);
            }
            else if (FunctionLine().Match(line) is { Success: true } fn)
            {
                names.Add(fn.Groups[1].Value);
            }
            else if (line.StartsWith("properties", StringComparison.Ordinal))
            {
                inProperties = true;
            }
            else if (inProperties && line == "end")
            {
                inProperties = false;
            }
            else if (inProperties && PropertyLine().Match(line) is { Success: true } prop)
            {
                names.Add(prop.Groups[1].Value);
            }
        }

        return names.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Renames <paramref name="component"/> to <paramref name="name"/>, as one edit: the design's
    /// references to it and the app's code's <c>app.Name</c> follow, as App Designer's rename does.
    /// </summary>
    /// <returns>Null when renamed; else why the name cannot be taken, and nothing changed.</returns>
    public string? Rename(DesignComponent component, string name)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(name);
        if (name == component.CodeName)
        {
            return null;
        }

        if (DesignNames.WhyNotName(name, component.CodeName, NamesInUse()) is { } why)
        {
            return why;
        }

        EndGesture(commit: false);
        Do([new RenameEdit(component, component.CodeName, name)]);
        return null;
    }

    /// <summary>
    /// The component <paramref name="label"/> labels: one named as App Designer names a labelled
    /// component's label, its own code name with <c>Label</c> after it, beside it in its container.
    /// App Designer keeps the two as a group; until the designer does (open item 150), the names pair them.
    /// </summary>
    public DesignComponent? LabelledBy(DesignComponent label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (label.Type != "matlab.ui.control.Label" || !label.CodeName.EndsWith("Label", StringComparison.Ordinal) || label.Parent is not { } parent)
        {
            return null;
        }

        string name = label.CodeName[..^"Label".Length];
        return parent.Children.FirstOrDefault(c => c.CodeName == name && !ReferenceEquals(c, label));
    }

    /// <summary>
    /// The renames a text edit brings (<c>ModifyCodeNameGenerator</c>): a component named from its old
    /// text is named from its new one; a labelled component's label names the component, and the label
    /// is named after it.
    /// </summary>
    private IEnumerable<Edit> Renames(DesignComponent component, string oldText, string newText)
    {
        var inUse = NamesInUse().ToList();
        if (LabelledBy(component) is { } labelled && DesignNames.Of(labelled.Type) is { } naming)
        {
            string componentName = DesignNames.IsNamedByHand(oldText, naming.Text, labelled.CodeName, naming.CodeName)
                ? labelled.CodeName
                : DesignNames.ForEditedText(newText, naming.Text, labelled.CodeName, naming.CodeName, inUse);
            if (componentName != labelled.CodeName)
            {
                inUse.Add(componentName);
                yield return new RenameEdit(labelled, labelled.CodeName, componentName) { Selects = false };
            }

            string stem = ReplaceFirst(component.CodeName, "Label", string.Empty);
            if (!DesignNames.IsNamedByHand(oldText, naming.Text, stem, naming.CodeName))
            {
                string from = componentName != labelled.CodeName ? componentName + "Label" : newText;
                string labelName = DesignNames.ForEditedText(from, "Label", component.CodeName, "Label", inUse);
                if (labelName != component.CodeName)
                {
                    yield return new RenameEdit(component, component.CodeName, labelName);
                }
            }

            yield break;
        }

        if (DesignNames.Of(component.Type) is { } own && !DesignNames.IsNamedByHand(oldText, own.Text, component.CodeName, own.CodeName))
        {
            string renamed = DesignNames.ForEditedText(newText, own.Text, component.CodeName, own.CodeName, inUse);
            if (renamed != component.CodeName)
            {
                yield return new RenameEdit(component, component.CodeName, renamed);
            }
        }
    }

    private static string ReplaceFirst(string text, string find, string with)
    {
        int at = text.IndexOf(find, StringComparison.Ordinal);
        return at < 0 ? text : text[..at] + with + text[(at + find.Length)..];
    }

    /// <summary>Renames every reference to <paramref name="from"/> in the design and every <c>app.from</c> in the code.</summary>
    private void RenameReferences(string from, string to)
    {
        foreach (DesignComponent c in Design.Components)
        {
            foreach ((string name, DesignValue value) in c.Properties.ToArray())
            {
                if (value is ComponentsValue refs && refs.CodeNames.Contains(from, StringComparer.Ordinal))
                {
                    c.Set(name, new ComponentsValue(refs.CodeNames.Select(n => n == from ? to : n).ToArray(), refs.Bracketed));
                }
            }
        }

        Text = Regex.Replace(Text, @"\b" + DesignValueText.AppObjectName + @"\." + Regex.Escape(from) + @"\b", DesignValueText.AppObjectName + "." + to);
    }

    [GeneratedRegex(@"^classdef\s+(?:\([^)]*\)\s*)?(\w+)")]
    private static partial Regex ClassLine();

    [GeneratedRegex(@"^function\s+(?:(?:\[[^\]]*\]|\w+)\s*=\s*)?(\w+)")]
    private static partial Regex FunctionLine();

    [GeneratedRegex(@"^([A-Za-z]\w*)\b")]
    private static partial Regex PropertyLine();

    // --- saving ----------------------------------------------------------------------------------

    /// <summary>
    /// Saves the design into its app: the component properties block and <c>createComponents</c>
    /// written again, every other line kept, and App Designer's component tree brought up to date
    /// with them (DS6).
    /// </summary>
    /// <returns>What the save did, for the status line.</returns>
    /// <exception cref="InvalidOperationException">The design cannot be saved (<see cref="CanSave"/>).</exception>
    /// <exception cref="System.IO.IOException">The file could not be written.</exception>
    public string Save()
    {
        if (!CanSave)
        {
            throw new InvalidOperationException(Path is null
                ? "The app has no file to save into."
                : "The app's createComponents has lines the designer cannot place, so a save would lose them: "
                    + string.Join(" ", ReadProblems));
        }

        string text = DesignCodeWriter.Splice(Text, Design, Style)
            ?? throw new System.IO.IOException("The app's code does not have its component properties and createComponents where App Designer puts them.");

        // A responsive app's generated callback and widths, as App Designer writes them again on every save (DS4b).
        text = DesignResponsive.Splice(text, Design, Style, c => BoxOf(c) is { } box ? box.Box.Size : null);
        (string saved, MlappSaveResult result) = AppDesignerDocument.Save(Path!, Path!, text);
        Text = saved;
        _savedVersion = _version;
        OnChanged();
        return $"Saved {Path}.{AppDesignerDocument.StatusNote(result)}";
    }

    /// <inheritdoc />
    public void Dispose() => Runtime.Dispose();

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static NumericValue Row(Rect2D r) => NumericValue.Row(r.X, r.Y, r.Width, r.Height);

    private static int IndexIn(DesignComponent parent, DesignComponent child)
    {
        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (ReferenceEquals(parent.Children[i], child))
            {
                return i;
            }
        }

        return parent.Children.Count;
    }

    // --- edits -----------------------------------------------------------------------------------

    private sealed class Gesture(Dictionary<DesignComponent, Rect2D> start)
    {
        public Dictionary<DesignComponent, Rect2D> Start { get; } = start;

        public Dictionary<DesignComponent, Rect2D> Last { get; } = new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>One change to the design and its component, which can be undone.</summary>
    private abstract record Edit(DesignComponent Component)
    {
        /// <summary>Whether the component is selected once the edit is done or undone (not a sibling that followed it).</summary>
        public bool Selects { get; init; } = true;

        public abstract void Do(DesignDocument document);

        public abstract void Undo(DesignDocument document);
    }

    /// <summary>
    /// A property set: what the design held before (null when unset) and holds after, and the
    /// runtime's value before, which undoing sets back. What the component kept is read back into
    /// the design.
    /// </summary>
    private sealed record PositionEdit(DesignComponent Component, DesignValue? Before, NumericValue After, NumericValue RuntimeBefore)
        : Edit(Component)
    {
        public override void Do(DesignDocument document)
        {
            document.Runtime.Set(Component, "Position", After);
            Component.Set("Position", document.Runtime.ReadNumbers(Component, "Position") is [_, _, _, _] kept
                ? NumericValue.Row(kept)
                : After);
        }

        public override void Undo(DesignDocument document)
        {
            document.Runtime.Set(Component, "Position", RuntimeBefore);
            if (Before is null)
            {
                Component.Unset("Position");
            }
            else
            {
                Component.Set("Position", Before);
            }
        }
    }

    /// <summary>
    /// A component moved into another container: last among its new siblings, as the last made. Undone,
    /// it goes back where it was when it moved - recorded then, since a step moving several children of
    /// one container undoes them last to first.
    /// </summary>
    private sealed record ParentEdit(DesignComponent Component, DesignComponent From, int Index, DesignComponent To) : Edit(Component)
    {
        private int _at = -1;

        public override void Do(DesignDocument document)
        {
            _at = IndexIn(From, Component);
            document.Runtime.SetParent(Component, To);
            From.Remove(Component);
            To.Add(Component);
        }

        public override void Undo(DesignDocument document)
        {
            document.Runtime.SetParent(Component, From);
            To.Remove(Component);
            From.Insert(System.Math.Min(_at >= 0 ? _at : Index, From.Children.Count), Component);
        }
    }

    /// <summary>
    /// A property set (DS3): what the design held before and after (null when unset), and the values
    /// the runtime's component is set to - after, when done (null when another edit sets it), and
    /// before, when undone (null when nothing needs putting back).
    /// </summary>
    private sealed record PropertyEdit(DesignComponent Component, string Name, DesignValue? Before, DesignValue? After, DesignValue? RuntimeBefore, DesignValue? RuntimeAfter)
        : Edit(Component)
    {
        public override void Do(DesignDocument document)
        {
            if (RuntimeAfter is not null)
            {
                document.Runtime.Set(Component, Name, RuntimeAfter);
            }

            Put(After);
        }

        public override void Undo(DesignDocument document)
        {
            if (RuntimeBefore is not null)
            {
                document.Runtime.Set(Component, Name, RuntimeBefore);
            }

            Put(Before);
        }

        private void Put(DesignValue? value)
        {
            if (value is null)
            {
                Component.Unset(Name);
            }
            else
            {
                Component.Set(Name, value);
            }
        }
    }

    /// <summary>A component added to a container (DS3): at <paramref name="Index"/> among its children, or last.</summary>
    private sealed record AddEdit(DesignComponent Component, DesignComponent Parent, int? Index) : Edit(Component)
    {
        public override void Do(DesignDocument document)
        {
            Parent.Insert(System.Math.Min(Index ?? Parent.Children.Count, Parent.Children.Count), Component);
            document.Runtime.MakeNew(Component);
        }

        public override void Undo(DesignDocument document)
        {
            document.Runtime.Remove(Component);
            Parent.Remove(Component);
        }
    }

    /// <summary>A component deleted from its container, with everything inside it (DS3); undone, it goes back where it was when it was deleted.</summary>
    private sealed record DeleteEdit(DesignComponent Component, DesignComponent Parent, int Index) : Edit(Component)
    {
        private int _at = -1;

        public override void Do(DesignDocument document)
        {
            _at = IndexIn(Parent, Component);
            document.Runtime.Remove(Component);
            Parent.Remove(Component);
        }

        public override void Undo(DesignDocument document)
        {
            Parent.Insert(System.Math.Min(_at >= 0 ? _at : Index, Parent.Children.Count), Component);
            document.Runtime.MakeNew(Component);
        }
    }

    /// <summary>Nothing changed, but <paramref name="Component"/> selected once the step is done or undone (DS4).</summary>
    private sealed record SelectEdit(DesignComponent Component) : Edit(Component)
    {
        public override void Do(DesignDocument document)
        {
        }

        public override void Undo(DesignDocument document)
        {
        }
    }

    /// <summary>A component renamed (DS3): its references in the design and in the app's code follow.</summary>
    private sealed record RenameEdit(DesignComponent Component, string From, string To) : Edit(Component)
    {
        public override void Do(DesignDocument document)
        {
            Component.CodeName = To;
            document.RenameReferences(From, To);
        }

        public override void Undo(DesignDocument document)
        {
            Component.CodeName = From;
            document.RenameReferences(To, From);
        }
    }
}
