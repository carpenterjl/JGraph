using JGraph.Devices.Bluetooth;
using JGraph.Scripting;
using JGraph.Scripting.Jgs;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// Bluetooth without a radio (device classes plan, stage D3): the SDP parser, the GATT names and UUID
/// forms R2025b's blelib uses, and the scripting objects against the simulated backend
/// (<c>jgraph.internal.btsim</c>): an RFCOMM channel driven by the peer protocol, discovery tables, reads,
/// writes, notifications and their callback, descriptors, and a dropped link.
/// </summary>
public class BluetoothTests
{
    [Fact]
    public void SdpFindsTheRfcommChannel()
    {
        // ( (L2CAP) (RFCOMM, channel 5) ), as a serial port service record lists it.
        byte[] list = [0x35, 0x0C, 0x35, 0x03, 0x19, 0x01, 0x00, 0x35, 0x05, 0x19, 0x00, 0x03, 0x08, 0x05];
        Assert.Equal(5, SdpRecord.RfcommChannel(list));
        Assert.Null(SdpRecord.RfcommChannel([0x35, 0x05, 0x35, 0x03, 0x19, 0x01, 0x00]));
        Assert.Null(SdpRecord.RfcommChannel([0x35, 0x20]));
    }

    [Theory]
    [InlineData("180D", "0000180D-0000-1000-8000-00805F9B34FB")]
    [InlineData("180d", "0000180D-0000-1000-8000-00805F9B34FB")]
    [InlineData("0000FFE0", "0000FFE0-0000-1000-8000-00805F9B34FB")]
    [InlineData("0000180d-0000-1000-8000-00805f9b34fb", "0000180D-0000-1000-8000-00805F9B34FB")]
    [InlineData("12345678-1234-1234-1234-123456789abc", "12345678-1234-1234-1234-123456789ABC")]
    public void UuidsTakeR2025bsThreeForms(string written, string canonical) =>
        Assert.Equal(canonical, GattNames.TryCanonical(written));

    [Theory]
    [InlineData("1180D")]
    [InlineData("1234180D")]
    [InlineData("zz")]
    public void OtherTextIsNoUuid(string written) => Assert.Null(GattNames.TryCanonical(written));

    [Fact]
    public void NamesComeFromTheTablesAndCustomOtherwise()
    {
        Assert.Equal("180D", GattNames.Shortest("0000180D-0000-1000-8000-00805F9B34FB"));
        Assert.Equal("Heart Rate", GattNames.ServiceName("180D"));
        Assert.Equal("Custom", GattNames.ServiceName("FFE0"));
        Assert.Equal("Thingy Configuration Service", GattNames.ServiceName("EF680100-9B35-4933-9B10-52FFA9740042"));
        Assert.Equal("Heart Rate Measurement", GattNames.CharacteristicName("180D", "2A37"));
        Assert.Equal("Custom", GattNames.CharacteristicName("FFE0", "2A37"));
        Assert.Equal("Client Characteristic Configuration", GattNames.DescriptorName("2902"));
        Assert.Equal("Custom", GattNames.DescriptorName("ABCD"));
    }

    [Fact]
    public void AClassicChannelCarriesThePeersProtocol()
    {
        Assert.Equal("1 10 65 66 10 JGraphPeer 1", Run("""
            jgraph.internal.btsim('on');
            b = bluetooth("JGraphPeer");
            write(b, uint8([27 27 123]), "uint8"); write(b, uint8('echo on}'), "uint8");
            write(b, [65 66 10], "uint8");
            pause(0.2);
            x = read(b, 3);
            fprintf('%d %d %d %d %d ', b.Channel, b.Timeout, x);
            clear b
            b2 = bluetooth();
            fprintf('%s %d', b2.Name, strlength(b2.Address) == 12);
            """));
    }

    [Fact]
    public void TheRadioAndTheListSayWhatR2025bSays()
    {
        Assert.Equal("MATLAB:bluetooth:bluetoothlist:winBluetoothNotPoweredOn|MATLAB:ble:ble:bluetoothOperationRadioNotAvailable|3|Requires pairing", Run("""
            jgraph.internal.btsim('radio', 'off');
            try, bluetoothlist; catch e, fprintf('%s|', e.identifier); end
            try, blelist; catch e, fprintf('%s|', e.identifier); end
            jgraph.internal.btsim('radio', 'on');
            L = bluetoothlist("Timeout", 5);
            fprintf('%d|%s', height(L), L.Status(3));
            """));
    }

    [Fact]
    public void APeripheralReadsWritesAndNotifies()
    {
        Assert.Equal("3 4|100|1|2 3 |JGraph echo|1 2 3 |MATLAB:ble:ble:invalidDataRanged|MATLAB:ble:ble:unsupportedOperation", Run("""
            jgraph.internal.btsim('on');
            p = ble("JGraphPeer");
            fprintf('%d %d|', height(p.Services), height(p.Characteristics));
            c = characteristic(p, "Battery Service", "Battery Level");
            fprintf('%d|', read(c));
            global N
            N = 0;
            x = characteristic(p, "FFE0", "FFE1");
            x.DataAvailableFcn = @(src, evt) bump();
            write(x, [1 2 3]);
            write(x, 4);
            pause(0.3);
            fprintf('%d|', N >= 2);
            fprintf('%d ', height(x.Descriptors), numel(x.Attributes) - 1); fprintf('|');
            fprintf('%s|', char(read(descriptor(x, "Characteristic User"))));
            write(x, 258, "uint16");
            fprintf('%d ', read(x, "oldest")); fprintf('|');
            try, write(x, 256); catch e, fprintf('%s|', e.identifier); end
            try, write(c, 1); catch e, fprintf('%s', e.identifier); end
            function bump()
                global N
                N = N + 1;
            end
            """));
    }

    [Fact]
    public void HeartRateNotificationsArriveAndADroppedLinkRefuses()
    {
        Assert.Equal("1|60|MATLAB:ble:ble:failToExecuteDeviceDisconnected|0", Run("""
            jgraph.internal.btsim('on');
            p = ble("JGraphPeer");
            h = characteristic(p, "Heart Rate", "Heart Rate Measurement");
            subscribe(h);
            pause(0.35);
            v = read(h, "oldest");
            fprintf('%d|%d|', v(1) == 0, v(2));
            c = characteristic(p, "Battery Service", "Battery Level");
            jgraph.internal.btsim('drop');
            try, read(c); catch e, fprintf('%s|', e.identifier); end
            ws = warning('off', 'MATLAB:ble:ble:deviceDisconnected');
            fprintf('%d', p.Connected);
            """));
    }

    [Fact]
    public void AnAmbiguousOrUnknownNameIsRefused()
    {
        Assert.Equal("MATLAB:ambiguousStringChoice|MATLAB:ble:ble:unsupportedService|MATLAB:ble:ble:invalidUUIDNameCustom|MATLAB:ble:ble:connectionExists", Run("""
            jgraph.internal.btsim('on');
            p = ble("JGraphPeer");
            try, characteristic(p, "180D", "Heart Rate"); catch e, fprintf('%s|', e.identifier); end
            try, characteristic(p, "1800", "Device Name"); catch e, fprintf('%s|', e.identifier); end
            try, characteristic(p, "Custom", "FFE1"); catch e, fprintf('%s|', e.identifier); end
            try, ble("JGraphPeer"); catch e, fprintf('%s', e.identifier); end
            """));
    }

    private static string Run(string code)
    {
        var output = new RecordingScriptOutput();
        ScriptRunResult result = JgsRunner.Run(
            code, new ScriptContext(output, (_, _) => { }, null), default, sourceId: "", hook: null, JgsDialect.Matlab);
        Assert.True(result.Success, result.Message + output.ErrorText);
        return output.NormalText;
    }
}
