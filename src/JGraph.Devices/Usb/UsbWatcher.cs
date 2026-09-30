using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static JGraph.Devices.Usb.UsbNative;

namespace JGraph.Devices.Usb;

/// <summary>
/// Watches for USB devices arriving and leaving (device classes plan, stage 6): a
/// <c>CM_Register_Notification</c> on the USB device interface class. The configuration manager calls
/// back on a thread of its own; <see cref="Changed"/> is raised there, with the device's instance ID
/// (read from its interface path) and whether it arrived.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class UsbWatcher : IDisposable
{
    private readonly GCHandle _self;
    private nint _notification;

    public UsbWatcher(Guid? interfaceClass = null)
    {
        _self = GCHandle.Alloc(this);
        var filter = new CM_NOTIFY_FILTER
        {
            cbSize = (uint)sizeof(CM_NOTIFY_FILTER),
            FilterType = CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE,
            ClassGuid = interfaceClass ?? GUID_DEVINTERFACE_USB_DEVICE,
        };
        nint handle = 0;
        uint result = CM_Register_Notification(&filter, GCHandle.ToIntPtr(_self), &Callback, &handle);
        if (result != CR_SUCCESS)
        {
            _self.Free();
            throw new DeviceOpenException($"The device notification could not be registered (CONFIGRET {result}).", (int)result);
        }

        _notification = handle;
    }

    /// <summary>Raised on the configuration manager's thread: the instance ID, the interface path, and true on arrival.</summary>
    public event Action<string, string, bool>? Changed;

    [UnmanagedCallersOnly]
    private static uint Callback(nint notification, nint context, uint action, byte* data, uint size)
    {
        try
        {
            if (GCHandle.FromIntPtr(context).Target is not UsbWatcher watcher)
            {
                return 0;
            }

            if (action is not (CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL or CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL))
            {
                return 0;
            }

            // CM_NOTIFY_EVENT_DATA: FilterType, Reserved, then ClassGuid and the symbolic link.
            string path = new((char*)(data + 8 + 16));
            watcher.Changed?.Invoke(InstanceIdOf(path), path, action == CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL);
        }
        catch (Exception)
        {
            // Nothing may escape into the configuration manager's thread.
        }

        return 0;
    }

    /// <summary>
    /// The instance ID an interface path names: <c>\\?\USB#VID_2E8A&amp;PID_000A#E6614C31#{guid}</c> is
    /// <c>USB\VID_2E8A&amp;PID_000A\E6614C31</c>.
    /// </summary>
    public static string InstanceIdOf(string path)
    {
        string trimmed = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
        int guid = trimmed.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0)
        {
            trimmed = trimmed[..guid];
        }

        return trimmed.Replace('#', '\\');
    }

    public void Dispose()
    {
        if (_notification != 0)
        {
            CM_Unregister_Notification(_notification);
            _notification = 0;
            _self.Free();
        }
    }
}
