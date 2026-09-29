namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// The event data a <c>BytesAvailableFcn</c> receives: <c>instrument.internal.DataAvailableInfo</c>, a
/// handle with <c>BytesAvailableFcnCount</c> (the count in "byte" mode, 1 in "terminator" mode) and
/// <c>AbsTime</c>, a datetime of when the bytes arrived (probe_sp_callbacks).
/// </summary>
internal sealed class DataAvailableInfo : DeviceObject
{
    private static readonly DeviceClass Declaration = new(
        "instrument.internal.DataAvailableInfo",
        "DataAvailableInfo",
        ["handle"],
        [
            new DeviceProperty("BytesAvailableFcnCount", static (o, _) => JgsValue.Number(((DataAvailableInfo)o)._count)),
            new DeviceProperty("AbsTime", static (o, _) => ((DataAvailableInfo)o).AbsTime),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal),
        [],
        ["BytesAvailableFcnCount", "AbsTime"]);

    private readonly double _count;
    private readonly DateTime _at;

    public DataAvailableInfo(DeviceSession session, Interpreter interpreter, double count, DateTime at)
        : base(session, interpreter)
    {
        _count = count;
        _at = at;
    }

    public override DeviceClass Class => Declaration;

    private JgsValue AbsTime => JgsBuiltins.DatetimeValue(_at);

    protected override void OnDelete()
    {
    }

    protected override string ShortDisplay()
    {
        return $"DataAvailableInfo with properties:\n\n    BytesAvailableFcnCount: {Shown(JgsValue.Number(_count))}\n                   AbsTime: {Shown(AbsTime)}";
    }
}

/// <summary>
/// What an <c>ErrorOccurredFcn</c> receives: <c>matlabshared.transportlib.internal.ErrorInfo</c> with the
/// error's <c>ID</c> and <c>Message</c>.
/// </summary>
internal sealed class ErrorInfo : DeviceObject
{
    private static readonly DeviceClass Declaration = new(
        "matlabshared.transportlib.internal.ErrorInfo",
        "ErrorInfo",
        ["handle"],
        [
            new DeviceProperty("ID", static (o, _) => JgsValue.StringScalar(((ErrorInfo)o)._id)),
            new DeviceProperty("Message", static (o, _) => JgsValue.StringScalar(((ErrorInfo)o)._message)),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal),
        [],
        ["ID", "Message"]);

    private readonly string _id;
    private readonly string _message;

    public ErrorInfo(DeviceSession session, Interpreter interpreter, string id, string message)
        : base(session, interpreter)
    {
        _id = id;
        _message = message;
    }

    public override DeviceClass Class => Declaration;

    protected override void OnDelete()
    {
    }
}
