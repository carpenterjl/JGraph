using System.Globalization;
using System.Text;
using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Devices.Simulation;
using JGraph.Devices.Video;
using JGraph.Objects;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>What webcamlist and webcam share (device classes plan, stage D11, ADR 0195).</summary>
internal static class WebcamShared
{
    /// <summary>The session's cameras: jgraph.internal.camsim's, or the machine's through Media Foundation.</summary>
    public static ICameraBackend Backend(DeviceSession session) =>
        session.CameraSimulation is { } simulated ? simulated
        : OperatingSystem.IsWindows() ? MediaFoundationCameras.Instance
        : NoCameras.Instance;

    /// <summary>A platform with no camera backend lists nothing.</summary>
    private sealed class NoCameras : ICameraBackend
    {
        public static readonly NoCameras Instance = new();

        public IReadOnlyList<CameraInfo> Cameras() => [];

        public ICamera Open(CameraInfo camera) => throw new CameraException("Cameras are supported on Windows only.");
    }

    /// <summary><c>webcamlist</c>: the cameras' names as a cell column, in the order webcam numbers them.</summary>
    public static JgsValue List(DeviceSession session, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count > 0)
        {
            throw new JgsRuntimeException(line, col, "MATLAB:TooManyInputs", "Too many input arguments.");
        }

        JgsValue[] names = Backend(session).Cameras().Select(static c => JgsValue.Str(c.Name)).ToArray();
        JgsValue list = JgsValue.Cell(names);
        list.Reshape(names.Length, names.Length == 0 ? 0 : 1);
        return list;
    }

    /// <summary>A frame as snapshot answers it: height-by-width-by-3 uint8, red first.</summary>
    public static JgsValue Picture(CameraFrame frame)
    {
        int height = frame.Height;
        int width = frame.Width;
        int plane = height * width;
        var flat = new double[plane * 3];
        byte[] pixels = frame.Bgrx;

        // The frame is row-major BGRX; an array is column-major with the channels last.
        for (int r = 0; r < height; r++)
        {
            int row = r * width * 4;
            for (int c = 0; c < width; c++)
            {
                int from = row + (c * 4);
                int to = (c * height) + r;
                flat[to] = pixels[from + 2];
                flat[plane + to] = pixels[from + 1];
                flat[(2 * plane) + to] = pixels[from];
            }
        }

        JgsValue picture = JgsMatrix.FromColumnMajorDims(flat, [height, width, 3]);
        picture.SetNumericClass(JgsNumericClass.UInt8);
        return picture;
    }

    /// <summary>A frame as an image plot takes it: row-major 0xAARRGGBB, opaque.</summary>
    public static uint[] Argb(CameraFrame frame)
    {
        byte[] pixels = frame.Bgrx;
        var argb = new uint[frame.Width * frame.Height];
        for (int i = 0; i < argb.Length; i++)
        {
            int at = i * 4;
            argb[i] = 0xFF000000u | ((uint)pixels[at + 2] << 16) | ((uint)pixels[at + 1] << 8) | pixels[at];
        }

        return argb;
    }
}

/// <summary>
/// <c>cam = webcam</c>, <c>webcam(index)</c>, <c>webcam(name)</c>, each with name-value pairs (device
/// classes plan, stage D11, ADR 0195): the MATLAB Support Package for USB Webcams' object, written from
/// its documentation because R2025b on the recording machine does not have the package. Name,
/// Resolution and AvailableResolutions are every camera's; the rest are the controls this camera has
/// (Brightness, Exposure, Focus …), each with an <c>…Mode</c> of 'auto' or 'manual' where the camera can
/// drive it itself. <c>snapshot</c> answers the next frame and when it arrived; <c>preview</c> shows the
/// frames in a figure until <c>closePreview</c>. The camera streams from the object's creation to its
/// deletion, and one camera has one object at a time.
/// </summary>
internal sealed class WebcamObject : DeviceObject
{
    private static readonly Dictionary<string, DeviceMethodBody> MethodTable = Methods();
    private static readonly string[] Listing = ["closePreview", "delete", "preview", "snapshot", "webcam"];

    // Declared once with no camera, so that snapshot(5) is an undefined function for a double rather
    // than an unknown name before any webcam exists.
    private static readonly DeviceClass Bare = Declare(null);

    /// <summary>Where preview figures are numbered from.</summary>
    internal const int FirstPreviewNumber = 1001;

    /// <summary>How long snapshot waits for a frame.</summary>
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(10);

    private readonly DeviceClass _class;
    private readonly ICamera _camera;
    private readonly DeviceEventQueue _queue;
    private readonly Action _onFrame;
    private FigureModel? _previewFigure;
    private RgbImagePlot? _previewPlot;
    private int _previewPending;
    private long _previewPosted;
    private long _previewShown;

    private WebcamObject(DeviceSession session, Interpreter interpreter, CameraInfo info, ICamera camera)
        : base(session, interpreter)
    {
        Info = info;
        _camera = camera;
        _class = Declare(camera);
        _queue = DeviceEventQueue.ForCurrentThread();
        _onFrame = OnFrame;
    }

    /// <summary>The camera this object holds.</summary>
    public CameraInfo Info { get; }

    public override DeviceClass Class => _class;

    public override string? Summary() => Deleted ? "deleted" : $"{_camera.Name}, {ResolutionText(_camera.Resolution)}";

    private static string ResolutionText((int Width, int Height) size) =>
        string.Create(CultureInfo.InvariantCulture, $"{size.Width}x{size.Height}");

    private static JgsRuntimeException Refusal(int line, int col, string key, string message) =>
        new(line, col, "MATLAB:webcam:" + key, message);

    // --- the constructor ---------------------------------------------------------------------------------

    public static JgsValue Create(DeviceSession session, Interpreter interpreter, IReadOnlyList<JgsValue> args, int line, int col)
    {
        // An odd count leads with the camera; an even one is name-value pairs only.
        JgsValue? which = args.Count % 2 == 1 ? TransportClient.Str2Char(args[0]) : null;
        IReadOnlyList<JgsValue> pairs = which is null ? args : args.Skip(1).ToArray();
        for (int i = 0; i < pairs.Count; i += 2)
        {
            if (!DeviceChecks.IsText(pairs[i]))
            {
                throw Refusal(line, col, "invalidNameValue", "Property names in webcam's name-value pairs must be text.");
            }
        }

        ICameraBackend backend = WebcamShared.Backend(session);
        IReadOnlyList<CameraInfo> cameras = backend.Cameras();
        if (cameras.Count == 0)
        {
            throw Refusal(line, col, "noWebcams", "No webcams have been detected. Verify that a webcam is connected to this computer and that Windows lists it.");
        }

        CameraInfo info = which is null ? cameras[0] : Pick(cameras, which, line, col);
        if (session.Live.OfType<WebcamObject>().Any(w => w.Info.Id == info.Id))
        {
            throw Refusal(line, col, "connectionExists",
                $"A webcam object for '{info.Name}' already exists. Clear it before creating another for the same camera.");
        }

        ICamera camera;
        try
        {
            camera = backend.Open(info);
        }
        catch (CameraException e)
        {
            throw Refusal(line, col, "openFailed", e.Message);
        }

        var cam = new WebcamObject(session, interpreter, info, camera);
        try
        {
            var call = new DeviceCall { Target = cam, Args = [], Line = line, Column = col };
            for (int i = 0; i < pairs.Count; i += 2)
            {
                cam.SetProperty(DeviceChecks.Text(pairs[i]), pairs[i + 1], call, ignoreCase: true);
            }
        }
        catch
        {
            camera.Dispose();
            throw;
        }

        session.Remember(cam);
        JgsValue made = JgsValue.External(cam);
        JgsLifetime.Minted(made);
        return made;
    }

    /// <summary>The camera an index (from 1) or a name picks: a name in any case, or the one camera whose name holds it.</summary>
    private static CameraInfo Pick(IReadOnlyList<CameraInfo> cameras, JgsValue which, int line, int col)
    {
        string Listed() => string.Join(", ", cameras.Select(static c => $"'{c.Name}'"));
        if (DeviceChecks.IsText(which))
        {
            string name = DeviceChecks.Text(which);
            CameraInfo[] exact = cameras.Where(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (exact.Length > 0)
            {
                return exact[0];
            }

            CameraInfo[] partial = name.Length == 0 ? [] : cameras.Where(c => c.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return partial.Length switch
            {
                1 => partial[0],
                0 => throw Refusal(line, col, "invalidName", $"No webcam is named '{name}'. The webcams are: {Listed()}."),
                _ => throw Refusal(line, col, "ambiguousName",
                    $"'{name}' matches more than one webcam: {string.Join(", ", partial.Select(static c => $"'{c.Name}'"))}. Give more of the name, or the index from webcamlist."),
            };
        }

        bool numeric = DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(which)) && DeviceChecks.Count(which) == 1
            && !JgsBuiltins.HasComplexPart(which);
        double k = numeric ? DeviceChecks.Numbers(which).First() : double.NaN;
        if (!(k >= 1 && k == Math.Floor(k) && k <= cameras.Count))
        {
            throw Refusal(line, col, "invalidIndex",
                cameras.Count == 1
                    ? "The webcam must be a name or the index 1: webcamlist lists one webcam."
                    : $"The webcam must be a name or an index from 1 to {cameras.Count.ToString(CultureInfo.InvariantCulture)}: webcamlist lists {cameras.Count.ToString(CultureInfo.InvariantCulture)} webcams.");
        }

        return cameras[(int)k - 1];
    }

    // --- the class ---------------------------------------------------------------------------------------

    private static WebcamObject Me(DeviceObject o) => (WebcamObject)o;

    /// <summary>
    /// The class of one camera's object: Name, Resolution and AvailableResolutions, then its controls in
    /// alphabetical order, each control that the camera can drive followed by its Mode.
    /// </summary>
    private static DeviceClass Declare(ICamera? camera)
    {
        var properties = new List<DeviceProperty>
        {
            new("Name", static (o, _) => JgsValue.Str(Me(o)._camera.Name)),
            new("Resolution",
                static (o, _) => JgsValue.Str(ResolutionText(Me(o)._camera.Resolution)),
                static (o, value, call) => Me(o).SetResolution(value, call)),
            new("AvailableResolutions", static (o, _) =>
            {
                JgsValue[] sizes = Me(o)._camera.Resolutions.Select(static r => JgsValue.Str(ResolutionText(r))).ToArray();
                JgsValue row = JgsValue.Cell(sizes);
                row.Reshape(1, sizes.Length);
                return row;
            }),
        };
        foreach (CameraControl control in (camera?.Controls ?? []).OrderBy(static c => c.Name, StringComparer.Ordinal))
        {
            CameraControl c = control;
            properties.Add(new DeviceProperty(c.Name,
                (o, call) => JgsValue.Number(Me(o).Read(c, call).Value),
                c.HasManual ? (o, value, call) => Me(o).SetControl(c, value, call) : null));
            if (c.HasAuto)
            {
                properties.Add(new DeviceProperty(c.Name + "Mode",
                    (o, call) => JgsValue.Str(Me(o).Read(c, call).Auto ? "auto" : "manual"),
                    c.HasManual ? (o, value, call) => Me(o).SetMode(c, value, call) : null));
            }
        }

        return new DeviceClass("webcam", "webcam", [], properties, MethodTable, Listing, properties.Select(static p => p.Name).ToArray());
    }

    private (int Value, bool Auto) Read(CameraControl control, DeviceCall call)
    {
        try
        {
            return _camera.ReadControl(control.Name);
        }
        catch (CameraException e)
        {
            throw call.Error("MATLAB:webcam:controlFailed", e.Message);
        }
    }

    private void SetResolution(JgsValue value, DeviceCall call)
    {
        value = TransportClient.Str2Char(value);
        string listed = string.Join(", ", _camera.Resolutions.Select(static r => $"'{ResolutionText(r)}'"));
        if (!DeviceChecks.IsText(value))
        {
            throw call.Error("MATLAB:webcam:invalidResolution", $"Resolution must be text, one of: {listed}.");
        }

        string text = DeviceChecks.Text(value).Trim();
        foreach ((int Width, int Height) size in _camera.Resolutions)
        {
            if (ResolutionText(size).Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _camera.SetResolution(size.Width, size.Height);
                }
                catch (CameraException e)
                {
                    throw call.Error("MATLAB:webcam:invalidResolution", e.Message);
                }
                catch (TimeoutException)
                {
                    throw call.Error("MATLAB:webcam:timeout", $"The webcam '{_camera.Name}' did not take the resolution in time.");
                }

                return;
            }
        }

        throw call.Error("MATLAB:webcam:invalidResolution", $"'{text}' is not a resolution of '{_camera.Name}'. Its resolutions are: {listed}.");
    }

    /// <summary>A control's new value: a real integer in the control's range, while the control is manual.</summary>
    private void SetControl(CameraControl control, JgsValue value, DeviceCall call)
    {
        string range = string.Create(CultureInfo.InvariantCulture, $"{control.Minimum} to {control.Maximum}");
        bool numeric = (DeviceChecks.NumericClasses.Contains(DeviceChecks.ClassOf(value)) || value.Type == JgsType.Bool)
            && DeviceChecks.Count(value) == 1 && !JgsBuiltins.HasComplexPart(value);
        double x = numeric ? DeviceChecks.Numbers(value).First() : double.NaN;
        if (!(x == Math.Floor(x) && x >= control.Minimum && x <= control.Maximum))
        {
            throw call.Error("MATLAB:webcam:invalidValue", $"{control.Name} must be a whole number from {range}.");
        }

        if (control.HasAuto && Read(control, call).Auto)
        {
            throw call.Error("MATLAB:webcam:controlIsAuto",
                $"{control.Name} is driven by the camera while {control.Name}Mode is 'auto'. Set {control.Name}Mode to 'manual' first.");
        }

        Write(control, (int)x, auto: false, call);
    }

    private void SetMode(CameraControl control, JgsValue value, DeviceCall call)
    {
        string mode = DeviceChecks.ValidateString(TransportClient.Str2Char(value), ["auto", "manual"],
            new DeviceChecks.Subject("webcam", control.Name + "Mode"), call.Line, call.Column);
        Write(control, Read(control, call).Value, auto: mode == "auto", call);
    }

    private void Write(CameraControl control, int value, bool auto, DeviceCall call)
    {
        try
        {
            _camera.WriteControl(control.Name, value, auto);
        }
        catch (CameraException e)
        {
            throw call.Error("MATLAB:webcam:controlFailed", e.Message);
        }
    }

    private static Dictionary<string, DeviceMethodBody> Methods() => new(StringComparer.Ordinal)
    {
        ["snapshot"] = static c =>
        {
            NoArguments(c);
            if (c.Wanted > 2)
            {
                throw c.Error("MATLAB:TooManyOutputs", "Too many output arguments.");
            }

            CameraFrame frame = Me(c.Target).Frame(c);
            return [WebcamShared.Picture(frame), JgsBuiltins.DatetimeValue(frame.Time)];
        },
        ["preview"] = static c =>
        {
            NoArguments(c);
            Me(c.Target).Preview(c);
            return [];
        },
        ["closePreview"] = static c =>
        {
            NoArguments(c);
            c.Target.LiveOrThrow(c.Line, c.Column);
            Me(c.Target).ClosePreview();
            return [];
        },
    };

    private static void NoArguments(DeviceCall call)
    {
        if (call.Args.Count > 0)
        {
            throw call.Error("MATLAB:TooManyInputs", "Too many input arguments.");
        }
    }

    /// <summary>The next frame the camera sends, or the refusal for a camera that sends none.</summary>
    private CameraFrame Frame(DeviceCall call)
    {
        LiveOrThrow(call.Line, call.Column);
        try
        {
            return _camera.NextFrame(FrameTimeout);
        }
        catch (TimeoutException)
        {
            throw call.Error("MATLAB:webcam:timeout", "Timeout occurred while trying to get a frame from the webcam.");
        }
        catch (CameraException e)
        {
            throw call.Error("MATLAB:webcam:deviceLost", e.Message);
        }
    }

    // --- preview -----------------------------------------------------------------------------------------

    /// <summary>Whether the preview's figure is still open (a person may have closed its window).</summary>
    private bool Previewing => _previewFigure is { } figure && JG.GetFigureNumber(figure) > 0;

    /// <summary>
    /// <c>preview(cam)</c>: a figure of its own showing the camera's frames, titled with its name. The
    /// frames reach it on the script thread, at the points where callbacks run, so it moves while the
    /// session is idle or pausing and holds still while a script computes.
    /// </summary>
    private void Preview(DeviceCall call)
    {
        if (Previewing)
        {
            return;
        }

        CameraFrame frame = Frame(call);
        int current = JG.CurrentFigureNumberOrZero;

        // A number out of a script's way: figure(1) and a bare figure never pick the preview's.
        int number = FirstPreviewNumber;
        while (JG.TryGetFigure(number, out _))
        {
            number++;
        }

        _previewFigure = JG.Figure(number);
        Draw(frame);
        Restore(current);
        _camera.FrameArrived += _onFrame;
    }

    /// <summary>Puts back the figure a script was drawing into, or none: the preview is never left current.</summary>
    private static void Restore(int current)
    {
        if (current > 0 && JG.TryGetFigure(current, out _))
        {
            JG.Figure(current);
        }
        else
        {
            JG.ClearCurrentFigure();
        }
    }

    private void Draw(CameraFrame frame)
    {
        _previewPlot = JG.RgbImage(WebcamShared.Argb(frame), frame.Width, frame.Height);
        JgsBuiltins.StyleImageAxes(JG.Gca());
        JG.Gca().Title = _camera.Name;
        _previewShown = frame.Number;
    }

    /// <summary>On the camera's thread: asks the script thread to show the newest frame, one request at a time.</summary>
    private void OnFrame()
    {
        // At most twenty-five requests a second; a request a finished run dropped is made again.
        long now = Environment.TickCount64;
        long since = now - Interlocked.Read(ref _previewPosted);
        if (since < 40 || (Volatile.Read(ref _previewPending) == 1 && since < 500))
        {
            return;
        }

        Volatile.Write(ref _previewPending, 1);
        Interlocked.Exchange(ref _previewPosted, now);
        _queue.Post(ShowLatest);
    }

    private void ShowLatest()
    {
        Volatile.Write(ref _previewPending, 0);
        if (Deleted || _previewFigure is null)
        {
            return;
        }

        if (!Previewing)
        {
            ClosePreview();
            return;
        }

        CameraFrame? frame = _camera.Latest();
        if (frame is null || frame.Number == _previewShown || _previewPlot is not { } plot)
        {
            return;
        }

        if (frame.Width == plot.Width && frame.Height == plot.Height)
        {
            plot.SetPixels(WebcamShared.Argb(frame));
            _previewShown = frame.Number;
            return;
        }

        // The resolution changed under the preview: the figure is drawn again at the new size.
        int current = JG.CurrentFigureNumberOrZero;
        int number = JG.GetFigureNumber(_previewFigure);
        JG.Clf(number);
        Draw(frame);
        Restore(current == number ? 0 : current);
    }

    /// <summary><c>closePreview(cam)</c>: stops the frames and closes the figure, if it is still open.</summary>
    private void ClosePreview()
    {
        _camera.FrameArrived -= _onFrame;
        FigureModel? figure = _previewFigure;
        _previewFigure = null;
        _previewPlot = null;
        if (figure is not null && JG.GetFigureNumber(figure) is > 0 and int number)
        {
            Session.Host.CloseFigure(number);
        }
    }

    // --- display and lifetime ------------------------------------------------------------------------------

    /// <summary>Every property, as R2025b's display of a webcam lists them; there is no shorter form.</summary>
    protected override string ShortDisplay()
    {
        var sb = new StringBuilder("webcam with properties:\n");
        var call = new DeviceCall { Target = this, Args = [] };
        foreach (DeviceProperty property in _class.Properties)
        {
            string shown;
            try
            {
                shown = Shown(property.Get(this, call));
            }
            catch (JgsException)
            {
                shown = "?";
            }

            sb.Append("\n    ").Append(property.Name).Append(": ").Append(shown);
        }

        return sb.ToString();
    }

    protected override void OnDelete()
    {
        try
        {
            ClosePreview();
        }
        finally
        {
            _camera.Dispose();
        }
    }
}
