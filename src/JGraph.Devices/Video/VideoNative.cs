using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Devices.Video;

/// <summary>
/// Media Foundation as the camera backend calls it (device classes plan, stage D11): the platform
/// functions, the attribute keys and format GUIDs, and the COM methods reached by their vtable slots.
/// The interfaces are called through raw pointers rather than runtime-callable wrappers, so that a
/// camera is released on the statement that deletes its object and its light goes out then. Every
/// slot number and GUID here is read from the Windows SDK's mfobjects.h, mfidl.h, mfreadwrite.h and
/// strmif.h (10.0.26100.0).
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class VideoNative
{
    public const uint MF_VERSION = 0x00020070;
    public const uint MF_SOURCE_READER_FIRST_VIDEO_STREAM = 0xFFFFFFFC;
    public const uint MF_SOURCE_READERF_ERROR = 0x1;
    public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x2;

    /// <summary>MF_E_NO_MORE_TYPES: the end of a stream's native media types.</summary>
    public const int MF_E_NO_MORE_TYPES = unchecked((int)0xC00D36B9);

    [LibraryImport("mfplat.dll")]
    public static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateAttributes(out nint attributes, uint initialSize);

    [LibraryImport("mfplat.dll")]
    public static partial int MFCreateMediaType(out nint mediaType);

    [LibraryImport("mf.dll")]
    public static partial int MFEnumDeviceSources(nint attributes, out nint activates, out uint count);

    [LibraryImport("mf.dll")]
    public static partial int MFCreateDeviceSource(nint attributes, out nint source);

    [LibraryImport("mfreadwrite.dll")]
    public static partial int MFCreateSourceReaderFromMediaSource(nint source, nint attributes, out nint reader);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(nint memory);

    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");
    public static readonly Guid MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK = new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f");
    public static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    public static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    public static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    public static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");
    public static readonly Guid MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING = new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d");
    public static readonly Guid MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING = new("0f81da2c-b537-4672-a8b2-a681b17307a3");
    public static readonly Guid IID_IMF2DBuffer = new("7DC9D5F9-9ED9-44ec-9BBF-0600BB589FBB");
    public static readonly Guid IID_IAMVideoProcAmp = new("C6E13360-30AC-11d0-A18C-00A0C9118956");
    public static readonly Guid IID_IAMCameraControl = new("C6E13370-30AC-11d0-A18C-00A0C9118956");

    private static nint Slot(nint com, int slot) => (*(nint**)com)[slot];

    // --- IUnknown -------------------------------------------------------------------------------------

    public static int QueryInterface(nint com, in Guid iid, out nint result)
    {
        fixed (Guid* id = &iid)
        fixed (nint* to = &result)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(com, 0))(com, id, to);
        }
    }

    public static void Release(ref nint com)
    {
        if (com != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(com, 2))(com);
            com = 0;
        }
    }

    public static void AddRef(nint com) => ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(com, 1))(com);

    // --- IMFAttributes (and IMFActivate, IMFMediaType, IMFSample, which begin with it) ---------------------

    public static int GetUINT32(nint attributes, in Guid key, out uint value)
    {
        fixed (Guid* k = &key)
        fixed (uint* v = &value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, uint*, int>)Slot(attributes, 7))(attributes, k, v);
        }
    }

    public static int GetUINT64(nint attributes, in Guid key, out ulong value)
    {
        fixed (Guid* k = &key)
        fixed (ulong* v = &value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, ulong*, int>)Slot(attributes, 8))(attributes, k, v);
        }
    }

    public static int GetGUID(nint attributes, in Guid key, out Guid value)
    {
        fixed (Guid* k = &key)
        fixed (Guid* v = &value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Slot(attributes, 10))(attributes, k, v);
        }
    }

    /// <summary>A string attribute, or null when the store has none.</summary>
    public static string? GetString(nint attributes, in Guid key)
    {
        fixed (Guid* k = &key)
        {
            nint text;
            uint length;
            int hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, uint*, int>)Slot(attributes, 13))(attributes, k, &text, &length);
            if (hr < 0)
            {
                return null;
            }

            string value = Marshal.PtrToStringUni(text, (int)length);
            CoTaskMemFree(text);
            return value;
        }
    }

    public static int SetUINT32(nint attributes, in Guid key, uint value)
    {
        fixed (Guid* k = &key)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, uint, int>)Slot(attributes, 21))(attributes, k, value);
        }
    }

    public static int SetUINT64(nint attributes, in Guid key, ulong value)
    {
        fixed (Guid* k = &key)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, ulong, int>)Slot(attributes, 22))(attributes, k, value);
        }
    }

    public static int SetGUID(nint attributes, in Guid key, in Guid value)
    {
        fixed (Guid* k = &key)
        fixed (Guid* v = &value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)Slot(attributes, 24))(attributes, k, v);
        }
    }

    public static int SetString(nint attributes, in Guid key, string value)
    {
        fixed (Guid* k = &key)
        fixed (char* v = value)
        {
            return ((delegate* unmanaged[Stdcall]<nint, Guid*, char*, int>)Slot(attributes, 25))(attributes, k, v);
        }
    }

    // --- IMFMediaSource --------------------------------------------------------------------------------

    public static int SourceShutdown(nint source) => ((delegate* unmanaged[Stdcall]<nint, int>)Slot(source, 12))(source);

    // --- IMFSourceReader -------------------------------------------------------------------------------

    public static int GetNativeMediaType(nint reader, uint stream, uint index, out nint mediaType)
    {
        fixed (nint* type = &mediaType)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, int>)Slot(reader, 5))(reader, stream, index, type);
        }
    }

    public static int GetCurrentMediaType(nint reader, uint stream, out nint mediaType)
    {
        fixed (nint* type = &mediaType)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(reader, 6))(reader, stream, type);
        }
    }

    public static int SetCurrentMediaType(nint reader, uint stream, nint mediaType) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint*, nint, int>)Slot(reader, 7))(reader, stream, null, mediaType);

    public static int ReadSample(nint reader, uint stream, out uint flags, out long timestamp, out nint sample)
    {
        uint actual;
        fixed (uint* f = &flags)
        fixed (long* t = &timestamp)
        fixed (nint* s = &sample)
        {
            return ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint*, uint*, long*, nint*, int>)Slot(reader, 9))(reader, stream, 0, &actual, f, t, s);
        }
    }

    // --- IMFSample, IMFMediaBuffer, IMF2DBuffer ------------------------------------------------------------

    public static int ConvertToContiguousBuffer(nint sample, out nint buffer)
    {
        fixed (nint* b = &buffer)
        {
            return ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(sample, 41))(sample, b);
        }
    }

    public static int BufferLock(nint buffer, out byte* data, out uint length)
    {
        uint maximum;
        fixed (byte** d = &data)
        fixed (uint* l = &length)
        {
            return ((delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, int>)Slot(buffer, 3))(buffer, d, &maximum, l);
        }
    }

    public static int BufferUnlock(nint buffer) => ((delegate* unmanaged[Stdcall]<nint, int>)Slot(buffer, 4))(buffer);

    public static int Lock2D(nint buffer2D, out byte* scanline0, out int pitch)
    {
        fixed (byte** s = &scanline0)
        fixed (int* p = &pitch)
        {
            return ((delegate* unmanaged[Stdcall]<nint, byte**, int*, int>)Slot(buffer2D, 3))(buffer2D, s, p);
        }
    }

    public static int Unlock2D(nint buffer2D) => ((delegate* unmanaged[Stdcall]<nint, int>)Slot(buffer2D, 4))(buffer2D);

    // --- IAMVideoProcAmp and IAMCameraControl, which share a layout ------------------------------------------

    public static int ControlRange(nint control, int property, out int minimum, out int maximum, out int step, out int standard, out int caps)
    {
        fixed (int* lo = &minimum)
        fixed (int* hi = &maximum)
        fixed (int* st = &step)
        fixed (int* df = &standard)
        fixed (int* cp = &caps)
        {
            return ((delegate* unmanaged[Stdcall]<nint, int, int*, int*, int*, int*, int*, int>)Slot(control, 3))(control, property, lo, hi, st, df, cp);
        }
    }

    public static int ControlSet(nint control, int property, int value, int flags) =>
        ((delegate* unmanaged[Stdcall]<nint, int, int, int, int>)Slot(control, 4))(control, property, value, flags);

    public static int ControlGet(nint control, int property, out int value, out int flags)
    {
        fixed (int* v = &value)
        fixed (int* f = &flags)
        {
            return ((delegate* unmanaged[Stdcall]<nint, int, int*, int*, int>)Slot(control, 5))(control, property, v, f);
        }
    }
}
