using System.Runtime.Versioning;
using JGraph.Devices;
using JGraph.Devices.Hid;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>h = jgraph.usb.hid(...)</c> (device classes plan, stage D6, ADR 0189): a HID collection opened for its
/// reports. Input reports queue on the reader thread; <c>read</c> takes one (waiting up to Timeout),
/// <c>write</c> sends an output report, <c>getFeature</c>/<c>setFeature</c>/<c>getInput</c> go through the
/// control pipe; <c>caps</c> answers the collection's capabilities as a table;
/// <c>configureCallback(h, "report", fcn)</c> runs <c>fcn(h, evt)</c> per report at the device queue's drain
/// points. Reports are uint8 rows with the report ID first, as hidapi gives them.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class HidObject : DeviceObject
{
    private static readonly DeviceClass Declaration = Declare();

    private readonly HidDevice _device;
    private readonly DeviceEventQueue _queue;
    private JgsValue _timeout = JgsValue.Number(1);
    private JgsValue? _callback;
    private JgsValue _userData = JgsEmpty.Zero();
    private string _tag = "";

    private HidObject(DeviceSession session, Interpreter interpreter, HidDevice device)
        : base(session, interpreter)
    {
        _device = device;
        _queue = DeviceEventQueue.ForCurrentThread();
        device.ReportReceived += waiting =>
        {
            if (_callback is not null)
            {
                _queue.Post(FireReport);
            }
        };
    }

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "deleted" : $"{_device.Info.VendorId:X4}:{_device.Info.ProductId:X4} {_device.Info.Product}";

    /// <summary><c>jgraph.usb.hid(Name=Value…)</c>: VendorID, ProductID, UsagePage, Usage, SerialNumber, Path, InputBuffers.</summary>
    public static JgsValue Open(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NameValue", "jgraph.usb.hid takes name-value pairs: VendorID, ProductID, UsagePage, Usage, SerialNumber, Path, InputBuffers.");
        }

        IEnumerable<HidInfo> candidates = HidDevice.List();
        int buffers = 0;
        for (int i = 0; i < args.Count; i += 2)
        {
            string written = JgsBuiltins.IsTextScalar(args[i]) ? JgsBuiltins.TextOf(args[i]) : "";
            if (written.Length == 0 || !DeviceChecks.Match(written, ["VendorID", "ProductID", "UsagePage", "Usage", "SerialNumber", "Path", "InputBuffers"], out string? name, out bool ambiguous) || ambiguous)
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:NameValue",
                    $"'{written}' is not an option of jgraph.usb.hid. The options are VendorID, ProductID, UsagePage, Usage, SerialNumber, Path and InputBuffers.");
            }

            JgsValue value = args[i + 1];
            switch (name)
            {
                case "VendorID":
                {
                    int id = JgsBuiltins.UsbIdOf(value, line, col);
                    candidates = candidates.Where(h => h.VendorId == id);
                    break;
                }

                case "ProductID":
                {
                    int id = JgsBuiltins.UsbIdOf(value, line, col);
                    candidates = candidates.Where(h => h.ProductId == id);
                    break;
                }

                case "UsagePage":
                {
                    int page = UsageOf(value, "UsagePage", line, col);
                    candidates = candidates.Where(h => h.UsagePage == page);
                    break;
                }

                case "Usage":
                {
                    int usage = UsageOf(value, "Usage", line, col);
                    candidates = candidates.Where(h => h.Usage == usage);
                    break;
                }

                case "SerialNumber":
                {
                    string serial = TextOption(value, "SerialNumber", line, col);
                    candidates = candidates.Where(h => h.SerialNumber.Equals(serial, StringComparison.OrdinalIgnoreCase));
                    break;
                }

                case "Path":
                {
                    string path = TextOption(value, "Path", line, col);
                    candidates = candidates.Where(h => h.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                    break;
                }

                default:
                    if (!DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)) || DeviceChecks.Count(value) != 1)
                    {
                        throw new JgsRuntimeException(line, col, "JGraph:usb:InvalidBuffers", "InputBuffers is a number from 2 to 512.");
                    }

                    buffers = (int)DeviceChecks.Numbers(value).First();
                    break;
            }
        }

        List<HidInfo> found = candidates.ToList();
        if (found.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NoDevice", "jgraph.usb.hid: no HID collection matches; jgraph.usb.hidlist lists them.");
        }

        if (found.Count > 1 && found.Select(static h => h.Path).Distinct().Count() > 1)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:SeveralDevices",
                $"jgraph.usb.hid: {found.Count} HID collections match; add UsagePage and Usage, a SerialNumber, or the Path from jgraph.usb.hidlist.");
        }

        if (found[0].SystemOwned)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:SystemOwned",
                "Windows keeps keyboards and mice for itself: their input reports cannot be read by a program.");
        }

        HidDevice device;
        try
        {
            device = HidDevice.Open(found[0].Path, buffers);
        }
        catch (DeviceOpenException e)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:OpenFailed", e.Message);
        }

        var hid = new HidObject(session, interpreter, device);
        session.Remember(hid);
        JgsValue made = JgsValue.External(hid);
        JgsLifetime.Minted(made);
        return made;
    }

    /// <summary>A usage page or usage: a number from 0 to 65535, or hex text ("FF00", "0xFF00").</summary>
    internal static int UsageOf(JgsValue value, string name, int line, int col)
    {
        try
        {
            return JgsBuiltins.UsbIdOf(value, line, col);
        }
        catch (JgsRuntimeException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:BadUsage", $"{name} is a number from 0 to 65535, or hex text (\"FF00\", \"0xFF00\").");
        }
    }

    private static string TextOption(JgsValue value, string name, int line, int col) =>
        JgsBuiltins.IsTextScalar(value) ? JgsBuiltins.TextOf(value) : throw new JgsRuntimeException(line, col, "JGraph:usb:BadText", $"{name} is text.");

    /// <summary>
    /// <c>T = jgraph.usb.hidlist(Name=Value...)</c>: every HID collection, one row each, with the USB device it
    /// belongs to (empty for a Bluetooth or I2C one); keyboards and mice marked as Windows' own.
    /// </summary>
    public static JgsValue List(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NameValue", "jgraph.usb.hidlist takes name-value pairs: VendorID, ProductID, UsagePage, Usage, SerialNumber.");
        }

        IEnumerable<HidInfo> rows = HidDevice.List();
        for (int i = 0; i < args.Count; i += 2)
        {
            string written = JgsBuiltins.IsTextScalar(args[i]) ? JgsBuiltins.TextOf(args[i]) : "";
            if (written.Length == 0 || !DeviceChecks.Match(written, ["VendorID", "ProductID", "UsagePage", "Usage", "SerialNumber"], out string? name, out bool ambiguous) || ambiguous)
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:NameValue",
                    $"'{written}' is not a name jgraph.usb.hidlist takes; the names are VendorID, ProductID, UsagePage, Usage and SerialNumber.");
            }

            JgsValue value = args[i + 1];
            rows = name switch
            {
                "VendorID" => Keep(rows, JgsBuiltins.UsbIdOf(value, line, col), static h => h.VendorId),
                "ProductID" => Keep(rows, JgsBuiltins.UsbIdOf(value, line, col), static h => h.ProductId),
                "UsagePage" => Keep(rows, UsageOf(value, "UsagePage", line, col), static h => h.UsagePage),
                "Usage" => Keep(rows, UsageOf(value, "Usage", line, col), static h => h.Usage),
                _ => KeepSerial(rows, TextOption(value, "SerialNumber", line, col)),
            };
        }

        List<HidInfo> list = rows.ToList();
        int n = list.Count;
        JgsValue Text(Func<HidInfo, string> pick) => JgsValue.StringArray(list.Select(h => JgsValue.Str(pick(h))).ToArray(), n, 1);
        JgsValue Number(Func<HidInfo, double> pick) => JgsMatrix.FromColumnMajor(list.Select(pick).ToArray(), n, 1);
        JgsValue owned = n == 0 ? JgsBuiltins.EmptyLogical(0, 1) : JgsValue.Array(list.Select(static h => JgsValue.Bool(h.SystemOwned)).ToArray());
        owned.Reshape(n, 1);
        (string Name, JgsValue Value)[] columns =
        [
            ("VendorID", Text(static h => JgsBuiltins.UsbHex(h.VendorId))),
            ("ProductID", Text(static h => JgsBuiltins.UsbHex(h.ProductId))),
            ("Manufacturer", Text(static h => h.Manufacturer)),
            ("Product", Text(static h => h.Product)),
            ("SerialNumber", Text(static h => h.SerialNumber)),
            ("UsagePage", Number(static h => h.UsagePage)),
            ("Usage", Number(static h => h.Usage)),
            ("InputReportLength", Number(static h => h.InputReportLength)),
            ("OutputReportLength", Number(static h => h.OutputReportLength)),
            ("FeatureReportLength", Number(static h => h.FeatureReportLength)),
            ("SystemOwned", owned),
            ("USBInstanceID", Text(static h => h.UsbInstanceId)),
            ("Path", Text(static h => h.Path)),
        ];
        return JgsValue.Table(new JGraph.Data.Table(columns.Select(c => JgsBuiltins.TableColumnFrom("jgraph.usb.hidlist", c.Name, c.Value, line, col)).ToList()));
    }

    private static IEnumerable<HidInfo> Keep(IEnumerable<HidInfo> rows, int want, Func<HidInfo, int> field) =>
        rows.Where(h => field(h) == want).ToList();

    private static IEnumerable<HidInfo> KeepSerial(IEnumerable<HidInfo> rows, string serial) =>
        rows.Where(h => h.SerialNumber.Equals(serial, StringComparison.OrdinalIgnoreCase)).ToList();

    private static HidObject Me(DeviceObject o) => (HidObject)o;

    private static DeviceClass Declare()
    {
        var properties = new List<DeviceProperty>
        {
            new("VendorID", static (o, _) => JgsValue.StringScalar(JgsBuiltins.UsbHex(Me(o)._device.Info.VendorId))),
            new("ProductID", static (o, _) => JgsValue.StringScalar(JgsBuiltins.UsbHex(Me(o)._device.Info.ProductId))),
            new("Manufacturer", static (o, _) => JgsValue.StringScalar(Me(o)._device.Info.Manufacturer)),
            new("Product", static (o, _) => JgsValue.StringScalar(Me(o)._device.Info.Product)),
            new("SerialNumber", static (o, _) => JgsValue.StringScalar(Me(o)._device.Info.SerialNumber)),
            new("UsagePage", static (o, _) => JgsValue.Number(Me(o)._device.Info.UsagePage)),
            new("Usage", static (o, _) => JgsValue.Number(Me(o)._device.Info.Usage)),
            new("InputReportLength", static (o, _) => JgsValue.Number(Me(o)._device.Info.InputReportLength)),
            new("OutputReportLength", static (o, _) => JgsValue.Number(Me(o)._device.Info.OutputReportLength)),
            new("FeatureReportLength", static (o, _) => JgsValue.Number(Me(o)._device.Info.FeatureReportLength)),
            new("NumReportsAvailable", static (o, _) => JgsValue.Number(Me(o)._device.ReportsWaiting)),
            new("NumInputBuffers", static (o, _) => JgsValue.Number(Me(o)._device.InputBuffers), static (o, v, c) => Me(o).SetBuffers(v, c)),
            new("Timeout", static (o, _) => Me(o)._timeout, static (o, v, c) => Me(o).SetTimeout(v, c)),
            new("ReportFcn", static (o, _) => Me(o)._callback ?? JgsEmpty.Zero(),
                static (_, _, c) => throw c.Error("JGraph:usb:ReadOnlyProperty", "To set \"ReportFcn\", use the \"configureCallback\" function.")),
            new("Path", static (o, _) => JgsValue.StringScalar(Me(o)._device.Info.Path)),
            new("UserData", static (o, _) => Me(o)._userData, static (o, v, _) => Me(o)._userData = v),
            new("Tag", static (o, _) => JgsValue.StringScalar(Me(o)._tag), static (o, v, c) => Me(o)._tag = DeviceChecks.IsText(v) ? DeviceChecks.Text(v)
                : throw c.Error("JGraph:usb:InvalidTag", "Tag must be text.")),
        };

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["read"] = static c => [Me(c.Target).Read(c)],
            ["write"] = static c => Void(c, static c => Me(c.Target).Write(c)),
            ["getFeature"] = static c => [Me(c.Target).Get(c, feature: true)],
            ["setFeature"] = static c => Void(c, static c => Me(c.Target).Set(c, feature: true)),
            ["getInput"] = static c => [Me(c.Target).Get(c, feature: false)],
            ["setOutput"] = static c => Void(c, static c => Me(c.Target).Set(c, feature: false)),
            ["caps"] = static c => [Me(c.Target).Caps(c)],
            ["getValue"] = static c => [Me(c.Target).GetValue(c)],
            ["getButtons"] = static c => [Me(c.Target).GetButtons(c)],
            ["configureCallback"] = static c => Void(c, static c => Me(c.Target).ConfigureCallback(c)),
            ["flush"] = static c => Void(c, static c => Me(c.Target).Live(c)._device.Flush()),
        };

        return new DeviceClass("jgraph.usb.HidDevice", "HidDevice", ["handle"], properties, methods,
            ["HidDevice", "caps", "configureCallback", "delete", "flush", "get", "getButtons", "getFeature", "getInput", "getValue",
             "isvalid", "read", "set", "setFeature", "setOutput", "write"],
            ["VendorID", "ProductID", "Product", "UsagePage", "Usage", "NumReportsAvailable"]);
    }

    private static JgsValue[] Void(DeviceCall call, Action<DeviceCall> body)
    {
        if (call.Wanted > 0)
        {
            throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        body(call);
        return [];
    }

    private HidObject Live(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (!_device.Connected)
        {
            throw call.Error("JGraph:usb:ConnectionLost", "The HID device is no longer connected. Plug it in and open it again.");
        }

        return this;
    }

    private double TimeoutSeconds => DeviceChecks.Numbers(_timeout).First();

    private void SetTimeout(JgsValue value, DeviceCall call)
    {
        var who = new DeviceChecks.Subject(null, "Timeout");
        DeviceChecks.Classes(value, ["numeric"], who, call.Line, call.Column);
        DeviceChecks.Attributes(value, ["scalar", "nonnegative"], who, call.Line, call.Column);
        _timeout = value;
    }

    private void SetBuffers(JgsValue value, DeviceCall call)
    {
        try
        {
            _device.InputBuffers = (int)DeviceChecks.Numbers(value).First();
        }
        catch (DeviceSettingsException e)
        {
            throw call.Error("JGraph:usb:InvalidBuffers", e.Message);
        }
    }

    /// <summary><c>report = read(h)</c>, <c>read(h, timeout)</c>: the oldest input report, or [] and a warning when none came.</summary>
    private JgsValue Read(DeviceCall call)
    {
        Live(call);
        double seconds = call.Args.Count >= 1 ? DeviceChecks.Numbers(call.Args[0]).First() : TimeoutSeconds;
        _device.Signal.WaitUntil(() => _device.ReportsWaiting > 0, TimeSpan.FromSeconds(Math.Max(0, seconds)), Interpreter.Cancellation);
        byte[]? report = _device.TakeReport();
        if (report is null)
        {
            JgsBuiltins.Warn(call.Host, "JGraph:usb:ReadWarning", "No input report arrived within the Timeout period for 'read'.");
            return JgsEmpty.Zero();
        }

        return Bytes(report);
    }

    private void Write(DeviceCall call)
    {
        Live(call);
        if (call.Args.Count != 1)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntax is write(h, REPORT), the report ID first.");
        }

        try
        {
            _device.Write(ByteArgument(call.Args[0], call), TimeSpan.FromSeconds(Math.Max(0.001, TimeoutSeconds)));
        }
        catch (Exception e) when (e is DeviceIOException or TimeoutException)
        {
            throw call.Error("JGraph:usb:WriteFailed", e.Message);
        }
    }

    private JgsValue Get(DeviceCall call, bool feature)
    {
        Live(call);
        int id = call.Args.Count >= 1 ? (int)DeviceChecks.Numbers(call.Args[0]).First() : 0;
        try
        {
            return Bytes(feature ? _device.GetFeature(id) : _device.GetInput(id));
        }
        catch (DeviceIOException e)
        {
            throw call.Error("JGraph:usb:ReportFailed", e.Message);
        }
    }

    private void Set(DeviceCall call, bool feature)
    {
        Live(call);
        if (call.Args.Count != 1)
        {
            throw call.Error("JGraph:usb:Nargin", $"Valid syntax is {(feature ? "setFeature" : "setOutput")}(h, REPORT), the report ID first.");
        }

        try
        {
            if (feature)
            {
                _device.SetFeature(ByteArgument(call.Args[0], call));
            }
            else
            {
                _device.SetOutput(ByteArgument(call.Args[0], call));
            }
        }
        catch (DeviceIOException e)
        {
            throw call.Error("JGraph:usb:ReportFailed", e.Message);
        }
    }

    /// <summary><c>T = caps(h)</c>: one row per button or value capability.</summary>
    private JgsValue Caps(DeviceCall call)
    {
        Live(call);
        IReadOnlyList<HidCapability> caps = _device.Capabilities();
        JgsValue Column(Func<HidCapability, double> pick) => JgsMatrix.FromColumnMajor(caps.Select(pick).ToArray(), caps.Count, 1);
        JgsValue Text(Func<HidCapability, string> pick)
        {
            JgsValue column = JgsValue.StringArray(caps.Select(pick).Select(JgsValue.Str).ToArray());
            column.Reshape(caps.Count, caps.Count == 0 ? 0 : 1);
            return column;
        }

        (string, JgsValue)[] columns =
        [
            ("ReportType", Text(static c => c.ReportType)),
            ("Kind", Text(static c => c.IsButton ? "button" : "value")),
            ("ReportID", Column(static c => c.ReportId)),
            ("UsagePage", Column(static c => c.UsagePage)),
            ("UsageMin", Column(static c => c.UsageMin)),
            ("UsageMax", Column(static c => c.UsageMax)),
            ("LogicalMin", Column(static c => c.LogicalMin)),
            ("LogicalMax", Column(static c => c.LogicalMax)),
            ("PhysicalMin", Column(static c => c.PhysicalMin)),
            ("PhysicalMax", Column(static c => c.PhysicalMax)),
            ("BitSize", Column(static c => c.BitSize)),
            ("ReportCount", Column(static c => c.ReportCount)),
            ("Units", Column(static c => c.Units)),
        ];
        return JgsValue.Table(new JGraph.Data.Table(columns.Select(c => JgsBuiltins.TableColumnFrom("caps", c.Item1, c.Item2, call.Line, call.Column)).ToList()));
    }

    /// <summary><c>v = getValue(h, report, usagePage, usage)</c> (input reports; "scaled" for the physical value).</summary>
    private JgsValue GetValue(DeviceCall call)
    {
        Live(call);
        if (call.Args.Count is < 3 or > 4)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntax is getValue(h, REPORT, USAGEPAGE, USAGE) or getValue(h, REPORT, USAGEPAGE, USAGE, \"scaled\").");
        }

        byte[] report = ByteArgument(call.Args[0], call);
        bool scaled = call.Args.Count == 4 && DeviceChecks.IsText(call.Args[3]) && DeviceChecks.Text(call.Args[3]) == "scaled";
        long? value = _device.GetValue(report, "input", (int)call.Args[1].AsNumber, (int)call.Args[2].AsNumber, scaled);
        return value is { } v ? JgsValue.Number(v) : JgsEmpty.Zero();
    }

    /// <summary><c>b = getButtons(h, report, usagePage)</c>: the usages of the buttons pressed.</summary>
    private JgsValue GetButtons(DeviceCall call)
    {
        Live(call);
        if (call.Args.Count != 2)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntax is getButtons(h, REPORT, USAGEPAGE).");
        }

        int[] buttons = _device.GetButtons(ByteArgument(call.Args[0], call), "input", (int)call.Args[1].AsNumber);
        return TransportClient.Row(buttons.Select(static b => (double)b).ToArray());
    }

    /// <summary><c>configureCallback(h, "report", fcn)</c> and <c>(h, "off")</c>.</summary>
    private void ConfigureCallback(DeviceCall call)
    {
        Live(call);
        string mode = call.Args.Count >= 1 && DeviceChecks.IsText(call.Args[0]) ? DeviceChecks.Text(call.Args[0]) : "";
        if (mode.Equals("off", StringComparison.OrdinalIgnoreCase) && call.Args.Count == 1)
        {
            _callback = null;
            return;
        }

        if (!mode.Equals("report", StringComparison.OrdinalIgnoreCase) || call.Args.Count != 2 || call.Args[1].Type != JgsType.Function)
        {
            throw call.Error("JGraph:usb:ConfigureCallback", "Valid syntaxes are configureCallback(h,\"off\") and configureCallback(h,\"report\",@callbackFcn).");
        }

        _callback = call.Args[1];
    }

    /// <summary>Runs the report callback for the oldest waiting report, on the script thread.</summary>
    private void FireReport()
    {
        if (Deleted || _callback is not { } callback)
        {
            return;
        }

        byte[]? report = _device.TakeReport();
        if (report is null)
        {
            return;
        }

        JgsValue evt = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Report"] = Bytes(report),
            ["ReportID"] = JgsValue.Number(report.Length > 0 ? report[0] : 0),
            ["AbsTime"] = JgsBuiltins.DatetimeValue(DateTime.Now),
        });
        try
        {
            JgsCallbacks.Invoke(callback.AsCallable, [JgsValue.External(this), evt], 0, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JgsException failure)
        {
            JgsBuiltins.Warn(Session.Host, "JGraph:usb:CallbackError", "Error executing the ReportFcn callback:\n" + failure.Message);
        }
    }

    /// <summary>A uint8 row.</summary>
    internal static JgsValue Bytes(byte[] bytes) =>
        TransportClient.TypedRow(bytes.Select(static b => (double)b).ToArray(), Precision.UInt8);

    /// <summary>Bytes from a numeric row (0–255) or text.</summary>
    internal static byte[] ByteArgument(JgsValue value, DeviceCall call)
    {
        double[] numbers = DeviceChecks.Numbers(TransportClient.Str2Char(value)).ToArray();
        if (numbers.Any(static x => x < 0 || x > 255 || x != Math.Floor(x)))
        {
            throw call.Error("JGraph:usb:InvalidBytes", "A report is bytes: integers from 0 to 255.");
        }

        return numbers.Select(static x => (byte)x).ToArray();
    }

    protected override void OnDelete() => _device.Dispose();
}
