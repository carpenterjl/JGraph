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
/// (<see cref="Preview"/>) and becomes one edit when it ends (<see cref="EndGesture"/>).
/// </para>
/// </remarks>
public sealed class DesignDocument : IDisposable
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
        Runtime = DesignRuntime.Build(Design, path is null ? null : System.IO.Path.GetDirectoryName(path));
    }

    /// <summary>Raised after the design, the selection or the saved state changed: the canvas draws again.</summary>
    public event EventHandler? Changed;

    /// <summary>The app's class, as it was read or last saved.</summary>
    public string Text { get; private set; }

    /// <summary>The <c>.mlapp</c> the app is in; null for one that is not saved.</summary>
    public string? Path { get; }

    /// <summary>How the app's code is laid out, which the save keeps.</summary>
    public DesignCodeStyle Style { get; }

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
    public bool CanUndo => _undo.Count > 0 && _gesture is null;

    /// <summary>Whether there is an edit to redo.</summary>
    public bool CanRedo => _redo.Count > 0 && _gesture is null;

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
            DesignBox box = boxes[i];
            if (box.Shown && box.Box.Contains(point) && box.Clip.Contains(point))
            {
                return box.Component;
            }
        }

        return Design.Figure;
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
    /// move and resize it: not the figure, a tab, or a grid's child (grids are a later stage's).
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
    /// <see cref="Preview"/> moves them from, and what <see cref="EndGesture"/> puts back when the
    /// gesture is cancelled.
    /// </summary>
    public void BeginGesture(IReadOnlyList<DesignComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        EndGesture(commit: false);
        var start = new Dictionary<DesignComponent, Rect2D>(ReferenceEqualityComparer.Instance);
        foreach (DesignComponent component in components)
        {
            if (IsMovable(component) && BoxOf(component) is { } box)
            {
                start[component] = box.Box;
            }
        }

        _gesture = new Gesture(start);
    }

    /// <summary>The boxes the gesture started from.</summary>
    public IReadOnlyDictionary<DesignComponent, Rect2D> GestureStart =>
        _gesture?.Start ?? (IReadOnlyDictionary<DesignComponent, Rect2D>)new Dictionary<DesignComponent, Rect2D>();

    /// <summary>
    /// Shows the gesture's components at <paramref name="boxes"/> (on the surface), on the runtime's
    /// components only: nothing is in the design until the gesture ends.
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
                Rect2D position = Runtime.ToPosition(parent, box);
                Runtime.Set(component, "Position", Row(position));
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
    public void EndGesture(bool commit, DesignComponent? parent = null)
    {
        if (_gesture is not { } gesture)
        {
            return;
        }

        _gesture = null;
        var edits = new List<Edit>();
        foreach ((DesignComponent component, Rect2D start) in gesture.Start)
        {
            DesignComponent from = component.Parent!;
            Rect2D startPosition = Runtime.ToPosition(from, start);

            // Put back first, so that an edit begins from where the component stood.
            Runtime.Set(component, "Position", Row(startPosition));
            if (!commit || !gesture.Last.TryGetValue(component, out Rect2D box))
            {
                continue;
            }

            DesignComponent into = parent is not null && !ReferenceEquals(parent, from) && CanHold(parent, component) ? parent : from;
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

    /// <summary>After an edit: the components it touched are selected, and the canvas draws again.</summary>
    private void Settled(Edit[] step)
    {
        _boxes = null;
        _selection = step.Select(static e => e.Component).Distinct(ReferenceEqualityComparer.Instance).Cast<DesignComponent>().ToArray();
        OnChanged();
    }

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

    /// <summary>A component moved into another container: last among its new siblings, as the last made.</summary>
    private sealed record ParentEdit(DesignComponent Component, DesignComponent From, int Index, DesignComponent To) : Edit(Component)
    {
        public override void Do(DesignDocument document)
        {
            document.Runtime.SetParent(Component, To);
            From.Remove(Component);
            To.Add(Component);
        }

        public override void Undo(DesignDocument document)
        {
            document.Runtime.SetParent(Component, From);
            To.Remove(Component);
            From.Insert(System.Math.Min(Index, From.Children.Count), Component);
        }
    }
}
