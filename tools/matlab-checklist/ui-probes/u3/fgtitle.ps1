# Prints the title of the foreground window. The u3w_* probes call it before every synthesized
# click, and click only when the window in front is their own figure.
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class U3Foreground {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    public static string Title() {
        var text = new StringBuilder(512);
        GetWindowText(GetForegroundWindow(), text, text.Capacity);
        return text.ToString();
    }
}
'@
[U3Foreground]::Title()
