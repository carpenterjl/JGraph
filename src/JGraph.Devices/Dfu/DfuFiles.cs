using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace JGraph.Devices.Dfu;

/// <summary>One contiguous piece of an image: where it goes and its bytes.</summary>
public sealed record DfuElement(uint Address, byte[] Data);

/// <summary>One target (alternate setting) of a DfuSe image.</summary>
public sealed record DfuTarget(int AlternateSetting, string Name, IReadOnlyList<DfuElement> Elements);

/// <summary>
/// A firmware image as the DFU functions read it: the DFU suffix's IDs (0xFFFF where the suffix says
/// "any"), whether its CRC checked, and its targets — one for a plain <c>.dfu</c>, <c>.bin</c> or
/// Intel HEX file, several for a DfuSe multi-target image.
/// </summary>
public sealed record DfuImage(
    string Format,
    int VendorId,
    int ProductId,
    int DeviceRelease,
    int DfuVersion,
    bool HasSuffix,
    bool CrcOk,
    IReadOnlyList<DfuTarget> Targets);

/// <summary>
/// The file formats firmware for DFU comes in (device classes plan, stage D8, ADR 0191): the DFU 1.1
/// suffix (appendix B, with its CRC), ST's DfuSe container (UM0391), Intel HEX, and raw binary.
/// </summary>
public static class DfuFiles
{
    /// <summary>Reads a file by its content: a DfuSe prefix, an Intel HEX colon, else raw bytes; a DFU suffix is taken off first.</summary>
    public static DfuImage Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Parse(bytes, Path.GetExtension(path));
    }

    public static DfuImage Parse(byte[] bytes, string extension = "")
    {
        (bool hasSuffix, bool crcOk, int vid, int pid, int release, int dfuVersion, int payload) = Suffix(bytes);
        ReadOnlySpan<byte> body = bytes.AsSpan(0, payload);
        if (body.Length >= 11 && Encoding.ASCII.GetString(body[..5]) == "DfuSe")
        {
            return new DfuImage("DfuSe", vid, pid, release, dfuVersion, hasSuffix, crcOk, DfuSe(body));
        }

        if (body.Length > 0 && body[0] == (byte)':' && extension.ToLowerInvariant() is ".hex" or ".ihex" or "")
        {
            return new DfuImage("Intel HEX", vid, pid, release, dfuVersion, hasSuffix, crcOk, [new DfuTarget(0, "", IntelHex(body))]);
        }

        return new DfuImage(hasSuffix ? "DFU" : "binary", vid, pid, release, dfuVersion, hasSuffix, crcOk,
            [new DfuTarget(0, "", [new DfuElement(0, body.ToArray())])]);
    }

    /// <summary>The 16-byte suffix: bcdDevice, idProduct, idVendor, bcdDFU, "UFD", bLength, dwCRC.</summary>
    private static (bool Has, bool CrcOk, int Vid, int Pid, int Release, int DfuVersion, int Payload) Suffix(byte[] bytes)
    {
        if (bytes.Length < 16)
        {
            return (false, true, 0xFFFF, 0xFFFF, 0xFFFF, 0, bytes.Length);
        }

        ReadOnlySpan<byte> s = bytes.AsSpan(bytes.Length - 16);
        if (s[8] != (byte)'U' || s[9] != (byte)'F' || s[10] != (byte)'D' || s[11] < 16)
        {
            return (false, true, 0xFFFF, 0xFFFF, 0xFFFF, 0, bytes.Length);
        }

        int length = s[11];
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);
        uint computed = Crc(bytes.AsSpan(0, bytes.Length - 4));
        return (true, stored == computed,
            BinaryPrimitives.ReadUInt16LittleEndian(s[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(s[2..]),
            BinaryPrimitives.ReadUInt16LittleEndian(s),
            BinaryPrimitives.ReadUInt16LittleEndian(s[6..]),
            bytes.Length - Math.Min(length, bytes.Length));
    }

    /// <summary>The DFU suffix's CRC: CRC-32 (reflected, 0xEDB88320) from 0xFFFFFFFF with no final inversion, as dfu-util computes it.</summary>
    public static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return crc;
    }

    /// <summary>Appends a DFU 1.1 suffix to <paramref name="payload"/>.</summary>
    public static byte[] WithSuffix(ReadOnlySpan<byte> payload, int vid, int pid, int release, int dfuVersion = 0x0100)
    {
        byte[] file = new byte[payload.Length + 16];
        payload.CopyTo(file);
        Span<byte> s = file.AsSpan(payload.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(s, (ushort)release);
        BinaryPrimitives.WriteUInt16LittleEndian(s[2..], (ushort)pid);
        BinaryPrimitives.WriteUInt16LittleEndian(s[4..], (ushort)vid);
        BinaryPrimitives.WriteUInt16LittleEndian(s[6..], (ushort)dfuVersion);
        s[8] = (byte)'U';
        s[9] = (byte)'F';
        s[10] = (byte)'D';
        s[11] = 16;
        BinaryPrimitives.WriteUInt32LittleEndian(s[12..], Crc(file.AsSpan(0, file.Length - 4)));
        return file;
    }

    /// <summary>A DfuSe container (UM0391) of <paramref name="targets"/>, with its DFU suffix (bcdDFU 1.1a).</summary>
    public static byte[] DfuSeFile(IReadOnlyList<DfuTarget> targets, int vid, int pid, int release)
    {
        using var body = new MemoryStream();
        body.Write("DfuSe"u8);
        body.WriteByte(1);
        body.Write(new byte[4]); // the image size, filled in below
        body.WriteByte((byte)targets.Count);
        Span<byte> head = stackalloc byte[8];
        foreach (DfuTarget target in targets)
        {
            byte[] prefix = new byte[274];
            "Target"u8.CopyTo(prefix);
            prefix[6] = (byte)target.AlternateSetting;
            if (target.Name.Length > 0)
            {
                prefix[7] = 1;
                Encoding.ASCII.GetBytes(target.Name[..Math.Min(254, target.Name.Length)]).CopyTo(prefix, 11);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(266), (uint)target.Elements.Sum(static e => 8 + e.Data.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(270), (uint)target.Elements.Count);
            body.Write(prefix);
            foreach (DfuElement element in target.Elements)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(head, element.Address);
                BinaryPrimitives.WriteUInt32LittleEndian(head[4..], (uint)element.Data.Length);
                body.Write(head);
                body.Write(element.Data);
            }
        }

        byte[] bytes = body.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(6), (uint)bytes.Length);
        return WithSuffix(bytes, vid, pid, release, 0x011A);
    }

    /// <summary>ST's DfuSe container (UM0391): the prefix, then targets of elements.</summary>
    private static IReadOnlyList<DfuTarget> DfuSe(ReadOnlySpan<byte> body)
    {
        int targets = body[10];
        int at = 11;
        var result = new List<DfuTarget>();
        for (int t = 0; t < targets && at + 274 <= body.Length; t++)
        {
            if (Encoding.ASCII.GetString(body.Slice(at, 6)) != "Target")
            {
                throw new InvalidDataException($"DfuSe target {t + 1} has no 'Target' signature.");
            }

            int alternate = body[at + 6];
            bool named = BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 7)..]) != 0;
            string name = named ? Encoding.ASCII.GetString(body.Slice(at + 11, 255)).TrimEnd('\0') : "";
            int elements = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 270)..]);
            at += 274;
            var list = new List<DfuElement>();
            for (int e = 0; e < elements && at + 8 <= body.Length; e++)
            {
                uint address = BinaryPrimitives.ReadUInt32LittleEndian(body[at..]);
                int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 4)..]);
                at += 8;
                if (at + size > body.Length)
                {
                    throw new InvalidDataException($"DfuSe element {e + 1} of target {t + 1} runs past the end of the file.");
                }

                list.Add(new DfuElement(address, body.Slice(at, size).ToArray()));
                at += size;
            }

            result.Add(new DfuTarget(alternate, name, list));
        }

        return result;
    }

    /// <summary>Intel HEX: data records gathered into contiguous elements, with extended segment and linear addresses.</summary>
    private static IReadOnlyList<DfuElement> IntelHex(ReadOnlySpan<byte> body)
    {
        var bytes = new SortedDictionary<uint, byte>();
        uint upper = 0;
        int lineNumber = 0;
        foreach (string raw in Encoding.ASCII.GetString(body).Split('\n'))
        {
            lineNumber++;
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] != ':' || line.Length < 11 || (line.Length - 1) % 2 != 0)
            {
                throw new InvalidDataException($"Intel HEX line {lineNumber} is not a record.");
            }

            byte[] record = Convert.FromHexString(line[1..]);
            int count = record[0];
            if (record.Length != count + 5)
            {
                throw new InvalidDataException($"Intel HEX line {lineNumber} has the wrong length.");
            }

            byte sum = 0;
            foreach (byte b in record)
            {
                sum += b;
            }

            if (sum != 0)
            {
                throw new InvalidDataException($"Intel HEX line {lineNumber} fails its checksum.");
            }

            uint offset = (uint)((record[1] << 8) | record[2]);
            switch (record[3])
            {
                case 0x00:
                    for (int i = 0; i < count; i++)
                    {
                        bytes[upper + offset + (uint)i] = record[4 + i];
                    }

                    break;
                case 0x01:
                    return Gather(bytes);
                case 0x02:
                    upper = (uint)(((record[4] << 8) | record[5]) << 4);
                    break;
                case 0x04:
                    upper = (uint)(((record[4] << 8) | record[5]) << 16);
                    break;
            }
        }

        return Gather(bytes);
    }

    private static List<DfuElement> Gather(SortedDictionary<uint, byte> bytes)
    {
        var elements = new List<DfuElement>();
        var current = new List<byte>();
        uint start = 0;
        uint next = 0;
        foreach ((uint address, byte value) in bytes)
        {
            if (current.Count > 0 && address != next)
            {
                elements.Add(new DfuElement(start, current.ToArray()));
                current.Clear();
            }

            if (current.Count == 0)
            {
                start = address;
            }

            current.Add(value);
            next = address + 1;
        }

        if (current.Count > 0)
        {
            elements.Add(new DfuElement(start, current.ToArray()));
        }

        return elements;
    }

    /// <summary>A DfuSe address as text: "0x08000000".</summary>
    public static string Hex(uint address) => "0x" + address.ToString("X8", CultureInfo.InvariantCulture);
}
