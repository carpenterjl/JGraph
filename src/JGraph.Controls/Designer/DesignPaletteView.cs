using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// The visual designer's component library (designer plan DS3): App Designer's sections, each with
/// its entries. A click on an entry chooses it for the canvas's click and draw, and a second click
/// lets it go, as App Designer's library does; an entry dragged onto the canvas is added where it is
/// dropped. An entry the designer does not make yet is shown and cannot be chosen.
/// </summary>
public sealed class DesignPaletteView : UserControl
{
    /// <summary>The drag-and-drop format an entry is dragged in: its name.</summary>
    public const string EntryFormat = "JGraph.DesignPaletteEntry";

    private readonly Dictionary<DesignPaletteEntry, Button> _buttons = new(ReferenceEqualityComparer.Instance);
    private DesignPaletteEntry? _chosen;
    private Point? _pressAt;

    public DesignPaletteView()
        : this(DesignPalette.R2025b)
    {
    }

    public DesignPaletteView(DesignPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        Palette = palette;
        var stack = new StackPanel();
        foreach (string category in DesignPalette.Categories)
        {
            var entries = new WrapPanel { Orientation = Orientation.Vertical, Margin = new Thickness(4, 0, 0, 4) };
            foreach (DesignPaletteEntry entry in palette.Entries.Where(e => e.Category == category))
            {
                var button = new Button
                {
                    Content = entry.Name,
                    Tag = entry,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 1, 0, 1),
                    ToolTip = entry.NotYet ?? $"{entry.Name}: click, then click or drag on the canvas; or drag it there",
                    IsEnabled = entry.NotYet is null,
                    BorderThickness = new Thickness(1),
                };
                AutomationProperties(button, entry);
                button.Click += (_, _) => Choose(ReferenceEquals(_chosen, entry) ? null : entry);
                button.PreviewMouseLeftButtonDown += (_, e) => _pressAt = e.GetPosition(this);
                button.PreviewMouseMove += (_, e) => DragOut(button, entry, e);
                _buttons[entry] = button;
                entries.Children.Add(button);
            }

            stack.Children.Add(new Expander { Header = category, IsExpanded = true, Content = entries });
        }

        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        Content = scroll;
        Paint();
    }

    /// <summary>Raised when the entry chosen for click and draw changed: an entry, or null when none is.</summary>
    public event EventHandler<DesignPaletteEntry?>? ChosenChanged;

    /// <summary>The library shown.</summary>
    public DesignPalette Palette { get; }

    /// <summary>The entry chosen for the canvas's click and draw; null when none.</summary>
    public DesignPaletteEntry? Chosen
    {
        get => _chosen;
        set
        {
            if (!ReferenceEquals(value, _chosen))
            {
                _chosen = value;
                Paint();
            }
        }
    }

    private void Choose(DesignPaletteEntry? entry)
    {
        Chosen = entry;
        ChosenChanged?.Invoke(this, entry);
    }

    /// <summary>A press dragged far enough carries the entry out to the canvas.</summary>
    private void DragOut(Button button, DesignPaletteEntry entry, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressAt is not { } from || !button.IsEnabled)
        {
            return;
        }

        Vector moved = e.GetPosition(this) - from;
        if (System.Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && System.Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _pressAt = null;
        Choose(null);
        DragDrop.DoDragDrop(button, new DataObject(EntryFormat, entry.Name), DragDropEffects.Copy);
    }

    /// <summary>The chosen entry drawn as pressed.</summary>
    private void Paint()
    {
        foreach ((DesignPaletteEntry entry, Button button) in _buttons)
        {
            if (ReferenceEquals(entry, _chosen))
            {
                button.SetResourceReference(BorderBrushProperty, ThemeKeys.Accent);
                button.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                button.ClearValue(BorderBrushProperty);
                button.FontWeight = FontWeights.Normal;
            }
        }
    }

    private static void AutomationProperties(Button button, DesignPaletteEntry entry)
    {
        System.Windows.Automation.AutomationProperties.SetName(button, entry.Name);
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "Palette." + entry.Name);
    }
}
