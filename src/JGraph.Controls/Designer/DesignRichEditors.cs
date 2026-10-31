using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// What drops down from a row's kebab (the App Designer window plan's stage W7c): the tables of Items, ticks, a gauge's colours, a
/// table's columns and a date picker's dates, the fields of a position or limits, and View Format's choices, over
/// <see cref="DesignItemsModel"/> and its fellows. Each change goes to the document as one step.
/// </summary>
internal static class DesignRichEditors
{
    /// <summary>The editor <paramref name="editor"/>'s kebab opens; null for none.</summary>
    public static UIElement? For(DesignEditor editor, DesignDocument document, DesignInspection inspection, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
    {
        DesignComponent c = inspection.Anchor;
        return editor.Kind switch
        {
            DesignEditorKind.Items => new ItemsTable(editor, document, c, commit),
            DesignEditorKind.Ticks => new TicksTable(editor, document, c, commit),
            DesignEditorKind.ScaleColors => new ScaleTable(document, c, commit),
            DesignEditorKind.Columns => new ColumnsTable(document, c, commit),
            DesignEditorKind.DisabledDates => new DatesTable(document, c, commit),
            DesignEditorKind.ColorList => new ColorsTable(editor, document, c, commit),
            DesignEditorKind.DaysOfWeek => new DaysList(editor, document, c, commit),
            DesignEditorKind.DateLimits => new DateFields(editor, document, c, commit),
            DesignEditorKind.Fields => new NumberFields(editor, document, c, commit),
            DesignEditorKind.ViewFormat => new FormatChoices(editor, document, c, commit),
            _ => null,
        };
    }

    /// <summary>A drop-down's frame, in the theme's colours.</summary>
    public static Border Framed(UIElement content)
    {
        var frame = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(6), Child = content };
        frame.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Panel);
        frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);
        return frame;
    }

    /// <summary>The brush of a colour, 0 to 1.</summary>
    public static SolidColorBrush Brush(IReadOnlyList<double> rgb) => new(Color.FromRgb(Byte(rgb[0]), Byte(rgb[1]), Byte(rgb[2])));

    private static byte Byte(double unit) => (byte)System.Math.Clamp(System.Math.Round(unit * 255), 0, 255);

    /// <summary>Sets a control's automation id and name.</summary>
    public static T Named<T>(T element, string id, string name)
        where T : FrameworkElement
    {
        System.Windows.Automation.AutomationProperties.SetAutomationId(element, id);
        System.Windows.Automation.AutomationProperties.SetName(element, name);
        return element;
    }

    /// <summary>A cell's text box: committed by Enter and on losing the focus, put back by Escape.</summary>
    public static TextBox Cell(string id, string name, string text, Action<string> commit, double width = double.NaN)
    {
        var box = Named(new TextBox { Text = text, Width = width, Margin = new Thickness(1), MinWidth = 40 }, id, name);
        string shown = text;
        void Done()
        {
            if (box.Text != shown)
            {
                shown = box.Text;
                commit(box.Text);
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Done();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = shown;
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Done();
        return box;
    }

    /// <summary>A table with its header, rows, plus and minus (<c>_PSTable</c>): rows are made anew by <see cref="Refill"/>.</summary>
    internal abstract class Table : DockPanel
    {
        private readonly Grid _grid = new();
        private readonly TextBlock _words = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 };
        private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        private readonly List<Border> _bands = [];
        private readonly string[] _headers;

        protected Table(string id, params string[] headers)
        {
            _headers = headers;
            Prefix = id;
            Plus = Named(new Button { Content = "+", Width = 22, Height = 22, Padding = new Thickness(0), Margin = new Thickness(0, 0, 3, 0) }, id + ".Add", "Add");
            Minus = Named(new Button { Content = "−", Width = 22, Height = 22, Padding = new Thickness(0) }, id + ".Delete", "Delete");
            Plus.Click += (_, _) => OnAdd();
            Minus.Click += (_, _) => OnDelete();
            _buttons.Children.Add(Plus);
            _buttons.Children.Add(Minus);
            var scroll = new ScrollViewer { Content = _grid, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            SetDock(_words, Dock.Bottom);
            SetDock(_buttons, Dock.Bottom);
            Children.Add(_words);
            Children.Add(_buttons);
            Children.Add(scroll);
            MinWidth = 200;
            Loaded += (_, _) => Refill();
        }

        protected string Prefix { get; }

        protected Button Plus { get; }

        protected Button Minus { get; }

        /// <summary>The row selected; -1 for none.</summary>
        protected int SelectedRow { get; set; }

        /// <summary>What is said under the table.</summary>
        protected string Words
        {
            get => _words.Text;
            set => _words.Text = value;
        }

        protected abstract int RowCount { get; }

        protected abstract void OnAdd();

        protected abstract void OnDelete();

        /// <summary>The controls of row <paramref name="row"/>, a column each.</summary>
        protected abstract IEnumerable<FrameworkElement> RowCells(int row);

        /// <summary>The table's buttons and words brought up to date.</summary>
        protected virtual void Settle()
        {
            Minus.IsEnabled = SelectedRow >= 0 && RowCount > 0;
        }

        /// <summary>A control over a column's head; its words by default.</summary>
        protected virtual FrameworkElement Header(int column) => new TextBlock { Text = _headers[column], FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 0, 3, 2) };

        /// <summary>Makes the rows anew.</summary>
        protected void Refill()
        {
            _grid.Children.Clear();
            _grid.RowDefinitions.Clear();
            _grid.ColumnDefinitions.Clear();
            _bands.Clear();
            foreach (string _ in _headers)
            {
                _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < _headers.Length; c++)
            {
                FrameworkElement head = Header(c);
                Grid.SetColumn(head, c);
                _grid.Children.Add(head);
            }

            int rows = RowCount;
            if (SelectedRow >= rows)
            {
                SelectedRow = rows - 1;
            }

            for (int r = 0; r < rows; r++)
            {
                _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var band = new Border { Background = Brushes.Transparent };
                Grid.SetRow(band, r + 1);
                Grid.SetColumnSpan(band, _headers.Length);
                _grid.Children.Add(band);
                _bands.Add(band);
                int row = r;
                int c = 0;
                foreach (FrameworkElement cell in RowCells(r))
                {
                    Grid.SetRow(cell, r + 1);
                    Grid.SetColumn(cell, c++);
                    _grid.Children.Add(cell);
                    cell.GotKeyboardFocus += (_, _) => Pick(row);
                    cell.PreviewMouseDown += (_, _) => Pick(row);
                }
            }

            Mark();
            Settle();
        }

        private void Pick(int row)
        {
            SelectedRow = row;
            Mark();
            Settle();
        }

        private void Mark()
        {
            for (int r = 0; r < _bands.Count; r++)
            {
                if (r == SelectedRow)
                {
                    _bands[r].SetResourceReference(Border.BackgroundProperty, ThemeKeys.Selection);
                }
                else
                {
                    _bands[r].Background = Brushes.Transparent;
                }
            }
        }
    }

    /// <summary><c>ItemsEditor</c>: a radio button for the one selected, and each item's words.</summary>
    private sealed class ItemsTable : Table
    {
        private readonly DesignEditor _editor;
        private readonly DesignDocument _document;
        private readonly DesignComponent _component;
        private readonly Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> _commit;
        private DesignItemsModel _model;

        public ItemsTable(DesignEditor editor, DesignDocument document, DesignComponent component, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
            : base("Editor.Items", "Selected", "Items")
        {
            _editor = editor;
            _document = document;
            _component = component;
            _commit = commit;
            _model = Read();
            SelectedRow = _model.Items.Count > 0 ? 0 : -1; // onOpen
            bool shown = _model.ButtonsShown;
            Plus.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            Minus.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override int RowCount => _model.Items.Count;

        private DesignItemsModel Read()
        {
            DesignValue? items = _document.ValueOf(_component, "Items");
            DesignValue? data = _document.ValueOf(_component, "ItemsData");
            return new DesignItemsModel(DesignEditorValues.Lines(items), DesignEditorValues.SelectedIndex(items, data, _document.ValueOf(_component, "Value")), _editor.Info);
        }

        protected override void Settle()
        {
            Plus.IsEnabled = _model.CanAdd;
            Minus.IsEnabled = _model.CanDelete(SelectedRow);
        }

        protected override IEnumerable<FrameworkElement> RowCells(int row)
        {
            var radio = Named(new RadioButton { IsChecked = _model.SelectedIndex == row + 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, GroupName = "Editor.Items" }, "Editor.Items.Row" + row + ".Selected", "Selected");
            radio.Checked += (_, _) =>
            {
                if (_model.SelectedIndex != row + 1)
                {
                    Send(_model.Select(row));
                }
            };
            yield return radio;
            yield return Cell("Editor.Items.Row" + row + ".Text", "Item", _model.Items[row], text => Send(_model.SetItem(row, text)), 160);
        }

        protected override void OnAdd()
        {
            IReadOnlyList<DesignEditorCommit> commits = _model.AddRow(SelectedRow, out int to);
            SelectedRow = to;
            Send(commits);
        }

        protected override void OnDelete()
        {
            IReadOnlyList<DesignEditorCommit> commits = _model.DeleteRow(SelectedRow, out int to);
            SelectedRow = to;
            Send(commits);
        }

        // The value goes after the items, as the component checks one against the other.
        private void Send(IReadOnlyList<DesignEditorCommit> commits)
        {
            var changes = new List<(string, DesignValue?)>();
            if (commits.Any(static c => c.Property == "Items"))
            {
                changes.Add(("Items", DesignEditorValues.Items(_model.Items)));
            }

            if (commits.Any(static c => c.Property == "SelectedIndex"))
            {
                bool several = _component.Class?.Property("Multiselect") is not null && _document.ValueOf(_component, "Multiselect") is OnOffValue { IsOn: true } or NumericValue { Data: [not 0] };
                changes.Add(("Value", DesignEditorValues.ValueAt(_model.SelectedIndex, _model.Items, _document.ValueOf(_component, "ItemsData"), several)));
            }

            if (!_commit(changes))
            {
                _model = Read();
            }

            Refill();
        }
    }

    /// <summary><c>TicksDialogEditor</c>: each tick and its label, with a menu over each column.</summary>
    private sealed class TicksTable : Table
    {
        private readonly DesignDocument _document;
        private readonly DesignComponent _component;
        private readonly Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> _commit;
        private readonly (string Ticks, string Labels, string Limits) _names;
        private DesignTicksModel _model;

        public TicksTable(DesignEditor editor, DesignDocument document, DesignComponent component, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
            : base("Editor.Ticks", DesignEditorValues.TickNames(editor.Name).Ticks, DesignEditorValues.TickNames(editor.Name).Labels)
        {
            _document = document;
            _component = component;
            _commit = commit;
            _names = DesignEditorValues.TickNames(editor.Name);
            _model = Read();
            SelectedRow = _model.Rows.Count > 0 ? 0 : -1;
        }

        protected override int RowCount => _model.Rows.Count;

        private string ModeOf(string name) =>
            _document.ValueOf(_component, name + "Mode") is CharValue { Text: { Length: > 0 } mode } ? mode : _component.Get(name) is null ? "auto" : "manual";

        private DesignTicksModel Read() => new(
            _names.Ticks,
            _names.Labels,
            DesignEditorValues.Rows(_document.ValueOf(_component, _names.Ticks)).SelectMany(static r => r).ToArray(),
            DesignEditorValues.Lines(_document.ValueOf(_component, _names.Labels)),
            _document.ValueOf(_component, _names.Limits) is NumericValue { Data: [var low, var high] } ? [low, high] : [0, 100],
            ModeOf(_names.Ticks),
            ModeOf(_names.Labels));

        protected override FrameworkElement Header(int column)
        {
            string word = column == 0 ? _names.Ticks : _names.Labels;
            var head = new Button { Content = word + " ▾", Margin = new Thickness(1), Padding = new Thickness(3, 0, 3, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left };
            Named(head, "Editor.Ticks.Header" + column, word);
            var popup = new Popup { PlacementTarget = head, Placement = PlacementMode.Bottom, StaysOpen = false };
            head.Click += (_, _) =>
            {
                popup.Child = Framed(HeaderMenu(column, popup));
                popup.IsOpen = true;
            };
            var holder = new Grid();
            holder.Children.Add(head);
            holder.Children.Add(popup);
            return holder;
        }

        // HeaderMenuEditor: Step by over the ticks, and Auto over each column.
        private StackPanel HeaderMenu(int column, Popup popup)
        {
            var menu = new StackPanel { MinWidth = 120 };
            if (column == 0)
            {
                var step = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                var words = new TextBlock { Text = "Step by", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                DockPanel.SetDock(words, Dock.Left);
                step.Children.Add(words);
                string interval = _model.CurrentInterval is { } i ? DesignDocument.NumberText(i) : string.Empty;
                step.Children.Add(Cell("Editor.Ticks.StepBy", "Step by", interval, text =>
                {
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double by) && by > 0)
                    {
                        _model.SetInterval(by);
                        Send();
                    }
                }));
                menu.Children.Add(step);
            }

            var auto = Named(new Button { Content = "Auto", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Left }, "Editor.Ticks.Auto" + column, "Auto");
            auto.IsEnabled = (column == 0 ? _model.TickMode : _model.LabelMode) != "auto";
            auto.Click += (_, _) =>
            {
                popup.IsOpen = false;
                if (column == 0)
                {
                    _model.TicksToAuto();
                }
                else
                {
                    _model.LabelsToAuto();
                }

                Send();
            };
            menu.Children.Add(auto);
            return menu;
        }

        protected override void Settle()
        {
            Words = _model.Error;
            Minus.IsEnabled = _model.Rows.Count > 0 && SelectedRow >= 0;
        }

        protected override IEnumerable<FrameworkElement> RowCells(int row)
        {
            (object tick, string label) = _model.Rows[row];
            TextBox t = Cell("Editor.Ticks.Row" + row + ".Tick", "Tick", tick is double d ? DesignDocument.NumberText(d) : (string)tick, text =>
            {
                _model.EditTick(row, text);
                Send();
            }, 80);
            if (_model.Marked.Contains(row))
            {
                t.Foreground = Brushes.Firebrick;
            }

            yield return t;
            yield return Cell("Editor.Ticks.Row" + row + ".Label", "Label", label, text =>
            {
                _model.EditLabel(row, text);
                Send();
            }, 80);
        }

        protected override void OnAdd()
        {
            _model.AddRow(SelectedRow);
            Send();
        }

        protected override void OnDelete()
        {
            _model.DeleteRow(SelectedRow);
            Send();
        }

        // Commits as they go out; a mode gone automatic is its property put back to its default (the component then works it out).
        private void Send()
        {
            var changes = new List<(string, DesignValue?)>();
            foreach (DesignEditorCommit commit in _model.Commits)
            {
                if (commit.Property == _names.Ticks + "Mode")
                {
                    changes.Add((_names.Ticks, null));
                }
                else if (commit.Property == _names.Labels + "Mode")
                {
                    changes.Add((_names.Labels, null));
                }
                else if (commit.Property == _names.Ticks)
                {
                    changes.Add((_names.Ticks, NumericValue.Row(((List<object>)commit.After!).Select(static t => (double)t).ToArray())));
                }
                else if (commit.Property == _names.Labels)
                {
                    changes.Add((_names.Labels, DesignEditorValues.Items((List<string>)commit.After!)));
                }
            }

            int to = _model.RowToSelect;
            bool sent = _commit(changes);
            if (to >= 0)
            {
                SelectedRow = to;
            }

            // While the ticks are out of order the table holds what is typed; else what the component now holds.
            if (!sent || (changes.Count > 0 && _model.Error.Length == 0))
            {
                _model = Read();
            }

            Refill();
        }
    }

    /// <summary><c>ScaleColorsEditor</c>: a colour, Start and End on each row.</summary>
    private sealed class ScaleTable : Table
    {
        private readonly DesignDocument _document;
        private readonly DesignComponent _component;
        private readonly Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> _commit;
        private DesignScaleColorsModel _model;

        public ScaleTable(DesignDocument document, DesignComponent component, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
            : base("Editor.Scale", "Color", "Start", "End")
        {
            _document = document;
            _component = component;
            _commit = commit;
            _model = Read();
            SelectedRow = _model.Table.Count > 0 ? 0 : -1;
        }

        protected override int RowCount => _model.Table.Count;

        private DesignScaleColorsModel Read() => new(
            DesignEditorValues.Rows(_document.ValueOf(_component, "ScaleColors")),
            DesignEditorValues.Rows(_document.ValueOf(_component, "ScaleColorLimits")),
            _document.ValueOf(_component, "Limits") is NumericValue { Data: [var low, var high] } ? [low, high] : [0, 100]);

        protected override IEnumerable<FrameworkElement> RowCells(int row)
        {
            (double[] color, double start, double end) = _model.Table[row];
            var swatch = Named(new Button { Background = Brush(color), Width = 44, Height = 20, Margin = new Thickness(1), Padding = new Thickness(0), BorderBrush = Brushes.Gray }, "Editor.Scale.Row" + row + ".Color", "Color");
            var popup = new Popup { PlacementTarget = swatch, Placement = PlacementMode.Bottom, StaysOpen = false };
            swatch.Click += (_, _) =>
            {
                popup.Child = Framed(new DesignColorPickerView(color, [], "Editor.Scale", picked =>
                {
                    popup.IsOpen = false;
                    if (picked is NumericValue { Data: [var r, var g, var b] })
                    {
                        Send(_model.SetColor(row, [r, g, b]));
                    }
                }));
                popup.IsOpen = true;
            };
            var holder = new Grid();
            holder.Children.Add(swatch);
            holder.Children.Add(popup);
            yield return holder;
            yield return Cell("Editor.Scale.Row" + row + ".Start", "Start", DesignDocument.NumberText(start), text => Limit(_model.SetStart(row, text, out string? refused), refused), 60);
            yield return Cell("Editor.Scale.Row" + row + ".End", "End", DesignDocument.NumberText(end), text => Limit(_model.SetEnd(row, text, out string? refused), refused), 60);
        }

        private void Limit(IReadOnlyList<DesignEditorCommit>? commits, string? refused)
        {
            Words = refused ?? string.Empty;
            if (commits is not null)
            {
                Send(commits);
            }
            else
            {
                Refill();
            }
        }

        protected override void OnAdd()
        {
            IReadOnlyList<DesignEditorCommit> commits = _model.AddRow(SelectedRow, out int to);
            SelectedRow = to;
            Send(commits);
        }

        protected override void OnDelete()
        {
            IReadOnlyList<DesignEditorCommit> commits = _model.DeleteRow(SelectedRow, out int to);
            SelectedRow = to;
            Send(commits);
        }

        private void Send(IReadOnlyList<DesignEditorCommit> commits)
        {
            var changes = new List<(string, DesignValue?)>();
            foreach (DesignEditorCommit c in commits)
            {
                changes.Add((c.Property, DesignEditorValues.Matrix((List<double[]>)c.After!, c.Property == "ScaleColors" ? 3 : 2)));
            }

            if (!_commit(changes))
            {
                _model = Read();
            }

            Refill();
        }
    }

    /// <summary><c>UITableColumnEditor</c>: Name, Width(px), Auto Width, Editable and Sortable.</summary>
    private sealed class ColumnsTable : Table
    {
        private readonly DesignDocument _document;
        private readonly DesignComponent _component;
        private readonly Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> _commit;
        private DesignColumnsModel _model;

        public ColumnsTable(DesignDocument document, DesignComponent component, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
            : base("Editor.Columns", "Name", "Width(px)", "Auto Width", "Editable", "Sortable")
        {
            _document = document;
            _component = component;
            _commit = commit;
            _model = Read();
            SelectedRow = _model.Names.Count > 0 ? 0 : -1;
        }

        protected override int RowCount => _model.Names.Count;

        private DesignColumnsModel Read() => new(
            DesignEditorValues.ColumnNames(_document.ValueOf(_component, "ColumnName")),
            DesignEditorValues.ColumnWidths(_document.ValueOf(_component, "ColumnWidth")),
            DesignEditorValues.Flags(_document.ValueOf(_component, "ColumnEditable")),
            DesignEditorValues.Flags(_document.ValueOf(_component, "ColumnSortable")));

        private CheckBox Tick(string id, string name, bool on, Func<bool, IReadOnlyList<DesignEditorCommit>> set)
        {
            var box = Named(new CheckBox { IsChecked = on, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, id, name);
            RoutedEventHandler changed = (_, _) => Send(set(box.IsChecked == true));
            box.Checked += changed;
            box.Unchecked += changed;
            return box;
        }

        protected override IEnumerable<FrameworkElement> RowCells(int row)
        {
            string at = "Editor.Columns.Row" + row + ".";
            yield return Cell(at + "Name", "Name", _model.Names[row], text => Send(_model.SetName(row, text)), 70);
            yield return Cell(at + "Width", "Width(px)", _model.WidthCell(row), text => Send(_model.SetWidth(row, text)), 62);
            yield return Tick(at + "Auto", "Auto Width", _model.IsAuto(row), on => _model.SetAuto(row, on));
            yield return Tick(at + "Editable", "Editable", _model.Editable[row], on => _model.SetEditable(row, on));
            yield return Tick(at + "Sortable", "Sortable", _model.Sortable is { } s && s[row], on => _model.SetSortable(row, on));
        }

        protected override void OnAdd()
        {
            IReadOnlyList<DesignEditorCommit> commits = _model.AddRow(SelectedRow, out int to);
            SelectedRow = to;
            Send(commits);
        }

        protected override void OnDelete()
        {
            IReadOnlyList<DesignEditorCommit> commits = _model.DeleteRow(SelectedRow, out int to);
            SelectedRow = to;
            Send(commits);
        }

        private void Send(IReadOnlyList<DesignEditorCommit> commits)
        {
            var changes = new List<(string, DesignValue?)>();
            foreach (DesignEditorCommit c in commits)
            {
                changes.Add((c.Property, DesignEditorValues.ColumnValue(c.Property, c.After)));
            }

            if (!_commit(changes))
            {
                _model = Read();
            }

            Dispatcher.BeginInvoke(Refill);
        }
    }

    /// <summary><c>DateTableEditor</c>: a date picked on each row.</summary>
    private sealed class DatesTable : Table
    {
        private readonly DesignDocument _document;
        private readonly DesignComponent _component;
        private readonly Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> _commit;
        private DesignDatesModel _model;

        public DatesTable(DesignDocument document, DesignComponent component, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
            : base("Editor.Dates", "DisabledDates")
        {
            _document = document;
            _component = component;
            _commit = commit;
            _model = new DesignDatesModel(DesignEditorValues.Dates(_document.ValueOf(_component, "DisabledDates")));
            SelectedRow = _model.Dates.Count > 0 ? 0 : -1;
        }

        protected override int RowCount => _model.Dates.Count;

        protected override IEnumerable<FrameworkElement> RowCells(int row)
        {
            DesignDate d = _model.Dates[row];
            var picker = Named(new DatePicker { SelectedDate = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Unspecified), Margin = new Thickness(1) }, "Editor.Dates.Row" + row, "Date");
            picker.SelectedDateChanged += (_, _) =>
            {
                if (picker.SelectedDate is { } chosen)
                {
                    _model.Dates[row] = new DesignDate(chosen.Year, chosen.Month, chosen.Day);
                    Send();
                }
            };
            yield return picker;
        }

        protected override void OnAdd()
        {
            SelectedRow = _model.AddRow(SelectedRow, DateTime.Now);
            Send();
        }

        protected override void OnDelete()
        {
            SelectedRow = _model.DeleteRow(SelectedRow);
            Send();
        }

        private void Send()
        {
            if (!_commit([("DisabledDates", DesignEditorValues.DatesValue(_model.Dates.Select(static d => (DesignDate?)d).ToArray()))]))
            {
                _model = new DesignDatesModel(DesignEditorValues.Dates(_document.ValueOf(_component, "DisabledDates")));
            }

            Dispatcher.BeginInvoke(Refill);
        }
    }

    /// <summary><c>ColorOrderEditor</c>: a list of colours, added to and taken from.</summary>
    private sealed class ColorsTable : Table
    {
        private readonly string _name;
        private readonly Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> _commit;
        private readonly List<double[]> _colors;

        public ColorsTable(DesignEditor editor, DesignDocument document, DesignComponent component, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
            : base("Editor.Colors", editor.DisplayName)
        {
            _name = editor.Name;
            _commit = commit;
            _colors = DesignEditorValues.Rows(document.ValueOf(component, editor.Name));
            SelectedRow = _colors.Count > 0 ? 0 : -1;
        }

        protected override int RowCount => _colors.Count;

        protected override IEnumerable<FrameworkElement> RowCells(int row)
        {
            double[] color = _colors[row];
            var swatch = Named(new Button { Background = Brush(color), Width = 90, Height = 20, Margin = new Thickness(1), Padding = new Thickness(0), BorderBrush = Brushes.Gray }, "Editor.Colors.Row" + row, "Color");
            var popup = new Popup { PlacementTarget = swatch, Placement = PlacementMode.Bottom, StaysOpen = false };
            swatch.Click += (_, _) =>
            {
                popup.Child = Framed(new DesignColorPickerView(color, [], "Editor.Colors", picked =>
                {
                    popup.IsOpen = false;
                    if (picked is NumericValue { Data: [var r, var g, var b] })
                    {
                        _colors[row] = [r, g, b];
                        Send();
                    }
                }));
                popup.IsOpen = true;
            };
            var holder = new Grid();
            holder.Children.Add(swatch);
            holder.Children.Add(popup);
            yield return holder;
        }

        protected override void OnAdd()
        {
            _colors.Add([1, 0, 0]);
            SelectedRow = _colors.Count - 1;
            Send();
        }

        protected override void OnDelete()
        {
            _colors.RemoveAt(SelectedRow);
            SelectedRow = _colors.Count == 0 ? -1 : System.Math.Max(0, SelectedRow - 1);
            Send();
        }

        private void Send()
        {
            _commit([(_name, DesignEditorValues.Matrix(_colors, 3))]);
            Refill();
        }
    }

    /// <summary><c>DateCheckboxListEditor</c>: the days of the week, with Select All and Clear All.</summary>
    private sealed class DaysList : StackPanel
    {
        private static readonly string[] Days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

        public DaysList(DesignEditor editor, DesignDocument document, DesignComponent component, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
        {
            var on = new SortedSet<int>(DesignEditorValues.Rows(document.ValueOf(component, editor.Name)).SelectMany(static r => r).Select(static d => (int)d));
            var boxes = new List<CheckBox>();
            bool quiet = false;
            void Send()
            {
                if (!quiet)
                {
                    commit([(editor.Name, on.Count == 0 ? new NumericValue(0, 0, []) : NumericValue.Row(on.Select(static d => (double)d).ToArray()))]);
                }
            }

            var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            void All(string id, string word, bool state)
            {
                var link = Named(new Button { Content = word, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 4, 0) }, id, word);
                link.Click += (_, _) =>
                {
                    quiet = true;
                    foreach (CheckBox box in boxes)
                    {
                        box.IsChecked = state;
                    }

                    quiet = false;
                    Send();
                };
                links.Children.Add(link);
            }

            All("Editor.Days.SelectAll", "Select All", true);
            All("Editor.Days.ClearAll", "Clear All", false);
            Children.Add(links);
            Children.Add(new TextBlock { Text = "Disabled Days of Week", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            for (int d = 1; d <= 7; d++)
            {
                int day = d;
                var box = Named(new CheckBox { Content = Days[d - 1], IsChecked = on.Contains(d), Margin = new Thickness(0, 1, 0, 1) }, "Editor.Days." + Days[d - 1], Days[d - 1]);
                box.Checked += (_, _) =>
                {
                    on.Add(day);
                    Send();
                };
                box.Unchecked += (_, _) =>
                {
                    on.Remove(day);
                    Send();
                };
                boxes.Add(box);
                Children.Add(box);
            }
        }
    }

    /// <summary><c>DatePickerRichEditor</c>: Start and End.</summary>
    private sealed class DateFields : Grid
    {
        public DateFields(DesignEditor editor, DesignDocument document, DesignComponent component, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            IReadOnlyList<DesignDate?> held = document.ValueOf(component, editor.Name) is DatetimeValue { Items.Count: 2 } d ? d.Items : [null, null];
            var dates = new DesignDate?[] { held[0], held[1] };
            string[] words = ["Start", "End"];
            for (int i = 0; i < 2; i++)
            {
                int at = i;
                RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var said = new TextBlock { Text = words[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                SetRow(said, i);
                Children.Add(said);
                var picker = Named(new DatePicker { Margin = new Thickness(1), SelectedDate = dates[i] is { } v ? new DateTime(v.Year == 0 ? 1 : v.Year, v.Month, v.Day, 0, 0, 0, DateTimeKind.Unspecified) : null }, "Editor.Dates." + words[i], words[i]);
                picker.SelectedDateChanged += (_, _) =>
                {
                    dates[at] = picker.SelectedDate is { } chosen ? new DesignDate(chosen.Year, chosen.Month, chosen.Day) : null;
                    commit([(editor.Name, DesignEditorValues.DatesValue(dates))]);
                };
                SetRow(picker, i);
                SetColumn(picker, 1);
                Children.Add(picker);
            }
        }
    }

    /// <summary><c>PropertyFieldsEditor</c> and <c>ResetLimitsEditor</c>: a field for each number, and for limits with a mode, Auto.</summary>
    private sealed class NumberFields : StackPanel
    {
        public NumberFields(DesignEditor editor, DesignDocument document, DesignComponent component, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
        {
            IReadOnlyList<string> fields = editor.Info.Fields.Count > 0 ? editor.Info.Fields : ["min", "max"];
            double[] numbers = DesignFieldsEditor.Read(DesignEditorValues.Rows(document.ValueOf(component, editor.Name)).SelectMany(static r => r).ToArray(), fields.Count);
            var words = new TextBlock { Foreground = Brushes.Firebrick };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            for (int i = 0; i < fields.Count; i++)
            {
                int at = i;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var said = new TextBlock { Text = fields[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                Grid.SetRow(said, i);
                grid.Children.Add(said);
                TextBox box = Cell("Editor.Fields." + fields[i], fields[i], DesignDocument.NumberText(numbers[i]), text =>
                {
                    if (DesignFieldsEditor.Number(text) is not { } number)
                    {
                        return;
                    }

                    double was = numbers[at];
                    numbers[at] = number;
                    if (DesignFieldsEditor.Refused(editor.Info, numbers))
                    {
                        words.Text = DesignFieldsEditor.MaxNotAboveMin;
                        return; // kept here until it is right (_pendingChange)
                    }

                    words.Text = string.Empty;
                    if (!commit([(editor.Name, NumericValue.Row(numbers))]))
                    {
                        numbers[at] = was;
                    }
                });
                Grid.SetRow(box, i);
                Grid.SetColumn(box, 1);
                grid.Children.Add(box);
            }

            Children.Add(grid);
            Children.Add(words);
            if (editor.Info.Rich == "ResetLimitsEditor")
            {
                var auto = Named(new Button { Content = "Auto", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left }, "Editor.Fields.Auto", "Auto");
                auto.Click += (_, _) => commit([(editor.Name, null)]);
                Children.Add(auto);
            }
        }
    }

    /// <summary><c>DisplayFormatEditor</c>: Default, a number of decimals, Integer, Scientific or Custom.</summary>
    private sealed class FormatChoices : StackPanel
    {
        private readonly TextBox _decimals;
        private readonly TextBox _custom;
        private readonly Dictionary<DesignFormatMode, RadioButton> _radios = [];
        private readonly Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> _commit;
        private readonly string _name;
        private readonly TextBlock _words = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, MaxWidth = 220 };
        private string _format;
        private bool _quiet;

        public FormatChoices(DesignEditor editor, DesignDocument document, DesignComponent component, Func<IReadOnlyList<(string Name, DesignValue? Value)>, bool> commit)
        {
            _commit = commit;
            _name = editor.Name;
            _format = document.ValueOf(component, editor.Name) is CharValue c ? c.Text : "%11.4g";
            (DesignFormatMode mode, string? value) = DesignDisplayFormat.Read(_format);
            _decimals = Cell("Editor.Format.Decimals", "decimals", mode == DesignFormatMode.Decimal ? value ?? "2" : "2", text => Typed(DesignFormatMode.Decimal, text), 40);
            _custom = Cell("Editor.Format.Custom", "Custom", mode == DesignFormatMode.Custom ? value ?? string.Empty : "%11.4g", text => Typed(DesignFormatMode.Custom, text), 120);
            Add(DesignFormatMode.Default, "Default", null, null);
            Add(DesignFormatMode.Decimal, "Exactly", _decimals, "decimals");
            Add(DesignFormatMode.Integer, "Integer", null, null);
            Add(DesignFormatMode.Scientific, "Scientific", null, null);
            Add(DesignFormatMode.Custom, "Custom", _custom, null);
            Children.Add(_words);
            _quiet = true;
            _radios[mode].IsChecked = true;
            _quiet = false;
            Settle(mode);
        }

        private void Add(DesignFormatMode mode, string word, TextBox? field, string? after)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            var radio = Named(new RadioButton { Content = word, GroupName = "Editor.Format", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }, "Editor.Format." + mode, word);
            radio.Checked += (_, _) =>
            {
                Settle(mode);
                if (!_quiet)
                {
                    Typed(mode, mode == DesignFormatMode.Decimal ? _decimals.Text : mode == DesignFormatMode.Custom ? _custom.Text : null);
                }
            };
            _radios[mode] = radio;
            line.Children.Add(radio);
            if (field is not null)
            {
                line.Children.Add(field);
            }

            if (after is not null)
            {
                line.Children.Add(new TextBlock { Text = after, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
            }

            Children.Add(line);
        }

        private void Settle(DesignFormatMode mode)
        {
            _decimals.IsEnabled = mode == DesignFormatMode.Decimal;
            _custom.IsEnabled = mode == DesignFormatMode.Custom;
        }

        // _commit, with _handleTextFieldChangeEvent's rule: no decimals at all is Integer.
        private void Typed(DesignFormatMode mode, string? value)
        {
            if (mode == DesignFormatMode.Decimal && value is { Length: > 0 } && value.All(static ch => ch == '0'))
            {
                _radios[DesignFormatMode.Integer].IsChecked = true;
                return;
            }

            _words.Text = string.Empty;
            if (DesignDisplayFormat.Write(mode, value) is not { } written)
            {
                _words.Text = DesignDisplayFormat.NotWhole;
                return;
            }

            if (!DesignDisplayFormat.IsValid(written))
            {
                _words.Text = "Invalid format specification: " + written;
                return;
            }

            if (written != _format && _commit([(_name, new CharValue(written))]))
            {
                _format = written;
            }
        }
    }
}

/// <summary>
/// The colour picker (<c>mw-colorpicker/ColorPicker</c>): the standard colours, the recent ones, and custom numbers, each choosing a colour at once.
/// </summary>
internal sealed class DesignColorPickerView : StackPanel
{
    // What was chosen lately, kept over the session as the picker keeps it (recentlyUsedColors), the seven newest.
    private static readonly List<string> RecentColors = [.. DesignColors.Recent];

    public DesignColorPickerView(IReadOnlyList<double> held, IReadOnlyList<string> styles, string id, Action<DesignValue> picked)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(picked);
        void Choose(double[] rgb)
        {
            string hex = DesignColors.Hex(rgb);
            RecentColors.Remove(hex);
            RecentColors.Insert(0, hex);
            if (RecentColors.Count > 7)
            {
                RecentColors.RemoveAt(7);
            }

            picked(NumericValue.Row(rgb));
        }

        foreach (string style in styles)
        {
            string word = char.ToUpperInvariant(style[0]) + style[1..];
            var button = DesignRichEditors.Named(new Button { Content = word, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Left }, id + ".Style." + style, word);
            button.Click += (_, _) => picked(new CharValue(style));
            Children.Add(button);
        }

        Children.Add(new TextBlock { Text = "Standard Colors", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        foreach (IReadOnlyList<string> row in DesignColors.Standard)
        {
            Children.Add(Cells(row, id + ".Standard", Choose));
        }

        Children.Add(new TextBlock { Text = "Recent Colors", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
        Children.Add(Cells(RecentColors, id + ".Recent", Choose));
        Children.Add(new TextBlock { Text = "Custom Colors", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
        var custom = new StackPanel { Orientation = Orientation.Horizontal };
        string[] words = ["R", "G", "B"];
        var boxes = new TextBox[3];
        void Typed()
        {
            var rgb = new double[3];
            for (int i = 0; i < 3; i++)
            {
                if (!double.TryParse(boxes[i].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || n < 0 || n > 255)
                {
                    return;
                }

                rgb[i] = n / 255;
            }

            Choose(rgb);
        }

        for (int i = 0; i < 3; i++)
        {
            custom.Children.Add(new TextBlock { Text = words[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(i == 0 ? 0 : 4, 0, 2, 0) });
            int shown = (int)System.Math.Round(System.Math.Clamp(held.Count > i ? held[i] : 0, 0, 1) * 255);
            boxes[i] = DesignRichEditors.Named(new TextBox { Text = shown.ToString(CultureInfo.InvariantCulture), Width = 34 }, id + ".Custom." + words[i], words[i]);
            custom.Children.Add(boxes[i]);
        }

        var apply = DesignRichEditors.Named(new Button { Content = "OK", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(6, 0, 0, 0) }, id + ".Custom.OK", "OK");
        apply.Click += (_, _) => Typed();
        custom.Children.Add(apply);
        Children.Add(custom);
    }

    private static StackPanel Cells(IEnumerable<string> colors, string id, Action<double[]> choose)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (string color in colors)
        {
            double[] rgb = DesignColors.FromHex(color);
            var cell = DesignRichEditors.Named(
                new Button { Background = DesignRichEditors.Brush(rgb), Width = 20, Height = 20, Margin = new Thickness(1), Padding = new Thickness(0), BorderBrush = Brushes.Gray, ToolTip = color },
                id + "." + color,
                color);
            cell.Click += (_, _) => choose(rgb);
            row.Children.Add(cell);
        }

        return row;
    }
}
