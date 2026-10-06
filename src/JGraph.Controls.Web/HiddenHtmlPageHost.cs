using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using JGraph.Core.Model;
using JGraph.Scripting;
using Microsoft.Web.WebView2.Core;

namespace JGraph.Controls.Web;

/// <summary>
/// Pages with no window (U9b): under <c>-batch</c>, and for a figure that is not shown, as R2025b
/// runs a uihtml's page in its hidden browser (probe <c>u0_uihtml</c>). Each page is a WebView2
/// controller in a window that is never shown, all on one thread of their own with a plain Win32
/// message loop, so the host needs no WPF and works in the console launcher too.
/// </summary>
public sealed class HiddenHtmlPageHost : IUiHtmlPageHost, IDisposable
{
    private readonly Win32UiThread _thread = new("JGraph uihtml pages");
    private readonly ConcurrentDictionary<HiddenPage, byte> _pages = new();
    private readonly Timer _sweep;

    public HiddenHtmlPageHost()
    {
        // A page whose component is gone is closed: deleted, or its figure closed.
        _sweep = new Timer(_ => Sweep(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>Whether DevTools open on a hidden page (they never show; kept for the window's view).</summary>
    public static bool DevTools { get; set; }

    /// <inheritdoc />
    public IUiHtmlPage? Open(UiHtmlModel html)
    {
        var page = new HiddenPage(this, html);
        _pages[page] = 0;
        _thread.Post(() => _ = page.StartAsync());
        return page;
    }

    private void Sweep()
    {
        foreach (HiddenPage page in _pages.Keys)
        {
            if (page.Html.IsGone)
            {
                page.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _sweep.Dispose();
        foreach (HiddenPage page in _pages.Keys)
        {
            page.Dispose();
        }

        _thread.Dispose();
    }

    private sealed class HiddenPage(HiddenHtmlPageHost host, UiHtmlModel html) : IUiHtmlPage
    {
        private IntPtr _window;
        private CoreWebView2Controller? _controller;
        private HtmlPageBridge? _bridge;
        private volatile bool _disposed;

        public UiHtmlModel Html { get; } = html;

        public bool InWindow => false;

        public async Task StartAsync()
        {
            try
            {
                Rect2DSize(out int width, out int height);
                _window = host._thread.CreateHostWindow(width, height);
                CoreWebView2Environment environment = await WebView2Runtime.EnvironmentAsync();
                if (_disposed)
                {
                    Close();
                    return;
                }

                _controller = await environment.CreateCoreWebView2ControllerAsync(_window);
                if (_disposed)
                {
                    Close();
                    return;
                }

                _controller.Bounds = new Rectangle(0, 0, width, height);
                _controller.IsVisible = true;
                _bridge = new HtmlPageBridge(Html, _controller.CoreWebView2, host._thread.Post);
                await _bridge.StartAsync(DevTools);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Trace.WriteLine($"uihtml: a hidden page did not start: {ex.Message}");
                Close();
            }
        }

        private void Rect2DSize(out int width, out int height)
        {
            JGraph.Core.Primitives.Rect2D box = Html.PixelPosition();
            width = Math.Max(1, (int)Math.Round(box.Width));
            height = Math.Max(1, (int)Math.Round(box.Height));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            host._pages.TryRemove(this, out _);
            Html.Detach(this);
            host._thread.Post(Close);
        }

        private void Close()
        {
            _bridge?.Dispose();
            _bridge = null;
            _controller?.Close();
            _controller = null;
            if (_window != IntPtr.Zero)
            {
                Win32UiThread.DestroyWindow(_window);
                _window = IntPtr.Zero;
            }
        }
    }
}

/// <summary>
/// A thread with a Win32 message loop and a <see cref="SynchronizationContext"/> that posts to it:
/// what WebView2 needs of a UI thread, without WPF or Windows Forms.
/// </summary>
internal sealed class Win32UiThread : IDisposable
{
    private const uint WmWork = 0x8000 + 0x21; // WM_APP + 0x21
    private const uint WmQuit = 0x0012;
    private const int WsPopup = unchecked((int)0x80000000);
    private static readonly IntPtr MessageOnly = new(-3);

    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly ManualResetEventSlim _started = new();
    private readonly string _className = "JGraphUiHtml" + Guid.NewGuid().ToString("N");
    private WndProc? _proc; // kept alive for as long as the class is registered
    private IntPtr _pump;

    public Win32UiThread(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Wait();
    }

    public void Post(Action action)
    {
        _work.Enqueue(action);
        PostMessage(_pump, WmWork, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>A window that is never shown, to hold a page; on this thread only.</summary>
    public IntPtr CreateHostWindow(int width, int height) =>
        CreateWindowEx(0, _className, "JGraph uihtml", WsPopup, -32000, -32000, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    private void Run()
    {
        _proc = Procedure;
        var windowClass = new WndClassEx
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            Procedure = Marshal.GetFunctionPointerForDelegate(_proc),
            Instance = GetModuleHandle(null),
            ClassName = _className,
        };
        RegisterClassEx(ref windowClass);
        _pump = CreateWindowEx(0, _className, "JGraph uihtml pump", 0, 0, 0, 0, 0, MessageOnly, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        SynchronizationContext.SetSynchronizationContext(new PostingContext(this));
        _started.Set();
        while (GetMessage(out Msg message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    private IntPtr Procedure(IntPtr window, uint message, IntPtr w, IntPtr l)
    {
        if (message == WmWork)
        {
            while (_work.TryDequeue(out Action? action))
            {
                try
                {
                    action();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Trace.WriteLine($"uihtml: {ex.Message}");
                }
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(window, message, w, l);
    }

    public void Dispose() => PostMessage(_pump, WmQuit, IntPtr.Zero, IntPtr.Zero);

    private sealed class PostingContext(Win32UiThread thread) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => thread.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (Environment.CurrentManagedThreadId == thread._thread.ManagedThreadId)
            {
                d(state);
                return;
            }

            using var done = new ManualResetEventSlim();
            thread.Post(() =>
            {
                try
                {
                    d(state);
                }
                finally
                {
                    done.Set();
                }
            });
            done.Wait();
        }

        public override SynchronizationContext CreateCopy() => this;
    }

    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr w, IntPtr l);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr W;
        public IntPtr L;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string title, int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr w, IntPtr l);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, IntPtr window, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
