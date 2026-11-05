using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <content>
/// A custom UI component's own rows in the inspector (App Designer window plan W15, R2025b's
/// <c>InspectableUserComponentNodeController</c>): its public managed properties under the class's name, each
/// edited as its default value, then the Testing group's "Give Test Cases Access". The root's inherited
/// properties (BackgroundColor, Visible, Interruptible, BusyAction, HandleVisibility) are written in the
/// code's <c>setup</c>.
/// </content>
public sealed partial class DesignInspectorView
{
    /// <summary>The Testing group's title and its one row (<c>allowTestCaseAccess</c>).</summary>
    public const string TestingTitle = "Testing";

    /// <summary>The Testing group's check box text.</summary>
    public const string TestCaseAccessText = "Give Test Cases Access";

    private void BuildComponentRows(DesignDocument document)
    {
        _appRows.Clear();
        string title = document.Code?.ClassName ?? document.AppName;
        var rows = new List<(FrameworkElement Label, FrameworkElement Control)>();
        foreach (DesignManagedProperty property in document.ManagedProperties.Where(static p => p.IsPublic))
        {
            rows.Add(ManagedRow(document, property));
        }

        if (rows.Count == 0)
        {
            var none = new TextBlock { Text = DesignComponentCode.PublicPropertyDescription, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 2) };
            Identify(none, "Inspector.Component.NoPublicProperties", DesignComponentCode.PublicPropertyDescription);
            rows.Add((new TextBlock(), none));
        }

        _groupPanel.Children.Add(PlainGroup(title, rows));
        _groupPanel.Children.Add(PlainGroup(TestingTitle, [TestCaseAccessRow(document)]));
    }

    private static Expander PlainGroup(string title, IEnumerable<(FrameworkElement Label, FrameworkElement Control)> rows)
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
        body.Children.Add(grid);
        var expander = new Expander { Header = title, IsExpanded = true, Content = body };
        AutomationProperties.SetName(expander, title);
        return expander;
    }

    /// <summary>A public managed property's row: its default value, typed as MATLAB code and checked against the declaration (<c>validateInspectorPropertyValue</c>); a logical one is a check box.</summary>
    private (FrameworkElement Label, FrameworkElement Control) ManagedRow(DesignDocument document, DesignManagedProperty property)
    {
        string tip = property.Description.Length == 0 ? property.Declaration : property.Description;
        DesignManagedCheck check = document.Runtime.ValidateManagedProperty(property);
        if (check.Editor == DesignManagedEditor.Logical)
        {
            var box = new CheckBox { IsChecked = IsTrue(property.DefaultValue), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            Identify(box, "Inspector.Component." + property.Name, property.Name);
            _appRows.Add(() => box.IsChecked = IsTrue(document.ManagedProperties.FirstOrDefault(p => p.Name == property.Name)?.DefaultValue ?? property.DefaultValue));
            box.Click += (_, _) =>
            {
                if (document.ManagedProperties.FirstOrDefault(p => p.Name == property.Name) is { } now
                    && document.SetManagedDefault(now, box.IsChecked == true ? "true" : "false") is { } why)
                {
                    Say(why);
                    box.IsChecked = IsTrue(now.DefaultValue);
                }
            };
            return (RowLabel(property.Name, tip), box);
        }

        var text = new TextBox { Text = property.DefaultValue, ToolTip = tip, Margin = new Thickness(0, 1, 0, 1) };
        Identify(text, "Inspector.Component." + property.Name, property.Name);
        string Shown() => document.ManagedProperties.FirstOrDefault(p => p.Name == property.Name)?.DefaultValue ?? property.DefaultValue;
        _appRows.Add(() =>
        {
            if (!text.IsKeyboardFocusWithin)
            {
                text.Text = Shown();
            }
        });
        Commits(text, Shown, typed =>
        {
            if (document.ManagedProperties.FirstOrDefault(p => p.Name == property.Name) is not { } now)
            {
                return;
            }

            if (document.SetManagedDefault(now, typed) is { } why)
            {
                Say(why);
            }

            text.Text = Shown();
        });
        return (RowLabel(property.Name, tip), text);
    }

    private (FrameworkElement Label, FrameworkElement Control) TestCaseAccessRow(DesignDocument document)
    {
        const string tip = "Declare the underlying components with GetAccess = ?matlab.unittest.TestCase, so that tests can read them";
        var check = new CheckBox { IsChecked = document.AllowTestCaseAccess, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
        Identify(check, "Inspector.Component.AllowTestCaseAccess", TestCaseAccessText);
        _appRows.Add(() => check.IsChecked = document.AllowTestCaseAccess);
        check.Click += (_, _) =>
        {
            if (document.SetAllowTestCaseAccess(check.IsChecked == true) is { } why)
            {
                Say(why);
                check.IsChecked = document.AllowTestCaseAccess;
            }
        };
        return (RowLabel(TestCaseAccessText, tip), check);
    }

    private static bool IsTrue(string text) => text.Trim() is "true" or "1" or "logical(1)";
}
