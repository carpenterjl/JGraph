using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JGraph.Core.Model;
using JGraph.Scripting;

namespace JGraph.Controls;

/// <summary>
/// A figure's own menu bar and toolbars (app-building plan, U8): the top-level <c>uimenu</c>s of a
/// figure as a WPF <see cref="Menu"/>, and each <c>uitoolbar</c> as a row of its tools. Like the
/// component layer it is fed only frames and never reads the model; a pick goes back through the
/// script queue. The bars are rebuilt only when a frame's menus or tools differ from what is
/// showing, so a script that redraws a label ten times a second does not close a menu a person
/// has open.
/// </summary>
public sealed class UiFigureBars : StackPanel
{
    private static readonly ResourceDictionary Styles = new()
    {
        Source = new Uri("pack://application:,,,/JGraph.Controls;component/Themes/UiComponents.xaml"),
    };

    private readonly Menu _menu = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _toolbars = new();
    private readonly Dictionary<UiToolModel, long> _pending = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<UiToolModel, ButtonBase> _tools = new(ReferenceEqualityComparer.Instance);
    private readonly List<(Key Key, MenuItemModel Item)> _accelerators = [];
    private string _menuSignature = string.Empty;
    private string _toolSignature = string.Empty;
    private bool _applying;

    public UiFigureBars()
    {
        AutomationProperties.SetAutomationId(_menu, "JG.MenuBar");
        AutomationProperties.SetAutomationId(_toolbars, "JG.ToolBars");
        Children.Add(_menu);
        Children.Add(_toolbars);
    }

    /// <summary>Shows a frame's menu bar and toolbars.</summary>
    public void Apply(UiFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        string menus = MenuSignature(frame.Menus);
        if (menus != _menuSignature)
        {
            _menuSignature = menus;
            _accelerators.Clear();
            _menu.Items.Clear();
            foreach (UiMenuFrame top in frame.Menus)
            {
                _menu.Items.Add(Build(top));
            }

            _menu.Visibility = frame.Menus.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        string tools = ToolSignature(frame.Toolbars);
        if (tools != _toolSignature)
        {
            _toolSignature = tools;
            _toolbars.Children.Clear();
            _tools.Clear();
            foreach (UiToolbarFrame bar in frame.Toolbars)
            {
                _toolbars.Children.Add(Build(bar));
            }
        }

        // A toggle tool's State: the frame's, unless a press of it is still on its way there.
        _applying = true;
        try
        {
            foreach (UiToolFrame tool in frame.Toolbars.SelectMany(static bar => bar.Tools))
            {
                if (_tools.TryGetValue(tool.Source, out ButtonBase? button) && button is ToggleButton toggle)
                {
                    bool waiting = _pending.TryGetValue(tool.Source, out long seq) && tool.UserWriteSeq < seq;
                    if (!waiting)
                    {
                        _pending.Remove(tool.Source);
                        toggle.IsChecked = tool.State;
                    }
                }
            }
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Takes down everything: the window is to show another figure.</summary>
    public void Clear()
    {
        _menu.Items.Clear();
        _menu.Visibility = Visibility.Collapsed;
        _toolbars.Children.Clear();
        _tools.Clear();
        _pending.Clear();
        _accelerators.Clear();
        _menuSignature = string.Empty;
        _toolSignature = string.Empty;
    }

    /// <summary>
    /// Runs the menu entry whose <c>Accelerator</c> is this key, pressed with Ctrl. Answers whether
    /// there was one.
    /// </summary>
    public bool TryAccelerator(Key key)
    {
        foreach ((Key bound, MenuItemModel item) in _accelerators)
        {
            if (bound == key)
            {
                ScriptGraphicsCallbacks.NotifyMenuSelected(item);
                return true;
            }
        }

        return false;
    }

    // --- menus -----------------------------------------------------------------------------------

    private static string MenuSignature(IReadOnlyList<UiMenuFrame> menus)
    {
        var text = new StringBuilder();
        void Walk(IReadOnlyList<UiMenuFrame> items)
        {
            foreach (UiMenuFrame item in items)
            {
                text.Append(item.Source.Id).Append('|').Append(item.Text).Append('|').Append(item.Checked ? 'c' : '-')
                    .Append(item.Enabled ? 'e' : '-').Append(item.Separator ? 's' : '-').Append(item.Accelerator).Append('|')
                    .Append(item.Tooltip).Append('|').Append(item.Foreground).Append('|').Append(item.Tag).Append('{');
                Walk(item.Items);
                text.Append('}');
            }
        }

        Walk(menus);
        return text.ToString();
    }

    /// <summary>MATLAB marks a menu's access key with an ampersand; WPF, with an underscore.</summary>
    private static string Header(string text)
    {
        var header = new StringBuilder(text.Length + 2);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '_')
            {
                header.Append("__");
            }
            else if (c == '&' && i + 1 < text.Length && text[i + 1] == '&')
            {
                header.Append('&');
                i++;
            }
            else if (c == '&')
            {
                header.Append('_');
            }
            else
            {
                header.Append(c);
            }
        }

        return header.ToString();
    }

    private MenuItem Build(UiMenuFrame frame)
    {
        var item = new MenuItem
        {
            Header = Header(frame.Text),
            IsChecked = frame.Checked,
            IsEnabled = frame.Enabled,
            ToolTip = frame.Tooltip.Length > 0 ? frame.Tooltip : null,
            Foreground = new SolidColorBrush(Color.FromRgb(frame.Foreground.R, frame.Foreground.G, frame.Foreground.B)),
        };
        AutomationProperties.SetAutomationId(item, frame.Tag);
        AutomationProperties.SetName(item, frame.Text.Replace("&", string.Empty, StringComparison.Ordinal));
        MenuItemModel source = frame.Source;
        if (frame.Accelerator.Length == 1)
        {
            item.InputGestureText = "Ctrl+" + char.ToUpperInvariant(frame.Accelerator[0]);
            if (new KeyConverter().ConvertFromInvariantString(frame.Accelerator.ToUpperInvariant()) is Key key && key != Key.None)
            {
                _accelerators.Add((key, source));
            }
        }

        if (frame.Items.Count == 0)
        {
            item.Click += (_, _) => ScriptGraphicsCallbacks.NotifyMenuSelected(source);
            return item;
        }

        // A menu that holds others runs its own callback as it opens, which is where a script
        // readies its entries.
        item.SubmenuOpened += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, item))
            {
                ScriptGraphicsCallbacks.NotifyMenuSelected(source);
            }
        };
        foreach (UiMenuFrame child in frame.Items)
        {
            if (child.Separator && item.Items.Count > 0)
            {
                item.Items.Add(new Separator());
            }

            item.Items.Add(Build(child));
        }

        return item;
    }

    // --- toolbars --------------------------------------------------------------------------------

    private static string ToolSignature(IReadOnlyList<UiToolbarFrame> bars)
    {
        var text = new StringBuilder();
        foreach (UiToolbarFrame bar in bars)
        {
            text.Append(bar.Source.Id).Append('|').Append(bar.Background).Append('|').Append(bar.Tag).Append('[');
            foreach (UiToolFrame tool in bar.Tools)
            {
                text.Append(tool.Source.Id).Append('|').Append(tool.IsToggle ? 't' : 'p').Append(tool.Enabled ? 'e' : '-')
                    .Append(tool.Separator ? 's' : '-').Append(tool.Tooltip).Append('|').Append(tool.Tag).Append('|')
                    .Append(tool.Picture is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(tool.Picture)).Append(';');
            }

            text.Append(']');
        }

        return text.ToString();
    }

    private Border Build(UiToolbarFrame bar)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new System.Windows.Thickness(2, 1, 2, 1) };
        foreach (UiToolFrame tool in bar.Tools)
        {
            if (tool.Separator && row.Children.Count > 0)
            {
                row.Children.Add(new Border
                {
                    Width = 1,
                    Margin = new System.Windows.Thickness(3, 2, 3, 2),
                    Background = new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xB4)),
                });
            }

            row.Children.Add(Build(tool));
        }

        var face = new Border
        {
            Child = row,
            Background = new SolidColorBrush(Color.FromScRgb(1, (float)bar.Background.R, (float)bar.Background.G, (float)bar.Background.B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new System.Windows.Thickness(0, 0, 0, 1),
            MinHeight = 26,
        };
        AutomationProperties.SetAutomationId(face, bar.Tag);
        return face;
    }

    private ButtonBase Build(UiToolFrame tool)
    {
        ButtonBase button = tool.IsToggle
            ? new ToggleButton { Style = (Style)Styles["JG.Ui.ToggleButton"], IsChecked = tool.State }
            : new Button { Style = (Style)Styles["JG.Ui.PushButton"] };
        button.Width = 24;
        button.Height = 24;
        button.Margin = new System.Windows.Thickness(1, 0, 1, 0);
        button.Padding = new System.Windows.Thickness(0);
        button.BorderThickness = new System.Windows.Thickness(tool.IsToggle ? 1 : 0);
        button.Background = Brushes.Transparent;
        button.Focusable = false;
        button.IsEnabled = tool.Enabled;
        button.Opacity = tool.Enabled ? 1 : 0.5;
        button.ToolTip = tool.Tooltip.Length > 0 ? tool.Tooltip : null;
        AutomationProperties.SetAutomationId(button, tool.Tag);
        AutomationProperties.SetName(button, tool.Tooltip);
        if (tool.Picture is { } picture)
        {
            var source = BitmapSource.Create(picture.Width, picture.Height, 96, 96, PixelFormats.Bgra32, null, picture.Bgra, picture.Width * 4);
            source.Freeze();
            button.Content = new Image { Source = source, Stretch = Stretch.Uniform, Width = 16, Height = 16, SnapsToDevicePixels = true };
        }

        UiToolModel model = tool.Source;
        _tools[model] = button;
        if (button is ToggleButton toggle)
        {
            // Heard as it changes, not as it is clicked, so that UI Automation's Toggle counts too.
            void Flipped(object? sender, RoutedEventArgs e)
            {
                if (!_applying)
                {
                    _pending[model] = ScriptGraphicsCallbacks.NotifyComponent(model, "pushed");
                }
            }

            toggle.Checked += Flipped;
            toggle.Unchecked += Flipped;
        }
        else
        {
            button.Click += (_, _) => ScriptGraphicsCallbacks.NotifyComponent(model, "pushed");
        }

        return button;
    }
}
