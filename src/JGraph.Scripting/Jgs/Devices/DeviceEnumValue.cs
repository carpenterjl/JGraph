namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// A member of one of the enumerations the device classes answer with (device classes plan, stage D4):
/// <c>visalib.InterfaceType</c>, <c>visalib.Parity</c>, <c>visalib.FlowControl</c> and
/// <c>matlab.lang.OnOffSwitchState</c>. JGraph has no enumeration classes, so a member is a small
/// external that answers what R2025b's do (probe_visa_misc): <c>==</c> and <c>~=</c> against its
/// name as text or against a member of its class, <c>char</c>, <c>string</c>, <c>isequal</c> with text,
/// and for OnOffSwitchState <c>logical</c> and <c>==</c> against a logical.
/// </summary>
internal sealed class DeviceEnumValue : DeviceObject
{
    public const string InterfaceType = "visalib.InterfaceType";
    public const string Parity = "visalib.Parity";
    public const string FlowControl = "visalib.FlowControl";
    public const string OnOffSwitchState = "matlab.lang.OnOffSwitchState";

    private static readonly Dictionary<string, DeviceClass> Classes = new(StringComparer.Ordinal);

    private DeviceEnumValue(DeviceSession session, Interpreter interpreter, string className, string member)
        : base(session, interpreter)
    {
        Choice = member;
        lock (Classes)
        {
            if (!Classes.TryGetValue(className, out DeviceClass? declared))
            {
                declared = Declare(className);
                Classes[className] = declared;
            }

            Class = declared;
        }
    }

    /// <summary>The member's name: <c>serial</c>, <c>even</c>, <c>on</c>.</summary>
    public string Choice { get; }

    public override DeviceClass Class { get; }

    /// <summary>A member as a script value.</summary>
    public static JgsValue Of(DeviceSession session, Interpreter interpreter, string className, string member) =>
        JgsValue.External(new DeviceEnumValue(session, interpreter, className, member));

    /// <summary>An OnOffSwitchState member.</summary>
    public static JgsValue OnOff(DeviceSession session, Interpreter interpreter, bool on) =>
        Of(session, interpreter, OnOffSwitchState, on ? "on" : "off");

    /// <summary>Whether <paramref name="value"/> is this member: its name as text, a member of its class, or (OnOffSwitchState) a logical.</summary>
    public bool Matches(JgsValue value)
    {
        if (value.AsExternalOrNull() is DeviceEnumValue other)
        {
            return other.ClassName == ClassName && other.Choice == Choice;
        }

        if (DeviceChecks.IsText(value))
        {
            return DeviceChecks.Text(value) == Choice;
        }

        if (ClassName == OnOffSwitchState && value.Type != JgsType.External && DeviceChecks.Count(value) == 1
            && DeviceChecks.NumericClasses.Append("logical").Contains(DeviceChecks.ClassOf(value)))
        {
            return (DeviceChecks.Numbers(value).First() != 0) == (Choice == "on");
        }

        return false;
    }

    /// <summary>The member's name, the text <c>char</c> and a display line show.</summary>
    public override string? Summary() => Choice;

    protected override string ShortDisplay()
    {
        string shortName = ClassName[(ClassName.LastIndexOf('.') + 1)..];
        return $"{shortName} enumeration\n\n    {Choice}";
    }

    protected override void OnDelete()
    {
    }

    private static DeviceClass Declare(string className)
    {
        static DeviceEnumValue Me(DeviceObject o) => (DeviceEnumValue)o;
        static JgsValue[] One(DeviceCall call, JgsValue value)
        {
            if (call.Args.Count != 0)
            {
                throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
            }

            return [value];
        }

        var methods = new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal)
        {
            ["char"] = static c => One(c, JgsValue.Str(Me(c.Target).Choice)),
            ["string"] = static c => One(c, JgsValue.StringScalar(Me(c.Target).Choice)),
            ["isequal"] = static c => [JgsValue.Bool(c.Args.All(a => Me(c.Target).Matches(a)))],
        };
        if (className == OnOffSwitchState)
        {
            methods["logical"] = static c => One(c, JgsValue.Bool(Me(c.Target).Choice == "on"));
            methods["double"] = static c => One(c, JgsValue.Number(Me(c.Target).Choice == "on" ? 1 : 0));
        }

        string shortName = className[(className.LastIndexOf('.') + 1)..];
        return new DeviceClass(className, shortName, [], [], methods, [], []);
    }
}
