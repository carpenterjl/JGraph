using System.Reflection;

namespace JGraph.Devices.Bluetooth;

/// <summary>What the system's Bluetooth radio is doing.</summary>
public enum BluetoothRadioState
{
    /// <summary>No Bluetooth adapter.</summary>
    Missing,

    /// <summary>An adapter whose radio is off.</summary>
    Off,

    /// <summary>A radio that is on.</summary>
    On,
}

/// <summary>How a classic device can be reached, as <c>bluetoothlist</c>'s Status column says it.</summary>
public enum ClassicDeviceStatus
{
    /// <summary>Paired, with a serial port (SPP) channel: <c>Ready to connect</c>.</summary>
    Ready,

    /// <summary>Paired and connected now.</summary>
    Connected,

    /// <summary>Seen but not paired.</summary>
    Unpaired,

    /// <summary>Paired, but offers no serial port channel.</summary>
    Unsupported,

    /// <summary>The device did not answer the service query.</summary>
    Unknown,
}

/// <summary>A classic Bluetooth device as discovery found it.</summary>
/// <param name="Name">Its name; empty when it has none.</param>
/// <param name="Address">Its 48-bit address.</param>
/// <param name="Channel">Its serial port (RFCOMM) channel, or null when not known.</param>
/// <param name="Status">Whether it can be connected.</param>
public sealed record ClassicDeviceInfo(string Name, ulong Address, int? Channel, ClassicDeviceStatus Status);

/// <summary>One advertisement a Bluetooth Low Energy scan heard, with the fields R2025b reports.</summary>
public sealed record BleAdvertisementInfo(
    string Name,
    ulong Address,
    int Rssi,
    string Type,
    int? Appearance,
    string? ShortenedLocalName,
    string? CompleteLocalName,
    int? TxPowerLevel,
    (double Min, double Max)? SlaveConnectionIntervalRange,
    byte[]? ManufacturerSpecificData,
    byte[]? ServiceData,
    IReadOnlyList<string> CompleteServiceUuids,
    IReadOnlyList<string> IncompleteServiceUuids,
    IReadOnlyList<string> ServiceSolicitationUuids);

/// <summary>A GATT attribute's UUID as the peripheral reported it (canonical, upper case) and its properties.</summary>
/// <param name="Uuid">The full 128-bit UUID, <c>0000180D-0000-1000-8000-00805F9B34FB</c>.</param>
/// <param name="Attributes">R2025b's attribute words: Read, Write, WriteWithoutResponse, Notify, Indicate, …</param>
public sealed record GattAttributeInfo(string Uuid, IReadOnlyList<string> Attributes);

/// <summary>How a characteristic's notifications are set up, the Client Characteristic Configuration's value.</summary>
public enum GattSubscription
{
    Unknown,
    None,
    Notify,
    Indicate,
}

/// <summary>A GATT failure, with R2025b's identifier word for it (<c>gattCommunicationUnreachable</c>, …).</summary>
public sealed class GattException : Exception
{
    public GattException(string kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    /// <summary>
    /// <c>Unreachable</c>, <c>AccessDenied</c>, <c>ProtocolError</c>, <c>Disconnected</c> or
    /// <c>ProfileChanged</c>: what the scripting layer maps to R2025b's identifiers.
    /// </summary>
    public string Kind { get; }
}

/// <summary>A connected Bluetooth Low Energy peripheral: its services, characteristics and descriptors, by index.</summary>
public interface IBlePeripheral : IDisposable
{
    string Name { get; }

    ulong Address { get; }

    /// <summary>Whether the link is up.</summary>
    bool Connected { get; }

    /// <summary>The primary services, in the order the peripheral lists them.</summary>
    IReadOnlyList<GattAttributeInfo> Services();

    /// <summary>A service's characteristics.</summary>
    IReadOnlyList<GattAttributeInfo> Characteristics(int service);

    /// <summary>A characteristic's descriptors.</summary>
    IReadOnlyList<GattAttributeInfo> Descriptors(int service, int characteristic);

    /// <summary>Reads a characteristic's value from the peripheral (not from a cache).</summary>
    byte[] Read(int service, int characteristic);

    /// <summary>Writes a characteristic, with or without a response.</summary>
    void Write(int service, int characteristic, byte[] data, bool withResponse);

    /// <summary>Reads a descriptor.</summary>
    byte[] ReadDescriptor(int service, int characteristic, int descriptor);

    /// <summary>Writes a descriptor.</summary>
    void WriteDescriptor(int service, int characteristic, int descriptor, byte[] data);

    /// <summary>The characteristic's Client Characteristic Configuration as the peripheral holds it.</summary>
    GattSubscription SubscriptionOf(int service, int characteristic);

    /// <summary>
    /// Turns notifications or indications on (or off with <see cref="GattSubscription.None"/>); each
    /// value that arrives is handed to <paramref name="onValue"/> on a background thread, with the time
    /// it came.
    /// </summary>
    void Subscribe(int service, int characteristic, GattSubscription kind, Action<byte[], DateTime>? onValue);
}

/// <summary>
/// The system's Bluetooth: the radio, classic discovery and RFCOMM channels, and Low Energy scanning
/// and connections (device classes plan, stage D3). The Windows implementation lives in
/// JGraph.Devices.Bluetooth, loaded at run time by <see cref="BluetoothBackends"/>; the simulated one
/// serves the tests.
/// </summary>
public interface IBluetoothBackend
{
    BluetoothRadioState Radio { get; }

    /// <summary>Classic devices: the paired ones, and those an inquiry of <paramref name="timeout"/> finds (none asked for: the paired ones and what is cached).</summary>
    IReadOnlyList<ClassicDeviceInfo> DiscoverClassic(TimeSpan? timeout, CancellationToken cancel);

    /// <summary>The paired classic devices, without an inquiry.</summary>
    IReadOnlyList<ClassicDeviceInfo> PairedClassic();

    /// <summary>Opens an RFCOMM channel to a paired device.</summary>
    IDeviceTransport ConnectRfcomm(ulong address, int channel, CancellationToken cancel);

    /// <summary>Scans for advertisements for <paramref name="timeout"/>, only from peripherals that advertise one of <paramref name="services"/> when any are given.</summary>
    IReadOnlyList<BleAdvertisementInfo> Scan(TimeSpan timeout, IReadOnlyList<string> services, CancellationToken cancel);

    /// <summary>Connects to a peripheral and discovers its services.</summary>
    IBlePeripheral ConnectBle(ulong address, CancellationToken cancel);
}

/// <summary>Finds the backend: a simulated one a test door installed, or the Windows one beside this assembly.</summary>
public static class BluetoothBackends
{
    private static readonly object Gate = new();
    private static IBluetoothBackend? s_system;
    private static string? s_failure;

    /// <summary>A backend installed for the calling session's tests; the system's when null.</summary>
    [ThreadStatic]
    private static IBluetoothBackend? t_simulated;

    /// <summary>Installs a simulated backend for scripts run on this thread (the test door); null removes it.</summary>
    public static void Simulate(IBluetoothBackend? backend) => t_simulated = backend;

    /// <summary>The simulated backend installed on this thread, if any.</summary>
    public static IBluetoothBackend? Simulated => t_simulated;

    /// <summary>The backend for this thread, or null with the reason the system's could not be loaded.</summary>
    public static IBluetoothBackend? Current(out string? failure)
    {
        failure = null;
        if (t_simulated is { } simulated)
        {
            return simulated;
        }

        lock (Gate)
        {
            if (s_system is null && s_failure is null)
            {
                Load();
            }

            failure = s_failure;
            return s_system;
        }
    }

    private static void Load()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            s_failure = "Bluetooth needs Windows 10 version 2004 or later.";
            return;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "JGraph.Devices.Bluetooth.dll");
        try
        {
            Assembly assembly = File.Exists(path) ? Assembly.LoadFrom(path) : Assembly.Load("JGraph.Devices.Bluetooth");
            Type type = assembly.GetType("JGraph.Devices.Bluetooth.WinRtBluetoothBackend", throwOnError: true)!;
            s_system = (IBluetoothBackend)Activator.CreateInstance(type)!;
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or TypeLoadException or MissingMethodException or TargetInvocationException or InvalidCastException)
        {
            s_failure = $"The Bluetooth backend could not be loaded: {e.Message}";
        }
    }
}

/// <summary>Bluetooth addresses as R2025b writes them: twelve upper-case hex digits, no separators.</summary>
public static class BluetoothAddress
{
    public static string Format(ulong address) => address.ToString("X12", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Parses twelve hex digits, with or without <c>:</c> or <c>-</c> between the pairs.</summary>
    public static bool TryParse(string text, out ulong address)
    {
        string digits = text.Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        address = 0;
        return digits.Length == 12 && ulong.TryParse(digits, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out address);
    }
}

/// <summary>The radio is off or missing: <see cref="State"/> says which.</summary>
public sealed class BluetoothRadioException : Exception
{
    public BluetoothRadioException(BluetoothRadioState state)
        : base(state == BluetoothRadioState.Missing ? "No Bluetooth adapter." : "The Bluetooth radio is off.")
    {
        State = state;
    }

    public BluetoothRadioState State { get; }
}
