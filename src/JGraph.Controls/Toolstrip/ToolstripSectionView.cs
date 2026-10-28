using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using JGraph.Controls.Themes;
using JGraph.Scripting.Toolstrip;

namespace JGraph.Controls.Toolstrip;

/// <summary>A panel that UI Automation sees as a named group: a section, the quick access bar, the toolstrip.</summary>
internal class ToolstripGroup(AutomationControlType type) : StackPanel
{
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this, type);

    private sealed class GroupPeer(ToolstripGroup owner, AutomationControlType type) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => type;

        protected override string GetClassNameCore() => "ToolstripGroup";

        protected override bool IsControlElementCore() => true;
    }
}

/// <summary>A column of a section and the labels it may drop.</summary>
/// <param name="View">The column.</param>
/// <param name="CanHideLabels">Whether the column, over the controls it shows now, may drop its labels (<see cref="ToolstripRules.CanHideLabels"/>).</param>
/// <param name="Labelled">The small buttons whose labels go.</param>
internal sealed record ToolstripColumnView(FrameworkElement View, Func<bool> CanHideLabels, IReadOnlyList<IToolstripLabelled> Labelled);

/// <summary>
/// A section on a tab: its columns over its title, or - collapsed when the tab is too narrow - a
/// drop-down showing its title and icon whose popup holds the columns (R2025b's
/// <c>Section.collapse</c>).
/// </summary>
internal sealed class ToolstripSectionView : Border, IToolstripResizable
{
    private readonly Grid _body = new();
    private readonly ToolstripGroup _columns = new(AutomationControlType.Group) { Orientation = Orientation.Horizontal };
    private readonly IReadOnlyList<ToolstripColumnView> _views;
    private readonly Popup _popup = new() { StaysOpen = false, Placement = PlacementMode.Bottom, AllowsTransparency = false };
    private readonly Border _popupFrame = new() { BorderThickness = new Thickness(1), Padding = new Thickness(4, 2, 4, 2) };
    private bool _labelsShown = true;

    public ToolstripSectionView(ToolstripSection spec, IReadOnlyList<ToolstripColumnView> columns, double height)
    {
        Spec = spec;
        _views = columns;
        BorderThickness = new Thickness(0, 0, 1, 0);
        SetResourceReference(BorderBrushProperty, ThemeKeys.Separator);
        Padding = new Thickness(3, 2, 3, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, spec.Tag ?? "");
        System.Windows.Automation.AutomationProperties.SetName(_columns, spec.Title ?? "");
        if (spec.Tag is not null)
        {
            System.Windows.Automation.AutomationProperties.SetAutomationId(_columns, spec.Tag);
        }

        foreach (ToolstripColumnView column in columns)
        {
            _columns.Children.Add(column.View);
        }

        _columns.Height = height;
        _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _body.Children.Add(_columns);
        var title = new TextBlock
        {
            Text = spec.Title ?? "",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 2),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        Grid.SetRow(title, 1);
        _body.Children.Add(title);

        DropDown = new ToolstripPopupButton
        {
            Content = new ToolstripFace(spec.Icon, spec.Title ?? "", vertical: true, arrow: true),
            Height = height - 2,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Top,
            OpenPopup = _ => OpenPopup(),
            ClosePopup = () => _popup.IsOpen = false,
        };
        DropDown.Padding = new Thickness(6, 2, 6, 2);
        System.Windows.Automation.AutomationProperties.SetName(DropDown, spec.Title ?? "");
        System.Windows.Automation.AutomationProperties.SetAutomationId(DropDown, (spec.Tag ?? spec.Title ?? "section") + ".dropDown");
        _body.Children.Add(DropDown);

        _popupFrame.SetResourceReference(BackgroundProperty, ThemeKeys.ToolBar);
        _popupFrame.SetResourceReference(BorderBrushProperty, ThemeKeys.BorderStrong);
        _popup.Child = _popupFrame;
        _popup.PlacementTarget = DropDown;
        _popup.Closed += (_, _) => DropDown.PopupClosed();
        Child = _body;
    }

    /// <summary>The section's layout.</summary>
    public ToolstripSection Spec { get; }

    /// <summary>The drop-down the section shows while collapsed.</summary>
    public ToolstripPopupButton DropDown { get; }

    /// <summary>Whether the collapsed section's popup is open.</summary>
    public bool IsPopupOpen => _popup.IsOpen;

    public int CollapsePriority => Spec.CollapsePriority;

    public bool IsCollapsed { get; private set; }

    /// <summary>Whether the labels of the columns that may drop them are showing.</summary>
    public bool LabelsShown => _labelsShown;

    public void Collapse()
    {
        if (IsCollapsed || Spec.CollapsePriority == -1)
        {
            return;
        }

        IsCollapsed = true;
        _body.Children.Remove(_columns);
        _popupFrame.Child = _columns;
        DropDown.Visibility = Visibility.Visible;
    }

    public void Expand()
    {
        if (!IsCollapsed)
        {
            return;
        }

        IsCollapsed = false;
        _popup.IsOpen = false;
        _popupFrame.Child = null;
        _body.Children.Insert(0, _columns);
        DropDown.Visibility = Visibility.Collapsed;
    }

    public void ShowLabels(bool show)
    {
        _labelsShown = show;
        foreach (ToolstripColumnView column in _views)
        {
            if (column.CanHideLabels())
            {
                foreach (IToolstripLabelled item in column.Labelled)
                {
                    item.ShowLabel(show);
                }
            }
        }
    }

    /// <summary>Closes the collapsed section's popup.</summary>
    public void ClosePopup() => _popup.IsOpen = false;

    private bool OpenPopup()
    {
        if (!IsCollapsed)
        {
            return false;
        }

        _popup.IsOpen = true;
        return true;
    }
}
