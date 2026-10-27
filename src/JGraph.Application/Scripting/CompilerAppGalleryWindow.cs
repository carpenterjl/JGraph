using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using JGraph.Controls.Themes;
using JGraph.Deploy;

namespace JGraph.Application.Scripting;

/// <summary>
/// The gallery of compiler apps (compiler and SDK plan, stage K19): what <c>deploytool</c> and
/// <c>libraryCompiler</c> open with no argument, R2025b's dialog for selecting a compiler app to
/// begin a task in, one entry per target with its name and description from the
/// <c>deployment:desktop</c> and <c>deployment:model</c> catalogs.
/// </summary>
internal sealed class CompilerAppGalleryWindow : Window
{
    private readonly ListBox _list = new();

    public CompilerAppGalleryWindow(Action<CompilerTarget> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        Title = "MATLAB Compiler";
        Width = 560;
        Height = 420;
        SetResourceReference(BackgroundProperty, ThemeKeys.Window);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        AutomationProperties.SetAutomationId(this, "CompilerAppGallery");

        var panel = new DockPanel { Margin = new Thickness(12) };
        var heading = new TextBlock { Text = "Select a compiler app to begin a new compiler task in.", Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);

        var openButton = new Button { Content = "Open", Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), IsDefault = true };
        AutomationProperties.SetAutomationId(openButton, "OpenApp");
        openButton.Click += (_, _) => OpenSelected();
        DockPanel.SetDock(openButton, Dock.Bottom);
        panel.Children.Add(openButton);

        AutomationProperties.SetAutomationId(_list, "Apps");
        foreach (CompilerTarget target in CompilerTargets.All)
        {
            var item = new StackPanel { Margin = new Thickness(4, 4, 4, 4) };
            item.Children.Add(new TextBlock { Text = CompilerTargets.AppTitle(target), FontWeight = FontWeights.SemiBold });
            item.Children.Add(new TextBlock { Text = CompilerTargets.Description(target) + " (" + CompilerTargets.Command(target) + ")", TextWrapping = TextWrapping.Wrap });
            var entry = new ListBoxItem { Content = item, Tag = target };
            AutomationProperties.SetAutomationId(entry, "App." + target);
            AutomationProperties.SetName(entry, CompilerTargets.AppTitle(target));
            entry.MouseDoubleClick += (_, _) => OpenSelected();
            _list.Items.Add(entry);
        }

        _list.SelectedIndex = 0;
        panel.Children.Add(_list);
        Content = panel;
        Opener = open;
        InputBindings.Add(new KeyBinding(new RoutedCommand(), Key.Escape, ModifierKeys.None));
    }

    private Action<CompilerTarget> Opener { get; }

    private void OpenSelected()
    {
        if (_list.SelectedItem is ListBoxItem { Tag: CompilerTarget target })
        {
            Opener(target);
            Close();
        }
    }
}
