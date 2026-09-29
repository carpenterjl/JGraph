using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using static JGraph.Devices.Serial.CommNative;

namespace JGraph.Devices.Serial;

/// <summary>
/// A serial port over the Win32 communications API: the handle opened overlapped, one reader thread
/// that fills <see cref="Input"/>, one event thread that watches for breaks, pin changes and loss of
/// the device, and writes made on the caller's thread with a timeout and a cancellation token.
/// </summary>
/// <remarks>
/// The reader uses the timeouts that make <c>ReadFile</c> return as soon as one byte is there
/// (<c>ReadIntervalTimeout</c> and the multiplier at <c>MAXDWORD</c>, a constant of 100 ms), so bytes
/// reach the buffer as they arrive, and the thread wakes ten times a second to see whether it should
/// stop. A read or wait failing with an error that means the device went away (an unplugged USB
/// adapter answers <c>ERROR_ACCESS_DENIED</c>, <c>ERROR_BAD_COMMAND</c>, <c>ERROR_GEN_FAILURE</c> or
/// <c>ERROR_DEVICE_REMOVED</c>) ends the connection once: the buffer is faulted and
/// <see cref="ConnectionLost"/> raised.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed unsafe class Win32SerialPort : ISerialTransport
{
    private readonly object _writeGate = new();
    private SafeFileHandle? _handle;
    private Thread? _reader;
    private Thread? _events;
    private nint _stop;
    private volatile bool _closing;
    private int _lost;
    private long _written;
    private int _breaks;
    private bool _rts;
    private bool _dtr;

    private Win32SerialPort(string name)
    {
        Name = name;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public bool Connected => _handle is { IsInvalid: false, IsClosed: false } && Volatile.Read(ref _lost) == 0 && !_closing;

    /// <inheritdoc/>
    public InputBuffer Input { get; } = new();

    /// <inheritdoc/>
    public long BytesWritten => Interlocked.Read(ref _written);

    /// <inheritdoc/>
    public SerialSettings Settings { get; private set; } = new();

    /// <inheritdoc/>
    public bool Rts => _rts;

    /// <inheritdoc/>
    public bool Dtr => _dtr;

    /// <inheritdoc/>
    public event Action<DeviceConnectionLostException>? ConnectionLost;

    /// <inheritdoc/>
    public event Action<int>? BreakReceived;

    /// <summary>
    /// Opens <paramref name="port"/> (<c>COM3</c>, or a <c>\\.\</c> path) with <paramref name="settings"/>,
    /// raising DTR and RTS as <paramref name="dtr"/> and <paramref name="rts"/> say.
    /// </summary>
    public static Win32SerialPort Open(string port, SerialSettings settings, bool dtr = true, bool rts = true)
    {
        var serial = new Win32SerialPort(port);
        serial.OpenCore(settings, dtr, rts);
        return serial;
    }

    private void OpenCore(SerialSettings settings, bool dtr, bool rts)
    {
        string path = Name.StartsWith(@"\\.\", StringComparison.Ordinal) ? Name : @"\\.\" + Name;
        SafeFileHandle handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, 0, 0, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new DeviceOpenException(error switch
            {
                ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND => $"The port {Name} does not exist.",
                ERROR_ACCESS_DENIED => $"The port {Name} is in use.",
                _ => $"The port {Name} could not be opened: {new Win32Exception(error).Message}",
            }, error);
        }

        _handle = handle;
        try
        {
            SetupComm(handle, 65536, 65536);
            var timeouts = new COMMTIMEOUTS
            {
                ReadIntervalTimeout = uint.MaxValue,
                ReadTotalTimeoutMultiplier = uint.MaxValue,
                ReadTotalTimeoutConstant = 100,
            };
            if (!SetCommTimeouts(handle, ref timeouts))
            {
                throw new DeviceOpenException($"The port {Name} refused its timeouts: {LastError()}", Marshal.GetLastPInvokeError());
            }

            _dtr = dtr;
            _rts = rts;
            ApplyCore(settings);
            PurgeComm(handle, PURGE_RXCLEAR | PURGE_TXCLEAR);
        }
        catch (DeviceSettingsException refused)
        {
            handle.Dispose();
            _handle = null;
            throw new DeviceOpenException(refused.Message, refused.Win32Error);
        }
        catch
        {
            handle.Dispose();
            _handle = null;
            throw;
        }

        _stop = CreateEvent(0, true, false, 0);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"JGraph serial reader {Name}" };
        _reader.Start();
        _events = new Thread(EventLoop) { IsBackground = true, Name = $"JGraph serial events {Name}" };
        _events.Start();
    }

    /// <inheritdoc/>
    public void Apply(SerialSettings settings)
    {
        ThrowIfClosed();
        ApplyCore(settings);
    }

    private void ApplyCore(SerialSettings settings)
    {
        SafeFileHandle handle = _handle!;
        var dcb = new DCB { DCBlength = (uint)sizeof(DCB) };
        if (!GetCommState(handle, ref dcb))
        {
            throw new DeviceSettingsException($"The settings of {Name} could not be read: {LastError()}", Marshal.GetLastPInvokeError());
        }

        dcb.BaudRate = (uint)settings.BaudRate;
        dcb.ByteSize = (byte)settings.DataBits;
        dcb.Parity = settings.Parity switch
        {
            SerialParity.Odd => ODDPARITY,
            SerialParity.Even => EVENPARITY,
            SerialParity.Mark => MARKPARITY,
            SerialParity.Space => SPACEPARITY,
            _ => NOPARITY,
        };
        dcb.StopBits = settings.StopBits switch
        {
            SerialStopBits.OnePointFive => ONE5STOPBITS,
            SerialStopBits.Two => TWOSTOPBITS,
            _ => ONESTOPBIT,
        };
        dcb.SetBit(DCB.fBinary, true);
        dcb.SetBit(DCB.fParity, settings.Parity != SerialParity.None);
        dcb.SetBit(DCB.fOutxDsrFlow, false);
        dcb.SetBit(DCB.fDsrSensitivity, false);
        dcb.SetBit(DCB.fErrorChar, false);
        dcb.SetBit(DCB.fNull, false);
        dcb.SetBit(DCB.fAbortOnError, false);
        dcb.SetField(DCB.fDtrControl, _dtr ? DTR_CONTROL_ENABLE : DTR_CONTROL_DISABLE);
        bool hardware = settings.FlowControl == SerialFlowControl.Hardware;
        bool software = settings.FlowControl == SerialFlowControl.Software;
        dcb.SetBit(DCB.fOutxCtsFlow, hardware);
        dcb.SetField(DCB.fRtsControl, hardware ? RTS_CONTROL_HANDSHAKE : _rts ? RTS_CONTROL_ENABLE : RTS_CONTROL_DISABLE);
        dcb.SetBit(DCB.fOutX, software);
        dcb.SetBit(DCB.fInX, software);
        dcb.SetBit(DCB.fTXContinueOnXoff, true);
        dcb.XonChar = 0x11;
        dcb.XoffChar = 0x13;
        dcb.XonLim = 2048;
        dcb.XoffLim = 512;
        if (!SetCommState(handle, ref dcb))
        {
            int error = Marshal.GetLastPInvokeError();
            throw new DeviceSettingsException($"The port {Name} refused the settings: {new Win32Exception(error).Message}", error);
        }

        Settings = settings;
    }

    /// <inheritdoc/>
    public void SetRts(bool on)
    {
        ThrowIfClosed();
        if (!EscapeCommFunction(_handle!, on ? SETRTS : CLRRTS))
        {
            throw Failure("RTS could not be set");
        }

        _rts = on;
    }

    /// <inheritdoc/>
    public void SetDtr(bool on)
    {
        ThrowIfClosed();
        if (!EscapeCommFunction(_handle!, on ? SETDTR : CLRDTR))
        {
            throw Failure("DTR could not be set");
        }

        _dtr = on;
    }

    /// <inheritdoc/>
    public SerialPins GetPins()
    {
        ThrowIfClosed();
        if (!GetCommModemStatus(_handle!, out uint status))
        {
            throw Failure("the pin status could not be read");
        }

        return new SerialPins((status & MS_CTS_ON) != 0, (status & MS_DSR_ON) != 0, (status & MS_RLSD_ON) != 0, (status & MS_RING_ON) != 0);
    }

    /// <inheritdoc/>
    public void SendBreak(int milliseconds, CancellationToken cancel)
    {
        ThrowIfClosed();
        if (!EscapeCommFunction(_handle!, SETBREAK))
        {
            throw Failure("the break could not be sent");
        }

        try
        {
            cancel.WaitHandle.WaitOne(Math.Max(0, milliseconds));
        }
        finally
        {
            EscapeCommFunction(_handle!, CLRBREAK);
        }

        cancel.ThrowIfCancellationRequested();
    }

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<byte> data, TimeSpan timeout, CancellationToken cancel)
    {
        ThrowIfClosed();
        if (data.IsEmpty)
        {
            return;
        }

        lock (_writeGate)
        {
            nint signal = CreateEvent(0, true, false, 0);
            var overlapped = (OVERLAPPED*)NativeMemory.AllocZeroed((nuint)sizeof(OVERLAPPED));
            byte* buffer = (byte*)NativeMemory.Alloc((nuint)data.Length);
            bool pending = false;
            try
            {
                data.CopyTo(new Span<byte>(buffer, data.Length));
                overlapped->hEvent = signal;
                uint done;
                if (!WriteFile(_handle!, buffer, (uint)data.Length, &done, overlapped))
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error != ERROR_IO_PENDING)
                    {
                        throw WriteFailure(error);
                    }

                    pending = true;
                    long deadline = timeout == Timeout.InfiniteTimeSpan ? long.MaxValue : Environment.TickCount64 + (long)timeout.TotalMilliseconds;
                    while (true)
                    {
                        uint waited = WaitForSingleObject(signal, 50);
                        if (waited == WAIT_OBJECT_0)
                        {
                            break;
                        }

                        if (cancel.IsCancellationRequested || Environment.TickCount64 >= deadline || !Connected)
                        {
                            CancelIoEx(_handle!, overlapped);
                            GetOverlappedResult(_handle!, overlapped, &done, true);
                            pending = false;
                            Interlocked.Add(ref _written, done);
                            cancel.ThrowIfCancellationRequested();
                            if (!Connected)
                            {
                                throw new DeviceConnectionLostException($"The port {Name} is no longer connected.");
                            }

                            throw new TimeoutException($"The write to {Name} did not finish in time; {done} of {data.Length} bytes were written.");
                        }
                    }

                    if (!GetOverlappedResult(_handle!, overlapped, &done, false))
                    {
                        pending = false;
                        throw WriteFailure(Marshal.GetLastPInvokeError());
                    }

                    pending = false;
                }

                Interlocked.Add(ref _written, done);
            }
            finally
            {
                if (pending)
                {
                    CancelIoEx(_handle!, overlapped);
                    uint ignored;
                    GetOverlappedResult(_handle!, overlapped, &ignored, true);
                }

                NativeMemory.Free(buffer);
                NativeMemory.Free(overlapped);
                CloseHandle(signal);
            }
        }
    }

    private Exception WriteFailure(int error)
    {
        if (IsGoneError(error))
        {
            Lose(error);
            return new DeviceConnectionLostException($"The port {Name} is no longer connected.", error);
        }

        return new DeviceIOException($"The write to {Name} failed: {new Win32Exception(error).Message}", error);
    }

    /// <inheritdoc/>
    public void FlushInput()
    {
        ThrowIfClosed();
        PurgeComm(_handle!, PURGE_RXCLEAR | PURGE_RXABORT);
        Input.Clear();
    }

    /// <inheritdoc/>
    public void FlushOutput()
    {
        ThrowIfClosed();
        PurgeComm(_handle!, PURGE_TXCLEAR | PURGE_TXABORT);
    }

    private void ReadLoop()
    {
        SafeFileHandle handle = _handle!;
        nint signal = CreateEvent(0, true, false, 0);
        var overlapped = (OVERLAPPED*)NativeMemory.AllocZeroed((nuint)sizeof(OVERLAPPED));
        const int Size = 16384;
        byte* buffer = (byte*)NativeMemory.Alloc(Size);
        nint* waits = stackalloc nint[2];
        waits[0] = signal;
        waits[1] = _stop;
        try
        {
            while (!_closing)
            {
                ResetEvent(signal);
                *overlapped = default;
                overlapped->hEvent = signal;
                uint got = 0;
                if (!ReadFile(handle, buffer, Size, &got, overlapped))
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error != ERROR_IO_PENDING)
                    {
                        if (!_closing && IsGoneError(error))
                        {
                            Lose(error);
                        }

                        if (_closing || Volatile.Read(ref _lost) != 0)
                        {
                            return;
                        }

                        Thread.Sleep(20);
                        continue;
                    }

                    uint which = WaitForMultipleObjects(2, waits, false, INFINITE);
                    if (which != WAIT_OBJECT_0)
                    {
                        CancelIoEx(handle, overlapped);
                        GetOverlappedResult(handle, overlapped, &got, true);
                        if (got > 0)
                        {
                            Input.Append(new ReadOnlySpan<byte>(buffer, (int)got));
                        }

                        return;
                    }

                    if (!GetOverlappedResult(handle, overlapped, &got, false))
                    {
                        int failed = Marshal.GetLastPInvokeError();
                        if (!_closing && IsGoneError(failed))
                        {
                            Lose(failed);
                            return;
                        }

                        continue;
                    }
                }

                if (got > 0)
                {
                    Input.Append(new ReadOnlySpan<byte>(buffer, (int)got));
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // The handle was closed under the loop: the port is closing.
        }
        finally
        {
            NativeMemory.Free(buffer);
            NativeMemory.Free(overlapped);
            CloseHandle(signal);
        }
    }

    private void EventLoop()
    {
        SafeFileHandle handle = _handle!;
        nint signal = CreateEvent(0, true, false, 0);
        var overlapped = (OVERLAPPED*)NativeMemory.AllocZeroed((nuint)sizeof(OVERLAPPED));
        uint* mask = (uint*)NativeMemory.AllocZeroed(sizeof(uint));
        nint* waits = stackalloc nint[2];
        waits[0] = signal;
        waits[1] = _stop;
        try
        {
            if (!SetCommMask(handle, EV_BREAK | EV_ERR))
            {
                return;
            }

            while (!_closing)
            {
                ResetEvent(signal);
                *overlapped = default;
                overlapped->hEvent = signal;
                *mask = 0;
                if (!WaitCommEvent(handle, mask, overlapped))
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error != ERROR_IO_PENDING)
                    {
                        if (!_closing && IsGoneError(error))
                        {
                            Lose(error);
                            return;
                        }

                        if (_closing)
                        {
                            return;
                        }

                        Thread.Sleep(50);
                        continue;
                    }

                    uint which = WaitForMultipleObjects(2, waits, false, INFINITE);
                    uint ignored;
                    if (which != WAIT_OBJECT_0)
                    {
                        CancelIoEx(handle, overlapped);
                        GetOverlappedResult(handle, overlapped, &ignored, true);
                        return;
                    }

                    if (!GetOverlappedResult(handle, overlapped, &ignored, false))
                    {
                        int failed = Marshal.GetLastPInvokeError();
                        if (!_closing && IsGoneError(failed))
                        {
                            Lose(failed);
                            return;
                        }

                        continue;
                    }
                }

                ClearCommError(handle, out uint errors, out _);
                if ((*mask & EV_BREAK) != 0 || (errors & CE_BREAK) != 0)
                {
                    try
                    {
                        BreakReceived?.Invoke(Interlocked.Increment(ref _breaks));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            NativeMemory.Free(mask);
            NativeMemory.Free(overlapped);
            CloseHandle(signal);
        }
    }

    private static bool IsGoneError(int error) => error is ERROR_ACCESS_DENIED or ERROR_BAD_COMMAND or ERROR_GEN_FAILURE
        or ERROR_DEVICE_REMOVED or ERROR_NO_SUCH_DEVICE or ERROR_DEVICE_NOT_CONNECTED or ERROR_INVALID_HANDLE;

    private void Lose(int error)
    {
        if (Interlocked.Exchange(ref _lost, 1) != 0)
        {
            return;
        }

        var lost = new DeviceConnectionLostException($"The port {Name} is no longer connected ({new Win32Exception(error).Message}).", error);
        Input.SetFault(lost);
        if (_stop != 0)
        {
            SetEvent(_stop);
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ConnectionLost?.Invoke(lost);
            }
            catch (Exception)
            {
                // A thread-pool thread must not end the process.
            }
        });
    }

    private void ThrowIfClosed()
    {
        if (_handle is null || _handle.IsClosed || _closing)
        {
            throw new ObjectDisposedException(Name);
        }

        if (Volatile.Read(ref _lost) != 0)
        {
            throw new DeviceConnectionLostException($"The port {Name} is no longer connected.");
        }
    }

    private DeviceIOException Failure(string what)
    {
        int error = Marshal.GetLastPInvokeError();
        if (IsGoneError(error))
        {
            Lose(error);
        }

        return new DeviceIOException($"On {Name}, {what}: {new Win32Exception(error).Message}", error);
    }

    private static string LastError() => new Win32Exception(Marshal.GetLastPInvokeError()).Message;

    /// <summary>Stops the threads and closes the port.</summary>
    public void Dispose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        if (_stop != 0)
        {
            SetEvent(_stop);
        }

        if (_handle is { IsInvalid: false } handle)
        {
            SetCommMask(handle, 0); // ends a pending WaitCommEvent
            CancelIoEx(handle, null);
        }

        _reader?.Join(2000);
        _events?.Join(2000);
        _handle?.Dispose();
        if (_stop != 0)
        {
            CloseHandle(_stop);
            _stop = 0;
        }
    }
}
