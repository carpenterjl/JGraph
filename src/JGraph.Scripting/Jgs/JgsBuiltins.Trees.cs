using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The verbs of the second batch of components (app-building plan, U9): <c>uitreenode</c> and the
/// tree verbs <c>expand</c>, <c>collapse</c> and <c>move</c>; <c>scroll</c> for every component
/// that scrolls; and <c>uistyle</c>, <c>addStyle</c> and <c>removeStyle</c>. Argument forms and
/// refusals are R2025b's, recorded headless (probe <c>u9_forms</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterUiTreeBuiltins(JgsEnvironment env, Interpreter interpreter)
    {
        void DefineQuiet(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body, bool bare = false) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                AutoCallsBare = bare,
                BindsAnsAsStatement = false,
                KeepsStringArguments = true,
            }));

        DefineQuiet("uitreenode", UiTreeNode, bare: true);
        DefineQuiet("expand", (args, line, col) => ExpandOrCollapse("expand", expand: true, args, line, col));
        DefineQuiet("collapse", (args, line, col) => ExpandOrCollapse("collapse", expand: false, args, line, col));
        DefineQuiet("move", MoveNode);
        DefineQuiet("scroll", Scroll);
        env.Builtins.Register("uistyle", JgsValue.Function(new BuiltinFunction("uistyle",
            (args, line, col) => UiStyle(interpreter, args, line, col))
        {
            KeepsStringArguments = true,
        }));
        DefineQuiet("addStyle", (args, line, col) => AddStyle(interpreter, args, line, col));
        DefineQuiet("removeStyle", RemoveStyle);
    }

    private const string NeedsATree = "'Parent' must be a valid Tree object or TreeNode object.";

    // --- uitreenode ------------------------------------------------------------------------------

    /// <summary>
    /// <c>uitreenode()</c>, <c>uitreenode(parent)</c>, <c>uitreenode(___, Name, Value)</c>. The
    /// pairs are checked before the parent is (R2025b, probe <c>u9_forms</c>): an odd count and a
    /// name a node has not are refused first.
    /// </summary>
    private static JgsValue UiTreeNode(IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string word = "TreeNode";
        int start = 0;
        JgsValue? parentValue = null;
        if (args.Count > 0 && LooksLikeAParent(args[0]))
        {
            parentValue = args[0];
            start = 1;
        }

        var prototype = new UiTreeNodeModel();
        List<(string Name, JgsValue Value)> pairs = MakerPairs(args, start, word, line, col);
        var options = new List<(string Name, JgsValue Value)>(pairs.Count);
        foreach ((string name, JgsValue value) in pairs)
        {
            string property = PropertyNamed(prototype, name, word, line, col);
            if (property.Equals("Parent", StringComparison.OrdinalIgnoreCase))
            {
                parentValue = value;
            }
            else
            {
                options.Add((property, value));
            }
        }

        GraphObject holder;
        if (parentValue is null)
        {
            // No parent named: a tree of its own in a new uifigure.
            FigureModel figure = NewUiFigure(line, col);
            var tree = new UiTreeModel();
            figure.Components.Add(tree);
            JgsHandleRegistry.For(tree);
            holder = tree;
        }
        else
        {
            if (parentValue.Type != JgsType.Number || !JgsHandleRegistry.TryGet(parentValue, out JgsHandleEntry? named))
            {
                throw MakerError(word, NeedsATree, line, col);
            }

            holder = named.Target switch
            {
                UiTreeModel or UiTreeNodeModel => named.Target,
                AxesModel { IsUiAxes: true } => throw MakerError(word, "UIAxes cannot be a parent.", line, col),
                _ when HoldsNothing(named.Target) => throw MakerError(word, $"{JgsGraphicsCallbackValues.ClassWord(named.Target)} cannot be a parent.", line, col),
                _ => throw MakerError(word, NeedsATree, line, col),
            };
        }

        var node = new UiTreeNodeModel();
        GraphObjectCollection<UiTreeNodeModel> siblings = holder is UiTreeModel own ? own.Nodes : ((UiTreeNodeModel)holder).Nodes;
        siblings.Add(node);
        JgsValue handle = JgsHandleRegistry.For(node);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(node);
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
        catch (JgsRuntimeException refused)
        {
            using (GraphObjectLifecycle.SuppressNotifications())
            {
                siblings.Remove(node);
            }

            throw MakerError(word, refused.Message, line, col);
        }

        // A node born under a checked node of a check box tree is checked too (probe u9_behave).
        if (node.Tree is { CheckBoxes: true } checks && holder is UiTreeNodeModel above && checks.Checked.Contains(above))
        {
            checks.Checked = [.. checks.Checked, node];
        }

        if (JgsGraphicsProperties.FigureOf(node) is { } home)
        {
            JG.TouchFigure(home);
        }

        if (fireCreate)
        {
            JgsCallbackDispatcher.Current?.FireCreateFcn(node);
        }

        return handle;
    }

    // --- expand, collapse, move ------------------------------------------------------------------

    private static JgsRuntimeException UndefinedFor(string verb, JgsValue value, int line, int col)
    {
        string type = value.Type == JgsType.Number && JgsHandleRegistry.TryGet(value, out JgsHandleEntry? named)
            ? JgsGraphicsProperties.FullClassOf(named.Target)
            : ClassOf(value, JgsDialect.Matlab);
        return new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", $"Undefined function '{verb}' for input arguments of type '{type}'.");
    }

    private static JgsRuntimeException NotAChoice(string[] choices, JgsValue given, int line, int col)
    {
        string list = string.Join(", ", choices.Select(static c => $"'{c}'"));
        string tail = IsTextScalar(given) ? $"The input, '{TextOf(given)}', did not match any of the valid values." : "The input did not match any of the valid values.";
        return new JgsRuntimeException(line, col, "MATLAB:unrecognizedStringChoice", $"Expected input to match one of these values:\n\n{list}\n\n{tail}");
    }

    /// <summary>A word matched the way <c>validatestring</c> matches it: in any case, by any unambiguous beginning.</summary>
    private static string? ChoiceOf(JgsValue value, string[] choices)
    {
        if (!IsTextScalar(value))
        {
            return null;
        }

        string typed = TextOf(value);
        string[] starts = [.. choices.Where(c => c.StartsWith(typed, StringComparison.OrdinalIgnoreCase))];
        return starts.Length == 1 || (starts.Length > 1 && choices.Any(c => c.Equals(typed, StringComparison.OrdinalIgnoreCase)))
            ? choices.FirstOrDefault(c => c.Equals(typed, StringComparison.OrdinalIgnoreCase)) ?? starts[0]
            : null;
    }

    private static JgsValue ExpandOrCollapse(string verb, bool expand, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", $"Unrecognized function or variable '{verb}'.");
        }

        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        // An array of nodes, or of trees, is each in turn (probe u9_tree: expand([n1 n2]) runs).
        int count = args[0].Type == JgsType.Array ? args[0].ArrayLength : 1;
        var targets = new List<JgsHandleEntry>(count);
        for (int i = 0; i < count; i++)
        {
            JgsValue one = args[0].Type == JgsType.Array ? args[0].ElementAt(i) : args[0];
            if (!JgsHandleRegistry.TryGet(one, out JgsHandleEntry? each) || each.Target is not (UiTreeModel or UiTreeNodeModel))
            {
                throw UndefinedFor(verb, one, line, col);
            }

            targets.Add(each);
        }

        if (targets.Count == 0)
        {
            throw UndefinedFor(verb, args[0], line, col);
        }

        bool all = false;
        if (args.Count == 2)
        {
            all = ChoiceOf(args[1], ["all"]) is not null ? true : throw NotAChoice(["all"], args[1], line, col);
        }

        IEnumerable<UiTreeNodeModel> nodes = targets.SelectMany(named => named.Target switch
        {
            UiTreeModel tree => all ? tree.AllNodes() : tree.Nodes,
            UiTreeNodeModel node => all ? [node, .. node.Descendants()] : [node],
            _ => (IEnumerable<UiTreeNodeModel>)[],
        });
        foreach (UiTreeNodeModel node in nodes.ToList())
        {
            node.Expanded = expand;
        }

        return JgsValue.Null;
    }

    private static JgsValue MoveNode(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsValue first = args[0];
        if (first.Type == JgsType.Array && first.ArrayLength > 1 && Enumerable.Range(0, first.ArrayLength).All(i =>
                JgsHandleRegistry.TryGet(first.ElementAt(i), out JgsHandleEntry? each) && each.Target is UiTreeNodeModel))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ui:TreeNode:requiresScalarTreeNode", "TreeNode object must be scalar.");
        }

        if (first.Type == JgsType.Number && JgsHandleRegistry.TryGet(first, out JgsHandleEntry? movingEntry))
        {
            if (movingEntry.Target is not UiTreeNodeModel)
            {
                throw UndefinedFor("move", first, line, col);
            }
        }
        else
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ui:TreeNode:firstArgumentRequiresTreeNode", "First argument must be a TreeNode object.");
        }

        var moving = (UiTreeNodeModel)movingEntry.Target;
        if (args[1].Type != JgsType.Number || !JgsHandleRegistry.TryGet(args[1], out JgsHandleEntry? targetEntry) || targetEntry.Target is not UiTreeNodeModel target)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:ui:TreeNode:targetRequiresTreeNode", "Target node must be a TreeNode object.");
        }

        bool after = true;
        if (args.Count == 3)
        {
            string[] choices = ["after", "before"];
            after = (ChoiceOf(args[2], choices) ?? throw NotAChoice(choices, args[2], line, col)) == "after";
        }

        if (ReferenceEquals(moving, target))
        {
            return JgsValue.Null;
        }

        if (target.Parent is not { } holder)
        {
            return JgsValue.Null;
        }

        JgsGraphicsProperties.MoveTreeNode(moving, holder, target, after, line, col);
        return JgsValue.Null;
    }

    // --- scroll ----------------------------------------------------------------------------------

    private static JgsValue Scroll(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", "Unrecognized function or variable 'scroll'.");
        }

        if (!JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? named)
            || named.Target is not (UiTreeModel or UiListBoxModel or UiTableModel or UiTextAreaModel or UiGridLayoutModel or UiPanelModel or UiTabModel or FigureModel))
        {
            throw UndefinedFor("scroll", args[0], line, col);
        }

        if (args.Count == 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        string word = JgsGraphicsCallbackValues.ClassWord(named.Target);
        string? place = IsTextScalar(args[1]) && !args[1].IsCharMatrix ? TextOf(args[1]).ToLowerInvariant() : null;
        switch (named.Target)
        {
            case UiTreeModel tree:
            {
                if (args.Count > 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                if (place is "top" or "bottom")
                {
                    tree.RequestScroll(place);
                }
                else if (args[1].Type == JgsType.Number && JgsHandleRegistry.TryGet(args[1], out JgsHandleEntry? nodeEntry)
                    && nodeEntry.Target is UiTreeNodeModel node && tree.Holds(node))
                {
                    tree.RequestScroll(node);
                }
                else
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:ui:Tree:invalidTreeScrollTarget", "Scroll location must be 'top', 'bottom', or a TreeNode object.");
                }

                break;
            }

            case UiListBoxModel list:
            {
                if (args.Count > 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                if (place is "top" or "bottom")
                {
                    list.RequestScroll(place);
                    break;
                }

                // An entry of Items, or of ItemsData where there is data.
                int at = -1;
                if (IsTextScalar(args[1]) && !args[1].IsCharMatrix)
                {
                    at = list.Items.ToList().IndexOf(TextOf(args[1]));
                }

                if (at < 0 && named.ItemsData is { } data)
                {
                    int count = data.Type == JgsType.Cell ? data.AsCell.Length : data.Type == JgsType.Array ? data.ArrayLength : 1;
                    for (int i = 0; i < count && at < 0; i++)
                    {
                        JgsValue one = data.Type == JgsType.Cell ? data.AsCell[i] : data.Type == JgsType.Array ? data.ElementAt(i) : data;
                        if (IsEqualValues(one, args[1]))
                        {
                            at = i;
                        }
                    }
                }

                if (at < 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:ui:ListBox:invalidScrollTarget", "Input to scroll must be either 'top', 'bottom' or an entry in Items or ItemsData.");
                }

                list.RequestScroll(at);
                break;
            }

            case UiTextAreaModel area:
                if (args.Count > 2)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                if (place is not ("top" or "bottom"))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:ui:TextArea:invalidScrollTarget", "Scroll location must be 'top' or 'bottom'.");
                }

                area.RequestScroll(place);
                break;

            case UiTableModel table:
            {
                (int rows, int columns) = JgsUiTables.SizeOf(JgsUiTables.StateOf(named).Data);
                if (args.Count == 2)
                {
                    switch (place)
                    {
                        case "top":
                            table.RequestScroll(0, -1);
                            break;
                        case "bottom":
                            table.RequestScroll(System.Math.Max(0, rows - 1), -1);
                            break;
                        case "left":
                            table.RequestScroll(-1, 0);
                            break;
                        case "right":
                            table.RequestScroll(-1, System.Math.Max(0, columns - 1));
                            break;
                        default:
                            throw new JgsRuntimeException(line, col, "MATLAB:ui:Table:invalidScrollLocation", "Location must be 'top', 'bottom', 'left' or 'right'.");
                    }

                    break;
                }

                if (args.Count > 3)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                double[]? index = args[2].Type is JgsType.Number or JgsType.Array && !args[2].IsStringArray ? ToDoubles("scroll", args[2], line, col) : null;
                bool Whole(double x, int most) => x >= 1 && x == System.Math.Floor(x) && x <= most;
                switch (place)
                {
                    case "row":
                        if (index is not { Length: 1 } || !Whole(index[0], rows))
                        {
                            throw new JgsRuntimeException(line, col, "MATLAB:ui:Table:invalidRowTargetIndex", "Option for 'row' must be a positive integer within the range of data.");
                        }

                        table.RequestScroll((int)index[0] - 1, -1);
                        break;
                    case "column":
                        if (index is not { Length: 1 } || !Whole(index[0], columns))
                        {
                            throw new JgsRuntimeException(line, col, "MATLAB:ui:Table:invalidColumnTargetIndex", "Option for 'column' must be a positive integer within the range of data.");
                        }

                        table.RequestScroll(-1, (int)index[0] - 1);
                        break;
                    case "cell":
                        if (index is not { Length: 2 } || args[2].Rows != 1 || !Whole(index[0], rows) || !Whole(index[1], columns))
                        {
                            throw new JgsRuntimeException(line, col, "MATLAB:ui:Table:invalidCellTargetIndex", "Option for 'cell' must be an 1-by-2 vector of positive integers within the range of data.");
                        }

                        table.RequestScroll((int)index[0] - 1, (int)index[1] - 1);
                        break;
                    default:
                        throw new JgsRuntimeException(line, col, "MATLAB:ui:Table:invalidScrollTarget", "Target must be 'row', 'column' or 'cell'.");
                }

                break;
            }

            default:
            {
                // A container: a grid scrolls here; a panel, a tab and a figure keep Scrollable and
                // scroll nothing yet (open item 47). One not scrollable warns, as R2025b's does.
                bool scrollable = named.Target switch
                {
                    UiContainerModel container => container.Scrollable,
                    FigureModel figure => figure.Scrollable,
                    _ => false,
                };
                if (args.Count > 3)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
                }

                double[]? pair = null;
                if (args.Count == 3)
                {
                    double? x = args[1].Type == JgsType.Number ? args[1].AsNumber : null;
                    double? y = args[2].Type == JgsType.Number ? args[2].AsNumber : null;
                    pair = x is { } px && y is { } py ? [px, py] : throw new JgsRuntimeException(line, col, "MATLAB:uicontainer:InvalidScrollTarget", "Invalid location");
                }
                else if (place is not ("top" or "bottom" or "left" or "right"))
                {
                    pair = args[1].Type == JgsType.Array && args[1].ArrayLength == 2 && !args[1].IsStringArray
                        ? ToDoubles("scroll", args[1], line, col)
                        : throw new JgsRuntimeException(line, col, "MATLAB:uicontainer:InvalidScrollTarget", "Invalid location");
                }

                if (!scrollable)
                {
                    if (JgsCallbackDispatcher.Current?.Interpreter?.Host is { } host)
                    {
                        Warn(host, "MATLAB:uicontainer:ScrollableOff", "'Scrollable' must be 'on' to scroll programmatically.");
                    }

                    break;
                }

                if (named.Target is UiGridLayoutModel grid)
                {
                    if (pair is not null)
                    {
                        grid.ScrollX = System.Math.Max(0, pair[0] - 1);
                        grid.ScrollY = System.Math.Max(0, pair[1] - 1);
                    }
                    else
                    {
                        switch (place)
                        {
                            case "top":
                                grid.ScrollY = 0;
                                break;
                            case "bottom":
                                grid.ScrollY = double.MaxValue;
                                break;
                            case "left":
                                grid.ScrollX = 0;
                                break;
                            default:
                                grid.ScrollX = double.MaxValue;
                                break;
                        }
                    }
                }

                break;
            }
        }

        _ = word;
        if (JgsGraphicsProperties.FigureOf(named.Target) is { } shown)
        {
            JG.TouchFigure(shown);
        }

        return JgsValue.Null;
    }

    /// <summary><c>open(cm, x, y)</c> and <c>open(cm, [x y])</c>: the menu at a point of its figure, in pixels from the lower-left corner.</summary>
    private static JgsValue OpenContextMenu(ContextMenuModel menu, JgsHandleEntry entry, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        double[] point = args.Count == 3
            ? [NumberArg("open", args[1], line, col), NumberArg("open", args[2], line, col)]
            : ToDoubles("open", args[1], line, col);
        if (point.Length != 2)
        {
            throw new JgsRuntimeException(line, col, "open: a context menu opens at a point, given as x, y or as [x y].");
        }

        entry.MenuPlace = (point[0], point[1]);
        menu.RequestOpen(point[0], point[1]);
        if (menu.Parent is FigureModel figure)
        {
            JG.TouchFigure(figure);
        }

        return JgsValue.Null;
    }

    private static double NumberArg(string verb, JgsValue value, int line, int col) =>
        value.Type == JgsType.Number ? value.AsNumber : throw new JgsRuntimeException(line, col, $"{verb}: a coordinate must be a number.");

    // --- uistyle, addStyle, removeStyle ----------------------------------------------------------

    private static JgsValue UiStyle(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsClass definition = interpreter.BuiltinClass(JgsBuiltinClasses.Style)
            ?? throw new JgsRuntimeException(line, col, "uistyle: the Style class is not available in this build.");
        JgsObject style = definition.NewDefault(line, col);
        var pairs = new List<(string Name, JgsValue Value)>();
        if (args.Count == 1 && args[0].Type == JgsType.Struct && !args[0].IsStructArray && args[0].ClassName is null)
        {
            foreach ((string name, JgsValue value) in args[0].AsStruct)
            {
                pairs.Add((name, value));
            }
        }
        else
        {
            for (int i = 0; i < args.Count; i += 2)
            {
                if (!IsTextScalar(args[i]) || args[i].IsCharMatrix)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
                }

                if (i + 1 >= args.Count)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                        $"No value was given for '{TextOf(args[i])}'. Name-value pair arguments require a name followed by a value.");
                }

                pairs.Add((TextOf(args[i]), args[i + 1]));
            }
        }

        foreach ((string typed, JgsValue value) in pairs)
        {
            string name = JgsBuiltinClasses.StyleProperties.FirstOrDefault(p => p.Equals(typed, StringComparison.OrdinalIgnoreCase))
                ?? JgsBuiltinClasses.StyleProperties.Where(p => p.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).ToList() switch
                {
                    { Count: 1 } one => one[0],
                    _ => throw new JgsRuntimeException(line, col, "MATLAB:noPublicFieldForClass",
                        $"Unrecognized property '{typed}' for class '{JgsBuiltinClasses.Style}'."),
                };
            style.Fields[name] = JgsBuiltinClasses.StyleValueOf(name, value, line, col);
        }

        return JgsValue.Object(style);
    }

    /// <summary>The component <c>addStyle</c> or <c>removeStyle</c> was given, or the refusal R2025b gives for anything else.</summary>
    private static JgsHandleEntry StyleTarget(string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsValue first = args[0];
        if (first.Type == JgsType.Number && JgsHandleRegistry.TryGet(first, out JgsHandleEntry? entry))
        {
            if (!JgsGraphicsProperties.TakesStyles(entry.Target))
            {
                throw UndefinedFor(verb, first, line, col);
            }

            return entry;
        }

        if (first.Type == JgsType.Number && JgsHandleRegistry.WasMinted(first.AsNumber))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:InvalidHandle", "Invalid or deleted object.");
        }

        // R2025b finds the method by its second argument when the first is no object.
        string type = args.Count > 1 && args[1].Type == JgsType.Object ? args[1].AsObject.Class.Name : ClassOf(first, JgsDialect.Matlab);
        throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", $"Undefined function '{verb}' for input arguments of type '{type}'.");
    }

    private static JgsValue AddStyle(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        _ = interpreter;
        if (args.Count < 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (args.Count > 4)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsHandleEntry entry = StyleTarget("addStyle", args, line, col);
        string word = JgsGraphicsCallbackValues.ClassWord(entry.Target);
        JgsRuntimeException Refuse(string id, string text) => new(line, col, $"MATLAB:ui:{word}:{id}", text);
        if (args[1].Type != JgsType.Object || !args[1].AsObject.Class.IsA(JgsBuiltinClasses.Style))
        {
            throw Refuse("invalidStyleObject", "Invalid Style object.");
        }

        if (args.Count == 3)
        {
            throw Refuse("invalidNumberOfInputs", "Incorrect number of input arguments.");
        }

        (string whole, string[] words, string sentence) = entry.Target switch
        {
            UiTableModel => ("table", new[] { "cell", "row", "column", "table" }, "Third argument must be 'cell', 'row', 'column', or 'table'."),
            UiTreeModel => ("tree", new[] { "tree", "node", "level", "subtree" }, "Third argument must be 'tree', 'node', 'level', or 'subtree'."),
            UiListBoxModel => ("listbox", new[] { "listbox", "item" }, "Third argument must be 'listbox' or 'item'."),
            _ => ("dropdown", new[] { "dropdown", "item" }, "Third argument must be 'dropdown' or 'item'."),
        };

        string target = whole;
        JgsValue index = JgsValue.Str(string.Empty);
        var indices = new List<int>();
        var nodes = new List<GraphObject>();
        if (args.Count == 4)
        {
            target = IsTextScalar(args[2]) && !args[2].IsCharMatrix
                ? words.FirstOrDefault(w => w.Equals(TextOf(args[2]), StringComparison.OrdinalIgnoreCase)) ?? throw Refuse("invalidStyleTarget", sentence)
                : throw Refuse("invalidStyleTarget", sentence);
            index = JgsValue.Share(args[3]);
            string Cap(string w) => char.ToUpperInvariant(w[0]) + w[1..];
            if (target == whole)
            {
                if (!(IsTextScalar(args[3]) && TextOf(args[3]).Length == 0) && !(args[3].Type == JgsType.String && args[3].AsString.Length == 0))
                {
                    throw Refuse("invalidTargetIndex", $"Option for '{whole}' must be an empty character vector.");
                }
            }
            else if (target is "row" or "column" or "level" or "item")
            {
                string kind = ClassOf(args[3], JgsDialect.Matlab);
                double[]? given = args[3].Type is JgsType.Number or JgsType.Array && !args[3].IsStringArray && kind != "logical"
                    ? ToDoubles("addStyle", args[3], line, col) : null;
                if (given is not { Length: > 0 } || given.Any(static x => x < 1 || x != System.Math.Floor(x)))
                {
                    throw Refuse($"invalid{Cap(target)}TargetIndex", $"Option for '{target}' must be a positive integer or a vector of positive integers.");
                }

                indices.AddRange(given.Select(static x => (int)x));
                index = given.Length == 1 ? JgsValue.Share(args[3]) : AsRow(JgsValue.Share(args[3])); // a column reads back as a row (probe u9_extra)
            }
            else if (target == "cell")
            {
                string kind = ClassOf(args[3], JgsDialect.Matlab);
                bool shaped = args[3].Type == JgsType.Array && !args[3].IsStringArray && kind != "logical" && args[3].Cols == 2 && args[3].Dims.Length == 2;
                double[]? given = shaped ? ToDoubles("addStyle", args[3], line, col) : null;
                if (given is null || given.Any(static x => x < 1 || x != System.Math.Floor(x)))
                {
                    throw Refuse("invalidCellTargetIndex", "Option for 'cell' must be an N-by-2 matrix of positive integers.");
                }

                int rows = args[3].Rows;
                for (int r = 0; r < rows; r++)
                {
                    indices.Add((int)given[r]);
                    indices.Add((int)given[rows + r]);
                }
            }
            else
            {
                // node, subtree: tree nodes of any tree.
                JgsValue value = args[3];
                int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
                bool ok = value.Type is JgsType.Number or JgsType.Array && !value.IsStringArray && count > 0;
                for (int i = 0; ok && i < count; i++)
                {
                    JgsValue one = value.Type == JgsType.Array ? value.ElementAt(i) : value;
                    if (JgsHandleRegistry.TryGet(one, out JgsHandleEntry? nodeEntry) && nodeEntry.Target is UiTreeNodeModel node)
                    {
                        nodes.Add(node);
                    }
                    else
                    {
                        ok = false;
                    }
                }

                if (!ok)
                {
                    throw Refuse($"invalid{Cap(target)}TargetIndex", $"Option for '{target}' must be a TreeNode or vector of TreeNodes.");
                }
            }
        }

        UiStyleTarget kindOf = target switch
        {
            "row" => UiStyleTarget.Row,
            "column" => UiStyleTarget.Column,
            "cell" => UiStyleTarget.Cell,
            "node" => UiStyleTarget.Node,
            "level" => UiStyleTarget.Level,
            "subtree" => UiStyleTarget.Subtree,
            "item" => UiStyleTarget.Item,
            _ => UiStyleTarget.Whole,
        };

        // The style as it is now: a value, so a later change to the variable does not reach the component.
        JgsObject given2 = args[1].AsObject;
        var copy = new JgsObject(given2.Class);
        foreach ((string name, JgsValue held) in given2.Fields)
        {
            copy.Fields[name] = JgsValue.Share(held);
        }

        var rule = new UiStyleRule(kindOf, indices, nodes, JgsBuiltinClasses.StyleOf(copy));
        (entry.Styles ??= []).Add(new JgsStyleRow(target, index, JgsValue.Object(copy), rule));
        JgsGraphicsProperties.SyncStyles(entry);
        return JgsValue.Null;
    }

    private static JgsValue RemoveStyle(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", "Unrecognized function or variable 'removeStyle'.");
        }

        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsHandleEntry entry = StyleTarget("removeStyle", args, line, col);
        string word = JgsGraphicsCallbackValues.ClassWord(entry.Target);
        List<JgsStyleRow> rows = entry.Styles ??= [];
        if (args.Count == 1)
        {
            rows.Clear();
            JgsGraphicsProperties.SyncStyles(entry);
            return JgsValue.Null;
        }

        string kind = ClassOf(args[1], JgsDialect.Matlab);
        double[]? given = args[1].Type is JgsType.Number or JgsType.Array && !args[1].IsStringArray && kind != "logical"
            ? ToDoubles("removeStyle", args[1], line, col) : null;
        if (given is not { Length: > 0 } || given.Any(static x => x < 1 || x != System.Math.Floor(x)))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:ui:{word}:invalidRemovalIndex", "Option must be a positive integer or a vector of positive integers.");
        }

        if (given.Any(x => x > rows.Count))
        {
            throw new JgsRuntimeException(line, col, $"MATLAB:ui:{word}:removalIndexOutOfBounds",
                "Error removing style: Style order number exceeds 'StyleConfigurations' table dimensions.");
        }

        foreach (int at in given.Select(static x => (int)x).Distinct().OrderByDescending(static x => x))
        {
            rows.RemoveAt(at - 1);
        }

        JgsGraphicsProperties.SyncStyles(entry);
        return JgsValue.Null;
    }
}
