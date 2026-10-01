using System.Globalization;
using System.Runtime.Versioning;
using JGraph.Devices.Serial;
using JGraph.Devices.Usb;
using JGraph.Devices.WinUsb;

namespace JGraph.Scripting.Devices;

/// <summary>What choosing a <see cref="DeviceAction"/> does.</summary>
public enum DeviceActionKind
{
    /// <summary><see cref="DeviceAction.Text"/> is a line of MATLAB code the host writes at its prompt, for the user to run.</summary>
    Code,

    /// <summary><see cref="DeviceAction.Text"/> goes to the clipboard.</summary>
    Copy,

    /// <summary><see cref="DeviceAction.Text"/> is a serial port the host's Serial Explorer pane should select.</summary>
    SerialExplorer,
}

/// <summary>One thing the Devices pane offers for a node.</summary>
/// <param name="Title">The menu text: <c>Open as serialport</c>.</param>
/// <param name="Text">The code line, the text to copy, or the port, by <paramref name="Kind"/>.</param>
/// <param name="Kind">What choosing it does.</param>
public sealed record DeviceAction(string Title, string Text, DeviceActionKind Kind = DeviceActionKind.Code);

/// <summary>What a <see cref="DeviceNode"/> is, so a host can pick how to draw it.</summary>
public enum DeviceNodeKind
{
    /// <summary>A heading: "Serial ports", "USB".</summary>
    Group,

    /// <summary>A serial port.</summary>
    SerialPort,

    /// <summary>A USB hub, a root hub included.</summary>
    Hub,

    /// <summary>A USB device.</summary>
    Device,
}

/// <summary>One line of the Devices pane.</summary>
/// <param name="Title">What the line says first: <c>COM3</c>, <c>0483:5740 STM32 Virtual ComPort</c>.</param>
/// <param name="Detail">What it says after, dimmer: the class, the driver, the speed.</param>
/// <param name="Kind">What the node is.</param>
/// <param name="ToolTip">The longer description shown on hover; empty for none.</param>
/// <param name="Actions">What the pane offers for it, the first being the double-click's.</param>
/// <param name="Children">The nodes under it.</param>
public sealed record DeviceNode(
    string Title,
    string Detail,
    DeviceNodeKind Kind,
    string ToolTip,
    IReadOnlyList<DeviceAction> Actions,
    IReadOnlyList<DeviceNode> Children);

/// <summary>
/// What the machine has attached, read once: the input of <see cref="DevicePaneModel.Build"/>. A
/// record of plain values, so a test builds one by hand.
/// </summary>
/// <param name="SerialPorts">Every serial port's name, in natural order.</param>
/// <param name="PortDescriptions">Port name → what Windows calls it, for the ports that say.</param>
/// <param name="Usb">The USB devices, hubs included.</param>
/// <param name="ComPorts">USB instance ID → the COM ports under that device.</param>
/// <param name="HidCollections">USB instance ID → how many HID collections that device has.</param>
/// <param name="WinUsb">The instance IDs of the devices with an interface WinUSB is bound to.</param>
public sealed record DeviceSnapshot(
    IReadOnlyList<string> SerialPorts,
    IReadOnlyDictionary<string, string> PortDescriptions,
    IReadOnlyList<UsbDeviceInfo> Usb,
    IReadOnlyDictionary<string, List<string>> ComPorts,
    IReadOnlyDictionary<string, int> HidCollections,
    IReadOnlySet<string> WinUsb);

/// <summary>
/// The Devices pane's content (device classes plan, stage 14, ADR 0197): the serial ports and the USB
/// topology of <c>jgraph.usb.tree</c>, each node with the lines of code that open it. Free of any UI
/// type, so the app binds to it and the tests read it.
/// </summary>
/// <remarks>
/// An action never touches a device. It is text: a host writes a <see cref="DeviceActionKind.Code"/>
/// line at its prompt and the user runs it, so the pane cannot open, claim or write to anything by
/// being clicked.
/// </remarks>
public static class DevicePaneModel
{
    /// <summary>The baud rate an "open as serialport" line is written with; the user edits it at the prompt.</summary>
    public const int DefaultBaudRate = 9600;

    /// <summary>
    /// Reads the machine: the registry's serial ports and the USB enumeration behind
    /// <c>jgraph.usb.devices</c>. Slow enough (tens to hundreds of milliseconds) that a host calls it
    /// off its UI thread.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static DeviceSnapshot Read()
    {
        IReadOnlyList<UsbDeviceInfo> usb = UsbEnumerator.Devices();
        var hid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, List<string>> pair in UsbCrossReference.HidPaths())
        {
            hid[pair.Key] = pair.Value.Count;
        }

        var winUsb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (UsbDeviceInfo device in usb)
        {
            if (!device.IsHub && WinUsbDevice.PathsOf(device.InstanceId).Count > 0)
            {
                winUsb.Add(device.InstanceId);
            }
        }

        return new DeviceSnapshot(
            SerialPortList.All(), UsbCrossReference.ComPortDescriptions(), usb, UsbCrossReference.ComPorts(), hid, winUsb);
    }

    /// <summary>The pane's two groups, "Serial ports" and "USB", for a snapshot.</summary>
    public static IReadOnlyList<DeviceNode> Build(DeviceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [SerialGroup(snapshot), UsbGroup(snapshot)];
    }

    /// <summary>The line that opens a port: <c>s = serialport("COM3", 9600)</c>.</summary>
    public static string SerialportLine(string port, int baudRate = DefaultBaudRate) =>
        $"s = serialport({Quoted(port)}, {baudRate.ToString(CultureInfo.InvariantCulture)})";

    // --- serial ports --------------------------------------------------------------------------------

    private static DeviceNode SerialGroup(DeviceSnapshot snapshot)
    {
        // The USB device each port belongs to, so a port says whose it is.
        var owner = new Dictionary<string, UsbDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (UsbDeviceInfo device in snapshot.Usb)
        {
            if (snapshot.ComPorts.TryGetValue(device.InstanceId, out List<string>? ports))
            {
                foreach (string port in ports)
                {
                    owner[port] = device;
                }
            }
        }

        var nodes = new List<DeviceNode>();
        foreach (string port in snapshot.SerialPorts)
        {
            string described = snapshot.PortDescriptions.TryGetValue(port, out string? text) ? text : "";
            string detail = owner.TryGetValue(port, out UsbDeviceInfo? device)
                ? $"{Ids(device)} {Name(device)}".TrimEnd()
                : described;
            string tip = owner.TryGetValue(port, out device)
                ? Lines(described, "USB device " + device.InstanceId)
                : described;
            nodes.Add(new DeviceNode(port, detail, DeviceNodeKind.SerialPort, tip, PortActions(port, "Open"), []));
        }

        string count = nodes.Count == 0 ? "none" : nodes.Count.ToString(CultureInfo.InvariantCulture);
        return new DeviceNode("Serial ports", count, DeviceNodeKind.Group, "The ports serialportlist answers.", [], nodes);
    }

    /// <summary>The actions of one port; <paramref name="verb"/> names the port when the node is not the port itself.</summary>
    private static List<DeviceAction> PortActions(string port, string verb) =>
    [
        new DeviceAction($"{verb} as serialport", SerialportLine(port)),
        new DeviceAction($"{verb} in Serial Explorer", port, DeviceActionKind.SerialExplorer),
    ];

    // --- USB -----------------------------------------------------------------------------------------

    private static DeviceNode UsbGroup(DeviceSnapshot snapshot)
    {
        var byParent = snapshot.Usb.GroupBy(static d => d.ParentInstanceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(snapshot.Usb.Select(static d => d.InstanceId), StringComparer.OrdinalIgnoreCase);

        // Two devices that answer to the same IDs need their serial numbers to be told apart.
        var twins = snapshot.Usb.Where(static d => !d.IsHub)
            .GroupBy(static d => (d.VendorId, d.ProductId))
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToHashSet();

        DeviceNode Node(UsbDeviceInfo device, int depth)
        {
            List<UsbDeviceInfo> children = byParent.TryGetValue(device.InstanceId, out List<UsbDeviceInfo>? list) ? list : [];
            DeviceNode[] kids = children.OrderBy(static c => c.Ports.Count > 0 ? c.Ports[^1] : 0)
                .ThenBy(static c => c.InstanceId, StringComparer.OrdinalIgnoreCase)
                .Select(c => Node(c, depth + 1)).ToArray();
            string where = depth == 0
                ? "Bus " + device.Bus.ToString(CultureInfo.InvariantCulture)
                : "Port " + (device.Ports.Count > 0 ? device.Ports[^1] : 0).ToString(CultureInfo.InvariantCulture);
            string title = device.IsHub && device.VendorId == 0
                ? $"{where}: {Name(device)}"
                : $"{where}: {Ids(device)} {Name(device)}".TrimEnd();
            // The detail leads the tooltip too: a narrow pane cuts the line off after the name.
            string detail = Detail(device, snapshot);
            return new DeviceNode(
                title, detail, device.IsHub ? DeviceNodeKind.Hub : DeviceNodeKind.Device, Lines(detail, ToolTip(device)),
                UsbActions(device, snapshot, twins.Contains((device.VendorId, device.ProductId))), kids);
        }

        DeviceNode[] roots = snapshot.Usb.Where(d => !ids.Contains(d.ParentInstanceId))
            .OrderBy(static r => r.Bus).ThenBy(static r => r.InstanceId, StringComparer.OrdinalIgnoreCase)
            .Select(r => Node(r, 0)).ToArray();
        int devices = snapshot.Usb.Count(static d => !d.IsHub);
        string count = devices == 0 ? "none" : devices.ToString(CultureInfo.InvariantCulture);
        return new DeviceNode("USB", count, DeviceNodeKind.Group, "The devices jgraph.usb.devices lists, under their hubs.", [], roots);
    }

    private static List<DeviceAction> UsbActions(UsbDeviceInfo device, DeviceSnapshot snapshot, bool hasTwin)
    {
        var actions = new List<DeviceAction>();
        if (snapshot.ComPorts.TryGetValue(device.InstanceId, out List<string>? ports))
        {
            foreach (string port in ports)
            {
                actions.AddRange(PortActions(port, "Open " + port));
            }
        }

        if (snapshot.HidCollections.TryGetValue(device.InstanceId, out int collections) && collections > 0)
        {
            string filter = $"VendorID={Quoted(Hex(device.VendorId))}, ProductID={Quoted(Hex(device.ProductId))}"
                + (hasTwin && device.SerialNumber.Length > 0 ? $", SerialNumber={Quoted(device.SerialNumber)}" : "");

            // One collection opens; several are listed, since jgraph.usb.hid wants the one named.
            actions.Add(collections == 1
                ? new DeviceAction("Open as HID", $"h = jgraph.usb.hid({filter})")
                : new DeviceAction($"List its {collections} HID collections", $"T = jgraph.usb.hidlist({filter})"));
        }

        if (snapshot.WinUsb.Contains(device.InstanceId))
        {
            actions.Add(new DeviceAction("Open as USB device (WinUSB)", $"d = jgraph.usb.device({Quoted(device.InstanceId)})"));
        }

        if (device.DeviceDescriptor.Length > 0)
        {
            actions.Add(new DeviceAction("Show descriptors", $"desc = jgraph.usb.descriptors({Quoted(device.InstanceId)})"));
        }

        actions.Add(new DeviceAction("Copy instance ID", device.InstanceId, DeviceActionKind.Copy));
        return actions;
    }

    private static string Detail(UsbDeviceInfo device, DeviceSnapshot snapshot)
    {
        var parts = new List<string>();
        string classes = Jgs.JgsBuiltins.UsbClassColumn(device);
        if (classes.Length > 0)
        {
            parts.Add(classes);
        }

        if (device.Driver.Length > 0)
        {
            parts.Add(device.Driver);
        }

        if (device.Speed != UsbSpeed.Unknown)
        {
            parts.Add(Jgs.JgsBuiltins.UsbSpeedName(device.Speed));
        }

        if (snapshot.ComPorts.TryGetValue(device.InstanceId, out List<string>? ports) && ports.Count > 0)
        {
            parts.Add(string.Join(", ", ports));
        }

        return string.Join(" · ", parts);
    }

    private static string ToolTip(UsbDeviceInfo device) => Lines(
        device.Manufacturer.Length > 0 ? "Manufacturer: " + device.Manufacturer : "",
        device.SerialNumber.Length > 0 ? "Serial number: " + device.SerialNumber : "",
        device.Location.Length > 0 ? "Location: " + device.Location : "",
        device.InstanceId);

    private static string Name(UsbDeviceInfo device) => device.Product.Length > 0 ? device.Product : device.Description;

    private static string Ids(UsbDeviceInfo device) => $"{Hex(device.VendorId)}:{Hex(device.ProductId)}";

    private static string Hex(int id) => id.ToString("X4", CultureInfo.InvariantCulture);

    /// <summary>MATLAB string text: quoted, a quote inside doubled. A backslash is itself in a MATLAB string.</summary>
    private static string Quoted(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Lines(params string[] lines) => string.Join("\n", lines.Where(static l => l.Length > 0));
}
