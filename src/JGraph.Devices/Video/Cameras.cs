using System.Runtime.Versioning;

namespace JGraph.Devices.Video;

/// <summary>A camera as webcamlist lists it: its name, and what opens it (a device path, or a simulated one's key).</summary>
public sealed record CameraInfo(string Name, string Id);

/// <summary>
/// One of a camera's adjustable controls (Brightness, Exposure, Focus): its range, its step, the
/// value it starts at, and whether the camera can drive it itself (<paramref name="HasAuto"/>) and
/// whether a value can be set (<paramref name="HasManual"/>).
/// </summary>
public sealed record CameraControl(string Name, int Minimum, int Maximum, int Step, int Default, bool HasAuto, bool HasManual);

/// <summary>
/// One picture from a camera: <see cref="Bgrx"/> holds four bytes a pixel (blue, green, red, unused),
/// rows from the top, with no padding between rows.
/// </summary>
public sealed class CameraFrame(int width, int height, byte[] bgrx, DateTime time, long number)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public byte[] Bgrx { get; } = bgrx;

    /// <summary>When the frame arrived, on this machine's clock.</summary>
    public DateTime Time { get; } = time;

    /// <summary>The frame's count since the camera was opened, from 1.</summary>
    public long Number { get; } = number;
}

/// <summary>A camera that could not be opened, configured or read.</summary>
public sealed class CameraException(string message) : Exception(message);

/// <summary>An open camera: it streams from the moment it is opened until it is disposed.</summary>
public interface ICamera : IDisposable
{
    string Name { get; }

    /// <summary>The frame sizes the camera offers, each once, in the order it lists them.</summary>
    IReadOnlyList<(int Width, int Height)> Resolutions { get; }

    (int Width, int Height) Resolution { get; }

    /// <summary>Switches to one of <see cref="Resolutions"/>; frames of the old size are dropped.</summary>
    void SetResolution(int width, int height);

    IReadOnlyList<CameraControl> Controls { get; }

    /// <summary>A control's value, and whether the camera is driving it.</summary>
    (int Value, bool Auto) ReadControl(string name);

    void WriteControl(string name, int value, bool auto);

    /// <summary>The first frame to arrive after the call; a <see cref="TimeoutException"/> when none does in time.</summary>
    CameraFrame NextFrame(TimeSpan timeout);

    /// <summary>The newest frame, or null before the first.</summary>
    CameraFrame? Latest();

    /// <summary>Raised on a device thread each time a frame arrives.</summary>
    event Action? FrameArrived;
}

/// <summary>What webcamlist and webcam stand on: the machine's cameras, or a simulation.</summary>
public interface ICameraBackend
{
    IReadOnlyList<CameraInfo> Cameras();

    /// <summary>Opens a camera and starts it streaming; a <see cref="CameraException"/> when it cannot.</summary>
    ICamera Open(CameraInfo camera);
}

/// <summary>
/// The machine's cameras through Media Foundation (device classes plan, stage D11, ADR 0195): the
/// video capture sources MFEnumDeviceSources lists, by their friendly names, each opened as a source
/// reader that hands back 32-bit RGB whatever the camera's own format is.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MediaFoundationCameras : ICameraBackend
{
    public static readonly MediaFoundationCameras Instance = new();

    private static readonly Lazy<int> Started = new(static () => VideoNative.MFStartup(VideoNative.MF_VERSION, 0));

    /// <summary>Starts Media Foundation for the process, once; it stays up until the process ends.</summary>
    internal static void Start()
    {
        if (Started.Value < 0)
        {
            throw new CameraException($"Media Foundation could not start ({Hex(Started.Value)}).");
        }
    }

    internal static string Hex(int hr) => "0x" + hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Runs <paramref name="work"/> on a thread of its own in the multithreaded apartment, where Media
    /// Foundation wants its callers, and answers what it answered or rethrows what it threw.
    /// </summary>
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
            Name = "JGraph camera call",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        return failure switch
        {
            null => result,
            CameraException or TimeoutException => throw failure,
            DllNotFoundException or EntryPointNotFoundException => throw new CameraException("Media Foundation is not installed on this machine."),
            _ => throw new CameraException(failure.Message),
        };
    }

    public IReadOnlyList<CameraInfo> Cameras()
    {
        try
        {
            return InMta(Enumerate);
        }
        catch (CameraException)
        {
            return [];
        }
    }

    private static unsafe IReadOnlyList<CameraInfo> Enumerate()
    {
        Start();
        var cameras = new List<CameraInfo>();
        if (VideoNative.MFCreateAttributes(out nint attributes, 1) < 0)
        {
            return cameras;
        }

        try
        {
            VideoNative.SetGUID(attributes, VideoNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, VideoNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
            if (VideoNative.MFEnumDeviceSources(attributes, out nint activates, out uint count) < 0)
            {
                return cameras;
            }

            for (uint i = 0; i < count; i++)
            {
                nint activate = ((nint*)activates)[i];
                string? name = VideoNative.GetString(activate, VideoNative.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME);
                string? link = VideoNative.GetString(activate, VideoNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK);
                if (name is not null && link is not null)
                {
                    cameras.Add(new CameraInfo(name, link));
                }

                VideoNative.Release(ref activate);
            }

            VideoNative.CoTaskMemFree(activates);
        }
        finally
        {
            VideoNative.Release(ref attributes);
        }

        return cameras;
    }

    public ICamera Open(CameraInfo camera) => InMta(() => (ICamera)new MediaFoundationCamera(camera));
}

/// <summary>
/// One camera open through a source reader. A capture thread reads samples for as long as the camera
/// is open and keeps the newest; a picture is copied out of it only when someone asks. A change of
/// resolution is made by that thread between two samples. The controls are DirectShow's
/// IAMVideoProcAmp and IAMCameraControl, which a capture source answers to.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class MediaFoundationCamera : ICamera
{
    private static readonly string[] ProcAmpNames =
        ["Brightness", "Contrast", "Hue", "Saturation", "Sharpness", "Gamma", "ColorEnable", "WhiteBalance", "BacklightCompensation", "Gain"];

    private static readonly string[] CameraControlNames = ["Pan", "Tilt", "Roll", "Zoom", "Exposure", "Iris", "Focus"];

    private const int FlagAuto = 1;
    private const int FlagManual = 2;

    private readonly object _gate = new();
    private readonly List<(int Width, int Height, double Rate)> _native = new();
    private readonly List<CameraControl> _controls = new();
    private readonly Dictionary<string, (bool ProcAmp, int Property)> _controlSlots = new(StringComparer.Ordinal);
    private readonly Thread _capture;
    private nint _source;
    private nint _reader;
    private nint _procAmp;
    private nint _cameraControl;
    private nint _latest;
    private (int Width, int Height, int Stride) _latestShape;
    private DateTime _latestTime;
    private long _number;
    private (int Width, int Height) _resolution;
    private int _stride;
    private CameraException? _failure;
    private volatile bool _stop;
    private (int Width, int Height)? _wanted;
    private Exception? _wantedFailure;
    private bool _wantedDone;
    private bool _disposed;

    public MediaFoundationCamera(CameraInfo camera)
    {
        Name = camera.Name;
        MediaFoundationCameras.Start();
        try
        {
            Check(VideoNative.MFCreateAttributes(out nint attributes, 2), "Opening the camera");
            try
            {
                VideoNative.SetGUID(attributes, VideoNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE, VideoNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
                VideoNative.SetString(attributes, VideoNative.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK, camera.Id);
                Check(VideoNative.MFCreateDeviceSource(attributes, out _source), "Opening the camera");
            }
            finally
            {
                VideoNative.Release(ref attributes);
            }

            Check(VideoNative.MFCreateAttributes(out nint readerAttributes, 1), "Opening the camera");
            try
            {
                // The reader's video processor turns the camera's own format (YUY2, NV12, MJPG) into RGB32.
                VideoNative.SetUINT32(readerAttributes, VideoNative.MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, 1);
                Check(VideoNative.MFCreateSourceReaderFromMediaSource(_source, readerAttributes, out _reader), "Opening the camera");
            }
            finally
            {
                VideoNative.Release(ref readerAttributes);
            }

            ReadNativeTypes();
            if (_native.Count == 0)
            {
                throw new CameraException($"The camera '{Name}' offers no video format.");
            }

            (int Width, int Height) first = (_native[0].Width, _native[0].Height);
            if (VideoNative.GetCurrentMediaType(_reader, VideoNative.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out nint current) >= 0)
            {
                if (FrameSize(current) is { } size && _native.Exists(n => (n.Width, n.Height) == size))
                {
                    first = size;
                }

                VideoNative.Release(ref current);
            }

            Configure(first.Width, first.Height);
            ReadControls();
        }
        catch
        {
            ReleaseAll();
            throw;
        }

        _capture = new Thread(CaptureLoop) { IsBackground = true, Name = "JGraph camera " + Name };
        _capture.SetApartmentState(ApartmentState.MTA);
        _capture.Start();
    }

    public string Name { get; }

    public IReadOnlyList<(int Width, int Height)> Resolutions =>
        _native.Where(static n => n.Width > 0).Select(static n => (n.Width, n.Height)).Distinct().ToList();

    public (int Width, int Height) Resolution
    {
        get
        {
            lock (_gate)
            {
                return _resolution;
            }
        }
    }

    public IReadOnlyList<CameraControl> Controls => _controls;

    public event Action? FrameArrived;

    private void Check(int hr, string what)
    {
        if (hr >= 0)
        {
            return;
        }

        string why = hr switch
        {
            unchecked((int)0x80070005) => "Windows denied access to it; see Settings, Privacy & security, Camera",
            unchecked((int)0xC00D3704) or unchecked((int)0xC00D3EA3) or unchecked((int)0x80070020) => "another program is using it",
            unchecked((int)0xC00D36B4) or unchecked((int)0xC00D5212) => "it cannot give that format",
            unchecked((int)0xC00D3EA2) => "it was disconnected or stopped sending",
            _ => System.Runtime.InteropServices.Marshal.GetExceptionForHR(hr)?.Message.TrimEnd('.') ?? "the device refused",
        };
        throw new CameraException($"{what} '{Name}' failed: {why} ({MediaFoundationCameras.Hex(hr)}).");
    }

    private static (int Width, int Height)? FrameSize(nint mediaType) =>
        VideoNative.GetUINT64(mediaType, VideoNative.MF_MT_FRAME_SIZE, out ulong packed) >= 0
            ? ((int)(packed >> 32), (int)(packed & 0xFFFFFFFF))
            : null;

    private void ReadNativeTypes()
    {
        for (uint i = 0; ; i++)
        {
            int hr = VideoNative.GetNativeMediaType(_reader, VideoNative.MF_SOURCE_READER_FIRST_VIDEO_STREAM, i, out nint type);
            if (hr < 0)
            {
                break;
            }

            if (FrameSize(type) is { Width: > 0, Height: > 0 } size)
            {
                double rate = VideoNative.GetUINT64(type, VideoNative.MF_MT_FRAME_RATE, out ulong packed) >= 0 && (packed & 0xFFFFFFFF) != 0
                    ? (double)(packed >> 32) / (packed & 0xFFFFFFFF)
                    : 0;
                _native.Add((size.Width, size.Height, rate));
            }
            else
            {
                _native.Add((0, 0, 0));
            }

            VideoNative.Release(ref type);
        }
    }

    /// <summary>
    /// Picks the camera's own format of this size with the highest frame rate (the first listed of
    /// equals), then asks the reader for RGB32 of the same size.
    /// </summary>
    private void Configure(int width, int height)
    {
        int best = -1;
        for (int i = 0; i < _native.Count; i++)
        {
            if (_native[i].Width == width && _native[i].Height == height && (best < 0 || _native[i].Rate > _native[best].Rate))
            {
                best = i;
            }
        }

        if (best < 0)
        {
            throw new CameraException($"The camera '{Name}' has no {width}x{height} format.");
        }

        const uint stream = VideoNative.MF_SOURCE_READER_FIRST_VIDEO_STREAM;
        Check(VideoNative.GetNativeMediaType(_reader, stream, (uint)best, out nint native), "Setting the resolution of");
        try
        {
            Check(VideoNative.SetCurrentMediaType(_reader, stream, native), "Setting the resolution of");
        }
        finally
        {
            VideoNative.Release(ref native);
        }

        Check(VideoNative.MFCreateMediaType(out nint rgb), "Setting the resolution of");
        try
        {
            VideoNative.SetGUID(rgb, VideoNative.MF_MT_MAJOR_TYPE, VideoNative.MFMediaType_Video);
            VideoNative.SetGUID(rgb, VideoNative.MF_MT_SUBTYPE, VideoNative.MFVideoFormat_RGB32);
            VideoNative.SetUINT64(rgb, VideoNative.MF_MT_FRAME_SIZE, ((ulong)(uint)width << 32) | (uint)height);
            Check(VideoNative.SetCurrentMediaType(_reader, stream, rgb), "Setting the resolution of");
        }
        finally
        {
            VideoNative.Release(ref rgb);
        }

        int stride = width * 4;
        if (VideoNative.GetCurrentMediaType(_reader, stream, out nint current) >= 0)
        {
            if (VideoNative.GetUINT32(current, VideoNative.MF_MT_DEFAULT_STRIDE, out uint raw) >= 0 && raw != 0)
            {
                stride = (int)raw;
            }

            VideoNative.Release(ref current);
        }

        lock (_gate)
        {
            _resolution = (width, height);
            _stride = stride;
            VideoNative.Release(ref _latest);
        }
    }

    private void ReadControls()
    {
        VideoNative.QueryInterface(_source, VideoNative.IID_IAMVideoProcAmp, out _procAmp);
        VideoNative.QueryInterface(_source, VideoNative.IID_IAMCameraControl, out _cameraControl);
        void Add(nint control, string[] names, bool procAmp)
        {
            for (int property = 0; control != 0 && property < names.Length; property++)
            {
                if (VideoNative.ControlRange(control, property, out int lo, out int hi, out int step, out int standard, out int caps) >= 0
                    && VideoNative.ControlGet(control, property, out _, out _) >= 0)
                {
                    _controls.Add(new CameraControl(names[property], lo, hi, step, standard, (caps & FlagAuto) != 0, (caps & FlagManual) != 0));
                    _controlSlots[names[property]] = (procAmp, property);
                }
            }
        }

        Add(_procAmp, ProcAmpNames, true);
        Add(_cameraControl, CameraControlNames, false);
    }

    private (nint Control, int Property) Slot(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _controlSlots.TryGetValue(name, out (bool ProcAmp, int Property) slot)
            ? (slot.ProcAmp ? _procAmp : _cameraControl, slot.Property)
            : throw new CameraException($"The camera '{Name}' has no {name} control.");
    }

    public (int Value, bool Auto) ReadControl(string name)
    {
        (nint control, int property) = Slot(name);
        return MediaFoundationCameras.InMta(() =>
        {
            Check(VideoNative.ControlGet(control, property, out int value, out int flags), $"Reading {name} of");
            return (value, (flags & FlagAuto) != 0);
        });
    }

    public void WriteControl(string name, int value, bool auto)
    {
        (nint control, int property) = Slot(name);
        MediaFoundationCameras.InMta(() =>
        {
            Check(VideoNative.ControlSet(control, property, value, auto ? FlagAuto : FlagManual), $"Setting {name} of");
            return 0;
        });
    }

    public void SetResolution(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_failure is not null)
            {
                throw _failure;
            }

            _wanted = (width, height);
            _wantedDone = false;
            _wantedFailure = null;
            DateTime until = DateTime.UtcNow.AddSeconds(15);
            while (!_wantedDone)
            {
                TimeSpan left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !Monitor.Wait(_gate, left))
                {
                    _wanted = null;
                    throw new TimeoutException();
                }
            }

            if (_wantedFailure is not null)
            {
                throw _wantedFailure;
            }
        }
    }

    private void CaptureLoop()
    {
        while (!_stop)
        {
            (int Width, int Height)? wanted;
            (int Width, int Height) was;
            lock (_gate)
            {
                wanted = _wanted;
                was = _resolution;
            }

            if (wanted is { } size)
            {
                Exception? failure = null;
                try
                {
                    Configure(size.Width, size.Height);
                }
                catch (CameraException refused)
                {
                    failure = refused;
                    try
                    {
                        Configure(was.Width, was.Height);
                    }
                    catch (CameraException)
                    {
                        // The camera took neither size; the read below reports what is wrong with it.
                    }
                }

                lock (_gate)
                {
                    _wanted = null;
                    _wantedFailure = failure;
                    _wantedDone = true;
                    Monitor.PulseAll(_gate);
                }
            }

            int hr = VideoNative.ReadSample(_reader, VideoNative.MF_SOURCE_READER_FIRST_VIDEO_STREAM, out uint flags, out _, out nint sample);
            if (_stop)
            {
                VideoNative.Release(ref sample);
                break;
            }

            if (hr < 0 || (flags & (VideoNative.MF_SOURCE_READERF_ERROR | VideoNative.MF_SOURCE_READERF_ENDOFSTREAM)) != 0)
            {
                VideoNative.Release(ref sample);
                lock (_gate)
                {
                    try
                    {
                        Check(hr < 0 ? hr : unchecked((int)0xC00D3EA2), "Reading from the camera");
                    }
                    catch (CameraException lost)
                    {
                        _failure = lost;
                    }

                    _wantedFailure = _failure;
                    _wantedDone = true;
                    Monitor.PulseAll(_gate);
                }

                return;
            }

            if (sample == 0)
            {
                continue;
            }

            lock (_gate)
            {
                VideoNative.Release(ref _latest);
                _latest = sample;
                _latestShape = (_resolution.Width, _resolution.Height, _stride);
                _latestTime = DateTime.Now;
                _number++;
                Monitor.PulseAll(_gate);
            }

            try
            {
                FrameArrived?.Invoke();
            }
            catch (Exception)
            {
                // A listener's failure must not end the capture.
            }
        }
    }

    public CameraFrame NextFrame(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nint sample;
        (int Width, int Height, int Stride) shape;
        DateTime time;
        long number;
        lock (_gate)
        {
            long after = _number;
            DateTime until = DateTime.UtcNow + timeout;
            while (_number == after || _latest == 0)
            {
                if (_failure is not null)
                {
                    throw _failure;
                }

                TimeSpan left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !Monitor.Wait(_gate, left))
                {
                    throw new TimeoutException();
                }
            }

            sample = _latest;
            VideoNative.AddRef(sample);
            shape = _latestShape;
            time = _latestTime;
            number = _number;
        }

        return Copy(sample, shape, time, number);
    }

    public CameraFrame? Latest()
    {
        nint sample;
        (int Width, int Height, int Stride) shape;
        DateTime time;
        long number;
        lock (_gate)
        {
            if (_disposed || _latest == 0)
            {
                return null;
            }

            sample = _latest;
            VideoNative.AddRef(sample);
            shape = _latestShape;
            time = _latestTime;
            number = _number;
        }

        return Copy(sample, shape, time, number);
    }

    /// <summary>Copies a sample's picture out, rows from the top, and lets the sample go.</summary>
    private CameraFrame Copy(nint sample, (int Width, int Height, int Stride) shape, DateTime time, long number)
    {
        int rowBytes = shape.Width * 4;
        byte[] pixels = new byte[rowBytes * shape.Height];
        try
        {
            Check(VideoNative.ConvertToContiguousBuffer(sample, out nint buffer), "Reading a frame from");
            try
            {
                nint buffer2D = 0;
                if (VideoNative.QueryInterface(buffer, VideoNative.IID_IMF2DBuffer, out buffer2D) >= 0
                    && VideoNative.Lock2D(buffer2D, out byte* scanline0, out int pitch) >= 0)
                {
                    fixed (byte* to = pixels)
                    {
                        for (int r = 0; r < shape.Height; r++)
                        {
                            Buffer.MemoryCopy(scanline0 + ((long)r * pitch), to + ((long)r * rowBytes), rowBytes, rowBytes);
                        }
                    }

                    VideoNative.Unlock2D(buffer2D);
                }
                else
                {
                    Check(VideoNative.BufferLock(buffer, out byte* data, out uint length), "Reading a frame from");
                    int stride = Math.Abs(shape.Stride);
                    if ((long)stride * shape.Height <= length && stride >= rowBytes)
                    {
                        fixed (byte* to = pixels)
                        {
                            for (int r = 0; r < shape.Height; r++)
                            {
                                // A negative stride is a picture stored from its bottom row up.
                                int from = shape.Stride < 0 ? shape.Height - 1 - r : r;
                                Buffer.MemoryCopy(data + ((long)from * stride), to + ((long)r * rowBytes), rowBytes, rowBytes);
                            }
                        }
                    }

                    VideoNative.BufferUnlock(buffer);
                }

                VideoNative.Release(ref buffer2D);
            }
            finally
            {
                VideoNative.Release(ref buffer);
            }
        }
        finally
        {
            VideoNative.Release(ref sample);
        }

        return new CameraFrame(shape.Width, shape.Height, pixels, time, number);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stop = true;
            Monitor.PulseAll(_gate);
        }

        // A read returns with the next frame; a camera that has stopped sending is shut down under it.
        if (!_capture.Join(TimeSpan.FromSeconds(3)))
        {
            nint source = _source;
            MediaFoundationCameras.InMta(() => VideoNative.SourceShutdown(source));
            _capture.Join(TimeSpan.FromSeconds(5));
        }

        MediaFoundationCameras.InMta(() =>
        {
            ReleaseAll();
            return 0;
        });
    }

    private void ReleaseAll()
    {
        lock (_gate)
        {
            VideoNative.Release(ref _latest);
        }

        VideoNative.Release(ref _procAmp);
        VideoNative.Release(ref _cameraControl);
        VideoNative.Release(ref _reader);
        if (_source != 0)
        {
            VideoNative.SourceShutdown(_source);
        }

        VideoNative.Release(ref _source);
    }
}
