using System.ComponentModel;
using System.Windows;
using JGraph.Controls.Internal;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Core.Undo;
using JGraph.Interaction.Editing;

namespace JGraph.Controls.Inspector;

/// <summary>
/// One row of the property inspector: wraps an <see cref="EditableProperty"/> for a specific target
/// object and exposes editor-shaped bindable state (text, checked, enum selection, color swatch).
/// Committed edits are written through the descriptor and recorded on the undo stack as a
/// <see cref="PropertyChangeAction"/>; external changes to the target (including undo/redo) refresh
/// the row via the target's property-change notification.
/// </summary>
public sealed class PropertyRowViewModel : ViewModelBase, IDisposable
{
    /// <summary>The palette offered in color dropdowns: theme series colors plus common inks.</summary>
    public static IReadOnlyList<string> Palette { get; } = BuildPalette();

    private readonly GraphObject _target;
    private readonly EditableProperty _property;
    private readonly UndoStack? _undoStack;
    private bool _updating;
    private bool _isExpanded;

    public PropertyRowViewModel(GraphObject target, EditableProperty property, UndoStack? undoStack)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _undoStack = undoStack;
        _target.PropertyChanged += OnTargetPropertyChanged;
    }

    public string DisplayName => _property.DisplayName;

    public string Category => _property.Category;

    public PropertyEditorKind Kind => _property.Editor;

    /// <summary>Null for a top-level row; the owning composite's display name for a child row.</summary>
    public string? Group => _property.Group;

    /// <summary>True for the caption row of a composite property, which expands and collapses its children.</summary>
    public bool IsHeader => _property.IsHeader;

    /// <summary>Child rows sit one level in from their header.</summary>
    public Thickness Indent => new(_property.Group is null ? 0 : 16, 0, 0, 0);

    /// <summary>The expander triangle is drawn only on header rows.</summary>
    public Visibility ExpanderVisibility => IsHeader ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Header captions are emphasized so a composite reads as a sub-section.</summary>
    public FontWeight NameWeight => IsHeader ? FontWeights.SemiBold : FontWeights.Normal;

    /// <summary>The values offered by the enum editor.</summary>
    public Array EnumValues => Enum.GetValues(_property.ValueType);

    /// <summary>The installed font families offered by the font editor, sorted by name.</summary>
    public static IReadOnlyList<string> FontFamilies { get; } = BuildFontFamilies();

    /// <summary>
    /// Whether a header row's children are shown. Meaningless on other rows. The inspector watches
    /// this to re-filter its view; composites start collapsed so the list stays scannable.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(ExpanderGlyph));
        }
    }

    /// <summary>The expander triangle's glyph for the current state.</summary>
    public string ExpanderGlyph => _isExpanded ? "▾" : "▸";

    /// <summary>Text-editor value. Setting parses and commits; invalid input reverts the display.</summary>
    public string Text
    {
        get => EditablePropertyFactory.Format(_property, _property.GetValue(_target));
        set
        {
            if (_updating)
            {
                return;
            }

            if (EditablePropertyFactory.TryParse(_property, value, out object? parsed))
            {
                Commit(parsed);
            }

            // Re-display the (possibly unchanged/reverted) model value.
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(SwatchHex));
        }
    }

    /// <summary>Toggle-editor value.</summary>
    public bool IsChecked
    {
        get => _property.GetValue(_target) is true;
        set
        {
            if (!_updating)
            {
                Commit(value);
            }
        }
    }

    /// <summary>Enum-editor selection.</summary>
    public object? SelectedEnumValue
    {
        get => _property.GetValue(_target);
        set
        {
            if (!_updating && value is not null)
            {
                Commit(value);
            }
        }
    }

    /// <summary>Whether an optional color is in its automatic (null) state.</summary>
    public bool IsAuto
    {
        get => Kind == PropertyEditorKind.OptionalColor && _property.GetValue(_target) is null;
        set
        {
            if (_updating || Kind != PropertyEditorKind.OptionalColor)
            {
                return;
            }

            if (value)
            {
                Commit(null);
            }
            else if (_property.GetValue(_target) is null)
            {
                Commit((Color?)Colors.Black);
            }

            Refresh();
        }
    }

    /// <summary>The current color as "#RRGGBB" for the swatch, or null when automatic/not a color.</summary>
    public string? SwatchHex =>
        _property.GetValue(_target) is Color color ? EditablePropertyFactory.FormatColor(color) : null;

    /// <summary>Palette-dropdown selection; setting commits the picked color and resets the selection.</summary>
    public string? PaletteSelection
    {
        get => null;
        set
        {
            if (!_updating && value is not null && Color.TryParse(value, out Color color))
            {
                Commit(Kind == PropertyEditorKind.OptionalColor ? (Color?)color : color);
                Refresh();
            }

            OnPropertyChanged(nameof(PaletteSelection));
        }
    }

    /// <summary>Whether the color sub-editor is enabled (always, except an optional color set to auto).</summary>
    public bool IsColorEditorEnabled => !IsAuto;

    /// <inheritdoc />
    public void Dispose() => _target.PropertyChanged -= OnTargetPropertyChanged;

    private void Commit(object? newValue)
    {
        object? oldValue = _property.GetValue(_target);
        if (Equals(oldValue, newValue))
        {
            return;
        }

        // Undo is recorded against the root property, not the edited member: for a child row of a
        // composite that means the whole struct, so one Undo restores every member together.
        object? oldRoot = _property.GetRootValue(_target);

        _updating = true;
        try
        {
            _property.SetValue(_target, newValue);
        }
        finally
        {
            _updating = false;
        }

        _undoStack?.Push(new PropertyChangeAction(_target, _property.Name, oldRoot, _property.GetRootValue(_target)));
    }

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_updating && (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == _property.Name))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(SelectedEnumValue));
        OnPropertyChanged(nameof(IsAuto));
        OnPropertyChanged(nameof(SwatchHex));
        OnPropertyChanged(nameof(IsColorEditorEnabled));
    }

    /// <summary>
    /// The font families installed on this machine. The editor is a combo box but stays editable, so
    /// a family absent here can still be typed for a figure destined for another machine.
    /// </summary>
    private static IReadOnlyList<string> BuildFontFamilies()
    {
        var names = new List<string>();
        foreach (System.Windows.Media.FontFamily family in System.Windows.Media.Fonts.SystemFontFamilies)
        {
            names.Add(family.Source);
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return names;
    }

    private static IReadOnlyList<string> BuildPalette()
    {
        var palette = new List<string>();
        foreach (Color color in Core.Drawing.Colors.DefaultSeriesOrder)
        {
            palette.Add(EditablePropertyFactory.FormatColor(color));
        }

        foreach (Color color in new[]
        {
            Core.Drawing.Colors.Black,
            Core.Drawing.Colors.DimGray,
            Core.Drawing.Colors.Gray,
            Core.Drawing.Colors.LightGray,
            Core.Drawing.Colors.White,
            Core.Drawing.Colors.Red,
            Core.Drawing.Colors.Green,
            Core.Drawing.Colors.Blue,
            Core.Drawing.Colors.Orange,
        })
        {
            string hex = EditablePropertyFactory.FormatColor(color);
            if (!palette.Contains(hex))
            {
                palette.Add(hex);
            }
        }

        return palette;
    }
}
