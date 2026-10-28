using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using JGraph.Controls.Themes;
using JGraph.Scripting.Toolstrip;

namespace JGraph.Controls.Toolstrip;

/// <summary>The toolstrip's flat button look, built once per button class.</summary>
internal static class ToolstripChrome
{
    private static readonly Dictionary<Type, ControlTemplate> Templates = [];

    /// <summary>A rounded face, clear until the pointer is over it, shaded while pressed or checked.</summary>
    public static ControlTemplate For(Type type)
    {
        if (Templates.TryGetValue(type, out ControlTemplate? template))
        {
            return template;
        }

        var face = new FrameworkElementFactory(typeof(Border), "face");
        face.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        face.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        face.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        face.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        face.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter), "content");
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        face.AppendChild(content);
        template = new ControlTemplate(type) { VisualTree = face };

        if (typeof(ToggleButton).IsAssignableFrom(type))
        {
            template.Triggers.Add(Shade(ToggleButton.IsCheckedProperty, true, ThemeKeys.Selection, ThemeKeys.BorderStrong));
        }

        template.Triggers.Add(Shade(UIElement.IsMouseOverProperty, true, ThemeKeys.Hover, ThemeKeys.ControlBorder));
        template.Triggers.Add(Shade(ButtonBase.IsPressedProperty, true, ThemeKeys.Pressed, ThemeKeys.ControlBorder));
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeKeys.FocusRing), "face"));
        template.Triggers.Add(focused);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "content"));
        template.Triggers.Add(disabled);
        template.Seal();
        Templates[type] = template;
        return template;
    }

    /// <summary>Gives a button the toolstrip's look and its usual spacing.</summary>
    public static void Apply(Control button)
    {
        button.Template = For(button.GetType());
        button.Style = null;
        button.FocusVisualStyle = null;
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(3, 2, 3, 2);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.SetResourceReference(Control.ForegroundProperty, ThemeKeys.Text);
        button.SetResourceReference(Control.FontFamilyProperty, ThemeKeys.FontUI);
        button.FontSize = 11;
    }

    private static Trigger Shade(DependencyProperty property, object value, string background, string border)
    {
        var trigger = new Trigger { Property = property, Value = value };
        trigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(background), "face"));
        trigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(border), "face"));
        return trigger;
    }
}

/// <summary>A button's face: its icon and label, large (icon over label) or small (icon beside it).</summary>
internal sealed class ToolstripFace : StackPanel
{
    private readonly TextBlock? _label;

    /// <param name="icon">The icon's name, or null.</param>
    /// <param name="label">The label, or empty.</param>
    /// <param name="vertical">Whether the button is large.</param>
    /// <param name="arrow">Whether a drop-down arrow follows the label.</param>
    public ToolstripFace(string? icon, string label, bool vertical, bool arrow)
    {
        Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        IsHitTestVisible = false;
        if (ToolstripIcons.Create(icon, vertical ? 24 : 16) is { } picture)
        {
            picture.HorizontalAlignment = HorizontalAlignment.Center;
            picture.VerticalAlignment = VerticalAlignment.Center;
            picture.Margin = vertical ? new Thickness(0, 1, 0, 2) : new Thickness(0);
            Children.Add(picture);
        }

        if (label.Length > 0)
        {
            _label = new TextBlock
            {
                Text = label,
                TextAlignment = vertical ? TextAlignment.Center : TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = vertical || Children.Count == 0 ? new Thickness(0) : new Thickness(4, 0, 0, 0),
                LineHeight = 13,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            Children.Add(_label);
        }

        if (arrow)
        {
            FrameworkElement glyph = ToolstripIcons.Create("dropDown", vertical ? 10 : 12)!;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = vertical ? new Thickness(0) : new Thickness(1, 0, 0, 0);
            Children.Add(glyph);
        }
    }

    /// <summary>Whether the face has a label that can be hidden.</summary>
    public bool HasLabel => _label is not null;

    /// <summary>Shows or hides the label (a narrow tab's small buttons drop theirs).</summary>
    public void ShowLabel(bool show)
    {
        if (_label is not null)
        {
            _label.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

/// <summary>Something on the toolstrip whose label a narrow tab may hide.</summary>
internal interface IToolstripLabelled
{
    void ShowLabel(bool show);
}

/// <summary>A button its column draws large or small, as the controls the column shows decide.</summary>
internal interface IToolstripOrientable
{
    void SetVertical(bool vertical);
}

/// <summary>The sizes of the toolstrip's buttons.</summary>
internal static class ToolstripLook
{
    /// <summary>A large button fills its column's height; a small one is a row high.</summary>
    public static void Size(Control button, bool vertical, double height)
    {
        if (vertical)
        {
            button.Height = height;
            button.MinWidth = 40;
            button.Padding = new Thickness(5, 2, 5, 2);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Top;
        }
        else
        {
            button.Height = Toolstrip.RowHeight;
            button.MinWidth = 0;
            button.Padding = new Thickness(3, 2, 3, 2);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.VerticalContentAlignment = VerticalAlignment.Center;
        }
    }
}

/// <summary>A button's icon, label and arrow, drawn large or small; a label hidden by a narrow tab stays hidden across a redraw.</summary>
internal sealed class ToolstripFaceState(ContentControl owner, string? icon, string? text, bool arrow, double height)
{
    private bool _labelShown = true;

    public void SetVertical(bool vertical)
    {
        // A large button never drops its label.
        _labelShown |= vertical;
        var face = new ToolstripFace(icon, ToolstripRules.Label(text, vertical), vertical, arrow);
        face.ShowLabel(_labelShown);
        owner.Content = face;
        ToolstripLook.Size(owner, vertical, height);
    }

    public void ShowLabel(bool show)
    {
        _labelShown = show;
        (owner.Content as ToolstripFace)?.ShowLabel(show);
    }
}

/// <summary>A push button of the toolstrip.</summary>
internal sealed class ToolstripPushButton : Button, IToolstripLabelled, IToolstripOrientable
{
    public ToolstripPushButton() => ToolstripChrome.Apply(this);

    /// <summary>What the button draws, when its column may redraw it.</summary>
    public ToolstripFaceState? FaceState { get; set; }

    /// <summary>Clicks the button: its command runs and Click is raised.</summary>
    public void PerformClick() => OnClick();

    public void ShowLabel(bool show)
    {
        if (FaceState is not null)
        {
            FaceState.ShowLabel(show);
        }
        else
        {
            (Content as ToolstripFace)?.ShowLabel(show);
        }
    }

    public void SetVertical(bool vertical) => FaceState?.SetVertical(vertical);
}

/// <summary>A toggle button of the toolstrip; in a button group, one of a set of choices.</summary>
internal sealed class ToolstripToggleButton : ToggleButton, IToolstripLabelled, IToolstripOrientable
{
    public ToolstripToggleButton() => ToolstripChrome.Apply(this);

    /// <summary>What the button draws.</summary>
    public ToolstripFaceState? FaceState { get; set; }

    public void SetVertical(bool vertical) => FaceState?.SetVertical(vertical);

    /// <summary>The group only one of whose buttons is pressed at a time, or null.</summary>
    public string? Group { get; init; }

    /// <summary>Clicks the button: it toggles, its command runs and Click is raised.</summary>
    public void PerformClick() => OnClick();

    public void ShowLabel(bool show) => FaceState?.ShowLabel(show);
}

/// <summary>
/// A button that opens a popup: a drop-down button, a split button's arrow, a collapsed section.
/// It stays pressed while its popup is open and offers UI Automation's expand and collapse.
/// </summary>
internal class ToolstripPopupButton : ToggleButton, IToolstripLabelled, IToolstripOrientable
{
    private long _closedAt;
    private bool _opening;

    public ToolstripPopupButton() => ToolstripChrome.Apply(this);

    /// <summary>Opens the popup for the button; returns false when there is none to open.</summary>
    public Func<ToolstripPopupButton, bool>? OpenPopup { get; set; }

    /// <summary>Closes the popup the button opened.</summary>
    public Action? ClosePopup { get; set; }

    /// <summary>Called by the popup when it closes, however it closed.</summary>
    public void PopupClosed()
    {
        _closedAt = Environment.TickCount64;
        if (IsChecked == true)
        {
            IsChecked = false;
        }
    }

    /// <summary>What the button draws, when its column may redraw it.</summary>
    public ToolstripFaceState? FaceState { get; set; }

    public void ShowLabel(bool show)
    {
        if (FaceState is not null)
        {
            FaceState.ShowLabel(show);
        }
        else
        {
            (Content as ToolstripFace)?.ShowLabel(show);
        }
    }

    public void SetVertical(bool vertical) => FaceState?.SetVertical(vertical);

    protected override void OnClick()
    {
        // The press that closed the popup by clicking this button must not open it again.
        if (IsChecked != true && Environment.TickCount64 - _closedAt < 250)
        {
            return;
        }

        base.OnClick();
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        _opening = true;
        bool opened = OpenPopup?.Invoke(this) ?? false;
        _opening = false;
        if (!opened)
        {
            IsChecked = false;
        }

        RaiseState(ExpandCollapseState.Collapsed, opened ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        if (!_opening)
        {
            ClosePopup?.Invoke();
        }

        RaiseState(ExpandCollapseState.Expanded, ExpandCollapseState.Collapsed);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ToolstripPopupButtonPeer(this);

    private void RaiseState(ExpandCollapseState was, ExpandCollapseState now)
    {
        if (was != now && UIElementAutomationPeer.FromElement(this) is ToolstripPopupButtonPeer peer)
        {
            peer.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, was, now);
        }
    }
}

/// <summary>A drop-down button's automation: a button that expands and collapses, not a toggle.</summary>
internal sealed class ToolstripPopupButtonPeer(ToolstripPopupButton owner) : ToggleButtonAutomationPeer(owner), IExpandCollapseProvider
{
    public ExpandCollapseState ExpandCollapseState =>
        ((ToolstripPopupButton)Owner).IsChecked == true ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public override object? GetPattern(PatternInterface patternInterface) => patternInterface switch
    {
        PatternInterface.ExpandCollapse => this,
        PatternInterface.Toggle => null,
        _ => base.GetPattern(patternInterface),
    };

    public void Expand()
    {
        if (!IsEnabled())
        {
            throw new ElementNotEnabledException();
        }

        ((ToolstripPopupButton)Owner).IsChecked = true;
    }

    public void Collapse() => ((ToolstripPopupButton)Owner).IsChecked = false;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

    protected override string GetClassNameCore() => "ToolstripDropDownButton";
}

/// <summary>
/// A split button: the part that runs the action, and the arrow that opens its popup. A large one
/// runs from its icon and opens from its label and arrow under it, as R2025b draws it; a small
/// one runs from its icon and label and opens from the arrow at its right.
/// </summary>
internal sealed class ToolstripSplitButton : StackPanel, IToolstripLabelled, IToolstripOrientable
{
    // The large button's upper part: the icon with its margins and padding.
    private const double IconPart = 30;
    private readonly string? _icon;
    private readonly string? _text;
    private readonly double _height;
    private bool _labelShown = true;
    private bool _vertical;

    public ToolstripSplitButton(string? icon, string? text, bool vertical, double height)
    {
        _icon = icon;
        _text = text;
        _height = height;
        Children.Add(Main);
        Children.Add(Arrow);
        SetVertical(vertical);
    }

    /// <summary>The part that runs the action.</summary>
    public ToolstripPushButton Main { get; } = new();

    /// <summary>The part that opens the popup.</summary>
    public ToolstripPopupButton Arrow { get; } = new();

    public void SetVertical(bool vertical)
    {
        _vertical = vertical;
        _labelShown |= vertical;
        Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        string label = ToolstripRules.Label(_text, vertical);
        Main.Content = new ToolstripFace(_icon, vertical ? "" : label, vertical, arrow: false);
        Arrow.Content = new ToolstripFace(null, vertical ? label : "", vertical, arrow: true);
        if (vertical)
        {
            Height = _height;
            Main.Height = IconPart;
            Arrow.Height = _height - IconPart;
            Main.Padding = new Thickness(6, 2, 6, 0);
            Arrow.Padding = new Thickness(3, 0, 3, 1);
            Main.HorizontalContentAlignment = HorizontalAlignment.Center;
            Arrow.VerticalContentAlignment = VerticalAlignment.Top;
            Main.HorizontalAlignment = HorizontalAlignment.Stretch;
            Arrow.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            Height = double.NaN;
            Main.Height = Toolstrip.RowHeight;
            Arrow.Height = Toolstrip.RowHeight;
            Main.Padding = new Thickness(3, 2, 3, 2);
            Arrow.Padding = new Thickness(1, 2, 1, 2);
            Main.HorizontalContentAlignment = HorizontalAlignment.Left;
            Arrow.VerticalContentAlignment = VerticalAlignment.Center;
            Main.HorizontalAlignment = HorizontalAlignment.Left;
            Arrow.HorizontalAlignment = HorizontalAlignment.Left;
        }

        ShowLabel(_labelShown);
    }

    public void ShowLabel(bool show)
    {
        _labelShown = show || _vertical;
        (Main.Content as ToolstripFace)?.ShowLabel(_labelShown);
        (Arrow.Content as ToolstripFace)?.ShowLabel(_labelShown);
    }
}
