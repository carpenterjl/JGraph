using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The makers of U8 (app-building plan): <c>uitable</c>, <c>uitabgroup</c>, <c>uitab</c>,
/// <c>uimenu</c>, <c>uicontextmenu</c>, <c>uitoolbar</c>, <c>uipushtool</c> and
/// <c>uitoggletool</c>. Their argument forms, what each takes as a parent and what it says of
/// anything else are R2025b's, recorded headless (probes <c>u8_forms</c>, <c>u8_parents</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterUiTableAndBarBuiltins(JgsEnvironment env)
    {
        void DefineMaker(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                AutoCallsBare = true,
                BindsAnsAsStatement = false,

                // A table's Data may be a string, which is not the text it holds.
                KeepsStringArguments = true,
            }));

        DefineMaker("uitable", UiTable);
        DefineMaker("uitabgroup", UiTabGroup);
        DefineMaker("uitab", UiTab);
        DefineMaker("uimenu", UiMenu);
        DefineMaker("uicontextmenu", UiContextMenu);
        DefineMaker("uitoolbar", UiToolbar);
        DefineMaker("uipushtool", (args, line, col) => UiTool(toggle: false, args, line, col));
        DefineMaker("uitoggletool", (args, line, col) => UiTool(toggle: true, args, line, col));
    }

    /// <summary>What a maker of U8 takes as a parent, and what it says of what it does not.</summary>
    private sealed record BarMaker(
        string Verb,
        Func<GraphObject, bool> Accepts,
        string InvalidId,
        string InvalidText,
        bool NonHandleIsAFirstArgument,
        Func<GraphObject> Prototype,
        bool TakesPrefixes = true);

    private static readonly BarMaker TableMaker = new("uitable",
        static target => target is IUiContainer and not UiTabGroupModel,
        "MATLAB:uitable:ParentMustBeFigureOrUIContainer", "Parent must be a Figure or any UIContainer", false, static () => new UiTableModel());

    private static readonly BarMaker TabGroupMaker = new("uitabgroup",
        static target => target is IUiContainer and not UiTabGroupModel,
        "MATLAB:uitabgroup:InvalidParent", "Parent must be a figure, uipanel or a uitab", true, static () => new UiTabGroupModel(), TakesPrefixes: false);

    private static readonly BarMaker TabMaker = new("uitab",
        static target => target is UiTabGroupModel,
        "MATLAB:uitab:InvalidParent", "Parent must be a TabGroup", false, static () => new UiTabModel(), TakesPrefixes: false);

    private static readonly BarMaker MenuMaker = new("uimenu",
        static target => target is FigureModel or ContextMenuModel or MenuItemModel,
        "MATLAB:uimenu:InvalidParent", "Parent must be a Figure, UIContextMenu, or another Menu", true, static () => new MenuItemModel());

    private static readonly BarMaker ContextMenuMaker = new("uicontextmenu",
        static target => target is FigureModel,
        "MATLAB:Uicontextmenu:InvalidParent", "Parent must be a Figure", false, static () => new ContextMenuModel());

    private static readonly BarMaker ToolbarMaker = new("uitoolbar",
        static target => target is FigureModel,
        "MATLAB:uitoolbar:InvalidParent", "Parent must be a Figure", true, static () => new UiToolbarModel());

    private static readonly BarMaker PushToolMaker = new("uipushtool",
        static target => target is UiToolbarModel,
        "MATLAB:uipushtool:InvalidParent", "Parent must be a Toolbar", false, static () => new UiToolModel());

    private static readonly BarMaker ToggleToolMaker = new("uitoggletool",
        static target => target is UiToolbarModel,
        "MATLAB:uitoggletool:InvalidParent", "Parent must be a Toolbar", false, static () => new UiToggleToolModel());

    /// <summary>
    /// Whether an object can hold nothing at all — a control, a table, a tool, an axes — which
    /// every maker refuses in the same words, whatever it would have taken instead.
    /// </summary>
    internal static bool HoldsNothing(GraphObject target) =>
        target is not (FigureModel or IUiContainer or MenuItemModel or ContextMenuModel or UiToolbarModel or JgsGraphicsRoot);

    /// <summary>
    /// The object a maker is to make its own in. A value that names nothing is refused here; one
    /// that names what the maker does not take is answered with the refusal owed for it, which
    /// the maker raises once it has found nothing else wrong with the call.
    /// </summary>
    private static (GraphObject? Parent, JgsRuntimeException? Refusal) BarParent(BarMaker maker, JgsValue value, bool positional, int line, int col)
    {
        if (value.Type != JgsType.Number || !JgsHandleRegistry.TryGetOrRoot(value, out JgsHandleEntry? named))
        {
            if (!positional && value.Type == JgsType.Array && value.ArrayLength == 0)
            {
                throw new JgsRuntimeException(line, col,
                    $"{maker.Verb}: an object with no parent cannot be made in this build; name the figure or the container it goes in.");
            }

            throw positional && maker.NonHandleIsAFirstArgument
                ? new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidConvenienceArgHandle",
                    "First argument must be a valid parent, such as a Figure or Panel object.")
                : new JgsRuntimeException(line, col, maker.InvalidId, maker.InvalidText);
        }

        if (maker.Accepts(named.Target))
        {
            return (named.Target, null);
        }

        // What can hold nothing is refused at once, in the same words for every maker.
        if (HoldsNothing(named.Target))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:gbtobjects:Component",
                $"{JgsGraphicsCallbackValues.ClassWord(named.Target)} cannot be a parent.");
        }

        return (null, new JgsRuntimeException(line, col, maker.InvalidId, maker.InvalidText));
    }

    /// <summary>
    /// The arguments of a maker of U8: <c>f()</c>, <c>f(parent)</c>, <c>f(___, Name, Value)</c> and
    /// <c>f(parent, s)</c>. Answers the parent, or null when none was named, and the options in
    /// order with 'Parent' taken out. A parent of the wrong kind is refused last: an odd number of
    /// arguments and a name the class does not have are R2025b's first complaints.
    /// </summary>
    private static (GraphObject? Parent, List<(string Name, JgsValue Value)> Options) BarArguments(
        BarMaker maker, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int start = 0;
        GraphObject? parent = null;
        JgsRuntimeException? refusal = null;
        if (args.Count > 0 && args[0].Type is JgsType.Number or JgsType.Array && !args[0].IsStringArray && !args[0].IsCharMatrix)
        {
            (parent, refusal) = BarParent(maker, args[0], positional: true, line, col);
            start = 1;
        }

        var options = new List<(string Name, JgsValue Value)>();
        if (args.Count - start == 1 && args[start].Type == JgsType.Struct && !args[start].IsStructArray)
        {
            foreach ((string name, JgsValue value) in args[start].AsStruct)
            {
                options.Add((name, value));
            }
        }
        else
        {
            if ((args.Count - start) % 2 != 0)
            {
                throw start == 1
                    ? new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidArgs", "Incorrect number of input arguments.")
                    : new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidConvenienceArgHandle",
                        "First argument must be a valid parent, such as a Figure or Panel object.");
            }

            for (int i = start; i < args.Count; i += 2)
            {
                if (!IsTextScalar(args[i]))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidArgs", "Incorrect number of input arguments.");
                }

                options.Add((TextOf(args[i]), args[i + 1]));
            }
        }

        // A name the class does not answer to, before anything is said of the parent.
        if (refusal is not null && options.Count > 0)
        {
            GraphObject prototype = maker.Prototype();
            foreach ((string name, _) in options)
            {
                if (!name.Equals("Parent", StringComparison.OrdinalIgnoreCase) && !JgsGraphicsProperties.TryFind(prototype, name, out _)
                    && !(maker.TakesPrefixes && JgsGraphicsProperties.NamesOf(prototype).Count(known => known.StartsWith(name, StringComparison.OrdinalIgnoreCase)) == 1))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hg:InvalidProperty",
                        $"Unrecognized property {name} for class {JgsGraphicsCallbackValues.ClassWord(prototype)}.");
                }
            }
        }

        foreach ((string name, JgsValue value) in options)
        {
            if (name.Equals("Parent", StringComparison.OrdinalIgnoreCase))
            {
                (parent, refusal) = BarParent(maker, value, positional: false, line, col);
            }
        }

        if (refusal is not null)
        {
            throw refusal;
        }

        options.RemoveAll(static option => option.Name.Equals("Parent", StringComparison.OrdinalIgnoreCase));
        return (parent, options);
    }

    /// <summary>The figure an object with no parent named is made in: the current one, or a new one.</summary>
    private static FigureModel FigureToMakeIn() =>
        JG.CurrentFigureNumberOrZero > 0 && JG.TryGetFigure(JG.CurrentFigureNumberOrZero, out FigureModel current)
            ? current
            : JG.Figure();

    /// <summary>
    /// Applies a new object's options, R2025b's way: a refused option means no object rather than
    /// one half made, and its refusal is worded bare. A name may be the start of one where the
    /// maker allows it.
    /// </summary>
    private static JgsValue FinishMaking(
        BarMaker maker, GraphObject made, Action undo, List<(string Name, JgsValue Value)> options, int line, int col)
    {
        JgsValue handle = JgsHandleRegistry.For(made);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(made);
        bool fireCreate = false;
        try
        {
            using (JgsGraphicsProperties.CreatingComponent())
            {
                foreach ((string typed, JgsValue value) in options)
                {
                    string name = typed;
                    if (maker.TakesPrefixes && !JgsGraphicsProperties.TryFind(made, typed, out _) && typed.Length > 0)
                    {
                        List<string> starts = [.. JgsGraphicsProperties.NamesOf(made)
                            .Where(known => known.StartsWith(typed, StringComparison.OrdinalIgnoreCase))];
                        if (starts.Count == 1)
                        {
                            name = starts[0];
                        }
                    }

                    JgsGraphicsProperties.Set(entry, name, value, line, col);
                    fireCreate |= name.Equals("CreateFcn", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            using (GraphObjectLifecycle.SuppressNotifications())
            {
                undo();
            }

            throw;
        }

        FigureModel? figure = made as FigureModel ?? FigureOf(made);
        if (figure is not null)
        {
            JG.TouchFigure(figure);
        }

        if (fireCreate)
        {
            JgsCallbackDispatcher.Current?.FireCreateFcn(made);
        }

        return handle;
    }

    // --- uitable -----------------------------------------------------------------------------------

    private static JgsValue UiTable(IReadOnlyList<JgsValue> args, int line, int col)
    {
        // The form R2025b removed: uitable(parent, struct) and its like were the Java table's.
        if (args.Count == 2 && args[1].Type == JgsType.Struct)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:uitable:DeprecatedFunctionWeb", "The 'v0' argument for uitable has been removed.");
        }

        (GraphObject? named, List<(string Name, JgsValue Value)> options) = BarArguments(TableMaker, args, line, col);
        var parent = (IUiContainer)(named ?? FigureToMakeIn());
        FigureModel? figure = parent as FigureModel ?? (parent as UiObject)?.Figure;
        UiTableModel table = figure is { IsUiFigure: true } ? new UiTableModel() : UiTableModel.ForClassicFigure();
        parent.Components.Add(table);
        JgsUiTables.Rebuild(JgsHandleRegistry.EntryFor(table));
        JgsValue handle = FinishMaking(TableMaker, table, () => parent.Components.Remove(table), options, line, col);
        JgsGraphicsProperties.GridMembershipChanged(table);
        return handle;
    }

    // --- tabs --------------------------------------------------------------------------------------

    private static JgsValue UiTabGroup(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (GraphObject? named, List<(string Name, JgsValue Value)> options) = BarArguments(TabGroupMaker, args, line, col);
        return FinishTabGroup((IUiContainer)(named ?? FigureToMakeIn()), options, line, col);
    }

    private static JgsValue FinishTabGroup(IUiContainer parent, List<(string Name, JgsValue Value)> options, int line, int col)
    {
        FigureModel? figure = parent as FigureModel ?? (parent as UiObject)?.Figure;
        UiTabGroupModel group = figure is { IsUiFigure: true } ? UiTabGroupModel.ForUiFigure() : new UiTabGroupModel();
        parent.Components.Add(group);
        JgsValue handle = FinishMaking(TabGroupMaker, group, () => parent.Components.Remove(group), options, line, col);
        JgsGraphicsProperties.GridMembershipChanged(group);
        return handle;
    }

    private static JgsValue UiTab(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (GraphObject? named, List<(string Name, JgsValue Value)> options) = BarArguments(TabMaker, args, line, col);

        // No group named: a new one, in the current figure.
        UiTabGroupModel group = named as UiTabGroupModel
            ?? (UiTabGroupModel)JgsHandleRegistry.Require(FinishTabGroup(FigureToMakeIn(), [], line, col), line, col).Target;
        UiTabModel tab = group.Figure is { IsUiFigure: true } ? UiTabModel.ForUiFigure() : new UiTabModel();
        group.Components.Add(tab);
        return FinishMaking(TabMaker, tab, () => group.Components.Remove(tab), options, line, col);
    }

    // --- menus -------------------------------------------------------------------------------------

    private static JgsValue UiMenu(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (GraphObject? named, List<(string Name, JgsValue Value)> options) = BarArguments(MenuMaker, args, line, col);
        GraphObject parent = named ?? FigureToMakeIn();
        var item = new MenuItemModel();
        GraphObjectCollection<MenuItemModel> siblings = parent switch
        {
            FigureModel figure => figure.Menus,
            ContextMenuModel menu => menu.Items,
            _ => ((MenuItemModel)parent).Items,
        };
        siblings.Add(item);
        return FinishMaking(MenuMaker, item, () => siblings.Remove(item), options, line, col);
    }

    private static JgsValue UiContextMenu(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (GraphObject? named, List<(string Name, JgsValue Value)> options) = BarArguments(ContextMenuMaker, args, line, col);
        var figure = (FigureModel)(named ?? FigureToMakeIn());
        var menu = new ContextMenuModel();
        figure.ContextMenus.Add(menu);
        return FinishMaking(ContextMenuMaker, menu, () => figure.ContextMenus.Remove(menu), options, line, col);
    }

    // --- toolbars ----------------------------------------------------------------------------------

    private static JgsValue UiToolbar(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (GraphObject? named, List<(string Name, JgsValue Value)> options) = BarArguments(ToolbarMaker, args, line, col);
        var figure = (FigureModel)(named ?? FigureToMakeIn());
        var bar = new UiToolbarModel();
        figure.Toolbars.Add(bar);
        return FinishMaking(ToolbarMaker, bar, () => figure.Toolbars.Remove(bar), options, line, col);
    }

    private static JgsValue UiTool(bool toggle, IReadOnlyList<JgsValue> args, int line, int col)
    {
        BarMaker maker = toggle ? ToggleToolMaker : PushToolMaker;
        (GraphObject? named, List<(string Name, JgsValue Value)> options) = BarArguments(maker, args, line, col);

        // No toolbar named: the current figure's own, which is made when it has none.
        if (named is not UiToolbarModel bar)
        {
            FigureModel figure = FigureToMakeIn();
            if (figure.Toolbars.Count > 0)
            {
                bar = figure.Toolbars[0];
            }
            else
            {
                bar = new UiToolbarModel();
                figure.Toolbars.Add(bar);
                JgsHandleRegistry.For(bar);
            }
        }

        UiToolModel tool = toggle ? new UiToggleToolModel() : new UiToolModel();
        bar.Tools.Add(tool);
        return FinishMaking(maker, tool, () => bar.Tools.Remove(tool), options, line, col);
    }

    /// <summary>
    /// A script wrote a toggle tool's <c>State</c>: its <c>OnCallback</c> or <c>OffCallback</c> is
    /// owed, as it is when a person presses the tool, and runs when the queue is next drained.
    /// </summary>
    internal static void ToolStateWritten(UiToolModel tool)
    {
        string name = tool.State ? "OnCallback" : "OffCallback";
        if (JgsHandleRegistry.TryGetEntry(tool, out JgsHandleEntry? entry) && entry.NamedCallbacks.ContainsKey(name))
        {
            ScriptEventQueue.Enqueue(new GraphicsEvent(
                GraphicsEventKind.ComponentUser, tool, Clicked: tool, Action: name,
                Interim: JgsUiEventData.Action(JgsHandleRegistry.For(tool))));
        }
    }
}
