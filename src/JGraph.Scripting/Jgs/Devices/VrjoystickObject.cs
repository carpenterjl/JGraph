using JGraph.Devices.Joystick;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>joy = vrjoystick(id)</c>, <c>vrjoystick(id, 'forcefeedback')</c> (device classes plan, stage D6,
/// ADR 0189): Simulink 3D Animation's joystick, which R2025b is to remove in favour of
/// sim3d.io.Joystick. Its refusals and warning are measured (probe_vrjoystick): its first construction in a session warns
/// <c>sl3d:init:hardwaredeprecationwarning</c>, and an id no joystick answers is
/// <c>sl3d:vrjoystick:notconnected</c>, whatever else is wrong with it. The methods follow R2025b's
/// help: <c>[a, b, p] = read(joy)</c> (axes in [-1, 1], buttons, points of view in degrees), <c>axis</c>,
/// <c>button</c>, <c>pov</c>, <c>caps</c>, <c>force</c> and <c>close</c>. The joysticks are WinMM's
/// (<see cref="JoystickBackends"/>), which have no force feedback.
/// </summary>
internal sealed class VrjoystickObject : DeviceObject
{
    private static readonly DeviceClass Declaration = Declare();

    private readonly IJoystickBackend _backend;
    private readonly int _id;

    private VrjoystickObject(DeviceSession session, Interpreter interpreter, IJoystickBackend backend, int id)
        : base(session, interpreter)
    {
        _backend = backend;
        _id = id;
        _name = backend.Caps(id)?.Name ?? "";
    }

    /// <summary>The controller's name as it was when the object was made: what the Workspace pane shows without asking the device.</summary>
    private readonly string _name;

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "closed" : _name.Length > 0 ? _name : $"joystick {_id + 1}";

    /// <summary>The constructor: its argument count, the deprecation warning, then the joystick or notconnected.</summary>
    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        // Too many inputs never reach the body; the body warns once a session, then finds the id missing.
        if (args.Count > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        if (!session.VrjoystickWarned)
        {
            session.VrjoystickWarned = true;
            JgsBuiltins.Warn(session.Host, "sl3d:init:hardwaredeprecationwarning",
                "'vrjoystick' will be removed in a future release. Use 'sim3d.io.Joystick' instead.");
        }

        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
        }
        IJoystickBackend backend = JoystickBackends.Current;
        IReadOnlyList<int> connected = backend.Connected();
        JgsValue idArg = args[0];
        bool numeric = DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(idArg)) && DeviceChecks.Count(idArg) == 1;
        double k = numeric ? DeviceChecks.Numbers(idArg).First() : double.NaN;
        if (!(k >= 1 && k == Math.Floor(k) && k <= connected.Count))
        {
            throw NotConnected(line, col);
        }

        if (args.Count == 2)
        {
            JgsValue option = TransportClient.Str2Char(args[1]);
            if (!DeviceChecks.IsText(option) || !DeviceChecks.Text(option).Equals("forcefeedback", StringComparison.OrdinalIgnoreCase))
            {
                throw new JgsRuntimeException(line, col, "JGraph:vrjoystick:InvalidOption", "The second argument of vrjoystick is 'forcefeedback'.");
            }
        }

        var joy = new VrjoystickObject(session, interpreter, backend, connected[(int)k - 1]);
        session.Remember(joy);
        JgsValue made = JgsValue.External(joy);
        JgsLifetime.Minted(made);
        return made;
    }

    private static JgsRuntimeException NotConnected(int line, int col) =>
        new(line, col, "sl3d:vrjoystick:notconnected", "Joystick is not connected.");

    private static VrjoystickObject Me(DeviceObject o) => (VrjoystickObject)o;

    private JoystickState State(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        return _backend.Read(_id) ?? throw NotConnected(call.Line, call.Column);
    }

    private JoystickCaps Caps(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        return _backend.Caps(_id) ?? throw NotConnected(call.Line, call.Column);
    }

    /// <summary>The elements a method's second argument picks (1-based), or all of them.</summary>
    private static T[] Pick<T>(T[] all, DeviceCall call)
    {
        if (call.Args.Count == 0)
        {
            return all;
        }

        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        double[] which = DeviceChecks.Numbers(call.Args[0]).ToArray();
        var picked = new T[which.Length];
        for (int i = 0; i < which.Length; i++)
        {
            double n = which[i];
            if (n != Math.Floor(n) || n < 1)
            {
                throw call.Error("MATLAB:badsubscript", "Array indices must be positive integers or logical values.");
            }

            if (n > all.Length)
            {
                throw call.Error("MATLAB:badsubscript",
                    $"Index exceeds the number of array elements. Index must not exceed {all.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
            }

            picked[i] = all[(int)n - 1];
        }

        return picked;
    }

    private static JgsValue Numbers(double[] values) => TransportClient.Row(values);

    private static JgsValue Logicals(bool[] values)
    {
        JgsValue row = JgsValue.Array(values.Select(JgsValue.Bool).ToArray());
        row.Reshape(1, values.Length);
        return row;
    }

    private JgsRuntimeException NoForce(DeviceCall call) =>
        call.Error("JGraph:vrjoystick:NoForceFeedback", $"This joystick has no force-feedback axes; JGraph reads joysticks through WinMM, which has none.");

    private static DeviceClass Declare()
    {
        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["read"] = static c =>
            {
                VrjoystickObject joy = Me(c.Target);
                if (c.Args.Count > 1)
                {
                    throw c.Error("MATLAB:TooManyInputs", "Too many input arguments.");
                }

                if (c.Args.Count == 1 && DeviceChecks.Count(c.Args[0]) > 0)
                {
                    throw joy.NoForce(c);
                }

                JoystickState state = joy.State(c);
                return [Numbers(state.Axes), Logicals(state.Buttons), Numbers(state.Povs)];
            },
            ["axis"] = static c => [Numbers(Pick(Me(c.Target).State(c).Axes, c))],
            ["button"] = static c => [Logicals(Pick(Me(c.Target).State(c).Buttons, c))],
            ["pov"] = static c => [Numbers(Pick(Me(c.Target).State(c).Povs, c))],
            ["caps"] = static c =>
            {
                JoystickCaps caps = Me(c.Target).Caps(c);
                return [JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Axes"] = JgsValue.Number(caps.Axes),
                    ["Buttons"] = JgsValue.Number(caps.Buttons),
                    ["POVs"] = JgsValue.Number(caps.Povs),
                    ["Forces"] = JgsValue.Number(caps.Forces),
                })];
            },
            ["force"] = static c =>
            {
                Me(c.Target).Caps(c);
                throw Me(c.Target).NoForce(c);
            },
            ["close"] = static c =>
            {
                c.Target.Delete();
                return [];
            },
        };

        return new DeviceClass("vrjoystick", "vrjoystick", [], [], methods,
            ["axis", "button", "caps", "close", "force", "pov", "read", "vrjoystick"], []);
    }

    protected override void OnDelete()
    {
    }
}
