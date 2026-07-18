namespace JGraph.Signal;

/// <summary>
/// A hand-rolled RIFF/WAVE codec, dependency-free like the rest of the project.
/// <see cref="Read(Stream)"/> walks the chunk list and decodes PCM 8/16/24/32-bit and IEEE float32
/// data (mono kept as-is,
/// multi-channel averaged down to mono) into samples normalized to [-1, 1].
/// <see cref="Write16BitPcm"/> produces the mono 16-bit PCM stream used for playback and fixtures.
/// </summary>
public static class WaveFile
{
    private const ushort FormatPcm = 0x0001;
    private const ushort FormatIeeeFloat = 0x0003;
    private const ushort FormatExtensible = 0xFFFE;

    /// <summary>Reads a .wav stream into normalized mono samples and the sample rate.</summary>
    /// <exception cref="InvalidDataException">When the stream is not a decodable RIFF/WAVE file.</exception>
    public static (double[] Samples, int SampleRate) Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        if (ReadTag(reader) != "RIFF")
        {
            throw new InvalidDataException("Not a WAV file: missing the RIFF header.");
        }

        reader.ReadUInt32(); // overall size, unused
        if (ReadTag(reader) != "WAVE")
        {
            throw new InvalidDataException("Not a WAV file: missing the WAVE tag.");
        }

        ushort format = 0;
        ushort channels = 0;
        int sampleRate = 0;
        ushort bitsPerSample = 0;
        byte[]? data = null;

        // Chunk walk: fmt and data can appear in any order, with other chunks (LIST, fact, …) between.
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string tag = ReadTag(reader);
            uint size = reader.ReadUInt32();
            long next = reader.BaseStream.Position + size + (size % 2); // chunks are word-aligned

            if (tag == "fmt ")
            {
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byte rate
                reader.ReadUInt16(); // block align
                bitsPerSample = reader.ReadUInt16();
                if (format == FormatExtensible && size >= 40)
                {
                    reader.ReadUInt16(); // extension size
                    reader.ReadUInt16(); // valid bits
                    reader.ReadUInt32(); // channel mask
                    format = reader.ReadUInt16(); // first two bytes of the sub-format GUID
                }
            }
            else if (tag == "data")
            {
                data = reader.ReadBytes(checked((int)size));
            }

            if (next > reader.BaseStream.Length)
            {
                break; // truncated final chunk — use what we have
            }

            reader.BaseStream.Position = next;
        }

        if (channels == 0 || sampleRate == 0)
        {
            throw new InvalidDataException("The WAV file has no fmt chunk.");
        }

        if (data is null)
        {
            throw new InvalidDataException("The WAV file has no data chunk.");
        }

        double[] samples = Decode(data, format, channels, bitsPerSample);
        return (samples, sampleRate);
    }

    /// <summary>Reads a .wav file into normalized mono samples and the sample rate.</summary>
    public static (double[] Samples, int SampleRate) Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Writes mono 16-bit PCM. Samples are clamped to [-1, 1].</summary>
    public static void Write16BitPcm(Stream stream, ReadOnlySpan<double> samples, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        int dataBytes = samples.Length * 2;

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write(FormatPcm);
        writer.Write((ushort)1);          // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);     // byte rate
        writer.Write((ushort)2);          // block align
        writer.Write((ushort)16);         // bits per sample
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (double sample in samples)
        {
            double clamped = System.Math.Clamp(sample, -1.0, 1.0);
            writer.Write((short)System.Math.Round(clamped * short.MaxValue));
        }
    }

    private static double[] Decode(byte[] data, ushort format, int channels, int bitsPerSample)
    {
        int bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample == 0)
        {
            throw new InvalidDataException("The WAV fmt chunk reports zero bits per sample.");
        }

        int frameBytes = bytesPerSample * channels;
        int frames = data.Length / frameBytes;
        var samples = new double[frames];
        int values = frames * channels;

        // One format dispatch for the whole file, then a dedicated bulk loop — a million-sample
        // clip decodes without a per-sample switch. 16-bit PCM and float data reinterpret the byte
        // payload as its sample type in place (WAV is little-endian, as is every platform JGraph
        // targets — asserted below rather than assumed).
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("WAV decoding requires a little-endian platform.");
        }

        switch (format, bitsPerSample)
        {
            case (FormatPcm, 8):
                for (int v = 0; v < values; v++)
                {
                    AddSample(samples, channels, v, (data[v] - 128) / 128.0);
                }

                break;
            case (FormatPcm, 16):
            {
                ReadOnlySpan<short> pcm = System.Runtime.InteropServices.MemoryMarshal
                    .Cast<byte, short>(data.AsSpan(0, values * 2));
                for (int v = 0; v < values; v++)
                {
                    AddSample(samples, channels, v, pcm[v] / 32768.0);
                }

                break;
            }

            case (FormatPcm, 24):
                for (int v = 0; v < values; v++)
                {
                    int offset = v * 3;
                    int raw = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
                    if (raw >= 0x800000)
                    {
                        raw -= 0x1000000;
                    }

                    AddSample(samples, channels, v, raw / 8388608.0);
                }

                break;
            case (FormatPcm, 32):
            {
                ReadOnlySpan<int> pcm = System.Runtime.InteropServices.MemoryMarshal
                    .Cast<byte, int>(data.AsSpan(0, values * 4));
                for (int v = 0; v < values; v++)
                {
                    AddSample(samples, channels, v, pcm[v] / 2147483648.0);
                }

                break;
            }

            case (FormatIeeeFloat, 32):
            {
                ReadOnlySpan<float> pcm = System.Runtime.InteropServices.MemoryMarshal
                    .Cast<byte, float>(data.AsSpan(0, values * 4));
                for (int v = 0; v < values; v++)
                {
                    AddSample(samples, channels, v, pcm[v]);
                }

                break;
            }

            case (FormatIeeeFloat, 64):
            {
                ReadOnlySpan<double> pcm = System.Runtime.InteropServices.MemoryMarshal
                    .Cast<byte, double>(data.AsSpan(0, values * 8));
                for (int v = 0; v < values; v++)
                {
                    AddSample(samples, channels, v, pcm[v]);
                }

                break;
            }

            default:
                throw new InvalidDataException(
                    $"Unsupported WAV format: format tag {format}, {bitsPerSample} bits per sample.");
        }

        if (channels > 1)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                samples[frame] /= channels;
            }
        }

        return samples;
    }

    /// <summary>Accumulates interleaved value <paramref name="v"/> into its frame (mono writes directly).</summary>
    private static void AddSample(double[] samples, int channels, int v, double value)
    {
        if (channels == 1)
        {
            samples[v] = value;
        }
        else
        {
            samples[v / channels] += value;
        }
    }

    private static string ReadTag(BinaryReader reader) =>
        new(reader.ReadChars(4));
}
