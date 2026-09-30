using System.Globalization;
using System.Text;
using JGraph.Devices.Midi;
using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// MIDI (device classes plan, stage D10b, ADR 0194): Audio Toolbox's <c>mididevinfo</c>,
/// <c>mididevice</c>, <c>midimsg</c> and <c>midimsgtype</c>, <c>midisend</c>, <c>midireceive</c>, and the
/// older control-surface functions <c>midicontrols</c>, <c>midiread</c>, <c>midisync</c>,
/// <c>midicallback</c> and <c>midiid</c>, transcribed from their R2025b files; and the test-only
/// <c>jgraph.internal.midisim</c>.
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterMidiBuiltins(JgsEnvironment env, Interpreter interpreter, JGraphScriptGlobals host)
    {
        void Keeping(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, body) { KeepsStringArguments = true }));
        void Silent(string name, Action<IReadOnlyList<JgsValue>, int, int> body) =>
            env.Builtins.Register(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                body(args, line, col);
                return JgsValue.Null;
            })
            {
                KeepsStringArguments = true,
                BindsAnsAsStatement = false,
            }));

        env.Builtins.Register("mididevinfo", JgsValue.Function(new BuiltinFunction("mididevinfo",
            (args, line, col) => MidiDevInfo(host, args, 1, line, col))
        {
            TakesOutputCount = true,
            AutoCallsBare = true,
            MultiOutput = (args, wanted, line, col) => wanted == 0 ? [MidiDevInfo(host, args, 0, line, col)] : [MidiDevInfo(host, args, wanted, line, col)],
        }));
        Keeping("mididevice", (args, line, col) => MididevObject.Create(host.Devices, interpreter, args, line, col));
        // A midimsg or a member among a call's arguments picks its class's methods (size, isequal, char …).
        Keeping("midimsg", (args, line, col) =>
        {
            interpreter.NoteNet();
            return MidiMsgs.Construct(args, line, col);
        });
        Keeping("midimsgtype", (args, line, col) =>
        {
            interpreter.NoteNet();
            return MidiTypeValue.Construct(args, line, col);
        });
        Keeping("midicontrols", (args, line, col) => MidicontrolsObject.Create(host.Devices, interpreter, args, line, col));

        // midisend, midireceive, midiread, midisync and midicallback call a method of their first
        // argument (send, receive, read, sync, callback), as the R2025b files do.
        Silent("midisend", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            MidiMethod("send", args, 0, line, col);
        });
        Keeping("midireceive", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
            }

            return MidiMethod("receive", args, 1, line, col)[0];
        });
        env.Builtins.Register("midiread", JgsValue.Function(new BuiltinFunction("midiread",
            (args, line, col) => MidiMethod("read", Exactly(args, line, col), 1, line, col)[0])
        {
            TakesOutputCount = true,
            // midiread.m answers one value; the method's second output (the last control) is read(mc)'s.
            MultiOutput = (args, wanted, line, col) => wanted > 1
                ? throw new JgsRuntimeException(line, col, "MATLAB:TooManyOutputs", "Too many output arguments.")
                : MidiMethod("read", Exactly(args, line, col), 1, line, col),
        }));
        Silent("midisync", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            MidiMethod("sync", args, 0, line, col);
        });
        env.Builtins.Register("midicallback", JgsValue.Function(new BuiltinFunction("midicallback", (args, line, col) =>
        {
            if (args.Count == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
            }

            return MidiMethod("callback", args, 1, line, col)[0];
        })
        {
            TakesOutputCount = true,
            BindsAnsAsStatement = false,
            MultiOutput = (args, wanted, line, col) =>
            {
                if (args.Count == 0)
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments.");
                }

                JgsValue[] old = MidiMethod("callback", args, 1, line, col);
                return wanted == 0 ? [] : old;
            },
        }));
        env.Builtins.Register("midiid", JgsValue.Function(new BuiltinFunction("midiid",
            (args, line, col) => MidiId(host, interpreter, args, line, col)[0])
        {
            TakesOutputCount = true,
            AutoCallsBare = true,
            MultiOutput = (args, wanted, line, col) => MidiId(host, interpreter, args, line, col),
        }));

        static IReadOnlyList<JgsValue> Exactly(IReadOnlyList<JgsValue> args, int line, int col) => args.Count switch
        {
            0 => throw new JgsRuntimeException(line, col, "MATLAB:minrhs", "Not enough input arguments."),
            > 1 => throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments."),
            _ => args,
        };
    }

    /// <summary>
    /// A MIDI function's call of a method on its first argument: the method of a mididevice or
    /// midicontrols, or R2025b's "Undefined function" for anything else.
    /// </summary>
    private static JgsValue[] MidiMethod(string method, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args[0].AsExternalOrNull() is DeviceObject device && device.HasMethod(method))
        {
            return device.CallMethod(method, args, wanted, line, col);
        }

        throw new JgsRuntimeException(line, col, "MATLAB:UndefinedFunction",
            $"Undefined function '{method}' for input arguments of type '{ClassOf(args[0], JgsDialect.Matlab)}'.");
    }

    // --- mididevinfo -----------------------------------------------------------------------------------------

    /// <summary>mididevinfo.m: asked for an output, the input and output lists as structs; asked for none, a table printed.</summary>
    private static JgsValue MidiDevInfo(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        IReadOnlyList<MidiDeviceInfo> devices = MidiShared.Backend(host.Devices).Devices();
        if (wanted > 0)
        {
            JgsValue List(bool input)
            {
                var elements = devices.Where(d => d.Input == input).Select(static d => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                {
                    ["Name"] = JgsValue.Str(d.Name),
                    ["Interface"] = JgsValue.Str(d.Interface),
                    ["ID"] = JgsValue.Number(d.Id),
                }).ToArray();
                return elements.Length == 0
                    ? JgsValue.StructArray(new JgsStructArray([], ["Name", "Interface", "ID"]), 0, 0)
                    : JgsValue.StructArray(elements);
            }

            return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["input"] = List(true),
                ["output"] = List(false),
            });
        }

        var text = new StringBuilder();
        if (devices.Count == 0)
        {
            text.Append("  No MIDI devices available.\n");
        }
        else
        {
            text.Append("  MIDI devices available:\n");
            text.Append("  ID   Direction   Interface   Name\n");
            foreach (MidiDeviceInfo d in devices)
            {
                text.Append(CultureInfo.InvariantCulture, $"  {d.Id,2}    {(d.Input ? "input" : "output"),6}     {d.Interface,-9}   '{d.Name}'\n");
            }
        }

        host.WriteOut(text.ToString());
        return JgsValue.Null;
    }

    // --- midiid -----------------------------------------------------------------------------------------------

    /// <summary>
    /// midiid.m: with no MIDI input, its warning and empty answers; with inputs, it waits for a control
    /// to move on any of them and answers the control's number (channel times 1000 plus control) and
    /// the device's name.
    /// </summary>
    private static JgsValue[] MidiId(JGraphScriptGlobals host, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        IMidiBackend backend = MidiShared.Backend(host.Devices);
        var opened = new List<(MidiDeviceInfo Device, IMidiInput Input)>();
        foreach (MidiDeviceInfo device in backend.Devices().Where(static d => d.Input))
        {
            try
            {
                opened.Add((device, MidiShared.OpenInput(backend, device)));
            }
            catch (JGraph.Devices.DeviceOpenException)
            {
                // midiOpenControl failed: the device is left out, as midiid leaves it.
            }
        }

        try
        {
            if (opened.Count == 0)
            {
                Warn(host, "audio:midiId:NoDevices", "could not find any attached MIDI input devices.");
                return [JgsEmpty.Zero(), JgsValue.Str("")];
            }

            host.WriteOut("Move the control you wish to identify. Type ^C to abort.\nWaiting for control message...");
            while (true)
            {
                foreach ((MidiDeviceInfo device, IMidiInput input) in opened)
                {
                    foreach (MidiEvent e in input.Take(int.MaxValue))
                    {
                        if (e.Bytes.Length >= 3 && (e.Bytes[0] & 0xF0) == 0xB0)
                        {
                            host.WriteOut(" done\n");
                            int channel = (e.Bytes[0] & 0x0F) + 1;
                            return [JgsValue.Number(e.Bytes[1] + (channel * 1000)), JgsValue.Str(device.Name)];
                        }
                    }
                }

                PumpWait(TimeSpan.FromSeconds(0.1), interpreter.Cancellation, host.Timers);
            }
        }
        finally
        {
            foreach ((_, IMidiInput input) in opened)
            {
                input.Dispose();
            }
        }
    }

    // --- midimsgtype as a class name ------------------------------------------------------------------------------

    /// <summary><c>midimsgtype.NoteOn</c>: a member; any other name is R2025b's refusal.</summary>
    internal static JgsValue MidiTypeStatic(string member, int line, int col) =>
        MidiTypeValue.TryMember(member, out JgsValue found) ? found
            : throw new JgsRuntimeException(line, col, "MATLAB:subscripting:classHasNoPropertyOrMethod",
                $"The class midimsgtype has no Constant property or Static method named '{member}'.");

    /// <summary><c>enumeration('midimsgtype')</c>: its members listed, or answered as a column and their names.</summary>
    internal static JgsValue[]? MidiTypeEnumeration(Interpreter interpreter, JgsValue asked, int wanted)
    {
        bool named = (IsTextScalar(asked) && TextOf(asked) == "midimsgtype") || asked.AsExternalOrNull() is MidiTypeValue;
        if (!named)
        {
            return null;
        }

        if (wanted == 0)
        {
            var text = new StringBuilder("\nEnumeration members for class 'midimsgtype':\n\n");
            foreach (string name in MidiMsgs.TypeNames)
            {
                text.Append("    ").Append(name).Append('\n');
            }

            interpreter.Host?.print(text.ToString());
            return [];
        }

        JgsValue members = MidiTypeValue.Column(MidiMsgs.TypeNames);
        JgsValue names = JgsValue.Cell(MidiMsgs.TypeNames.Select(JgsValue.Str).ToArray());
        names.Reshape(MidiMsgs.TypeNames.Length, 1);
        return [members, names];
    }

    // --- jgraph.internal.midisim ------------------------------------------------------------------------------------

    /// <summary>
    /// <c>jgraph.internal.midisim('on')</c>: simulated MIDI devices for this session (device classes plan,
    /// stage D10b), so nothing is heard: an input and an output named "JGraph Loopback", the output's
    /// messages arriving at the input, and an output "JGraph Sink". <c>midisim('off')</c> removes them,
    /// and <c>midisim('sent')</c> answers what the outputs have sent, one row of bytes in hex a message.
    /// Test-only and undocumented.
    /// </summary>
    private static JgsValue MidiSimFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction("jgraph.internal.midisim", (args, line, col) =>
        {
            JGraphScriptGlobals host = interpreter.Host
                ?? throw new JgsRuntimeException(line, col, "JGraph:midisim:Arguments", "This session has no host.");
            string verb = args.Count == 1 && IsTextScalar(args[0]) ? TextOf(args[0]) : "";
            switch (verb)
            {
                case "on":
                    host.Devices.MidiSimulation ??= new JGraph.Devices.Simulation.SimulatedMidi();
                    return JgsValue.Null;
                case "off":
                    foreach (DeviceObject device in host.Devices.Live.Where(static d => d is MididevObject or MidicontrolsObject))
                    {
                        device.Delete();
                    }

                    host.Devices.MidiSimulation = null;
                    return JgsValue.Null;
                case "sent" when host.Devices.MidiSimulation is { } sim:
                {
                    JgsValue[] rows = sim.Sent.Select(static s => JgsValue.Str(string.Join(" ", s.Bytes.Select(static b => b.ToString("X2", CultureInfo.InvariantCulture))))).ToArray();
                    JgsValue cell = JgsValue.Cell(rows);
                    cell.Reshape(rows.Length, rows.Length == 0 ? 0 : 1);
                    return cell;
                }

                default:
                    throw new JgsRuntimeException(line, col, "JGraph:midisim:Arguments", "jgraph.internal.midisim takes 'on', 'off' or 'sent'.");
            }
        })
        {
            BindsAnsAsStatement = false,
        });
}
