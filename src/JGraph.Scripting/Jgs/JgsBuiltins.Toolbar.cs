using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M80: <c>axtoolbar</c> and <c>axtoolbarbtn</c>, the two verbs that come off the plot-tools
/// exclusion list.
/// <para>
/// The other seven stay off it for the reason recorded in M43: JGraph's dockable plot browser and
/// property inspector <em>are</em> those tools, and a verb that opened one would open something
/// already open. These two are different — they describe a strip of buttons over one axes, which is
/// a thing this build can have and now does.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    private static readonly string[] ToolbarOptionNames =
        ["Visible", "SelectionChangedFcn", "Tag", "UserData"];

    private static readonly string[] ToolbarButtonOptionNames =
    [
        "Icon", "Tooltip", "Value", "ButtonPushedFcn", "ValueChangedFcn", "Tag", "UserData",
    ];

    private static void RegisterToolbarBuiltins(JgsEnvironment env)
    {
        // Asked for two outputs it hands back the buttons as well, which is the documented form a
        // script uses to reach them without walking Children.
        env.Declare("axtoolbar", JgsValue.Function(new BuiltinFunction("axtoolbar",
            (args, line, col) => AxToolbar(args, line, col))
        {
            AutoCallsBare = true,
            MultiOutput = (args, wanted, line, col) =>
            {
                JgsValue handle = AxToolbar(args, line, col);
                if (wanted < 2)
                {
                    return [handle];
                }

                var toolbar = (AxesToolbarModel)JgsHandleRegistry.Require(handle, line, col).Target;
                return [handle, JgsGraphicsProperties.HandleList([.. toolbar.Buttons])];
            },
        }));

        env.Declare("axtoolbarbtn", JgsValue.Function(new BuiltinFunction("axtoolbarbtn",
            (args, line, col) => AxToolbarButton(args, line, col))));
    }

    /// <summary>
    /// <c>axtoolbar</c>, <c>axtoolbar(ax)</c>, <c>axtoolbar(buttons)</c>,
    /// <c>axtoolbar(ax, buttons)</c> and the option tail. A bare name answers the current axes'
    /// toolbar, which is the reading that makes <c>tb = axtoolbar</c> work.
    /// </summary>
    private static JgsValue AxToolbar(IReadOnlyList<JgsValue> args, int line, int col)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        AxesModel axes = named ?? JG.Gca();
        AxesToolbarModel toolbar = axes.Toolbar;

        int next = 0;
        if (rest.Count > 0 && IsButtonList(rest[0]))
        {
            string[] wanted = ToolbarButtons(rest[0], line, col);
            if (wanted is ["default"])
            {
                toolbar.Restore();
            }
            else
            {
                toolbar.Replace(wanted);
            }

            next = 1;
        }

        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(toolbar);
        foreach ((string name, JgsValue value) in Pairs("axtoolbar", rest, next, line, col))
        {
            JgsGraphicsProperties.Set(entry, name, value, line, col);
        }

        return JgsHandleRegistry.For(toolbar);
    }

    /// <summary>
    /// Whether an argument is the button list rather than the start of the option tail. A list is a
    /// cell, or the one word <c>'default'</c>: every other bare word there is an option name.
    /// </summary>
    private static bool IsButtonList(JgsValue value) =>
        value.Type == JgsType.Cell
        || (value.Type == JgsType.String
            && !ToolbarOptionNames.Contains(value.AsString, StringComparer.OrdinalIgnoreCase));

    /// <summary>The named buttons, each checked against the ones this build draws.</summary>
    private static string[] ToolbarButtons(JgsValue value, int line, int col)
    {
        // 'default' arrives as a bare word rather than in a cell, which is how MATLAB documents it.
        string[] words = value.Type == JgsType.String
            ? [value.AsString]
            : JgsRulerTicks.LabelWords("axtoolbar: buttons", value, line, col);
        foreach (string word in words)
        {
            if (word.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!AxesToolbarModel.KnownButtons.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col,
                    $"axtoolbar has no '{word}' button. It knows "
                    + $"{string.Join(", ", AxesToolbarModel.KnownButtons)}.");
            }
        }

        return [.. words.Select(static word => word.ToLowerInvariant())];
    }

    /// <summary>
    /// <c>axtoolbarbtn(tb)</c>, <c>axtoolbarbtn(tb, style)</c> and the option tail. A button made
    /// this way goes on the left, which is where MATLAB puts one.
    /// </summary>
    private static JgsValue AxToolbarButton(IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("axtoolbarbtn", args, 1, int.MaxValue, line, col);
        if (!JgsHandleRegistry.TryGet(args[0], out JgsHandleEntry? owner)
            || owner.Target is not AxesToolbarModel toolbar)
        {
            throw new JgsRuntimeException(line, col,
                "axtoolbarbtn expects the toolbar to add the button to: axtoolbarbtn(tb).");
        }

        int next = 1;
        var style = ToolbarButtonStyle.Push;
        if (args.Count > 1 && args[1].Type == JgsType.String
            && !ToolbarButtonOptionNames.Contains(args[1].AsString, StringComparer.OrdinalIgnoreCase))
        {
            string word = Str("axtoolbarbtn", args, 1, line, col).ToLowerInvariant();
            style = word switch
            {
                "push" => ToolbarButtonStyle.Push,
                "state" => ToolbarButtonStyle.State,
                _ => throw new JgsRuntimeException(line, col,
                    $"axtoolbarbtn: the style is 'push' or 'state', but got '{word}'."),
            };
            next = 2;
        }

        AxesToolbarButtonModel button = toolbar.Add(new AxesToolbarButtonModel(string.Empty)
        {
            Style = style,
        });

        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(button);
        foreach ((string name, JgsValue value) in Pairs("axtoolbarbtn", args, next, line, col))
        {
            JgsGraphicsProperties.Set(entry, name, value, line, col);
        }

        return JgsHandleRegistry.For(button);
    }
}
