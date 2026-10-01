namespace JGraph.Devices.Mtp;

/// <summary>An MTP or PTP failure, in words a script can show; <see cref="Code"/> is the HRESULT when Windows gave one.</summary>
public sealed class MtpException(string message, int code = 0) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>A portable device as the list shows it.</summary>
/// <param name="Name">Its friendly name ("Pixel 8").</param>
/// <param name="Manufacturer">Who made it.</param>
/// <param name="Description">Its description.</param>
/// <param name="Id">The path Windows opens it by.</param>
public sealed record MtpDeviceInfo(string Name, string Manufacturer, string Description, string Id);

/// <summary>One object on a device: a storage, a folder or a file.</summary>
/// <param name="Id">The device's own ID for it.</param>
/// <param name="Name">Its file name; a storage's is its label.</param>
/// <param name="IsFolder">Whether it holds other objects: a storage or a folder.</param>
/// <param name="IsStorage">Whether it is a storage, the top of a tree.</param>
/// <param name="Size">A file's size in bytes; 0 for a folder.</param>
/// <param name="Modified">When it was last changed, as the device tells it; null when it does not.</param>
public sealed record MtpEntry(string Id, string Name, bool IsFolder, bool IsStorage, long Size, DateTime? Modified);

/// <summary>An open portable device.</summary>
public interface IMtpDevice : IDisposable
{
    MtpDeviceInfo Info { get; }

    /// <summary>Model, SerialNumber, FirmwareVersion, Protocol and Type, each empty when the device does not say.</summary>
    IReadOnlyDictionary<string, string> Properties { get; }

    /// <summary>The objects directly under <paramref name="parentId"/>; under <see cref="MtpPaths.RootId"/>, the storages.</summary>
    IReadOnlyList<MtpEntry> Children(string parentId);

    /// <summary>A storage's capacity and free space in bytes.</summary>
    (long Capacity, long Free) Space(string storageId);

    /// <summary>Copies a file's bytes into <paramref name="to"/>.</summary>
    void Download(string id, Stream to);

    /// <summary>Creates a file under <paramref name="parentId"/> from <paramref name="from"/>'s next <paramref name="size"/> bytes; answers its ID.</summary>
    string Upload(string parentId, string name, Stream from, long size);

    /// <summary>Creates a folder; answers its ID.</summary>
    string CreateFolder(string parentId, string name);

    /// <summary>Deletes an object for good; a folder that holds anything needs <paramref name="recursive"/>.</summary>
    void Delete(string id, bool recursive);

    /// <summary>Asks the device to take a picture (PTP's InitiateCapture); the picture is a new object in its storage.</summary>
    void Capture();
}

/// <summary>The portable devices a script reaches (device classes plan, stage D12): the machine's through WPD, or the simulated ones.</summary>
public interface IMtpBackend
{
    IReadOnlyList<MtpDeviceInfo> Devices();

    IMtpDevice Open(MtpDeviceInfo device);
}

/// <summary>
/// Paths on a portable device. A device has no paths of its own, only objects with parents, so a path
/// is read here: storages first, then folder names, parted by <c>/</c> or <c>\</c>. A name is matched
/// exactly, else by the one object that differs from it only in case.
/// </summary>
public static class MtpPaths
{
    /// <summary>The ID of the device itself, whose children are its storages (WPD_DEVICE_OBJECT_ID).</summary>
    public const string RootId = "DEVICE";

    /// <summary>The device itself as an entry.</summary>
    public static MtpEntry Root { get; } = new(RootId, "", true, false, 0, null);

    /// <summary>A path's names; none for the root.</summary>
    public static string[] Split(string path) => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A path written the one way: names joined by <c>/</c>, none leading.</summary>
    public static string Join(IEnumerable<string> names) => string.Join("/", names);

    /// <summary>One of <paramref name="entries"/> by name; null when none has it, or several differ from it only in case.</summary>
    public static MtpEntry? Pick(IReadOnlyList<MtpEntry> entries, string name)
    {
        MtpEntry? exact = entries.FirstOrDefault(e => e.Name == name);
        if (exact is not null)
        {
            return exact;
        }

        List<MtpEntry> loose = entries.Where(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        return loose.Count == 1 ? loose[0] : null;
    }

    /// <summary>
    /// The object a path names, with the path as the device spells it; null when some name along it is
    /// not there, and <paramref name="missing"/> is then the first such name.
    /// </summary>
    public static (MtpEntry Entry, string Path)? Find(IMtpDevice device, string path, out string missing)
    {
        missing = "";
        MtpEntry at = Root;
        var spelled = new List<string>();
        foreach (string name in Split(path))
        {
            MtpEntry? next = at.IsFolder ? Pick(device.Children(at.Id), name) : null;
            if (next is null)
            {
                missing = name;
                return null;
            }

            at = next;
            spelled.Add(next.Name);
        }

        return (at, Join(spelled));
    }
}
