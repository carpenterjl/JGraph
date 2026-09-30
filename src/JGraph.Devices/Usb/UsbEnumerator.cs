using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using static JGraph.Devices.Usb.UsbNative;

namespace JGraph.Devices.Usb;

/// <summary>The speed a USB device runs at.</summary>
public enum UsbSpeed
{
    Unknown,
    Low,
    Full,
    High,
    Super,
    SuperPlus,
}

/// <summary>
/// One USB device as the machine sees it (device classes plan, stage 6): its identity from the
/// device descriptor, its strings, where it sits (the hub chain and port), the driver Windows bound
/// to it, and its configuration descriptor's bytes for the decoder.
/// </summary>
public sealed record UsbDeviceInfo
{
    public required string InstanceId { get; init; }

    public int VendorId { get; init; }

    public int ProductId { get; init; }

    public int Release { get; init; }

    public int Class { get; init; }

    public int Subclass { get; init; }

    public int Protocol { get; init; }

    public string Manufacturer { get; init; } = "";

    public string Product { get; init; } = "";

    public string SerialNumber { get; init; } = "";

    public string Description { get; init; } = "";

    public UsbSpeed Speed { get; init; }

    /// <summary>The port chain from the root hub: [3, 2] is port 2 of the hub on the root's port 3.</summary>
    public IReadOnlyList<int> Ports { get; init; } = [];

    /// <summary>Which host controller's root hub it hangs from, counted from 1.</summary>
    public int Bus { get; init; }

    /// <summary>The service Windows bound: usbser, HidUsb, WINUSB, USBSTOR, usbccgp, USBHUB3, …</summary>
    public string Driver { get; init; } = "";

    public bool IsHub { get; init; }

    public string ParentInstanceId { get; init; } = "";

    public byte[] DeviceDescriptor { get; init; } = [];

    public byte[] ConfigurationDescriptor { get; init; } = [];

    public byte[] BosDescriptor { get; init; } = [];

    /// <summary>The languages string descriptor 0 lists, as LANGIDs (0x0409 is US English).</summary>
    public IReadOnlyList<int> Languages { get; init; } = [];

    /// <summary>Every string the descriptors name, by index, in the first language.</summary>
    public IReadOnlyDictionary<int, string> Strings { get; init; } = new Dictionary<int, string>();

    /// <summary>
    /// The service bound to each interface: a composite device's interfaces are devnodes of their own
    /// (<c>USB\VID_...&amp;MI_02</c>), each with a driver; a device with one function has its own for all.
    /// </summary>
    public IReadOnlyDictionary<int, string> InterfaceDrivers { get; init; } = new Dictionary<int, string>();

    /// <summary><c>bus-port.port…</c>, as lsusb writes a location: "1-3.2".</summary>
    public string Location => Bus == 0 ? "" : Ports.Count == 0 ? $"{Bus}" : $"{Bus}-{string.Join('.', Ports)}";
}

/// <summary>One port of a hub, with what is plugged into it.</summary>
public sealed record UsbHubPort(int Port, bool Connected, bool UserVisible, UsbSpeed Speed, string DeviceInstanceId);

/// <summary>
/// Lists the USB devices through SetupAPI and reads each one's descriptors through its parent hub's
/// IOCTLs (device classes plan, architecture E). Needs no admin rights and opens no device.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class UsbEnumerator
{
    /// <summary>Every USB device present, hubs included, in bus and port order.</summary>
    public static IReadOnlyList<UsbDeviceInfo> Devices()
    {
        Dictionary<string, string> hubPaths = InterfacePaths(GUID_DEVINTERFACE_USB_HUB);
        var rootHubs = hubPaths.Keys.Where(static id => id.StartsWith("USB\\ROOT_HUB", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToList();
        var hubHandles = new Dictionary<string, SafeFileHandle?>(StringComparer.OrdinalIgnoreCase);
        var devices = new List<UsbDeviceInfo>();
        try
        {
            foreach ((string instance, uint devInst) in DeviceInstances(GUID_DEVINTERFACE_USB_DEVICE))
            {
                devices.Add(Read(instance, devInst, hubPaths, rootHubs, hubHandles));
            }

            // Hubs expose the hub interface, not always the device one; the root hubs never do.
            foreach ((string instance, uint devInst) in DeviceInstances(GUID_DEVINTERFACE_USB_HUB))
            {
                if (!devices.Exists(d => d.InstanceId.Equals(instance, StringComparison.OrdinalIgnoreCase)))
                {
                    devices.Add(Read(instance, devInst, hubPaths, rootHubs, hubHandles));
                }
            }
        }
        finally
        {
            foreach (SafeFileHandle? handle in hubHandles.Values)
            {
                handle?.Dispose();
            }
        }

        return devices.OrderBy(static d => d.Bus).ThenBy(static d => string.Join(".", d.Ports.Select(static p => p.ToString("D3"))))
            .ThenBy(static d => d.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Every port of every hub: whether something is connected, whether the port is one a person can reach.</summary>
    public static IReadOnlyList<(string HubInstanceId, UsbHubPort Port)> HubPorts()
    {
        var result = new List<(string, UsbHubPort)>();
        Dictionary<string, string> driverKeys = DriverKeys();
        foreach ((string instance, string path) in InterfacePaths(GUID_DEVINTERFACE_USB_HUB))
        {
            using SafeFileHandle hub = CreateFile(path, GENERIC_WRITE, FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
            if (hub.IsInvalid)
            {
                continue;
            }

            int ports = PortCount(hub);
            for (int port = 1; port <= ports; port++)
            {
                (byte[]? info, _) = ConnectionInfo(hub, port);
                bool connected = info is not null && BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(31)) == 1;
                UsbSpeed speed = info is null ? UsbSpeed.Unknown : SpeedOf(info[23], hub, port);
                string key = connected ? DriverKeyDevice(hub, port) : "";
                string attached = key.Length > 0 && driverKeys.TryGetValue(key, out string? id) ? id : "";
                result.Add((instance, new UsbHubPort(port, connected, UserVisible(hub, port), speed, attached)));
            }
        }

        return result.OrderBy(static r => r.Item1, StringComparer.OrdinalIgnoreCase).ThenBy(static r => r.Item2.Port).ToList();
    }

    private static UsbDeviceInfo Read(string instance, uint devInst, Dictionary<string, string> hubPaths, List<string> rootHubs,
        Dictionary<string, SafeFileHandle?> hubHandles)
    {
        string service = RegistryString(devInst, SPDRP_SERVICE);
        string description = DevicePropertyString(devInst, DEVPKEY_Device_BusReportedDeviceDesc);
        if (description.Length == 0)
        {
            description = RegistryString(devInst, SPDRP_DEVICEDESC);
        }

        uint parent = 0;
        string parentId = CM_Get_Parent(&parent, devInst, 0) == CR_SUCCESS ? DeviceId(parent) : "";
        string[] locationPaths = RegistryStrings(devInst, SPDRP_LOCATION_PATHS);
        List<int> ports = PortChain(locationPaths);
        int bus = BusOf(locationPaths, rootHubs);
        bool isHub = hubPaths.ContainsKey(instance) || service.Contains("hub", StringComparison.OrdinalIgnoreCase);

        (int vid, int pid, int rev) = IdsOf(RegistryStrings(devInst, SPDRP_HARDWAREID));
        Dictionary<int, string> interfaceDrivers = InterfaceDriversOf(devInst);
        var info = new UsbDeviceInfo
        {
            InstanceId = instance,
            VendorId = vid,
            ProductId = pid,
            Release = rev,
            Driver = service,
            Description = description,
            ParentInstanceId = parentId,
            Ports = ports,
            Bus = bus,
            IsHub = isHub,
            InterfaceDrivers = interfaceDrivers,
        };

        // Descriptors come from the parent hub, for the port the device is on.
        if (ports.Count == 0 || !hubPaths.TryGetValue(parentId, out string? hubPath))
        {
            return info;
        }

        if (!hubHandles.TryGetValue(parentId, out SafeFileHandle? hub))
        {
            SafeFileHandle opened = CreateFile(hubPath, GENERIC_WRITE, FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
            hub = opened.IsInvalid ? null : opened;
            if (hub is null)
            {
                opened.Dispose();
            }

            hubHandles[parentId] = hub;
        }

        if (hub is null)
        {
            return info;
        }

        int port = ports[^1];
        (byte[]? connection, _) = ConnectionInfo(hub, port);
        if (connection is null)
        {
            return info;
        }

        byte[] device = connection.AsSpan(4, 18).ToArray();
        UsbSpeed speed = SpeedOf(connection[23], hub, port);
        byte[] configuration = Descriptor(hub, port, 2, 0, 0) ?? [];
        byte[] bos = BinaryPrimitives.ReadUInt16LittleEndian(device.AsSpan(2)) >= 0x0201 ? Descriptor(hub, port, 15, 0, 0) ?? [] : [];
        byte[]? zero = Descriptor(hub, port, 3, 0, 0);
        var languages = new List<int>();
        for (int at = 2; zero is not null && at + 1 < Math.Min(zero.Length, zero[0]); at += 2)
        {
            languages.Add(BinaryPrimitives.ReadUInt16LittleEndian(zero.AsSpan(at)));
        }

        ushort language = languages.Count > 0 ? (ushort)languages[0] : (ushort)0x0409;
        var strings = new Dictionary<int, string>();
        foreach (int index in UsbDescriptors.StringIndices(device, configuration))
        {
            string text = StringDescriptor(hub, port, index, language);
            if (text.Length > 0)
            {
                strings[index] = text;
            }
        }

        return info with
        {
            VendorId = BinaryPrimitives.ReadUInt16LittleEndian(device.AsSpan(8)),
            ProductId = BinaryPrimitives.ReadUInt16LittleEndian(device.AsSpan(10)),
            Release = BinaryPrimitives.ReadUInt16LittleEndian(device.AsSpan(12)),
            Class = device[4],
            Subclass = device[5],
            Protocol = device[6],
            Manufacturer = StringDescriptor(hub, port, device[14], language),
            Product = StringDescriptor(hub, port, device[15], language),
            SerialNumber = StringDescriptor(hub, port, device[16], language),
            Speed = speed,
            IsHub = info.IsHub || connection[24] != 0,
            DeviceDescriptor = device,
            ConfigurationDescriptor = configuration,
            BosDescriptor = bos,
            Languages = languages,
            Strings = strings,
        };
    }

    /// <summary>The services of a composite device's interface devnodes, by interface number.</summary>
    private static Dictionary<int, string> InterfaceDriversOf(uint devInst)
    {
        var result = new Dictionary<int, string>();
        uint child = 0;
        if (CM_Get_Child(&child, devInst, 0) != CR_SUCCESS)
        {
            return result;
        }

        for (int guard = 0; guard < 64; guard++)
        {
            Match m = InterfaceNumber().Match(DeviceId(child));
            if (m.Success)
            {
                result[Convert.ToInt32(m.Groups[1].Value, 16)] = RegistryString(child, SPDRP_SERVICE);
            }

            uint next = 0;
            if (CM_Get_Sibling(&next, child, 0) != CR_SUCCESS)
            {
                break;
            }

            child = next;
        }

        return result;
    }

    [GeneratedRegex(@"&MI_([0-9A-F]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex InterfaceNumber();

    /// <summary>The instance ID of every present USB device, by the driver key a hub's port reports.</summary>
    private static Dictionary<string, string> DriverKeys()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Guid guid in new[] { GUID_DEVINTERFACE_USB_DEVICE, GUID_DEVINTERFACE_USB_HUB })
        {
            foreach ((string instance, uint devInst) in DeviceInstances(guid))
            {
                string key = RegistryString(devInst, SPDRP_DRIVER);
                if (key.Length > 0)
                {
                    result[key] = instance;
                }
            }
        }

        return result;
    }

    /// <summary>USB_NODE_CONNECTION_INFORMATION_EX for a port: 35 bytes and 32 pipes' room.</summary>
    private static (byte[]? Info, int Length) ConnectionInfo(SafeFileHandle hub, int port)
    {
        byte[] io = new byte[35 + (32 * 11)];
        BinaryPrimitives.WriteUInt32LittleEndian(io, (uint)port);
        int n = Ioctl(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, io);
        return n < 35 ? (null, 0) : (io, n);
    }

    private static UsbSpeed SpeedOf(byte speed, SafeFileHandle hub, int port)
    {
        byte[] v2 = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(v2, (uint)port);
        BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(v2.AsSpan(8), 0x7);
        if (Ioctl(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2, v2) >= 16)
        {
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(v2.AsSpan(12));
            if ((flags & 0x4) != 0)
            {
                return UsbSpeed.SuperPlus;
            }

            if ((flags & 0x1) != 0)
            {
                return UsbSpeed.Super;
            }
        }

        return speed switch
        {
            0 => UsbSpeed.Low,
            1 => UsbSpeed.Full,
            2 => UsbSpeed.High,
            3 => UsbSpeed.Super,
            _ => UsbSpeed.Unknown,
        };
    }

    /// <summary>A descriptor read by the hub from the device on <paramref name="port"/>, or null.</summary>
    internal static byte[]? Descriptor(SafeFileHandle hub, int port, int type, int index, int language)
    {
        const int Header = 12;

        // A string descriptor is at most 255 bytes, and devices refuse a longer request for one (USBView asks for 255).
        int wanted = type == 3 ? 255 : 4096;
        byte[] io = new byte[Header + wanted];
        BinaryPrimitives.WriteUInt32LittleEndian(io, (uint)port);
        io[4] = 0x80;
        io[5] = 6; // GET_DESCRIPTOR
        BinaryPrimitives.WriteUInt16LittleEndian(io.AsSpan(6), (ushort)((type << 8) | index));
        BinaryPrimitives.WriteUInt16LittleEndian(io.AsSpan(8), (ushort)language);
        BinaryPrimitives.WriteUInt16LittleEndian(io.AsSpan(10), (ushort)wanted);
        int n = Ioctl(hub, IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, io);
        if (n <= Header)
        {
            return null;
        }

        byte[] data = io.AsSpan(Header, n - Header).ToArray();

        // A configuration or BOS descriptor answers its total length in its header; trim to it.
        if (type is 2 or 15 && data.Length >= 4)
        {
            int total = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
            if (total > 0 && total < data.Length)
            {
                data = data[..total];
            }
        }

        return data;
    }

    internal static string StringDescriptor(SafeFileHandle hub, int port, int index, ushort language)
    {
        if (index == 0)
        {
            return "";
        }

        byte[]? data = Descriptor(hub, port, 3, index, language);
        if (data is null || data.Length < 2)
        {
            return "";
        }

        int length = Math.Min(data[0], data.Length);
        return length <= 2 ? "" : Encoding.Unicode.GetString(data, 2, (length - 2) & ~1).TrimEnd('\0');
    }

    private static int PortCount(SafeFileHandle hub)
    {
        byte[] io = new byte[76];
        return Ioctl(hub, IOCTL_USB_GET_NODE_INFORMATION, io) >= 7 ? io[6] : 0;
    }

    private static bool UserVisible(SafeFileHandle hub, int port)
    {
        // USB_PORT_CONNECTOR_PROPERTIES: ConnectionIndex, ActualLength, UsbPortProperties (bit 0 UserConnectable).
        byte[] io = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(io, (uint)port);
        return Ioctl(hub, IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES, io) >= 12 && (io[8] & 1) != 0;
    }

    private static string DriverKeyDevice(SafeFileHandle hub, int port)
    {
        // USB_NODE_CONNECTION_DRIVERKEY_NAME: ConnectionIndex, ActualLength, DriverKeyName[].
        byte[] io = new byte[8 + 1024];
        BinaryPrimitives.WriteUInt32LittleEndian(io, (uint)port);
        int n = Ioctl(hub, IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME, io);
        return n > 8 ? Encoding.Unicode.GetString(io, 8, n - 8).TrimEnd('\0') : "";
    }

    // --- SetupAPI and the configuration manager ------------------------------------------------------

    /// <summary>Device instance IDs and devnodes of the devices that expose <paramref name="interfaceGuid"/>.</summary>
    internal static List<(string InstanceId, uint DevInst)> DeviceInstances(Guid interfaceGuid)
    {
        var result = new List<(string, uint)>();
        nint set = SetupDiGetClassDevs(&interfaceGuid, null, 0, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == -1)
        {
            return result;
        }

        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, &data); i++)
            {
                result.Add((DeviceId(data.DevInst), data.DevInst));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    /// <summary>Each device exposing <paramref name="interfaceGuid"/>, by instance ID, and its interface path.</summary>
    internal static Dictionary<string, string> InterfacePaths(Guid interfaceGuid)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        nint set = SetupDiGetClassDevs(&interfaceGuid, null, 0, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == -1)
        {
            return result;
        }

        try
        {
            var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)sizeof(SP_DEVICE_INTERFACE_DATA) };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, null, &interfaceGuid, i, &iface); i++)
            {
                var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
                uint required = 0;
                SetupDiGetDeviceInterfaceDetail(set, &iface, null, 0, &required, null);
                if (required == 0)
                {
                    continue;
                }

                byte* detail = (byte*)NativeMemory.AllocZeroed(required);
                try
                {
                    *(uint*)detail = (uint)(IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                    if (SetupDiGetDeviceInterfaceDetail(set, &iface, detail, required, null, &devInfo))
                    {
                        result[DeviceId(devInfo.DevInst)] = new string((char*)(detail + 4));
                    }
                }
                finally
                {
                    NativeMemory.Free(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    internal static string DeviceId(uint devInst)
    {
        char* buffer = stackalloc char[512];
        return CM_Get_Device_ID(devInst, buffer, 512, 0) == CR_SUCCESS ? new string(buffer) : "";
    }

    internal static string RegistryString(uint devInst, uint property)
    {
        byte[] buffer = new byte[2048];
        uint type = 0;
        uint length = (uint)buffer.Length;
        fixed (byte* p = buffer)
        {
            if (CM_Get_DevNode_Registry_Property(devInst, property + 1, &type, p, &length, 0) != CR_SUCCESS || length < 2)
            {
                return "";
            }
        }

        return Encoding.Unicode.GetString(buffer, 0, (int)length).TrimEnd('\0');
    }

    internal static string[] RegistryStrings(uint devInst, uint property) =>
        RegistryString(devInst, property).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    internal static string DevicePropertyString(uint devInst, DEVPROPKEY key)
    {
        byte[] buffer = new byte[2048];
        uint type = 0;
        uint length = (uint)buffer.Length;
        fixed (byte* p = buffer)
        {
            if (CM_Get_DevNode_Property(devInst, &key, &type, p, &length, 0) != CR_SUCCESS || length < 2)
            {
                return "";
            }
        }

        return Encoding.Unicode.GetString(buffer, 0, (int)length).TrimEnd('\0');
    }

    /// <summary>The ports in a location path: "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)#USB(2)" is [3, 2].</summary>
    internal static List<int> PortChain(string[] locationPaths)
    {
        string? path = locationPaths.FirstOrDefault(static p => p.Contains("#USBROOT(", StringComparison.OrdinalIgnoreCase));
        if (path is null)
        {
            return [];
        }

        return UsbPort().Matches(path).Select(static m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }

    // USB(n) is a hub port; USBMI(n), an interface of a composite device, is not.
    [GeneratedRegex(@"#USB\((\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex UsbPort();

    /// <summary>The bus number: the place of the controller in the root hubs' order, from 1.</summary>
    private static int BusOf(string[] locationPaths, List<string> rootHubs)
    {
        string? path = locationPaths.FirstOrDefault(static p => p.Contains("#USBROOT(", StringComparison.OrdinalIgnoreCase));
        if (path is null)
        {
            return 0;
        }

        string controller = path[..path.IndexOf("#USBROOT(", StringComparison.OrdinalIgnoreCase)];
        for (int i = 0; i < rootHubs.Count; i++)
        {
            uint devInst = 0;
            if (CM_Locate_DevNode(&devInst, rootHubs[i], 0) == CR_SUCCESS)
            {
                string[] rootPaths = RegistryStrings(devInst, SPDRP_LOCATION_PATHS);
                if (rootPaths.Any(p => p.StartsWith(controller, StringComparison.OrdinalIgnoreCase)))
                {
                    return i + 1;
                }
            }
        }

        return 0;
    }

    /// <summary>VID, PID and REV from a hardware ID "USB\VID_2E8A&amp;PID_000A&amp;REV_0100".</summary>
    internal static (int Vid, int Pid, int Rev) IdsOf(string[] hardwareIds)
    {
        foreach (string id in hardwareIds)
        {
            Match m = VidPid().Match(id);
            if (m.Success)
            {
                int rev = m.Groups[3].Success ? Convert.ToInt32(m.Groups[3].Value, 16) : 0;
                return (Convert.ToInt32(m.Groups[1].Value, 16), Convert.ToInt32(m.Groups[2].Value, 16), rev);
            }
        }

        return (0, 0, 0);
    }

    // A root hub's hardware ID spells them without the underscores: USB\ROOT_HUB30&VID8086&PID7AE0&REV0000.
    [GeneratedRegex(@"VID_?([0-9A-F]{4})&PID_?([0-9A-F]{4})(?:&REV_?([0-9A-F]{4}))?", RegexOptions.IgnoreCase)]
    private static partial Regex VidPid();
}
