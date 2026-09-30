using System.Runtime.Versioning;
using JGraph.Devices.Usb;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>w = jgraph.usb.watch(@fcn, Name=Value…)</c> (device classes plan, stage D5, ADR 0188): runs
/// <c>fcn(w, evt)</c> when a USB device arrives or leaves, at the device queue's drain points (a
/// <c>pause</c>, <c>drawnow</c>, the idle prompt). The filters are jgraph.usb.devices'; a removal is
/// matched against the device as it was last seen. <c>evt.Type</c> is <c>"DeviceArrived"</c> or
/// <c>"DeviceRemoved"</c>, <c>evt.Device</c> the device's row as a struct, <c>evt.AbsTime</c> when.
/// <c>delete(w)</c>, or its last holder going, stops it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UsbWatchObject : DeviceObject
{
    private static readonly DeviceClass Declaration = new(
        "jgraph.usb.Watcher",
        "Watcher",
        ["handle"],
        [
            new DeviceProperty("Callback", static (o, _) => ((UsbWatchObject)o)._callback),
            new DeviceProperty("Events", static (o, _) => JgsValue.Number(((UsbWatchObject)o)._events)),
        ],
        new Dictionary<string, DeviceMethodBody>(StringComparer.Ordinal),
        ["Watcher", "delete", "get", "isvalid"],
        ["Callback", "Events"]);

    private readonly UsbWatcher _watcher;
    private readonly JgsValue _callback;
    private readonly Func<UsbDeviceInfo, bool> _filter;
    private readonly DeviceEventQueue _queue;
    private readonly Dictionary<string, UsbDeviceInfo> _known = new(StringComparer.OrdinalIgnoreCase);
    private int _events;

    private UsbWatchObject(DeviceSession session, Interpreter interpreter, JgsValue callback, Func<UsbDeviceInfo, bool> filter)
        : base(session, interpreter)
    {
        _callback = callback;
        _filter = filter;
        _queue = DeviceEventQueue.ForCurrentThread();
        foreach (UsbDeviceInfo device in UsbEnumerator.Devices())
        {
            _known[device.InstanceId] = device;
        }

        _watcher = new UsbWatcher();
        _watcher.Changed += OnChanged;
    }

    public override DeviceClass Class => Declaration;

    public override string? Summary() => Deleted ? "stopped" : $"{_events} events";

    public static JgsValue Start(DeviceSession session, Interpreter interpreter, JgsValue callback, Func<UsbDeviceInfo, bool> filter)
    {
        var watch = new UsbWatchObject(session, interpreter, callback, filter);
        session.Remember(watch);
        JgsValue made = JgsValue.External(watch);
        JgsLifetime.Minted(made);
        return made;
    }

    /// <summary>On the configuration manager's thread: the device read now (arrival) or as it was (removal), then queued.</summary>
    private void OnChanged(string instance, string path, bool arrived)
    {
        UsbDeviceInfo? device;
        if (arrived)
        {
            device = UsbEnumerator.Devices().FirstOrDefault(d => d.InstanceId.Equals(instance, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                lock (_known)
                {
                    _known[instance] = device;
                }
            }
        }
        else
        {
            lock (_known)
            {
                _known.Remove(instance, out device);
            }
        }

        device ??= new UsbDeviceInfo { InstanceId = instance };
        if (!_filter(device))
        {
            return;
        }

        DateTime at = DateTime.Now;
        _queue.Post(() => Fire(device, arrived, at));
    }

    private void Fire(UsbDeviceInfo device, bool arrived, DateTime at)
    {
        if (Deleted)
        {
            return;
        }

        _events++;
        JgsValue evt = JgsValue.Struct(new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Type"] = JgsValue.StringScalar(arrived ? "DeviceArrived" : "DeviceRemoved"),
            ["Device"] = JgsBuiltins.UsbDeviceStruct(device),
            ["AbsTime"] = JgsBuiltins.DatetimeValue(at),
        });
        try
        {
            JgsCallbacks.Invoke(_callback.AsCallable, [JgsValue.External(this), evt], 0, 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JgsException failure)
        {
            JgsBuiltins.Warn(Session.Host, "JGraph:usb:CallbackError", "Error executing the jgraph.usb.watch callback:\n" + failure.Message);
        }
    }

    protected override void OnDelete() => _watcher.Dispose();
}
