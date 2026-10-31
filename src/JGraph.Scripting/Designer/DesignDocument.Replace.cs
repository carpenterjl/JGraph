using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <content>
/// Replace With on App Designer's canvas (<c>CompReplacementProvider</c>, <c>ReplaceComponentCommand</c>):
/// one component, or a labelled component, made into another class of its family as one edit.
/// </content>
public sealed partial class DesignDocument
{
    /// <summary>
    /// What the selection can be replaced with, in the menu's order (<c>inclusionCriteria</c>,
    /// <c>getMenuItemProperties</c>): empty unless one component with a family is selected. A labelled
    /// component and its field count as the field; its label does not count at all.
    /// </summary>
    public IReadOnlyList<(string Type, string Name)> Replacements =>
        ReplaceSource() is { } source ? DesignReplacement.Offered(source.Type) : [];

    private DesignComponent? ReplaceSource()
    {
        DesignComponent? source = _picks switch
        {
            [{ Group: { IsLabeled: true, Field: { } field } }] => field,
            [{ Component: { Parent: not null } component }] when LabelledBy(component) is null => component,
            _ => null,
        };
        return source is not null && CanDelete(source) ? source : null;
    }

    /// <summary>
    /// Replaces the selected component with a new <paramref name="type"/> (<c>ReplaceComponentCommand</c>), as
    /// one edit: where it stood among its siblings and in its group, holding what the two classes share and
    /// what a new one holds of the rest, with what was inside it. The replacement is placed and named as
    /// <see cref="DesignReplacement"/> says, the design's references and the user's code follow its name, and
    /// so does its label's. A callback stays bound where the new class has the property, and takes the new
    /// default name if it had the old one; one that cannot stay is dropped if it holds only its default
    /// code, and otherwise stays in the code, bound to nothing. The replacement is selected.
    /// </summary>
    /// <returns>The replacement; null when the selection cannot be replaced with that class.</returns>
    public DesignComponent? ReplaceWith(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (ReplaceSource() is not { } source || !Replacements.Any(r => r.Type == type))
        {
            return null;
        }

        EndGesture(commit: false);
        DesignComponent parent = source.Parent!;
        DesignGroup? group = Design.GroupOf(source);
        DesignGroup? labelled = Design.LabeledGroupOf(source);
        DesignComponent? label = labelled?.Label;

        DesignComponent target = Palette.MakeNamed(type, source.CodeName);
        Carry(source, target);
        PlaceReplacement(source, target, labelled is null ? DesignLabelPlace.None : Palette.LabelPlaceOf(source.Type));

        string? said = label is not null ? TextOf(label) : TextOf(source);
        (string codeName, string? labelCodeName, string? labelText) = DesignReplacement.NamesOf(
            source.Type, type, label is not null, source.CodeName, label?.CodeName, said, TextOf(target), NamesInUse(), Palette.TextsIn(Design));
        target.CodeName = codeName;

        List<Edit> step = [new DeleteEdit(source, parent, IndexIn(parent, source)) { Selects = false }];
        if (source.Children.Count > 0)
        {
            step.Add(new MoveChildrenEdit(target, source) { Selects = false });
        }

        step.Add(new AddEdit(target, parent, IndexIn(parent, source)) { Selects = false });
        if (group is not null)
        {
            int at = group.Members.ToList().FindIndex(m => ReferenceEquals(m.Component, source));
            step.Add(new SwapMemberEdit(target, group, source, at) { Selects = false });
        }

        if (codeName != source.CodeName)
        {
            step.Add(new ReferencesEdit(target, source.CodeName, codeName) { Selects = false });
        }

        if (label is not null)
        {
            step.AddRange(LabelEdits(label, source, target, labelCodeName!, labelText));
        }

        step.AddRange(ReplacementCallbackEdits(source, target));
        IReadOnlyList<DesignPick> after = labelled is not null ? [DesignPick.Of(labelled)] : [DesignPick.Of(target)];
        step.Add(new SelectionEdit(target, _picks.ToArray(), after));
        Do([.. step]);
        return target;
    }

    // What the new one holds of the old one's values (_ComponentFamilyReplacer.replaceComponent, the family's
    // conversions): what the two classes share, and a new one's values of the rest.
    private void Carry(DesignComponent source, DesignComponent target)
    {
        Dictionary<string, DesignValue> fresh = target.Properties.ToDictionary(static p => p.Key, static p => p.Value, StringComparer.Ordinal);
        foreach (DesignProperty property in source.Class?.Properties ?? [])
        {
            string name = property.Name;
            if (name is "Position" or "OuterPosition" or "InnerPosition" || target.Class?.Property(name) is not { } theirs)
            {
                continue;
            }

            DesignValue? held = source.Get(name);
            if (held is null && property.HasMode)
            {
                continue; // its mode is automatic, and stays so
            }

            if ((held ?? property.DefaultFor(Theme)) is not { } value)
            {
                continue;
            }

            if (held is not null && property.HasMode)
            {
                target.Set(name, value);
            }
            else if (DesignCodeWriter.IsDefault(target, theirs, value, Theme))
            {
                target.Unset(name);
            }
            else
            {
                target.Set(name, value);
            }
        }

        bool oneRow = target.Type is "matlab.ui.control.Slider" or "matlab.ui.control.RangeSlider";
        IReadOnlyList<string> callbacks = DesignCallbacks.R2025b.PropertiesOf(target.Type);
        foreach ((string name, DesignValue value) in source.Properties)
        {
            switch (value)
            {
                case CallbackValue when callbacks.Contains(DesignCallbacks.PropertyName(name), StringComparer.Ordinal):
                    target.Set(name, value);
                    break;
                case NumericValue { Data.Count: > 1 } span when name == "Layout.Row" && oneRow:
                    target.Set(name, NumericValue.Scalar(span.Data[0])); // _convertLayoutConstraints
                    break;
                case not CallbackValue when name.StartsWith("Layout.", StringComparison.Ordinal) || (name == "ContextMenu" && TakesContextMenu(target)):
                    target.Set(name, value);
                    break;
            }
        }

        switch (DesignReplacement.FamilyOf(target.Type))
        {
            case "NUMERIC":
                CarryNumbers(source, target);
                break;
            case "TEXTUAL_INPUT":
                CarryText(source, target);
                break;
            case "TEXTUAL_DISPLAY":
                UseNewIfDefault("FontColor");
                UseNewIfDefault("Text");
                break;
            case "GAUGE":
                target.Unset("Orientation");
                break;
        }

        // _useDestinationIfSourceUsesDefault.
        void UseNewIfDefault(string name)
        {
            if (source.Get(name) is { } held && source.Class?.Property(name) is { } ours && !DesignCodeWriter.IsDefault(source, ours, held, Theme))
            {
                return;
            }

            if (fresh.TryGetValue(name, out DesignValue? made))
            {
                target.Set(name, made);
            }
            else
            {
                target.Unset(name);
            }
        }
    }

    // The numeric family's _convertLimits and _convertValue.
    private void CarryNumbers(DesignComponent source, DesignComponent target)
    {
        double[] value = source.Type == "matlab.ui.control.EditField" ? [0] : Numbers(source, "Value");
        (double Low, double High) targetLimits = Pair(Numbers(target, "Limits", defaults: true));
        double[] targetValue = Numbers(target, "Value", defaults: true);
        (double Low, double High) limits = targetLimits;
        if (source.Class?.Property("Limits") is not null)
        {
            limits = DesignReplacement.LimitsOf(Pair(Numbers(source, "Limits")), value[0], targetLimits, targetValue[0]);
            SetOrDefault(target, "Limits", NumericValue.Row(limits.Low, limits.High));
        }

        double[] carried = DesignReplacement.NumericValueOf(source.Type, target.Type, value, targetLimits, targetValue, limits.High);
        SetOrDefault(target, "Value", carried.Length == 1 ? NumericValue.Scalar(carried[0]) : NumericValue.Row(carried));

        double[] Numbers(DesignComponent c, string name, bool defaults = false) =>
            (defaults ? c.Class?.Property(name)?.DefaultFor(Theme) : LikeOf(c, name)) is NumericValue { Data.Count: > 0 } n ? [.. n.Data] : [0];

        static (double Low, double High) Pair(double[] n) => n.Length >= 2 ? (n[0], n[1]) : (double.NegativeInfinity, double.PositiveInfinity);
    }

    // The textual input family's _convertValue.
    private void CarryText(DesignComponent source, DesignComponent target)
    {
        IReadOnlyList<string> lines = LikeOf(source, "Value") switch
        {
            CellValue cell => cell.Items.Select(static i => DesignPalette.Text(i) ?? string.Empty).ToArray(),
            { } one when DesignPalette.Text(one) is { } text => [text],
            _ => [string.Empty],
        };
        IReadOnlyList<string> carried = DesignReplacement.TextValueOf(source.Type, target.Type, lines);
        if (carried is [""])
        {
            target.Unset("Value");
            return;
        }

        SetOrDefault(target, "Value", carried is [var only]
            ? new CharValue(only)
            : CellValue.Column(carried.Select(static l => (DesignValue)new CharValue(l)).ToArray()));
    }

    private void SetOrDefault(DesignComponent target, string name, DesignValue value)
    {
        if (IsDefaultValue(target, name, value))
        {
            target.Unset(name);
        }
        else
        {
            target.Set(name, value);
        }
    }

    // Where the new one goes and its size: the family's conversions, then setComponentToPreferredSize for
    // the classes sized by their text, kept to the side their text is aligned to. A grid's child stays in
    // its cells, and a tool has no place of its own.
    private void PlaceReplacement(DesignComponent source, DesignComponent target, DesignLabelPlace sourceLabel)
    {
        if (IsGridChild(source) || OuterPositionOf(source) is not { } outer || Runtime.PositionOf(source) is not { } inner)
        {
            return;
        }

        DesignClassBox box = Palette.BoxOf(target.Type);
        Size2D size = DesignReplacement.SizeOf(source.Type, target.Type, inner.Size, box);
        Point2D at = DesignReplacement.LocationOf(target.Type, sourceLabel, outer, size, box);
        double x = at.X;
        if (LikeOf(target, "Text") is { } shown && DesignPalette.Text(shown) is { } text)
        {
            Size2D? fit = target.Type switch
            {
                "matlab.ui.control.Label" or "matlab.ui.control.Hyperlink" => DesignPalette.LabelSize(text),
                "matlab.ui.control.Button" or "matlab.ui.control.StateButton" => ButtonFit(text),
                _ => null,
            };
            if (fit is { } w)
            {
                x -= Slide(target, w.Width - size.Width);
                size = w;
            }
        }

        target.Set("Position", NumericValue.Row(x, at.Y, size.Width, size.Height));

        static Size2D ButtonFit(string text)
        {
            (double width, double height) = DesignStructure.ButtonPreferredSize(toggle: true, text);
            return new Size2D(width, height);
        }
    }

    // How far a widening moves a component's left edge (_getLocationBasedOnNewSize).
    private double Slide(DesignComponent component, double wider) =>
        (LikeOf(component, "HorizontalAlignment") is { } a ? DesignPalette.Text(a) : "left") switch
        {
            "right" => wider,
            "center" => 0.5 * wider,
            _ => 0,
        };

    // A labelled component's label after its field is replaced (AddLabelToComponentCommand, then
    // setComponentToPreferredSize): where a new label of the new class goes beside it, sized to what it
    // says, with its text and its name; all else about it stays.
    private List<Edit> LabelEdits(DesignComponent label, DesignComponent source, DesignComponent target, string labelCodeName, string? labelText)
    {
        var edits = new List<Edit>();
        if (labelText is not null)
        {
            var text = new CharValue(labelText);
            edits.Add(new PropertyEdit(label, "Text", label.Get("Text"), text, LikeOf(label, "Text"), text) { Selects = false });
        }

        DesignLabelPlace place = Palette.LabelPlaceOf(target.Type);
        string said = labelText ?? TextOf(label) ?? string.Empty;
        if (place != DesignLabelPlace.None)
        {
            if (IsGridChild(source))
            {
                edits.AddRange(LabelCellEdits(label, source.Parent!, source, place));
            }
            else if (target.Get("Position") is NumericValue { Data: [var x, var y, var w, var h] })
            {
                var inner = new Rect2D(x, y, w, h);
                DesignClassBox box = Palette.BoxOf(target.Type);
                var outer = new Rect2D(x - box.Left, y - box.Bottom, w + (box.Outer.Width - box.Size.Width), h + (box.Outer.Height - box.Size.Height));
                (_, _, string madeText) = DesignNames.ForNewLabeled(target.Type, NamesInUse(), Palette.TextsIn(Design));
                Size2D made = DesignPalette.LabelSize(madeText);
                Size2D size = DesignPalette.LabelSize(said);
                (double left, double bottom) = LabelCorner(place, target.Type, outer, inner, made);
                left -= Slide(label, size.Width - made.Width);
                edits.Add(new PositionEdit(label, label.Get("Position"), NumericValue.Row(left, bottom, size.Width, size.Height), LabelPositionNow(label)) { Selects = false });
            }
        }

        if (labelCodeName != label.CodeName)
        {
            edits.Add(new RenameEdit(label, label.CodeName, labelCodeName) { Selects = false });
        }

        return edits;
    }

    private NumericValue LabelPositionNow(DesignComponent label) =>
        Runtime.PositionOf(label) is { } p ? NumericValue.Row(p.X, p.Y, p.Width, p.Height) : (label.Get("Position") as NumericValue ?? NumericValue.Row(1, 1, 31, 22));

    // A grid child's label beside its cells: left of them or under them; left where it is when the grid has
    // no such cell (AddLabelToComponentCommand keeps the old label's cell then).
    private List<Edit> LabelCellEdits(DesignComponent label, DesignComponent grid, DesignComponent source, DesignLabelPlace place)
    {
        var edits = new List<Edit>();
        if (CellsOf(grid) is not { } cells || SpanOf(source) is not { } span)
        {
            return edits;
        }

        bool west = place != DesignLabelPlace.SouthCenter;
        if (west ? span.ColumnStart == 1 : span.RowEnd == cells.Rows)
        {
            return edits;
        }

        DesignValue row = west ? span.RowValue : NumericValue.Scalar(span.RowEnd + 1);
        DesignValue column = west ? NumericValue.Scalar(span.ColumnStart - 1) : span.ColumnValue;
        edits.Add(new PropertyEdit(label, "Layout.Row", label.Get("Layout.Row"), row, label.Get("Layout.Row"), row) { Selects = false });
        edits.Add(new PropertyEdit(label, "Layout.Column", label.Get("Layout.Column"), column, label.Get("Layout.Column"), column) { Selects = false });
        return edits;
    }

    // What becomes of the callbacks bound to the replaced component (DeleteCommand's bindings, CodeController's
    // binding of an added component, CompReplacementUtil.updateCallbackDefaultNames).
    private List<Edit> ReplacementCallbackEdits(DesignComponent source, DesignComponent target)
    {
        var edits = new List<Edit>();
        if (Code is null)
        {
            return edits;
        }

        var inUse = NamesInUse().ToList();
        inUse.Add(target.CodeName);
        for (int i = Code.Callbacks.Count - 1; i >= 0; i--)
        {
            DesignCallback callback = Code.Callbacks[i];
            if (callback.IsGenerated || callback.IsStartup)
            {
                continue;
            }

            IReadOnlyList<DesignCallbackBinding> bindings = DesignCallbacks.BindingsOf(Design, callback.Name);
            DesignCallbackBinding[] own = bindings.Where(b => ReferenceEquals(b.Component, source)).ToArray();
            if (own.Length == 0)
            {
                continue;
            }

            DesignCallbackBinding[] kept = own.Where(b => target.Get(DesignCallbacks.DesignPropertyName(target, b.Property)) is CallbackValue).ToArray();
            if (kept.Length == 0)
            {
                // Nothing on the new one holds it: it goes with the old one if nothing else does and it has no code of its own.
                if (bindings.Count == own.Length && callback.IsDefaultOrEmpty)
                {
                    edits.Add(new CallbackListEdit(Design.Figure, callback, i, Add: false));
                }

                continue;
            }

            if (bindings.Count == own.Length)
            {
                IReadOnlyList<string> defaults = DesignCallbacks.R2025b.DefaultBody(target, kept[0].Property);
                IReadOnlyList<string>? body = callback.IsDefaultOrEmpty ? defaults.Select(l => Style.Indent(3) + l).ToArray() : null;
                edits.Add(new CallbackCodeEdit(Design.Figure, callback, defaults, body));
            }

            string property = bindings[0].Property;
            bool sameProperty = bindings.All(b => b.Property == property);
            if (callback.Name == callback.GeneratedName && sameProperty)
            {
                string renamed = DesignCallbacks.Unique(DesignCallbacks.DefaultName(target, property), inUse.Where(n => n != callback.Name));
                if (renamed != callback.Name)
                {
                    inUse.Add(renamed);
                    edits.Add(new RenameCallbackEdit(Design.Figure, callback, callback.Name, renamed, Generated: true));
                }
            }
        }

        return edits;
    }

    /// <summary>What is inside one component moved to another that is not yet made, as the runtime makes them together.</summary>
    private sealed record MoveChildrenEdit(DesignComponent Component, DesignComponent From) : Edit(Component)
    {
        public override void Do(DesignDocument document) => Move(From, Component);

        public override void Undo(DesignDocument document) => Move(Component, From);

        private static void Move(DesignComponent from, DesignComponent to)
        {
            foreach (DesignComponent child in from.Children.ToArray())
            {
                from.Remove(child);
                to.Add(child);
            }
        }
    }

    /// <summary>A component in another's place in its group; undone, the other back in its place.</summary>
    private sealed record SwapMemberEdit(DesignComponent Component, DesignGroup Group, DesignComponent Other, int Index) : Edit(Component)
    {
        public override void Do(DesignDocument document) => Swap(document, Other, Component);

        public override void Undo(DesignDocument document) => Swap(document, Component, Other);

        private void Swap(DesignDocument document, DesignComponent from, DesignComponent to)
        {
            document.Design.RemoveFromGroup(Group, [DesignGroupMember.Of(from)]);
            document.Design.InsertIntoGroup(Group, Index, DesignGroupMember.Of(to));
        }
    }

    /// <summary>The design's references and the user's code following a replaced component's name to its replacement's (<c>renameComponentFromReplacement</c>).</summary>
    private sealed record ReferencesEdit(DesignComponent Component, string From, string To) : Edit(Component)
    {
        public override void Do(DesignDocument document) => document.RenameReferences(From, To);

        public override void Undo(DesignDocument document) => document.RenameReferences(To, From);
    }

    /// <summary>A callback's default code for the component it is now bound to, and its body when that was still the default.</summary>
    private sealed record CallbackCodeEdit(DesignComponent Component, DesignCallback Callback, IReadOnlyList<string> Default, IReadOnlyList<string>? Body) : CodeEdit(Component)
    {
        private IReadOnlyList<string> _default = [];
        private string[] _body = [];

        public override void Do(DesignDocument document)
        {
            _default = Callback.DefaultBody;
            _body = [.. Callback.Body];
            Callback.DefaultBody = Default;
            if (Body is not null)
            {
                Callback.Body.Clear();
                Callback.Body.AddRange(Body);
            }
        }

        public override void Undo(DesignDocument document)
        {
            Callback.DefaultBody = _default;
            Callback.Body.Clear();
            Callback.Body.AddRange(_body);
        }
    }
}
