using JGraph.Core.Model;

namespace JGraph.Scripting;

/// <summary>
/// Where pages for <c>uihtml</c> come from (U9b). The application installs both when the WebView2
/// host loads (<c>JGraph.Controls.Web</c>); a test installs a stand-in. With none, a component keeps
/// its state and shows nothing.
/// </summary>
public static class UiHtmlPages
{
    private static readonly object Gate = new();
    private static bool _triedHidden;

    /// <summary>Opens hidden pages: under <c>-batch</c>, and for a figure that has no window.</summary>
    public static IUiHtmlPageHost? Hidden { get; set; }

    /// <summary>
    /// Loads the WebView2 host (<c>JGraph.Controls.Web</c>, beside the program) and has it install its
    /// hidden host, once; a missing assembly or runtime leaves <see cref="Unavailable"/> saying why.
    /// </summary>
    public static void TryLoadHidden()
    {
        lock (Gate)
        {
            if (_triedHidden || Hidden is not null)
            {
                return;
            }

            _triedHidden = true;
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "JGraph.Controls.Web.dll");
                if (!File.Exists(path))
                {
                    Unavailable ??= "the WebView2 host (JGraph.Controls.Web) is not installed beside the program";
                    return;
                }

                System.Reflection.Assembly assembly = System.Reflection.Assembly.LoadFrom(path);
                assembly.GetType("JGraph.Controls.Web.UiHtmlHidden")?.GetMethod("Install")?.Invoke(null, null);
            }
            catch (Exception ex) when (ex is FileLoadException or BadImageFormatException or System.Reflection.TargetInvocationException or TypeLoadException)
            {
                Unavailable ??= $"the WebView2 host could not be loaded ({(ex.InnerException ?? ex).Message})";
            }
        }
    }

    /// <summary>Whether figure windows open their own pages (the application's window layer does).</summary>
    public static bool WindowsHostPages { get; set; }

    /// <summary>Why there is no host, when the loader knows: shown once in the warning.</summary>
    public static string? Unavailable { get; set; }
}

/// <summary>Something that can open a page for a <see cref="UiHtmlModel"/>.</summary>
public interface IUiHtmlPageHost
{
    /// <summary>
    /// Opens a page showing <paramref name="html"/>: it loads the source, runs the bridge, takes the
    /// model's posted messages and reports what the page does through
    /// <see cref="ScriptGraphicsCallbacks.NotifyComponent"/>. Answers null when it cannot.
    /// </summary>
    IUiHtmlPage? Open(UiHtmlModel html);
}
