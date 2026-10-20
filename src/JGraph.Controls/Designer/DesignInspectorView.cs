using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// The visual designer's inspector (designer plan DS3): the selected component's code name, and its
/// properties in App Designer's groups (<see cref="DesignInspector"/>), each edited as App Designer's
/// inspector edits it - text, on/off, a choice, a colour, a context menu, or the value as MATLAB
/// writes it. An edit goes to the document (<see cref="DesignDocument.SetProperty"/>), which sets it
/// through the component and keeps what the component kept, and the rows show what it holds. What
/// the generator never writes is shown and not edited.
/// </summary>
public sealed class DesignInspectorView : UserControl
{
    private readonly StackPanel _panel = new() { Margin = new Thickness(4) };
    private readonly List<Row> _rows = [];
    private DesignDocument? _document;
    private DesignComponent? _shown;
    private bool _shownInGrid;
    private bool _refreshing;
    private bool _queued;
    private TextBox? _name;
    private string _nameShown = string.Empty;

    public DesignInspectorView()
    {
        var scroll = new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        Content = scroll;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "Inspector");
    }

    /// <summary>Raised with what an edit was refused for, for the window's status line.</summary>
    public event EventHandler<string>? Problem;

    /// <summary>The app whose selection is inspected.</summary>
    public DesignDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(value, _document))
            {
                return;
            }

            if (_document is not null)
            {
                _document.Changed -= OnChanged;
            }

            _document = value;
            if (value is not null)
            {
                value.Changed += OnChanged;
            }

            Rebuild();
        }
    }

    /// <summary>Changes come in runs (a drag's every step); the rows are brought up to date once the run is over.</summary>
    private void OnChanged(object? sender, EventArgs e)
    {
        if (_queued)
        {
            return;
        }

        _queued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _queued = false;
            // The same component, unless it went into or out of a grid, which changes what is edited.
            if (_document?.Selection is [var one] && ReferenceEquals(one, _shown) && one.IsInGrid == _shownInGrid)
            {
                RefreshValues();
            }
            else
            {
                Rebuild();
            }
        });
    }

    // --- building ----------------------------------------------------------------------------------

    private void Rebuild()
    {
        _panel.Children.Clear();
        _rows.Clear();
        _name = null;
        _shown = null;
        if (_document is not { } document)
        {
            return;
        }

        if (document.Selection is not [var component])
        {
            _panel.Children.Add(new TextBlock { Text = $"{document.Selection.Count} components selected.", Margin = new Thickness(2) });
            return;
        }

        _shown = component;
        _shownInGrid = component.IsInGrid;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        _name = new TextBox { Text = component.CodeName, FontWeight = FontWeights.SemiBold, ToolTip = "The component's code name: app." + component.CodeName };
        _nameShown = component.CodeName;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_name, "Inspector.CodeName");
        System.Windows.Automation.AutomationProperties.SetName(_name, "Code name");
        Commits(_name, () => _nameShown, text =>
        {
            if (_document is { } d && _shown is { } c && d.Rename(c, text.Trim()) is { } why)
            {
                Problem?.Invoke(this, why);
                _name!.Text = _nameShown = c.CodeName;
            }
        });
        var type = new TextBlock { Text = component.Type, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0) };
        DockPanel.SetDock(type, Dock.Bottom);
        header.Children.Add(type);
        header.Children.Add(_name);
        _panel.Children.Add(header);

        foreach (DesignInspectorGroup group in _document?.InspectorGroupsOf(component) ?? DesignInspector.R2025b.GroupsOf(component.Type))
        {
            var grid = new Grid { Margin = new Thickness(2, 2, 2, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto), MinWidth = 90 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var expander = new Expander { Header = group.Title, IsExpanded = group.Expanded, Content = grid };
            System.Windows.Automation.AutomationProperties.SetName(expander, group.Title);
            var groupRows = new List<Row>();
            foreach (DesignEditor shown in group.Rows.SelectMany(static r => r.Editors))
            {
                // A grid's child shows its position and does not edit it (DS4).
                DesignEditor editor = _document?.IsEditable(component, shown.Name) == false
                    ? shown with { Kind = DesignEditorKind.ReadOnly }
                    : shown;
                Row row = Build(component, editor);
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int at = grid.RowDefinitions.Count - 1;
                var label = new TextBlock { Text = editor.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 1), ToolTip = editor.Name };
                Grid.SetRow(label, at);
                Grid.SetRow(row.Control, at);
                Grid.SetColumn(row.Control, 1);
                grid.Children.Add(label);
                grid.Children.Add(row.Control);
                groupRows.Add(row);
            }

            // A collapsed group reads its values when it is opened.
            expander.Expanded += (_, _) =>
            {
                foreach (Row row in groupRows)
                {
                    row.Visible = true;
                    Show(row);
                }
            };
            expander.Collapsed += (_, _) => groupRows.ForEach(static r => r.Visible = false);
            foreach (Row row in groupRows)
            {
                row.Visible = group.Expanded;
            }

            _rows.AddRange(groupRows);
            _panel.Children.Add(expander);
        }

        RefreshValues();
    }

    /// <summary>One property's editor, and how it shows the value.</summary>
    private sealed class Row(DesignEditor editor, FrameworkElement control, Action<DesignValue?> show)
    {
        public DesignEditor Editor { get; } = editor;

        public FrameworkElement Control { get; } = control;

        public Action<DesignValue?> ShowValue { get; } = show;

        public bool Visible { get; set; }
    }

    private Row Build(DesignComponent component, DesignEditor editor)
    {
        string id = "Inspector." + editor.Name;
        switch (editor.Kind)
        {
            case DesignEditorKind.OnOff or DesignEditorKind.Logical:
            {
                var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
                Identify(box, id, editor.Name);
                RoutedEventHandler changed = (_, _) =>
                {
                    if (!_refreshing)
                    {
                        bool on = box.IsChecked == true;
                        Set(editor.Name, editor.Kind == DesignEditorKind.OnOff ? (on ? OnOffValue.On : OnOffValue.Off) : NumericValue.Logical(on));
                    }
                };
                box.Checked += changed;
                box.Unchecked += changed;
                return new Row(editor, box, value => box.IsChecked = value switch
                {
                    OnOffValue o => o.IsOn,
                    CharValue c => c.Text == "on",
                    NumericValue { Data: [var d] } => d != 0,
                    _ => false,
                });
            }

            case DesignEditorKind.Choice or DesignEditorKind.Component:
            {
                var combo = new ComboBox { Margin = new Thickness(0, 1, 0, 1) };
                Identify(combo, id, editor.Name);
                bool menus = editor.Kind == DesignEditorKind.Component;
                IEnumerable<string> choices = menus
                    ? [string.Empty, .. (_document?.Design.Components ?? []).Where(static c => c.Type == "matlab.ui.container.ContextMenu").Select(static c => c.CodeName)]
                    : editor.Choices;
                foreach (string choice in choices)
                {
                    combo.Items.Add(choice);
                }

                combo.SelectionChanged += (_, _) =>
                {
                    if (!_refreshing && combo.SelectedItem is string chosen)
                    {
                        Set(editor.Name, menus
                            ? chosen.Length == 0 ? null : new ComponentsValue([chosen], bracketed: false)
                            : new CharValue(chosen));
                    }
                };
                return new Row(editor, combo, value =>
                {
                    string shown = value switch
                    {
                        ComponentsValue { CodeNames: [var one] } => one,
                        CharValue c => c.Text,
                        StringValue s => s.Text,
                        _ => string.Empty,
                    };
                    if (!combo.Items.Contains(shown))
                    {
                        combo.Items.Add(shown);
                    }

                    combo.SelectedItem = shown;
                });
            }

            case DesignEditorKind.ReadOnly:
            {
                var text = new TextBlock { Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Identify(text, id, editor.Name);
                return new Row(editor, text, value => text.Text = value is null ? string.Empty : Shown(value, plain: false));
            }

            default:
            {
                // Text, a colour, or the value as MATLAB writes it.
                bool plain = editor.Kind == DesignEditorKind.Text;
                var box = new TextBox { Margin = new Thickness(0, 1, 0, 1) };
                Identify(box, id, editor.Name);
                string current = string.Empty;
                Commits(box, () => current, text =>
                {
                    if (!plain && component.Type == DesignDocument.GridType && editor.Name is "RowHeight" or "ColumnWidth"
                        && _document is { } document)
                    {
                        // A grid's tracks take App Designer's comma form too, and keep room for its components.
                        (DesignValue? tracks, string? refused) = document.ParseTracks(
                            component, editor.Name == "RowHeight" ? GridAxis.Row : GridAxis.Column, text);
                        if (tracks is null)
                        {
                            Problem?.Invoke(this, $"{component.CodeName}.{editor.Name}: {refused}");
                            RefreshValues();
                            return;
                        }

                        Set(editor.Name, tracks);
                        return;
                    }

                    DesignValue value = plain ? new CharValue(text) : DesignValueText.Parse(text);
                    Set(editor.Name, value);
                });
                FrameworkElement control = box;
                Border? swatch = null;
                if (editor.Kind == DesignEditorKind.Color)
                {
                    swatch = new Border { Width = 16, Height = 16, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Margin = new Thickness(0, 0, 4, 0) };
                    var dock = new DockPanel();
                    DockPanel.SetDock(swatch, Dock.Left);
                    dock.Children.Add(swatch);
                    dock.Children.Add(box);
                    control = dock;
                }

                return new Row(editor, control, value =>
                {
                    // A box being typed in keeps what is typed; one showing what it last showed takes the new value.
                    bool untouched = box.Text == current || !box.IsKeyboardFocusWithin;
                    current = value is null ? string.Empty : Shown(value, plain);
                    if (untouched)
                    {
                        box.Text = current;
                    }

                    if (swatch is not null)
                    {
                        swatch.Background = value is NumericValue { Data: [var r, var g, var b] }
                            ? new SolidColorBrush(Color.FromRgb(Byte(r), Byte(g), Byte(b)))
                            : Brushes.Transparent;
                    }
                });
            }
        }
    }

    private static byte Byte(double unit) => (byte)System.Math.Clamp(System.Math.Round(unit * 255), 0, 255);

    /// <summary>A value as the inspector shows it: text as itself, anything else as MATLAB writes it.</summary>
    private static string Shown(DesignValue value, bool plain) => value switch
    {
        CharValue c when plain => c.Text,
        StringValue s when plain => s.Text,
        CellValue { Items: [CharValue one] } when plain => one.Text,
        _ => DesignValueText.Format(value),
    };

    private static void Identify(FrameworkElement element, string id, string name)
    {
        System.Windows.Automation.AutomationProperties.SetAutomationId(element, id);
        System.Windows.Automation.AutomationProperties.SetName(element, name);
    }

    /// <summary>A text box that commits on Enter and on losing focus when its text changed, and goes back on Escape.</summary>
    private static void Commits(TextBox box, Func<string> shown, Action<string> commit)
    {
        void Commit()
        {
            if (box.Text != shown())
            {
                commit(box.Text);
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = shown();
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Commit();
    }

    // --- values ------------------------------------------------------------------------------------

    private void Set(string name, DesignValue? value)
    {
        if (_document is not { } document || _shown is not { } component)
        {
            return;
        }

        if (document.SetProperty(component, name, value) is { } refused)
        {
            Problem?.Invoke(this, $"{component.CodeName}.{name}: {refused}");
            RefreshValues();
        }
    }

    private void RefreshValues()
    {
        if (_document is not { } document || _shown is not { } component)
        {
            return;
        }

        if (_name is not null && (_name.Text == _nameShown || !_name.IsKeyboardFocusWithin))
        {
            _name.Text = _nameShown = component.CodeName;
        }
        else
        {
            _nameShown = component.CodeName;
        }

        foreach (Row row in _rows.Where(static r => r.Visible))
        {
            Show(row);
        }
    }

    private void Show(Row row)
    {
        if (_document is not { } document || _shown is not { } component)
        {
            return;
        }

        _refreshing = true;
        try
        {
            row.ShowValue(row.Editor.Name == "ContextMenu" ? component.Get("ContextMenu") : document.ValueOf(component, row.Editor.Name));
        }
        finally
        {
            _refreshing = false;
        }
    }
}
