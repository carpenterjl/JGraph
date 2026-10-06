using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Rendering.Layout;
using JGraph.Scripting;

namespace JGraph.Controls;

/// <summary>
/// The WPF realisation of the second batch of components (app-building plan, U9): the drawn faces
/// of a knob, a discrete knob, a switch, a gauge and a lamp; a date picker as a field with a
/// calendar; a colour picker as a button with a palette; a tree as a <see cref="TreeView"/>; a
/// component's own context menu; and the styles <c>addStyle</c> lays over a list's or a tree's
/// items. As everywhere in the layer, what a person does goes back through
/// <see cref="ScriptGraphicsCallbacks.NotifyComponent"/> and the control keeps showing it until a
/// frame arrives that has taken it.
/// </summary>
public sealed partial class UiComponentLayer
{
    /// <summary>The web palette a colour picker offers, R2025b's forty, left to right and top to bottom.</summary>
    private static readonly string[] PaletteHex =
    [
        "#000000", "#434343", "#666666", "#999999", "#B7B7B7", "#CCCCCC", "#D9D9D9", "#EFEFEF", "#F3F3F3", "#FFFFFF",
        "#980000", "#FF0000", "#FF9900", "#FFFF00", "#00FF00", "#00FFFF", "#4A86E8", "#0000FF", "#9900FF", "#FF00FF",
        "#E6B8AF", "#F4CCCC", "#FCE5CD", "#FFF2CC", "#D9EAD3", "#D0E0E3", "#C9DAF8", "#CFE2F3", "#D9D2E9", "#EAD1DC",
        "#CC4125", "#E06666", "#F6B26B", "#FFD966", "#93C47D", "#76A5AF", "#6D9EEB", "#6FA8DC", "#8E7CC3", "#C27BA0",
    ];

    // --- the drawn faces ------------------------------------------------------------------------

    private static Shown MakeFace(UiComponentFrame frame)
    {
        UiComponentModel source = frame.Source;
        switch (frame.Kind)
        {
            case UiComponentKind.Knob:
            {
                var face = new UiKnobFace();
                var shown = new Shown(source, frame.Kind, face) { Knob = face };
                face.Changing += value => Tell(shown, "changing", value);
                face.Changed += value => Tell(shown, "value", value);
                return shown;
            }

            case UiComponentKind.DiscreteKnob:
            {
                var face = new UiDiscreteKnobFace();
                var shown = new Shown(source, frame.Kind, face) { Dial = face };
                face.Changed += index => Tell(shown, "value", index);
                return shown;
            }

            case UiComponentKind.Switch:
            case UiComponentKind.RockerSwitch:
            case UiComponentKind.ToggleSwitch:
            {
                var face = new UiSwitchFace
                {
                    SwitchStyle = frame.Kind switch
                    {
                        UiComponentKind.RockerSwitch => UiSwitchStyle.Rocker,
                        UiComponentKind.ToggleSwitch => UiSwitchStyle.Toggle,
                        _ => UiSwitchStyle.Slider,
                    },
                };
                var shown = new Shown(source, frame.Kind, face) { Switch = face };
                face.Changed += index => Tell(shown, "value", index);
                return shown;
            }

            case UiComponentKind.Lamp:
            {
                var face = new UiLampFace();
                return new Shown(source, frame.Kind, face) { Lamp = face };
            }

            default:
            {
                var face = new UiGaugeFace
                {
                    GaugeStyle = frame.Kind switch
                    {
                        UiComponentKind.LinearGauge => UiGaugeStyle.Linear,
                        UiComponentKind.NinetyDegreeGauge => UiGaugeStyle.NinetyDegree,
                        UiComponentKind.SemicircularGauge => UiGaugeStyle.Semicircular,
                        _ => UiGaugeStyle.Circular,
                    },
                };
                return new Shown(source, frame.Kind, face) { Gauge = face };
            }
        }
    }

    /// <summary>
    /// The rectangle a component with labels outside its own takes: a knob's, a discrete knob's
    /// and a switch's, as the models work them out (<see cref="UiKnobModel.OuterOf"/>).
    /// </summary>
    private static Rect2D OuterOf(UiComponentFrame frame, Rect2D box) => UiShapedCells.OuterOf(frame, box);

    private static void RefreshFace(Shown shown, UiComponentFrame frame, Brush foreground, Typeface face, double size, bool waiting)
    {
        Rect2D box = frame.Position;
        Rect2D outer = OuterOf(frame, box);
        var inset = new System.Windows.Thickness(box.X - outer.X, box.Y - outer.Y, outer.Right - box.Right, outer.Bottom - box.Bottom);
        if (shown.Knob is { } knob)
        {
            knob.Inset = inset;
            knob.Minimum = frame.Min;
            knob.Maximum = frame.Max;
            knob.MajorTicks = frame.MajorTicks;
            knob.MinorTicks = frame.MinorTicks;
            knob.TickLabels = frame.TickLabels;
            knob.Foreground = foreground;
            knob.Face = face;
            knob.FontSize = size;
            if (!waiting && !knob.IsMouseCaptured)
            {
                knob.Value = frame.Number;
            }

            knob.InvalidateVisual();
        }
        else if (shown.Dial is { } dial)
        {
            dial.Inset = inset;
            dial.Items = frame.Items;
            dial.Foreground = foreground;
            dial.Face = face;
            dial.FontSize = size;
            if (!waiting)
            {
                dial.Selected = frame.Selected.Count > 0 ? frame.Selected[0] : 0;
            }

            dial.InvalidateVisual();
        }
        else if (shown.Switch is { } toggle)
        {
            toggle.Inset = inset;
            toggle.Items = frame.Items;
            toggle.Upright = frame.Upright;
            toggle.Foreground = foreground;
            toggle.Face = face;
            toggle.FontSize = size;
            if (!waiting)
            {
                toggle.IsOn = frame.Checked;
            }

            toggle.InvalidateVisual();
        }
        else if (shown.Gauge is { } gauge)
        {
            gauge.Inset = default;
            gauge.Minimum = frame.Min;
            gauge.Maximum = frame.Max;
            gauge.Value = frame.Number;
            gauge.MajorTicks = frame.MajorTicks;
            gauge.MinorTicks = frame.MinorTicks;
            gauge.TickLabels = frame.TickLabels;
            gauge.Colors = frame.Colors;
            gauge.ColorLimits = frame.ColorLimits;
            gauge.Clockwise = frame.Checked;
            gauge.Orientation = frame.Word;
            gauge.Foreground = foreground;
            gauge.Face = face;
            gauge.FontSize = size;
            gauge.Background = BrushOf(frame.Background);
            gauge.InvalidateVisual();
        }
        else if (shown.Lamp is { } lamp)
        {
            if (frame.Color is { } colour)
            {
                lamp.Color = Color.FromRgb((byte)System.Math.Round(colour.R * 255), (byte)System.Math.Round(colour.G * 255), (byte)System.Math.Round(colour.B * 255));
            }

            lamp.InvalidateVisual();
        }
    }

    // --- date picker ------------------------------------------------------------------------------

    private Shown MakeDatePicker(UiComponentFrame frame)
    {
        var box = new TextBox { Style = (Style)Styles["JG.Ui.Edit"] };
        var button = new Button { Style = (Style)Styles["JG.Ui.PushButton"], Content = "▾", Width = 22, Padding = new System.Windows.Thickness(0), Focusable = false };
        AutomationProperties.SetAutomationId(button, "Calendar");
        var calendar = new Calendar { SelectionMode = CalendarSelectionMode.SingleDate };
        var popup = new Popup { Child = new Border { Child = calendar, Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new System.Windows.Thickness(1) }, StaysOpen = false, Placement = PlacementMode.Bottom };
        var wrap = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(button, Dock.Right);
        wrap.Children.Add(button);
        wrap.Children.Add(box);
        wrap.Children.Add(popup);
        popup.PlacementTarget = wrap;
        var shown = new Shown(frame.Source, frame.Kind, wrap) { Input = box, DateBox = box, Calendar = calendar, Popup = popup };
        button.Click += (_, _) =>
        {
            if (shown.Frame is { Enabled: true })
            {
                popup.IsOpen = !popup.IsOpen;
            }
        };
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (!shown.Applying && calendar.SelectedDate is { } picked)
            {
                popup.IsOpen = false;
                Tell(shown, "value", UiDatePickerModel.FromDateTime(picked));
            }
        };
        void CommitTyped()
        {
            if (!shown.Applying && box.Text != shown.Committed)
            {
                shown.Committed = box.Text;
                Tell(shown, "value", box.Text);
            }
        }

        box.LostKeyboardFocus += (_, _) => CommitTyped();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
            {
                CommitTyped();
            }
        };
        return shown;
    }

    private static void RefreshDatePicker(Shown shown, UiComponentFrame frame, bool waiting)
    {
        TextBox box = shown.DateBox!;
        Calendar calendar = shown.Calendar!;
        box.IsReadOnly = !frame.Editable;
        box.Background = BrushOf(frame.Background) ?? Brushes.White;
        if (!waiting && !box.IsKeyboardFocusWithin)
        {
            box.Text = frame.Text;
            shown.Committed = frame.Text;
        }

        calendar.DisplayDateStart = UiDatePickerModel.ToDateTime(frame.Min) ?? DateTime.MinValue;
        calendar.DisplayDateEnd = UiDatePickerModel.ToDateTime(frame.Max) ?? DateTime.MaxValue;
        calendar.BlackoutDates.Clear();
        if (frame.Date is { } dates)
        {
            foreach (double day in dates.DisabledDays)
            {
                if (UiDatePickerModel.ToDateTime(day) is { } blocked && blocked >= calendar.DisplayDateStart && blocked <= calendar.DisplayDateEnd)
                {
                    calendar.BlackoutDates.Add(new CalendarDateRange(blocked));
                }
            }
        }

        DateTime? held = frame.HasNumber ? UiDatePickerModel.ToDateTime(frame.Number) : null;
        if (!waiting && calendar.SelectedDate != held)
        {
            calendar.SelectedDate = held;
            if (held is { } shownDate)
            {
                calendar.DisplayDate = shownDate;
            }
        }
    }

    // --- colour picker -------------------------------------------------------------------------------

    private Shown MakeColorPicker(UiComponentFrame frame)
    {
        var swatch = new Border { Width = 14, Height = 14, BorderBrush = Brushes.Gray, BorderThickness = new System.Windows.Thickness(1), Background = Brushes.Red, SnapsToDevicePixels = true };
        var picture = new Image { Stretch = Stretch.Uniform, MaxHeight = 16, MaxWidth = 16, Margin = new System.Windows.Thickness(0, 0, 3, 0), Visibility = Visibility.Collapsed };
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        content.Children.Add(picture);
        content.Children.Add(swatch);
        var button = new Button { Style = (Style)Styles["JG.Ui.PushButton"], Content = content, Padding = new System.Windows.Thickness(2) };
        var palette = new UniformGrid { Columns = 10, Rows = 4 };
        var hex = new TextBox { Style = (Style)Styles["JG.Ui.Edit"], Margin = new System.Windows.Thickness(2), MinWidth = 80 };
        AutomationProperties.SetAutomationId(hex, "Hex");
        var panel = new StackPanel { Background = Brushes.White };
        panel.Children.Add(palette);
        panel.Children.Add(hex);
        var popup = new Popup { Child = new Border { Child = panel, BorderBrush = Brushes.Gray, BorderThickness = new System.Windows.Thickness(1) }, StaysOpen = false, Placement = PlacementMode.Bottom, PlacementTarget = button };
        var wrap = new Grid();
        wrap.Children.Add(button);
        wrap.Children.Add(popup);
        var shown = new Shown(frame.Source, frame.Kind, wrap) { Input = button, Swatch = swatch, Picture = picture, Popup = popup, Hex = hex };
        foreach (string code in PaletteHex)
        {
            var cell = new Border
            {
                Width = 18,
                Height = 18,
                Margin = new System.Windows.Thickness(1),
                Background = (Brush)new BrushConverter().ConvertFromString(code)!,
                BorderBrush = Brushes.Gray,
                BorderThickness = new System.Windows.Thickness(1),
                Cursor = Cursors.Hand,
            };
            AutomationProperties.SetAutomationId(cell, code);
            Color picked = ((SolidColorBrush)cell.Background).Color;
            cell.MouseLeftButtonUp += (_, _) =>
            {
                popup.IsOpen = false;
                Tell(shown, "value", new UiColor(picked.R / 255.0, picked.G / 255.0, picked.B / 255.0));
            };
            palette.Children.Add(cell);
        }

        void CommitHex()
        {
            if (TryHex(hex.Text, out UiColor typed) && typed != shown.Frame?.Color)
            {
                popup.IsOpen = false;
                Tell(shown, "value", typed);
            }
        }

        hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
            {
                CommitHex();
            }
        };
        hex.LostKeyboardFocus += (_, _) => CommitHex();
        button.Click += (_, _) =>
        {
            if (shown.Frame is { Enabled: true })
            {
                popup.IsOpen = !popup.IsOpen;
            }
        };
        return shown;
    }

    private static bool TryHex(string text, out UiColor colour)
    {
        colour = default;
        string hex = text.Trim().TrimStart('#');
        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int value))
        {
            return false;
        }

        colour = new UiColor(((value >> 16) & 255) / 255.0, ((value >> 8) & 255) / 255.0, (value & 255) / 255.0);
        return true;
    }

    private static void RefreshColorPicker(Shown shown, UiComponentFrame frame)
    {
        if (frame.Color is { } colour)
        {
            shown.Swatch!.Background = BrushOf(colour);
            shown.Hex!.Text = $"#{(int)System.Math.Round(colour.R * 255):X2}{(int)System.Math.Round(colour.G * 255):X2}{(int)System.Math.Round(colour.B * 255):X2}";
        }

        var button = (Button)shown.Input!;
        button.Background = BrushOf(frame.Background) ?? Brushes.Transparent;
        Image picture = shown.Picture!;
        if (!ReferenceEquals(shown.Drawn, frame.Image))
        {
            shown.Drawn = frame.Image;
            picture.Source = SourceOf(frame.Image);
            picture.Visibility = frame.Image is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // --- trees ------------------------------------------------------------------------------------------

    private Shown MakeTree(UiComponentFrame frame)
    {
        var tree = new TreeView { BorderBrush = new SolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3)), BorderThickness = new System.Windows.Thickness(1), Background = Brushes.White };
        ScrollViewer.SetHorizontalScrollBarVisibility(tree, ScrollBarVisibility.Auto);
        var shown = new Shown(frame.Source, frame.Kind, tree) { Input = tree, Tree = tree };
        tree.SelectedItemChanged += (_, _) =>
        {
            if (shown.Applying || shown.Frame?.Tree is not { } held)
            {
                return;
            }

            // Ctrl holds the earlier picks of a multiselect tree; a plain pick replaces them.
            var picked = new List<UiTreeNodeModel>();
            if (held.Selected.Count > 0 && shown.Frame is { Multi: true } && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                picked.AddRange(shown.TreeSelection);
            }

            if (tree.SelectedItem is TreeViewItem item && item.Tag is UiTreeNodeModel node)
            {
                if (picked.Contains(node))
                {
                    picked.Remove(node);
                }
                else
                {
                    picked.Add(node);
                }
            }

            shown.TreeSelection = picked;
            PaintTreeSelection(shown);
            Tell(shown, JgsTreeActions.NodeSelect, picked.ToArray());
        };
        tree.PreviewMouseLeftButtonUp += (_, e) =>
        {
            UiTreeNodeModel? node = (NodeItemUnder(e.OriginalSource as DependencyObject)?.Tag) as UiTreeNodeModel;
            Dispatcher.BeginInvoke(() => Tell(shown, "clicked", node));
        };
        tree.MouseDoubleClick += (_, e) =>
        {
            TreeViewItem? item = NodeItemUnder(e.OriginalSource as DependencyObject);
            if (item?.Tag is UiTreeNodeModel node)
            {
                Tell(shown, "doubleclicked", node);
                if (shown.Frame is { Editable: true })
                {
                    BeginNodeEdit(shown, item, node);
                }
            }
        };
        tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.F2 && shown.Frame is { Editable: true } && tree.SelectedItem is TreeViewItem item && item.Tag is UiTreeNodeModel node)
            {
                BeginNodeEdit(shown, item, node);
                e.Handled = true;
            }
        };
        return shown;
    }

    private static TreeViewItem? NodeItemUnder(DependencyObject? hit)
    {
        for (DependencyObject? up = hit; up is not null; up = VisualTreeHelper.GetParent(up))
        {
            if (up is TreeViewItem item)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>Lets a person retype a node's text in place; Enter or leaving commits, Escape gives up.</summary>
    private static void BeginNodeEdit(Shown shown, TreeViewItem item, UiTreeNodeModel node)
    {
        if (item.Header is not StackPanel header || header.Children.OfType<TextBlock>().LastOrDefault() is not { } label)
        {
            return;
        }

        var box = new TextBox { Text = label.Text, MinWidth = 60, Style = (Style)Styles["JG.Ui.Edit"] };
        int at = header.Children.IndexOf(label);
        header.Children.RemoveAt(at);
        header.Children.Insert(at, box);
        bool done = false;
        void End(bool commit)
        {
            if (done)
            {
                return;
            }

            done = true;
            string typed = box.Text;
            header.Children.Remove(box);
            header.Children.Insert(at, label);
            if (commit && typed != label.Text)
            {
                label.Text = typed;
                Tell(shown, JgsTreeActions.NodeText, new object[] { node, typed });
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
            {
                End(commit: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                End(commit: false);
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => End(commit: true);
        box.Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    private static void RefreshTree(Shown shown, UiComponentFrame frame, Brush foreground, FontFamily family, FontWeight weight, FontStyle style, double size, bool waiting)
    {
        if (frame.Tree is not { } held)
        {
            return;
        }

        TreeView tree = shown.Tree!;
        tree.Background = BrushOf(frame.Background) ?? Brushes.White;
        string signature = TreeSignature(held.Nodes, held.CheckBoxes);
        if (signature != shown.TreeSignature)
        {
            shown.TreeSignature = signature;
            shown.TreeItems.Clear();
            tree.Items.Clear();
            foreach (UiTreeNodeFrame node in held.Nodes)
            {
                tree.Items.Add(BuildNode(shown, node, held.CheckBoxes, 1));
            }
        }

        // Each item takes its text, its picture, whether it stands open and its style.
        foreach ((UiTreeNodeModel node, TreeViewItem item) in shown.TreeItems)
        {
            if (shown.TreeFrames.TryGetValue(node, out UiTreeNodeFrame? nodeFrame))
            {
                if (item.Header is StackPanel header)
                {
                    foreach (TextBlock label in header.Children.OfType<TextBlock>())
                    {
                        label.Text = nodeFrame.Text;
                        label.FontFamily = family;
                        label.FontSize = size;
                        UiStyle styled = UiStyleRule.Combine(frame.Styles.Where(rule => rule.CoversNode(node)));
                        label.Foreground = BrushOf(styled.FontColor) ?? foreground;
                        label.FontWeight = styled.FontWeight.Length > 0 ? (styled.FontWeight == "bold" ? FontWeights.Bold : FontWeights.Normal) : weight;
                        label.FontStyle = styled.FontAngle.Length > 0 ? (styled.FontAngle == "italic" ? FontStyles.Italic : FontStyles.Normal) : style;
                        header.Background = BrushOf(styled.BackgroundColor) ?? Brushes.Transparent;
                        if (styled.FontName.Length > 0)
                        {
                            label.FontFamily = new FontFamily(UiLayout.FontFamily(styled.FontName));
                        }
                    }

                    foreach (Image picture in header.Children.OfType<Image>())
                    {
                        picture.Source = SourceOf(nodeFrame.Icon);
                        picture.Visibility = nodeFrame.Icon is null ? Visibility.Collapsed : Visibility.Visible;
                    }

                    foreach (CheckBox check in header.Children.OfType<CheckBox>())
                    {
                        bool ticked = held.Checked.Contains(node);
                        bool partly = !ticked && node.Descendants().Any(held.Checked.Contains);
                        check.IsChecked = ticked ? true : partly ? null : false;
                    }
                }

                if (item.IsExpanded != nodeFrame.Expanded)
                {
                    item.IsExpanded = nodeFrame.Expanded;
                }
            }
        }

        if (!waiting)
        {
            shown.TreeSelection = [.. held.Selected];
            PaintTreeSelection(shown);
            TreeViewItem? first = held.Selected.Count > 0 && shown.TreeItems.TryGetValue(held.Selected[0], out TreeViewItem? selectedItem) ? selectedItem : null;
            if (first is not null && !first.IsSelected)
            {
                first.IsSelected = true;
            }
            else if (first is null && tree.SelectedItem is TreeViewItem was)
            {
                was.IsSelected = false;
            }
        }

        if (frame.ScrollRequests != shown.ScrollSeen)
        {
            shown.ScrollSeen = frame.ScrollRequests;
            switch (frame.ScrollTarget)
            {
                case UiTreeNodeModel node when shown.TreeItems.TryGetValue(node, out TreeViewItem? item):
                    item.BringIntoView();
                    break;
                case "top" when tree.Items.Count > 0:
                    (tree.Items[0] as TreeViewItem)?.BringIntoView();
                    break;
                case "bottom" when shown.TreeItems.Count > 0:
                    shown.TreeItems.Values.Last().BringIntoView();
                    break;
            }
        }
    }

    private static string TreeSignature(IReadOnlyList<UiTreeNodeFrame> nodes, bool checkBoxes)
    {
        var text = new System.Text.StringBuilder(checkBoxes ? "c" : "t");
        void Walk(IReadOnlyList<UiTreeNodeFrame> level)
        {
            foreach (UiTreeNodeFrame node in level)
            {
                text.Append(node.Source.Id).Append('(');
                Walk(node.Children);
                text.Append(')');
            }
        }

        Walk(nodes);
        return text.ToString();
    }

    private static TreeViewItem BuildNode(Shown shown, UiTreeNodeFrame node, bool checkBoxes, int level)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        if (checkBoxes)
        {
            var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new System.Windows.Thickness(0, 0, 4, 0), IsThreeState = false };
            AutomationProperties.SetAutomationId(check, node.Tag.Length > 0 ? node.Tag + ".Check" : "Check");
            RoutedEventHandler ticked = (_, _) =>
            {
                if (!shown.Applying)
                {
                    Tell(shown, JgsTreeActions.NodeCheck, new object[] { node.Source, check.IsChecked == true });
                }
            };
            check.Checked += ticked;
            check.Unchecked += ticked;
            header.Children.Add(check);
        }

        header.Children.Add(new Image { Stretch = Stretch.Uniform, MaxHeight = 16, MaxWidth = 16, Margin = new System.Windows.Thickness(0, 0, 4, 0), Visibility = Visibility.Collapsed });
        header.Children.Add(new TextBlock { Text = node.Text, VerticalAlignment = VerticalAlignment.Center });
        var item = new TreeViewItem { Header = header, Tag = node.Source, IsExpanded = node.Expanded };
        AutomationProperties.SetAutomationId(item, node.Tag);
        AutomationProperties.SetName(item, node.Text);
        item.Expanded += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, item) && !shown.Applying)
            {
                Tell(shown, JgsTreeActions.NodeExpand, node.Source);
            }
        };
        item.Collapsed += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, item) && !shown.Applying)
            {
                Tell(shown, JgsTreeActions.NodeCollapse, node.Source);
            }
        };
        shown.TreeItems[node.Source] = item;
        shown.TreeFrames[node.Source] = node;
        foreach (UiTreeNodeFrame child in node.Children)
        {
            item.Items.Add(BuildNode(shown, child, checkBoxes, level + 1));
        }

        return item;
    }

    /// <summary>Marks the nodes of a multiselect tree that are selected beyond the one WPF holds.</summary>
    private static void PaintTreeSelection(Shown shown)
    {
        foreach ((UiTreeNodeModel node, TreeViewItem item) in shown.TreeItems)
        {
            bool picked = shown.TreeSelection.Contains(node);
            if (item.Header is StackPanel header)
            {
                header.Tag = picked ? "selected" : null;
                foreach (TextBlock label in header.Children.OfType<TextBlock>())
                {
                    label.TextDecorations = picked && !item.IsSelected ? TextDecorations.Underline : null;
                }
            }
        }
    }

    // --- styles on a list's or a drop-down's items -----------------------------------------------------

    private static void ApplyItemStyles(ItemsControl list, UiComponentFrame frame, Brush foreground)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.Items[i] is not ContentControl item)
            {
                continue;
            }

            UiStyle styled = UiStyleRule.Combine(frame.Styles.Where(rule => rule.CoversItem(i + 1)));
            item.Background = BrushOf(styled.BackgroundColor) ?? Brushes.Transparent;
            item.Foreground = BrushOf(styled.FontColor) ?? foreground;
            item.FontWeight = styled.FontWeight == "bold" ? FontWeights.Bold : styled.FontWeight == "normal" ? FontWeights.Normal : list.FontWeight;
            item.FontStyle = styled.FontAngle == "italic" ? FontStyles.Italic : styled.FontAngle == "normal" ? FontStyles.Normal : list.FontStyle;
            if (styled.FontName.Length > 0)
            {
                item.FontFamily = new FontFamily(UiLayout.FontFamily(styled.FontName));
            }

            item.HorizontalContentAlignment = styled.HorizontalAlignment switch
            {
                "center" => System.Windows.HorizontalAlignment.Center,
                "right" => System.Windows.HorizontalAlignment.Right,
                "left" => System.Windows.HorizontalAlignment.Left,
                _ => System.Windows.HorizontalAlignment.Stretch,
            };
        }
    }

    // --- a component's context menu ---------------------------------------------------------------------

    /// <summary>
    /// Gives a component the right-click menu its <c>ContextMenu</c> names, built when the press
    /// comes: the entries as they are then, the opening callback queued, a pick queued like any
    /// other. A tree looks first for the menu of the node under the mouse.
    /// </summary>
    private void AttachContextMenu(Shown shown)
    {
        shown.Element.ContextMenuOpening += (_, e) =>
        {
            GraphObject target = shown.Source;
            if (shown.Tree is not null && NodeItemUnder(e.OriginalSource as DependencyObject)?.Tag is UiTreeNodeModel node
                && ScriptGraphicsCallbacks.ResolveContextMenu(node) is not null)
            {
                target = node;
            }

            if (ScriptGraphicsCallbacks.ResolveContextMenu(target) is not { } menu)
            {
                return;
            }

            e.Handled = true;
            Point at = Mouse.GetPosition(this);
            ScriptGraphicsCallbacks.NotifyContextMenuOpening(menu, target, (at.X, ActualHeight - at.Y));
            var built = new ContextMenu { PlacementTarget = shown.Element };
            foreach (UiMenuFrame entry in UiFrame.TakeMenus(menu.Items))
            {
                if (entry.Separator && built.Items.Count > 0)
                {
                    built.Items.Add(new Separator());
                }

                built.Items.Add(MenuEntry(entry));
            }

            built.IsOpen = built.Items.Count > 0;
        };
    }

    private static MenuItem MenuEntry(UiMenuFrame frame)
    {
        var item = new MenuItem
        {
            Header = frame.Text.Replace("&", "_", StringComparison.Ordinal),
            IsChecked = frame.Checked,
            IsEnabled = frame.Enabled,
            ToolTip = frame.Tooltip.Length > 0 ? frame.Tooltip : null,
        };
        AutomationProperties.SetAutomationId(item, frame.Tag);
        AutomationProperties.SetName(item, frame.Text.Replace("&", string.Empty, StringComparison.Ordinal));
        MenuItemModel source = frame.Source;
        if (frame.Items.Count == 0)
        {
            item.Click += (_, _) => ScriptGraphicsCallbacks.NotifyMenuSelected(source);
            return item;
        }

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

            item.Items.Add(MenuEntry(child));
        }

        return item;
    }

    /// <summary><c>open(cm, x, y)</c>: a context menu a script asked to show, at a point of the figure.</summary>
    private void ApplyMenuOpens(UiFrame frame)
    {
        foreach (UiMenuOpenFrame request in frame.MenuOpens)
        {
            if (_menuOpensSeen.TryGetValue(request.Source, out int seen) && seen >= request.Requests)
            {
                continue;
            }

            _menuOpensSeen[request.Source] = request.Requests;
            var built = new ContextMenu
            {
                PlacementTarget = this,
                Placement = PlacementMode.RelativePoint,
                HorizontalOffset = request.X,
                VerticalOffset = ActualHeight - request.Y,
            };
            foreach (UiMenuFrame entry in request.Items)
            {
                if (entry.Separator && built.Items.Count > 0)
                {
                    built.Items.Add(new Separator());
                }

                built.Items.Add(MenuEntry(entry));
            }

            if (built.Items.Count > 0)
            {
                built.IsOpen = true;
            }
        }
    }

    private readonly Dictionary<ContextMenuModel, int> _menuOpensSeen = new(ReferenceEqualityComparer.Instance);
}

/// <summary>The actions a tree reports, spelled as the script side's event preparer expects them.</summary>
internal static class JgsTreeActions
{
    public const string NodeSelect = "nodeselect";
    public const string NodeExpand = "nodeexpand";
    public const string NodeCollapse = "nodecollapse";
    public const string NodeText = "nodetext";
    public const string NodeCheck = "nodecheck";
}
