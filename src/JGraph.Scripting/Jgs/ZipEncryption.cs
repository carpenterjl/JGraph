using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The two ways a ZIP archive's entries are encrypted, as R2025b's <c>zip</c> writes them and its
/// <c>unzip</c> reads them (open item 172, ADR 0357), in managed code over
/// <c>System.Security.Cryptography</c> alone: PKWARE's traditional encryption (ZipCrypto) and
/// WinZip's AES (AE-1 and AE-2). .NET's ZIP classes neither encrypt nor decrypt.
/// </summary>
/// <remarks>
/// <para>
/// What R2025b writes (measured, <c>probe_zip</c> and <c>probe_zip2</c>, 2026-11-14): every file
/// with content is deflated and encrypted (general-purpose flag bit 0); an empty file and a folder
/// are written plain. ZipCrypto prefixes the 12-byte encryption header. AES is compression method
/// 99 with the <c>0x9901</c> extra field (vendor <c>AE</c>, the strength, the real method 8): a salt
/// of 8 bytes (AES-128) or 16 (AES-256), a two-byte password verifier, the data in counter mode, and
/// the first ten bytes of an HMAC-SHA1 over it. Keys come from PBKDF2-HMAC-SHA1 over 1,000 rounds.
/// A file under 20 bytes is AE-2, its CRC written as zero; a longer one AE-1, with its CRC. The
/// password's bytes are its UTF-8.
/// </para>
/// </remarks>
internal static class ZipEncryption
{
    /// <summary>WinZip's compression method for an AES-encrypted entry.</summary>
    public const ushort AesMethod = 99;

    /// <summary>The extra field WinZip's AES puts on an entry.</summary>
    public const ushort AesExtraId = 0x9901;

    private const int AesRounds = 1000;
    private const int AesAuthLength = 10;
    private const int ZipCryptoHeader = 12;

    private static readonly uint[] CrcTable = MakeCrcTable();

    /// <summary>The bytes a password stands for in an archive.</summary>
    public static byte[] PasswordBytes(string password) => Encoding.UTF8.GetBytes(password);

    // --- CRC-32 ----------------------------------------------------------------------------------

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint CrcByte(uint crc, byte b) => CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

    /// <summary>The ZIP CRC-32 of <paramref name="data"/>.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc = CrcByte(crc, b);
        }

        return ~crc;
    }

    // --- Deflate ---------------------------------------------------------------------------------

    /// <summary>The raw deflate stream of <paramref name="data"/>.</summary>
    public static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Data under compression method <paramref name="method"/> (0 stored, 8 deflated) as it was; null for a method this does not read.</summary>
    public static byte[]? Inflate(byte[] data, int method)
    {
        if (method == 0)
        {
            return data;
        }

        if (method != 8)
        {
            return null;
        }

        using var input = new MemoryStream(data);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    // --- ZipCrypto -------------------------------------------------------------------------------

    /// <summary>PKWARE's three keys, initialised from a password.</summary>
    private struct ZipCryptoKeys
    {
        private uint _k0, _k1, _k2;

        public ZipCryptoKeys(byte[] password)
        {
            _k0 = 0x12345678;
            _k1 = 0x23456789;
            _k2 = 0x34567890;
            foreach (byte b in password)
            {
                Update(b);
            }
        }

        private void Update(byte plain)
        {
            _k0 = CrcByte(_k0, plain);
            _k1 = ((_k1 + (_k0 & 0xFF)) * 134775813) + 1;
            _k2 = CrcByte(_k2, (byte)(_k1 >> 24));
        }

        private readonly byte Stream()
        {
            uint t = (_k2 | 2) & 0xFFFF;
            return (byte)((t * (t ^ 1)) >> 8);
        }

        public byte Encrypt(byte plain)
        {
            byte cipher = (byte)(plain ^ Stream());
            Update(plain);
            return cipher;
        }

        public byte Decrypt(byte cipher)
        {
            byte plain = (byte)(cipher ^ Stream());
            Update(plain);
            return plain;
        }
    }

    /// <summary>
    /// <paramref name="data"/> encrypted with ZipCrypto: the 12-byte header (eleven random bytes and
    /// <paramref name="check"/>, the CRC's high byte when the sizes are in the local header) and the data.
    /// </summary>
    public static byte[] ZipCryptoEncrypt(byte[] data, byte[] password, byte check)
    {
        var keys = new ZipCryptoKeys(password);
        var output = new byte[ZipCryptoHeader + data.Length];
        RandomNumberGenerator.Fill(output.AsSpan(0, ZipCryptoHeader - 1));
        output[ZipCryptoHeader - 1] = check;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = keys.Encrypt(i < ZipCryptoHeader ? output[i] : data[i - ZipCryptoHeader]);
        }

        return output;
    }

    /// <summary>
    /// ZipCrypto data decrypted; null when the header's last byte is not <paramref name="check"/>,
    /// which is how a wrong password shows (255 times in 256).
    /// </summary>
    public static byte[]? ZipCryptoDecrypt(byte[] data, byte[] password, byte check)
    {
        if (data.Length < ZipCryptoHeader)
        {
            return null;
        }

        var keys = new ZipCryptoKeys(password);
        byte last = 0;
        for (int i = 0; i < ZipCryptoHeader; i++)
        {
            last = keys.Decrypt(data[i]);
        }

        if (last != check)
        {
            return null;
        }

        var output = new byte[data.Length - ZipCryptoHeader];
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = keys.Decrypt(data[ZipCryptoHeader + i]);
        }

        return output;
    }

    // --- WinZip AES ------------------------------------------------------------------------------

    /// <summary>The key length in bytes for a strength (1 AES-128, 2 AES-192, 3 AES-256).</summary>
    public static int AesKeyLength(int strength) => strength switch
    {
        1 => 16,
        2 => 24,
        3 => 32,
        _ => throw new ArgumentOutOfRangeException(nameof(strength)),
    };

    private static (byte[] Key, byte[] Mac, byte[] Verifier) AesKeys(byte[] password, byte[] salt, int keyLength)
    {
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, AesRounds, HashAlgorithmName.SHA1, (2 * keyLength) + 2);
        return (derived[..keyLength], derived[keyLength..(2 * keyLength)], derived[(2 * keyLength)..]);
    }

    /// <summary>AES in WinZip's counter mode: the counter a 16-byte little-endian number from 1, its blocks encrypted and laid over the data.</summary>
    private static void AesCtr(byte[] key, Span<byte> data)
    {
        using Aes aes = Aes.Create();
        aes.Key = key;
        var counter = new byte[16];
        var block = new byte[16];
        for (int at = 0; at < data.Length; at += 16)
        {
            for (int i = 0; i < 16 && ++counter[i] == 0; i++)
            {
            }

            aes.EncryptEcb(counter, block, PaddingMode.None);
            int n = Math.Min(16, data.Length - at);
            for (int i = 0; i < n; i++)
            {
                data[at + i] ^= block[i];
            }
        }
    }

    /// <summary><paramref name="data"/> encrypted with WinZip's AES: the salt, the verifier, the data, and the authentication code.</summary>
    public static byte[] AesEncrypt(byte[] data, byte[] password, int strength)
    {
        int keyLength = AesKeyLength(strength);
        byte[] salt = RandomNumberGenerator.GetBytes(keyLength / 2);
        (byte[] key, byte[] mac, byte[] verifier) = AesKeys(password, salt, keyLength);
        byte[] cipher = (byte[])data.Clone();
        AesCtr(key, cipher);
        byte[] code = HMACSHA1.HashData(mac, cipher);
        return [.. salt, .. verifier, .. cipher, .. code.AsSpan(0, AesAuthLength)];
    }

    /// <summary>
    /// WinZip AES data decrypted; null when the password's verifier or the authentication code does
    /// not match, which is how a wrong password shows.
    /// </summary>
    public static byte[]? AesDecrypt(byte[] data, byte[] password, int strength)
    {
        int keyLength = AesKeyLength(strength);
        int saltLength = keyLength / 2;
        if (data.Length < saltLength + 2 + AesAuthLength)
        {
            return null;
        }

        (byte[] key, byte[] mac, byte[] verifier) = AesKeys(password, data[..saltLength], keyLength);
        if (!data.AsSpan(saltLength, 2).SequenceEqual(verifier))
        {
            return null;
        }

        byte[] cipher = data[(saltLength + 2)..^AesAuthLength];
        byte[] code = HMACSHA1.HashData(mac, cipher);
        if (!CryptographicOperations.FixedTimeEquals(code.AsSpan(0, AesAuthLength), data.AsSpan(data.Length - AesAuthLength)))
        {
            return null;
        }

        AesCtr(key, cipher);
        return cipher;
    }

    /// <summary>The AES extra field's version (1 or 2), strength and real method; null when <paramref name="extra"/> holds none.</summary>
    public static (int Version, int Strength, int Method)? AesField(ReadOnlySpan<byte> extra)
    {
        int at = 0;
        while (at + 4 <= extra.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra[at..]);
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(extra[(at + 2)..]);
            if (id == AesExtraId && size >= 7 && at + 4 + size <= extra.Length)
            {
                ReadOnlySpan<byte> d = extra.Slice(at + 4, size);
                return (BinaryPrimitives.ReadUInt16LittleEndian(d), d[4], BinaryPrimitives.ReadUInt16LittleEndian(d[5..]));
            }

            at += 4 + size;
        }

        return null;
    }

    /// <summary>The AES extra field for an entry: AE-<paramref name="version"/>, vendor <c>AE</c>, the strength, deflate.</summary>
    public static byte[] AesExtra(int version, int strength)
    {
        var extra = new byte[11];
        BinaryPrimitives.WriteUInt16LittleEndian(extra, AesExtraId);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(2), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(4), (ushort)version);
        extra[6] = (byte)'A';
        extra[7] = (byte)'E';
        extra[8] = (byte)strength;
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(9), 8);
        return extra;
    }

    // --- Archives --------------------------------------------------------------------------------

    /// <summary>One entry as the central directory records it.</summary>
    public sealed record CentralEntry(string Name, ushort Flags, ushort Method, ushort Time, ushort Date, uint Crc,
        long CompressedSize, long Size, long LocalOffset, byte[] Extra)
    {
        public bool IsEncrypted => (Flags & 1) != 0;
    }

    /// <summary>The central directory of the archive in <paramref name="file"/>, in its order; null when it cannot be found.</summary>
    public static List<CentralEntry>? CentralDirectory(Stream file)
    {
        long length = file.Length;
        int tail = (int)Math.Min(length, 22 + 65535);
        var end = new byte[tail];
        file.Seek(length - tail, SeekOrigin.Begin);
        file.ReadExactly(end);
        int eocd = -1;
        for (int i = tail - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(i)) == 0x06054B50)
            {
                eocd = i;
                break;
            }
        }

        if (eocd < 0)
        {
            return null;
        }

        long count = BinaryPrimitives.ReadUInt16LittleEndian(end.AsSpan(eocd + 10));
        long size = BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(eocd + 12));
        long offset = BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(eocd + 16));

        // ZIP64: the locator just before the end record names the ZIP64 end record.
        if ((offset == 0xFFFFFFFF || count == 0xFFFF) && eocd >= 20
            && BinaryPrimitives.ReadUInt32LittleEndian(end.AsSpan(eocd - 20)) == 0x07064B50)
        {
            long at64 = BinaryPrimitives.ReadInt64LittleEndian(end.AsSpan(eocd - 12));
            var record = new byte[56];
            file.Seek(at64, SeekOrigin.Begin);
            file.ReadExactly(record);
            count = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(32));
            size = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(40));
            offset = BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(48));
        }

        var directory = new byte[size];
        file.Seek(offset, SeekOrigin.Begin);
        file.ReadExactly(directory);
        var entries = new List<CentralEntry>((int)count);
        int p = 0;
        for (long n = 0; n < count && p + 46 <= directory.Length; n++)
        {
            ReadOnlySpan<byte> h = directory.AsSpan(p);
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(h[8..]);
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(h[28..]);
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(h[30..]);
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(h[32..]);
            byte[] extra = directory[(p + 46 + nameLength)..(p + 46 + nameLength + extraLength)];
            long compressed = BinaryPrimitives.ReadUInt32LittleEndian(h[20..]);
            long uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(h[24..]);
            long local = BinaryPrimitives.ReadUInt32LittleEndian(h[42..]);
            ReadZip64(extra, ref uncompressed, ref compressed, ref local);
            Encoding encoding = (flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.Latin1;
            entries.Add(new CentralEntry(
                encoding.GetString(directory, p + 46, nameLength), flags, BinaryPrimitives.ReadUInt16LittleEndian(h[10..]),
                BinaryPrimitives.ReadUInt16LittleEndian(h[12..]), BinaryPrimitives.ReadUInt16LittleEndian(h[14..]),
                BinaryPrimitives.ReadUInt32LittleEndian(h[16..]), compressed, uncompressed, local, extra));
            p += 46 + nameLength + extraLength + commentLength;
        }

        return entries;
    }

    private static void ReadZip64(byte[] extra, ref long size, ref long compressed, ref long local)
    {
        int at = 0;
        while (at + 4 <= extra.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(at));
            ushort length = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(at + 2));
            if (id == 1)
            {
                int q = at + 4;
                if (size == 0xFFFFFFFF && q + 8 <= at + 4 + length)
                {
                    size = BinaryPrimitives.ReadInt64LittleEndian(extra.AsSpan(q));
                    q += 8;
                }

                if (compressed == 0xFFFFFFFF && q + 8 <= at + 4 + length)
                {
                    compressed = BinaryPrimitives.ReadInt64LittleEndian(extra.AsSpan(q));
                    q += 8;
                }

                if (local == 0xFFFFFFFF && q + 8 <= at + 4 + length)
                {
                    local = BinaryPrimitives.ReadInt64LittleEndian(extra.AsSpan(q));
                }

                return;
            }

            at += 4 + length;
        }
    }

    /// <summary>The bytes an entry's local record holds after its header, as stored (encrypted when it is).</summary>
    public static byte[] StoredData(Stream file, CentralEntry entry)
    {
        var header = new byte[30];
        file.Seek(entry.LocalOffset, SeekOrigin.Begin);
        file.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x04034B50)
        {
            throw new InvalidDataException("an entry's local header is missing");
        }

        int skip = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        file.Seek(entry.LocalOffset + 30 + skip, SeekOrigin.Begin);
        var data = new byte[entry.CompressedSize];
        file.ReadExactly(data);
        return data;
    }

    /// <summary>Why an encrypted entry could not be read: the password is wrong, or it is encrypted in a way this does not read.</summary>
    public enum Failure
    {
        None,
        WrongPassword,
        Unsupported,
    }

    /// <summary>An encrypted entry's content, decrypted and decompressed and its CRC held; null with the failure otherwise.</summary>
    public static byte[]? Decrypt(Stream file, CentralEntry entry, byte[] password, out Failure failure)
    {
        failure = Failure.None;
        if ((entry.Flags & 0x40) != 0)
        {
            failure = Failure.Unsupported; // PKWARE's strong encryption
            return null;
        }

        byte[] stored = StoredData(file, entry);
        byte[]? plain;
        int method;
        bool checkCrc;
        if (entry.Method == AesMethod)
        {
            if (AesField(entry.Extra) is not { } aes || aes.Strength is < 1 or > 3)
            {
                failure = Failure.Unsupported;
                return null;
            }

            plain = AesDecrypt(stored, password, aes.Strength);
            method = aes.Method;
            checkCrc = aes.Version == 1;
        }
        else
        {
            // With the sizes after the data (bit 3) the check is the time's high byte, else the CRC's.
            byte check = (entry.Flags & 8) != 0 ? (byte)(entry.Time >> 8) : (byte)(entry.Crc >> 24);
            plain = ZipCryptoDecrypt(stored, password, check);
            method = entry.Method;
            checkCrc = true;
        }

        if (plain is null)
        {
            failure = Failure.WrongPassword;
            return null;
        }

        byte[]? content;
        try
        {
            content = Inflate(plain, method);
        }
        catch (InvalidDataException)
        {
            failure = Failure.WrongPassword; // a wrong ZipCrypto password that passed the check byte
            return null;
        }

        if (content is null)
        {
            failure = Failure.Unsupported;
            return null;
        }

        if (checkCrc && Crc32(content) != entry.Crc)
        {
            failure = Failure.WrongPassword;
            return null;
        }

        return content;
    }

    /// <summary>One entry to write: its name in the archive, a folder or a file's content, and when it was last written.</summary>
    public sealed record NewEntry(string Name, bool IsFolder, byte[] Content, DateTime Written);

    /// <summary>
    /// Writes an archive of <paramref name="entries"/> to <paramref name="output"/>, each file with
    /// content deflated and encrypted with <paramref name="password"/>: ZipCrypto when
    /// <paramref name="strength"/> is 0, else WinZip AES of that strength. Folders and empty files
    /// are written plain, as R2025b writes them.
    /// </summary>
    public static void WriteArchive(Stream output, IReadOnlyList<NewEntry> entries, byte[] password, int strength)
    {
        using var central = new MemoryStream();
        long count = 0;
        foreach (NewEntry entry in entries)
        {
            byte[] name = Encoding.UTF8.GetBytes(entry.Name);
            bool utf8 = name.Any(static b => b >= 0x80);
            (ushort time, ushort date) = DosTime(entry.Written);
            ushort flags = (ushort)(utf8 ? 0x800 : 0);
            ushort method = 0;
            uint crc = 0;
            byte[] data = [];
            byte[] extra = [];
            if (!entry.IsFolder)
            {
                crc = Crc32(entry.Content);
                byte[] deflated = Deflate(entry.Content);
                method = 8;
                data = deflated;
                if (entry.Content.Length > 0)
                {
                    flags |= 1;
                    if (strength == 0)
                    {
                        data = ZipCryptoEncrypt(deflated, password, (byte)(crc >> 24));
                    }
                    else
                    {
                        int version = entry.Content.Length < 20 ? 2 : 1;
                        data = AesEncrypt(deflated, password, strength);
                        extra = AesExtra(version, strength);
                        method = AesMethod;
                        if (version == 2)
                        {
                            crc = 0;
                        }
                    }
                }
            }

            long offset = output.Position;
            if (offset + data.LongLength > uint.MaxValue)
            {
                throw new IOException("the archive would need ZIP64, which an encrypted archive here does not write");
            }

            var local = new byte[30];
            BinaryPrimitives.WriteUInt32LittleEndian(local, 0x04034B50);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(6), flags);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(8), method);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(10), time);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(12), date);
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(14), crc);
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(18), (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(local.AsSpan(22), (uint)entry.Content.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(26), (ushort)name.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(local.AsSpan(28), (ushort)extra.Length);
            output.Write(local);
            output.Write(name);
            output.Write(extra);
            output.Write(data);

            var record = new byte[46];
            BinaryPrimitives.WriteUInt32LittleEndian(record, 0x02014B50);
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), 20); // made by MS-DOS, version 2.0
            local.AsSpan(4, 26).CopyTo(record.AsSpan(6));
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(30), (ushort)extra.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(38), entry.IsFolder ? 0x10u : 0u); // the folder attribute
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(42), (uint)offset);
            central.Write(record);
            central.Write(name);
            central.Write(extra);
            count++;
        }

        if (count > 0xFFFE || output.Position > uint.MaxValue)
        {
            throw new IOException("the archive would need ZIP64, which an encrypted archive here does not write");
        }

        long centralAt = output.Position;
        central.Position = 0;
        central.CopyTo(output);
        var tail = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(tail, 0x06054B50);
        BinaryPrimitives.WriteUInt16LittleEndian(tail.AsSpan(8), (ushort)count);
        BinaryPrimitives.WriteUInt16LittleEndian(tail.AsSpan(10), (ushort)count);
        BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(12), (uint)central.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(16), (uint)centralAt);
        output.Write(tail);
    }

    /// <summary>MS-DOS time and date words, as an entry records when it was written (1980 at the earliest).</summary>
    private static (ushort Time, ushort Date) DosTime(DateTime written)
    {
        if (written.Year < 1980)
        {
            written = new DateTime(1980, 1, 1);
        }

        return ((ushort)((written.Hour << 11) | (written.Minute << 5) | (written.Second / 2)),
            (ushort)(((written.Year - 1980) << 9) | (written.Month << 5) | written.Day));
    }

    /// <summary>The time an entry's DOS words record.</summary>
    public static DateTime FromDosTime(ushort time, ushort date)
    {
        try
        {
            return new DateTime(1980 + (date >> 9), Math.Max(1, (date >> 5) & 15), Math.Max(1, date & 31),
                time >> 11, (time >> 5) & 63, Math.Min(59, (time & 31) * 2));
        }
        catch (ArgumentOutOfRangeException)
        {
            return new DateTime(1980, 1, 1);
        }
    }
}
