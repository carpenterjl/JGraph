using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using JGraph.Devices.Usb;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace JGraph.Devices.WinUsb;

/// <summary>winusb.dll (winusb.h): the generic USB driver's user-mode interface.</summary>
internal static unsafe partial class WinUsbNative
{
    public const uint SHORT_PACKET_TERMINATE = 0x01;
    public const uint AUTO_CLEAR_STALL = 0x02;
    public const uint PIPE_TRANSFER_TIMEOUT = 0x03;
    public const uint IGNORE_SHORT_PACKETS = 0x04;
    public const uint ALLOW_PARTIAL_READS = 0x05;
    public const uint AUTO_FLUSH = 0x06;
    public const uint RAW_IO = 0x07;
    public const uint MAXIMUM_TRANSFER_SIZE = 0x08;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WINUSB_SETUP_PACKET
    {
        public byte RequestType;
        public byte Request;
        public ushort Value;
        public ushort Index;
        public ushort Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINUSB_PIPE_INFORMATION
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct USB_INTERFACE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public byte bInterfaceNumber;
        public byte bAlternateSetting;
        public byte bNumEndpoints;
        public byte bInterfaceClass;
        public byte bInterfaceSubClass;
        public byte bInterfaceProtocol;
        public byte iInterface;
    }

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_Initialize(SafeFileHandle device, nint* handle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_Free(nint handle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_GetAssociatedInterface(nint handle, byte index, nint* associated);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_QueryInterfaceSettings(nint handle, byte alternate, USB_INTERFACE_DESCRIPTOR* descriptor);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_QueryPipe(nint handle, byte alternate, byte pipeIndex, WINUSB_PIPE_INFORMATION* pipe);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_ControlTransfer(nint handle, WINUSB_SETUP_PACKET setup, byte* buffer, uint length, uint* transferred, nint overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_ReadPipe(nint handle, byte pipe, byte* buffer, uint length, uint* transferred, nint overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_WritePipe(nint handle, byte pipe, byte* buffer, uint length, uint* transferred, nint overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_SetPipePolicy(nint handle, byte pipe, uint policy, uint length, void* value);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_ResetPipe(nint handle, byte pipe);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_AbortPipe(nint handle, byte pipe);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_FlushPipe(nint handle, byte pipe);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_SetCurrentAlternateSetting(nint handle, byte setting);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_GetCurrentAlternateSetting(nint handle, byte* setting);
}

/// <summary>One pipe of an interface.</summary>
public sealed record WinUsbPipe(int Interface, int Address, string Direction, string Type, int MaxPacketSize, int Interval);

/// <summary>
/// A USB device opened through WinUSB (device classes plan, stage 8): control transfers on the
/// default pipe; bulk and interrupt transfers on any pipe of any of its interfaces (a composite's
/// other interfaces through WinUsb_GetAssociatedInterface); pipe policies; alternate settings. A
/// transfer that has not finished in its timeout fails with <see cref="TimeoutException"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class WinUsbDevice : IDisposable
{
    private readonly SafeFileHandle _file;
    private readonly nint _first;
    private readonly Dictionary<int, nint> _interfaces = new();
    private readonly Dictionary<byte, (nint Handle, WinUsbPipe Pipe)> _pipes = new();
    private bool _disposed;

    private WinUsbDevice(SafeFileHandle file, nint first, string path)
    {
        _file = file;
        _first = first;
        Path = path;
        Discover();
    }

    public string Path { get; }

    public IReadOnlyList<WinUsbPipe> Pipes => _pipes.Values.Select(static p => p.Pipe).OrderBy(static p => p.Address).ToList();

    public IReadOnlyCollection<int> Interfaces => _interfaces.Keys;

    /// <summary>
    /// The WinUSB interface paths of a USB device, each with the interface it opens (-1 for a whole
    /// device bound to WinUSB), from the device interface GUIDs its driver registered (an INF, or MS OS
    /// 2.0 descriptors); none when every interface is bound to another driver.
    /// </summary>
    public static IReadOnlyList<(int Interface, string Path)> PathsOf(string usbInstanceId)
    {
        var paths = new List<(int, string)>();
        foreach ((string instance, int number) in new[] { (usbInstanceId, -1) }.Concat(ChildInstances(usbInstanceId)))
        {
            foreach (Guid guid in InterfaceGuids(instance))
            {
                foreach ((string owner, string path) in UsbEnumerator.InterfacePaths(guid))
                {
                    if (owner.Equals(instance, StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add((number, path));
                    }
                }
            }
        }

        return paths.OrderBy(static p => p.Item1).ToList();
    }

    /// <summary>The GUIDs under the devnode's "Device Parameters" key, where WinUSB reads them.</summary>
    private static IEnumerable<Guid> InterfaceGuids(string instance)
    {
        var guids = new List<Guid>();
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instance}\Device Parameters");
            object? value = key?.GetValue("DeviceInterfaceGUIDs") ?? key?.GetValue("DeviceInterfaceGUID");
            IEnumerable<string> texts = value switch
            {
                string[] many => many,
                string one => [one],
                _ => [],
            };
            foreach (string text in texts)
            {
                if (Guid.TryParse(text, out Guid guid))
                {
                    guids.Add(guid);
                }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return guids;
    }

    /// <summary>A composite device's interface devnodes (<c>USB\VID_x&amp;PID_y&amp;MI_nn\...</c>) and their numbers.</summary>
    private static List<(string Instance, int Interface)> ChildInstances(string usbInstanceId)
    {
        var children = new List<(string, int)>();
        uint devInst = 0;
        if (Usb.UsbNative.CM_Locate_DevNode(&devInst, usbInstanceId, 0) != Usb.UsbNative.CR_SUCCESS)
        {
            return children;
        }

        uint child = 0;
        if (Usb.UsbNative.CM_Get_Child(&child, devInst, 0) != Usb.UsbNative.CR_SUCCESS)
        {
            return children;
        }

        for (int guard = 0; guard < 64; guard++)
        {
            string id = UsbEnumerator.DeviceId(child);
            int mi = id.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase);
            if (mi >= 0 && mi + 6 <= id.Length && int.TryParse(id.AsSpan(mi + 4, 2), System.Globalization.NumberStyles.HexNumber, null, out int number))
            {
                children.Add((id, number));
            }

            uint next = 0;
            if (Usb.UsbNative.CM_Get_Sibling(&next, child, 0) != Usb.UsbNative.CR_SUCCESS)
            {
                break;
            }

            child = next;
        }

        return children;
    }

    /// <summary>Opens a WinUSB interface path.</summary>
    public static WinUsbDevice Open(string path)
    {
        SafeFileHandle file = Usb.UsbNative.CreateFile(path, Usb.UsbNative.GENERIC_READ | Usb.UsbNative.GENERIC_WRITE,
            Usb.UsbNative.FILE_SHARE_READ | Usb.UsbNative.FILE_SHARE_WRITE, 0, Usb.UsbNative.OPEN_EXISTING, 0x80 | Serial.CommNative.FILE_FLAG_OVERLAPPED, 0);
        if (file.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            file.Dispose();
            throw new DeviceOpenException(error == 5 ? "The device is open in another program." : $"The device could not be opened: {new Win32Exception(error).Message}", error);
        }

        nint handle = 0;
        if (!WinUsbNative.WinUsb_Initialize(file, &handle))
        {
            int error = Marshal.GetLastPInvokeError();
            file.Dispose();
            throw new DeviceOpenException($"WinUSB could not take the device: {new Win32Exception(error).Message}", error);
        }

        return new WinUsbDevice(file, handle, path);
    }

    private void Discover()
    {
        nint current = _first;
        for (int index = -1; index < 32; index++)
        {
            if (index >= 0)
            {
                nint next = 0;
                if (!WinUsbNative.WinUsb_GetAssociatedInterface(_first, (byte)index, &next))
                {
                    break;
                }

                current = next;
            }

            WinUsbNative.USB_INTERFACE_DESCRIPTOR descriptor;
            if (!WinUsbNative.WinUsb_QueryInterfaceSettings(current, 0, &descriptor))
            {
                continue;
            }

            _interfaces[descriptor.bInterfaceNumber] = current;
            byte setting = 0;
            WinUsbNative.WinUsb_GetCurrentAlternateSetting(current, &setting);
            WinUsbNative.WinUsb_QueryInterfaceSettings(current, setting, &descriptor);
            for (byte p = 0; p < descriptor.bNumEndpoints; p++)
            {
                WinUsbNative.WINUSB_PIPE_INFORMATION info;
                if (WinUsbNative.WinUsb_QueryPipe(current, setting, p, &info))
                {
                    string type = info.PipeType switch { 0 => "control", 1 => "isochronous", 2 => "bulk", _ => "interrupt" };
                    _pipes[info.PipeId] = (current, new WinUsbPipe(descriptor.bInterfaceNumber, info.PipeId,
                        (info.PipeId & 0x80) != 0 ? "in" : "out", type, info.MaximumPacketSize, info.Interval));
                }
            }
        }
    }

    /// <summary>A control transfer on the default pipe; answers the bytes read (an IN request) or an empty array.</summary>
    public byte[] Control(byte requestType, byte request, ushort value, ushort index, ReadOnlySpan<byte> data, int readLength, TimeSpan timeout)
    {
        bool input = (requestType & 0x80) != 0;
        int length = input ? readLength : data.Length;
        byte[] buffer = new byte[length];
        if (!input)
        {
            data.CopyTo(buffer);
        }

        SetTimeout(_first, 0, timeout);
        var setup = new WinUsbNative.WINUSB_SETUP_PACKET { RequestType = requestType, Request = request, Value = value, Index = index, Length = (ushort)length };
        uint done = 0;
        fixed (byte* p = buffer)
        {
            if (!WinUsbNative.WinUsb_ControlTransfer(_first, setup, p, (uint)length, &done, 0))
            {
                throw Failure("The control transfer");
            }
        }

        return input ? buffer[..(int)done] : [];
    }

    /// <summary>Reads up to <paramref name="count"/> bytes from an IN pipe; answers what came.</summary>
    public byte[] Read(byte pipe, int count, TimeSpan timeout)
    {
        (nint handle, WinUsbPipe info) = PipeOf(pipe);
        if (info.Direction != "in")
        {
            throw new DeviceIOException($"Endpoint 0x{pipe:X2} is an OUT endpoint; read from an IN one (0x80 and above).");
        }

        SetTimeout(handle, pipe, timeout);
        byte[] buffer = new byte[count];
        uint done = 0;
        fixed (byte* p = buffer)
        {
            if (!WinUsbNative.WinUsb_ReadPipe(handle, pipe, p, (uint)count, &done, 0))
            {
                throw Failure($"The read from endpoint 0x{pipe:X2}");
            }
        }

        return buffer[..(int)done];
    }

    /// <summary>Writes every byte to an OUT pipe.</summary>
    public int Write(byte pipe, ReadOnlySpan<byte> data, TimeSpan timeout)
    {
        (nint handle, WinUsbPipe info) = PipeOf(pipe);
        if (info.Direction != "out")
        {
            throw new DeviceIOException($"Endpoint 0x{pipe:X2} is an IN endpoint; write to an OUT one (below 0x80).");
        }

        SetTimeout(handle, pipe, timeout);
        uint done = 0;
        fixed (byte* p = data)
        {
            if (!WinUsbNative.WinUsb_WritePipe(handle, pipe, p, (uint)data.Length, &done, 0))
            {
                throw Failure($"The write to endpoint 0x{pipe:X2}");
            }
        }

        return (int)done;
    }

    /// <summary>Sets a boolean pipe policy (RAW_IO, SHORT_PACKET_TERMINATE, AUTO_CLEAR_STALL, …).</summary>
    public void SetPolicy(byte pipe, uint policy, bool on)
    {
        (nint handle, _) = PipeOf(pipe);
        byte value = on ? (byte)1 : (byte)0;
        if (!WinUsbNative.WinUsb_SetPipePolicy(handle, pipe, policy, 1, &value))
        {
            throw Failure("The pipe policy");
        }
    }

    public void ResetPipe(byte pipe)
    {
        (nint handle, _) = PipeOf(pipe);
        if (!WinUsbNative.WinUsb_ResetPipe(handle, pipe))
        {
            throw Failure("Resetting the pipe");
        }
    }

    public void AbortPipe(byte pipe)
    {
        (nint handle, _) = PipeOf(pipe);
        WinUsbNative.WinUsb_AbortPipe(handle, pipe);
    }

    public void FlushPipe(byte pipe)
    {
        (nint handle, _) = PipeOf(pipe);
        WinUsbNative.WinUsb_FlushPipe(handle, pipe);
    }

    /// <summary>Clears a halted endpoint: CLEAR_FEATURE(ENDPOINT_HALT) and the pipe's reset.</summary>
    public void ClearHalt(byte pipe) => ResetPipe(pipe);

    /// <summary>Selects an alternate setting of an interface; its pipes are read again.</summary>
    public void SetAlternateSetting(int interfaceNumber, int setting)
    {
        if (!_interfaces.TryGetValue(interfaceNumber, out nint handle))
        {
            throw new DeviceIOException($"The device has no interface {interfaceNumber} under WinUSB.");
        }

        if (!WinUsbNative.WinUsb_SetCurrentAlternateSetting(handle, (byte)setting))
        {
            throw Failure("Selecting the alternate setting");
        }

        foreach (byte address in _pipes.Where(p => p.Value.Pipe.Interface == interfaceNumber).Select(static p => p.Key).ToList())
        {
            _pipes.Remove(address);
        }

        WinUsbNative.USB_INTERFACE_DESCRIPTOR descriptor;
        WinUsbNative.WinUsb_QueryInterfaceSettings(handle, (byte)setting, &descriptor);
        for (byte p = 0; p < descriptor.bNumEndpoints; p++)
        {
            WinUsbNative.WINUSB_PIPE_INFORMATION info;
            if (WinUsbNative.WinUsb_QueryPipe(handle, (byte)setting, p, &info))
            {
                string type = info.PipeType switch { 0 => "control", 1 => "isochronous", 2 => "bulk", _ => "interrupt" };
                _pipes[info.PipeId] = (handle, new WinUsbPipe(interfaceNumber, info.PipeId, (info.PipeId & 0x80) != 0 ? "in" : "out",
                    type, info.MaximumPacketSize, info.Interval));
            }
        }
    }

    public int AlternateSetting(int interfaceNumber)
    {
        byte setting = 0;
        return _interfaces.TryGetValue(interfaceNumber, out nint handle) && WinUsbNative.WinUsb_GetCurrentAlternateSetting(handle, &setting) ? setting : 0;
    }

    private (nint Handle, WinUsbPipe Pipe) PipeOf(byte pipe) =>
        _pipes.TryGetValue(pipe, out (nint, WinUsbPipe) found)
            ? found
            : throw new DeviceIOException($"The device has no endpoint 0x{pipe:X2}; its endpoints are {string.Join(", ", _pipes.Keys.Select(static k => $"0x{k:X2}"))}.");

    private static void SetTimeout(nint handle, byte pipe, TimeSpan timeout)
    {
        uint ms = (uint)Math.Clamp(timeout.TotalMilliseconds, 1, uint.MaxValue - 1);
        WinUsbNative.WinUsb_SetPipePolicy(handle, pipe, WinUsbNative.PIPE_TRANSFER_TIMEOUT, 4, &ms);
    }

    private static Exception Failure(string what)
    {
        int error = Marshal.GetLastPInvokeError();
        return error switch
        {
            Serial.CommNative.ERROR_SEM_TIMEOUT or 1460 => new TimeoutException($"{what} did not finish in time."),
            Serial.CommNative.ERROR_GEN_FAILURE => new DeviceIOException($"{what} failed: the device stalled the request.", error),
            Serial.CommNative.ERROR_BAD_COMMAND or Serial.CommNative.ERROR_DEVICE_NOT_CONNECTED or 1167 =>
                new DeviceConnectionLostException($"{what} failed: the device is no longer connected.", error),
            _ => new DeviceIOException($"{what} failed: {new Win32Exception(error).Message}", error),
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopReader();
        foreach (nint handle in _interfaces.Values.Where(h => h != _first))
        {
            WinUsbNative.WinUsb_Free(handle);
        }

        WinUsbNative.WinUsb_Free(_first);
        _file.Dispose();
    }

    /// <summary>
    /// A driver-binding refusal naming the drivers Windows bound (each interface's, for a composite) and
    /// how to bind WinUSB instead.
    /// </summary>
    public static string NotBoundSentence(UsbDeviceInfo device, int? interfaceNumber = null)
    {
        string bound = interfaceNumber is { } n
            ? device.InterfaceDrivers.TryGetValue(n, out string? one) ? $"interface {n} is bound to {one}" : $"the device has no interface {n}"
            : device.InterfaceDrivers.Count > 0
                ? string.Join("; ", device.InterfaceDrivers.GroupBy(static p => p.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(static g => (Numbers: g.Select(static p => p.Key).Order().ToList(), Driver: g.Key))
                    .OrderBy(static g => g.Numbers[0])
                    .Select(static g => g.Numbers.Count == 1
                        ? $"interface {g.Numbers[0]} is bound to {g.Driver}"
                        : $"interfaces {string.Join(", ", g.Numbers.Take(g.Numbers.Count - 1))} and {g.Numbers[^1]} are bound to {g.Driver}"))
                : device.Driver.Length > 0 ? $"it is bound to {device.Driver}" : "no driver is bound";
        return new StringBuilder()
            .Append($"No WinUSB driver is bound to {device.VendorId:X4}:{device.ProductId:X4}{(interfaceNumber is { } i ? $" interface {i}" : "")}")
            .Append($" ({bound}).")
            .Append(" Bind WinUSB with Zadig or an INF, or give the firmware MS OS 2.0 descriptors, which make Windows bind it by itself.")
            .ToString();
    }

    // --- a continuous read ---------------------------------------------------------------------------

    private Thread? _reader;
    private volatile bool _readerStop;
    private byte _readerPipe;

    /// <summary>
    /// Reads an IN pipe continuously on a thread of its own, <paramref name="chunk"/> bytes a request,
    /// handing each transfer to <paramref name="onData"/> and a failure to <paramref name="onError"/>
    /// (both on the reader thread). One reader at a time.
    /// </summary>
    public void StartReader(byte pipe, int chunk, Action<byte[]> onData, Action<Exception> onError)
    {
        StopReader();
        (nint handle, WinUsbPipe info) = PipeOf(pipe);
        if (info.Direction != "in")
        {
            throw new DeviceIOException($"Endpoint 0x{pipe:X2} is an OUT endpoint; a reader reads an IN one (0x80 and above).");
        }

        _readerStop = false;
        _readerPipe = pipe;
        SetTimeout(handle, pipe, TimeSpan.FromMilliseconds(250));
        _reader = new Thread(() =>
        {
            byte[] buffer = new byte[Math.Max(1, chunk)];
            while (!_readerStop)
            {
                uint done = 0;
                bool ok;
                fixed (byte* p = buffer)
                {
                    ok = WinUsbNative.WinUsb_ReadPipe(handle, pipe, p, (uint)buffer.Length, &done, 0);
                }

                if (_readerStop)
                {
                    break;
                }

                if (!ok)
                {
                    Exception failure = Failure($"The read from endpoint 0x{pipe:X2}");
                    if (failure is TimeoutException)
                    {
                        continue;
                    }

                    try
                    {
                        onError(failure);
                    }
                    catch (Exception)
                    {
                        // A handler on the reader thread must not end the process.
                    }

                    return;
                }

                if (done > 0)
                {
                    try
                    {
                        onData(buffer[..(int)done]);
                    }
                    catch (Exception)
                    {
                        // As above.
                    }
                }
            }
        })
        { IsBackground = true, Name = "JGraph WinUSB reader" };
        _reader.Start();
    }

    /// <summary>Stops the continuous read, if one runs.</summary>
    public void StopReader()
    {
        if (_reader is null)
        {
            return;
        }

        _readerStop = true;
        if (_pipes.TryGetValue(_readerPipe, out (nint Handle, WinUsbPipe Pipe) found))
        {
            WinUsbNative.WinUsb_AbortPipe(found.Handle, _readerPipe);
        }

        _reader.Join(2000);
        _reader = null;
    }
}
