using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Imaging.Codecs;

/// <summary>
/// An MP4 carrying H.264, written through Media Foundation's sink writer.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0072 recorded that there was no <c>VideoWriter</c> because "a real video container needs a
/// codec this build does not carry", and that was true of a codec we would have had to ship. It is
/// not true of the one Windows already has: <c>mfreadwrite</c>'s sink writer is an H.264 encoder and
/// an MP4 muxer behind six calls, present on every Windows this application runs on. Nothing is
/// vendored and no binary is added — the frames go out through the operating system's own encoder.
/// </para>
/// <para>
/// The interop below declares each COM interface's vtable in full, because the CLR builds a vtable
/// from declaration order and a missing slot would silently call the wrong method. The slots this
/// encoder never uses are named for their position and left unmarshalled; that is deliberate, and
/// safe precisely because they are never called.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class Mpeg4VideoEncoder : IVideoEncoder
{
    private const uint MfVersion = 0x0002_0070;
    private const uint MfStartupFull = 0;
    private const uint MfVideoInterlaceProgressive = 2;

    /// <summary>The largest side the H.264 encoder refuses; anything larger it accepts.</summary>
    private const int SmallestSide = 32;

    /// <summary>Media Foundation counts time in 100-nanosecond units.</summary>
    private const long HundredNanosecondsPerSecond = 10_000_000;

    private static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MfVideoFormatH264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid MfVideoFormatRgb32 = new("00000016-0000-0010-8000-00aa00389b71");
    private static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MfMtAvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MfMtInterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MfMtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MfMtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MfMtPixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MfMtDefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");

    private readonly int _width;
    private readonly int _height;
    private readonly long _frameDuration;
    private readonly int _stride;

    private IMFSinkWriter? _writer;
    private int _stream;
    private long _clock;
    private byte[]? _frame;
    private bool _started;
    private bool _closed;

    /// <summary>Opens the file and negotiates the encoder, so a bad size or codec fails at once.</summary>
    internal Mpeg4VideoEncoder(string path, int width, int height, double frameRate, int quality)
    {
        // H.264 codes in 16-by-16 macroblocks over a 4:2:0 chroma plane, so both dimensions have to
        // be even. MATLAB refuses an odd frame for the same reason, and saying so here is far kinder
        // than letting the encoder fail on the first frame.
        if ((width & 1) != 0 || (height & 1) != 0)
        {
            throw new ArgumentException(
                $"MPEG-4 needs an even frame width and height, but the frame is {height} by {width}. "
                + "Crop or resize it by a pixel, or write one of the AVI profiles.");
        }

        // Measured against this encoder: 33 pixels is accepted in each direction and 32 is not, so
        // the smallest even frame is 34 by 34. Checking it here turns MF_E_INVALIDMEDIATYPE — which
        // says only "no" — into the sentence that explains it. MATLAB refuses these sizes too, for
        // the same reason: it is the same Windows encoder underneath.
        if (width <= SmallestSide || height <= SmallestSide)
        {
            throw new ArgumentException(
                $"MPEG-4 frames must be more than {SmallestSide} pixels on each side, but the frame "
                + $"is {height} by {width}. Use a larger frame, or write one of the AVI profiles, "
                + "which have no such floor.");
        }

        _width = width;
        _height = height;
        _stride = width * 4;
        _frameDuration = (long)Math.Round(HundredNanosecondsPerSecond / frameRate);

        Check(MFStartup(MfVersion, MfStartupFull), "start Media Foundation");
        _started = true;
        try
        {
            Build(path, frameRate, quality);
        }
        catch
        {
            Release();
            throw;
        }
    }

    /// <inheritdoc />
    public VideoSampleLayout Layout => VideoSampleLayout.Rgb24;

    /// <inheritdoc />
    public int FrameCount { get; private set; }

    /// <inheritdoc />
    public void WriteFrame(ReadOnlySpan<byte> samples)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        int wanted = _width * _height * 3;
        if (samples.Length != wanted)
        {
            throw new ArgumentException(
                $"a frame is {wanted} samples ({_height} by {_width}), but {samples.Length} were given.",
                nameof(samples));
        }

        IMFSinkWriter writer = _writer ?? throw new InvalidOperationException("the encoder is not open.");
        int bytes = _stride * _height;
        Check(MFCreateMemoryBuffer(bytes, out IMFMediaBuffer buffer), "allocate a frame buffer");
        try
        {
            Check(buffer.Lock(out nint destination, out _, out _), "lock a frame buffer");
            try
            {
                CopyBgra(samples, destination);
            }
            finally
            {
                buffer.Unlock();
            }

            Check(buffer.SetCurrentLength(bytes), "size a frame buffer");
            Check(MFCreateSample(out IMFSample sample), "allocate a frame");
            try
            {
                Check(sample.AddBuffer(buffer), "attach a frame buffer");
                Check(sample.SetSampleTime(_clock), "time a frame");
                Check(sample.SetSampleDuration(_frameDuration), "size a frame in time");
                Check(writer.WriteSample(_stream, sample), "encode a frame");
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
        }

        _clock += _frameDuration;
        FrameCount++;
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        try
        {
            if (_writer is { } writer)
            {
                // Finalizing is what writes the MP4's index and header. A file whose last call failed
                // is not a video, so the failure is reported rather than swallowed.
                Check(writer.FinalizeWriting(), "finish the video file");
            }
        }
        finally
        {
            Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        // Disposal without Close is the failure path — an exception on the way through the run. The
        // file is abandoned rather than finalized, and nothing here throws over it.
        _closed = true;
        Release();
    }

    /// <summary>Negotiates the output and input types and opens the stream for writing.</summary>
    private void Build(string path, double frameRate, int quality)
    {
        Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out IMFSinkWriter writer),
            $"create '{Path.GetFileName(path)}'");
        _writer = writer;

        Check(MFCreateMediaType(out IMFMediaType output), "describe the encoded video");
        try
        {
            Check(output.SetGUID(MfMtMajorType, MfMediaTypeVideo), "set the output kind");
            Check(output.SetGUID(MfMtSubtype, MfVideoFormatH264), "select H.264");
            Check(output.SetUINT32(MfMtAvgBitrate, Bitrate(frameRate, quality)), "set the bitrate");
            Check(output.SetUINT32(MfMtInterlaceMode, MfVideoInterlaceProgressive), "set the scan order");
            Check(output.SetUINT64(MfMtFrameSize, Pack(_width, _height)), "set the output frame size");
            Check(output.SetUINT64(MfMtFrameRate, PackRate(frameRate)), "set the output frame rate");
            Check(output.SetUINT64(MfMtPixelAspectRatio, Pack(1, 1)), "set the output pixel shape");
            Check(writer.AddStream(output, out _stream), "add the video stream");
        }
        finally
        {
            Marshal.ReleaseComObject(output);
        }

        Check(MFCreateMediaType(out IMFMediaType input), "describe the frames");
        try
        {
            Check(input.SetGUID(MfMtMajorType, MfMediaTypeVideo), "set the input kind");
            Check(input.SetGUID(MfMtSubtype, MfVideoFormatRgb32), "select 32-bit colour");
            Check(input.SetUINT32(MfMtInterlaceMode, MfVideoInterlaceProgressive), "set the input scan order");

            // A positive default stride is how Media Foundation is told the rows run top-down. Left
            // unsaid, an RGB type means a bottom-up DIB and every frame comes out upside down.
            Check(input.SetUINT32(MfMtDefaultStride, unchecked((uint)_stride)), "set the row order");
            Check(input.SetUINT64(MfMtFrameSize, Pack(_width, _height)), "set the input frame size");
            Check(input.SetUINT64(MfMtFrameRate, PackRate(frameRate)), "set the input frame rate");
            Check(input.SetUINT64(MfMtPixelAspectRatio, Pack(1, 1)), "set the input pixel shape");
            Check(writer.SetInputMediaType(_stream, input, IntPtr.Zero), "accept the frames");
        }
        finally
        {
            Marshal.ReleaseComObject(input);
        }

        Check(writer.BeginWriting(), "begin writing");
    }

    /// <summary>
    /// Widens the caller's RGB rows into the BGRA the encoder takes. The widening happens in a
    /// managed frame this encoder keeps and reuses, so a long animation allocates once rather than
    /// once a frame.
    /// </summary>
    private void CopyBgra(ReadOnlySpan<byte> rgb, nint destination)
    {
        int bytes = _stride * _height;
        byte[] target = _frame ??= new byte[bytes];
        for (int i = 0, at = 0; i < _width * _height; i++, at += 4)
        {
            int from = i * 3;
            target[at] = rgb[from + 2];
            target[at + 1] = rgb[from + 1];
            target[at + 2] = rgb[from];
            target[at + 3] = byte.MaxValue;
        }

        Marshal.Copy(target, 0, destination, bytes);
    }

    /// <summary>
    /// The bitrate a quality asks for, as bits per pixel per second. MATLAB's <c>Quality</c> is a
    /// number from 0 to 100 with no published meaning, so this maps it across the range a viewer can
    /// tell apart: visibly blocky at the bottom, visually lossless at the top.
    /// </summary>
    private uint Bitrate(double frameRate, int quality)
    {
        double bitsPerPixel = 0.03 + (quality / 100.0 * 0.37);
        double rate = _width * (double)_height * frameRate * bitsPerPixel;
        return (uint)Math.Clamp(rate, 100_000, 200_000_000);
    }

    /// <summary>Two 32-bit numbers in one 64-bit attribute, high word first — Media Foundation's pairing.</summary>
    private static ulong Pack(int high, int low) => ((ulong)(uint)high << 32) | (uint)low;

    /// <summary>
    /// A frame rate as the numerator/denominator pair the attribute wants. A whole number is exact;
    /// anything else is written over a thousand, which keeps 29.97 and 23.976 exact too.
    /// </summary>
    private static ulong PackRate(double frameRate) =>
        Math.Abs(frameRate - Math.Round(frameRate)) < 1e-9
            ? Pack((int)Math.Round(frameRate), 1)
            : Pack((int)Math.Round(frameRate * 1000.0), 1000);

    private void Release()
    {
        if (_writer is { } writer)
        {
            _writer = null;
            Marshal.ReleaseComObject(writer);
        }

        if (_started)
        {
            _started = false;
            MFShutdown();
        }
    }

    /// <summary>Turns a COM failure into a message that names what was being attempted.</summary>
    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new IOException($"could not {what} (Media Foundation reported 0x{hr:X8}).");
        }
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IMFMediaType type);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(out IMFSample sample);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSinkWriterFromURL(
        string url, IntPtr byteStream, IntPtr attributes, out IMFSinkWriter writer);

    /// <summary>
    /// <c>IMFAttributes</c>, and through it <c>IMFMediaType</c>: thirty slots of which this encoder
    /// calls three. The rest hold their places in the vtable and nothing more.
    /// </summary>
    [ComImport]
    [Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaType
    {
        [PreserveSig] int GetItem();

        [PreserveSig] int GetItemType();

        [PreserveSig] int CompareItem();

        [PreserveSig] int Compare();

        [PreserveSig] int GetUINT32();

        [PreserveSig] int GetUINT64();

        [PreserveSig] int GetDouble();

        [PreserveSig] int GetGUID();

        [PreserveSig] int GetStringLength();

        [PreserveSig] int GetString();

        [PreserveSig] int GetAllocatedString();

        [PreserveSig] int GetBlobSize();

        [PreserveSig] int GetBlob();

        [PreserveSig] int GetAllocatedBlob();

        [PreserveSig] int GetUnknown();

        [PreserveSig] int SetItem();

        [PreserveSig] int DeleteItem();

        [PreserveSig] int DeleteAllItems();

        [PreserveSig] int SetUINT32(in Guid key, uint value);

        [PreserveSig] int SetUINT64(in Guid key, ulong value);

        [PreserveSig] int SetDouble();

        [PreserveSig] int SetGUID(in Guid key, in Guid value);
    }

    /// <summary><c>IMFSample</c>: the thirty attribute slots, then the three this encoder uses.</summary>
    [ComImport]
    [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        [PreserveSig] int Attribute01();

        [PreserveSig] int Attribute02();

        [PreserveSig] int Attribute03();

        [PreserveSig] int Attribute04();

        [PreserveSig] int Attribute05();

        [PreserveSig] int Attribute06();

        [PreserveSig] int Attribute07();

        [PreserveSig] int Attribute08();

        [PreserveSig] int Attribute09();

        [PreserveSig] int Attribute10();

        [PreserveSig] int Attribute11();

        [PreserveSig] int Attribute12();

        [PreserveSig] int Attribute13();

        [PreserveSig] int Attribute14();

        [PreserveSig] int Attribute15();

        [PreserveSig] int Attribute16();

        [PreserveSig] int Attribute17();

        [PreserveSig] int Attribute18();

        [PreserveSig] int Attribute19();

        [PreserveSig] int Attribute20();

        [PreserveSig] int Attribute21();

        [PreserveSig] int Attribute22();

        [PreserveSig] int Attribute23();

        [PreserveSig] int Attribute24();

        [PreserveSig] int Attribute25();

        [PreserveSig] int Attribute26();

        [PreserveSig] int Attribute27();

        [PreserveSig] int Attribute28();

        [PreserveSig] int Attribute29();

        [PreserveSig] int Attribute30();

        [PreserveSig] int GetSampleFlags();

        [PreserveSig] int SetSampleFlags();

        [PreserveSig] int GetSampleTime();

        [PreserveSig] int SetSampleTime(long time);

        [PreserveSig] int GetSampleDuration();

        [PreserveSig] int SetSampleDuration(long duration);

        [PreserveSig] int GetBufferCount();

        [PreserveSig] int GetBufferByIndex();

        [PreserveSig] int ConvertToContiguousBuffer();

        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
    }

    /// <summary><c>IMFMediaBuffer</c>: the memory one frame is handed over in.</summary>
    [ComImport]
    [Guid("045fa593-8799-42b8-bc8d-8968c6453507")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out nint buffer, out int maxLength, out int currentLength);

        [PreserveSig] int Unlock();

        [PreserveSig] int GetCurrentLength(out int length);

        [PreserveSig] int SetCurrentLength(int length);

        [PreserveSig] int GetMaxLength(out int length);
    }

    /// <summary><c>IMFSinkWriter</c>: the encoder and muxer behind one file.</summary>
    [ComImport]
    [Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType targetType, out int streamIndex);

        [PreserveSig] int SetInputMediaType(int streamIndex, IMFMediaType inputType, IntPtr parameters);

        [PreserveSig] int BeginWriting();

        [PreserveSig] int WriteSample(int streamIndex, IMFSample sample);

        [PreserveSig] int SendStreamTick();

        [PreserveSig] int PlaceMarker();

        [PreserveSig] int NotifyEndOfSegment();

        [PreserveSig] int Flush();

        /// <summary>The vtable's <c>Finalize</c>, renamed because the CLR already has one.</summary>
        [PreserveSig] int FinalizeWriting();
    }
}
