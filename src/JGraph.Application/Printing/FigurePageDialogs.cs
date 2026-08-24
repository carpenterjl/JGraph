using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JGraph.Core.Model;
using JGraph.Export;

// Both JGraph.Core.Primitives and System.Windows spell a margin Thickness, and this file is the one
// place in the build that reaches for both models at once — a page is measured in the first and drawn
// in the second. The alias says which one every bare mention means.
using Thickness = System.Windows.Thickness;

namespace JGraph.Application.Printing;

/// <summary>
/// The three page dialogs a figure is set up through (M84): a preview of the page it would print on,
/// the page setup that decides that page, and the export setup the picture verbs fall back on.
/// </summary>
/// <remarks>
/// <para>
/// Built in code rather than in XAML. Each is a handful of rows over properties that already exist,
/// and a markup file per dialog would be three more files whose whole content is a two-column grid.
/// The import wizard is in XAML because it has a live preview grid and a view model with real
/// decisions in it; these three read and write a page rectangle.
/// </para>
/// <para>
/// Nothing here decides anything about a page. <see cref="FigureModel.EffectivePaperSize"/>,
/// <see cref="PaperSizes"/> and <see cref="FigureModel.PaperPosition"/> are M75's, and this is the
/// first thing that lets a person see them — which is what M75's own header said they were waiting
/// for.
/// </para>
/// </remarks>
internal static class FigurePageDialogs
{
    /// <summary>The dialog's owner: the active window, so it opens over what it is about.</summary>
    private static Window? Owner =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

    /// <summary>Renders the figure onto the page its <c>Paper*</c> properties describe.</summary>
    /// <remarks>
    /// The picture is the one <c>print -dpng</c> writes: rendered through <see cref="FigureExporter"/>
    /// at the asked resolution and placed on the page at the asked position. Sharing the renderer is
    /// what makes "the printed page is the same picture the file gets" a property a test can check
    /// without a printer.
    /// </remarks>
    public static Visual ComposePage(FigureModel figure, double dpi)
    {
        JGraph.Core.Primitives.Size2D paper = figure.EffectivePaperSize();
        JGraph.Core.Primitives.Rect2D position = figure.PaperPositionAuto
            ? Centred(paper, figure)
            : figure.PaperPosition;

        double scale = dpi / 96.0;
        (int width, int height, byte[] rgba) = FigureExporter.RenderRgba(figure, new ExportOptions
        {
            Scale = scale,
            Size = new JGraph.Core.Primitives.Size2D(position.Width * 96, position.Height * 96),
        });

        var bitmap = BitmapSource.Create(
            width, height, dpi, dpi, PixelFormats.Pbgra32, null, ToPremultiplied(rgba), width * 4);

        var page = new Canvas
        {
            Width = paper.Width * dpi,
            Height = paper.Height * dpi,
            Background = Brushes.White,
        };

        var image = new Image { Source = bitmap, Stretch = Stretch.Fill };

        // MATLAB measures PaperPosition from the bottom-left of the page and WPF from the top-left,
        // which is the same flip JgsGraphicsProperties.Up performs for a figure annotation.
        Canvas.SetLeft(image, position.X * dpi);
        Canvas.SetTop(image, (paper.Height - position.Y - position.Height) * dpi);
        image.Width = position.Width * dpi;
        image.Height = position.Height * dpi;
        page.Children.Add(image);

        page.Measure(new Size(page.Width, page.Height));
        page.Arrange(new Rect(0, 0, page.Width, page.Height));
        return page;
    }

    /// <summary>The rectangle an automatic paper position comes to: centred, with an inch of margin.</summary>
    private static JGraph.Core.Primitives.Rect2D Centred(JGraph.Core.Primitives.Size2D paper, FigureModel figure)
    {
        double width = System.Math.Max(1, paper.Width - 2);
        double aspect = figure.Size.Height / System.Math.Max(1, figure.Size.Width);
        double height = System.Math.Min(System.Math.Max(1, paper.Height - 2), width * aspect);
        return new JGraph.Core.Primitives.Rect2D((paper.Width - width) / 2, (paper.Height - height) / 2, width, height);
    }

    /// <summary>RGBA to the premultiplied BGRA a WPF bitmap wants.</summary>
    private static byte[] ToPremultiplied(byte[] rgba)
    {
        var bgra = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            double alpha = rgba[i + 3] / 255.0;
            bgra[i] = (byte)(rgba[i + 2] * alpha);
            bgra[i + 1] = (byte)(rgba[i + 1] * alpha);
            bgra[i + 2] = (byte)(rgba[i] * alpha);
            bgra[i + 3] = rgba[i + 3];
        }

        return bgra;
    }

    /// <summary>Shows the page a figure would print on, with a button to print it.</summary>
    public static bool Preview(FigureModel figure, Func<FigureModel, bool> print)
    {
        var window = new Window
        {
            Title = "Print preview",
            Width = 640,
            Height = 760,
            Owner = Owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var page = new ContentControl { Margin = new Thickness(12) };
        void Redraw() => page.Content = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = new Rectangle2D(ComposePage(figure, 96)),
        };

        Redraw();

        var printButton = new Button { Content = "Print…", Width = 90, Margin = new Thickness(4) };
        printButton.Click += (_, _) =>
        {
            if (print(figure))
            {
                window.DialogResult = true;
                window.Close();
            }
        };

        var setup = new Button { Content = "Page setup…", Width = 110, Margin = new Thickness(4) };
        setup.Click += (_, _) =>
        {
            if (PageSetup(figure))
            {
                Redraw();
            }
        };

        var close = new Button { Content = "Close", Width = 90, Margin = new Thickness(4), IsCancel = true };
        close.Click += (_, _) => window.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8),
        };
        buttons.Children.Add(setup);
        buttons.Children.Add(printButton);
        buttons.Children.Add(close);

        var layout = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(new ScrollViewer
        {
            Content = page,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        window.Content = layout;
        window.ShowDialog();
        return true;
    }

    /// <summary>The page-setup dialog, writing the figure's <c>Paper*</c> properties.</summary>
    public static bool PageSetup(FigureModel figure)
    {
        var types = new ComboBox { Margin = new Thickness(4) };
        foreach (string name in PaperSizes.KnownNames)
        {
            types.Items.Add(name);
        }

        types.SelectedItem = figure.PaperType;

        var orientation = new ComboBox { Margin = new Thickness(4) };
        orientation.Items.Add("portrait");
        orientation.Items.Add("landscape");
        orientation.SelectedItem = figure.PaperOrientation == PaperOrientationType.Landscape
            ? "landscape"
            : "portrait";

        var auto = new CheckBox
        {
            Content = "Place the figure automatically",
            IsChecked = figure.PaperPositionAuto,
            Margin = new Thickness(4),
        };

        JGraph.Core.Primitives.Rect2D position = figure.PaperPosition;
        TextBox Inches(double value) => new()
        {
            Text = value.ToString("0.###", CultureInfo.InvariantCulture),
            Width = 70,
            Margin = new Thickness(4),
        };

        TextBox left = Inches(position.X);
        TextBox bottom = Inches(position.Y);
        TextBox width = Inches(position.Width);
        TextBox height = Inches(position.Height);

        var grid = new Grid { Margin = new Thickness(12) };
        for (int i = 0; i < 7; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        void Row(int index, string label, UIElement editor)
        {
            var text = new TextBlock
            {
                Text = label,
                Margin = new Thickness(4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(text, index);
            Grid.SetColumn(text, 0);
            Grid.SetRow(editor, index);
            Grid.SetColumn(editor, 1);
            grid.Children.Add(text);
            grid.Children.Add(editor);
        }

        Row(0, "Paper type", types);
        Row(1, "Orientation", orientation);
        Row(2, string.Empty, auto);
        Row(3, "Left (in)", left);
        Row(4, "Bottom (in)", bottom);
        Row(5, "Width (in)", width);
        Row(6, "Height (in)", height);

        return Ask("Page setup", grid, () =>
        {
            figure.PaperType = (string)types.SelectedItem;
            figure.PaperOrientation = (string)orientation.SelectedItem == "landscape"
                ? PaperOrientationType.Landscape
                : PaperOrientationType.Portrait;
            figure.PaperPositionAuto = auto.IsChecked == true;
            figure.PaperPosition = new JGraph.Core.Primitives.Rect2D(
                Read(left, position.X), Read(bottom, position.Y),
                Read(width, position.Width), Read(height, position.Height));
        });
    }

    /// <summary>The export-setup dialog, writing the figure's export preset.</summary>
    public static bool ExportSetup(FigureModel figure)
    {
        var resolution = new TextBox
        {
            Text = (figure.ExportSetup.Resolution ?? 96).ToString("0.###", CultureInfo.InvariantCulture),
            Width = 70,
            Margin = new Thickness(4),
        };

        var useSize = new CheckBox
        {
            Content = "Draw at a stated size",
            IsChecked = figure.ExportSetup.Size is not null,
            Margin = new Thickness(4),
        };

        JGraph.Core.Primitives.Size2D size = figure.ExportSetup.Size ?? figure.Size;
        var width = new TextBox
        {
            Text = size.Width.ToString("0.#", CultureInfo.InvariantCulture),
            Width = 70,
            Margin = new Thickness(4),
        };
        var height = new TextBox
        {
            Text = size.Height.ToString("0.#", CultureInfo.InvariantCulture),
            Width = 70,
            Margin = new Thickness(4),
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = "These stand in only where an export says nothing of its own.",
            Margin = new Thickness(4, 4, 4, 12),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock { Text = "Resolution (dpi)", Margin = new Thickness(4) });
        panel.Children.Add(resolution);
        panel.Children.Add(useSize);
        panel.Children.Add(new TextBlock { Text = "Width", Margin = new Thickness(4) });
        panel.Children.Add(width);
        panel.Children.Add(new TextBlock { Text = "Height", Margin = new Thickness(4) });
        panel.Children.Add(height);

        return Ask("Export setup", panel, () =>
        {
            figure.ExportSetup.Resolution = Read(resolution, 96);
            figure.ExportSetup.Size = useSize.IsChecked == true
                ? new JGraph.Core.Primitives.Size2D(Read(width, size.Width), Read(height, size.Height))
                : null;
        });
    }

    /// <summary>A modal dialog over <paramref name="body"/> with OK and Cancel.</summary>
    private static bool Ask(string title, UIElement body, Action accept)
    {
        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Owner = Owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(4), IsDefault = true };
        ok.Click += (_, _) =>
        {
            accept();
            window.DialogResult = true;
        };

        var cancel = new Button
        {
            Content = "Cancel", Width = 80, Margin = new Thickness(4), IsCancel = true,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var layout = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(body);
        window.Content = layout;

        return window.ShowDialog() == true;
    }

    /// <summary>A number typed into a box, or <paramref name="fallback"/> when it is not one.</summary>
    private static double Read(TextBox box, double fallback) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
        && double.IsFinite(value)
            ? value
            : fallback;
}

/// <summary>A visual wrapped so it can be a <see cref="FrameworkElement"/>'s child.</summary>
internal sealed class Rectangle2D : FrameworkElement
{
    private readonly Visual _visual;

    public Rectangle2D(Visual visual)
    {
        _visual = visual;
        AddVisualChild(visual);
        if (visual is FrameworkElement element)
        {
            Width = element.Width;
            Height = element.Height;
        }
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _visual;
}
