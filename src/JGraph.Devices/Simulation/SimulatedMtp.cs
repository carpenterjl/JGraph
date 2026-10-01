using System.Globalization;
using System.Text;
using JGraph.Devices.Mtp;

namespace JGraph.Devices.Simulation;

/// <summary>
/// Portable devices for the tests and <c>jgraph.internal.mtpsim</c> (device classes plan, stage D12):
/// <list type="bullet">
/// <item>"JGraph Test Phone", MTP, with two storages. "Internal storage" holds DCIM/Camera with two
/// pictures, Documents with notes.txt, and an empty Music; "SD card" holds readme.txt. It cannot take
/// a picture when asked.</item>
/// <item>"JGraph Test Camera", PTP, with "Memory card" holding DCIM/100JGRPH/DSC_0001.JPG. Asked to
/// capture, it adds the next DSC_ file there.</item>
/// </list>
/// A picture's bytes are its number plus the byte's place, so a download can be checked. An unplugged
/// device refuses everything. Nothing here touches a real device.
/// </summary>
public sealed class SimulatedMtp : IMtpBackend
{
    public const string PhoneName = "JGraph Test Phone";
    public const string CameraName = "JGraph Test Camera";

    private sealed class Node(string id, string name, bool folder, bool storage, byte[]? data, DateTime? modified)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public bool IsFolder { get; } = folder;

        public bool IsStorage { get; } = storage;

        public byte[] Data { get; } = data ?? [];

        public DateTime? Modified { get; } = modified;

        public List<Node> Children { get; } = [];

        public long Capacity { get; init; }
    }

    private sealed class Device : IMtpDevice
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Node> _byId = new(StringComparer.Ordinal);
        private readonly Node _root = new(MtpPaths.RootId, "", true, false, null, null);
        private readonly bool _captures;
        private int _next = 1;

        public Device(MtpDeviceInfo info, IReadOnlyDictionary<string, string> properties, bool captures)
        {
            Info = info;
            Properties = properties;
            _captures = captures;
            _byId[_root.Id] = _root;
        }

        public MtpDeviceInfo Info { get; }

        public IReadOnlyDictionary<string, string> Properties { get; }

        public bool Unplugged { get; set; }

        public int OpenCount { get; set; }

        /// <summary>Adds an object by path, making the folders above it; a storage is a top-level folder with a capacity.</summary>
        public void Add(string path, byte[]? data, DateTime? modified, long capacity = 0)
        {
            string[] names = MtpPaths.Split(path);
            Node at = _root;
            for (int i = 0; i < names.Length; i++)
            {
                Node? next = at.Children.Find(c => c.Name == names[i]);
                if (next is null)
                {
                    bool last = i == names.Length - 1;
                    bool folder = !last || data is null;
                    next = new Node(at == _root ? $"s{_next++:X5}" : $"o{_next++:X}", names[i], folder, at == _root, last ? data : null, folder ? null : modified)
                    {
                        Capacity = at == _root ? capacity : 0,
                    };
                    at.Children.Add(next);
                    _byId[next.Id] = next;
                }

                at = next;
            }
        }

        private Node Live(string id)
        {
            if (Unplugged)
            {
                throw new MtpException($"{Info.Name} is no longer connected.");
            }

            return _byId.TryGetValue(id, out Node? node) ? node : throw new MtpException($"{Info.Name} has no object with the ID {id}.");
        }

        public IReadOnlyList<MtpEntry> Children(string parentId)
        {
            lock (_gate)
            {
                return Live(parentId).Children.Select(static c => new MtpEntry(c.Id, c.Name, c.IsFolder, c.IsStorage, c.Data.LongLength, c.Modified)).ToList();
            }
        }

        private static long Used(Node node) => node.Data.LongLength + node.Children.Sum(Used);

        public (long Capacity, long Free) Space(string storageId)
        {
            lock (_gate)
            {
                Node storage = Live(storageId);
                return (storage.Capacity, storage.Capacity - Used(storage));
            }
        }

        public void Download(string id, Stream to)
        {
            lock (_gate)
            {
                Node node = Live(id);
                if (node.IsFolder)
                {
                    throw new MtpException($"{node.Name} is a folder, which has no bytes to download.");
                }

                to.Write(node.Data);
            }
        }

        private Node NewChild(string parentId, string name, bool folder, byte[]? data)
        {
            Node parent = Live(parentId);
            if (!parent.IsFolder || parent == _root)
            {
                throw new MtpException(parent == _root ? "A file or folder goes into a storage, not beside the storages." : $"{parent.Name} is a file, not a folder.");
            }

            if (parent.Children.Exists(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new MtpException($"{name} is already there.");
            }

            var node = new Node($"o{_next++:X}", name, folder, false, data, folder ? null : new DateTime(2026, 9, 30, 12, 0, 0));
            parent.Children.Add(node);
            _byId[node.Id] = node;
            return node;
        }

        public string Upload(string parentId, string name, Stream from, long size)
        {
            lock (_gate)
            {
                byte[] data = new byte[size];
                from.ReadExactly(data);
                return NewChild(parentId, name, false, data).Id;
            }
        }

        public string CreateFolder(string parentId, string name)
        {
            lock (_gate)
            {
                return NewChild(parentId, name, true, null).Id;
            }
        }

        public void Delete(string id, bool recursive)
        {
            lock (_gate)
            {
                Node node = Live(id);
                if (node == _root || node.IsStorage)
                {
                    throw new MtpException("A storage cannot be deleted.");
                }

                if (node.Children.Count > 0 && !recursive)
                {
                    throw new MtpException($"{node.Name} is not empty.");
                }

                void Forget(Node n)
                {
                    _byId.Remove(n.Id);
                    n.Children.ForEach(Forget);
                }

                Forget(node);
                foreach (Node parent in _byId.Values)
                {
                    parent.Children.Remove(node);
                }
            }
        }

        public void Capture()
        {
            lock (_gate)
            {
                Live(MtpPaths.RootId);
                if (!_captures)
                {
                    throw new MtpException($"{Info.Name} has no still-image capture function.");
                }

                Node folder = _root.Children[0].Children[0].Children[0];
                int number = folder.Children.Count + 1;
                NewChild(folder.Id, string.Create(CultureInfo.InvariantCulture, $"DSC_{number:D4}.JPG"), false, Picture(100 + number, 1024));
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                OpenCount = Math.Max(0, OpenCount - 1);
            }
        }
    }

    private readonly Device _phone;
    private readonly Device _camera;

    public SimulatedMtp()
    {
        _phone = new Device(new MtpDeviceInfo(PhoneName, "JGraph", "Simulated MTP phone", @"\\?\sim#mtp#phone"), new Dictionary<string, string>
        {
            ["Model"] = "Test Phone",
            ["SerialNumber"] = "SIM-MTP-0001",
            ["FirmwareVersion"] = "1.2.3",
            ["Protocol"] = "MTP: 1.00",
            ["Type"] = "phone",
        }, captures: false);
        var day = new DateTime(2026, 1, 2, 3, 4, 5);
        _phone.Add("Internal storage", null, null, 64_000_000_000);
        _phone.Add("Internal storage/DCIM/Camera/IMG_0001.jpg", Picture(1, 2048), day);
        _phone.Add("Internal storage/DCIM/Camera/IMG_0002.jpg", Picture(2, 4096), day.AddDays(1));
        _phone.Add("Internal storage/Documents/notes.txt", Encoding.ASCII.GetBytes("Hello from the JGraph test phone.\n"), day.AddDays(2));
        _phone.Add("Internal storage/Music", null, null);
        _phone.Add("SD card", null, null, 32_000_000_000);
        _phone.Add("SD card/readme.txt", Encoding.ASCII.GetBytes("SD card\n"), day.AddDays(3));

        _camera = new Device(new MtpDeviceInfo(CameraName, "JGraph", "Simulated PTP camera", @"\\?\sim#mtp#camera"), new Dictionary<string, string>
        {
            ["Model"] = "Test Camera",
            ["SerialNumber"] = "SIM-PTP-0001",
            ["FirmwareVersion"] = "0.9",
            ["Protocol"] = "PTP: 1.00",
            ["Type"] = "camera",
        }, captures: true);
        _camera.Add("Memory card", null, null, 16_000_000_000);
        _camera.Add("Memory card/DCIM/100JGRPH/DSC_0001.JPG", Picture(101, 1024), day);
    }

    /// <summary>A picture's bytes: byte i is <paramref name="number"/> + i, modulo 256.</summary>
    public static byte[] Picture(int number, int size)
    {
        byte[] data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = (byte)(number + i);
        }

        return data;
    }

    private Device? Named(string name) => name == PhoneName ? _phone : name == CameraName ? _camera : null;

    /// <summary>Takes a simulated device away: it leaves the list, and an object open on it refuses everything.</summary>
    public void Unplug(string name)
    {
        if (Named(name) is { } device)
        {
            device.Unplugged = true;
        }
    }

    /// <summary>Brings a simulated device back.</summary>
    public void Plug(string name)
    {
        if (Named(name) is { } device)
        {
            device.Unplugged = false;
        }
    }

    /// <summary>How many times the devices are open.</summary>
    public int OpenCount => _phone.OpenCount + _camera.OpenCount;

    public IReadOnlyList<MtpDeviceInfo> Devices() => new[] { _phone, _camera }.Where(static d => !d.Unplugged).Select(static d => d.Info).ToList();

    public IMtpDevice Open(MtpDeviceInfo device)
    {
        Device found = new[] { _phone, _camera }.FirstOrDefault(d => d.Info.Id == device.Id && !d.Unplugged)
            ?? throw new MtpException($"{device.Name} is no longer connected.");
        found.OpenCount++;
        return found;
    }
}
