using System.Globalization;
using System.Text;
using JGraph.Data;
using JGraph.Devices;
using JGraph.Devices.Visa;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>The VISA interface types visadev tells apart (visalib.InterfaceType).</summary>
internal enum VisaKind
{
    Gpib,
    Vxi,
    Serial,
    Pxi,
    Tcpip,
    Usb,
    Socket,
}

/// <summary>A resource as visadevlist lists it and visadev looks it up (visalib.internal.ResourceInfo).</summary>
internal sealed record VisaResourceInfo(string Name, string Alias, string Vendor, string Model, string SerialNumber, VisaKind? Kind);

/// <summary>
/// <c>visadev</c>, <c>visadevlist</c> and <c>visadevfind</c> (device classes plan, stage D4), transcribed
/// from R2025b's <c>visadev.m</c>, <c>visadevlist.m</c>, <c>visalib.Resource</c> and its seven subclasses,
/// with the p-coded VisaClient's reads as probe_visa_timing measured them:
/// <list type="bullet">
/// <item><c>read</c> turns VISA's termination character off, asks VISA for the bytes, and hands them to
/// the shared client, which waits the Timeout again for any still missing: a read that gets nothing
/// warns after twice the Timeout; a serial read that times out part-way fails and drops what came.</item>
/// <item><c>readline</c> asks VISA for one read that ends at the termination character or END; a
/// timeout is <c>operationTimedOut</c>. With the terminator "off" and END on, the whole message is the
/// line.</item>
/// <item><c>readbinblock</c> reads the header a byte at a time, then the block.</item>
/// </list>
/// Two R2025b defects are not kept (div=ADR0187): a read that fails leaves VISA's termination character
/// off, and a read/write terminator pair sets VISA's character from the write one.
/// </summary>
internal sealed class VisadevObject : DeviceObject, ILegacyTransport
{
    internal static readonly TransportInterface Interface = new()
    {
        Name = "visadev",
        ObjectName = "v",
        SharedEventInfo = true,
        ReadFailedId = "transportlib:generic:ReadFailed",
        ReadFailedLead = "Error reading data from the transport:",
    };

    private const string ConnectDoc = "\nSee <a href=\"matlab: helpview('instrument', 'visadev_connectError')\">related documentation</a> for troubleshooting steps.";
    private const string VisaId = "instrument:interface:visa:";
    private const int TransferSize = 1024;

    private static readonly Dictionary<VisaKind, DeviceClass> Declarations = Enum.GetValues<VisaKind>().ToDictionary(static k => k, Declare);

    private readonly VisaKind _kind;
    private readonly IVisaSession _session;
    private readonly VisaChannel _channel;
    private readonly TransportClient _client;
    private readonly VisaResourceInfo _info;
    private readonly Dictionary<string, JgsValue> _values = new(StringComparer.Ordinal);
    private string _byteOrder = "little-endian";
    private string _tag = "";
    private bool _readTerminatorDisabled;
    private bool _checkSuppressEnd = true;
    private bool _eoiMode = true;
    private bool _writeRead;
    private JgsValue _eosMode = JgsValue.Str("read&write");
    private JgsValue _inputBufferSize = JgsValue.Number(512);
    private JgsValue _outputBufferSize = JgsValue.Number(1024);
    private JgsValue _transferPeriod = JgsValue.Number(0.01);
    private JgsValue _transferSize = JgsValue.Number(TransferSize);

    private VisadevObject(DeviceSession session, Interpreter interpreter, VisaKind kind, IVisaSession visa, VisaResourceInfo info, string preferred)
        : base(session, interpreter)
    {
        _kind = kind;
        _session = visa;
        _info = info;
        _channel = new VisaChannel(info.Name, visa, interpreter);
        _client = new TransportClient(_channel, Interface, this) { Timeout = JgsValue.Number(10) };
        PreferredVisa = preferred;
    }

    public override DeviceClass Class => Declarations[_kind];

    public string ResourceName => _info.Name;

    public string PreferredVisa { get; }

    public override string? Summary() => Deleted ? "deleted" : _info.Name;

    // --- visadevlist ------------------------------------------------------------------------------------

    /// <summary><c>visadevlist</c>, <c>visadevlist("Timeout", t, "Identification", id)</c>: a table of the VISA's resources.</summary>
    public static JgsValue List(DeviceSession session, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 4)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:tooManyInputs", "Too many input arguments.");
        }

        var given = new Dictionary<string, JgsValue>(StringComparer.Ordinal);
        for (int i = 0; i < args.Count; i += 2)
        {
            JgsValue name = TransportClient.Str2Char(args[i]);
            if (!DeviceChecks.IsText(name))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            string written = DeviceChecks.Text(name);
            if (!DeviceChecks.Match(written, ["Timeout", "Identification"], out string? option, out _) || written.Length == 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:UnmatchedParameter",
                    $"'{written}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.");
            }

            if (i + 1 >= args.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    $"No value was given for '{option}'. Name-value pair arguments require a name followed by a value.");
            }

            given[option!] = args[i + 1];
        }

        double timeout = 10;
        if (given.TryGetValue("Timeout", out JgsValue? t))
        {
            double seconds = double.NaN;
            if (t.IsDuration && t.ArrayLength == 1)
            {
                // A duration counts milliseconds; visadevlist takes seconds(t).
                seconds = t.ElementAt(0).AsNumber / 1000;
            }
            else if (DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(t)) && DeviceChecks.Count(t) == 1)
            {
                seconds = DeviceChecks.Numbers(t).First();
            }

            if (!(double.IsFinite(seconds) && seconds >= 2))
            {
                throw new JgsRuntimeException(line, col, VisaId + "invalidTimeout", "The value of 'Timeout' is invalid. Timeout must be greater than or equal to 2.");
            }

            timeout = seconds;
        }

        bool identify = true;
        var commands = new Dictionary<string, string>(StringComparer.Ordinal);
        if (given.TryGetValue("Identification", out JgsValue? id))
        {
            if (DeviceChecks.ClassOf(id) == "logical")
            {
                if (DeviceChecks.Count(id) != 1)
                {
                    throw InvalidIdentification(line, col);
                }

                identify = DeviceChecks.Numbers(id).First() != 0;
            }
            else if (!id.IsStringArray)
            {
                throw InvalidIdentification(line, col);
            }
            else if (id.Cols != 2)
            {
                throw new JgsRuntimeException(line, col, VisaId + "unexpectedOneDimensionalArrayFormat",
                    "The value of 'Identification' is invalid. Expected a logical or an N-by-2 string array, where each row contains a resource name and its corresponding identification command.");
            }
            else
            {
                for (int r = 0; r < id.Rows; r++)
                {
                    string resource = id.ElementAt(r).AsString;
                    string command = id.ElementAt(r + id.Rows).AsString;
                    if (!commands.TryAdd(resource, command))
                    {
                        throw new JgsRuntimeException(line, col, VisaId + "duplicateResourceName", $"Repeated resource name {resource}. Ensure all resource names are unique.");
                    }
                }
            }
        }

        IVisaBackend backend = Backend(session, line, col);

        // UsingDefaults names every parameter left at its default, so a list asked with a Timeout and no
        // Identification still reports the default timeout's sentence (visadevlist.m).
        bool usingDefaults = given.Count < 2;
        IReadOnlyList<string> names = backend.Find("?*::INSTR").Distinct(StringComparer.Ordinal).ToList();
        foreach (string asked in commands.Keys)
        {
            if (!names.Contains(asked, StringComparer.Ordinal))
            {
                throw new JgsRuntimeException(line, col, VisaId + "invalidResourceName", $"Invalid resource name {asked}.");
            }
        }

        var listed = new List<VisaResourceInfo>();
        foreach (string name in names)
        {
            if (!identify)
            {
                listed.Add(new VisaResourceInfo(name, "", "", "", "", null));
                continue;
            }

            VisaParsedName parsed;
            try
            {
                parsed = backend.Parse(name);
            }
            catch (VisaException)
            {
                continue;
            }

            // Identifying needs the resource opened: NI-VISA leaves out a port another program holds.
            try
            {
                backend.Open(name, 2000).Dispose();
            }
            catch (VisaException)
            {
                continue;
            }

            VisaKind? kind = KindOf(parsed);
            (string vendor, string model, string serial) = kind is VisaKind.Serial or null
                ? ("", "", "")
                : Identify(backend, name, commands.TryGetValue(name, out string? command) ? command : "*IDN?");
            listed.Add(new VisaResourceInfo(name, parsed.Alias, vendor, model, serial, kind));
        }

        session.VisaCache = listed;
        if (listed.Count == 0)
        {
            throw usingDefaults
                ? new JgsRuntimeException(line, col, VisaId + "unableToFindResourcesDefaultTimeout",
                    $"Unable to find any VISA resources during the default timeout period of {Number(timeout)} seconds. Specify a larger \"Timeout\" value or open the vendor's control software to search for available VISA resources.")
                : new JgsRuntimeException(line, col, VisaId + "unableToFindResources",
                    "Unable to find any VISA resources during the specified timeout period. Open the vendor's control software to search for available VISA resources.");
        }

        int n = listed.Count;
        JgsValue Strings(Func<VisaResourceInfo, string> pick) => JgsValue.StringArray(listed.Select(r => JgsValue.Str(pick(r))).ToArray(), n, 1);

        // R2025b's Type is a visalib.InterfaceType column, which JGraph holds as its names (div=ADR0187).
        var table = new Table(
        [
            JgsBuiltins.TableColumnFrom("visadevlist", "ResourceName", Strings(static r => r.Name), line, col),
            JgsBuiltins.TableColumnFrom("visadevlist", "Alias", Strings(static r => r.Alias), line, col),
            JgsBuiltins.TableColumnFrom("visadevlist", "Vendor", Strings(static r => r.Vendor), line, col),
            JgsBuiltins.TableColumnFrom("visadevlist", "Model", Strings(static r => r.Model), line, col),
            JgsBuiltins.TableColumnFrom("visadevlist", "SerialNumber", Strings(static r => r.SerialNumber), line, col),
            JgsBuiltins.TableColumnFrom("visadevlist", "Type", Strings(static r => KindName(r.Kind)), line, col),
        ])
        {
            RowNames = Enumerable.Range(1, n).Select(static k => k.ToString(CultureInfo.InvariantCulture)).ToArray(),
        };
        return JgsValue.Table(table);
    }

    private static JgsRuntimeException InvalidIdentification(int line, int col) =>
        new(line, col, VisaId + "invalidNVPairValue",
            "The value of 'Identification' is invalid. Value must be true, false, or a string array of resource names and identification commands.");

    /// <summary>Asks an instrument who it is: Vendor, Model and SerialNumber from the first three fields of its answer.</summary>
    private static (string Vendor, string Model, string Serial) Identify(IVisaBackend backend, string name, string command)
    {
        try
        {
            using IVisaSession probe = backend.Open(name, 2000);
            probe.SetAttribute(VisaAttribute.TimeoutValue, 2000);
            probe.SetAttribute(VisaAttribute.TermChar, 10);
            probe.SetAttribute(VisaAttribute.TermCharEnabled, 1);
            probe.Write(Encoding.ASCII.GetBytes(command + "\n"), CancellationToken.None);
            VisaReadResult answer = probe.Read(1024, CancellationToken.None);
            string[] fields = Encoding.ASCII.GetString(answer.Data).Trim().Split(',');
            string Field(int k) => k < fields.Length ? fields[k].Trim() : "";
            return answer.TimedOut && answer.Data.Length == 0 ? ("", "", "") : (Field(0), Field(1), Field(2));
        }
        catch (VisaException)
        {
            return ("", "", "");
        }
    }

    private static string KindName(VisaKind? kind) => kind switch
    {
        null => "unset",
        VisaKind k => k.ToString().ToLowerInvariant(),
    };

    private static VisaKind? KindOf(VisaParsedName parsed) => (parsed.InterfaceType, parsed.ResourceClass.ToUpperInvariant()) switch
    {
        (1, "INSTR") => VisaKind.Gpib,
        (2, "INSTR") => VisaKind.Vxi,
        (4, "INSTR") => VisaKind.Serial,
        (5, "INSTR") => VisaKind.Pxi,
        (6, "INSTR") => VisaKind.Tcpip,
        (6, "SOCKET") => VisaKind.Socket,
        (7, "INSTR") => VisaKind.Usb,
        _ => null,
    };

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The session's VISA: the simulated one a test installed, or the installed library; R2025b's refusal when there is none.</summary>
    private static IVisaBackend Backend(DeviceSession session, int line, int col) =>
        session.VisaSimulation as IVisaBackend
        ?? VisaBackends.Installed(out _)
        ?? throw new JgsRuntimeException(line, col, VisaId + "unableToFindPreferredVISA",
            "Unable to find VISA installations. <a href=\"matlab:instrument.internal.supportPackageInstaller\">Open the Support Package Installer</a> to install VISA. ");

    // --- visadev -----------------------------------------------------------------------------------------

    /// <summary><c>v = visadev(resourceID)</c>, <c>visadev(resourceID, "Tag", tag)</c>.</summary>
    public static JgsValue[] Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        if (args.Count == 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:narginchk:notEnoughInputs", "Not enough input arguments.");
        }

        string tag = ParseOptions(args.Skip(1).ToList(), line, col);
        string resourceId = ResourceId(args[0], line, col);
        IVisaBackend backend = Backend(session, line, col);
        if (resourceId == "reset")
        {
            session.VisaOpen.Clear();
            if (wanted > 0)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:unassignedOutputs",
                    "Output argument \"v\" (and possibly others) not assigned a value in the execution with \"visadev\" function.");
            }

            return [];
        }

        // A name or alias the last visadevlist listed (contains, as visadev.m matches), then the VISA.
        List<VisaResourceInfo> cache = session.VisaCache;
        VisaResourceInfo? info = cache.Any(r => (r.Alias.Length > 0 && r.Alias.Contains(resourceId, StringComparison.Ordinal)) || r.Name.Contains(resourceId, StringComparison.Ordinal))
            ? cache.First(r => r.Alias.Contains(resourceId, StringComparison.Ordinal) || r.Name.Contains(resourceId, StringComparison.Ordinal))
            : null;
        if (info?.Kind is null)
        {
            info = Specified(backend, resourceId, line, col);
        }

        string upper = info.Name.ToUpperInvariant();
        if (session.VisaOpen.Contains(upper))
        {
            throw new JgsRuntimeException(line, col, VisaId + "multipleIdenticalResources",
                $"Creating a second device for the {upper} resource is not supported." + ConnectDoc);
        }

        IVisaSession visa;
        try
        {
            visa = backend.Open(info.Name, 2000);
        }
        catch (VisaException)
        {
            throw Undetermined(line, col);
        }

        var made = new VisadevObject(session, interpreter, info.Kind!.Value, visa, info, backend.PreferredVisa);
        var call = new DeviceCall { Target = made, Args = [], Line = line, Column = col };
        try
        {
            made.Connect(call);
        }
        catch
        {
            visa.Dispose();
            throw;
        }

        made._tag = tag;
        session.VisaOpen.Add(upper);
        session.Remember(made);
        JgsValue value = JgsValue.External(made);
        JgsLifetime.Minted(value);
        return [value];
    }

    private static JgsRuntimeException Undetermined(int line, int col) =>
        new(line, col, VisaId + "unableToDetermineInterfaceType", "Resource string is invalid or resource was not found." + ConnectDoc);

    /// <summary>visadev.m's inputParser: an optional logical SynchronousRead, then Tag, partial and case-blind.</summary>
    private static string ParseOptions(List<JgsValue> rest, int line, int col)
    {
        static bool IsTag(JgsValue v) => DeviceChecks.IsText(TransportClient.Str2Char(v))
            && DeviceChecks.Match(DeviceChecks.Text(TransportClient.Str2Char(v)), ["Tag"], out _, out _)
            && DeviceChecks.Text(TransportClient.Str2Char(v)).Length > 0;

        int at = 0;
        if (rest.Count > 0 && !(IsTag(rest[0]) && rest.Count >= 2))
        {
            JgsValue sync = rest[0];
            if (!(DeviceChecks.Count(sync) == 0 || DeviceChecks.ClassOf(sync) == "logical"))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ArgumentFailedValidation",
                    "The value of 'SynchronousRead' is invalid. It must satisfy the function: @(x)isempty(x)||islogical(x).");
            }

            at = 1;
        }

        string tag = "";
        for (; at < rest.Count; at += 2)
        {
            JgsValue name = TransportClient.Str2Char(rest[at]);
            if (!DeviceChecks.IsText(name))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMustBeChar", "Expected a string scalar or character vector for the parameter name.");
            }

            if (!IsTag(name))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:UnmatchedParameter",
                    $"'{DeviceChecks.Text(name)}' is not a recognized parameter. For a list of valid name-value pair arguments, see the documentation for this function.");
            }

            if (at + 1 >= rest.Count)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ParamMissingValue",
                    "No value was given for 'Tag'. Name-value pair arguments require a name followed by a value.");
            }

            JgsValue value = rest[at + 1];
            if (!(value.IsStringArray || DeviceChecks.ClassOf(value) == "char"))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:InputParser:ArgumentFailedValidation",
                    "The value of 'Tag' is invalid. It must satisfy the function: @(x)isstring(x)||ischar(x).");
            }

            tag = DeviceChecks.IsText(value) ? DeviceChecks.Text(value) : "";
        }

        return tag;
    }

    /// <summary>checkArguments' mustBeNonzeroLengthText, then a scalar: the identifier as text.</summary>
    private static string ResourceId(JgsValue value, int line, int col)
    {
        const string Id = "MATLAB:validators:mustBeNonzeroLengthText";
        if (value.Type == JgsType.Cell)
        {
            JgsValue[] cells = value.AsCell;
            if (cells.Any(static c => DeviceChecks.ClassOf(c) != "char"))
            {
                throw new JgsRuntimeException(line, col, Id, "Invalid argument at position 1. Value must be a character vector, string array, or cell array of character vectors.");
            }

            if (cells.Length == 0 || cells.Any(static c => DeviceChecks.Text(c).Length == 0))
            {
                throw new JgsRuntimeException(line, col, Id, "Invalid argument at position 1. Value must be text with one or more characters.");
            }

            if (cells.Length != 1)
            {
                throw new JgsRuntimeException(line, col, "MATLAB:validation:IncompatibleSize", "Invalid argument at position 1. Value must be a scalar.");
            }

            return DeviceChecks.Text(cells[0]);
        }

        if (!value.IsStringArray && DeviceChecks.ClassOf(value) != "char")
        {
            throw new JgsRuntimeException(line, col, Id, "Invalid argument at position 1. Value must be a character vector, string array, or cell array of character vectors.");
        }

        if (value.IsStringArray && value.ArrayLength != 1)
        {
            if (Enumerable.Range(0, value.ArrayLength).Any(k => value.ElementAt(k).AsString.Length == 0))
            {
                throw new JgsRuntimeException(line, col, Id, "Invalid argument at position 1. Value must be text with one or more characters.");
            }

            throw new JgsRuntimeException(line, col, "MATLAB:validation:IncompatibleSize", "Invalid argument at position 1. Value must be a scalar.");
        }

        string text = DeviceChecks.IsText(value) ? DeviceChecks.Text(value) : "";
        if (text.Length == 0)
        {
            throw new JgsRuntimeException(line, col, Id, "Invalid argument at position 1. Value must be text with one or more characters.");
        }

        return text;
    }

    /// <summary>
    /// ResourceManager.getSpecifiedResource: the VISA's parse of the name, a session opened to see the
    /// resource is there, and an instrument's answer to <c>*IDN?</c> (a socket is asked and not heard).
    /// </summary>
    private static VisaResourceInfo Specified(IVisaBackend backend, string resourceId, int line, int col)
    {
        VisaParsedName parsed;
        try
        {
            parsed = backend.Parse(resourceId);
        }
        catch (VisaException)
        {
            throw Undetermined(line, col);
        }

        VisaKind kind = KindOf(parsed) ?? throw Undetermined(line, col);
        string vendor = "";
        string model = "";
        string serial = "";
        if (kind == VisaKind.Serial)
        {
            try
            {
                backend.Open(parsed.ExpandedName, 2000).Dispose();
            }
            catch (VisaException)
            {
                throw Undetermined(line, col);
            }
        }
        else
        {
            try
            {
                backend.Open(parsed.ExpandedName, 2000).Dispose();
            }
            catch (VisaException)
            {
                throw Undetermined(line, col);
            }

            (string v, string m, string s) = Identify(backend, parsed.ExpandedName, "*IDN?");
            if (kind != VisaKind.Socket)
            {
                (vendor, model, serial) = (v, m, s);
            }
        }

        return new VisaResourceInfo(parsed.ExpandedName, parsed.Alias, vendor, model, serial, kind);
    }

    // --- connection ----------------------------------------------------------------------------------------

    /// <summary>visalib.Resource's initResource, connect and each class's hooks, in their order.</summary>
    private void Connect(DeviceCall call)
    {
        InitResourceHook(call);

        // Synchronous reads: the Timeout is VISA's, 10 s to start with.
        SetTimeout(JgsValue.Number(10), call);
        switch (_kind)
        {
            case VisaKind.Serial:
                SetTermCharEnabled(true);
                break;
            case VisaKind.Socket:
                SetAttribute(VisaAttribute.SuppressEndEnabled, 0, call);
                SetTermCharEnabled(true);
                break;
            case VisaKind.Pxi:
                _values["Slot"] = JgsValue.Number(GetAttribute(VisaAttribute.Slot, call));
                SetEoiMode(true, call);
                break;
            default:
                if (_kind == VisaKind.Vxi)
                {
                    _values["Slot"] = JgsValue.Number(GetAttribute(VisaAttribute.Slot, call));
                }

                SetEoiMode(true, call);
                ConfigureTerminatorCore(SerialportObject.Retarget(call, [JgsValue.StringScalar("off"), JgsValue.StringScalar("LF")]));
                break;
        }
    }

    private void InitResourceHook(DeviceCall call)
    {
        switch (_kind)
        {
            case VisaKind.Serial:
            {
                _values["BaudRate"] = JgsValue.Number(GetAttribute(VisaAttribute.AsrlBaud, call));
                _values["DataBits"] = JgsValue.Number(GetAttribute(VisaAttribute.AsrlDataBits, call));
                _values["StopBits"] = JgsValue.Number(StopBitsOf(GetAttribute(VisaAttribute.AsrlStopBits, call)));
                _values["Parity"] = EnumValue(DeviceEnumValue.Parity, GetAttribute(VisaAttribute.AsrlParity, call) switch { 1 => "odd", 2 => "even", _ => "none" });
                ulong flow = GetAttribute(VisaAttribute.AsrlFlowControl, call);
                if (flow is not (0 or 1 or 2 or 4))
                {
                    SetAttribute(VisaAttribute.AsrlFlowControl, 0, call);
                    flow = GetAttribute(VisaAttribute.AsrlFlowControl, call);
                    JgsBuiltins.Warn(call.Host, VisaId + "unsupportedFlowControlType",
                        "Combined hardware and software control is not supported. Supported flow control modes are \"none\", \"software\", and \"hardware\".");
                }

                _values["FlowControl"] = EnumValue(DeviceEnumValue.FlowControl, flow switch { 1 => "software", 2 or 4 => "hardware", _ => "none" });
                _values["Port"] = JgsValue.StringScalar(_info.Name.Split("::")[0]);
                break;
            }

            case VisaKind.Tcpip:
                _values["BoardIndex"] = JgsValue.Number(GetAttribute(VisaAttribute.InterfaceNumber, call));
                _values["LANName"] = JgsValue.StringScalar(GetString(VisaAttribute.TcpipDeviceName, call));
                _values["InstrumentAddress"] = JgsValue.StringScalar(GetString(VisaAttribute.TcpipAddress, call));
                _checkSuppressEnd = GetAttribute(VisaAttribute.TcpipIsHislip, call) == 0;
                _values["KeepAlive"] = EnumValue(DeviceEnumValue.OnOffSwitchState, "off");
                _values["NoDelay"] = EnumValue(DeviceEnumValue.OnOffSwitchState, "on");
                break;
            case VisaKind.Socket:
                _values["IPAddress"] = JgsValue.StringScalar(GetString(VisaAttribute.TcpipAddress, call));
                _values["Port"] = JgsValue.Number(GetAttribute(VisaAttribute.TcpipPort, call));
                _values["KeepAlive"] = EnumValue(DeviceEnumValue.OnOffSwitchState, "off");
                _values["NoDelay"] = EnumValue(DeviceEnumValue.OnOffSwitchState, "on");
                break;
            case VisaKind.Usb:
                _values["VendorID"] = JgsValue.StringScalar(Hex(GetAttribute(VisaAttribute.ManufacturerId, call)));
                _values["ProductID"] = JgsValue.StringScalar(Hex(GetAttribute(VisaAttribute.ModelCode, call)));
                _values["BoardIndex"] = JgsValue.Number(GetAttribute(VisaAttribute.InterfaceNumber, call));
                _values["InterfaceIndex"] = JgsValue.Number(GetAttribute(VisaAttribute.UsbInterfaceNumber, call));
                break;
            case VisaKind.Gpib:
                _values["BoardIndex"] = JgsValue.Number(GetAttribute(VisaAttribute.InterfaceNumber, call));
                _values["PrimaryAddress"] = JgsValue.Number(GetAttribute(VisaAttribute.GpibPrimaryAddress, call));
                _values["SecondaryAddress"] = JgsValue.Number(GetAttribute(VisaAttribute.GpibSecondaryAddress, call));
                break;
            case VisaKind.Vxi:
                _values["ChassisIndex"] = JgsValue.Number(GetAttribute(VisaAttribute.PxiChassis, call));
                _values["LogicalAddress"] = JgsValue.Number(GetAttribute(VisaAttribute.VxiLogicalAddress, call));
                _values["Slot"] = JgsValue.Number(0);
                break;
            case VisaKind.Pxi:
                _values["Bus"] = JgsValue.Number(GetAttribute(VisaAttribute.PxiBusNumber, call));
                _values["DeviceIndex"] = JgsValue.Number(GetAttribute(VisaAttribute.PxiDeviceNumber, call));
                _values["FunctionIndex"] = JgsValue.Number(GetAttribute(VisaAttribute.PxiFunctionNumber, call));
                _values["ChassisIndex"] = JgsValue.Number(GetAttribute(VisaAttribute.PxiChassis, call));
                _values["Slot"] = JgsValue.Number(0);
                break;
        }
    }

    /// <summary>compose("%#x", n): <c>0x2e8a</c>, and <c>0</c> for zero.</summary>
    private static string Hex(ulong n) => n == 0 ? "0" : "0x" + n.ToString("x", CultureInfo.InvariantCulture);

    private static double StopBitsOf(ulong attribute) => attribute switch { 15 => 1.5, 20 => 2, _ => 1 };

    private JgsValue EnumValue(string className, string member) => DeviceEnumValue.Of(Session, Interpreter, className, member);

    // --- VISA attributes -------------------------------------------------------------------------------------

    private ulong GetAttribute(uint attribute, DeviceCall call)
    {
        try
        {
            return _session.GetAttribute(attribute);
        }
        catch (VisaException e)
        {
            throw Translate(e, call, "GetAttributesByType");
        }
    }

    private string GetString(uint attribute, DeviceCall call)
    {
        try
        {
            return _session.GetStringAttribute(attribute);
        }
        catch (VisaException e)
        {
            throw Translate(e, call, "GetAttributesByType");
        }
    }

    private void SetAttribute(uint attribute, ulong value, DeviceCall call)
    {
        try
        {
            _session.SetAttribute(attribute, value);
        }
        catch (VisaException e)
        {
            throw Translate(e, call, "SetAttributesByType");
        }
    }

    /// <summary>A VISA failure in R2025b's words (the device plugin's error map).</summary>
    private static JgsRuntimeException Translate(VisaException e, DeviceCall call, string command) => e.Status switch
    {
        VisaStatus.ErrorTimeout => call.Error(VisaId + "operationTimedOut", "Timeout expired before the operation completed."),
        VisaStatus.ErrorInvalidSetup => call.Error(VisaId + "inconsistentAttributeState", "Unable to set this attribute because this value puts the instrument in an invalid state."),
        VisaStatus.ErrorAttributeStateNotSupported => call.Error(VisaId + "resourceDoesNotSupportThisSetting", "This device does not support the specified attribute value."),
        VisaStatus.ErrorResourceBusy or VisaStatus.ErrorResourceLocked => call.Error(VisaId + "resourceBusy", "The VISA library is unable to access this resource because it is already in use. "),
        VisaStatus.ErrorAttributeNotSupported => call.Error(VisaId + "standardAdaptorCommandFailed", $"Adaptor command '{command}' failed with status code: {e.Status}."),
        _ => call.Error(VisaId + "driverErrorOccurred", $"VISA driver command failed and returned \nStatus code: {e.Status}  \nDiagnostic: '{e.Message}'\n"),
    };

    private bool TermCharEnabled(DeviceCall call) => GetAttribute(VisaAttribute.TermCharEnabled, call) != 0;

    private void SetTermCharEnabled(bool on) => _session.SetAttribute(VisaAttribute.TermCharEnabled, on ? 1UL : 0UL);

    /// <summary>disableVisaTerminator: VISA's character off, and a serial port's END_IN with it.</summary>
    private void DisableVisaTerminator(DeviceCall call)
    {
        SetAttribute(VisaAttribute.TermCharEnabled, 0, call);
        if (_kind == VisaKind.Serial)
        {
            SetAttribute(VisaAttribute.AsrlEndIn, 0, call);
        }
    }

    /// <summary>enableVisaTerminator: VISA's character on, and a serial port's END_IN at it.</summary>
    private void EnableVisaTerminator(DeviceCall call)
    {
        SetAttribute(VisaAttribute.TermCharEnabled, 1, call);
        if (_kind == VisaKind.Serial)
        {
            SetAttribute(VisaAttribute.AsrlEndIn, 2, call);
        }
    }

    /// <summary>
    /// Runs a VISA read with the termination character off, as read and readbinblock do. The character
    /// comes back on whether or not the read succeeded; R2025b's leaves it off after a failure (div=ADR0187).
    /// </summary>
    private void WithoutTerminator(DeviceCall call, Action read)
    {
        if (!TermCharEnabled(call))
        {
            read();
            return;
        }

        DisableVisaTerminator(call);
        try
        {
            read();
        }
        finally
        {
            EnableVisaTerminator(call);
        }
    }

    // --- the synchronous reads (the p-coded VisaClient, as measured) ------------------------------------------

    private VisaReadResult VisaRead(int count, DeviceCall call)
    {
        try
        {
            return _session.Read(count, Interpreter.Cancellation);
        }
        catch (VisaException e)
        {
            throw Translate(e, call, "Read");
        }
    }

    /// <summary>initiateReadSync: one VISA read of the bytes asked; nothing on a bare timeout, a failure on a partial one.</summary>
    private void InitiateRead(double bytes, DeviceCall call)
    {
        if (!(bytes >= 1 && bytes <= int.MaxValue && bytes == Math.Floor(bytes)))
        {
            return;
        }

        VisaReadResult result = VisaRead((int)bytes, call);
        if (result.TimedOut)
        {
            if (result.Data.Length == 0)
            {
                return;
            }

            throw call.Error(VisaId + "operationTimedOut", "Timeout expired before the operation completed.");
        }

        _channel.Input.Append(result.Data);
    }

    /// <summary>initiateReadlineSync: VISA reads until one ends at the termination character or END; a timeout fails.</summary>
    private void InitiateReadline(DeviceCall call)
    {
        var got = new List<byte>();
        while (true)
        {
            VisaReadResult result = VisaRead(TransferSize, call);
            if (result.TimedOut)
            {
                throw call.Error(VisaId + "operationTimedOut", "Timeout expired before the operation completed.");
            }

            got.AddRange(result.Data);
            if (result.Status != VisaStatus.SuccessMaxCount)
            {
                break;
            }
        }

        _channel.Input.Append(got.ToArray());
    }

    /// <summary>initiateReadBinblockSync: to the '#', its digit, the length, then the block.</summary>
    private void InitiateBinblock(DeviceCall call)
    {
        byte One()
        {
            VisaReadResult r = VisaRead(1, call);
            return r.TimedOut || r.Data.Length == 0
                ? throw call.Error(VisaId + "operationTimedOut", "Timeout expired before the operation completed.")
                : r.Data[0];
        }

        JgsRuntimeException Bad() =>
            call.Error(VisaId + "unableToCompleteOperation", "Unable to fully execute command 'readbinblock: Bad input: std::invalid_argument thrown'.");

        while (One() != (byte)'#')
        {
        }

        byte digit = One();
        if (digit is < (byte)'1' or > (byte)'9')
        {
            throw Bad();
        }

        var header = new StringBuilder();
        for (int i = 0; i < digit - '0'; i++)
        {
            byte b = One();
            if (b is < (byte)'0' or > (byte)'9')
            {
                throw Bad();
            }

            header.Append((char)b);
        }

        long length = long.Parse(header.ToString(), CultureInfo.InvariantCulture);
        var block = new List<byte>();
        while (block.Count < length)
        {
            VisaReadResult r = VisaRead((int)Math.Min(length - block.Count, int.MaxValue), call);
            if (r.TimedOut)
            {
                throw call.Error(VisaId + "operationTimedOut", "Timeout expired before the operation completed.");
            }

            block.AddRange(r.Data);
            if (r.Status != VisaStatus.SuccessMaxCount || r.Data.Length == 0)
            {
                break;
            }
        }

        _channel.Input.Append([(byte)'#', digit, .. Encoding.ASCII.GetBytes(header.ToString()), .. block]);
    }

    // --- the methods ----------------------------------------------------------------------------------------

    internal TransportClient Live(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        return _client;
    }

    TransportClient ILegacyTransport.Live(DeviceCall call) => Live(call);

    /// <summary>
    /// visalib.Resource.read: the count and precision through getNumBytesToRead's arguments block, a VISA
    /// read of that many bytes, then the client's read of them.
    /// </summary>
    private JgsValue Read(DeviceCall call)
    {
        TransportClient client = Live(call);
        IReadOnlyList<JgsValue> args = call.Args;
        if (args.Count >= 1)
        {
            double count = ArgumentDouble(args[0], 1, call);
            JgsValue precision = args.Count == 2 ? args[1] : JgsValue.StringScalar("uint8");
            int size = BytesPerValue(ArgumentString(precision, 2, call), call);
            WithoutTerminator(call, () => InitiateRead(count * size, call));
        }

        return client.Read(call);
    }

    /// <summary>An <c>arguments</c> block's <c>(1,1) double</c>: a scalar, converted.</summary>
    private static double ArgumentDouble(JgsValue value, int position, DeviceCall call)
    {
        if (DeviceChecks.Count(value) != 1)
        {
            throw call.Error("MATLAB:validation:IncompatibleSize", $"Invalid argument at position {position}. Value must be a scalar.");
        }

        if (value.IsStringArray)
        {
            return double.TryParse(value.ElementAt(0).AsString, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : double.NaN;
        }

        if (value.Type is JgsType.Cell or JgsType.Struct or JgsType.External)
        {
            throw call.Error("MATLAB:validation:UnableToConvert", $"Invalid argument at position {position}. Value must be of type double or be convertible to double.");
        }

        return DeviceChecks.Numbers(value).First();
    }

    /// <summary>An <c>arguments</c> block's <c>(1,1) string</c>.</summary>
    private static string ArgumentString(JgsValue value, int position, DeviceCall call)
    {
        if (DeviceChecks.IsText(value))
        {
            return DeviceChecks.Text(value);
        }

        if (DeviceChecks.Count(value) != 1)
        {
            throw call.Error("MATLAB:validation:IncompatibleSize", $"Invalid argument at position {position}. Value must be a scalar.");
        }

        if (DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)))
        {
            return DeviceChecks.Numbers(value).First().ToString(CultureInfo.InvariantCulture);
        }

        if (DeviceChecks.ClassOf(value) == "logical")
        {
            return DeviceChecks.Numbers(value).First() != 0 ? "true" : "false";
        }

        throw call.Error("MATLAB:validation:UnableToConvert", $"Invalid argument at position {position}. Value must be of type string or be convertible to string.");
    }

    private static int BytesPerValue(string precision, DeviceCall call) => precision switch
    {
        "int8" or "uint8" or "char" or "string" => 1,
        "int16" or "uint16" => 2,
        "int32" or "uint32" or "single" => 4,
        "int64" or "uint64" or "double" => 8,
        _ => throw call.Error("transportlib:transport:unknownPrecision", "Invalid precision specified."),
    };

    /// <summary>
    /// visalib.Resource.readline: one VISA read, then either the whole message (the terminator off and
    /// END on) or the client's line.
    /// </summary>
    private JgsValue ReadLine(DeviceCall call)
    {
        TransportClient client = Live(call);
        InitiateReadline(call);
        bool endEnabled = GetAttribute(VisaAttribute.SuppressEndEnabled, call) == 0;
        bool termCharEnabled = TermCharEnabled(call);
        int n = client.NumBytesAvailable;
        if (endEnabled && !termCharEnabled && n != 0)
        {
            return client.Read(SerialportObject.Retarget(call, [JgsValue.Number(n), JgsValue.StringScalar("string")]));
        }

        if (_writeRead && call.Args.Count == 0)
        {
            return client.TryReadLine(call)
                ?? throw call.Error(VisaId + "terminatorTimeout", "The specified terminator was not read before the timeout period expired.");
        }

        return client.ReadLine(call);
    }

    private JgsValue ReadBinblock(DeviceCall call)
    {
        TransportClient client = Live(call);
        WithoutTerminator(call, () => InitiateBinblock(call));
        return client.ReadBinblock(call);
    }

    /// <summary>visalib.Resource.writeread: writeline, then readline with the client's error on a missing terminator.</summary>
    private JgsValue WriteRead(DeviceCall call)
    {
        if (call.Args.Count == 0)
        {
            throw call.Error("MATLAB:minrhs", "Not enough input arguments.");
        }

        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        Live(call).WriteLine(call);
        _writeRead = true;
        try
        {
            return ReadLine(SerialportObject.Retarget(call, []));
        }
        finally
        {
            _writeRead = false;
        }
    }

    /// <summary>visalib.Resource.configureTerminator: "off" disables VISA's character and keeps the client's.</summary>
    private void ConfigureTerminator(DeviceCall call)
    {
        TransportClient client = Live(call);
        if (call.Args.Count is < 1 or > 2)
        {
            client.ConfigureTerminator(call);
        }

        ConfigureTerminatorCore(call);
    }

    private void ConfigureTerminatorCore(DeviceCall call)
    {
        TransportClient client = _client;
        JgsValue first = TransportClient.Str2Char(call.Args[0]);
        if (DeviceChecks.IsText(first) && DeviceChecks.Text(first).Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            _readTerminatorDisabled = false;
            DisableVisaTerminator(call);
            JgsValue read = client.ReadTerminator.Shown;
            JgsValue write = call.Args.Count == 2 ? call.Args[1] : client.WriteTerminator.Shown;
            client.ConfigureTerminator(SerialportObject.Retarget(call, [read, write]));
            _readTerminatorDisabled = true;
            return;
        }

        _readTerminatorDisabled = false;
        client.ConfigureTerminator(call);

        // VISA's character is the read terminator's last one; R2025b takes the write terminator's (div=ADR0187).
        byte[] bytes = client.ReadTerminator.Bytes;
        SetAttribute(VisaAttribute.TermChar, bytes[^1], call);
        EnableVisaTerminator(call);
    }

    /// <summary>The Terminator property: the client's, with "off" for a read terminator VISA ignores.</summary>
    private JgsValue TerminatorValue()
    {
        JgsValue value = _client.TerminatorValue;
        if (_readTerminatorDisabled && value.Type == JgsType.Cell)
        {
            JgsValue[] cells = value.AsCell.ToArray();
            cells[0] = JgsValue.StringScalar("off");
            JgsValue pair = JgsValue.Cell(cells);
            pair.Reshape(1, 2);
            return pair;
        }

        return value;
    }

    /// <summary>visalib.Resource.flush: a device clear first when both buffers are flushed.</summary>
    private void Flush(DeviceCall call)
    {
        TransportClient client = Live(call);
        if (call.Args.Count == 0)
        {
            try
            {
                _session.Clear();
            }
            catch (VisaException e)
            {
                throw Translate(e, call, "ClearDevice");
            }
        }

        client.Flush(call);
    }

    private JgsValue[] VisaStatusOf(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count > 0)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        ushort status;
        try
        {
            status = _session.ReadStatusByte();
        }
        catch (VisaException e) when (e.Status is VisaStatus.ErrorInvalidSetup or VisaStatus.ErrorOperationNotSupported)
        {
            throw call.Error(VisaId + "visaStatusUnavailableAsConfigured", $"Unable to determine status for {_info.Name} resource in the current configuration.");
        }
        catch (VisaException e)
        {
            throw Translate(e, call, "ReadStatusByte");
        }

        // READY stayed false in R2025b even after a HiSLIP service request (probe_visa_misc).
        return [JgsValue.Bool(false), JgsValue.Number(status)];
    }

    private void VisaTrigger(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count > 0)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        try
        {
            _session.AssertTrigger();
        }
        catch (VisaException e)
        {
            throw Translate(e, call, "AssertTrigger");
        }
    }

    // --- properties ------------------------------------------------------------------------------------------

    /// <summary>The Timeout: the client's check, then VISA's timeout in milliseconds, read back (setTimeoutNormal).</summary>
    private void SetTimeout(JgsValue value, DeviceCall call)
    {
        double seconds = CheckClientTimeout(value, call);
        double desired = 1000 * seconds;
        ulong asked = (ulong)Math.Min(Math.Round(desired, MidpointRounding.AwayFromZero), ulong.MaxValue);
        SetAttribute(VisaAttribute.TimeoutValue, asked, call);
        double actual = GetAttribute(VisaAttribute.TimeoutValue, call);
        if (actual != desired)
        {
            JgsBuiltins.Warn(call.Host, VisaId + "unableToSetTimeoutValue",
                $"Unable to set the timeout value to {Num2Str(call, seconds)}. Setting timeout to {Num2Str(call, actual / 1000)} instead.");
        }

        _client.Timeout = JgsValue.Number(CheckClientTimeout(JgsValue.Number(actual / 1000), call));
    }

    /// <summary>The shared client's check of a Timeout: a double scalar, nonnegative, nonzero and finite.</summary>
    private static double CheckClientTimeout(JgsValue value, DeviceCall call)
    {
        const string Id = "transportlib:client:InvalidType";
        string cls = DeviceChecks.ClassOf(value);
        if (cls != "double")
        {
            throw call.Error(Id, DeviceChecks.TypeRefusal(new DeviceChecks.Subject(null, "Timeout"), "double", cls, call.Line, call.Column).Message);
        }

        if (DeviceChecks.Count(value) != 1)
        {
            throw call.Error(Id, "Expected Timeout to be a scalar.");
        }

        double x = DeviceChecks.Numbers(value).First();
        if (x < 0)
        {
            throw call.Error(Id, "Expected Timeout to be nonnegative.");
        }

        if (x == 0)
        {
            throw call.Error(Id, "Expected Timeout to be nonzero.");
        }

        if (!double.IsFinite(x))
        {
            throw call.Error(Id, "Expected Timeout to be finite.");
        }

        return x;
    }

    private static string Num2Str(DeviceCall call, double value)
    {
        if (call.Interpreter.Globals.Builtins.TryGet("num2str", out JgsValue function) && function.Type == JgsType.Function)
        {
            JgsValue text = function.AsCallable.Call([JgsValue.Number(value)], call.Line, call.Column);
            if (DeviceChecks.IsText(text))
            {
                return DeviceChecks.Text(text);
            }
        }

        return value.ToString("G5", CultureInfo.InvariantCulture);
    }

    private string SetterPrefix(string property, string? owner = null) =>
        $"Error setting property '{property}' of class '{owner ?? Class.Name}'. ";

    /// <summary>A <c>(1,1) double</c> property's conversion: the scalar, as a double.</summary>
    private double PropertyDouble(string property, JgsValue value, DeviceCall call)
    {
        if (DeviceChecks.Count(value) != 1)
        {
            throw call.Error("MATLAB:validation:IncompatibleSize", SetterPrefix(property) + "Value must be a scalar.");
        }

        if (value.IsStringArray)
        {
            return double.TryParse(value.ElementAt(0).AsString, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : double.NaN;
        }

        if (value.Type is JgsType.Cell or JgsType.Struct or JgsType.External)
        {
            throw call.Error("MATLAB:validation:UnableToConvert", SetterPrefix(property) + "Value must be of type double or be convertible to double.");
        }

        return DeviceChecks.Numbers(value).First();
    }

    private void MustBeMember(string property, double value, double[] set, DeviceCall call)
    {
        if (!set.Contains(value))
        {
            throw call.Error("MATLAB:validators:mustBeMember", SetterPrefix(property) + "Value must be a member of this set:\n"
                + string.Concat(set.Select(static s => "    " + s.ToString(CultureInfo.InvariantCulture) + "\n")));
        }
    }

    /// <summary>An enumeration property's conversion: a member, or its name matched in part and case-blind.</summary>
    private string EnumMember(string property, string owner, JgsValue value, string className, string[] members, DeviceCall call)
    {
        string listed = members.Length == 2
            ? $"'{members[0]}' or '{members[1]}'"
            : string.Join(", ", members[..^1].Select(static m => $"'{m}'")) + $", or '{members[^1]}'";
        if (value.AsExternalOrNull() is DeviceEnumValue member && member.ClassName == className)
        {
            return member.Choice;
        }

        if (DeviceChecks.IsText(value))
        {
            string text = DeviceChecks.Text(value);
            if (text.Length > 0 && DeviceChecks.Match(text, members, out string? found, out bool ambiguous) && !ambiguous)
            {
                return found!;
            }

            throw call.Error("MATLAB:validation:UnableToConvert", SetterPrefix(property, owner) + $"'{text}' is invalid. Value must be {listed}.");
        }

        if (className == DeviceEnumValue.OnOffSwitchState && DeviceChecks.Count(value) == 1
            && DeviceChecks.NumericClasses.Append("logical").Contains(DeviceChecks.ClassOf(value)))
        {
            return DeviceChecks.Numbers(value).First() != 0 ? "on" : "off";
        }

        throw call.Error("MATLAB:validation:UnableToConvert", SetterPrefix(property, owner) + $"Value must be {listed}.");
    }

    private void SetBaudRate(JgsValue value, DeviceCall call)
    {
        double baud = PropertyDouble("BaudRate", value, call);
        if (!(baud > 0))
        {
            throw call.Error("MATLAB:validators:mustBePositive", SetterPrefix("BaudRate") + "Value must be positive.");
        }

        if (baud != Math.Floor(baud) || !double.IsFinite(baud))
        {
            throw call.Error("MATLAB:validators:mustBeInteger", SetterPrefix("BaudRate") + "Value must be integer.");
        }

        bool unsupported = false;
        try
        {
            _session.SetAttribute(VisaAttribute.AsrlBaud, (ulong)Math.Min(baud, uint.MaxValue));
        }
        catch (VisaException e) when (e.Status == VisaStatus.ErrorAttributeStateNotSupported)
        {
            unsupported = true;
        }
        catch (VisaException e)
        {
            throw Translate(e, call, "SetAttributesByType");
        }

        double actual = GetAttribute(VisaAttribute.AsrlBaud, call);
        _values["BaudRate"] = JgsValue.Number(actual);
        if (unsupported && baud != actual)
        {
            JgsBuiltins.Warn(call.Host, VisaId + "unableToSetPropertyValue",
                $"The value of BaudRate property was set to {Number(actual)} because this device does not support setting it to {Number(baud)}.");
        }
    }

    private void SetDataBits(JgsValue value, DeviceCall call)
    {
        double bits = PropertyDouble("DataBits", value, call);
        MustBeMember("DataBits", bits, [5, 6, 7, 8], call);
        SetAttribute(VisaAttribute.AsrlDataBits, (ulong)bits, call);
        _values["DataBits"] = JgsValue.Number(GetAttribute(VisaAttribute.AsrlDataBits, call));
    }

    private void SetStopBits(JgsValue value, DeviceCall call)
    {
        double bits = PropertyDouble("StopBits", value, call);
        MustBeMember("StopBits", bits, [1, 1.5, 2], call);
        SetAttribute(VisaAttribute.AsrlStopBits, bits switch { 1.5 => 15UL, 2 => 20UL, _ => 10UL }, call);
        _values["StopBits"] = JgsValue.Number(StopBitsOf(GetAttribute(VisaAttribute.AsrlStopBits, call)));
    }

    private void SetParity(JgsValue value, DeviceCall call)
    {
        string parity = EnumMember("Parity", Class.Name, value, DeviceEnumValue.Parity, ["none", "even", "odd"], call);
        SetAttribute(VisaAttribute.AsrlParity, parity switch { "odd" => 1UL, "even" => 2UL, _ => 0UL }, call);
        _values["Parity"] = EnumValue(DeviceEnumValue.Parity, GetAttribute(VisaAttribute.AsrlParity, call) switch { 1 => "odd", 2 => "even", _ => "none" });
    }

    private void SetFlowControl(JgsValue value, DeviceCall call)
    {
        string flow = EnumMember("FlowControl", Class.Name, value, DeviceEnumValue.FlowControl, ["none", "hardware", "software"], call);
        SetAttribute(VisaAttribute.AsrlFlowControl, flow switch { "software" => 1UL, "hardware" => 2UL, _ => 0UL }, call);
        ulong read = GetAttribute(VisaAttribute.AsrlFlowControl, call);
        _values["FlowControl"] = EnumValue(DeviceEnumValue.FlowControl, read switch { 1 => "software", 2 or 4 => "hardware", _ => "none" });
    }

    /// <summary>EOIModeSupport's setter: END asserted with the last byte written, and (not over HiSLIP) END ending reads.</summary>
    private void SetEoiMode(bool on, DeviceCall call)
    {
        ulong SetAndRead(uint attribute, ulong value)
        {
            SetAttribute(attribute, value, call);
            return GetAttribute(attribute, call);
        }

        if (on)
        {
            ulong suppress = _checkSuppressEnd ? SetAndRead(VisaAttribute.SuppressEndEnabled, 0) : 0;
            ulong send = SetAndRead(VisaAttribute.SendEndEnabled, 1);
            _eoiMode = _checkSuppressEnd ? suppress == 0 && send != 0 : send != 0;
        }
        else
        {
            _eoiMode = SetAndRead(VisaAttribute.SendEndEnabled, 0) != 0;
        }
    }

    private void SetTcpOption(string property, uint attribute, JgsValue value, DeviceCall call)
    {
        string state = EnumMember(property, Class.Name, value, DeviceEnumValue.OnOffSwitchState, ["off", "on"], call);
        SetAttribute(attribute, state == "on" ? 1UL : 0UL, call);

        // The setter stores what VISA reads back, a number (probe_visa_instr: char(v.KeepAlive) is char(1)).
        _values[property] = JgsValue.Number(GetAttribute(attribute, call));
    }

    private JgsValue PinStatus(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count > 0)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsValue Pin(uint attribute) => DeviceEnumValue.OnOff(Session, Interpreter, GetAttribute(attribute, call) != 0);
        return JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["ClearToSend"] = Pin(VisaAttribute.AsrlCtsState),
            ["DataSendReady"] = Pin(VisaAttribute.AsrlDsrState),
            ["CarrierDetect"] = Pin(VisaAttribute.AsrlDcdState),
            ["RingIndicator"] = Pin(VisaAttribute.AsrlRiState),
        });
    }

    /// <summary>setDTR and setRTS: the arguments block's <c>(1,1) logical</c>, then the pin.</summary>
    private void SetPin(DeviceCall call, uint attribute)
    {
        LiveOrThrow(call.Line, call.Column);
        if (call.Args.Count == 0)
        {
            throw call.Error("MATLAB:minrhs", "Invalid argument list. Function requires 1 more input(s).");
        }

        if (call.Args.Count > 1)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsValue value = call.Args[0];
        string cls = DeviceChecks.ClassOf(value);
        if (!(cls == "logical" || DeviceChecks.NumericClasses.Contains(cls)) || value.Type == JgsType.External)
        {
            throw call.Error("MATLAB:validation:UnableToConvert", "Invalid argument at position 2. Value must be of type logical or be convertible to logical.");
        }

        if (DeviceChecks.Count(value) != 1)
        {
            throw call.Error("MATLAB:validation:IncompatibleSize", "Invalid argument at position 2. Value must be a scalar.");
        }

        SetAttribute(attribute, DeviceChecks.Numbers(value).First() != 0 ? 1UL : 0UL, call);
    }

    private static JgsRuntimeException NotSupported(DeviceCall call, string what, string name) =>
        call.Error(VisaId + what + "NotSupportedForVISA", $"The {name} {(what == "Property" ? "property" : "method")} is no longer supported for visadev objects.");

    /// <summary>LegacyVisa's EOSCharCode: the terminator, with a warning when read and write differ.</summary>
    private JgsValue EosCharCode(DeviceCall call)
    {
        JgsValue terminator = TerminatorValue();
        if (terminator.Type != JgsType.Cell)
        {
            return terminator;
        }

        JgsValue read = terminator.AsCell[0];
        JgsValue write = terminator.AsCell[1];
        bool mismatch = DeviceChecks.ClassOf(read) != DeviceChecks.ClassOf(write) || !SerialportObject.SameValue(read, write);
        if (mismatch)
        {
            static string Word(JgsValue v) => DeviceChecks.IsText(v) ? DeviceChecks.Text(v) : Number(DeviceChecks.Numbers(v).First());
            JgsBuiltins.Warn(call.Host, "transportlib:legacy:EOSCharCodeMismatch",
                $"EOSCharCode returned is not meaningful because read/write-terminators differ.\nRead-terminator is set to {Word(read)}  and write-terminator is set to {Word(write)}.");
        }

        return read;
    }

    // --- the declaration ------------------------------------------------------------------------------------

    private static VisadevObject Me(DeviceObject o) => (VisadevObject)o;

    private static JgsValue[] Void(DeviceCall call, Action<DeviceCall> body)
    {
        if (call.Wanted > 0)
        {
            throw call.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
        }

        body(call);
        return [];
    }

    private static DeviceClass Declare(VisaKind kind)
    {
        (string cls, string shortName) = kind switch
        {
            VisaKind.Gpib => ("visalib.GPIB", "GPIB"),
            VisaKind.Vxi => ("visalib.VXI", "VXI"),
            VisaKind.Serial => ("visalib.Serial", "Serial"),
            VisaKind.Pxi => ("visalib.PXI", "PXI"),
            VisaKind.Tcpip => ("visalib.TCPIP", "TCPIP"),
            VisaKind.Usb => ("visalib.USB", "USB"),
            _ => ("visalib.Socket", "Socket"),
        };

        static DeviceProperty Held(string name, bool hidden = false) => new(name, (o, _) => Me(o)._values[name], Hidden: hidden);

        // The subclass's own properties come first, then visalib.Resource's, Tag, and EOIMode last.
        var properties = new List<DeviceProperty>();
        switch (kind)
        {
            case VisaKind.Serial:
                properties.Add(new DeviceProperty("BaudRate", static (o, _) => Me(o)._values["BaudRate"], static (o, v, c) => Me(o).SetBaudRate(v, c)));
                properties.Add(new DeviceProperty("DataBits", static (o, _) => Me(o)._values["DataBits"], static (o, v, c) => Me(o).SetDataBits(v, c)));
                properties.Add(new DeviceProperty("StopBits", static (o, _) => Me(o)._values["StopBits"], static (o, v, c) => Me(o).SetStopBits(v, c)));
                properties.Add(new DeviceProperty("Parity", static (o, _) => Me(o)._values["Parity"], static (o, v, c) => Me(o).SetParity(v, c)));
                properties.Add(new DeviceProperty("FlowControl", static (o, _) => Me(o)._values["FlowControl"], static (o, v, c) => Me(o).SetFlowControl(v, c)));
                properties.Add(Held("Port"));
                break;
            case VisaKind.Tcpip:
                properties.AddRange([Held("BoardIndex"), Held("LANName"), Held("InstrumentAddress")]);
                break;
            case VisaKind.Socket:
                properties.AddRange([Held("IPAddress"), Held("Port")]);
                break;
            case VisaKind.Usb:
                properties.AddRange([Held("VendorID"), Held("ProductID"), Held("BoardIndex"), Held("InterfaceIndex")]);
                break;
            case VisaKind.Gpib:
                properties.AddRange([Held("BoardIndex"), Held("PrimaryAddress"), Held("SecondaryAddress")]);
                break;
            case VisaKind.Vxi:
                properties.AddRange([Held("ChassisIndex"), Held("LogicalAddress"), Held("Slot")]);
                break;
            case VisaKind.Pxi:
                properties.AddRange([Held("Bus"), Held("DeviceIndex"), Held("FunctionIndex"), Held("ChassisIndex"), Held("Slot")]);
                break;
        }

        static JgsRuntimeException ReadOnlyTerminator(DeviceCall call) =>
            call.Error("transportlib:client:ReadOnlyProperty", "To set \"Terminator\", use the \"configureTerminator\" function.");

        properties.AddRange(
        [
            new DeviceProperty("ByteOrder", static (o, _) => JgsValue.StringScalar(Me(o)._byteOrder), static (o, v, c) =>
            {
                Me(o)._byteOrder = TcpclientObject.ByteOrderOf(v, c, "ByteOrder");
                Me(o)._client.BigEndian = Me(o)._byteOrder == "big-endian";
            }),
            new DeviceProperty("Terminator", static (o, _) => Me(o).TerminatorValue(), static (_, _, c) => throw ReadOnlyTerminator(c)),
            new DeviceProperty("Timeout", static (o, c) => JgsValue.Number(Me(o).GetAttribute(VisaAttribute.TimeoutValue, c) / 1000.0),
                static (o, v, c) => Me(o).SetTimeout(v, c)),
            new DeviceProperty("ErrorOccurredFcn", static (o, _) => Me(o)._client.ErrorOccurredFcn ?? JgsEmpty.Zero(),
                static (o, v, c) => Me(o)._client.ErrorOccurredFcn = Me(o)._client.CallbackValue(v, "InvalidErrorOccurredFcn", "ErrorOccurredFcn must be a function handle.", c)),
            new DeviceProperty("UserData", static (o, _) => Me(o)._client.UserData, static (o, v, _) => Me(o)._client.UserData = v),
            new DeviceProperty("NumBytesWritten", static (o, _) => JgsValue.Number(Me(o)._client.NumBytesWritten)),
            new DeviceProperty("PreferredVisa", static (o, _) => JgsValue.StringScalar(Me(o).PreferredVisa)),
            new DeviceProperty("ResourceName", static (o, _) => JgsValue.StringScalar(Me(o)._info.Name)),
            new DeviceProperty("Alias", static (o, _) => JgsValue.StringScalar(Me(o)._info.Alias)),
            new DeviceProperty("Vendor", static (o, _) => JgsValue.StringScalar(Me(o)._info.Vendor)),
            new DeviceProperty("Model", static (o, _) => JgsValue.StringScalar(Me(o)._info.Model)),
            new DeviceProperty("SerialNumber", static (o, _) => JgsValue.StringScalar(Me(o)._info.SerialNumber)),
            new DeviceProperty("Type", static (o, _) => Me(o).EnumValue(DeviceEnumValue.InterfaceType, KindName(Me(o)._kind))),
            new DeviceProperty("Tag", static (o, _) => JgsValue.StringScalar(Me(o)._tag), static (o, v, c) => Me(o)._tag = NetworkShared.TagOf(v, c)),
        ]);
        bool eoi = kind is not VisaKind.Serial;
        if (eoi)
        {
            properties.Add(new DeviceProperty("EOIMode", static (o, _) => DeviceEnumValue.OnOff(o.Session, o.Interpreter, Me(o)._eoiMode),
                static (o, v, c) => Me(o).SetEoiMode(Me(o).EnumMember("EOIMode", "visalib.EOIModeSupport", v, DeviceEnumValue.OnOffSwitchState, ["off", "on"], c) == "on", c)));
        }

        // Hidden: the asynchronous surface visadev dropped in R2022a, the transfer settings, and LegacyVisa's names.
        foreach (string name in new[] { "BytesAvailableFcnMode", "BytesAvailableFcnCount", "BytesAvailableFcn" })
        {
            properties.Add(new DeviceProperty(name, (_, c) => throw NotSupported(c, "Property", name), (_, _, c) => throw NotSupported(c, "Property", name), Hidden: true));
        }

        properties.AddRange(
        [
            new DeviceProperty("NumBytesAvailable", static (_, c) => throw NotSupported(c, "Property", "NumBytesAvailable"), Hidden: true),
            new DeviceProperty("TransferPeriod", static (o, _) => Me(o)._transferPeriod, static (o, v, _) => Me(o)._transferPeriod = v, Hidden: true),
            new DeviceProperty("TransferSize", static (o, _) => Me(o)._transferSize, static (o, v, _) => Me(o)._transferSize = v, Hidden: true),
            new DeviceProperty("Connected", static (_, _) => JgsValue.Bool(true), Hidden: true),
            new DeviceProperty("ResourceClass", static (o, _) => JgsValue.StringScalar(Me(o)._kind == VisaKind.Socket ? "SOCKET" : "INSTR"), Hidden: true),
            new DeviceProperty("ObjectType", static (_, _) => JgsValue.StringScalar("visadev"), Hidden: true),
            new DeviceProperty("EOSMode", static (o, _) => Me(o)._eosMode, static (o, v, _) => Me(o)._eosMode = v, Hidden: true),
            new DeviceProperty("EOSCharCode", static (o, c) => Me(o).EosCharCode(c), Hidden: true),
            new DeviceProperty("RsrcName", static (o, _) => JgsValue.Str(Me(o)._info.Name), Hidden: true),
            new DeviceProperty("Status", static (_, _) => JgsValue.Str("open"), Hidden: true),
            new DeviceProperty("InputBufferSize", static (o, _) => Me(o)._inputBufferSize, static (o, v, _) => Me(o)._inputBufferSize = v, Hidden: true),
            new DeviceProperty("OutputBufferSize", static (o, _) => Me(o)._outputBufferSize, static (o, v, _) => Me(o)._outputBufferSize = v, Hidden: true),
            new DeviceProperty("ErrorFcn", static (o, _) => Me(o)._client.ErrorOccurredFcn ?? JgsEmpty.Zero(),
                static (o, v, c) => Me(o)._client.ErrorOccurredFcn = Me(o)._client.CallbackValue(v, "InvalidErrorOccurredFcn", "ErrorOccurredFcn must be a function handle.", c), Hidden: true),
            new DeviceProperty("BytesAvailable", static (_, c) => throw NotSupported(c, "Property", "NumBytesAvailable"), Hidden: true),
        ]);
        if (kind is VisaKind.Tcpip or VisaKind.Socket)
        {
            properties.Add(new DeviceProperty("KeepAlive", static (o, _) => Me(o)._values["KeepAlive"],
                static (o, v, c) => Me(o).SetTcpOption("KeepAlive", VisaAttribute.TcpipKeepAlive, v, c), Hidden: true));
            properties.Add(new DeviceProperty("NoDelay", static (o, _) => Me(o)._values["NoDelay"],
                static (o, v, c) => Me(o).SetTcpOption("NoDelay", VisaAttribute.TcpipNoDelay, v, c), Hidden: true));
        }

        foreach (string name in SerialportObject.UnsupportedProperties)
        {
            properties.Add(new DeviceProperty(name,
                (_, c) =>
                {
                    Unsupported(c, "PropertyNotSupported", name == "LocalPortMode" ? "LocalPortMod" : name);
                    return JgsEmpty.Zero();
                },
                (_, _, c) => Unsupported(c, "PropertyNotSupported", name == "LocalPortMode" ? "LocalPortMod" : name),
                Hidden: true));
        }

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["read"] = static c => [Me(c.Target).Read(c)],
            ["readline"] = static c => [Me(c.Target).ReadLine(c)],
            ["readbinblock"] = static c => [Me(c.Target).ReadBinblock(c)],
            ["write"] = static c => Void(c, static c => Me(c.Target).Live(c).Write(c)),
            ["writeline"] = static c => Void(c, static c => Me(c.Target).Live(c).WriteLine(c)),
            ["writebinblock"] = static c => Void(c, static c => Me(c.Target).Live(c).WriteBinblock(c)),
            ["writeread"] = static c => [Me(c.Target).WriteRead(c)],
            ["configureTerminator"] = static c => Void(c, static c => Me(c.Target).ConfigureTerminator(c)),
            ["flush"] = static c => Void(c, static c => Me(c.Target).Flush(c)),
            ["visastatus"] = static c => Me(c.Target).VisaStatusOf(c),
            ["configureCallback"] = static c => Void(c, static c =>
            {
                c.Target.LiveOrThrow(c.Line, c.Column);
                throw NotSupported(c, "Method", "configureCallback");
            }),

            // LegacyBase, the four mixins, and LegacyVisa's clrdevice.
            ["fopen"] = static c => Void(c, static c => c.Target.LiveOrThrow(c.Line, c.Column)),
            ["fclose"] = static c => Void(c, static c =>
            {
                c.Target.LiveOrThrow(c.Line, c.Column);
                JgsBuiltins.Warn(c.Host, "transportlib:legacy:DoesNotCloseConnection",
                    "The fclose method does not close the connection. Instead, clear the interface object to close the connection.");
            }),
            ["flushinput"] = static c => Void(c, static c => Me(c.Target).Flush(SerialportObject.Retarget(c, [JgsValue.Str("input")]))),
            ["flushoutput"] = static c => Void(c, static c => Me(c.Target).Flush(SerialportObject.Retarget(c, [JgsValue.Str("output")]))),
            ["clrdevice"] = static c => Void(c, static c => Me(c.Target).Flush(SerialportObject.Retarget(c, []))),
            ["fprintf"] = static c => Void(c, static c => SerialportLegacy.Fprintf(Me(c.Target), c)),
            ["fwrite"] = static c => Void(c, static c => SerialportLegacy.Fwrite(Me(c.Target), c)),
            ["fread"] = static c => SerialportLegacy.Fread(Me(c.Target), c),
            ["fgetl"] = static c => SerialportLegacy.Fgetl(Me(c.Target), c, keepTerminator: false),
            ["fgets"] = static c => SerialportLegacy.Fgetl(Me(c.Target), c, keepTerminator: true),
            ["fscanf"] = static c => SerialportLegacy.Fscanf(Me(c.Target), c),
            ["scanstr"] = static c => SerialportLegacy.Scanstr(Me(c.Target), c),
            ["query"] = static c => SerialportLegacy.Query(Me(c.Target), c),
            ["binblockread"] = static c => SerialportLegacy.BinblockRead(Me(c.Target), c),
            ["binblockwrite"] = static c => Void(c, static c => SerialportLegacy.BinblockWrite(Me(c.Target), c)),
        };
        foreach (string name in SerialportObject.UnsupportedMethods)
        {
            methods[name] = c =>
            {
                Unsupported(c, "MethodNotSupported", name);
                return [];
            };
        }

        var listing = new List<string>
        {
            "addlistener", "configureTerminator", "eq", "findobj", "findprop", "flush", "ge", "get", "gt", "isvalid", "le", "listener",
            "lt", "ne", "notify", "read", "readbinblock", "readline", "set", "visastatus", "write", "writebinblock", "writeline", "writeread",
        };
        if (kind == VisaKind.Serial)
        {
            methods["getpinstatus"] = static c => [Me(c.Target).PinStatus(c)];
            methods["setDTR"] = static c => Void(c, static c => Me(c.Target).SetPin(c, VisaAttribute.AsrlDtrState));
            methods["setRTS"] = static c => Void(c, static c => Me(c.Target).SetPin(c, VisaAttribute.AsrlRtsState));
            listing.AddRange(["getpinstatus", "setDTR", "setRTS"]);
        }

        if (kind is VisaKind.Gpib or VisaKind.Vxi)
        {
            methods["visatrigger"] = static c => Void(c, static c => Me(c.Target).VisaTrigger(c));
            listing.Add("visatrigger");
        }

        listing.Sort(StringComparer.OrdinalIgnoreCase);

        List<string> bases =
        [
            "visalib.Resource", "matlabshared.testmeas.internal.SetGet", "matlab.mixin.SetGet", "handle", "matlab.mixin.Heterogeneous",
            "matlabshared.testmeas.CustomDisplay", "matlab.mixin.CustomDisplay", "matlabshared.transportlib.internal.compatibility.LegacyBase",
            "matlabshared.transportlib.internal.compatibility.LegacyBinaryMixin", "matlabshared.transportlib.internal.compatibility.LegacyNullMixin",
            "matlabshared.transportlib.internal.compatibility.LegacyASCIIMixin", "matlabshared.transportlib.internal.compatibility.LegacyBinblockMixin",
            "matlabshared.transportlib.internal.compatibility.LegacyQueryMixin", "matlabshared.testmeas.internal.mixins.CacheEnabler",
            "matlabshared.transportlib.internal.TagAccessor",
        ];
        if (eoi)
        {
            bases.Add("visalib.EOIModeSupport");
        }

        // The short display: the resource's names, the class's own, and Tag (Resource.getGroupList).
        List<string> display = kind switch
        {
            VisaKind.Serial => ["ResourceName", "Alias", "Port", "BaudRate", "Tag"],
            VisaKind.Tcpip => ["ResourceName", "Alias", "Vendor", "Model", "LANName", "InstrumentAddress", "Tag"],
            VisaKind.Socket => ["ResourceName", "Alias", "Vendor", "Model", "IPAddress", "Port", "Tag"],
            VisaKind.Gpib => ["ResourceName", "Alias", "Vendor", "Model", "BoardIndex", "PrimaryAddress", "SecondaryAddress", "Tag"],
            _ => ["ResourceName", "Alias", "Vendor", "Model", "Tag"],
        };

        return new DeviceClass(cls, shortName, bases, properties, methods, listing, display);
    }

    private static void Unsupported(DeviceCall call, string key, string name) =>
        JgsBuiltins.Warn(call.Host, "transportlib:legacy:" + key,
            key == "MethodNotSupported" ? $"{name} is not a valid method for this interface." : $"{name} is not a valid property for this interface.");

    // --- ILegacyTransport: the mixins reach visalib.Resource's methods ------------------------------------------

    JgsValue ILegacyTransport.LegacyRead(DeviceCall call) => Read(call);

    JgsValue ILegacyTransport.LegacyReadLine(DeviceCall call) => ReadLine(call);

    JgsValue ILegacyTransport.LegacyWriteRead(DeviceCall call) => WriteRead(call);

    JgsValue ILegacyTransport.LegacyReadBinblock(DeviceCall call) => ReadBinblock(call);

    void ILegacyTransport.LegacyWrite(DeviceCall call) => Live(call).Write(call);

    void ILegacyTransport.LegacyWriteLine(DeviceCall call) => Live(call).WriteLine(call);

    void ILegacyTransport.LegacyWriteBinblock(DeviceCall call) => Live(call).WriteBinblock(call);

    protected override void OnDelete()
    {
        // Serial.preDisconnectHook: flow control off before the port closes.
        if (_kind == VisaKind.Serial)
        {
            try
            {
                _session.SetAttribute(VisaAttribute.AsrlFlowControl, 0);
            }
            catch (VisaException)
            {
            }
        }

        _session.Dispose();
        Session.VisaOpen.Remove(_info.Name.ToUpperInvariant());
    }

    /// <summary>
    /// The shared client's transport for a visadev: its input is filled only by the synchronous reads
    /// above (nothing arrives unasked, as nothing does with SynchronousRead); a write is a VISA write.
    /// </summary>
    private sealed class VisaChannel(string name, IVisaSession session, Interpreter interpreter) : IDeviceTransport
    {
        private long _written;

        public string Name => name;

        public bool Connected => true;

        public InputBuffer Input { get; } = new();

        public long BytesWritten => Interlocked.Read(ref _written);

        public void Write(ReadOnlySpan<byte> data, TimeSpan timeout, CancellationToken cancel)
        {
            try
            {
                session.Write(data, interpreter.Cancellation);
            }
            catch (VisaException e)
            {
                throw new DeviceIOException(e.Message, e.Status);
            }

            Interlocked.Add(ref _written, data.Length);
        }

        public void FlushInput() => Input.Clear();

        public void FlushOutput()
        {
        }

        public event Action<DeviceConnectionLostException>? ConnectionLost
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }
}
