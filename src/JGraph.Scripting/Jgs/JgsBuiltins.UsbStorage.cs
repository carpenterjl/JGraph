using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using JGraph.Devices;
using JGraph.Devices.Storage;
using JGraph.Devices.Usb;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// USB mass storage under <c>jgraph.usb</c> (device classes plan, stage D9, ADR 0192), a JGraph
/// extension: <c>[disks, volumes] = jgraph.usb.storage(Name=Value...)</c> and
/// <c>jgraph.usb.eject(drive or row or instanceId)</c>. Nothing reads the media: the disks are
/// described by the storage stack's IOCTLs on handles opened with no access rights.
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>A jgraph.usb function that answers several outputs.</summary>
    private static JgsValue UsbMultiFunction(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue[]> body)
    {
        JgsValue[] Guarded(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:NotSupported", $"{name} runs on Windows only.");
            }

            return body(args, wanted, line, col);
        }

        return JgsValue.Function(new BuiltinFunction(name, (args, line, col) => Guarded(args, 1, line, col)[0])
        {
            KeepsStringArguments = true,
            TakesOutputCount = true,
            AutoCallsBare = true,
            MultiOutput = Guarded,
        });
    }

    private static readonly string[] UsbDiskColumns =
        ["DiskNumber", "VendorID", "ProductID", "Vendor", "Product", "Revision", "SerialNumber", "BusType", "Removable", "Size", "BytesPerSector", "Drives", "Location", "InstanceID"];

    private static readonly string[] UsbVolumeColumns = ["DiskNumber", "Drive", "Label", "FileSystem", "Capacity", "Free", "VolumeName"];

    /// <summary>
    /// <c>[disks, volumes] = jgraph.usb.storage(Name=Value...)</c>: one row per USB disk (its USB IDs and
    /// serial number, SCSI vendor, product and revision, bus, removable media, size, and drive letters),
    /// and one row per volume on them; filtered by jgraph.usb.devices' names, applied to the USB device.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue[] UsbStorageTables(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        Func<UsbDeviceInfo, bool> keep = UsbFilter("jgraph.usb.storage", args, 0, line, col);
        Dictionary<string, UsbDeviceInfo> usb = UsbEnumerator.Devices().ToDictionary(static d => d.InstanceId, StringComparer.OrdinalIgnoreCase);
        List<(UsbDisk Disk, UsbDeviceInfo Device)> disks = UsbStorage.Disks()
            .Where(d => usb.TryGetValue(d.UsbInstanceId, out UsbDeviceInfo? device) && keep(device))
            .Select(d => (d, usb[d.UsbInstanceId])).ToList();
        int n = disks.Count;
        JgsValue Text(Func<(UsbDisk Disk, UsbDeviceInfo Device), string> pick) => JgsValue.StringArray(disks.Select(r => JgsValue.Str(pick(r))).ToArray(), n, 1);
        JgsValue Number(Func<(UsbDisk Disk, UsbDeviceInfo Device), double> pick) => JgsMatrix.FromColumnMajor(disks.Select(pick).ToArray(), n, 1);
        JgsValue removable = n == 0 ? EmptyLogical(0, 1) : JgsValue.Array(disks.Select(static r => JgsValue.Bool(r.Disk.Identity.Removable)).ToArray());
        removable.Reshape(n, 1);
        JgsValue[] diskColumns =
        [
            Number(static r => r.Disk.DiskNumber),
            Text(static r => UsbHex(r.Device.VendorId)),
            Text(static r => UsbHex(r.Device.ProductId)),
            Text(static r => r.Disk.Identity.Vendor),
            Text(static r => r.Disk.Identity.Product),
            Text(static r => r.Disk.Identity.Revision),
            Text(static r => r.Device.SerialNumber.Length > 0 ? r.Device.SerialNumber : r.Disk.Identity.SerialNumber),
            Text(static r => r.Disk.Identity.BusType),
            removable,
            Number(static r => r.Disk.Size),
            Number(static r => r.Disk.BytesPerSector),
            Text(static r => string.Join(", ", r.Disk.Volumes.SelectMany(static v => v.Paths))),
            Text(static r => r.Device.Location),
            Text(static r => r.Device.InstanceId),
        ];
        JgsValue diskTable = JgsValue.Table(new JGraph.Data.Table(
            UsbDiskColumns.Select((name, c) => TableColumnFrom("jgraph.usb.storage", name, diskColumns[c], line, col)).ToList()));
        if (wanted < 2)
        {
            return [diskTable];
        }

        List<UsbVolume> volumes = disks.SelectMany(static r => r.Disk.Volumes).ToList();
        int m = volumes.Count;
        JgsValue VText(Func<UsbVolume, string> pick) => JgsValue.StringArray(volumes.Select(v => JgsValue.Str(pick(v))).ToArray(), m, 1);
        JgsValue VNumber(Func<UsbVolume, double> pick) => JgsMatrix.FromColumnMajor(volumes.Select(pick).ToArray(), m, 1);
        JgsValue[] volumeColumns =
        [
            VNumber(static v => v.DiskNumber),
            VText(static v => string.Join(", ", v.Paths)),
            VText(static v => v.Label),
            VText(static v => v.FileSystem),
            VNumber(static v => v.Capacity),
            VNumber(static v => v.Free),
            VText(static v => v.VolumeName),
        ];
        JgsValue volumeTable = JgsValue.Table(new JGraph.Data.Table(
            UsbVolumeColumns.Select((name, c) => TableColumnFrom("jgraph.usb.storage", name, volumeColumns[c], line, col)).ToList()));
        return [diskTable, volumeTable];
    }

    /// <summary>
    /// <c>jgraph.usb.eject(x)</c>: asks Windows to eject a USB device, as "Safely Remove Hardware" does. x is a
    /// drive ("E:", "E"), one row of jgraph.usb.storage or jgraph.usb.devices, or a device's InstanceID. A
    /// veto is refused with its reason and what holds the device, where Windows names it.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue UsbEject(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1 || !(args[0].Type == JgsType.Table || IsTextScalar(args[0])))
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:Nargin",
                "Valid syntax is jgraph.usb.eject(X): a drive (\"E:\"), a row of jgraph.usb.storage or jgraph.usb.devices, or a device's InstanceID.");
        }

        string instance;
        string text = args[0].Type == JgsType.Table ? "" : TextOf(args[0]).Trim();
        if (Regex.IsMatch(text, @"^[A-Za-z](:\\?)?$"))
        {
            string drive = char.ToUpperInvariant(text[0]) + ":\\";
            UsbDisk? disk = UsbStorage.Disks().FirstOrDefault(d => d.Volumes.Any(v => v.Paths.Any(p => p.Equals(drive, StringComparison.OrdinalIgnoreCase))));
            instance = disk?.UsbInstanceId
                ?? throw new JgsRuntimeException(line, col, "JGraph:usb:NotUsbDrive", $"{drive} is not a volume on a USB disk; jgraph.usb.storage lists those.");
        }
        else
        {
            instance = OneUsbDevice("jgraph.usb.eject", args, line, col).InstanceId;
        }

        try
        {
            UsbStorage.Eject(instance);
        }
        catch (DeviceOpenException e)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NoDevice", e.Message);
        }
        catch (DeviceIOException e)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:EjectVetoed", e.Message);
        }

        return JgsValue.Null;
    }
}
