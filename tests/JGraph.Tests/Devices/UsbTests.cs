using System.Runtime.Versioning;
using JGraph.Devices.Usb;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// The USB layer (device classes plan, stage D5, ADR 0188): the descriptor decoders on the laptop's own
/// devices' captured descriptors (<see cref="UsbCaptures"/>) and on built ones, the names and locations
/// Windows writes, and the enumeration and the script functions on whatever this machine has.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("JG facade")]
public class UsbTests
{
    private static byte[] Hex(string text) => Convert.FromHexString(text);

    [Fact]
    public void AWebcamsFunctionsAssociationsFramesAndDfuInterfaceDecode()
    {
        UsbConfiguration c = UsbDescriptors.Configuration(Hex(UsbCaptures.WebcamConfiguration));
        Assert.Equal(19, c.Interfaces.Count);
        Assert.Equal(3, c.Associations.Count);
        Assert.Equal((0, 2, "Video"), (c.Associations[0].FirstInterface, c.Associations[0].InterfaceCount, c.Associations[0].ClassName));
        Assert.Equal(500, c.MaxPowerMilliamps);
        Assert.False(c.RemoteWakeup);

        UsbClassDescriptor header = c.Interfaces[0].ClassSpecific[0];
        Assert.Equal("VC Header", header.Name);
        Assert.Equal("1.10", Field(header, "UVCVersion"));

        IReadOnlyList<UsbClassDescriptor> streaming = c.Interfaces[1].ClassSpecific;
        Assert.Equal("Frame MJPEG", streaming[2].Name);
        Assert.Equal((1280.0, 720.0, 30.0), ((double)Field(streaming[2], "Width"), (double)Field(streaming[2], "Height"), (double)Field(streaming[2], "FrameRate")));
        Assert.Equal("Format Uncompressed", streaming[9].Name);
        Assert.Equal("YUY2", Field(streaming[9], "FourCC"));

        UsbInterface dfu = c.Interfaces[^1];
        Assert.Equal("DFU", dfu.ClassName);
        UsbClassDescriptor functional = Assert.Single(dfu.ClassSpecific);
        Assert.Equal("DFU Functional", functional.Name);
        Assert.Equal(1.0, Field(functional, "WillDetach"));
        Assert.Equal(200.0, Field(functional, "DetachTimeout"));
        Assert.Equal(4096.0, Field(functional, "TransferSize"));
        Assert.Equal("1.10", Field(functional, "DFUVersion"));
    }

    [Fact]
    public void AKeyboardsHidDescriptorsDecode()
    {
        UsbConfiguration c = UsbDescriptors.Configuration(Hex(UsbCaptures.KeyboardConfiguration));
        Assert.Equal(4, c.Interfaces.Count);
        Assert.All(c.Interfaces, static f => Assert.Equal("HID", f.ClassName));
        Assert.Equal([192.0, 77.0, 83.0, 48.0], c.Interfaces.Select(static f => (double)Field(f.ClassSpecific[0], "ReportDescriptorLength")));
        Assert.Equal("1.10", Field(c.Interfaces[0].ClassSpecific[0], "HIDVersion"));
        UsbEndpoint input = c.Interfaces[0].Endpoints[0];
        Assert.Equal(("in", "interrupt"), (input.Direction, input.TransferType));
    }

    [Fact]
    public void ABluetoothRadiosAlternateSettingsAreKept()
    {
        byte[] device = Hex(UsbCaptures.BluetoothDevice);
        Assert.Equal("Bluetooth", UsbDescriptors.ClassName(device[4], device[5], device[6]));
        UsbConfiguration c = UsbDescriptors.Configuration(Hex(UsbCaptures.BluetoothConfiguration));
        Assert.Equal([0, 0, 1, 2, 3, 4, 5, 6], c.Interfaces.Select(static f => f.AlternateSetting));
        Assert.All(c.Interfaces[1].Endpoints, static e => Assert.Equal("isochronous", e.TransferType));
    }

    [Fact]
    public void StringIndicesListEachStringTheDescriptorsName()
    {
        Assert.Equal([3, 1, 2], UsbDescriptors.StringIndices(Hex(UsbCaptures.WebcamDevice), Hex(UsbCaptures.WebcamConfiguration)).Take(3));
        Assert.Equal([1, 2], UsbDescriptors.StringIndices(Hex(UsbCaptures.KeyboardDevice), Hex(UsbCaptures.KeyboardConfiguration)));
        Assert.Empty(UsbDescriptors.StringIndices(Hex(UsbCaptures.BluetoothDevice), Hex(UsbCaptures.BluetoothConfiguration)));
    }

    [Fact]
    public void BosPlatformCapabilitiesAreNamedByTheirUuid()
    {
        static byte[] Platform(string uuid) => [28, 0x10, 5, 0, .. new Guid(uuid).ToByteArray(), 0, 0, 3, 6, 0xB2, 0, 1, 0];
        byte[] extension = [7, 0x10, 2, 6, 0, 0, 0];
        byte[] ms = Platform(UsbDescriptors.MsOs20Uuid);
        byte[] web = Platform(UsbDescriptors.WebUsbUuid);
        int total = 5 + extension.Length + ms.Length + web.Length;
        byte[] bos = [5, 0x0F, (byte)total, 0, 3, .. extension, .. ms, .. web];
        IReadOnlyList<UsbCapability> capabilities = UsbDescriptors.Capabilities(bos);
        Assert.Equal(["USB 2.0 Extension", "MS OS 2.0", "WebUSB"], capabilities.Select(static c => c.Name));
        Assert.Equal(UsbDescriptors.MsOs20Uuid, capabilities[1].PlatformId);
    }

    [Fact]
    public void CdcAndDfuSeDescriptorsDecode()
    {
        // A CDC ACM function: IAD, communication interface with its functional descriptors, data interface.
        byte[] configuration =
        [
            9, 2, 75, 0, 2, 1, 0, 0x80, 50,
            8, 11, 0, 2, 2, 2, 0, 0,
            9, 4, 0, 0, 1, 2, 2, 0, 0,
            5, 0x24, 0, 0x20, 0x01,
            5, 0x24, 1, 0, 1,
            4, 0x24, 2, 2,
            5, 0x24, 6, 0, 1,
            7, 5, 0x81, 3, 8, 0, 16,
            9, 4, 1, 0, 2, 0x0A, 0, 0, 0,
            7, 5, 0x02, 2, 64, 0, 0,
            7, 5, 0x82, 2, 64, 0, 0,
        ];
        UsbConfiguration c = UsbDescriptors.Configuration(configuration);
        Assert.Equal("CDC ACM", c.Associations[0].ClassName);
        Assert.Equal(["Header", "Call Management", "Abstract Control Management", "Union"], c.Interfaces[0].ClassSpecific.Select(static d => d.Name));
        Assert.Equal("1.20", Field(c.Interfaces[0].ClassSpecific[0], "CDCVersion"));
        Assert.Equal("1", Field(c.Interfaces[0].ClassSpecific[3], "SubordinateInterfaces"));
        Assert.Equal("CDC Data", c.Interfaces[1].ClassName);

        // ST's DfuSe bootloader says bcdDFUVersion 0x011A.
        byte[] dfuse = [9, 2, 27, 0, 1, 1, 0, 0x80, 50, 9, 4, 0, 0, 0, 0xFE, 1, 2, 0, 9, 0x21, 0x0B, 0xFF, 0, 0, 8, 0x1A, 0x01];
        UsbClassDescriptor functional = UsbDescriptors.Configuration(dfuse).Interfaces[0].ClassSpecific[0];
        Assert.Equal("1.1a", Field(functional, "DFUVersion"));
        Assert.Equal(2048.0, Field(functional, "TransferSize"));
    }

    [Theory]
    [InlineData(new[] { "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)#USB(2)" }, new[] { 3, 2 })]
    [InlineData(new[] { "ACPI(_SB_)#ACPI(PC00)#ACPI(XHCI)#ACPI(RHUB)#ACPI(HS08)", "PCIROOT(0)#PCI(0D00)#USBROOT(0)#USB(8)" }, new[] { 8 })]
    [InlineData(new[] { "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(9)#USBMI(0)" }, new[] { 9 })]
    [InlineData(new string[0], new int[0])]
    public void LocationPathsGiveThePortChain(string[] paths, int[] ports) => Assert.Equal(ports, UsbEnumerator.PortChain(paths));

    [Theory]
    [InlineData("USB\\VID_2E8A&PID_000A&REV_0100", 0x2E8A, 0x000A, 0x0100)]
    [InlineData("USB\\ROOT_HUB30&VID8086&PID7AE0&REV0000", 0x8086, 0x7AE0, 0)]
    [InlineData("USB\\COMPOSITE", 0, 0, 0)]
    public void HardwareIdsGiveVidPidAndRev(string id, int vid, int pid, int rev) =>
        Assert.Equal((vid, pid, rev), UsbEnumerator.IdsOf([id]));

    [Fact]
    public void AnInterfacePathNamesItsInstance() =>
        Assert.Equal("USB\\VID_2E8A&PID_000A\\E6614C31",
            UsbWatcher.InstanceIdOf("\\\\?\\USB#VID_2E8A&PID_000A#E6614C31#{a5dcbf10-6530-11d2-901f-00c04fb951ed}"));

    [Theory]
    [InlineData(0x02, 0x02, 0x00, "CDC ACM")]
    [InlineData(0xE0, 0x01, 0x03, "RNDIS")]
    [InlineData(0xEF, 0x02, 0x01, "Miscellaneous")]
    [InlineData(0xFE, 0x03, 0x01, "USBTMC")]
    [InlineData(0x42, 0, 0, "Class 0x42")]
    public void ClassCodesHaveNames(int cls, int sub, int proto, string name) => Assert.Equal(name, UsbDescriptors.ClassName(cls, sub, proto));

    [Fact]
    public void TheEnumerationReadsThisMachineWithoutFailing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        IReadOnlyList<UsbDeviceInfo> devices = UsbEnumerator.Devices();
        Assert.All(devices, static d => Assert.False(string.IsNullOrEmpty(d.InstanceId)));
        Assert.All(UsbEnumerator.HubPorts(), static p => Assert.True(p.Port.Port >= 1));
        using var watcher = new UsbWatcher();
    }

    [Fact]
    public void TheScriptFunctionsAnswerTheirShapesAndRefusals()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal("0 12 VendorID,InstanceID|JGraph:usb:NameValue|JGraph:usb:BadId|JGraph:usb:NoDevice|JGraph:usb:WatchCallback|jgraph.usb.Watcher 1 0|", Run("""
            T = jgraph.usb.devices("VendorID", "FFFF", "ProductID", 65535);
            v = T.Properties.VariableNames;
            fprintf('%d %d %s,%s|', height(T), width(T), v{1}, v{end});
            try, jgraph.usb.devices("Bogus", 1); catch e, fprintf('%s|', e.identifier); end
            try, jgraph.usb.devices("VendorID", "XYZWV"); catch e, fprintf('%s|', e.identifier); end
            try, jgraph.usb.descriptors("VendorID", "FFFF"); catch e, fprintf('%s|', e.identifier); end
            try, jgraph.usb.watch(5); catch e, fprintf('%s|', e.identifier); end
            w = jgraph.usb.watch(@(w, e) disp(e.Type), "VendorID", "FFFF");
            fprintf('%s %d ', class(w), isvalid(w));
            delete(w);
            fprintf('%d|', isvalid(w));
            """));
    }

    private static object Field(UsbClassDescriptor descriptor, string name) =>
        descriptor.Fields.First(f => f.Key == name).Value;

    private static string Run(string code)
    {
        var output = new RecordingScriptOutput();
        ScriptRunResult result = JgsRunner.Run(
            code, new ScriptContext(output, (_, _) => { }, null), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }
}
