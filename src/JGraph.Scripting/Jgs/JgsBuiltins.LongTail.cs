using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using JGraph.Devices;
using JGraph.Devices.Printing;
using JGraph.Devices.Simulation;
using JGraph.Devices.Usb;
using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The long tail of the device classes (plan stage D12, ADR 0196), all JGraph extensions with no
/// MATLAB counterpart: smart cards under <c>jgraph.pcsc</c>, and under <c>jgraph.usb</c> the printer
/// queues and a job sent as it is, a USB device's network adapter, and MTP and PTP devices. The
/// test-only <c>jgraph.internal.pcscsim</c>, <c>printsim</c> and <c>mtpsim</c> stand in for the
/// hardware in the fixtures.
/// </summary>
internal static partial class JgsBuiltins
{
    private static DeviceSession SessionOf(Interpreter interpreter, string package, int line, int col) =>
        (interpreter.Host ?? throw new JgsRuntimeException(line, col, $"JGraph:{package}:NoHost", "This session has no host.")).Devices;

    /// <summary>A function of the long tail: it keeps string arguments, and runs when named bare.</summary>
    private static JgsValue TailFunction(string name, Func<IReadOnlyList<JgsValue>, int, int, JgsValue> body) =>
        JgsValue.Function(new BuiltinFunction(name, body)
        {
            KeepsStringArguments = true,
            AutoCallsBare = true,
        });

    /// <summary>The functions under <c>jgraph.pcsc</c>.</summary>
    private static Dictionary<string, JgsValue> PcscPackage(Interpreter interpreter) => new(StringComparer.Ordinal)
    {
        ["readers"] = TailFunction("jgraph.pcsc.readers", (args, line, col) => PcscShared.ReadersTable(SessionOf(interpreter, "pcsc", line, col), args, line, col)),
        ["connect"] = TailFunction("jgraph.pcsc.connect", (args, line, col) => PcscCardObject.Connect(SessionOf(interpreter, "pcsc", line, col), interpreter, args, line, col)),
        ["watch"] = TailFunction("jgraph.pcsc.watch", (args, line, col) => PcscWatchObject.Start(SessionOf(interpreter, "pcsc", line, col), interpreter, args, line, col)),
        ["ctlcode"] = TailFunction("jgraph.pcsc.ctlcode", PcscShared.ControlCode),
    };

    /// <summary>The long tail's functions under <c>jgraph.usb</c>.</summary>
    private static void AddLongTail(Dictionary<string, JgsValue> usb, Interpreter interpreter)
    {
        usb["printers"] = TailFunction("jgraph.usb.printers", (args, line, col) => PrintersTable(SessionOf(interpreter, "usb", line, col), args, line, col));
        usb["printraw"] = TailFunction("jgraph.usb.printraw", (args, line, col) => PrintRaw(SessionOf(interpreter, "usb", line, col), args, line, col));
        usb["netadapter"] = UsbFunction("jgraph.usb.netadapter", (args, _, line, col) => OperatingSystem.IsWindows() ? NetAdapterTable(args, line, col) : JgsValue.Null);
        usb["mtplist"] = TailFunction("jgraph.usb.mtplist", (args, line, col) => MtpObject.List(SessionOf(interpreter, "usb", line, col), args, line, col));
        usb["mtp"] = TailFunction("jgraph.usb.mtp", (args, line, col) => MtpObject.Open(SessionOf(interpreter, "usb", line, col), interpreter, args, line, col));
    }

    // --- printers ----------------------------------------------------------------------------------------------

    /// <summary>The session's printer queues: jgraph.internal.printsim's, or the spooler's.</summary>
    private static IPrinterBackend PrinterBackend(DeviceSession session, string function, int line, int col) =>
        session.PrinterSimulation is { } simulated ? simulated
        : OperatingSystem.IsWindows() ? WinPrinters.Instance
        : throw new JgsRuntimeException(line, col, "JGraph:usb:NotSupported", $"{function} runs on Windows only.");

    [GeneratedRegex(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex UsbIdsInInstance();

    /// <summary>The vendor and product IDs an instance ID carries, as jgraph.usb.devices writes them; empty when it carries none.</summary>
    private static (string Vendor, string Product) UsbIdsOf(string instanceId) =>
        UsbIdsInInstance().Match(instanceId) is { Success: true } m ? (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant()) : ("", "");

    /// <summary>
    /// <c>T = jgraph.usb.printers</c>: one row per printer queue. A queue whose port is a USB printer
    /// has the device's IDs and the fields of the IEEE 1284 device ID it answers; the rest leave them empty.
    /// </summary>
    private static JgsValue PrintersTable(DeviceSession session, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        IReadOnlyList<PrinterInfo> printers = PrinterBackend(session, "jgraph.usb.printers", line, col).Printers();
        int n = printers.Count;
        JgsValue Text(Func<PrinterInfo, string> pick) => JgsValue.StringArray(printers.Select(p => JgsValue.Str(pick(p))).ToArray(), n, 1);
        JgsValue isDefault = n == 0 ? EmptyLogical(0, 1) : JgsValue.Array(printers.Select(static p => JgsValue.Bool(p.IsDefault)).ToArray());
        isDefault.Reshape(n, 1);
        (string Name, JgsValue Value)[] columns =
        [
            ("Name", Text(static p => p.Name)),
            ("Port", Text(static p => p.Port)),
            ("Driver", Text(static p => p.Driver)),
            ("Default", isDefault),
            ("Status", Text(static p => p.Status)),
            ("Jobs", JgsMatrix.FromColumnMajor(printers.Select(static p => (double)p.Jobs).ToArray(), n, 1)),
            ("VendorID", Text(static p => UsbIdsOf(p.UsbInstanceId).Vendor)),
            ("ProductID", Text(static p => UsbIdsOf(p.UsbInstanceId).Product)),
            ("Manufacturer", Text(static p => p.Manufacturer)),
            ("Model", Text(static p => p.Model)),
            ("CommandSet", Text(static p => p.CommandSet)),
            ("DeviceID", Text(static p => p.DeviceId)),
            ("InstanceID", Text(static p => p.UsbInstanceId)),
        ];
        return JgsValue.Table(new JGraph.Data.Table(columns.Select(c => TableColumnFrom("jgraph.usb.printers", c.Name, c.Value, line, col)).ToList()));
    }

    private static readonly string[] PrintRawOptions = ["DocumentName", "OutputFile"];

    /// <summary>
    /// <c>job = jgraph.usb.printraw(printer, data, DocumentName=, OutputFile=)</c>: sends bytes to a
    /// queue as one job the driver does not touch (ESC/POS, ZPL, PCL), and answers the job's number.
    /// The printer is a queue's name or one row of jgraph.usb.printers; the data is bytes, or text
    /// whose characters are the bytes. With OutputFile the spooler writes the job to that file.
    /// </summary>
    private static JgsValue PrintRaw(DeviceSession session, IReadOnlyList<JgsValue> args, int line, int col)
    {
        const string Syntax = "Valid syntax is JOB = jgraph.usb.printraw(PRINTER, DATA, Name=Value) with DocumentName and OutputFile.";
        if (args.Count < 2 || args.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:Nargin", Syntax);
        }

        IPrinterBackend backend = PrinterBackend(session, "jgraph.usb.printraw", line, col);
        string asked;
        if (args[0].Type == JgsType.Table)
        {
            JgsValue names = TableColumnValue(args[0].AsTable, "Name", line, col);
            if (!names.IsStringArray || names.ArrayLength != 1)
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:OneRow", $"jgraph.usb.printraw takes one row of jgraph.usb.printers; this table has {names.ArrayLength}.");
            }

            asked = names.ElementAt(0).AsString;
        }
        else if (IsTextScalar(args[0]))
        {
            asked = TextOf(args[0]);
        }
        else
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:Nargin", Syntax);
        }

        IReadOnlyList<PrinterInfo> printers = backend.Printers();
        PrinterInfo printer = printers.FirstOrDefault(p => p.Name.Equals(asked, StringComparison.OrdinalIgnoreCase))
            ?? throw new JgsRuntimeException(line, col, "JGraph:usb:NoPrinter",
                printers.Count == 0 ? $"No printer is named '{asked}': this machine has no printer queues."
                : $"No printer is named '{asked}'. The printers are: {string.Join(", ", printers.Select(static p => $"'{p.Name}'"))}.");

        byte[] bytes = PrintBytes(args[1], line, col);
        string document = "JGraph raw job";
        string? output = null;
        for (int i = 2; i < args.Count; i += 2)
        {
            string written = IsTextScalar(args[i]) ? TextOf(args[i]) : "";
            if (!DeviceChecks.Match(written, PrintRawOptions, out string? name, out bool ambiguous) || ambiguous)
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:NameValue", $"'{written}' is not a name jgraph.usb.printraw takes; the names are DocumentName and OutputFile.");
            }

            if (!IsTextScalar(args[i + 1]) || TextOf(args[i + 1]).Length == 0)
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:BadText", $"{name} is text.");
            }

            if (name == "DocumentName")
            {
                document = TextOf(args[i + 1]);
            }
            else
            {
                output = session.Host.ResolveForWrite(TextOf(args[i + 1]));
                if (Path.GetDirectoryName(output) is { Length: > 0 } parent && !Directory.Exists(parent))
                {
                    throw new JgsRuntimeException(line, col, "JGraph:usb:PrintFailed", $"The job cannot be written to {output}: the folder {parent} does not exist.");
                }
            }
        }

        try
        {
            return JgsValue.Number(backend.PrintRaw(printer.Name, bytes, document, output));
        }
        catch (Exception e) when (e is DeviceIOException or DeviceOpenException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:PrintFailed", e.Message);
        }
    }

    /// <summary>A job's bytes: a numeric array of 0 to 255, or text whose characters are each one byte.</summary>
    private static byte[] PrintBytes(JgsValue value, int line, int col)
    {
        if (IsTextScalar(value))
        {
            string text = TextOf(value);
            if (text.Any(static c => c > 255))
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:InvalidBytes",
                    "Text sent as it is has characters up to 255, one byte each; unicode2native(text, encoding) makes the bytes of other text.");
            }

            return text.Select(static c => (byte)c).ToArray();
        }

        if (!DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)))
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:InvalidBytes", "The data is bytes (integers from 0 to 255) or text.");
        }

        double[] numbers = DeviceChecks.Numbers(value).ToArray();
        if (Array.Exists(numbers, static x => x < 0 || x > 255 || x != Math.Floor(x)))
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:InvalidBytes", "The data is bytes (integers from 0 to 255) or text.");
        }

        return Array.ConvertAll(numbers, static x => (byte)x);
    }

    // --- network adapters --------------------------------------------------------------------------------------

    /// <summary>
    /// <c>T = jgraph.usb.netadapter</c>: the network adapters that are USB devices (CDC ECM, NCM, EEM,
    /// RNDIS), each with its connection name, addresses and link. <c>jgraph.usb.netadapter(dev)</c>, with
    /// a row of jgraph.usb.devices, an InstanceID or its filters, keeps those of the devices named.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue NetAdapterTable(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Dictionary<string, UsbDeviceInfo> usb = UsbEnumerator.Devices().ToDictionary(static d => d.InstanceId, StringComparer.OrdinalIgnoreCase);
        Func<NetworkAdapterInfo, bool> keep;
        if (args.Count == 0)
        {
            keep = static _ => true;
        }
        else if (args.Count == 1)
        {
            string instance = OneUsbDevice("jgraph.usb.netadapter", args, line, col).InstanceId;
            keep = a => a.UsbInstanceId.Equals(instance, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Func<UsbDeviceInfo, bool> filter = UsbFilter("jgraph.usb.netadapter", args, 0, line, col);
            keep = a => usb.TryGetValue(a.UsbInstanceId, out UsbDeviceInfo? device) && filter(device);
        }

        List<NetworkAdapterInfo> adapters = UsbNetworkAdapters.List().Where(keep).ToList();
        int n = adapters.Count;
        JgsValue Text(Func<NetworkAdapterInfo, string> pick) => JgsValue.StringArray(adapters.Select(a => JgsValue.Str(pick(a))).ToArray(), n, 1);
        (string Name, JgsValue Value)[] columns =
        [
            ("Name", Text(static a => a.Name)),
            ("Description", Text(static a => a.Description)),
            ("Kind", Text(a => usb.TryGetValue(a.UsbInstanceId, out UsbDeviceInfo? device) ? UsbNetworkAdapters.Kind(device.ConfigurationDescriptor) : "")),
            ("MACAddress", Text(static a => a.MacAddress)),
            ("IPv4", Text(static a => string.Join(", ", a.IPv4))),
            ("IPv6", Text(static a => string.Join(", ", a.IPv6))),
            ("Status", Text(static a => a.Status)),
            ("Speed", JgsMatrix.FromColumnMajor(adapters.Select(static a => (double)a.Speed).ToArray(), n, 1)),
            ("VendorID", Text(static a => UsbIdsOf(a.UsbInstanceId).Vendor)),
            ("ProductID", Text(static a => UsbIdsOf(a.UsbInstanceId).Product)),
            ("InstanceID", Text(static a => a.UsbInstanceId)),
        ];
        return JgsValue.Table(new JGraph.Data.Table(columns.Select(c => TableColumnFrom("jgraph.usb.netadapter", c.Name, c.Value, line, col)).ToList()));
    }

    // --- the simulators ----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>jgraph.internal.pcscsim('on')</c> replaces the machine's smart-card readers with
    /// <see cref="SimulatedSmartCards"/> for the session and <c>'off'</c> puts them back, deleting the
    /// card and watch objects first; <c>('insert', reader)</c> and <c>('remove')</c> move the card,
    /// <c>('unplug', reader)</c> and <c>('plug', reader)</c> a reader, and <c>('open')</c> counts the
    /// connections. Test-only and undocumented.
    /// </summary>
    private static JgsValue PcscSimFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction("jgraph.internal.pcscsim", (args, line, col) =>
        {
            DeviceSession session = SessionOf(interpreter, "pcscsim", line, col);
            string verb = args.Count >= 1 && IsTextScalar(args[0]) ? TextOf(args[0]) : "";
            string? name = args.Count == 2 && IsTextScalar(args[1]) ? TextOf(args[1]) : null;
            try
            {
                switch (verb)
                {
                    case "on" when args.Count == 1:
                        session.SmartCardSimulation ??= new SimulatedSmartCards();
                        return JgsValue.Null;
                    case "off" when args.Count == 1:
                        foreach (DeviceObject device in session.Live.Where(static d => d is PcscCardObject or PcscWatchObject))
                        {
                            device.Delete();
                        }

                        session.SmartCardSimulation = null;
                        return JgsValue.Null;
                    case "insert" when name is not null && session.SmartCardSimulation is { } sim:
                        sim.Insert(name);
                        return JgsValue.Null;
                    case "remove" when args.Count == 1 && session.SmartCardSimulation is { } sim:
                        sim.Remove();
                        return JgsValue.Null;
                    case "unplug" when name is not null && session.SmartCardSimulation is { } sim:
                        sim.Unplug(name);
                        return JgsValue.Null;
                    case "plug" when name is not null && session.SmartCardSimulation is { } sim:
                        sim.Plug(name);
                        return JgsValue.Null;
                    case "open" when args.Count == 1 && session.SmartCardSimulation is { } sim:
                        return JgsValue.Number(sim.OpenCount);
                }
            }
            catch (ArgumentException e)
            {
                throw new JgsRuntimeException(line, col, "JGraph:pcscsim:Arguments", e.Message);
            }

            throw new JgsRuntimeException(line, col, "JGraph:pcscsim:Arguments",
                "jgraph.internal.pcscsim takes 'on', 'off', 'open', 'remove', or 'insert', 'unplug' or 'plug' and a simulated reader's name.");
        })
        {
            BindsAnsAsStatement = false,
        });

    /// <summary>
    /// <c>jgraph.internal.printsim('on')</c> replaces the machine's printer queues with
    /// <see cref="SimulatedPrinters"/> for the session and <c>'off'</c> puts them back;
    /// <c>('jobs')</c> counts the jobs taken, <c>('job', n)</c> answers one as a struct, and
    /// <c>('offline', printer, tf)</c> takes a queue offline or back. Test-only and undocumented.
    /// </summary>
    private static JgsValue PrintSimFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction("jgraph.internal.printsim", (args, line, col) =>
        {
            DeviceSession session = SessionOf(interpreter, "printsim", line, col);
            string verb = args.Count >= 1 && IsTextScalar(args[0]) ? TextOf(args[0]) : "";
            switch (verb)
            {
                case "on" when args.Count == 1:
                    session.PrinterSimulation ??= new SimulatedPrinters();
                    return JgsValue.Null;
                case "off" when args.Count == 1:
                    session.PrinterSimulation = null;
                    return JgsValue.Null;
                case "jobs" when args.Count == 1 && session.PrinterSimulation is { } sim:
                    return JgsValue.Number(sim.Jobs.Count);
                case "job" when args.Count == 2 && session.PrinterSimulation is { } sim
                    && DeviceChecks.Numbers(args[1]).FirstOrDefault(double.NaN) is var k && k >= 1 && k <= sim.Jobs.Count && k == Math.Floor(k):
                {
                    SimulatedPrintJob job = sim.Jobs[(int)k - 1];
                    return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
                    {
                        ["Printer"] = JgsValue.StringScalar(job.Printer),
                        ["DocumentName"] = JgsValue.StringScalar(job.DocumentName),
                        ["Data"] = PcscShared.Bytes(job.Data),
                        ["OutputFile"] = JgsValue.StringScalar(job.OutputFile ?? ""),
                    });
                }

                case "offline" when args.Count == 3 && IsTextScalar(args[1]) && session.PrinterSimulation is { } sim:
                    sim.SetOffline(TextOf(args[1]), DeviceChecks.Numbers(args[2]).FirstOrDefault() != 0);
                    return JgsValue.Null;
                default:
                    throw new JgsRuntimeException(line, col, "JGraph:printsim:Arguments",
                        "jgraph.internal.printsim takes 'on', 'off', 'jobs', 'job' and a number, or 'offline', a simulated printer's name and true or false.");
            }
        })
        {
            BindsAnsAsStatement = false,
        });

    /// <summary>
    /// <c>jgraph.internal.mtpsim('on')</c> replaces the machine's portable devices with
    /// <see cref="SimulatedMtp"/> for the session and <c>'off'</c> puts them back, deleting the Mtp
    /// objects first; <c>('unplug', name)</c> and <c>('plug', name)</c> take a simulated device away
    /// and bring it back, and <c>('open')</c> counts the devices held open. Test-only and undocumented.
    /// </summary>
    private static JgsValue MtpSimFunction(Interpreter interpreter) =>
        JgsValue.Function(new BuiltinFunction("jgraph.internal.mtpsim", (args, line, col) =>
        {
            DeviceSession session = SessionOf(interpreter, "mtpsim", line, col);
            string verb = args.Count >= 1 && IsTextScalar(args[0]) ? TextOf(args[0]) : "";
            string? name = args.Count == 2 && IsTextScalar(args[1]) ? TextOf(args[1]) : null;
            switch (verb)
            {
                case "on" when args.Count == 1:
                    session.MtpSimulation ??= new SimulatedMtp();
                    return JgsValue.Null;
                case "off" when args.Count == 1:
                    foreach (DeviceObject device in session.Live.Where(static d => d is MtpObject))
                    {
                        device.Delete();
                    }

                    session.MtpSimulation = null;
                    return JgsValue.Null;
                case "unplug" when name is not null && session.MtpSimulation is { } sim:
                    sim.Unplug(name);
                    return JgsValue.Null;
                case "plug" when name is not null && session.MtpSimulation is { } sim:
                    sim.Plug(name);
                    return JgsValue.Null;
                case "open" when args.Count == 1 && session.MtpSimulation is { } sim:
                    return JgsValue.Number(sim.OpenCount);
                default:
                    throw new JgsRuntimeException(line, col, "JGraph:mtpsim:Arguments",
                        "jgraph.internal.mtpsim takes 'on', 'off', 'open', or 'unplug' or 'plug' and a simulated device's name.");
            }
        })
        {
            BindsAnsAsStatement = false,
        });
}
