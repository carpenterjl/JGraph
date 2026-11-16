using System.Runtime.CompilerServices;
using JGraph.Core.Drawing;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A tiled layout's <c>Title</c>, <c>Subtitle</c>, <c>XLabel</c> or <c>YLabel</c> in a MATLAB-dialect
/// script: R2025b's <c>matlab.graphics.layout.Text</c> (open item 348, ADR 0382; probes p8eb, p8ed,
/// p8ee). A handle of its own with fourteen properties - the words, the font, the angle, the
/// alignments, the interpreter, the colour, and Children, Parent, Visible and HandleVisibility - and
/// no Type, Tag or Position. Its words and style live on the <see cref="TiledLayoutModel"/> (the
/// document carries them and the renderer draws them); the angle, the alignments, the font's name and
/// the visibility are kept here. One object per layout and role, made when first reached; its handle
/// is hidden, so no search finds it, and it dies with its layout (<see cref="Forget"/>). The JGS
/// dialect reads the layout's text as a string, as it always has.
/// </summary>
internal sealed class JgsLayoutText : GraphObject
{
    private static readonly ConditionalWeakTable<TiledLayoutModel, Dictionary<string, JgsLayoutText>> Texts = new();

    private JgsValue? _stored;
    private string? _rendered;
    private string? _hiddenWords;

    private JgsLayoutText(TiledLayoutModel layout, string role)
    {
        Layout = layout;
        Role = role;
        Rotation = role == "YLabel" ? 90 : 0;
        VerticalAlignment = role == "XLabel" ? "top" : "bottom";
        Selectable = false;
    }

    /// <summary>The layout whose text this is.</summary>
    public TiledLayoutModel Layout { get; }

    /// <summary>Which of the four: <c>Title</c>, <c>Subtitle</c>, <c>XLabel</c> or <c>YLabel</c>.</summary>
    public string Role { get; }

    /// <summary>The angle the text is written at: 90 for the y label, 0 for the rest (probe p8ee).</summary>
    public double Rotation { get; set; }

    public string HorizontalAlignment { get; set; } = "center";

    /// <summary><c>'bottom'</c>, the x label's <c>'top'</c> (probe p8ee).</summary>
    public string VerticalAlignment { get; set; }

    /// <summary>The font's name as written, or null for R2025b's <c>'Helvetica'</c>.</summary>
    public string? FontName { get; set; }

    /// <summary>Whether the size was written (<c>FontSizeMode</c> <c>'manual'</c>).</summary>
    public bool FontSizeManual { get; set; }

    /// <summary>Whether the text shows; hidden, the layout holds no words for it and the words wait here.</summary>
    public bool Shown => _hiddenWords is null;

    /// <summary>The layout's text object for <paramref name="role"/>, made the first time a script reaches it.</summary>
    public static JgsLayoutText For(TiledLayoutModel layout, string role)
    {
        Dictionary<string, JgsLayoutText> held = Texts.GetValue(layout, static _ => new(StringComparer.Ordinal));
        if (!held.TryGetValue(role, out JgsLayoutText? text))
        {
            text = new JgsLayoutText(layout, role);
            held[role] = text;
            JgsHandleRegistry.EntryFor(text).HandleVisibility = "off"; // R2025b's (probe p8eb)
        }

        return text;
    }

    /// <summary>The text objects a script has reached on <paramref name="layout"/>.</summary>
    public static IEnumerable<JgsLayoutText> Existing(TiledLayoutModel layout) =>
        Texts.TryGetValue(layout, out Dictionary<string, JgsLayoutText>? held) ? held.Values : [];

    /// <summary>A deleted layout takes its text objects with it: their handles go.</summary>
    public static void Forget(TiledLayoutModel layout)
    {
        if (Texts.TryGetValue(layout, out Dictionary<string, JgsLayoutText>? held))
        {
            Texts.Remove(layout);
            foreach (JgsLayoutText text in held.Values)
            {
                JgsHandleRegistry.Retire(text);
            }
        }
    }

    /// <summary>The words the layout holds for this role.</summary>
    private string Words
    {
        get => Role switch
        {
            "Title" => Layout.Title,
            "Subtitle" => Layout.Subtitle,
            "XLabel" => Layout.XLabel,
            _ => Layout.YLabel,
        };
        set
        {
            switch (Role)
            {
                case "Title": Layout.Title = value; break;
                case "Subtitle": Layout.Subtitle = value; break;
                case "XLabel": Layout.XLabel = value; break;
                default: Layout.YLabel = value; break;
            }

            Layout.Arrange();
        }
    }

    /// <summary>The style the layout draws this role in.</summary>
    public TextStyle Style
    {
        get => Role switch
        {
            "Title" => Layout.TitleStyle,
            "Subtitle" => Layout.SubtitleStyle,
            "XLabel" => Layout.XLabelStyle,
            _ => Layout.YLabelStyle,
        };
        set
        {
            switch (Role)
            {
                case "Title": Layout.TitleStyle = value; break;
                case "Subtitle": Layout.SubtitleStyle = value; break;
                case "XLabel": Layout.XLabelStyle = value; break;
                default: Layout.YLabelStyle = value; break;
            }
        }
    }

    /// <summary>
    /// <c>String</c>: the words as written - a character vector, or a column cell of them for more
    /// than one line (probe p8eb <c>tt_set_cellstr</c>) - or the layout's words when something else
    /// wrote them since.
    /// </summary>
    public JgsValue ReadString()
    {
        string text = _hiddenWords ?? Words;
        return _stored is not null && _rendered == text ? _stored : JgsValue.Str(text);
    }

    /// <summary>Writes the words: a number as its digits, a string as characters, several as lines.</summary>
    public void WriteString(JgsValue value, int line, int col)
    {
        JgsValue[]? lines = value.Type == JgsType.Cell
            ? value.AsCell
            : value.IsStringArray && value.ArrayLength != 1
                ? [.. Enumerable.Range(0, value.ArrayLength).Select(value.ElementAt)]
                : null;
        string joined;
        if (lines is null)
        {
            joined = JgsBuiltins.TitleText("String", value, line, col);
            _stored = null;
            _rendered = null;
        }
        else
        {
            string[] words = [.. lines.Select(item => JgsBuiltins.TitleText("String", item, line, col))];
            JgsValue column = JgsValue.Cell([.. words.Select(static w => JgsValue.Str(w))]);
            column.Reshape(words.Length, words.Length == 0 ? 0 : 1);
            joined = string.Join("\n", words);
            _stored = column;
            _rendered = joined;
        }

        if (_hiddenWords is not null)
        {
            _hiddenWords = joined;
        }
        else
        {
            Words = joined;
        }
    }

    /// <summary>Shows or hides the text: hidden, the layout draws and makes room for nothing.</summary>
    public void Show(bool shown)
    {
        if (shown && _hiddenWords is { } waiting)
        {
            _hiddenWords = null;
            Words = waiting;
        }
        else if (!shown && _hiddenWords is null)
        {
            _hiddenWords = Words;
            Words = string.Empty;
        }
    }
}
