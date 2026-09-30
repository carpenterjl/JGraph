using System.Runtime.Versioning;
using JGraph.Devices.WinUsb;

namespace JGraph.Devices.Dfu;

/// <summary>A DFU interface's control pipe through WinUSB.</summary>
[SupportedOSPlatform("windows")]
public sealed class WinUsbDfuTransport(WinUsbDevice device) : IDfuTransport
{
    public byte[] Control(byte requestType, byte request, ushort value, ushort index, ReadOnlySpan<byte> data, int readLength, TimeSpan timeout) =>
        device.Control(requestType, request, value, index, data, readLength, timeout);

    public void SetAlternate(int interfaceNumber, int setting) => device.SetAlternateSetting(interfaceNumber, setting);

    public void Dispose() => device.Dispose();
}
