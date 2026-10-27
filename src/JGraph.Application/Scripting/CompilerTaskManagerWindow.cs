using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using JGraph.Controls.Themes;
using JGraph.Deploy;
using JGraph.Deploy.Projects;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Compiler Task Manager (compiler and SDK plan, stage K20): R2025b's "Manage the compiler
/// tasks in this project" window over the open project, in the words of its
/// <c>deployment:configmgr</c> and <c>compiler_ui_common</c> catalogs: the tasks in the project by
/// name and type ("Tasks in &lt;project&gt;", "No tasks detected in the current project"), Open
/// bringing up the task's app window, New Task asking a name and a target type ("Specify details
/// for the new compiler task.", "Task name:", "Target type:") and making the task in the project,
/// Rename and Remove. Every control carries an automation id.
/// </summary>
internal sealed class CompilerTaskManagerWindow : Window
{
    private readonly MatlabProject _project;
    private readonly Action<CompilerTaskConfiguration> _open;
    private readonly ListBox _list = new();
    private readonly TextBlock _note = new() { Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _name = new() { MinWidth = 220 };
    private readonly ComboBox _type = new() { MinWidth = 260 };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };

    public CompilerTaskManagerWindow(MatlabProject project, CompilerTarget? preferred, Action<CompilerTaskConfiguration> open)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(open);
        _project = project;
        _open = open;
        Title = "Compiler Task Manager";
        Width = 640;
        Height = 520;
        ShowInTaskbar = true;
        SetResourceReference(BackgroundProperty, ThemeKeys.Window);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        AutomationProperties.SetAutomationId(this, "CompilerTaskManager");

        var panel = new DockPanel { Margin = new Thickness(12) };
        var heading = new TextBlock { Text = $"Tasks in {project.Name}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetAutomationId(heading, "Heading");
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);
        AutomationProperties.SetAutomationId(_note, "Note");
        DockPanel.SetDock(_note, Dock.Top);
        panel.Children.Add(_note);

        // The new task: a name, a target type, Create.
        var create = new GroupBox { Header = "Create Compiler Task", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(8) };
        AutomationProperties.SetAutomationId(create, "CreateTask");
        var form = new StackPanel();
        form.Children.Add(new TextBlock { Text = "Specify details for the new compiler task.", Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        nameRow.Children.Add(new TextBlock { Text = "Task name:", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetAutomationId(_name, "NewTaskName");
        nameRow.Children.Add(_name);
        form.Children.Add(nameRow);
        var typeRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        typeRow.Children.Add(new TextBlock { Text = "Target type:", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetAutomationId(_type, "NewTaskType");
        foreach (CompilerTarget target in CompilerTargets.All)
        {
            var item = new ComboBoxItem { Content = CompilerTargets.TypeName(target), Tag = target };
            AutomationProperties.SetAutomationId(item, "Type." + target);
            _type.Items.Add(item);
        }

        _type.SelectedIndex = preferred is { } wanted ? CompilerTargets.All.ToList().IndexOf(wanted) : 0;
        typeRow.Children.Add(_type);
        form.Children.Add(typeRow);
        var createButton = new Button { Content = "Create", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2) };
        AutomationProperties.SetAutomationId(createButton, "Create");
        createButton.Click += (_, _) => CreateTask();
        form.Children.Add(createButton);
        AutomationProperties.SetAutomationId(_status, "Status");
        form.Children.Add(_status);
        create.Content = form;
        DockPanel.SetDock(create, Dock.Bottom);
        panel.Children.Add(create);

        // The task list and its buttons.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(ActionButton("Open", "OpenTask", OpenSelected));
        buttons.Children.Add(ActionButton("Rename…", "RenameTask", RenameSelected));
        buttons.Children.Add(ActionButton("Remove", "RemoveTask", RemoveSelected));
        buttons.Children.Add(ActionButton("Refresh", "RefreshTasks", Refresh));
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        AutomationProperties.SetAutomationId(_list, "Tasks");
        _list.MouseDoubleClick += (_, _) => OpenSelected();
        panel.Children.Add(_list);
        Content = panel;
        Refresh();
    }

    private static Button ActionButton(string text, string id, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(10, 2, 10, 2) };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>Lists the project's tasks again.</summary>
    public void Refresh()
    {
        string? selected = (_list.SelectedItem as ListBoxItem)?.Tag as string;
        _list.Items.Clear();
        IReadOnlyList<CompilerTaskConfiguration> tasks = _project.Tasks;
        foreach (CompilerTaskConfiguration task in tasks)
        {
            var text = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
            text.Children.Add(new TextBlock { Text = task.Name, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = task.Kind.CategoryTitle + (task.Target is null ? " (not built by JGraph)" : "") });
            var entry = new ListBoxItem { Content = text, Tag = task.Name };
            AutomationProperties.SetAutomationId(entry, "Task." + task.Name);
            AutomationProperties.SetName(entry, task.Name);
            _list.Items.Add(entry);
            if (task.Name == selected)
            {
                _list.SelectedItem = entry;
            }
        }

        if (_list.SelectedItem is null && _list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        _note.Text = tasks.Count == 0
            ? "No tasks detected in the current project"
            : "The following tasks exist in the open project. Select one to open it or create a new one.";
    }

    private CompilerTaskConfiguration? Selected() =>
        (_list.SelectedItem as ListBoxItem)?.Tag is string name ? _project.FindTask(name) : null;

    private void OpenSelected()
    {
        if (Selected() is not { } task)
        {
            return;
        }

        if (task.Target is null)
        {
            _status.Text = $"{task.Name} is a {task.Kind.CategoryTitle} task, a target JGraph's compiler does not build.";
            return;
        }

        _open(task);
    }

    private void CreateTask()
    {
        string name = _name.Text.Trim();
        if (_type.SelectedItem is not ComboBoxItem { Tag: CompilerTarget target })
        {
            return;
        }

        try
        {
            CompilerTaskConfiguration.CheckName(name);
            if (_project.FindTask(name) is not null)
            {
                _status.Text = $"The name {name} is already taken";
                return;
            }

            CompilerTaskConfiguration task = _project.GetOrCreateTask(CompilerTaskKinds.TypeOf(target), name);
            _status.Text = $"A new {CompilerTargets.TypeName(target)} task has been created that lets you manage the packaging process. Create additional tasks as needed to deploy to multiple targets.";
            _name.Text = "";
            Refresh();
            Select(task.Name);
            _open(task);
        }
        catch (ProjectException ex)
        {
            _status.Text = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = ex.Message;
        }
    }

    private void Select(string name)
    {
        foreach (ListBoxItem item in _list.Items)
        {
            if ((item.Tag as string) == name)
            {
                _list.SelectedItem = item;
            }
        }
    }

    private void RenameSelected()
    {
        if (Selected() is not { } task)
        {
            return;
        }

        string? newName = NameDialog.Ask(this, $"Enter a name for the new {task.Kind.CategoryTitle} task", task.Name);
        if (newName is null || newName == task.Name)
        {
            return;
        }

        try
        {
            _project.RenameTask(task.Name, newName);
            _status.Text = "";
            Refresh();
            Select(newName);
        }
        catch (ProjectException ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void RemoveSelected()
    {
        if (Selected() is not { } task)
        {
            return;
        }

        try
        {
            _project.RemoveTask(task.Name);
            _status.Text = "";
            Refresh();
        }
        catch (ProjectException ex)
        {
            _status.Text = ex.Message;
        }
    }

    /// <summary>A small dialog asking for a name, with its box and buttons under automation ids.</summary>
    private sealed class NameDialog : Window
    {
        private readonly TextBox _box = new() { MinWidth = 260 };

        private NameDialog(Window owner, string prompt, string initial)
        {
            Owner = owner;
            Title = "Task Name";
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            SetResourceReference(BackgroundProperty, ThemeKeys.Window);
            SetResourceReference(ForegroundProperty, ThemeKeys.Text);
            AutomationProperties.SetAutomationId(this, "TaskNameDialog");
            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 6) });
            AutomationProperties.SetAutomationId(_box, "TaskName");
            _box.Text = initial;
            panel.Children.Add(_box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 0, 6, 0) };
            AutomationProperties.SetAutomationId(ok, "OK");
            ok.Click += (_, _) => { DialogResult = true; Close(); };
            var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 2, 12, 2) };
            AutomationProperties.SetAutomationId(cancel, "Cancel");
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
            _box.Focus();
            _box.SelectAll();
        }

        public static string? Ask(Window owner, string prompt, string initial)
        {
            var dialog = new NameDialog(owner, prompt, initial);
            return dialog.ShowDialog() == true ? dialog._box.Text.Trim() : null;
        }
    }
}
