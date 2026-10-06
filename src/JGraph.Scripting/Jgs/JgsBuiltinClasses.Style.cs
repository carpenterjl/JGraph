using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>matlab.ui.style.Style</c>, the value class <c>uistyle</c> makes (app-building plan, U9): ten
/// properties, each empty until set, each checked by R2025b's rule when it is (probe
/// <c>u9_forms</c>). A value class: <c>s2 = s</c> is a copy, and a style added to a component is
/// copied as it stands. The class is declared here, as the mixins and <c>AppBase</c> are, with
/// native <c>set</c> methods for the checks.
/// </summary>
internal static partial class JgsBuiltinClasses
{
    /// <summary>The class <c>uistyle</c> makes.</summary>
    public const string Style = "matlab.ui.style.Style";

    /// <summary>The properties, in the order R2025b lists them.</summary>
    public static readonly string[] StyleProperties =
    [
        "BackgroundColor", "FontColor", "FontWeight", "FontAngle", "FontName", "HorizontalAlignment", "HorizontalClipping", "IconAlignment", "Interpreter", "Icon",
    ];

    private static readonly string[] StockStyleIcons = ["error", "warning", "info", "success", "question"];

    private static ClassdefStmt StyleDeclaration()
    {
        var properties = new List<ClassProperty>(StyleProperties.Length);
        var methods = new List<ClassMethod>(StyleProperties.Length);
        foreach (string name in StyleProperties)
        {
            JgsValue start = name is "BackgroundColor" or "FontColor" ? JgsMatrix.FromColumnMajor([], 0, 0) : JgsValue.Str(string.Empty);
            properties.Add(new ClassProperty(new ArgumentSpec(name, null, null, [], new PreEvaluated(start)), Constant: false));
            string captured = name;
            methods.Add(new ClassMethod(new FnStmt("set." + name, ["obj", "value"], [], ["obj"]) { Dialect = JgsDialect.Matlab }, Static: false)
            {
                Native = new BuiltinFunction("set." + name, (args, line, col) => SetStyleProperty(captured, args, line, col)) { KeepsStringArguments = true },
            });
        }

        return new ClassdefStmt(Style, isHandle: false, properties, methods)
        {
            Dialect = JgsDialect.Matlab,
            Superclasses = [],
        };
    }

    /// <summary><c>s.Name = value</c>: the checked value in a copy of the object, which is what a value class's set method answers.</summary>
    private static JgsValue SetStyleProperty(string name, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 2 || args[0].Type != JgsType.Object)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }

        JgsObject source = args[0].AsObject;
        var copy = new JgsObject(source.Class);
        foreach ((string field, JgsValue held) in source.Fields)
        {
            copy.Fields[field] = held;
        }

        copy.Fields[name] = StyleValueOf(name, args[1], line, col);
        return JgsValue.Object(copy);
    }

    private static JgsRuntimeException StyleError(string id, string text, int line, int col) => new(line, col, "MATLAB:ui:Style:" + id, text);

    /// <summary>A style property's value as R2025b stores it, or its refusal.</summary>
    public static JgsValue StyleValueOf(string name, JgsValue value, int line, int col)
    {
        bool empty = (value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray)
            || (value.Type == JgsType.String && value.AsString.Length == 0)
            || (value.IsStringArray && value.ArrayLength == 0);
        switch (name)
        {
            case "BackgroundColor":
            case "FontColor":
            {
                if (empty)
                {
                    return JgsMatrix.FromColumnMajor([], 0, 0);
                }

                UiColor? colour = StyleColor(value);
                return colour is { } c ? JgsGraphicsProperties.Row(c.R, c.G, c.B) : throw StyleError("invalidColor", "Invalid color value.", line, col);
            }

            case "FontWeight":
                return StyleWord(name, value, ["normal", "bold"], "'FontWeight' value must be 'normal', 'bold', or ''.", line, col);
            case "FontAngle":
                return StyleWord(name, value, ["normal", "italic"], "'FontAngle' value must be 'normal', 'italic', or ''.", line, col);
            case "HorizontalAlignment":
                return StyleWord(name, value, ["left", "center", "right"], "'HorizontalAlignment' value must be 'left', 'center', 'right', or ''.", line, col);
            case "HorizontalClipping":
                return StyleWord(name, value, ["left", "right"], "'HorizontalClipping' value must be 'left', 'right', or ''.", line, col);
            case "IconAlignment":
                return StyleWord(name, value, ["left", "center", "right", "leftmargin", "rightmargin"],
                    "'IconAlignment' value must be 'left', 'center', 'right', 'leftmargin', 'rightmargin', or ''.", line, col);
            case "Interpreter":
                return StyleWord(name, value, ["none", "html", "latex", "tex"], "'Interpreter' value must be 'none', 'html', 'latex', 'tex', or ''.", line, col);
            case "FontName":
                if (empty)
                {
                    return JgsValue.Str(string.Empty);
                }

                return JgsBuiltins.IsTextScalar(value) && !value.IsCharMatrix
                    ? JgsValue.Str(JgsBuiltins.TextOf(value))
                    : throw StyleError("invalidFontName", "'FontName' must be a character vector, string scalar, or ''.", line, col);
            default:
                return StyleIcon(value, line, col);
        }
    }

    private static JgsValue StyleWord(string name, JgsValue value, string[] words, string sentence, int line, int col)
    {
        if (value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray)
        {
            return JgsValue.Str(string.Empty);
        }

        if (value.Type is JgsType.Bool or JgsType.Cell or JgsType.Struct or JgsType.Function or JgsType.Object
            || (value.Type == JgsType.Array && !value.IsStringArray && !value.IsCharMatrix))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:validation:UnableToConvert",
                $"Error setting property '{name}' of class 'matlab.ui.style.internal.Stylable'. Value must be of type char or be convertible to char.");
        }

        string typed = JgsBuiltins.IsTextScalar(value) && !value.IsCharMatrix ? JgsBuiltins.TextOf(value) : "\0";
        if (typed.Length == 0)
        {
            return JgsValue.Str(string.Empty);
        }

        string? word = Array.Find(words, w => w.Equals(typed, StringComparison.OrdinalIgnoreCase));
        return word is not null ? JgsValue.Str(word) : throw StyleError("invalid" + name, sentence, line, col);
    }

    /// <summary>A colour as a style takes it: a name, a hexadecimal code, or three numbers in [0, 1]; null for anything else.</summary>
    private static UiColor? StyleColor(JgsValue value)
    {
        if (JgsBuiltins.IsTextScalar(value) && !value.IsCharMatrix)
        {
            string word = JgsBuiltins.TextOf(value).Trim();
            return word.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : JgsGraphicsProperties.NamedColorOf(word);
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        if (value.Type != JgsType.Array || value.ArrayLength != 3 || kind == "logical" || JgsNumericClasses.Parse(kind) is null)
        {
            return null;
        }

        double[] rgb = JgsBuiltins.ToDoubles("Color", value, 0, 0);
        double scale = kind switch
        {
            "uint8" => 255,
            "uint16" => 65535,
            _ => 1,
        };
        for (int i = 0; i < 3; i++)
        {
            rgb[i] /= scale;
            if (!(rgb[i] >= 0 && rgb[i] <= 1))
            {
                return null;
            }
        }

        return new UiColor(rgb[0], rgb[1], rgb[2]);
    }

    /// <summary>
    /// A style's <c>Icon</c>: a stock word, a file that exists (kept by name), an m-by-n-by-3
    /// array, or empty; a file name that is no file warns and leaves none (probe <c>u9_forms</c>).
    /// </summary>
    private static JgsValue StyleIcon(JgsValue value, int line, int col)
    {
        if ((value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray) || (value.Type == JgsType.String && value.AsString.Length == 0))
        {
            return JgsValue.Str(string.Empty);
        }

        if (JgsBuiltins.IsTextScalar(value) && !value.IsCharMatrix)
        {
            string text = JgsBuiltins.TextOf(value);
            if (JgsBuiltins.IsMissingText(text))
            {
                return JgsValue.Str(string.Empty);
            }

            if (StockStyleIcons.Contains(text, StringComparer.Ordinal))
            {
                return JgsValue.Str(text);
            }

            string path = JgsCallbackDispatcher.Current?.Interpreter?.Host is { } host ? host.Resolve(text) : text;
            if (File.Exists(path))
            {
                return JgsValue.Str(text);
            }

            if (Path.GetExtension(text).Length > 0)
            {
                JgsGraphicsProperties.WarnOnce("MATLAB:ui:Style:invalidIconNotInPath", $"File '{text}' is not on the MATLAB or specified path.");
                return JgsValue.Str(string.Empty);
            }

            throw StyleError("invalidIconFile",
                "You have specified a file that cannot be found or is not an image. Specify a file name that is on the MATLAB path, or use a full or relative path.", line, col);
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        int[] dims = value.Type == JgsType.Array ? value.Dims : [1, 1];
        if (value.Type != JgsType.Array || value.IsStringArray || kind is not ("double" or "single" or "uint8" or "uint16") || dims.Length != 3 || dims[2] != 3)
        {
            throw StyleError("invalidIconCData",
                "You have specified an invalid CData. Specify an RGB image as an m-by-n-by-3 array of type double, single, uint8, or uint16.", line, col);
        }

        return JgsValue.Share(value);
    }

    /// <summary>What a style object sets, for the window to draw by.</summary>
    public static UiStyle StyleOf(JgsObject style)
    {
        JgsValue Field(string name) => style.Fields.TryGetValue(name, out JgsValue? held) ? held : JgsValue.Str(string.Empty);
        static UiColor? ColourOf(JgsValue value) =>
            value.Type == JgsType.Array && value.ArrayLength == 3 ? new UiColor(value.ElementAt(0).AsNumber, value.ElementAt(1).AsNumber, value.ElementAt(2).AsNumber) : null;
        static string Word(JgsValue value) => value.Type == JgsType.String ? value.AsString : string.Empty;
        JgsValue icon = Field("Icon");
        UiImage? picture = null;
        string source = string.Empty;
        if (icon.Type == JgsType.String && icon.AsString.Length > 0)
        {
            source = icon.AsString;
            string path = JgsCallbackDispatcher.Current?.Interpreter?.Host is { } host ? host.Resolve(source) : source;
            picture = File.Exists(path) ? JgsBuiltins.TryReadPicture(path) : null;
        }
        else if (icon.Type == JgsType.Array && icon.Dims.Length == 3)
        {
            picture = JgsGraphicsProperties.PictureFromCube(icon);
        }

        return new UiStyle
        {
            BackgroundColor = ColourOf(Field("BackgroundColor")),
            FontColor = ColourOf(Field("FontColor")),
            FontWeight = Word(Field("FontWeight")),
            FontAngle = Word(Field("FontAngle")),
            FontName = Word(Field("FontName")),
            HorizontalAlignment = Word(Field("HorizontalAlignment")),
            HorizontalClipping = Word(Field("HorizontalClipping")),
            IconAlignment = Word(Field("IconAlignment")),
            Interpreter = Word(Field("Interpreter")),
            Icon = picture,
            IconSource = source,
        };
    }
}
