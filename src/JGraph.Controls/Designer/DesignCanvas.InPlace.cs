using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JGraph.Core.Model;
using JGraph.Core.Primitives;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <content>
/// In-place text editing (App Designer window plan, stage W5b): the box App Designer's
/// <c>InPlaceEditorManager</c> lays over a component - a single line, lines, a text area, a list's
/// items, a hyperlink's text with its URL and Visit under it - outlined 2 pixels, in the component's
/// font, growing with what is typed; its keys as <see cref="DesignInPlace.KeyAction"/> says; saved when
/// it loses the keyboard, and by a zoom; closed unsaved by the wheel and the canvas resizing; and an
/// image's file chosen in R2025b's Open Image File dialog.
/// </content>
public sealed partial class DesignCanvas
{
    /// <summary>The editor's outline, outside the place it edits (<c>_INPLACE_EDITOR_OUTLINE_WIDTH</c>).</summary>
    private const double InPlaceOutline = 2;

    /// <summary>The narrowest a lines editor is (<c>_getEditorNodeWidth</c>).</summary>
    private const double InPlaceMinWidth = 15;

    private readonly Canvas _inPlaceHost = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private DesignInPlaceEdit? _inPlaceShown;
    private TextBox? _inPlaceBox;
    private TextBox? _inPlaceUrl;
    private Button? _inPlaceVisit;
    private bool _inPlaceClosing;

    /// <summary>Whether an in-place editor is open on the canvas.</summary>
    public bool IsEditingInPlace => _inPlaceBox is not null;

    /// <summary>The in-place edit open, or null.</summary>
    public DesignInPlaceEdit? InPlaceEdit => _gestures?.InPlace;

    /// <summary>The text an open in-place editor holds; null when none is open.</summary>
    public string? InPlaceText => _inPlaceBox?.Text;

    /// <summary>Opens the in-place editor on the one selected component, as F2 does.</summary>
    /// <returns>Whether one opened.</returns>
    public bool EditSelectionInPlace() => _gestures?.KeyDown(DesignKey.EditInPlace, DesignModifiers.None) == true;

    /// <summary>
    /// Where the component layer drew <paramref name="component"/>'s places to edit, on the surface: a
    /// tab's heading, a scale's end labels, a switch's or a discrete knob's items, a table's column
    /// headings, a tree node's row (W5c); null where the document's own estimate stands.
    /// </summary>
    private IReadOnlyList<Rect2D>? InPlaceHotspots(DesignComponent component)
    {
        static Rect2D On(Rect r) => new(r.X, r.Y, r.Width, r.Height);
        return _document?.Runtime.ObjectOf(component) switch
        {
            UiTabModel tab => _layer.TabHeadingBoxOf(tab) is { } heading ? [On(heading)] : null,
            UiTreeNodeModel node => _layer.TreeNodeBoxOf(node) is { } row ? [On(row)] : null,
            UiComponentModel model => _layer.InPlaceBoxesOf(model)?.Select(On).ToArray(),
            _ => null,
        };
    }

    /// <summary>Shows the edit the gestures have open, and takes down one they have closed.</summary>
    private void SyncInPlace()
    {
        DesignInPlaceEdit? edit = _gestures?.InPlace;
        if (ReferenceEquals(edit, _inPlaceShown))
        {
            return;
        }

        RemoveInPlaceUi();
        _inPlaceShown = edit;
        if (edit is null)
        {
            return;
        }

        if (edit.Kind == DesignInPlaceKind.FileBrowser)
        {
            Dispatcher.BeginInvoke(() => ChooseImageFile(edit));
            return;
        }

        ShowInPlace(edit);
    }

    /// <summary>An image's picture chosen as R2025b's <c>ImageUtil.showImageFilePicker</c> chooses one; a refusal said in App Designer's alert.</summary>
    private void ChooseImageFile(DesignInPlaceEdit edit)
    {
        if (_gestures is not { } gestures || !ReferenceEquals(gestures.InPlace, edit))
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Image File",
            Filter = "All Image Files|*.gif;*.jpeg;*.jpg;*.png;*.svg|Graphics Interchange Format|*.gif|Joint Photographic Experts Group|*.jpg;*.jpeg"
                + "|Portable Network Graphics|*.png|Scalable Vector Graphics|*.svg|All Files|*.*",
        };
        string? path = dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        if (gestures.ChooseImage(path) is { } refused)
        {
            Alerted?.Invoke(this, ("App Designer", refused));
        }
    }

    private void ShowInPlace(DesignInPlaceEdit edit)
    {
        DesignDocument document = _document!;
        Rect2D at = edit.Box;
        bool single = edit.Kind == DesignInPlaceKind.SingleLine;
        double size = document.ValueOf(edit.Component, "FontSize") is NumericValue { Data: [var points] } && points > 0 ? points : 12;
        string font = document.ValueOf(edit.Component, "FontName") is CharValue { Text.Length: > 0 } name ? name.Text : "Helvetica";
        bool bold = document.ValueOf(edit.Component, "FontWeight") is CharValue { Text: "bold" };
        bool italic = document.ValueOf(edit.Component, "FontAngle") is CharValue { Text: "italic" };

        // A label's or a button's lines stand where it shows its text, up and down (the text node's place).
        VerticalAlignment upright = single ? VerticalAlignment.Center
            : edit.Kind is DesignInPlaceKind.MultiLine or DesignInPlaceKind.Hyperlink
                ? document.ValueOf(edit.Component, "VerticalAlignment") switch
                {
                    CharValue { Text: "top" } => VerticalAlignment.Top,
                    CharValue { Text: "bottom" } => VerticalAlignment.Bottom,
                    _ => VerticalAlignment.Center,
                }
                : VerticalAlignment.Top;

        Brush accent = TryFindResource(Themes.ThemeKeys.Accent) as Brush ?? Brushes.DodgerBlue;
        var box = new TextBox
        {
            Text = edit.Text,
            AcceptsReturn = false,
            AcceptsTab = false,
            TextWrapping = edit.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextAlignment = edit.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left },
            VerticalContentAlignment = upright,
            Padding = new System.Windows.Thickness(0),
            BorderThickness = new System.Windows.Thickness(InPlaceOutline),
            BorderBrush = accent,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            FontFamily = new FontFamily(JGraph.Rendering.Layout.UiLayout.FontFamily(font)),
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            MinWidth = System.Math.Max(at.Width, single ? 0 : InPlaceMinWidth) + (2 * InPlaceOutline),
            MinHeight = (single ? System.Math.Max(at.Height, size + 6) : at.Height) + (2 * InPlaceOutline),
            SpellCheck = { IsEnabled = false },
        };
        if (edit.WordWrap)
        {
            // Wrapped lines keep the place's width and grow down (pre-wrap).
            box.Width = box.MinWidth;
        }

        System.Windows.Automation.AutomationProperties.SetAutomationId(box, "Canvas.InPlaceEditor");
        System.Windows.Automation.AutomationProperties.SetName(box, edit.Component.CodeName + "." + edit.Property);
        Canvas.SetLeft(box, Gutter + at.X - InPlaceOutline);
        Canvas.SetTop(box, Gutter + at.Y - InPlaceOutline);
        box.PreviewKeyDown += (_, e) => InPlaceKey(e, DesignInPlaceField.Text);
        box.KeyDown += (_, e) => e.Handled = true; // nothing typed in the editor reaches the canvas's keys
        box.LostKeyboardFocus += (_, e) => InPlaceBlurred(e);
        _inPlaceHost.Children.Add(box);
        _inPlaceBox = box;

        if (edit.Kind == DesignInPlaceKind.Hyperlink)
        {
            ShowUrl(edit, box);
        }

        box.Focus();
        Keyboard.Focus(box);
        if (edit.Seed is not null)
        {
            box.CaretIndex = box.Text.Length;
        }
        else
        {
            box.SelectAll();
        }

        DesignChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A hyperlink's URL box and Visit link under its text (<c>ad-hyperlink-inplace-editor</c>).</summary>
    private void ShowUrl(DesignInPlaceEdit edit, TextBox text)
    {
        var url = new TextBox { Text = edit.Url ?? string.Empty, MinWidth = 160, Margin = new System.Windows.Thickness(4) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(url, "Canvas.InPlaceEditor.URL");
        System.Windows.Automation.AutomationProperties.SetName(url, "Enter URL");
        var placeholder = new TextBlock { Text = "Enter URL", Opacity = 0.55, IsHitTestVisible = false, Margin = new System.Windows.Thickness(8, 6, 4, 4) };
        var visit = new Button
        {
            Content = "Visit",
            Margin = new System.Windows.Thickness(4),
            Padding = new System.Windows.Thickness(2, 0, 2, 0),
            Background = Brushes.Transparent,
            BorderThickness = new System.Windows.Thickness(0),
            Foreground = TryFindResource(Themes.ThemeKeys.Accent) as Brush ?? Brushes.DodgerBlue,
            Cursor = Cursors.Hand,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(visit, "Canvas.InPlaceEditor.Visit");
        void Validate()
        {
            bool valid = DesignInPlace.IsValidUrl(url.Text);
            visit.IsEnabled = valid && url.Text.Length > 0;
            url.BorderBrush = valid ? SystemColors.ControlDarkBrush : Brushes.Red;
            placeholder.Visibility = url.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        url.TextChanged += (_, _) => Validate();
        url.PreviewKeyDown += (_, e) => InPlaceKey(e, DesignInPlaceField.Url);
        url.KeyDown += (_, e) => e.Handled = true;
        url.LostKeyboardFocus += (_, e) => InPlaceBlurred(e);
        visit.PreviewKeyDown += (_, e) => InPlaceKey(e, DesignInPlaceField.Visit);
        visit.KeyDown += (_, e) => e.Handled = true;
        visit.LostKeyboardFocus += (_, e) => InPlaceBlurred(e);
        visit.Click += (_, _) => VisitUrl();
        Validate();

        var field = new Grid();
        field.Children.Add(url);
        field.Children.Add(placeholder);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(field);
        row.Children.Add(visit);
        var popup = new Border
        {
            Child = row,
            Background = Brushes.White,
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new System.Windows.Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.3 },
        };
        Canvas.SetLeft(popup, Canvas.GetLeft(text));
        Canvas.SetTop(popup, Canvas.GetTop(text) + text.MinHeight + 4);
        _inPlaceHost.Children.Add(popup);
        _inPlaceUrl = url;
        _inPlaceVisit = visit;
    }

    /// <summary>A key in one of the editor's boxes, as App Designer's editor takes it.</summary>
    private void InPlaceKey(KeyEventArgs e, DesignInPlaceField field)
    {
        if (_gestures?.InPlace is not { } edit || _inPlaceBox is not { } box)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        DesignInPlaceKey? pressed = key switch
        {
            Key.Enter => DesignInPlaceKey.Enter,
            Key.Escape => DesignInPlaceKey.Escape,
            Key.Tab => DesignInPlaceKey.Tab,
            Key.Z => DesignInPlaceKey.Z,
            _ => null,
        };
        if (pressed is not { } inPlaceKey)
        {
            return;
        }

        DesignInPlaceAction action = DesignInPlace.KeyAction(edit.Kind, inPlaceKey, Modifiers(), field);
        switch (action)
        {
            case DesignInPlaceAction.None:
                return;
            case DesignInPlaceAction.Swallow:
                break;
            case DesignInPlaceAction.NewLine:
                if (e.OriginalSource is TextBox target)
                {
                    int caret = target.SelectionStart;
                    target.SelectedText = "\n";
                    target.CaretIndex = caret + 1;
                }

                break;
            case DesignInPlaceAction.Revert:
                if (field == DesignInPlaceField.Url && _inPlaceUrl is { } url)
                {
                    url.Text = edit.Url ?? string.Empty;
                }
                else
                {
                    box.Text = string.Join('\n', edit.Lines);
                    box.CaretIndex = box.Text.Length;
                }

                break;
            case DesignInPlaceAction.Save:
                CloseInPlace(save: true, focusCanvas: true);
                break;
            case DesignInPlaceAction.SaveNext or DesignInPlaceAction.SavePrevious:
                CloseInPlace(save: true, next: action == DesignInPlaceAction.SavePrevious, focusCanvas: true);
                break;
            case DesignInPlaceAction.Cancel:
                CloseInPlace(save: false, focusCanvas: true);
                break;
            case DesignInPlaceAction.ToUrl:
                _inPlaceUrl?.Focus();
                break;
            case DesignInPlaceAction.ToText:
                box.Focus();
                break;
            case DesignInPlaceAction.ToVisit:
                if (_inPlaceVisit is { IsEnabled: true } visit)
                {
                    visit.Focus();
                }

                break;
            case DesignInPlaceAction.Visit:
                VisitUrl();
                break;
        }

        e.Handled = true;
    }

    /// <summary>A box of the editor lost the keyboard: unless to another of its boxes, what it holds is saved (the blur listener).</summary>
    private void InPlaceBlurred(KeyboardFocusChangedEventArgs e)
    {
        if (_inPlaceClosing || _inPlaceBox is null)
        {
            return;
        }

        if (e.NewFocus is DependencyObject to && (ReferenceEquals(to, _inPlaceBox) || ReferenceEquals(to, _inPlaceUrl) || ReferenceEquals(to, _inPlaceVisit)))
        {
            return;
        }

        CloseInPlace(save: true);
    }

    /// <summary>Opens a hyperlink's URL in the browser, when it is one its editor saves (<c>_visitURL</c>).</summary>
    private void VisitUrl()
    {
        if (_inPlaceUrl is not { } url || url.Text.Length == 0 || !DesignInPlace.IsValidUrl(url.Text))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.Text) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Problem?.Invoke(this, ex.Message);
        }
    }

    /// <summary>
    /// Closes the in-place editor: <paramref name="save"/> saves what it holds as one edit; Tab's
    /// <paramref name="next"/> then opens the component's next (or previous) place once the canvas has
    /// drawn the change; a key that closed it gives the canvas the keyboard back.
    /// </summary>
    public void CloseInPlace(bool save, bool? next = null, bool focusCanvas = false)
    {
        if (_inPlaceClosing || _gestures is not { } gestures)
        {
            return;
        }

        if (_inPlaceBox is null && gestures.InPlace is null)
        {
            return;
        }

        _inPlaceClosing = true;
        try
        {
            string text = _inPlaceBox?.Text ?? string.Empty;
            string? url = _inPlaceUrl?.Text;
            DesignInPlaceEdit? edit = gestures.InPlace;
            if (focusCanvas)
            {
                Focus();
            }

            (string? refused, int? following) = gestures.EndInPlace(save && _inPlaceBox is not null, text, url, next);
            RemoveInPlaceUi();
            _inPlaceShown = null;
            if (refused is not null && edit is not null)
            {
                Problem?.Invoke(this, $"{edit.Component.CodeName}.{edit.Property}: {refused}");
            }

            if (following is { } hotspot && edit is not null)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    if (ReferenceEquals(_gestures, gestures) && gestures.InPlace is null)
                    {
                        gestures.BeginInPlace(edit.Component, hotspot: hotspot);
                    }
                });
            }
        }
        finally
        {
            _inPlaceClosing = false;
        }
    }

    private void RemoveInPlaceUi()
    {
        if (_inPlaceBox is null && _inPlaceHost.Children.Count == 0)
        {
            return;
        }

        bool wasClosing = _inPlaceClosing;
        _inPlaceClosing = true;
        try
        {
            bool hadKeyboard = _inPlaceHost.IsKeyboardFocusWithin;
            _inPlaceHost.Children.Clear();
            _inPlaceBox = null;
            _inPlaceUrl = null;
            _inPlaceVisit = null;
            if (hadKeyboard)
            {
                Focus();
            }
        }
        finally
        {
            _inPlaceClosing = wasClosing;
        }

        DesignChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);

        // A character typed with one component selected opens its editor on it (DesignAreaKeyboardController).
        if (!e.Handled && !IsEditingInPlace && _gestures?.TypeText(e.Text) == true)
        {
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        // The window resized closes the editor unsaved (_addOnceResizeListener).
        CloseInPlace(save: false);
    }
}
