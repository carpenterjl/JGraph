using System.Globalization;
using System.Text.Json;

namespace JGraph.Scripting.Toolstrip;

/// <summary>
/// Reads a toolstrip from the configuration tree R2025b's toolstrip framework takes: nodes with a
/// <c>type</c> (<c>TabGroup</c>, <c>Tab</c>, <c>Section</c>, <c>Column</c>, <c>PushButton</c>...), a
/// <c>tag</c> and <c>children</c>, and the <c>PopupList</c>s their buttons open - the shape App
/// Designer's tab modules return from <c>_generateTabLayoutConfig</c> and
/// <c>_generateStaticPopupsConfig</c>, which stage W0 recorded in <c>w0_toolstrip.json</c>.
/// </summary>
/// <remarks>
/// The reader takes the words it is given. An item that names only an action carries no words in
/// the raw tree; W0's resolved tree has them filled from the action, as the framework fills them.
/// </remarks>
public static class ToolstripConfigReader
{
    /// <summary>
    /// Reads one document type of a W0 recording (<c>documentTypes[].config</c>, by default App
    /// Designer's app) and the quick access bar into a layout, from the resolved trees.
    /// </summary>
    /// <param name="recording">The recording's root.</param>
    /// <param name="config">The document type's configuration name.</param>
    /// <param name="shareCommonSections">
    /// Whether a section a tab names in its common options (the RUN section) is added at the end of
    /// each tab that names it and does not hold it already, as the framework shares it.
    /// </param>
    public static ToolstripLayout ReadRecording(JsonElement recording, string config = "AppDocumentTypeConfig", bool shareCommonSections = true)
    {
        JsonElement documentType = recording.GetProperty("documentTypes").EnumerateArray()
            .First(d => d.GetProperty("config").GetString() == config);
        var tabs = new List<ToolstripTab>();
        var popups = new Dictionary<string, ToolstripPopup>(StringComparer.Ordinal);
        var common = new List<IReadOnlyList<string>>();
        foreach (JsonElement tab in documentType.GetProperty("tabs").EnumerateArray())
        {
            JsonElement resolved = tab.GetProperty("resolved");
            bool contextual = tab.TryGetProperty("isContextual", out JsonElement c) && c.ValueKind == JsonValueKind.True;
            List<ToolstripTab> read = ReadTabGroup(resolved.GetProperty("layout"), contextual);
            tabs.AddRange(read);
            List<string> options = tab.TryGetProperty("commonOptions", out JsonElement o) && o.ValueKind == JsonValueKind.Array
                ? [.. o.EnumerateArray().Select(e => e.GetString() ?? "")]
                : [];
            common.AddRange(read.Select(_ => (IReadOnlyList<string>)options));
            if (resolved.TryGetProperty("popups", out JsonElement p))
            {
                foreach (ToolstripPopup popup in ReadPopups(p))
                {
                    popups.TryAdd(popup.Tag, popup);
                }
            }
        }

        if (shareCommonSections)
        {
            Dictionary<string, ToolstripSection> sections = tabs.SelectMany(t => t.Sections)
                .Where(s => s.Tag is not null)
                .GroupBy(s => s.Tag!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            for (int i = 0; i < tabs.Count; i++)
            {
                List<ToolstripSection> own = [.. tabs[i].Sections];
                foreach (string tag in common[i])
                {
                    if (sections.TryGetValue(tag, out ToolstripSection? shared) && !own.Any(s => s.Tag == tag))
                    {
                        own.Add(shared);
                    }
                }

                tabs[i] = tabs[i] with { Sections = own };
            }
        }

        IReadOnlyList<ToolstripItem> quickAccess = recording.TryGetProperty("quickAccessBar", out JsonElement q) ? ReadQuickAccess(q) : [];
        return new ToolstripLayout(tabs, popups, quickAccess);
    }

    /// <summary>Reads a <c>TabGroup</c> node (or a single <c>Tab</c>) into its tabs.</summary>
    /// <param name="node">The node.</param>
    /// <param name="contextual">Whether the group's tabs are contextual.</param>
    public static List<ToolstripTab> ReadTabGroup(JsonElement node, bool contextual)
    {
        string type = Str(node, "type") ?? "";
        if (type == "Tab")
        {
            return [ReadTab(node, null, contextual)];
        }

        string? group = Str(node, "tag");
        return [.. Children(node).Where(n => Str(n, "type") == "Tab").Select(n => ReadTab(n, group, contextual))];
    }

    /// <summary>Reads popup lists: an array of <c>PopupList</c> nodes, one, or an object holding them as <c>popups</c> (a tab module's static popups); null reads as none.</summary>
    public static IEnumerable<ToolstripPopup> ReadPopups(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement n in node.EnumerateArray())
            {
                foreach (ToolstripPopup popup in ReadPopups(n))
                {
                    yield return popup;
                }
            }
        }
        else if (node.ValueKind == JsonValueKind.Object && Str(node, "type") is null && node.TryGetProperty("popups", out JsonElement inner))
        {
            foreach (ToolstripPopup popup in ReadPopups(inner))
            {
                yield return popup;
            }
        }
        else if (node.ValueKind == JsonValueKind.Object && Str(node, "type") == "PopupList" && Str(node, "tag") is { } tag)
        {
            yield return new ToolstripPopup(tag, [.. Children(node).Select(ReadEntry).OfType<ToolstripPopupEntry>()]);
        }
    }

    /// <summary>Reads the quick access bar: its buttons, in order (the group nodes are skipped).</summary>
    public static IReadOnlyList<ToolstripItem> ReadQuickAccess(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<ToolstripItem>();
        foreach (JsonElement n in node.EnumerateArray())
        {
            string type = Str(n, "type") ?? "";
            if (type.StartsWith("QAB", StringComparison.Ordinal) && ReadItem(n, type["QAB".Length..]) is { } item)
            {
                items.Add(item with { Icon = item.QuickAccessIcon ?? item.Icon });
            }
        }

        return items;
    }

    private static ToolstripTab ReadTab(JsonElement node, string? group, bool contextual) =>
        new(Str(node, "tag") ?? "", Str(node, "title") ?? "", group, contextual,
            [.. Children(node).Where(n => Str(n, "type") == "Section").Select(ReadSection)], Mnemonic(node));

    private static ToolstripSection ReadSection(JsonElement node) =>
        new(Str(node, "tag"), Str(node, "title"), [.. Children(node).Where(n => Str(n, "type") == "Column").Select(ReadColumn)],
            Str(node, "icon") ?? "section_24",
            node.TryGetProperty("collapsePriority", out JsonElement p) && p.TryGetInt32(out int priority) ? priority : 0);

    private static ToolstripColumn ReadColumn(JsonElement node) =>
        new(Str(node, "tag"), Num(node, "width") ?? 0, Str(node, "horizontalAlignment"),
            [.. Children(node).Select(n => ReadItem(n, Str(n, "type") ?? "")).OfType<ToolstripItem>()]);

    private static ToolstripItem? ReadItem(JsonElement node, string type)
    {
        if (!Enum.TryParse(type, out ToolstripItemKind kind))
        {
            return null;
        }

        var item = new ToolstripItem
        {
            Kind = kind,
            Tag = Str(node, "tag"),
            Text = Str(node, "textOverride") ?? Str(node, "text"),
            Description = Str(node, "descriptionOverride") ?? Str(node, "description"),
            Icon = Str(node, "iconOverride") ?? Str(node, "icon"),
            QuickAccessIcon = Str(node, "quickAccessIcon"),
            Shortcut = Str(node, "shortcut") is { Length: > 0 } s ? s : null,
            ActionId = Str(node, "actionId"),
            PopupTag = Str(node, "popupTag"),
            HasDynamicPopup = Bool(node, "hasDynamicPopup"),
            ButtonGroup = Str(node, "buttonGroupName"),
            Mnemonic = Mnemonic(node),
            Selected = Bool(node, "selected"),
            Enabled = node.TryGetProperty("enabled", out JsonElement e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : null,
            Editable = Bool(node, "editable"),
            Items = node.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array
                ? [.. items.EnumerateArray().Select(ItemText)]
                : [],
            Value = Str(node, "selectedItem") ?? ValueText(node),
            ErrorText = Str(node, "errorText"),
            Minimum = Num(node, "minimum"),
            Maximum = Num(node, "maximum"),
            Step = Num(node, "stepSize") ?? 1,
        };
        if (kind == ToolstripItemKind.Panel)
        {
            item = item with
            {
                Children = [.. Children(node).Select(n => Str(n, "type") == "Column"
                    ? new ToolstripPanelChild(ReadColumn(n), null)
                    : new ToolstripPanelChild(null, ReadItem(n, Str(n, "type") ?? "")))
                    .Where(c => c.Column is not null || c.Item is not null)],
            };
        }

        return item;
    }

    private static ToolstripPopupEntry? ReadEntry(JsonElement node)
    {
        ToolstripPopupEntryKind? kind = Str(node, "type") switch
        {
            "ListItem" => ToolstripPopupEntryKind.Item,
            "ListItemWithCheckBox" => ToolstripPopupEntryKind.CheckItem,
            "PopupListHeader" => ToolstripPopupEntryKind.Header,
            "PopupListSeparator" => ToolstripPopupEntryKind.Separator,
            "PopupListPanel" => ToolstripPopupEntryKind.Panel,
            _ => null,
        };
        return kind is null ? null : new ToolstripPopupEntry
        {
            Kind = kind.Value,
            Tag = Str(node, "tag"),
            Text = kind == ToolstripPopupEntryKind.Header ? Str(node, "title") : Str(node, "textOverride") ?? Str(node, "text"),
            Description = Str(node, "descriptionOverride") ?? Str(node, "description"),
            Icon = Str(node, "iconOverride") ?? Str(node, "icon"),
            Shortcut = Str(node, "shortcut") is { Length: > 0 } s ? s : null,
            ActionId = Str(node, "actionId"),
            ShowDescription = Bool(node, "showDescription"),
            Selected = Bool(node, "selected"),
            ClosePopupOnClick = Bool(node, "closePopupOnClick"),
            MaxHeight = Num(node, "maxHeight") ?? 0,
        };
    }

    private static IEnumerable<JsonElement> Children(JsonElement node) =>
        node.TryGetProperty("children", out JsonElement c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray() : [];

    private static string? Str(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    private static double? Num(JsonElement node, string name) =>
        node.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string? Mnemonic(JsonElement node) => Str(node, "mnemonic") is { Length: > 0 } m ? m : null;

    private static string? ValueText(JsonElement node) =>
        node.TryGetProperty("value", out JsonElement v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetDouble().ToString(CultureInfo.InvariantCulture),
            _ => null,
        } : null;

    // A combo box's items are strings, or {label, value} pairs.
    private static string ItemText(JsonElement item) => item.ValueKind switch
    {
        JsonValueKind.String => item.GetString() ?? "",
        JsonValueKind.Object => Str(item, "label") ?? Str(item, "text") ?? Str(item, "value") ?? "",
        _ => item.ToString(),
    };
}
