using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace JGraph.Devices.Usb;

/// <summary>A network adapter, and the USB device it is when it is one.</summary>
/// <param name="Name">The connection's name ("Ethernet 3").</param>
/// <param name="Description">The adapter's description.</param>
/// <param name="MacAddress">Its hardware address, "AA:BB:CC:DD:EE:FF"; empty when it has none.</param>
/// <param name="IPv4">Its IPv4 addresses.</param>
/// <param name="IPv6">Its IPv6 addresses.</param>
/// <param name="Status">"up", "down", or another operational status in lower case.</param>
/// <param name="Speed">The link speed in bits a second; 0 when the adapter does not say.</param>
/// <param name="InstanceId">The adapter devnode's instance ID.</param>
/// <param name="UsbInstanceId">The USB device above it; empty for an adapter that is not a USB device.</param>
public sealed record NetworkAdapterInfo(string Name, string Description, string MacAddress, IReadOnlyList<string> IPv4, IReadOnlyList<string> IPv6,
    string Status, long Speed, string InstanceId, string UsbInstanceId);

/// <summary>
/// From a USB device to its network adapter (device classes plan, stage D12): the adapter devnodes
/// that expose the network interface class, each one's NetCfgInstanceId from its driver key, and the
/// matching <see cref="NetworkInterface"/>'s addresses and link. A CDC ECM, NCM, EEM or RNDIS device,
/// or a USB Ethernet or Wi-Fi adapter, is found by walking the adapter's devnode up to the USB device.
/// Talking to the device is then tcpclient's and udpport's.
/// </summary>
[SupportedOSPlatform("windows")]
public static class UsbNetworkAdapters
{
    private static readonly Guid GUID_DEVINTERFACE_NET = new("CAC88484-7515-4C03-82E6-71A87ABAC361");

    /// <summary>
    /// The adapters that are USB devices: those the USB bus itself enumerated, as a device or as one
    /// of its interfaces. An adapter another bus made under a USB device is not one (the Bluetooth
    /// personal-area network under a USB Bluetooth radio).
    /// </summary>
    public static IReadOnlyList<NetworkAdapterInfo> List() =>
        All().Where(static a => a.UsbInstanceId.Length > 0 && a.InstanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Every present adapter with a network interface, USB or not, in the order of their names.</summary>
    internal static IReadOnlyList<NetworkAdapterInfo> All()
    {
        var interfaces = new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            interfaces.TryAdd(nic.Id, nic);
        }

        var result = new List<NetworkAdapterInfo>();
        foreach ((string instance, uint devInst) in UsbEnumerator.DeviceInstances(GUID_DEVINTERFACE_NET))
        {
            string driverKey = UsbEnumerator.RegistryString(devInst, UsbNative.SPDRP_DRIVER);
            string? netId = null;
            try
            {
                using RegistryKey? key = driverKey.Length == 0 ? null : Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Class\{driverKey}");
                netId = key?.GetValue("NetCfgInstanceId") as string;
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }

            if (netId is null || !interfaces.TryGetValue(netId, out NetworkInterface? nic))
            {
                continue;
            }

            result.Add(Describe(nic, instance, UsbCrossReference.UsbAncestor(devInst) ?? ""));
        }

        result.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static NetworkAdapterInfo Describe(NetworkInterface nic, string instance, string usb)
    {
        IReadOnlyList<string> Addresses(AddressFamily family)
        {
            try
            {
                return nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == family).Select(static a => a.Address.ToString()).ToList();
            }
            catch (NetworkInformationException)
            {
                return [];
            }
        }

        return new NetworkAdapterInfo(nic.Name, nic.Description, MacText(nic.GetPhysicalAddress().GetAddressBytes()),
            Addresses(AddressFamily.InterNetwork), Addresses(AddressFamily.InterNetworkV6),
            nic.OperationalStatus.ToString().ToLowerInvariant(), Math.Max(0, nic.Speed), instance, usb);
    }

    /// <summary>A hardware address as text: "AA:BB:CC:DD:EE:FF".</summary>
    internal static string MacText(byte[] address) => string.Join(":", address.Select(static b => b.ToString("X2")));

    /// <summary>
    /// What kind of USB networking a configuration descriptor declares: "ECM", "NCM", "EEM", "RNDIS", or
    /// "" when none of its interfaces is one. RNDIS is told by any of the three codes devices use for it.
    /// </summary>
    public static string Kind(byte[] configurationDescriptor)
    {
        if (configurationDescriptor.Length == 0)
        {
            return "";
        }

        foreach (UsbInterface f in UsbDescriptors.Configuration(configurationDescriptor).Interfaces)
        {
            switch (f.Class, f.Subclass, f.Protocol)
            {
                case (0x02, 0x06, _):
                    return "ECM";
                case (0x02, 0x0D, _):
                    return "NCM";
                case (0x02, 0x0C, _):
                    return "EEM";
                case (0xE0, 0x01, 0x03) or (0xEF, 0x04, 0x01) or (0x02, 0x02, 0xFF):
                    return "RNDIS";
            }
        }

        return "";
    }
}
