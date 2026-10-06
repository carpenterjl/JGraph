using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The properties of a <c>uihtml</c> (app-building plan, U9b): R2025b's 21 (probe
/// <c>u9b_matrix</c>) are the component's common ones less <c>Enable</c> and a font, plus
/// <c>Data</c>, <c>HTMLSource</c>, <c>DataChangedFcn</c> and <c>HTMLEventReceivedFcn</c>.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static UiHtmlModel Html(JgsHandleEntry entry) => (UiHtmlModel)entry.Target;

    private static void AddHtmlBlock(IDictionary<string, GraphicsProperty> table)
    {
        table.Remove("Enable");

        // Data takes anything, and keeps it as given; the page sees its jsonencode.
        Put(table, "Data",
            entry => JgsUiHtml.DataOf(Html(entry)),
            (entry, value, line, col) => JgsUiHtml.SetData(Html(entry), value));

        Put(table, "HTMLSource",
            entry => JgsValue.Str(Html(entry).Source),
            (entry, value, line, col) => JgsUiHtml.SetSource(Html(entry), HtmlSourceText(value, line, col),
                (identifier, message) => new JgsRuntimeException(line, col, identifier, message)));

        AddNamedSlot(table, "DataChangedFcn");
        AddNamedSlot(table, "HTMLEventReceivedFcn");

        // A string scalar is kept for these two (the set path would make it char, and a missing
        // one '<missing>'), so it is turned into the char row a callback is here: '' when missing.
        foreach (string name in new[] { "DataChangedFcn", "HTMLEventReceivedFcn" })
        {
            GraphicsProperty slot = table[name];
            Put(table, name, slot.Read, (entry, value, line, col) =>
            {
                if (value.IsStringArray && value.ArrayLength == 1)
                {
                    string text = value.ElementAt(0).AsString;
                    value = JgsValue.Str(JgsBuiltins.IsMissingText(text) ? string.Empty : text);
                }

                slot.Write!(entry, value, line, col);
            });
        }
    }

    /// <summary>
    /// The text an <c>HTMLSource</c> write names: a char row or a string scalar; <c>[]</c>,
    /// <c>""</c> and a missing string are no source (probe <c>u9b_matrix</c>).
    /// </summary>
    private static string HtmlSourceText(JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.String)
        {
            return value.AsString;
        }

        if (value.IsStringArray && value.ArrayLength == 1)
        {
            string text = value.ElementAt(0).AsString;
            return JgsBuiltins.IsMissingText(text) ? string.Empty : text;
        }

        if (value.Type == JgsType.Array && !value.IsStringArray && !value.IsCharMatrix && value.Rows == 0 && value.Cols == 0
            && JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "double")
        {
            return string.Empty;
        }

        throw new JgsRuntimeException(line, col, "MATLAB:ui:HTML:invalidHTMLSource",
            "'HTMLSource' must be a character vector or a string scalar.");
    }
}
