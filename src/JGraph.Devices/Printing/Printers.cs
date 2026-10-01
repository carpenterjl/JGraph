using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using JGraph.Devices.Usb;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace JGraph.Devices.Printing;

/// <summary>A printer queue, and the USB printer behind it when there is one.</summary>
/// <param name="Name">The queue's name.</param>
/// <param name="Port">Its port: "USB001", "FILE:", an address.</param>
/// <param name="Driver">Its driver's name.</param>
/// <param name="IsDefault">Whether it is the user's default printer.</param>
/// <param name="Status">"ready", or what holds it: "paused", "offline, paper out".</param>
/// <param name="Jobs">How many jobs wait in the queue.</param>
/// <param name="UsbInstanceId">The USB device's instance ID; empty for a queue that is not a USB printer.</param>
/// <param name="DeviceId">The IEEE 1284 device ID the printer answers ("MFG:…;MDL:…;CMD:…;"); empty when it gave none.</param>
public sealed record PrinterInfo(string Name, string Port, string Driver, bool IsDefault, string Status, int Jobs, string UsbInstanceId, string DeviceId)
{
    public string Manufacturer => Field("MFG", "MANUFACTURER");

    public string Model => Field("MDL", "MODEL");

    public string CommandSet => Field("CMD", "COMMAND SET");

    /// <summary>A field of the 1284 device ID, by its short or long key.</summary>
    private string Field(params string[] keys)
    {
        foreach (string part in DeviceId.Split(';'))
        {
            int colon = part.IndexOf(':');
            if (colon > 0 && keys.Contains(part[..colon].Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return part[(colon + 1)..].Trim();
            }
        }

        return "";
    }
}

/// <summary>The printer queues a script reaches (device classes plan, stage D12): the spooler's, or the simulated ones.</summary>
public interface IPrinterBackend
{
    IReadOnlyList<PrinterInfo> Printers();

    /// <summary>
    /// Sends <paramref name="bytes"/> to a queue as one job the driver does not touch; answers the job's
    /// number. With <paramref name="outputFile"/> the spooler writes the job there instead of to the port.
    /// </summary>
    int PrintRaw(string printer, ReadOnlySpan<byte> bytes, string documentName, string? outputFile);
}

/// <summary>
/// The spooler's queues (device classes plan, stage D12): every local and connected printer, a USB
/// printer matched to its queue through the port usbmon gave it and asked for its IEEE 1284 device
/// ID (IOCTL_USBPRINT_GET_1284_ID, on a handle opened with no access rights), and a job sent as it
/// is for ESC/POS, ZPL or PCL. A version 4 driver takes such a job as "XPS_PASS", an older one as
/// "RAW", which is the rule of Microsoft's own sample.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WinPrinters : IPrinterBackend
{
    private static readonly Guid GUID_DEVINTERFACE_USBPRINT = new("28D78FAD-5A12-11D1-AE5B-0000F803A8C2");
    private const uint IOCTL_USBPRINT_GET_1284_ID = 0x220034;
    private const uint PRINTER_ENUM_LOCAL = 2;
    private const uint PRINTER_ENUM_CONNECTIONS = 4;

    public static WinPrinters Instance { get; } = new();

    private WinPrinters()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DOC_INFO_1
    {
        public nint pDocName;
        public nint pOutputFile;
        public nint pDatatype;
    }

    [LibraryImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumPrinters(uint flags, char* name, uint level, byte* buffer, uint size, uint* needed, uint* returned);

    [LibraryImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenPrinter(string name, nint* handle, nint defaults);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClosePrinter(nint handle);

    [LibraryImport("winspool.drv", EntryPoint = "GetPrinterDriverW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPrinterDriver(nint handle, char* environment, uint level, byte* buffer, uint size, uint* needed);

    [LibraryImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true)]
    private static partial uint StartDocPrinter(nint handle, uint level, DOC_INFO_1* info);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndDocPrinter(nint handle);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartPagePrinter(nint handle);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndPagePrinter(nint handle);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WritePrinter(nint handle, byte* data, uint length, uint* written);

    [LibraryImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDefaultPrinter(char* buffer, uint* size);

    private static readonly (uint Bit, string Word)[] StatusWords =
    [
        (0x1, "paused"), (0x2, "error"), (0x4, "pending deletion"), (0x8, "paper jam"), (0x10, "paper out"), (0x20, "manual feed"),
        (0x40, "paper problem"), (0x80, "offline"), (0x100, "io active"), (0x200, "busy"), (0x400, "printing"), (0x800, "output bin full"),
        (0x1000, "not available"), (0x2000, "waiting"), (0x4000, "processing"), (0x8000, "initializing"), (0x10000, "warming up"),
        (0x20000, "toner low"), (0x40000, "no toner"), (0x80000, "page punt"), (0x100000, "user intervention"), (0x200000, "out of memory"),
        (0x400000, "door open"), (0x800000, "server unknown"), (0x1000000, "power save"),
    ];

    /// <summary>PRINTER_INFO_2's Status in words: "ready" for none, else each PRINTER_STATUS_ bit set.</summary>
    public static string StatusText(uint status)
    {
        if (status == 0)
        {
            return "ready";
        }

        string words = string.Join(", ", StatusWords.Where(w => (status & w.Bit) != 0).Select(static w => w.Word));
        return words.Length > 0 ? words : $"status 0x{status:X}";
    }

    public IReadOnlyList<PrinterInfo> Printers()
    {
        uint needed = 0;
        uint count = 0;
        EnumPrinters(PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS, null, 2, null, 0, &needed, &count);
        if (needed == 0)
        {
            return [];
        }

        byte[] buffer = new byte[needed];
        var result = new List<PrinterInfo>();
        string defaultName = DefaultPrinter();
        Dictionary<string, (string Instance, string DeviceId)> usb = UsbPrinters();
        fixed (byte* p = buffer)
        {
            if (!EnumPrinters(PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS, null, 2, p, needed, &needed, &count))
            {
                return [];
            }

            // PRINTER_INFO_2W: thirteen pointers (server, printer, share, port, driver, comment, location,
            // devmode, separator file, print processor, datatype, parameters, security descriptor), then
            // eight DWORDs (attributes, priority, default priority, start, until, status, jobs, pages a minute).
            int stride = (IntPtr.Size * 13) + (4 * 8);
            for (int i = 0; i < count; i++)
            {
                byte* row = p + (i * stride);
                nint* fields = (nint*)row;
                uint* numbers = (uint*)(row + (IntPtr.Size * 13));
                string name = Marshal.PtrToStringUni(fields[1]) ?? "";
                string port = Marshal.PtrToStringUni(fields[3]) ?? "";
                string driver = Marshal.PtrToStringUni(fields[4]) ?? "";

                // A queue pooled over several ports lists them with commas; the first USB one is its printer.
                (string Instance, string DeviceId) behind = port.Split(',').Select(static s => s.Trim())
                    .Where(usb.ContainsKey).Select(s => usb[s]).FirstOrDefault(("", ""));
                result.Add(new PrinterInfo(name, port, driver, name.Equals(defaultName, StringComparison.OrdinalIgnoreCase),
                    StatusText(numbers[5]), (int)numbers[6], behind.Instance, behind.DeviceId));
            }
        }

        return result;
    }

    private static string DefaultPrinter()
    {
        uint size = 0;
        GetDefaultPrinter(null, &size);
        if (size == 0)
        {
            return "";
        }

        char[] name = new char[size];
        fixed (char* p = name)
        {
            return GetDefaultPrinter(p, &size) ? new string(p) : "";
        }
    }

    /// <summary>Spooler port ("USB001") → the USB device on it and the IEEE 1284 device ID it answers.</summary>
    private static Dictionary<string, (string Instance, string DeviceId)> UsbPrinters()
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach ((string instance, string path) in UsbEnumerator.InterfacePaths(GUID_DEVINTERFACE_USBPRINT))
        {
            string port = PortOf(path);
            if (port.Length == 0)
            {
                continue;
            }

            uint devInst = 0;
            string usb = UsbNative.CM_Locate_DevNode(&devInst, instance, 0) == UsbNative.CR_SUCCESS ? UsbCrossReference.UsbAncestor(devInst) ?? instance : instance;
            result[port] = (usb, DeviceIdOf(path));
        }

        return result;
    }

    private static string DeviceIdOf(string path)
    {
        using SafeFileHandle printer = UsbNative.CreateFile(path, 0, UsbNative.FILE_SHARE_READ | UsbNative.FILE_SHARE_WRITE, 0, UsbNative.OPEN_EXISTING, 0, 0);
        if (printer.IsInvalid)
        {
            return "";
        }

        byte[] io = new byte[1024];
        return DeviceIdFrom(io, UsbNative.Ioctl(printer, IOCTL_USBPRINT_GET_1284_ID, io));
    }

    /// <summary>
    /// The text of a 1284 device ID as usbprint hands it back: the printer's own answer begins with its
    /// length in two bytes, most significant first, which some drivers strip and some leave.
    /// </summary>
    internal static string DeviceIdFrom(byte[] io, int returned)
    {
        if (returned <= 0)
        {
            return "";
        }

        // An ID's text begins with a letter, and a length under 8192 with a byte below a space.
        int start = 0;
        int end = Math.Min(returned, io.Length);
        if (end >= 2 && io[0] < 0x20)
        {
            start = 2;
            end = Math.Clamp((io[0] << 8) | io[1], 2, end);
        }

        string text = Encoding.ASCII.GetString(io, start, end - start);
        int nul = text.IndexOf('\0');
        return (nul >= 0 ? text[..nul] : text).TrimEnd();
    }

    /// <summary>
    /// The spooler's port for a usbprint interface: usbmon keeps "Base Name" and "Port Number" under the
    /// interface's own registry key, and the port is the two together ("USB" and 1 make "USB001").
    /// </summary>
    private static string PortOf(string interfacePath)
    {
        if (!interfacePath.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return "";
        }

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Control\DeviceClasses\{GUID_DEVINTERFACE_USBPRINT:B}\##?#{interfacePath[4..]}\#\Device Parameters");
            return key is null ? "" : PortName(key.GetValue("Base Name") as string, key.GetValue("Port Number"));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return "";
        }
    }

    /// <summary>usbmon's port name from its two registry values; empty when either is missing.</summary>
    internal static string PortName(string? baseName, object? number) =>
        string.IsNullOrEmpty(baseName) || number is not int n ? "" : $"{baseName}{n:D3}";

    public int PrintRaw(string printer, ReadOnlySpan<byte> bytes, string documentName, string? outputFile)
    {
        nint handle = 0;
        if (!OpenPrinter(printer, &handle, 0))
        {
            throw new DeviceOpenException($"The printer '{printer}' could not be opened: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}", Marshal.GetLastPInvokeError());
        }

        nint doc = Marshal.StringToHGlobalUni(documentName);
        nint type = Marshal.StringToHGlobalUni(DriverVersion(handle) >= 4 ? "XPS_PASS" : "RAW");
        nint file = outputFile is null ? 0 : Marshal.StringToHGlobalUni(outputFile);
        try
        {
            var info = new DOC_INFO_1 { pDocName = doc, pOutputFile = file, pDatatype = type };
            uint job = StartDocPrinter(handle, 1, &info);
            if (job == 0)
            {
                throw new DeviceIOException($"The printer '{printer}' refused the job: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}", Marshal.GetLastPInvokeError());
            }

            try
            {
                if (!StartPagePrinter(handle))
                {
                    throw new DeviceIOException($"The printer '{printer}' refused the job: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}", Marshal.GetLastPInvokeError());
                }

                fixed (byte* p = bytes)
                {
                    uint written = 0;
                    if (bytes.Length > 0 && (!WritePrinter(handle, p, (uint)bytes.Length, &written) || written != bytes.Length))
                    {
                        throw new DeviceIOException($"The printer '{printer}' took {written} of {bytes.Length} bytes: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}", Marshal.GetLastPInvokeError());
                    }
                }

                EndPagePrinter(handle);
            }
            finally
            {
                EndDocPrinter(handle);
            }

            return (int)job;
        }
        finally
        {
            Marshal.FreeHGlobal(doc);
            Marshal.FreeHGlobal(type);
            if (file != 0)
            {
                Marshal.FreeHGlobal(file);
            }

            ClosePrinter(handle);
        }
    }

    /// <summary>The queue's driver version (DRIVER_INFO_2's cVersion): 3 for the classic model, 4 for the XPS-based one; 0 unknown.</summary>
    private static uint DriverVersion(nint handle)
    {
        uint needed = 0;
        GetPrinterDriver(handle, null, 2, null, 0, &needed);
        if (needed < 4)
        {
            return 0;
        }

        byte[] buffer = new byte[needed];
        fixed (byte* p = buffer)
        {
            return GetPrinterDriver(handle, null, 2, p, needed, &needed) ? *(uint*)p : 0;
        }
    }
}
