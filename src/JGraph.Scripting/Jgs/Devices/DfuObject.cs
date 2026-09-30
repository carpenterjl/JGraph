using System.Globalization;
using System.Runtime.Versioning;
using JGraph.Devices;
using JGraph.Devices.Dfu;
using JGraph.Devices.Simulation;
using JGraph.Devices.Usb;
using JGraph.Devices.WinUsb;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>d = jgraph.usb.dfu(...)</c> (device classes plan, stage D8, ADR 0191): a device's DFU interface over
/// WinUSB, in DFU mode or run-time mode. <c>download(d, file|bytes, Address=, Alternate=, Force=,
/// Verify=, Progress=@fcn)</c>, <c>upload(d, count, Address=)</c>, <c>status(d)</c>,
/// <c>d2 = detach(d)</c>, <c>clearStatus</c>, <c>abort</c>, and DfuSe's <c>erase</c>, <c>massErase</c>,
/// <c>readUnprotect</c> and <c>leave</c>. As dfu-util does, a file whose DFU suffix names another device
/// is refused unless Force is true, and one whose CRC fails is refused always, both before anything is
/// sent. Writing firmware is what the script asked for, as with fwrite, so nothing asks again.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DfuObject : DeviceObject
{
    private static readonly DeviceClass Declaration = Declare();

    private readonly DfuDevice _dfu;
    private readonly UsbDeviceInfo _info;
    private JgsValue _timeout = JgsValue.Number(5);
    private JgsValue _userData = JgsEmpty.Zero();

    private DfuObject(DeviceSession session, Interpreter interpreter, DfuDevice dfu, UsbDeviceInfo info, SimulatedDfu? simulation)
        : base(session, interpreter)
    {
        _dfu = dfu;
        _info = info;
        Simulation = simulation;
    }

    public override DeviceClass Class => Declaration;

    /// <summary>The simulated device behind a <c>jgraph.internal.dfusim</c> object.</summary>
    internal SimulatedDfu? Simulation { get; }

    public override string? Summary() => Deleted ? "deleted" : $"{_info.VendorId:X4}:{_info.ProductId:X4} {Name(_info)} ({(_dfu.DfuMode ? "DFU mode" : "run-time")})";

    private static string Name(UsbDeviceInfo d) => d.Product.Length > 0 ? d.Product : d.Description;

    /// <summary>A device's DFU interface: its number, whether it is in DFU mode, each alternate's name, and the functional descriptor.</summary>
    internal static (int Interface, bool DfuMode, IReadOnlyList<string> Names, DfuFunctional Functional)? DfuInterface(UsbDeviceInfo info)
    {
        if (info.ConfigurationDescriptor.Length == 0)
        {
            return null;
        }

        UsbConfiguration configuration = UsbDescriptors.Configuration(info.ConfigurationDescriptor);
        List<UsbInterface> dfu = configuration.Interfaces.Where(static f => f.Class == 0xFE && f.Subclass == 0x01).ToList();
        if (dfu.Count == 0 || DfuFunctional.From(dfu.SelectMany(static f => f.ClassSpecific).Select(static c => c.Bytes)) is not { } functional)
        {
            return null;
        }

        int number = dfu[0].Number;
        string[] names = dfu.Where(f => f.Number == number).OrderBy(static f => f.AlternateSetting)
            .Select(f => info.Strings.TryGetValue(f.NameIndex, out string? s) ? s : "").ToArray();
        return (number, dfu[0].Protocol == 2, names, functional);
    }

    // --- opening -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>jgraph.usb.dfu()</c> for the one device with a DFU interface, <c>jgraph.usb.dfu(row)</c>,
    /// <c>jgraph.usb.dfu(instanceId)</c>, or <c>jgraph.usb.dfu(Name=Value...)</c> with jgraph.usb.devices'
    /// filters among the devices that have one.
    /// </summary>
    public static JgsValue Open(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        UsbDeviceInfo info;
        if (args.Count == 1 && (args[0].Type == JgsType.Table || JgsBuiltins.IsTextScalar(args[0])))
        {
            info = JgsBuiltins.OneUsbDevice("jgraph.usb.dfu", args, line, col);
        }
        else
        {
            Func<UsbDeviceInfo, bool> keep = JgsBuiltins.UsbFilter("jgraph.usb.dfu", args, 0, line, col);
            List<UsbDeviceInfo> found = UsbEnumerator.Devices().Where(keep).Where(static d => DfuInterface(d) is not null).ToList();
            info = found.Count switch
            {
                1 => found[0],
                0 => throw new JgsRuntimeException(line, col, "JGraph:usb:NoDevice",
                    args.Count == 0 ? "No USB device with a DFU interface is connected." : "No USB device with a DFU interface matches; jgraph.usb.dfulist lists them."),
                _ => throw new JgsRuntimeException(line, col, "JGraph:usb:SeveralDevices",
                    $"{found.Count} USB devices with a DFU interface match; give jgraph.usb.dfu a SerialNumber too, or one row of jgraph.usb.dfulist."),
            };
        }

        return OpenInfo(session, interpreter, info, line, col);
    }

    private static JgsValue OpenInfo(DeviceSession session, Interpreter interpreter, UsbDeviceInfo info, int line, int col)
    {
        if (DfuInterface(info) is not { } dfu)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NoDfu",
                $"{info.VendorId:X4}:{info.ProductId:X4} has no DFU interface; jgraph.usb.dfulist lists the devices that do.");
        }

        IReadOnlyList<(int Interface, string Path)> paths = WinUsbDevice.PathsOf(info.InstanceId);
        (int Interface, string Path)? chosen = paths.Where(p => p.Interface == dfu.Interface || p.Interface == -1)
            .Select(static p => ((int, string)?)p).FirstOrDefault();
        if (chosen is not { } open)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NotWinUsb", WinUsbDevice.NotBoundSentence(info, dfu.Interface));
        }

        WinUsbDevice usb;
        try
        {
            usb = WinUsbDevice.Open(open.Path);
        }
        catch (DeviceOpenException e)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:OpenFailed", e.Message);
        }

        return Mint(session, interpreter, new DfuDevice(new WinUsbDfuTransport(usb), dfu.Interface, dfu.Functional, dfu.DfuMode, dfu.Names), info, null);
    }

    /// <summary>A Dfu object on a simulated device (<c>jgraph.internal.dfusim</c>), found through its descriptors as a real one is.</summary>
    internal static JgsValue OpenSimulated(DeviceSession session, Interpreter interpreter, SimulatedDfu simulation)
    {
        var dfu = DfuInterface(simulation.Info)!.Value;
        return Mint(session, interpreter, new DfuDevice(simulation, dfu.Interface, dfu.Functional, dfu.DfuMode, dfu.Names), simulation.Info, simulation);
    }

    private static JgsValue Mint(DeviceSession session, Interpreter interpreter, DfuDevice dfu, UsbDeviceInfo info, SimulatedDfu? simulation)
    {
        var made = new DfuObject(session, interpreter, dfu, info, simulation);
        session.Remember(made);
        JgsValue value = JgsValue.External(made);
        JgsLifetime.Minted(value);
        return value;
    }

    // --- jgraph.usb.dfulist, dfufile, dfusuffix ------------------------------------------------------------

    /// <summary><c>T = jgraph.usb.dfulist(Name=Value...)</c>: the devices with a DFU interface, from their descriptors alone; nothing is sent to them.</summary>
    public static JgsValue List(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Func<UsbDeviceInfo, bool> keep = JgsBuiltins.UsbFilter("jgraph.usb.dfulist", args, 0, line, col);
        var rows = UsbEnumerator.Devices().Where(keep)
            .Select(static d => (Device: d, Dfu: DfuInterface(d)))
            .Where(static r => r.Dfu is not null)
            .Select(static r => (r.Device, Dfu: r.Dfu!.Value))
            .ToList();
        int n = rows.Count;
        JgsValue Text(Func<(UsbDeviceInfo Device, (int Interface, bool DfuMode, IReadOnlyList<string> Names, DfuFunctional Functional) Dfu), string> pick) =>
            JgsValue.StringArray(rows.Select(r => JgsValue.Str(pick(r))).ToArray(), n, 1);
        JgsValue Number(Func<(UsbDeviceInfo Device, (int Interface, bool DfuMode, IReadOnlyList<string> Names, DfuFunctional Functional) Dfu), double> pick) =>
            JgsMatrix.FromColumnMajor(rows.Select(pick).ToArray(), n, 1);
        JgsValue Logical(Func<(UsbDeviceInfo Device, (int Interface, bool DfuMode, IReadOnlyList<string> Names, DfuFunctional Functional) Dfu), bool> pick)
        {
            JgsValue column = n == 0 ? JgsBuiltins.EmptyLogical(0, 1) : JgsValue.Array(rows.Select(r => JgsValue.Bool(pick(r))).ToArray());
            column.Reshape(n, 1);
            return column;
        }

        (string Name, JgsValue Value)[] columns =
        [
            ("VendorID", Text(static r => JgsBuiltins.UsbHex(r.Device.VendorId))),
            ("ProductID", Text(static r => JgsBuiltins.UsbHex(r.Device.ProductId))),
            ("Manufacturer", Text(static r => r.Device.Manufacturer)),
            ("Product", Text(static r => Name(r.Device))),
            ("SerialNumber", Text(static r => r.Device.SerialNumber)),
            ("Mode", Text(static r => r.Dfu.DfuMode ? "dfu" : "runtime")),
            ("Version", Text(static r => VersionText(r.Dfu.Functional.Version))),
            ("Interface", Number(static r => r.Dfu.Interface)),
            ("Alternates", Number(static r => r.Dfu.Names.Count)),
            ("TransferSize", Number(static r => r.Dfu.Functional.TransferSize)),
            ("CanDownload", Logical(static r => r.Dfu.Functional.CanDownload)),
            ("CanUpload", Logical(static r => r.Dfu.Functional.CanUpload)),
            ("WinUSB", Logical(static r => WinUsbDevice.PathsOf(r.Device.InstanceId).Any(p => p.Interface == r.Dfu.Interface || p.Interface == -1))),
            ("Memory", Text(static r => string.Join("; ", r.Dfu.Names.Where(static s => s.Length > 0)))),
            ("Location", Text(static r => r.Device.Location)),
            ("InstanceID", Text(static r => r.Device.InstanceId)),
        ];
        return JgsValue.Table(new JGraph.Data.Table(columns.Select(c => JgsBuiltins.TableColumnFrom("jgraph.usb.dfulist", c.Name, c.Value, line, col)).ToList()));
    }

    private static string VersionText(int bcd) => $"{bcd >> 8:X}.{bcd & 0xFF:x2}";

    /// <summary>
    /// <c>img = jgraph.usb.dfufile(path)</c>: a firmware file read as the DFU functions read it (a .dfu
    /// suffix, a DfuSe container, Intel HEX or raw binary): Format, the suffix's IDs, whether its CRC
    /// checked, and Targets, each with its alternate setting, name and elements of Address and Data.
    /// </summary>
    public static JgsValue FileInfo(JGraphScriptGlobals? host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1 || !JgsBuiltins.IsTextScalar(args[0]))
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:Nargin", "Valid syntax is jgraph.usb.dfufile(FILENAME).");
        }

        DfuImage image = ReadImage(host, JgsBuiltins.TextOf(args[0]), line, col);
        static JgsValue Hex(int id) => JgsValue.StringScalar(id == 0xFFFF ? "" : JgsBuiltins.UsbHex(id));
        JgsValue targets = JgsValue.StructArray(image.Targets.Select(t => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Alternate"] = JgsValue.Number(t.AlternateSetting),
            ["Name"] = JgsValue.StringScalar(t.Name),
            ["Elements"] = JgsValue.StructArray(t.Elements.Select(e => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Address"] = JgsValue.Number(e.Address),
                ["Data"] = HidObject.Bytes(e.Data),
            }).ToArray()),
        }).ToArray());
        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Format"] = JgsValue.StringScalar(image.Format),
            ["VendorID"] = Hex(image.VendorId),
            ["ProductID"] = Hex(image.ProductId),
            ["Release"] = Hex(image.DeviceRelease),
            ["DfuVersion"] = JgsValue.StringScalar(image.HasSuffix ? VersionText(image.DfuVersion) : ""),
            ["HasSuffix"] = JgsValue.Bool(image.HasSuffix),
            ["CrcOk"] = JgsValue.Bool(image.CrcOk),
            ["Size"] = JgsValue.Number(image.Targets.Sum(static t => t.Elements.Sum(static e => (double)e.Data.Length))),
            ["Targets"] = targets,
        });
    }

    private static DfuImage ReadImage(JGraphScriptGlobals? host, string path, int line, int col)
    {
        try
        {
            return DfuFiles.Read(host is null ? path : host.Resolve(path));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:DfuFile", $"The firmware file {path} could not be read: {e.Message}");
        }
    }

    /// <summary>
    /// <c>bytes = jgraph.usb.dfusuffix(payload, VendorID=, ProductID=, Release=)</c>: the payload with a DFU
    /// 1.1 suffix and its CRC appended, as dfu-suffix -a writes it; an ID left out is 0xFFFF, "any".
    /// </summary>
    public static JgsValue Suffix(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count < 1 || args.Count % 2 != 1 || !DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(args[0])))
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:Nargin", "Valid syntax is jgraph.usb.dfusuffix(BYTES, VendorID=V, ProductID=P, Release=R).");
        }

        double[] numbers = DeviceChecks.Numbers(args[0]).ToArray();
        if (numbers.Any(static x => x < 0 || x > 255 || x != Math.Floor(x)))
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:InvalidBytes", "The payload is bytes: integers from 0 to 255.");
        }

        int vid = 0xFFFF, pid = 0xFFFF, release = 0xFFFF;
        for (int i = 1; i < args.Count; i += 2)
        {
            string written = JgsBuiltins.IsTextScalar(args[i]) ? JgsBuiltins.TextOf(args[i]) : "";
            if (!DeviceChecks.Match(written, ["VendorID", "ProductID", "Release"], out string? name, out bool ambiguous) || ambiguous)
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:NameValue", $"'{written}' is not a name jgraph.usb.dfusuffix takes; the names are VendorID, ProductID and Release.");
            }

            int id = JgsBuiltins.UsbIdOf(args[i + 1], line, col);
            _ = name switch { "VendorID" => vid = id, "ProductID" => pid = id, _ => release = id };
        }

        return HidObject.Bytes(DfuFiles.WithSuffix(numbers.Select(static x => (byte)x).ToArray(), vid, pid, release));
    }

    /// <summary>A memory address: a number from 0 to 2^32 − 1, or hex text ("0x08000000").</summary>
    private static uint AddressOf(JgsValue value, DeviceCall call)
    {
        if (JgsBuiltins.IsTextScalar(value))
        {
            string text = JgsBuiltins.TextOf(value).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text[2..];
            }

            if (text.Length is > 0 and <= 8 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address))
            {
                return address;
            }
        }
        else if (DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)) && DeviceChecks.Count(value) == 1
            && DeviceChecks.Numbers(value).First() is var n && n == Math.Floor(n) && n is >= 0 and <= uint.MaxValue)
        {
            return (uint)n;
        }

        throw call.Error("JGraph:usb:DfuAddress", "An address is a number from 0 to 4294967295, or hex text (\"0x08000000\").");
    }

    // --- the object ------------------------------------------------------------------------------------------

    private static DfuObject Me(DeviceObject o) => (DfuObject)o;

    private static DeviceClass Declare()
    {
        var properties = new List<DeviceProperty>
        {
            new("VendorID", static (o, _) => JgsValue.StringScalar(JgsBuiltins.UsbHex(Me(o)._info.VendorId))),
            new("ProductID", static (o, _) => JgsValue.StringScalar(JgsBuiltins.UsbHex(Me(o)._info.ProductId))),
            new("Manufacturer", static (o, _) => JgsValue.StringScalar(Me(o)._info.Manufacturer)),
            new("Product", static (o, _) => JgsValue.StringScalar(Name(Me(o)._info))),
            new("SerialNumber", static (o, _) => JgsValue.StringScalar(Me(o)._info.SerialNumber)),
            new("Mode", static (o, _) => JgsValue.StringScalar(Me(o)._dfu.DfuMode ? "dfu" : "runtime")),
            new("Version", static (o, _) => JgsValue.StringScalar(VersionText(Me(o)._dfu.Functional.Version))),
            new("DfuSe", static (o, _) => JgsValue.Bool(Me(o)._dfu.IsDfuSe)),
            new("Interface", static (o, _) => JgsValue.Number(Me(o)._dfu.InterfaceNumber)),
            new("Alternate", static (o, _) => JgsValue.Number(Me(o)._dfu.Alternate), static (o, v, c) => Me(o).SetAlternate(v, c)),
            new("AlternateNames", static (o, _) => JgsValue.StringArray(Me(o)._dfu.AlternateNames.Select(JgsValue.Str).ToArray(), 1, Me(o)._dfu.AlternateNames.Count)),
            new("Layout", static (o, _) => Me(o).LayoutTable()),
            new("TransferSize", static (o, _) => JgsValue.Number(Me(o)._dfu.Functional.TransferSize)),
            new("CanDownload", static (o, _) => JgsValue.Bool(Me(o)._dfu.Functional.CanDownload)),
            new("CanUpload", static (o, _) => JgsValue.Bool(Me(o)._dfu.Functional.CanUpload)),
            new("ManifestationTolerant", static (o, _) => JgsValue.Bool(Me(o)._dfu.Functional.ManifestationTolerant)),
            new("WillDetach", static (o, _) => JgsValue.Bool(Me(o)._dfu.Functional.WillDetach)),
            new("DetachTimeout", static (o, _) => JgsValue.Number(Me(o)._dfu.Functional.DetachTimeout)),
            new("Location", static (o, _) => JgsValue.StringScalar(Me(o)._info.Location)),
            new("Timeout", static (o, _) => Me(o)._timeout, static (o, v, c) =>
            {
                var who = new DeviceChecks.Subject(null, "Timeout");
                DeviceChecks.Classes(v, ["numeric"], who, c.Line, c.Column);
                DeviceChecks.Attributes(v, ["scalar", "positive", "finite"], who, c.Line, c.Column);
                Me(o)._timeout = v;
                Me(o)._dfu.Timeout = TimeSpan.FromSeconds(DeviceChecks.Numbers(v).First());
            }),
            new("UserData", static (o, _) => Me(o)._userData, static (o, v, _) => Me(o)._userData = v),
        };

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["status"] = static c => [Me(c.Target).Status(c)],
            ["download"] = static c => Void(c, static c => Me(c.Target).Download(c)),
            ["upload"] = static c => [Me(c.Target).Upload(c)],
            ["detach"] = static c => Me(c.Target).Detach(c),
            ["clearStatus"] = static c => Void(c, static c => Me(c.Target).Run(c, "clearStatus", 0, static (d, _) => d.ClearStatus())),
            ["abort"] = static c => Void(c, static c => Me(c.Target).Run(c, "abort", 0, static (d, _) => d.Abort())),
            ["erase"] = static c => Void(c, static c => Me(c.Target).Erase(c)),
            ["massErase"] = static c => Void(c, static c => Me(c.Target).Run(c, "massErase", 0, static (d, _) => d.MassErase(), dfuSe: true)),
            ["readUnprotect"] = static c => Void(c, static c => Me(c.Target).Run(c, "readUnprotect", 0, static (d, _) => d.ReadUnprotect(), dfuSe: true)),
            ["leave"] = static c => Void(c, static c => Me(c.Target).Leave(c)),
        };

        return new DeviceClass("jgraph.usb.Dfu", "Dfu", ["handle"], properties, methods,
            ["Dfu", "abort", "clearStatus", "delete", "detach", "download", "erase", "get", "isvalid", "leave", "massErase", "readUnprotect", "set", "status", "upload"],
            ["VendorID", "ProductID", "Product", "Mode", "Version", "Alternate", "TransferSize"]);
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

    private JgsValue LayoutTable()
    {
        IReadOnlyList<DfuSeSegment> segments = _dfu.Layout?.Segments ?? [];
        int n = segments.Count;
        JgsValue Text(Func<DfuSeSegment, string> pick) => JgsValue.StringArray(segments.Select(s => JgsValue.Str(pick(s))).ToArray(), n, 1);
        JgsValue Number(Func<DfuSeSegment, double> pick) => JgsMatrix.FromColumnMajor(segments.Select(pick).ToArray(), n, 1);
        JgsValue Logical(Func<DfuSeSegment, bool> pick)
        {
            JgsValue column = n == 0 ? JgsBuiltins.EmptyLogical(0, 1) : JgsValue.Array(segments.Select(s => JgsValue.Bool(pick(s))).ToArray());
            column.Reshape(n, 1);
            return column;
        }

        (string Name, JgsValue Value)[] columns =
        [
            ("Start", Text(static s => DfuFiles.Hex(s.Start))),
            ("End", Text(static s => DfuFiles.Hex(s.End))),
            ("PageSize", Number(static s => s.PageSize)),
            ("Pages", Number(static s => s.Pages)),
            ("Readable", Logical(static s => s.Readable)),
            ("Erasable", Logical(static s => s.Erasable)),
            ("Writeable", Logical(static s => s.Writeable)),
        ];
        return JgsValue.Table(new JGraph.Data.Table(columns.Select(c => JgsBuiltins.TableColumnFrom("Layout", c.Name, c.Value, 0, 0)).ToList()));
    }

    private void SetAlternate(JgsValue value, DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (!DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)) || DeviceChecks.Count(value) != 1
            || DeviceChecks.Numbers(value).First() is var k && (k != Math.Floor(k) || k < 0 || k >= _dfu.AlternateNames.Count))
        {
            throw call.Error("JGraph:usb:DfuAlternate", $"Alternate is an alternate setting of the DFU interface, from 0 to {_dfu.AlternateNames.Count - 1}.");
        }

        Guard(call, () => _dfu.Alternate = (int)DeviceChecks.Numbers(value).First());
    }

    /// <summary>Runs a device operation, turning the device layer's failures into JGraph:usb:DfuFailed.</summary>
    private void Guard(DeviceCall call, Action action)
    {
        try
        {
            action();
        }
        catch (DfuException e)
        {
            throw call.Error("JGraph:usb:DfuFailed", e.Message);
        }
        catch (Exception e) when (e is DeviceIOException or TimeoutException or DeviceConnectionLostException)
        {
            throw call.Error("JGraph:usb:DfuFailed", e.Message);
        }
    }

    private void RequireDfuMode(DeviceCall call, string what)
    {
        if (!_dfu.DfuMode)
        {
            throw call.Error("JGraph:usb:DfuRuntime", $"The device is running its application, so {what} cannot run; d2 = detach(d) brings it into DFU mode.");
        }
    }

    private void RequireDfuSe(DeviceCall call, string what)
    {
        if (!_dfu.IsDfuSe)
        {
            throw call.Error("JGraph:usb:DfuNotDfuSe", $"{what} is a DfuSe command, and this device speaks plain DFU {VersionText(_dfu.Functional.Version)}.");
        }
    }

    private void Run(DeviceCall call, string what, int arguments, Action<DfuDevice, DeviceCall> action, bool dfuSe = false)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count != arguments)
        {
            throw call.Error("JGraph:usb:Nargin", $"Valid syntax is {what}(d).");
        }

        RequireDfuMode(call, what);
        if (dfuSe)
        {
            RequireDfuSe(call, what);
        }

        Guard(call, () => action(_dfu, call));
    }

    private JgsValue Status(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        DfuStatus s = default;
        Guard(call, () => s = _dfu.GetStatus());
        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Status"] = JgsValue.StringScalar(s.StatusName),
            ["State"] = JgsValue.StringScalar(s.StateName),
            ["PollTimeout"] = JgsValue.Number(s.PollTimeout),
        });
    }

    /// <summary>The name-value pairs of download and upload, by their full names.</summary>
    private static Dictionary<string, JgsValue> Options(DeviceCall call, int first, string function, string[] names)
    {
        if ((call.Args.Count - first) % 2 != 0)
        {
            throw call.Error("JGraph:usb:NameValue", $"{function} takes name-value pairs after its arguments; the names are {string.Join(", ", names)}.");
        }

        var options = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = first; i < call.Args.Count; i += 2)
        {
            string written = DeviceChecks.IsText(call.Args[i]) ? DeviceChecks.Text(call.Args[i]) : "";
            if (written.Length == 0 || !DeviceChecks.Match(written, names, out string? name, out bool ambiguous) || ambiguous)
            {
                throw call.Error("JGraph:usb:NameValue", $"'{written}' is not an option of {function}. The options are {string.Join(", ", names)}.");
            }

            options[name!] = call.Args[i + 1];
        }

        return options;
    }

    private static bool Flag(JgsValue value, string name, DeviceCall call) =>
        DeviceChecks.Count(value) == 1 && (value.Type == JgsType.Bool || DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)))
            ? DeviceChecks.Numbers(value).First() != 0
            : throw call.Error("JGraph:usb:NameValue", $"{name} is true or false.");

    private int AlternateOption(JgsValue value, DeviceCall call)
    {
        if (!DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)) || DeviceChecks.Count(value) != 1
            || DeviceChecks.Numbers(value).First() is var k && (k != Math.Floor(k) || k < 0 || k >= _dfu.AlternateNames.Count))
        {
            throw call.Error("JGraph:usb:DfuAlternate", $"Alternate is an alternate setting of the DFU interface, from 0 to {_dfu.AlternateNames.Count - 1}.");
        }

        return (int)DeviceChecks.Numbers(value).First();
    }

    private static readonly string[] DownloadOptions = ["Address", "Alternate", "Force", "Verify", "Progress"];

    /// <summary>One piece of a download: the alternate setting it goes to, where, and its bytes.</summary>
    private readonly record struct Piece(int Alternate, uint Address, byte[] Data);

    /// <summary><c>download(d, FILE or BYTES, Address=, Alternate=, Force=, Verify=, Progress=@fcn)</c>.</summary>
    private void Download(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count < 1)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntax is download(d, FILE or BYTES, Name=Value...) with Address, Alternate, Force, Verify and Progress.");
        }

        Dictionary<string, JgsValue> options = Options(call, 1, "download", DownloadOptions);
        RequireDfuMode(call, "download");
        if (!_dfu.Functional.CanDownload)
        {
            throw call.Error("JGraph:usb:DfuNoDownload", "The device's DFU functional descriptor says it cannot download.");
        }

        uint? address = options.TryGetValue("Address", out JgsValue? a) ? AddressOf(a, call) : null;
        int? alternate = options.TryGetValue("Alternate", out JgsValue? alt) ? AlternateOption(alt, call) : null;
        bool force = options.TryGetValue("Force", out JgsValue? f) && Flag(f, "Force", call);
        bool verify = options.TryGetValue("Verify", out JgsValue? v) && Flag(v, "Verify", call);
        JgsValue? progress = null;
        if (options.TryGetValue("Progress", out JgsValue? p))
        {
            progress = p.Type == JgsType.Function ? p : throw call.Error("JGraph:usb:NameValue", "Progress takes a function handle, called as fcn(sent, total).");
        }

        if (verify && !_dfu.Functional.CanUpload)
        {
            throw call.Error("JGraph:usb:DfuNoUpload", "Verify reads the image back, and the device's DFU functional descriptor says it cannot upload.");
        }

        if (verify && !_dfu.IsDfuSe && !_dfu.Functional.ManifestationTolerant)
        {
            // Plain DFU reads back the firmware it has, which is the new image only after manifestation,
            // and a device that is not manifestation tolerant resets there instead of answering.
            throw call.Error("JGraph:usb:DfuNoVerify", "Verify reads the image back after manifestation, and this device resets there (it is not manifestation tolerant).");
        }

        List<Piece> pieces = Pieces(call, address, alternate, force);
        long total = pieces.Sum(static x => (long)x.Data.Length);
        long sent = 0;
        foreach (Piece piece in pieces)
        {
            Guard(call, () => _dfu.Alternate = piece.Alternate);
            long before = sent;
            Guard(call, () => _dfu.DownloadImage(piece.Data, piece.Address, done =>
            {
                sent = before + done;
                if (progress is { } fcn)
                {
                    JgsCallbacks.Invoke(fcn.AsCallable, [JgsValue.Number(sent), JgsValue.Number(total)], 0, 0);
                }
            }));
            if (verify)
            {
                byte[] back = [];
                Guard(call, () => back = _dfu.UploadImage(piece.Data.Length, piece.Address));
                if (!back.AsSpan().SequenceEqual(piece.Data))
                {
                    int at = Enumerable.Range(0, Math.Min(back.Length, piece.Data.Length)).FirstOrDefault(i => back[i] != piece.Data[i], Math.Min(back.Length, piece.Data.Length));
                    throw call.Error("JGraph:usb:DfuVerify", _dfu.IsDfuSe
                        ? $"The image read back from {DfuFiles.Hex(piece.Address)} differs from the one sent, first at {DfuFiles.Hex(piece.Address + (uint)at)}."
                        : $"The image read back differs from the one sent, first at byte {at}.");
                }
            }
        }
    }

    /// <summary>What a download sends: a file's targets and elements, or bytes; checked before anything is sent.</summary>
    private List<Piece> Pieces(DeviceCall call, uint? address, int? alternate, bool force)
    {
        JgsValue source = TransportClient.Str2Char(call.Args[0]);
        int currentAlternate = alternate ?? _dfu.Alternate;
        List<Piece> pieces;
        if (source.Type == JgsType.String)
        {
            DfuImage image = ReadImage(call.Host, source.AsString, call.Line, call.Column);
            if (image.HasSuffix && !image.CrcOk)
            {
                throw call.Error("JGraph:usb:DfuCrc", "The file's DFU suffix CRC does not match its contents, so the file is damaged; nothing was sent.");
            }

            bool vidOk = image.VendorId == 0xFFFF || image.VendorId == _info.VendorId;
            bool pidOk = image.ProductId == 0xFFFF || image.ProductId == _info.ProductId;
            if (image.HasSuffix && !(vidOk && pidOk) && !force)
            {
                throw call.Error("JGraph:usb:DfuWrongDevice",
                    $"The file is for {image.VendorId:X4}:{image.ProductId:X4}, and this device is {_info.VendorId:X4}:{_info.ProductId:X4}; nothing was sent. Give Force=true to send it anyway.");
            }

            bool placed = image.Format is "DfuSe" or "Intel HEX";
            if (placed && address is not null)
            {
                throw call.Error("JGraph:usb:DfuAddressGiven", $"A {image.Format} file names its own addresses; give Address only with a .bin or .dfu file or with bytes.");
            }

            if (image.Format == "DfuSe")
            {
                if (!_dfu.IsDfuSe)
                {
                    throw call.Error("JGraph:usb:DfuNotDfuSe", "The file is a DfuSe image, and this device speaks plain DFU.");
                }

                IEnumerable<DfuTarget> targets = image.Targets.Where(t => alternate is not { } want || t.AlternateSetting == want);
                pieces = targets.SelectMany(t => t.Elements.Select(e => new Piece(t.AlternateSetting, e.Address, e.Data))).ToList();
                if (image.Targets.Any(t => t.AlternateSetting >= _dfu.AlternateNames.Count))
                {
                    throw call.Error("JGraph:usb:DfuAlternate", $"The file has a target for an alternate setting the device does not have (it has 0 to {_dfu.AlternateNames.Count - 1}).");
                }

                if (pieces.Count == 0)
                {
                    throw call.Error("JGraph:usb:DfuNoTarget", $"The file has no target for alternate setting {alternate}.");
                }
            }
            else
            {
                pieces = image.Targets.SelectMany(static t => t.Elements).Select(e => new Piece(currentAlternate, placed ? e.Address : address ?? 0, e.Data)).ToList();
            }
        }
        else if (source.Type == JgsType.Bool || DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(source)))
        {
            pieces = [new Piece(currentAlternate, address ?? 0, HidObject.ByteArgument(source, call))];
        }
        else
        {
            throw call.Error("JGraph:usb:DfuSource", "download takes a firmware file's name, or the image as bytes.");
        }

        if (pieces.Sum(static x => (long)x.Data.Length) == 0)
        {
            throw call.Error("JGraph:usb:DfuEmpty", "The image is empty; nothing was sent.");
        }

        if (!_dfu.IsDfuSe)
        {
            if (address is not null)
            {
                throw call.Error("JGraph:usb:DfuNoAddress", "A plain DFU device places its image itself, so it takes no Address.");
            }

            if (pieces.Count > 1)
            {
                throw call.Error("JGraph:usb:DfuPieces", $"A plain DFU device takes one contiguous image, and this one is in {pieces.Count} pieces.");
            }

            return pieces;
        }

        // DfuSe: an image with no address goes at the start of its alternate setting's memory, as dfu-util sends it.
        var placedPieces = new List<Piece>();
        foreach (Piece piece in pieces)
        {
            DfuSeLayout layout = DfuSeLayout.Parse(_dfu.AlternateNames[piece.Alternate])
                ?? throw call.Error("JGraph:usb:DfuNoLayout", $"Alternate setting {piece.Alternate} names no DfuSe memory layout.");
            uint at = piece.Address == 0 && layout.SegmentAt(0) is null && layout.Segments.Count > 0 ? layout.Segments[0].Start : piece.Address;
            if (layout.Unwriteable(at, piece.Data.Length) is { } why)
            {
                throw call.Error("JGraph:usb:DfuAddress", $"The image cannot go at {DfuFiles.Hex(at)}: {why}; nothing was sent.");
            }

            placedPieces.Add(piece with { Address = at });
        }

        return placedPieces;
    }

    private static readonly string[] UploadOptions = ["Address", "Alternate"];

    /// <summary><c>bytes = upload(d)</c>, <c>upload(d, COUNT)</c>, with <c>Address=</c> (DfuSe) and <c>Alternate=</c>.</summary>
    private JgsValue Upload(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        int first = call.Args.Count > 0 && !DeviceChecks.IsText(call.Args[0]) ? 1 : 0;
        Dictionary<string, JgsValue> options = Options(call, first, "upload", UploadOptions);
        RequireDfuMode(call, "upload");
        if (!_dfu.Functional.CanUpload)
        {
            throw call.Error("JGraph:usb:DfuNoUpload", "The device's DFU functional descriptor says it cannot upload.");
        }

        int? count = null;
        if (first == 1)
        {
            if (!DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(call.Args[0])) || DeviceChecks.Count(call.Args[0]) != 1
                || DeviceChecks.Numbers(call.Args[0]).First() is var c && (c != Math.Floor(c) || c < 1 || c > 1 << 28))
            {
                throw call.Error("JGraph:usb:DfuCount", "COUNT is a whole number of bytes from 1 to 268435456.");
            }

            count = (int)DeviceChecks.Numbers(call.Args[0]).First();
        }

        if (options.TryGetValue("Alternate", out JgsValue? alt))
        {
            int setting = AlternateOption(alt, call);
            Guard(call, () => _dfu.Alternate = setting);
        }

        uint? address = options.TryGetValue("Address", out JgsValue? a) ? AddressOf(a, call) : null;
        byte[] bytes = [];
        if (!_dfu.IsDfuSe)
        {
            if (address is not null)
            {
                throw call.Error("JGraph:usb:DfuNoAddress", "A plain DFU device uploads its image from the start, so it takes no Address.");
            }

            Guard(call, () => bytes = _dfu.UploadImage(count ?? 1 << 28, 0));
            return HidObject.Bytes(bytes);
        }

        DfuSeLayout layout = _dfu.Layout ?? throw call.Error("JGraph:usb:DfuNoLayout", $"Alternate setting {_dfu.Alternate} names no DfuSe memory layout.");
        uint from = address ?? (layout.Segments.Count > 0 ? layout.Segments[0].Start : 0);
        uint end = from;
        while (layout.SegmentAt(end) is { Readable: true } segment)
        {
            end = segment.End;
        }

        if (end == from)
        {
            throw call.Error("JGraph:usb:DfuAddress", $"{DfuFiles.Hex(from)} is not readable memory of {layout.Name}.");
        }

        int length = count ?? (int)Math.Min(end - from, 1u << 28);
        if (from + (ulong)length > end)
        {
            throw call.Error("JGraph:usb:DfuAddress", $"{length} bytes from {DfuFiles.Hex(from)} run past the readable memory of {layout.Name}, which ends at {DfuFiles.Hex(end)}.");
        }

        Guard(call, () => bytes = _dfu.UploadImage(length, from));
        return HidObject.Bytes(bytes);
    }

    /// <summary><c>erase(d, ADDRESS)</c>, <c>erase(d, ADDRESS, COUNT)</c>: DfuSe's page erase, of every page [ADDRESS, ADDRESS + COUNT) touches.</summary>
    private void Erase(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count is not (1 or 2))
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntaxes are erase(d, ADDRESS) and erase(d, ADDRESS, COUNT).");
        }

        RequireDfuMode(call, "erase");
        RequireDfuSe(call, "erase");
        uint address = AddressOf(call.Args[0], call);
        int count = call.Args.Count == 2 ? (int)DeviceChecks.Numbers(call.Args[1]).First() : 1;
        DfuSeLayout layout = _dfu.Layout ?? throw call.Error("JGraph:usb:DfuNoLayout", $"Alternate setting {_dfu.Alternate} names no DfuSe memory layout.");
        for (uint at = address; at < address + (uint)Math.Max(1, count);)
        {
            if (layout.SegmentAt(at) is not { Erasable: true } segment)
            {
                throw call.Error("JGraph:usb:DfuAddress", $"{DfuFiles.Hex(at)} is not erasable memory of {layout.Name}.");
            }

            at = segment.End;
        }

        Guard(call, () =>
        {
            _dfu.Idle();
            foreach (uint page in layout.PagesTouched(address, Math.Max(1, count)))
            {
                _dfu.ErasePage(page);
            }

            _dfu.Abort();
        });
    }

    /// <summary><c>leave(d)</c>, <c>leave(d, ADDRESS)</c>: DfuSe leaves DFU mode and starts the application there.</summary>
    private void Leave(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count > 1)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntaxes are leave(d) and leave(d, ADDRESS).");
        }

        RequireDfuMode(call, "leave");
        if (!_dfu.IsDfuSe)
        {
            throw call.Error("JGraph:usb:DfuNoLeave", "A plain DFU device leaves DFU mode when it is reset, which WinUSB cannot do from here; unplug and replug it.");
        }

        uint address = call.Args.Count == 1 ? AddressOf(call.Args[0], call) : _dfu.Layout is { Segments.Count: > 0 } layout ? layout.Segments[0].Start : 0x08000000;
        Guard(call, () => _dfu.Leave(address));
    }

    private static readonly string[] DetachOptions = ["Wait"];

    /// <summary>
    /// <c>detach(d)</c> asks a run-time interface to re-enumerate in DFU mode; <c>d2 = detach(d, Wait=s)</c>
    /// also waits, 10 seconds unless told, for a DFU-mode device on the same port and opens it.
    /// </summary>
    private JgsValue[] Detach(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        Dictionary<string, JgsValue> options = Options(call, 0, "detach", DetachOptions);
        double wait = 10;
        if (options.TryGetValue("Wait", out JgsValue? w))
        {
            if (!DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(w)) || DeviceChecks.Count(w) != 1 || DeviceChecks.Numbers(w).First() is var s && !(s >= 0 && s <= 600))
            {
                throw call.Error("JGraph:usb:NameValue", "Wait is a number of seconds from 0 to 600.");
            }

            wait = DeviceChecks.Numbers(w).First();
        }

        if (_dfu.DfuMode)
        {
            throw call.Error("JGraph:usb:DfuNotRuntime", "The device is already in DFU mode.");
        }

        Guard(call, () => _dfu.Detach(_dfu.Functional.DetachTimeout));
        if (!_dfu.Functional.WillDetach)
        {
            JgsBuiltins.Warn(call.Host, "JGraph:usb:DfuReset",
                $"The device enters DFU mode on a USB reset, which WinUSB cannot send from here; unplug and replug it within {_dfu.Functional.DetachTimeout} ms.");
        }

        if (call.Wanted == 0)
        {
            return [];
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(wait);
        while (true)
        {
            if (Simulation?.Detached is { } loader)
            {
                return [OpenSimulated(Session, Interpreter, loader)];
            }

            if (Simulation is null && _info.Location.Length > 0)
            {
                UsbDeviceInfo? found = UsbEnumerator.Devices().FirstOrDefault(d => d.Location == _info.Location && DfuInterface(d) is { DfuMode: true });
                if (found is not null)
                {
                    return [OpenInfo(Session, Interpreter, found, call.Line, call.Column)];
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw call.Error("JGraph:usb:DfuDetach",
                    $"No DFU-mode device appeared on port {(_info.Location.Length > 0 ? _info.Location : "(unknown)")} within {wait.ToString(CultureInfo.InvariantCulture)} seconds.");
            }

            Thread.Sleep(250);
        }
    }

    protected override void OnDelete() => _dfu.Dispose();
}
