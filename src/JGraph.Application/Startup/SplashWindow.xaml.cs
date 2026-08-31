using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JGraph.Imaging.Codecs;
using JGraph.Scripting.Startup;

namespace JGraph.Application.Startup;

/// <summary>
/// The startup splash: shows what JGraph is doing while the plugin registry, the script engines and
/// the previous session load, so a cold start does not look like a hang.
/// </summary>
/// <remarks>
/// <para>
/// Its artwork is replaceable (see <see cref="SplashArtwork"/>) and comes in two forms. An animated
/// PNG is played as the background, with the wordmark and the progress over it; a still replaces the
/// wordmark, which is what a still has always meant here. With neither, the built-in design shows.
/// </para>
/// <para>
/// The shipped animation is drawn on no page, so what is on screen is the shape of the surface and
/// not a rectangle laid over the desktop. That is the whole reason the artwork is an APNG: it is the
/// only container here that carries an alpha channel, and none of MATLAB's own video profiles does.
/// </para>
/// <para>
/// It loops for as long as loading takes, and when the shell is ready the pass on screen is played
/// out rather than cut off — see <see cref="PlayToEndAsync"/>, which is what waits for it. The frames
/// are decoded one at a time on a background-priority tick, so the animation yields to the warm-up
/// rather than competing with it.
/// </para>
/// </remarks>
public partial class SplashWindow : Window
{
    /// <summary>
    /// The longest a finished warm-up will wait for the artwork. The shipped animation runs for
    /// seven and a half seconds; the artwork is replaceable, and a file someone drops in
    /// <c>%AppData%\JGraph</c> must not be able to turn a start into a hang.
    /// </summary>
    private static readonly TimeSpan LongestFinish = TimeSpan.FromSeconds(30);

    private AnimatedPngReader? _animation;
    private WriteableBitmap? _frame;
    private DispatcherTimer? _timer;
    private byte[]? _bgra;
    private TaskCompletionSource? _finish;           // set once the shell is ready and the pass is playing out
    private TimeSpan _finishFrom;                    // where in the pass that happened

    /// <summary>Creates the splash and loads the artwork, if any.</summary>
    public SplashWindow()
    {
        InitializeComponent();
        TryLoadArtwork();
        Closed += (_, _) => StopAnimation();
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

    /// <summary>
    /// Plays the animation out to its last frame and returns when it gets there. The caller is the
    /// warm-up, which has finished: what is left is the artwork, so the caption says so and the
    /// progress bar starts again and fills across whatever is left of the pass — the bar measures the
    /// wait the user is actually in, which is the only measurement worth showing by then.
    /// </summary>
    /// <remarks>
    /// Returns at once when there is no animation — a still, the built-in design, or an artwork that
    /// stopped on a frame it could not decode. It gives up after <see cref="LongestFinish"/> whatever
    /// the artwork is doing, and that wait is off the dispatcher so a stalled tick cannot hold the
    /// shell back either.
    /// </remarks>
    public async Task PlayToEndAsync()
    {
        Task? pass = Dispatcher.CheckAccess() ? BeginFinalPass() : Dispatcher.Invoke(BeginFinalPass);
        if (pass is null)
        {
            return;
        }

        await Task.WhenAny(pass, Task.Delay(LongestFinish)).ConfigureAwait(true);
    }

    /// <summary>
    /// Puts the splash into its last pass and hands back the task that ends with it, or null when
    /// there is no animation left to play.
    /// </summary>
    private Task? BeginFinalPass()
    {
        if (_finish is not null)
        {
            return _finish.Task;
        }

        if (_animation is null || _timer is null)
        {
            return null;
        }

        _finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _finishFrom = _animation.Elapsed;
        Caption.Text = "Finishing splash animation…";
        Progress.Value = 0;
        return _finish.Task;
    }

    private void TryLoadArtwork()
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JGraph");

        if (TryStartAnimation(SplashArtwork.FindAnimation(appData, AppContext.BaseDirectory, File.Exists)))
        {
            return;
        }

        TryShowStill(SplashArtwork.Find(appData, AppContext.BaseDirectory, File.Exists));
    }

    private bool TryStartAnimation(string? path)
    {
        if (path is null)
        {
            return false;
        }

        try
        {
            AnimatedPngReader reader = AnimatedPngReader.Open(path);
            _animation = reader;
            _bgra = new byte[reader.Width * reader.Height * 4];
            _frame = new WriteableBitmap(reader.Width, reader.Height, 96, 96, PixelFormats.Bgra32, null);

            Animation.Source = _frame;
            Animation.Visibility = Visibility.Visible;

            // The animation is its own ground. Leaving the panel behind it would paint back the
            // page the artwork was drawn without.
            Panel.Visibility = Visibility.Collapsed;

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
            _timer.Tick += (_, _) => ShowNextFrame();
            ShowNextFrame();
            _timer.Start();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or NotSupportedException or ArgumentException or OverflowException)
        {
            // A missing, unreadable or corrupt animation must never block startup — fall back to a
            // still, and past that to the built-in design.
            StopAnimation();
            return false;
        }
    }

    private void TryShowStill(string? path)
    {
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

    /// <summary>
    /// Puts the next frame on screen, starting the pass again at the end. Nothing counts the passes:
    /// the splash lives exactly as long as the loading behind it, and the loop is what fills that.
    /// </summary>
    private void ShowNextFrame()
    {
        if (_animation is null || _frame is null || _bgra is null)
        {
            return;
        }

        try
        {
            if (!_animation.Advance())
            {
                if (_finish is not null)
                {
                    // The last frame is on screen and it is the one that was asked for. Stopping
                    // here leaves it standing until the splash closes.
                    Progress.Value = 1;
                    StopAnimation();
                    return;
                }

                _animation.Rewind();
                if (!_animation.Advance())
                {
                    StopAnimation();
                    return;
                }
            }

            ReportFinalPass();

            ReadOnlySpan<byte> rgba = _animation.Pixels;
            for (int i = 0; i < _bgra.Length; i += 4)
            {
                _bgra[i] = rgba[i + 2];
                _bgra[i + 1] = rgba[i + 1];
                _bgra[i + 2] = rgba[i];
                _bgra[i + 3] = rgba[i + 3];
            }

            _frame.WritePixels(
                new Int32Rect(0, 0, _animation.Width, _animation.Height), _bgra, _animation.Width * 4, 0);

            if (_timer is not null)
            {
                _timer.Interval = _animation.Delay;
            }
        }
        catch (InvalidDataException)
        {
            // A frame that will not decode ends the animation and leaves the last good one up. It is
            // not worth a failed start.
            StopAnimation();
        }
    }

    /// <summary>
    /// Moves the bar across the last pass. It is charged in the artwork's own time rather than in
    /// frames, so a file whose frames are not all the same length still reads as time remaining.
    /// </summary>
    private void ReportFinalPass()
    {
        if (_finish is null || _animation is null)
        {
            return;
        }

        TimeSpan left = _animation.Duration - _finishFrom;
        Progress.Value = left <= TimeSpan.Zero
            ? 1
            : Math.Clamp((_animation.Elapsed - _finishFrom) / left, 0, 1);
    }

    private void StopAnimation()
    {
        _timer?.Stop();
        _timer = null;
        _animation?.Dispose();
        _animation = null;

        // Whatever ended the animation — the last frame, a frame that would not decode, the splash
        // closing — nothing more will play, so anyone waiting for the pass is waiting for nothing.
        _finish?.TrySetResult();
    }
}
