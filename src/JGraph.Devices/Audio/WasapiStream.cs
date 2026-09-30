using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Devices.Audio;

/// <summary>
/// One WASAPI stream in shared mode (device classes plan, stage D10, ADR 0193), rendering or capturing
/// 32-bit float at the rate asked, which the audio engine converts to the device's
/// (AUTOCONVERTPCM with its default-quality resampler). Each start runs on a thread of its own in the
/// MTA, which owns every Core Audio object it makes; a failure to open is thrown from Start.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WasapiStream : IAudioOutputStream, IAudioInputStream
{
    private const long BufferDuration = 1_000_000; // 100 ms, in 100-ns units

    private readonly AudioDeviceInfo _device;
    private readonly int _rate;
    private readonly int _channels;
    private readonly bool _render;
    private Thread? _thread;
    private volatile bool _stop;
    private long _played;

    public WasapiStream(AudioDeviceInfo device, int sampleRate, int channels, bool render)
    {
        _device = device;
        _rate = sampleRate;
        _channels = channels;
        _render = render;
    }

    public long FramesPlayed => Interlocked.Read(ref _played);

    public bool Running => _thread is { IsAlive: true } && !_stop;

    public void Start(float[] interleaved, int frames, Action done)
    {
        Interlocked.Exchange(ref _played, 0);
        Run(() => Render(interleaved, frames, done));
    }

    public void Start(Action<float[], int> frames, Action<Exception> failed) => Run(() => Capture(frames, failed));

    private Exception? _failure;
    private ManualResetEventSlim? _ready;

    private void Run(Action body)
    {
        Stop();
        _stop = false;
        _failure = null;
        _ready = new ManualResetEventSlim();
        _thread = new Thread(() => body()) { IsBackground = true, Name = _render ? "JGraph audio output" : "JGraph audio input" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _ready.Wait();
        if (_failure is { } failure)
        {
            _thread.Join();
            throw failure;
        }
    }

    private IAudioClient Open(uint flow)
    {
        IMMDeviceEnumerator enumerator = WasapiAudio.CreateEnumerator();
        IMMDevice device;
        int hr = _device.EndpointId is { } id ? enumerator.GetDevice(id, out device) : enumerator.GetDefaultAudioEndpoint((int)flow, AudioNative.eConsole, out device);
        if (hr != 0)
        {
            throw new DeviceOpenException($"The audio device {_device.Name} is not available (0x{hr:X8}).", hr);
        }

        Guid iid = typeof(IAudioClient).GUID;
        hr = device.Activate(ref iid, AudioNative.CLSCTX_ALL, 0, out object activated);
        if (hr != 0)
        {
            throw new DeviceOpenException($"The audio device {_device.Name} could not be opened (0x{hr:X8}).", hr);
        }

        var client = (IAudioClient)activated;
        AudioNative.WAVEFORMATEXTENSIBLE format = AudioNative.FloatFormat(_rate, _channels);
        hr = client.Initialize(0, AudioNative.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AudioNative.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
            BufferDuration, 0, &format, 0);
        if (hr != 0)
        {
            throw new DeviceOpenException($"The audio device {_device.Name} refused {_channels} channel(s) at {_rate} Hz (0x{hr:X8}).", hr);
        }

        return client;
    }

    private void Render(float[] data, int frames, Action done)
    {
        IAudioClient? client = null;
        try
        {
            client = Open(AudioNative.eRender);
            client.GetBufferSize(out uint bufferFrames);
            Guid iid = typeof(IAudioRenderClient).GUID;
            client.GetService(ref iid, out object service);
            var render = (IAudioRenderClient)service;
            long written = 0;
            void Fill(long room)
            {
                long count = Math.Min(room, frames - written);
                if (count <= 0 || render.GetBuffer((uint)count, out nint buffer) != 0)
                {
                    return;
                }

                new ReadOnlySpan<float>(data, (int)(written * _channels), (int)(count * _channels)).CopyTo(new Span<float>((void*)buffer, (int)(count * _channels)));
                render.ReleaseBuffer((uint)count, 0);
                written += count;
            }

            Fill(bufferFrames);
            client.Start();
            _ready!.Set();
            bool finished = false;
            while (!_stop)
            {
                Thread.Sleep(5);
                if (client.GetCurrentPadding(out uint padding) != 0)
                {
                    break; // the device went away
                }

                Interlocked.Exchange(ref _played, written - padding);
                if (written >= frames)
                {
                    if (padding == 0)
                    {
                        finished = true;
                        break;
                    }

                    continue;
                }

                Fill(bufferFrames - padding);
            }

            client.Stop();
            if (!_stop)
            {
                _stop = true;
                if (finished || written >= frames)
                {
                    Interlocked.Exchange(ref _played, frames);
                }

                done();
            }
        }
        catch (Exception e) when (!_ready!.IsSet)
        {
            _failure = e is DeviceOpenException ? e : new DeviceOpenException(e.Message);
            _ready.Set();
        }
        catch (Exception)
        {
            // A failure mid-stream ends it; the object sees Running go false.
            _stop = true;
        }
        finally
        {
            Release(client);
        }
    }

    private void Capture(Action<float[], int> deliver, Action<Exception> failed)
    {
        IAudioClient? client = null;
        try
        {
            client = Open(AudioNative.eCapture);
            Guid iid = typeof(IAudioCaptureClient).GUID;
            client.GetService(ref iid, out object service);
            var capture = (IAudioCaptureClient)service;
            client.Start();
            _ready!.Set();
            while (!_stop)
            {
                Thread.Sleep(5);
                while (!_stop && capture.GetNextPacketSize(out uint packet) == 0 && packet > 0)
                {
                    if (capture.GetBuffer(out nint buffer, out uint frames, out uint flags, out _, out _) != 0)
                    {
                        break;
                    }

                    float[] block = new float[frames * _channels];
                    if ((flags & AudioNative.AUDCLNT_BUFFERFLAGS_SILENT) == 0)
                    {
                        new ReadOnlySpan<float>((void*)buffer, block.Length).CopyTo(block);
                    }

                    capture.ReleaseBuffer(frames);
                    deliver(block, (int)frames);
                }
            }

            client.Stop();
        }
        catch (Exception e) when (!_ready!.IsSet)
        {
            _failure = e is DeviceOpenException ? e : new DeviceOpenException(e.Message);
            _ready.Set();
        }
        catch (Exception e)
        {
            _stop = true;
            failed(e);
        }
        finally
        {
            Release(client);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.FinalReleaseComObject(com);
        }
    }

    public void Stop()
    {
        _stop = true;
        if (_thread is { } thread && thread != Thread.CurrentThread)
        {
            thread.Join();
        }
    }

    public void Dispose() => Stop();
}
