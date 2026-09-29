using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace JGraph.Devices.Serial;

/// <summary>
/// The serial ports this machine has: the names the serial drivers publish under
/// <c>HKLM\HARDWARE\DEVICEMAP\SERIALCOMM</c> (USB CDC and FTDI adapters, motherboard UARTs, com0com
/// pairs and Bluetooth SPP ports alike), in natural order (<c>COM1, COM3, COM11</c>).
/// </summary>
[SupportedOSPlatform("windows")]
public static class SerialPortList
{
    /// <summary>Every port name the drivers publish.</summary>
    public static IReadOnlyList<string> All()
    {
        var names = new List<string>();
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key is not null)
            {
                foreach (string value in key.GetValueNames())
                {
                    if (key.GetValue(value) is string port && port.Length > 0 && !names.Contains(port, StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(port);
                    }
                }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // No list is the answer a machine without the key gives too.
        }

        names.Sort(NaturalOrder.Instance);
        return names;
    }

    /// <summary>Whether <paramref name="port"/> can be opened now (no program holds it).</summary>
    public static bool CanOpen(string port)
    {
        using SafeFileHandle handle = CommNative.CreateFile(@"\\.\" + port, CommNative.GENERIC_READ | CommNative.GENERIC_WRITE, 0, 0,
            CommNative.OPEN_EXISTING, CommNative.FILE_FLAG_OVERLAPPED, 0);
        _ = Marshal.GetLastPInvokeError();
        return !handle.IsInvalid;
    }
}

/// <summary>Orders names by their text with the runs of digits compared as numbers (COM1, COM3, COM11).</summary>
public sealed class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.CompareOrdinal(x, y);
        }

        int i = 0;
        int j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int si = i;
                int sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                string a = x[si..i].TrimStart('0');
                string b = y[sj..j].TrimStart('0');
                int byLength = a.Length.CompareTo(b.Length);
                if (byLength != 0)
                {
                    return byLength;
                }

                int byDigits = string.CompareOrdinal(a, b);
                if (byDigits != 0)
                {
                    return byDigits;
                }

                continue;
            }

            int byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (byChar != 0)
            {
                return byChar;
            }

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
