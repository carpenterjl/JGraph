using System.IO.Compression;
using JGraph.Scripting.Jgs;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>
/// ZipCrypto and WinZip AES in <see cref="ZipEncryption"/> (open item 172, ADR 0357). R2025b's own
/// archives, and archives written here read by R2025b, are the fixture <c>oi171_gzip_tar</c>'s and
/// the wave's probe's; these hold the pieces.
/// </summary>
public sealed class ZipEncryptionTests
{
    private static readonly byte[] Password = ZipEncryption.PasswordBytes("secret é");

    private static List<ZipEncryption.NewEntry> Entries() =>
    [
        new("folder/", true, [], new DateTime(2026, 11, 14, 10, 0, 0)),
        new("folder/long.txt", false, System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("hello world ", 40))), new DateTime(2026, 11, 14, 10, 0, 2)),
        new("short.txt", false, "alpha"u8.ToArray(), new DateTime(2026, 11, 14, 10, 0, 4)),
        new("zero.txt", false, [], new DateTime(2026, 11, 14, 10, 0, 6)),
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void AnArchiveWrittenEncryptedReadsBackWithItsPassword_AndRefusesAnother(int strength)
    {
        using var stream = new MemoryStream();
        ZipEncryption.WriteArchive(stream, Entries(), Password, strength);
        List<ZipEncryption.CentralEntry> records = ZipEncryption.CentralDirectory(stream)!;
        Assert.Equal(["folder/", "folder/long.txt", "short.txt", "zero.txt"], records.Select(static r => r.Name));

        // As R2025b writes them: a folder and an empty file plain, every file with content encrypted.
        Assert.Equal([false, true, true, false], records.Select(static r => r.IsEncrypted));
        if (strength > 0)
        {
            Assert.Equal<(int, int, int)?>((1, strength, 8), ZipEncryption.AesField(records[1].Extra));
            Assert.Equal<(int, int, int)?>((2, strength, 8), ZipEncryption.AesField(records[2].Extra)); // under 20 bytes: AE-2, no CRC
            Assert.Equal(0u, records[2].Crc);
            Assert.Equal(ZipEncryption.AesMethod, records[1].Method);
        }

        List<ZipEncryption.NewEntry> entries = Entries();
        for (int i = 1; i <= 2; i++)
        {
            Assert.Equal(entries[i].Content, ZipEncryption.Decrypt(stream, records[i], Password, out ZipEncryption.Failure failure));
            Assert.Equal(ZipEncryption.Failure.None, failure);
            Assert.Null(ZipEncryption.Decrypt(stream, records[i], ZipEncryption.PasswordBytes("nope"), out failure));
            Assert.Equal(ZipEncryption.Failure.WrongPassword, failure);
        }

        // .NET's reader lists it, and reads the plain entries.
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Equal([false, true, true, false], archive.Entries.Select(static e => e.IsEncrypted));
        Assert.Equal(new DateTime(2026, 11, 14, 10, 0, 2), archive.Entries[1].LastWriteTime.DateTime);
    }

    [Fact]
    public void Crc32IsZipsCrc()
    {
        Assert.Equal(0xCBF43926u, ZipEncryption.Crc32("123456789"u8));
        Assert.Equal(0u, ZipEncryption.Crc32([]));
    }

    [Fact]
    public void AesDataIsSaltVerifierCipherAndTenBytesOfCode()
    {
        byte[] data = "the quick brown fox"u8.ToArray();
        byte[] sealed128 = ZipEncryption.AesEncrypt(data, Password, 1);
        byte[] sealed256 = ZipEncryption.AesEncrypt(data, Password, 3);
        Assert.Equal(8 + 2 + data.Length + 10, sealed128.Length);
        Assert.Equal(16 + 2 + data.Length + 10, sealed256.Length);
        Assert.Equal(data, ZipEncryption.AesDecrypt(sealed256, Password, 3));

        // A changed byte of the data fails the authentication code.
        sealed256[20] ^= 1;
        Assert.Null(ZipEncryption.AesDecrypt(sealed256, Password, 3));
    }
}
