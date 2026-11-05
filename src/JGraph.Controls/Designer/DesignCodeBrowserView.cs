using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>What a code browser row stands for.</summary>
public enum DesignCodeItemKind
{
    /// <summary>A callback or the startup function.</summary>
    Callback,

    /// <summary>A function the user wrote in the editable section.</summary>
    Function,

    /// <summary>A property the user declared in the editable section, or a custom UI component's managed property (W15).</summary>
    Property,

    /// <summary>A custom UI component's event with its public callback (App Designer window plan W15).</summary>
    Event,
}

/// <summary>One row of the code browser: its kind and its name.</summary>
public sealed record DesignCodeItem(DesignCodeItemKind Kind, string Name);

/// <summary>
/// The visual designer's code browser (designer plan DS5; App Designer's Code Browser): three tabs,
/// Callbacks, Functions and Properties, each a searched list with a button to add one. A row picked
/// scrolls the code view to its code; Go to puts the caret there; Delete, Rename and Insert at
/// Cursor are on each row's menu. The callbacks are the app's functions in the order of its code,
/// one no component names marked; the functions and properties are read from the editable section.
/// </summary>
public sealed class DesignCodeBrowserView : UserControl
{
    private readonly TabControl _tabs = new();
    private readonly Tab _callbacks;
    private readonly Tab _functions;
    private readonly Tab _properties;
    private readonly Tab _events;
    private DesignDocument? _document;
    private bool _queued;
    private bool _showing;

    public DesignCodeBrowserView()
    {
        _callbacks = new Tab(this, DesignCodeItemKind.Callback, "Callbacks", "Add a callback function to make your app respond to user interactions such as button clicks.");
        _functions = new Tab(this, DesignCodeItemKind.Function, "Functions", "Add a function to organize your code into helper or utility functions. Call the function with app as the first input argument:  func(app);");
        _properties = new Tab(this, DesignCodeItemKind.Property, "Properties", "Add a property to create a variable to store and share data between callbacks and functions. Specify the property name with the prefix app. to access the property value:  app.Property = someData;");
        _events = new Tab(this, DesignCodeItemKind.Event, "Events", DesignComponentCode.AddEventDescription);
        _tabs.Items.Add(_callbacks.Item);
        _tabs.Items.Add(_functions.Item);
        _tabs.Items.Add(_properties.Item);
        _tabs.Items.Add(_events.Item);
        _events.Item.Visibility = Visibility.Collapsed;
        _tabs.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_tabs, "CodeBrowser");
        Content = _tabs;
    }

    /// <summary>Raised when a row is picked: the code view scrolls to it.</summary>
    public event EventHandler<DesignCodeItem>? Picked;

    /// <summary>Raised for Go to (the menu, Ctrl+D): the code view puts the caret there.</summary>
    public event EventHandler<DesignCodeItem>? GoToRequested;

    /// <summary>Raised for Rename (the menu, F2, a double-click, as App Designer's browser opens its rename editor): the window asks for the name and shows what changes.</summary>
    public event EventHandler<DesignCodeItem>? RenameRequested;

    /// <summary>Raised with the text Insert at Cursor puts into the code view.</summary>
    public event EventHandler<string>? InsertRequested;

    /// <summary>Raised by the Callbacks tab's add button: the window opens the Add Callback dialog.</summary>
    public event EventHandler? AddCallbackRequested;

    /// <summary>Raised by the startup function's App Input Arguments: the window opens the code options.</summary>
    public event EventHandler? InputArgumentsRequested;

    /// <summary>Raised by a custom UI component's Events tab's add button, or the Callbacks tab's Event–Public Callback Pair (W15): the window opens the Add Event dialog.</summary>
    public event EventHandler? AddEventRequested;

    /// <summary>Raised for Edit on an event's row (W15): the window opens the event's dialog.</summary>
    public event EventHandler<DesignCodeEvent>? EditEventRequested;

    /// <summary>Raised by a custom UI component's Properties tab's add button with the access chosen (W15): the window opens the Add Property dialog.</summary>
    public event EventHandler<string>? AddManagedPropertyRequested;

    /// <summary>Raised for Edit on a managed property's row (W15): the window opens the property's dialog.</summary>
    public event EventHandler<DesignManagedProperty>? EditManagedPropertyRequested;

    /// <summary>Raised when a function or property was added, with its kind and name: the code view selects it.</summary>
    public event EventHandler<DesignCodeItem>? Added;

    /// <summary>Raised with what could not be done, for the window's status line.</summary>
    public event EventHandler<string>? Problem;

    /// <summary>The app shown.</summary>
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

            Show();
        }
    }

    /// <summary>Shows the Callbacks tab with <paramref name="name"/> picked: the lists are filled again first, so a callback just added has its row.</summary>
    public void ShowCallback(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Show();
        _tabs.SelectedItem = _callbacks.Item;
        _callbacks.Select(name);
    }

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
            Show();
        });
    }

    private void Show()
    {
        _showing = true;
        try
        {
            if (_document is not { } document)
            {
                _callbacks.Fill([]);
                _functions.Fill([]);
                _properties.Fill([]);
                _events.Fill([]);
                _events.Item.Visibility = Visibility.Collapsed;
                _callbacks.SetComponent(false);
                _properties.SetComponent(false);
                return;
            }

            // A custom UI component's browser (W15): the Callbacks tab's add splits into a callback and an event-callback pair, the
            // Properties tab lists its managed properties before the user's own and adds through the Add Property dialog, and the
            // Events tab stands beside them.
            bool component = document.IsComponent;
            _callbacks.SetComponent(component);
            _properties.SetComponent(component);
            _events.Item.Visibility = component ? Visibility.Visible : Visibility.Collapsed;
            if (!component && ReferenceEquals(_tabs.SelectedItem, _events.Item))
            {
                _tabs.SelectedItem = _callbacks.Item;
            }

            string objectName = document.Code?.ObjectName ?? "app";
            _callbacks.Fill(document.Callbacks.Select(c => new Entry(c.Name, c.Name,
                !c.IsStartup && !c.IsGenerated && DesignCallbacks.BindingsOf(document.Design, c.Name).Count == 0 ? $"{c.Name} is not associated with a component." : null,
                c.IsGenerated)).ToArray());
            _functions.Fill(document.UserFunctions().Select(f => new Entry(f.Name, f.Parameters is null ? f.Name : $"{f.Name}({f.Parameters})", null, false)).ToArray());
            _properties.Fill(document.ManagedProperties.Select(m => new Entry(m.Name, objectName + "." + m.Name, null, false, Managed: true, Tip: m.Description.Length == 0 ? null : m.Description))
                .Concat(document.UserProperties().Select(p => new Entry(p, objectName + "." + p, null, false))).ToArray());
            _events.Fill(document.Events.Select(e => new Entry(e.Name, e.Name, null, false, Tip: e.Description.Length == 0 ? e.CallbackProperty : $"{e.CallbackProperty}: {e.Description}")).ToArray());
        }
        finally
        {
            _showing = false;
        }
    }

    private void Add(DesignCodeItemKind kind, string access)
    {
        if (_document is not { } document)
        {
            return;
        }

        if (kind == DesignCodeItemKind.Callback)
        {
            AddCallbackRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (kind == DesignCodeItemKind.Event)
        {
            AddEventRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (kind == DesignCodeItemKind.Property && document.IsComponent)
        {
            AddManagedPropertyRequested?.Invoke(this, access);
            return;
        }

        string? name = kind == DesignCodeItemKind.Function ? document.InsertUserFunction(access) : document.InsertUserProperty(access);
        if (name is null)
        {
            Problem?.Invoke(this, "The editable section's blocks could not be read, so nothing was added.");
            return;
        }

        Added?.Invoke(this, new DesignCodeItem(kind, name));
    }

    private void Delete(DesignCodeItem item)
    {
        if (_document is not { } document)
        {
            return;
        }

        string? refused = item.Kind switch
        {
            DesignCodeItemKind.Callback => document.Callbacks.FirstOrDefault(c => c.Name == item.Name) is { } callback ? document.DeleteCallback(callback) : null,
            DesignCodeItemKind.Function => document.DeleteUserFunction(item.Name),
            DesignCodeItemKind.Event => document.Events.FirstOrDefault(e => e.Name == item.Name) is { } found ? document.RemoveEvent(found) : null,
            _ when document.ManagedProperties.FirstOrDefault(m => m.Name == item.Name) is { } managed => document.RemoveManagedProperty(managed),
            _ => document.DeleteUserProperty(item.Name),
        };
        if (refused is not null)
        {
            Problem?.Invoke(this, refused);
        }
    }

    private string InsertText(DesignCodeItem item) => item.Kind switch
    {
        DesignCodeItemKind.Callback => $"{item.Name}({ObjectName});",
        DesignCodeItemKind.Property => $"{ObjectName}.{item.Name}",
        DesignCodeItemKind.Event => _document?.Events.FirstOrDefault(e => e.Name == item.Name)?.NotifyLine ?? $"notify({ObjectName}, '{item.Name}');",
        _ => $"{item.Name}({(_document?.UserFunctions().FirstOrDefault(f => f.Name == item.Name).Parameters ?? string.Empty)});",
    };

    private string ObjectName => _document?.Code?.ObjectName ?? "app";

    /// <summary>Opens an event's or a managed property's dialog for the row (its Edit item, F2, a double-click).</summary>
    private void Edit(DesignCodeItem item)
    {
        if (_document is not { } document)
        {
            return;
        }

        if (item.Kind == DesignCodeItemKind.Event && document.Events.FirstOrDefault(e => e.Name == item.Name) is { } found)
        {
            EditEventRequested?.Invoke(this, found);
        }
        else if (item.Kind == DesignCodeItemKind.Property && document.ManagedProperties.FirstOrDefault(m => m.Name == item.Name) is { } managed)
        {
            EditManagedPropertyRequested?.Invoke(this, managed);
        }
        else
        {
            RenameRequested?.Invoke(this, item);
        }
    }

    /// <summary>A row: the name, what is shown, a warning, whether it is App Designer's own (not deleted), whether it is a managed property, and its tip.</summary>
    private sealed record Entry(string Name, string Shown, string? Warning, bool Generated, bool Managed = false, string? Tip = null);

    /// <summary>One tab: a search box, the add button(s) and the list.</summary>
    private sealed class Tab
    {
        private readonly DesignCodeBrowserView _owner;
        private readonly DesignCodeItemKind _kind;
        private readonly TextBox _search = new() { MinWidth = 60, Margin = new Thickness(2), ToolTip = "Search" };
        private readonly ListBox _list = new() { BorderThickness = new Thickness(0) };
        private readonly TextBlock _help;
        private readonly Button? _more;
        private readonly MenuItem? _privateItem;
        private readonly MenuItem? _publicItem;
        private readonly string _helpText;
        private IReadOnlyList<Entry> _entries = [];

        public Tab(DesignCodeBrowserView owner, DesignCodeItemKind kind, string title, string help)
        {
            _owner = owner;
            _kind = kind;
            _helpText = help;
            _help = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6), Visibility = Visibility.Collapsed };
            _help.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
            _list.SetResourceReference(Control.BackgroundProperty, ThemeKeys.Panel);
            _list.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Text);
            System.Windows.Automation.AutomationProperties.SetAutomationId(_list, "CodeBrowser." + title);
            System.Windows.Automation.AutomationProperties.SetAutomationId(_search, "CodeBrowser." + title + ".Search");

            var bar = new DockPanel { LastChildFill = true };
            string what = kind switch { DesignCodeItemKind.Callback => "callback", DesignCodeItemKind.Function => "function", DesignCodeItemKind.Event => "event", _ => "property" };
            var add = new Button { Content = "+", Width = 24, Margin = new Thickness(2), ToolTip = kind switch
            {
                DesignCodeItemKind.Callback => "Add a callback function to respond to user interactions",
                DesignCodeItemKind.Function => "Add a function to organize your code into helper or utility functions",
                DesignCodeItemKind.Event => DesignComponentCode.AddEventHelpText,
                _ => "Add a property to create a variable to store and share data between callbacks and functions",
            } };
            System.Windows.Automation.AutomationProperties.SetAutomationId(add, "CodeBrowser." + title + ".Add");
            System.Windows.Automation.AutomationProperties.SetName(add, "Add " + what);
            add.Click += (_, _) => _owner.Add(_kind, "private");
            DockPanel.SetDock(add, Dock.Right);
            bar.Children.Add(add);
            if (kind is DesignCodeItemKind.Function or DesignCodeItemKind.Property)
            {
                string noun = kind == DesignCodeItemKind.Function ? "Function" : "Property";
                var more = new Button { Content = "▾", Width = 18, Margin = new Thickness(0, 2, 0, 2), ToolTip = "Private or public" };
                var menu = new ContextMenu();
                var privateItem = new MenuItem { Header = "Private " + noun, ToolTip = kind == DesignCodeItemKind.Function ? "Private functions can be called within the app only" : "Private properties store data to be shared within the app only" };
                var publicItem = new MenuItem { Header = "Public " + noun, ToolTip = kind == DesignCodeItemKind.Function ? "Public functions can be called inside and outside of the app" : "Public properties store data to be shared inside and outside of the app" };
                privateItem.Click += (_, _) => _owner.Add(_kind, "private");
                publicItem.Click += (_, _) => _owner.Add(_kind, "public");
                menu.Items.Add(privateItem);
                menu.Items.Add(publicItem);
                more.Click += (_, _) =>
                {
                    menu.PlacementTarget = more;
                    menu.IsOpen = true;
                };
                DockPanel.SetDock(more, Dock.Right);
                bar.Children.Add(more);
                _more = more;
                _privateItem = privateItem;
                _publicItem = publicItem;
            }
            else if (kind == DesignCodeItemKind.Callback)
            {
                // A custom UI component's Callback button splits (W15): a callback, or an event with its public callback.
                var more = new Button { Content = "▾", Width = 18, Margin = new Thickness(0, 2, 0, 2), ToolTip = "Callback or event", Visibility = Visibility.Collapsed };
                System.Windows.Automation.AutomationProperties.SetAutomationId(more, "CodeBrowser.Callbacks.More");
                var menu = new ContextMenu();
                var callbackItem = new MenuItem { Header = "Callback", ToolTip = "Add a callback function to respond to user interactions" };
                var eventItem = new MenuItem { Header = "Event–Public Callback Pair", ToolTip = DesignComponentCode.AddEventDescription };
                System.Windows.Automation.AutomationProperties.SetAutomationId(eventItem, "CodeBrowser.Callbacks.AddEventPair");
                callbackItem.Click += (_, _) => _owner.Add(DesignCodeItemKind.Callback, "private");
                eventItem.Click += (_, _) => _owner.Add(DesignCodeItemKind.Event, "public");
                menu.Items.Add(callbackItem);
                menu.Items.Add(eventItem);
                more.Click += (_, _) =>
                {
                    menu.PlacementTarget = more;
                    menu.IsOpen = true;
                };
                DockPanel.SetDock(more, Dock.Right);
                bar.Children.Add(more);
                _more = more;
            }

            bar.Children.Add(_search);
            _search.TextChanged += (_, _) => Filter();
            _search.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _search.Clear();
                    e.Handled = true;
                }
            };

            var body = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top);
            DockPanel.SetDock(_help, Dock.Top);
            body.Children.Add(bar);
            body.Children.Add(_help);
            body.Children.Add(_list);
            Item = new TabItem { Header = title, Content = body };

            _list.SelectionChanged += (_, _) =>
            {
                if (!_owner._showing && Selected is { } item)
                {
                    _owner.Picked?.Invoke(_owner, item);
                }
            };
            // A double-click opens the rename, as App Designer's browser opens its in-place editor (never on App Designer's own function); an event's or a managed property's dialog (W15).
            _list.MouseDoubleClick += (_, _) =>
            {
                if (Selected is { } item && !IsGenerated(item))
                {
                    _owner.Edit(item);
                }
            };
            _list.KeyDown += (_, e) =>
            {
                if (Selected is not { } item)
                {
                    return;
                }

                if (e.Key == Key.Delete || e.Key == Key.Back)
                {
                    if (!IsGenerated(item))
                    {
                        _owner.Delete(item);
                    }

                    e.Handled = true;
                }
                else if (e.Key == Key.F2)
                {
                    _owner.Edit(item);
                    e.Handled = true;
                }
                else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    _owner.GoToRequested?.Invoke(_owner, item);
                    e.Handled = true;
                }
            };
            _list.ContextMenuOpening += (_, e) =>
            {
                if (Selected is not { } item)
                {
                    e.Handled = true;
                    return;
                }

                _list.ContextMenu = Menu(item);
            };
            _list.ContextMenu = new ContextMenu();
        }

        public TabItem Item { get; }

        /// <summary>Whether the document is a custom UI component (W15): the Callbacks tab shows its split, the Properties tab says <c>comp.</c> and adds through the dialog.</summary>
        public void SetComponent(bool component)
        {
            if (_kind == DesignCodeItemKind.Callback && _more is not null)
            {
                _more.Visibility = component ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (_kind == DesignCodeItemKind.Property)
            {
                _help.Text = component ? _helpText.Replace("app.", "comp.", StringComparison.Ordinal) : _helpText;
                if (_privateItem is not null && _publicItem is not null)
                {
                    _privateItem.ToolTip = component ? "Private properties store data to be shared within the component only" : "Private properties store data to be shared within the app only";
                    _publicItem.ToolTip = component ? DesignComponentCode.PublicPropertyDescription : "Public properties store data to be shared inside and outside of the app";
                }
            }
        }

        private DesignCodeItem? Selected => _list.SelectedItem is ListBoxItem { Tag: Entry entry } ? new DesignCodeItem(_kind, entry.Name) : null;

        private bool IsGenerated(DesignCodeItem item) => _entries.FirstOrDefault(e => e.Name == item.Name)?.Generated == true;

        public void Fill(IReadOnlyList<Entry> entries)
        {
            string? selected = Selected?.Name;
            _entries = entries;
            Filter();
            _help.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _search.IsEnabled = entries.Count > 0;
            if (selected is not null)
            {
                Select(selected);
            }
        }

        public void Select(string name)
        {
            _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is Entry e && e.Name == name);
            if (_list.SelectedItem is ListBoxItem item)
            {
                item.BringIntoView();
            }
        }

        private void Filter()
        {
            string find = _search.Text.Trim();
            _list.Items.Clear();
            IEnumerable<Entry> shown = _entries.Where(e => find.Length == 0 || e.Shown.Contains(find, StringComparison.OrdinalIgnoreCase));
            if (find.Length > 0)
            {
                shown = shown.OrderBy(static e => e.Shown, StringComparer.OrdinalIgnoreCase);
            }

            foreach (Entry entry in shown)
            {
                var row = new ListBoxItem { Content = entry.Warning is null ? entry.Shown : entry.Shown + "  ⚠", Tag = entry, ToolTip = entry.Warning ?? entry.Tip };
                System.Windows.Automation.AutomationProperties.SetName(row, entry.Shown);
                _list.Items.Add(row);
            }

            if (_entries.Count > 0 && _list.Items.Count == 0)
            {
                _list.Items.Add(new ListBoxItem { Content = "No matches found", IsEnabled = false });
            }
        }

        private ContextMenu Menu(DesignCodeItem item)
        {
            var menu = new ContextMenu();
            if (item.Kind == DesignCodeItemKind.Callback && _owner._document?.Callbacks.FirstOrDefault(c => c.Name == item.Name) is { IsStartup: true })
            {
                var arguments = new MenuItem { Header = _owner._document.InputParameters.Length == 0 ? "Add App Input Arguments..." : "Edit App Input Arguments..." };
                arguments.Click += (_, _) => _owner.InputArgumentsRequested?.Invoke(_owner, EventArgs.Empty);
                menu.Items.Add(arguments);
                menu.Items.Add(new Separator());
            }

            // An event and a managed property (W15) are edited in their dialogs and have no code of their own to go to.
            bool dialog = item.Kind == DesignCodeItemKind.Event || _entries.FirstOrDefault(e => e.Name == item.Name)?.Managed == true;
            var delete = new MenuItem { Header = "Delete", InputGestureText = "Delete", IsEnabled = !IsGenerated(item) };
            delete.Click += (_, _) => _owner.Delete(item);
            var rename = new MenuItem { Header = dialog ? "Edit..." : "Rename...", InputGestureText = "F2", IsEnabled = !IsGenerated(item) };
            rename.Click += (_, _) => _owner.Edit(item);
            var insert = new MenuItem { Header = item.Kind == DesignCodeItemKind.Event ? DesignComponentCode.CopyTriggerCode : "Insert at Cursor" };
            insert.Click += (_, _) =>
            {
                if (item.Kind == DesignCodeItemKind.Event)
                {
                    Clipboard.SetText(_owner.InsertText(item));
                }
                else
                {
                    _owner.InsertRequested?.Invoke(_owner, _owner.InsertText(item));
                }
            };
            menu.Items.Add(delete);
            menu.Items.Add(rename);
            menu.Items.Add(new Separator());
            menu.Items.Add(insert);
            if (!dialog)
            {
                var go = new MenuItem { Header = "Go to", InputGestureText = "Ctrl+D" };
                go.Click += (_, _) => _owner.GoToRequested?.Invoke(_owner, item);
                menu.Items.Add(go);
            }

            return menu;
        }
    }
}
