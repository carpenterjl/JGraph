using System.Text;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// A row of device objects of one class: what <c>serialportfind</c> answers when several objects match,
/// where R2025b answers an object array. It indexes with parentheses to one object and answers
/// <c>numel</c> and <c>size</c>; a dot or a method on the row itself is refused, as R2025b refuses most
/// of them on an array.
/// </summary>
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
