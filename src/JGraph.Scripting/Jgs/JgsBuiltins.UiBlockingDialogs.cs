using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Objects;
using JGraph.Objects.Annotations;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>waitbar</c>, and the three classic dialogs that hold a script until they are answered —
/// <c>questdlg</c>, <c>inputdlg</c> and <c>listdlg</c> (app-building plan, U4). Like the message
/// boxes they are figures built from controls, laid out by R2025b's arithmetic
/// (<c>u1w_dialogs</c>), and the blocking ones wait in <c>uiwait</c>.
/// </summary>
/// <remarks>
/// Without somebody to answer, R2025b refuses the blocking three before it reads their arguments
/// (<c>MATLAB:hg:NonInteractiveFunctionSupport</c>), and so does this. A test answers them through
/// <see cref="ScriptGraphicsCallbacks.BlockingDialogShown"/>, which is told of the dialog's figure
/// once it is built and presses its controls the way the window does.
/// </remarks>
internal static partial class JgsBuiltins
{
    private const string NonInteractiveId = "MATLAB:hg:NonInteractiveFunctionSupport";

    private const string NonInteractiveText =
        "Creating dialog boxes that block execution is not supported when MATLAB is in a configuration that is non-interactive or disables window display.";

    /// <summary>R2025b's refusal for a blocking dialog with nobody to answer it.</summary>
    private static void RequireSomebodyToAnswer(int line, int col)
    {
        if (!CanInteract)
        {
            throw new JgsRuntimeException(line, col, NonInteractiveId, NonInteractiveText);
        }
    }

    /// <summary>Shows a built dialog, tells a test that is standing in for the user, and waits on it.</summary>
    private static void WaitOnDialog(
        FigureModel figure, UiControlModel? focus, JGraphScriptGlobals host, CancellationToken cancellationToken, int line, int col)
    {
        focus?.RequestFocus();
        ScriptGraphicsCallbacks.BlockingDialogShown?.Invoke(figure);
        if (!figure.BeingDeleted && JgsHandleRegistry.TryGetEntry(figure, out _))
        {
            UiWait(host, cancellationToken, [JgsHandleRegistry.For(figure)], line, col);
        }
    }

    private static bool StillThere(GraphObject target) => !target.BeingDeleted && JgsHandleRegistry.TryGetEntry(target, out _);

    private static void Resume(FigureModel figure)
    {
        if (StillThere(figure))
        {
            JgsHandleRegistry.EntryFor(figure).WaitStatus = "inactive";
        }
    }

    private static JgsValue[] DialogFont(UiControlModel? like = null) =>
    [
        Text("FontUnits"), Text("points"), Text("FontSize"), JgsValue.Number(like?.FontSize ?? DialogFontSize),
        Text("FontName"), Text(like?.FontName ?? DialogFontName),
    ];

    /// <summary>A pushbutton's <c>Extent</c>, in pixels, for a label in the dialogs' font.</summary>
    private static Size2D ButtonExtent(string label) => new UiControlModel
    {
        Style = UiControlStyle.PushButton,
        Text = UiText.Of(label),
        FontName = DialogFontName,
        FontSize = DialogFontSize,
    }.ExtentPixels();

    private static void RegisterBlockingDialogBuiltins(JgsEnvironment env, JGraphScriptGlobals host, CancellationToken cancellationToken)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body)
            {
                AutoCallsBare = true,
                KeepsStringArguments = true,
            }));

        Define("questdlg", (args, line, col) => QuestDlg(env, host, cancellationToken, args, line, col));
        Define("inputdlg", (args, line, col) => InputDlg(host, cancellationToken, args, line, col));
        env.Builtins.Register("listdlg", JgsValue.Function(new BuiltinFunction("listdlg",
            (args, line, col) => ListDlg(env, host, cancellationToken, args, line, col)[0])
        {
            AutoCallsBare = true,
            KeepsStringArguments = true,
            MultiOutput = (args, wanted, line, col) => wanted > 2
                ? throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.")
                : ListDlg(env, host, cancellationToken, args, line, col),
        }));
    }

    // --- questdlg ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>questdlg(question, title, btn1, btn2, btn3, default)</c>: asks a question with up to three
    /// buttons and answers the one pressed — or <c>''</c> when the dialog is closed instead.
    /// </summary>
    private static JgsValue QuestDlg(
        JgsEnvironment env, JGraphScriptGlobals host, CancellationToken cancellationToken,
        IReadOnlyList<JgsValue> given, int line, int col)
    {
        if (given.Count > 6)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        RequireSomebodyToAnswer(line, col);
        if (given.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:questdlg:TooFewArguments", "Too few arguments for QUESTDLG");
        }

        JgsValue[] args = [.. given.Select(AsChars)];
        List<string> question = DialogLines(args[0], line, col);
        JgsValue title = args.Length > 1 ? args[1] : Text(" ");
        string interpreter = "none";

        // Which arguments are buttons depends on how many there are: the last one names the default.
        string[] buttons;
        JgsValue fallback;
        switch (args.Length)
        {
            case <= 3:
                buttons = ["Yes", "No", "Cancel"];
                fallback = args.Length == 3 ? args[2] : Text("Yes");
                break;
            case 4:
                buttons = [TextOf(args[2])];
                fallback = args[3];
                break;
            case 5:
                buttons = [TextOf(args[2]), TextOf(args[3])];
                fallback = args[4];
                break;
            default:
                buttons = [TextOf(args[2]), TextOf(args[3]), TextOf(args[4])];
                fallback = args[5];
                break;
        }

        if (fallback.Type == JgsType.Struct && !fallback.IsStructArray)
        {
            Dictionary<string, JgsValue> fields = fallback.AsStruct;
            if (fields.TryGetValue("Interpreter", out JgsValue? named) && IsTextScalar(named))
            {
                interpreter = TextOf(named);
            }

            fallback = fields.TryGetValue("Default", out JgsValue? chosen) ? AsChars(chosen) : Text(string.Empty);
        }

        string defaultName = IsTextScalar(fallback) ? TextOf(fallback) : string.Empty;
        int defaultButton = System.Array.IndexOf(buttons, defaultName);
        bool defaultValid = defaultButton >= 0;

        // R2025b's layout, in pixels: 10 between things, a 32-pixel icon, buttons at least 56 by 22.
        const double gap = 10;
        const double iconSide = 32;
        const double margin = 1.4;
        double buttonWidth = 56;
        Size2D extent = default;
        for (int i = 0; i < buttons.Length; i++)
        {
            extent = ButtonExtent(buttons[i]);
            buttonWidth = System.Math.Max(buttonWidth, i == 2 ? extent.Width * margin : extent.Width + 8);
        }

        double buttonHeight = System.Math.Max(22, extent.Height * margin);
        double textX = gap + iconSide;
        double figWidth = System.Math.Max(267, textX + (buttons.Length * (buttonWidth + (2 * gap))));
        double figHeight = 70;
        double textY = gap + gap + buttonHeight;
        double textWidth = System.Math.Max(1, figWidth - gap - textX - iconSide);
        double textHeight = System.Math.Max(1, figHeight - gap - textY);

        string answer = string.Empty;
        bool answered = false;
        FigureModel figure = null!;
        JgsValue figureKeys = DialogCallback("doFigureKeyPress", (_, eventData) =>
        {
            switch (KeyOf(eventData))
            {
                case "return" or "space" when defaultValid:
                    answer = defaultName;
                    answered = true;
                    Resume(figure);
                    break;
                case "escape":
                    DeleteGraphics(figure, host);
                    break;
            }
        });
        figure = MakeDialog(
        [
            ("Visible", Text("off")), ("Name", title), ("Pointer", Text("arrow")),
            ("Position", Vec(1, 1, figWidth, figHeight)), ("KeyPressFcn", figureKeys), ("WindowStyle", Text("normal")),
            ("CloseRequestFcn", DialogCallback("doDelete", (_, _) => DeleteGraphics(figure, host))), ("Tag", title),
        ], line, col);

        try
        {
            if (!defaultValid)
            {
                Warn(host, "MATLAB:questdlg:StringMismatch", "Default character vector does not match any button character vector name.");
            }

            var made = new UiControlModel[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                string name = buttons[i];
                UiControlModel button = null!;
                button = NewControl(figure, line, col,
                    ("Style", Text("pushbutton")), ("Position", Vec(textX, gap, buttonWidth, buttonHeight)),
                    ("KeyPressFcn", DialogCallback("doControlKeyPress", (_, eventData) =>
                    {
                        switch (KeyOf(eventData))
                        {
                            case "return" when defaultValid:
                                answer = name;
                                answered = true;
                                Resume(figure);
                                break;
                            case "escape":
                                DeleteGraphics(figure, host);
                                break;
                        }
                    })),
                    ("Callback", DialogCallback("doButton", (_, _) =>
                    {
                        answer = name;
                        answered = true;
                        Resume(figure);
                    })),
                    ("String", Text(name)), ("HorizontalAlignment", Text("center")), ("Tag", Text($"Btn{i + 1}")));
                StoreAppData(JgsHandleRegistry.EntryFor(button), "QuestDlgReturnName", Text(name), sharesOnStore: true);
                made[i] = button;
            }

            UiControlModel sizer = NewControl(figure, line, col,
                ("Style", Text("text")), ("Position", Vec(textX, textY, 0.95 * textWidth, textHeight)),
                ("String", LinesCell([" "])), ("Tag", Text("Question")), ("HorizontalAlignment", Text("left")),
                ("FontWeight", Text("bold")));
            JgsValue[] wrap = TextWrap(
                [JgsHandleRegistry.For(sizer), LinesCell([.. question.SelectMany(static l => l.Split('\n'))]), JgsValue.Number(75)],
                2, line, col);
            double[] wrappedBox = ToDoubles("questdlg", wrap[1], line, col);

            AxesModel axes = NewAxes(figure, line, col, ("Position", Vec(0, 0, 1, 1)), ("Visible", Text("off")));
            TextAnnotation message = DialogText(axes, UiUnits.Pixels, wrap[0], interpreter, "Question", line, col, made[0].FontName);
            message.FontSize = made[0].FontSize;
            double[] measured = Read(message, "Extent", line, col);
            textWidth = System.Math.Max(textWidth, System.Math.Max(wrappedBox[2] + 2, measured[2]));
            textHeight = System.Math.Max(textHeight, System.Math.Max(wrappedBox[3] + 2, measured[3]));
            textX = gap + iconSide + gap;
            figWidth = System.Math.Max((buttons.Length * (buttonWidth + gap)) + gap, textX + textWidth + gap);

            double iconY;
            if (iconSide > textHeight)
            {
                iconY = gap + buttonHeight + gap;
                textY = iconY + ((iconSide - textHeight) / 2);
                figHeight = iconY + iconSide + gap;
            }
            else
            {
                textY = gap + buttonHeight + gap;
                iconY = textY + ((textHeight - iconSide) / 2);
                figHeight = textY + textHeight + gap;
            }

            double middle = (figWidth - buttonWidth) / 2;
            double[] buttonX = buttons.Length switch
            {
                1 => [middle],
                2 => [((figWidth - gap) / 2) - buttonWidth, (figWidth + gap) / 2],
                _ => [middle - gap - buttonWidth, middle, middle + buttonWidth + gap],
            };

            PlaceDialog(figure, figWidth, figHeight, line, col);
            for (int i = 0; i < made.Length; i++)
            {
                SetAll(made[i], line, col, ("Position", Vec(buttonX[i], gap, buttonWidth, buttonHeight)));
            }

            DeleteGraphics(sizer, host);
            SetAll(message, line, col, ("Position", Vec(textX, textY, 0)));
            AxesModel iconAxes = NewAxes(figure, line, col,
                ("Units", Text("pixels")), ("Position", Vec(gap, iconY, iconSide, iconSide)), ("Tag", Text("IconAxes")));
            DialogIcon(env, iconAxes, "quest", null, null, line, col);
            SetAll(figure, line, col, ("WindowStyle", Text("modal")), ("Visible", Text("on")));

            WaitOnDialog(figure, defaultValid ? made[defaultButton] : null, host, cancellationToken, line, col);
        }
        catch
        {
            DeleteGraphics(figure, host);
            throw;
        }

        // A dialog closed from outside answers ''; one answered is taken down here.
        bool closed = !StillThere(figure);
        DeleteGraphics(figure, host);
        return Text(closed || !answered ? string.Empty : answer);
    }

    // --- inputdlg ---------------------------------------------------------------------------------------

    /// <summary>
    /// <c>inputdlg(prompt, title, dims, defaults, options)</c>: one edit field under each prompt,
    /// with OK and Cancel. Answers a column cell of what the fields hold, or <c>{}</c> when cancelled.
    /// </summary>
    private static JgsValue InputDlg(
        JGraphScriptGlobals host, CancellationToken cancellationToken, IReadOnlyList<JgsValue> given, int line, int col)
    {
        if (given.Count > 5)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        RequireSomebodyToAnswer(line, col);
        JgsValue[] args = [.. given];
        List<JgsValue> prompts = args.Length < 1 ? [Text("Input:")]
            : args[0].Type == JgsType.Cell ? [.. args[0].AsCell]
            : args[0].IsStringArray && args[0].Type == JgsType.Array
                ? [.. Enumerable.Range(0, args[0].ArrayLength).Select(i => Text(TextOf(args[0].ElementAt(i))))]
                : [AsChars(args[0])];
        int count = prompts.Count;
        JgsValue title = args.Length > 1 ? AsChars(args[1]) : Text(" ");

        // The size of each field: lines, or lines and columns.
        double[] dims = args.Length > 2 ? ToDoubles("inputdlg", args[2], line, col) : [1];
        (int rows, int columns) = args.Length > 2 ? ShapeOf(args[2]) : (1, 1);
        var fieldLines = new double[count];
        double[]? fieldColumns = null;
        if (rows == 1 && columns == 2)
        {
            fieldColumns = new double[count];
            System.Array.Fill(fieldLines, dims[0]);
            System.Array.Fill(fieldColumns, dims[1]);
        }
        else if (rows == 1 && columns == 1)
        {
            System.Array.Fill(fieldLines, dims[0]);
        }
        else if (rows == 1 && columns == count)
        {
            dims.CopyTo(fieldLines, 0);
        }
        else if (rows != count || columns > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:inputdlg:IncorrectSize",
                "NumLines size is incorrect.");
        }
        else
        {
            // One row to a prompt, column-major: the lines, then — when there are two columns — the widths.
            for (int i = 0; i < count; i++)
            {
                fieldLines[i] = dims[i];
            }

            if (columns == 2)
            {
                fieldColumns = [.. dims.Skip(count)];
            }
        }

        JgsValue[] defaults;
        if (args.Length > 3)
        {
            JgsValue offered = args[3];
            if (offered.IsStringArray)
            {
                int n = offered.Type == JgsType.Array ? offered.ArrayLength : 1;
                offered = JgsValue.Cell([.. Enumerable.Range(0, n).Select(i => Text(TextOf(offered.Type == JgsType.Array ? offered.ElementAt(i) : offered)))]);
            }

            if (offered.Type != JgsType.Cell)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:inputdlg:InvalidDefaultAnswer", "Default answer must be a string array or cell array of character vectors.");
            }

            defaults = offered.AsCell;
        }
        else
        {
            defaults = [.. Enumerable.Repeat(Text(string.Empty), count)];
        }

        string resize = "off";
        string windowStyle = "modal";
        string interpreter = "none";
        if (args.Length > 4)
        {
            if (args[4].Type == JgsType.Struct && !args[4].IsStructArray)
            {
                Dictionary<string, JgsValue> fields = args[4].AsStruct;
                string Field(string name, string otherwise) =>
                    fields.TryGetValue(name, out JgsValue? value) && IsTextScalar(value) ? TextOf(value) : otherwise;
                resize = Field("Resize", resize);
                windowStyle = Field("WindowStyle", windowStyle);
                interpreter = Field("Interpreter", interpreter);
            }
            else if (IsTextScalar(args[4]))
            {
                resize = TextOf(args[4]);
            }
        }

        const double gap = 5;
        const double margin = 1.4;
        double figWidth = 175;
        bool accepted = false;
        FigureModel figure = null!;
        void Accept()
        {
            accepted = true;
            Resume(figure);
        }

        JgsValue figureKeys = DialogCallback("doFigureKeyPress", (_, eventData) =>
        {
            switch (KeyOf(eventData))
            {
                case "return" or "space":
                    Accept();
                    break;
                case "escape":
                    DeleteGraphics(figure, host);
                    break;
            }
        });
        figure = MakeDialog(
        [
            ("Visible", Text("off")), ("KeyPressFcn", figureKeys), ("Name", title), ("Pointer", Text("arrow")),
            ("Units", Text("pixels")), ("UserData", Text("Cancel")), ("Tag", title), ("WindowStyle", Text(windowStyle)),
            ("Resize", Text(resize)),
        ], line, col);

        var edits = new UiControlModel[count];
        try
        {
            Size2D cancelExtent = ButtonExtent("Cancel");
            double buttonWidth = System.Math.Max(53, cancelExtent.Width + 8);
            double buttonHeight = System.Math.Max(23, cancelExtent.Height * margin);
            double textWidth = figWidth - (2 * gap);

            // Each prompt, wrapped by a text control of the dialog's width and measured by it.
            UiControlModel sizer = NewControl(figure, line, col,
                ("Units", Text("pixels")), ("FontSize", JgsValue.Number(DialogFontSize)), ("HorizontalAlignment", Text("left")),
                ("Style", Text("text")), ("String", Text(string.Empty)),
                ("Position", Vec(gap, gap, 0.96 * textWidth, buttonHeight)), ("Visible", Text("off")));
            var wrapped = new JgsValue[count];
            var promptWidth = new double[count];
            var promptHeight = new double[count];
            for (int i = 0; i < count; i++)
            {
                List<string> lines = DialogLines(AsChars(prompts[i]), line, col);
                JgsValue[] wrap = TextWrap(
                    [JgsHandleRegistry.For(sizer), LinesCell([.. lines.SelectMany(static l => l.Split('\n'))]),
                        JgsValue.Number(fieldColumns is null ? 80 : System.Math.Max(1, fieldColumns[i]))],
                    2, line, col);
                wrapped[i] = wrap[0];
                double[] box = ToDoubles("inputdlg", wrap[1], line, col);
                promptWidth[i] = box[2];
                promptHeight[i] = box[3];
            }

            DeleteGraphics(sizer, host);
            double lineHeight = promptHeight[0] / System.Math.Max(1, wrapped[0].ArrayLength);
            var editHeight = new double[count];
            for (int i = 0; i < count; i++)
            {
                editHeight[i] = (lineHeight * fieldLines[i]) + (fieldLines[i] == 1 ? 4 : 0);
            }

            double figHeight = ((count + 2) * gap) + buttonHeight + editHeight.Sum() + promptHeight.Sum();
            var promptY = new double[count];
            var editY = new double[count];
            promptY[0] = figHeight - gap - promptHeight[0];
            editY[0] = promptY[0] - editHeight[0];
            for (int i = 1; i < count; i++)
            {
                promptY[i] = editY[i - 1] - promptHeight[i] - gap;
                editY[i] = promptY[i] - editHeight[i];
            }

            AxesModel axes = NewAxes(figure, line, col, ("Position", Vec(0, 0, 1, 1)), ("Visible", Text("off")));
            for (int i = 0; i < count; i++)
            {
                JgsValue offered = i < defaults.Length ? AsChars(defaults[i]) : Text(string.Empty);
                if (ClassOf(offered, JgsDialect.Matlab) != "char")
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:inputdlg:InvalidInput", "Default answer must be a string array or cell array of character vectors.");
                }

                UiControlModel edit = null!;
                edit = NewControl(figure, line, col,
                    ("Units", Text("pixels")), ("FontSize", JgsValue.Number(DialogFontSize)), ("HorizontalAlignment", Text("left")),
                    ("Style", Text("edit")), ("Max", JgsValue.Number(fieldLines[i])),
                    ("Callback", DialogCallback("doEditCallback", (_, _) =>
                    {
                        // Enter in a one-line field accepts the dialog.
                        if (figure.CurrentKey == "return" && edit.Max - edit.Min == 1)
                        {
                            Accept();
                        }
                    })),
                    ("Position", Vec(gap, editY[i], textWidth, editHeight[i])), ("String", offered), ("Tag", Text("Edit")));
                edits[i] = edit;
                TextAnnotation prompt = DialogText(axes, UiUnits.Pixels, wrapped[i], interpreter, "Quest", line, col, "Helvetica");
                SetAll(prompt, line, col, ("Position", Vec(gap, promptY[i], 0)));

                double least = promptWidth.Max();
                if (fieldColumns is not null)
                {
                    // A field given a width in characters is as wide as that many x's, and a scroll bar.
                    Size2D xs = new UiControlModel
                    {
                        Style = UiControlStyle.Edit,
                        Text = UiText.Of(new string('x', (int)System.Math.Max(0, fieldColumns[i]))),
                        FontName = edit.FontName,
                        FontSize = edit.FontSize,
                    }.ExtentPixels();
                    double editWidth = xs.Width + (fieldLines[i] > 1 ? 19 : 0);
                    SetAll(edit, line, col, ("Position", Vec(gap, editY[i], editWidth + 1, editHeight[i])));
                    least = System.Math.Max(least, editWidth);
                }

                least = System.Math.Max(least, Read(prompt, "Extent", line, col)[2]);
                figWidth = System.Math.Max(figWidth, least + (2 * gap));
            }

            if (fieldColumns is null)
            {
                textWidth = figWidth - (2 * gap);
                for (int i = 0; i < count; i++)
                {
                    SetAll(edits[i], line, col, ("Position", Vec(gap, editY[i], textWidth, editHeight[i])));
                }
            }

            figWidth = System.Math.Max(figWidth, (2 * (buttonWidth + gap)) + gap);
            PlaceDialog(figure, figWidth, figHeight, line, col);

            JgsValue controlKeys(bool cancels) => DialogCallback("doControlKeyPress", (_, eventData) =>
            {
                switch (KeyOf(eventData))
                {
                    case "return" when !cancels:
                        Accept();
                        break;
                    case "return" or "escape":
                        DeleteGraphics(figure, host);
                        break;
                }
            });
            NewControl(figure, line, col,
                ("Units", Text("pixels")), ("FontSize", JgsValue.Number(DialogFontSize)), ("Style", Text("pushbutton")),
                ("HorizontalAlignment", Text("center")),
                ("Position", Vec(figWidth - (2 * buttonWidth) - (2 * gap), gap, buttonWidth, buttonHeight)),
                ("KeyPressFcn", controlKeys(false)), ("String", Text("OK")),
                ("Callback", DialogCallback("doCallback", (_, _) => Accept())), ("Tag", Text("OK")), ("UserData", Text("OK")));
            NewControl(figure, line, col,
                ("Units", Text("pixels")), ("FontSize", JgsValue.Number(DialogFontSize)), ("Style", Text("pushbutton")),
                ("HorizontalAlignment", Text("center")),
                ("Position", Vec(figWidth - buttonWidth - gap, gap, buttonWidth, buttonHeight)),
                ("KeyPressFcn", controlKeys(true)), ("String", Text("Cancel")),
                ("Callback", DialogCallback("doCallback", (_, _) => DeleteGraphics(figure, host))),
                ("Tag", Text("Cancel")), ("UserData", Text("Cancel")));

            // A resizable dialog keeps its fields as wide as itself, unless their widths were given.
            if (fieldColumns is null)
            {
                SetAll(figure, line, col, ("SizeChangedFcn", DialogCallback("doResize", (_, _) =>
                {
                    double width = JgsGraphicsProperties.FigurePixels(figure).Width;
                    foreach (UiControlModel edit in edits.Where(StillThere))
                    {
                        Rect2D box = edit.PixelPosition();
                        SetAll(edit, 0, 0, ("Position", Vec(box.X, box.Y, System.Math.Max(1, width - (2 * gap)), box.Height)));
                    }
                })));
            }

            if (FigureOf(JgsGraphicsCallbackState.CallbackObject) is { WindowStyle: FigureWindowStyle.Modal })
            {
                figure.WindowStyle = FigureWindowStyle.Modal;
            }

            SetAll(figure, line, col, ("Visible", Text("on")));
            WaitOnDialog(figure, count > 0 ? edits[0] : null, host, cancellationToken, line, col);
        }
        catch
        {
            DeleteGraphics(figure, host);
            throw;
        }

        JgsValue answer = JgsValue.Cell([]);
        answer.Reshape(0, 0);
        if (StillThere(figure) && accepted)
        {
            answer = JgsValue.Cell([.. edits.Select(edit =>
                JgsValue.Share(JgsGraphicsProperties.Get(JgsHandleRegistry.EntryFor(edit), "String", line, col)))]);
            answer.Reshape(count, count == 0 ? 0 : 1);
        }

        DeleteGraphics(figure, host);
        return answer;
    }

    /// <summary>The rows and columns of a value, as <c>size</c> gives them for a matrix.</summary>
    private static (int Rows, int Columns) ShapeOf(JgsValue value) =>
        value.Type == JgsType.Array ? (value.Rows, value.Cols) : (1, 1);

    // --- listdlg ----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>[selection, ok] = listdlg('ListString', list, …)</c>: a list to pick from, with OK and
    /// Cancel and, where several may be picked, Select all.
    /// </summary>
    private static JgsValue[] ListDlg(
        JgsEnvironment env, JGraphScriptGlobals host, CancellationToken cancellationToken,
        IReadOnlyList<JgsValue> given, int line, int col)
    {
        RequireSomebodyToAnswer(line, col);
        if (given.Count < 1)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        if (given.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:listdlg:InvalidArgument", "Arguments to LISTDLG must be param/value pairs.");
        }

        string name = string.Empty;
        bool multiple = true;
        List<string> prompt = [];
        JgsValue? list = null;
        double[] listSize = [160, 300];
        JgsValue initial = JgsMatrix.FromColumnMajor([], 0, 0);
        string okString = "OK";
        string cancelString = "Cancel";
        double fus = 8;
        double ffs = 8;
        double uh = 22;
        for (int i = 0; i < given.Count; i += 2)
        {
            JgsValue value = AsChars(given[i + 1]);
            string option = IsTextScalar(given[i]) ? TextOf(given[i]) : string.Empty;
            switch (option.ToLowerInvariant())
            {
                case "name":
                    name = TextOf(value);
                    break;
                case "promptstring":
                    prompt = DialogLines(value, line, col);
                    break;
                case "selectionmode":
                    multiple = TextOf(value).ToLowerInvariant() switch { "single" => false, "multiple" => true, _ => multiple };
                    break;
                case "listsize":
                    listSize = ToDoubles("listdlg", value, line, col);
                    break;
                case "liststring":
                    list = value;
                    break;
                case "initialvalue":
                    initial = value;
                    break;
                case "uh":
                    uh = NumOf("listdlg", value, line, col);
                    break;
                case "fus":
                    fus = NumOf("listdlg", value, line, col);
                    break;
                case "ffs":
                    ffs = NumOf("listdlg", value, line, col);
                    break;
                case "okstring":
                    okString = TextOf(value);
                    break;
                case "cancelstring":
                    cancelString = TextOf(value);
                    break;
                default:
                    throw new JgsRuntimeException(line, col, "MATLAB:listdlg:UnknownParameter", $"Unknown parameter name passed to LISTDLG.  Name was {option}");
            }
        }

        if (list is null || IsEmptyValue(list))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:listdlg:NeedParameter", "ListString parameter is required.");
        }

        List<string> items = DialogLines(list, line, col);
        if (IsEmptyValue(initial))
        {
            initial = JgsValue.Number(1);
        }

        double lineHeight = DialogFontSize * 1.7;
        double promptHeight = lineHeight * prompt.Count;
        double extra = multiple ? fus + uh : 0;
        double width = (2 * (fus + ffs)) + listSize[0];
        double height = (2 * ffs) + (6 * fus) + promptHeight + listSize[1] + uh + extra;

        // R2025b's list dialog is an ordinary figure, numbered and current while it is up.
        JgsValue handle = CallBuiltin(env, "listdlg", "figure",
        [
            Text("WindowStyle"), Text("modal"), Text("Name"), Text(name), Text("Resize"), Text("off"),
            Text("NumberTitle"), Text("off"), Text("MenuBar"), Text("none"), Text("Units"), Text("pixels"),
            Text("Visible"), Text("off"), Text("Position"), Vec(1, 1, width, height),
            Text("CloseRequestFcn"), Text("delete(gcbf)"), Text("Color"), Vec(UiFigureColor.R, UiFigureColor.G, UiFigureColor.B),
        ], line, col);
        FigureModel figure = (FigureModel)JgsHandleRegistry.Require(handle, line, col).Target;

        JgsValue selection = JgsMatrix.FromColumnMajor([], 0, 0);
        bool accepted = false;
        try
        {
            if (prompt.Count > 0)
            {
                NewControl(figure, line, col,
                    ("Style", Text("text")), ("String", LinesCell(prompt)), ("HorizontalAlignment", Text("left")),
                    ("Position", Vec(ffs + fus, height - (ffs + fus + promptHeight), listSize[0], promptHeight)));
            }

            double buttonWidth = (width - (2 * (ffs + fus)) - fus) / 2;
            UiControlModel listbox = NewControl(figure, line, col,
                ("Style", Text("listbox")), ("Position", Vec(ffs + fus, ffs + uh + (4 * fus) + extra, listSize[0], listSize[1])),
                ("String", LinesCell(items)), ("Max", JgsValue.Number(multiple ? 2 : 1)), ("Tag", Text("listbox")),
                ("Value", initial));
            void Accept()
            {
                if (!accepted && StillThere(listbox))
                {
                    accepted = true;
                    selection = JgsValue.Share(JgsGraphicsProperties.Get(JgsHandleRegistry.EntryFor(listbox), "Value", line, col));
                    DeleteGraphics(figure, host);
                }
            }

            UiControlModel ok = NewControl(figure, line, col,
                ("Style", Text("pushbutton")), ("String", Text(okString)), ("Position", Vec(ffs + fus, ffs + fus, buttonWidth, uh)),
                ("Tag", Text("ok_btn")), ("Callback", DialogCallback("doOK", (_, _) => Accept())));
            UiControlModel cancel = NewControl(figure, line, col,
                ("Style", Text("pushbutton")), ("String", Text(cancelString)),
                ("Position", Vec(ffs + (2 * fus) + buttonWidth, ffs + fus, buttonWidth, uh)),
                ("Tag", Text("cancel_btn")), ("Callback", DialogCallback("doCancel", (_, _) => DeleteGraphics(figure, host))));

            UiControlModel? selectAll = null;
            if (multiple)
            {
                selectAll = NewControl(figure, line, col,
                    ("Style", Text("pushbutton")), ("String", Text("Select all")),
                    ("Position", Vec(ffs + fus, (4 * fus) + ffs + uh, listSize[0], uh)), ("Tag", Text("selectall_btn")),
                    ("Callback", DialogCallback("doSelectAll", (source, _) =>
                    {
                        SetAll(JgsHandleRegistry.Require(source, line, col).Target, line, col, ("Enable", Text("off")));
                        SetAll(listbox, line, col, ("Value", Vec([.. Enumerable.Range(1, items.Count).Select(static i => (double)i)])));
                    })));
                if (ToDoubles("listdlg", initial, line, col).Length == items.Count)
                {
                    SetAll(selectAll, line, col, ("Enable", Text("off")));
                }
            }

            // A double click accepts; any other click re-arms Select all while something is unpicked.
            SetAll(listbox, line, col, ("Callback", DialogCallback("doListboxClick", (_, _) =>
            {
                if (figure.SelectionType == SelectionKind.Open)
                {
                    Accept();
                }
                else if (selectAll is not null && StillThere(selectAll))
                {
                    bool all = listbox.Value.Data.Count == items.Count;
                    SetAll(selectAll, line, col, ("Enable", Text(all ? "off" : "on")));
                }
            })));

            JgsValue keys = DialogCallback("doKeypress", (_, eventData) =>
            {
                if (KeyOf(eventData) == "escape")
                {
                    DeleteGraphics(figure, host);
                }
            });
            foreach (GraphObject listener in new GraphObject[] { figure, ok, cancel, listbox })
            {
                SetAll(listener, line, col, ("KeyPressFcn", keys));
            }

            PlaceDialog(figure, width, height, line, col);
            SetAll(figure, line, col, ("Visible", Text("on")));
            WaitOnDialog(figure, listbox, host, cancellationToken, line, col);
        }
        catch
        {
            DeleteGraphics(figure, host);
            throw;
        }

        DeleteGraphics(figure, host);
        return [accepted ? selection : JgsMatrix.FromColumnMajor([], 0, 0), JgsValue.Number(accepted ? 1 : 0)];
    }

    // --- waitbar ----------------------------------------------------------------------------------------

    private const string WaitbarTag = "TMWWaitbar";

    private static void RegisterWaitbarBuiltin(JgsEnvironment env, JGraphScriptGlobals host) =>
        env.Builtins.Register("waitbar", JgsValue.Function(new BuiltinFunction("waitbar",
            (args, line, col) => Waitbar(host, args, line, col))
        {
            BindsAnsAsStatement = false,
            KeepsStringArguments = true,
        }));

    /// <summary>
    /// <c>waitbar(x, message, …)</c> makes a progress window; <c>waitbar(x, h)</c> and
    /// <c>waitbar(x, h, message)</c> move one along, and <c>waitbar(x)</c> the most recent one.
    /// </summary>
    private static JgsValue Waitbar(JGraphScriptGlobals host, IReadOnlyList<JgsValue> given, int line, int col)
    {
        if (given.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:waitbar:InvalidArguments", "Input arguments not valid.");
        }

        if (!IsNumericClass(given[0]) || (given[0].Type == JgsType.Array && given[0].ArrayLength != 1))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:waitbar:InvalidFirstInput",
                "The first argument must be a numeric value between 0 and 1.");
        }

        double x = System.Math.Clamp(ToDoubles("waitbar", given[0], line, col)[0], 0, 1) * 100;
        if (double.IsNaN(x))
        {
            x = 0;
        }

        FigureModel? existing = null;
        JgsValue name = Text("Waitbar");
        if (given.Count == 1)
        {
            existing = JgsGraphicsProperties.RootFigures(hidden: true).OfType<FigureModel>().LastOrDefault(static f => f.Tag == WaitbarTag);
        }
        else
        {
            JgsValue which = AsChars(given[1]);
            if (IsTextScalar(which) || which.Type == JgsType.String || which.Type == JgsType.Cell || which.IsStringArray)
            {
                name = which.IsStringArray ? LinesCell(DialogLines(which, line, col)) : which;
            }
            else if (which.Type == JgsType.Number && JgsHandleRegistry.TryGet(which, out JgsHandleEntry? named) && named.Target is FigureModel bar)
            {
                existing = bar;
            }
            else
            {
                throw new JgsRuntimeException(line, col, "MATLAB:waitbar:InvalidSecondInput",
                    "The second argument must be a message character vector or a handle to an existing waitbar.");
            }
        }

        if (existing is not null)
        {
            JgsHandleEntry entry = JgsHandleRegistry.EntryFor(existing);
            if (!entry.AppData.TryGetValue("TMWWaitbar_handles", out JgsValue? stored) || stored.Type != JgsType.Struct
                || stored.AsStruct.Count != 5)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:waitbar:WaitbarHandlesNotFound", "Couldn't find waitbar handles.");
            }

            Dictionary<string, JgsValue> handles = stored.AsStruct;
            if (JgsHandleRegistry.TryGet(handles["progressbar"], out JgsHandleEntry? barEntry) && barEntry.Target is UiProgressIndicatorModel indicator)
            {
                indicator.Value = x / 100;
            }

            StoreAppData(entry, "TMWWaitbar_value", JgsValue.Number(x), sharesOnStore: true);
            if (given.Count > 2 && JgsHandleRegistry.TryGet(handles["axesTitle"], out JgsHandleEntry? titleEntry))
            {
                try
                {
                    JgsGraphicsProperties.Set(titleEntry, "String", AsChars(given[2]), line, col);
                }
                catch (JgsRuntimeException)
                {
                    // R2025b takes anything here without complaint; what is not text leaves the words as they are.
                }
            }

            return JgsHandleRegistry.For(existing);
        }

        try
        {
            return JgsHandleRegistry.For(NewWaitbar(host, x, name, [.. given.Skip(2).Select(AsChars)], line, col));
        }
        catch (JgsRuntimeException)
        {
            foreach (FigureModel stale in JgsGraphicsProperties.RootFigures(hidden: true).OfType<FigureModel>().Where(static f => f.Tag == WaitbarTag).ToList())
            {
                DeleteGraphics(stale, host);
            }

            throw new JgsRuntimeException(line, col, "MATLAB:waitbar:InvalidArguments", "Improper arguments for waitbar.");
        }
    }

    private static FigureModel NewWaitbar(JGraphScriptGlobals host, double x, JgsValue name, JgsValue[] options, int line, int col)
    {
        if (options.Length % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:waitbar:InvalidOptionalArgsPass", "Optional initialization arguments must be passed in pairs.");
        }

        // R2025b's arithmetic, in points: a 360 by 75 pixel window in the middle of the screen.
        const double pointsPerPixel = 72.0 / 96;
        Rect2D screen = UiScreen.Primary;
        double screenWidth = screen.Width * pointsPerPixel;
        double screenHeight = screen.Height * pointsPerPixel;
        double[] pos = [(screenWidth / 2) - 135, (screenHeight / 2) - 28.125, 270, 56.25];

        var figure = new FigureModel
        {
            IntegerHandle = false,
            NumberTitle = false,
            Resizable = false,
            MenuBar = false,
            Visible = false,
            Background = UiFigureColor.ToColor(),
            Name = string.Empty,
            Size = new Size2D(360, 75),
        };
        JG.RegisterHiddenFigure(figure);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(figure);
        SetAll(figure, line, col,
            ("Units", Text("points")), ("BusyAction", Text("queue")), ("Position", Vec(pos)), ("Tag", Text(WaitbarTag)),
            ("Interruptible", Text("off")));

        string visible = "on";
        double lift = 0;
        UiControlModel? cancel = null;
        for (int i = 0; i < options.Length; i += 2)
        {
            string option = IsTextScalar(options[i]) ? TextOf(options[i]) : string.Empty;
            JgsValue value = options[i + 1];
            if (option.StartsWith("vis", StringComparison.OrdinalIgnoreCase) && IsTextScalar(value))
            {
                visible = TextOf(value);
                continue;
            }

            try
            {
                if (option.Equals("CreateCancelBtn", StringComparison.OrdinalIgnoreCase) && cancel is null)
                {
                    const double cancelHeight = 23 * pointsPerPixel;
                    const double cancelWidth = 60 * pointsPerPixel;
                    lift += cancelHeight;
                    SetAll(figure, line, col, ("Position", Vec(pos[0], pos[1], pos[2], pos[3] + lift)), ("CloseRequestFcn", value));
                    cancel = NewControl(figure, line, col,
                        ("Units", Text("points")), ("Callback", value), ("ButtonDownFcn", value), ("Enable", Text("on")),
                        ("Interruptible", Text("off")), ("Position", Vec(pos[2] - (cancelWidth * 1.4), 7, cancelWidth, cancelHeight)),
                        ("String", Text("Cancel")), ("Tag", Text("TMWWaitbarCancelButton")));
                }
                else
                {
                    JgsGraphicsProperties.Set(entry, option, value, line, col);
                }
            }
            catch (JgsRuntimeException)
            {
                string shown = IsTextScalar(value) ? TextOf(value)
                    : value.Type is JgsType.Number or JgsType.Bool ? value.AsNumber.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                    : string.Empty;
                Warn(host, "MATLAB:uistring:waitbar:WarningCouldNotSetPropertyValue",
                    $"Could not set property value\nProperty: {option}\nValue: {shown}\n");
            }
        }

        double[] axNorm = [0.05, 0.3, 0.9, 0.2];
        double[] axPos = [axNorm[0] * pos[2], (axNorm[1] * pos[3]) + lift, axNorm[2] * pos[2], axNorm[3] * pos[3]];
        AxesModel axes = NewAxes(figure, line, col,
            ("XLim", Vec(0, 100)), ("YLim", Vec(0, 1)), ("Units", Text("points")), ("Position", Vec(axPos)), ("Visible", Text("off")));

        // The message is the axes' title, as it is in R2025b: a hidden axes still shows its title.
        // Its font is 10-point Helvetica, plain, and it is measured here because a title's own
        // Extent is a drawn thing and nothing has been drawn yet.
        JgsHandleEntry axesEntry = JgsHandleRegistry.EntryFor(axes);
        JgsValue titleHandle = JgsGraphicsProperties.Get(axesEntry, "Title", line, col);
        JgsHandleEntry titleEntry = JgsHandleRegistry.Require(titleHandle, line, col);
        const double titlePixels = 10 * 96.0 / 72;
        List<string> words = DialogLines(name, line, col);
        JgsGraphicsProperties.Set(titleEntry, "FontName", Text("Helvetica"), line, col);
        JgsGraphicsProperties.Set(titleEntry, "FontWeight", Text("normal"), line, col);
        JgsGraphicsProperties.Set(titleEntry, "FontSize", JgsValue.Number(titlePixels), line, col);
        JgsGraphicsProperties.Set(titleEntry, "String", name.Type == JgsType.Cell ? name : Text(string.Join('\n', words)), line, col);
        double titleWidth = 0;
        double titleLine = 0;
        foreach (string word in words.SelectMany(static l => l.Split('\n')))
        {
            (double w, double h) = UiFonts.Measure(word.Length == 0 ? " " : word, "Helvetica", titlePixels);
            titleWidth = System.Math.Max(titleWidth, w);
            titleLine = h;
        }

        double[] extent = [0, 0, titleWidth * pointsPerPixel, titleLine * System.Math.Max(1, words.Count) * pointsPerPixel];

        bool moved = false;
        double titleTop = extent[3] + axPos[1] + axPos[3] + 5;
        if (titleTop > pos[3])
        {
            pos[3] = titleTop;
            pos[1] = (screenHeight / 2) - (pos[3] / 2);
            moved = true;
        }

        if (extent[2] > pos[2])
        {
            pos[2] = System.Math.Min(extent[2] * 1.10, screenWidth);
            pos[0] = (screenWidth / 2) - (pos[2] / 2);
            axPos[0] = axNorm[0] * pos[2];
            axPos[2] = axNorm[2] * pos[2];
            SetAll(axes, line, col, ("Position", Vec(axPos)));
            moved = true;
        }

        if (moved)
        {
            SetAll(figure, line, col, ("Position", Vec(pos)));
            if (cancel is not null)
            {
                double[] box = Read(cancel, "Position", line, col);
                SetAll(cancel, line, col, ("Position", Vec(pos[2] - (box[2] * 1.4), box[1], box[2], box[3])));
            }
        }

        // The bar itself: six pixels high along the axes' lower edge.
        var indicator = new UiProgressIndicatorModel
        {
            Position = new Rect2D((axPos[0] / pointsPerPixel) + 1, (axPos[1] / pointsPerPixel) + 1, axPos[2] / pointsPerPixel, 6),
            Value = x / 100,
        };
        figure.Components.Add(indicator);
        JgsHandleRegistry.EntryFor(indicator).HandleVisibility = "off";

        var handles = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["figure"] = JgsHandleRegistry.For(figure),
            ["axes"] = JgsHandleRegistry.For(axes),
            ["axesTitle"] = titleHandle,
            ["progressbar"] = JgsHandleRegistry.For(indicator),
            ["container"] = JgsMatrix.FromColumnMajor([], 0, 0),
        };
        StoreAppData(entry, "TMWWaitbar_handles", JgsValue.Struct(handles), sharesOnStore: true);
        StoreAppData(entry, "TMWWaitbar_value", JgsValue.Number(x), sharesOnStore: true);
        entry.HandleVisibility = "callback";
        SetAll(figure, line, col, ("Visible", Text(visible)));
        JgsGraphicsProperties.RememberSize(figure);
        return figure;
    }
}
