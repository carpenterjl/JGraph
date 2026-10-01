using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The device classes' built-ins (device classes plan, stages 1 and 2): <c>serialport</c>,
/// <c>serialportlist</c>, <c>serialportfind</c>, <c>internal.Serialport.clearPreferences</c>, and the
/// test-only <c>jgraph.internal.devicesim</c>; and the answers the generic verbs (<c>get</c>, <c>set</c>,
/// <c>delete</c>, <c>isvalid</c>, <c>properties</c>, <c>methods</c>) give for a device object, which
/// the interpreter's dispatch reaches through <see cref="TryDeviceBuiltin"/> and friends.
/// </summary>
internal static partial class JgsBuiltins
{
    private const string DeviceSimName = "jgraph.internal.devicesim";

    /// <summary>Declares the device built-ins.</summary>
    private static void RegisterDeviceBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        env.Builtins.Register("serialport", JgsValue.Function(new BuiltinFunction("serialport",
            (args, line, col) => SerialportObject.Create(host.Devices, interpreter, args, line, col))
        {
            KeepsStringArguments = true,
        }));
        env.Builtins.Register("serialportlist", JgsValue.Function(new BuiltinFunction("serialportlist",
            (args, line, col) => SerialportObject.List(host.Devices, args, line, col))
        {
            KeepsStringArguments = true,
        }));
        env.Builtins.Register("serialportfind", JgsValue.Function(new BuiltinFunction("serialportfind",
            (args, line, col) => SerialportObject.Find(host.Devices, args, line, col))
        {
            KeepsStringArguments = true,
        }));

        // Stage D2: the network clients and servers, their finds, the echo servers and resolvehost.
        void Keeping(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));
        Keeping("tcpclient", (args, line, col) => TcpclientObject.Create(host.Devices, interpreter, args, line, col));
        Keeping("tcpserver", (args, line, col) => TcpserverObject.Create(host.Devices, interpreter, args, line, col));
        Keeping("udpport", (args, line, col) => UdpportObject.Create(host.Devices, interpreter, args, line, col));
        Keeping("tcpclientfind", (args, line, col) => NetworkShared.Find(host.Devices, static d => d is TcpclientObject, args, line, col));
        Keeping("tcpserverfind", (args, line, col) => NetworkShared.Find(host.Devices, static d => d is TcpserverObject, args, line, col));
        Keeping("udpportfind", (args, line, col) => NetworkShared.Find(host.Devices, static d => d is UdpportObject, args, line, col));
        Keeping("echotcpip", (args, line, col) => NetworkUtilities.Echo(host.Devices, tcp: true, args, line, col));
        Keeping("echoudp", (args, line, col) => NetworkUtilities.Echo(host.Devices, tcp: false, args, line, col));
        env.Builtins.Register("resolvehost", JgsValue.Function(new BuiltinFunction("resolvehost",
            (args, line, col) => NetworkUtilities.ResolveHost(host, args, 1, line, col)[0])
        {
            KeepsStringArguments = true,
            MultiOutput = (args, wanted, line, col) => NetworkUtilities.ResolveHost(host, args, wanted, line, col),
        }));

        // Stage D3: Bluetooth classic and Low Energy. The lists print their pointer to each other only
        // when nobody takes their answer, so they are told the output count.
        Keeping("bluetooth", (args, line, col) => BluetoothObject.Create(host.Devices, interpreter, args, line, col));
        Keeping("ble", (args, line, col) => BleObject.Create(host.Devices, interpreter, args, line, col));
        env.Builtins.Register("bluetoothlist", JgsValue.Function(new BuiltinFunction("bluetoothlist",
            (args, line, col) => BluetoothObject.List(host, interpreter, args, 1, line, col))
        {
            KeepsStringArguments = true,
            TakesOutputCount = true,
            MultiOutput = (args, wanted, line, col) => [BluetoothObject.List(host, interpreter, args, wanted, line, col)],
        }));
        env.Builtins.Register("blelist", JgsValue.Function(new BuiltinFunction("blelist",
            (args, line, col) => BleObject.List(host.Devices, interpreter, args, 1, line, col))
        {
            KeepsStringArguments = true,
            TakesOutputCount = true,
            MultiOutput = (args, wanted, line, col) => [BleObject.List(host.Devices, interpreter, args, wanted, line, col)],
        }));

        // Stage D4: VISA instruments. visadev("reset") answers nothing, so it is told the output count.
        env.Builtins.Register("visadev", JgsValue.Function(new BuiltinFunction("visadev",
            (args, line, col) => VisadevObject.Create(host.Devices, interpreter, args, 1, line, col) is [var made, ..] ? made : JgsValue.Null)
        {
            KeepsStringArguments = true,
            TakesOutputCount = true,
            MultiOutput = (args, wanted, line, col) => VisadevObject.Create(host.Devices, interpreter, args, wanted, line, col),
        }));
        Keeping("visadevlist", (args, line, col) => VisadevObject.List(host.Devices, args, line, col));
        Keeping("visadevfind", (args, line, col) => NetworkShared.Find(host.Devices, static d => d is VisadevObject, args, line, col));

        // Stage D6: Simulink 3D Animation's joystick.
        Keeping("vrjoystick", (args, line, col) => VrjoystickObject.Create(host.Devices, interpreter, args, line, col));

        // Stage D10: audio devices, audioplayer, audiorecorder, sound and soundsc.
        if (OperatingSystem.IsWindows())
        {
            RegisterAudioBuiltins(env, interpreter, host);
        }

        // Stage D10b: MIDI.
        RegisterMidiBuiltins(env, interpreter, host);

        // Stage D11: cameras.
        RegisterWebcamBuiltins(env, interpreter, host);

        // internal.Serialport.clearPreferences(): the hidden static method R2025b's own tests use.
        env.Builtins.RegisterConstant("internal", JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Serialport"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["clearPreferences"] = JgsValue.Function(new BuiltinFunction("internal.Serialport.clearPreferences",
                    (_, _, _) => SerialportObject.ClearPreferences())),
            }),
        }));
    }

    /// <summary>
    /// <c>jgraph.internal.devicesim(port)</c>: registers a simulated serial port for the session, with
    /// the peer engine on its far end, shadowing any real port of that name; the next port up (COM21
    /// beside COM20) is listed as the port the peer holds, as com0com's pair is. Test-only and
    /// undocumented, like <c>jgraph.internal.nativehost</c>.
    /// </summary>
    private static JgsValue DeviceSimFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction(DeviceSimName, (args, line, col) =>
        {
            JGraphScriptGlobals host = interpreter.Host
                ?? throw new JgsRuntimeException(line, col, "JGraph:devicesim:Arguments", "This session has no host.");

            // devicesim('unplug', port): the simulated port loses its device, as an unplugged adapter does.
            if (args.Count == 2 && IsTextScalar(args[0]) && TextOf(args[0]) == "unplug" && IsTextScalar(args[1]))
            {
                host.Devices.SimulatedFor(TextOf(args[1]))?.Unplug();
                return JgsValue.Null;
            }

            if (args.Count != 1 || !IsTextScalar(args[0]))
            {
                throw new JgsRuntimeException(line, col, "JGraph:devicesim:Arguments",
                    $"{DeviceSimName} takes the name of the port to simulate, or 'unplug' and a simulated port's name.");
            }
            string name = TextOf(args[0]);
            string? peer = null;
            int digits = name.Length;
            while (digits > 0 && char.IsAsciiDigit(name[digits - 1]))
            {
                digits--;
            }

            if (digits < name.Length && int.TryParse(name[digits..], out int number))
            {
                peer = name[..digits] + (number + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            host.Devices.Simulate(name, peer);
            return JgsValue.Null;
        })
        {
            BindsAnsAsStatement = false,
        });

    /// <summary>
    /// <c>jgraph.internal.btsim('on')</c>: the simulated Bluetooth for this session (device classes plan,
    /// stage D3), with the classic peer <c>JGraphPeer</c> on channel 1 and the peripheral of the same
    /// name; <c>btsim('off')</c> removes it, <c>btsim('radio', 'on'|'off'|'missing')</c> sets its radio,
    /// <c>btsim('drop')</c> drops every peripheral's link. It ends with the run. Test-only and undocumented.
    /// </summary>
    private static JgsValue BluetoothSimFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction("jgraph.internal.btsim", (args, line, col) =>
        {
            JGraphScriptGlobals host = interpreter.Host
                ?? throw new JgsRuntimeException(line, col, "JGraph:btsim:Arguments", "This session has no host.");
            string verb = args.Count >= 1 && IsTextScalar(args[0]) ? TextOf(args[0]) : "";
            switch (verb)
            {
                case "on" when args.Count == 1:
                    host.Devices.StartBluetoothSimulation();
                    break;
                case "off" when args.Count == 1:
                    host.Devices.StopBluetoothSimulation();
                    break;
                case "drop" when args.Count == 1:
                    host.Devices.StartBluetoothSimulation().DisconnectAll();
                    break;
                case "radio" when args.Count == 2 && IsTextScalar(args[1]) && TextOf(args[1]) is "on" or "off" or "missing":
                    host.Devices.StartBluetoothSimulation().Radio = TextOf(args[1]) switch
                    {
                        "on" => JGraph.Devices.Bluetooth.BluetoothRadioState.On,
                        "off" => JGraph.Devices.Bluetooth.BluetoothRadioState.Off,
                        _ => JGraph.Devices.Bluetooth.BluetoothRadioState.Missing,
                    };
                    break;
                default:
                    throw new JgsRuntimeException(line, col, "JGraph:btsim:Arguments",
                        "jgraph.internal.btsim takes 'on', 'off', 'drop', or 'radio' and 'on', 'off' or 'missing'.");
            }

            return JgsValue.Null;
        })
        {
            BindsAnsAsStatement = false,
        });

    /// <summary>
    /// <c>jgraph.internal.visasim('on')</c>: the simulated VISA for this session (device classes plan,
    /// stage D4) — the simulated serial ports as ASRL resources, and the socket and HiSLIP instruments of
    /// visa_peer.m — ending with the run; <c>visasim('off')</c> removes it. Test-only and undocumented.
    /// </summary>
    private static JgsValue VisaSimFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction("jgraph.internal.visasim", (args, line, col) =>
        {
            JGraphScriptGlobals host = interpreter.Host
                ?? throw new JgsRuntimeException(line, col, "JGraph:visasim:Arguments", "This session has no host.");
            string verb = args.Count == 1 && IsTextScalar(args[0]) ? TextOf(args[0]) : "";
            switch (verb)
            {
                case "on":
                    host.Devices.StartVisaSimulation();
                    break;
                case "off":
                    host.Devices.StopVisaSimulation();
                    break;
                default:
                    throw new JgsRuntimeException(line, col, "JGraph:visasim:Arguments", "jgraph.internal.visasim takes 'on' or 'off'.");
            }

            return JgsValue.Null;
        })
        {
            BindsAnsAsStatement = false,
        });

    /// <summary>
    /// <c>d = jgraph.internal.dfusim('dfuse'|'dfu'|'runtime')</c>: a Dfu object on a new simulated device
    /// (device classes plan, stage D8); <c>dfusim(d, 'peek', address, count)</c> reads its memory past the
    /// loader, <c>dfusim(d, 'image')</c> the plain loader's firmware, <c>dfusim(d, 'state')</c> its DFU
    /// state, <c>dfusim(d, 'gone')</c> whether it reset, and <c>dfusim(d, 'fail', n)</c> makes its n-th
    /// write report errWRITE. Test-only and undocumented.
    /// </summary>
    private static JgsValue DfuSimFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction("jgraph.internal.dfusim", (args, line, col) =>
        {
            JGraphScriptGlobals host = interpreter.Host
                ?? throw new JgsRuntimeException(line, col, "JGraph:dfusim:Arguments", "This session has no host.");
            if (!OperatingSystem.IsWindows())
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:NotSupported", "jgraph.internal.dfusim runs on Windows only.");
            }

            if (args.Count == 1 && IsTextScalar(args[0]))
            {
                JGraph.Devices.Simulation.SimulatedDfuKind kind = TextOf(args[0]) switch
                {
                    "dfuse" => JGraph.Devices.Simulation.SimulatedDfuKind.DfuSe,
                    "dfu" => JGraph.Devices.Simulation.SimulatedDfuKind.Dfu,
                    "runtime" => JGraph.Devices.Simulation.SimulatedDfuKind.Runtime,
                    _ => throw new JgsRuntimeException(line, col, "JGraph:dfusim:Arguments", "jgraph.internal.dfusim makes a 'dfuse', 'dfu' or 'runtime' device."),
                };
                return DfuObject.OpenSimulated(host.Devices, interpreter, new JGraph.Devices.Simulation.SimulatedDfu(kind));
            }

            if (args.Count < 2 || args[0].AsExternalOrNull() is not DfuObject { Simulation: { } sim } || !IsTextScalar(args[1]))
            {
                throw new JgsRuntimeException(line, col, "JGraph:dfusim:Arguments", "jgraph.internal.dfusim takes a kind, or a simulated Dfu object and 'peek', 'image', 'state', 'gone' or 'fail'.");
            }

            double Number(int i) => args.Count > i ? DeviceChecks.Numbers(args[i]).First() : throw new JgsRuntimeException(line, col, "JGraph:dfusim:Arguments", "A number is missing.");
            switch (TextOf(args[1]))
            {
                case "peek":
                    return HidObject.Bytes(sim.Peek((uint)Number(2), (int)Number(3)));
                case "image":
                    return HidObject.Bytes(sim.Image);
                case "state":
                    return JgsValue.StringScalar(JGraph.Devices.Dfu.DfuStatus.StateNameOf(sim.State));
                case "gone":
                    return JgsValue.Bool(sim.Gone);
                case "fail":
                    sim.FailAtWrite = (int)Number(2);
                    return JgsValue.Null;
                default:
                    throw new JgsRuntimeException(line, col, "JGraph:dfusim:Arguments", "jgraph.internal.dfusim takes 'peek', 'image', 'state', 'gone' or 'fail'.");
            }
        })
        {
            BindsAnsAsStatement = false,
        });

    // --- the generic verbs on a device object ---------------------------------------------------------------

    /// <summary>Whether <paramref name="value"/> is a device object or a row of them.</summary>
    internal static bool IsDeviceValue(IJgsExternal? value) => value is DeviceObject or DeviceArray;

    /// <summary>
    /// <c>get</c>, <c>set</c>, <c>delete</c> and <c>isvalid</c> with a device object first — the road the
    /// JGS dialect takes, since it dispatches no methods (ADR 0172), and the one a row takes.
    /// </summary>
    internal static bool TryDeviceBuiltin(string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col, out JgsValue result)
    {
        result = JgsValue.Null;
        if (args.Count == 0 || args[0].AsExternalOrNull() is not { } external || !IsDeviceValue(external))
        {
            return false;
        }

        if (external is DeviceArray array)
        {
            switch (name)
            {
                case "isvalid":
                    result = JgsValue.Array(array.Items.Select(static d => JgsValue.Bool(!d.Deleted)).ToArray());
                    return true;
                case "delete":
                    foreach (DeviceObject item in array.Items)
                    {
                        item.Delete();
                    }

                    return true;
                default:
                    return false;
            }
        }

        var device = (DeviceObject)external;
        JgsValue[] answer = RunDeviceVerb(device, name, args, wanted, line, col);
        result = answer is [var first, ..] ? first : JgsValue.Null;
        return true;
    }

    /// <summary>The generic handle verbs every device object answers, whatever its class declares.</summary>
    internal static readonly HashSet<string> DeviceVerbs = ["get", "set", "delete", "isvalid"];

    /// <summary>Runs one of <see cref="DeviceVerbs"/> on <paramref name="device"/>, the object first among <paramref name="args"/>.</summary>
    internal static JgsValue[] RunDeviceVerb(DeviceObject device, string name, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        var call = new DeviceCall { Target = device, Args = args.Skip(1).ToArray(), Wanted = wanted, Line = line, Column = col };
        if (device.Class.NoGetSet && name is "get" or "set")
        {
            throw call.Error(name == "get" ? "MATLAB:graphics:GetMethodUnknown" : "MATLAB:graphics:SetMethodUnknown",
                $"Cannot find '{name}' method for {device.Class.Name} class.");
        }

        switch (name)
        {
            case "isvalid":
                return [JgsValue.Bool(!device.Deleted)];
            case "delete":
                if (wanted > 0)
                {
                    throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
                }

                device.Delete();
                return [];
            case "get":
                return [DeviceGet(device, call)];
            default:
                DeviceSet(device, call);
                return [];
        }
    }

    private static JgsValue DeviceGet(DeviceObject device, DeviceCall call)
    {
        device.LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count == 0)
        {
            if (call.Wanted == 0)
            {
                call.Host.WriteOut(device.LongDisplay());
                return JgsValue.Null;
            }

            var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
            foreach (string property in device.Class.VisibleNames)
            {
                fields[property] = device.GetProperty(property, call);
            }

            return JgsValue.Struct(fields);
        }

        JgsValue asked = call.Args[0];
        if (asked.Type == JgsType.Cell)
        {
            JgsValue[] names = asked.AsCell;
            var values = new JgsValue[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                values[i] = device.GetProperty(IsTextScalar(names[i]) ? TextOf(names[i]) : "", call);
            }

            JgsValue row = JgsValue.Cell(values);
            row.Reshape(1, values.Length);
            return row;
        }

        return device.GetProperty(IsTextScalar(asked) ? TextOf(asked) : "", call);
    }

    private static void DeviceSet(DeviceObject device, DeviceCall call)
    {
        device.LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count == 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (DeviceProperty property in device.Class.Properties.Where(static p => !p.Hidden && p.Set is not null))
            {
                sb.Append("    ").Append(property.Name).Append(": {}\n");
            }

            call.Host.WriteOut(sb.ToString());
            return;
        }

        if (call.Args.Count == 1)
        {
            call.Host.WriteOut("  0x0 empty cell array\n");
            return;
        }

        if (call.Args.Count % 2 != 0)
        {
            throw call.Error("MATLAB:class:BadParamValuePairs", "Invalid parameter/value pair arguments.");
        }

        for (int i = 0; i < call.Args.Count; i += 2)
        {
            device.SetProperty(IsTextScalar(call.Args[i]) ? TextOf(call.Args[i]) : "", call.Args[i + 1], call, ignoreCase: true);
        }
    }

    /// <summary>What <c>properties</c> and <c>fieldnames</c> list for a device object.</summary>
    internal static IEnumerable<string> DevicePropertyNames(IJgsExternal target) => target switch
    {
        DeviceObject device => device.Class.VisibleNames,
        DeviceArray array => array.Items[0].Class.VisibleNames,
        _ => [],
    };

    /// <summary>What <c>methods</c> lists for a device object.</summary>
    internal static IEnumerable<string> DeviceMethodNames(IJgsExternal target) => target switch
    {
        DeviceObject device => device.Class.MethodListing,
        DeviceArray array => array.Items[0].Class.MethodListing,
        _ => [],
    };

    /// <summary><c>isprop(obj, name)</c> for a device object: any property, hidden ones included (as R2025b answers).</summary>
    internal static bool DeviceHasProperty(IJgsExternal target, string name) => target switch
    {
        DeviceObject device => device.Class.Find(name) is not null,
        DeviceArray array => array.Items[0].Class.Find(name) is not null,
        _ => false,
    };

    /// <summary>
    /// The method a call <c>name(…, obj, …)</c> reaches on a device object: the user-method layer of the
    /// search order, above the built-ins (M145).
    /// </summary>
    internal static bool TryDeviceMethod(string name, JgsValue dominant, out IJgsCallable? callable)
    {
        callable = null;
        if (dominant.AsExternalOrNull() is not DeviceObject device)
        {
            return false;
        }

        if (device.HasMethod(name))
        {
            callable = new DeviceObject.DeviceFunctionSyntax(device, name);
            return true;
        }

        if (DeviceVerbs.Contains(name))
        {
            callable = new DeviceVerbCallable(device, name);
            return true;
        }

        if (DeviceClass.IsSomeClassMethod(name) && !device.Interpreter.Globals.Builtins.TryGet(name, out _))
        {
            callable = new UndefinedForClass(name, device.ClassName);
            return true;
        }

        return false;
    }

    /// <summary>A method some other device class has, called on one whose class has none.</summary>
    private sealed class UndefinedForClass(string name, string className) : IJgsCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            throw new JgsRuntimeException(line, column, "MATLAB:UndefinedFunction",
                $"Undefined function '{name}' for input arguments of type '{className}'.");
    }

    /// <summary><c>get(obj, …)</c>, <c>set(obj, …)</c>, <c>delete(obj)</c>, <c>isvalid(obj)</c> reached as methods.</summary>
    private sealed class DeviceVerbCallable(DeviceObject device, string name) : IJgsCallable, IJgsMultiCallable
    {
        public string Name => name;

        public JgsValue Call(IReadOnlyList<JgsValue> arguments, int line, int column) =>
            CallMultiple(arguments, 1, line, column) is [var first, ..] ? first : JgsValue.Null;

        public JgsValue[] CallMultiple(IReadOnlyList<JgsValue> arguments, int wanted, int line, int column)
        {
            var rest = new List<JgsValue>(arguments.Count) { JgsValue.External(device) };
            bool dropped = false;
            foreach (JgsValue argument in arguments)
            {
                if (!dropped && ReferenceEquals(argument.AsExternalOrNull(), device))
                {
                    dropped = true;
                    continue;
                }

                rest.Add(argument);
            }

            return RunDeviceVerb(device, name, rest, wanted, line, column);
        }
    }

    /// <summary>An operator with a device object on either side: identity under == and ~=, R2025b's refusal otherwise.</summary>
    internal static JgsValue DeviceOperator(TokenType op, JgsValue left, JgsValue right, int line, int col)
    {
        if (op is TokenType.EqualEqual or TokenType.BangEqual)
        {
            bool same = left.AsExternalOrNull() is DeviceEnumValue leftMember ? leftMember.Matches(right)
                : right.AsExternalOrNull() is DeviceEnumValue rightMember ? rightMember.Matches(left)
                : left.AsExternalOrNull() is { } a && ReferenceEquals(a, right.AsExternalOrNull());
            return JgsValue.Bool(op == TokenType.EqualEqual ? same : !same);
        }

        IJgsExternal device = left.AsExternalOrNull() is { } l && IsDeviceValue(l) ? l : right.AsExternal;
        throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
            $"Operator '{OperatorWord(op)}' is not supported for operands of type '{device.ClassName}'.");
    }

    private static string OperatorWord(TokenType op) => op switch
    {
        TokenType.Plus => "+",
        TokenType.Minus => "-",
        TokenType.Star => "*",
        TokenType.Slash => "/",
        TokenType.Less => "<",
        TokenType.Greater => ">",
        TokenType.LessEqual => "<=",
        TokenType.GreaterEqual => ">=",
        _ => op.ToString(),
    };

    /// <summary>A scalar datetime of <paramref name="at"/>, built as <c>datetime</c> builds one (an event's AbsTime).</summary>
    internal static JgsValue DatetimeValue(DateTime at)
    {
        double[] moment = [JgsTime.FromDateTime(at)];
        return Numbers(moment).MarkTime(JgsTime.DatetimeTag(moment, null));
    }

    /// <summary><c>arr(k)</c> on a row of device objects.</summary>
    internal static JgsValue DeviceArrayIndex(DeviceArray array, IReadOnlyList<JgsValue> indices, int line, int col)
    {
        if (indices.Count is < 1 or > 2)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:badsubscript", "Index exceeds the number of array elements.");
        }

        double k = indices[^1].AsNumber;
        if (k != Math.Floor(k) || k < 1 || k > array.Items.Count)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:index:outOfBounds",
                $"Index exceeds the number of array elements. Index must not exceed {array.Items.Count}.");
        }

        return JgsValue.External(array.Items[(int)k - 1]);
    }
}
