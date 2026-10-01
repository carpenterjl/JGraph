using JGraph.Devices.Video;

namespace JGraph.Devices.Simulation;

/// <summary>
/// Simulated cameras for tests and fixtures (device classes plan, stage D11, ADR 0195). "JGraph Test
/// Camera" has four resolutions and six controls, three of which it can drive itself; "JGraph Second
/// Camera" has one resolution and a Brightness. A frame is a known picture: red rises left to right,
/// green top to bottom, blue steps by eight with each frame, and Brightness above or below its default
/// lifts or lowers all three. No real camera is opened.
/// </summary>
public sealed class SimulatedCameras : ICameraBackend
{
    public const string FirstName = "JGraph Test Camera";
    public const string SecondName = "JGraph Second Camera";

    private readonly object _gate = new();
    private readonly HashSet<string> _unplugged = new(StringComparer.Ordinal);
    private readonly List<SimulatedCamera> _open = [];

    public IReadOnlyList<CameraInfo> Cameras()
    {
        lock (_gate)
        {
            var cameras = new List<CameraInfo>();
            if (!_unplugged.Contains(FirstName))
            {
                cameras.Add(new CameraInfo(FirstName, "sim:0"));
            }

            if (!_unplugged.Contains(SecondName))
            {
                cameras.Add(new CameraInfo(SecondName, "sim:1"));
            }

            return cameras;
        }
    }

    public ICamera Open(CameraInfo camera)
    {
        lock (_gate)
        {
            if (_unplugged.Contains(camera.Name))
            {
                throw new CameraException($"Opening the camera '{camera.Name}' failed: it is not connected.");
            }

            var opened = camera.Id == "sim:0"
                ? new SimulatedCamera(this, camera.Name,
                    [(640, 480), (320, 240), (160, 120), (1280, 720)],
                    [
                        new CameraControl("Brightness", 0, 255, 1, 128, false, true),
                        new CameraControl("Contrast", 0, 255, 1, 32, false, true),
                        new CameraControl("WhiteBalance", 2800, 6500, 1, 4600, true, true),
                        new CameraControl("Zoom", 100, 500, 1, 100, false, true),
                        new CameraControl("Exposure", -11, -2, 1, -5, true, true),
                        new CameraControl("Focus", 0, 250, 5, 0, true, true),
                    ])
                : new SimulatedCamera(this, camera.Name,
                    [(320, 240)],
                    [new CameraControl("Brightness", 0, 100, 1, 50, false, true)]);
            _open.Add(opened);
            return opened;
        }
    }

    /// <summary>How many cameras are open now.</summary>
    public int OpenCount
    {
        get
        {
            lock (_gate)
            {
                return _open.Count;
            }
        }
    }

    /// <summary>Unplugs a camera: it leaves the list, and an open one stops sending and refuses a read.</summary>
    public void Unplug(string name)
    {
        lock (_gate)
        {
            _unplugged.Add(name);
            foreach (SimulatedCamera camera in _open.Where(c => c.Name == name))
            {
                camera.Lose();
            }
        }
    }

    /// <summary>Plugs a camera back in.</summary>
    public void Plug(string name)
    {
        lock (_gate)
        {
            _unplugged.Remove(name);
        }
    }

    private void Closed(SimulatedCamera camera)
    {
        lock (_gate)
        {
            _open.Remove(camera);
        }
    }

    private sealed class SimulatedCamera : ICamera
    {
        private readonly object _gate = new();
        private readonly SimulatedCameras _owner;
        private readonly Dictionary<string, (int Value, bool Auto)> _values = new(StringComparer.Ordinal);
        private (int Width, int Height) _resolution;
        private long _number;
        private CameraFrame? _latest;
        private Action? _listeners;
        private Timer? _timer;
        private bool _lost;
        private bool _disposed;

        public SimulatedCamera(SimulatedCameras owner, string name, (int Width, int Height)[] resolutions, CameraControl[] controls)
        {
            _owner = owner;
            Name = name;
            Resolutions = resolutions;
            Controls = controls;
            _resolution = resolutions[0];
            foreach (CameraControl control in controls)
            {
                _values[control.Name] = (control.Default, control.HasAuto);
            }
        }

        public string Name { get; }

        public IReadOnlyList<(int Width, int Height)> Resolutions { get; }

        public IReadOnlyList<CameraControl> Controls { get; }

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

        /// <summary>A timer sends frames at thirty a second for as long as anyone listens.</summary>
        public event Action? FrameArrived
        {
            add
            {
                lock (_gate)
                {
                    _listeners += value;
                    if (!_disposed)
                    {
                        _timer ??= new Timer(_ => Tick(), null, 33, 33);
                    }
                }
            }

            remove
            {
                lock (_gate)
                {
                    _listeners -= value;
                    if (_listeners is null)
                    {
                        _timer?.Dispose();
                        _timer = null;
                    }
                }
            }
        }

        private void Tick()
        {
            Action? listeners;
            lock (_gate)
            {
                if (_disposed || _lost)
                {
                    return;
                }

                Make();
                listeners = _listeners;
            }

            try
            {
                listeners?.Invoke();
            }
            catch (Exception)
            {
                // A listener's failure must not end the frames.
            }
        }

        public void Lose()
        {
            lock (_gate)
            {
                _lost = true;
            }
        }

        public void SetResolution(int width, int height)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!Resolutions.Contains((width, height)))
                {
                    throw new CameraException($"The camera '{Name}' has no {width}x{height} format.");
                }

                _resolution = (width, height);
                _latest = null;
            }
        }

        public (int Value, bool Auto) ReadControl(string name)
        {
            lock (_gate)
            {
                return _values.TryGetValue(name, out (int Value, bool Auto) value)
                    ? value
                    : throw new CameraException($"The camera '{Name}' has no {name} control.");
            }
        }

        public void WriteControl(string name, int value, bool auto)
        {
            lock (_gate)
            {
                if (!_values.ContainsKey(name))
                {
                    throw new CameraException($"The camera '{Name}' has no {name} control.");
                }

                _values[name] = (value, auto);
            }
        }

        public CameraFrame NextFrame(TimeSpan timeout)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_lost)
                {
                    throw new CameraException($"Reading from the camera '{Name}' failed: it was disconnected or stopped sending.");
                }

                return Make();
            }
        }

        public CameraFrame? Latest()
        {
            lock (_gate)
            {
                return _disposed || _lost ? null : _latest ?? Make();
            }
        }

        private CameraFrame Make()
        {
            (int width, int height) = _resolution;
            _number++;
            byte[] pixels = new byte[width * height * 4];
            int lift = _values.TryGetValue("Brightness", out (int Value, bool Auto) brightness)
                ? brightness.Value - Controls.First(static c => c.Name == "Brightness").Default
                : 0;
            byte blue = Clamp((int)((_number * 8) & 255) + lift);
            for (int r = 0; r < height; r++)
            {
                byte green = Clamp((int)Math.Round(255.0 * r / (height - 1)) + lift);
                int row = r * width * 4;
                for (int c = 0; c < width; c++)
                {
                    int at = row + (c * 4);
                    pixels[at] = blue;
                    pixels[at + 1] = green;
                    pixels[at + 2] = Clamp((int)Math.Round(255.0 * c / (width - 1)) + lift);
                    pixels[at + 3] = 255;
                }
            }

            _latest = new CameraFrame(width, height, pixels, DateTime.Now, _number);
            return _latest;
        }

        private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _timer?.Dispose();
                _timer = null;
                _listeners = null;
            }

            _owner.Closed(this);
        }
    }
}
