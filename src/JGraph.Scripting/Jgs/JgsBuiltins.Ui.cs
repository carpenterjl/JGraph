using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The app-building verbs (app-building plan). U1 brought <c>uicontrol</c>; U2 brings the containers
/// and the placement verbs: <c>uipanel</c>, <c>uifigure</c>, <c>getpixelposition</c>,
/// <c>setpixelposition</c>, <c>movegui</c>, <c>uistack</c> and <c>allchild</c> — R2025b's argument
/// forms and refusals, measured in <c>tools/matlab-checklist/ui-probes/u2</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterUiBuiltins(JgsEnvironment env)
    {
        // AutoCallsBare, because the documented spelling is the bare name on an assignment's right
        // side — h = uicontrol — and a bare name in expression position is otherwise the function.
        // A custom component named as the parent is its area's handle (U10).
        void DefineMaker(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => body(JgsComponentContainers.AsHandles(args), line, col))
            {
                AutoCallsBare = true,
                BindsAnsAsStatement = false,
            }));

        void DefineQuiet(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        DefineMaker("uicontrol", UiControl);
        DefineMaker("uipanel", UiPanel);
        DefineMaker("uibuttongroup", UiButtonGroup);
        DefineMaker("uifigure", UiFigure);
        RegisterUiTextBuiltins(env);
        env.Builtins.Register("getpixelposition", JgsValue.Function(new BuiltinFunction("getpixelposition",
            (args, line, col) => GetPixelPosition(JgsComponentContainers.AsHandles(args), line, col))));
        DefineQuiet("setpixelposition", (args, line, col) => SetPixelPosition(JgsComponentContainers.AsHandles(args), line, col));
        DefineQuiet("movegui", (args, line, col) => MoveGui(JgsComponentContainers.AsHandles(args), line, col));
        DefineQuiet("uistack", (args, line, col) => UiStack(JgsComponentContainers.AsHandles(args), line, col));
        env.Builtins.Register("allchild", JgsValue.Function(new BuiltinFunction("allchild", AllChild)));

        // A figure whose handle is hidden cannot be the current one (U2).
        JG.CanBeCurrent = static figure =>
            !JgsHandleRegistry.TryGetEntry(figure, out JgsHandleEntry? entry) || entry.HandleVisible;
    }

    // --- making components ------------------------------------------------------------------------

    /// <summary>
    /// The arguments every component maker shares: <c>f()</c>, <c>f(parent)</c>,
    /// <c>f(___, Name, Value)</c> and <c>f(parent, s)</c> with a struct of properties — R2025b's
    /// forms, and its refusals for the others. Answers the options in order with 'Parent' taken out.
    /// </summary>
    private static (IUiContainer Parent, List<(string Name, JgsValue Value)> Options) ComponentArguments(
        IReadOnlyList<JgsValue> args, bool focusForm, int line, int col, bool control = false)
    {
        int start = 0;
        IUiContainer? parent = null;
        bool positionalParent = false;
        if (args.Count > 0 && args[0].Type is JgsType.Number or JgsType.Array && !args[0].IsStringArray)
        {
            parent = ComponentParent(args[0], focusForm && args.Count > 1, positional: true, line, col, control);
            positionalParent = true;
            start = 1;
        }

        // The options: name-value pairs, or one struct whose fields are the names.
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
                throw positionalParent
                    ? new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidArgs",
                        "Incorrect number of input arguments.")
                    : new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidConvenienceArgHandle",
                        "First argument must be a valid parent, such as a Figure or Panel object.");
            }

            for (int i = start; i < args.Count; i += 2)
            {
                if (!IsTextScalar(args[i]))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidArgs",
                        "Incorrect number of input arguments.");
                }

                options.Add((TextOf(args[i]), args[i + 1]));
            }
        }

        // 'Parent' among the options says where the component goes before anything else is set.
        foreach ((string name, JgsValue value) in options)
        {
            if (name.Equals("Parent", StringComparison.OrdinalIgnoreCase))
            {
                // 'Parent', [] makes one that belongs to nothing yet (open item 48).
                parent = JgsDetachedComponents.MeansNoParent(value) && !control
                    ? JgsDetachedComponents.Holder
                    : ComponentParent(value, pairsWithFocus: false, positional: false, line, col, control);
            }
        }

        options.RemoveAll(static option => option.Name.Equals("Parent", StringComparison.OrdinalIgnoreCase));
        parent ??= JG.CurrentFigureNumberOrZero > 0 && JG.TryGetFigure(JG.CurrentFigureNumberOrZero, out FigureModel current)
            ? current
            : JG.Figure();
        return (parent, options);
    }

    /// <summary>
    /// Adds a new component to its parent and applies the options, R2025b's way: a refused option
    /// means no component rather than one half made, and its refusal is worded bare.
    /// </summary>
    private static JgsValue AddComponent(
        UiObject component, IUiContainer parent, List<(string Name, JgsValue Value)> options, int line, int col)
    {
        JgsComponentContainers.RequireOpen(parent, component, line, col); // U10: only while a custom component's setup runs
        parent.Components.Add(component);
        JgsValue handle = JgsHandleRegistry.For(component);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(component);
        bool fireCreate = false;
        try
        {
            using (JgsGraphicsProperties.CreatingComponent())
            {
                foreach ((string name, JgsValue value) in options)
                {
                    JgsGraphicsProperties.Set(entry, name, value, line, col);
                    fireCreate |= name.Equals("CreateFcn", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            using (GraphObjectLifecycle.SuppressNotifications())
            {
                parent.Components.Remove(component);
            }

            throw;
        }

        // A button made in a group joins it once its options are in (U3).
        if (component is UiControlModel button && parent is UiButtonGroupModel group)
        {
            group.Added(button);
        }

        // Made in a grid, it takes the next cell (U5).
        JgsGraphicsProperties.GridMembershipChanged(component);

        if (component.Figure is { } figure)
        {
            JG.TouchFigure(figure);
        }

        if (fireCreate)
        {
            JgsCallbackDispatcher.Current?.FireCreateFcn(component);
        }

        return handle;
    }

    /// <summary><c>uicontrol</c> in R2025b's forms (measured in U1); its parent is a figure or a container.</summary>
    private static JgsValue UiControl(IReadOnlyList<JgsValue> args, int line, int col)
    {
        // uicontrol(h) gives an existing control the keyboard and answers it (U3).
        if (args.Count == 1 && args[0].Type == JgsType.Number
            && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? named) && named.Target is UiControlModel existing)
        {
            existing.RequestFocus();
            return JgsHandleRegistry.For(existing);
        }

        (IUiContainer parent, List<(string Name, JgsValue Value)> options) = ComponentArguments(args, focusForm: true, line, col, control: true);
        if (parent is UiGridLayoutModel)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:uicontrol:InvalidParent", "Parent must be a Figure or UITab or any UIContainer");
        }

        if (parent is UiButtonGroupModel mixed && mixed.Components.Any(static other => other is UiRadioButtonModel or UiToggleButtonModel))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:gbtobjects:MutualExclusivityViolation",
                "Mutual exclusivity violated for ButtonGroup.\nA UIControl can only be parented to a ButtonGroup with UIControl.");
        }

        return AddComponent(new UiControlModel(), parent, options, line, col);
    }

    /// <summary>
    /// <c>uipanel</c> in R2025b's forms. A panel made in a <c>uifigure</c> starts with that kind's
    /// defaults — pixels, a 260 by 221 box, 12-pixel Helvetica, children that resize with it.
    /// </summary>
    private static JgsValue UiPanel(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (IUiContainer parent, List<(string Name, JgsValue Value)> options) = ComponentArguments(args, focusForm: false, line, col);
        FigureModel? figure = parent as FigureModel ?? (parent as UiObject)?.Figure;
        UiPanelModel panel = figure is { IsUiFigure: true } ? UiPanelModel.ForUiFigure() : new UiPanelModel();
        return AddComponent(panel, parent, options, line, col);
    }

    /// <summary>
    /// <c>uibuttongroup</c> in R2025b's forms (U3): a panel that keeps one of its radio buttons and
    /// toggle buttons selected. One made in a <c>uifigure</c> starts with that kind's defaults.
    /// </summary>
    private static JgsValue UiButtonGroup(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (IUiContainer parent, List<(string Name, JgsValue Value)> options) = ComponentArguments(args, focusForm: false, line, col);
        FigureModel? figure = parent as FigureModel ?? (parent as UiObject)?.Figure;
        UiButtonGroupModel group = figure is { IsUiFigure: true } ? UiButtonGroupModel.ForUiFigure() : new UiButtonGroupModel();
        return AddComponent(group, parent, options, line, col);
    }

    /// <summary>
    /// The figure or container a component is made in, from a handle a script named, with R2025b's
    /// refusals for what is not a handle and for what cannot hold a component.
    /// </summary>
    private static IUiContainer ComponentParent(
        JgsValue value, bool pairsWithFocus, bool positional, int line, int col, bool control = false)
    {
        if (!JgsHandleRegistry.TryGet(value, out JgsHandleEntry? named))
        {
            throw positional
                ? new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidConvenienceArgHandle",
                    "First argument must be a valid parent, such as a Figure or Panel object.")
                : new JgsRuntimeException(line, col, "MATLAB:hg:dt_conv:Matrix_to_HObject:BadHandle", "Value must be a handle.");
        }

        return named.Target switch
        {
            // A tab group holds tabs and nothing else, and a menu or a toolbar no component (U8).
            UiTabGroupModel or MenuItemModel or ContextMenuModel or UiToolbarModel => throw (control
                ? (JgsRuntimeException)new JgsRuntimeException(line, col, "MATLAB:uicontrol:InvalidParent", "Parent must be a Figure or UITab or any UIContainer")
                : new JgsRuntimeException(line, col, "MATLAB:uicontainer:InvalidParentFigure", "Parent must be a Figure or any UIContainer")),
            IUiContainer holder => holder,
            UiObject when pairsWithFocus => throw new JgsRuntimeException(line, col,
                "MATLAB:hgbuiltins:object_creation:CannotSpecifyPVPairsWithNonParentConvenienceArg",
                "Invalid input combination. Parameter-value pairs must be specified with a valid parent, such as a Figure or Panel object."),
            _ => throw new JgsRuntimeException(line, col, "MATLAB:gbtobjects:Component",
                $"{JgsGraphicsCallbackValues.ClassWord(named.Target)} cannot be a parent."),
        };
    }

    // --- uifigure -----------------------------------------------------------------------------------

    /// <summary>R2025b's figure grey, 245/255 — what a <c>uifigure</c>'s <c>Color</c> starts as.</summary>
    private static readonly UiColor UiFigureColor = new(245 / 255.0, 245 / 255.0, 245 / 255.0);

    /// <summary>
    /// <c>uifigure</c>, <c>uifigure(Name, Value)</c> and <c>uifigure(s)</c>: a figure with the
    /// app-building defaults (U2). It has no figure number, its handle is hidden — so it is never
    /// <c>gcf</c> and <c>close all</c> leaves it — and its window is plain.
    /// </summary>
    private static JgsValue UiFigure(IReadOnlyList<JgsValue> args, int line, int col)
    {
        var options = new List<(string Name, JgsValue Value)>();
        if (args.Count == 1 && args[0].Type == JgsType.Struct && !args[0].IsStructArray)
        {
            foreach ((string name, JgsValue value) in args[0].AsStruct)
            {
                options.Add((name, value));
            }
        }
        else
        {
            if (args.Count > 0 && !IsTextScalar(args[0]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:ui:uifigure:BadInputArgument",
                    "Illegal first argument. To set focus on a figure, use the focus function instead.");
            }

            if (args.Count % 2 != 0 || Enumerable.Range(0, args.Count / 2).Any(i => !IsTextScalar(args[2 * i])))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:HandleCtor:InvalidArgument",
                    "Specify inputs as one or more name-value arguments, a scalar structure with property names as field names, or a cell array of property names followed by a cell array of values.");
            }

            for (int i = 0; i < args.Count; i += 2)
            {
                options.Add((TextOf(args[i]), args[i + 1]));
            }
        }

        var figure = new FigureModel
        {
            IsUiFigure = true,
            IntegerHandle = false,
            MenuBar = false,
            AutoResizeChildren = true,
            NumberTitle = false,
            ToolBar = FigureToolBarMode.None,
            Background = UiFigureColor.ToColor(),
            Size = new Size2D(560, 420),
            Name = string.Empty,
        };

        int number = JG.RegisterHiddenFigure(figure);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(figure);
        entry.HandleVisibility = "off";
        try
        {
            foreach ((string name, JgsValue value) in options)
            {
                if (!JgsGraphicsProperties.TryFind(figure, name, out _))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:handle_graphics:invalidCtorProperty",
                        $"Can't find {name} property on the Figure class.");
                }

                JgsGraphicsProperties.Set(entry, name, value, line, col);
            }
        }
        catch
        {
            using (GraphObjectLifecycle.SuppressNotifications())
            {
                JG.CloseFigure(number);
            }

            throw;
        }

        // The size it was made with is the size its first resize is measured from.
        JgsGraphicsProperties.RememberSize(figure);

        // One made with its handle visible is the current figure, as in R2025b (open item 55): gcf,
        // the root's CurrentFigure, gca and a bare plot reach it until another figure is raised.
        if (entry.HandleVisible)
        {
            JG.Figure(number);
        }

        // A CreateFcn among the options runs once the others are set (open item 80, measured).
        JgsCallbackDispatcher.Current?.FireCreateFcn(figure);
        return JgsHandleRegistry.For(figure);
    }

    // --- pixels -------------------------------------------------------------------------------------

    /// <summary>
    /// An object's rectangle in pixels within its parent — or, with <paramref name="recursive"/>,
    /// within its figure, which adds where each container's inner area begins.
    /// </summary>
    internal static Rect2D PixelPositionOf(GraphObject target, bool recursive)
    {
        Rect2D box = target switch
        {
            FigureModel figure => JgsGraphicsProperties.FigurePixels(figure),
            UiObject component => component.PixelPosition(),
            AxesModel axes => JgsGraphicsProperties.AxesPixels(axes, inner: true),
            _ => new Rect2D(0, 0, 0, 0),
        };

        if (!recursive || target is FigureModel)
        {
            return box;
        }

        for (GraphObject? up = JgsGraphicsProperties.ParentOf(target); up is UiContainerModel container;
             up = JgsGraphicsProperties.ParentOf(up))
        {
            Rect2D inner = container.InnerPixelRect();
            box = new Rect2D(box.X + inner.X - 1, box.Y + inner.Y - 1, box.Width, box.Height);
        }

        return box;
    }

    private static JgsValue GetPixelPosition(IReadOnlyList<JgsValue> args, int line, int col)
    {
        MatlabArity(args, 1, 2, line, col);
        if (args[0].Type != JgsType.Number || !JgsHandleRegistry.TryGetOrRoot(args[0], out JgsHandleEntry? entry))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:getpixelposition:InvalidHandle",
                "First argument must be a valid scalar graphics object handle.");
        }

        bool recursive = args.Count > 1 && args[1].Type is JgsType.Bool or JgsType.Number && args[1].IsTruthy;
        Rect2D box = PixelPositionOf(entry.Target, recursive);
        return JgsGraphicsProperties.Row(box.X, box.Y, box.Width, box.Height);
    }

    private static JgsValue SetPixelPosition(IReadOnlyList<JgsValue> args, int line, int col)
    {
        MatlabArity(args, 2, 3, line, col);
        if (args[0].Type != JgsType.Number || !JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:setpixelposition:InvalidHandle",
                "First argument must be a valid graphics object handle.");
        }

        string kind = ClassOf(args[1], JgsDialect.Matlab);
        double[] box = kind != "logical" && JgsNumericClasses.Parse(kind) is not null ? ToDoubles("setpixelposition", args[1], line, col) : [];
        if (box.Length != 4)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:setpixelposition:InvalidPosition",
                "Second argument \"position\" must be a 4 element vector.");
        }

        bool recursive = args.Count > 2 && args[2].IsTruthy;
        GraphObject target = entry.Target;
        if (recursive && JgsGraphicsProperties.ParentOf(target) is UiContainerModel parent)
        {
            // Where the parent's inner area begins in the figure is what the child is measured from.
            Rect2D origin = PixelPositionOf(parent, recursive: true);
            Thickness inset = parent.Insets();
            box[0] -= origin.X + inset.Left - 1;
            box[1] -= origin.Y + inset.Bottom - 1;
        }

        // Written through the property, in pixels, with the object's own Units put back afterwards
        // — so the refusals are Position's own.
        JgsValue units = JgsGraphicsProperties.Get(entry, "Units", line, col);
        JgsGraphicsProperties.Set(entry, "Units", JgsValue.Str("pixels"), line, col);
        try
        {
            JgsGraphicsProperties.Set(entry, "Position", JgsGraphicsProperties.Row(box), line, col);
        }
        finally
        {
            JgsGraphicsProperties.Set(entry, "Units", units, line, col);
        }

        return JgsValue.Null;
    }

    /// <summary>R2025b's own words for a call with too few or too many arguments.</summary>
    private static void MatlabArity(IReadOnlyList<JgsValue> args, int min, int max, int line, int col)
    {
        if (args.Count < min)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > max)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }
    }

    // --- movegui ------------------------------------------------------------------------------------

    private static readonly string[] ScreenLocations =
        ["north", "south", "east", "west", "northeast", "southeast", "northwest", "southwest", "center", "onscreen"];

    /// <summary>
    /// <c>movegui(h, position)</c>: moves a figure to a named place on its screen, to an offset from
    /// the screen's edges, or back on screen. The arithmetic is done on the whole window. With a
    /// window that is its real bounds; without one it is R2025b's own estimate of a window's border
    /// and header, so a headless run lands where R2025b's does.
    /// </summary>
    private static JgsValue MoveGui(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        FigureModel? figure = null;
        string? word = null;
        double[]? offset = null;
        foreach (JgsValue argument in args)
        {
            if (argument.Type == JgsType.Number && JgsHandleRegistry.TryGet(argument, out JgsHandleEntry? entry))
            {
                figure = FigureOf(entry.Target) ?? throw new JgsRuntimeException(line, col, "MATLAB:movegui:InvalidHandle",
                    "handle of figure or descendent required");
            }
            else if (IsTextScalar(argument))
            {
                word = TextOf(argument);
                if (!ScreenLocations.Contains(word, StringComparer.Ordinal))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:movegui:UnrecognizedPosition", "unrecognized position");
                }
            }
            else if (argument.Type == JgsType.Array && !argument.IsStringArray && argument.ArrayLength == 2
                     && JgsNumericClasses.Parse(ClassOf(argument, JgsDialect.Matlab)) is not null)
            {
                offset = ToDoubles("movegui", argument, line, col);
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:movegio:UnrecognizedInput", "unrecognized input argument");
            }
        }

        figure ??= FigureOf(JgsGraphicsCallbackState.CallbackObject) ?? JG.CurrentFigure;
        word = offset is null ? word ?? "onscreen" : null;

        // The window around the drawable area: how far its corner stands out, and how much it adds.
        Rect2D inner = JgsGraphicsProperties.FigurePixels(figure);
        double border;
        double widthAdjustment;
        double heightAdjustment;
        if (ScriptGraphicsCallbacks.WindowBoundsProvider?.Invoke(figure) is { } window)
        {
            border = 0; // a shown figure's Position is its window's corner here
            widthAdjustment = System.Math.Max(0, window.Width - inner.Width);
            heightAdjustment = System.Math.Max(0, window.Height - inner.Height);
        }
        else
        {
            border = 8;
            widthAdjustment = 16;
            heightAdjustment = 8 + 31 + (figure.ToolBar == FigureToolBarMode.Figure ? 27 : 0);
        }

        double left = inner.X - border;
        double bottom = inner.Y - border;
        double width = inner.Width + widthAdjustment;
        double height = inner.Height + heightAdjustment;

        // The screen the figure is on: the first monitor one of its corners lies strictly inside.
        IReadOnlyList<Rect2D> monitors = UiScreen.Monitors;
        Rect2D screen = monitors[0];
        foreach (Rect2D monitor in monitors)
        {
            bool Inside(double x, double y) =>
                x > monitor.X && x < monitor.X + monitor.Width && y > monitor.Y && y < monitor.Y + monitor.Height;
            if (Inside(left, bottom) || Inside(left, bottom + height) || Inside(left + width, bottom + height) || Inside(left + width, bottom))
            {
                screen = monitor;
                break;
            }
        }

        width = System.Math.Min(width, screen.Width);
        height = System.Math.Min(height, screen.Height);
        double spareWidth = screen.Width - width;
        double spareHeight = screen.Height - height;
        double x, y;
        if (offset is not null)
        {
            x = (offset[0] < 0 ? spareWidth + offset[0] : offset[0]) + screen.X;
            y = (offset[1] < 0 ? spareHeight + offset[1] : offset[1]) + screen.Y;
        }
        else if (word == "onscreen")
        {
            double margin = 30 + border;
            x = left;
            y = bottom;
            if (x > screen.X + spareWidth - margin)
            {
                x = screen.X + spareWidth - margin;
            }

            if (y < screen.Y + margin)
            {
                y = screen.Y + margin;
            }

            if (x < screen.X + margin)
            {
                x = screen.X + margin;
            }

            if (y > screen.Y + spareHeight - margin)
            {
                y = screen.Y + spareHeight - margin;
            }
        }
        else
        {
            (x, y) = word switch
            {
                "north" => (spareWidth / 2, spareHeight),
                "south" => (spareWidth / 2, 0.0),
                "east" => (spareWidth, spareHeight / 2),
                "west" => (0.0, spareHeight / 2),
                "northeast" => (spareWidth, spareHeight),
                "southeast" => (spareWidth, 0.0),
                "northwest" => (0.0, spareHeight),
                "southwest" => (0.0, 0.0),
                _ => (spareWidth / 2, spareHeight / 2),
            };
            x += screen.X;
            y += screen.Y;
        }

        JgsGraphicsProperties.SetFigurePixels(figure, new Rect2D(
            x + border, y + border, width - widthAdjustment, height - heightAdjustment));
        return JgsValue.Null;
    }

    // --- stacking -----------------------------------------------------------------------------------

    /// <summary>
    /// <c>uistack(h, how, step)</c>: moves objects up or down the order their parent draws them in.
    /// Components and axes are two stacks — a component is always in front of an axes — so each
    /// handle moves among its own kind.
    /// </summary>
    private static JgsValue UiStack(IReadOnlyList<JgsValue> args, int line, int col)
    {
        MatlabArity(args, 1, 3, line, col);
        var targets = new List<GraphObject>();
        int count = args[0].Type == JgsType.Array ? args[0].ArrayLength : 1;
        for (int i = 0; i < count; i++)
        {
            JgsValue element = args[0].Type == JgsType.Array ? args[0].ElementAt(i) : args[0];
            if (element.Type != JgsType.Number || !JgsHandleRegistry.TryGet(element, out JgsHandleEntry? entry))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:uistack:PassedInvalidHandles", "Invalid object specified for uistack.");
            }

            targets.Add(entry.Target);
        }

        string how = args.Count > 1 ? TextOf(args[1]) : "up";
        double step = 1;
        if (args.Count > 2)
        {
            step = NumOf("uistack", args[2], line, col);
            if (step != System.Math.Floor(step))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:NonIntegerInput", "Size inputs must be integers.");
            }

            step = System.Math.Max(0, step);
        }

        var parents = targets.Select(JgsGraphicsProperties.ParentOf).Distinct(ReferenceEqualityComparer.Instance).ToList();
        if (parents.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:uistack:ParentMustBeSame",
                "Objects specified for uistack must have the same parent.");
        }

        if (how is not ("up" or "down" or "top" or "bottom"))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:uistack:InvalidStackOption",
                "Stacking location must be 'up', 'down', 'top', or 'bottom'.");
        }

        // A figure has no stack to move in: R2025b raises its window, and here nothing changes.
        if (parents.Count == 0 || parents[0] is null or JgsGraphicsRoot)
        {
            return JgsValue.Null;
        }

        using (GraphObjectLifecycle.SuppressNotifications())
        {
            switch (parents[0])
            {
                // A tab group's tabs are held in the order Children lists them (U8).
                case UiTabGroupModel pages:
                    RestackKind(pages.Components, [.. targets.OfType<UiObject>()], how, (int)step, frontFirst: false);
                    break;
                case IUiContainer holder:
                    RestackKind(holder.Components, [.. targets.OfType<UiObject>()], how, (int)step);
                    if (JgsGraphicsProperties.AxesHolder((GraphObject)holder) is { } figure)
                    {
                        RestackKind(figure.Axes, [.. targets.OfType<AxesModel>()], how, (int)step,
                            axes => ReferenceEquals(JgsGraphicsProperties.ParentOf(axes), holder));
                    }

                    break;
                case AxesModel axes:
                    RestackKind(axes.Plots, [.. targets.OfType<PlotObject>()], how, (int)step);
                    break;
            }
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// R2025b's restacking of one kind of child (its <c>uistack.m</c>): the members of the collection
    /// that <paramref name="among"/> admits are listed front first, the moving ones are shifted, and
    /// the result is written back into the slots those members held.
    /// </summary>
    private static void RestackKind<T>(
        GraphObjectCollection<T> collection, List<T> moving, string how, int step, Func<T, bool>? among = null, bool frontFirst = true)
        where T : GraphObject
    {
        if (moving.Count == 0)
        {
            return;
        }

        // Front first, as Children lists them: the collection is held back to front.
        List<T> order = [.. collection.Where(item => among?.Invoke(item) ?? true)];
        if (frontFirst)
        {
            order.Reverse();
        }

        if (order.Count <= moving.Count)
        {
            return;
        }

        switch (how)
        {
            case "top":
                order.RemoveAll(moving.Contains);
                order.InsertRange(0, moving);
                break;
            case "bottom":
                order.RemoveAll(moving.Contains);
                order.AddRange(moving);
                break;
            case "up":
                foreach (T item in order.Where(moving.Contains).ToList())
                {
                    int at = order.IndexOf(item);
                    order.RemoveAt(at);
                    order.Insert(System.Math.Max(0, at - step), item);
                }

                break;
            default:
                foreach (T item in order.Where(moving.Contains).Reverse().ToList())
                {
                    int at = order.IndexOf(item);
                    order.RemoveAt(at);
                    order.Insert(System.Math.Min(order.Count, at + step), item);
                }

                break;
        }

        if (frontFirst)
        {
            order.Reverse();
        }

        JgsGraphicsProperties.Restack(collection, order);
    }

    /// <summary>
    /// <c>allchild(h)</c>: an object's children, hidden handles included, front first, as a column.
    /// A vector of handles answers a cell of such columns, as R2025b's does.
    /// </summary>
    private static JgsValue AllChild(IReadOnlyList<JgsValue> args, int line, int col)
    {
        MatlabArity(args, 1, 1, line, col);
        JgsValue One(JgsValue handle)
        {
            JgsHandleEntry entry = JgsHandleRegistry.Require(handle, line, col);
            IReadOnlyList<GraphObject> children = entry.Target switch
            {
                JgsGraphicsRoot => JgsGraphicsProperties.RootFigures(hidden: true),
                UiComponentContainerModel => [], // what a custom component's setup built is its own (U10)
                _ => JgsGraphicsProperties.ChildrenOf(entry.Target),
            };
            var handles = new double[children.Count];
            for (int i = 0; i < children.Count; i++)
            {
                handles[i] = JgsHandleRegistry.For(children[children.Count - 1 - i]).AsNumber;
            }

            return JgsMatrix.FromColumnMajor(handles, handles.Length, handles.Length == 0 ? 0 : 1);
        }

        if (args[0].Type == JgsType.Array && args[0].ArrayLength != 1)
        {
            JgsValue cell = JgsValue.Cell([.. Enumerable.Range(0, args[0].ArrayLength).Select(i => One(args[0].ElementAt(i)))]);
            cell.Reshape(args[0].ArrayLength, args[0].ArrayLength == 0 ? 0 : 1);
            return cell;
        }

        return One(args[0].Type == JgsType.Array ? args[0].ElementAt(0) : args[0]);
    }
}
