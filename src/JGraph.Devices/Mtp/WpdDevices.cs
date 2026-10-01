using System.ComponentModel;
using System.Globalization;
using System.Runtime.Versioning;
using static JGraph.Devices.Mtp.WpdNative;

namespace JGraph.Devices.Mtp;

/// <summary>
/// The machine's portable devices through Windows Portable Devices (device classes plan, stage D12):
/// phones and players that speak MTP, cameras that speak PTP, and whatever else Windows lists as
/// one (a USB stick's volume appears too). Every call runs on a thread of its own in the
/// multithreaded apartment, where the free-threaded device class wants its callers.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WpdDevices : IMtpBackend
{
    public static WpdDevices Instance { get; } = new();

    private WpdDevices()
    {
    }

    /// <summary>Runs <paramref name="work"/> in the multithreaded apartment; answers what it answered or rethrows what it threw.</summary>
    internal static T InMta<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                failure = e;
            }
        })
        {
            IsBackground = true,
            Name = "JGraph portable device call",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        return failure switch
        {
            null => result,
            MtpException or IOException or UnauthorizedAccessException => throw failure,
            DllNotFoundException or EntryPointNotFoundException => throw new MtpException("Windows Portable Devices is not installed on this machine."),
            _ => throw new MtpException(failure.Message),
        };
    }

    /// <summary>Throws what an HRESULT means, in a sentence that begins with <paramref name="what"/>.</summary>
    internal static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new MtpException($"{what}: {Describe(hr)}.", hr);
        }
    }

    /// <summary>The words for the failures a portable device gives most.</summary>
    public static string Describe(int hr) => (uint)hr switch
    {
        0x80070005 => "the device refused (a phone must be unlocked and set to transfer files)",
        0x8007001F => "the device is not answering (a phone must be unlocked and set to transfer files)",
        0x80070015 => "the device is not ready",
        0x800700AA => "the device is busy",
        0x80070032 => "the device does not support that",
        0x80070070 => "the device is full",
        0x800700B7 => "an object of that name is already there",
        0x80070490 => "the device has no such object",
        0x80070002 => "the device has no such object",
        0x8007048F => "the device is no longer connected",
        0x802A0002 => "the device is not open",
        0x802A0006 => "the device stopped answering",
        0x80040154 => "Windows Portable Devices is not installed on this machine",
        _ => ((uint)hr & 0xFFFF0000) == 0x80070000
            ? new Win32Exception(hr & 0xFFFF).Message.TrimEnd('.', ' ') + " (" + Hex(hr) + ")"
            : "error " + Hex(hr),
    };

    internal static string Hex(int hr) => "0x" + hr.ToString("X8", CultureInfo.InvariantCulture);

    public IReadOnlyList<MtpDeviceInfo> Devices()
    {
        try
        {
            return InMta(Enumerate);
        }
        catch (MtpException)
        {
            return [];
        }
    }

    private static IReadOnlyList<MtpDeviceInfo> Enumerate()
    {
        var devices = new List<MtpDeviceInfo>();
        if (Create(CLSID_PortableDeviceManager, IID_IPortableDeviceManager, out nint manager) < 0)
        {
            return devices;
        }

        try
        {
            RefreshDeviceList(manager);
            if (GetDevices(manager, out string[] ids) < 0)
            {
                return devices;
            }

            foreach (string id in ids)
            {
                string description = ManagerText(manager, 6, id);
                string name = ManagerText(manager, 5, id);
                devices.Add(new MtpDeviceInfo(name.Length > 0 ? name : description, ManagerText(manager, 7, id), description, id));
            }
        }
        finally
        {
            Release(ref manager);
        }

        devices.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return devices;
    }

    public IMtpDevice Open(MtpDeviceInfo device) => InMta(() => (IMtpDevice)new WpdDevice(device));
}

/// <summary>One portable device, open.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WpdDevice : IMtpDevice
{
    private readonly object _gate = new();
    private nint _device;
    private nint _content;
    private nint _properties;

    /// <summary>Opens the device; on the calling thread, which must be in the multithreaded apartment.</summary>
    public WpdDevice(MtpDeviceInfo info)
    {
        Info = info;
        try
        {
            WpdDevices.Check(Create(CLSID_PortableDeviceFTM, IID_IPortableDevice, out _device), $"{info.Name} could not be opened");
            int hr = OpenWith(GENERIC_READ | GENERIC_WRITE);
            if (hr == E_ACCESSDENIED)
            {
                // A device that only lets itself be read is still worth listing and downloading from.
                hr = OpenWith(GENERIC_READ);
            }

            WpdDevices.Check(hr, $"{info.Name} could not be opened");
            WpdDevices.Check(Content(_device, out _content), $"{info.Name} could not be opened");
            WpdDevices.Check(WpdNative.Properties(_content, out _properties), $"{info.Name} could not be opened");
            Properties = ReadProperties();
        }
        catch
        {
            ReleaseAll();
            throw;
        }
    }

    private int OpenWith(uint access)
    {
        WpdDevices.Check(Create(CLSID_PortableDeviceValues, IID_IPortableDeviceValues, out nint client), $"{Info.Name} could not be opened");
        try
        {
            SetStringValue(client, WPD_CLIENT_NAME, "JGraph");
            SetUnsignedIntegerValue(client, WPD_CLIENT_MAJOR_VERSION, 1);
            SetUnsignedIntegerValue(client, WPD_CLIENT_MINOR_VERSION, 0);
            SetUnsignedIntegerValue(client, WPD_CLIENT_REVISION, 0);
            SetUnsignedIntegerValue(client, WPD_CLIENT_SECURITY_QUALITY_OF_SERVICE, SECURITY_IMPERSONATION);
            SetUnsignedIntegerValue(client, WPD_CLIENT_DESIRED_ACCESS, access);
            return Open(_device, Info.Id, client);
        }
        finally
        {
            Release(ref client);
        }
    }

    public MtpDeviceInfo Info { get; }

    public IReadOnlyDictionary<string, string> Properties { get; }

    private Dictionary<string, string> ReadProperties()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Model"] = "",
            ["SerialNumber"] = "",
            ["FirmwareVersion"] = "",
            ["Protocol"] = "",
            ["Type"] = "",
        };
        if (GetValues(_properties, MtpPaths.RootId, 0, out nint values) < 0)
        {
            return result;
        }

        try
        {
            result["Model"] = GetStringValue(values, WPD_DEVICE_MODEL);
            result["SerialNumber"] = GetStringValue(values, WPD_DEVICE_SERIAL_NUMBER);
            result["FirmwareVersion"] = GetStringValue(values, WPD_DEVICE_FIRMWARE_VERSION);
            result["Protocol"] = GetStringValue(values, WPD_DEVICE_PROTOCOL);
            result["Type"] = GetUnsignedIntegerValue(values, WPD_DEVICE_TYPE, out uint type) < 0 ? "" : TypeName(type);
        }
        finally
        {
            Release(ref values);
        }

        return result;
    }

    /// <summary>WPD_DEVICE_TYPES in a word.</summary>
    internal static string TypeName(uint type) => type switch
    {
        0 => "generic",
        1 => "camera",
        2 => "media player",
        3 => "phone",
        4 => "video",
        5 => "personal information manager",
        6 => "audio recorder",
        _ => "unknown",
    };

    /// <summary>Runs a device call on an apartment thread, one at a time, refusing once the device is closed.</summary>
    private T Call<T>(Func<T> work) => WpdDevices.InMta(() =>
    {
        lock (_gate)
        {
            if (_device == 0)
            {
                throw new MtpException($"{Info.Name} is closed.");
            }

            return work();
        }
    });

    private static nint Keys(params PROPERTYKEY[] keys)
    {
        WpdDevices.Check(Create(CLSID_PortableDeviceKeyCollection, IID_IPortableDeviceKeyCollection, out nint collection), "The device's properties could not be asked for");
        foreach (PROPERTYKEY key in keys)
        {
            AddKey(collection, key);
        }

        return collection;
    }

    public IReadOnlyList<MtpEntry> Children(string parentId) => Call(() =>
    {
        var ids = new List<string>();
        WpdDevices.Check(EnumObjects(_content, parentId, out nint enumerator), $"{Info.Name} could not be listed");
        try
        {
            while (true)
            {
                int fetched = Next(enumerator, 64, ids);
                WpdDevices.Check(fetched, $"{Info.Name} could not be listed");
                if (fetched == 0)
                {
                    break;
                }
            }
        }
        finally
        {
            Release(ref enumerator);
        }

        var entries = new List<MtpEntry>(ids.Count);
        nint keys = Keys(WPD_OBJECT_NAME, WPD_OBJECT_ORIGINAL_FILE_NAME, WPD_OBJECT_CONTENT_TYPE, WPD_OBJECT_SIZE, WPD_OBJECT_DATE_MODIFIED, WPD_FUNCTIONAL_OBJECT_CATEGORY);
        try
        {
            foreach (string id in ids)
            {
                // A driver that refuses a key it does not know is asked again for everything it has.
                if (GetValues(_properties, id, keys, out nint values) < 0 && GetValues(_properties, id, 0, out values) < 0)
                {
                    continue;
                }

                try
                {
                    GetGuidValue(values, WPD_OBJECT_CONTENT_TYPE, out Guid type);
                    bool functional = type == WPD_CONTENT_TYPE_FUNCTIONAL_OBJECT;
                    if (functional && GetGuidValue(values, WPD_FUNCTIONAL_OBJECT_CATEGORY, out Guid category) >= 0 && category != WPD_FUNCTIONAL_CATEGORY_STORAGE)
                    {
                        // A camera's capture function, a phone's messaging service: not a place files live.
                        continue;
                    }

                    // A drive seen as a portable device names its storage "E:\", and a path parts at that slash.
                    string name = GetStringValue(values, WPD_OBJECT_NAME);
                    if (functional)
                    {
                        name = name.TrimEnd('\\', '/');
                    }

                    string file = functional ? "" : GetStringValue(values, WPD_OBJECT_ORIGINAL_FILE_NAME);
                    bool folder = functional || type == WPD_CONTENT_TYPE_FOLDER;
                    long size = !folder && GetUnsignedLargeIntegerValue(values, WPD_OBJECT_SIZE, out ulong bytes) >= 0 ? (long)bytes : 0;
                    entries.Add(new MtpEntry(id, file.Length > 0 ? file : name, folder, functional, size, folder ? null : GetDateValue(values, WPD_OBJECT_DATE_MODIFIED)));
                }
                finally
                {
                    Release(ref values);
                }
            }
        }
        finally
        {
            Release(ref keys);
        }

        return (IReadOnlyList<MtpEntry>)entries;
    });

    public (long Capacity, long Free) Space(string storageId) => Call(() =>
    {
        nint keys = Keys(WPD_STORAGE_CAPACITY, WPD_STORAGE_FREE_SPACE_IN_BYTES);
        try
        {
            if (GetValues(_properties, storageId, keys, out nint values) < 0)
            {
                return (0L, 0L);
            }

            try
            {
                GetUnsignedLargeIntegerValue(values, WPD_STORAGE_CAPACITY, out ulong capacity);
                GetUnsignedLargeIntegerValue(values, WPD_STORAGE_FREE_SPACE_IN_BYTES, out ulong free);
                return ((long)capacity, (long)free);
            }
            finally
            {
                Release(ref values);
            }
        }
        finally
        {
            Release(ref keys);
        }
    });

    public void Download(string id, Stream to) => Call(() =>
    {
        WpdDevices.Check(Transfer(_content, out nint resources), "The file could not be read from the device");
        nint stream = 0;
        try
        {
            WpdDevices.Check(GetStream(resources, id, WPD_RESOURCE_DEFAULT, STGM_READ, out uint optimal, out stream), "The file could not be read from the device");
            byte[] buffer = new byte[Math.Clamp(optimal, 64 * 1024, 4 * 1024 * 1024)];
            fixed (byte* p = buffer)
            {
                while (true)
                {
                    WpdDevices.Check(Read(stream, p, (uint)buffer.Length, out uint read), "The file could not be read from the device");
                    if (read == 0)
                    {
                        break;
                    }

                    to.Write(buffer, 0, (int)read);
                }
            }
        }
        finally
        {
            Release(ref stream);
            Release(ref resources);
        }

        return 0;
    });

    private nint NewObject(string parentId, string name, Guid contentType)
    {
        WpdDevices.Check(Create(CLSID_PortableDeviceValues, IID_IPortableDeviceValues, out nint values), "The object could not be described to the device");
        SetStringValue(values, WPD_OBJECT_PARENT_ID, parentId);
        SetStringValue(values, WPD_OBJECT_NAME, name);
        SetStringValue(values, WPD_OBJECT_ORIGINAL_FILE_NAME, name);
        SetGuidValue(values, WPD_OBJECT_CONTENT_TYPE, contentType);
        return values;
    }

    public string Upload(string parentId, string name, Stream from, long size) => Call(() =>
    {
        nint values = NewObject(parentId, Path.GetFileNameWithoutExtension(name) is { Length: > 0 } stem ? stem : name, WPD_CONTENT_TYPE_GENERIC_FILE);
        nint stream = 0;
        nint data = 0;
        try
        {
            SetStringValue(values, WPD_OBJECT_ORIGINAL_FILE_NAME, name);
            SetGuidValue(values, WPD_OBJECT_FORMAT, WPD_OBJECT_FORMAT_UNSPECIFIED);
            SetUnsignedLargeIntegerValue(values, WPD_OBJECT_SIZE, (ulong)size);
            WpdDevices.Check(CreateObjectWithPropertiesAndData(_content, values, out stream, out uint optimal), $"{name} could not be created on the device");
            byte[] buffer = new byte[Math.Clamp(optimal, 64 * 1024, 4 * 1024 * 1024)];
            long left = size;
            fixed (byte* p = buffer)
            {
                while (left > 0)
                {
                    int n = from.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (n <= 0)
                    {
                        throw new MtpException($"{name} ended {left} bytes early.");
                    }

                    WpdDevices.Check(Write(stream, p, (uint)n, out uint written), $"{name} could not be written to the device");
                    if (written != n)
                    {
                        throw new MtpException($"{name} could not be written to the device: it took {written} of {n} bytes.");
                    }

                    left -= n;
                }
            }

            WpdDevices.Check(Commit(stream), $"{name} could not be written to the device");
            string id = "";
            if (QueryInterface(stream, IID_IPortableDeviceDataStream, out data) >= 0)
            {
                GetObjectID(data, out id);
            }

            return id;
        }
        finally
        {
            Release(ref data);
            Release(ref stream);
            Release(ref values);
        }
    });

    public string CreateFolder(string parentId, string name) => Call(() =>
    {
        nint values = NewObject(parentId, name, WPD_CONTENT_TYPE_FOLDER);
        try
        {
            WpdDevices.Check(CreateObjectWithPropertiesOnly(_content, values, out string id), $"The folder {name} could not be created on the device");
            return id;
        }
        finally
        {
            Release(ref values);
        }
    });

    public void Delete(string id, bool recursive) => Call(() =>
    {
        WpdDevices.Check(Create(CLSID_PortableDevicePropVariantCollection, IID_IPortableDevicePropVariantCollection, out nint ids), "The object could not be deleted from the device");
        nint results = 0;
        try
        {
            WpdDevices.Check(AddString(ids, id), "The object could not be deleted from the device");
            int hr = WpdNative.Delete(_content, recursive ? PORTABLE_DEVICE_DELETE_WITH_RECURSION : PORTABLE_DEVICE_DELETE_NO_RECURSION, ids, out results);
            WpdDevices.Check(hr, "The object could not be deleted from the device");

            // S_FALSE says some object was not deleted; why is in the results, one error an object.
            if (hr != 0 && results != 0 && GetCount(results, out uint count) >= 0 && count > 0 && GetAt(results, 0, out _, out int error) >= 0)
            {
                WpdDevices.Check(error < 0 ? error : unchecked((int)0x80004005), "The object could not be deleted from the device");
            }
        }
        finally
        {
            Release(ref results);
            Release(ref ids);
        }

        return 0;
    });

    public void Capture() => Call(() =>
    {
        nint capabilities = 0;
        nint functions = 0;
        nint parameters = 0;
        nint results = 0;
        try
        {
            string? target = null;
            if (Capabilities(_device, out capabilities) >= 0 && GetFunctionalObjects(capabilities, WPD_FUNCTIONAL_CATEGORY_STILL_IMAGE_CAPTURE, out functions) >= 0
                && GetCount(functions, out uint count) >= 0 && count > 0)
            {
                GetAt(functions, 0, out target, out _);
            }

            if (string.IsNullOrEmpty(target))
            {
                throw new MtpException($"{Info.Name} has no still-image capture function.");
            }

            WpdDevices.Check(Create(CLSID_PortableDeviceValues, IID_IPortableDeviceValues, out parameters), "The capture could not be asked for");
            SetGuidValue(parameters, WPD_PROPERTY_COMMON_COMMAND_CATEGORY, WPD_COMMAND_STILL_IMAGE_CAPTURE_INITIATE.Fmtid);
            SetUnsignedIntegerValue(parameters, WPD_PROPERTY_COMMON_COMMAND_ID, WPD_COMMAND_STILL_IMAGE_CAPTURE_INITIATE.Pid);
            SetStringValue(parameters, WPD_PROPERTY_COMMON_COMMAND_TARGET, target);
            WpdDevices.Check(SendCommand(_device, parameters, out results), $"{Info.Name} did not take the picture");
            if (results != 0 && GetErrorValue(results, WPD_PROPERTY_COMMON_HRESULT, out int answered) >= 0)
            {
                WpdDevices.Check(answered, $"{Info.Name} did not take the picture");
            }
        }
        finally
        {
            Release(ref results);
            Release(ref parameters);
            Release(ref functions);
            Release(ref capabilities);
        }

        return 0;
    });

    private void ReleaseAll()
    {
        Release(ref _properties);
        Release(ref _content);
        if (_device != 0)
        {
            Close(_device);
            Release(ref _device);
        }
    }

    public void Dispose()
    {
        try
        {
            WpdDevices.InMta(() =>
            {
                lock (_gate)
                {
                    ReleaseAll();
                }

                return 0;
            });
        }
        catch (MtpException)
        {
            // Closing a device that has already gone has nothing left to do.
        }
    }
}
