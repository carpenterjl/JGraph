using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Devices.Audio;

/// <summary>
/// dsound.dll's two enumerations, which name the devices the way R2025b's PortAudio DirectSound host
/// does, and the Core Audio (WASAPI) interfaces the streams run on (device classes plan, stage D10).
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class AudioNative
{
    [LibraryImport("dsound.dll")]
    public static partial int DirectSoundEnumerateW(delegate* unmanaged[Stdcall]<Guid*, char*, char*, nint, int> callback, nint context);

    [LibraryImport("dsound.dll")]
    public static partial int DirectSoundCaptureEnumerateW(delegate* unmanaged[Stdcall]<Guid*, char*, char*, nint, int> callback, nint context);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(PROPVARIANT* variant);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(nint memory);

    public static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    /// <summary>PKEY_AudioEndpoint_GUID: the endpoint's DirectSound device GUID, as text.</summary>
    public static readonly PROPERTYKEY PKEY_AudioEndpoint_GUID = new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 4);

    /// <summary>PKEY_AudioEngine_DeviceFormat: the device's own format, a WAVEFORMATEX blob.</summary>
    public static readonly PROPERTYKEY PKEY_AudioEngine_DeviceFormat = new(new Guid("F19F064D-082C-4E27-BC73-6882A1BB8E4C"), 0);

    public const int eRender = 0;
    public const int eCapture = 1;
    public const int eConsole = 0;
    public const uint DEVICE_STATE_ACTIVE = 1;
    public const uint CLSCTX_ALL = 23;

    public const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    public const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    public const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
    public const uint AUDCLNT_BUFFERFLAGS_SILENT = 2;
    public const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PROPERTYKEY(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    /// <summary>A PROPVARIANT, read as its type and the pointer or blob after it.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public nint pointer;
        [FieldOffset(8)] public uint blobSize;
        [FieldOffset(16)] public nint blobData;
    }

    public const ushort VT_LPWSTR = 31;
    public const ushort VT_BLOB = 65;

    /// <summary>WAVEFORMATEXTENSIBLE, the format a stream is opened with.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WAVEFORMATEXTENSIBLE
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
        public ushort wValidBitsPerSample;
        public uint dwChannelMask;
        public Guid SubFormat;
    }

    public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new("00000003-0000-0010-8000-00AA00389B71");

    /// <summary>32-bit float at <paramref name="rate"/> Hz: what every stream here is opened with, converted by the audio engine.</summary>
    public static WAVEFORMATEXTENSIBLE FloatFormat(int rate, int channels) => new()
    {
        wFormatTag = 0xFFFE,
        nChannels = (ushort)channels,
        nSamplesPerSec = (uint)rate,
        nAvgBytesPerSec = (uint)(rate * channels * 4),
        nBlockAlign = (ushort)(channels * 4),
        wBitsPerSample = 32,
        cbSize = 22,
        wValidBitsPerSample = 32,
        dwChannelMask = channels == 1 ? 0x4u : 0x3u,
        SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT,
    };
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(nint client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(nint client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [PreserveSig]
    int OpenPropertyStore(uint access, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out AudioNative.PROPERTYKEY key);

    [PreserveSig]
    int GetValue(in AudioNative.PROPERTYKEY key, AudioNative.PROPVARIANT* value);
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe interface IAudioClient
{
    [PreserveSig]
    int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, AudioNative.WAVEFORMATEXTENSIBLE* format, nint sessionGuid);

    [PreserveSig]
    int GetBufferSize(out uint frames);

    [PreserveSig]
    int GetStreamLatency(out long latency);

    [PreserveSig]
    int GetCurrentPadding(out uint frames);

    [PreserveSig]
    int IsFormatSupported(int shareMode, AudioNative.WAVEFORMATEXTENSIBLE* format, nint* closest);

    [PreserveSig]
    int GetMixFormat(out nint format);

    [PreserveSig]
    int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int SetEventHandle(nint eventHandle);

    [PreserveSig]
    int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    [PreserveSig]
    int GetBuffer(uint frames, out nint data);

    [PreserveSig]
    int ReleaseBuffer(uint frames, uint flags);
}

[ComImport]
[Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig]
    int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);

    [PreserveSig]
    int ReleaseBuffer(uint frames);

    [PreserveSig]
    int GetNextPacketSize(out uint frames);
}
