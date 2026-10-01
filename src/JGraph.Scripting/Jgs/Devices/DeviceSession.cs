using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using JGraph.Devices.Simulation;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// A session's devices (device classes plan, stage 1): the objects alive in it (what
/// <c>serialportfind</c> searches, R2025b's <c>ObjectCacher</c>), the simulated ports the test door
/// <c>jgraph.internal.devicesim</c> registered, and the ports this session holds, so
/// <c>serialportlist("available")</c> can leave them out.
/// </summary>
internal sealed class DeviceSession
{
    private readonly List<DeviceObject> _live = new();
    private readonly Dictionary<string, SimulatedLine> _simulated = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _simulatedPeers = new(StringComparer.OrdinalIgnoreCase);

    public DeviceSession(JGraphScriptGlobals host)
    {
        Host = host;
    }

    public JGraphScriptGlobals Host { get; }

    /// <summary>Every live device object, oldest first.</summary>
    public IReadOnlyList<DeviceObject> Live
    {
        get
        {
            lock (_live)
            {
                return _live.ToArray();
            }
        }
    }

    public void Remember(DeviceObject device)
    {
        lock (_live)
        {
            _live.Add(device);
        }
    }

    public void Forget(DeviceObject device)
    {
        lock (_live)
        {
            _live.Remove(device);
        }
    }

    /// <summary>
    /// The classic device the last <c>bluetooth</c> object deleted was connected to: what
    /// <c>bluetooth()</c> with no argument reconnects to (R2025b's LastConnectionInfo).
    /// </summary>
    public (string Name, ulong Address, int Channel)? LastBluetooth { get; set; }

    /// <summary>
    /// Every peripheral a <c>blelist</c> or <c>ble</c> scan has heard, by address: its name and
    /// whether it accepts connections (blelib's Utility.getDevices), what <c>ble(name)</c> matches.
    /// </summary>
    public Dictionary<string, (string Name, bool Connectable)> BleSeen { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The simulated Bluetooth <c>jgraph.internal.btsim('on')</c> installed on this session's thread.</summary>
    public JGraph.Devices.Simulation.SimulatedBluetooth? BluetoothSimulation { get; private set; }

    /// <summary>Installs the simulated Bluetooth for scripts on the calling thread, or answers the one installed.</summary>
    public JGraph.Devices.Simulation.SimulatedBluetooth StartBluetoothSimulation()
    {
        if (BluetoothSimulation is null)
        {
            BluetoothSimulation = new JGraph.Devices.Simulation.SimulatedBluetooth();
        }

        JGraph.Devices.Bluetooth.BluetoothBackends.Simulate(BluetoothSimulation);
        return BluetoothSimulation;
    }

    /// <summary>Removes the simulated Bluetooth: the device objects on it are deleted first.</summary>
    public void StopBluetoothSimulation()
    {
        if (BluetoothSimulation is null)
        {
            return;
        }

        foreach (DeviceObject device in Live.Where(static d => d is BluetoothObject or BleObject))
        {
            device.Delete();
        }

        JGraph.Devices.Bluetooth.BluetoothBackends.Simulate(null);
        BluetoothSimulation.Dispose();
        BluetoothSimulation = null;
        BleSeen.Clear();
        LastBluetooth = null;
    }

    /// <summary>
    /// The resources visadev objects hold, by upper-cased name (R2025b's ResourceFactory list): a second
    /// object for one is refused, and <c>visadev("reset")</c> forgets them.
    /// </summary>
    public HashSet<string> VisaOpen { get; } = new(StringComparer.Ordinal);

    /// <summary>What the last <c>visadevlist</c> listed, which <c>visadev</c> looks a name up in first.</summary>
    public List<VisaResourceInfo> VisaCache { get; set; } = new();

    /// <summary>The simulated VISA <c>jgraph.internal.visasim('on')</c> installed for this session.</summary>
    public JGraph.Devices.Simulation.SimulatedVisa? VisaSimulation { get; private set; }

    /// <summary>Installs the simulated VISA, over this session's simulated serial ports, or answers the one installed.</summary>
    public JGraph.Devices.Simulation.SimulatedVisa StartVisaSimulation() =>
        VisaSimulation ??= new JGraph.Devices.Simulation.SimulatedVisa(
            SimulatedFor,
            () => SimulatedPorts().Select(static p => p.Name).ToList(),
            IsSimulatedPeer);

    /// <summary>Removes the simulated VISA: the visadev objects on it are deleted first.</summary>
    public void StopVisaSimulation()
    {
        if (VisaSimulation is null)
        {
            return;
        }

        foreach (DeviceObject device in Live.Where(static d => d is VisadevObject))
        {
            device.Delete();
        }

        VisaSimulation.Dispose();
        VisaSimulation = null;
        VisaCache = new();
        VisaOpen.Clear();
    }

    /// <summary>The simulated audio devices <c>jgraph.internal.audiosim('on')</c> installed for this session.</summary>
    public JGraph.Devices.Simulation.SimulatedAudio? AudioSimulation { get; set; }

    /// <summary>The simulated MIDI devices <c>jgraph.internal.midisim('on')</c> installed for this session.</summary>
    public JGraph.Devices.Simulation.SimulatedMidi? MidiSimulation { get; set; }

    /// <summary>The simulated cameras <c>jgraph.internal.camsim('on')</c> installed for this session.</summary>
    public JGraph.Devices.Simulation.SimulatedCameras? CameraSimulation { get; set; }

    /// <summary>The simulated smart-card readers <c>jgraph.internal.pcscsim('on')</c> installed for this session.</summary>
    public JGraph.Devices.Simulation.SimulatedSmartCards? SmartCardSimulation { get; set; }

    /// <summary>The simulated printer queues <c>jgraph.internal.printsim('on')</c> installed for this session.</summary>
    public JGraph.Devices.Simulation.SimulatedPrinters? PrinterSimulation { get; set; }

    /// <summary>The simulated portable devices <c>jgraph.internal.mtpsim('on')</c> installed for this session.</summary>
    public JGraph.Devices.Simulation.SimulatedMtp? MtpSimulation { get; set; }

    /// <summary>The players <c>sound</c> started, kept until they have played (sound.m's persistent list).</summary>
    public List<DeviceObject> SoundPlayers { get; } = new();

    /// <summary>Whether vrjoystick has warned it is to be removed: R2025b warns once a session.</summary>
    public bool VrjoystickWarned { get; set; }

    /// <summary>The TCP echo server <c>echotcpip("on", port)</c> started, until <c>echotcpip("off")</c>.</summary>
    public JGraph.Devices.Network.EchoServer? EchoTcp { get; set; }

    /// <summary>The UDP echo server <c>echoudp("on", port)</c> started, until <c>echoudp("off")</c>.</summary>
    public JGraph.Devices.Network.EchoServer? EchoUdp { get; set; }

    /// <summary>Deletes every live object and stops the echo servers: the session is ending.</summary>
    public void CloseAll()
    {
        foreach (DeviceObject device in Live)
        {
            device.Delete();
        }

        EchoTcp?.Dispose();
        EchoTcp = null;
        EchoUdp?.Dispose();
        EchoUdp = null;
        VisaSimulation?.Dispose();
        VisaSimulation = null;
        VisaCache = new();
        VisaOpen.Clear();
    }

    // --- the simulator's ports ------------------------------------------------------------------------

    /// <summary>
    /// Registers a simulated port <paramref name="name"/> with the peer engine on its far end, and its
    /// com0com partner <paramref name="peer"/> as a port the peer holds; answers the line (an existing
    /// one when the name is registered already).
    /// </summary>
    public SimulatedLine Simulate(string name, string? peer)
    {
        lock (_simulated)
        {
            if (!_simulated.TryGetValue(name, out SimulatedLine? line))
            {
                line = new SimulatedLine(name.ToUpperInvariant());
                _simulated[name] = line;
            }

            if (peer is not null)
            {
                _simulatedPeers.Add(peer);
            }

            return line;
        }
    }

    /// <summary>The simulated line a name opens, or null for a real port.</summary>
    public SimulatedLine? SimulatedFor(string name)
    {
        lock (_simulated)
        {
            return _simulated.TryGetValue(name, out SimulatedLine? line) ? line : null;
        }
    }

    /// <summary>Whether <paramref name="name"/> is the port the simulated peer holds (COM21 beside COM20).</summary>
    public bool IsSimulatedPeer(string name)
    {
        lock (_simulated)
        {
            return _simulatedPeers.Contains(name);
        }
    }

    /// <summary>The simulated names, the peer's included, with whether each can be opened now.</summary>
    public IEnumerable<(string Name, bool Available)> SimulatedPorts()
    {
        lock (_simulated)
        {
            foreach (SimulatedLine line in _simulated.Values)
            {
                yield return (line.Name, !line.InUse);
            }

            foreach (string peer in _simulatedPeers)
            {
                yield return (peer.ToUpperInvariant(), false);
            }
        }
    }

    /// <summary>Whether this session has any simulated port.</summary>
    public bool Simulating
    {
        get
        {
            lock (_simulated)
            {
                return _simulated.Count > 0;
            }
        }
    }
}

/// <summary>
/// Device events waiting for the script thread (device classes plan, architecture C): a
/// <c>BytesAvailableFcn</c>, an <c>ErrorOccurredFcn</c>, and later hot-plug and HID reports, posted
/// from reader threads. R2025b runs them at <c>pause</c> — <c>pause(0)</c> included — at
/// <c>drawnow</c>, and when MATLAB is idle; never between two statements of a busy loop and never
/// inside a blocking <c>read</c> (probe_sp_callbacks). One queue per script thread.
/// </summary>
internal sealed class DeviceEventQueue
{
    [ThreadStatic]
    private static DeviceEventQueue? t_current;

    private static int s_pending;

    // Signalled by every post, so a pause waiting in slices wakes at once and runs the callback
    // while the bytes that raised it are still the ones waiting, as R2025b's does.
    private static readonly AutoResetEvent s_posted = new(false);

    private readonly ConcurrentQueue<Action> _work = new();
    private int _draining;

    private DeviceEventQueue(int thread) => ScriptThreadId = thread;

    /// <summary>The thread that drains this queue.</summary>
    public int ScriptThreadId { get; }

    /// <summary>Whether any script thread has device events waiting.</summary>
    public static bool AnyPending => Volatile.Read(ref s_pending) > 0;

    /// <summary>Signalled when an event is posted: what a waiting pause also wakes on.</summary>
    public static WaitHandle Posted => s_posted;

    /// <summary>The calling thread's queue, made on first use.</summary>
    public static DeviceEventQueue ForCurrentThread() => t_current ??= new DeviceEventQueue(Environment.CurrentManagedThreadId);

    /// <summary>Runs the calling thread's waiting events: what every drain point calls.</summary>
    public static void DrainCurrent() => t_current?.Drain();

    /// <summary>Queues an event from any thread.</summary>
    public void Post(Action work)
    {
        _work.Enqueue(work);
        Interlocked.Increment(ref s_pending);
        s_posted.Set();
        ScriptEventQueue.PokePump();
    }

    /// <summary>
    /// Runs the events present when it starts, oldest first, on the script thread; an event a callback
    /// posts waits for the next drain point. A drain inside a callback (its own <c>pause</c>) does not
    /// start another.
    /// </summary>
    public void Drain()
    {
        if (Environment.CurrentManagedThreadId != ScriptThreadId || Interlocked.Exchange(ref _draining, 1) == 1)
        {
            return;
        }

        try
        {
            for (int budget = _work.Count; budget > 0 && _work.TryDequeue(out Action? next); budget--)
            {
                Interlocked.Decrement(ref s_pending);
                next();
            }
        }
        finally
        {
            Volatile.Write(ref _draining, 0);
        }
    }

    /// <summary>Drops every waiting event (the run ended).</summary>
    public void Clear()
    {
        while (_work.TryDequeue(out _))
        {
            Interlocked.Decrement(ref s_pending);
        }
    }
}

/// <summary>
/// MATLAB's preferences for the device classes, per user (R2025b keeps them in <c>prefdir</c>, group
/// <c>instrument_preferences</c>): a JSON file under the user's application data, or under the folder
/// <c>JGRAPH_PREFDIR</c> names, which the test lanes point at a temporary folder so a run never reads
/// what a person's session saved.
/// </summary>
internal static class DevicePreferences
{
    private static readonly object Gate = new();

    private static string FilePath
    {
        get
        {
            string? folder = Environment.GetEnvironmentVariable("JGRAPH_PREFDIR");
            if (string.IsNullOrEmpty(folder))
            {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JGraph");
            }

            return Path.Combine(folder, "device-preferences.json");
        }
    }

    /// <summary>The saved record of <paramref name="kind"/> (<c>serialport</c>), or null.</summary>
    public static JsonObject? Read(string kind)
    {
        lock (Gate)
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    return null;
                }

                return JsonNode.Parse(File.ReadAllText(path)) is JsonObject all && all[kind] is JsonObject record
                    ? (JsonObject)record.DeepClone()
                    : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>Saves <paramref name="record"/> as <paramref name="kind"/>'s, or removes it when null; answers whether it could.</summary>
    public static bool Write(string kind, JsonObject? record)
    {
        lock (Gate)
        {
            try
            {
                string path = FilePath;
                JsonObject all = File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing ? existing : new JsonObject();
                bool had = all.ContainsKey(kind);
                if (record is null)
                {
                    all.Remove(kind);
                }
                else
                {
                    all[kind] = record;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, all.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                return record is not null || had;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                return false;
            }
        }
    }
}
