using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JGraph.Controls.Designer;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Code View's left pane (App Designer window plan W8d): the code browser over App Layout, a picture of the canvas scaled to fit (<c>CodeView.rescaleThumbnailAppWindow</c>) that selects what is clicked
/// in it (<c>ThumbnailInteractionStrategy</c>: its component select plugin, no keys).
/// </summary>
internal sealed partial class AppDesignerWindow
{
    private readonly Image _appLayout = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(AppLayoutMargin) };
    private readonly Grid _codePane = new();
    private readonly ScrollViewer _appLayoutHost = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    private readonly DispatcherTimer _appLayoutPending = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private double _appLayoutScale = 1;

    /// <summary>The figure's distance from the pane's edge (<c>FigureDom.offsetLeft</c>).</summary>
    private const double AppLayoutMargin = 6;

    private void BuildAppLayout()
    {
        _codePane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _codePane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _codePane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _codePane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
        Grid.SetRow(_codeBrowser, 0);
        _codePane.Children.Add(_codeBrowser);
        var splitter = new GridSplitter { Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = GridResizeDirection.Rows };
        splitter.SetResourceReference(BackgroundProperty, ThemeKeys.Border);
        Grid.SetRow(splitter, 1);
        _codePane.Children.Add(splitter);
        var title = new TextBlock { Text = "App Layout", FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 3, 6, 3) };
        AutomationProperties.SetAutomationId(title, "AppLayout.Title");
        Grid.SetRow(title, 2);
        _codePane.Children.Add(title);
        _appLayoutHost.Content = _appLayout;
        Grid.SetRow(_appLayoutHost, 3);
        _codePane.Children.Add(_appLayoutHost);
        AutomationProperties.SetAutomationId(_appLayout, "AppLayout");
        AutomationProperties.SetName(_appLayout, "App Layout");
        _appLayoutHost.SizeChanged += (_, _) => ScaleAppLayout();
        _appLayout.MouseLeftButtonDown += (_, e) => AppLayoutClicked(e);
        _appLayoutPending.Tick += (_, _) =>
        {
            _appLayoutPending.Stop();
            ShowAppLayout();
        };
    }

    /// <summary>A change of the design while the Code View shows: the picture follows a moment later.</summary>
    private void AppLayoutChanged()
    {
        if (Active is { InCodeView: true })
        {
            _appLayoutPending.Stop();
            _appLayoutPending.Start();
        }
    }

    /// <summary>The picture taken anew from the active app's canvas, which is shown just long enough to lay out; nothing is drawn on the screen meanwhile.</summary>
    private void ShowAppLayout()
    {
        if (Active is not { InCodeView: true } app)
        {
            return;
        }

        Visibility was = app.Canvas.Visibility;
        app.Canvas.Visibility = Visibility.Visible;
        BitmapSource? picture;
        try
        {
            app.Canvas.UpdateLayout();
            picture = app.Canvas.Snapshot();
        }
        finally
        {
            app.Canvas.Visibility = was;
        }

        _appLayout.Source = picture;
        ScaleAppLayout();
    }

    private void ScaleAppLayout()
    {
        if (_appLayout.Source is not BitmapSource picture || picture.PixelWidth == 0 || picture.PixelHeight == 0)
        {
            return;
        }

        _appLayoutScale = DesignZoom.ThumbnailScale(_appLayoutHost.ActualWidth, _appLayoutHost.ActualHeight, AppLayoutMargin, picture.PixelWidth, picture.PixelHeight);
        _appLayout.Width = picture.PixelWidth * _appLayoutScale;
        _appLayout.Height = picture.PixelHeight * _appLayoutScale;
    }

    private void AppLayoutClicked(MouseButtonEventArgs e)
    {
        if (Active is { Document: var document })
        {
            Point at = e.GetPosition(_appLayout);
            document.Select([document.ComponentAt(new JGraph.Core.Primitives.Point2D(at.X / _appLayoutScale, at.Y / _appLayoutScale))]);
        }
    }
}
