using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Controls.Designer;

/// <summary>
/// App Designer's Start Page (App Designer window plan, stage W3), laid out as R2025b's
/// <c>StartScreenLayout</c> lays it out: a header over the page; on the left the open menu - Open...
/// and the recent files (<c>OpenMenu</c>), each by its name with "App" or "Custom UI Component" under
/// it and its full name as its tooltip, or "No Recent Files"; in the centre the links bar and a
/// part per section (<c>AppTypeSectionWidget</c>): its title, a tile per new document, and the
/// "Show examples" toggle over its example groups, hidden until it is clicked. A tile JGraph does
/// not make yet is shown disabled. The page raises what was chosen; its host acts on it.
/// </summary>
public sealed class DesignStartPageView : UserControl
{
    private readonly StackPanel _open = new() { Margin = new Thickness(12, 12, 8, 12) };
    private IReadOnlyList<DesignRecentFile> _recent = [];

    public DesignStartPageView()
    {
        AutomationProperties.SetAutomationId(this, "StartPage");

        // The header: R2025b's branding band (its slideshow is MathWorks' own and is not shown).
        var title = new TextBlock { Text = "App Designer", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(16, 10, 16, 10), VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextOnAccent);
        var header = new Border { Height = 52, Child = title };
        header.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Accent);

        var left = new ScrollViewer { Content = _open, Width = 249, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        left.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        var leftRule = new Border { Width = 1 };
        leftRule.SetResourceReference(Border.BackgroundProperty, ThemeKeys.Border);

        var center = new StackPanel { Margin = new Thickness(20, 12, 20, 20) };
        center.Children.Add(LinksBar());
        int section = 0;
        foreach (DesignStartSection part in DesignStartPage.Sections)
        {
            center.Children.Add(Section(part, section++));
        }

        var centerScroll = new ScrollViewer { Content = center, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        centerScroll.SetResourceReference(BackgroundProperty, ThemeKeys.Surface);

        var body = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(leftRule, Dock.Left);
        body.Children.Add(left);
        body.Children.Add(leftRule);
        body.Children.Add(centerScroll);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(body);
        root.SetResourceReference(BackgroundProperty, ThemeKeys.Surface);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        Content = root;
        FillOpenMenu();
    }

    /// <summary>Raised by a new-document tile JGraph makes.</summary>
    public event EventHandler<DesignNewKind>? NewRequested;

    /// <summary>Raised by Open...</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Raised by a recent file, with its full name.</summary>
    public event EventHandler<string>? RecentRequested;

    /// <summary>Raised by an example's tile.</summary>
    public event EventHandler<DesignExample>? ExampleRequested;

    /// <summary>Raised by a link, with the scripting guide's section it names.</summary>
    public event EventHandler<DesignStartLink>? LinkRequested;

    /// <summary>The recent files the open menu lists, newest first.</summary>
    public IReadOnlyList<DesignRecentFile> RecentFiles
    {
        get => _recent;
        set
        {
            _recent = value ?? [];
            FillOpenMenu();
        }
    }

    /// <summary>Puts the keyboard focus on the Blank App tile, as R2025b's page focuses <c>FocusNodeNewBlankApp</c>.</summary>
    public void FocusFirstTile()
    {
        if (FindTile("AD_NEW") is { } tile)
        {
            tile.Focus();
        }
    }

    private Button? FindTile(string actionId) => FindByAutomationId<Button>(this, "StartPage.Tile." + actionId);

    private static T? FindByAutomationId<T>(DependencyObject root, string id)
        where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node)
            {
                continue;
            }

            if (node is T match && AutomationProperties.GetAutomationId(node) == id)
            {
                return match;
            }

            if (FindByAutomationId<T>(node, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // OpenMenu: the Open... button (116 wide, R2025b's) and the Recent Files list.
    private void FillOpenMenu()
    {
        _open.Children.Clear();
        var open = new Button { Content = DesignStartPage.OpenButton, Width = 116, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 14) };
        AutomationProperties.SetAutomationId(open, "OpenPanel.OpenButton");
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _open.Children.Add(open);

        var heading = new TextBlock { Text = DesignStartPage.RecentFiles, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        heading.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        _open.Children.Add(heading);
        if (_recent.Count == 0)
        {
            var none = new TextBlock { Text = DesignStartPage.NoRecentFiles, Margin = new Thickness(0, 2, 0, 2) };
            AutomationProperties.SetAutomationId(none, "OpenPanel.NoRecentFiles");
            _open.Children.Add(none);
            return;
        }

        for (int i = 0; i < _recent.Count; i++)
        {
            DesignRecentFile file = _recent[i];
            var lines = new StackPanel();
            lines.Children.Add(new TextBlock { Text = System.IO.Path.GetFileNameWithoutExtension(file.FilePath), TextTrimming = TextTrimming.CharacterEllipsis });
            var kind = new TextBlock { Text = DesignStartPage.EntryType(file.AppType), FontSize = 11 };
            kind.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
            lines.Children.Add(kind);
            var item = new Button
            {
                Content = lines,
                ToolTip = file.FilePath,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 0, 2),
                BorderThickness = new Thickness(0),
            };
            item.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
            AutomationProperties.SetAutomationId(item, $"OpenPanel.RecentFile.{i}");
            AutomationProperties.SetName(item, System.IO.Path.GetFileNameWithoutExtension(file.FilePath));
            item.Click += (_, _) => RecentRequested?.Invoke(this, file.FilePath);
            _open.Children.Add(item);
        }
    }

    private WrapPanel LinksBar()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (DesignStartLink link in DesignStartPage.Links)
        {
            var text = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(link.Text));
            AutomationProperties.SetAutomationId(text, "StartPage.Link." + link.GuideAnchor);
            text.Click += (_, _) => LinkRequested?.Invoke(this, link);
            bar.Children.Add(new TextBlock(text) { Margin = new Thickness(0, 0, 18, 0) });
        }

        return bar;
    }

    // AppTypeSectionWidget: the title, the tiles, and "Show examples" over the example groups.
    private StackPanel Section(DesignStartSection part, int index)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(new TextBlock { Text = part.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var tiles = new WrapPanel();
        foreach (DesignStartTile tile in part.Tiles)
        {
            tiles.Children.Add(NewTile(tile));
        }

        panel.Children.Add(tiles);
        if (part.Examples.Count == 0)
        {
            return panel; // R2025b removes the examples part of a section that has none
        }

        var groups = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        foreach (DesignExampleGroup group in part.Examples)
        {
            var heading = new TextBlock { Text = group.Title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) };
            heading.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
            groups.Children.Add(heading);
            var examples = new WrapPanel();
            foreach (DesignExample example in group.Examples)
            {
                examples.Children.Add(ExampleTile(example));
            }

            groups.Children.Add(examples);
        }

        var toggle = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(DesignStartPage.ShowExamples));
        var toggleText = new TextBlock(toggle) { Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetAutomationId(toggle, $"StartPage.Examples.{index}");
        toggle.Click += (_, _) =>
        {
            bool show = groups.Visibility != Visibility.Visible;
            groups.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ((System.Windows.Documents.Run)toggle.Inlines.FirstInline).Text = show ? DesignStartPage.HideExamples : DesignStartPage.ShowExamples;
        };
        panel.Children.Add(toggleText);
        panel.Children.Add(groups);
        return panel;
    }

    private Button NewTile(DesignStartTile tile)
    {
        var content = new StackPanel { Width = 150 };
        content.Children.Add(Thumbnail(tile.Kind));
        content.Children.Add(new TextBlock { Text = tile.Text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Center });
        var button = Tile(content, tile.IsLive
            ? tile.Description
            : $"{tile.Description}. JGraph makes this in stage {tile.Stage} of the App Designer window plan.");
        button.IsEnabled = tile.IsLive;
        AutomationProperties.SetAutomationId(button, "StartPage.Tile." + tile.ActionId);
        AutomationProperties.SetName(button, tile.Text);
        button.Click += (_, _) => NewRequested?.Invoke(this, tile.Kind);
        return button;
    }

    private Button ExampleTile(DesignExample example)
    {
        var content = new StackPanel { Width = 180 };
        content.Children.Add(new TextBlock { Text = example.Title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        var description = new TextBlock { Text = example.Description, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        description.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.TextSecondary);
        content.Children.Add(description);
        Button button = Tile(content, example.Description);
        button.MinHeight = 80;
        AutomationProperties.SetAutomationId(button, "StartPage.Example." + example.Folder);
        AutomationProperties.SetName(button, example.Title);
        button.Click += (_, _) => ExampleRequested?.Invoke(this, example);
        return button;
    }

    private static Button Tile(object content, string tooltip)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 12, 12),
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        button.SetResourceReference(BackgroundProperty, ThemeKeys.Panel);
        button.SetResourceReference(BorderBrushProperty, ThemeKeys.Border);
        return button;
    }

    /// <summary>JGraph's own drawing of what a new document starts as: a window, its panels, or a component's box.</summary>
    private static FrameworkElement Thumbnail(DesignNewKind kind)
    {
        var canvas = new Grid { Width = 120, Height = 80, HorizontalAlignment = HorizontalAlignment.Center };
        bool component = kind is DesignNewKind.BlankComponent or DesignNewKind.BlankComponentWithGrid;
        var frame = new Rectangle { StrokeThickness = 1.5, RadiusX = 2, RadiusY = 2 };
        if (component)
        {
            frame.StrokeDashArray = new DoubleCollection([4, 2]);
        }

        frame.SetResourceReference(Shape.StrokeProperty, ThemeKeys.BorderStrong);
        frame.SetResourceReference(Shape.FillProperty, ThemeKeys.Surface);
        canvas.Children.Add(frame);
        if (!component)
        {
            var bar = new Rectangle { Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1.5) };
            bar.SetResourceReference(Shape.FillProperty, ThemeKeys.Accent);
            canvas.Children.Add(bar);
        }

        int columns = kind switch
        {
            DesignNewKind.TwoPanelApp => 2,
            DesignNewKind.ThreePanelApp => 3,
            DesignNewKind.BlankComponentWithGrid => 2,
            _ => 0,
        };
        if (columns > 0)
        {
            var panels = new UniformGrid(columns, kind == DesignNewKind.BlankComponentWithGrid ? 2 : 1)
            {
                Margin = component ? new Thickness(6) : new Thickness(6, 16, 6, 6),
            };
            for (int i = 0; i < columns * (kind == DesignNewKind.BlankComponentWithGrid ? 2 : 1); i++)
            {
                var cell = new Rectangle { Margin = new Thickness(2), StrokeThickness = 1 };
                cell.SetResourceReference(Shape.StrokeProperty, ThemeKeys.Border);
                cell.SetResourceReference(Shape.FillProperty, ThemeKeys.Panel);
                panels.Children.Add(cell);
            }

            canvas.Children.Add(panels);
        }

        return canvas;
    }

    // A two-way uniform grid without System.Windows.Controls.Primitives' constructor-less one.
    private sealed class UniformGrid : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
        }
    }
}
