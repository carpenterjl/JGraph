using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Devices.Audio;

/// <summary>
/// An audio device as audiodevinfo lists it: its ID, its name, whether it records, how many channels
/// it has, and the Core Audio endpoint it stands for (null for the primary driver, which follows the
/// default device).
/// </summary>
public sealed record AudioDeviceInfo(int Id, string Name, bool Input, int Channels, string? EndpointId);

/// <summary>An output stream: plays one block of interleaved float frames from its start.</summary>
public interface IAudioOutputStream : IDisposable
{
    /// <summary>Starts playing <paramref name="frames"/> frames of <paramref name="interleaved"/>; <paramref name="done"/> runs on a device thread once the last has played.</summary>
    void Start(float[] interleaved, int frames, Action done);

    /// <summary>Stops at once; what was played so far stays counted.</summary>
    void Stop();

    /// <summary>Frames the device has played since <see cref="Start"/>.</summary>
    long FramesPlayed { get; }

    bool Running { get; }
}

/// <summary>An input stream: hands each block it captures to a callback on a device thread.</summary>
public interface IAudioInputStream : IDisposable
{
    /// <summary>Starts capturing; <paramref name="frames"/> hears interleaved float frames and their count.</summary>
    void Start(Action<float[], int> frames, Action<Exception> failed);

    void Stop();

    bool Running { get; }
}

/// <summary>What audiodevinfo, audioplayer and audiorecorder stand on: the machine's devices, or a simulation.</summary>
public interface IAudioBackend
{
    /// <summary>Inputs first, then outputs, numbered from 0 in that order.</summary>
    IReadOnlyList<AudioDeviceInfo> Devices();

    /// <summary>Forgets the cached list (audiodevreset).</summary>
    void Reset();

    IAudioOutputStream OpenOutput(AudioDeviceInfo device, int sampleRate, int channels);

    IAudioInputStream OpenInput(AudioDeviceInfo device, int sampleRate, int channels);
}

/// <summary>
/// The machine's audio devices (device classes plan, stage D10, ADR 0193). They are named and numbered
/// as R2025b's PortAudio DirectSound host numbers them: DirectSound's capture list, then its playback
/// list, each led by its primary driver, and each name followed by " (Windows DirectSound)". Each maps
/// to its Core Audio endpoint through PKEY_AudioEndpoint_GUID, and the streams run on WASAPI in shared
/// mode, the audio engine converting the rate and format.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class WasapiAudio : IAudioBackend
{
    public const string HostApiName = "Windows DirectSound";

    public static readonly WasapiAudio Instance = new();

    private readonly object _gate = new();
    private IReadOnlyList<AudioDeviceInfo>? _devices;

    public IReadOnlyList<AudioDeviceInfo> Devices()
    {
        lock (_gate)
        {
            return _devices ??= Enumerate();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _devices = null;
        }
    }

    private static IReadOnlyList<AudioDeviceInfo> Enumerate()
    {
        // Every enumeration runs on a thread of its own in the MTA, where Core Audio wants to be.
        IReadOnlyList<AudioDeviceInfo> result = [];
        var thread = new Thread(() =>
        {
            try
            {
                result = EnumerateHere();
            }
            catch (Exception e) when (e is COMException or DllNotFoundException or EntryPointNotFoundException)
            {
                result = [];
            }
        });
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static IReadOnlyList<AudioDeviceInfo> EnumerateHere()
    {
        List<(Guid? Guid, string Description)> capture = DirectSound(input: true);
        List<(Guid? Guid, string Description)> render = DirectSound(input: false);
        var endpoints = new Dictionary<Guid, (string Id, int Channels)>();
        var defaults = new Dictionary<bool, int>();
        IMMDeviceEnumerator enumerator = CreateEnumerator();
        if (enumerator.EnumAudioEndpoints(2, AudioNative.DEVICE_STATE_ACTIVE, out IMMDeviceCollection collection) == 0)
        {
            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out IMMDevice device) != 0 || device.GetId(out string id) != 0)
                {
                    continue;
                }

                if (device.OpenPropertyStore(0, out IPropertyStore store) == 0
                    && StringProperty(store, AudioNative.PKEY_AudioEndpoint_GUID) is { } text && Guid.TryParse(text, out Guid guid))
                {
                    endpoints[guid] = (id, Channels(store));
                }
            }
        }

        foreach (bool input in new[] { true, false })
        {
            if (enumerator.GetDefaultAudioEndpoint(input ? AudioNative.eCapture : AudioNative.eRender, AudioNative.eConsole, out IMMDevice device) == 0
                && device.OpenPropertyStore(0, out IPropertyStore store) == 0)
            {
                defaults[input] = Channels(store);
            }
        }

        var devices = new List<AudioDeviceInfo>();
        foreach ((bool input, List<(Guid? Guid, string Description)> list) in new[] { (true, capture), (false, render) })
        {
            foreach ((Guid? guid, string description) in list)
            {
                (string? id, int channels) = guid is { } g && endpoints.TryGetValue(g, out (string Id, int Channels) found)
                    ? (found.Id, found.Channels)
                    : (null, defaults.TryGetValue(input, out int c) ? c : 2);
                if (guid is not null && id is null)
                {
                    continue; // a DirectSound device with no active endpoint behind it
                }

                devices.Add(new AudioDeviceInfo(devices.Count, $"{description} ({HostApiName})", input, Math.Max(1, channels), id));
            }
        }

        return devices;
    }

    private static List<(Guid? Guid, string Description)> DirectSound(bool input)
    {
        var list = new List<(Guid?, string)>();
        GCHandle handle = GCHandle.Alloc(list);
        try
        {
            if (input)
            {
                AudioNative.DirectSoundCaptureEnumerateW(&Collect, GCHandle.ToIntPtr(handle));
            }
            else
            {
                AudioNative.DirectSoundEnumerateW(&Collect, GCHandle.ToIntPtr(handle));
            }
        }
        finally
        {
            handle.Free();
        }

        return list;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Collect(Guid* guid, char* description, char* module, nint context)
    {
        try
        {
            var list = (List<(Guid?, string)>)GCHandle.FromIntPtr(context).Target!;
            list.Add((guid == null ? null : *guid, new string(description)));
        }
        catch (Exception)
        {
            // Nothing may escape into dsound.dll.
        }

        return 1;
    }

    internal static IMMDeviceEnumerator CreateEnumerator() =>
        (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(AudioNative.CLSID_MMDeviceEnumerator, throwOnError: true)!)!;

    private static string? StringProperty(IPropertyStore store, AudioNative.PROPERTYKEY key)
    {
        AudioNative.PROPVARIANT value = default;
        try
        {
            return store.GetValue(key, &value) == 0 && value.vt == AudioNative.VT_LPWSTR ? Marshal.PtrToStringUni(value.pointer) : null;
        }
        finally
        {
            AudioNative.PropVariantClear(&value);
        }
    }

    /// <summary>The device format's channel count (WAVEFORMATEX.nChannels), or 2.</summary>
    private static int Channels(IPropertyStore store)
    {
        AudioNative.PROPVARIANT value = default;
        try
        {
            return store.GetValue(AudioNative.PKEY_AudioEngine_DeviceFormat, &value) == 0 && value.vt == AudioNative.VT_BLOB && value.blobSize >= 4
                ? *(ushort*)(value.blobData + 2)
                : 2;
        }
        finally
        {
            AudioNative.PropVariantClear(&value);
        }
    }

    public IAudioOutputStream OpenOutput(AudioDeviceInfo device, int sampleRate, int channels) => new WasapiStream(device, sampleRate, channels, render: true);

    public IAudioInputStream OpenInput(AudioDeviceInfo device, int sampleRate, int channels) => new WasapiStream(device, sampleRate, channels, render: false);
}
