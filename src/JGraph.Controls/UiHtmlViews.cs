using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using JGraph.Core.Model;
using JGraph.Scripting;

namespace JGraph.Controls;

/// <summary>Makes the element a figure window shows a <c>uihtml</c>'s page in (U9b).</summary>
public interface IUiHtmlViewFactory
{
    /// <summary>The element, which opens its page once it is loaded and closes it when disposed.</summary>
    FrameworkElement Create(UiHtmlModel html);
}

/// <summary>
/// Where a figure window's <c>uihtml</c> views come from (U9b): <c>JGraph.Controls.Web</c>, which
/// targets a Windows SDK framework for the WebView2 composition control and is loaded by path the
/// first time a window needs one, as the Bluetooth backend is (ADR 0186). Loading it also installs
/// its hidden host, for pages with no window.
/// </summary>
public static class UiHtmlViews
{
    private static readonly object Gate = new();
    private static bool _tried;
    private static IUiHtmlViewFactory? _factory;

    /// <summary>The factory, loading it on first use; null when WebView2 cannot be used here.</summary>
    public static IUiHtmlViewFactory? Factory
    {
        get
        {
            lock (Gate)
            {
                if (!_tried)
                {
                    _tried = true;
                    _factory = Load();
                }

                return _factory;
            }
        }
    }

    /// <summary>Loads the factory now — what an application with figure windows does at start, so pages go to windows from the first.</summary>
    public static bool EnsureLoaded() => Factory is not null;

    private static IUiHtmlViewFactory? Load()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "JGraph.Controls.Web.dll");
            Assembly assembly = File.Exists(path) ? Assembly.LoadFrom(path) : Assembly.Load("JGraph.Controls.Web");
            Type? entry = assembly.GetType("JGraph.Controls.Web.UiHtmlWeb");
            object? made = entry?.GetMethod("InstallWindows", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            return made as IUiHtmlViewFactory;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException or TargetInvocationException or TypeLoadException)
        {
            Trace.WriteLine($"uihtml: no web views: {ex.Message}");
            UiHtmlPages.Unavailable ??= "the WebView2 host could not be loaded";
            return null;
        }
    }
}
