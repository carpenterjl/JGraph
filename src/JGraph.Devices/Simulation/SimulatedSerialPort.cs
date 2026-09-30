namespace JGraph.Devices.Simulation;

/// <summary>
/// A simulated serial line: the <see cref="PeerEngine"/> on its far end, and at most one open near end
/// at a time. The line outlives its near ends, as a com0com pair with <c>peer-sim.exe</c> on COM21
/// outlives the objects a fixture opens and clears on COM20: what the device logged, its echo and its
/// rules stay until it is told to reset.
/// </summary>
public sealed class SimulatedLine : IPeerLink, IDisposable
{
    private readonly object _gate = new();
    private SimulatedSerialPort? _near;
    private bool _farRts;
    private bool _farDtr;

    public SimulatedLine(string name)
    {
        Name = name;
        Engine = new PeerEngine(this);
    }

    /// <summary>The port name the near end opens as.</summary>
    public string Name { get; }

    /// <summary>The device on the far end.</summary>
    public PeerEngine Engine { get; }

    /// <summary>Whether a near end is open.</summary>
    public bool InUse
    {
        get
        {
            lock (_gate)
            {
                return _near is { Connected: true };
            }
        }
    }

    /// <summary>Opens the near end; a second open while one is open is refused as a held port is.</summary>
    public SimulatedSerialPort Open(SerialSettings settings, bool dtr = true, bool rts = true)
    {
        lock (_gate)
        {
            if (_near is { Connected: true })
            {
                throw new DeviceOpenException($"The port {Name} is in use.", 5);
            }

            var near = new SimulatedSerialPort(this, settings, dtr, rts);
            _near = near;
            return near;
        }
    }

    internal void Closed(SimulatedSerialPort near)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_near, near))
            {
                _near = null;
            }
        }
    }

    internal SerialPins NearPins => new(_farRts, _farDtr, _farDtr, false);

    /// <summary>Ends the open near end's connection as an unplugged USB adapter does; nothing when none is open.</summary>
    public void Unplug()
    {
        SimulatedSerialPort? near;
        lock (_gate)
        {
            near = _near;
        }

        near?.Unplug();
    }

    // --- the far end, as the engine sees it ---------------------------------------------------------

    void IPeerLink.Send(ReadOnlySpan<byte> bytes)
    {
        SimulatedSerialPort? near;
        lock (_gate)
        {
            near = _near;
        }

        near?.Deliver(bytes);
    }

    void IPeerLink.SetRts(bool on) => _farRts = on;

    void IPeerLink.SetDtr(bool on) => _farDtr = on;

    SerialPins IPeerLink.Pins
    {
        get
        {
            SimulatedSerialPort? near;
            lock (_gate)
            {
                near = _near;
            }

            return near is null ? default : new SerialPins(near.Rts, near.Dtr, near.Dtr, false);
        }
    }

    void IPeerLink.SendBreak(int milliseconds)
    {
        SimulatedSerialPort? near;
        lock (_gate)
        {
            near = _near;
        }

        near?.BreakFromFar();
    }

    public void Dispose() => Engine.Dispose();
}

/// <summary>
/// The near end of a <see cref="SimulatedLine"/>: a serial port wired as a com0com pair is wired (its
/// RTS is the far end's CTS, its DTR the far end's DSR and DCD, and the reverse), so a fixture reads
/// the same pins from it that R2025b reads through com0com. Bytes it writes reach the engine at once,
/// on the writing thread; what the engine sends lands in <see cref="Input"/> on the sending thread.
/// </summary>
public sealed class SimulatedSerialPort : ISerialTransport
{
    private readonly SimulatedLine _line;
    private bool _rts;
    private bool _dtr;
    private volatile bool _closed;
    private long _written;
    private int _breaks;

    internal SimulatedSerialPort(SimulatedLine line, SerialSettings settings, bool dtr, bool rts)
    {
        _line = line;
        _dtr = dtr;
        _rts = rts;
        Apply(settings);
    }

    public string Name => _line.Name;

    public bool Connected => !_closed;

    public InputBuffer Input { get; } = new();

    public long BytesWritten => Interlocked.Read(ref _written);

    public SerialSettings Settings { get; private set; } = new();

    public bool Rts => _rts;

    public bool Dtr => _dtr;

    public event Action<DeviceConnectionLostException>? ConnectionLost;

    public event Action<int>? BreakReceived;

    /// <summary>
    /// Takes what com0com takes (measured through R2025b): any baud rate above zero, 5 to 8 data bits,
    /// and any stop bits with any of them.
    /// </summary>
    public void Apply(SerialSettings settings)
    {
        if (settings.BaudRate <= 0 || settings.DataBits is < 5 or > 8)
        {
            throw new DeviceSettingsException($"The port {Name} refused the settings: The parameter is incorrect.", 87);
        }

        Settings = settings;
    }

    public void Write(ReadOnlySpan<byte> data, TimeSpan timeout, CancellationToken cancel)
    {
        ThrowIfClosed();
        cancel.ThrowIfCancellationRequested();
        Interlocked.Add(ref _written, data.Length);
        _line.Engine.Receive(data);
    }

    public void FlushInput()
    {
        ThrowIfClosed();
        Input.Clear();
    }

    public void FlushOutput() => ThrowIfClosed();

    public void SetRts(bool on)
    {
        ThrowIfClosed();

        // Under RTS/CTS handshaking the driver owns RTS and keeps it raised while there is room.
        if (Settings.FlowControl != SerialFlowControl.Hardware)
        {
            _rts = on;
        }
    }

    public void SetDtr(bool on)
    {
        ThrowIfClosed();
        _dtr = on;
    }

    public SerialPins GetPins()
    {
        ThrowIfClosed();
        return _line.NearPins;
    }

    public void SendBreak(int milliseconds, CancellationToken cancel)
    {
        ThrowIfClosed();
        cancel.WaitHandle.WaitOne(Math.Max(0, milliseconds));
        cancel.ThrowIfCancellationRequested();
        _line.Engine.BreakReceived();

        // A break reaches the far end's driver as a NUL, which com0com delivers as data (probe_visa_misc).
        _line.Engine.Receive([0]);
    }

    /// <summary>Ends the connection as an unplugged adapter does, for tests of the lost-connection path.</summary>
    public void Unplug()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _line.Closed(this);
        var lost = new DeviceConnectionLostException($"The port {Name} is no longer connected.", 22);
        Input.SetFault(lost);
        ConnectionLost?.Invoke(lost);
    }

    internal void Deliver(ReadOnlySpan<byte> bytes)
    {
        if (!_closed)
        {
            Input.Append(bytes);
        }
    }

    internal void BreakFromFar() => BreakReceived?.Invoke(Interlocked.Increment(ref _breaks));

    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _line.Closed(this);
    }

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw Input.Fault as DeviceConnectionLostException ?? (Exception)new ObjectDisposedException(Name);
        }
    }
}
