// U0 item 3 of the app-building plan: does WebView2CompositionControl draw under a WPF overlay, and
// what does it cost against the HwndHost WebView2? Throwaway; opens one window for ~30 s, then exits.
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Wv2Spike;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new SpikeWindow();
        app.Run(window);
        return 0;
    }
}

public sealed class SpikeWindow : Window
{
    const string Page = """
<!DOCTYPE html><html><head><meta charset="utf-8"><style>
html,body{margin:0;height:100%;background:#00c000;overflow:hidden}
#box{position:absolute;width:60px;height:60px;background:#ffff00;top:20px}
</style></head><body><div id="box"></div><script>
let frames = 0, x = 0, last = performance.now(), run = true;
function tick(t){ frames++; x = (x + 3) % 400; document.getElementById('box').style.left = x + 'px';
  if (t - last >= 1000) { chrome.webview.postMessage({kind:'fps', fps: frames * 1000 / (t - last)}); frames = 0; last = t; }
  if (run) requestAnimationFrame(tick); }
requestAnimationFrame(tick);
chrome.webview.addEventListener('message', e => { if (e.data === 'stop') { run = false; return; } chrome.webview.postMessage({kind:'echo', data: e.data}); });
</script></body></html>
""";

    readonly WebView2CompositionControl _comp = new();
    readonly WebView2 _hwnd = new();
    readonly Dictionary<string, List<double>> _fps = new() { ["comp"] = new(), ["hwnd"] = new() };
    readonly Dictionary<string, TaskCompletionSource<double>> _echo = new();
    readonly StringBuilder _log = new();
    readonly string _dir = AppContext.BaseDirectory;
    int _wpfFrames;

    public SpikeWindow()
    {
        Title = "JGraph WebView2 spike";
        Width = 900; Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Cell(_comp, 0, "composition"));
        grid.Children.Add(Cell(_hwnd, 1, "hwndhost"));
        Content = grid;
        Loaded += async (_, _) =>
        {
            try { await RunAsync(); }
            catch (Exception ex) { _log.AppendLine("ERROR " + ex); }
            File.WriteAllText(Path.Combine(_dir, "results.txt"), _log.ToString());
            Close();
        };
        CompositionTarget.Rendering += (_, _) => _wpfFrames++;
    }

    static Grid Cell(FrameworkElement web, int column, string name)
    {
        var cell = new Grid { Margin = new Thickness(6) };
        Grid.SetColumn(cell, column);
        cell.Children.Add(web);
        // The overlay a uialert would be: a solid red panel over the middle of the page.
        cell.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 0, 0)),
            Width = 160, Height = 100,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = name + " overlay", Foreground = Brushes.White, Margin = new Thickness(6) },
        });
        return cell;
    }

    async Task RunAsync()
    {
        string data = Path.Combine(_dir, "wv2data");
        var env = await CoreWebView2Environment.CreateAsync(null, data);
        _log.AppendLine($"runtime {env.BrowserVersionString}");
        foreach (var (name, web) in new (string, IWebView2)[] { ("comp", _comp), ("hwnd", _hwnd) })
        {
            var sw = Stopwatch.StartNew();
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.WebMessageReceived += (_, e) => OnMessage(name, e.WebMessageAsJson);
            web.NavigateToString(Page);
            _log.AppendLine($"{name}: core ready in {sw.ElapsedMilliseconds} ms");
        }

        await Task.Delay(3000);

        // 1. Overlay: what is on screen at the centre of each overlay and beside it?
        _log.AppendLine("overlay centre pixels (expect red where the overlay draws on top):");
        foreach (var (name, web) in new (string, FrameworkElement)[] { ("comp", _comp), ("hwnd", _hwnd) })
        {
            var centre = web.PointToScreen(new Point(web.ActualWidth / 2, web.ActualHeight / 2));
            var side = web.PointToScreen(new Point(20, web.ActualHeight - 20));
            _log.AppendLine($"  {name}: overlay centre {Describe(ScreenPixel(centre))}, page corner {Describe(ScreenPixel(side))}");
        }
        SaveScreenshot();

        // 2. Frame cost: each control alone for 5 s.
        foreach (var (name, web, other) in new (string, FrameworkElement, FrameworkElement)[] { ("comp", _comp, _hwnd), ("hwnd", _hwnd, _comp) })
        {
            other.Visibility = Visibility.Hidden;
            web.Visibility = Visibility.Visible;
            await Task.Delay(1500);
            _fps[name].Clear();
            var proc = Process.GetCurrentProcess();
            TimeSpan cpu0 = proc.TotalProcessorTime; int f0 = _wpfFrames; var sw = Stopwatch.StartNew();
            await Task.Delay(5000);
            proc.Refresh();
            double cpu = (proc.TotalProcessorTime - cpu0).TotalMilliseconds / sw.ElapsedMilliseconds * 100;
            double wpf = (_wpfFrames - f0) * 1000.0 / sw.ElapsedMilliseconds;
            double pageFps = _fps[name].Count > 0 ? _fps[name].Average() : double.NaN;
            _log.AppendLine($"{name} alone: page rAF {pageFps:F1} fps, WPF frames {wpf:F1}/s, host process CPU {cpu:F1} % of one core");
        }
        _comp.Visibility = Visibility.Visible; _hwnd.Visibility = Visibility.Visible;
        await Task.Delay(500);

        // 2b. Idle cost: stop both animations, measure 5 s with both pages showing.
        _comp.CoreWebView2.PostWebMessageAsString("stop"); _hwnd.CoreWebView2.PostWebMessageAsString("stop");
        await Task.Delay(1500);
        {
            var proc = Process.GetCurrentProcess();
            TimeSpan cpu0 = proc.TotalProcessorTime; int f0 = _wpfFrames; var sw = Stopwatch.StartNew();
            await Task.Delay(5000);
            proc.Refresh();
            _log.AppendLine($"both static: WPF frames {(_wpfFrames - f0) * 1000.0 / sw.ElapsedMilliseconds:F1}/s, host process CPU {(proc.TotalProcessorTime - cpu0).TotalMilliseconds / sw.ElapsedMilliseconds * 100:F1} % of one core");
        }
        _comp.Visibility = Visibility.Hidden;
        await Task.Delay(1500);
        {
            var proc = Process.GetCurrentProcess();
            TimeSpan cpu0 = proc.TotalProcessorTime; int f0 = _wpfFrames; var sw = Stopwatch.StartNew();
            await Task.Delay(5000);
            proc.Refresh();
            _log.AppendLine($"hwnd static alone: WPF frames {(_wpfFrames - f0) * 1000.0 / sw.ElapsedMilliseconds:F1}/s, host process CPU {(proc.TotalProcessorTime - cpu0).TotalMilliseconds / sw.ElapsedMilliseconds * 100:F1} % of one core");
        }
        _comp.Visibility = Visibility.Visible;
        await Task.Delay(500);

        // 3. Bridge round trip, the path htmlComponent messages would take.
        foreach (var (name, web) in new (string, IWebView2)[] { ("comp", _comp), ("hwnd", _hwnd) })
        {
            var times = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                var tcs = new TaskCompletionSource<double>();
                _echo[name] = tcs;
                var sw = Stopwatch.StartNew();
                web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { i, v = new[] { 1, 2, 3 } }));
                await tcs.Task;
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            _log.AppendLine($"{name}: message round trip median {times[10]:F2} ms, max {times[^1]:F2} ms");
        }

        // 4. CapturePreviewAsync, the exportapp/getframe road.
        foreach (var (name, web) in new (string, IWebView2)[] { ("comp", _comp), ("hwnd", _hwnd) })
        {
            using var ms = new MemoryStream();
            var sw = Stopwatch.StartNew();
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            _log.AppendLine($"{name}: CapturePreviewAsync {frame.PixelWidth}x{frame.PixelHeight} px in {sw.ElapsedMilliseconds} ms (control {((FrameworkElement)web).ActualWidth:F0}x{((FrameworkElement)web).ActualHeight:F0} DIP)");
        }

        // 5. RenderTargetBitmap of the window: does a WPF-side capture see each page?
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(this);
        foreach (var (name, web) in new (string, FrameworkElement)[] { ("comp", _comp), ("hwnd", _hwnd) })
        {
            var p = web.TranslatePoint(new Point(20, web.ActualHeight - 20), this);
            var px = new byte[4];
            rtb.CopyPixels(new Int32Rect((int)p.X, (int)p.Y, 1, 1), px, 4, 0);
            _log.AppendLine($"{name}: RenderTargetBitmap sees page corner as rgb({px[2]},{px[1]},{px[0]})");
        }
    }

    void OnMessage(string name, string json)
    {
        using var doc = JsonDocument.Parse(json);
        string kind = doc.RootElement.GetProperty("kind").GetString()!;
        if (kind == "fps") _fps[name].Add(doc.RootElement.GetProperty("fps").GetDouble());
        else if (kind == "echo" && _echo.TryGetValue(name, out var tcs)) tcs.TrySetResult(0);
    }

    static System.Drawing.Color ScreenPixel(Point screen)
    {
        using var bmp = new System.Drawing.Bitmap(1, 1);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen((int)screen.X, (int)screen.Y, 0, 0, new System.Drawing.Size(1, 1));
        return bmp.GetPixel(0, 0);
    }

    static string Describe(System.Drawing.Color c) =>
        $"rgb({c.R},{c.G},{c.B}) = " + (c.R > 200 && c.G < 60 && c.B < 60 ? "RED (overlay on top)"
            : c.G > 150 && c.R < 80 ? "GREEN (page on top)" : c.R > 200 && c.G > 200 && c.B < 80 ? "YELLOW (page box)" : "other");

    void SaveScreenshot()
    {
        var tl = PointToScreen(new Point(0, 0));
        var br = PointToScreen(new Point(ActualWidth, ActualHeight));
        int w = (int)(br.X - tl.X), h = (int)(br.Y - tl.Y);
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen((int)tl.X, (int)tl.Y, 0, 0, new System.Drawing.Size(w, h));
        bmp.Save(Path.Combine(_dir, "screenshot.png"));
    }
}
