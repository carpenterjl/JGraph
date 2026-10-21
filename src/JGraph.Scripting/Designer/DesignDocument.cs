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
    private string _newline;
    private bool _trailingNewline;

    private DesignDocument(string text, string? path, DesignReadResult read, DesignFileFormat format = DesignFileFormat.Mlapp, DesignPlainTextDetails? details = null, IReadOnlyList<string>? pathFolders = null, IReadOnlyList<DesignGroupRecord>? groups = null)
    {
        Text = text;
        Path = path;
        Format = format;
        Details = details;
        Style = read.Style;
        Design = read.Design!;
        if (groups is not null)
        {
            Design.AttachGroups(groups); // the file's component groups (open item 150); a plain-text app's reader makes its own
        }

        ReadProblems = read.Problems;
        Code = ReadCode(text, Design);
        _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        _trailingNewline = text.EndsWith('\n');
        string? folder = path is null ? null : System.IO.Path.GetDirectoryName(path);
        Runtime = DesignRuntime.Build(Design, folder, Style.Theme, pathFolders);

        // The custom components registered for the app: in its folder and on the session's path
        // (designer plan DS7b), each as a library entry under its category.
        UserComponents = DesignUserComponents.Discover(folder is null ? pathFolders ?? [] : [folder, .. pathFolders ?? []]);
        UserPaletteEntries = DesignUserComponents.Sections(UserComponents)
            .SelectMany(static s => s.Entries.Select(c => new DesignPaletteEntry(c.ComponentName, s.Title, c.ClassName) { UserComponent = c }))
            .ToArray();
        AttachUserClasses();
        LayOutRegions();
    }

    /// <summary>The custom components registered for the app (designer plan DS7b): those in its folder and on the session's path.</summary>
    public IReadOnlyList<DesignUserComponent> UserComponents { get; }

    /// <summary>The library entries the custom components make, section by section after App Designer's own, each section's entries by name.</summary>
    public IReadOnlyList<DesignPaletteEntry> UserPaletteEntries { get; }

    /// <summary>The library entry keyed <paramref name="key"/> (<see cref="DesignPaletteEntry.Key"/>): one of App Designer's, or a custom component's; null for none.</summary>
    public DesignPaletteEntry? FindPaletteEntry(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Palette.Find(key) ?? UserPaletteEntries.FirstOrDefault(e => e.Key == key);
    }

    /// <summary>
    /// The inspector's groups for <paramref name="component"/>: App Designer's for its class, or a
    /// custom component's from its running class (designer plan DS7b); empty for a class neither knows.
    /// </summary>
    public IReadOnlyList<DesignInspectorGroup> InspectorGroupsOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.Class is { IsUserComponent: true } && Runtime.UserClassOf(component.Type) is { } user
            ? user.InspectorGroups
            : DesignInspector.R2025b.GroupsOf(component.Type);
    }

    /// <summary>
    /// Gives every component read as a call of a class the designer has no record of - a custom
    /// component's constructor - its class, described from the running class (designer plan DS7b),
    /// so that the generator, the inspector and the Callbacks tab know it; one whose class the
    /// runtime cannot make stays as read.
    /// </summary>
    private void AttachUserClasses()
    {
        var missing = new List<string>();
        var errored = new List<string>();
        foreach (DesignComponent component in Design.Components)
        {
            if (component.Class is not null || !DesignUserComponents.IsUserComponent(component))
            {
                continue;
            }

            if (Runtime.UserClassOf(component.Type) is { } user)
            {
                component.Class = user.Class;
            }
            else
            {
                // As MLAPPUserComponentValidator tells them apart: a class MATLAB cannot find is
                // missing; one it finds whose object could not be made (its setup failed) errored.
                List<string> list = Runtime.KnowsClass(component.Type) ? errored : missing;
                if (!list.Contains(component.Type, StringComparer.Ordinal))
                {
                    list.Add(component.Type);
                }
            }
        }

        MissingUserComponents = missing;
        ErroredUserComponents = errored;
    }

    /// <summary>
    /// The custom component classes the app names that are not on the app's folder or the path
    /// (designer plan DS7b; App Designer's <c>MissingUserComponents</c> warning), each once, in the
    /// design's order; empty when every class was found.
    /// </summary>
    public IReadOnlyList<string> MissingUserComponents { get; private set; } = [];

    /// <summary>
    /// The custom component classes the app names that were found but whose component could not be
    /// made (App Designer's <c>ErroredUserComponents</c> warning: a class whose setup fails), each
    /// once; empty when every component was made.
    /// </summary>
    public IReadOnlyList<string> ErroredUserComponents { get; private set; } = [];

    /// <summary>
    /// Takes out of the design every component of a missing or errored custom component class, as
    /// App Designer's Open Anyway removes them before the app opens (<c>_removeComponentsFromLoadedData</c>):
    /// the components and what names them go, the callback functions they named stay in the code,
    /// unbound; nothing to undo, and the document is not changed by it - the next Save writes the
    /// app without them.
    /// </summary>
    public void RemoveUnavailableUserComponents()
    {
        DesignComponent[] gone = Design.Components.Where(static c => c.Class is null && DesignUserComponents.IsUserComponent(c)).ToArray();
        if (gone.Length > 0)
        {
            // The callback functions they named stay, whatever their code (App Designer leaves
            // them unbound), so the deletion's callback edits are left out.
            string[] names = gone.SelectMany(static g => Walk(g)).Select(static c => c.CodeName).ToArray();
            Edit[] step = [.. DeleteEdits(gone).Where(static e => e is not CallbackListEdit)];
            foreach (Edit edit in step)
            {
                edit.Do(this);
            }

            foreach (string name in names)
            {
                Runtime.ForgetProblemsOf(name);
            }

            Settled(step, done: true);
        }

        MissingUserComponents = [];
        ErroredUserComponents = [];
    }

    /// <summary>The format the app is kept in: the <c>.mlapp</c> package, or App Designer's plain-text <c>.m</c> file (designer plan DS7a).</summary>
    public DesignFileFormat Format { get; private set; }

    /// <summary>
    /// A plain-text app's details - its App Details, internal data and thumbnail - as read from the
    /// file and written back; null for an app kept as an <c>.mlapp</c>, which holds them in its
    /// package. Made for an app saved as plain text for the first time.
    /// </summary>
    public DesignPlainTextDetails? Details { get; private set; }

    /// <summary>The format a path's extension names: <c>.m</c> is the plain-text format, anything else the package.</summary>
    public static DesignFileFormat FormatOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return System.IO.Path.GetExtension(path).Equals(".m", StringComparison.OrdinalIgnoreCase) ? DesignFileFormat.PlainText : DesignFileFormat.Mlapp;
    }

    /// <summary>Raised after the design, the selection or the saved state changed: the canvas draws again.</summary>
    public event EventHandler? Changed;

    /// <summary>The app's class, as it was read or last saved.</summary>
    public string Text { get; private set; }

    /// <summary>The <c>.mlapp</c> or plain-text <c>.m</c> the app is in; null for one that is not saved.</summary>
    public string? Path { get; private set; }

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

    /// <summary>Whether the design can be saved: it holds everything its code does, and the code around the components can be told from the user's (<see cref="Code"/>).</summary>
    public bool CanSave => ReadProblems.Count == 0 && Path is not null && Code is not null;

    /// <summary>Whether there are edits not saved.</summary>
    public bool IsDirty => _version != _savedVersion || _textDirty;

    /// <summary>The selected components, all in one container; the figure is selected alone.</summary>
    public IReadOnlyList<DesignComponent> Selection => _selection;

    /// <summary>Whether there is an edit to undo.</summary>
    public bool CanUndo => _undo.Count > 0 && _gesture is null && !_regionPreview;

    /// <summary>Whether there is an edit to redo.</summary>
    public bool CanRedo => _redo.Count > 0 && _gesture is null && !_regionPreview;

    /// <summary>Opens the App Designer file <paramref name="path"/>: an <c>.mlapp</c> package, or a plain-text <c>.m</c> app.</summary>
    /// <exception cref="System.IO.InvalidDataException">The file is not an app App Designer lays out.</exception>
    public static DesignDocument Open(string path, IReadOnlyList<string>? pathFolders = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (FormatOf(path) == DesignFileFormat.PlainText)
        {
            return FromPlainText(System.IO.File.ReadAllText(path), path, pathFolders);
        }

        string text = JgsMlapp.ReadCode(path);
        DesignReadResult read = DesignCodeReader.Read(text);
        if (read.Design is null)
        {
            throw new System.IO.InvalidDataException(
                $"The app's components cannot be read: {string.Join(" ", read.Problems)}");
        }

        // The package's component groups come with the code (open item 150).
        return new DesignDocument(text, path, read, pathFolders: pathFolders, groups: JgsMlapp.ReadGroups(path));
    }

    /// <summary>
    /// Opens a plain-text app's <paramref name="text"/> (designer plan DS7a): its components from the
    /// appendix, its code from the class; <paramref name="path"/> is the <c>.m</c> it saves into, if any;
    /// <paramref name="pathFolders"/> the session's path, where a custom component's class is found (DS7b).
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException">The text is not a plain-text app.</exception>
    public static DesignDocument FromPlainText(string text, string? path, IReadOnlyList<string>? pathFolders = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        DesignPlainTextReadResult read = DesignPlainText.Read(text);
        if (read.Read.Design is null)
        {
            throw new System.IO.InvalidDataException(
                $"The app's components cannot be read: {string.Join(" ", read.Read.Problems)}");
        }

        return new DesignDocument(text, path, read.Read, DesignFileFormat.PlainText, read.Details, pathFolders);
    }

    /// <summary>Opens an app's class text; <paramref name="path"/> is the <c>.mlapp</c> it saves into, if any; <paramref name="pathFolders"/> the session's path (DS7b).</summary>
    /// <exception cref="System.IO.InvalidDataException">The text is not an app App Designer lays out.</exception>
    public static DesignDocument FromText(string text, string? path, IReadOnlyList<string>? pathFolders = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        DesignReadResult read = DesignCodeReader.Read(text);
        if (read.Design is null)
        {
            throw new System.IO.InvalidDataException(
                $"The app's components cannot be read: {string.Join(" ", read.Problems)}");
        }

        return new DesignDocument(text, path, read, pathFolders: pathFolders);
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

    /// <summary>
    /// Selects <paramref name="components"/> - a member of a group as its group (open item 150); an
    /// empty selection selects the figure, as App Designer's does.
    /// </summary>
    public void Select(IReadOnlyList<DesignComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        SetPicks(components.Count == 0 ? [DesignPick.Of(Design.Figure)] : Promoted(components));
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
        Settled(step, done: false);
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
        Settled(step, done: true);
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
        Settled(step, done: true);
    }

    /// <summary>
    /// After an edit: the components it touched and still in the design are selected (the figure when
    /// none is) - a group's as the group, and the picks as they were when the edit touched just what
    /// was selected (a member moved on its own stays selected on its own); an edit that sets the
    /// picks itself has its way (open item 150). The theme follows the figure's, and the canvas draws again.
    /// </summary>
    private void Settled(Edit[] step, bool done)
    {
        _boxes = null;
        _renderedText = null;
        DesignComponent[] touched = step.Where(static e => e.Selects && !e.KeepsSelection).Select(static e => e.Component)
            .Where(c => IsInDesign(c)).Distinct(ReferenceEqualityComparer.Instance).Cast<DesignComponent>().ToArray();
        if (touched.Length == 0 && step.Any(static e => e.KeepsSelection) && step.All(static e => e.KeepsSelection || !e.Selects))
        {
            // An edit of the code around the components (DS5) leaves the selection where it was - the
            // properties it unbinds along the way select nothing either.
            touched = _selection.Where(IsInDesign).ToArray();
        }

        // Every edit's picks together (several groups taken apart select every one's members).
        List<DesignPick> gathered = [];
        foreach (DesignPick pick in step.SelectMany(e => e.PicksAfter(done) ?? []))
        {
            if (pick.Leaves().All(IsInDesign) && !DesignGroupRules.IsSelected(gathered, pick))
            {
                gathered.Add(pick);
            }
        }

        IReadOnlyList<DesignPick>? picks = step.Any(e => e.PicksAfter(done) is not null) ? gathered : null;
        if (picks is { Count: > 0 })
        {
            _picks = picks;
            _selection = picks.SelectMany(static p => p.Leaves()).Distinct(ReferenceEqualityComparer.Instance).Cast<DesignComponent>().ToArray();
        }
        else if (touched.Length > 0 && touched.Length == _selection.Count && touched.All(t => _selection.Contains(t, ReferenceEqualityComparer.Instance))
            && _picks.All(p => p.Leaves().All(IsInDesign)))
        {
            // The same components as before: the picks stay as they were.
        }
        else
        {
            _picks = touched.Length > 0 ? Promoted(touched) : [DesignPick.Of(Design.Figure)];
            _selection = _picks.SelectMany(static p => p.Leaves()).Distinct(ReferenceEqualityComparer.Instance).Cast<DesignComponent>().ToArray();
        }

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
        IReadOnlyList<(DesignComponent Component, int? Index)> made = MakeFromEntry(entry, parent, size);
        Place(parent, made.Select(static m => m.Component).ToArray(), topLeft);

        List<Edit> step = made.Select(m => (Edit)new AddEdit(m.Component, parent, m.Index)).ToList();
        AddLabeledGroup(entry, made, step);
        Do([.. step]);
        return made.Select(static m => m.Component).ToArray();
    }

    /// <summary>A labelled entry's label and field are a labelled component: grouped as they are made (open item 150).</summary>
    private static void AddLabeledGroup(DesignPaletteEntry entry, IReadOnlyList<(DesignComponent Component, int? Index)> made, List<Edit> step)
    {
        if (entry.Label != DesignLabelPlace.None && made.Count == 2 && made[0].Component.Type == DesignGroup.LabelType)
        {
            step.Add(LabeledGroupEdit(made[0].Component, made[1].Component));
        }
    }

    /// <summary>What <paramref name="entry"/> makes for <paramref name="parent"/>: App Designer's palette's components, or a custom component (DS7b).</summary>
    /// <exception cref="InvalidOperationException">The entry is one the designer does not make, or a custom component whose class the runtime cannot make.</exception>
    private IReadOnlyList<(DesignComponent Component, int? Index)> MakeFromEntry(DesignPaletteEntry entry, DesignComponent parent, Size2D? size = null) =>
        entry.UserComponent is { } user ? [(MakeUserComponent(user, size), null)] : Palette.Make(entry, Design, parent, new Point2D(0, 0), size);

    /// <summary>
    /// A new custom component (designer plan DS7b, <c>_UserComponentEntryDefinition._makeEntryData</c>):
    /// of its class as the runtime describes it, named by the class's short name made unique among
    /// the app's names, sized by its registration's default position (or as drawn), with no label.
    /// </summary>
    /// <exception cref="InvalidOperationException">The class is not on the path, or is no custom component the runtime can make.</exception>
    private DesignComponent MakeUserComponent(DesignUserComponent user, Size2D? size)
    {
        DesignUserClass described = Runtime.UserClassOf(user.ClassName)
            ?? throw new InvalidOperationException($"{user.ComponentName}: the class {user.ClassName} is not on the path, or is no custom component the designer can make.");
        (string codeName, _) = DesignNames.ForNew(user.ClassName, [.. Design.Components.Select(static c => c.CodeName), .. NamesInUse()], []);
        var component = new DesignComponent(codeName, described.Class);
        (double width, double height) = user.DefaultSize;
        component.Set("Position", NumericValue.Row(1, 1, size?.Width ?? width, size?.Height ?? height));
        return component;
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
        // Only labelled components' fields: the labelled components whole (open item 150).
        components = WithLabels(components);
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

        // Out of their groups first (undone last), then gone.
        edits.AddRange(GroupEditsForDelete(gone));
        foreach (DesignComponent component in gone)
        {
            DesignComponent parent = component.Parent!;
            edits.Add(new DeleteEdit(component, parent, IndexIn(parent, component)));
        }

        // A callback the deleted components alone were bound to, whose code is still what it was made
        // with, goes with them (CallbackBindingCollection.removeBinding without keepOrphan, DS5); one
        // with code of its own stays, no longer associated with a component. Taken out last to first,
        // so that an undo, which replays the step backwards, puts each back at its own index.
        if (Code is not null)
        {
            var deleted = new HashSet<DesignComponent>(gone.SelectMany(static g => Walk(g)), ReferenceEqualityComparer.Instance);
            for (int i = Code.Callbacks.Count - 1; i >= 0; i--)
            {
                DesignCallback callback = Code.Callbacks[i];
                if (callback.IsGenerated || callback.IsStartup)
                {
                    continue;
                }

                IReadOnlyList<DesignCallbackBinding> bindings = DesignCallbacks.BindingsOf(Design, callback.Name);
                if (bindings.Count > 0 && bindings.All(b => deleted.Contains(b.Component)) && callback.IsDefaultOrEmpty)
                {
                    edits.Add(new CallbackListEdit(Design.Figure, callback, i, Add: false));
                }
            }
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
    /// The names a component's code name may not take (App Designer's <c>getCodeNameTypeHashmap</c>):
    /// every component's, every function, property and the class the app's code declares, and the
    /// members every app inherits from <c>matlab.apps.AppBase</c>.
    /// </summary>
    public IReadOnlyList<string> NamesInUse()
    {
        var names = new List<string>(Design.Components.Select(static c => c.CodeName));
        names.AddRange(DesignNames.AppDesignerKeywords);
        names.AddRange(Design.Components.SelectMany(static c => c.Properties).Select(static p => p.Value).OfType<CallbackValue>().Select(static c => c.Method));
        names.AddRange(Callbacks.Select(static c => c.Name));
        bool inProperties = false;
        foreach (string raw in CurrentText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
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
        Do([new RenameEdit(component, component.CodeName, name), .. CallbackRenames(component, component.CodeName, name)]);
        return null;
    }

    /// <summary>
    /// The callbacks renamed with their component (<c>CodeController.renameComponent</c>): a callback
    /// bound to the component alone, still named as App Designer named it (every callback counts as
    /// named so once an app is loaded; one the user renamed no longer does), whose name holds the old
    /// code name, takes the new one in its place, made unique.
    /// </summary>
    private IEnumerable<Edit> CallbackRenames(DesignComponent component, string from, string to)
    {
        if (Code is null)
        {
            yield break;
        }

        var inUse = NamesInUse().ToList();
        foreach (DesignCallback callback in Code.Callbacks.Where(static c => !c.IsGenerated && !c.IsStartup && c.Name == c.GeneratedName))
        {
            IReadOnlyList<DesignCallbackBinding> bindings = DesignCallbacks.BindingsOf(Design, callback.Name);
            int at = callback.Name.IndexOf(from, StringComparison.Ordinal);
            if (bindings.Count != 1 || !ReferenceEquals(bindings[0].Component, component) || at < 0)
            {
                continue;
            }

            string renamed = DesignCallbacks.Unique(callback.Name.Remove(at, from.Length).Insert(at, to), inUse);
            if (renamed != callback.Name)
            {
                inUse.Add(renamed);
                yield return new RenameCallbackEdit(Design.Figure, callback, callback.Name, renamed, Generated: true);
            }
        }
    }

    /// <summary>
    /// The component <paramref name="label"/> labels: the field of the labelled component it is the
    /// label of (open item 150: App Designer pairs them by the group, never by name).
    /// </summary>
    public DesignComponent? LabelledBy(DesignComponent label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return Design.LabeledGroupOf(label) is { Label: { } l, Field: { } field } && ReferenceEquals(l, label) ? field : null;
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

    /// <summary>Renames every reference to <paramref name="from"/> in the design and every <c>app.from</c> in the user's code.</summary>
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

        if (Code is not null)
        {
            RenameComponentInUserCode(from, to);
        }
        else
        {
            Text = Regex.Replace(Text, @"\b" + DesignValueText.AppObjectName + @"\." + Regex.Escape(from) + @"\b", DesignValueText.AppObjectName + "." + to);
        }
    }

    [GeneratedRegex(@"^classdef\s+(?:\([^)]*\)\s*)?(\w+)")]
    private static partial Regex ClassLine();

    [GeneratedRegex(@"^function\s+(?:(?:\[[^\]]*\]|\w+)\s*=\s*)?(\w+)")]
    private static partial Regex FunctionLine();

    [GeneratedRegex(@"^([A-Za-z]\w*)\b")]
    private static partial Regex PropertyLine();

    // --- saving ----------------------------------------------------------------------------------

    /// <summary>
    /// Saves the design into its app: the class as the design and its code now stand
    /// (<see cref="CurrentText"/>: every generated part written again, the user's code as it is), and
    /// App Designer's component tree brought up to date with it (DS6).
    /// </summary>
    /// <returns>What the save did, for the status line.</returns>
    /// <exception cref="InvalidOperationException">The design cannot be saved (<see cref="CanSave"/>).</exception>
    /// <exception cref="System.IO.IOException">The file could not be written.</exception>
    public string Save()
    {
        if (!CanSave)
        {
            throw new InvalidOperationException(Path is null ? "The app has no file to save into." : Code is null ? CodeProblem! : SaveRefusal);
        }

        if (Format == DesignFileFormat.PlainText)
        {
            string text = CurrentText;
            WriteWhole(Path!, text);
            Text = text;
            _savedVersion = _version;
            _textDirty = false;
            OnChanged();
            return $"Saved {Path}.";
        }

        (string saved, MlappSaveResult result) = AppDesignerDocument.Save(Path!, Path!, CurrentText, Design.GroupRecords());
        Text = saved;
        _savedVersion = _version;
        _textDirty = false;
        OnChanged();
        return $"Saved {Path}.{AppSaveNote(result)}";
    }

    /// <summary>Whether the design can be saved under another name: it holds everything its code does, and its code can be worked on.</summary>
    public bool CanSaveAs => ReadProblems.Count == 0 && Code is not null;

    /// <summary>
    /// Why a save is refused when the design does not hold everything its file does
    /// (<see cref="ReadProblems"/>): lines of <c>createComponents</c> the designer cannot place, or
    /// entries of a plain-text app's appendix it cannot read. For the status line and the refusal.
    /// </summary>
    public string SaveRefusal => Format == DesignFileFormat.PlainText
        ? $"The app's appendix has {ReadProblems.Count} entr{(ReadProblems.Count == 1 ? "y" : "ies")} the designer cannot read, so a save would lose them: {string.Join(" ", ReadProblems)}"
        : $"The app's createComponents has {ReadProblems.Count} line(s) the designer cannot place, so a save would lose them: {string.Join(" ", ReadProblems)}";

    /// <summary>
    /// Saves the app as <paramref name="target"/>, in the format its extension names (designer plan
    /// DS7a): as an <c>.mlapp</c>, written whole with App Designer's model when the app was not one
    /// (<see cref="AppDesignerDocument.SaveNew(string, string, MlappMetadata?)"/>), copied as App Designer's Save As copies it when
    /// it was; as a plain-text <c>.m</c>, the class and its appendix. The class takes the file's
    /// name, as a class file must, and the App Details name follows it when it was the class's
    /// (<c>PlainTextSerializationModel.save</c>). The document then is that file's.
    /// The App Details go with the app across the formats: into a plain-text file from the package's
    /// metadata, as App Designer's loader carries them when it converts an app (then the oldest
    /// release the file opens in is R2025a, the first with the format), and into a package from the
    /// plain-text details. An image kept beside the app is copied beside the new file when that is
    /// another folder, so the file written names what is there. The document is the new file's only
    /// once it is written: a write that fails leaves the document as it was, so the next Save goes
    /// to the file it came from, in its format.
    /// </summary>
    /// <returns>What the save did, for the status line.</returns>
    /// <exception cref="InvalidOperationException">The design cannot be saved (<see cref="CanSaveAs"/>).</exception>
    /// <exception cref="System.IO.IOException">The file could not be written, or the name is not one a class can have.</exception>
    public string SaveAs(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!CanSaveAs)
        {
            throw new InvalidOperationException(Code is null ? CodeProblem! : SaveRefusal);
        }

        target = System.IO.Path.GetFullPath(target);
        string name = AppDesignerDocument.ClassNameFor(target);
        DesignFileFormat format = FormatOf(target);
        DesignCode code = Code!;

        // What a failed write puts back.
        string classWas = code.ClassName;
        DesignFileFormat formatWas = Format;
        DesignPlainTextDetails? detailsWere = Details;
        string? detailsNameWas = Details?.Name;
        (string newlineWas, bool trailingWas) = (_newline, _trailingNewline);
        string? source = Path;

        string note = string.Empty;
        string saved;
        try
        {
            // The App Details name follows the class's while it still is the class's.
            if (Details is { } named && named.Name == code.ClassName)
            {
                named.Name = name;
            }

            if (format == DesignFileFormat.PlainText)
            {
                DesignPlainTextDetails details = Details ?? DetailsOf(source, name);
                code.ClassName = name;
                Details = details;
                Format = format;
                (_newline, _trailingNewline) = formatWas == format ? (_newline, _trailingNewline) : ("\n", false);
                _renderedText = null;
                saved = CurrentText;
                WriteWhole(target, saved);
            }
            else if (formatWas == DesignFileFormat.Mlapp && source is not null && System.IO.File.Exists(source))
            {
                _renderedText = null;
                (saved, MlappSaveResult result) = AppDesignerDocument.Save(source, target, CurrentText, Design.GroupRecords());
                code.ClassName = name;
                note = AppSaveNote(result);
            }
            else
            {
                Format = format;
                (_newline, _trailingNewline) = ("\n", true);
                _renderedText = null;
                (saved, MlappWriteResult result) = AppDesignerDocument.SaveNew(target, CurrentText, MetadataOf(Details), Design.GroupRecords());
                code.ClassName = name;
                note = AppDesignerDocument.NewFileNote(result);
            }
        }
        catch
        {
            code.ClassName = classWas;
            Format = formatWas;
            Details = detailsWere;
            if (detailsWere is not null && detailsNameWas is not null)
            {
                detailsWere.Name = detailsNameWas;
            }

            (_newline, _trailingNewline) = (newlineWas, trailingWas);
            _renderedText = null;
            throw;
        }

        note += CopyImages(source, target);
        Path = target;
        Text = saved;
        _renderedText = null;
        _savedVersion = _version;
        _textDirty = false;
        OnChanged();
        return $"Saved {Path}.{note}";
    }

    private static string AppSaveNote(MlappSaveResult result) => AppDesignerDocument.StatusNote(result);

    /// <summary>
    /// The details a plain-text file gets for an app that had none: the package's metadata when the
    /// app came from an <c>.mlapp</c> on disk (<c>AppLoader</c>'s conversion: the details kept, the
    /// oldest release R2025a), else a new app's.
    /// </summary>
    private DesignPlainTextDetails DetailsOf(string? source, string name)
    {
        if (Format != DesignFileFormat.Mlapp || source is null || !System.IO.File.Exists(source))
        {
            return DesignPlainTextDetails.NewApp(name, Design.IsResponsive);
        }

        MlappMetadata metadata = JgsMlapp.ReadMetadata(source);
        var details = DesignPlainTextDetails.NewApp(name, Design.IsResponsive);
        details.Author = metadata.Author;
        details.Summary = metadata.Summary;
        details.Description = metadata.Description;
        details.MinimumSupportedMatlabRelease = "R2025a";
        if (metadata.Version.Length > 0)
        {
            details.Version = metadata.Version;
        }

        if (metadata.Uuid.Length > 0)
        {
            details.AppId = metadata.Uuid;
        }

        if (metadata.Name.Length > 0 && metadata.Name != Code!.ClassName)
        {
            details.Name = metadata.Name; // a name of its own; the class's follows the file
        }

        return details;
    }

    /// <summary>The package metadata a plain-text app's details become; null for an app that has none.</summary>
    private static MlappMetadata? MetadataOf(DesignPlainTextDetails? details) => details is null ? null
        : new MlappMetadata(details.Name, details.Author, details.Summary, details.Description, details.Version, details.AppId);

    /// <summary>
    /// Copies the images the design keeps beside the app from the folder of <paramref name="source"/>
    /// to the folder of <paramref name="target"/> when that is another folder, those that are there
    /// and not yet beside the new file; what to add to the status line about it.
    /// </summary>
    private string CopyImages(string? source, string target)
    {
        string? from = source is null ? Runtime.AppFolder : System.IO.Path.GetDirectoryName(source);
        string? to = System.IO.Path.GetDirectoryName(target);
        if (from is null || to is null || string.Equals(System.IO.Path.GetFullPath(from), System.IO.Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        int copied = 0;
        foreach (string relative in DesignPlainText.ImagesOf(Design))
        {
            string image = System.IO.Path.Combine(from, relative);
            string beside = System.IO.Path.Combine(to, relative);
            if (System.IO.File.Exists(image) && !System.IO.File.Exists(beside))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(beside)!);
                System.IO.File.Copy(image, beside);
                copied++;
            }
        }

        return copied == 0 ? string.Empty : $"  {copied} image(s) copied beside it.";
    }

    /// <summary>Writes a whole file, replacing what was at <paramref name="path"/> only once the new text is written; a write that fails leaves nothing behind.</summary>
    private static void WriteWhole(string path, string text)
    {
        string copy = path + ".saving";
        try
        {
            System.IO.File.WriteAllText(copy, text, new System.Text.UTF8Encoding(false));
            System.IO.File.Move(copy, path, overwrite: true);
        }
        catch
        {
            try
            {
                System.IO.File.Delete(copy);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                // the write's own error is the one to report
            }

            throw;
        }
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

        /// <summary>Whether the edit, selecting nothing itself, leaves the selection as it was (an edit of the code, DS5).</summary>
        public virtual bool KeepsSelection => false;

        /// <summary>The picks the edit selects once done (<paramref name="done"/>) or undone, as App Designer's commands set the selection (groups, open item 150); null leaves it to the step.</summary>
        public virtual IReadOnlyList<DesignPick>? PicksAfter(bool done) => null;

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
            document.NoteMoved(Component);
        }

        public override void Undo(DesignDocument document)
        {
            document.Runtime.SetParent(Component, From);
            To.Remove(Component);
            From.Insert(System.Math.Min(_at >= 0 ? _at : Index, From.Children.Count), Component);
            document.NoteMoved(Component);
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
            document.NoteBinding(Component, Name, Before, After);
        }

        public override void Undo(DesignDocument document)
        {
            if (RuntimeBefore is not null)
            {
                document.Runtime.Set(Component, Name, RuntimeBefore);
            }

            Put(Before);
            document.NoteBinding(Component, Name, After, Before);
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

    /// <summary>
    /// A binding put on or taken off a callback (<c>CallbackBindingCollection</c>'s <c>bindingAdded</c>
    /// and <c>bindingRemoved</c>, DS5): App Designer generates the callback's comment again; the
    /// callback's bindings keep the order they were made in; and the first binding of a callback
    /// made with no code gives it its default code (<c>CallbackModel._onGenerateDefaultCode</c>).
    /// </summary>
    /// <param name="component">The component whose property changed.</param>
    /// <param name="designProperty">The property, by the design's name for it.</param>
    /// <param name="before">What the property held before; a callback value is a binding taken off.</param>
    /// <param name="after">What it holds now; a callback value is a binding put on.</param>
    private void NoteBinding(DesignComponent component, string designProperty, DesignValue? before, DesignValue? after)
    {
        if (Code is null)
        {
            return;
        }

        var binding = new DesignCallbackBinding(component, DesignCallbacks.PropertyName(designProperty));
        if (before is CallbackValue was && Code.Find(was.Method) is { } lost)
        {
            lost.BindingsChanged = true;
            lost.Bindings.Remove(binding);
        }

        if (after is CallbackValue now && Code.Find(now.Method) is { } got)
        {
            got.BindingsChanged = true;
            if (!got.Bindings.Contains(binding))
            {
                got.Bindings.Add(binding);
            }

            if (got.Bindings.Count == 1 && got.DefaultBody is [""])
            {
                got.DefaultBody = DesignCallbacks.R2025b.DefaultBody(component, binding.Property);
            }
        }
    }

    /// <summary>Every binding on <paramref name="component"/> and the components inside it noted as put on (<paramref name="added"/>) or taken off: a component added, deleted, or back.</summary>
    private void NoteBindings(DesignComponent component, bool added)
    {
        foreach (DesignComponent c in Walk(component))
        {
            foreach ((string property, DesignValue value) in c.Properties.ToArray())
            {
                if (value is CallbackValue)
                {
                    NoteBinding(c, property, added ? null : value, added ? value : null);
                }
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
            document.NoteBindings(Component, added: true);
        }

        public override void Undo(DesignDocument document)
        {
            document.Runtime.Remove(Component);
            Parent.Remove(Component);
            document.NoteBindings(Component, added: false);
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
            document.NoteBindings(Component, added: false);
        }

        public override void Undo(DesignDocument document)
        {
            Parent.Insert(System.Math.Min(_at >= 0 ? _at : Index, Parent.Children.Count), Component);
            document.Runtime.MakeNew(Component);
            document.NoteBindings(Component, added: true);
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
