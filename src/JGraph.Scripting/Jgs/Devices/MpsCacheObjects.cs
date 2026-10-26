using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using JGraph.Scripting.MatFile;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// MATLAB Production Server's persistence helpers as a session has them (compiler and SDK plan, stage
/// K15): <c>mps.cache.control</c>'s Controller, <c>mps.cache.connect</c>'s MATFileCache (a DataCache),
/// the Connection the two share, and <c>mps.sync.mutex</c>'s TimedMATFileMutex, over the MatlabTest
/// provider, a MAT-file service in a folder, transcribed from R2025b's toolbox/mps/persistence
/// (fixture <c>k15_mps_cache</c>). The Redis and MongoDB providers are not here (decision KD8: Redis is
/// out of scope). A mutex excludes here, which R2025b's MatlabTest mutex does not: its lock is a
/// folder made under a unique name, so every acquire succeeds (ADR 0271).
/// </summary>
internal sealed class MpsCacheState
{
    private static readonly ConditionalWeakTable<Interpreter, MpsCacheState> States = new();

    /// <summary>The connections by name, in the order made (<c>list</c> answers them sorted, as containers.Map keys are).</summary>
    public Dictionary<string, MpsConnectionObject> Connections { get; } = new(StringComparer.Ordinal);

    /// <summary>The MAT-file services started or attached in this session, by folder.</summary>
    public Dictionary<string, MatFileService> ActiveServices { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static MpsCacheState Of(Interpreter interpreter) => States.GetValue(interpreter, static _ => new MpsCacheState());

    public static JgsRuntimeException Error(DeviceCall call, string code, string message) => call.Error("MPS:MATLAB:Cache:" + code, message);
}

/// <summary>
/// The MatlabTest provider's service: caches stored as MAT-files in a folder, each a <c>name</c> list
/// and a <c>value</c> cell. R2025b writes a string array in a v7.3 file; JGraph writes a cell of char
/// rows in its version 7 form, which holds every value a cache may take (its v7.3 writer refuses
/// strings), and reads either (ADR 0271).
/// </summary>
internal sealed class MatFileService(string folder)
{
    /// <summary>The empty double, <c>[]</c>, a missing value reads as.</summary>
    public static JgsValue Nothing() => JgsMatrix.FromColumnMajor([], 0, 0);

    private const string Ext = ".mat";
    private const string HashPrefix = "#";
    private const string ConfigFile = ".configFile";

    public string Folder { get; } = folder;

    public bool Stopped { get; private set; }

    public bool KeepData { get; set; }

    public (bool Ok, string Message) Start()
    {
        if (Folder.Length > 0 && Directory.Exists(Folder))
        {
            return (false, $"Cannot start MAT-file service in folder {Folder} because that folder already exists. Try attach() instead.");
        }

        Directory.CreateDirectory(Folder);
        MatFileWriter.Write(Path.Combine(Folder, ConfigFile + Ext), [("KeepData", JgsValue.Bool(KeepData))]);
        return (true, "");
    }

    public (bool Ok, string Message) Attach() =>
        Folder.Length > 0 && !Directory.Exists(Folder)
            ? (false, $"Cannot attach MAT-file service to folder {Folder} because that folder does not exist. Try start() instead.")
            : (true, "");

    public (bool Ok, string Message) Detach() => Stopped ? (false, StoppedMessage) : (true, "");

    public (bool Ok, string Message) Stop()
    {
        if (Stopped)
        {
            return (false, StoppedMessage);
        }

        if (!KeepData && Directory.Exists(Folder))
        {
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (false, ex.Message);
            }
        }

        Stopped = true;
        return (true, "");
    }

    public (bool Ok, string Message) Ping() => Stopped ? (false, StoppedMessage) : (true, $"MAT-File service active in {Folder}.");

    private string StoppedMessage => $"MAT-File service in folder {Folder} has stopped.";

    private string CacheFile(string cache) => Path.Combine(Folder, cache + Ext);

    private string HashFile(string cache, string hashName) => Path.Combine(Folder, HashPrefix + hashName + "_" + cache + Ext);

    /// <summary>A file's names and values; none when the file is not there.</summary>
    private static (List<string> Names, List<JgsValue> Values) Load(string file)
    {
        var names = new List<string>();
        var values = new List<JgsValue>();
        if (!File.Exists(file))
        {
            return (names, values);
        }

        IReadOnlyList<(string Name, JgsValue Value)> variables = MatFileReader.Read(file);
        JgsValue? nameVar = variables.FirstOrDefault(static v => v.Name == "name").Value;
        JgsValue? valueVar = variables.FirstOrDefault(static v => v.Name == "value").Value;
        if (nameVar is not null)
        {
            if (nameVar.IsStringArray)
            {
                for (int i = 0; i < nameVar.ArrayLength; i++)
                {
                    names.Add(nameVar.ElementAt(i).AsString);
                }
            }
            else if (nameVar.Type == JgsType.Cell)
            {
                names.AddRange(nameVar.AsCell.Select(static e => e.Type == JgsType.String ? e.AsString : ""));
            }
            else if (nameVar.Type == JgsType.String)
            {
                names.Add(nameVar.AsString);
            }
        }

        if (valueVar is { Type: JgsType.Cell })
        {
            values.AddRange(valueVar.AsCell);
        }

        while (values.Count < names.Count)
        {
            values.Add(Nothing());
        }

        return (names, values);
    }

    private static void Save(string file, List<string> names, List<JgsValue> values)
    {
        JgsValue nameCell = JgsValue.Cell([.. names.Select(JgsValue.Str)]);
        nameCell.Reshape(1, names.Count);
        JgsValue valueCell = JgsValue.Cell([.. values]);
        valueCell.Reshape(1, values.Count);
        MatFileWriter.Write(file, [("name", nameCell), ("value", valueCell)]);
    }

    public List<string> Keys(string cache) => Load(CacheFile(cache)).Names;

    public int CountRemoteKeys(string cache) => Load(CacheFile(cache)).Names.Count;

    /// <summary>Writes name-value pairs: an existing name's value is replaced, a new one appended; answers how many were written.</summary>
    public int Write(string cache, IReadOnlyList<(string Name, JgsValue Value)> pairs)
    {
        string file = CacheFile(cache);
        (List<string> names, List<JgsValue> values) = Load(file);
        foreach ((string name, JgsValue value) in pairs)
        {
            int at = names.IndexOf(name);
            if (at >= 0)
            {
                values[at] = JgsValue.Share(value);
            }
            else
            {
                names.Add(name);
                values.Add(JgsValue.Share(value));
            }
        }

        Save(file, names, values);
        return pairs.Count;
    }

    /// <summary>The values of the names asked, <c>[]</c> for one the cache lacks.</summary>
    public JgsValue[] Read(string cache, IReadOnlyList<string> wanted)
    {
        (List<string> names, List<JgsValue> values) = Load(CacheFile(cache));
        var answer = new JgsValue[wanted.Count];
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = names.IndexOf(wanted[i]);
            answer[i] = at >= 0 ? JgsValue.Share(values[at]) : Nothing();
        }

        return answer;
    }

    public int Remove(string cache, IReadOnlyList<string> wanted)
    {
        string file = CacheFile(cache);
        if (!File.Exists(file))
        {
            return 0;
        }

        (List<string> names, List<JgsValue> values) = Load(file);
        int removed = 0;
        foreach (string name in wanted)
        {
            int at = names.IndexOf(name);
            if (at >= 0)
            {
                names.RemoveAt(at);
                values.RemoveAt(at);
                removed++;
            }
        }

        if (removed > 0)
        {
            Save(file, names, values);
        }

        return removed;
    }

    /// <summary>The bytes a value takes stored, as whos counts a cell element: the cell's 120 and the value's own; NaN for a name the cache lacks.</summary>
    public double[] Bytes(string cache, IReadOnlyList<string> wanted)
    {
        (List<string> names, List<JgsValue> values) = Load(CacheFile(cache));
        var sizes = new double[wanted.Count];
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = names.IndexOf(wanted[i]);
            sizes[i] = at < 0 ? double.NaN : 120 + ValueBytes(values[at]);
        }

        return sizes;
    }

    private static double ValueBytes(JgsValue value) => value.Type switch
    {
        JgsType.Number or JgsType.Bool => ElementSize(value),
        JgsType.String => value.IsCharMatrix ? 2.0 * value.CharMatrixRows().Sum(static r => r.Length) : 2.0 * value.AsString.Length,
        JgsType.Array when value.IsStringArray => value.AsCell.Sum(static e => 2.0 * (e.Type == JgsType.String ? e.AsString.Length : 0)),
        JgsType.Array => value.ArrayLength * ElementSize(value),
        JgsType.Cell => value.AsCell.Sum(static e => 120 + ValueBytes(e)),
        _ => 0,
    };

    private static double ElementSize(JgsValue value) => value.Type == JgsType.Bool ? 1 : value.NumericClass switch
    {
        JgsNumericClass.Single or JgsNumericClass.Int32 or JgsNumericClass.UInt32 => 4,
        JgsNumericClass.Int8 or JgsNumericClass.UInt8 => 1,
        JgsNumericClass.Int16 or JgsNumericClass.UInt16 => 2,
        _ => 8,
    };

    public bool HasHash(string cache, string hashName) => File.Exists(HashFile(cache, hashName));

    public (List<string> Names, List<JgsValue> Values) HashGet(string cache, string hashName) => Load(HashFile(cache, hashName));

    public void HashSet(string cache, string hashName, IReadOnlyList<(string Name, JgsValue Value)> pairs)
    {
        string file = HashFile(cache, hashName);
        (List<string> names, List<JgsValue> values) = Load(file);
        foreach ((string name, JgsValue value) in pairs)
        {
            int at = names.IndexOf(name);
            if (at >= 0)
            {
                values[at] = JgsValue.Share(value);
            }
            else
            {
                names.Add(name);
                values.Add(JgsValue.Share(value));
            }
        }

        Save(file, names, values);
    }

    public void HashRemove(string cache, string hashName, IReadOnlyList<string> wanted)
    {
        string file = HashFile(cache, hashName);
        if (!File.Exists(file))
        {
            return;
        }

        (List<string> names, List<JgsValue> values) = Load(file);
        bool changed = false;
        foreach (string name in wanted)
        {
            int at = names.IndexOf(name);
            if (at >= 0)
            {
                names.RemoveAt(at);
                values.RemoveAt(at);
                changed = true;
            }
        }

        if (changed)
        {
            Save(file, names, values);
        }
    }

    /// <summary>A composite key's fingerprint: SHA-256 of its large-notation JSON (R2025b's fingerprint is a builtin of its own).</summary>
    public static string Fingerprint(JgsValue key)
    {
        string text;
        try
        {
            text = JgsMpsJson.Encode(key, JgsMpsJson.Notation.Large, JgsMpsJson.NanInfType.String, false);
        }
        catch (JgsMpsJson.Refused)
        {
            text = key.ToString() ?? "";
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}

/// <summary>A tri-state or location member: <c>mps.cache.TriStateBoolean</c> (True, False, Unknown, a double) and <c>mps.cache.Location</c> (Local, Remote).</summary>
internal sealed class MpsEnumValue : DeviceObject
{
    public const string TriStateBoolean = "mps.cache.TriStateBoolean";
    public const string Location = "mps.cache.Location";

    private static readonly Dictionary<string, DeviceClass> Classes = new(StringComparer.Ordinal);

    private MpsEnumValue(DeviceSession session, Interpreter interpreter, string className, string member)
        : base(session, interpreter)
    {
        Choice = member;
        lock (Classes)
        {
            if (!Classes.TryGetValue(className, out DeviceClass? declared))
            {
                Classes[className] = declared = Declare(className);
            }

            Class = declared;
        }
    }

    public string Choice { get; }

    public override DeviceClass Class { get; }

    public double Number => Choice switch { "True" => 1, "False" => 0, "Unknown" => -1, _ => 0 };

    public static JgsValue Of(DeviceSession session, Interpreter interpreter, string className, string member) =>
        JgsValue.External(new MpsEnumValue(session, interpreter, className, member));

    public bool Matches(JgsValue value) =>
        value.AsExternalOrNull() is MpsEnumValue other ? other.ClassName == ClassName && other.Choice == Choice
        : DeviceChecks.IsText(value) ? DeviceChecks.Text(value) == Choice
        : ClassName == TriStateBoolean && DeviceChecks.Count(value) == 1 && value.Type is JgsType.Number or JgsType.Bool && value.AsNumber == Number;

    public override string? Summary() => Choice;

    protected override string ShortDisplay() => $"{Class.ShortName} enumeration\n\n    {Choice}";

    protected override void OnDelete()
    {
    }

    private static DeviceClass Declare(string className)
    {
        static MpsEnumValue Me(DeviceObject o) => (MpsEnumValue)o;
        static JgsValue[] One(DeviceCall call, JgsValue value) =>
            call.Args.Count != 0 ? throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.") : [value];

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["char"] = static c => One(c, JgsValue.Str(Me(c.Target).Choice)),
            ["string"] = static c => One(c, JgsValue.StringScalar(Me(c.Target).Choice)),
            ["isequal"] = static c => [JgsValue.Bool(c.Args.All(a => Me(c.Target).Matches(a)))],
            ["eq"] = static c => [JgsValue.Bool(c.Args.Count == 1 && Me(c.Target).Matches(c.Args[0]))],
            ["ne"] = static c => [JgsValue.Bool(!(c.Args.Count == 1 && Me(c.Target).Matches(c.Args[0])))],
        };
        if (className == TriStateBoolean)
        {
            methods["double"] = static c => One(c, JgsValue.Number(Me(c.Target).Number));
            methods["logical"] = static c => One(c, JgsValue.Bool(Me(c.Target).Number != 0));
        }

        string shortName = className[(className.LastIndexOf('.') + 1)..];
        return new DeviceClass(className, shortName, className == TriStateBoolean ? ["double"] : [], [], methods, [], []);
    }
}

/// <summary>
/// <c>mps.cache.FolderConnection</c>: the named link to a MAT-file service's folder that a Controller,
/// a cache and a mutex share; its <c>ServiceHandle</c> is the service once started or attached.
/// </summary>
internal sealed class MpsConnectionObject : DeviceObject
{
    private static readonly DeviceClass Declaration = new(
        "mps.cache.FolderConnection",
        "FolderConnection",
        ["mps.cache.Connection", "handle"],
        [
            new DeviceProperty("Folder", static (o, _) => JgsValue.Str(((MpsConnectionObject)o).Folder)),
            new DeviceProperty("ConnectionName", static (o, _) => JgsValue.Str(((MpsConnectionObject)o).ConnectionName)),
            new DeviceProperty("ProviderName", static (o, _) => JgsValue.Str(((MpsConnectionObject)o).ProviderName)),
            new DeviceProperty("Operations", static (o, _) => JgsValue.StringScalar(((MpsConnectionObject)o).Operations)),
            new DeviceProperty("CtrlParams", static (o, _) => JgsValue.Str(((MpsConnectionObject)o).CtrlParams), Hidden: true),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal),
        [],
        ["Folder", "ConnectionName", "ProviderName", "Operations"]);

    public MpsConnectionObject(DeviceSession session, Interpreter interpreter, string name, string provider, string folder, string operations)
        : base(session, interpreter)
    {
        ConnectionName = name;
        ProviderName = provider;
        Folder = folder;
        Operations = operations;
    }

    public override DeviceClass Class => Declaration;

    public string ConnectionName { get; }

    public string ProviderName { get; }

    public string Folder { get; }

    public string Operations { get; }

    public string CtrlParams => $"--folder \"{Folder}\"";

    /// <summary>The service, while the connection is active.</summary>
    public MatFileService? ServiceHandle { get; set; }

    protected override void OnDelete()
    {
    }
}

/// <summary><c>mps.cache.Controller</c>: starts, stops, attaches and detaches the service of a connection.</summary>
internal sealed class MpsControllerObject : DeviceObject
{
    private static readonly DeviceClass Declaration = new(
        "mps.cache.Controller",
        "Controller",
        ["dynamicprops", "handle"],
        [
            new DeviceProperty("ActiveConnection", static (o, c) => ((MpsControllerObject)o).TriState(c, ((MpsControllerObject)o)._active ? "True" : "False")),
            new DeviceProperty("ManageService", static (o, c) => ((MpsControllerObject)o).TriState(c, ((MpsControllerObject)o)._manage)),
            new DeviceProperty("ProviderName", static (o, _) => JgsValue.Str(((MpsControllerObject)o).Connection.ProviderName)),
            new DeviceProperty("ConnectionName", static (o, _) => JgsValue.Str(((MpsControllerObject)o).Connection.ConnectionName)),
            new DeviceProperty("Folder", static (o, _) => JgsValue.Str(((MpsControllerObject)o).Connection.Folder)),
            new DeviceProperty("Operations", static (o, _) => JgsValue.StringScalar(((MpsControllerObject)o).Connection.Operations)),
            new DeviceProperty("CtrlParams", static (o, _) => JgsValue.Str(((MpsControllerObject)o).Connection.CtrlParams), Hidden: true),
            new DeviceProperty("ConnectionClass", static (o, _) => JgsValue.Str(((MpsControllerObject)o).Connection.ClassName), Hidden: true),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["start"] = static c => Me(c).Start(c),
            ["stop"] = static c => Me(c).Stop(c),
            ["attach"] = static c => Me(c).Attach(c),
            ["detach"] = static c => Me(c).Detach(c),
            ["restart"] = static c => Me(c).Restart(c),
            ["ping"] = static c => Me(c).Ping(c),
            ["version"] = static c => Me(c).Version(c),
            ["delete"] = static c => Me(c).DeleteMethod(),
        },
        ["Controller", "addlistener", "addprop", "attach", "delete", "detach", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "ping", "restart", "start", "stop", "version"],
        ["ActiveConnection", "ManageService", "ProviderName", "ConnectionName", "Folder", "Operations"]);

    private readonly MpsCacheState _state;
    private bool _active;
    private string _manage = "Unknown";

    public MpsControllerObject(DeviceSession session, Interpreter interpreter, MpsCacheState state, MpsConnectionObject connection)
        : base(session, interpreter)
    {
        _state = state;
        Connection = connection;
    }

    public override DeviceClass Class => Declaration;

    public MpsConnectionObject Connection { get; }

    private static MpsControllerObject Me(DeviceCall call) => (MpsControllerObject)call.Target;

    private JgsValue TriState(DeviceCall call, string member) => MpsEnumValue.Of(Session, Interpreter, MpsEnumValue.TriStateBoolean, member);

    private static JgsValue[] None(DeviceCall call) =>
        call.Wanted > 0 ? throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.") : [];

    private static void NoArguments(DeviceCall call)
    {
        if (call.Args.Count != 0)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }
    }

    private JgsRuntimeException ActionFailed(DeviceCall call, string action, string on, string reason) =>
        MpsCacheState.Error(call, "ControlActionFailed", $"Could not perform action \"{action}\" on {on} service. Reason:\n{reason}");

    private string NoServiceConnection => $"Connection \"{Connection.ConnectionName}\" to {Connection.ProviderName} service is invalid.";

    /// <summary>The control script's action: start and attach may run without an active connection, the rest may not.</summary>
    private void Invoke(DeviceCall call, string action)
    {
        if (action is not ("start" or "attach") && !_active)
        {
            throw ActionFailed(call, action, Connection.ConnectionName, NoServiceConnection);
        }

        string folder = Connection.Folder;
        (bool ok, string message) result;
        switch (action)
        {
            case "start":
            {
                var service = new MatFileService(folder);
                result = service.Start();
                if (result.ok)
                {
                    _state.ActiveServices[folder] = service;
                    Connection.ServiceHandle = service;
                }

                break;
            }

            case "stop":
            {
                _state.ActiveServices.Remove(folder, out MatFileService? service);
                result = service?.Stop() ?? (false, NoServiceConnection);
                break;
            }

            case "attach":
            {
                if (!_state.ActiveServices.TryGetValue(folder, out MatFileService? service))
                {
                    service = new MatFileService(folder);
                }

                result = service.Attach();
                if (result.ok)
                {
                    _state.ActiveServices[folder] = service;
                    Connection.ServiceHandle = service;
                }

                break;
            }

            case "detach":
                result = _state.ActiveServices.TryGetValue(folder, out MatFileService? attached) ? attached.Detach() : (false, NoServiceConnection);
                if (result.ok)
                {
                    _state.ActiveServices.Remove(folder);
                }

                break;
            case "ping":
                result = _state.ActiveServices.TryGetValue(folder, out MatFileService? pinged) ? pinged.Ping() : (false, NoServiceConnection);
                if (result.ok)
                {
                    LastPing = result.message;
                }

                break;
            default:
                result = (true, "");
                break;
        }

        if (!result.ok)
        {
            throw ActionFailed(call, action, Connection.ProviderName, result.message);
        }
    }

    private string LastPing { get; set; } = "";

    private JgsValue[] Start(DeviceCall call)
    {
        NoArguments(call);
        if (_manage == "False")
        {
            throw MpsCacheState.Error(call, "ServiceConnected", $"Already connected to {Connection.ProviderName} service on connection \"{Connection.ConnectionName}\". Cannot perform action: \"start\".");
        }

        if (_active)
        {
            if (_state.ActiveServices.ContainsKey(Connection.Folder))
            {
                JgsBuiltins.Warn(call.Host, "MPS:MATLAB:Cache:MaybeRestartService",
                    $"{Connection.ProviderName} service on connection \"{Connection.ConnectionName}\" already started. If restart is necessary, call the restart method.");
            }
            else
            {
                _active = false;
                JgsBuiltins.Warn(call.Host, "MPS:MATLAB:Cache:RestartQuitService",
                    $"{Connection.ProviderName} service on connection \"{Connection.ConnectionName}\" quit; starting it again.");
            }
        }

        if (!_active)
        {
            Invoke(call, "start");
            _manage = "True";
            _active = true;
        }

        return None(call);
    }

    private JgsValue[] Stop(DeviceCall call)
    {
        NoArguments(call);
        if (_manage == "False")
        {
            throw MpsCacheState.Error(call, "ConnectionManagementMismatch", $"Cannot perform action \"stop\" because connection \"{Connection.ConnectionName}\" requires the \"detach\" command.");
        }

        Invoke(call, "stop");
        _active = false;
        _manage = "Unknown";
        Connection.ServiceHandle = null;
        return None(call);
    }

    private JgsValue[] Attach(DeviceCall call)
    {
        NoArguments(call);
        if (_manage == "True")
        {
            throw MpsCacheState.Error(call, "ServiceConnected", $"Already connected to {Connection.ProviderName} service on connection \"{Connection.ConnectionName}\". Cannot perform action: \"attach\".");
        }

        if (_active)
        {
            if (!_state.ActiveServices.ContainsKey(Connection.Folder))
            {
                _active = false;
                throw ActionFailed(call, "ping", Connection.ConnectionName, NoServiceConnection);
            }

            JgsBuiltins.Warn(call.Host, "MPS:MATLAB:Cache:ServiceConnected",
                $"Already connected to {Connection.ProviderName} service on connection \"{Connection.ConnectionName}\". Cannot perform action: \"attach\".");
        }

        Invoke(call, "attach");
        _active = true;
        _manage = "False";
        return None(call);
    }

    private JgsValue[] Detach(DeviceCall call)
    {
        NoArguments(call);
        if (_manage == "True")
        {
            throw MpsCacheState.Error(call, "ConnectionManagementMismatch", $"Cannot perform action \"detach\" because connection \"{Connection.ConnectionName}\" requires the \"stop\" command.");
        }

        Invoke(call, "detach");
        _active = false;
        _manage = "Unknown";
        Connection.ServiceHandle = null;
        return None(call);
    }

    private JgsValue[] Restart(DeviceCall call)
    {
        NoArguments(call);
        if (_manage == "False")
        {
            throw MpsCacheState.Error(call, "ConnectionManagementMismatch", $"Cannot perform action \"restart\" because connection \"{Connection.ConnectionName}\" requires the \"detach\" command.");
        }

        if (!_active)
        {
            throw MpsCacheState.Error(call, "MaybeStartService", $"{Connection.ProviderName} service on connection \"{Connection.ConnectionName}\" not started, cannot restart. If start is necessary, call the start method.");
        }

        Invoke(call, "stop");
        Invoke(call, "start");
        _active = true;
        return None(call);
    }

    private JgsValue[] Ping(DeviceCall call)
    {
        NoArguments(call);
        Invoke(call, "ping");
        if (call.Wanted < 1)
        {
            call.Host.WriteOut(LastPing.Trim() + "\n");
        }

        return [JgsValue.True];
    }

    private JgsValue[] Version(DeviceCall call)
    {
        NoArguments(call);
        Invoke(call, "version");
        string version = "JGraph " + (typeof(MpsControllerObject).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        if (call.Wanted < 1)
        {
            call.Host.WriteOut(version + "\n");
        }

        return [JgsValue.Str(version)];
    }

    private JgsValue[] DeleteMethod()
    {
        Delete();
        return [];
    }

    protected override void OnDelete()
    {
        if (_active && _manage == "True" && _state.ActiveServices.Remove(Connection.Folder, out MatFileService? service))
        {
            service.Stop();
            Connection.ServiceHandle = null;
        }
    }

    protected override string ShortDisplay()
    {
        var sb = new StringBuilder("Controller with properties:\n");
        foreach (string name in Class.DisplayProperties)
        {
            sb.Append("\n    ").Append(name).Append(": ").Append(Quoted(Class.Find(name)!.Get(this, new DeviceCall { Target = this, Args = [] })));
        }

        return sb.ToString();
    }

    /// <summary>A property value as R2025b's display writes it: a char row quoted, a string double-quoted, an enumeration member by its name.</summary>
    internal static string Quoted(JgsValue value) =>
        value.AsExternalOrNull() is MpsEnumValue member ? member.Choice
        : value.Type == JgsType.String && !value.IsCharMatrix ? "'" + value.AsString + "'"
        : value.Type == JgsType.Bool ? (value.AsBool ? "1" : "0")
        : Shown(value);
}

/// <summary>
/// <c>mps.cache.MATFileCache</c>, a DataCache: keys and values in a MAT-file service, read and written
/// as <c>c.key</c>, <c>c.key = v</c>, <c>get</c>, <c>put</c>, <c>keys</c>, <c>isKey</c>, <c>remove</c>,
/// <c>clear</c>, <c>length</c>, <c>bytes</c>, with <c>retain</c>'s locally held keys written by
/// <c>flush</c> and <c>purge</c>; a struct key is a composite key kept by its fingerprint.
/// </summary>
internal sealed class MpsCacheObject : DeviceObject
{
    private const string CompositeKeyVar = "compositeKey";

    private static readonly string[] MethodNames =
        ["bytes", "clear", "countRemoteKeys", "delete", "flush", "get", "getp", "isKey", "keys", "length", "purge", "put", "remove", "retain", "save"];

    private static readonly DeviceClass Declaration = new(
        "mps.cache.MATFileCache",
        "MATFileCache",
        ["mps.cache.DataCache", "handle", "matlab.mixin.CustomDisplay"],
        [],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["keys"] = static c => [Me(c).KeysMethod(c)],
            ["isKey"] = static c => [Me(c).IsKey(c)],
            ["retain"] = static c => Me(c).Retain(c),
            ["get"] = static c => Me(c).Get(c),
            ["put"] = static c => Me(c).Put(c),
            ["purge"] = static c => Me(c).Purge(c),
            ["flush"] = static c => Me(c).Flush(c),
            ["remove"] = static c => [Me(c).Remove(c)],
            ["clear"] = static c => [Me(c).Clear(c)],
            ["length"] = static c => [Me(c).Length(c)],
            ["bytes"] = static c => [Me(c).Bytes(c)],
            ["countRemoteKeys"] = static c => [JgsValue.Number(Me(c).CountRemote())],
            ["getp"] = static c => [Me(c).Getp(c)],
            ["save"] = static c => { Me(c).FlushLocal(); return []; },
            ["delete"] = static c => { Me(c).FlushLocal(); Me(c).Delete(); return []; },
        },
        ["MATFileCache", "addlistener", "bytes", "clear", "countRemoteKeys", "delete", "eq", "findobj", "findprop", "flush", "ge", "get", "getp", "gt", "isKey", "isvalid", "keys", "le", "length", "listener", "lt", "ne", "notify", "purge", "put", "remove", "retain", "save", "subsasgn", "subsref"],
        []);

    private readonly MpsCacheState _state;
    private readonly List<string> _localNames = [];
    private readonly Dictionary<string, JgsValue> _localData = new(StringComparer.Ordinal);
    private readonly List<string> _modified = [];
    private readonly Dictionary<string, JgsValue> _compositeKeys = new(StringComparer.Ordinal);
    private bool _hasLocalData;

    public MpsCacheObject(DeviceSession session, Interpreter interpreter, MpsCacheState state, string name, MpsConnectionObject connection, JgsValue localKeys)
        : base(session, interpreter)
    {
        _state = state;
        Name = name;
        Connection = connection;
        LocalKeys = localKeys;
        InitLocalData();
    }

    /// <summary>An empty cell, 0-by-0.</summary>
    private static JgsValue EmptyCell()
    {
        JgsValue cell = JgsValue.Cell([]);
        cell.Reshape(0, 0);
        return cell;
    }

    public override DeviceClass Class => Declaration;

    public string Name { get; }

    public MpsConnectionObject Connection { get; }

    /// <summary>The keys held locally: a cell of names, <c>true</c> for every key, <c>false</c> or an empty cell for none.</summary>
    public JgsValue LocalKeys { get; private set; }

    private static MpsCacheObject Me(DeviceCall call) => (MpsCacheObject)call.Target;

    /// <summary>
    /// The service, reconnected when the connection lost it and the folder's service is still active
    /// (R2025b's ResourceHandle validator); else R2025b's refusal (k15_mps_cache: cache_after_stop).
    /// </summary>
    private MatFileService Service
    {
        get
        {
            if (Connection.ServiceHandle is { Stopped: false } held)
            {
                return held;
            }

            if (_state.ActiveServices.TryGetValue(Connection.Folder, out MatFileService? active) && !active.Stopped)
            {
                Connection.ServiceHandle = active;
                return active;
            }

            throw new JgsRuntimeException(0, 0, "MPS:MATLAB:Cache:InactiveService",
                $"{Connection.ProviderName} service on connection \"{Connection.ConnectionName}\" not active. Use Controller object to start or attach to service.");
        }
    }

    // --- the dot: a key read or written ---------------------------------------------------------------

    protected override bool TryMember(string name, DeviceCall call, out JgsValue value)
    {
        value = ReadOne(name);
        if (IsEmpty(value) && MethodNames.Contains(name, StringComparer.Ordinal))
        {
            JgsValue[] answer = Class.Methods[name](call);
            value = answer.Length > 0 ? answer[0] : JgsValue.Null;
        }

        return true;
    }

    protected override bool TrySetMember(string name, JgsValue value, DeviceCall call)
    {
        if (MethodNames.Contains(name, StringComparer.Ordinal))
        {
            JgsBuiltins.Warn(call.Host, "MPS:MATLAB:Cache:VariableShadowsMethod", $"Variable \"{name}\" shadows a method of the same name. Use get(c, \"{name}\") to retrieve its value.");
        }

        Store([JgsValue.Str(name)], [value]);
        return true;
    }

    private static bool IsEmpty(JgsValue value) => value.Type switch
    {
        JgsType.Null => true,
        JgsType.Array or JgsType.Cell => value.ArrayLength == 0,
        JgsType.String => !value.IsCharMatrix && value.AsString.Length == 0,
        _ => false,
    };

    // --- keys -----------------------------------------------------------------------------------------

    /// <summary>A key as the service stores it: a struct by its fingerprint, text as given, anything else by its text.</summary>
    private string Simple(JgsValue key)
    {
        if (key.Type == JgsType.Struct)
        {
            string hash = MatFileService.Fingerprint(key);
            _compositeKeys[hash] = key;
            return hash;
        }

        if (DeviceChecks.IsText(key))
        {
            return DeviceChecks.Text(key);
        }

        // string(5) is "5": a number keys by its text, as R2025b's string() of it (cache_composite_keys: 5).
        return key.Type is JgsType.Number or JgsType.Bool ? JgsSprintf.FormatScalarGeneral(key.AsNumber) : DeviceChecks.ClassOf(key);
    }

    private static bool IsComposite(JgsValue key) => key.Type == JgsType.Struct;

    /// <summary>The keys named by the arguments: one cell or string array flattened, else each argument.</summary>
    private static List<JgsValue> KeyList(IReadOnlyList<JgsValue> args)
    {
        if (args.Count == 1 && args[0].Type == JgsType.Cell)
        {
            return [.. args[0].AsCell];
        }

        if (args.Count == 1 && args[0].IsStringArray)
        {
            return [.. Enumerable.Range(0, args[0].ArrayLength).Select(i => JgsValue.Str(args[0].ElementAt(i).AsString))];
        }

        return [.. args];
    }

    private static JgsValue Column(IEnumerable<JgsValue> items)
    {
        JgsValue[] cells = [.. items];
        JgsValue cell = JgsValue.Cell(cells);
        cell.Reshape(cells.Length, cells.Length == 0 ? 1 : 1);
        return cell;
    }

    /// <summary>A list of names as flush and purge answer it: a column, or a 0-by-0 cell when there are none.</summary>
    private static JgsValue NameList(IEnumerable<string> names)
    {
        string[] list = [.. names];
        return list.Length == 0 ? EmptyCell() : Column(list.Select(JgsValue.Str));
    }

    private static JgsValue Int64(long n)
    {
        JgsValue value = JgsValue.Number(n);
        value.SetNumericClass(JgsNumericClass.Int64);
        return value;
    }

    private static string? LocationOf(JgsValue arg, DeviceCall call) =>
        arg.AsExternalOrNull() is MpsEnumValue { ClassName: MpsEnumValue.Location } member ? member.Choice
        : throw call.Error("MATLAB:invalidType", "The location must be mps.cache.Location.Local or mps.cache.Location.Remote.");

    /// <summary>Every key, composite ones as their structs, as a column; local and remote, or one side.</summary>
    private List<JgsValue> AllKeys(string? location)
    {
        var simple = new SortedSet<string>(StringComparer.Ordinal);
        var composite = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        if (location is null or "Remote")
        {
            MatFileService service = Service;
            foreach (string key in service.Keys(Name))
            {
                simple.Add(key);
            }

            if (service.HasHash(Name, CompositeKeyVar))
            {
                (List<string> hashes, List<JgsValue> structs) = service.HashGet(Name, CompositeKeyVar);
                for (int i = 0; i < hashes.Count && i < structs.Count; i++)
                {
                    composite[hashes[i]] = structs[i];
                }
            }
        }

        if (location is null or "Local")
        {
            foreach ((string hash, JgsValue key) in _compositeKeys)
            {
                composite[hash] = key;
            }

            foreach (string name in _localNames)
            {
                simple.Add(name);
            }
        }

        return [.. simple.Select(k => composite.TryGetValue(k, out JgsValue? made) ? made : JgsValue.Str(k))];
    }

    private JgsValue KeysMethod(DeviceCall call)
    {
        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        return Column(AllKeys(call.Args.Count == 1 ? LocationOf(call.Args[0], call) : null));
    }

    private JgsValue IsKey(DeviceCall call)
    {
        List<JgsValue> asked = KeyList(call.Args);
        List<JgsValue> known = AllKeys(null);
        var answers = new JgsValue[asked.Count];
        for (int i = 0; i < asked.Count; i++)
        {
            answers[i] = JgsValue.Bool(known.Any(k => SameKey(k, asked[i])));
        }

        JgsValue row = JgsValue.Array(answers);
        row.Reshape(1, answers.Length);
        return row;
    }

    private static bool SameKey(JgsValue a, JgsValue b) =>
        a.Type == JgsType.Struct && b.Type == JgsType.Struct ? MatFileService.Fingerprint(a) == MatFileService.Fingerprint(b)
        : a.Type != JgsType.Struct && b.Type != JgsType.Struct && DeviceChecks.IsText(b) && DeviceChecks.IsText(a) && DeviceChecks.Text(a) == DeviceChecks.Text(b);

    // --- local storage ----------------------------------------------------------------------------------

    private void InitLocalData()
    {
        _localNames.Clear();
        _localData.Clear();
        _modified.Clear();
        if (LocalKeys.Type == JgsType.Cell)
        {
            _hasLocalData = LocalKeys.ArrayLength > 0;
            foreach (JgsValue key in LocalKeys.AsCell)
            {
                string name = Simple(key);
                if (!_localData.ContainsKey(name))
                {
                    _localNames.Add(name);
                    _localData[name] = MatFileService.Nothing();
                }
            }
        }
        else
        {
            _hasLocalData = true;
        }
    }

    private bool LocalOff => LocalKeys.Type == JgsType.Cell ? LocalKeys.ArrayLength == 0 : LocalKeys.Type == JgsType.Bool && !LocalKeys.AsBool;

    private bool IsLocalName(string name) =>
        LocalKeys.Type == JgsType.Bool ? LocalKeys.AsBool : LocalKeys.Type == JgsType.Cell && LocalKeys.AsCell.Any(k => Simple(k) == name);

    private JgsValue[] Retain(DeviceCall call)
    {
        if (call.Args.Count == 0)
        {
            return [JgsValue.Share(LocalKeys)];
        }

        PurgeLocal();
        JgsValue keys;
        if (call.Args.Count == 1 && (call.Args[0].Type == JgsType.Cell || call.Args[0].Type == JgsType.Bool))
        {
            keys = JgsValue.Share(call.Args[0]);
        }
        else if (call.Args.Count == 1 && call.Args[0].IsStringArray)
        {
            keys = JgsValue.Cell([.. KeyList(call.Args)]);
            keys.Reshape(1, call.Args[0].ArrayLength);
        }
        else
        {
            keys = JgsValue.Cell([.. call.Args.Select(JgsValue.Share)]);
            keys.Reshape(1, call.Args.Count);
        }

        LocalKeys = keys;
        InitLocalData();
        return call.Wanted > 0 ? [JgsValue.Share(LocalKeys)] : [];
    }

    private JgsValue ReadOne(string name) => Read([name])[0];

    private JgsValue[] Read(IReadOnlyList<string> names)
    {
        if (!_hasLocalData)
        {
            return Service.Read(Name, names);
        }

        var answer = new JgsValue[names.Count];
        var remote = new List<int>();
        for (int i = 0; i < names.Count; i++)
        {
            if (_localData.TryGetValue(names[i], out JgsValue? held))
            {
                answer[i] = JgsValue.Share(held);
            }
            else
            {
                remote.Add(i);
            }
        }

        if (remote.Count > 0)
        {
            JgsValue[] fetched = Service.Read(Name, [.. remote.Select(i => names[i])]);
            for (int j = 0; j < remote.Count; j++)
            {
                answer[remote[j]] = fetched[j];
                string name = names[remote[j]];
                if (!_localData.ContainsKey(name))
                {
                    _localNames.Add(name);
                }

                _localData[name] = JgsValue.Share(fetched[j]);
            }
        }

        return answer;
    }

    private JgsValue[] Get(DeviceCall call)
    {
        List<JgsValue> keys = KeyList(call.Args);
        JgsValue[] values = Read([.. keys.Select(Simple)]);
        if (call.Wanted <= 1 && values.Length > 1)
        {
            JgsValue cell = JgsValue.Cell(values);
            cell.Reshape(1, values.Length);
            return [cell];
        }

        if (call.Wanted > values.Length)
        {
            throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        return values;
    }

    private JgsValue[] Put(DeviceCall call)
    {
        IReadOnlyList<JgsValue> args = call.Args;
        int pairs = args.Count / 2;
        var keys = new List<JgsValue>();
        var values = new List<JgsValue>();
        for (int i = 0; i < pairs; i++)
        {
            keys.Add(args[2 * i]);
            values.Add(args[(2 * i) + 1]);
        }

        if (args.Count == 2)
        {
            bool flattened = false;
            if (keys[0].Type == JgsType.Cell)
            {
                keys = [.. keys[0].AsCell];
                flattened = true;
            }
            else if (keys[0].IsStringArray)
            {
                keys = KeyList([keys[0]]);
                flattened = true;
            }

            if (values[0].Type == JgsType.Cell && (keys.Count > 1 || flattened))
            {
                values = [.. values[0].AsCell];
                if (values.Count != keys.Count)
                {
                    throw call.Error("MPS:MATLAB:KVStore:KeyValueCountMismatch", $"Number of keys ({keys.Count}) must match number of values ({values.Count}).");
                }
            }
        }

        if (keys.Count == 0)
        {
            throw call.Error("MPS:MATLAB:KVStore:BadInputsToWrite",
                $"Wrong number of inputs: {(2 * pairs) + 2}. Expecting even number four or greater: <service connection>, <cache name>, <list of variable / value pairs>.");
        }

        Store(keys, values);
        return [];
    }

    /// <summary>Stores keys and values: the locally held ones in local storage, marked modified; the rest in the service.</summary>
    private void Store(List<JgsValue> keys, List<JgsValue> values)
    {
        if (LocalOff)
        {
            Write([.. keys.Zip(values, (k, v) => (Simple(k), v))]);
            return;
        }

        var remote = new List<(string Name, JgsValue Value)>();
        for (int i = 0; i < keys.Count; i++)
        {
            string name = Simple(keys[i]);
            if (IsLocalName(name))
            {
                if (!_localData.ContainsKey(name))
                {
                    _localNames.Add(name);
                }

                _localData[name] = JgsValue.Share(values[i]);
                if (!_modified.Contains(name))
                {
                    _modified.Add(name);
                }
            }
            else
            {
                remote.Add((name, values[i]));
            }
        }

        if (remote.Count > 0)
        {
            Write(remote);
        }
    }

    /// <summary>Writes to the service, recording the composite keys among the names.</summary>
    private void Write(IReadOnlyList<(string Name, JgsValue Value)> pairs)
    {
        MatFileService service = Service;
        var composite = new List<(string, JgsValue)>();
        foreach ((string name, _) in pairs)
        {
            if (_compositeKeys.TryGetValue(name, out JgsValue? key))
            {
                composite.Add((name, key));
            }
        }

        if (composite.Count > 0)
        {
            service.HashSet(Name, CompositeKeyVar, composite);
        }

        service.Write(Name, pairs);
    }

    private JgsValue[] Flush(DeviceCall call)
    {
        List<string> names = FlushLocal();
        return call.Wanted > 0 ? [NameList(names)] : [];
    }

    private List<string> FlushLocal()
    {
        var names = new List<string>(_modified);
        if (names.Count > 0)
        {
            Write([.. names.Select(n => (n, _localData[n]))]);
            _modified.Clear();
        }

        return names;
    }

    private JgsValue[] Purge(DeviceCall call)
    {
        List<string> names = PurgeLocal();
        return call.Wanted > 0 ? [NameList(names)] : [];
    }

    private List<string> PurgeLocal()
    {
        if (!_hasLocalData)
        {
            return [];
        }

        List<string> names = FlushLocal();
        LocalKeys = EmptyCell();
        InitLocalData();
        return names;
    }

    private JgsValue Remove(DeviceCall call)
    {
        List<JgsValue> keys = KeyList(call.Args);
        long removed = 0;
        if (keys.Count > 0)
        {
            MatFileService service = Service;
            string[] simple = [.. keys.Select(Simple)];
            removed = service.Remove(Name, simple);
            string[] composite = [.. keys.Where(IsComposite).Select(MatFileService.Fingerprint)];
            if (composite.Length > 0)
            {
                service.HashRemove(Name, CompositeKeyVar, composite);
                foreach (string hash in composite)
                {
                    _compositeKeys.Remove(hash);
                }
            }
        }

        if (_hasLocalData)
        {
            foreach (string name in keys.Select(Simple))
            {
                if (_localData.Remove(name))
                {
                    _localNames.Remove(name);
                    _modified.Remove(name);
                    removed++;
                }
            }
        }

        return Int64(removed);
    }

    private JgsValue Clear(DeviceCall call)
    {
        List<JgsValue> keys = AllKeys(null);
        if (LocalOff ? CountRemote() == 0 : keys.Count == 0)
        {
            return JgsValue.Number(0);
        }

        return Remove(new DeviceCall { Target = this, Args = [Column(keys)], Line = call.Line, Column = call.Column });
    }

    private int CountRemote() => Service.CountRemoteKeys(Name);

    private JgsValue Length(DeviceCall call)
    {
        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        string? location = call.Args.Count == 1 ? LocationOf(call.Args[0], call) : null;
        long count;
        if (location is null)
        {
            count = LocalOff ? CountRemote() : AllKeys(null).Count;
        }
        else
        {
            count = location == "Remote" ? CountRemote() : _localData.Count;
        }

        return Int64(count);
    }

    private JgsValue Bytes(DeviceCall call)
    {
        if (call.Args.Count == 1 && call.Args[0].Type == JgsType.Cell && call.Args[0].ArrayLength != 1)
        {
            throw call.Error("MATLAB:strcmp:InputsSizeMismatch", "Inputs must be the same size or either one can be a scalar.");
        }

        List<JgsValue> keys = KeyList(call.Args);
        double[] sizes = Service.Bytes(Name, [.. keys.Select(Simple)]);
        return sizes.Length == 1 ? JgsValue.Number(sizes[0]) : JgsMatrix.FromColumnMajor(sizes, 1, sizes.Length);
    }

    private JgsValue Getp(DeviceCall call)
    {
        string name = call.Args.Count == 1 && DeviceChecks.IsText(call.Args[0]) ? DeviceChecks.Text(call.Args[0]) : "";
        return name switch
        {
            "Name" => JgsValue.Str(Name),
            "Operations" => JgsValue.StringScalar(Connection.Operations),
            "LocalKeys" => JgsValue.Share(LocalKeys),
            "Connection" => JgsValue.Str(Connection.ConnectionName),
            _ => throw MpsCacheState.Error(call, "InvalidPropertyName", $"Class {ClassName} has no property named \"{name}\"."),
        };
    }

    protected override void OnDelete()
    {
    }

    protected override string ShortDisplay()
    {
        string localKeys = LocalKeys.Type == JgsType.Cell && LocalKeys.ArrayLength == 0 ? "{}" : MpsControllerObject.Quoted(LocalKeys);
        return $"MATFileCache with properties:\n\n    Name: '{Name}'\n    Operations: \"{Connection.Operations}\"\n    LocalKeys: {localKeys}\n    Connection: '{Connection.ConnectionName}'\n\n  Use getp instead of dot notation to access cache properties.";
    }
}

/// <summary>
/// <c>mps.sync.TimedMATFileMutex</c>: a lease on <c>locks\&lt;name&gt;\expirationDate</c> in the service's
/// folder, held until released or until its expiration passes; a second holder waits or fails
/// (R2025b's never does: ADR 0271).
/// </summary>
internal sealed class MpsMutexObject : DeviceObject
{
    private static readonly DeviceClass Declaration = new(
        "mps.sync.TimedMATFileMutex",
        "TimedMATFileMutex",
        ["mps.sync.TimedMutex", "mps.sync.ServiceMutex", "handle"],
        [
            new DeviceProperty("Expiration", static (o, _) => JgsValue.Number(((MpsMutexObject)o).Expiration)),
            new DeviceProperty("ConnectionName", static (o, _) => JgsValue.Str(((MpsMutexObject)o).Connection.ConnectionName)),
            new DeviceProperty("MutexName", static (o, _) => JgsValue.Str(((MpsMutexObject)o).MutexName)),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["acquire"] = static c => [JgsValue.Bool(((MpsMutexObject)c.Target).Acquire(c))],
            ["release"] = static c => [JgsValue.Bool(((MpsMutexObject)c.Target).Release())],
            ["own"] = static c => [JgsValue.Bool(((MpsMutexObject)c.Target).Own())],
            ["delete"] = static c => { ((MpsMutexObject)c.Target).Delete(); return []; },
        },
        ["TimedMATFileMutex", "acquire", "addlistener", "delete", "eq", "findobj", "findprop", "ge", "gt", "isvalid", "le", "listener", "lt", "ne", "notify", "own", "release"],
        ["Expiration", "ConnectionName", "MutexName"]);

    private string? _lease;

    public MpsMutexObject(DeviceSession session, Interpreter interpreter, string name, MpsConnectionObject connection, double expiration)
        : base(session, interpreter)
    {
        MutexName = name;
        Connection = connection;
        Expiration = expiration;
    }

    public override DeviceClass Class => Declaration;

    public string MutexName { get; }

    public MpsConnectionObject Connection { get; }

    public double Expiration { get; }

    private string LockFolder => Path.Combine(Connection.Folder, "locks", MutexName);

    private string LeaseFile => Path.Combine(LockFolder, "expirationDate");

    private bool ActiveLease()
    {
        if (_lease is null || !File.Exists(_lease))
        {
            return false;
        }

        if (ReadExpiry(_lease) is { } expiry && DateTime.UtcNow < expiry)
        {
            return true;
        }

        Release();
        return false;
    }

    private static DateTime? ReadExpiry(string file)
    {
        try
        {
            return long.TryParse(File.ReadAllText(file).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) ? new DateTime(ticks, DateTimeKind.Utc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool Own() => ActiveLease();

    public bool Acquire(DeviceCall call)
    {
        double timeout = 0;
        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        if (call.Args.Count == 1)
        {
            timeout = DeviceChecks.Numbers(call.Args[0]).FirstOrDefault();
        }

        if (_lease is not null)
        {
            if (ActiveLease())
            {
                return false;
            }

            _lease = null;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (TryTakeLease())
            {
                return true;
            }

            if (watch.Elapsed.TotalSeconds > timeout)
            {
                return false;
            }

            Thread.Sleep(5);
        }
    }

    private bool TryTakeLease()
    {
        try
        {
            Directory.CreateDirectory(LockFolder);
            if (File.Exists(LeaseFile) && ReadExpiry(LeaseFile) is { } expiry && DateTime.UtcNow >= expiry)
            {
                File.Delete(LeaseFile);
            }

            using (var file = new FileStream(LeaseFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file))
            {
                writer.Write(DateTime.UtcNow.AddSeconds(Expiration).Ticks.ToString(CultureInfo.InvariantCulture));
            }

            _lease = LeaseFile;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool Release()
    {
        if (_lease is null)
        {
            return false;
        }

        bool released = false;
        try
        {
            if (File.Exists(_lease))
            {
                File.Delete(_lease);
                released = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The lease stays until it expires.
        }

        _lease = null;
        return released;
    }

    protected override void OnDelete() => Release();
}
