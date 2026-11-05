using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// The properties panel's third tab for a custom UI component (App Designer window plan W15; R2025b's
/// Event–Callback Pairs tab): each event with the public callback property it makes and its description,
/// with Add, Edit, Delete and Copy Code to Trigger Event. The dialogs are the window's; this raises for them.
/// </summary>
public sealed class DesignEventPairsView : UserControl
{
    /// <summary>The tab's title.</summary>
    public const string Title = "Event–Callback Pairs";

    private readonly ListView _list = new() { BorderThickness = new Thickness(0) };
    private readonly Button _edit = new() { Content = "Edit...", Margin = new Thickness(2), MinWidth = 60 };
    private readonly Button _delete = new() { Content = "Delete", Margin = new Thickness(2), MinWidth = 60 };
    private readonly Button _copy = new() { Content = DesignComponentCode.CopyTriggerCode, Margin = new Thickness(2) };
    private DesignDocument? _document;
    private bool _queued;

    public DesignEventPairsView()
    {
        var help = new TextBlock { Text = DesignComponentCode.AddEventDescription, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
        help.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        var add = new Button { Content = "Add...", Margin = new Thickness(2), MinWidth = 60, ToolTip = DesignComponentCode.AddEventHelpText };
        AutomationProperties.SetAutomationId(add, "EventPairs.Add");
        AutomationProperties.SetAutomationId(_edit, "EventPairs.Edit");
        AutomationProperties.SetAutomationId(_delete, "EventPairs.Delete");
        AutomationProperties.SetAutomationId(_copy, "EventPairs.Copy");
        AutomationProperties.SetAutomationId(_list, "EventPairs.List");
        AutomationProperties.SetAutomationId(this, "EventPairs");
        add.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        _edit.Click += (_, _) =>
        {
            if (Selected is { } chosen)
            {
                EditRequested?.Invoke(this, chosen);
            }
        };
        _delete.Click += (_, _) =>
        {
            if (Selected is { } chosen && _document?.RemoveEvent(chosen) is { } why)
            {
                Problem?.Invoke(this, why);
            }
        };
        _copy.Click += (_, _) =>
        {
            if (Selected is { } chosen)
            {
                Clipboard.SetText(chosen.NotifyLine);
                Copied?.Invoke(this, chosen.NotifyLine);
            }
        };
        _list.MouseDoubleClick += (_, _) =>
        {
            if (Selected is { } chosen)
            {
                EditRequested?.Invoke(this, chosen);
            }
        };
        _list.SelectionChanged += (_, _) => ShowButtons();

        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Event", DisplayMemberBinding = new System.Windows.Data.Binding("Name"), Width = 110 });
        view.Columns.Add(new GridViewColumn { Header = "Public Callback", DisplayMemberBinding = new System.Windows.Data.Binding("CallbackProperty"), Width = 130 });
        view.Columns.Add(new GridViewColumn { Header = "Description", DisplayMemberBinding = new System.Windows.Data.Binding("Description"), Width = 200 });
        _list.View = view;
        _list.SetResourceReference(Control.BackgroundProperty, ThemeKeys.Panel);
        _list.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Text);

        var buttons = new WrapPanel { Margin = new Thickness(4, 0, 4, 4) };
        buttons.Children.Add(add);
        buttons.Children.Add(_edit);
        buttons.Children.Add(_delete);
        buttons.Children.Add(_copy);
        var body = new DockPanel();
        DockPanel.SetDock(help, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Top);
        body.Children.Add(help);
        body.Children.Add(buttons);
        body.Children.Add(_list);
        body.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        Content = body;
        ShowButtons();
    }

    /// <summary>Raised by Add: the window opens the Add Event dialog.</summary>
    public event EventHandler? AddRequested;

    /// <summary>Raised by Edit or a double-click with the event: the window opens its dialog.</summary>
    public event EventHandler<DesignCodeEvent>? EditRequested;

    /// <summary>Raised with the line Copy Code to Trigger Event put on the clipboard, for the status line.</summary>
    public event EventHandler<string>? Copied;

    /// <summary>Raised with what could not be done.</summary>
    public event EventHandler<string>? Problem;

    /// <summary>The component shown.</summary>
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

    /// <summary>The event picked in the list, or null.</summary>
    public DesignCodeEvent? Selected => _list.SelectedItem as DesignCodeEvent;

    /// <summary>Picks the event named <paramref name="name"/>, after a dialog made or renamed it.</summary>
    public void Select(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Show();
        _list.SelectedItem = _list.Items.OfType<DesignCodeEvent>().FirstOrDefault(e => e.Name == name);
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
        string? selected = Selected?.Name;
        _list.Items.Clear();
        if (_document is { } document)
        {
            foreach (DesignCodeEvent e in document.Events.OrderBy(static e => e.Name, StringComparer.Ordinal))
            {
                _list.Items.Add(e);
            }
        }

        if (selected is not null)
        {
            _list.SelectedItem = _list.Items.OfType<DesignCodeEvent>().FirstOrDefault(e => e.Name == selected);
        }

        ShowButtons();
    }

    private void ShowButtons()
    {
        bool picked = Selected is not null;
        _edit.IsEnabled = picked;
        _delete.IsEnabled = picked;
        _copy.IsEnabled = picked;
    }
}
