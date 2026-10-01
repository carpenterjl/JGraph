using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Devices.Mtp;

/// <summary>
/// Windows Portable Devices as the MTP backend calls it (device classes plan, stage D12): the classes
/// created by CoCreateInstance, the property keys, and the COM methods reached by their vtable slots
/// on raw pointers, so a device is released on the statement that deletes its object. Every slot
/// number, GUID and key here is read from the Windows SDK's PortableDeviceApi.h, PortableDeviceTypes.h,
/// PortableDevice.h and objidl.h (10.0.26100.0).
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WpdNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY(string fmtid, uint pid)
    {
        public Guid Fmtid = new(fmtid);
        public uint Pid = pid;
    }

    /// <summary>A PROPVARIANT: its type, then eight bytes on its own boundary that hold the value or point to it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PROPVARIANT
    {
        public ushort Vt;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Value;
        public nint Value2;
    }

    public const ushort VT_DATE = 7;
    public const ushort VT_ERROR = 10;
    public const ushort VT_LPWSTR = 31;
    public const uint STGM_READ = 0;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint SECURITY_IMPERSONATION = 0x00020000;
    public const uint PORTABLE_DEVICE_DELETE_NO_RECURSION = 0;
    public const uint PORTABLE_DEVICE_DELETE_WITH_RECURSION = 1;
    public const int E_ACCESSDENIED = unchecked((int)0x80070005);

    public static readonly Guid CLSID_PortableDeviceManager = new("0af10cec-2ecd-4b92-9581-34f6ae0637f3");
    public static readonly Guid CLSID_PortableDeviceFTM = new("f7c0039a-4762-488a-b4b3-760ef9a1ba9b");
    public static readonly Guid CLSID_PortableDeviceValues = new("0c15d503-d017-47ce-9016-7b3f978721cc");
    public static readonly Guid CLSID_PortableDeviceKeyCollection = new("de2d022d-2480-43be-97f0-d1fa2cf98f4f");
    public static readonly Guid CLSID_PortableDevicePropVariantCollection = new("08a99e2f-6d6d-4b80-af5a-baf2bcbe4cb9");
    public static readonly Guid IID_IPortableDeviceManager = new("a1567595-4c2f-4574-a6fa-ecef917b9a40");
    public static readonly Guid IID_IPortableDevice = new("625e2df8-6392-4cf0-9ad1-3cfa5f17775c");
    public static readonly Guid IID_IPortableDeviceValues = new("6848f6f2-3155-4f86-b6f5-263eeeab3143");
    public static readonly Guid IID_IPortableDeviceKeyCollection = new("dada2357-e0ad-492e-98db-dd61c53ba353");
    public static readonly Guid IID_IPortableDevicePropVariantCollection = new("89b2e422-4f1b-4316-bcef-a44afea83eb3");
    public static readonly Guid IID_IPortableDeviceDataStream = new("88e04db3-1012-4d64-9996-f703a950d3f4");

    public static readonly Guid WPD_CONTENT_TYPE_FUNCTIONAL_OBJECT = new("99ED0160-17FF-4C44-9D98-1D7A6F941921");
    public static readonly Guid WPD_CONTENT_TYPE_FOLDER = new("27E2E392-A111-48E0-AB0C-E17705A05F85");
    public static readonly Guid WPD_CONTENT_TYPE_GENERIC_FILE = new("0085E0A6-8D34-45D7-BC5C-447E59C73D48");
    public static readonly Guid WPD_OBJECT_FORMAT_UNSPECIFIED = new("30000000-AE6C-4804-98BA-C57B46965FE7");
    public static readonly Guid WPD_FUNCTIONAL_CATEGORY_STORAGE = new("23F05BBC-15DE-4C2A-A55B-A9AF5CE412EF");
    public static readonly Guid WPD_FUNCTIONAL_CATEGORY_STILL_IMAGE_CAPTURE = new("613CA327-AB93-4900-B4FA-895BB5874B79");

    private const string Object = "EF6B490D-5CD8-437A-AFFC-DA8B60EE4A3C";
    private const string Client = "204D9F0C-2292-4080-9F42-40664E70F859";
    private const string Device = "26D4979A-E643-4626-9E2B-736DC0C92FDC";
    private const string Storage = "01A3057A-74D6-4E80-BEA7-DC4C212CE50A";
    private const string Common = "F0422A9C-5DC8-4440-B5BD-5DF28835658A";

    public static readonly PROPERTYKEY WPD_OBJECT_PARENT_ID = new(Object, 3);
    public static readonly PROPERTYKEY WPD_OBJECT_NAME = new(Object, 4);
    public static readonly PROPERTYKEY WPD_OBJECT_FORMAT = new(Object, 6);
    public static readonly PROPERTYKEY WPD_OBJECT_CONTENT_TYPE = new(Object, 7);
    public static readonly PROPERTYKEY WPD_OBJECT_SIZE = new(Object, 11);
    public static readonly PROPERTYKEY WPD_OBJECT_ORIGINAL_FILE_NAME = new(Object, 12);
    public static readonly PROPERTYKEY WPD_OBJECT_DATE_MODIFIED = new(Object, 19);
    public static readonly PROPERTYKEY WPD_FUNCTIONAL_OBJECT_CATEGORY = new("8F052D93-ABCA-4FC5-A5AC-B01DF4DBE598", 2);
    public static readonly PROPERTYKEY WPD_STORAGE_CAPACITY = new(Storage, 4);
    public static readonly PROPERTYKEY WPD_STORAGE_FREE_SPACE_IN_BYTES = new(Storage, 5);
    public static readonly PROPERTYKEY WPD_CLIENT_NAME = new(Client, 2);
    public static readonly PROPERTYKEY WPD_CLIENT_MAJOR_VERSION = new(Client, 3);
    public static readonly PROPERTYKEY WPD_CLIENT_MINOR_VERSION = new(Client, 4);
    public static readonly PROPERTYKEY WPD_CLIENT_REVISION = new(Client, 5);
    public static readonly PROPERTYKEY WPD_CLIENT_SECURITY_QUALITY_OF_SERVICE = new(Client, 8);
    public static readonly PROPERTYKEY WPD_CLIENT_DESIRED_ACCESS = new(Client, 9);
    public static readonly PROPERTYKEY WPD_DEVICE_FIRMWARE_VERSION = new(Device, 3);
    public static readonly PROPERTYKEY WPD_DEVICE_PROTOCOL = new(Device, 6);
    public static readonly PROPERTYKEY WPD_DEVICE_MODEL = new(Device, 8);
    public static readonly PROPERTYKEY WPD_DEVICE_SERIAL_NUMBER = new(Device, 9);
    public static readonly PROPERTYKEY WPD_DEVICE_TYPE = new(Device, 15);
    public static readonly PROPERTYKEY WPD_PROPERTY_COMMON_COMMAND_CATEGORY = new(Common, 1001);
    public static readonly PROPERTYKEY WPD_PROPERTY_COMMON_COMMAND_ID = new(Common, 1002);
    public static readonly PROPERTYKEY WPD_PROPERTY_COMMON_HRESULT = new(Common, 1003);
    public static readonly PROPERTYKEY WPD_PROPERTY_COMMON_COMMAND_TARGET = new(Common, 1006);
    public static readonly PROPERTYKEY WPD_RESOURCE_DEFAULT = new("E81E79BE-34F0-41BF-B53F-F1A06AE87842", 0);
    public static readonly PROPERTYKEY WPD_COMMAND_STILL_IMAGE_CAPTURE_INITIATE = new("4FCD6982-22A2-4B05-A48B-62D38BF27B32", 2);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(nint memory);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(PROPVARIANT* value);

    /// <summary>An in-process COM object, or 0 with the HRESULT why not.</summary>
    public static int Create(Guid clsid, Guid iid, out nint result)
    {
        fixed (nint* to = &result)
        {
            return CoCreateInstance(&clsid, 0, 1, &iid, to);
        }
    }

    private static nint Slot(nint com, int slot) => (*(nint**)com)[slot];

    // --- IUnknown -------------------------------------------------------------------------------------

    public static int QueryInterface(nint com, Guid iid, out nint result)
    {
        fixed (nint* to = &result)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(com, 0))(com, &iid, to);
        }
    }

    public static void Release(ref nint com)
    {
        if (com != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(com, 2))(com);
            com = 0;
        }
    }

    /// <summary>A string the callee allocated, freed here.</summary>
    private static string Take(nint text)
    {
        if (text == 0)
        {
            return "";
        }

        string value = Marshal.PtrToStringUni(text) ?? "";
        CoTaskMemFree(text);
        return value;
    }

    // --- IPortableDeviceManager -------------------------------------------------------------------------

    /// <summary>GetDevices (slot 3): the PnP device IDs of the portable devices.</summary>
    public static int GetDevices(nint manager, out string[] ids)
    {
        ids = [];
        uint count = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, uint*, int>)Slot(manager, 3))(manager, null, &count);
        if (hr < 0 || count == 0)
        {
            return hr;
        }

        nint[] raw = new nint[count];
        fixed (nint* p = raw)
        {
            hr = ((delegate* unmanaged[Stdcall]<nint, nint*, uint*, int>)Slot(manager, 3))(manager, p, &count);
        }

        if (hr < 0)
        {
            return hr;
        }

        ids = new string[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = Take(raw[i]);
        }

        return hr;
    }

    public static int RefreshDeviceList(nint manager) => ((delegate* unmanaged[Stdcall]<nint, int>)Slot(manager, 4))(manager);

    /// <summary>GetDeviceFriendlyName (5), GetDeviceDescription (6) or GetDeviceManufacturer (7); empty when the device has none.</summary>
    public static string ManagerText(nint manager, int slot, string id)
    {
        fixed (char* device = id)
        {
            uint length = 0;
            var call = (delegate* unmanaged[Stdcall]<nint, char*, char*, uint*, int>)Slot(manager, slot);
            if (call(manager, device, null, &length) < 0 || length == 0)
            {
                return "";
            }

            char[] buffer = new char[length];
            fixed (char* p = buffer)
            {
                return call(manager, device, p, &length) < 0 ? "" : new string(p);
            }
        }
    }

    // --- IPortableDevice ------------------------------------------------------------------------------------

    public static int Open(nint device, string id, nint clientInfo)
    {
        fixed (char* p = id)
        {
            return ((delegate* unmanaged[Stdcall]<nint, char*, nint, int>)Slot(device, 3))(device, p, clientInfo);
        }
    }

    public static int SendCommand(nint device, nint parameters, out nint results)
    {
        fixed (nint* to = &results)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint, nint, nint*, int>)Slot(device, 4))(device, 0, parameters, to);
        }
    }

    public static int Content(nint device, out nint content)
    {
        fixed (nint* to = &content)
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(device, 5))(device, to);
        }
    }

    public static int Capabilities(nint device, out nint capabilities)
    {
        fixed (nint* to = &capabilities)
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(device, 6))(device, to);
        }
    }

    public static int Close(nint device) => ((delegate* unmanaged[Stdcall]<nint, int>)Slot(device, 8))(device);

    // --- IPortableDeviceContent ---------------------------------------------------------------------------

    public static int EnumObjects(nint content, string parentId, out nint enumerator)
    {
        fixed (char* p = parentId)
        fixed (nint* to = &enumerator)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint, char*, nint, nint*, int>)Slot(content, 3))(content, 0, p, 0, to);
        }
    }

    public static int Properties(nint content, out nint properties)
    {
        fixed (nint* to = &properties)
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(content, 4))(content, to);
        }
    }

    public static int Transfer(nint content, out nint resources)
    {
        fixed (nint* to = &resources)
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(content, 5))(content, to);
        }
    }

    public static int CreateObjectWithPropertiesOnly(nint content, nint values, out string objectId)
    {
        nint id = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(content, 6))(content, values, &id);
        objectId = Take(id);
        return hr;
    }

    public static int CreateObjectWithPropertiesAndData(nint content, nint values, out nint stream, out uint optimalBuffer)
    {
        fixed (nint* to = &stream)
        fixed (uint* size = &optimalBuffer)
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint, nint*, uint*, nint*, int>)Slot(content, 7))(content, values, to, size, null);
        }
    }

    public static int Delete(nint content, uint options, nint objectIds, out nint results)
    {
        fixed (nint* to = &results)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint, nint, nint*, int>)Slot(content, 8))(content, options, objectIds, to);
        }
    }

    // --- IEnumPortableDeviceObjectIDs -----------------------------------------------------------------------

    /// <summary>Next (slot 3): up to <paramref name="want"/> object IDs; none at the end.</summary>
    public static int Next(nint enumerator, int want, List<string> into)
    {
        nint* raw = stackalloc nint[want];
        uint fetched = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, uint*, int>)Slot(enumerator, 3))(enumerator, (uint)want, raw, &fetched);
        for (int i = 0; i < fetched; i++)
        {
            into.Add(Take(raw[i]));
        }

        return hr < 0 ? hr : (int)fetched;
    }

    // --- IPortableDeviceProperties ----------------------------------------------------------------------------

    /// <summary>GetValues (slot 5); <paramref name="keys"/> 0 asks for every property.</summary>
    public static int GetValues(nint properties, string objectId, nint keys, out nint values)
    {
        fixed (char* p = objectId)
        fixed (nint* to = &values)
        {
            return ((delegate* unmanaged[Stdcall]<nint, char*, nint, nint*, int>)Slot(properties, 5))(properties, p, keys, to);
        }
    }

    // --- IPortableDeviceResources -----------------------------------------------------------------------------

    public static int GetStream(nint resources, string objectId, PROPERTYKEY key, uint mode, out uint optimalBuffer, out nint stream)
    {
        fixed (char* p = objectId)
        fixed (uint* size = &optimalBuffer)
        fixed (nint* to = &stream)
        {
            return ((delegate* unmanaged[Stdcall]<nint, char*, PROPERTYKEY*, uint, uint*, nint*, int>)Slot(resources, 5))(resources, p, &key, mode, size, to);
        }
    }

    // --- IPortableDeviceCapabilities ----------------------------------------------------------------------------

    public static int GetFunctionalObjects(nint capabilities, Guid category, out nint objectIds)
    {
        fixed (nint* to = &objectIds)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(capabilities, 6))(capabilities, &category, to);
        }
    }

    // --- IPortableDeviceValues --------------------------------------------------------------------------------

    public static int SetStringValue(nint values, PROPERTYKEY key, string value)
    {
        fixed (char* p = value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, char*, int>)Slot(values, 7))(values, &key, p);
        }
    }

    /// <summary>GetStringValue (slot 8); empty when the store has none.</summary>
    public static string GetStringValue(nint values, PROPERTYKEY key)
    {
        nint text = 0;
        return ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, nint*, int>)Slot(values, 8))(values, &key, &text) < 0 ? "" : Take(text);
    }

    public static int SetUnsignedIntegerValue(nint values, PROPERTYKEY key, uint value) =>
        ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, uint, int>)Slot(values, 9))(values, &key, value);

    public static int GetUnsignedIntegerValue(nint values, PROPERTYKEY key, out uint value)
    {
        fixed (uint* to = &value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, uint*, int>)Slot(values, 10))(values, &key, to);
        }
    }

    public static int SetUnsignedLargeIntegerValue(nint values, PROPERTYKEY key, ulong value) =>
        ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, ulong, int>)Slot(values, 13))(values, &key, value);

    public static int GetUnsignedLargeIntegerValue(nint values, PROPERTYKEY key, out ulong value)
    {
        fixed (ulong* to = &value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, ulong*, int>)Slot(values, 14))(values, &key, to);
        }
    }

    public static int GetErrorValue(nint values, PROPERTYKEY key, out int value)
    {
        fixed (int* to = &value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, int*, int>)Slot(values, 20))(values, &key, to);
        }
    }

    public static int SetGuidValue(nint values, PROPERTYKEY key, Guid value) =>
        ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, Guid*, int>)Slot(values, 27))(values, &key, &value);

    public static int GetGuidValue(nint values, PROPERTYKEY key, out Guid value)
    {
        fixed (Guid* to = &value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, Guid*, int>)Slot(values, 28))(values, &key, to);
        }
    }

    /// <summary>GetValue (slot 6) of a date property; null when the store has none or it is not a date.</summary>
    public static DateTime? GetDateValue(nint values, PROPERTYKEY key)
    {
        PROPVARIANT value = default;
        if (((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, PROPVARIANT*, int>)Slot(values, 6))(values, &key, &value) < 0)
        {
            return null;
        }

        DateTime? date = null;
        if (value.Vt == VT_DATE)
        {
            try
            {
                date = DateTime.FromOADate(*(double*)&value.Value);
            }
            catch (ArgumentException)
            {
            }
        }

        PropVariantClear(&value);
        return date;
    }

    // --- IPortableDeviceKeyCollection, IPortableDevicePropVariantCollection --------------------------------------

    public static int AddKey(nint keys, PROPERTYKEY key) => ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, int>)Slot(keys, 5))(keys, &key);

    public static int GetCount(nint collection, out uint count)
    {
        fixed (uint* to = &count)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(collection, 3))(collection, to);
        }
    }

    /// <summary>Add (slot 5) of a string, which the collection copies.</summary>
    public static int AddString(nint collection, string text)
    {
        var value = new PROPVARIANT { Vt = VT_LPWSTR, Value = Marshal.StringToCoTaskMemUni(text) };
        int hr = ((delegate* unmanaged[Stdcall]<nint, PROPVARIANT*, int>)Slot(collection, 5))(collection, &value);
        PropVariantClear(&value);
        return hr;
    }

    /// <summary>GetAt (slot 4): the element as a string, or as an error code.</summary>
    public static int GetAt(nint collection, uint index, out string? text, out int error)
    {
        text = null;
        error = 0;
        PROPVARIANT value = default;
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, PROPVARIANT*, int>)Slot(collection, 4))(collection, index, &value);
        if (hr < 0)
        {
            return hr;
        }

        if (value.Vt == VT_LPWSTR)
        {
            text = Marshal.PtrToStringUni(value.Value);
        }
        else if (value.Vt == VT_ERROR)
        {
            error = *(int*)&value.Value;
        }

        PropVariantClear(&value);
        return hr;
    }

    // --- IStream, IPortableDeviceDataStream ---------------------------------------------------------------------

    public static int Read(nint stream, byte* buffer, uint size, out uint read)
    {
        fixed (uint* to = &read)
        {
            return ((delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)Slot(stream, 3))(stream, buffer, size, to);
        }
    }

    public static int Write(nint stream, byte* buffer, uint size, out uint written)
    {
        fixed (uint* to = &written)
        {
            return ((delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)Slot(stream, 4))(stream, buffer, size, to);
        }
    }

    public static int Commit(nint stream) => ((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(stream, 8))(stream, 0);

    /// <summary>IPortableDeviceDataStream::GetObjectID (slot 14): the ID of the object a committed stream made.</summary>
    public static int GetObjectID(nint dataStream, out string objectId)
    {
        nint id = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(dataStream, 14))(dataStream, &id);
        objectId = Take(id);
        return hr;
    }
}
