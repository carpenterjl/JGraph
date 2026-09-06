using System.Runtime.CompilerServices;
using JGraph.Core.Drawing;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

// Stable handles for text already drawn by the axes and colorbar renderers.
internal sealed class JgsTextLabel : GraphObject
{
    private static readonly ConditionalWeakTable<GraphObject, Dictionary<string, JgsTextLabel>> Labels = new();
    public GraphObject Owner { get; }
    public string Role { get; }
    private JgsValue? stored;
    private string? rendered;
    private JgsTextLabel(GraphObject owner, string role) { Owner = owner; Role = role; }
    public static JgsTextLabel For(GraphObject owner, string role = "Label") =>
        Labels.GetOrCreateValue(owner).TryGetValue(role, out var label) ? label
            : Labels.GetOrCreateValue(owner)[role] = new JgsTextLabel(owner, role);
    public static IEnumerable<GraphObject> Existing(GraphObject owner) =>
        Labels.TryGetValue(owner, out var labels) ? labels.Values : [];
    public string Text
    {
        get => Owner switch { AxisModel r => r.Label, ColorbarModel b => b.Label ?? "",
            AxesModel a => Role == "Subtitle" ? a.Subtitle : a.Title, FigureModel f => f.Title, _ => "" };
        set { switch (Owner) { case AxisModel r: r.Label = value; break; case ColorbarModel b: b.Label = value; break;
            case AxesModel a: if (Role == "Subtitle") a.Subtitle = value; else a.Title = value; break;
            case FigureModel f: f.Title = value; break; } }
    }
    public JgsValue ReadString() => stored is not null && rendered == Text ? stored : JgsValue.Str(Text);
    public void WriteString(JgsValue value, int line, int col)
    {
        rendered = JgsBuiltins.TitleText("String", value, line, col);
        stored = value.Type == JgsType.Number ? JgsValue.Str(rendered) : value;
        if (value.Type == JgsType.Cell)
        {
            stored = JgsValue.Cell(value.AsCell.Select(v => JgsValue.Str(JgsBuiltins.TitleText("String",v,line,col))).ToArray());
            stored.TakeShapeOf(value);
        }
        Text = rendered;
    }
    public TextStyle Style
    {
        get => Owner switch { AxisModel r => r.LabelStyle, ColorbarModel b => b.LabelStyle,
            AxesModel a => Role == "Subtitle" ? a.SubtitleStyle : a.TitleStyle, FigureModel f => f.TitleStyle, _ => new TextStyle(Colors.Black, 11) };
        set { switch (Owner) { case AxisModel r: r.LabelStyle = value; break; case ColorbarModel b: b.LabelStyle = value; break;
            case AxesModel a: if (Role == "Subtitle") a.SubtitleStyle = value; else a.TitleStyle = value; break;
            case FigureModel f: f.TitleStyle = value; break; } }
    }
    public double Rotation
    {
        get => Owner is AxisModel r ? r.LabelRotation ?? (r.Orientation == AxisOrientation.Vertical ? 90 : 0) : 0;
        set { if (Owner is AxisModel r) r.LabelRotation = value; }
    }
}
