namespace JGraph.Imaging.Codecs;

/// <summary>
/// The video containers JGraph can write. Each one is a MATLAB <c>VideoWriter</c> profile, and the
/// names are that profile's, because the profile is the only thing a script names.
/// </summary>
public enum VideoCodec
{
    /// <summary>An AVI whose frames are each a whole JPEG. MATLAB's "Motion JPEG AVI".</summary>
    MotionJpegAvi,

    /// <summary>An AVI of raw bottom-up RGB24 rows. MATLAB's "Uncompressed AVI".</summary>
    UncompressedAvi,

    /// <summary>
    /// An AVI of raw bottom-up BGRA rows — the same container with a fourth byte per pixel that is
    /// the coverage rather than padding. Not one of MATLAB's profiles; see <see cref="AnimatedPng"/>.
    /// </summary>
    UncompressedAvi32,

    /// <summary>An AVI of 8-bit samples under a grey ramp. MATLAB's "Grayscale AVI".</summary>
    GrayscaleAvi,

    /// <summary>An AVI of 8-bit indices under a colormap the script gives. MATLAB's "Indexed AVI".</summary>
    IndexedAvi,

    /// <summary>An MP4 carrying H.264. MATLAB's "MPEG-4".</summary>
    Mpeg4,

    /// <summary>
    /// An animated PNG: every frame whole, losslessly compressed, and carrying eight bits of alpha.
    /// Not one of MATLAB's profiles, and the reason it is here is that none of MATLAB's seven keeps
    /// a transparent page transparent — an exported cut-out has nowhere to go otherwise.
    /// </summary>
    AnimatedPng,
}

/// <summary>How a frame's samples are laid out in the span handed to <see cref="IVideoEncoder.WriteFrame"/>.</summary>
public enum VideoSampleLayout
{
    /// <summary>Three bytes per pixel, red first, rows top-down and unpadded.</summary>
    Rgb24,

    /// <summary>One byte per pixel — a grey level or a palette index — rows top-down and unpadded.</summary>
    Indexed8,

    /// <summary>
    /// Four bytes per pixel, red first and coverage last, rows top-down and unpadded. The colours
    /// are not premultiplied: they are what was drawn, beside how much of it was drawn.
    /// </summary>
    Rgba32,
}

/// <summary>
/// Writes one video file, a frame at a time. The frame size is fixed when the encoder is made, which
/// is what lets a container commit to its headers before a single frame has arrived.
/// </summary>
/// <remarks>
/// Every implementation writes as it goes rather than gathering frames: a hundred-frame animation
/// never holds more than one frame in memory, which is the same promise <see cref="GifEncoder"/>
/// makes and for the same reason — a script that renders a long morph should not have to fit it.
/// </remarks>
public interface IVideoEncoder : IDisposable
{
    /// <summary>The layout <see cref="WriteFrame"/> expects.</summary>
    VideoSampleLayout Layout { get; }

    /// <summary>How many frames have been written.</summary>
    int FrameCount { get; }

    /// <summary>Appends one frame.</summary>
    /// <param name="samples">The frame's samples in <see cref="Layout"/>, exactly one frame's worth.</param>
    /// <exception cref="ArgumentException">The span is not one frame's worth of samples.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    void WriteFrame(ReadOnlySpan<byte> samples);

    /// <summary>Finishes the file — indexes, trailers, sizes — and releases it. Idempotent.</summary>
    void Close();
}

/// <summary>Makes an encoder for a codec, and says what each codec can and cannot do.</summary>
public static class VideoEncoder
{
    /// <summary>Whether <paramref name="codec"/> takes palette indices rather than colour.</summary>
    public static bool IsIndexed(VideoCodec codec) =>
        codec is VideoCodec.GrayscaleAvi or VideoCodec.IndexedAvi;

    /// <summary>Whether <paramref name="codec"/> keeps a frame's alpha channel rather than dropping it.</summary>
    public static bool CarriesAlpha(VideoCodec codec) =>
        codec is VideoCodec.UncompressedAvi32 or VideoCodec.AnimatedPng;

    /// <summary>
    /// Creates an encoder writing <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file to write. Any existing file is replaced.</param>
    /// <param name="codec">The container and compression to write.</param>
    /// <param name="width">Frame width in pixels; must be positive.</param>
    /// <param name="height">Frame height in pixels; must be positive.</param>
    /// <param name="frameRate">Frames per second; must be positive.</param>
    /// <param name="quality">0–100, read by the codecs that compress and ignored by the ones that do not.</param>
    /// <param name="palette">256 entries of R, G, B for an indexed codec; ignored by the others. A
    /// grey ramp is used when an indexed codec is given none.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension, the rate or the quality is out of range.</exception>
    /// <exception cref="PlatformNotSupportedException">The codec needs an encoder this machine has not got.</exception>
    /// <exception cref="IOException">The file cannot be created.</exception>
    public static IVideoEncoder Create(
        string path,
        VideoCodec codec,
        int width,
        int height,
        double frameRate,
        int quality = 75,
        ReadOnlySpan<byte> palette = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (!(frameRate > 0) || double.IsInfinity(frameRate))
        {
            throw new ArgumentOutOfRangeException(nameof(frameRate), frameRate, "the frame rate must be positive.");
        }

        if (codec == VideoCodec.AnimatedPng)
        {
            return new AnimatedPngEncoder(path, width, height, frameRate);
        }

        if (codec != VideoCodec.Mpeg4)
        {
            return new AviVideoEncoder(path, codec, width, height, frameRate, Math.Clamp(quality, 0, 100), palette);
        }

        // MPEG-4 goes out through Media Foundation, which is a Windows component. The guard lives
        // here rather than inside the encoder so the platform analyser can see it.
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "MPEG-4 is written through Media Foundation, which is a Windows component; "
                + "use one of the AVI profiles on this machine.");
        }

        return new Mpeg4VideoEncoder(path, width, height, frameRate, Math.Clamp(quality, 0, 100));
    }
}
