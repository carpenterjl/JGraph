using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using JGraph.Controls.Themes;

namespace JGraph.Controls.Toolstrip;

/// <summary>A spinner's text box: what UI Automation reads and sets (the Value pattern).</summary>
internal sealed class ToolstripSpinnerBox(ToolstripSpinner spinner) : TextBox
{
    public ToolstripSpinner Spinner { get; } = spinner;
}

/// <summary>
/// A number field with up and down arrows. A value typed outside its limits, or not a number, is
/// refused with the layout's error text (R2025b's "Value must be between 10 and 50") until a good
/// one is typed; the arrows step within the limits.
/// </summary>
internal sealed class ToolstripSpinner : Grid
{
    private readonly string? _errorText;
    private readonly double? _minimum;
    private readonly double? _maximum;
    private readonly double _step;
    private double _value;

    public ToolstripSpinner(string? errorText, double? minimum, double? maximum, double step, bool editable)
    {
        _errorText = errorText;
        _minimum = minimum;
        _maximum = maximum;
        _step = step > 0 ? step : 1;
        Box = new ToolstripSpinnerBox(this)
        {
            IsReadOnly = !editable,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 0, 2, 0),
        };
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition());
        SetRowSpan(Box, 2);
        Children.Add(Box);
        RepeatButton up = Arrow(1, "Increase");
        RepeatButton down = Arrow(-1, "Decrease");
        SetColumn(up, 1);
        SetColumn(down, 1);
        SetRow(down, 1);
        Children.Add(up);
        Children.Add(down);
        Box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    Commit();
                    e.Handled = true;
                    break;
                case Key.Up:
                    StepBy(1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    StepBy(-1);
                    e.Handled = true;
                    break;
            }
        };
        Box.LostKeyboardFocus += (_, _) => Commit();
    }

    /// <summary>Raised with a good value, typed or stepped to.</summary>
    public event Action<double>? Committed;

    /// <summary>The text box.</summary>
    public ToolstripSpinnerBox Box { get; }

    /// <summary>The text shown.</summary>
    public string Text
    {
        get => Box.Text;
        set
        {
            Box.Text = value;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                _value = v;
            }

            ShowError(false);
        }
    }

    /// <summary>Whether the text typed was refused.</summary>
    public bool HasError { get; private set; }

    /// <summary>Takes the typed text: a good value is kept and raised, any other shows the error text.</summary>
    public void Commit()
    {
        string text = Box.Text.Trim();
        if ((double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            && double.IsFinite(v) && (_minimum is null || v >= _minimum) && (_maximum is null || v <= _maximum))
        {
            ShowError(false);
            if (v != _value)
            {
                _value = v;
                Committed?.Invoke(v);
            }

            return;
        }

        ShowError(true);
    }

    private void StepBy(int direction)
    {
        double v = _value + direction * _step;
        v = Math.Clamp(v, _minimum ?? double.MinValue, _maximum ?? double.MaxValue);
        Box.Text = v.ToString(CultureInfo.CurrentCulture);
        Commit();
    }

    private void ShowError(bool error)
    {
        HasError = error;
        if (error)
        {
            Box.SetResourceReference(Control.BorderBrushProperty, ThemeKeys.Error);
            Box.ToolTip = _errorText;
            AutomationProperties.SetHelpText(Box, _errorText ?? "");
        }
        else
        {
            Box.SetResourceReference(Control.BorderBrushProperty, ThemeKeys.ControlBorder);
            Box.ToolTip = AutomationProperties.GetName(Box) is { Length: > 0 } name ? name : null;
            AutomationProperties.SetHelpText(Box, "");
        }
    }

    private RepeatButton Arrow(int direction, string name)
    {
        // The chevrons: "collapse" points up, "expand" down.
        var button = new RepeatButton { Focusable = false, Interval = 120, Delay = 400 };
        ToolstripChrome.Apply(button);
        button.Padding = new Thickness(0);
        button.Content = ToolstripIcons.Create(direction > 0 ? "collapse" : "expand", 10);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => StepBy(direction);
        return button;
    }
}
