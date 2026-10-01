using System.Globalization;
using System.Text;
using JGraph.Devices.Mtp;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>m = jgraph.usb.mtp(...)</c> (device classes plan, stage D12, ADR 0196): a phone, player or
/// camera that speaks MTP or PTP, through Windows Portable Devices. A path names an object by its
/// storage and folders, parted by <c>/</c> or <c>\</c>: <c>"Internal storage/DCIM/Camera"</c>.
/// <c>dir(m, path)</c> lists as <c>dir</c> does; <c>download(m, path, localFile)</c> and
/// <c>upload(m, localFile, folder)</c> copy a file; <c>mkdir(m, path)</c> makes folders;
/// <c>deleteObject(m, path)</c> deletes for good, as the device has no recycle bin; and
/// <c>capture(m)</c> asks a camera to take a picture.
/// </summary>
internal sealed class MtpObject : DeviceObject
{
    private static readonly string[] DirectoryFields = ["name", "folder", "date", "bytes", "isdir", "datenum"];

    private static readonly DeviceClass Declaration = Declare();

    private readonly IMtpDevice _device;
    private JgsValue _userData = JgsEmpty.Zero();

    private MtpObject(DeviceSession session, Interpreter interpreter, IMtpDevice device)
        : base(session, interpreter)
    {
        _device = device;
    }

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "closed" : $"{_device.Info.Name} ({_device.Properties.GetValueOrDefault("Protocol", "")})";

    /// <summary>The session's portable devices: jgraph.internal.mtpsim's, or the machine's through WPD.</summary>
    public static IMtpBackend Backend(DeviceSession session) =>
        session.MtpSimulation is { } simulated ? simulated
        : OperatingSystem.IsWindows() ? WpdDevices.Instance
        : NoDevices.Instance;

    /// <summary>A platform with no portable-device backend lists nothing.</summary>
    private sealed class NoDevices : IMtpBackend
    {
        public static readonly NoDevices Instance = new();

        public IReadOnlyList<MtpDeviceInfo> Devices() => [];

        public IMtpDevice Open(MtpDeviceInfo device) => throw new MtpException("Portable devices are supported on Windows only.");
    }

    // --- jgraph.usb.mtplist, jgraph.usb.mtp ----------------------------------------------------------------

    /// <summary><c>T = jgraph.usb.mtplist</c>: the portable devices, by name.</summary>
    public static JgsValue List(DeviceSession session, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        IReadOnlyList<MtpDeviceInfo> devices = Backend(session).Devices();
        int n = devices.Count;
        JgsValue Text(Func<MtpDeviceInfo, string> pick) => JgsValue.StringArray(devices.Select(d => JgsValue.Str(pick(d))).ToArray(), n, 1);
        (string Name, JgsValue Value)[] columns =
        [
            ("Name", Text(static d => d.Name)),
            ("Manufacturer", Text(static d => d.Manufacturer)),
            ("Description", Text(static d => d.Description)),
            ("DeviceID", Text(static d => d.Id)),
        ];
        return JgsValue.Table(new JGraph.Data.Table(columns.Select(c => JgsBuiltins.TableColumnFrom("jgraph.usb.mtplist", c.Name, c.Value, line, col)).ToList()));
    }

    private static string Quoted(IEnumerable<MtpDeviceInfo> devices) => string.Join(", ", devices.Select(static d => $"'{d.Name}'"));

    /// <summary><c>jgraph.usb.mtp()</c> for the one device, <c>mtp(name)</c>, <c>mtp(index)</c>, or <c>mtp(row)</c> of jgraph.usb.mtplist.</summary>
    public static JgsValue Open(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 1)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:Nargin", "Valid syntax is jgraph.usb.mtp(), jgraph.usb.mtp(NAME), jgraph.usb.mtp(INDEX), or one row of jgraph.usb.mtplist.");
        }

        IMtpBackend backend = Backend(session);
        IReadOnlyList<MtpDeviceInfo> devices = backend.Devices();
        if (devices.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NoDevice", "No portable device is connected. A phone must be unlocked and set to transfer files before Windows lists it.");
        }

        MtpDeviceInfo chosen;
        if (args.Count == 0)
        {
            chosen = devices.Count == 1
                ? devices[0]
                : throw new JgsRuntimeException(line, col, "JGraph:usb:SeveralDevices", $"{devices.Count} portable devices are connected; name one of {Quoted(devices)}.");
        }
        else if (args[0].Type == JgsType.Table)
        {
            JgsValue ids = JgsBuiltins.TableColumnValue(args[0].AsTable, "DeviceID", line, col);
            if (!ids.IsStringArray || ids.ArrayLength != 1)
            {
                throw new JgsRuntimeException(line, col, "JGraph:usb:OneRow", $"jgraph.usb.mtp takes one row of jgraph.usb.mtplist; this table has {ids.ArrayLength}.");
            }

            string id = ids.ElementAt(0).AsString;
            chosen = devices.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new JgsRuntimeException(line, col, "JGraph:usb:NoDevice", "That portable device is no longer connected.");
        }
        else if (DeviceChecks.IsText(args[0]))
        {
            string name = DeviceChecks.Text(args[0]);
            List<MtpDeviceInfo> partial = name.Length == 0 ? [] : devices.Where(d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
            chosen = devices.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? partial.Count switch
            {
                1 => partial[0],
                0 => throw new JgsRuntimeException(line, col, "JGraph:usb:NoDevice", $"No portable device is named '{name}'. The devices are: {Quoted(devices)}."),
                _ => throw new JgsRuntimeException(line, col, "JGraph:usb:SeveralDevices", $"'{name}' matches more than one portable device: {Quoted(partial)}. Give more of the name."),
            };
        }
        else if (DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(args[0])) && DeviceChecks.Count(args[0]) == 1
            && DeviceChecks.Numbers(args[0]).First() is var k && k == Math.Floor(k) && k >= 1 && k <= devices.Count)
        {
            chosen = devices[(int)k - 1];
        }
        else
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:NoDevice",
                $"The device must be a name, a row of jgraph.usb.mtplist, or an index from 1 to {devices.Count}.");
        }

        IMtpDevice device;
        try
        {
            device = backend.Open(chosen);
        }
        catch (MtpException e)
        {
            throw new JgsRuntimeException(line, col, "JGraph:usb:OpenFailed", e.Message);
        }

        var made = new MtpObject(session, interpreter, device);
        session.Remember(made);
        JgsValue value = JgsValue.External(made);
        JgsLifetime.Minted(value);
        return value;
    }

    // --- the object ------------------------------------------------------------------------------------------

    private static MtpObject Me(DeviceObject o) => (MtpObject)o;

    private static DeviceClass Declare()
    {
        static DeviceProperty Told(string name) => new(name, (o, _) => JgsValue.StringScalar(Me(o)._device.Properties.GetValueOrDefault(name, "")));
        var properties = new List<DeviceProperty>
        {
            new("Name", static (o, _) => JgsValue.StringScalar(Me(o)._device.Info.Name)),
            new("Manufacturer", static (o, _) => JgsValue.StringScalar(Me(o)._device.Info.Manufacturer)),
            Told("Model"),
            Told("SerialNumber"),
            Told("FirmwareVersion"),
            Told("Protocol"),
            Told("Type"),
            new("Storage", static (o, c) => Me(o).StorageTable(c)),
            new("UserData", static (o, _) => Me(o)._userData, static (o, v, _) => Me(o)._userData = v),
        };

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["dir"] = static c => Me(c.Target).Dir(c),
            ["download"] = static c => Me(c.Target).Download(c),
            ["upload"] = static c => Me(c.Target).Upload(c),
            ["mkdir"] = static c => Void(c, static c => Me(c.Target).MakeFolder(c)),
            ["deleteObject"] = static c => Void(c, static c => Me(c.Target).DeleteObject(c)),
            ["capture"] = static c => Void(c, static c => Me(c.Target).Capture(c)),
        };

        return new DeviceClass("jgraph.usb.Mtp", "Mtp", ["handle"], properties, methods,
            ["Mtp", "capture", "delete", "deleteObject", "dir", "download", "get", "isvalid", "mkdir", "set", "upload"],
            ["Name", "Manufacturer", "Model", "Protocol", "Type"]);
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

    /// <summary>Runs a device operation, turning the device layer's failures into JGraph:usb:MtpFailed.</summary>
    private static T Guard<T>(DeviceCall call, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (MtpException e)
        {
            throw call.Error("JGraph:usb:MtpFailed", e.Message);
        }
    }

    private static string PathArgument(DeviceCall call, int index, string syntax)
    {
        if (call.Args.Count <= index || !DeviceChecks.IsText(call.Args[index]))
        {
            throw call.Error("JGraph:usb:Nargin", $"Valid syntax is {syntax}.");
        }

        return DeviceChecks.Text(call.Args[index]);
    }

    /// <summary>The object a path names, or the refusal that says which name along it is missing.</summary>
    private (MtpEntry Entry, string Path) Find(DeviceCall call, string path) =>
        Guard(call, () => MtpPaths.Find(_device, path, out string missing)
            ?? throw call.Error("JGraph:usb:MtpNoObject", $"The device has no '{path}': '{missing}' was not found."));

    private JgsValue StorageTable(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        List<(MtpEntry Entry, (long Capacity, long Free) Space)> storages = Guard(call, () =>
            _device.Children(MtpPaths.RootId).Where(static e => e.IsStorage).Select(e => (e, _device.Space(e.Id))).ToList());
        int n = storages.Count;
        (string Name, JgsValue Value)[] columns =
        [
            ("Name", JgsValue.StringArray(storages.Select(static s => JgsValue.Str(s.Entry.Name)).ToArray(), n, 1)),
            ("Capacity", JgsMatrix.FromColumnMajor(storages.Select(static s => (double)s.Space.Capacity).ToArray(), n, 1)),
            ("Free", JgsMatrix.FromColumnMajor(storages.Select(static s => (double)s.Space.Free).ToArray(), n, 1)),
        ];
        return JgsValue.Table(new JGraph.Data.Table(columns.Select(c => JgsBuiltins.TableColumnFrom("Storage", c.Name, c.Value, call.Line, call.Column)).ToList()));
    }

    /// <summary><c>dir(m)</c>, <c>dir(m, path)</c>: the listing printed, or a struct array with dir's fields.</summary>
    private JgsValue[] Dir(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count > 1)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntax is dir(m) or dir(m, PATH).");
        }

        string asked = call.Args.Count == 1 ? PathArgument(call, 0, "dir(m) or dir(m, PATH)") : "";
        (MtpEntry at, string path) = Find(call, asked);
        List<MtpEntry> entries;
        string folder;
        if (at.IsFolder)
        {
            entries = Guard(call, () => _device.Children(at.Id)).ToList();
            folder = path;

            // The storages keep the device's order; a folder's contents are sorted as dir sorts.
            if (at.Id != MtpPaths.RootId)
            {
                entries.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
            }
        }
        else
        {
            entries = [at];
            string[] names = MtpPaths.Split(path);
            folder = MtpPaths.Join(names[..^1]);
        }

        if (call.Wanted == 0)
        {
            var sb = new StringBuilder();
            foreach (MtpEntry entry in entries)
            {
                sb.Append(entry.Name).Append(entry.IsFolder ? "/" : "").Append('\n');
            }

            call.Host.WriteOut(sb.ToString());
            return [];
        }

        var elements = new Dictionary<string, JgsValue>[entries.Count];
        for (int i = 0; i < elements.Length; i++)
        {
            MtpEntry entry = entries[i];
            DateTime? whole = entry.Modified is { } m ? m.AddTicks(-(m.Ticks % TimeSpan.TicksPerSecond)) : null;
            elements[i] = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["name"] = JgsValue.Str(entry.Name),
                ["folder"] = JgsValue.Str(folder),
                ["date"] = JgsValue.Str(whole?.ToString("dd-MMM-yyyy HH:mm:ss", CultureInfo.InvariantCulture) ?? ""),
                ["bytes"] = JgsValue.Number(entry.Size),
                ["isdir"] = JgsValue.Bool(entry.IsFolder),
                ["datenum"] = whole is { } w ? JgsValue.Number(w.ToOADate() + JgsTime.DatenumOffset) : JgsEmpty.Zero(),
            };
        }

        return [JgsValue.StructArray(new JgsStructArray(elements, DirectoryFields), elements.Length, 1)];
    }

    /// <summary><c>file = download(m, path)</c>, <c>download(m, path, localFile or folder)</c>: copies a file off the device, replacing a local file of that name.</summary>
    private JgsValue[] Download(DeviceCall call)
    {
        const string Syntax = "download(m, PATH) or download(m, PATH, LOCALFILE)";
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count is < 1 or > 2 || call.Wanted > 1)
        {
            throw call.Error("JGraph:usb:Nargin", $"Valid syntax is {Syntax}.");
        }

        (MtpEntry entry, string path) = Find(call, PathArgument(call, 0, Syntax));
        if (entry.IsFolder)
        {
            throw call.Error("JGraph:usb:MtpIsFolder", $"'{path}' is a folder; download takes one file. dir(m, path) lists a folder's files.");
        }

        string local = call.Host.ResolveForWrite(call.Args.Count == 2 ? PathArgument(call, 1, Syntax) : entry.Name);
        if (Directory.Exists(local))
        {
            local = Path.Combine(local, entry.Name);
        }

        if (Path.GetDirectoryName(local) is { Length: > 0 } parent && !Directory.Exists(parent))
        {
            throw call.Error("JGraph:usb:MtpLocalFile", $"The file could not be written to {local}: the folder {parent} does not exist.");
        }

        // The bytes land beside the target first, so a transfer that fails leaves no half file under its name.
        string part = local + ".part";
        try
        {
            using (FileStream to = File.Create(part))
            {
                Guard(call, () =>
                {
                    _device.Download(entry.Id, to);
                    return 0;
                });
            }

            File.Move(part, local, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            TryDelete(part);
            throw call.Error("JGraph:usb:MtpLocalFile", $"The file could not be written to {local}: {e.Message}");
        }
        catch
        {
            TryDelete(part);
            throw;
        }

        return call.Wanted == 1 ? [JgsValue.Str(local)] : [];
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary><c>path = upload(m, localFile, folder)</c>: copies a local file into a folder on the device, under its own name.</summary>
    private JgsValue[] Upload(DeviceCall call)
    {
        const string Syntax = "upload(m, LOCALFILE, FOLDER)";
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count != 2 || call.Wanted > 1)
        {
            throw call.Error("JGraph:usb:Nargin", $"Valid syntax is {Syntax}.");
        }

        string written = PathArgument(call, 0, Syntax);
        string local = call.Host.Resolve(written);
        if (!File.Exists(local))
        {
            throw call.Error("JGraph:usb:MtpLocalFile", $"The file {written} was not found.");
        }

        (MtpEntry folder, string path) = Find(call, PathArgument(call, 1, Syntax));
        if (!folder.IsFolder || folder.Id == MtpPaths.RootId)
        {
            throw call.Error("JGraph:usb:MtpNotFolder", folder.IsFolder
                ? "A file goes into a storage or a folder on it; dir(m) lists the storages."
                : $"'{path}' is a file, not a folder.");
        }

        string name = Path.GetFileName(local);
        if (MtpPaths.Pick(Guard(call, () => _device.Children(folder.Id)), name) is not null)
        {
            throw call.Error("JGraph:usb:MtpExists", $"'{path}/{name}' is already on the device; deleteObject(m, path) removes it first.");
        }

        try
        {
            using FileStream from = File.OpenRead(local);
            Guard(call, () => _device.Upload(folder.Id, name, from, from.Length));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw call.Error("JGraph:usb:MtpLocalFile", $"The file {written} could not be read: {e.Message}");
        }

        return call.Wanted == 1 ? [JgsValue.Str($"{path}/{name}")] : [];
    }

    /// <summary><c>mkdir(m, path)</c>: makes the folder, and the folders above it that are missing; one already there is left alone.</summary>
    private void MakeFolder(DeviceCall call)
    {
        const string Syntax = "mkdir(m, PATH)";
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count != 1)
        {
            throw call.Error("JGraph:usb:Nargin", $"Valid syntax is {Syntax}.");
        }

        string[] names = MtpPaths.Split(PathArgument(call, 0, Syntax));
        if (names.Length < 2)
        {
            throw call.Error("JGraph:usb:MtpNotFolder", "A folder is made on a storage: \"Storage/Folder\". dir(m) lists the storages.");
        }

        Guard(call, () =>
        {
            MtpEntry at = MtpPaths.Root;
            for (int i = 0; i < names.Length; i++)
            {
                MtpEntry? next = MtpPaths.Pick(_device.Children(at.Id), names[i]);
                if (next is null && i == 0)
                {
                    throw call.Error("JGraph:usb:MtpNoObject", $"The device has no storage '{names[0]}'; dir(m) lists the storages.");
                }

                if (next is { IsFolder: false })
                {
                    throw call.Error("JGraph:usb:MtpNotFolder", $"'{MtpPaths.Join(names[..(i + 1)])}' is a file, not a folder.");
                }

                at = next ?? new MtpEntry(_device.CreateFolder(at.Id, names[i]), names[i], true, false, 0, null);
            }

            return 0;
        });
    }

    /// <summary><c>deleteObject(m, path)</c>, <c>deleteObject(m, path, Recursive=true)</c>: deletes a file or folder for good.</summary>
    private void DeleteObject(DeviceCall call)
    {
        const string Syntax = "deleteObject(m, PATH) or deleteObject(m, PATH, Recursive=true)";
        LiveOrThrow(call.Line, call.Column);
        bool recursive = false;
        if (call.Args.Count == 3 && DeviceChecks.IsText(call.Args[1]) && DeviceChecks.Match(DeviceChecks.Text(call.Args[1]), ["Recursive"], out _, out _)
            && DeviceChecks.Count(call.Args[2]) == 1 && (call.Args[2].Type == JgsType.Bool || DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(call.Args[2]))))
        {
            recursive = DeviceChecks.Numbers(call.Args[2]).First() != 0;
        }
        else if (call.Args.Count != 1)
        {
            throw call.Error("JGraph:usb:Nargin", $"Valid syntax is {Syntax}.");
        }

        (MtpEntry entry, string path) = Find(call, PathArgument(call, 0, Syntax));
        if (entry.Id == MtpPaths.RootId || entry.IsStorage)
        {
            throw call.Error("JGraph:usb:MtpStorage", "A storage cannot be deleted; name a file or a folder on it.");
        }

        if (entry.IsFolder && !recursive && Guard(call, () => _device.Children(entry.Id)).Count > 0)
        {
            throw call.Error("JGraph:usb:MtpNotEmpty", $"'{path}' is not empty; deleteObject(m, path, Recursive=true) deletes it with everything in it.");
        }

        Guard(call, () =>
        {
            _device.Delete(entry.Id, recursive);
            return 0;
        });
    }

    private void Capture(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count != 0)
        {
            throw call.Error("JGraph:usb:Nargin", "Valid syntax is capture(m).");
        }

        Guard(call, () =>
        {
            _device.Capture();
            return 0;
        });
    }

    protected override void OnDelete() => _device.Dispose();
}
