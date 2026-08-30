using JGraph.Imaging.Codecs;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// <c>VideoWriter</c> and the three verbs that drive it (M108): <c>open</c>, <c>writeVideo</c> and
/// <c>close</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists now.</b> ADR 0072 recorded "there is no <c>VideoWriter</c>… a real video
/// container needs a codec this build does not carry", and made GIF the way a script saved an
/// animation. That was true of a codec we would have had to ship and vendor. It was never true of
/// the two that need nothing shipped: an AVI is a RIFF file this project can write itself, and MP4
/// goes out through the encoder Windows already has. So the divergence is retired rather than
/// worked around, and <c>getframe</c> — which has been here since M72 with nowhere to send its
/// frames — finally has a destination.
/// </para>
/// <para>
/// <b>The object.</b> A writer is a struct wearing the class name, like <c>containers.Map</c> and
/// the spatial reference types, and it is a <em>handle</em> class as MATLAB's is: <c>open(v)</c>
/// has to be visible to the <c>v</c> the caller is holding, or <c>writeVideo</c> after it would say
/// the file was never opened. The encoder itself cannot live in a value — it owns a file handle and,
/// for MPEG-4, a Media Foundation session — so the struct carries an id and the run owns the encoder,
/// exactly as <c>fopen</c> does. That is also what closes a video a script forgot to close: the
/// encoder dies with the run that made it, having finished its file.
/// </para>
/// </remarks>
internal static partial class JgsBuiltins
{
    /// <summary>The class name a <c>VideoWriter</c> answers to.</summary>
    internal const string VideoWriterClassName = "VideoWriter";

    /// <summary>
    /// What a writer knows that MATLAB does not publish: which profile it is, whether it is open, and
    /// what its settings were when it was opened.
    /// <para>
    /// It is kept beside the value rather than in it. Hidden fields would have been simpler and were
    /// how this was first written, and they showed up in <c>fieldnames(v)</c> and <c>properties(v)</c>
    /// next to the real ones — a writer that answered fifteen names where MATLAB's answers thirteen.
    /// A writer is a handle class, so it is never copied and its storage is its identity; a weak table
    /// on that storage holds the private half exactly as long as the value lives.
    /// </para>
    /// </summary>
    private sealed class VideoWriterState
    {
        /// <summary>The profile name this writer was made with.</summary>
        public required string Profile { get; init; }

        /// <summary>The run's id for the open encoder, 0 before <c>open</c>, -1 between open and the
        /// first frame (the container cannot be built until a frame says how big it is).</summary>
        public int Encoder { get; set; }

        /// <summary>The frame rate as it stood at <c>open</c>, or null while closed.</summary>
        public double? PinnedRate { get; set; }

        /// <summary>The quality as it stood at <c>open</c>, or null while closed.</summary>
        public double? PinnedQuality { get; set; }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JgsStructArray, VideoWriterState>
        VideoWriterStates = new();

    /// <summary>The private half of a writer, which every writer has from the moment it is made.</summary>
    private static VideoWriterState StateOf(JgsValue writer, int line, int col) =>
        VideoWriterStates.TryGetValue(writer.AsStructArray, out VideoWriterState? state)
            ? state
            : throw new JgsRuntimeException(line, col,
                "this VideoWriter has lost track of its file — it was copied out of the run that made it.");

    /// <summary>
    /// The profiles, in the order <c>VideoWriter.getProfiles</c> lists them. Two of MATLAB's seven are
    /// missing and say so by name when asked for: both are JPEG 2000, which no encoder here writes.
    /// </summary>
    private static readonly (string Name, string Extension, string Compression, string Format, int Channels, VideoCodec? Codec, string Description)[] VideoProfiles =
    [
        ("Archival", ".mj2", "Motion JPEG 2000", "Mono", 1, null,
            "Video file compression with JPEG 2000 codec with lossless mode enabled."),
        ("Motion JPEG 2000", ".mj2", "Motion JPEG 2000", "Mono", 1, null,
            "Video file compression with JPEG 2000 codec."),
        ("Motion JPEG AVI", ".avi", "Motion JPEG", "RGB24", 3, VideoCodec.MotionJpegAvi,
            "An AVI file with Motion JPEG compression"),
        ("Grayscale AVI", ".avi", "Grayscale", "Grayscale", 1, VideoCodec.GrayscaleAvi,
            "An AVI file with Grayscale Video Data"),
        ("Indexed AVI", ".avi", "Indexed", "Indexed", 1, VideoCodec.IndexedAvi,
            "An AVI file with Indexed Video Data"),
        ("MPEG-4", ".mp4", "H.264", "RGB24", 3, VideoCodec.Mpeg4,
            "A MPEG-4 file with H.264 Compression"),
        ("Uncompressed AVI", ".avi", "Uncompressed", "RGB24", 3, VideoCodec.UncompressedAvi,
            "An AVI file with uncompressed RGB24 video data"),
    ];

    /// <summary>Whether a value is a <c>VideoWriter</c>.</summary>
    internal static bool IsVideoWriter(JgsValue value) =>
        value.Type == JgsType.Struct && value.ClassName == VideoWriterClassName;

    /// <summary>Registers <c>VideoWriter</c> and its verbs into <paramref name="env"/>.</summary>
    internal static void RegisterVideoBuiltins(JgsEnvironment env, JGraphScriptGlobals host)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(host);

        // VideoWriter.getProfiles() is a static read off the constructor, which the interpreter
        // already routes through TryGetBuiltinStatic — the same door uint8.empty came in by.
        env.Declare(VideoWriterClassName, JgsValue.Function(new BuiltinFunction(VideoWriterClassName,
            (args, line, col) => NewVideoWriter(host, args, line, col))));

        env.Declare("open", JgsValue.Function(new BuiltinFunction("open", (args, line, col) =>
        {
            Arity("open", args, 1, line, col);
            OpenVideoWriter(host, RequireVideoWriter("open", args[0], line, col), line, col);
            return JgsValue.Null;
        })));

        env.Declare("writeVideo", JgsValue.Function(new BuiltinFunction("writeVideo", (args, line, col) =>
        {
            Arity("writeVideo", args, 2, line, col);
            WriteVideoFrame(host, RequireVideoWriter("writeVideo", args[0], line, col), args[1], line, col);
            return JgsValue.Null;
        })));
    }

    /// <summary>
    /// <c>close(v)</c>, told from <c>close(figureNumber)</c> by the argument's class. The figure verb
    /// takes numbers and the word 'all'; a writer is neither, so the two never compete.
    /// </summary>
    internal static bool TryCloseVideoWriter(
        JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        if (args.Count != 1 || !IsVideoWriter(args[0]))
        {
            return false;
        }

        JgsValue writer = args[0];
        VideoWriterState state = StateOf(writer, line, col);
        if (state.Encoder > 0)
        {
            host.CloseVideoWriter(state.Encoder);
        }

        // MATLAB lets close stand for "make sure it is shut", so closing an unopened writer is
        // quietly nothing rather than an error.
        state.Encoder = 0;
        state.PinnedRate = null;
        state.PinnedQuality = null;
        return true;
    }

    /// <summary>Builds a writer for a path and, optionally, a named profile.</summary>
    private static JgsValue NewVideoWriter(
        JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int line, int col)
    {
        ArityRange("VideoWriter", args, 1, 2, line, col);
        string given = Str("VideoWriter", args, 0, line, col);

        // MATLAB's rule: the profile decides the extension, and a name with none gets the profile's.
        // A name whose extension disagrees with the profile is the script saying two things at once,
        // so it is refused rather than silently renamed.
        string requested = args.Count == 2 ? Str("VideoWriter", args, 1, line, col) : string.Empty;
        string extension = Path.GetExtension(given);
        var profile = FindProfile(requested.Length > 0 ? requested : ProfileForExtension(extension, line, col), line, col);

        if (profile.Codec is null)
        {
            throw new JgsRuntimeException(line, col,
                $"VideoWriter: the '{profile.Name}' profile is JPEG 2000, which this build has no "
                + "encoder for. Use 'Motion JPEG AVI', 'Uncompressed AVI', 'Grayscale AVI', "
                + "'Indexed AVI' or 'MPEG-4'.");
        }

        // MATLAB's rule for a name the profile does not agree with is to append rather than to
        // refuse or to replace: VideoWriter('clip.mp4', 'Motion JPEG AVI') writes 'clip.mp4.avi'.
        // It looks odd written down and it is what a script that relies on it gets.
        string path = AcceptsExtension(profile.Name, extension) ? given : given + profile.Extension;

        string full = host.ResolveForWrite(path);
        var fields = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
        {
            ["Filename"] = JgsValue.Str(Path.GetFileName(full)),
            ["Path"] = JgsValue.Str(Path.GetDirectoryName(full) ?? string.Empty),
            ["FileFormat"] = JgsValue.Str(profile.Extension.TrimStart('.')),
            ["Duration"] = JgsValue.Number(0),
            ["ColorChannels"] = JgsValue.Number(profile.Channels),
            ["Height"] = JgsValue.Array([]),
            ["Width"] = JgsValue.Array([]),
            ["FrameCount"] = JgsValue.Number(0),
            ["FrameRate"] = JgsValue.Number(30),
            ["VideoBitsPerPixel"] = JgsValue.Number(profile.Channels == 1 ? 8 : 24),
            ["VideoFormat"] = JgsValue.Str(profile.Format),
            ["VideoCompressionMethod"] = JgsValue.Str(profile.Compression),
        };

        // Quality means nothing to a codec that does not throw anything away, and MATLAB's object
        // does not carry it for those two either.
        if (profile.Compression is "Motion JPEG" or "H.264")
        {
            fields["Quality"] = JgsValue.Number(75);
        }

        // An indexed video needs a colour table and starts without one, so a script that forgets to
        // give it is told rather than handed a grey video it did not ask for.
        if (profile.Name == "Indexed AVI")
        {
            fields["Colormap"] = JgsValue.Array([]);
        }

        JgsValue writer = JgsValue.Struct(fields);
        writer.SetClassName(VideoWriterClassName);
        VideoWriterStates.Add(writer.AsStructArray, new VideoWriterState { Profile = profile.Name });
        return writer;
    }

    /// <summary>Creates the encoder the first frame will go to. The frame size is not known yet.</summary>
    private static void OpenVideoWriter(
        JGraphScriptGlobals host, JgsValue writer, int line, int col)
    {
        VideoWriterState state = StateOf(writer, line, col);
        if (state.Encoder != 0)
        {
            return; // already open; MATLAB's open is idempotent
        }

        double rate = FieldNumber(writer, "FrameRate");
        if (!(rate > 0) || double.IsNaN(rate) || double.IsInfinity(rate))
        {
            throw new JgsRuntimeException(line, col, "VideoWriter: expected FrameRate to be positive.");
        }

        // Nothing is created here but the intent: a container cannot commit to its headers until it
        // knows the frame size, and the frame size arrives with the first frame. Marking the writer
        // open is what makes writeVideo legal and makes a second open a no-op.
        // Nothing is created here but the intent: -1 marks the writer open and the container unbuilt.
        state.Encoder = -1;
        writer.AsStruct["FrameCount"] = JgsValue.Number(0);
        writer.AsStruct["Duration"] = JgsValue.Number(0);

        // What the container will be built with, remembered now. MATLAB refuses these outright once
        // open has been called; a struct field cannot refuse an assignment here, so the settings are
        // pinned at open and a later change is caught at the first frame rather than silently obeyed.
        state.PinnedRate = rate;
        state.PinnedQuality = writer.AsStruct.TryGetValue("Quality", out JgsValue? quality)
            ? quality.AsNumber
            : null;
        _ = host;
    }

    /// <summary>
    /// Refuses a setting changed after <c>open</c>. MATLAB's message names the property and says the
    /// change is not allowed after OPEN; this says the same thing at the first frame, which is the
    /// first moment the change could have had an effect.
    /// </summary>
    private static void RequirePinned(JgsValue writer, string property, double? pinned, int line, int col)
    {
        if (pinned is not { } was
            || !writer.AsStruct.TryGetValue(property, out JgsValue? now)
            || was == now.AsNumber)
        {
            return;
        }

        throw new JgsRuntimeException(line, col,
            $"writeVideo: modifying the {property} property of a VideoWriter is not allowed after "
            + $"open has been called (it was {was:G} at open and is {now.AsNumber:G} now).");
    }

    /// <summary>Appends one frame, creating the encoder if this is the first.</summary>
    private static void WriteVideoFrame(
        JGraphScriptGlobals host, JgsValue writer, JgsValue frame, int line, int col)
    {
        VideoWriterState state = StateOf(writer, line, col);
        if (state.Encoder == 0)
        {
            throw new JgsRuntimeException(line, col,
                "writeVideo: the VideoWriter must be open before writing. Call open(v) first.");
        }

        // A frame array is the other half of the getframe idiom — F(k) = getframe(fig) in the loop
        // and one writeVideo after it — so a struct array is written element by element rather than
        // refused for not being a picture.
        if (frame.Type == JgsType.Struct && frame.ClassName is null && frame.IsStructArray
            && frame.AsStructArray.Length != 1 && frame.AsStructArray.FieldNames.Contains("cdata"))
        {
            RequireFrameFields(frame, line, col);
            JgsStructArray frames = frame.AsStructArray;
            for (int i = 0; i < frames.Length; i++)
            {
                WriteVideoFrame(host, writer, JgsValue.Struct(frames.Elements[i]), line, col);
            }

            return;
        }

        // getframe answers a struct; MATLAB's writeVideo takes that struct or the picture inside it.
        JgsValue picture = frame;
        if (frame.Type == JgsType.Struct && frame.ClassName is null
            && frame.AsStruct.TryGetValue("cdata", out JgsValue? cdata))
        {
            RequireFrameFields(frame, line, col);
            picture = cdata;
        }

        (string profileName, VideoCodec codec) = ProfileOf(state, line, col);
        bool indexed = VideoEncoder.IsIndexed(codec);
        VideoFrame read = ReadFrame(profileName, picture, indexed, line, col);

        IVideoEncoder encoder;
        if (state.Encoder < 0)
        {
            RequirePinned(writer, "FrameRate", state.PinnedRate, line, col);
            RequirePinned(writer, "Quality", state.PinnedQuality, line, col);
            encoder = CreateEncoder(host, writer, codec, read, line, col);
            state.Encoder = host.AddVideoWriter(encoder);
            writer.AsStruct["Height"] = JgsValue.Number(read.Height);
            writer.AsStruct["Width"] = JgsValue.Number(read.Width);
        }
        else
        {
            encoder = host.VideoWriter(state.Encoder)
                ?? throw new JgsRuntimeException(line, col, "writeVideo: this VideoWriter has been closed.");
            int height = (int)FieldNumber(writer, "Height");
            int width = (int)FieldNumber(writer, "Width");
            if (read.Height != height || read.Width != width)
            {
                throw new JgsRuntimeException(line, col,
                    $"writeVideo: every frame must be {height} by {width}, "
                    + $"but this one is {read.Height} by {read.Width}.");
            }
        }

        try
        {
            encoder.WriteFrame(read.Samples);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            throw new JgsRuntimeException(line, col, $"writeVideo: {ex.Message}");
        }

        writer.AsStruct["FrameCount"] = JgsValue.Number(encoder.FrameCount);
        writer.AsStruct["Duration"] = JgsValue.Number(encoder.FrameCount / FieldNumber(writer, "FrameRate"));
    }

    /// <summary>Builds the encoder once the first frame has said how big the video is.</summary>
    private static IVideoEncoder CreateEncoder(
        JGraphScriptGlobals host, JgsValue writer, VideoCodec codec, VideoFrame frame, int line, int col)
    {
        string path = Path.Combine(
            FieldText(writer, "Path"), FieldText(writer, "Filename"));
        double rate = FieldNumber(writer, "FrameRate");
        int quality = writer.AsStruct.TryGetValue("Quality", out JgsValue? q) ? (int)Math.Round(q.AsNumber) : 75;
        byte[] palette = [];
        if (codec == VideoCodec.IndexedAvi)
        {
            JgsValue map = writer.AsStruct.TryGetValue("Colormap", out JgsValue? given) ? given : JgsValue.Array([]);
            if (JgsMatrix.DimsOf(map) is not [> 0, 3])
            {
                throw new JgsRuntimeException(line, col,
                    "writeVideo: a Colormap is required when writing an Indexed AVI file — "
                    + "set v.Colormap to an m-by-3 colour table before the first frame.");
            }

            palette = PaletteBytes("VideoWriter: Colormap", map, line, col);
        }

        try
        {
            return VideoEncoder.Create(path, codec, frame.Width, frame.Height, rate, quality, palette);
        }
        catch (Exception ex)
            when (ex is IOException or ArgumentException or PlatformNotSupportedException
                or UnauthorizedAccessException)
        {
            throw new JgsRuntimeException(line, col, $"VideoWriter: {ex.Message}");
        }
        finally
        {
            _ = host;
        }
    }

    /// <summary>
    /// A frame struct must carry both of MATLAB's fields. <c>getframe</c> always produces both — the
    /// colour table is empty for a true-colour frame — so this only ever catches a struct built by
    /// hand, and catching it is what stops such a script from working here and failing in MATLAB.
    /// </summary>
    private static void RequireFrameFields(JgsValue frame, int line, int col)
    {
        string[] fields = frame.AsStructArray.FieldNames;
        if (!fields.Contains("cdata") || !fields.Contains("colormap"))
        {
            throw new JgsRuntimeException(line, col,
                "writeVideo: a frame struct must have the fields 'cdata' and 'colormap' — "
                + "the pair getframe answers with.");
        }
    }

    /// <summary>One frame's samples, already in the layout the encoder takes.</summary>
    private readonly record struct VideoFrame(int Height, int Width, byte[] Samples);

    /// <summary>
    /// Reads a frame from whatever a script handed over, applying MATLAB's rule for how a number
    /// becomes a sample: an integer class is already 0–255, and a double is a fraction of full scale
    /// and is scaled by 255. Getting that backwards is the difference between a video and a white
    /// rectangle, which is why the class is read rather than guessed from the values.
    /// </summary>
    private static VideoFrame ReadFrame(string profile, JgsValue picture, bool indexed, int line, int col)
    {
        int[] dims = JgsMatrix.DimsOf(picture) ?? [];
        int height, width, channels;
        switch (dims)
        {
            case [int h, int w, 3]:
                (height, width, channels) = (h, w, 3);
                break;
            case [int h, int w]:
                (height, width, channels) = (h, w, 1);
                break;
            default:
                throw new JgsRuntimeException(line, col,
                    "writeVideo: a frame is a height-by-width-by-3 colour array, a height-by-width "
                    + "grayscale or indexed array, or the struct getframe answers with.");
        }

        if (height == 0 || width == 0)
        {
            throw new JgsRuntimeException(line, col, "writeVideo: the frame is empty.");
        }

        if (indexed && channels == 3)
        {
            throw new JgsRuntimeException(line, col,
                $"writeVideo: the '{profile}' profile takes a height-by-width frame of "
                + (profile == "Indexed AVI" ? "colormap indices" : "grey levels")
                + ", but a colour frame was given.");
        }

        // A double frame runs 0 to 1 and an integer one 0 to 255 — MATLAB's convention for every verb
        // that turns numbers into pixels. A double frame outside that range is very nearly always a
        // uint8 picture someone forgot to cast, and turning it into white by clamping hides the
        // mistake, so MATLAB refuses it and so does this.
        bool real = picture.NumericClass is JgsNumericClass.Double or JgsNumericClass.Single;
        double scale = real ? 255.0 : 1.0;
        if (real)
        {
            long count = (long)height * width * channels;
            for (long i = 0; i < count; i++)
            {
                double sample = picture.ElementAt((int)i).AsNumber;
                if (sample < 0 || sample > 1)
                {
                    throw new JgsRuntimeException(line, col,
                        "writeVideo: frames of type double must be in the range 0 to 1 "
                        + $"(this one holds {sample:G}). Cast the frame with uint8() if it is 0 to 255.");
                }
            }
        }

        int outputChannels = indexed ? 1 : 3;
        var samples = new byte[(long)height * width * outputChannels];
        for (int r = 0; r < height; r++)
        {
            for (int c = 0; c < width; c++)
            {
                int to = ((r * width) + c) * outputChannels;
                if (outputChannels == 1)
                {
                    samples[to] = Sample(picture, r + (c * height), scale);
                    continue;
                }

                for (int ch = 0; ch < 3; ch++)
                {
                    // The frame is column-major with the channels last; the encoder wants rows of
                    // interleaved pixels, so this is a transpose as much as a conversion.
                    int from = channels == 3 ? r + (c * height) + (ch * height * width) : r + (c * height);
                    samples[to + ch] = Sample(picture, from, scale);
                }
            }
        }

        return new VideoFrame(height, width, samples);
    }

    /// <summary>One sample, scaled and clamped into a byte.</summary>
    private static byte Sample(JgsValue picture, int at, double scale)
    {
        double value = picture.ElementAt(at).AsNumber * scale;
        return double.IsNaN(value) ? (byte)0 : (byte)Math.Clamp(Math.Round(value), 0, 255);
    }

    /// <summary>A colormap as the 256 RGB triplets an AVI palette holds.</summary>
    private static byte[] PaletteBytes(string what, JgsValue map, int line, int col)
    {
        double[][] rows = JgsMatrix.ToRows(what, map, line, col);
        if (rows.Length == 0 || rows.Length > 256 || rows[0].Length != 3)
        {
            throw new JgsRuntimeException(line, col,
                $"{what} must be an m-by-3 colormap with at most 256 rows, but it is "
                + $"{rows.Length}-by-{(rows.Length == 0 ? 0 : rows[0].Length)}.");
        }

        var palette = new byte[256 * 3];
        for (int i = 0; i < rows.Length; i++)
        {
            for (int ch = 0; ch < 3; ch++)
            {
                palette[(i * 3) + ch] = (byte)Math.Clamp(Math.Round(rows[i][ch] * 255), 0, 255);
            }
        }

        return palette;
    }

    /// <summary>The profile a writer was made with, and the codec behind it.</summary>
    private static (string Name, VideoCodec Codec) ProfileOf(VideoWriterState state, int line, int col)
    {
        var profile = FindProfile(state.Profile, line, col);
        return (profile.Name, profile.Codec
            ?? throw new JgsRuntimeException(line, col, $"VideoWriter: no encoder for '{state.Profile}'."));
    }

    /// <summary>The profile of a given name, however it was capitalised.</summary>
    private static (string Name, string Extension, string Compression, string Format, int Channels, VideoCodec? Codec, string Description)
        FindProfile(string name, int line, int col)
    {
        foreach (var profile in VideoProfiles)
        {
            if (profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        throw new JgsRuntimeException(line, col,
            $"VideoWriter: '{name}' is not a profile. The profiles are "
            + string.Join(", ", VideoProfiles.Select(static p => $"'{p.Name}'")) + ".");
    }

    /// <summary>The profile a bare file name implies, which is MATLAB's mapping from extension.</summary>
    private static string ProfileForExtension(string extension, int line, int col) =>
        extension.ToLowerInvariant() switch
        {
            "" or ".avi" => "Motion JPEG AVI",
            ".mp4" or ".m4v" => "MPEG-4",
            ".mj2" => "Archival",
            _ => throw new JgsRuntimeException(line, col,
                $"VideoWriter: '{extension}' is not a video extension — use .avi, .mp4, .m4v or .mj2, "
                + "or name a profile."),
        };

    /// <summary>Whether a profile writes files of this extension. MPEG-4 alone writes two.</summary>
    private static bool AcceptsExtension(string profile, string extension) =>
        profile == "MPEG-4"
            ? extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
            : extension.Equals(
                VideoProfiles.First(p => p.Name == profile).Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The struct array <c>VideoWriter.getProfiles</c> answers with.</summary>
    private static JgsValue ProfileTable()
    {
        var rows = new Dictionary<string, JgsValue>[VideoProfiles.Length];
        for (int i = 0; i < VideoProfiles.Length; i++)
        {
            var profile = VideoProfiles[i];
            rows[i] = new Dictionary<string, JgsValue>(StringComparer.Ordinal)
            {
                ["Name"] = JgsValue.Str(profile.Name),
                ["Description"] = JgsValue.Str(profile.Description),
                ["FileExtensions"] = profile.Name == "MPEG-4"
                    ? JgsValue.Cell([JgsValue.Str(".mp4"), JgsValue.Str(".m4v")])
                    : JgsValue.Cell([JgsValue.Str(profile.Extension)]),
                ["VideoCompressionMethod"] = JgsValue.Str(profile.Compression),
                ["VideoFormat"] = JgsValue.Str(profile.Format),
            };
        }

        return JgsValue.StructArray(rows);
    }

    /// <summary>The callable <c>VideoWriter.getProfiles</c> resolves to.</summary>
    internal static JgsValue VideoProfilesBuiltin() =>
        JgsValue.Function(new BuiltinFunction("VideoWriter.getProfiles", (args, line, col) =>
        {
            ArityRange("VideoWriter.getProfiles", args, 0, 1, line, col);
            return ProfileTable();
        }));

    /// <summary>The argument as a writer, or a message naming what arrived instead.</summary>
    private static JgsValue RequireVideoWriter(string builtin, JgsValue value, int line, int col) =>
        IsVideoWriter(value)
            ? value
            : throw new JgsRuntimeException(line, col,
                $"{builtin} expects a VideoWriter, but got a {value.TypeName}.");

    /// <summary>One numeric field of a writer, or zero when it holds nothing numeric.</summary>
    private static double FieldNumber(JgsValue writer, string name) =>
        writer.AsStruct.TryGetValue(name, out JgsValue? held) && held.Type is JgsType.Number or JgsType.Bool
            ? held.AsNumber
            : 0;

    /// <summary>One text field of a writer.</summary>
    private static string FieldText(JgsValue writer, string name) =>
        writer.AsStruct.TryGetValue(name, out JgsValue? held) && held.Type == JgsType.String
            ? held.AsString
            : string.Empty;
}
