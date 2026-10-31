using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using JGraph.Core.Model;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <content>
/// The inspector's own editors (the App Designer window plan's stage W7c), as R2025b's sheets name them: a text area for lines,
/// toggle buttons, a font's lists, a spinner, a file's Browse, a colour with its picker, and behind each row's kebab the tables
/// and fields of its rich editor (<c>InspectorPropertyEditorGroup</c>).
/// </content>
public sealed partial class DesignInspectorView
{
    private Row BuildEditor(DesignEditor editor, string id)
    {
        switch (editor.Kind)
        {
            case DesignEditorKind.Toggle:
                return ToggleRow(editor, id);
            case DesignEditorKind.ToggleGroup:
                return ToggleGroupRow(editor, id);
            case DesignEditorKind.FontName:
                return ComboRow(editor, id, [.. UiFonts.Families()], text => new CharValue(text));
            case DesignEditorKind.FontSize:
                return ComboRow(editor, id, editor.Choices, text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? NumericValue.Scalar(n) : new CharValue(text));
            case DesignEditorKind.Spinner:
                return SpinnerRow(editor, id);
            case DesignEditorKind.File:
                return FileRow(editor, id);
            case DesignEditorKind.Color:
                return ColorRow(editor, id);
            case DesignEditorKind.Date:
                return DateRow(editor, id);
            case DesignEditorKind.Lines:
            case DesignEditorKind.Items:
                return LinesRow(editor, id);
            default:
                return RichRow(editor, id);
        }
    }

    // --- a text area ------------------------------------------------------------------------------------

    // TextBoxEditor showing the value, and TextAreaEditor editing it: lines while it has the focus, and committed as it loses it.
    private Row LinesRow(DesignEditor editor, string id)
    {
        var box = new TextBox { Margin = new Thickness(0, 1, 0, 1), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MaxLines = 8, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Identify(box, id, editor.Name);
        List<string> lines = [];
        bool raw = editor.ClassName.EndsWith("NonDelimitedMultlineText", StringComparison.Ordinal);
        string Editing() => DesignTextArea.Edited(lines);
        string Resting() => editor.Kind == DesignEditorKind.Items || lines.Count > 1 ? DesignTextArea.Shown(lines) : Editing();
        bool editing = false;
        box.GotKeyboardFocus += (_, _) =>
        {
            editing = true;
            if (box.Text == Resting())
            {
                box.Text = Editing();
                box.CaretIndex = box.Text.Length;
            }
        };
        void Commit()
        {
            if (!_refreshing && box.Text.Replace("\r\n", "\n", StringComparison.Ordinal) != Editing())
            {
                Set(editor.Name, LinesValue(editor, box.Text, raw));
            }
        }

        box.LostKeyboardFocus += (_, _) =>
        {
            Commit();
            editing = false;
            box.Text = Resting();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                box.Text = Editing();
                e.Handled = true;
            }
        };
        FrameworkElement control = editor.Kind == DesignEditorKind.Items ? WithKebab(editor, id, box) : box;
        return new Row(editor, control, value =>
        {
            lines = DesignEditorValues.Lines(value);
            if (!editing)
            {
                box.Text = Resting();
            }
        });
    }

    private static DesignValue LinesValue(DesignEditor editor, string typed, bool raw)
    {
        string text = typed.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (raw)
        {
            return new CharValue(text);
        }

        if (editor.Name == "ItemsData" || editor.ClassName.EndsWith("ItemsValue", StringComparison.Ordinal))
        {
            return editor.Name == "Value" && !editor.ClassName.Contains("Multiple", StringComparison.Ordinal)
                ? new CharValue(text.TrimEnd('\n'))
                : DesignEditorValues.ItemsData(text);
        }

        string[] lines = DesignTextArea.Lines(text);
        return editor.Kind == DesignEditorKind.Items || editor.Name == "MajorTickLabels" ? DesignEditorValues.Items(lines) : DesignEditorValues.Text(lines);
    }

    // --- buttons --------------------------------------------------------------------------------------------

    // BinaryToggleButtonEditor: its icon's button, the second word when on.
    private Row ToggleRow(DesignEditor editor, string id)
    {
        IReadOnlyList<string> words = editor.Info.Enumerated.Count == 2 ? editor.Info.Enumerated : ["normal", "bold"];
        var button = new ToggleButton
        {
            Width = 24,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0),
            Content = words[1] == "italic" ? new TextBlock { Text = "I", FontStyle = FontStyles.Italic } : new TextBlock { Text = "B", FontWeight = FontWeights.Bold },
            ToolTip = "'" + words[1] + "'",
        };
        Identify(button, id, editor.Name);
        RoutedEventHandler changed = (_, _) =>
        {
            if (!_refreshing)
            {
                Set(editor.Name, new CharValue(words[button.IsChecked == true ? 1 : 0]));
            }
        };
        button.Checked += changed;
        button.Unchecked += changed;
        return new Row(editor, button, value => button.IsChecked = (value as CharValue)?.Text == words[1]);
    }

    // ToggleButtonGroupEditor: a button for each word, one always on.
    private Row ToggleGroupRow(DesignEditor editor, string id)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        var buttons = new Dictionary<string, ToggleButton>(StringComparer.Ordinal);
        IReadOnlyList<string> words = editor.Info.Enumerated;
        for (int i = 0; i < words.Count; i++)
        {
            string word = words[i];
            var button = new ToggleButton
            {
                Width = 24,
                Height = 22,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 2, 0),
                ToolTip = "'" + word + "'",
                Content = GroupIcon(i < editor.Info.Icons.Count ? editor.Info.Icons[i] : string.Empty, word),
            };
            Identify(button, id + "." + word, word);
            buttons[word] = button;
            button.Checked += (_, _) =>
            {
                foreach ((string other, ToggleButton b) in buttons)
                {
                    if (other != word)
                    {
                        b.IsChecked = false;
                    }
                }

                if (!_refreshing)
                {
                    Set(editor.Name, new CharValue(word));
                }
            };
            button.Unchecked += (_, _) =>
            {
                // Pressing the one that is on leaves it on.
                if (!_refreshing && !buttons.Values.Any(static b => b.IsChecked == true))
                {
                    button.IsChecked = true;
                }
            };
            panel.Children.Add(button);
        }

        System.Windows.Automation.AutomationProperties.SetName(panel, editor.Name);
        return new Row(editor, panel, value =>
        {
            string held = (value as CharValue)?.Text ?? string.Empty;
            foreach ((string word, ToggleButton b) in buttons)
            {
                b.IsChecked = word == held;
            }
        });
    }

    // JGraph's own marks for R2025b's icons: three bars for an alignment, and else the word's first letters.
    private static FrameworkElement GroupIcon(string icon, string word)
    {
        string name = icon.ToLowerInvariant();
        if (!name.Contains("align", StringComparison.Ordinal))
        {
            return new TextBlock { Text = word.Length > 2 ? word[..2] : word, FontSize = 10 };
        }

        var bars = new StackPanel { Width = 14 };
        bool across = name.Contains("left", StringComparison.Ordinal) || name.Contains("right", StringComparison.Ordinal) || name.Contains("center", StringComparison.Ordinal);
        for (int i = 0; i < 3; i++)
        {
            bars.Children.Add(new Border
            {
                Height = 2,
                Width = !across || i != 1 ? 14 : 9,
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalAlignment = name.Contains("left", StringComparison.Ordinal) ? HorizontalAlignment.Left : name.Contains("right", StringComparison.Ordinal) ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                Background = SystemColors.ControlTextBrush,
            });
        }

        return bars;
    }

    // --- lists ------------------------------------------------------------------------------------------------

    // A list that takes what is typed too (FontNameEditor, FontSizeEditor: isProtected false).
    private Row ComboRow(DesignEditor editor, string id, IReadOnlyList<string> choices, Func<string, DesignValue> value)
    {
        var combo = new ComboBox { Margin = new Thickness(0, 1, 0, 1), IsEditable = true, IsTextSearchEnabled = true };
        Identify(combo, id, editor.Name);
        foreach (string choice in choices)
        {
            combo.Items.Add(choice);
        }

        string shown = string.Empty;
        void Commit()
        {
            if (!_refreshing && combo.Text != shown)
            {
                Set(editor.Name, value(combo.Text));
            }
        }

        combo.SelectionChanged += (_, e) =>
        {
            if (!_refreshing && combo.SelectedItem is string chosen && chosen != shown)
            {
                Set(editor.Name, value(chosen));
            }
        };
        combo.LostKeyboardFocus += (_, e) =>
        {
            // The keyboard going to the list's own text box is not leaving it.
            if (e.NewFocus is not DependencyObject next || !combo.IsAncestorOf(next))
            {
                Commit();
            }
        };
        combo.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
        return new Row(editor, combo, v =>
        {
            shown = v switch
            {
                NumericValue { Data: [var d] } => DesignDocument.NumberText(d),
                CharValue c => c.Text,
                StringValue s => s.Text,
                _ => string.Empty,
            };
            combo.Text = shown;
        });
    }

    // --- a number -------------------------------------------------------------------------------------------

    // SpinnerEditor: the number, and its arrows by its step within its limits.
    private Row SpinnerRow(DesignEditor editor, string id)
    {
        var box = new TextBox { Margin = new Thickness(0, 1, 0, 1) };
        Identify(box, id, editor.Name);
        var up = new RepeatButton { Content = "▴", Padding = new Thickness(0), FontSize = 8, Height = 11, Width = 16 };
        var down = new RepeatButton { Content = "▾", Padding = new Thickness(0), FontSize = 8, Height = 11, Width = 16 };
        Identify(up, id + ".Up", "Up");
        Identify(down, id + ".Down", "Down");
        double number = 0;
        void Step(double by)
        {
            double next = number + by;
            if (editor.Info.Minimum is { } low)
            {
                next = System.Math.Max(next, low);
            }

            if (editor.Info.Maximum is { } high)
            {
                next = System.Math.Min(next, high);
            }

            Set(editor.Name, NumericValue.Scalar(next));
        }

        up.Click += (_, _) => Step(editor.Info.Step);
        down.Click += (_, _) => Step(-editor.Info.Step);
        string current = string.Empty;
        Commits(box, () => current, text => Set(editor.Name, double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? NumericValue.Scalar(n) : new CharValue(text)));
        var arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        arrows.Children.Add(up);
        arrows.Children.Add(down);
        var dock = new DockPanel();
        DockPanel.SetDock(arrows, Dock.Right);
        dock.Children.Add(arrows);
        dock.Children.Add(box);
        return new Row(editor, dock, value =>
        {
            number = value is NumericValue { Data: [var d] } ? d : 0;
            current = DesignDocument.NumberText(number);
            box.Text = current;
        });
    }

    // --- a file ----------------------------------------------------------------------------------------------

    // FilePickerEditor: the path, and Browse.
    private Row FileRow(DesignEditor editor, string id)
    {
        var box = new TextBox { Margin = new Thickness(0, 1, 0, 1) };
        Identify(box, id, editor.Name);
        var browse = new Button { Content = "Browse", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(3, 1, 0, 1) };
        Identify(browse, id + ".Browse", "Browse");
        string current = string.Empty;
        Commits(box, () => current, text => Set(editor.Name, new CharValue(text.Trim())));
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open Image File",
                Filter = "All Image Files|*.gif;*.jpeg;*.jpg;*.png;*.svg|Graphics Interchange Format|*.gif|Joint Photographic Experts Group|*.jpg;*.jpeg"
                    + "|Portable Network Graphics|*.png|Scalable Vector Graphics|*.svg|All Files|*.*",
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true && _document is { } document)
            {
                Set(editor.Name, document.ImageValue(dialog.FileName));
            }
        };
        var dock = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        dock.Children.Add(browse);
        dock.Children.Add(box);
        return new Row(editor, dock, value =>
        {
            current = DesignDocument.ImagePathText(value);
            box.Text = current;
        });
    }

    // --- a colour -----------------------------------------------------------------------------------------------

    // RGBColorEditor: the numbers in a box, and beside them the swatch that opens the picker; a word it offers (none) as itself.
    private Row ColorRow(DesignEditor editor, string id)
    {
        var box = new TextBox { Margin = new Thickness(0, 1, 0, 1) };
        Identify(box, id, editor.Name);
        var swatch = new Button
        {
            Width = 18,
            Height = 18,
            Padding = new Thickness(0),
            Margin = new Thickness(3, 0, 0, 0),
            BorderBrush = Brushes.Gray,
            ToolTip = "Colors",
        };
        Identify(swatch, id + ".Picker", "Colors");
        string current = string.Empty;
        double[] held = [1, 1, 1];
        DesignValue Typed(string text) => DesignColors.Parse(text) is { } rgb ? NumericValue.Row(rgb) : new CharValue(text.Trim());
        Commits(box, () => current, text => Set(editor.Name, Typed(text)));
        var popup = new Popup { PlacementTarget = swatch, Placement = PlacementMode.Bottom, StaysOpen = false };
        swatch.Click += (_, _) =>
        {
            popup.Child = DesignRichEditors.Framed(new DesignColorPickerView(held, editor.Info.StyleOptions, id, picked =>
            {
                popup.IsOpen = false;
                Set(editor.Name, picked);
            }));
            popup.IsOpen = true;
        };
        var dock = new DockPanel();
        DockPanel.SetDock(swatch, Dock.Right);
        dock.Children.Add(swatch);
        dock.Children.Add(box);
        dock.Children.Add(popup);
        return new Row(editor, dock, value =>
        {
            if (value is NumericValue { Data: [var r, var g, var b] })
            {
                held = [r, g, b];
                current = DesignColors.Text(held);
                swatch.Background = DesignRichEditors.Brush(held);
            }
            else
            {
                current = (value as CharValue)?.Text ?? string.Empty;
                swatch.Background = Brushes.Transparent;
            }

            box.Text = current;
        });
    }

    // --- a date ------------------------------------------------------------------------------------------------

    // DatePickerEditor: a date, or none for NaT.
    private Row DateRow(DesignEditor editor, string id)
    {
        var picker = new DatePicker { Margin = new Thickness(0, 1, 0, 1) };
        Identify(picker, id, editor.Name);
        picker.SelectedDateChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                Set(editor.Name, DesignEditorValues.DatesValue([picker.SelectedDate is { } d ? new DesignDate(d.Year, d.Month, d.Day) : null]));
            }
        };
        return new Row(editor, picker, value =>
        {
            picker.SelectedDate = value is DatetimeValue { Items: [{ } date] } ? new DateTime(date.Year == 0 ? 1 : date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified) : null;
        });
    }

    // --- a row with a rich editor -----------------------------------------------------------------------------------

    // The numbers or words in a box, and behind the kebab its rich editor.
    private Row RichRow(DesignEditor editor, string id)
    {
        var box = new TextBox { Margin = new Thickness(0, 1, 0, 1) };
        Identify(box, id, editor.Name);
        string current = string.Empty;
        bool typed = editor.Kind is not (DesignEditorKind.DateLimits or DesignEditorKind.DisabledDates);
        box.IsReadOnly = !typed;
        if (typed)
        {
            Commits(box, () => current, text => Typed(editor, text));
        }

        return new Row(editor, WithKebab(editor, id, box), value =>
        {
            bool untouched = box.Text == current || !box.IsKeyboardFocusWithin;
            current = RichText(editor, value);
            if (untouched)
            {
                box.Text = current;
            }
        });
    }

    private static string RichText(DesignEditor editor, DesignValue? value) => (editor.Kind, value) switch
    {
        (_, null) => string.Empty,
        (DesignEditorKind.Ticks, CellValue) or (DesignEditorKind.Columns, CellValue) => DesignTextArea.Shown(DesignEditorValues.Lines(value)),
        (DesignEditorKind.DateLimits or DesignEditorKind.DisabledDates, DatetimeValue dates) => string.Join(",", dates.Items.Select(DesignEditorValues.DateText)),
        (DesignEditorKind.ViewFormat, CharValue format) => format.Text,
        _ => Shown(value!, plain: false),
    };

    // What is typed in a rich editor's row (the *TextBoxEditor's setValue).
    private void Typed(DesignEditor editor, string text)
    {
        switch (editor.Kind)
        {
            case DesignEditorKind.ViewFormat:
                if (!DesignDisplayFormat.IsValid(text))
                {
                    Say("Invalid format specification: " + text);
                    RefreshValues();
                    return;
                }

                Set(editor.Name, new CharValue(text));
                return;
            case DesignEditorKind.Ticks when editor.Name.Contains("Label", StringComparison.Ordinal):
            case DesignEditorKind.Columns when editor.Name == "ColumnName":
                string[] words = text.Split(',').Select(static w => w.Trim().Trim('\'', '"')).ToArray();
                Set(editor.Name, editor.Name == "ColumnName"
                    ? DesignEditorValues.ColumnValue("ColumnName", words.ToList())
                    : DesignEditorValues.Items(words));
                return;
            default:
                Set(editor.Name, DesignEditorValues.Numbers(text));
                return;
        }
    }

    // The kebab beside an editor: its rich editor drops down from it (_addRichEditorAffordance), made anew each time it opens (onOpen).
    private FrameworkElement WithKebab(DesignEditor editor, string id, FrameworkElement control)
    {
        var kebab = new ToggleButton
        {
            Content = "⋮",
            Width = 16,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 1, 0, 1),
            Background = Brushes.Transparent,
            ToolTip = editor.DisplayName,
        };
        Identify(kebab, id + ".Rich", editor.Name + " editor");
        var popup = new Popup { PlacementTarget = kebab, Placement = PlacementMode.Left, StaysOpen = false };
        popup.Opened += (_, _) => kebab.IsHitTestVisible = false; // a press on it while open only closes it
        popup.Closed += (_, _) =>
        {
            kebab.IsChecked = false;
            kebab.IsHitTestVisible = true;
        };
        kebab.Checked += (_, _) =>
        {
            if (_document is { } document && _inspection is { } inspection && DesignRichEditors.For(editor, document, inspection, Commit) is { } rich)
            {
                popup.Child = DesignRichEditors.Framed(rich);
                popup.IsOpen = true;
            }
            else
            {
                kebab.IsChecked = false;
            }
        };
        kebab.Unchecked += (_, _) => popup.IsOpen = false;
        var dock = new DockPanel();
        DockPanel.SetDock(kebab, Dock.Right);
        dock.Children.Add(kebab);
        dock.Children.Add(control);
        dock.Children.Add(popup);
        return dock;
    }

    // A rich editor's change, as one step; a refusal said in the alert.
    private bool Commit(IReadOnlyList<(string Name, DesignValue? Value)> changes)
    {
        if (changes.Count == 0 || _document is not { } document || _inspection is not { } inspection)
        {
            return true;
        }

        if (document.SetInspected(inspection, changes) is { } refused)
        {
            Say(refused);
            RefreshValues();
            return false;
        }

        return true;
    }
}
