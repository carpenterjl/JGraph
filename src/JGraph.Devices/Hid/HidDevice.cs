using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JGraph.Devices.Usb;
using Microsoft.Win32.SafeHandles;
using static JGraph.Devices.Hid.HidNative;

namespace JGraph.Devices.Hid;

/// <summary>One HID collection as <c>jgraph.usb.hidlist</c> lists it.</summary>
public sealed record HidInfo(
    string Path,
    int VendorId,
    int ProductId,
    int Release,
    int UsagePage,
    int Usage,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength,
    string Manufacturer,
    string Product,
    string SerialNumber,
    string UsbInstanceId,
    bool SystemOwned);

/// <summary>One button or value capability, from the preparsed data.</summary>
public sealed record HidCapability(
    string ReportType,
    bool IsButton,
    int ReportId,
    int UsagePage,
    int UsageMin,
    int UsageMax,
    int LogicalMin,
    int LogicalMax,
    int PhysicalMin,
    int PhysicalMax,
    int BitSize,
    int ReportCount,
    uint Units,
    uint UnitsExponent,
    int LinkCollection);

/// <summary>
/// A HID collection opened for reports (device classes plan, stage 7): input reports read on a thread
/// of their own into a queue, output reports written, feature and input reports got and set, and the
/// capabilities decoded from the preparsed data (Windows does not hand out the report descriptor).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class HidDevice : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly nint _preparsed;
    private readonly Thread _reader;
    private readonly object _gate = new();
    private readonly Queue<byte[]> _reports = new();
    private readonly nint _stop;
    private volatile bool _closing;
    private int _lost;

    private HidDevice(SafeFileHandle handle, nint preparsed, HidInfo info)
    {
        _handle = handle;
        _preparsed = preparsed;
        Info = info;
        _stop = Serial.CommNative.CreateEvent(0, true, false, 0);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "JGraph HID reader" };
        _reader.Start();
    }

    public HidInfo Info { get; }

    /// <summary>Raised on the reader thread per input report, with how many are waiting.</summary>
    public event Action<int>? ReportReceived;

    /// <summary>Raised once when the device goes away.</summary>
    public event Action<DeviceConnectionLostException>? ConnectionLost;

    public bool Connected => !_closing && Volatile.Read(ref _lost) == 0;

    /// <summary>A pulse for waiters: the queue is guarded by this buffer's lock and wait.</summary>
    public InputBuffer Signal { get; } = new();

    public int ReportsWaiting
    {
        get
        {
            lock (_gate)
            {
                return _reports.Count;
            }
        }
    }

    /// <summary>Every HID collection present, keyboards and mice marked as the system's.</summary>
    public static IReadOnlyList<HidInfo> List()
    {
        Guid hid;
        HidD_GetHidGuid(&hid);
        Dictionary<string, List<string>> usbOf = UsbCrossReference.HidPaths();
        var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string usb, List<string> paths) in usbOf)
        {
            foreach (string path in paths)
            {
                owner[path] = usb;
            }
        }

        var result = new List<HidInfo>();
        foreach ((string _, string path) in UsbEnumerator.InterfacePaths(hid))
        {
            using SafeFileHandle handle = Usb.UsbNative.CreateFile(path, 0, Usb.UsbNative.FILE_SHARE_READ | Usb.UsbNative.FILE_SHARE_WRITE, 0, Usb.UsbNative.OPEN_EXISTING, 0, 0);
            if (handle.IsInvalid)
            {
                continue;
            }

            HidInfo? info = Describe(handle, path, owner.TryGetValue(path, out string? usb) ? usb : "");
            if (info is not null)
            {
                result.Add(info);
            }
        }

        return result;
    }

    private static HidInfo? Describe(SafeFileHandle handle, string path, string usb)
    {
        var attributes = new HIDD_ATTRIBUTES { Size = (uint)sizeof(HIDD_ATTRIBUTES) };
        if (!HidD_GetAttributes(handle, &attributes))
        {
            return null;
        }

        nint preparsed = 0;
        HIDP_CAPS caps = default;
        if (HidD_GetPreparsedData(handle, &preparsed))
        {
            HidP_GetCaps(preparsed, &caps);
            HidD_FreePreparsedData(preparsed);
        }

        char* text = stackalloc char[256];
        string Read(delegate*<SafeFileHandle, char*, uint, bool> get)
        {
            new Span<char>(text, 256).Clear();
            return get(handle, text, 512) ? new string(text).TrimEnd('\0') : "";
        }

        // Windows keeps keyboards and mice (usage page 1, usages 2 and 6) for itself: their reports cannot be read.
        bool system = caps.UsagePage == 1 && caps.Usage is 2 or 6;
        return new HidInfo(path, attributes.VendorID, attributes.ProductID, attributes.VersionNumber, caps.UsagePage, caps.Usage,
            caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength,
            Read(&HidD_GetManufacturerString), Read(&HidD_GetProductString), Read(&HidD_GetSerialNumberString), usb, system);
    }

    /// <summary>Opens the collection at <paramref name="path"/> for reading and writing reports.</summary>
    public static HidDevice Open(string path, int inputBuffers = 0)
    {
        SafeFileHandle handle = Usb.UsbNative.CreateFile(path, Usb.UsbNative.GENERIC_READ | Usb.UsbNative.GENERIC_WRITE,
            Usb.UsbNative.FILE_SHARE_READ | Usb.UsbNative.FILE_SHARE_WRITE, 0, Usb.UsbNative.OPEN_EXISTING, Serial.CommNative.FILE_FLAG_OVERLAPPED, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new DeviceOpenException(error == 5
                ? "The HID collection is in use or kept by Windows (a keyboard or a mouse cannot be opened for its reports)."
                : $"The HID collection could not be opened: {new Win32Exception(error).Message}", error);
        }

        nint preparsed = 0;
        if (!HidD_GetPreparsedData(handle, &preparsed))
        {
            handle.Dispose();
            throw new DeviceOpenException("The HID collection's report layout could not be read.");
        }

        if (inputBuffers > 0)
        {
            HidD_SetNumInputBuffers(handle, (uint)inputBuffers);
        }

        HidInfo info = Describe(handle, path, "") ?? throw new DeviceOpenException("The HID collection could not be described.");
        return new HidDevice(handle, preparsed, info);
    }

    /// <summary>The number of input reports the driver keeps (HidD_GetNumInputBuffers).</summary>
    public int InputBuffers
    {
        get
        {
            uint count = 0;
            return HidD_GetNumInputBuffers(_handle, &count) ? (int)count : 0;
        }

        set
        {
            if (!HidD_SetNumInputBuffers(_handle, (uint)value))
            {
                throw new DeviceSettingsException($"The driver refused {value} input buffers (2 to 512).");
            }
        }
    }

    /// <summary>Takes the oldest input report, report ID first; null when none is waiting.</summary>
    public byte[]? TakeReport()
    {
        lock (_gate)
        {
            return _reports.TryDequeue(out byte[]? report) ? report : null;
        }
    }

    /// <summary>Discards the waiting input reports.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            _reports.Clear();
        }
    }

    /// <summary>Writes an output report through the interrupt OUT pipe (WriteFile), padded to the report length.</summary>
    public void Write(ReadOnlySpan<byte> report, TimeSpan timeout)
    {
        int length = Math.Max(report.Length, Info.OutputReportLength);
        byte[] padded = new byte[length];
        report.CopyTo(padded);
        nint signal = Serial.CommNative.CreateEvent(0, true, false, 0);
        var overlapped = (Serial.CommNative.OVERLAPPED*)NativeMemory.AllocZeroed((nuint)sizeof(Serial.CommNative.OVERLAPPED));
        try
        {
            overlapped->hEvent = signal;
            fixed (byte* p = padded)
            {
                uint done = 0;
                if (!Serial.CommNative.WriteFile(_handle, p, (uint)length, &done, overlapped))
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error != Serial.CommNative.ERROR_IO_PENDING)
                    {
                        throw new DeviceIOException($"The output report could not be written: {new Win32Exception(error).Message}", error);
                    }

                    if (Serial.CommNative.WaitForSingleObject(signal, (uint)Math.Clamp(timeout.TotalMilliseconds, 1, uint.MaxValue - 1)) != 0)
                    {
                        Serial.CommNative.CancelIoEx(_handle, overlapped);
                        Serial.CommNative.GetOverlappedResult(_handle, overlapped, &done, true);
                        throw new TimeoutException("The output report was not taken in time.");
                    }

                    if (!Serial.CommNative.GetOverlappedResult(_handle, overlapped, &done, false))
                    {
                        int failed = Marshal.GetLastPInvokeError();
                        throw new DeviceIOException($"The output report could not be written: {new Win32Exception(failed).Message}", failed);
                    }
                }
            }
        }
        finally
        {
            NativeMemory.Free(overlapped);
            Serial.CommNative.CloseHandle(signal);
        }
    }

    /// <summary>A feature report by ID (HidD_GetFeature), report ID first.</summary>
    public byte[] GetFeature(int reportId) => GetReport(reportId, Info.FeatureReportLength, feature: true);

    /// <summary>An input report by ID through the control pipe (HidD_GetInputReport).</summary>
    public byte[] GetInput(int reportId) => GetReport(reportId, Info.InputReportLength, feature: false);

    private byte[] GetReport(int reportId, int length, bool feature)
    {
        byte[] buffer = new byte[Math.Max(length, 1)];
        buffer[0] = (byte)reportId;
        fixed (byte* p = buffer)
        {
            bool ok = feature ? HidD_GetFeature(_handle, p, (uint)buffer.Length) : HidD_GetInputReport(_handle, p, (uint)buffer.Length);
            if (!ok)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new DeviceIOException($"The {(feature ? "feature" : "input")} report {reportId} could not be read: {new Win32Exception(error).Message}", error);
            }
        }

        return buffer;
    }

    /// <summary>Sends a feature report (HidD_SetFeature), report ID first, padded to the report length.</summary>
    public void SetFeature(ReadOnlySpan<byte> report) => SetReport(report, Info.FeatureReportLength, feature: true);

    /// <summary>Sends an output report through the control pipe (HidD_SetOutputReport).</summary>
    public void SetOutput(ReadOnlySpan<byte> report) => SetReport(report, Info.OutputReportLength, feature: false);

    private void SetReport(ReadOnlySpan<byte> report, int length, bool feature)
    {
        byte[] buffer = new byte[Math.Max(length, report.Length)];
        report.CopyTo(buffer);
        fixed (byte* p = buffer)
        {
            bool ok = feature ? HidD_SetFeature(_handle, p, (uint)buffer.Length) : HidD_SetOutputReport(_handle, p, (uint)buffer.Length);
            if (!ok)
            {
                int error = Marshal.GetLastPInvokeError();
                throw new DeviceIOException($"The {(feature ? "feature" : "output")} report could not be sent: {new Win32Exception(error).Message}", error);
            }
        }
    }

    /// <summary>Every button and value capability of every report type.</summary>
    public IReadOnlyList<HidCapability> Capabilities()
    {
        var result = new List<HidCapability>();
        HIDP_CAPS caps;
        HidP_GetCaps(_preparsed, &caps);
        (byte Type, string Name, ushort Buttons, ushort Values)[] kinds =
        [
            (HidP_Input, "input", caps.NumberInputButtonCaps, caps.NumberInputValueCaps),
            (HidP_Output, "output", caps.NumberOutputButtonCaps, caps.NumberOutputValueCaps),
            (HidP_Feature, "feature", caps.NumberFeatureButtonCaps, caps.NumberFeatureValueCaps),
        ];
        foreach ((byte type, string name, ushort buttons, ushort values) in kinds)
        {
            Collect(type, name, buttons, button: true, result);
            Collect(type, name, values, button: false, result);
        }

        return result;
    }

    private void Collect(byte type, string name, ushort count, bool button, List<HidCapability> into)
    {
        if (count == 0)
        {
            return;
        }

        var list = new HIDP_VALUE_CAPS[count];
        ushort length = count;
        fixed (HIDP_VALUE_CAPS* p = list)
        {
            int status = button ? HidP_GetButtonCaps(type, p, &length, _preparsed) : HidP_GetValueCaps(type, p, &length, _preparsed);
            if (status != HIDP_STATUS_SUCCESS)
            {
                return;
            }
        }

        for (int i = 0; i < length; i++)
        {
            HIDP_VALUE_CAPS c = list[i];
            bool range = c.IsRange != 0;
            int usageMin = c.UsageMin;
            int usageMax = range ? c.UsageMax : c.UsageMin;
            if (button)
            {
                // A button's ReportCount sits where a value's HasNull and Reserved do.
                int reportCount = c.HasNull | (c.Reserved << 8);
                into.Add(new HidCapability(name, true, c.ReportID, c.UsagePage, usageMin, usageMax, 0, 1, 0, 1, 1, reportCount, 0, 0, c.LinkCollection));
            }
            else
            {
                into.Add(new HidCapability(name, false, c.ReportID, c.UsagePage, usageMin, usageMax, c.LogicalMin, c.LogicalMax,
                    c.PhysicalMin, c.PhysicalMax, c.BitSize, c.ReportCount, c.Units, c.UnitsExp, c.LinkCollection));
            }
        }
    }

    /// <summary>A value usage read out of a report (HidP_GetUsageValue), or null when the report has none.</summary>
    public long? GetValue(ReadOnlySpan<byte> report, string reportType, int usagePage, int usage, bool scaled)
    {
        byte type = TypeOf(reportType);
        fixed (byte* p = report)
        {
            if (scaled)
            {
                int value;
                return HidP_GetScaledUsageValue(type, (ushort)usagePage, 0, (ushort)usage, &value, _preparsed, p, (uint)report.Length) == HIDP_STATUS_SUCCESS
                    ? value : null;
            }

            uint raw;
            return HidP_GetUsageValue(type, (ushort)usagePage, 0, (ushort)usage, &raw, _preparsed, p, (uint)report.Length) == HIDP_STATUS_SUCCESS
                ? raw : null;
        }
    }

    /// <summary>Writes a value usage into a report (HidP_SetUsageValue); false when the report has no such usage.</summary>
    public bool SetValue(Span<byte> report, string reportType, int usagePage, int usage, uint value)
    {
        byte type = TypeOf(reportType);
        fixed (byte* p = report)
        {
            return HidP_SetUsageValue(type, (ushort)usagePage, 0, (ushort)usage, value, _preparsed, p, (uint)report.Length) == HIDP_STATUS_SUCCESS;
        }
    }

    /// <summary>The buttons pressed in a report, on one usage page (HidP_GetUsages).</summary>
    public int[] GetButtons(ReadOnlySpan<byte> report, string reportType, int usagePage)
    {
        byte type = TypeOf(reportType);
        uint max = HidP_MaxUsageListLength(type, (ushort)usagePage, _preparsed);
        if (max == 0)
        {
            return [];
        }

        var usages = new ushort[max];
        uint length = max;
        fixed (byte* p = report)
        fixed (ushort* u = usages)
        {
            return HidP_GetUsages(type, (ushort)usagePage, 0, u, &length, _preparsed, p, (uint)report.Length) == HIDP_STATUS_SUCCESS
                ? usages.Take((int)length).Select(static x => (int)x).ToArray()
                : [];
        }
    }

    private static byte TypeOf(string reportType) => reportType switch
    {
        "output" => HidP_Output,
        "feature" => HidP_Feature,
        _ => HidP_Input,
    };

    private void ReadLoop()
    {
        int size = Math.Max(Info.InputReportLength, 1);
        byte* buffer = (byte*)NativeMemory.Alloc((nuint)size);
        nint signal = Serial.CommNative.CreateEvent(0, true, false, 0);
        var overlapped = (Serial.CommNative.OVERLAPPED*)NativeMemory.AllocZeroed((nuint)sizeof(Serial.CommNative.OVERLAPPED));
        nint* waits = stackalloc nint[2];
        waits[0] = signal;
        waits[1] = _stop;
        try
        {
            while (!_closing)
            {
                Serial.CommNative.ResetEvent(signal);
                *overlapped = default;
                overlapped->hEvent = signal;
                uint got = 0;
                if (!Serial.CommNative.ReadFile(_handle, buffer, (uint)size, &got, overlapped))
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error != Serial.CommNative.ERROR_IO_PENDING)
                    {
                        Lose(error);
                        return;
                    }

                    if (Serial.CommNative.WaitForMultipleObjects(2, waits, false, Serial.CommNative.INFINITE) != 0)
                    {
                        Serial.CommNative.CancelIoEx(_handle, overlapped);
                        Serial.CommNative.GetOverlappedResult(_handle, overlapped, &got, true);
                        return;
                    }

                    if (!Serial.CommNative.GetOverlappedResult(_handle, overlapped, &got, false))
                    {
                        Lose(Marshal.GetLastPInvokeError());
                        return;
                    }
                }

                if (got > 0)
                {
                    byte[] report = new ReadOnlySpan<byte>(buffer, (int)got).ToArray();
                    int waiting;
                    lock (_gate)
                    {
                        _reports.Enqueue(report);
                        waiting = _reports.Count;
                    }

                    Signal.Pulse();
                    ReportReceived?.Invoke(waiting);
                }
            }
        }
        finally
        {
            NativeMemory.Free(buffer);
            NativeMemory.Free(overlapped);
            Serial.CommNative.CloseHandle(signal);
        }
    }

    private void Lose(int error)
    {
        if (_closing || Interlocked.Exchange(ref _lost, 1) != 0)
        {
            return;
        }

        var lost = new DeviceConnectionLostException($"The HID device is no longer connected ({new Win32Exception(error).Message}).", error);
        Signal.SetFault(lost);
        ThreadPool.QueueUserWorkItem(_ => ConnectionLost?.Invoke(lost));
    }

    public void Dispose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Serial.CommNative.SetEvent(_stop);
        _reader.Join(2000);
        HidD_FreePreparsedData(_preparsed);
        _handle.Dispose();
        Serial.CommNative.CloseHandle(_stop);
    }
}
