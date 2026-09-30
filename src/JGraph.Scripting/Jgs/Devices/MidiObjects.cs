using System.Globalization;
using System.Text;
using JGraph.Devices;
using JGraph.Devices.Midi;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>What mididevice, midicontrols and mididevinfo share (device classes plan, stage D10b, ADR 0194).</summary>
internal static class MidiShared
{
    /// <summary>The session's MIDI: jgraph.internal.midisim's devices, or the machine's.</summary>
    public static IMidiBackend Backend(DeviceSession session) =>
        session.MidiSimulation ?? (OperatingSystem.IsWindows() ? WinMmMidi.Instance : (IMidiBackend)NoMidi.Instance);

    /// <summary>A machine with no MIDI API JGraph knows: no devices.</summary>
    private sealed class NoMidi : IMidiBackend
    {
        public static readonly NoMidi Instance = new();

        public IReadOnlyList<MidiDeviceInfo> Devices() => [];

        public int DefaultInput() => -1;

        public IMidiInput OpenInput(MidiDeviceInfo device) => throw new DeviceOpenException("No MIDI on this platform.");

        public IMidiOutput OpenOutput(MidiDeviceInfo device) => throw new DeviceOpenException("No MIDI on this platform.");
    }

    public static JgsRuntimeException Error(int line, int col, string key, string text) => new(line, col, $"audio:midi:{key}", text);

    // --- shared ports ---------------------------------------------------------------------------------------------

    private static readonly object Gate = new();
    private static readonly Dictionary<(IMidiBackend, int), (IMidiInput Port, int Holders)> Inputs = [];
    private static readonly Dictionary<(IMidiBackend, int), (IMidiOutput Port, int Holders)> Outputs = [];

    /// <summary>
    /// A device's input, shared by everything that has it open: R2025b's midimex opens a device once
    /// however many mididevice objects name it (two mididevice(0) on the MIDI Mapper, which WinMM
    /// lets only one client open, both succeed; probe_midi_env). The port closes with its last holder.
    /// </summary>
    public static IMidiInput OpenInput(IMidiBackend backend, MidiDeviceInfo device)
    {
        lock (Gate)
        {
            (IMidiInput port, int holders) = Inputs.TryGetValue((backend, device.Id), out var open) ? open : (backend.OpenInput(device), 0);
            Inputs[(backend, device.Id)] = (port, holders + 1);
            return new HeldInput(port, () => Release(Inputs, (backend, device.Id)));
        }
    }

    /// <summary>A device's output, shared as <see cref="OpenInput"/> shares an input.</summary>
    public static IMidiOutput OpenOutput(IMidiBackend backend, MidiDeviceInfo device)
    {
        lock (Gate)
        {
            (IMidiOutput port, int holders) = Outputs.TryGetValue((backend, device.Id), out var open) ? open : (backend.OpenOutput(device), 0);
            Outputs[(backend, device.Id)] = (port, holders + 1);
            return new HeldOutput(port, () => Release(Outputs, (backend, device.Id)));
        }
    }

    private static void Release<T>(Dictionary<(IMidiBackend, int), (T Port, int Holders)> ports, (IMidiBackend, int) key)
        where T : IDisposable
    {
        lock (Gate)
        {
            if (!ports.TryGetValue(key, out var open))
            {
                return;
            }

            if (open.Holders > 1)
            {
                ports[key] = (open.Port, open.Holders - 1);
                return;
            }

            ports.Remove(key);
            open.Port.Dispose();
        }
    }

    /// <summary>One holder's hold on a shared input.</summary>
    private sealed class HeldInput(IMidiInput port, Action release) : IMidiInput
    {
        private int _released;

        public int Waiting => port.Waiting;

        public IReadOnlyList<MidiEvent> Take(int max) => port.Take(max);

        public event Action<MidiEvent>? Arrived
        {
            add => port.Arrived += value;
            remove => port.Arrived -= value;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                release();
            }
        }
    }

    /// <summary>One holder's hold on a shared output.</summary>
    private sealed class HeldOutput(IMidiOutput port, Action release) : IMidiOutput
    {
        private int _released;

        public void Send(IReadOnlyList<(byte[] Bytes, double Delay)> messages) => port.Send(messages);

        public bool WaitSent(TimeSpan timeout) => port.WaitSent(timeout);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                release();
            }
        }
    }

    /// <summary>A received message as midimsgs: one, or a system exclusive message as SystemExclusive, Data and EOX.</summary>
    public static IEnumerable<MidiMsgItem> ToMessages(MidiEvent e)
    {
        byte[] bytes = e.Bytes;
        if (bytes.Length == 0 || bytes[0] != 0xF0)
        {
            yield return MidiMsgItem.Of(bytes, e.Timestamp);
            yield break;
        }

        yield return new MidiMsgItem(0xF0, e.Timestamp);
        int end = bytes[^1] == 0xF7 ? bytes.Length - 1 : bytes.Length;
        for (int at = 1; at < end; at += 8)
        {
            int count = Math.Min(8, end - at);
            yield return count == 8
                ? MidiMsgItem.Of(bytes.AsSpan(at, 8), e.Timestamp)
                : MidiMsgItem.Of([.. bytes.AsSpan(at, count), 0xF4], e.Timestamp);
        }

        if (end < bytes.Length)
        {
            yield return new MidiMsgItem(0xF7, e.Timestamp);
        }
    }

    /// <summary>
    /// Messages as they go on the wire, each with its delay: a channel or system message is its own
    /// bytes; SystemExclusive, the Data after it and EOX are one system exclusive message, sent at
    /// the SystemExclusive's time. Data outside a system exclusive message is not sent.
    /// </summary>
    public static List<(byte[] Bytes, double Delay)> ToWire(IEnumerable<MidiMsgItem> messages)
    {
        var wire = new List<(byte[], double)>();
        List<byte>? sysex = null;
        double sysexTime = 0;
        foreach (MidiMsgItem m in messages)
        {
            byte status = m[0];
            if (status == 0xF0)
            {
                if (sysex is not null)
                {
                    wire.Add(([.. sysex], sysexTime));
                }

                sysex = [0xF0];
                sysexTime = m.Timestamp;
            }
            else if (status < 0x80)
            {
                sysex?.AddRange(MidiMsgs.MsgBytes(m));
            }
            else if (status == 0xF7 && sysex is not null)
            {
                sysex.Add(0xF7);
                wire.Add(([.. sysex], sysexTime));
                sysex = null;
            }
            else
            {
                wire.Add((MidiMsgs.MsgBytes(m), m.Timestamp));
            }
        }

        if (sysex is not null)
        {
            wire.Add(([.. sysex], sysexTime));
        }

        return wire;
    }
}

/// <summary>
/// <c>device = mididevice(…)</c> (device classes plan, stage D10b, ADR 0194): Audio Toolbox's handle
/// class, transcribed from R2025b's mididevice.m — its parseArgs and validateDevice, its read-only
/// Input, Output, InputID and OutputID, its disp, and its hidden send, receive and hasdata, which
/// midisend and midireceive call. Sending schedules each message at its timestamp, a delay in seconds.
/// </summary>
internal sealed class MididevObject : DeviceObject, IJgsOwnDisp
{
    private static readonly DeviceClass Declaration = Declare();

    private readonly IMidiInput? _input;
    private readonly IMidiOutput? _output;

    private MididevObject(DeviceSession session, Interpreter interpreter, string input, string output, int inputId, int outputId,
        IMidiInput? rx, IMidiOutput? tx)
        : base(session, interpreter)
    {
        Input = input;
        Output = output;
        InputId = inputId;
        OutputId = outputId;
        _input = rx;
        _output = tx;
    }

    public override DeviceClass Class => Declaration;

    public string Input { get; }

    public string Output { get; }

    public int InputId { get; }

    public int OutputId { get; }

    public override string? Summary() => Deleted ? "closed" : string.Join(", ", new[] { Input, Output }.Where(static n => n.Length > 0).Distinct());

    public string Disp()
    {
        if (Deleted)
        {
            return "  handle to deleted mididevice";
        }

        var sb = new StringBuilder("  mididevice connected to\n");
        if (Input.Length > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {"Input",8}: '{Input}' ({InputId})\n");
        }

        if (Output.Length > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {"Output",8}: '{Output}' ({OutputId})\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    protected override string ShortDisplay() => Disp().TrimStart();

    /// <summary>The constructor: parseArgs, then each side opened.</summary>
    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        IMidiBackend backend = MidiShared.Backend(session);
        IReadOnlyList<MidiDeviceInfo> devs = backend.Devices();
        (int inputId, int outputId) = ParseArgs(args, devs, line, col);
        IMidiInput? rx = null;
        IMidiOutput? tx = null;
        string input = "";
        string output = "";
        try
        {
            if (inputId >= 0)
            {
                input = devs[inputId].Name;
                try
                {
                    rx = MidiShared.OpenInput(backend, devs[inputId]);
                }
                catch (DeviceOpenException e)
                {
                    throw MidiShared.Error(line, col, "MidiDeviceOpenRxFailed", $"Could not open input device '{input}': {e.Message}");
                }
            }

            if (outputId >= 0)
            {
                output = devs[outputId].Name;
                try
                {
                    tx = MidiShared.OpenOutput(backend, devs[outputId]);
                }
                catch (DeviceOpenException e)
                {
                    throw MidiShared.Error(line, col, "MidiDeviceOpenTxFailed", $"Could not open output device '{output}': {e.Message}");
                }
            }
        }
        catch
        {
            rx?.Dispose();
            tx?.Dispose();
            throw;
        }

        var device = new MididevObject(session, interpreter, input, output, inputId, outputId, rx, tx);
        session.Remember(device);
        JgsValue made = JgsValue.External(device);
        JgsLifetime.Minted(made);
        return made;
    }

    private static (int Input, int Output) ParseArgs(IReadOnlyList<JgsValue> args, IReadOnlyList<MidiDeviceInfo> devs, int line, int col)
    {
        string[] io = ["Input", "Output"];
        switch (args.Count)
        {
            case 0:
                throw MidiShared.Error(line, col, "MidiDeviceNoArg",
                    "Missing argument: give the name or ID of an attached MIDI device. Use mididevinfo to identify attached devices.");
            case 1:
                return ValidateDevice(args[0], devs, "either", 1, line, col);
            case 2:
                return DeviceChecks.ValidateString(args[0], io, new DeviceChecks.Subject(null, null, 1), line, col) == "Input"
                    ? (ValidateDevice(args[1], devs, "in", 2, line, col).Input, -1)
                    : (-1, ValidateDevice(args[1], devs, "out", 2, line, col).Output);
            case 3:
                throw MidiShared.Error(line, col, "MidiDeviceWrongNumInputs", "Incorrect number of arguments.");
            case 4:
                if (DeviceChecks.ValidateString(args[0], io, new DeviceChecks.Subject(null, null, 1), line, col) == "Input")
                {
                    int input = ValidateDevice(args[1], devs, "in", 2, line, col).Input;
                    DeviceChecks.ValidateString(args[2], ["Output"], new DeviceChecks.Subject(null, null, 3), line, col);
                    return (input, ValidateDevice(args[3], devs, "out", 4, line, col).Output);
                }
                else
                {
                    int output = ValidateDevice(args[1], devs, "out", 2, line, col).Output;
                    DeviceChecks.ValidateString(args[2], ["Input"], new DeviceChecks.Subject(null, null, 3), line, col);
                    return (ValidateDevice(args[3], devs, "in", 4, line, col).Input, output);
                }

            default:
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }
    }

    private static (int Input, int Output) ValidateDevice(JgsValue device, IReadOnlyList<MidiDeviceInfo> devs, string inout, int narg, int line, int col)
    {
        if (DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(device)) || DeviceChecks.ClassOf(device) == "logical")
        {
            if (devs.Count == 0)
            {
                throw MidiShared.Error(line, col, "MidiDeviceNoDevices",
                    "No MIDI devices are currently attached to this computer. Attach a MIDI device and restart MATLAB.");
            }

            var who = new DeviceChecks.Subject("mididevice", "deviceNumber");
            DeviceChecks.Classes(device, ["numeric"], who, line, col);
            DeviceChecks.Attributes(device, ["real", "finite", "integer", "scalar", "ge:0",
                $"le:{(devs.Count - 1).ToString(CultureInfo.InvariantCulture)}"], who, line, col);
            int id = (int)DeviceChecks.Numbers(device).First();
            switch (inout)
            {
                case "in" when !devs[id].Input:
                    throw MidiShared.Error(line, col, "MidiDeviceIdNotInput", $"Device ID {id} is not a MIDI input.");
                case "out" when devs[id].Input:
                    throw MidiShared.Error(line, col, "MidiDeviceIdNotOutput", $"Device ID {id} is not a MIDI output.");
                case "in":
                    return (id, -1);
                case "out":
                    return (-1, id);
                default:
                    return devs[id].Input ? (id, -1) : (-1, id);
            }
        }

        var textWho = new DeviceChecks.Subject(null, null, narg);
        DeviceChecks.Classes(device, ["char", "string"], textWho, line, col, namesCell: true); // "Instead its type was cell." (probe_midi_env)
        if (!DeviceChecks.IsText(device))
        {
            throw DeviceChecks.Expected(textWho, "expectedScalartext", "scalar text", line, col);
        }

        string name = DeviceChecks.Text(device);
        int[] inputs = Matches(devs, name, input: true);
        int[] outputs = Matches(devs, name, input: false);
        JgsRuntimeException Ambiguous() => MidiShared.Error(line, col, "MidiDeviceAmbiguousName",
            $"Multiple device names match '{name}'. Specify a unique name, or an ID number. Use mididevinfo to identify attached devices.");
        switch (inout)
        {
            case "in":
                return inputs.Length == 1 ? (inputs[0], -1)
                    : inputs.Length > 1 ? throw Ambiguous()
                    : throw MidiShared.Error(line, col, "MidiDeviceNoSuchInput",
                        $"'{name}' is not an input device currently attached to this computer. Use mididevinfo to identify attached devices.");
            case "out":
                return outputs.Length == 1 ? (-1, outputs[0])
                    : outputs.Length > 1 ? throw Ambiguous()
                    : throw MidiShared.Error(line, col, "MidiDeviceNoSuchOutput",
                        $"'{name}' is not an output device currently attached to this computer. Use mididevinfo to identify attached devices.");
            default:
                if (inputs.Length > 1 || outputs.Length > 1)
                {
                    throw Ambiguous();
                }

                // Input and output together only when their names match exactly.
                if (inputs.Length == 1 && outputs.Length == 1 && devs[inputs[0]].Name != devs[outputs[0]].Name)
                {
                    throw Ambiguous();
                }

                if (inputs.Length == 0 && outputs.Length == 0)
                {
                    throw MidiShared.Error(line, col, "MidiDeviceNoSuchDevice",
                        $"'{name}' is not currently attached to this computer. Use mididevinfo to identify attached devices.");
                }

                return (inputs.Length == 1 ? inputs[0] : -1, outputs.Length == 1 ? outputs[0] : -1);
        }
    }

    /// <summary>The devices of one direction an exact name picks, or else those whose names contain it.</summary>
    private static int[] Matches(IReadOnlyList<MidiDeviceInfo> devs, string name, bool input)
    {
        int[] strict = devs.Where(d => d.Input == input && d.Name == name).Select(static d => d.Id).ToArray();
        return strict.Length > 0 ? strict
            : devs.Where(d => d.Input == input && d.Name.Contains(name, StringComparison.Ordinal)).Select(static d => d.Id).ToArray();
    }

    private static MididevObject Me(DeviceObject o) => (MididevObject)o;

    /// <summary>send(device, msgs) and send(device, varargin): midimsgs, or midimsg's arguments, sent in timestamp order.</summary>
    private void Send(DeviceCall call)
    {
        if (call.Args.Count < 1)
        {
            throw call.Error("MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        LiveOrThrow(call.Line, call.Column);
        if (_output is null)
        {
            throw MidiShared.Error(call.Line, call.Column, "MidiSendNotAnOutput", "Device is not a MIDI output.");
        }

        JgsValue first = call.Args[0];
        bool given = call.Args.Count == 1
            && (first.AsExternalOrNull() is MidiMsgValue
                || (first.Type == JgsType.Cell && first.AsCell.Length > 0 && first.AsCell[0].AsExternalOrNull() is MidiMsgValue));
        JgsValue msgs = given ? first : MidiMsgs.Construct(call.Args, call.Line, call.Column);

        // toStruct: a cell's elements are read one message each, as m{i}.RawBytes reads them.
        MidiMsgItem[] items;
        if (msgs.Type == JgsType.Cell)
        {
            items = new MidiMsgItem[msgs.AsCell.Length];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = msgs.AsCell[i].AsExternalOrNull() is MidiMsgValue { Items.Length: 1 } one
                    ? one.Items[0]
                    : throw call.Error("MATLAB:index:expected_one_output_for_curly", "Each cell of the list must hold one midimsg.");
            }
        }
        else
        {
            items = ((MidiMsgValue)msgs.AsExternal).Items;
        }

        // sortStructsByTimestamps: a stable sort, and only when they are out of order.
        IEnumerable<MidiMsgItem> ordered = items.Zip(items.Skip(1)).Any(static p => p.Second.Timestamp < p.First.Timestamp)
            ? items.OrderBy(static m => m.Timestamp) : items;
        try
        {
            _output.Send(MidiShared.ToWire(ordered));
        }
        catch (DeviceIOException e)
        {
            throw MidiShared.Error(call.Line, call.Column, "MidiSendFailed", $"Could not send messages: {e.Message}");
        }
    }

    private IMidiInput InputOrThrow(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        return _input ?? throw MidiShared.Error(call.Line, call.Column, "MidiReceiveNotAnInput", "Device is not a MIDI input.");
    }

    /// <summary>receive(device, maxmsgs): what has arrived, oldest first, as a column of midimsgs.</summary>
    private JgsValue Receive(DeviceCall call)
    {
        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        IMidiInput input = InputOrThrow(call);
        int most = int.MaxValue;
        if (call.Args.Count == 1)
        {
            var who = new DeviceChecks.Subject(null, null);
            DeviceChecks.Classes(call.Args[0], ["numeric"], who, call.Line, call.Column);
            DeviceChecks.Attributes(call.Args[0], ["real", "scalar", "nonnegative"], who, call.Line, call.Column);
            double asked = DeviceChecks.Numbers(call.Args[0]).First();
            if (double.IsFinite(asked))
            {
                DeviceChecks.Attributes(call.Args[0], ["integer"], who, call.Line, call.Column);
                most = (int)Math.Min(asked, int.MaxValue);
            }
        }

        MidiMsgItem[] items = input.Take(most).SelectMany(MidiShared.ToMessages).ToArray();
        return MidiMsgValue.Of(items, items.Length, items.Length == 0 ? 0 : 1);
    }

    private static DeviceClass Declare()
    {
        static DeviceProperty ReadOnly(string name, Func<MididevObject, JgsValue> get) => new(name,
            (o, _) => get(Me(o)),
            (_, _, c) => throw c.Error("MATLAB:class:SetProhibited",
                $"Unable to set the '{name}' property of class ''mididevice'' because it is read-only."));

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["send"] = static c =>
            {
                Me(c.Target).Send(c);
                return [];
            },
            ["receive"] = static c => [Me(c.Target).Receive(c)],
            ["hasdata"] = static c => [JgsValue.Bool(Me(c.Target).InputOrThrow(c).Waiting > 0)],
        };

        return new DeviceClass("mididevice", "mididevice", [],
            [
                ReadOnly("Input", static d => JgsValue.Str(d.Input)),
                ReadOnly("Output", static d => JgsValue.Str(d.Output)),
                ReadOnly("InputID", static d => JgsValue.Number(d.InputId)),
                ReadOnly("OutputID", static d => JgsValue.Number(d.OutputId)),
            ],
            methods,
            ["addlistener", "delete", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "mididevice", "ne", "notify"],
            [])
        {
            NoGetSet = true,
        };
    }

    protected override void OnDelete()
    {
        _input?.Dispose();
        _output?.Dispose();
    }
}

/// <summary>
/// <c>mc = midicontrols(…)</c> (device classes plan, stage D10b, ADR 0194): Audio Toolbox's handle
/// class over MIDI control changes, transcribed from R2025b's midicontrols.m — its inputParser, its
/// device lookup and warnings, midiread, midisync, midicallback and disp. With no input it keeps its
/// initial values, as R2025b's does on a machine with none (probe_midi_controls). Values are kept raw
/// (0 to 127) and read through midicontrols' own scaling (probe_midi_scale): raw r is r/126 up to 63
/// and (r-1)/126 above, and x is round(x*126), one more above one half.
/// </summary>
internal sealed class MidicontrolsObject : DeviceObject, IJgsOwnDisp
{
    private static readonly DeviceClass Declaration = Declare();

    private readonly object _gate = new();
    private readonly double[] _controlNumbers;
    private readonly int[] _controlRows;
    private readonly int[] _channels;
    private readonly int[] _controls;
    private readonly double[] _raw;
    private readonly double[] _initial;
    private readonly bool _normalize;
    private readonly string _device;
    private readonly IMidiInput? _input;
    private readonly IMidiBackend _backend;
    private readonly DeviceEventQueue _queue;
    private (int Channel, int Control)? _last;
    private JgsValue _callback = JgsEmpty.Zero();
    private double[]? _previous;
    private int _checkPosted;

    private MidicontrolsObject(DeviceSession session, Interpreter interpreter, double[] controlNumbers, int[] shape,
        double[] initial, bool normalize, string device, IMidiInput? input, IMidiBackend backend)
        : base(session, interpreter)
    {
        _controlNumbers = controlNumbers;
        _controlRows = shape;
        _normalize = normalize;
        _device = device;
        _input = input;
        _backend = backend;
        _queue = DeviceEventQueue.ForCurrentThread();
        if (controlNumbers.Length == 0)
        {
            _channels = [0];
            _controls = [-1];
        }
        else
        {
            _channels = controlNumbers.Select(static n => (int)Math.Floor(n / 1000)).ToArray();
            _controls = controlNumbers.Select(static n => (int)(n - (Math.Floor(n / 1000) * 1000))).ToArray();
        }

        _initial = initial;
        _raw = _controls.Select((_, i) => initial.Length == 1 ? initial[0] : initial[i]).ToArray();
        if (_input is not null)
        {
            _input.Arrived += Arrived;
        }
    }

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "deleted" : _device.Length > 0 ? _device : "no MIDI device";

    // --- scaling (midiScaleFromRaw, midiScaleToRaw) -------------------------------------------------------------

    public static double FromRaw(double raw) => raw <= 63 ? raw / 126 : (raw - 1) / 126;

    public static double ToRaw(double x)
    {
        double raw = Math.Round(x * 126, MidpointRounding.AwayFromZero);
        return raw > 63 ? raw + 1 : raw;
    }

    // --- the constructor ----------------------------------------------------------------------------------------

    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        JgsValue controlArg = JgsEmpty.Zero();
        JgsValue initialArg = JgsValue.Number(0);
        string midiDevice = "";
        string outputMode = "normalized";
        string[] parameters = ["MIDIDevice", "OutputMode"];
        var given = args.Select(TransportClient.Str2Char).ToList();

        bool IsParameterName(JgsValue v) => DeviceChecks.IsText(v)
            && parameters.Any(p => p.StartsWith(DeviceChecks.Text(v), StringComparison.OrdinalIgnoreCase) && DeviceChecks.Text(v).Length > 0);

        void Validate(string name, Action check)
        {
            try
            {
                check();
            }
            catch (JgsRuntimeException e)
            {
                throw new JgsRuntimeException(line, col, e.Identifier ?? "", $"The value of '{name}' is invalid. {e.Message}");
            }
        }

        int at = 0;
        if (at < given.Count && !IsParameterName(given[at]))
        {
            JgsValue v = given[at++];
            Validate("ControlNumbers", () => CheckControlNum(v, line, col));
            controlArg = v;
            if (at < given.Count && !IsParameterName(given[at]))
            {
                JgsValue w = given[at++];
                Validate("InitialValues", () => CheckInitialVal(w, null, null, line, col));
                initialArg = w;
            }
        }

        while (at < given.Count)
        {
            if (!DeviceChecks.IsText(given[at]))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar",
                    "Expected a string scalar or character vector for the parameter name.");
            }

            string asked = DeviceChecks.Text(given[at]);
            string? parameter = parameters.FirstOrDefault(p => p.StartsWith(asked, StringComparison.OrdinalIgnoreCase) && asked.Length > 0);
            if (parameter is null)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:UnmatchedParameter",
                    $"'{asked}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.");
            }

            if (at + 1 >= given.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    $"No value was given for '{parameter}'. Name-value pair arguments require a name followed by a value.");
            }

            JgsValue value = given[at + 1];
            at += 2;
            if (parameter == "MIDIDevice")
            {
                Validate(parameter, () =>
                {
                    if (!(value.Type == JgsType.String || (DeviceChecks.ClassOf(value) == "char" && DeviceChecks.Count(value) == 0)))
                    {
                        throw new JgsRuntimeException(line, col, "audio:midicontrols:invalidMIDIDevice", "Invalid MIDI device name.");
                    }
                });
                midiDevice = DeviceChecks.Count(value) == 0 ? "" : DeviceChecks.Text(value);
            }
            else
            {
                Validate(parameter, () =>
                {
                    string mode = DeviceChecks.IsText(value) ? DeviceChecks.Text(value) : "";
                    if (mode.Length == 0 || !("normalized".StartsWith(mode, StringComparison.OrdinalIgnoreCase) || "rawmidi".StartsWith(mode, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new JgsRuntimeException(line, col, "audio:midicontrols:invalidOutputMode", "Invalid output mode.");
                    }
                });
                outputMode = DeviceChecks.Text(value);
            }
        }

        double[] controlNumbers = DeviceChecks.Count(controlArg) == 0 ? [] : DeviceChecks.Numbers(controlArg).ToArray();
        int[] shape = DeviceChecks.Count(controlArg) == 0 ? [0, 0] : [JgsMatrix.RowCount(Shaped(controlArg)), JgsMatrix.ColCount(Shaped(controlArg))];
        double[] initial = DeviceChecks.Numbers(initialArg).ToArray();
        bool normalize = "normalized".StartsWith(outputMode, StringComparison.OrdinalIgnoreCase);
        CheckInitialVal(initialArg, normalize, shape, line, col);
        if (normalize)
        {
            initial = Array.ConvertAll(initial, ToRaw);
        }

        // getDeviceNum: the named input (the first whose name contains the text), or the default one.
        IMidiBackend backend = MidiShared.Backend(session);
        IReadOnlyList<MidiDeviceInfo> devs = backend.Devices();
        int deviceNum;
        if (midiDevice.Length == 0)
        {
            deviceNum = backend.DefaultInput();
            if (deviceNum < 0)
            {
                JgsBuiltins.Warn(session.Host, "audio:midi:systemDefaultDeviceOpenFailed", "could not open system default MIDI device.");
            }
        }
        else
        {
            deviceNum = devs.FirstOrDefault(d => d.Input && d.Name.Contains(midiDevice, StringComparison.Ordinal))?.Id ?? -1;
            if (deviceNum < 0)
            {
                JgsBuiltins.Warn(session.Host, "audio:midi:specifiedDeviceOpenFailed", $"could not open specified MIDI device {midiDevice}.");
            }
        }

        IMidiInput? input = null;
        if (deviceNum >= 0)
        {
            try
            {
                input = MidiShared.OpenInput(backend, devs[deviceNum]);
            }
            catch (DeviceOpenException e)
            {
                throw new JgsRuntimeException(line, col, "audio:midi:openFailedUnknown", $"Internal error while opening a control: {e.Message}");
            }
        }

        var controls = new MidicontrolsObject(session, interpreter, controlNumbers, shape, initial, normalize,
            deviceNum >= 0 ? devs[deviceNum].Name : "", input, backend);
        session.Remember(controls);
        JgsValue made = JgsValue.External(controls);
        JgsLifetime.Minted(made);
        return made;
    }

    private static JgsValue Shaped(JgsValue value) => value.Type == JgsType.Array ? value : JgsValue.Array([value]);

    /// <summary>checkControlNum: whole numbers from 0, each a channel (0 to 16) times 1000 plus a control (0 to 127).</summary>
    private static void CheckControlNum(JgsValue arg, int line, int col)
    {
        if (DeviceChecks.Count(arg) == 0 && DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(arg)))
        {
            return;
        }

        bool ok = DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(arg)) && !JgsBuiltins.HasComplexPart(arg) && DeviceChecks.Count(arg) > 0;
        double[] values = ok ? DeviceChecks.Numbers(arg).ToArray() : [];
        ok = ok && values.All(static v => Math.Floor(v) == v && v >= 0);
        ok = ok && values.All(static v => Math.Floor(v / 1000) <= 16 && v - (Math.Floor(v / 1000) * 1000) <= 127);
        if (!ok)
        {
            throw new JgsRuntimeException(line, col, "audio:midicontrols:invalidControlNum", "Invalid control number.");
        }
    }

    /// <summary>checkInitialVal: numeric, real, not empty, not negative; with the mode and shape, one value or one a control, in range.</summary>
    private static void CheckInitialVal(JgsValue arg, bool? normalize, int[]? shape, int line, int col, string key = "audio:midicontrols:invalidInitialVal", string text = "Invalid initial value.")
    {
        bool yes = DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(arg)) && !JgsBuiltins.HasComplexPart(arg) && DeviceChecks.Count(arg) > 0;
        double[] values = yes ? DeviceChecks.Numbers(arg).ToArray() : [];
        yes = yes && values.All(static v => v >= 0);
        if (normalize is { } normalized && shape is not null)
        {
            yes = yes && (values.Length == 1 || (JgsMatrix.RowCount(Shaped(arg)) == shape[0] && JgsMatrix.ColCount(Shaped(arg)) == shape[1]));
            yes = yes && (normalized ? values.All(static v => v <= 1) : values.All(static v => v <= 127 && v == Math.Floor(v)));
        }

        if (!yes)
        {
            throw new JgsRuntimeException(line, col, key, text);
        }
    }

    // --- controls ---------------------------------------------------------------------------------------------------

    /// <summary>A control change on the device: every control it names takes its value.</summary>
    private void Arrived(MidiEvent e)
    {
        if (e.Bytes.Length < 3 || (e.Bytes[0] & 0xF0) != 0xB0)
        {
            return;
        }

        int channel = (e.Bytes[0] & 0x0F) + 1;
        int control = e.Bytes[1];
        lock (_gate)
        {
            bool matched = false;
            for (int i = 0; i < _controls.Length; i++)
            {
                if ((_controls[i] == -1 || _controls[i] == control) && (_channels[i] == 0 || _channels[i] == channel))
                {
                    _raw[i] = e.Bytes[2];
                    matched = true;
                }
            }

            // The last control is the last one this object follows that moved, as midiid reads it.
            if (matched)
            {
                _last = (channel, control);
            }
        }

        if (_previous is not null && Interlocked.Exchange(ref _checkPosted, 1) == 0)
        {
            _queue.Post(CheckCallback);
        }
    }

    /// <summary>The values as midiread answers them: the controls' shape, scaled unless the mode is rawmidi.</summary>
    private double[] Values()
    {
        lock (_gate)
        {
            return _normalize ? Array.ConvertAll(_raw, FromRaw) : (double[])_raw.Clone();
        }
    }

    private JgsValue ValuesValue()
    {
        double[] values = Values();
        return _controlNumbers.Length == 0 ? JgsValue.Number(values[0]) : JgsMatrix.FromColumnMajor(values, _controlRows[0], _controlRows[1]);
    }

    /// <summary>The callback listener: when the values have changed since it last looked, cb(obj).</summary>
    private void CheckCallback()
    {
        Interlocked.Exchange(ref _checkPosted, 0);
        if (Deleted || _previous is null || _callback.Type != JgsType.Function)
        {
            return;
        }

        double[] now = Values();
        if (now.AsSpan().SequenceEqual(_previous))
        {
            return;
        }

        _previous = now;
        try
        {
            JgsCallbacks.Invoke(_callback.AsCallable, [JgsValue.External(this)], 0, 0);
        }
        catch (JgsException failure)
        {
            Session.Host.WriteErr($"Error while evaluating the midicallback for midicontrols\n\n{failure.Message}");
        }
    }

    private static MidicontrolsObject Me(DeviceObject o) => (MidicontrolsObject)o;

    public string Disp()
    {
        if (Deleted)
        {
            return "handle to deleted midicontrols";
        }

        string controls = _controlNumbers.Length switch
        {
            0 => "any control",
            1 => $"control {(int)_controlNumbers[0]}",
            _ when IsContiguous(_controlNumbers) && (_controlRows[0] == 1 || _controlRows[1] == 1) =>
                $"controls {(int)_controlNumbers[0]}:{(int)_controlNumbers[^1]}",
            _ => $"{_controlNumbers.Length} controls",
        };
        string device = _device.Length == 0 ? "no MIDI device" : $"'{_device}'";
        return $"midicontrols object: {controls} on {device}";

        static bool IsContiguous(double[] n) => n.Select((v, i) => v == n[0] + i).All(static b => b);
    }

    protected override string ShortDisplay() => Disp();

    private static DeviceClass Declare()
    {
        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["read"] = static c =>
            {
                MidicontrolsObject mc = Me(c.Target);
                mc.LiveOrThrow(c.Line, c.Column);
                if (c.Args.Count > 0)
                {
                    throw c.Error("MATLAB:TooManyInputs", "Too many input arguments.");
                }

                JgsValue values = mc.ValuesValue();
                if (c.Wanted < 2)
                {
                    return [values];
                }

                (int Channel, int Control)? last;
                lock (mc._gate)
                {
                    last = mc._last;
                }

                return [values, last is { } l ? JgsValue.Number((l.Channel * 1000) + l.Control) : JgsEmpty.Zero()];
            },
            ["sync"] = static c =>
            {
                MidicontrolsObject mc = Me(c.Target);
                mc.LiveOrThrow(c.Line, c.Column);
                if (c.Args.Count > 1)
                {
                    throw new JgsRuntimeException(c.Line, c.Column, "audio:midisync:tooManyInputs", "too many input arguments.");
                }

                double[] values;
                if (c.Args.Count == 1)
                {
                    CheckInitialVal(c.Args[0], mc._normalize, mc._controlRows[0] == 0 ? [1, 1] : mc._controlRows, c.Line, c.Column,
                        "audio:midisync:invalidSyncVal", "invalid sync value.");
                    values = DeviceChecks.Numbers(c.Args[0]).ToArray();
                    if (mc._normalize)
                    {
                        values = Array.ConvertAll(values, ToRaw);
                    }
                }
                else
                {
                    values = mc._initial;
                }

                mc.Sync(values);
                return [];
            },
            ["callback"] = static c =>
            {
                MidicontrolsObject mc = Me(c.Target);
                mc.LiveOrThrow(c.Line, c.Column);
                if (c.Args.Count > 1)
                {
                    throw new JgsRuntimeException(c.Line, c.Column, "audio:midicallback:tooManyInputs", "too many input arguments.");
                }

                JgsValue old = mc._callback;
                if (c.Args.Count == 1)
                {
                    JgsValue newFh = c.Args[0];
                    bool empty = DeviceChecks.Count(newFh) == 0 && newFh.Type != JgsType.Function;
                    if (!empty && newFh.Type != JgsType.Function)
                    {
                        throw new JgsRuntimeException(c.Line, c.Column, "audio:midicallback:invalidFunctionHandle", "not a function handle.");
                    }

                    mc._callback = empty ? JgsEmpty.Zero() : newFh;
                    mc._previous = empty ? null : mc.Values();
                }

                return [old];
            },
            ["midiinfo"] = static c =>
            {
                MidicontrolsObject mc = Me(c.Target);
                mc.LiveOrThrow(c.Line, c.Column);
                JgsValue numbers = mc._controlNumbers.Length == 0 ? JgsMatrix.FromColumnMajor([], 0, 0)
                    : JgsMatrix.FromColumnMajor(mc._controlNumbers, mc._controlRows[0], mc._controlRows[1]);
                return [JgsNumericClasses.Stamp(numbers, JgsNumericClass.Int32), JgsValue.Str(mc._device)];
            },
        };

        return new DeviceClass("midicontrols", "midicontrols", [], [], methods,
            ["addlistener", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "midicontrols", "ne", "notify"],
            [])
        {
            NoGetSet = true,
        };
    }

    /// <summary>midiSyncControl: each control that names one gets a control change on the output of the input's name.</summary>
    private void Sync(double[] values)
    {
        if (_input is null)
        {
            return;
        }

        MidiDeviceInfo? output = _backend.Devices().FirstOrDefault(d => !d.Input && d.Name == _device);
        if (output is null)
        {
            return;
        }

        var wire = new List<(byte[], double)>();
        for (int i = 0; i < _controls.Length; i++)
        {
            if (_controls[i] < 0)
            {
                continue;
            }

            double raw = values.Length == 1 ? values[0] : values[i];
            int channel = Math.Max(1, _channels[i]);
            wire.Add(([(byte)(0xB0 + channel - 1), (byte)_controls[i], (byte)raw], 0));
        }

        if (wire.Count == 0)
        {
            return;
        }

        try
        {
            using IMidiOutput port = MidiShared.OpenOutput(_backend, output);
            port.Send(wire);
            port.WaitSent(TimeSpan.FromSeconds(1)); // the port sends on its own thread; closing at once would drop what it has not
        }
        catch (Exception e) when (e is DeviceOpenException or DeviceIOException)
        {
            // midisync cannot tell whether a value was sent, and says nothing when one is not.
        }
    }

    protected override void OnDelete()
    {
        if (_input is not null)
        {
            _input.Arrived -= Arrived;
            _input.Dispose();
        }
    }
}
