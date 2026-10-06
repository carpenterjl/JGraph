using JGraph.Core.Model;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// What a graphics callback property holds, and how it runs (app-building plan, U1). R2025b takes
/// the same three forms in every such property — a function handle, text, or a cell led by either —
/// and refuses anything else with one message. Before U1 a graphics slot took a function handle
/// alone (and text for <c>CloseRequestFcn</c>, which then failed to run), while timers and audio
/// already took all three; this is the one place that now says what they all take.
/// </summary>
internal static class JgsGraphicsCallbackValues
{
    private const string CreateCallbackId = "MATLAB:datatypes:callback:CreateCallback";

    /// <summary>
    /// The value a callback property stores for what a script wrote, or null for no callback.
    /// R2025b's own coercions (measured in U1): <c>[]</c>, <c>''</c> and <c>{}</c> clear; a string
    /// scalar is stored as text; a cell holding only a function handle is that handle; any other
    /// cell is kept as a column.
    /// </summary>
    public static JgsValue? Normalize(GraphObject target, string property, JgsValue value, int line, int col)
    {
        if (value.Type == JgsType.Function)
        {
            return value;
        }

        if (JgsBuiltins.IsTextScalar(value))
        {
            string text = JgsBuiltins.TextOf(value);
            return text.Length == 0 ? null : JgsValue.Str(text);
        }

        if (value.Type == JgsType.Array && value.ArrayLength == 0 && !value.IsStringArray)
        {
            return null;
        }

        if (value.Type == JgsType.Cell)
        {
            JgsValue[] parts = value.AsCell;
            if (parts.Length == 0)
            {
                return null;
            }

            if (parts[0].Type == JgsType.Function || JgsBuiltins.IsTextScalar(parts[0]))
            {
                if (parts.Length == 1 && parts[0].Type == JgsType.Function)
                {
                    return parts[0];
                }

                var column = new JgsValue[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    column[i] = parts[i].IsStringArray && parts[i].ArrayLength == 1
                        ? JgsValue.Str(JgsBuiltins.TextOf(parts[i]))
                        : JgsValue.Share(parts[i]);
                }

                JgsValue cell = JgsValue.Cell(column);
                cell.Reshape(column.Length, 1);
                return cell;
            }
        }

        throw new JgsRuntimeException(line, col, CreateCallbackId,
            $"Error setting property '{property}' of class '{ClassWord(target)}':\n"
            + "Callback value must be a character vector, a function handle, or a cell array containing character vector or function handle");
    }

    /// <summary>
    /// Runs a stored callback the way R2025b does: a handle as <c>fcn(src, event)</c>, text in the
    /// base workspace, and <c>{fcn, a, b}</c> as <c>fcn(src, event, a, b)</c> with a name in the
    /// first cell looked up as a function.
    /// </summary>
    public static void Invoke(Interpreter? interpreter, JgsValue callback, JgsValue source, JgsValue eventData)
    {
        if (callback.Type == JgsType.Function)
        {
            JgsCallbacks.Invoke(callback.AsCallable, [source, eventData], 0, 0);
            return;
        }

        if (interpreter is null)
        {
            throw new JgsRuntimeException(0, 0, "A callback written as text or a cell needs a running workspace to run in.");
        }

        if (JgsBuiltins.IsTextScalar(callback))
        {
            interpreter.EvaluateSource(JgsBuiltins.TextOf(callback), interpreter.Globals, 0, 0, asStatement: true);
            return;
        }

        JgsValue[] parts = callback.AsCell;
        JgsValue head = parts[0].Type == JgsType.Function
            ? parts[0]
            : interpreter.EvaluateSource("@" + JgsBuiltins.TextOf(parts[0]), interpreter.Globals, 0, 0);
        var arguments = new JgsValue[parts.Length + 1];
        arguments[0] = source;
        arguments[1] = eventData;
        Array.Copy(parts, 1, arguments, 2, parts.Length - 1);
        JgsCallbacks.Invoke(head.AsCallable, arguments, 0, 0);
    }

    /// <summary>The class name R2025b's property errors give for an object, as in "of class 'UIControl'".</summary>
    public static string ClassWord(GraphObject target) => target switch
    {
        UiControlModel => "UIControl",
        UiButtonGroupModel => "ButtonGroup",
        UiProgressIndicatorModel => "ProgressIndicator",
        UiPanelModel => "Panel",
        UiGridLayoutModel => "GridLayout",
        UiTabGroupModel => "TabGroup",
        UiTabModel => "Tab",
        UiToolbarModel => "Toolbar",
        UiToolModel tool => tool.IsToggle ? "ToggleTool" : "PushTool",
        UiTreeNodeModel => "TreeNode",
        UiHtmlModel => "HTML",
        UiComponentModel component => component.Kind.ToString(),
        UiOverlayModel => "matlab.ui.dialog.ProgressDialog",
        FigureModel => "Figure",
        AxesModel => "Axes",
        ContextMenuModel => "ContextMenu",
        MenuItemModel => "Menu",
        LinePlot => "Line",
        _ => Capitalised(JgsGraphicsProperties.TypeNameOf(target)),
    };

    private static string Capitalised(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}

/// <summary>
/// The event data R2025b hands graphics callbacks, as classed structs: <c>class(evt)</c> names
/// MATLAB's class and <c>isa(evt, 'event.EventData')</c> holds, which is what scripts ask. Measured
/// in U1: headless (<c>u1_eventdata</c>), a <c>DeleteFcn</c> gets <c>event.EventData</c> named
/// <c>ObjectBeingDestroyed</c> and a <c>CloseRequestFcn</c> a <c>WindowCloseRequestData</c> named
/// <c>Close</c>; in a window (<c>u1w_keys</c>), a <c>uicontrol</c>'s <c>Callback</c> gets an
/// <c>ActionData</c> named <c>Action</c>, keys a <c>KeyData</c> (a component's own a
/// <c>UIClientComponentKeyEvent</c>), the window's button events a <c>WindowMouseData</c>, a
/// figure's <c>ButtonDownFcn</c> a <c>MouseData</c> and the wheel a <c>ScrollWheelData</c>. The
/// fields are in R2025b's order, its own ones before <c>Source</c> and <c>EventName</c>.
/// </summary>
internal static class JgsUiEventData
{
    public const string ActionDataClass = "matlab.ui.eventdata.ActionData";
    public const string WindowCloseRequestDataClass = "matlab.ui.eventdata.WindowCloseRequestData";
    public const string KeyDataClass = "matlab.ui.eventdata.KeyData";
    public const string ComponentKeyEventClass = "matlab.ui.eventdata.UIClientComponentKeyEvent";
    public const string WindowMouseDataClass = "matlab.ui.eventdata.WindowMouseData";
    public const string MouseDataClass = "matlab.ui.eventdata.MouseData";
    public const string ScrollWheelDataClass = "matlab.ui.eventdata.ScrollWheelData";
    public const string HitClass = "matlab.graphics.eventdata.Hit";
    public const string SizeChangedDataClass = "matlab.ui.eventdata.SizeChangedData";
    public const string SelectionChangedDataClass = "matlab.ui.eventdata.SelectionChangedData";

    /// <summary>The built-in event data classes made here, each a handle and an <c>event.EventData</c>.</summary>
    private static readonly HashSet<string> Classes = new(StringComparer.Ordinal)
    {
        ActionDataClass,
        WindowCloseRequestDataClass,
        KeyDataClass,
        ComponentKeyEventClass,
        WindowMouseDataClass,
        MouseDataClass,
        ScrollWheelDataClass,
        HitClass,
        SizeChangedDataClass,
        SelectionChangedDataClass,
    };

    /// <summary>
    /// Whether a classed struct is one of the built-in event data classes made here: the ones above,
    /// and every component's <c>matlab.ui.eventdata.*</c> (U5 to U9b), each an <c>event.EventData</c>.
    /// </summary>
    public static bool IsBuiltinEventData(JgsValue value) =>
        value.Type == JgsType.Struct && value.ClassName is { } name
        && (Classes.Contains(name) || name.StartsWith("matlab.ui.eventdata.", StringComparison.Ordinal));

    public static JgsValue Action(JgsValue source) => Make(ActionDataClass, source, "Action");

    public static JgsValue WindowCloseRequest(JgsValue source) => Make(WindowCloseRequestDataClass, source, "Close");

    /// <summary>One event data value: the class's own fields first, then <c>Source</c> and <c>EventName</c>.</summary>
    public static JgsValue Make(
        string className, JgsValue source, string eventName, Dictionary<string, JgsValue>? own = null)
    {
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        foreach ((string name, JgsValue value) in own ?? [])
        {
            fields[name] = value;
        }

        fields["Source"] = source;
        fields["EventName"] = JgsValue.Str(eventName);
        JgsValue data = JgsValue.Struct(fields);
        data.SetClassName(className);
        return data;
    }
}
