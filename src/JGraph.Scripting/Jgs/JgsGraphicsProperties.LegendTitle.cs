using JGraph.Core.Drawing;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The surface of a legend's title, <see cref="JgsLegendTitle"/> (open item 141): R2025b's 21 names
/// of <c>matlab.graphics.illustration.legend.Text</c> and nothing else (<c>oi141_classes</c>). The
/// size, angle, font, interpreter and colour follow the legend's own until written, the weight is
/// bold until written, and each has a mode saying which (probes <c>oi141_font</c>,
/// <c>oi141_follow</c>); the refusals are R2025b's sentences (<c>oi141_title</c>).
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private const string LegendTextClass = "matlab.graphics.illustration.legend.Text";

    private static void AddLegendTitleBlock(IDictionary<string, GraphicsProperty> table)
    {
        static JgsLegendTitle Title(JgsHandleEntry entry) => (JgsLegendTitle)entry.Target;
        static LegendModel Legend(JgsHandleEntry entry) => Title(entry).Legend;

        // What the legend's own text is now, its automatic size settled first (open item 118).
        static TextStyle Following(JgsHandleEntry entry)
        {
            (Legend(entry).Parent as AxesModel)?.ApplyAutoFont();
            return Legend(entry).TextStyle;
        }

        Put(table, "String",
            entry => Title(entry).ReadString(),
            (entry, value, line, col) => Title(entry).WriteString(value, line, col));

        Put(table, "Color",
            entry => Legend(entry).TitleColorNone ? JgsValue.Str("none") : ColorRow(Legend(entry).TitleColor ?? Following(entry).Color),
            (entry, value, line, col) =>
            {
                if (value.Type == JgsType.String && value.AsString.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    Legend(entry).TitleColor = null;
                    Legend(entry).TitleColorNone = true;
                    return;
                }

                Legend(entry).TitleColor = JgsBuiltins.OptionColor(value, line, col, "Color");
                Legend(entry).TitleColorNone = false;
            });

        Put(table, "FontAngle",
            entry => JgsValue.Str((Legend(entry).TitleItalic ?? Following(entry).Italic) ? "italic" : "normal"),
            (entry, value, line, col) => Legend(entry).TitleItalic =
                LegendTextWord("FontAngle", value, ["normal", "italic"], line, col) == "italic");

        Put(table, "FontName",
            entry => JgsValue.Str(Legend(entry).TitleFontFamily ?? Following(entry).FontFamily),
            (entry, value, line, col) => Legend(entry).TitleFontFamily = JgsBuiltins.StrOf("FontName", value, line, col));

        Put(table, "FontSize",
            entry => JgsValue.Number(Legend(entry).TitleFontSize ?? Following(entry).FontSize),
            (entry, value, line, col) =>
            {
                double size = value.Type is JgsType.Number or JgsType.Bool ? value.AsNumber : Numbers("FontSize", value, 1, line, col)[0];
                if (!(size > 0) || !double.IsFinite(size))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:datatypes:OutsideRangeFiniteGreater0",
                        "Error setting property 'FontSize' of class 'Text':\nValue should be a finite number greater than 0");
                }

                Legend(entry).TitleFontSize = size;
            });

        Put(table, "FontWeight",
            entry => JgsValue.Str((Legend(entry).TitleBold ?? true) ? "bold" : "normal"),
            (entry, value, line, col) => Legend(entry).TitleBold =
                LegendTextWord("FontWeight", value, ["normal", "bold"], line, col) == "bold");

        Put(table, "Interpreter",
            entry => JgsValue.Str(JgsBuiltins.InterpreterWord(Legend(entry).TitleInterpreter ?? Following(entry).Interpreter)),
            (entry, value, line, col) => Legend(entry).TitleInterpreter =
                LegendTextWord("Interpreter", value, ["none", "tex", "latex"], line, col) switch
                {
                    "none" => TextInterpreter.None,
                    "latex" => TextInterpreter.Latex,
                    _ => TextInterpreter.Tex,
                });

        // Each mode says whether its setting was written; 'manual' keeps what shows, 'auto' lets it follow again.
        AddLegendTitleMode(table, "ColorMode",
            entry => Legend(entry).TitleColor is not null || Legend(entry).TitleColorNone,
            entry => Legend(entry).TitleColor ??= Following(entry).Color,
            entry =>
            {
                Legend(entry).TitleColor = null;
                Legend(entry).TitleColorNone = false;
            });
        AddLegendTitleMode(table, "FontAngleMode",
            entry => Legend(entry).TitleItalic is not null,
            entry => Legend(entry).TitleItalic ??= Following(entry).Italic,
            entry => Legend(entry).TitleItalic = null);
        AddLegendTitleMode(table, "FontNameMode",
            entry => Legend(entry).TitleFontFamily is not null,
            entry => Legend(entry).TitleFontFamily ??= Following(entry).FontFamily,
            entry => Legend(entry).TitleFontFamily = null);
        AddLegendTitleMode(table, "FontSizeMode",
            entry => Legend(entry).TitleFontSize is not null,
            entry => Legend(entry).TitleFontSize ??= Following(entry).FontSize,
            entry => Legend(entry).TitleFontSize = null);
        AddLegendTitleMode(table, "FontWeightMode",
            entry => Legend(entry).TitleBold is not null,
            entry => Legend(entry).TitleBold ??= true,
            entry => Legend(entry).TitleBold = null);
        AddLegendTitleMode(table, "InterpreterMode",
            entry => Legend(entry).TitleInterpreter is not null,
            entry => Legend(entry).TitleInterpreter ??= Following(entry).Interpreter,
            entry => Legend(entry).TitleInterpreter = null);

        // It holds nothing, and it belongs to its legend for good (oi141_follow G16, G17).
        Put(table, "Children",
            static _ => HandleRow([]),
            (entry, value, line, col) =>
            {
                if (value.Type == JgsType.Array && value.ArrayLength == 0)
                {
                    return;
                }

                throw new JgsRuntimeException(line, col, "A legend's title has no children to set.");
            });
        Put(table, "Parent",
            entry => JgsHandleRegistry.For(Legend(entry)),
            static (_, _, line, col) => throw new JgsRuntimeException(line, col, "MATLAB:hg:TryingToReparentConnectedObject",
                "Trying to reparent an already connected internal component object."));

        Put(table, "Visible",
            entry => OnOff(Legend(entry).TitleVisible),
            (entry, value, line, col) => Legend(entry).TitleVisible = ToOnOff("Visible", value, line, col));

        // The five every graphics object has, as AddAliases gives them to the rest.
        Put(table, "HandleVisibility",
            entry => JgsValue.Str(entry.HandleVisibility),
            (entry, value, line, col) => entry.HandleVisibility = value.Type is JgsType.Bool or JgsType.Number
                ? (value.IsTruthy ? "on" : "off")
                : HandleVisibilityWords[Word(entry, "HandleVisibility", value, HandleVisibilityWords, HandleVisibilityWords, line, col)]);
        Put(table, "Selected",
            entry => OnOff(entry.Target.IsSelected),
            (entry, value, line, col) => entry.Target.IsSelected = ToOnOff("Selected", value, line, col));
        Put(table, "SelectionHighlight",
            entry => OnOff(entry.Target.SelectionHighlight),
            (entry, value, line, col) => entry.Target.SelectionHighlight = ToOnOff("SelectionHighlight", value, line, col));
        Put(table, "HitTest",
            entry => OnOff(entry.Target.Selectable),
            (entry, value, line, col) => entry.Target.Selectable = ToOnOff("HitTest", value, line, col));
        Put(table, "PickableParts",
            entry => JgsValue.Str(entry.PickableParts),
            (entry, value, line, col) => entry.PickableParts =
                LegendTextWord("PickableParts", value, ["visible", "none", "all"], line, col));
    }

    /// <summary>A mode of the title's: 'manual' while its setting is written, 'auto' while it follows.</summary>
    private static void AddLegendTitleMode(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<JgsHandleEntry, bool> manual,
        Action<JgsHandleEntry> pin,
        Action<JgsHandleEntry> release)
    {
        Put(table, name,
            entry => AutoManual(manual(entry)),
            (entry, value, line, col) =>
            {
                if (LegendTextWord(name, value, ["auto", "manual"], line, col) == "manual")
                {
                    pin(entry);
                }
                else
                {
                    release(entry);
                }
            });
    }

    /// <summary>
    /// One of a property's words, in any case, or R2025b's refusal naming them all (oi141_title T29:
    /// "'heavy' is not a valid value. Use one of these values: 'normal' | 'bold'.").
    /// </summary>
    private static string LegendTextWord(string property, JgsValue value, string[] words, int line, int col)
    {
        string given = JgsBuiltins.IsTextScalar(value) ? JgsBuiltins.TextOf(value) : string.Empty;
        foreach (string word in words)
        {
            if (word.Equals(given, StringComparison.OrdinalIgnoreCase))
            {
                return word;
            }
        }

        string shown = JgsBuiltins.IsTextScalar(value) ? $"'{given}'" : "Value";
        throw new JgsRuntimeException(line, col, "MATLAB:datatypes:InvalidEnumValue",
            $"Error setting property '{property}' of class 'Text':\n{shown} is not a valid value. Use one of these values: "
            + string.Join(" | ", words.Select(static w => $"'{w}'")) + ".");
    }
}
