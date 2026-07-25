using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using JGraph.Scripting.Startup;

namespace JGraph.Application.Startup;

/// <summary>
/// The startup splash: shows what JGraph is doing while the plugin registry, the script engines and
/// the previous session load, so a cold start does not look like a hang. Its artwork is replaceable
/// (see <see cref="SplashArtwork"/>); with none it draws a built-in wordmark.
/// </summary>
public partial class SplashWindow : Window
{
    /// <summary>Creates the splash and loads the artwork, if any.</summary>
    public SplashWindow()
    {
        InitializeComponent();
        TryLoadArtwork();
    }

    /// <summary>
    /// Updates the caption and progress. Safe from any thread — the startup sequence reports from a
    /// background thread while the container warms up.
    /// </summary>
    /// <param name="caption">What is happening now.</param>
    /// <param name="fraction">How far along, from 0 to 1.</param>
    public void Report(string caption, double fraction)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Report(caption, fraction));
            return;
        }

        Caption.Text = caption;
        Progress.Value = Math.Clamp(fraction, 0, 1);
    }

    private void TryLoadArtwork()
    {
        string? path = SplashArtwork.Find(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JGraph"),
            AppContext.BaseDirectory,
            File.Exists);
        if (path is null)
        {
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // don't hold the file open
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();

            Artwork.Source = image;
            Artwork.Visibility = Visibility.Visible;
            DefaultArtwork.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or UriFormatException or ArgumentException)
        {
            // A missing, unreadable or corrupt image must never block startup — keep the built-in one.
        }
    }
}
