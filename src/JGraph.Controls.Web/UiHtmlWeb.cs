using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JGraph.Controls;
using JGraph.Core.Model;
using JGraph.Scripting;
using Microsoft.Web.WebView2.Wpf;

namespace JGraph.Controls.Web;

/// <summary>
/// The entry point the script side finds by name (U9b): pages with no window. It touches no WPF
/// type, so the console launcher, which has no WPF, can call it.
/// </summary>
public static class UiHtmlHidden
{
    private static readonly object Gate = new();
    private static HiddenHtmlPageHost? _hidden;

    /// <summary>Installs the hidden host when the runtime is there, else says why not. Answers whether it did.</summary>
    public static bool Install()
    {
        lock (Gate)
        {
            if (_hidden is not null)
            {
                return true;
            }

            if (WebView2Runtime.Version(out string? why) is null)
            {
                UiHtmlPages.Unavailable = why;
                return false;
            }

            _hidden = new HiddenHtmlPageHost();
            UiHtmlPages.Hidden = _hidden;
            return true;
        }
    }
}

/// <summary>The entry point the window layer finds by name (U9b): the window's views, and the hidden host too.</summary>
public static class UiHtmlWeb
{
    /// <summary>Installs both hosts and answers the window's view factory, or null without the runtime.</summary>
    public static IUiHtmlViewFactory? InstallWindows()
    {
        if (!UiHtmlHidden.Install())
        {
            return null;
        }

        UiHtmlPages.WindowsHostPages = true;
        return new ViewFactory();
    }

    private sealed class ViewFactory : IUiHtmlViewFactory
    {
        public FrameworkElement Create(UiHtmlModel html) => new HtmlComponentView(html);
    }
}

/// <summary>
/// A uihtml's page in a figure window (U9b): a <see cref="WebView2CompositionControl"/>, which draws
/// into WPF's own surface — so a uialert sits over it and a figure's capture includes it (the U0
/// spike) — at some cost while it animates. It takes over from a hidden page the component had.
/// </summary>
internal sealed class HtmlComponentView : Border, IUiHtmlPage
{
    private readonly UiHtmlModel _html;
    private readonly WebView2CompositionControl _web = new() { DefaultBackgroundColor = System.Drawing.Color.White };
    private HtmlPageBridge? _bridge;
    private bool _started;
    private bool _disposed;

    public HtmlComponentView(UiHtmlModel html)
    {
        _html = html;
        Background = Brushes.White;
        Child = _web;
        Loaded += (_, _) => _ = StartAsync();
    }

    public bool InWindow => true;

    private async Task StartAsync()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        try
        {
            await _web.EnsureCoreWebView2Async(await WebView2Runtime.EnvironmentAsync());
            if (_disposed)
            {
                return;
            }

            if (_html.Attach(this) is { } replaced)
            {
                replaced.Dispose();
            }

            _bridge = new HtmlPageBridge(_html, _web.CoreWebView2, action => Dispatcher.BeginInvoke(action));
            await _bridge.StartAsync(HiddenHtmlPageHost.DevTools);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.WriteLine($"uihtml: a window's page did not start: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bridge?.Dispose();
        _html.Detach(this);
        _web.Dispose();
    }
}
