using System.Runtime.InteropServices;
using JGraph.Core.Primitives;

namespace JGraph.Core.Model;

/// <summary>
/// The monitors as Windows reports them, turned into MATLAB's rectangles (see <see cref="UiScreen"/>).
/// A process that is aware of scaling is handed device pixels and divides by the monitor's scale; one
/// that is not is handed scaled pixels already, with a scale of one. Anywhere but Windows, and on any
/// failure, the answer is empty and the caller falls back.
/// </summary>
internal static class SystemScreens
{
    public static IReadOnlyList<Rect2D> Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            var found = new List<(Rect2D Box, bool Primary)>();
            MonitorEnumProc collect = (IntPtr monitor, IntPtr _, ref NativeRect _, IntPtr _) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    double scale = 1;
                    try
                    {
                        if (GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
                        {
                            scale = dpiX / 96.0;
                        }
                    }
                    catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                    {
                        // Before Windows 8.1 there is no per-monitor scale to ask for.
                    }

                    NativeRect r = info.Monitor;
                    found.Add((
                        new Rect2D(r.Left / scale, r.Top / scale, (r.Right - r.Left) / scale, (r.Bottom - r.Top) / scale),
                        (info.Flags & 1) != 0));
                }

                return true;
            };

            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, collect, IntPtr.Zero) || found.Count == 0)
            {
                return [];
            }

            GC.KeepAlive(collect);
            found.Sort(static (a, b) => b.Primary.CompareTo(a.Primary));
            double primaryHeight = found[0].Box.Height;
            var monitors = new List<Rect2D>(found.Count);
            foreach ((Rect2D box, _) in found)
            {
                // Top-left origin, Y downward -> MATLAB's bottom-left origin, 1-based, Y upward.
                monitors.Add(new Rect2D(box.X + 1, primaryHeight - (box.Y + box.Height) + 1, box.Width, box.Height));
            }

            return monitors;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return [];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);

    /// <summary>
    /// The pointer in MATLAB's screen pixels (bottom-left origin, 1-based, Y upward), scaled by the
    /// monitor it is on, or null where it cannot be read.
    /// </summary>
    public static Point2D? ReadPointer(double primaryHeight)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            if (!GetCursorPos(out NativePoint at))
            {
                return null;
            }

            double scale = ScaleAt(at);
            return new Point2D((at.X / scale) + 1, primaryHeight - (at.Y / scale));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return null;
        }
    }

    /// <summary>Moves the pointer to a point given as <see cref="ReadPointer"/> answers one.</summary>
    public static void MovePointer(Point2D point, double primaryHeight)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            // The scale is the one at the target, found from the target as an unscaled point first.
            var guess = new NativePoint { X = (int)Math.Round(point.X - 1), Y = (int)Math.Round(primaryHeight - point.Y) };
            double scale = ScaleAt(guess);
            SetCursorPos((int)Math.Round((point.X - 1) * scale), (int)Math.Round((primaryHeight - point.Y) * scale));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // Nowhere to move it.
        }
    }

    private static double ScaleAt(NativePoint at)
    {
        try
        {
            IntPtr monitor = MonitorFromPoint(at, 2); // the nearest monitor
            return monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0
                ? dpiX / 96.0
                : 1;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 1;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint dpiX, out uint dpiY);
}
