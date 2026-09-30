using System.Runtime.Versioning;
using JGraph.Devices;
using JGraph.Devices.Usb;
using JGraph.Devices.WinUsb;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>u = jgraph.usb.device(...)</c> (device classes plan, stage D7, ADR 0190): a vendor-specific (or any
/// WinUSB-bound) device. <c>control</c> for the default pipe; <c>read</c>/<c>write</c> for bulk and
/// interrupt pipes with the transport client's precisions; <c>clearHalt</c>, <c>resetPipe</c>,
/// <c>abortPipe</c>, <c>setPolicy</c>; alternate settings; <c>configureCallback(u, endpoint, count, fcn)</c>
/// for a continuous read. A device bound to another driver is refused with the drivers' names and how
/// to bind WinUSB.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UsbDeviceObject : DeviceObject
{
    private static readonly DeviceClass Declaration = Declare();

    private readonly WinUsbDevice _device;
    private readonly UsbDeviceInfo _info;
    private readonly DeviceEventQueue _queue;
    private JgsValue? _callback;
    private int _callbackEndpoint;
    private JgsValue _timeout = JgsValue.Number(1);
    private JgsValue _userData = JgsEmpty.Zero();
    private string _byteOrder = "little-endian";

    private UsbDeviceObject(DeviceSession session, Interpreter interpreter, WinUsbDevice device, UsbDeviceInfo info)
        : base(session, interpreter)
    {
        _device = device;
        _info = info;
        _queue = DeviceEventQueue.ForCurrentThread();
    }

    public override DeviceClass Class => Declaration;

    internal WinUsbDevice Device => _device;

    internal UsbDeviceInfo Info => _info;

    public override string? Summary() => Deleted ? "deleted" : $"{_info.VendorId:X4}:{_info.ProductId:X4} {(_info.Product.Length > 0 ? _info.Product : _info.Description)}";

    /// <summary>
    /// <c>jgraph.usb.device(row)</c>, <c>jgraph.usb.device(instanceId)</c>, <c>jgraph.usb.device(Name=Value...)</c>
    /// with jgraph.usb.devices' filters, and <c>Interface=n</c> to pick one WinUSB interface of a composite.
    /// </summary>
    public static JgsValue Open(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        int? interfaceNumber = null;
        var rest = new List<JgsValue>();
        int first = args.Count > 0 && (args[0].Type == JgsType.Table || (args.Count % 2 == 1 && JgsBuiltins.IsTextScalar(args[0]))) ? 1 : 0;
        rest.AddRange(args.Take(first));
        for (int i = first; i < args.Count; i++)
        {
            if (i + 1 < args.Count && JgsBuiltins.IsTextScalar(args[i]) && JgsBuiltins.TextOf(args[i]).Length > 0
                && "Interface".StartsWith(JgsBuiltins.TextOf(args[i]), StringComparison.OrdinalIgnoreCase)
                && JgsBuiltins.TextOf(args[i]).Length >= 2)
            {
                JgsValue n = args[i + 1];
                if (!DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(n)) || DeviceChecks.Count(n) != 1
                    || DeviceChecks.Numbers(n).First() is var k && (k != Math.Floor(k) || k < 0 || k > 255))
                {
                    throw new JgsRuntimeException(line, col, "JGraph:usb:BadInterface", "Interface is an interface number from 0 to 255.");
                }

                interfaceNumber = (int)DeviceChecks.Numbers(n).First();
                i++;
                continue;
            }

            rest.Add(args[i]);
        }

        if (first == 1 && rest.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NameValue",
                "jgraph.usb.device takes a row or an instance ID and then only Interface=n, or name-value pairs.");
        }

        UsbDeviceInfo info = JgsBuiltins.OneUsbDevice("jgraph.usb.device", rest, line, col);
        IReadOnlyList<(int Interface, string Path)> paths = WinUsbDevice.PathsOf(info.InstanceId);
        (int Interface, string Path)? chosen = interfaceNumber is { } wanted
            ? paths.Where(p => p.Interface == wanted).Select(static p => ((int, string)?)p).FirstOrDefault()
            : paths.Count > 0 ? paths[0] : null;
        if (chosen is not { } open)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NotWinUsb", WinUsbDevice.NotBoundSentence(info, interfaceNumber));
        }

        WinUsbDevice device;
        try
        {
            device = WinUsbDevice.Open(open.Path);
        }
        catch (DeviceOpenException e)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:OpenFailed", e.Message);
        }

        var usb = new UsbDeviceObject(session, interpreter, device, info);
        session.Remember(usb);
        JgsValue made = JgsValue.External(usb);
        JgsLifetime.Minted(made);
        return made;
    }

    private static UsbDeviceObject Me(DeviceObject o) => (UsbDeviceObject)o;

    private static DeviceClass Declare()
    {
        var properties = new List<DeviceProperty>
        {
            new("VendorID", static (o, _) => JgsValue.StringScalar(JgsBuiltins.UsbHex(Me(o)._info.VendorId))),
            new("ProductID", static (o, _) => JgsValue.StringScalar(JgsBuiltins.UsbHex(Me(o)._info.ProductId))),
            new("Manufacturer", static (o, _) => JgsValue.StringScalar(Me(o)._info.Manufacturer)),
            new("Product", static (o, _) => JgsValue.StringScalar(Me(o)._info.Product.Length > 0 ? Me(o)._info.Product : Me(o)._info.Description)),
            new("SerialNumber", static (o, _) => JgsValue.StringScalar(Me(o)._info.SerialNumber)),
            new("Endpoints", static (o, _) => Me(o).EndpointTable()),
            new("Interfaces", static (o, _) => TransportClient.Row(Me(o)._device.Interfaces.Order().Select(static i => (double)i).ToArray())),
            new("Timeout", static (o, _) => Me(o)._timeout, static (o, v, c) =>
            {
                var who = new DeviceChecks.Subject(null, "Timeout");
                DeviceChecks.Classes(v, ["numeric"], who, c.Line, c.Column);
                DeviceChecks.Attributes(v, ["scalar", "positive", "finite"], who, c.Line, c.Column);
                Me(o)._timeout = v;
            }),
            new("ByteOrder", static (o, _) => JgsValue.StringScalar(Me(o)._byteOrder), static (o, v, c) =>
                Me(o)._byteOrder = DeviceChecks.ValidateString(TransportClient.Str2Char(v), ["little-endian", "big-endian"], new DeviceChecks.Subject(null, null), c.Line, c.Column)),
            new("UserData", static (o, _) => Me(o)._userData, static (o, v, _) => Me(o)._userData = v),
            new("DataFcn", static (o, _) => Me(o)._callback ?? JgsEmpty.Zero(),
                static (_, _, c) => throw c.Error("JGraph:usb:ReadOnlyProperty", "To set \"DataFcn\", use the \"configureCallback\" function.")),
        };

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["control"] = static c => [Me(c.Target).Control(c)],
            ["read"] = static c => [Me(c.Target).Read(c)],
            ["write"] = static c => Void(c, static c => Me(c.Target).Write(c)),
            ["clearHalt"] = static c => Void(c, static c => Me(c.Target).Pipe(c, static (d, p) => d.ClearHalt(p))),
            ["resetPipe"] = static c => Void(c, static c => Me(c.Target).Pipe(c, static (d, p) => d.ResetPipe(p))),
            ["abortPipe"] = static c => Void(c, static c => Me(c.Target).Pipe(c, static (d, p) => d.AbortPipe(p))),
            ["flushPipe"] = static c => Void(c, static c => Me(c.Target).Pipe(c, static (d, p) => d.FlushPipe(p))),
            ["setPolicy"] = static c => Void(c, static c => Me(c.Target).SetPolicy(c)),
            ["setAlternate"] = static c => Void(c, static c => Me(c.Target).SetAlternate(c)),
            ["getAlternate"] = static c => [JgsValue.Number(Me(c.Target)._device.AlternateSetting((int)c.Args[0].AsNumber))],
            ["configureCallback"] = static c => Void(c, static c => Me(c.Target).ConfigureCallback(c)),
        };

        return new DeviceClass("jgraph.usb.Device", "Device", ["handle"], properties, methods,
            ["Device", "abortPipe", "clearHalt", "configureCallback", "control", "delete", "flushPipe", "get", "getAlternate", "isvalid",
             "read", "resetPipe", "set", "setAlternate", "setPolicy", "write"],
            ["VendorID", "ProductID", "Product", "SerialNumber", "Interfaces"]);
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

    private TimeSpan Timeout => TimeSpan.FromSeconds(DeviceChecks.Numbers(_timeout).First());

    private JgsValue EndpointTable()
    {
        IReadOnlyList<WinUsbPipe> pipes = _device.Pipes;
        JgsValue Numbers(Func<WinUsbPipe, double> pick) => JgsMatrix.FromColumnMajor(pipes.Select(pick).ToArray(), pipes.Count, 1);
        JgsValue Strings(Func<WinUsbPipe, string> pick)
        {
            JgsValue column = JgsValue.StringArray(pipes.Select(pick).Select(JgsValue.Str).ToArray());
            column.Reshape(pipes.Count, pipes.Count == 0 ? 0 : 1);
            return column;
        }

        (string, JgsValue)[] columns =
        [
            ("Interface", Numbers(static p => p.Interface)),
            ("Address", Numbers(static p => p.Address)),
            ("Direction", Strings(static p => p.Direction)),
            ("Type", Strings(static p => p.Type)),
            ("MaxPacketSize", Numbers(static p => p.MaxPacketSize)),
            ("Interval", Numbers(static p => p.Interval)),
        ];
        return JgsValue.Table(new JGraph.Data.Table(columns.Select(c => JgsBuiltins.TableColumnFrom("Endpoints", c.Item1, c.Item2, 0, 0)).ToList()));
    }

    private static readonly string[] ControlOptions = ["Direction", "Type", "Recipient", "Request", "Value", "Index", "Data", "Length"];

    /// <summary>
    /// <c>data = control(u, Direction="in", Type="vendor", Recipient="device", Request=r, Value=v, Index=i, Length=n)</c>
    /// and the OUT form with <c>Data=bytes</c>.
    /// </summary>
    private JgsValue Control(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        string direction = "in";
        string type = "vendor";
        string recipient = "device";
        int request = -1;
        int value = 0;
        int index = 0;
        byte[] data = [];
        int length = 0;
        for (int i = 0; i + 1 < call.Args.Count; i += 2)
        {
            string written = DeviceChecks.IsText(call.Args[i]) ? DeviceChecks.Text(call.Args[i]) : "";
            if (!DeviceChecks.Match(written, ControlOptions, out string? name, out _))
            {
                throw call.Error("JGraph:usb:NameValue", $"'{written}' is not an option of control. The options are {string.Join(", ", ControlOptions)}.");
            }

            JgsValue v = TransportClient.Str2Char(call.Args[i + 1]);
            switch (name)
            {
                case "Direction":
                    direction = DeviceChecks.ValidateString(v, ["in", "out"], new DeviceChecks.Subject(null, "Direction"), call.Line, call.Column);
                    break;
                case "Type":
                    type = DeviceChecks.ValidateString(v, ["standard", "class", "vendor"], new DeviceChecks.Subject(null, "Type"), call.Line, call.Column);
                    break;
                case "Recipient":
                    recipient = DeviceChecks.ValidateString(v, ["device", "interface", "endpoint", "other"], new DeviceChecks.Subject(null, "Recipient"), call.Line, call.Column);
                    break;
                case "Request":
                    request = (int)v.AsNumber;
                    break;
                case "Value":
                    value = (int)v.AsNumber;
                    break;
                case "Index":
                    index = (int)v.AsNumber;
                    break;
                case "Data":
                    data = HidObject.ByteArgument(v, call);
                    direction = "out";
                    break;
                default:
                    length = (int)v.AsNumber;
                    break;
            }
        }

        if (call.Args.Count % 2 != 0 || request < 0)
        {
            throw call.Error("JGraph:usb:ControlSyntax",
                "Valid syntax is control(u, Direction=\"in\"|\"out\", Type=\"standard\"|\"class\"|\"vendor\", Recipient=\"device\"|\"interface\"|\"endpoint\", Request=R, Value=V, Index=I, Length=N | Data=BYTES).");
        }

        byte requestType = (byte)((direction == "in" ? 0x80 : 0)
            | (type switch { "class" => 1, "vendor" => 2, _ => 0 } << 5)
            | recipient switch { "interface" => 1, "endpoint" => 2, "other" => 3, _ => 0 });
        try
        {
            byte[] answer = _device.Control(requestType, (byte)request, (ushort)value, (ushort)index, data, length, Timeout);
            return direction == "in" ? HidObject.Bytes(answer) : JgsEmpty.Zero();
        }
        catch (Exception e) when (e is DeviceIOException or TimeoutException or DeviceConnectionLostException)
        {
            throw call.Error("JGraph:usb:ControlFailed", e.Message);
        }
    }

    /// <summary><c>data = read(u, endpoint, count)</c>, <c>read(u, endpoint, count, precision)</c>.</summary>
    private JgsValue Read(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count is < 2 or > 3)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntaxes are read(u, ENDPOINT, COUNT) and read(u, ENDPOINT, COUNT, PRECISION).");
        }

        byte endpoint = (byte)call.Args[0].AsNumber;
        int count = (int)call.Args[1].AsNumber;
        Precision precision = call.Args.Count == 3
            ? PrecisionCodec.Parse(DeviceChecks.ValidateString(TransportClient.Str2Char(call.Args[2]), PrecisionCodec.Names, new DeviceChecks.Subject(null, "precision", 4), call.Line, call.Column))
            : Precision.UInt8;
        int size = PrecisionCodec.Size(precision);
        try
        {
            byte[] bytes = _device.Read(endpoint, count * size, Timeout);
            if (bytes.Length < count * size)
            {
                JgsBuiltins.Warn(call.Host, "JGraph:usb:ShortRead", $"Endpoint 0x{endpoint:X2} answered {bytes.Length} of {count * size} bytes (a short packet ends a transfer).");
            }

            int whole = bytes.Length / size * size;
            return precision switch
            {
                Precision.Char => JgsValue.Str(PrecisionCodec.Latin1(bytes)),
                Precision.String => JgsValue.StringScalar(PrecisionCodec.Latin1(bytes)),
                _ => TransportClient.Row(PrecisionCodec.Decode(bytes.AsSpan(0, whole), precision, _byteOrder == "big-endian")),
            };
        }
        catch (TimeoutException e)
        {
            JgsBuiltins.Warn(call.Host, "JGraph:usb:ReadWarning", e.Message);
            return JgsEmpty.Zero();
        }
        catch (Exception e) when (e is DeviceIOException or DeviceConnectionLostException)
        {
            throw call.Error("JGraph:usb:ReadFailed", e.Message);
        }
    }

    /// <summary><c>write(u, endpoint, data)</c>, <c>write(u, endpoint, data, precision)</c>.</summary>
    private void Write(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count is < 2 or > 3)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntaxes are write(u, ENDPOINT, DATA) and write(u, ENDPOINT, DATA, PRECISION).");
        }

        byte endpoint = (byte)call.Args[0].AsNumber;
        Precision precision = call.Args.Count == 3
            ? PrecisionCodec.Parse(DeviceChecks.ValidateString(TransportClient.Str2Char(call.Args[2]), PrecisionCodec.Names, new DeviceChecks.Subject(null, "precision", 4), call.Line, call.Column))
            : Precision.UInt8;
        double[] values = DeviceChecks.Numbers(TransportClient.Str2Char(call.Args[1])).ToArray();
        try
        {
            _device.Write(endpoint, PrecisionCodec.Encode(values, precision, _byteOrder == "big-endian"), Timeout);
        }
        catch (Exception e) when (e is DeviceIOException or TimeoutException or DeviceConnectionLostException)
        {
            throw call.Error("JGraph:usb:WriteFailed", e.Message);
        }
    }

    private void Pipe(DeviceCall call, Action<WinUsbDevice, byte> action)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count != 1)
        {
            throw call.Error("JGraph:usb:Nargin", "Give the endpoint address.");
        }

        try
        {
            action(_device, (byte)call.Args[0].AsNumber);
        }
        catch (DeviceIOException e)
        {
            throw call.Error("JGraph:usb:PipeFailed", e.Message);
        }
    }

    /// <summary><c>setPolicy(u, endpoint, "RawIO"|"ShortPacketTerminate"|"AutoClearStall"|"AllowPartialReads"|"IgnoreShortPackets", tf)</c>.</summary>
    private void SetPolicy(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count != 3)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntax is setPolicy(u, ENDPOINT, POLICY, TF).");
        }

        string policy = DeviceChecks.ValidateString(TransportClient.Str2Char(call.Args[1]),
            ["RawIO", "ShortPacketTerminate", "AutoClearStall", "AllowPartialReads", "IgnoreShortPackets", "AutoFlush"], new DeviceChecks.Subject(null, "POLICY"), call.Line, call.Column);
        uint code = policy switch
        {
            "RawIO" => 0x07,
            "ShortPacketTerminate" => 0x01,
            "AutoClearStall" => 0x02,
            "AllowPartialReads" => 0x05,
            "IgnoreShortPackets" => 0x04,
            _ => 0x06,
        };
        try
        {
            _device.SetPolicy((byte)call.Args[0].AsNumber, code, call.Args[2].AsNumber != 0);
        }
        catch (DeviceIOException e)
        {
            throw call.Error("JGraph:usb:PipeFailed", e.Message);
        }
    }

    private void SetAlternate(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count != 2)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntax is setAlternate(u, INTERFACE, SETTING).");
        }

        try
        {
            _device.SetAlternateSetting((int)call.Args[0].AsNumber, (int)call.Args[1].AsNumber);
        }
        catch (DeviceIOException e)
        {
            throw call.Error("JGraph:usb:AlternateFailed", e.Message);
        }
    }

    /// <summary>
    /// <c>configureCallback(u, endpoint, count, @fcn)</c>: a continuous read of an IN endpoint, count bytes
    /// a request, calling <c>fcn(u, evt)</c> per transfer at the device queue's drain points
    /// (evt.Data, evt.Endpoint, evt.AbsTime); <c>configureCallback(u, "off")</c> stops it.
    /// </summary>
    private void ConfigureCallback(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count == 1 && DeviceChecks.IsText(call.Args[0]) && DeviceChecks.Text(call.Args[0]).Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            _device.StopReader();
            _callback = null;
            return;
        }

        if (call.Args.Count != 3 || call.Args[2].Type != JgsType.Function
            || !DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(call.Args[0])) || !DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(call.Args[1])))
        {
            throw call.Error("JGraph:usb:ConfigureCallback",
                "Valid syntaxes are configureCallback(u,\"off\") and configureCallback(u,ENDPOINT,COUNT,@callbackFcn).");
        }

        byte endpoint = (byte)DeviceChecks.Numbers(call.Args[0]).First();
        int count = (int)DeviceChecks.Numbers(call.Args[1]).First();
        _callback = call.Args[2];
        _callbackEndpoint = endpoint;
        try
        {
            _device.StartReader(endpoint, count,
                data => _queue.Post(() => FireData(data, DateTime.Now)),
                failure => _queue.Post(() => JgsBuiltins.Warn(Session.Host, "JGraph:usb:ReaderStopped", failure.Message)));
        }
        catch (DeviceIOException e)
        {
            _callback = null;
            throw call.Error("JGraph:usb:ConfigureCallback", e.Message);
        }
    }

    private void FireData(byte[] data, DateTime at)
    {
        if (Deleted || _callback is not { } callback)
        {
            return;
        }

        JgsValue evt = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Data"] = HidObject.Bytes(data),
            ["Endpoint"] = JgsValue.Number(_callbackEndpoint),
            ["AbsTime"] = JgsBuiltins.DatetimeValue(at),
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
            JgsBuiltins.Warn(Session.Host, "JGraph:usb:CallbackError", "Error executing the DataFcn callback:\n" + failure.Message);
        }
    }

    protected override void OnDelete() => _device.Dispose();
}
