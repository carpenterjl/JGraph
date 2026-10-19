using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// The popup a grid being configured shows for its picked row or column (designer plan DS4, App
/// Designer's <c>GridConfigPopupWidget</c> and <c>GridConfigWidget</c>): buttons that insert a row or
/// column either side of it and delete it, and its kind - Fit, Weighted with a weight, Fixed with
/// pixels. Every rule is <see cref="DesignGridConfigure"/>'s; this only shows it.
/// </summary>
internal sealed class DesignGridConfigPopup
{
    private readonly Popup _popup;
    private readonly Button _before;
    private readonly Button _after;
    private readonly Button _delete;
    private readonly RadioButton _fit;
    private readonly RadioButton _weighted;
    private readonly RadioButton _fixed;
    private readonly TextBox _weight;
    private readonly TextBox _pixels;
    private DesignGridConfigure? _configure;
    private DesignGestures? _gestures;
    private bool _showing;

    public DesignGridConfigPopup(UIElement target)
    {
        _before = Tool("GridConfig.InsertBefore");
        _after = Tool("GridConfig.InsertAfter");
        _delete = Tool("GridConfig.Delete");
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        tools.Children.Add(_before);
        tools.Children.Add(_after);
        tools.Children.Add(_delete);

        _fit = Radio("Fit", "GridConfig.Fit");
        _weighted = Radio("Weighted", "GridConfig.Weighted");
        _fixed = Radio("Fixed", "GridConfig.Fixed");
        _weight = Number("GridConfig.Weight");
        _pixels = Number("GridConfig.Pixels");
        var table = new Grid();
        for (int i = 0; i < 3; i++)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        }

        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Put(table, _fit, 0, 0);
        Put(table, _weighted, 1, 0);
        Put(table, _weight, 1, 1);
        Put(table, new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }, 1, 2);
        Put(table, _fixed, 2, 0);
        Put(table, _pixels, 2, 1);
        Put(table, new TextBlock { Text = "Pixel", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }, 2, 2);

        var content = new StackPanel { Margin = new Thickness(8) };
        content.Children.Add(tools);
        content.Children.Add(new Separator());
        content.Children.Add(table);
        var border = new Border
        {
            Child = content,
            BorderThickness = new Thickness(1),
            BorderBrush = SystemColors.ActiveBorderBrush,
            Background = SystemColors.WindowBrush,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(border, "GridConfig");
        border.PreviewKeyDown += (_, e) =>
        {
            // Delete or Backspace with the popup itself focused deletes the track (GridConfigPopupWidget).
            if (e.Key is Key.Delete or Key.Back && e.OriginalSource is not (TextBox or RadioButton))
            {
                Delete();
                e.Handled = true;
            }
        };
        _popup = new Popup
        {
            Child = border,
            PlacementTarget = target,
            Placement = PlacementMode.Relative,
            StaysOpen = true,
            AllowsTransparency = false,
        };

        _before.Click += (_, _) => _configure?.InsertBefore();
        _after.Click += (_, _) => _configure?.InsertAfter();
        _delete.Click += (_, _) => Delete();
        _fit.Checked += (_, _) => Choose(DesignTrackKind.Fit);
        _weighted.Checked += (_, _) => Choose(DesignTrackKind.Weighted);
        _fixed.Checked += (_, _) => Choose(DesignTrackKind.Fixed);
        Commits(_weight, value => _configure?.SetWeight(value));
        Commits(_pixels, value => _configure?.SetPixels(value));
        Hints(_before, insert: true, after: false);
        Hints(_after, insert: true, after: true);
        Hints(_delete, insert: false, after: false);
    }

    /// <summary>What went wrong with a value typed.</summary>
    public event EventHandler<string>? Problem;

    /// <summary>Shows the popup for <paramref name="gestures"/>'s grid being configured, at its pick; hides it when nothing is picked.</summary>
    public void Show(DesignGestures? gestures, double offsetX, double offsetY)
    {
        _gestures = gestures;
        _configure = gestures?.Configuring;
        if (_configure is not { Selected: { } track } configure)
        {
            _popup.IsOpen = false;
            return;
        }

        _showing = true;
        bool row = track.Axis == GridAxis.Row;
        _before.Content = row ? "Insert row above" : "Insert column on left";
        _after.Content = row ? "Insert row below" : "Insert column on right";
        _delete.Content = row ? "Delete row" : "Delete column";
        _fit.IsChecked = configure.Kind == DesignTrackKind.Fit;
        _weighted.IsChecked = configure.Kind == DesignTrackKind.Weighted;
        _fixed.IsChecked = configure.Kind == DesignTrackKind.Fixed;
        _weight.IsEnabled = configure.WeightEnabled;
        _pixels.IsEnabled = configure.PixelsEnabled;
        if (!_weight.IsKeyboardFocusWithin)
        {
            _weight.Text = configure.Weight.ToString("R", CultureInfo.InvariantCulture);
            _weight.Tag = _weight.Text;
        }

        if (!_pixels.IsKeyboardFocusWithin)
        {
            _pixels.Text = configure.Pixels.ToString("R", CultureInfo.InvariantCulture);
            _pixels.Tag = _pixels.Text;
        }

        _showing = false;

        // App Designer opens it 150 pixels above the pick.
        _popup.HorizontalOffset = offsetX + configure.PickedAt.X;
        _popup.VerticalOffset = System.Math.Max(0, offsetY + configure.PickedAt.Y - 150);
        _popup.IsOpen = true;
    }

    /// <summary>Hides the popup.</summary>
    public void Hide() => _popup.IsOpen = false;

    private void Choose(DesignTrackKind kind)
    {
        if (!_showing && _configure?.Choose(kind) is { } refused)
        {
            Problem?.Invoke(this, refused);
        }
    }

    private void Delete()
    {
        if (_configure?.DeleteTrack() == true)
        {
            _gestures?.EndConfigure();
        }
    }

    private void Commits(TextBox box, Func<double, string?> set)
    {
        void Commit()
        {
            // Only a value typed is an edit: focus coming and going is not.
            if (_showing || _configure is null || box.Text == (string?)box.Tag)
            {
                return;
            }

            // The spinner's own rule: a number, finite, within its limits (mw-form/Spinner).
            if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                Problem?.Invoke(this, $"'{box.Text}' is not a number.");
                return;
            }

            if (set(value) is { } refused)
            {
                Problem?.Invoke(this, refused);
            }
            else
            {
                box.Tag = box.Text;
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Commit();
    }

    private void Hints(Button button, bool insert, bool after)
    {
        button.MouseEnter += (_, _) => _gestures?.ShowHint(_configure?.HintFor(insert, after));
        button.MouseLeave += (_, _) => _gestures?.ShowHint(null);
    }

    private static Button Tool(string id)
    {
        var button = new Button { Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(4, 1, 4, 1) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, id);
        return button;
    }

    private static RadioButton Radio(string text, string id)
    {
        var radio = new RadioButton { Content = text, GroupName = "GridConfigKind", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(radio, id);
        return radio;
    }

    private static TextBox Number(string id)
    {
        var box = new TextBox { VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, id);
        return box;
    }

    private static void Put(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
