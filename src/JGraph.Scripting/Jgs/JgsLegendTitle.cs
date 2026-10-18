using System.Runtime.CompilerServices;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// A legend's <c>Title</c>: the text object R2025b's legend answers with,
/// <c>matlab.graphics.illustration.legend.Text</c> (open item 141, probes <c>oi141_title</c>,
/// <c>oi141_font</c>, <c>oi141_follow</c>, <c>oi141_classes</c>). It is a handle of its own with 21
/// properties - the words, the font, the colour, their modes, and the handful every graphics object
/// has - and no Type, Tag or Position. Its words and style live on the <see cref="LegendModel"/>
/// (the document carries them and the renderer draws them); this object is the stable handle a
/// script holds, one per legend, made when first reached.
/// <para>
/// It is not a child of the legend: R2025b's <c>allchild(lg)</c> is empty, so searches do not find
/// it. The handle registry keeps it alive for as long as its legend lives, and a deleted legend
/// takes it with it (<see cref="Forget"/>), so a legend made again has a title of its own.
/// </para>
/// </summary>
internal sealed class JgsLegendTitle : GraphObject
{
    private static readonly ConditionalWeakTable<LegendModel, JgsLegendTitle> Titles = new();

    private JgsValue? _stored;
    private string? _rendered;

    private JgsLegendTitle(LegendModel legend)
    {
        Legend = legend;
        Selectable = false; // HitTest 'off' (oi141_follow G15)
    }

    /// <summary>The legend whose title this is.</summary>
    public LegendModel Legend { get; }

    /// <summary>The legend's title object, made the first time a script reaches it.</summary>
    public static JgsLegendTitle For(LegendModel legend) => Titles.GetValue(legend, static l => new JgsLegendTitle(l));

    /// <summary>The legend's title object if a script has reached it, else null.</summary>
    public static JgsLegendTitle? Existing(LegendModel legend) => Titles.TryGetValue(legend, out JgsLegendTitle? title) ? title : null;

    /// <summary>
    /// What a deleted legend does to its title: the words and the style go, and so does the handle,
    /// so that a legend made again starts with an empty title of its own (oi141_title T51).
    /// </summary>
    public static void Forget(LegendModel legend)
    {
        legend.Title = null;
        legend.TitleFontSize = null;
        legend.TitleBold = null;
        legend.TitleItalic = null;
        legend.TitleColor = null;
        legend.TitleColorNone = false;
        legend.TitleFontFamily = null;
        legend.TitleInterpreter = null;
        legend.TitleVisible = true;
        if (Titles.TryGetValue(legend, out JgsLegendTitle? title))
        {
            Titles.Remove(legend);
            JgsHandleRegistry.Retire(title);
        }
    }

    /// <summary>
    /// <c>String</c>: the words as written - a character vector, or a column cell of them for more
    /// than one line, as R2025b turns a row cell or a string array (oi141_title T18, T19) - or the
    /// legend's words when something else wrote them since.
    /// </summary>
    public JgsValue ReadString()
    {
        string text = Legend.Title ?? string.Empty;
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
        if (lines is null)
        {
            string text = JgsBuiltins.TitleText("String", value, line, col);
            _stored = null;
            _rendered = null;
            Legend.Title = text;
            return;
        }

        string[] words = [.. lines.Select(item => JgsBuiltins.TitleText("String", item, line, col))];
        JgsValue column = JgsValue.Cell([.. words.Select(static w => JgsValue.Str(w))]);
        column.Reshape(words.Length, words.Length == 0 ? 0 : 1);
        string joined = string.Join("\n", words);
        Legend.Title = joined;
        _stored = column;
        _rendered = joined;
    }

    /// <summary><c>delete(lg.Title)</c>: R2025b empties the words and keeps the object (oi141_font F20).</summary>
    public void Clear()
    {
        _stored = null;
        _rendered = null;
        Legend.Title = null;
    }
}
