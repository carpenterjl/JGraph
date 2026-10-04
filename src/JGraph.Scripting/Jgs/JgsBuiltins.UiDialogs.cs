using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MATLAB's classic dialogs (app-building plan, U4): <c>dialog</c>, <c>msgbox</c> and its three
/// wrappers <c>errordlg</c>, <c>warndlg</c> and <c>helpdlg</c>. In MATLAB these are figures built
/// from the same objects a script builds its own windows from, and they are that here too: a
/// script that finds the dialog's text by its tag and changes its font, or waits on the figure,
/// finds what R2025b gives it.
/// </summary>
/// <remarks>
/// The object trees, the tags and the arithmetic of the layout are R2025b's (probes
/// <c>u0_dialogs</c>, <c>u1w_dialogs</c> and <c>u4_dialogs</c>): the margins, the button sizes and
/// which measure decides a width are its own. The measures themselves — how wide a line of text
/// is — are this build's, as a control's <c>Extent</c> is (ADR 0200), so a dialog's size can differ
/// from R2025b's by what the two fonts differ by.
/// </remarks>
internal static partial class JgsBuiltins
{
    private const string DialogButtonDown = "if isempty(allchild(gcbf)), close(gcbf), end";

    private const string DialogFontName = "MS Sans Serif";

    private const double DialogFontSize = 8;

    private static void RegisterUiDialogBuiltins(JgsEnvironment env, JGraphScriptGlobals host, CancellationToken cancellationToken)
    {
        void DefineMaker(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                AutoCallsBare = true,
                BindsAnsAsStatement = false,
                KeepsStringArguments = true,
            }));

        DefineMaker("dialog", (args, line, col) => JgsHandleRegistry.For(Dialog(args, line, col)));
        DefineMaker("msgbox", (args, line, col) => MsgBox(env, host, args, line, col));
        DefineMaker("errordlg", (args, line, col) => MessageDialog(env, host, "errordlg", args, line, col));
        DefineMaker("warndlg", (args, line, col) => MessageDialog(env, host, "warndlg", args, line, col));
        DefineMaker("helpdlg", (args, line, col) => MessageDialog(env, host, "helpdlg", args, line, col));
        RegisterWaitbarBuiltin(env, host);
        RegisterBlockingDialogBuiltins(env, host, cancellationToken);
        RegisterNativeDialogBuiltins(env, host);
    }

    // --- small things every dialog uses ------------------------------------------------------------

    private static JgsValue Text(string value) => JgsValue.Str(value);

    private static JgsValue Vec(params double[] values) => JgsGraphicsProperties.Row(values);

    /// <summary>Lines as a column cell, which is how a dialog hands text to its objects.</summary>
    private static JgsValue LinesCell(IReadOnlyList<string> lines)
    {
        JgsValue cell = JgsValue.Cell([.. lines.Select(static l => JgsValue.Str(l))]);
        cell.Reshape(lines.Count, lines.Count == 0 ? 0 : 1);
        return cell;
    }

    private static void SetAll(GraphObject target, int line, int col, params (string Name, JgsValue Value)[] pairs)
    {
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(target);
        foreach ((string name, JgsValue value) in pairs)
        {
            JgsGraphicsProperties.Set(entry, name, value, line, col);
        }
    }

    private static double[] Read(GraphObject target, string name, int line, int col) =>
        ToDoubles(name, JgsGraphicsProperties.Get(JgsHandleRegistry.EntryFor(target), name, line, col), line, col);

    /// <summary>A callback written here rather than in a script: R2025b's are nested functions.</summary>
    private static JgsValue DialogCallback(string name, Action<JgsValue, JgsValue> body) =>
        JgsValue.Function(new BuiltinFunction(name, (args, _, _) =>
        {
            body(args.Count > 0 ? args[0] : JgsValue.Null, args.Count > 1 ? args[1] : JgsValue.Null);
            return JgsValue.Null;
        })
        { BindsAnsAsStatement = false });

    /// <summary>The key an event data names, or empty.</summary>
    private static string KeyOf(JgsValue eventData) =>
        eventData.Type == JgsType.Struct && eventData.AsStruct.TryGetValue("Key", out JgsValue? key) && IsTextScalar(key)
            ? TextOf(key)
            : string.Empty;

    private static UiControlModel NewControl(FigureModel figure, int line, int col, params (string Name, JgsValue Value)[] pairs)
    {
        var args = new List<JgsValue> { JgsHandleRegistry.For(figure) };
        foreach ((string name, JgsValue value) in pairs)
        {
            args.Add(Text(name));
            args.Add(value);
        }

        return (UiControlModel)JgsHandleRegistry.Require(UiControl(args, line, col), line, col).Target;
    }

    private static AxesModel NewAxes(FigureModel figure, int line, int col, params (string Name, JgsValue Value)[] pairs)
    {
        var args = new List<JgsValue> { Text("Parent"), JgsHandleRegistry.For(figure) };
        foreach ((string name, JgsValue value) in pairs)
        {
            args.Add(Text(name));
            args.Add(value);
        }

        return (AxesModel)JgsHandleRegistry.Require(Axes(args, line, col), line, col).Target;
    }

    private static void DeleteGraphics(GraphObject target, JGraphScriptGlobals host)
    {
        if (!target.BeingDeleted && JgsHandleRegistry.TryGetEntry(target, out _))
        {
            TryDeleteGraphics(JgsHandleRegistry.For(target), host);
        }
    }

    /// <summary>
    /// Text handed to a dialog as its lines, R2025b's way: a character matrix is its rows, a cell
    /// its elements, a string array its strings — and anything else is refused by name.
    /// </summary>
    private static List<string> DialogLines(JgsValue value, int line, int col)
    {
        if (value.IsCharMatrix)
        {
            return [.. value.CharMatrixRows()];
        }

        string kind = ClassOf(value, JgsDialect.Matlab);
        if (value.Type == JgsType.Cell)
        {
            return [.. value.AsCell.Select(element => IsTextScalar(element) || element.Type == JgsType.String
                ? TextOf(element)
                : throw new JgsRuntimeException(line, col, "MATLAB:invalidConversion",
                    $"Conversion to cellstr from {ClassOf(element, JgsDialect.Matlab)} is not possible."))];
        }

        if (kind == "string")
        {
            int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
            return [.. Enumerable.Range(0, count).Select(i => TextOf(value.Type == JgsType.Array ? value.ElementAt(i) : value))];
        }

        if (kind == "char")
        {
            // An empty character array has no rows, and so no lines.
            return value.Type == JgsType.String && value.AsString.Length > 0 ? [value.AsString] : [];
        }

        throw new JgsRuntimeException(line, col, "MATLAB:dialogCellstrHelper:invalidType",
            $"Expected input to be one of these types:\n\nchar, cell\n\nInstead its type was {kind}.");
    }

    /// <summary>A string scalar as the character vector R2025b's dialogs turn it into; anything else as it is.</summary>
    private static JgsValue AsChars(JgsValue value) =>
        value.IsStringArray && value.ArrayLength == 1 ? Text(TextOf(value)) : value;

    // --- placement ------------------------------------------------------------------------------------

    /// <summary>
    /// Where R2025b puts a dialog of a given pixel size: over the figure whose callback asked for it,
    /// or on the primary screen — centred across, two thirds of the way up, each edge on a whole pixel.
    /// </summary>
    private static Rect2D NiceDialogPixels(double width, double height)
    {
        Rect2D over = UiScreen.Primary;
        if (FigureOf(JgsGraphicsCallbackState.CallbackObject) is { Visible: true } asking)
        {
            over = JgsGraphicsProperties.FigurePixels(asking);
        }

        double x = over.X + ((over.Width - width) / 2);
        double y = over.Y + ((over.Height - height) * 2 / 3);
        double left = System.Math.Round(x, MidpointRounding.AwayFromZero);
        double bottom = System.Math.Round(y, MidpointRounding.AwayFromZero);
        return new Rect2D(
            left,
            bottom,
            System.Math.Round(x + width, MidpointRounding.AwayFromZero) - left,
            System.Math.Round(y + height, MidpointRounding.AwayFromZero) - bottom);
    }

    /// <summary>Sizes a dialog, in pixels, and puts it where R2025b would; then keeps it on screen.</summary>
    private static void PlaceDialog(FigureModel figure, double widthPixels, double heightPixels, int line, int col)
    {
        JgsGraphicsProperties.SetFigurePixels(figure, NiceDialogPixels(widthPixels, heightPixels));
        MoveGui([JgsHandleRegistry.For(figure)], line, col);
    }

    // --- dialog -----------------------------------------------------------------------------------------

    private static readonly string[] DialogOwnNames =
    [
        "ButtonDownFcn", "Colormap", "IntegerHandle", "InvertHardcopy", "HandleVisibility", "MenuBar", "NumberTitle",
        "PaperPositionMode", "WindowStyle", "Resize", "Visible", "DockControls",
    ];

    /// <summary>
    /// <c>dialog(Name, Value, …)</c>: a figure with a dialog's defaults — modal, not resizable, no
    /// number, no menu bar, its handle visible to callbacks only — and whatever else was asked.
    /// </summary>
    private static FigureModel Dialog(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:dialog:NeedParamPairs", "Must have param/value pairs.");
        }

        var options = new List<(string Name, JgsValue Value)>();
        for (int i = 0; i < args.Count; i += 2)
        {
            options.Add((IsTextScalar(args[i]) ? TextOf(args[i]) : string.Empty, AsChars(args[i + 1])));
        }

        return MakeDialog(options, line, col);
    }

    /// <summary>Makes a dialog's figure: R2025b's defaults first, then the caller's options over them.</summary>
    private static FigureModel MakeDialog(List<(string Name, JgsValue Value)> options, int line, int col)
    {
        var figure = new FigureModel
        {
            IntegerHandle = false,
            NumberTitle = false,
            Resizable = false,
            MenuBar = false,
            Visible = false,
            WindowStyle = FigureWindowStyle.Modal,
            Background = UiFigureColor.ToColor(),
            Size = new Size2D(640, 480),
            Name = string.Empty,
        };

        int number = JG.RegisterHiddenFigure(figure);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(figure);
        entry.HandleVisibility = "callback";
        try
        {
            // The dialog's own defaults give way to an option of the same name; Visible is written
            // last, so nothing is seen half made.
            JgsValue visible = Text("on");
            if (!options.Any(static o => o.Name.Equals("ButtonDownFcn", StringComparison.OrdinalIgnoreCase)))
            {
                JgsGraphicsProperties.Set(entry, "ButtonDownFcn", Text(DialogButtonDown), line, col);
            }

            foreach ((string name, JgsValue value) in options.Where(o => DialogOwnNames.Contains(o.Name, StringComparer.OrdinalIgnoreCase)))
            {
                if (name.Equals("Visible", StringComparison.OrdinalIgnoreCase))
                {
                    visible = value;
                }
                else if (!name.Equals("InvertHardcopy", StringComparison.OrdinalIgnoreCase))
                {
                    JgsGraphicsProperties.Set(entry, name, value, line, col);
                }
            }

            foreach ((string name, JgsValue value) in options.Where(o => !DialogOwnNames.Contains(o.Name, StringComparer.OrdinalIgnoreCase)))
            {
                if (!JgsGraphicsProperties.TryFind(figure, name, out _))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hg:InvalidProperty",
                        $"Unrecognized property {name} for class Figure.");
                }

                JgsGraphicsProperties.Set(entry, name, value, line, col);
            }

            JgsGraphicsProperties.Set(entry, "Visible", visible, line, col);
        }
        catch
        {
            using (GraphObjectLifecycle.SuppressNotifications())
            {
                JG.CloseFigure(number);
            }

            throw;
        }

        JgsGraphicsProperties.RememberSize(figure);
        return figure;
    }

    // --- msgbox -----------------------------------------------------------------------------------------

    /// <summary>What the last argument of a <c>msgbox</c> says about how it is made.</summary>
    private static (bool Given, string Mode, string Interpreter) CreateFlag(JgsValue? mode, int line, int col)
    {
        string interpreter = "none";
        if (mode is null || IsEmptyValue(mode))
        {
            return (false, "non-modal", interpreter);
        }

        if (mode.Type == JgsType.Cell && mode.AsCell.Length == 1)
        {
            mode = mode.AsCell[0];
        }

        if (mode.Type == JgsType.Struct && !mode.IsStructArray)
        {
            Dictionary<string, JgsValue> fields = mode.AsStruct;
            if (!fields.TryGetValue("Interpreter", out JgsValue? named) || !fields.TryGetValue("WindowStyle", out JgsValue? style))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:msgbox:InvalidInput",
                    "Must specify fields Interpreter and WindowStyle when passing struct as last input argument.");
            }

            if (!IsTextScalar(named) || !IsTextScalar(style))
            {
                return (false, "non-modal", IsTextScalar(named) ? TextOf(named) : interpreter);
            }

            interpreter = TextOf(named);
            mode = style;
        }

        if (IsTextScalar(mode))
        {
            string word = TextOf(mode);
            foreach (string known in new[] { "non-modal", "modal", "replace" })
            {
                if (known.Equals(word, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, known, interpreter);
                }
            }
        }

        return (false, "non-modal", interpreter);
    }

    /// <summary><c>errordlg</c>, <c>warndlg</c> and <c>helpdlg</c>: a <c>msgbox</c> with an icon and words of its own.</summary>
    private static JgsValue MessageDialog(
        JgsEnvironment env, JGraphScriptGlobals host, string verb, IReadOnlyList<JgsValue> args, int line, int col)
    {
        (string icon, string words, string title, int most) = verb switch
        {
            "errordlg" => ("error", "This is the default error.", "Error Dialog", 3),
            "warndlg" => ("warn", "This is the default warning.", "Warning Dialog", 3),
            _ => ("help", "This is the default help.", "Help Dialog", 2),
        };
        if (args.Count > most)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsValue message = args.Count > 0 ? AsChars(args[0]) : Text(words);
        JgsValue name = args.Count > 1 ? AsChars(args[1]) : Text(title);
        JgsValue mode = verb == "helpdlg" ? Text("replace") : args.Count > 2 ? AsChars(args[2]) : Text("non-modal");
        if (verb == "errordlg" && IsTextScalar(mode))
        {
            mode = TextOf(mode) switch { "on" => Text("replace"), "off" => Text("non-modal"), _ => mode };
        }

        // The wrappers hand the lines on as a cell, having checked them themselves.
        return MsgBox(env, host, [LinesCell(DialogLines(message, line, col)), name, Text(icon), mode], line, col);
    }

    /// <summary>
    /// <c>msgbox(message, title, icon, iconData, iconMap, mode)</c>: a figure holding the message,
    /// an icon when one was asked for, and an OK button that deletes it.
    /// </summary>
    private static JgsValue MsgBox(JgsEnvironment env, JGraphScriptGlobals host, IReadOnlyList<JgsValue> given, int line, int col)
    {
        if (given.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (given.Count > 6)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        JgsValue[] args = [.. given.Select(AsChars)];
        List<string> body = DialogLines(args[0], line, col);
        (bool flag, string mode, string interpreter) = CreateFlag(args.Length > 1 ? args[^1] : null, line, col);
        if (!new[] { "latex", "tex", "none" }.Contains(interpreter, StringComparer.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:msgbox:interpreter", "Bad value for text property: 'Interpreter'");
        }

        JgsValue title = Text(" ");
        string icon = "none";
        JgsValue? iconData = null;
        JgsValue? iconMap = null;
        switch (args.Length)
        {
            case 2:
                if (!flag)
                {
                    title = args[1];
                }

                break;
            case 3:
                title = args[1];
                if (!flag)
                {
                    icon = IsTextScalar(args[2]) ? TextOf(args[2]) : string.Empty;
                }

                break;
            case 4:
                title = args[1];
                icon = IsTextScalar(args[2]) ? TextOf(args[2]) : string.Empty;
                if (!flag)
                {
                    iconData = args[3];
                }

                break;
            case 5:
                if (flag)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:msgbox:colormap",
                        "A Colormap must be specified when calling MSGBOX with 5 input arguments.");
                }

                title = args[1];
                icon = IsTextScalar(args[2]) ? TextOf(args[2]) : string.Empty;
                if (!icon.Equals("custom", StringComparison.OrdinalIgnoreCase))
                {
                    Warn(host, "MATLAB:msgbox:customicon", "Icon must be 'custom' when specifying icon data in MSGBOX");
                    icon = "custom";
                }

                iconData = args[3];
                iconMap = args[4];
                break;
            case 6:
                title = args[1];
                icon = IsTextScalar(args[2]) ? TextOf(args[2]) : string.Empty;
                iconData = args[3];
                iconMap = args[4];
                break;
        }

        icon = icon.ToLowerInvariant();
        switch (icon)
        {
            case "custom":
                if (iconData is null || IsEmptyValue(iconData))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:msgbox:icondata", "Must specify icon data when Icon is 'custom'.");
                }

                if (!IsNumericClass(iconData))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:msgbox:IncorrectIconDataType",
                        "Must specify numeric data for icon when Icon is 'custom'.");
                }

                if (iconMap is not null && !IsNumericClass(iconMap))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:msgbox:IncorrectIconColormap",
                        "Colormap must be an array of type double, float or sparse when Icon is 'custom'.");
                }

                break;
            case "none" or "help" or "warn" or "error":
                break;
            default:
                Warn(host, "MATLAB:msgbox:iconstring", "Invalid character vector for Icon in MSGBOX.");
                icon = "none";
                break;
        }

        // R2025b's layout, in points: a 7-point margin, a 40 by 17 button, a 24-point icon.
        const double margin = 7;
        const double iconSide = 32 * 72.0 / 96;
        const double okWidth = 40;
        const double okHeight = 17;
        bool hasIcon = icon != "none";
        double figWidth = hasIcon ? 190 : 150;
        double textWidth = hasIcon ? figWidth - (2 * margin) - iconSide : figWidth - (2 * margin);
        double figHeight = 50;
        double textX = margin;
        double textY = margin + margin + okHeight;
        double textHeight = figHeight - margin - textY;

        // The title is the figure's Name, and R2025b refuses there what is not text.
        if (!IsTextScalar(title) && !(title.Type == JgsType.String))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:class:RequireString",
                "Error setting property 'Name' of class 'Figure':\nValue must be a character vector or a string scalar.");
        }

        string titleText = TextOf(title);
        string tag = "Msgbox_" + titleText;

        // Anything but 'non-modal' takes over a message box of the same name instead of adding one.
        FigureModel? figure = null;
        if (mode != "non-modal")
        {
            List<FigureModel> old = [.. JgsGraphicsProperties.RootFigures(hidden: true).OfType<FigureModel>()
                .Where(f => f.Tag == tag && f.Name == titleText).Reverse()];
            if (old.Count > 0)
            {
                figure = old[0];
                foreach (FigureModel extra in old.Skip(1))
                {
                    DeleteGraphics(extra, host);
                }

                foreach (GraphObject child in JgsGraphicsProperties.ChildrenOf(figure).ToList())
                {
                    DeleteGraphics(child, host);
                }

                JG.TouchFigure(figure);
            }
        }

        string windowStyle = mode == "modal" ? "modal" : "normal";
        JgsValue keys = DialogCallback("doKeyPress", (source, eventData) =>
        {
            if (KeyOf(eventData) is "return" or "space" or "escape"
                && JgsHandleRegistry.TryGet(source, out JgsHandleEntry? pressed) && FigureOf(pressed.Target) is { } box)
            {
                DeleteGraphics(box, host);
            }
        });

        if (figure is null)
        {
            figure = MakeDialog(
            [
                ("Name", title), ("Pointer", Text("arrow")), ("Units", Text("points")), ("Visible", Text("off")),
                ("KeyPressFcn", keys), ("WindowStyle", Text(windowStyle)), ("ToolBar", Text("none")),
                ("HandleVisibility", Text("on")), ("Tag", Text(tag)),
            ], line, col);
        }
        else
        {
            SetAll(figure, line, col, ("WindowStyle", Text(windowStyle)), ("HandleVisibility", Text("on")), ("Visible", Text("off")));
        }

        try
        {
            UiControlModel ok = NewControl(figure, line, col,
                ("FontUnits", Text("points")), ("FontSize", JgsValue.Number(DialogFontSize)), ("FontName", Text(DialogFontName)),
                ("Style", Text("pushbutton")), ("Units", Text("points")),
                ("Position", Vec((figWidth - okWidth) / 2, margin, okWidth, okHeight)),
                ("Callback", Text("delete(gcbf)")), ("KeyPressFcn", keys), ("String", Text("OK")),
                ("HorizontalAlignment", Text("center")), ("Tag", Text("OKButton")));

            // A text control of the message's width does the wrapping and gives one measure of it.
            UiControlModel sizer = NewControl(figure, line, col,
                ("FontUnits", Text("points")), ("FontSize", JgsValue.Number(DialogFontSize)), ("FontName", Text(DialogFontName)),
                ("Style", Text("text")), ("Units", Text("points")), ("Position", Vec(textX, textY, textWidth, textHeight)),
                ("String", Text(" ")), ("Tag", Text("MessageBox")), ("HorizontalAlignment", Text("left")));
            List<string> paragraphs = [.. body.SelectMany(static l => l.Split('\n'))];
            JgsValue[] wrap = TextWrap([JgsHandleRegistry.For(sizer), LinesCell(paragraphs), JgsValue.Number(75)], 2, line, col);
            double[] wrappedBox = ToDoubles("msgbox", wrap[1], line, col);
            DeleteGraphics(sizer, host);

            AxesModel axes = NewAxes(figure, line, col, ("Position", Vec(0, 0, 1, 1)), ("Visible", Text("off")));
            TextAnnotation message = DialogText(axes, UiUnits.Points, wrap[0], interpreter, "MessageBox", line, col);
            double[] extent = Read(message, "Extent", line, col);
            textWidth = System.Math.Max(textWidth, System.Math.Max(wrappedBox[2], extent[2]));
            textHeight = System.Math.Max(textHeight, System.Math.Max(wrappedBox[3], extent[3]));

            double iconX = margin;
            double iconY;
            if (hasIcon)
            {
                textX = iconX + iconSide + margin;
                figWidth = textX + textWidth + margin;
                if (iconSide > textHeight)
                {
                    iconY = margin + okHeight + margin;
                    textY = iconY + ((iconSide - textHeight) / 2);
                    figHeight = iconY + iconSide + margin;
                }
                else
                {
                    textY = margin + okHeight + margin;
                    iconY = textY + ((textHeight - iconSide) / 2);
                    figHeight = textY + textHeight + margin;
                }
            }
            else
            {
                iconY = 0;
                figWidth = textWidth + (2 * margin);
                textY = margin + okHeight + margin;
                figHeight = textY + textHeight + margin;
            }

            PlaceDialog(figure, figWidth * 96 / 72, figHeight * 96 / 72, line, col);
            SetAll(ok, line, col, ("Position", Vec((figWidth - okWidth) / 2, margin, okWidth, okHeight)));

            // A message box asked for from a modal figure's callback is modal too.
            if (FigureOf(JgsGraphicsCallbackState.CallbackObject) is { WindowStyle: FigureWindowStyle.Modal })
            {
                figure.WindowStyle = FigureWindowStyle.Modal;
            }

            SetAll(message, line, col, ("Position", Vec(textX, textY, 0)));
            if (hasIcon)
            {
                AxesModel iconAxes = NewAxes(figure, line, col,
                    ("Units", Text("points")), ("Position", Vec(iconX, iconY, iconSide, iconSide)), ("Tag", Text("IconAxes")));
                DialogIcon(env, iconAxes, icon, iconData, iconMap, line, col);
            }

            SetAll(figure, line, col, ("HandleVisibility", Text("callback")), ("Visible", Text("on")));
        }
        catch
        {
            DeleteGraphics(figure, host);
            throw;
        }

        return JgsHandleRegistry.For(figure);
    }

    private static bool IsNumericClass(JgsValue value) =>
        ClassOf(value, JgsDialect.Matlab) is var kind && kind != "logical" && JgsNumericClasses.Parse(kind) is not null;

    /// <summary>
    /// The text object a dialog shows its words in: placed in device units in an axes that fills
    /// the figure, at the lower left of the words, in the dialogs' font.
    /// </summary>
    private static TextAnnotation DialogText(
        AxesModel axes, UiUnits units, JgsValue lines, string interpreter, string tag, int line, int col,
        string fontName = DialogFontName)
    {
        TextAnnotation label = axes.AddText(0, 0, string.Empty);
        label.DeviceUnits = units;
        label.FontSize = DialogFontSize;
        label.FontFamily = fontName;
        label.HorizontalAlignment = Core.Drawing.HorizontalAlignment.Left;
        label.VerticalAlignment = Core.Drawing.VerticalAlignment.Bottom;
        label.Color = Core.Drawing.Color.FromScRgb(0.129411764705882, 0.129411764705882, 0.129411764705882);
        SetAll(label, line, col, ("String", lines), ("Interpreter", Text(interpreter)), ("Tag", Text(tag)));
        return label;
    }

    /// <summary>
    /// Puts a dialog's icon in its axes: one of the four standard pictures, or the caller's own
    /// image and colormap. The axes is then fitted to the picture, turned over and hidden.
    /// </summary>
    private static void DialogIcon(
        JgsEnvironment env, AxesModel axes, string icon, JgsValue? data, JgsValue? map, int line, int col)
    {
        JgsValue axesHandle = JgsHandleRegistry.For(axes);
        var args = new List<JgsValue> { Text("CData"), Text("Parent"), axesHandle };
        if (icon == "custom")
        {
            args[0] = Text("CData");
            args.Insert(1, data!);
        }
        else
        {
            (JgsValue picture, JgsValue alpha) = UiDialogIcons.Standard(icon);
            args.Insert(1, picture);
            args.Add(Text("AlphaData"));
            args.Add(alpha);
        }

        JgsValue image = CallBuiltin(env, "msgbox", "image", [.. args], line, col);
        if (icon == "custom" && map is not null && !IsEmptyValue(map))
        {
            SetAll(axes, line, col, ("Colormap", map));
        }

        GraphObject drawn = JgsHandleRegistry.Require(image, line, col).Target;
        double[] across = Read(drawn, "XData", line, col);
        double[] down = Read(drawn, "YData", line, col);
        if (across.Length > 0 && down.Length > 0)
        {
            SetAll(axes, line, col,
                ("XLim", Vec(across[0] - 0.5, across[^1] + 0.5)), ("YLim", Vec(down[0] - 0.5, down[^1] + 0.5)));
        }

        SetAll(axes, line, col, ("Visible", Text("off")), ("YDir", Text("reverse")));
    }
}
