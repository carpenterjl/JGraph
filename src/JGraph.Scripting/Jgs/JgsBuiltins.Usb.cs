using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using JGraph.Devices.Usb;
using JGraph.Scripting.Jgs.Devices;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The USB layer's script surface (device classes plan, stage D5, ADR 0188), a JGraph extension under
/// <c>jgraph.usb</c> with no MATLAB counterpart:
/// <list type="bullet">
/// <item><c>devices</c>: a table, one row per USB device, hubs included;</item>
/// <item><c>descriptors</c>: one device's descriptors, decoded;</item>
/// <item><c>tree</c>: the hub topology, printed or answered;</item>
/// <item><c>ports</c>: every hub port, and what is plugged into it;</item>
/// <item><c>watch</c>: a callback when a device arrives or leaves.</item>
/// </list>
/// Everything is read through SetupAPI, the configuration manager and the hubs' IOCTLs, the way
/// USBView reads it: no device is opened, and nothing needs administrator rights.
/// </summary>
internal static partial class JgsBuiltins
{
    private static readonly string[] UsbDeviceFilters = ["VendorID", "ProductID", "Class", "SerialNumber", "Driver", "Product"];

    /// <summary>The functions under <c>jgraph.usb</c>.</summary>
    private static Dictionary<string, JgsValue> UsbPackage(Interpreter interpreter) => new(StringComparer.Ordinal)
    {
        ["devices"] = UsbFunction("jgraph.usb.devices", (args, _, line, col) => OperatingSystem.IsWindows() ? UsbDevicesTable(args, line, col) : JgsValue.Null),
        ["descriptors"] = UsbFunction("jgraph.usb.descriptors", (args, _, line, col) => OperatingSystem.IsWindows() ? UsbDescriptorsOf(args, line, col) : JgsValue.Null),
        ["tree"] = UsbFunction("jgraph.usb.tree", (args, wanted, line, col) => OperatingSystem.IsWindows() ? UsbTree(interpreter, args, wanted, line, col) : JgsValue.Null),
        ["ports"] = UsbFunction("jgraph.usb.ports", (args, _, line, col) => OperatingSystem.IsWindows() ? UsbPorts(args, line, col) : JgsValue.Null),
        ["watch"] = UsbFunction("jgraph.usb.watch", (args, _, line, col) => OperatingSystem.IsWindows() ? UsbWatch(interpreter, args, line, col) : JgsValue.Null),
    };

    private static JgsValue UsbFunction(string name, Func<IReadOnlyList<JgsValue>, int, int, int, JgsValue> body)
    {
        JgsValue Guarded(IReadOnlyList<JgsValue> args, int wanted, int line, int col)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:NotSupported", $"{name} runs on Windows only.");
            }

            return body(args, wanted, line, col);
        }

        return JgsValue.Function(new BuiltinFunction(name, (args, line, col) => Guarded(args, 1, line, col))
        {
            KeepsStringArguments = true,
            TakesOutputCount = true,
            AutoCallsBare = true,
            MultiOutput = (args, wanted, line, col) => [Guarded(args, wanted, line, col)],
        });
    }

    /// <summary>The name-value filters every jgraph.usb function that picks devices takes; partial and case-blind names.</summary>
    internal static Func<UsbDeviceInfo, bool> UsbFilter(string function, IReadOnlyList<JgsValue> args, int first, int line, int col)
    {
        if ((args.Count - first) % 2 != 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NameValue",
                $"{function} takes name-value pairs; the names are {string.Join(", ", UsbDeviceFilters)}.");
        }

        var tests = new List<Func<UsbDeviceInfo, bool>>();
        for (int i = first; i < args.Count; i += 2)
        {
            string written = IsTextScalar(args[i]) ? TextOf(args[i]) : "";
            if (written.Length == 0 || !DeviceChecks.Match(written, UsbDeviceFilters, out string? name, out bool ambiguous) || ambiguous)
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:NameValue",
                    $"'{written}' is not a name {function} takes; the names are {string.Join(", ", UsbDeviceFilters)}.");
            }

            JgsValue value = args[i + 1];
            switch (name)
            {
                case "VendorID":
                {
                    int want = UsbId(value, line, col);
                    tests.Add(d => d.VendorId == want);
                    break;
                }

                case "ProductID":
                {
                    int want = UsbId(value, line, col);
                    tests.Add(d => d.ProductId == want);
                    break;
                }

                case "Class":
                    if (IsTextScalar(value))
                    {
                        string cls = TextOf(value);
                        tests.Add(d => UsbClassNames(d).Any(n => n.Contains(cls, StringComparison.OrdinalIgnoreCase)));
                    }
                    else if (DeviceChecks.NumericClasses.Contains(ClassOf(value, JgsDialect.Matlab)) && DeviceChecks.Count(value) == 1)
                    {
                        int code = (int)DeviceChecks.Numbers(value).First();
                        tests.Add(d => d.Class == code || UsbDescriptors.Configuration(d.ConfigurationDescriptor).Interfaces.Any(f => f.Class == code));
                    }
                    else
                    {
                        throw new JgsRuntimeException(line, col, "JGraph:usb:BadClass", "Class is a class code, or text in a class's name (\"HID\", \"CDC\").");
                    }

                    break;
                default:
                {
                    if (!IsTextScalar(value))
                    {
                        throw new JgsRuntimeException(line, col, "JGraph:usb:BadText", $"{name} is text.");
                    }

                    string text = TextOf(value);
                    tests.Add(name switch
                    {
                        "SerialNumber" => d => d.SerialNumber.Equals(text, StringComparison.OrdinalIgnoreCase),
                        "Driver" => d => d.Driver.Equals(text, StringComparison.OrdinalIgnoreCase)
                            || d.InterfaceDrivers.Values.Any(v => v.Equals(text, StringComparison.OrdinalIgnoreCase)),
                        _ => d => UsbName(d).Contains(text, StringComparison.OrdinalIgnoreCase),
                    });
                    break;
                }
            }
        }

        return d => tests.TrueForAll(t => t(d));
    }

    /// <summary>A vendor or product ID given as a number or as hex text ("2E8A", "0x2E8A").</summary>
    private static int UsbId(JgsValue value, int line, int col)
    {
        if (IsTextScalar(value))
        {
            string text = TextOf(value).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text[2..];
            }

            if (text.Length is > 0 and <= 4 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int id))
            {
                return id;
            }
        }
        else if (DeviceChecks.NumericClasses.Contains(ClassOf(value, JgsDialect.Matlab)) && DeviceChecks.Count(value) == 1
            && DeviceChecks.Numbers(value).First() is var n && n == Math.Floor(n) && n is >= 0 and <= 0xFFFF)
        {
            return (int)n;
        }

        throw new JgsRuntimeException(line, col, "JGraph:usb:BadId",
            "A USB vendor or product ID is a number from 0 to 65535, or hex text (\"2E8A\", \"0x2E8A\").");
    }

    /// <summary>The name a device is shown by: its product string, or what Windows calls it.</summary>
    private static string UsbName(UsbDeviceInfo d) => d.Product.Length > 0 ? d.Product : d.Description;

    /// <summary>The class names a device answers to: its own, and each interface's.</summary>
    private static IEnumerable<string> UsbClassNames(UsbDeviceInfo device)
    {
        yield return UsbDescriptors.ClassName(device.Class, device.Subclass, device.Protocol);
        foreach (UsbInterface f in UsbDescriptors.Configuration(device.ConfigurationDescriptor).Interfaces)
        {
            yield return f.ClassName;
        }
    }

    /// <summary>
    /// What the Class column says: the device's class, or for a composite (class 0, or a Miscellaneous
    /// device of interface associations) its interfaces' classes.
    /// </summary>
    private static string UsbClassColumn(UsbDeviceInfo d)
    {
        if (d.Class is 0x00 or 0xEF && d.ConfigurationDescriptor.Length > 0)
        {
            string[] names = UsbDescriptors.Configuration(d.ConfigurationDescriptor).Interfaces
                .Where(static f => f.AlternateSetting == 0).Select(static f => f.ClassName).Distinct().ToArray();
            if (names.Length > 0)
            {
                return string.Join(", ", names);
            }
        }

        return d.DeviceDescriptor.Length == 0 && d.IsHub ? "Hub" : UsbDescriptors.ClassName(d.Class, d.Subclass, d.Protocol);
    }

    private static string UsbHex(int id) => id.ToString("X4", CultureInfo.InvariantCulture);

    internal static string UsbSpeedName(UsbSpeed speed) => speed switch
    {
        UsbSpeed.Low => "low (1.5 Mbps)",
        UsbSpeed.Full => "full (12 Mbps)",
        UsbSpeed.High => "high (480 Mbps)",
        UsbSpeed.Super => "super (5 Gbps)",
        UsbSpeed.SuperPlus => "super+ (10 Gbps)",
        _ => "",
    };

    private static readonly string[] UsbTableColumns =
        ["VendorID", "ProductID", "Manufacturer", "Product", "SerialNumber", "Class", "Speed", "Location", "Driver", "Ports", "HIDCollections", "InstanceID"];

    /// <summary>One device's values, in the table's column order.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue[] UsbRow(UsbDeviceInfo d, Dictionary<string, List<string>> comPorts, Dictionary<string, List<string>> hid) =>
    [
        JgsValue.Str(UsbHex(d.VendorId)),
        JgsValue.Str(UsbHex(d.ProductId)),
        JgsValue.Str(d.Manufacturer),
        JgsValue.Str(UsbName(d)),
        JgsValue.Str(d.SerialNumber),
        JgsValue.Str(UsbClassColumn(d)),
        JgsValue.Str(UsbSpeedName(d.Speed)),
        JgsValue.Str(d.Location),
        JgsValue.Str(d.Driver),
        JgsValue.Str(comPorts.TryGetValue(d.InstanceId, out List<string>? names) ? string.Join(", ", names) : ""),
        JgsValue.Number(hid.TryGetValue(d.InstanceId, out List<string>? paths) ? paths.Count : 0),
        JgsValue.Str(d.InstanceId),
    ];

    /// <summary><c>T = jgraph.usb.devices(Name=Value…)</c>: a table, one row per device, in bus and port order.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue UsbDevicesTable(IReadOnlyList<JgsValue> args, int line, int col)
    {
        Func<UsbDeviceInfo, bool> keep = UsbFilter("jgraph.usb.devices", args, 0, line, col);
        List<UsbDeviceInfo> devices = UsbEnumerator.Devices().Where(keep).ToList();
        Dictionary<string, List<string>> comPorts = UsbCrossReference.ComPorts();
        Dictionary<string, List<string>> hid = UsbCrossReference.HidPaths();
        List<JgsValue[]> rows = devices.Select(d => UsbRow(d, comPorts, hid)).ToList();
        var columns = new List<JGraph.Data.TableColumn>();
        for (int c = 0; c < UsbTableColumns.Length; c++)
        {
            JgsValue column = c == 10
                ? JgsMatrix.FromColumnMajor(rows.Select(r => r[c].AsNumber).ToArray(), rows.Count, 1)
                : JgsValue.StringArray(rows.Select(r => r[c]).ToArray(), rows.Count, 1);
            columns.Add(TableColumnFrom("jgraph.usb.devices", UsbTableColumns[c], column, line, col));
        }

        return JgsValue.Table(new JGraph.Data.Table(columns));
    }

    /// <summary>A device as one struct with the table's fields: what a watch's event carries.</summary>
    [SupportedOSPlatform("windows")]
    internal static JgsValue UsbDeviceStruct(UsbDeviceInfo device)
    {
        JgsValue[] row = UsbRow(device, UsbCrossReference.ComPorts(), UsbCrossReference.HidPaths());
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int c = 0; c < UsbTableColumns.Length; c++)
        {
            fields[UsbTableColumns[c]] = c == 10 ? row[c] : JgsValue.StringScalar(DeviceChecks.Text(row[c]));
        }

        return JgsValue.Struct(fields);
    }

    /// <summary>The one device a call names: a table row of jgraph.usb.devices, an instance ID, or filters that match one.</summary>
    [SupportedOSPlatform("windows")]
    private static UsbDeviceInfo OneUsbDevice(string function, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NoArguments",
                $"{function} takes a row of jgraph.usb.devices, a device's InstanceID, or name-value pairs that match one device.");
        }

        List<UsbDeviceInfo> all = UsbEnumerator.Devices().ToList();
        List<UsbDeviceInfo> found;
        if (args.Count == 1 && args[0].Type == JgsType.Table)
        {
            JgsValue ids = TableColumnValue(args[0].AsTable, "InstanceID", line, col);
            if (!ids.IsStringArray || ids.ArrayLength != 1)
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:OneRow",
                    $"{function} takes one row of jgraph.usb.devices; this table has {ids.ArrayLength}.");
            }

            string id = ids.ElementAt(0).AsString;
            found = all.Where(d => d.InstanceId.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else if (args.Count == 1 && IsTextScalar(args[0]))
        {
            string id = TextOf(args[0]);
            found = all.Where(d => d.InstanceId.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            found = all.Where(UsbFilter(function, args, 0, line, col)).ToList();
        }

        return found.Count switch
        {
            1 => found[0],
            0 => throw new JgsRuntimeException(line, col, "JGraph:usb:NoDevice", $"No USB device matches; {function} found none."),
            _ => throw new JgsRuntimeException(line, col, "JGraph:usb:SeveralDevices",
                $"{found.Count} USB devices match; give {function} a SerialNumber too, or one row of jgraph.usb.devices."),
        };
    }

    /// <summary><c>d = jgraph.usb.descriptors(dev)</c>: the device, configuration and BOS descriptors, decoded.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue UsbDescriptorsOf(IReadOnlyList<JgsValue> args, int line, int col)
    {
        UsbDeviceInfo device = OneUsbDevice("jgraph.usb.descriptors", args, line, col);
        static JgsValue Num(double x) => JgsValue.Number(x);
        static JgsValue Str(string s) => JgsValue.StringScalar(s);
        static JgsValue Bytes(byte[] b) => JgsMatrix.FromColumnMajor(b.Select(static x => (double)x).ToArray(), 1, b.Length);
        string Text(int index) => device.Strings.TryGetValue(index, out string? s) ? s : "";
        static JgsValue Row(IEnumerable<Dictionary<string, JgsValue>> elements) => JgsValue.StructArray(elements.ToArray());

        UsbConfiguration configuration = UsbDescriptors.Configuration(device.ConfigurationDescriptor);
        JgsValue interfaces = Row(configuration.Interfaces.Select(f => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Number"] = Num(f.Number),
            ["AlternateSetting"] = Num(f.AlternateSetting),
            ["Class"] = Num(f.Class),
            ["Subclass"] = Num(f.Subclass),
            ["Protocol"] = Num(f.Protocol),
            ["ClassName"] = Str(f.ClassName),
            ["Name"] = Str(Text(f.NameIndex)),
            ["Driver"] = Str(InterfaceDriver(device, configuration, f.Number)),
            ["Endpoints"] = Row(f.Endpoints.Select(e => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Address"] = Num(e.Address),
                ["Direction"] = Str(e.Direction),
                ["Type"] = Str(e.TransferType),
                ["MaxPacketSize"] = Num(e.MaxPacketSize),
                ["Interval"] = Num(e.Interval),
            })),
            ["ClassSpecific"] = Row(f.ClassSpecific.Select(c => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = Str(c.Name),
                ["Type"] = Num(c.Type),
                ["Subtype"] = Num(c.Subtype),
                ["Fields"] = JgsValue.Struct(c.Fields.ToDictionary(static p => p.Key,
                    static p => p.Value is string s ? JgsValue.StringScalar(s) : JgsValue.Number((double)p.Value), StringComparer.Ordinal)),
                ["Bytes"] = Bytes(c.Bytes),
            })),
        }));
        JgsValue associations = Row(configuration.Associations.Select(a => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["FirstInterface"] = Num(a.FirstInterface),
            ["InterfaceCount"] = Num(a.InterfaceCount),
            ["Class"] = Num(a.Class),
            ["Subclass"] = Num(a.Subclass),
            ["Protocol"] = Num(a.Protocol),
            ["ClassName"] = Str(a.ClassName),
            ["Name"] = Str(Text(a.NameIndex)),
        }));
        JgsValue capabilities = Row(UsbDescriptors.Capabilities(device.BosDescriptor).Select(c => new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Type"] = Num(c.Type),
            ["Name"] = Str(c.Name),
            ["Platform"] = Str(c.PlatformId ?? ""),
            ["Bytes"] = Bytes(c.Bytes),
        }));
        JgsValue strings = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Languages"] = JgsMatrix.FromColumnMajor(device.Languages.Select(static l => (double)l).ToArray(), 1, device.Languages.Count),
            ["Index"] = JgsMatrix.FromColumnMajor(device.Strings.Keys.Order().Select(static k => (double)k).ToArray(), 1, device.Strings.Count),
            ["Text"] = JgsValue.StringArray(device.Strings.OrderBy(static p => p.Key).Select(static p => JgsValue.Str(p.Value)).ToArray(), 1, device.Strings.Count),
        });
        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["VendorID"] = Str(UsbHex(device.VendorId)),
            ["ProductID"] = Str(UsbHex(device.ProductId)),
            ["Release"] = Str(device.Release == 0 ? "" : $"{device.Release >> 8:X}.{device.Release & 0xFF:X2}"),
            ["Class"] = Num(device.Class),
            ["Subclass"] = Num(device.Subclass),
            ["Protocol"] = Num(device.Protocol),
            ["ClassName"] = Str(UsbDescriptors.ClassName(device.Class, device.Subclass, device.Protocol)),
            ["Manufacturer"] = Str(device.Manufacturer),
            ["Product"] = Str(device.Product),
            ["SerialNumber"] = Str(device.SerialNumber),
            ["Speed"] = Str(UsbSpeedName(device.Speed)),
            ["Location"] = Str(device.Location),
            ["Driver"] = Str(device.Driver),
            ["InstanceID"] = Str(device.InstanceId),
            ["Device"] = Bytes(device.DeviceDescriptor),
            ["Configuration"] = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Value"] = Num(configuration.Value),
                ["Name"] = Str(Text(configuration.NameIndex)),
                ["SelfPowered"] = JgsValue.Bool(configuration.SelfPowered),
                ["RemoteWakeup"] = JgsValue.Bool(configuration.RemoteWakeup),
                ["MaxPower"] = Num(configuration.MaxPowerMilliamps),
                ["Associations"] = associations,
                ["Interfaces"] = interfaces,
                ["Bytes"] = Bytes(device.ConfigurationDescriptor),
            }),
            ["Capabilities"] = capabilities,
            ["Strings"] = strings,
        });
    }

    /// <summary>
    /// The driver bound to an interface: its own devnode's, else that of the first interface of the
    /// association it belongs to (a video function's streaming interfaces are its control interface's),
    /// else the device's.
    /// </summary>
    private static string InterfaceDriver(UsbDeviceInfo device, UsbConfiguration configuration, int number)
    {
        if (device.InterfaceDrivers.TryGetValue(number, out string? own))
        {
            return own;
        }

        UsbAssociation? function = configuration.Associations.FirstOrDefault(a => number >= a.FirstInterface && number < a.FirstInterface + a.InterfaceCount);
        return function is not null && device.InterfaceDrivers.TryGetValue(function.FirstInterface, out string? shared) ? shared : device.Driver;
    }

    /// <summary><c>P = jgraph.usb.ports</c>: every hub's ports, connected or not, and what is on each.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue UsbPorts(IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NoArguments", "jgraph.usb.ports takes no arguments.");
        }

        var rows = UsbEnumerator.HubPorts();
        int n = rows.Count;
        JgsValue Strings(Func<(string Hub, UsbHubPort Port), string> pick) => JgsValue.StringArray(rows.Select(r => JgsValue.Str(pick(r))).ToArray(), n, 1);
        JgsValue Logical(Func<(string Hub, UsbHubPort Port), bool> pick)
        {
            JgsValue column = JgsValue.Array(rows.Select(r => JgsValue.Bool(pick(r))).ToArray());
            column.Reshape(n, n == 0 ? 0 : 1);
            return column;
        }

        return JgsValue.Table(new JGraph.Data.Table(
        [
            TableColumnFrom("jgraph.usb.ports", "Hub", Strings(static r => r.Hub), line, col),
            TableColumnFrom("jgraph.usb.ports", "Port", JgsMatrix.FromColumnMajor(rows.Select(static r => (double)r.Port.Port).ToArray(), n, 1), line, col),
            TableColumnFrom("jgraph.usb.ports", "Connected", Logical(static r => r.Port.Connected), line, col),
            TableColumnFrom("jgraph.usb.ports", "UserVisible", Logical(static r => r.Port.UserVisible), line, col),
            TableColumnFrom("jgraph.usb.ports", "Speed", Strings(static r => UsbSpeedName(r.Port.Speed)), line, col),
            TableColumnFrom("jgraph.usb.ports", "Device", Strings(static r => r.Port.DeviceInstanceId), line, col),
        ]));
    }

    /// <summary>
    /// <c>jgraph.usb.tree</c>: prints the topology as <c>lsusb -t</c> does, one line per device indented
    /// under its hub; <c>t = jgraph.usb.tree</c> answers it instead, a cell of root hubs, each a struct
    /// whose Children are its own.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue UsbTree(Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count != 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NoArguments", "jgraph.usb.tree takes no arguments.");
        }

        List<UsbDeviceInfo> devices = UsbEnumerator.Devices().ToList();
        var byParent = devices.GroupBy(static d => d.ParentInstanceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static g => g.Key, static g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(devices.Select(static d => d.InstanceId), StringComparer.OrdinalIgnoreCase);
        List<UsbDeviceInfo> roots = devices.Where(d => !ids.Contains(d.ParentInstanceId)).ToList();
        var text = new StringBuilder();

        JgsValue Node(UsbDeviceInfo d, int depth)
        {
            string where = depth == 0 ? "Bus " + d.Bus.ToString(CultureInfo.InvariantCulture)
                : "Port " + (d.Ports.Count > 0 ? d.Ports[^1] : 0).ToString(CultureInfo.InvariantCulture);
            text.Append(' ', depth * 4).Append(where).Append(": ")
                .Append(UsbHex(d.VendorId)).Append(':').Append(UsbHex(d.ProductId)).Append(' ').Append(UsbName(d))
                .Append(" [").Append(UsbClassColumn(d)).Append(", ").Append(d.Driver);
            if (d.Speed != UsbSpeed.Unknown)
            {
                text.Append(", ").Append(UsbSpeedName(d.Speed));
            }

            text.Append("]\n");
            List<UsbDeviceInfo> children = byParent.TryGetValue(d.InstanceId, out List<UsbDeviceInfo>? list) ? list : [];
            JgsValue[] kids = children.OrderBy(static c => c.Ports.Count > 0 ? c.Ports[^1] : 0).Select(c => Node(c, depth + 1)).ToArray();
            JgsValue cell = JgsValue.Cell(kids);
            cell.Reshape(kids.Length == 0 ? 0 : 1, kids.Length);
            return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = JgsValue.StringScalar(UsbName(d)),
                ["VendorID"] = JgsValue.StringScalar(UsbHex(d.VendorId)),
                ["ProductID"] = JgsValue.StringScalar(UsbHex(d.ProductId)),
                ["Location"] = JgsValue.StringScalar(d.Location),
                ["Driver"] = JgsValue.StringScalar(d.Driver),
                ["InstanceID"] = JgsValue.StringScalar(d.InstanceId),
                ["Children"] = cell,
            });
        }

        JgsValue[] tree = roots.OrderBy(static r => r.Bus).ThenBy(static r => r.InstanceId, StringComparer.OrdinalIgnoreCase).Select(r => Node(r, 0)).ToArray();
        if (wanted == 0)
        {
            interpreter.Host?.WriteOut(text.ToString());
            return JgsValue.Null;
        }

        JgsValue answer = JgsValue.Cell(tree);
        answer.Reshape(tree.Length == 0 ? 0 : 1, tree.Length);
        return answer;
    }

    /// <summary><c>w = jgraph.usb.watch(@fcn, Name=Value…)</c>: <c>fcn(w, evt)</c> when a matching device arrives or leaves.</summary>
    [SupportedOSPlatform("windows")]
    private static JgsValue UsbWatch(Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count == 0 || args[0].Type != JgsType.Function)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:WatchCallback",
                "jgraph.usb.watch takes a function handle first, called as fcn(watcher, event), then name-value pairs to filter devices.");
        }

        Func<UsbDeviceInfo, bool> filter = UsbFilter("jgraph.usb.watch", args, 1, line, col);
        JGraphScriptGlobals host = interpreter.Host
            ?? throw new JgsRuntimeException(line, col, "JGraph:usb:NoHost", "This session has no host.");
        try
        {
            return UsbWatchObject.Start(host.Devices, interpreter, args[0], filter);
        }
        catch (JGraph.Devices.DeviceOpenException e)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:WatchFailed", e.Message);
        }
    }
}
