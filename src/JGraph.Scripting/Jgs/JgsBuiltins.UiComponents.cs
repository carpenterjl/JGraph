using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Imaging;
using JGraph.Imaging.Codecs;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The makers of a <c>uifigure</c>'s components (app-building plan, U5): <c>uilabel</c>,
/// <c>uibutton</c>, <c>uieditfield</c>, <c>uitextarea</c>, <c>uidropdown</c>, <c>uilistbox</c>,
/// <c>uicheckbox</c>, <c>uiradiobutton</c>, <c>uitogglebutton</c>, <c>uislider</c>,
/// <c>uispinner</c>, <c>uiimage</c>, <c>uihyperlink</c>, <c>uigridlayout</c> and <c>uiaxes</c>, with
/// the dialogs a figure lays over itself — <c>uialert</c>, <c>uiconfirm</c>, <c>uiprogressdlg</c> —
/// and <c>focus</c>. The forms each call takes and the words of each refusal are R2025b's, recorded
/// headless (probes <c>u5_forms</c>, <c>u5_grid</c>, <c>u5_dialogs</c>, <c>u5_dialogs2</c>).
/// </summary>
internal static partial class JgsBuiltins
{
    // --- values ------------------------------------------------------------------------------------

    /// <summary>A number that reads back in a numeric class: <c>int8(5)</c> stays <c>int8</c>.</summary>
    internal static JgsValue NumberOfClass(double number, string kind)
    {
        JgsValue value = JgsValue.Number(number);
        if (JgsNumericClasses.Parse(kind) is { } numeric && numeric != JgsNumericClass.Double)
        {
            value.SetNumericClass(numeric);
        }

        return value;
    }

    /// <summary>A row of numbers in a numeric class; no numbers is the 0-by-0 empty.</summary>
    internal static JgsValue RowOfClass(double[] numbers, string kind)
    {
        JgsValue value = JgsMatrix.FromColumnMajor(numbers, numbers.Length == 0 ? 0 : 1, numbers.Length);
        if (JgsNumericClasses.Parse(kind) is { } numeric && numeric != JgsNumericClass.Double)
        {
            value.SetNumericClass(numeric);
        }

        return value;
    }

    /// <summary>One element of a numeric array, in the array's class.</summary>
    internal static JgsValue ElementOfClass(JgsValue array, int index)
    {
        JgsValue element = array.ElementAt(index);
        if (element.Type != JgsType.Number || array.NumericClass == JgsNumericClass.Double)
        {
            return element;
        }

        JgsValue classed = JgsValue.Number(element.AsNumber);
        classed.SetNumericClass(array.NumericClass);
        return classed;
    }

    /// <summary>A column as a row, which is how R2025b keeps a list's data.</summary>
    internal static JgsValue AsRow(JgsValue value)
    {
        if (value.Type == JgsType.Cell)
        {
            return JgsValue.Cell([.. value.AsCell]);
        }

        if (value.IsStringArray)
        {
            return JgsValue.StringArray([.. Enumerable.Range(0, value.ArrayLength).Select(i => value.ElementAt(i))]);
        }

        string kind = ClassOf(value, JgsDialect.Matlab);
        if (kind != "logical" && JgsNumericClasses.Parse(kind) is not null
            && !Enumerable.Range(0, value.ArrayLength).Any(i => value.ElementAt(i).Type == JgsType.Complex))
        {
            return RowOfClass(ToDoubles("ItemsData", value, 0, 0), kind);
        }

        return JgsValue.Array([.. Enumerable.Range(0, value.ArrayLength).Select(i => value.ElementAt(i))]);
    }

    /// <summary>MATLAB's <c>isequal</c> of two values.</summary>
    internal static bool IsEqualValues(JgsValue left, JgsValue right)
    {
        // A string scalar and a character row holding the same text are equal, as isequal says.
        if (IsTextScalar(left) && IsTextScalar(right))
        {
            return TextOf(left) == TextOf(right);
        }

        // [] and '' are equal, as are any two empty arrays of one shape (isequal([], '') is true);
        // an empty cell is not one of them (isequal([], {}) is false).
        static bool EmptyArray(JgsValue v) => v.Type == JgsType.String ? v.AsString.Length == 0 && !v.IsCharMatrix
            : v.Type == JgsType.Array && v.ArrayLength == 0;
        if (EmptyArray(left) && EmptyArray(right))
        {
            int[] a = left.Type == JgsType.Array ? left.Dims : [0, 0];
            int[] b = right.Type == JgsType.Array ? right.Dims : [0, 0];
            return a.SequenceEqual(b);
        }

        return JgsStdlib.DeepEquals(left, right);
    }

    /// <summary>A picture file as the window draws one, or null when it cannot be read as a picture.</summary>
    internal static UiImage? TryReadPicture(string path)
    {
        try
        {
            (ImageBuffer image, ImageBuffer? alpha) = ImageCodec.ReadWithAlpha(path, 0);
            using (image)
            using (alpha)
            {
                return ToUiImage(image, alpha);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    /// <summary>An image value as the window draws one.</summary>
    internal static UiImage ToUiImage(ImageBuffer image, ImageBuffer? alpha = null)
    {
        int rows = image.Height;
        int cols = image.Width;
        var pixels = new byte[rows * cols * 4];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int at = ((r * cols) + c) * 4;
                byte Sample(int channel) => (byte)System.Math.Round(255 * System.Math.Clamp(image[r, c, System.Math.Min(channel, image.Channels - 1)], 0, 1));
                pixels[at] = Sample(2);
                pixels[at + 1] = Sample(1);
                pixels[at + 2] = Sample(0);
                pixels[at + 3] = alpha is not null
                    ? (byte)System.Math.Round(255 * System.Math.Clamp(alpha[r, c, 0], 0, 1))
                    : image.Channels == 4 ? (byte)System.Math.Round(255 * System.Math.Clamp(image[r, c, 3], 0, 1)) : (byte)255;
            }
        }

        return new UiImage(cols, rows, pixels);
    }

    // --- registration --------------------------------------------------------------------------------

    /// <summary>What one maker makes: its verb, MATLAB's class word, the styles it takes, and the model for each.</summary>
    private sealed record ComponentMaker(
        string Verb, string ClassWord, string[]? Styles, Func<string?, UiComponentModel> Make, bool InGroup = false, string? StyleList = null);

    // audit: registered through MakeComponent — RegisterUiComponentBuiltins defines each Verb below as a call of it
    private static readonly ComponentMaker[] ComponentMakers =
    [
        new("uilabel", "Label", null, static _ => new UiLabelModel()),
        new("uibutton", "Button", ["push", "state"], static style => style == "state" ? new UiStateButtonModel() : new UiButtonModel()),
        new("uieditfield", "EditField", ["text", "numeric"], static style => style == "numeric" ? new UiNumericEditFieldModel() : new UiEditFieldModel()),
        new("uitextarea", "TextArea", null, static _ => new UiTextAreaModel()),
        new("uidropdown", "DropDown", null, static _ => new UiDropDownModel()),
        new("uilistbox", "ListBox", null, static _ => new UiListBoxModel()),
        new("uicheckbox", "CheckBox", null, static _ => new UiCheckBoxModel()),
        new("uiradiobutton", "RadioButton", null, static _ => new UiRadioButtonModel(), InGroup: true),
        new("uitogglebutton", "ToggleButton", null, static _ => new UiToggleButtonModel(), InGroup: true),
        new("uislider", "Slider", ["slider", "range"], static style => style == "range" ? new UiRangeSliderModel() : new UiSliderModel()),
        new("uispinner", "Spinner", null, static _ => new UiSpinnerModel()),
        new("uiimage", "Image", null, static _ => new UiImageModel()),
        new("uihyperlink", "Hyperlink", null, static _ => new UiHyperlinkModel()),

        // U9: the words each refusal lists are R2025b's, in its order (probe u9_forms).
        new("uiknob", "Knob", ["continuous", "discrete"], static style => style == "discrete" ? new UiDiscreteKnobModel() : new UiKnobModel(),
            StyleList: "'continuous' or 'discrete'"),
        new("uiswitch", "Switch", ["slider", "rocker", "toggle"], static style => new UiSwitchModel(style switch
        {
            "rocker" => UiSwitchStyle.Rocker,
            "toggle" => UiSwitchStyle.Toggle,
            _ => UiSwitchStyle.Slider,
        }), StyleList: "'slider', 'toggle' or 'rocker'"),
        new("uigauge", "Gauge", ["circular", "linear", "ninetydegree", "semicircular"], static style => style switch
        {
            "linear" => new UiLinearGaugeModel(),
            "ninetydegree" => new UiNinetyDegreeGaugeModel(),
            "semicircular" => new UiSemicircularGaugeModel(),
            _ => new UiGaugeModel(),
        }, StyleList: "'circular', 'semicircular', 'ninetydegree' or 'linear'"),
        new("uilamp", "Lamp", null, static _ => new UiLampModel()),
        new("uidatepicker", "DatePicker", null, static _ => new UiDatePickerModel()),
        new("uicolorpicker", "ColorPicker", null, static _ => new UiColorPickerModel()),
        new("uitree", "Tree", ["tree", "checkbox"], static style => style == "checkbox" ? new UiCheckBoxTreeModel() : new UiTreeModel(),
            StyleList: "'tree' or 'checkbox'"),

        // U9b: a web page (probe u9b_forms).
        new("uihtml", "HTML", null, static _ => new UiHtmlModel()),
    ];

    private static void RegisterUiComponentBuiltins(JgsEnvironment env, JGraphScriptGlobals host, CancellationToken cancellationToken)
    {
        // A custom component named as the parent is its area's handle (U10).
        void DefineMaker(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) => body(JgsComponentContainers.AsHandles(args), line, col))
            {
                AutoCallsBare = true,
                BindsAnsAsStatement = false,
            }));

        void DefineQuiet(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        foreach (ComponentMaker maker in ComponentMakers)
        {
            ComponentMaker captured = maker;
            DefineMaker(captured.Verb, (args, line, col) => MakeComponent(captured, args, line, col));
        }

        DefineMaker("uigridlayout", UiGridLayout);
        DefineMaker("uiaxes", UiAxes);
        DefineQuiet("uialert", UiAlert);
        env.Builtins.Register("uiconfirm", JgsValue.Function(new BuiltinFunction("uiconfirm",
            (args, line, col) => UiConfirm(host, cancellationToken, args, wanted: 1, line, col))
        {
            BindsAnsAsStatement = false,
            TakesOutputCount = true,
            MultiOutput = (args, wanted, line, col) =>
                UiConfirm(host, cancellationToken, args, wanted, line, col) is { Type: not JgsType.Null } answer ? [answer] : [],
        }));
        DefineMaker("uiprogressdlg", UiProgressDlg);
        DefineQuiet("focus", Focus);

        // A grid child's place as a value of its own (open item 48), reached by its dotted name.
        env.Builtins.Register(JgsGraphicsProperties.GridLayoutOptionsClass, JgsValue.Function(new BuiltinFunction(
            JgsGraphicsProperties.GridLayoutOptionsClass, JgsGraphicsProperties.NewGridLayoutOptions)
        {
            AutoCallsBare = true,
        }));
    }

    // --- one component -------------------------------------------------------------------------------

    private static JgsRuntimeException MakerError(string classWord, string message, int line, int col) =>
        new(line, col, $"MATLAB:ui:{classWord}:unknownInput", message);

    private const string NeedsAParent = "'Parent' must be a parent component, such as a uifigure object.";
    private const string NeedsAGroup = "'Parent' value must be specified as a ButtonGroup object.";

    /// <summary>A value that names no component, where a parent was expected first: a number, an empty.</summary>
    private static bool LooksLikeAParent(JgsValue value) =>
        value.Type is JgsType.Number or JgsType.Array && !value.IsStringArray && !value.IsCharMatrix;

    /// <summary>The figure or container a handle names, or the refusal for one that can hold nothing.</summary>
    private static IUiContainer ContainerNamed(JgsValue value, string classWord, string notAHandle, int line, int col)
    {
        if (value.Type != JgsType.Number || !JgsHandleRegistry.TryGet(value, out JgsHandleEntry? named))
        {
            throw MakerError(classWord, notAHandle, line, col);
        }

        // A tab group holds tabs and nothing else, and a menu or a toolbar no component (U8).
        if (named.Target is UiTabGroupModel or MenuItemModel or ContextMenuModel or UiToolbarModel or UiTreeModel or UiTreeNodeModel)
        {
            throw MakerError(classWord, NeedsAParent, line, col);
        }

        return named.Target as IUiContainer
            ?? throw MakerError(classWord, named.Target switch
            {
                JgsGraphicsRoot => NeedsAParent,
                AxesModel { IsUiAxes: true } => "UIAxes cannot be a parent.",
                _ => $"{JgsGraphicsCallbackValues.ClassWord(named.Target)} cannot be a parent.",
            }, line, col);
    }

    /// <summary>A new, shown <c>uifigure</c>: what a component made with no parent is put in.</summary>
    private static FigureModel NewUiFigure(int line, int col)
    {
        JgsValue handle = UiFigure([], line, col);
        return (FigureModel)JgsHandleRegistry.Require(handle, line, col).Target;
    }

    /// <summary>The <c>uifigure</c> a custom component made with no parent is put in (U10, measured).</summary>
    internal static IUiContainer NewUiFigureForComponent(int line, int col) => NewUiFigure(line, col);

    /// <summary>The container a custom component's parent names, with a maker's refusals (U10: <c>UIAxes cannot be a parent.</c>).</summary>
    internal static IUiContainer ContainerForComponent(JgsValue value, int line, int col) =>
        ComponentParent(value, pairsWithFocus: false, positional: true, line, col);

    /// <summary>Puts a custom component's area in its parent, as a maker puts a component (U10).</summary>
    internal static JgsValue AddComponentArea(UiComponentContainerModel area, IUiContainer parent, int line, int col) =>
        AddComponent(area, parent, [], line, col);

    /// <summary>The property a name in a creating call means: itself in any case, or the one name it begins.</summary>
    private static string PropertyNamed(GraphObject target, string typed, string classWord, int line, int col)
    {
        if (typed.Equals("Parent", StringComparison.OrdinalIgnoreCase) || JgsGraphicsProperties.TryFind(target, typed, out _))
        {
            return typed;
        }

        List<string> starts = [.. JgsGraphicsProperties.NamesOf(target)
            .Where(name => typed.Length > 0 && name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))];
        return starts.Count == 1
            ? starts[0]
            : throw MakerError(classWord, $"Unrecognized property {typed} for class {JgsGraphicsCallbackValues.ClassWord(target)}.", line, col);
    }

    /// <summary>
    /// The name-value pairs of a creating call, from position <paramref name="start"/>: pairs, or one
    /// struct whose fields are the names.
    /// </summary>
    private static List<(string Name, JgsValue Value)> MakerPairs(
        IReadOnlyList<JgsValue> args, int start, string classWord, int line, int col)
    {
        var pairs = new List<(string, JgsValue)>();
        if (args.Count - start == 1 && args[start].Type == JgsType.Struct && !args[start].IsStructArray)
        {
            foreach ((string name, JgsValue value) in args[start].AsStruct)
            {
                pairs.Add((name, value));
            }

            return pairs;
        }

        if ((args.Count - start) % 2 != 0)
        {
            JgsValue last = args[^1];
            throw MakerError(classWord, IsTextScalar(last)
                ? $"No value was given for '{TextOf(last)}'. Name-value pair arguments require a name followed by a value."
                : "Invalid parameter/value pair arguments.", line, col);
        }

        for (int i = start; i < args.Count; i += 2)
        {
            if (!IsTextScalar(args[i]))
            {
                throw MakerError(classWord, "Invalid parameter/value pair arguments.", line, col);
            }

            pairs.Add((TextOf(args[i]), args[i + 1]));
        }

        return pairs;
    }

    private static JgsValue MakeComponent(ComponentMaker maker, IReadOnlyList<JgsValue> args, int line, int col)
    {
        string word = maker.ClassWord;
        string noParent = maker.InGroup ? NeedsAGroup : NeedsAParent;
        int start = 0;
        IUiContainer? parent = null;
        if (args.Count > 0 && LooksLikeAParent(args[0]))
        {
            parent = ContainerNamed(args[0], word, noParent, line, col);
            start = 1;
        }

        // uitree('v0') is the removed tree of old, refused in R2025b's words (probe u9_forms).
        if (maker.Verb == "uitree" && start == 0 && args.Count == 1 && IsTextScalar(args[0])
            && TextOf(args[0]).Equals("v0", StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:uitree:DeprecatedFunctionWeb", "The 'v0' argument for uitree has been removed");
        }

        // A style, where the maker has styles: the word left over when the rest are pairs.
        string? style = null;
        if (maker.Styles is not null && (args.Count - start) % 2 == 1 && args[start].Type != JgsType.Struct)
        {
            string typed = IsTextScalar(args[start]) ? TextOf(args[start]) : string.Empty;
            style = Array.Find(maker.Styles, known => known.Equals(typed, StringComparison.OrdinalIgnoreCase))
                ?? throw MakerError(word,
                    $"'{typed}' is not a valid STYLE for {maker.Verb}. STYLE must be {maker.StyleList ?? $"'{maker.Styles[0]}' or '{maker.Styles[1]}'"}.", line, col);
            start++;
        }

        // A refusal names the maker's class whatever style was asked for: uitree(uf, 'checkbox', ...)
        // refuses as a Tree (probe u9_forms).
        UiComponentModel component = maker.Make(style);
        List<(string Name, JgsValue Value)> pairs = MakerPairs(args, start, word, line, col);
        var options = new List<(string Name, JgsValue Value)>(pairs.Count);
        foreach ((string name, JgsValue value) in pairs)
        {
            string property = PropertyNamed(component, name, word, line, col);
            if (property.Equals("Parent", StringComparison.OrdinalIgnoreCase))
            {
                // 'Parent', [] makes one that belongs to nothing yet (open item 48).
                parent = JgsDetachedComponents.MeansNoParent(value) && !maker.InGroup
                    ? JgsDetachedComponents.Holder
                    : ContainerNamed(value, word, noParent, line, col);
            }
            else
            {
                options.Add((property, value));
            }
        }

        // No parent named: a new figure of its own — and, for a radio or toggle button, a group in it.
        if (parent is null)
        {
            FigureModel figure = NewUiFigure(line, col);
            parent = figure;
            if (maker.InGroup)
            {
                UiButtonGroupModel group = UiButtonGroupModel.ForUiFigure();
                figure.Components.Add(group);
                JgsHandleRegistry.For(group);
                parent = group;
            }
        }

        if (maker.InGroup)
        {
            if (parent is not UiButtonGroupModel group)
            {
                throw MakerError(word, NeedsAGroup, line, col);
            }

            RequireOneKindOfButton(group, component, word, line, col);
        }

        return AddUiComponent(component, parent, options, word, line, col);
    }

    /// <summary>
    /// R2025b lets a button group hold one kind of selectable button: radio buttons, toggle buttons,
    /// or classic controls — never a mixture.
    /// </summary>
    private static void RequireOneKindOfButton(UiButtonGroupModel group, UiObject arriving, string word, int line, int col)
    {
        foreach (UiObject other in group.Components)
        {
            bool selectable = other is UiRadioButtonModel or UiToggleButtonModel
                || other is UiControlModel control && UiButtonGroupModel.IsButton(control);
            if (selectable && other.GetType() != arriving.GetType() && !ReferenceEquals(other, arriving))
            {
                throw MakerError(word,
                    $"Mutual exclusivity violated for ButtonGroup.\nA {word} can only be parented to a ButtonGroup with {word}.", line, col);
            }
        }
    }

    /// <summary>
    /// Adds a component to its parent and applies the options. A refused option means no component,
    /// and every refusal of a creating call carries the class's <c>unknownInput</c> identifier, as
    /// R2025b's do.
    /// </summary>
    private static JgsValue AddUiComponent(
        UiObject component, IUiContainer parent, List<(string Name, JgsValue Value)> options, string word, int line, int col)
    {
        JgsComponentContainers.RequireOpen(parent, component, line, col); // U10: only while a custom component's setup runs
        parent.Components.Add(component);
        JgsValue handle = JgsHandleRegistry.For(component);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(component);
        bool fireCreate = false;

        // A value is checked against the items, the limits and the input type it was given with,
        // wherever in the call it stands (R2025b, probe u5_forms): it is written last.
        static bool IsValue(string name) =>
            name.Equals("Value", StringComparison.OrdinalIgnoreCase) || name.Equals("ValueIndex", StringComparison.OrdinalIgnoreCase);
        options = [.. options.Where(static option => !IsValue(option.Name)), .. options.Where(static option => IsValue(option.Name))];
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
                parent.Components.Remove(component);
            }

            throw MakerError(word, refused.Message, line, col);
        }

        JgsGraphicsProperties.GridMembershipChanged(component);
        if (component is UiCaptionModel button and (UiRadioButtonModel or UiToggleButtonModel) && parent is UiButtonGroupModel group)
        {
            JoinGroup(group, button);
        }

        if (component.Figure is { } home)
        {
            JG.TouchFigure(home);
        }

        if (fireCreate)
        {
            JgsCallbackDispatcher.Current?.FireCreateFcn(component);
        }

        return handle;
    }

    /// <summary>
    /// A radio or toggle button has joined a group (probe <c>u5_forms</c>): the first one is selected
    /// whatever it was made with, and a later one made selected takes the selection.
    /// </summary>
    internal static void JoinGroup(UiButtonGroupModel group, UiCaptionModel button)
    {
        List<UiCaptionModel> others = [.. group.Components.OfType<UiCaptionModel>()
            .Where(b => b is UiRadioButtonModel or UiToggleButtonModel && !ReferenceEquals(b, button))];
        if (others.Count == 0)
        {
            button.Value = true;
            return;
        }

        if (button.Value)
        {
            foreach (UiCaptionModel other in others)
            {
                other.Value = false;
            }
        }
    }

    /// <summary>The selected radio or toggle button of a <c>uifigure</c>'s group, or null.</summary>
    internal static UiCaptionModel? SelectedButton(UiButtonGroupModel group) =>
        group.Components.OfType<UiCaptionModel>().FirstOrDefault(static b => b is UiRadioButtonModel or UiToggleButtonModel && b.Value);

    // --- uigridlayout --------------------------------------------------------------------------------

    private const string GridSizeText = "Grid size must be a 1x2 vector of positive numbers representing the number of rows and columns.";

    private static JgsValue UiGridLayout(IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string word = "GridLayout";
        int start = 0;
        IUiContainer? parent = null;
        if (args.Count > 0 && args[0].Type == JgsType.Number && JgsHandleRegistry.TryGet(args[0], out _))
        {
            parent = ContainerNamed(args[0], word, NeedsAParent, line, col);
            start = 1;
        }
        else if (args.Count > 0 && args[0].Type == JgsType.Array && args[0].ArrayLength == 0 && !args[0].IsStringArray)
        {
            throw MakerError(word, NeedsAParent, line, col);
        }

        // The size, when the next argument is not a name: [rows columns].
        (int Rows, int Columns)? size = null;
        if (args.Count > start && !IsTextScalar(args[start]))
        {
            JgsValue given = args[start];
            string kind = ClassOf(given, JgsDialect.Matlab);
            double[] numbers = given.Type == JgsType.Array && given.Rows == 1 && kind != "logical" && JgsNumericClasses.Parse(kind) is not null
                && !given.IsStringArray && !given.IsCharMatrix
                ? ToDoubles("uigridlayout", given, line, col)
                : [];
            if (numbers.Length != 2 || numbers.Any(static x => x < 1 || x != System.Math.Floor(x) || !double.IsFinite(x)))
            {
                throw MakerError(word, GridSizeText, line, col);
            }

            size = ((int)numbers[0], (int)numbers[1]);
            start++;
        }

        var grid = new UiGridLayoutModel();
        if (size is { } both)
        {
            grid.Rows = [.. Enumerable.Repeat(UiGridTrack.One, both.Rows)];
            grid.Columns = [.. Enumerable.Repeat(UiGridTrack.One, both.Columns)];
        }

        List<(string Name, JgsValue Value)> pairs = MakerPairs(args, start, word, line, col);
        var options = new List<(string Name, JgsValue Value)>(pairs.Count);
        foreach ((string name, JgsValue value) in pairs)
        {
            string property = PropertyNamed(grid, name, word, line, col);
            if (property.Equals("Parent", StringComparison.OrdinalIgnoreCase))
            {
                parent = JgsDetachedComponents.MeansNoParent(value)
                    ? JgsDetachedComponents.Holder // open item 48
                    : ContainerNamed(value, word, NeedsAParent, line, col);
            }
            else
            {
                options.Add((property, value));
            }
        }

        parent ??= NewUiFigure(line, col);
        return AddUiComponent(grid, parent, options, word, line, col);
    }

    // --- uiaxes --------------------------------------------------------------------------------------

    /// <summary>
    /// <c>uiaxes</c>: an axes with the app-building defaults, placed in pixels at
    /// <c>[10 10 400 300]</c> in a figure or a container, and never the current axes. With no
    /// parent it makes a <c>uifigure</c> of its own.
    /// </summary>
    private static JgsValue UiAxes(IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string word = "UIAxes";
        int start = 0;
        GraphObject? parent = null;
        GraphObject ParentOfAxes(JgsValue value)
        {
            if (value.Type != JgsType.Number || !JgsHandleRegistry.TryGet(value, out JgsHandleEntry? named))
            {
                throw MakerError(word, NeedsAParent, line, col);
            }

            return named.Target is FigureModel or UiContainerModel and not UiTabGroupModel
                ? named.Target
                : throw MakerError(word, $"UIAxes cannot be a child of {JgsGraphicsCallbackValues.ClassWord(named.Target)}.", line, col);
        }

        if (args.Count > 0 && LooksLikeAParent(args[0]))
        {
            parent = ParentOfAxes(args[0]);
            start = 1;
        }

        if ((args.Count - start) % 2 != 0 || Enumerable.Range(0, (args.Count - start) / 2).Any(i => !IsTextScalar(args[start + (2 * i)])))
        {
            throw MakerError(word, "Invalid parameter/value pair arguments.", line, col);
        }

        for (int i = start; i + 1 < args.Count; i += 2)
        {
            if (TextOf(args[i]).Equals("Parent", StringComparison.OrdinalIgnoreCase))
            {
                // 'Parent', [] makes one that belongs to nothing yet (open item 48).
                parent = JgsDetachedComponents.MeansNoParent(args[i + 1])
                    ? JgsDetachedComponents.HolderFigure
                    : ParentOfAxes(args[i + 1]);
            }
        }

        parent ??= NewUiFigure(line, col);
        FigureModel figure = parent as FigureModel ?? ((UiContainerModel)parent).Figure
            ?? throw MakerError(word, NeedsAParent, line, col);
        AxesModel axes = figure.AddAxes();
        axes.Container = parent as UiContainerModel;

        // What a UIAxes is: placed by its outer rectangle in pixels, with nothing behind its cell
        // (BackgroundColor 'none') and its toolbar showing.
        axes.Units = UiUnits.Pixels;
        axes.PixelBounds = new Rect2D(10, 10, 400, 300);
        axes.Toolbar.Visible = true;
        axes.ReplaceChildrenOnly = true;
        axes.PositionIsOuter = true;
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(axes);
        try
        {
            for (int i = start; i + 1 < args.Count; i += 2)
            {
                string name = TextOf(args[i]);
                if (name.Equals("Parent", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!JgsGraphicsProperties.TryFind(axes, name, out _))
                {
                    throw MakerError(word, $"Unrecognized property {name} for class UIAxes.", line, col);
                }

                JgsGraphicsProperties.Set(entry, name, args[i + 1], line, col);
            }
        }
        catch (JgsRuntimeException refused)
        {
            using (GraphObjectLifecycle.SuppressNotifications())
            {
                figure.Axes.Remove(axes);
            }

            throw MakerError(word, refused.Message, line, col);
        }

        JgsGraphicsProperties.GridMembershipChanged(axes);
        JG.TouchFigure(figure);

        // A CreateFcn among the options runs once the others are set (open item 80, measured).
        JgsCallbackDispatcher.Current?.FireCreateFcn(axes);
        return JgsHandleRegistry.For(axes);
    }

    // --- the dialogs a figure lays over itself -----------------------------------------------------------

    private const string DialogIds = "MATLAB:uitools:uidialogs:";

    /// <summary>
    /// The figure a dialog is asked of, R2025b's way: not a figure at all is one refusal, and a
    /// figure that is not shown is another — said before any other argument is looked at.
    /// </summary>
    private static FigureModel DialogFigure(JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.Number || !JgsHandleRegistry.TryGet(value, out JgsHandleEntry? named) || named.Target is not FigureModel figure)
        {
            throw new JgsRuntimeException(line, col, DialogIds + "InvalidFigureHandle",
                "Invalid or deleted figure handle. First argument must be a valid figure handle");
        }

        return figure.Visible
            ? figure
            : throw new JgsRuntimeException(line, col, DialogIds + "InvisibleFigure", "Figure handle 'Visible' value must be 'on'.");
    }

    private static IReadOnlyList<string> DialogMessage(JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return value.AsString.Split('\n');
        }

        if (value.IsCharMatrix)
        {
            return value.CharMatrixRows();
        }

        if (value.Type == JgsType.Cell && value.AsCell.All(IsTextScalar))
        {
            return [.. value.AsCell.Select(TextOf)];
        }

        if (value.IsStringArray)
        {
            return [.. Enumerable.Range(0, value.ArrayLength).Select(i => TextOf(value.ElementAt(i)))];
        }

        throw new JgsRuntimeException(line, col, DialogIds + "InvalidMessageText",
            "'Message' must be a char array, string array or a cell array of character vectors.");
    }

    private static string DialogTitle(JgsValue value, int line, int col) =>
        IsTextScalar(value)
            ? TextOf(value)
            : throw new JgsRuntimeException(line, col, DialogIds + "InvalidTitleText", "'Title' must be a char array or a string scalar.");

    private static readonly string[] DialogIcons = ["error", "warning", "info", "question", "success", "none"];

    /// <summary>A dialog's icon: one of the stock words (in any case, or begun), nothing, or a picture.</summary>
    internal static (string Word, UiImage? Image) DialogIcon(JgsValue value, string idPrefix, int line, int col)
    {
        if (IsTextScalar(value))
        {
            string typed = TextOf(value);
            if (typed.Length == 0)
            {
                return (string.Empty, null);
            }

            string? stock = Array.Find(DialogIcons, word => word.Equals(typed, StringComparison.OrdinalIgnoreCase))
                ?? (DialogIcons.Count(word => word.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) == 1
                    ? DialogIcons.First(word => word.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                    : null);
            if (stock is not null)
            {
                return (stock == "none" ? string.Empty : stock, null);
            }

            string path = JgsCallbackDispatcher.Current?.Interpreter?.Host is { } host ? host.Resolve(typed) : typed;
            if (File.Exists(path) && TryReadPicture(path) is { } read)
            {
                return (string.Empty, read);
            }

            throw new JgsRuntimeException(line, col, idPrefix + "InvalidIconSpecified",
                "Icon value must be 'error', 'warning', 'info', 'question', 'success', 'none', a valid file path, or an m-by-n-by-3 color data matrix.");
        }

        if (value.Type == JgsType.Image)
        {
            return (string.Empty, ToUiImage(value.AsImage));
        }

        int[] dims = value.Type == JgsType.Array ? value.Dims : [1, 1];
        string kind = ClassOf(value, JgsDialect.Matlab);
        if (kind is not ("double" or "single" or "uint8" or "uint16") || dims.Length != 3 || dims[2] != 3)
        {
            throw new JgsRuntimeException(line, col, idPrefix + "invalidIconCData",
                "You have specified an invalid CData. Specify an RGB image as an m-by-n-by-3 array of type double, single, uint8, or uint16.");
        }

        double[] data = ToDoubles("Icon", value, line, col);
        double high = kind == "uint8" ? 255 : kind == "uint16" ? 65535 : 1;
        int rows = dims[0];
        int cols = dims[1];
        var pixels = new byte[rows * cols * 4];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int at = ((r * cols) + c) * 4;
                for (int plane = 0; plane < 3; plane++)
                {
                    pixels[at + 2 - plane] = (byte)System.Math.Round(255 * System.Math.Clamp(data[r + (c * rows) + (plane * rows * cols)] / high, 0, 1));
                }

                pixels[at + 3] = 255;
            }
        }

        return (string.Empty, new UiImage(cols, rows, pixels));
    }

    /// <summary>The name-value pairs of a dialog call, matched to the names it takes.</summary>
    private static Dictionary<string, JgsValue> DialogPairs(
        IReadOnlyList<JgsValue> args, int start, string[] names, bool listNames, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, DialogIds + "IncorrectNameValuePairs",
                "Incorrect number of name-value pairs. Each parameter name must be followed by a corresponding value.");
        }

        var pairs = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = start; i < args.Count; i += 2)
        {
            string typed = IsTextScalar(args[i]) ? TextOf(args[i]) : string.Empty;
            string? name = Array.Find(names, known => known.Equals(typed, StringComparison.OrdinalIgnoreCase));
            if (name is null && typed.Length > 0)
            {
                string[] starts = [.. names.Where(known => known.StartsWith(typed, StringComparison.OrdinalIgnoreCase))];
                name = starts.Length == 1 ? starts[0] : null;
            }

            if (name is null)
            {
                throw listNames
                    ? new JgsRuntimeException(line, col, DialogIds + "IncorrectParameterName",
                        $"Invalid parameter name '{typed}'. Valid names are {string.Join(", ", names.Select(static n => $"'{n}'"))}.")
                    : new JgsRuntimeException(line, col, DialogIds + "IncorrectParameterNameShort", $"Invalid parameter name '{typed}'.");
            }

            pairs[name] = args[i + 1];
        }

        return pairs;
    }

    private static string DialogInterpreter(JgsValue value, int line, int col)
    {
        string typed = IsTextScalar(value) ? TextOf(value) : string.Empty;
        return Array.Find(["none", "html", "latex", "tex"], word => word.Equals(typed, StringComparison.OrdinalIgnoreCase))
            ?? throw new JgsRuntimeException(line, col, DialogIds + "InvalidInterpreter",
                "'Interpreter' value must be 'none', 'html', 'latex', or 'tex'.");
    }

    private static JgsValue? DialogCloseFcn(JgsValue value, int line, int col)
    {
        bool sound = value.Type == JgsType.Function || IsTextScalar(value)
            || (value.Type == JgsType.Cell && value.AsCell.Length > 0 && (value.AsCell[0].Type == JgsType.Function || IsTextScalar(value.AsCell[0])))
            || (value.Type is JgsType.Array or JgsType.Cell && value.ArrayLength == 0);
        if (!sound)
        {
            throw new JgsRuntimeException(line, col, DialogIds + "InvalidCloseFcnValue",
                "'CloseFcn' value must be a character vector, a function handle, or a cell array containing a character vector or a function handle.");
        }

        JgsValue? stored = IsTextScalar(value) && TextOf(value).Length == 0 ? null
            : value.Type is JgsType.Array or JgsType.Cell && value.ArrayLength == 0 ? null
            : JgsValue.Share(value);
        if (stored is not null)
        {
            JgsLifetime.Pin(stored);
        }

        return stored;
    }

    /// <summary>Puts a dialog over its figure and shows the figure's window what changed.</summary>
    private static void ShowOverlay(FigureModel figure, UiOverlayModel overlay)
    {
        figure.Overlays.Add(overlay);
        JgsHandleRegistry.For(overlay);
        JG.TouchFigure(figure);
    }

    /// <summary>
    /// <c>uialert(fig, message, title, …)</c>: a message over the figure with an OK button. It does
    /// not wait: the script goes on, and <c>CloseFcn</c> runs when the person closes it.
    /// </summary>
    private static JgsValue UiAlert(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        FigureModel figure = DialogFigure(args[0], line, col);
        var overlay = new UiOverlayModel(UiOverlayKind.Alert)
        {
            Message = DialogMessage(args[1], line, col),
            Title = DialogTitle(args[2], line, col),
            Icon = "error",
        };
        Dictionary<string, JgsValue> pairs = DialogPairs(args, 3, ["Icon", "Modal", "CloseFcn", "Interpreter"], listNames: true, line, col);
        if (pairs.TryGetValue("Icon", out JgsValue? icon))
        {
            (overlay.Icon, overlay.Image) = DialogIcon(icon, DialogIds, line, col);
        }

        if (pairs.TryGetValue("Modal", out JgsValue? modal))
        {
            overlay.Modal = modal.Type is JgsType.Bool or JgsType.Number
                ? modal.IsTruthy
                : throw new JgsRuntimeException(line, col, DialogIds + "InvalidModalValue", "'Modal' value must be true or false.");
        }

        if (pairs.TryGetValue("Interpreter", out JgsValue? interpreter))
        {
            overlay.Interpreter = DialogInterpreter(interpreter, line, col);
        }

        JgsValue? closeFcn = pairs.TryGetValue("CloseFcn", out JgsValue? close) ? DialogCloseFcn(close, line, col) : null;
        ShowOverlay(figure, overlay);
        JgsHandleRegistry.EntryFor(overlay).OverlayCloseFcn = closeFcn;
        return JgsValue.Null;
    }

    /// <summary>
    /// <c>selection = uiconfirm(fig, message, title, …)</c>: a question over the figure, which the
    /// script waits on. With nobody to answer it refuses as R2025b's does. Asked for no output it
    /// shows the question and returns at once, leaving the answer to a <c>CloseFcn</c> (open item 50,
    /// measured), and answers null.
    /// </summary>
    private static JgsValue UiConfirm(
        JGraphScriptGlobals host, CancellationToken cancellationToken, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count < 3)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        // Asked for nothing it still needs a window: R2025b under -batch refuses both forms
        // (oi_app_odds, confirm_invisible).
        RequireSomebodyToAnswer(line, col);
        FigureModel figure = DialogFigure(args[0], line, col);
        var overlay = new UiOverlayModel(UiOverlayKind.Confirm)
        {
            Message = DialogMessage(args[1], line, col),
            Title = DialogTitle(args[2], line, col),
            Icon = "question",
            Options = ["OK", "Cancel"],
        };
        Dictionary<string, JgsValue> pairs = DialogPairs(
            args, 3, ["Options", "Icon", "DefaultOption", "CancelOption", "CloseFcn", "Interpreter"], listNames: true, line, col);
        if (pairs.TryGetValue("Options", out JgsValue? options))
        {
            string[]? words = IsTextScalar(options) ? [TextOf(options)]
                : options.Type == JgsType.Cell && options.AsCell.All(IsTextScalar) ? [.. options.AsCell.Select(TextOf)]
                : options.IsStringArray ? [.. Enumerable.Range(0, options.ArrayLength).Select(i => TextOf(options.ElementAt(i)))]
                : null;
            overlay.Options = words is { Length: >= 1 and <= 4 }
                ? words
                : throw new JgsRuntimeException(line, col, DialogIds + "InvalidOptionsValue",
                    "Options must be a character vector or a string scalar containing 1 option or a cell array of character vectors or a string array containing 1 to 4 options.");
        }

        int Option(string name, int fallback)
        {
            if (!pairs.TryGetValue(name, out JgsValue? given) || (given.Type == JgsType.Array && given.ArrayLength == 0))
            {
                return fallback;
            }

            int at = IsTextScalar(given)
                ? overlay.Options.ToList().FindIndex(word => word == TextOf(given))
                : given.Type == JgsType.Number && given.AsNumber == System.Math.Floor(given.AsNumber) ? (int)given.AsNumber - 1 : -1;
            return at >= 0 && at < overlay.Options.Count
                ? at
                : throw new JgsRuntimeException(line, col, DialogIds + "InvalidDefaultOption",
                    $"'{name}' must be a character vector or a string scalar from the 'Options' cell array or an index of the 'Options' cell array");
        }

        overlay.DefaultOption = Option("DefaultOption", 0);
        overlay.CancelOption = Option("CancelOption", overlay.Options.Count - 1);
        if (pairs.TryGetValue("Icon", out JgsValue? icon))
        {
            (overlay.Icon, overlay.Image) = DialogIcon(icon, DialogIds, line, col);
        }

        if (pairs.TryGetValue("Interpreter", out JgsValue? interpreter))
        {
            overlay.Interpreter = DialogInterpreter(interpreter, line, col);
        }

        JgsValue? closeFcn = pairs.TryGetValue("CloseFcn", out JgsValue? close) ? DialogCloseFcn(close, line, col) : null;
        ShowOverlay(figure, overlay);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(overlay);
        entry.OverlayCloseFcn = closeFcn;

        // Shown, and what the script changed is in the window, before the wait begins; a test's
        // stand-in for the person is told now.
        host.ShowTouchedFigures();
        ScriptComponentFrames.Flush(force: true);
        ScriptGraphicsCallbacks.OverlayShown?.Invoke(overlay);
        if (wanted == 0)
        {
            return JgsValue.Null;
        }

        BlockUntil("uiconfirm", host, cancellationToken,
            () => overlay.BeingDeleted || overlay.Parent is null || figure.BeingDeleted, deadline: null, line, col);
        int answer = entry.OverlayAnswer ?? overlay.CancelOption;
        return JgsValue.Str(overlay.Options[System.Math.Clamp(answer, 0, overlay.Options.Count - 1)]);
    }

    /// <summary>
    /// <c>d = uiprogressdlg(fig, …)</c>: a bar over the figure, answered as an object whose
    /// <c>Value</c> and <c>Message</c> the script moves, and which <c>close(d)</c> takes away.
    /// </summary>
    private static JgsValue UiProgressDlg(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        FigureModel figure = DialogFigure(args[0], line, col);
        var overlay = new UiOverlayModel(UiOverlayKind.Progress) { Options = [] };
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(overlay);
        Dictionary<string, JgsValue> pairs = DialogPairs(
            args, 1,
            ["Value", "Message", "Title", "Indeterminate", "Icon", "ShowPercentage", "Cancelable", "CancelText", "Interpreter"],
            listNames: false, line, col);
        using (JgsGraphicsProperties.CreatingOverlay())
        {
            foreach ((string name, JgsValue value) in pairs)
            {
                JgsGraphicsProperties.Set(entry, name, value, line, col);
            }
        }

        ShowOverlay(figure, overlay);
        return JgsHandleRegistry.For(overlay);
    }

    /// <summary>Takes a dialog off its figure: <c>close(d)</c>, <c>delete(d)</c>, or the figure closing.</summary>
    internal static void RemoveOverlay(UiOverlayModel overlay)
    {
        if (overlay.Parent is FigureModel figure)
        {
            figure.Overlays.Remove(overlay);
            JG.TouchFigure(figure);
        }
    }

    /// <summary>
    /// <c>focus(c)</c>: gives a figure or a component the keyboard. On a figure that is not shown it
    /// warns and does nothing, as R2025b's does.
    /// </summary>
    private static JgsValue Focus(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction", "Unrecognized function or variable 'focus'.");
        }

        JgsValue asked = args[0];
        if (asked.Type == JgsType.Array && asked.ArrayLength != 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:nonLogicalConditional",
                "Operands to the logical AND (&&) and OR (||) operators must be convertible to logical scalar values. Use the ANY or ALL functions to reduce operands to logical scalar values.");
        }

        if (!JgsHandleRegistry.TryGet(asked.Type == JgsType.Array ? asked.ElementAt(0) : asked, out JgsHandleEntry? named))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
                $"Undefined function 'focus' for input arguments of type '{ClassOf(asked, JgsDialect.Matlab)}'.");
        }

        GraphObject target = named.Target;
        // A gauge and a lamp take no keyboard (U9): focus has no method for them.
        if (target is not (FigureModel or UiComponentModel) || target is UiGaugeModel or UiLampModel)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
                $"Undefined function 'focus' for input arguments of type '{JgsGraphicsProperties.FullClassOf(target)}'.");
        }

        FigureModel? figure = target as FigureModel ?? (target as UiObject)?.Figure;
        if (figure is null || !figure.Visible)
        {
            if (JgsCallbackDispatcher.Current?.Interpreter?.Host is { } host)
            {
                Warn(host, $"MATLAB:ui:{JgsGraphicsCallbackValues.ClassWord(target)}:FigureNotFocusable",
                    "Focusing this component is not supported when the 'Visible' value of the figure is 'off'.");
            }

            return JgsValue.Null;
        }

        if (target is UiComponentModel component)
        {
            component.RequestFocus();
        }

        JG.TouchFigure(figure);
        return JgsValue.Null;
    }
}
