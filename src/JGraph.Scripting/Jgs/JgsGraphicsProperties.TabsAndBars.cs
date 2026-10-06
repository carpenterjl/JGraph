using System.Runtime.CompilerServices;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The property surfaces of <c>uitabgroup</c>, <c>uitab</c>, <c>uimenu</c>, <c>uicontextmenu</c>,
/// <c>uitoolbar</c>, <c>uipushtool</c> and <c>uitoggletool</c> (app-building plan, U8). In R2025b
/// each is one class in a classic figure and in a <c>uifigure</c>; names, defaults, words and
/// refusals were recorded headless (probes <c>u8_matrix</c>, <c>u8_behave</c>, <c>u8_more</c>).
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static readonly string[] TabLocationWords = ["top", "left", "bottom", "right"];

    /// <summary><c>Clipping</c> on a menu, a toolbar and a tool: kept, and nothing reads it.</summary>
    private static readonly ConditionalWeakTable<GraphObject, StrongBox<bool>> ClippingOff = [];

    /// <summary>
    /// Whether an object refuses in R2025b's words — "Unrecognized property…", "…is read-only" —
    /// rather than this build's own: every component, and the menus, toolbars and tools of U8.
    /// </summary>
    internal static bool SpeaksAsComponent(GraphObject target) =>
        target is UiObject or MenuItemModel or ContextMenuModel or UiToolbarModel or UiToolModel or UiTreeNodeModel;

    /// <summary>
    /// One line of text as <c>Tag</c>, a tab's <c>Title</c> and a menu's <c>Text</c> take it: a
    /// character row, a character matrix read down its columns, or a string scalar.
    /// </summary>
    private static string OneText(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        if (value.IsCharMatrix)
        {
            return ColumnMajorText(value);
        }

        if (value.IsStringArray && value.ArrayLength != 1)
        {
            throw ComponentError(entry, property, "MATLAB:class:RequireScalar", "Value must be a scalar.", line, col);
        }

        if (!JgsBuiltins.IsTextScalar(value))
        {
            throw ComponentError(entry, property, "MATLAB:class:RequireString",
                "Value must be a character vector or a string scalar.", line, col);
        }

        string text = JgsBuiltins.TextOf(value);
        return JgsBuiltins.IsMissingText(text)
            ? throw ComponentError(entry, property, "MATLAB:string:MissingNotSupported", "<missing> string element not supported.", line, col)
            : text;
    }

    /// <summary>
    /// One of a property's words as the classes of U8 take it: the newer components' refusals
    /// (<see cref="EnumWord"/>), a list of words that may be longer than the one the refusal names,
    /// and the word's place in that list for an answer.
    /// </summary>
    private static int EnumIndex(JgsHandleEntry entry, string property, JgsValue value, string[] words, string[] shown, int line, int col)
    {
        if (ReferenceEquals(words, shown))
        {
            return Array.IndexOf(words, EnumWord(entry, property, value, words, line, col));
        }

        // The older words are still taken; the refusal names only the ones R2025b lists.
        if (value.Type == JgsType.String && Matching(value.AsString, words) is { } older)
        {
            return Array.IndexOf(words, older);
        }

        return Array.IndexOf(words, EnumWord(entry, property, value, shown, line, col));
    }

    /// <summary>
    /// A tooltip as the classes of U8 take one: text, a character matrix, or a cell or a string
    /// array of lines. Numbers are refused one way, and what is not text at all another.
    /// </summary>
    private static UiText ModernTooltip(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        const string prefix = "MATLAB:hg:datatypes:NumericOrStringDataType:";
        JgsRuntimeException Numbers() => ComponentError(entry, "Tooltip", "MATLAB:graphics:datatype:UIStringsDataType:NoNumericValue",
            "UIStrings data type does not support numeric values.", line, col);
        if (value.Type == JgsType.String)
        {
            return UiText.Of(MissingAsEmpty(value.AsString));
        }

        if (value.IsCharMatrix)
        {
            return new UiText(UiTextForm.CharMatrix, value.CharMatrixRows());
        }

        if (value.IsStringArray)
        {
            string[] lines = [.. Enumerable.Range(0, value.ArrayLength).Select(i => MissingAsEmpty(JgsBuiltins.TextOf(value.ElementAt(i))))];
            return lines.Length == 1 ? UiText.Of(lines[0]) : new UiText(UiTextForm.Cell, lines);
        }

        if (value.Type == JgsType.Cell)
        {
            var lines = new List<string>();
            bool numbers = false;
            foreach (JgsValue element in value.AsCell)
            {
                if (JgsBuiltins.IsTextScalar(element))
                {
                    lines.Add(MissingAsEmpty(JgsBuiltins.TextOf(element)));
                }
                else if (element.Type is JgsType.Number or JgsType.Complex
                    || (element.Type == JgsType.Array && !element.IsStringArray && !element.IsCharMatrix && !JgsBuiltins.IsLogicalValue(element)))
                {
                    numbers = true;
                }
                else
                {
                    throw ComponentError(entry, "Tooltip", prefix + "InvalidCellArray",
                        "Cell array can contain only non-empty character vectors, string vectors, or numbers.", line, col);
                }
            }

            return numbers ? throw Numbers() : new UiText(UiTextForm.Cell, lines);
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        throw kind != "logical" && IsNumericKind(kind) && value.Type is JgsType.Number or JgsType.Complex or JgsType.Array
            ? Numbers()
            : ComponentError(entry, "Tooltip", prefix + "ArrayClass",
                "Value must be a character vector, categorical array, string array, numeric array, or cell array of character vectors.", line, col);
    }

    /// <summary>A rectangle as the classes of U8 take one: four numbers in a row or a column, never logicals.</summary>
    private static Rect2D ModernRect(JgsHandleEntry entry, string property, JgsValue value, int line, int col)
    {
        if (JgsBuiltins.ClassOf(value, JgsDialect.Matlab) == "logical")
        {
            throw ComponentError(entry, property, "MATLAB:hg:dt_conv:Matrix_to_matlabRect:ExpectedNumeric", "Value must be numeric and finite", line, col);
        }

        if (value.Type == JgsType.Complex || (value.Type == JgsType.Array && !value.IsStringArray && !value.IsCharMatrix && value.Rows > 1 && value.Cols > 1))
        {
            throw ComponentError(entry, property, "MATLAB:hg:dt_conv:Matrix_to_matlabRect:Expected4ElementVector", "Value must be a 4 element vector", line, col);
        }

        return ComponentPosition(entry, property, value, line, col);
    }

    /// <summary>
    /// The names every class of U8 shares with the newer components, with their refusals: what a
    /// table, a tab group and a tab answer in place of a classic control's.
    /// </summary>
    private static void AddModernCommon(IDictionary<string, GraphicsProperty> table, bool visible = true)
    {
        Put(table, "Tag",
            entry => JgsValue.Str(entry.Target.Tag ?? string.Empty),
            (entry, value, line, col) => entry.Target.Tag = OneText(entry, "Tag", value, line, col));
        if (visible)
        {
            Put(table, "Visible",
                entry => OnOff(entry.Target.Visible),
                (entry, value, line, col) => entry.Target.Visible = OnOffState(entry, "Visible", value, line, col));
        }

        Put(table, "HandleVisibility",
            entry => JgsValue.Str(entry.HandleVisibility),
            (entry, value, line, col) =>
                entry.HandleVisibility = EnumWord(entry, "HandleVisibility", value, HandleVisibilityWords, line, col));
        Put(table, "BusyAction",
            entry => JgsValue.Str(entry.BusyActionQueues ? "queue" : "cancel"),
            (entry, value, line, col) =>
                entry.BusyActionQueues = EnumWord(entry, "BusyAction", value, BusyActionWords, line, col) == "queue");
        Put(table, "Interruptible",
            entry => OnOff(entry.Interruptible),
            (entry, value, line, col) => entry.Interruptible = OnOffState(entry, "Interruptible", value, line, col));
        foreach (string name in new[] { "Tooltip", "TooltipString" })
        {
            if (table.ContainsKey(name))
            {
                bool listed = table[name].Listed;
                Put(table, name,
                    entry => TextValue(((UiObject)entry.Target).Tooltip),
                    (entry, value, line, col) => ((UiObject)entry.Target).Tooltip = ModernTooltip(entry, value, line, col));
                if (!listed)
                {
                    Unlist(table, name);
                }
            }
        }

        if (table.ContainsKey("Units"))
        {
            Put(table, "Units",
                entry => UnitsValue(((UiObject)entry.Target).Units),
                (entry, value, line, col) => ((UiObject)entry.Target).ChangeUnits(
                    (UiUnits)EnumIndex(entry, "Units", value, UnitWords, UnitWords, line, col)));
        }
    }

    /// <summary>
    /// Lets every callback property of a table take what the newer components' take beside the
    /// three usual forms: a string array, kept as a column cell of text, and a character matrix,
    /// kept as its text read down the columns.
    /// </summary>
    private static void WidenCallbacks(IDictionary<string, GraphicsProperty> table)
    {
        foreach (string name in table.Keys.Where(static key =>
                     key.EndsWith("Fcn", StringComparison.Ordinal) || key.EndsWith("Callback", StringComparison.Ordinal)).ToList())
        {
            GraphicsProperty slot = table[name];
            if (slot.Write is null)
            {
                continue;
            }

            table[name] = new GraphicsProperty(slot.Name, slot.Read, (entry, value, line, col) =>
            {
                if (value.IsStringArray && value.ArrayLength > 1)
                {
                    JgsValue cell = JgsValue.Cell([.. Enumerable.Range(0, value.ArrayLength).Select(i => JgsValue.Str(JgsBuiltins.TextOf(value.ElementAt(i))))]);
                    cell.Reshape(value.ArrayLength, 1);
                    value = cell;
                }
                else if (value.IsCharMatrix)
                {
                    value = JgsValue.Str(ColumnMajorText(value));
                }

                slot.Write(entry, value, line, col);
            })
            {
                Listed = slot.Listed,
                Words = slot.Words,
            };
        }
    }

    /// <summary>A place as a table and a tab group take one: R2025b's rectangle of U8, a grid's child left where the grid put it.</summary>
    private static void AddModernRects(IDictionary<string, GraphicsProperty> table, params string[] names)
    {
        foreach (string name in names)
        {
            string captured = name;
            GraphicsProperty read = table[captured];
            Put(table, captured, read.Read, (entry, value, line, col) =>
            {
                Rect2D box = ModernRect(entry, captured, value, line, col);
                if (((UiObject)entry.Target).Parent is UiGridLayoutModel)
                {
                    PropertyWarning("MATLAB:ui:components:noPositionSetWhenInLayoutContainer",
                        "Unable to set 'Position', 'InnerPosition', or 'OuterPosition' for components in 'GridLayout'.");
                    return;
                }

                ((UiObject)entry.Target).Position = box;
            });
        }
    }

    // --- uitabgroup ------------------------------------------------------------------------------

    private static UiTabGroupModel TabGroup(JgsHandleEntry entry) => (UiTabGroupModel)entry.Target;

    private static UiTabModel Tab(JgsHandleEntry entry) => (UiTabModel)entry.Target;

    private static void AddUiTabGroupBlock(IDictionary<string, GraphicsProperty> table)
    {
        table.Remove("Enable");
        table.Remove("TooltipString");
        AddModernCommon(table);
        AddModernRects(table, "Position", "OuterPosition");

        Put(table, "TabLocation",
            entry => JgsValue.Str(TabLocationWords[(int)TabGroup(entry).TabLocation]),
            (entry, value, line, col) => TabGroup(entry).TabLocation =
                (UiTabLocation)EnumIndex(entry, "TabLocation", value, TabLocationWords, TabLocationWords, line, col));
        Options(table, "TabLocation", TabLocationWords);

        Put(table, "SelectedTab",
            entry => TabGroup(entry).SelectedTab is { } tab ? JgsHandleRegistry.For(tab) : JgsMatrix.FromColumnMajor([], 0, 0),
            (entry, value, line, col) =>
            {
                JgsRuntimeException Invalid() => new(line, col, "MATLAB:UITabGroup", "Invalid value for SelectedTab.");

                // Nothing, for a group that has nothing to show, is what it already has.
                if (IsEmptyValue(value))
                {
                    if (TabGroup(entry).SelectedTab is null)
                    {
                        return;
                    }

                    throw Invalid();
                }

                // A string, a table and a struct are refused as a wrong tab is; text, a logical and a
                // number that names nothing, as what is no handle at all.
                JgsValue one = value.Type == JgsType.Array && !value.IsStringArray && value.ArrayLength == 1 ? value.ElementAt(0) : value;
                if (value.IsStringArray || value.Type is JgsType.Table or JgsType.Struct)
                {
                    throw Invalid();
                }

                if (one.Type != JgsType.Number || !JgsHandleRegistry.TryGetOrRoot(one, out JgsHandleEntry? named))
                {
                    throw one.Type == JgsType.Number && JgsHandleRegistry.WasMinted(one.AsNumber)
                        ? Invalid()
                        : ComponentError(entry, "SelectedTab", "MATLAB:datatypes:handleoremptydatatype:InvalidHGHandle",
                            "The value set for this property must be a valid HG handle.", line, col);
                }

                if (named.Target is not UiTabModel tab || !ReferenceEquals(tab.Parent, entry.Target))
                {
                    throw Invalid();
                }

                TabGroup(entry).Select(tab);
            });

        AddCallbackSlot(table, "SelectionChangedFcn",
            static entry => entry.SelectionChangedFcn, static (entry, value) => entry.SelectionChangedFcn = value);
        AddResizeSlot(table, "SizeChangedFcn");

        Put(table, "AutoResizeChildren",
            entry => OnOff(TabGroup(entry).AutoResizeChildren),
            (entry, value, line, col) =>
            {
                bool on = OnOffState(entry, "AutoResizeChildren", value, line, col);
                TabGroup(entry).AutoResizeChildren = on;
                WarnIfResizeCallbackIsSilenced(entry, on);
            });

        // What the strip of headings and the border leave, in the group's own units.
        Put(table, "InnerPosition", entry =>
        {
            UiTabGroupModel group = TabGroup(entry);
            return RectRow(UiUnitConverter.FromPixels(group.InnerPixelRect(), group.Units, group.ReferenceSize()));
        });

        Put(table, "Layout",
            LayoutValue,
            (entry, value, line, col) =>
            {
                if (GridOf(entry.Target) is null)
                {
                    throw ComponentError(entry, "Layout", "MATLAB:ui:datatypes:LayoutOptionsDatatype:InvalidClass",
                        "'Layout' value must be specified as a matlab.ui.layout.LayoutOptions object.", line, col);
                }

                SetLayout(entry, value, line, col);
            });
        WidenCallbacks(table);
    }

    private static void AddUiTabBlock(IDictionary<string, GraphicsProperty> table)
    {
        // A tab is neither shown nor enabled on its own account: its group shows it.
        table.Remove("Enable");
        table.Remove("Visible");
        Unlist(table, "TooltipString");
        AddModernCommon(table, visible: false);

        Put(table, "Title",
            entry => JgsValue.Str(Tab(entry).Title),
            (entry, value, line, col) => Tab(entry).Title = OneText(entry, "Title", value, line, col));
        Put(table, "BackgroundColor",
            entry => UiColorValue(Tab(entry).BackgroundColor),
            (entry, value, line, col) => Tab(entry).BackgroundColor = ClearableColor(entry, "BackgroundColor", value, line, col));
        Put(table, "ForegroundColor",
            entry => UiColorValue(Tab(entry).ForegroundColor),
            (entry, value, line, col) => Tab(entry).ForegroundColor = ClearableColor(entry, "ForegroundColor", value, line, col));

        // A tab is where its group puts it: its rectangle is read, in its own units, and never written.
        foreach (string name in new[] { "Position", "InnerPosition", "OuterPosition" })
        {
            Put(table, name, entry =>
            {
                UiTabModel tab = Tab(entry);
                return RectRow(UiUnitConverter.FromPixels(tab.PixelPosition(), tab.Units, tab.ReferenceSize()));
            });
        }

        Put(table, "Units",
            entry => UnitsValue(Tab(entry).Units),
            (entry, value, line, col) => Tab(entry).Units = (UiUnits)EnumIndex(entry, "Units", value, UnitWords, UnitWords, line, col));

        Put(table, "Scrollable",
            entry => OnOff(Tab(entry).Scrollable),
            (entry, value, line, col) => Tab(entry).Scrollable = OnOffState(entry, "Scrollable", value, line, col));
        Put(table, "AutoResizeChildren",
            entry => OnOff(Tab(entry).AutoResizeChildren),
            (entry, value, line, col) =>
            {
                bool on = OnOffState(entry, "AutoResizeChildren", value, line, col);
                Tab(entry).AutoResizeChildren = on;
                WarnIfResizeCallbackIsSilenced(entry, on);
            });
        AddResizeSlot(table, "SizeChangedFcn");
        WidenCallbacks(table);
    }

    // --- menus, toolbars and tools ---------------------------------------------------------------

    /// <summary>
    /// What a menu, a context menu, a toolbar and a tool share: none of the model's own names, the
    /// interaction words unlisted, and R2025b's coercions for the names they do answer to.
    /// </summary>
    private static void AddBarCommon(Type type, IDictionary<string, GraphicsProperty> table)
    {
        table.Remove("Name");
        table.Remove("ZOrder");
        table.Remove("Selectable");
        table.Remove("PickableParts");
        Unlist(table, "Selected", "SelectionHighlight", "HitTest", "UIContextMenu");

        Put(table, "Tag",
            entry => JgsValue.Str(entry.Target.Tag ?? string.Empty),
            (entry, value, line, col) => entry.Target.Tag = OneText(entry, "Tag", value, line, col));
        Put(table, "Visible",
            entry => OnOff(entry.Target.Visible),
            (entry, value, line, col) => entry.Target.Visible = OnOffState(entry, "Visible", value, line, col));
        Put(table, "HandleVisibility",
            entry => JgsValue.Str(entry.HandleVisibility),
            (entry, value, line, col) =>
                entry.HandleVisibility = EnumWord(entry, "HandleVisibility", value, HandleVisibilityWords, line, col));
        Put(table, "BusyAction",
            entry => JgsValue.Str(entry.BusyActionQueues ? "queue" : "cancel"),
            (entry, value, line, col) =>
                entry.BusyActionQueues = EnumWord(entry, "BusyAction", value, BusyActionWords, line, col) == "queue");
        Put(table, "Interruptible",
            entry => OnOff(entry.Interruptible),
            (entry, value, line, col) => entry.Interruptible = OnOffState(entry, "Interruptible", value, line, col));
        Put(table, "Clipping",
            entry => OnOff(!ClippingOff.TryGetValue(entry.Target, out StrongBox<bool>? off) || !off.Value),
            (entry, value, line, col) => ClippingOff.GetOrCreateValue(entry.Target).Value = !OnOffState(entry, "Clipping", value, line, col));

        // The right-click menu, with a component's refusals. A menu since deleted reads as none.
        foreach (string name in new[] { "ContextMenu", "UIContextMenu" })
        {
            string captured = name;
            table[captured] = new GraphicsProperty(captured,
                entry => entry.ContextMenu is { BeingDeleted: false } menu && JgsHandleRegistry.TryGetEntry(menu, out _)
                    ? JgsHandleRegistry.For(menu)
                    : JgsMatrix.FromColumnMajor([], 0, 0),
                (entry, value, line, col) =>
                {
                    if ((value.Type == JgsType.Array && value.ArrayLength == 0)
                        || (JgsBuiltins.IsTextScalar(value) && JgsBuiltins.TextOf(value).Length == 0))
                    {
                        entry.ContextMenu = null;
                        return;
                    }

                    if (!JgsHandleRegistry.TryGet(value, out JgsHandleEntry? menu))
                    {
                        throw ComponentError(entry, captured, "MATLAB:datatypes:handleoremptydatatype:InvalidHGHandle",
                            "The value set for this property must be a valid HG handle.", line, col);
                    }

                    entry.ContextMenu = menu.Target is ContextMenuModel
                        ? menu.Target
                        : throw new JgsRuntimeException(line, col, "MATLAB:hgutils:InvalidContextMenu", "Handle must be a uicontextmenu.");
                })
            {
                Listed = name == "ContextMenu",
            };
        }

        Options(table, "Visible", OnOffWords);
        Options(table, "HandleVisibility", HandleVisibilityWords);
        Options(table, "BusyAction", "queue", "cancel");
        Options(table, "Interruptible", OnOffWords);
        Options(table, "Clipping", OnOffWords);
    }

    private static MenuItemModel MenuItem(JgsHandleEntry entry) => (MenuItemModel)entry.Target;

    /// <summary>The list a menu entry stands in: its figure's bar, a context menu's, or another entry's.</summary>
    internal static GraphObjectCollection<MenuItemModel>? SiblingsOf(MenuItemModel item) => item.Parent switch
    {
        FigureModel figure => figure.Menus,
        ContextMenuModel menu => menu.Items,
        MenuItemModel above => above.Items,
        _ => null,
    };

    /// <summary>
    /// What each entry of a menu's list shows as its <c>Position</c>: its place, counted from 1,
    /// or the number last written to it when that was no place. A written number of 1 or more
    /// takes a place from the count; a smaller one takes none.
    /// </summary>
    private static Dictionary<MenuItemModel, double> MenuPlaces(MenuItemModel item)
    {
        var places = new Dictionary<MenuItemModel, double>(ReferenceEqualityComparer.Instance);
        int place = 1;
        foreach (MenuItemModel sibling in (IEnumerable<MenuItemModel>?)SiblingsOf(item) ?? [item])
        {
            double? written = JgsHandleRegistry.TryGetEntry(sibling, out JgsHandleEntry? entry) ? entry.MenuPosition : null;
            if (written is { } kept)
            {
                places[sibling] = kept;
                if (kept >= 1)
                {
                    place++;
                }
            }
            else
            {
                places[sibling] = place++;
            }
        }

        return places;
    }

    private static void AddMenuBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddBarCommon(typeof(MenuItemModel), table);
        foreach (string name in new[] { "Text", "Label" })
        {
            string captured = name;
            Put(table, captured,
                entry => JgsValue.Str(MenuItem(entry).Text),
                (entry, value, line, col) => MenuItem(entry).Text = OneText(entry, captured, value, line, col));
        }

        foreach (string name in new[] { "MenuSelectedFcn", "Callback" })
        {
            AddCallbackSlot(table, name, static entry => entry.MenuSelectedFcn, static (entry, value) => entry.MenuSelectedFcn = value);
        }

        Unlist(table, "Label", "Callback");

        Put(table, "Checked",
            entry => OnOff(MenuItem(entry).Checked),
            (entry, value, line, col) => MenuItem(entry).Checked = OnOffState(entry, "Checked", value, line, col));
        Put(table, "Enable",
            entry => OnOff(MenuItem(entry).Enable),
            (entry, value, line, col) => MenuItem(entry).Enable = OnOffState(entry, "Enable", value, line, col));
        Put(table, "Separator",
            entry => OnOff(MenuItem(entry).Separator),
            (entry, value, line, col) => MenuItem(entry).Separator = OnOffState(entry, "Separator", value, line, col));
        Put(table, "Accelerator",
            entry => JgsValue.Str(MenuItem(entry).Accelerator),
            (entry, value, line, col) =>
            {
                string key = JgsBuiltins.IsTextScalar(value) ? MissingAsEmpty(JgsBuiltins.TextOf(value)) : "??";
                MenuItem(entry).Accelerator = key.Length <= 1
                    ? key
                    : throw ComponentError(entry, "Accelerator", "MATLAB:gbtdatatypes:InputMustBeASingleCharacter",
                        "Input can either be an empty Char array or a Char array with a single character.", line, col);
            });
        Put(table, "Tooltip",
            entry => entry.MenuTooltip is { } kept ? TextValue(kept) : JgsValue.Str(MenuItem(entry).Tooltip),
            (entry, value, line, col) =>
            {
                UiText tip = ModernTooltip(entry, value, line, col);
                entry.MenuTooltip = tip;
                MenuItem(entry).Tooltip = tip.Joined;
            });
        Put(table, "ForegroundColor",
            entry =>
            {
                JGraph.Core.Drawing.Color ink = MenuItem(entry).ForegroundColor;
                return entry.MenuInkIsNone ? JgsValue.Str("none")
                    : entry.MenuInk is { } kept ? Row(kept.R, kept.G, kept.B) : Row(ink.R / 255.0, ink.G / 255.0, ink.B / 255.0);
            },
            (entry, value, line, col) =>
            {
                // 'none' is kept and read back; the entry is then drawn in the colour it would have had.
                UiColor? ink = ClearableColor(entry, "ForegroundColor", value, line, col);
                entry.MenuInkIsNone = ink is null;
                entry.MenuInk = ink;
                MenuItem(entry).ForegroundColor = (ink ?? UiComponentModel.DefaultFontColor).ToColor();
            });

        // Where the entry stands among its siblings, from 1. Writing a number moves the entry to
        // where that number falls among the others' places. A number that is one of the places
        // becomes the place; any other - a fraction, zero, one past the end - is kept and read
        // back as it was written, the others counting round it (R2025b, fixture u8_bars).
        Put(table, "Position",
            entry => JgsValue.Number(MenuPlaces(MenuItem(entry))[MenuItem(entry)]),
            (entry, value, line, col) =>
            {
                string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
                bool numeric = kind == "logical" || (IsNumericKind(kind) && value.Type is JgsType.Number or JgsType.Array or JgsType.Complex);
                bool scalar = value.Type is JgsType.Number or JgsType.Bool or JgsType.Complex or JgsType.Function
                    || (value.Type == JgsType.Array && value.ArrayLength == 1) || (value.Type == JgsType.String && value.AsString.Length == 1);

                // A string, a table and a struct are refused for what they are; anything else that
                // is not one value, for that, before it is asked whether it is a number.
                if (value.IsStringArray || value.Type is JgsType.Table or JgsType.Struct)
                {
                    throw ComponentError(entry, "Position", "MATLAB:class:RequireNumeric", "Value must be numeric or logical.", line, col);
                }

                if (!scalar)
                {
                    throw ComponentError(entry, "Position", "MATLAB:class:RequireScalar", "Value must be a scalar.", line, col);
                }

                if (!numeric)
                {
                    throw ComponentError(entry, "Position", "MATLAB:class:RequireNumeric", "Value must be numeric or logical.", line, col);
                }

                double wanted = JgsBuiltins.ToDoubles("Position", value, line, col)[0];
                if (wanted < 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hg:Position", "Position cannot be negative.");
                }

                MenuItemModel item = MenuItem(entry);
                if (SiblingsOf(item) is not { } siblings)
                {
                    entry.MenuPosition = wanted;
                    return;
                }

                // The others keep the places they show; the entry goes where its number falls among
                // them, before one that shows the same.
                Dictionary<MenuItemModel, double> places = MenuPlaces(item);
                bool isPlace = wanted == System.Math.Floor(wanted) && wanted >= 1 && wanted <= siblings.Count;
                entry.MenuPosition = isPlace ? null : wanted;
                double key = double.IsNaN(wanted) ? double.PositiveInfinity : wanted;
                List<MenuItemModel> order = [.. siblings.Where(other => !ReferenceEquals(other, item))];
                int at = order.FindIndex(other => (double.IsNaN(places[other]) ? double.PositiveInfinity : places[other]) >= key);
                order.Insert(at < 0 ? order.Count : at, item);
                if (!order.SequenceEqual(siblings))
                {
                    Restack(siblings, order);
                }
            });

        Options(table, "Checked", OnOffWords);
        Options(table, "Enable", OnOffWords);
        Options(table, "Separator", OnOffWords);
        WidenCallbacks(table);
    }

    private static void AddContextMenuBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddBarCommon(typeof(ContextMenuModel), table);
        Unlist(table, "Visible");

        // R2025b's context menu answers to a Position it does not list: where it last opened.
        Put(table, "Position",
            entry => entry.MenuPlace is { } place ? Row(place.X, place.Y) : Row(0, 0),
            (entry, value, line, col) =>
            {
                double[] place = NumericVector(entry, "Position", value, 2, line, col);
                entry.MenuPlace = (place[0], place[1]);
            });
        Unlist(table, "Position");
        AddCallbackSlot(table, "ContextMenuOpeningFcn",
            static entry => entry.ContextMenuOpeningFcn, static (entry, value) => entry.ContextMenuOpeningFcn = value);
        WidenCallbacks(table);
    }

    private static void AddUiToolbarBlock(IDictionary<string, GraphicsProperty> table)
    {
        AddBarCommon(typeof(UiToolbarModel), table);
        Put(table, "BackgroundColor",
            entry => UiColorValue(((UiToolbarModel)entry.Target).BackgroundColor),
            (entry, value, line, col) => ((UiToolbarModel)entry.Target).BackgroundColor =
                ClearableColor(entry, "BackgroundColor", value, line, col)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:hg:ColorSpec_None", "Cannot set Toolbar BackgroundColor to 'none'."));
        WidenCallbacks(table);
    }

    private static UiToolModel Tool(JgsHandleEntry entry) => (UiToolModel)entry.Target;

    private static void AddUiToolBlock(Type type, IDictionary<string, GraphicsProperty> table)
    {
        AddBarCommon(type, table);
        Put(table, "Children", static _ => JgsMatrix.FromColumnMajor([], 0, 0), static (_, _, _, _) => { });
        Put(table, "Enable",
            entry => OnOff(Tool(entry).Enable),
            (entry, value, line, col) => Tool(entry).Enable = OnOffState(entry, "Enable", value, line, col));
        Put(table, "Separator",
            entry => OnOff(Tool(entry).Separator),
            (entry, value, line, col) => Tool(entry).Separator = OnOffState(entry, "Separator", value, line, col));
        foreach (string name in new[] { "Tooltip", "TooltipString" })
        {
            Put(table, name,
                entry => TextValue(Tool(entry).Tooltip),
                (entry, value, line, col) => Tool(entry).Tooltip = ModernTooltip(entry, value, line, col));
        }

        Unlist(table, "TooltipString");
        AddNamedSlot(table, "ClickedCallback");

        // The picture: an Icon, when there is one, and otherwise the CData. Both are kept as given.
        Put(table, "CData",
            entry => entry.UiCData ?? JgsMatrix.FromColumnMajor([], 0, 0),
            (entry, value, line, col) =>
            {
                UiImage? picture = CDataPicture(entry, value, line, col, tool: true);
                if (entry.ToolIcon is null)
                {
                    Tool(entry).Picture = picture;
                }
                else
                {
                    PropertyWarning("MATLAB:hg:toolbar:IconCDataBothSet",
                        "When both the Icon and CData properties are set, the CData property is ignored. To display an icon in the tool, use the Icon property.");
                }
            });
        Put(table, "Icon",
            entry => entry.ToolIcon ?? JgsValue.Str(string.Empty),
            (entry, value, line, col) => SetToolIcon(entry, value, line, col));

        Options(table, "Enable", OnOffWords);
        Options(table, "Separator", OnOffWords);
        if (!typeof(UiToggleToolModel).IsAssignableFrom(type))
        {
            return;
        }

        // A toggle tool's three: its State, and what runs as it goes down and as it comes up.
        Put(table, "State",
            entry => OnOff(Tool(entry).State),
            (entry, value, line, col) =>
            {
                UiToolModel tool = Tool(entry);
                bool on = OnOffState(entry, "State", value, line, col);
                if (on != tool.State)
                {
                    // R2025b runs OnCallback and OffCallback for a State a script writes too, and
                    // never the ClickedCallback (probe u8_behave).
                    tool.State = on;
                    JgsBuiltins.ToolStateWritten(tool);
                }
            });
        AddNamedSlot(table, "OnCallback");
        AddNamedSlot(table, "OffCallback");
        Options(table, "State", OnOffWords);
        WidenCallbacks(table);
    }

    /// <summary>
    /// A tool's <c>Icon</c>: a file's name, kept whether or not there is such a file (R2025b only
    /// warns), or an m-by-n-by-3 array.
    /// </summary>
    private static void SetToolIcon(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        UiToolModel tool = Tool(entry);
        if (IsEmptyValue(value) || JgsBuiltins.IsTextScalar(value) || value.IsCharMatrix)
        {
            string text = IsEmptyValue(value) ? string.Empty
                : value.IsCharMatrix ? ColumnMajorText(value)
                : MissingAsEmpty(JgsBuiltins.TextOf(value));
            entry.ToolIcon = text.Length == 0 ? null : JgsValue.Str(text);
            UiImage? read = null;
            if (text.Length > 0)
            {
                string path = JgsCallbackDispatcher.Current?.Interpreter?.Host is { } host ? host.Resolve(text) : text;
                read = File.Exists(path) ? JgsBuiltins.TryReadPicture(path) : null;
                if (read is null && Path.GetExtension(text).Length > 0)
                {
                    PropertyWarning("MATLAB:hg:gbtdatatypes:Icon:fileNotFound", $"File '{text}' is not on the MATLAB or specified path.");
                }
            }

            tool.Picture = read ?? (text.Length == 0 && entry.UiCData is not null ? tool.Picture : read);
            return;
        }

        string kind = JgsBuiltins.ClassOf(value, JgsDialect.Matlab);
        int[] dims = value.Type == JgsType.Array ? value.Dims : [1, 1];
        if (kind == "logical" || !IsNumericKind(kind) || dims.Length != 3 || dims[2] != 3)
        {
            throw ComponentError(entry, "Icon", "MATLAB:hg:gbtdatatypes:Icon:invalidIcon",
                "Icon value must be a valid file path or an m-by-n-by-3 color data matrix.", line, col);
        }

        JgsValue? cdata = entry.UiCData;
        UiImage? picture = CDataPicture(entry, value, line, col, tool: true);
        entry.ToolIcon = entry.UiCData;
        entry.UiCData = cdata;
        tool.Picture = picture;
    }
}
