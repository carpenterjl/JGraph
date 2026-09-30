using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using JGraph.Devices.Serial;
using static JGraph.Devices.Usb.UsbNative;

namespace JGraph.Devices.Usb;

/// <summary>
/// From a USB device to what Windows made of it: its COM ports, its HID collections, its disks. Each
/// is found by walking a function's devnode up through its parents to the USB device it belongs to
/// (a composite device's interfaces hang under it).
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class UsbCrossReference
{
    /// <summary>USB device instance ID → the COM ports under it, in natural order.</summary>
    public static Dictionary<string, List<string>> ComPorts()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string _, uint devInst) in UsbEnumerator.DeviceInstances(GUID_DEVINTERFACE_COMPORT))
        {
            string friendly = UsbEnumerator.RegistryString(devInst, SPDRP_FRIENDLYNAME);
            Match m = ComName().Match(friendly);
            if (!m.Success || UsbAncestor(devInst) is not { } usb)
            {
                continue;
            }

            if (!result.TryGetValue(usb, out List<string>? names))
            {
                result[usb] = names = new List<string>();
            }

            names.Add(m.Groups[1].Value);
            names.Sort(NaturalOrder.Instance);
        }

        return result;
    }

    /// <summary>USB device instance ID → the paths of the HID collections under it.</summary>
    public static Dictionary<string, List<string>> HidPaths()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string instance, string path) in UsbEnumerator.InterfacePaths(GUID_DEVINTERFACE_HID))
        {
            uint devInst = 0;
            if (CM_Locate_DevNode(&devInst, instance, 0) != CR_SUCCESS || UsbAncestor(devInst) is not { } usb)
            {
                continue;
            }

            if (!result.TryGetValue(usb, out List<string>? paths))
            {
                result[usb] = paths = new List<string>();
            }

            paths.Add(path);
        }

        return result;
    }

    /// <summary>The nearest ancestor (or the devnode itself) that is a USB device, not one of its interfaces.</summary>
    public static string? UsbAncestor(uint devInst)
    {
        uint current = devInst;
        for (int depth = 0; depth < 16; depth++)
        {
            string id = UsbEnumerator.DeviceId(current);
            if (id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) && !id.Contains("&MI_", StringComparison.OrdinalIgnoreCase)
                && !id.StartsWith("USB\\ROOT_HUB", StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }

            uint parent = 0;
            if (CM_Get_Parent(&parent, current, 0) != CR_SUCCESS)
            {
                return null;
            }

            current = parent;
        }

        return null;
    }

    [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ComName();
}
