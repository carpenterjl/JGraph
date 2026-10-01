using System.Text;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// A row of device objects of one class: what <c>serialportfind</c> answers when several objects match,
/// where R2025b answers an object array. It indexes with parentheses to one object and answers
/// <c>numel</c>, <c>size</c> and the other questions about its shape as a 1-by-N array does (ADR 0197);
/// a dot or a method on the row itself is refused.
/// </summary>
/// <remarks>
/// It is not R2025b's object array (probe_dev_arrays records what that does): brackets do not make
/// one, it has no other shape, a vector of subscripts does not index it, a loop does not walk it,
/// and it does not hold its objects open. Those wait on the open-items file.
/// </remarks>
internal sealed class DeviceArray(IReadOnlyList<DeviceObject> items) : IJgsExternal
{
    public IReadOnlyList<DeviceObject> Items { get; } = items;

    public string ClassName => Items[0].ClassName;

    public bool IsHandle => true;

    public bool IsA(string className) => Items[0].IsA(className);

    public IJgsExternal CopyForBinding() => this;

    public string Kind => "device object array";

    public string Display()
    {
        var sb = new StringBuilder($"1x{Items.Count} {Items[0].Class.ShortName} array with properties:\n");
        foreach (DeviceObject item in Items)
        {
            sb.Append('\n').Append(item.Display());
        }

        return sb.ToString();
    }

    public string? Summary() => $"{Items.Count} objects";
}
