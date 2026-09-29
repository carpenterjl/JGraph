namespace JGraph.Devices;

/// <summary>
/// A byte stream to a device: a serial port, a socket, an RFCOMM channel, a USBTMC pipe, or the
/// simulator. What the shared transport client (JGraph.Scripting's <c>TransportClient</c>) reads and
/// writes through; one reader thread per open transport fills <see cref="Input"/>.
/// </summary>
public interface IDeviceTransport : IDisposable
{
    /// <summary>What the transport is connected to: <c>COM3</c>, <c>127.0.0.1:5025</c>.</summary>
    string Name { get; }

    /// <summary>Whether the transport is open and has not failed.</summary>
    bool Connected { get; }

    /// <summary>Received bytes waiting to be read.</summary>
    InputBuffer Input { get; }

    /// <summary>Bytes written since the transport opened.</summary>
    long BytesWritten { get; }

    /// <summary>Writes every byte or throws; a write that has not finished in <paramref name="timeout"/> is cancelled and throws <see cref="TimeoutException"/>.</summary>
    void Write(ReadOnlySpan<byte> data, TimeSpan timeout, CancellationToken cancel);

    /// <summary>Discards received bytes, in the driver and in <see cref="Input"/>.</summary>
    void FlushInput();

    /// <summary>Discards bytes written and not yet sent.</summary>
    void FlushOutput();

    /// <summary>
    /// Raised once, on a background thread, when the connection is lost (a USB adapter unplugged, the
    /// peer closed a socket). <see cref="Input"/> is faulted with the same exception.
    /// </summary>
    event Action<DeviceConnectionLostException>? ConnectionLost;
}

/// <summary>The modem-status lines a serial port reads.</summary>
public readonly record struct SerialPins(bool ClearToSend, bool DataSetReady, bool CarrierDetect, bool RingIndicator);

/// <summary>Parity as the DCB spells it.</summary>
public enum SerialParity
{
    None,
    Odd,
    Even,
    Mark,
    Space,
}

/// <summary>Stop bits as the DCB spells them.</summary>
public enum SerialStopBits
{
    One,
    OnePointFive,
    Two,
}

/// <summary>Flow control: none, RTS/CTS, or XON/XOFF.</summary>
public enum SerialFlowControl
{
    None,
    Hardware,
    Software,
}

/// <summary>A serial port's line settings.</summary>
public sealed record SerialSettings
{
    public int BaudRate { get; init; } = 9600;

    public int DataBits { get; init; } = 8;

    public SerialParity Parity { get; init; } = SerialParity.None;

    public SerialStopBits StopBits { get; init; } = SerialStopBits.One;

    public SerialFlowControl FlowControl { get; init; } = SerialFlowControl.None;
}

/// <summary>A serial port: a byte stream with line settings, the two output pins, the four input pins and break.</summary>
public interface ISerialTransport : IDeviceTransport
{
    /// <summary>The settings in force.</summary>
    SerialSettings Settings { get; }

    /// <summary>Applies settings; throws <see cref="DeviceSettingsException"/> when the driver refuses them (the old ones stay).</summary>
    void Apply(SerialSettings settings);

    /// <summary>Raises or lowers RTS.</summary>
    void SetRts(bool on);

    /// <summary>Raises or lowers DTR.</summary>
    void SetDtr(bool on);

    /// <summary>Whether RTS is raised.</summary>
    bool Rts { get; }

    /// <summary>Whether DTR is raised.</summary>
    bool Dtr { get; }

    /// <summary>Reads CTS, DSR, DCD and RI.</summary>
    SerialPins GetPins();

    /// <summary>Holds the line in break for <paramref name="milliseconds"/>, blocking the caller that long.</summary>
    void SendBreak(int milliseconds, CancellationToken cancel);

    /// <summary>Raised on a background thread when the far end sends a break; its argument counts every break so far.</summary>
    event Action<int>? BreakReceived;
}

/// <summary>A transport could not be opened: the port does not exist, is in use, or refused its settings.</summary>
public sealed class DeviceOpenException(string message, int win32Error = 0) : Exception(message)
{
    /// <summary>The Win32 error the open failed with, 0 when none applies.</summary>
    public int Win32Error { get; } = win32Error;
}

/// <summary>The driver refused a setting.</summary>
public sealed class DeviceSettingsException(string message, int win32Error = 0) : Exception(message)
{
    public int Win32Error { get; } = win32Error;
}

/// <summary>An open transport lost its device.</summary>
public sealed class DeviceConnectionLostException(string message, int win32Error = 0) : Exception(message)
{
    public int Win32Error { get; } = win32Error;
}

/// <summary>An I/O call failed on a transport that is still open.</summary>
public sealed class DeviceIOException(string message, int win32Error = 0) : Exception(message)
{
    public int Win32Error { get; } = win32Error;
}
