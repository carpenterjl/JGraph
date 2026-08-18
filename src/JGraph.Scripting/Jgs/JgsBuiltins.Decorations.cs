using System.Text;
using JGraph.Api;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M54 wave E: the words written on a figure and the lines drawn across it — the frame, the two
/// title lines, the labels on a contour, and the reference lines a threshold is marked with.
/// <para>
/// The five verbs that write text share one option parser, so <c>title('x', 'Color', 'r')</c> and
/// <c>subtitle('y', 'FontSize', 8)</c> take the same words and complain the same way. The four that
/// already existed are re-declared here rather than edited in place, which is how the option surface
/// reaches them without every earlier registration learning about text properties.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The text properties every titling verb accepts, in the spellings MATLAB documents.</summary>
    private static readonly string[] TitleOptionNames =
    [
        "Color", "FontSize", "FontName", "FontWeight", "FontAngle",
    ];

    private static void RegisterDecorationBuiltins(JgsEnvironment env, JgsDialect dialect)
    {
        void Define(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, body)));

        void DefineSilent(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Declare(name, JgsValue.Function(
                new BuiltinFunction(name, body) { BindsAnsAsStatement = false }));

        // Every axes-facing verb takes a leading axes handle without moving gca (M51).
        void DefineOnAxes(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            Define(name, (args, line, col) =>
            {
                (AxesModel? axes, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
                return OnAxes(axes, () => body(rest, line, col));
            });

        // --- The titling family --------------------------------------------------------------
        DefineOnAxes("title", (args, line, col) => Titled(
            "title", args, line, col,
            text => JG.Gca().Title = text,
            () => JG.Gca().TitleStyle,
            style => JG.Gca().TitleStyle = style));

        DefineOnAxes("subtitle", (args, line, col) => Titled(
            "subtitle", args, line, col,
            text => JG.Gca().Subtitle = text,
            () => JG.Gca().SubtitleStyle,
            style => JG.Gca().SubtitleStyle = style));

        // sgtitle names the figure, not an axes — but it still accepts a leading handle, which for
        // a figure is its number, and MATLAB's own sgtitle(fig, …) form passes exactly that.
        Define("sgtitle", (args, line, col) =>
        {
            IReadOnlyList<JgsValue> rest = args;
            if (args.Count > 1 && JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? entry)
                && entry.Target is FigureModel)
            {
                rest = args.Skip(1).ToList();
            }

            FigureModel figure = JG.CurrentFigure;
            return Titled(
                "sgtitle", rest, line, col,
                text => figure.Title = text,
                () => figure.TitleStyle,
                style => figure.TitleStyle = style);
        });

        // A label names one ruler, so a handle on a ruler labels that one rather than whichever side
        // yyaxis last made active — which is how the two sides of a plotyy get their own labels.
        void DefineLabel(string name, Func<AxesModel, AxisModel> otherwise) =>
            Define(name, (args, line, col) =>
            {
                (AxesModel? axes, AxisModel? aimed, IReadOnlyList<JgsValue> rest) = PeelRuler(args);
                return OnAxes(axes, () =>
                {
                    AxisModel ruler = aimed ?? otherwise(JG.Gca());
                    return Titled(
                        name, rest, line, col,
                        text => ruler.Label = text,
                        () => ruler.LabelStyle,
                        style => ruler.LabelStyle = style);
                });
            });

        DefineLabel("xlabel", axes => axes.PrimaryXAxis);
        DefineLabel("ylabel", axes => axes.ActiveYAxis);
        DefineLabel("zlabel", axes => axes.ZAxis);

        // --- The frame -------------------------------------------------------------------------
        DefineOnAxes("box", (args, line, col) =>
        {
            ArityRange("box", args, 0, 1, line, col);

            // box on its own toggles, which is what OnOff does for grid and hold; 'on'/'off' name it.
            JG.Box(OnOff("box", args, line, col, dialect, () => JG.Gca().FrameVisible));
            return JgsValue.Null;
        });

        // --- Reference lines ---------------------------------------------------------------------
        DefineSilent("xline", (args, line, col) => ConstantLine("xline", args, line, col, vertical: true));
        DefineSilent("yline", (args, line, col) => ConstantLine("yline", args, line, col, vertical: false));

        // --- Contour labels -----------------------------------------------------------------------
        DefineSilent("clabel", (args, line, col) => Clabel(args, line, col));

        // --- The contour matrix ------------------------------------------------------------------
        // [C, h] = contour(…) is how a script gets at the traced curves, and it is the form clabel is
        // written against. One output still answers the handle, which is what this build has always
        // returned and what every plotting verb here returns — a recorded divergence from MATLAB,
        // where a lone output is the matrix.
        void DefineContour(string name, bool silent, bool filled, bool elevated)
        {
            // The peel is here rather than around Single so both the one-output and the
            // [C, h] = contour(ax, Z) paths see the same argument list.
            var body = OnNamedAxes((args, line, col) => Contour(name, args, line, col, filled, elevated));

            JgsValue Single(IReadOnlyList<JgsValue> args, int line, int col) => body(args, line, col);

            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, Single)
            {
                BindsAnsAsStatement = !silent,
                MultiOutput = (args, wanted, line, col) =>
                {
                    JgsValue handle = Single(args, line, col);
                    JgsHandleEntry entry = JgsHandleRegistry.Require(handle, line, col);
                    return wanted >= 2 && entry.Target is ContourPlot plot
                        ? [ContourMatrixOf(plot), handle]
                        : [handle];
                },
            }));
        }

        DefineContour("contour", silent: true, filled: false, elevated: false);
        DefineContour("contourf", silent: true, filled: true, elevated: false);
        DefineContour("contour3", silent: false, filled: false, elevated: true);

        // --- TeX from a plain expression ------------------------------------------------------------
        Define("texlabel", (args, line, col) =>
        {
            ArityRange("texlabel", args, 1, 2, line, col);
            bool literal = args.Count == 2
                && Str("texlabel", args, 1, line, col).Equals("literal", StringComparison.OrdinalIgnoreCase);
            if (args.Count == 2 && !literal)
            {
                throw new JgsRuntimeException(line, col,
                    $"texlabel's second argument is 'literal', but got '{Str("texlabel", args, 1, line, col)}'.");
            }

            return JgsValue.Str(TexLabel(Str("texlabel", args, 0, line, col), literal));
        });
    }

    /// <summary>
    /// The shape every titling verb has: one string, then <c>'Name', value</c> text properties. The
    /// text is written first and the style after it, so a verb that is only given options — the way
    /// <c>title('')</c> followed by a restyle would be — still leaves the axes consistent.
    /// </summary>
    private static JgsValue Titled(
        string verb,
        IReadOnlyList<JgsValue> args,
        int line,
        int col,
        Action<string> write,
        Func<TextStyle> read,
        Action<TextStyle> restyle)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb} expects the text to write.");
        }

        write(TitleText(verb, args[0], line, col));

        if (args.Count > 1)
        {
            restyle(TextOptions(verb, read(), args, 1, line, col));
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// The text a titling verb was handed. A cell of strings is MATLAB's multi-line title; this build
    /// draws one line, so the rows are joined with a space and the divergence is recorded rather than
    /// the call refused — losing the line break is better than losing the title.
    /// </summary>
    private static string TitleText(string verb, JgsValue value, int line, int col)
    {
        if (value.Type != JgsType.Cell)
        {
            return StrOf(verb, value, line, col);
        }

        var parts = new List<string>();
        foreach (JgsValue item in value.AsCell)
        {
            parts.Add(StrOf(verb, item, line, col));
        }

        return string.Join(' ', parts);
    }

    /// <summary>Applies <c>'Name', value</c> text properties onto a style, naming the spellings it knows.</summary>
    private static TextStyle TextOptions(
        string verb, TextStyle style, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: text options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf(verb, args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "color":
                    style = style.WithColor(OptionColor(value, line, col, verb));
                    break;
                case "fontsize":
                    style = style.WithSize(NumOf($"{verb}: FontSize", value, line, col));
                    break;
                case "fontname":
                    style = new TextStyle(
                        style.Color, style.FontSize, StrOf($"{verb}: FontName", value, line, col),
                        style.Bold, style.Italic);
                    break;
                case "fontweight":
                    style = style.WithBold(Weight(verb, "FontWeight", value, "bold", "normal", line, col));
                    break;
                case "fontangle":
                    style = new TextStyle(
                        style.Color, style.FontSize, style.FontFamily, style.Bold,
                        Weight(verb, "FontAngle", value, "italic", "normal", line, col));
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"{verb}: unknown text option '{name}'. Use {string.Join(", ", TitleOptionNames)}.");
            }
        }

        return style;
    }

    /// <summary>A two-word switch such as bold/normal, read as the boolean the style actually stores.</summary>
    private static bool Weight(
        string verb, string option, JgsValue value, string onWord, string offWord, int line, int col)
    {
        string word = StrOf($"{verb}: {option}", value, line, col);
        if (word.Equals(onWord, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (word.Equals(offWord, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new JgsRuntimeException(line, col,
            $"{verb}: {option} is '{onWord}' or '{offWord}', but got '{word}'.");
    }

    /// <summary>
    /// <c>xline(v)</c> / <c>yline(v)</c>, with a vector drawing one line per value, an optional line
    /// spec, an optional label, and the constant-line properties after them. Returns the handle, or an
    /// array of handles when several values were named — which is what MATLAB returns.
    /// </summary>
    private static JgsValue ConstantLine(
        string verb, IReadOnlyList<JgsValue> args, int line, int col, bool vertical)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        return OnAxes(named, () =>
        {
            if (rest.Count == 0)
            {
                throw new JgsRuntimeException(line, col, $"{verb} expects the value to draw the line at.");
            }

            double[] values = ToDoubles(verb, rest[0], line, col);
            int next = 1;

            // A bare string after the value is a line spec ('--r'), then a label, then name/value
            // pairs. The spec is told from a label by whether LineSpec can read all of it.
            LineSpec? spec = null;
            if (next < rest.Count && rest[next].Type == JgsType.String && IsLineSpec(rest[next].AsString))
            {
                spec = LineSpec.Parse(rest[next].AsString);
                next++;
            }

            string? label = null;
            if (next < rest.Count && (rest[next].Type == JgsType.String || rest[next].Type == JgsType.Cell))
            {
                // Only a label if what follows it is a complete list of pairs — otherwise this string
                // is the name half of the first pair.
                if ((rest.Count - next) % 2 == 1)
                {
                    label = TitleText(verb, rest[next], line, col);
                    next++;
                }
            }

            var handles = new List<JgsValue>();
            foreach (double value in values)
            {
                ConstantLinePlot plot = vertical ? JG.XLine(value) : JG.YLine(value);
                plot.Color ??= PaletteColorFor(plot);

                if (spec is { } style)
                {
                    if (style.Color is { } color)
                    {
                        plot.Color = color;
                    }

                    if (style.Dash is { } dash)
                    {
                        plot.Dash = dash;
                    }
                }

                if (label is not null)
                {
                    plot.Label = label;
                }

                ConstantLineOptions(verb, plot, rest, next, line, col);
                handles.Add(Handle(plot));
            }

            return handles.Count == 1 ? handles[0] : JgsValue.Array([.. handles]);
        });
    }

    private static void ConstantLineOptions(
        string verb, ConstantLinePlot plot, IReadOnlyList<JgsValue> args, int start, int line, int col)
    {
        if ((args.Count - start) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, $"{verb}: options come in 'Name', value pairs.");
        }

        for (int i = start; i < args.Count; i += 2)
        {
            string name = StrOf(verb, args[i], line, col);
            JgsValue value = args[i + 1];
            switch (name.ToLowerInvariant())
            {
                case "color":
                    plot.Color = OptionColor(value, line, col, verb);
                    break;
                case "linewidth":
                    plot.LineWidth = NumOf($"{verb}: LineWidth", value, line, col);
                    break;
                case "linestyle":
                    plot.Dash = LineSpec.Parse(StrOf($"{verb}: LineStyle", value, line, col)).Dash ?? plot.Dash;
                    break;
                case "label":
                    plot.Label = TitleText(verb, value, line, col);
                    break;
                case "displayname":
                    SetDisplayName(plot, StrOf($"{verb}: DisplayName", value, line, col));
                    break;
                case "alpha":
                    plot.Opacity = NumOf($"{verb}: Alpha", value, line, col);
                    break;
                case "fontsize":
                    plot.LabelStyle = (plot.LabelStyle ?? new TextStyle(plot.Color ?? Colors.Black, 10))
                        .WithSize(NumOf($"{verb}: FontSize", value, line, col));
                    break;
                case "labelhorizontalalignment":
                    plot.LabelHorizontalAlignment = StrOf($"{verb}: LabelHorizontalAlignment", value, line, col)
                        .ToLowerInvariant() switch
                        {
                            "left" => HorizontalAlignment.Left,
                            "center" => HorizontalAlignment.Center,
                            "right" => HorizontalAlignment.Right,
                            _ => throw new JgsRuntimeException(line, col,
                                $"{verb}: LabelHorizontalAlignment is 'left', 'center' or 'right'."),
                        };
                    break;
                case "labelverticalalignment":
                    plot.LabelVerticalAlignment = StrOf($"{verb}: LabelVerticalAlignment", value, line, col)
                        .ToLowerInvariant() switch
                        {
                            "top" => VerticalAlignment.Top,
                            "middle" => VerticalAlignment.Middle,
                            "bottom" => VerticalAlignment.Bottom,
                            _ => throw new JgsRuntimeException(line, col,
                                $"{verb}: LabelVerticalAlignment is 'top', 'middle' or 'bottom'."),
                        };
                    break;
                default:
                    throw new JgsRuntimeException(line, col,
                        $"{verb}: unknown option '{name}'. Use Color, LineWidth, LineStyle, Label, "
                        + "DisplayName, Alpha, FontSize, LabelHorizontalAlignment, LabelVerticalAlignment.");
            }
        }
    }

    /// <summary>
    /// <c>clabel(C, h)</c> and its shorter forms. The contour matrix is read for nothing but its
    /// presence — the levels a script names come from the third argument, and the plot itself knows
    /// which curves it drew — so the MATLAB idiom <c>[C, h] = contour(…); clabel(C, h)</c> works
    /// unchanged, and <c>clabel(h)</c> works too.
    /// </summary>
    private static JgsValue Clabel(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "clabel expects a contour to label.");
        }

        // Whichever of the first two arguments is a contour handle is the one meant; the other is the
        // contour matrix, which this build does not need.
        ContourPlot? contour = null;
        int next = 1;
        for (int i = 0; i < System.Math.Min(2, args.Count); i++)
        {
            if (JgsHandleRegistry.TryGet(args[i], out JgsHandleEntry? entry) && entry.Target is ContourPlot found)
            {
                contour = found;
                next = i + 1;
            }
        }

        if (contour is null)
        {
            throw new JgsRuntimeException(line, col,
                "clabel wants the handle a contour returned, as in [C, h] = contour(X, Y, Z); clabel(C, h).");
        }

        double[]? levels = null;
        if (next < args.Count && args[next].Type is JgsType.Number or JgsType.Array)
        {
            levels = ToDoubles("clabel", args[next], line, col);
            next++;
        }

        // 'manual' asks the user to click each label into place, which a script cannot answer for.
        if (next < args.Count && args[next].Type == JgsType.String
            && args[next].AsString.Equals("manual", StringComparison.OrdinalIgnoreCase))
        {
            throw new JgsRuntimeException(line, col,
                "clabel 'manual' places labels by clicking, which a script cannot do. "
                + "Name the levels instead: clabel(C, h, [1 2 3]).");
        }

        contour.ShowText = true;
        contour.LabelLevels = levels;

        if (next < args.Count)
        {
            contour.LabelStyle = TextOptions(
                "clabel", contour.LabelStyle ?? new TextStyle(Colors.Black, 9), args, next, line, col);
        }

        return Handle(contour);
    }

    /// <summary>
    /// <c>texlabel</c>: the TeX an expression written in plain characters would have been. Greek names
    /// become their commands, a run of digits or a parenthesised group after <c>_</c> or <c>^</c>
    /// becomes a braced group, and digits trailing a name become a subscript — which is the whole of
    /// what the documented examples show. <c>'literal'</c> leaves Greek names as words.
    /// </summary>
    private static string TexLabel(string expression, bool literal)
    {
        var output = new StringBuilder();
        int i = 0;
        while (i < expression.Length)
        {
            char c = expression[i];
            if (char.IsLetter(c))
            {
                int start = i;
                while (i < expression.Length && char.IsLetter(expression[i]))
                {
                    i++;
                }

                string word = expression[start..i];
                output.Append(!literal && GreekNames.Contains(word) ? "\\" + word : word);

                // Digits immediately after a name are its subscript: lambda12 is \lambda_{12}.
                int digits = i;
                while (digits < expression.Length && char.IsAsciiDigit(expression[digits]))
                {
                    digits++;
                }

                if (digits > i)
                {
                    output.Append("_{").Append(expression[i..digits]).Append('}');
                    i = digits;
                }

                continue;
            }

            if (c is '^' or '_')
            {
                output.Append(c);
                i++;
                output.Append(Braced(expression, ref i));
                continue;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    /// <summary>The group an exponent or subscript applies to, always emitted braced.</summary>
    private static string Braced(string expression, ref int i)
    {
        if (i >= expression.Length)
        {
            return "{}";
        }

        if (expression[i] == '(')
        {
            int depth = 0;
            int start = ++i;
            while (i < expression.Length)
            {
                if (expression[i] == '(')
                {
                    depth++;
                }
                else if (expression[i] == ')')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }

                i++;
            }

            string inner = expression[start..System.Math.Min(i, expression.Length)];
            if (i < expression.Length)
            {
                i++; // the closing bracket
            }

            return "{" + TexLabel(inner, literal: false) + "}";
        }

        int from = i;
        while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '.'))
        {
            i++;
        }

        return "{" + (i > from ? TexLabel(expression[from..i], literal: false) : string.Empty) + "}";
    }

    /// <summary>The names texlabel turns into commands, lower and upper case as TeX spells them.</summary>
    private static readonly HashSet<string> GreekNames = new(StringComparer.Ordinal)
    {
        "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta", "vartheta", "iota",
        "kappa", "lambda", "mu", "nu", "xi", "pi", "rho", "sigma", "varsigma", "tau", "upsilon",
        "phi", "varphi", "chi", "psi", "omega",
        "Gamma", "Delta", "Theta", "Lambda", "Xi", "Pi", "Sigma", "Upsilon", "Phi", "Psi", "Omega",
        "infty", "partial", "int", "sum", "prod", "surd", "approx", "neq", "leq", "geq", "pm",
        "times", "div", "rightarrow", "leftarrow", "propto", "sim", "cong", "equiv",
    };

    /// <summary>The contour matrix a labelled contour was drawn from, in MATLAB's C layout.</summary>
    private static JgsValue ContourMatrixOf(ContourPlot plot) =>
        ContourMatrix(plot.X, plot.Y, plot.Z, plot.ResolvedLevels);

    /// <summary>
    /// Whether a string is a line spec rather than a label. Every character has to be one the spec
    /// grammar consumes, and there are at most four of them — the same ambiguity MATLAB has, resolved
    /// the same way, so a label spelled entirely out of spec characters must be named with 'Label'.
    /// </summary>
    private static bool IsLineSpec(string text)
    {
        const string Alphabet = "-:.bgrcmykwox+*sd^vph";
        if (text.Length is 0 or > 4)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!Alphabet.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
