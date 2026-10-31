using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <content>
/// The app's own row in the inspector (App Designer window plan W8a, <c>InspectableAppNodeController.getPropertySheet</c>): Sharing
/// Details and Code Options, each group's kebab opening App Details at that section (<c>InspectableAppKabob</c>).
/// </content>
public sealed partial class DesignInspectorView
{
    private readonly List<Action> _appRows = [];

    /// <summary>Raised when a kebab of the app's rows asks for App Details; the argument says whether at Code Options.</summary>
    public event EventHandler<bool>? AppDetailsAsked;

    /// <summary>The input arguments changed from the app's row, after which App Designer goes to the startup function.</summary>
    public event EventHandler? InputArgumentsEdited;

    private void BuildAppRows(DesignDocument document)
    {
        _appRows.Clear();
        _groupPanel.Children.Add(AppGroup(DesignAppDetails.SharingDetailsTitle, false,
        [
            TextRow(document, "Name", "App name", DesignAppDetails.NameLimit),
            TextRow(document, "Version", "App version", DesignAppDetails.NameLimit),
            TextRow(document, "Author", "App author", document.DetailsAsPlainText ? DesignAppDetails.NameLimit : DesignAppDetails.MlappAuthorLimit),
            TextRow(document, "Summary", "Purpose of the app", DesignAppDetails.SummaryLimit),
            TextRow(document, "Description", "App overview and details", DesignAppDetails.DescriptionLimit),
        ]));
        _groupPanel.Children.Add(AppGroup(DesignAppDetails.CodeOptionsTitle, true, [SingleRow(document), ArgumentsRow(document)]));
    }

    private Expander AppGroup(string title, bool codeOptions, IEnumerable<(FrameworkElement Label, FrameworkElement Control)> rows)
    {
        Grid grid = NewRowGrid();
        foreach ((FrameworkElement label, FrameworkElement control) in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int at = grid.RowDefinitions.Count - 1;
            Grid.SetRow(label, at);
            Grid.SetRow(control, at);
            Grid.SetColumn(control, 1);
            grid.Children.Add(label);
            grid.Children.Add(control);
        }

        var body = new StackPanel { Margin = new Thickness(2, 2, 2, 6) };
        Grid.SetIsSharedSizeScope(body, true);
        var kebab = new Button { Content = "⋮", Width = 20, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = DesignAppDetails.Title };
        Identify(kebab, "Inspector.App.Kebab." + (codeOptions ? "CodeOptions" : "SharingDetails"), title + " " + DesignAppDetails.Title);
        kebab.Click += (_, _) => AppDetailsAsked?.Invoke(this, codeOptions);
        body.Children.Add(kebab);
        body.Children.Add(grid);
        var expander = new Expander { Header = title, IsExpanded = true, Content = body };
        AutomationProperties.SetName(expander, title);
        return expander;
    }

    private (FrameworkElement Label, FrameworkElement Control) TextRow(DesignDocument document, string name, string tip, int limit)
    {
        bool lines = name is "Summary" or "Description";
        var box = new TextBox { MaxLength = limit, ToolTip = tip, Margin = new Thickness(0, 1, 0, 1), AcceptsReturn = lines, TextWrapping = lines ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (lines)
        {
            box.MinHeight = 40;
            box.MaxHeight = 120;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }

        string Shown() => name switch
        {
            "Name" => document.AppDetailsValues.Name,
            "Version" => document.AppDetailsValues.Version,
            "Author" => document.AppDetailsValues.Author,
            "Summary" => document.AppDetailsValues.Summary,
            _ => document.AppDetailsValues.Description,
        };

        Identify(box, "Inspector.App." + name, name);
        box.Text = Shown();
        _appRows.Add(() =>
        {
            if (!box.IsKeyboardFocusWithin)
            {
                box.Text = Shown();
            }
        });
        Commits(box, Shown, text =>
        {
            if (document.SetAppDetail(name, text.Replace("\r\n", "\n", StringComparison.Ordinal)) is { } why)
            {
                Say(why);
                box.Text = Shown();
            }
        });
        return (RowLabel(name, tip), box);
    }

    private (FrameworkElement Label, FrameworkElement Control) SingleRow(DesignDocument document)
    {
        const string tip = "App allows only one running instance at a time";
        var check = new CheckBox { IsChecked = document.SingleInstance, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
        Identify(check, "Inspector.App.SingletonMode", "Single Running Instance");
        _appRows.Add(() => check.IsChecked = document.SingleInstance);
        check.Checked += (_, _) => document.SetSingleInstance(true);
        check.Unchecked += (_, _) => document.SetSingleInstance(false);
        return (RowLabel("Single Running Instance", tip), check);
    }

    private (FrameworkElement Label, FrameworkElement Control) ArgumentsRow(DesignDocument document)
    {
        const string tip = "Input arguments for the startupFcn callback";
        var box = new TextBox { Text = document.InputParameters, ToolTip = tip, Margin = new Thickness(0, 1, 0, 1) };
        Identify(box, "Inspector.App.InputArguments", "Input Arguments");
        _appRows.Add(() =>
        {
            if (!box.IsKeyboardFocusWithin)
            {
                box.Text = document.InputParameters;
            }
        });
        Commits(box, () => document.InputParameters, text =>
        {
            string was = document.InputParameters;
            if (document.SetInputParameters(text) is { } why)
            {
                Say(why);
                box.Text = document.InputParameters;
            }
            else if (document.InputParameters != was)
            {
                InputArgumentsEdited?.Invoke(this, EventArgs.Empty);
            }
        });
        return (RowLabel("Input Arguments", tip), box);
    }

    private static TextBlock RowLabel(string text, string tip) =>
        new() { Text = text, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
}
