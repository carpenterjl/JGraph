using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace JGraph.Devices.Usb;

/// <summary>
/// SetupAPI, the configuration manager and the USB hub IOCTLs (usbioctl.h), declared for the USB
/// enumeration: the device list, each device's properties and parent, and the descriptors a hub
/// reads for its ports — the way USBView reads them, with no admin rights and without opening the
/// device.
/// </summary>
internal static unsafe partial class UsbNative
{
    public static readonly Guid GUID_DEVINTERFACE_USB_DEVICE = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    public static readonly Guid GUID_DEVINTERFACE_USB_HUB = new("F18A0E88-C30C-11D0-8815-00A0C906BED8");
    public static readonly Guid GUID_DEVINTERFACE_USB_HOST_CONTROLLER = new("3ABF6F2D-71C4-462A-8A92-1E6861E6AF27");
    public static readonly Guid GUID_DEVINTERFACE_COMPORT = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");
    public static readonly Guid GUID_DEVINTERFACE_HID = new("4D1E55B2-F16F-11CF-88CB-001111000030");
    public static readonly Guid GUID_DEVINTERFACE_DISK = new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

    public const uint DIGCF_PRESENT = 0x02;
    public const uint DIGCF_ALLCLASSES = 0x04;
    public const uint DIGCF_DEVICEINTERFACE = 0x10;

    public const uint SPDRP_DEVICEDESC = 0x00;
    public const uint SPDRP_HARDWAREID = 0x01;
    public const uint SPDRP_COMPATIBLEIDS = 0x02;
    public const uint SPDRP_SERVICE = 0x04;
    public const uint SPDRP_CLASS = 0x07;
    public const uint SPDRP_DRIVER = 0x09;
    public const uint SPDRP_MFG = 0x0B;
    public const uint SPDRP_FRIENDLYNAME = 0x0C;
    public const uint SPDRP_LOCATION_INFORMATION = 0x0D;
    public const uint SPDRP_ADDRESS = 0x1C;
    public const uint SPDRP_LOCATION_PATHS = 0x23;

    public const uint CR_SUCCESS = 0;

    public const uint FILE_SHARE_READ = 1;
    public const uint FILE_SHARE_WRITE = 2;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint OPEN_EXISTING = 3;

    public const uint IOCTL_USB_GET_NODE_INFORMATION = 0x220408;
    public const uint IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = 0x220410;
    public const uint IOCTL_USB_GET_NODE_CONNECTION_NAME = 0x220414;
    public const uint IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME = 0x220420;
    public const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x220448;
    public const uint IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES = 0x220458;
    public const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2 = 0x22045C;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;

        public DEVPROPKEY(string guid, uint id)
        {
            fmtid = new Guid(guid);
            pid = id;
        }
    }

    /// <summary>The device's bus-reported description and its container (DEVPKEY_Device_*).</summary>
    public static readonly DEVPROPKEY DEVPKEY_Device_BusReportedDeviceDesc = new("540B947E-8B40-45BC-A8A2-6A0B894CBDA2", 4);
    public static readonly DEVPROPKEY DEVPKEY_Device_ContainerId = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2);
    public static readonly DEVPROPKEY DEVPKEY_Device_Parent = new("4340A6C5-93FA-4706-972C-7B648008A5A7", 8);
    public static readonly DEVPROPKEY DEVPKEY_Device_Children = new("4340A6C5-93FA-4706-972C-7B648008A5A7", 9);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    public static partial nint SetupDiGetClassDevs(Guid* classGuid, char* enumerator, nint parent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDeviceInfoList(nint set);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDeviceInfo(nint set, uint index, SP_DEVINFO_DATA* data);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDeviceInterfaces(nint set, SP_DEVINFO_DATA* devInfo, Guid* interfaceGuid, uint index, SP_DEVICE_INTERFACE_DATA* data);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInterfaceDetail(nint set, SP_DEVICE_INTERFACE_DATA* data, byte* detail, uint size, uint* required, SP_DEVINFO_DATA* devInfo);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceRegistryProperty(nint set, SP_DEVINFO_DATA* data, uint property, uint* type, byte* buffer, uint size, uint* required);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceProperty(nint set, SP_DEVINFO_DATA* data, DEVPROPKEY* key, uint* type, byte* buffer, uint size, uint* required, uint flags);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInstanceId(nint set, SP_DEVINFO_DATA* data, char* buffer, uint size, uint* required);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Parent(uint* parent, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Child(uint* child, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Sibling(uint* sibling, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW")]
    public static partial uint CM_Get_Device_ID(uint devInst, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Locate_DevNode(uint* devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    public static partial uint CM_Get_DevNode_Property(uint devInst, DEVPROPKEY* key, uint* type, byte* buffer, uint* size, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Registry_PropertyW")]
    public static partial uint CM_Get_DevNode_Registry_Property(uint devInst, uint property, uint* type, byte* buffer, uint* length, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_DevNode_Status(uint* status, uint* problem, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Request_Device_EjectW")]
    public static partial uint CM_Request_Device_Eject(uint devInst, int* vetoType, char* vetoName, uint nameLength, uint flags);

    // CM_Register_Notification (Windows 8+): device-interface arrival and removal.
    public const uint CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE = 0;
    public const uint CM_NOTIFY_FILTER_FLAG_ALL_INTERFACE_CLASSES = 1;
    public const uint CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL = 0;
    public const uint CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct CM_NOTIFY_FILTER
    {
        public uint cbSize;
        public uint Flags;
        public uint FilterType;
        public uint Reserved;
        public Guid ClassGuid;
        public fixed char Padding[192]; // the union is MAX_DEVICE_ID_LEN (200) wide chars
    }

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Register_Notification(CM_NOTIFY_FILTER* filter, nint context, delegate* unmanaged<nint, nint, uint, byte*, uint, uint> callback, nint* notification);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Unregister_Notification(nint notification);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(SafeFileHandle device, uint code, byte* input, uint inputSize, byte* output, uint outputSize, uint* returned, nint overlapped);

    /// <summary>Calls DeviceIoControl with <paramref name="io"/> as both buffers; answers the bytes returned, or -1.</summary>
    public static int Ioctl(SafeFileHandle device, uint code, byte[] io)
    {
        fixed (byte* p = io)
        {
            uint returned = 0;
            return DeviceIoControl(device, code, p, (uint)io.Length, p, (uint)io.Length, &returned, 0) ? (int)returned : -1;
        }
    }
}
