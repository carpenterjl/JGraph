using System.IO;
using System.Reflection;
using Microsoft.Web.WebView2.Core;

namespace JGraph.Controls.Web;

/// <summary>
/// The Edge WebView2 runtime as JGraph uses it (U9b): whether it is installed, where its native
/// loader is, and one environment per UI thread, all sharing JGraph's user data folder.
/// </summary>
public static class WebView2Runtime
{
    /// <summary>Where to get the runtime, for the warning when it is missing.</summary>
    public const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private static readonly object Gate = new();
    private static bool _loaderSet;

    [ThreadStatic]
    private static Task<CoreWebView2Environment>? t_environment;

    /// <summary>The runtime's version, or null with <paramref name="why"/> when it cannot be used.</summary>
    public static string? Version(out string? why)
    {
        PointTheLoader();
        try
        {
            why = null;
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            why = $"the Microsoft Edge WebView2 Runtime is not installed (get it from {DownloadUrl})";
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            why = $"the WebView2 loader could not be loaded ({ex.Message})";
            return null;
        }
    }

    /// <summary>JGraph's WebView2 data: the user's local application data, not beside the program.</summary>
    public static string UserDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JGraph", "WebView2");

    /// <summary>
    /// This UI thread's environment, made once. A page with no window keeps its timers and frames
    /// running, as a page in MATLAB's hidden browser does.
    /// </summary>
    public static Task<CoreWebView2Environment> EnvironmentAsync()
    {
        PointTheLoader();
        return t_environment ??= CoreWebView2Environment.CreateAsync(null, UserDataFolder, new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows",
        });
    }

    /// <summary>The native loader travels beside this assembly (the hosts load it by path).</summary>
    private static void PointTheLoader()
    {
        lock (Gate)
        {
            if (_loaderSet)
            {
                return;
            }

            _loaderSet = true;
            string? here = Path.GetDirectoryName(typeof(WebView2Runtime).Assembly.Location);
            foreach (string? folder in new[] { here, here is null ? null : Path.Combine(here, "runtimes", "win-x64", "native") })
            {
                if (folder is not null && File.Exists(Path.Combine(folder, "WebView2Loader.dll")))
                {
                    try
                    {
                        CoreWebView2Environment.SetLoaderDllFolderPath(folder);
                    }
                    catch (InvalidOperationException)
                    {
                        // An environment exists already; the loader it found is the one in use.
                    }

                    return;
                }
            }
        }
    }

    /// <summary>The bridge script every page runs first.</summary>
    internal static string BridgeScript { get; } = ReadBridge();

    private static string ReadBridge()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JGraph.Controls.Web.bridge.js")
            ?? throw new InvalidOperationException("The uihtml bridge script is missing from JGraph.Controls.Web.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
