using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering.Layout;
using JGraph.Scripting.Jgs;

namespace JGraph.Scripting.Designer;

/// <summary>
/// Where a component shows on the designer's canvas (designer plan DS2): its box on the figure's
/// surface, top-left origin, in pixels; the part of the surface its containers leave it; and
/// whether it shows at all - a component on a tab that is not the one showing does not.
/// </summary>
/// <param name="Component">The component.</param>
/// <param name="Box">Its <c>Position</c>, on the surface.</param>
/// <param name="Clip">What its containers leave of the surface; the box is seen only within it.</param>
/// <param name="Shown">Whether it shows: every tab above it is the one its group shows.</param>
/// <param name="Hidden">Whether its <c>Visible</c> is set off, which the canvas still draws, faded.</param>
public readonly record struct DesignBox(DesignComponent Component, Rect2D Box, Rect2D Clip, bool Shown, bool Hidden);

/// <summary>
/// An app's components as the runtime makes them, for the visual designer's canvas (designer plan
/// DS2): the canvas is the runtime's own renderer, so the design is shown by making its components
/// as <c>createComponents</c> does - each component's generated lines run by an interpreter of the
/// designer's own - in a figure no script can reach. An edit goes the same way, through the
/// component's own set, and what the component keeps is read back (DS1's finding: a component holds
/// what it takes, not always what it was given).
/// </summary>
/// <remarks>
/// <para>
/// Design time is not run time: no callback is wired, and a component whose <c>Visible</c> is off is
/// made visible, as App Designer's canvas shows it (faded, which the canvas draws). The figure is
/// made here, never registered, so it has no number and is in no search; the lines run with the
/// console's session out of reach (<see cref="JgsCallbackDispatcher.Isolate"/>) and with no frame
/// sent from this thread (<see cref="ScriptComponentFrames.HoldOnThisThread"/>).
/// </para>
/// <para>
/// Every call runs on the calling thread; one thread at a time, which the canvas's is.
/// </para>
/// </remarks>
public sealed class DesignRuntime : IDisposable
{
    private const string ValueVariable = "designer__value";

    private readonly JgsEnvironment _environment;
    private readonly Interpreter _interpreter;
    private readonly Dictionary<DesignComponent, GraphObject> _objects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GraphObject, DesignComponent> _components = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _problems = [];
    private UiLayoutResult? _layout;
    private bool _disposed;

    private DesignRuntime(AppDesign design, string? appFolder)
    {
        Design = design;
        AppFolder = appFolder;
        var context = new ScriptContext(new DesignOutput(_problems), static (_, _) => { }, appFolder);
        var globals = new JGraphScriptGlobals(context);
        _environment = JgsBuiltins.CreateGlobals(globals, CancellationToken.None, JgsDialect.Matlab);
        _interpreter = new Interpreter(_environment, CancellationToken.None, hook: null, echo: null, JgsDialect.Matlab)
        {
            Host = globals,
        };

        // The makers that resolve handles through the interpreter (uitreenode among them), as a session has them.
        JgsBuiltins.RegisterEvalBuiltins(_environment, _interpreter, globals);
        _environment.Builtins.Seal();
        Figure = JgsBuiltins.NewUiFigureModel();
        JgsHandleRegistry.Keep(Figure);
        _objects[design.Figure] = Figure;
        _components[Figure] = design.Figure;
    }

    /// <summary>The design the components were made from.</summary>
    public AppDesign Design { get; }

    /// <summary>The app's folder, where its images are; null for an app not yet saved.</summary>
    public string? AppFolder { get; }

    /// <summary>The figure the components are in.</summary>
    public FigureModel Figure { get; }

    /// <summary>Each line the runtime refused, with what it said; empty when the whole design was made.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// Makes <paramref name="design"/>'s components: its figure here, and every other component by
    /// its generated lines, in the order <c>createComponents</c> makes them.
    /// </summary>
    /// <param name="design">The design.</param>
    /// <param name="appFolder">The app's folder, which an image beside the app is read from.</param>
    public static DesignRuntime Build(AppDesign design, string? appFolder)
    {
        ArgumentNullException.ThrowIfNull(design);
        var runtime = new DesignRuntime(design, appFolder);
        runtime.Make();
        return runtime;
    }

    /// <summary>The object made for <paramref name="component"/>, or null when it could not be made.</summary>
    public GraphObject? ObjectOf(DesignComponent component) => _objects.GetValueOrDefault(component);

    /// <summary>The component <paramref name="target"/> was made for, or null for an object the design did not make.</summary>
    public DesignComponent? ComponentOf(GraphObject target) => _components.GetValueOrDefault(target);

    /// <summary>
    /// Sets <paramref name="name"/> on <paramref name="component"/> to <paramref name="value"/> by
    /// the line the generator writes for it, through the component's own set.
    /// </summary>
    /// <returns>Null when the component took it, else what the runtime said.</returns>
    public string? Set(DesignComponent component, string name, DesignValue value)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (!_objects.ContainsKey(component))
        {
            return $"{component.CodeName} was not made.";
        }

        using Scope scope = Enter();
        return Run(DesignCodeWriter.PropertyLine(component, name, value));
    }

    /// <summary>
    /// The numbers <paramref name="component"/>'s <paramref name="name"/> holds, as the runtime reads
    /// it; null when it is not numbers or cannot be read.
    /// </summary>
    public double[]? ReadNumbers(DesignComponent component, string name)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(name);
        if (!_objects.ContainsKey(component))
        {
            return null;
        }

        using Scope scope = Enter();
        return Numbers(component, name);
    }

    /// <summary><see cref="ReadNumbers"/> within a scope already entered.</summary>
    private double[]? Numbers(DesignComponent component, string name)
    {
        if (Run($"{ValueVariable} = app.{component.CodeName}.{name};") is not null
            || !_environment.TryGet(ValueVariable, out JgsValue value))
        {
            return null;
        }

        if (value.Type == JgsType.Number)
        {
            return [value.AsNumber];
        }

        if (value.Type != JgsType.Array || value.IsStringArray || value.IsCharMatrix)
        {
            return null;
        }

        var numbers = new double[value.ArrayLength];
        for (int i = 0; i < numbers.Length; i++)
        {
            JgsValue element = value.ElementAt(i);
            if (element.Type != JgsType.Number)
            {
                return null;
            }

            numbers[i] = element.AsNumber;
        }

        return numbers;
    }

    /// <summary>
    /// <paramref name="component"/>'s <c>Position</c> as the runtime holds it - a pixel rectangle in
    /// its parent, MATLAB's way: from the parent's bottom-left corner, which is pixel 1 - or null for
    /// a component with no place of its own.
    /// </summary>
    public Rect2D? PositionOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!_objects.ContainsKey(component))
        {
            return null;
        }

        using Scope scope = Enter();
        return Position(component);
    }

    /// <summary><see cref="PositionOf"/> within a scope already entered.</summary>
    private Rect2D? Position(DesignComponent component) =>
        component.Class?.Properties.Any(static p => p.Name == "Position") == true
        && Numbers(component, "Position") is [var x, var y, var w, var h]
            ? new Rect2D(x, y, w, h)
            : null;

    /// <summary>
    /// The boxes of every component the canvas shows, back to front: a container before what it
    /// holds, and in each container its axes behind its other components, each kind in the order it
    /// was made. The figure, menus, toolbars and tree nodes have none.
    /// </summary>
    public IReadOnlyList<DesignBox> Boxes()
    {
        using Scope scope = Enter();
        UiLayoutResult layout = Layout();
        var placed = new Dictionary<GraphObject, UiComponentPlacement>(ReferenceEqualityComparer.Instance);
        foreach (UiComponentPlacement placement in layout.Components)
        {
            placed[placement.Component.Source] = placement;
        }

        var boxes = new List<DesignBox>();
        Rect2D surface = InnerOnSurface(Design.Figure);
        foreach (DesignComponent child in PaintOrder(Design.Figure))
        {
            Place(child, surface, null, shown: true);
        }

        return boxes;

        // Each box is where the window draws it: the layout's, which scrolls a scrollable container's
        // content and shows a tab group's one tab. An axes has its own place in a grid's cell, or by
        // its Position in its container's inner area.
        void Place(DesignComponent component, Rect2D clip, UiPanelPlacement? parent, bool shown)
        {
            if (!_objects.TryGetValue(component, out GraphObject? target))
            {
                return;
            }

            bool hidden = component.Get("Visible") is { } visible && IsOff(visible);
            if (target is UiComponentModel && placed.TryGetValue(target, out UiComponentPlacement at))
            {
                boxes.Add(new DesignBox(component, at.Box, at.Clip, shown && at.Visible, hidden));
            }
            else if (target is UiContainerModel container && layout.Find(container) is { } panel)
            {
                boxes.Add(new DesignBox(component, panel.Box, panel.Clip, shown && panel.Visible, hidden));
                foreach (DesignComponent child in PaintOrder(component))
                {
                    Place(child, Intersect(panel.Clip, panel.Viewport ?? panel.Inner), panel, shown && panel.Visible);
                }
            }
            else if (target is AxesModel axes)
            {
                Rect2D? box = parent?.AxesCells is { } cells && cells.TryGetValue(axes, out Rect2D cell) ? cell
                    : Position(component) is { } position && component.Parent is { } holder ? ToSurface(holder, position)
                    : null;
                if (box is { } b)
                {
                    boxes.Add(new DesignBox(component, b, clip, shown, hidden));
                }
            }
        }
    }

    /// <summary>
    /// The area <paramref name="container"/> places its children in, on the surface: the figure's
    /// whole surface, or a container's inner area - where its content is when it scrolls.
    /// </summary>
    public Rect2D InnerOnSurface(DesignComponent container)
    {
        ArgumentNullException.ThrowIfNull(container);
        UiLayoutResult layout = Layout();
        if (ObjectOf(container) is UiContainerModel inner && !ReferenceEquals(container, Design.Figure)
            && layout.Find(inner) is { } placement)
        {
            return placement.Inner;
        }

        return layout.FigureScroll is { } scroll
            ? new Rect2D(-scroll.ScrollX, -scroll.ScrollY, scroll.Content.Width, scroll.Content.Height)
            : new Rect2D(0, 0, Figure.Size.Width, Figure.Size.Height);
    }

    /// <summary>The layout of the components as they stand, worked out again after anything changed them.</summary>
    private UiLayoutResult Layout() => _layout ??= UiLayout.Compute(TakeFrame(), Figure.Size);

    /// <summary>A <c>Position</c> in <paramref name="parent"/> - MATLAB's pixels - as a box on the surface.</summary>
    public Rect2D ToSurface(DesignComponent parent, Rect2D position)
    {
        Rect2D inner = InnerOnSurface(parent);
        return new Rect2D(inner.X + position.X - 1, inner.Y + inner.Height - (position.Y - 1) - position.Height, position.Width, position.Height);
    }

    /// <summary>A box on the surface as a <c>Position</c> in <paramref name="parent"/>.</summary>
    public Rect2D ToPosition(DesignComponent parent, Rect2D box)
    {
        Rect2D inner = InnerOnSurface(parent);
        return new Rect2D(box.X - inner.X + 1, inner.Y + inner.Height - box.Y - box.Height + 1, box.Width, box.Height);
    }

    /// <summary>
    /// What <paramref name="component"/>'s shape allows: the proportions the runtime keeps it in, the
    /// side a slider keeps, and the least size App Designer's canvas resizes it to.
    /// </summary>
    public DesignShape ShapeOf(DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        GraphObject? target = ObjectOf(component);
        Size2D minimum = target is UiTabGroupModel or UiButtonGroupModel ? DesignGeometry.GroupMinimum : DesignShape.Free.Minimum;
        return target switch
        {
            UiSliderModel slider => new DesignShape(null, slider.Vertical, !slider.Vertical, minimum),
            UiObject shaped when JgsGraphicsProperties.AspectRatioOf(shaped) is { } ratio => new DesignShape(ratio, false, false, minimum),
            _ => DesignShape.Free with { Minimum = minimum },
        };
    }

    /// <summary>Moves <paramref name="component"/> into <paramref name="parent"/> through its <c>Parent</c>; null when it went.</summary>
    public string? SetParent(DesignComponent component, DesignComponent parent)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(parent);
        if (!_objects.ContainsKey(component) || !_objects.ContainsKey(parent))
        {
            return $"{component.CodeName} or {parent.CodeName} was not made.";
        }

        using Scope scope = Enter();
        return Run($"app.{component.CodeName}.Parent = app.{parent.CodeName};");
    }

    /// <summary>
    /// Shows <paramref name="tab"/> in its group on the canvas, as a click on its heading in App
    /// Designer does. What a group shows at design time is the canvas's, not the design's.
    /// </summary>
    public void ShowTab(DesignComponent tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (ObjectOf(tab) is UiTabModel { Group: { } group } shown)
        {
            group.Select(shown);
            _layout = null;
        }
    }

    /// <summary>Takes a frame of the components, as a flush takes one: for the canvas to draw.</summary>
    public UiFrame TakeFrame()
    {
        // A container told it was resized runs no callback of the console's session.
        using Scope scope = Enter();
        return ScriptComponentFrames.TakeFrame(Figure);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            JgsHandleRegistry.Release(Figure);
        }
    }

    // --- making ----------------------------------------------------------------------------------

    private void Make()
    {
        using Scope scope = Enter();
        _environment.Declare("pathToMLAPP", JgsValue.Str(AppFolder ?? string.Empty));
        foreach (DesignComponent component in Design.Components)
        {
            if (!ReferenceEquals(component, Design.Figure))
            {
                string creation = DesignCodeWriter.ComponentCode(component)[0];
                if (Run(creation) is { } refused)
                {
                    _problems.Add($"{component.CodeName}: {refused}");
                    continue;
                }

                if (Made(component) is not { } made)
                {
                    _problems.Add($"{component.CodeName}: its creation made nothing.");
                    continue;
                }

                _objects[component] = made;
                _components[made] = component;
            }

            foreach ((string name, DesignValue value) in DesignCodeWriter.WrittenProperties(component, DesignTheme.Light))
            {
                // Design time: no callback is wired, and a hidden component is still shown. A check
                // box tree's checked nodes name nodes made after it, so they wait for them.
                if (value is CallbackValue || name == "Visible" || IsEnd(component, name))
                {
                    continue;
                }

                if (Run(DesignCodeWriter.PropertyLine(component, name, value)) is { } refused)
                {
                    _problems.Add($"{component.CodeName}.{name}: {refused}");
                }
            }
        }

        foreach (DesignComponent component in Design.Components)
        {
            foreach ((string name, DesignValue value) in DesignCodeWriter.WrittenProperties(component, DesignTheme.Light))
            {
                if (IsEnd(component, name) && _objects.ContainsKey(component)
                    && Run(DesignCodeWriter.PropertyLine(component, name, value)) is { } refused)
                {
                    _problems.Add($"{component.CodeName}.{name}: {refused}");
                }
            }
        }
    }

    private static bool IsEnd(DesignComponent component, string name) =>
        component.Type == "matlab.ui.container.CheckBoxTree" && name == "CheckedNodes";

    /// <summary>The object the creation line put in <c>app</c> under the component's name.</summary>
    private GraphObject? Made(DesignComponent component) =>
        _environment.TryGet("app", out JgsValue app) && app.Type == JgsType.Struct && !app.IsStructArray
        && app.AsStruct.TryGetValue(component.CodeName, out JgsValue? handle)
        && JgsHandleRegistry.TryGet(handle, out JgsHandleEntry? entry)
            ? entry.Target
            : null;

    /// <summary>Runs one statement; null when it ran, else what stopped it.</summary>
    private string? Run(string line)
    {
        _layout = null;
        try
        {
            _interpreter.BeginStatement(CancellationToken.None);
            _interpreter.Run(Parser.Parse(line, string.Empty, JgsDialect.Matlab));
            return null;
        }
        catch (JgsException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Enters the designer's isolation, and binds <c>app</c> to the components made so far by
    /// handles minted now: a <c>clear</c> at the console forgets every handle, the designer's too.
    /// </summary>
    private Scope Enter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var scope = new Scope(JgsCallbackDispatcher.Isolate(), ScriptComponentFrames.HoldOnThisThread());
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach ((DesignComponent component, GraphObject target) in _objects)
        {
            fields[component.CodeName] = JgsHandleRegistry.For(target);
        }

        _environment.Declare("app", JgsValue.Struct(fields));
        return scope;
    }

    private sealed class Scope(IDisposable isolation, IDisposable hold) : IDisposable
    {
        public void Dispose()
        {
            hold.Dispose();
            isolation.Dispose();
        }
    }

    // --- geometry --------------------------------------------------------------------------------

    /// <summary>A container's children in painting order: its axes behind its other components, each kind as made.</summary>
    private IEnumerable<DesignComponent> PaintOrder(DesignComponent container) =>
        container.Children.Where(c => ObjectOf(c) is AxesModel)
            .Concat(container.Children.Where(c => ObjectOf(c) is not AxesModel));

    private static Rect2D Intersect(Rect2D a, Rect2D b)
    {
        double left = System.Math.Max(a.X, b.X);
        double top = System.Math.Max(a.Y, b.Y);
        double right = System.Math.Min(a.X + a.Width, b.X + b.Width);
        double bottom = System.Math.Min(a.Y + a.Height, b.Y + b.Height);
        return right <= left || bottom <= top ? new Rect2D(left, top, 0, 0) : new Rect2D(left, top, right - left, bottom - top);
    }

    private static bool IsOff(DesignValue value) => value switch
    {
        OnOffValue onOff => !onOff.IsOn,
        CharValue text => string.Equals(text.Text, "off", StringComparison.OrdinalIgnoreCase),
        StringValue text => string.Equals(text.Text, "off", StringComparison.OrdinalIgnoreCase),
        NumericValue { Data: [var d] } => d == 0,
        _ => false,
    };

    /// <summary>What the designer's interpreter writes: kept as problems, never shown at the console.</summary>
    private sealed class DesignOutput(List<string> problems) : IScriptOutput
    {
        public void Write(string text)
        {
        }

        public void WriteLine(string text)
        {
        }

        public void WriteError(string text) => problems.Add(text);
    }
}
