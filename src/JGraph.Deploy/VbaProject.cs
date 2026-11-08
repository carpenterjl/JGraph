using System.Buffers.Binary;
using System.Text;

namespace JGraph.Deploy;

/// <summary>A module of a VBA project: its name, its source (the <c>Attribute</c> lines included) and whether it is a document module.</summary>
public sealed record VbaModule(string Name, string Source, bool IsDocument);

/// <summary>What <see cref="VbaProject.Read"/> finds: the project's name and id, its modules with their sources, its references' library ids and the PROJECT stream's text.</summary>
public sealed record VbaProjectContents(string Name, Guid Id, IReadOnlyList<VbaModule> Modules, IReadOnlyList<string> References, string ProjectStream);

/// <summary>
/// A source-only VBA project (MS-OVBA) written as the <c>vbaProject.bin</c> part of an Excel add-in
/// workbook (compiler and SDK plan, stage K17e, decision KO12) and read back: the <c>VBA/dir</c>
/// stream's records, the <c>_VBA_PROJECT</c> stream with no performance cache (version 0xFFFF, so
/// Excel compiles the modules when it opens the file), one compressed stream per module holding its
/// source alone (its offset 0), the <c>PROJECT</c> stream's properties with the protection, password
/// and visibility fields encrypted as the format asks, and <c>PROJECTwm</c>. The references are the
/// two every workbook project carries (<c>stdole</c> and <c>Office</c>); VBA and Excel's own are
/// implicit. The layout follows MathWorks' <c>FunctionWizard2007.xlam</c>, read as a reference.
/// </summary>
public static class VbaProject
{
    private const ushort SysKind = 0x0001;
    private const ushort Lcid = 0x0002;
    private const ushort CodePage = 0x0003;
    private const ushort ProjectName = 0x0004;
    private const ushort DocString = 0x0005;
    private const ushort HelpFilePath = 0x0006;
    private const ushort HelpContext = 0x0007;
    private const ushort LibFlags = 0x0008;
    private const ushort Version = 0x0009;
    private const ushort Constants = 0x000C;
    private const ushort ReferenceRegistered = 0x000D;
    private const ushort Modules = 0x000F;
    private const ushort Terminator = 0x0010;
    private const ushort ProjectCookie = 0x0013;
    private const ushort LcidInvoke = 0x0014;
    private const ushort ReferenceName = 0x0016;
    private const ushort ModuleName = 0x0019;
    private const ushort ModuleStreamName = 0x001A;
    private const ushort ModuleDocString = 0x001C;
    private const ushort ModuleHelpContext = 0x001E;
    private const ushort ModuleTypeProcedural = 0x0021;
    private const ushort ModuleTypeDocument = 0x0022;
    private const ushort ModuleTerminator = 0x002B;
    private const ushort ModuleCookie = 0x002C;
    private const ushort ModuleOffset = 0x0031;
    private const ushort ModuleStreamNameUnicode = 0x0032;
    private const ushort ConstantsUnicode = 0x003C;
    private const ushort HelpFilePathUnicode = 0x003D;
    private const ushort ReferenceNameUnicode = 0x003E;
    private const ushort DocStringUnicode = 0x0040;
    private const ushort ModuleNameUnicode = 0x0047;
    private const ushort ModuleDocStringUnicode = 0x0048;

    private static readonly Encoding Mbcs = Encoding.Latin1;

    private static readonly (string Name, string LibId)[] References =
    [
        ("stdole", @"*\G{00020430-0000-0000-C000-000000000046}#2.0#0#C:\Windows\System32\stdole2.tlb#OLE Automation"),
        ("Office", @"*\G{2DF8D04C-5BFA-101B-BDE5-00AA0044DE52}#2.0#0#C:\Program Files\Common Files\Microsoft Shared\OFFICE16\MSO.DLL#Microsoft Office 16.0 Object Library"),
    ];

    /// <summary>The attribute lines of a workbook's document module (<c>ThisWorkbook</c>), as Excel exports them.</summary>
    public static string ThisWorkbookSource { get; } =
        "Attribute VB_Name = \"ThisWorkbook\"\r\n"
        + "Attribute VB_Base = \"0{00020819-0000-0000-C000-000000000046}\"\r\n"
        + "Attribute VB_GlobalNameSpace = False\r\n"
        + "Attribute VB_Creatable = False\r\n"
        + "Attribute VB_PredeclaredId = True\r\n"
        + "Attribute VB_Exposed = True\r\n"
        + "Attribute VB_TemplateDerived = False\r\n"
        + "Attribute VB_Customizable = True\r\n";

    /// <summary>A standard module's source from its text: the <c>Attribute VB_Name</c> line, then the text.</summary>
    public static VbaModule StandardModule(string name, string text) => new(name, $"Attribute VB_Name = \"{name}\"\r\n" + text, IsDocument: false);

    /// <summary>The workbook's document module, with no code of its own.</summary>
    public static VbaModule ThisWorkbook { get; } = new("ThisWorkbook", ThisWorkbookSource, IsDocument: true);

    /// <summary>Writes the project as a compound file.</summary>
    public static byte[] Write(string projectName, Guid projectId, IReadOnlyList<VbaModule> modules)
    {
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(modules);
        string id = "{" + projectId.ToString("D").ToUpperInvariant() + "}";

        var dir = new MemoryStream();
        Record(dir, SysKind, Int32Bytes(3));
        Record(dir, Lcid, Int32Bytes(0x409));
        Record(dir, LcidInvoke, Int32Bytes(0x409));
        Record(dir, CodePage, Int16Bytes(1252));
        Record(dir, ProjectName, Mbcs.GetBytes(projectName));
        Record(dir, DocString, []);
        Record(dir, DocStringUnicode, []);
        Record(dir, HelpFilePath, []);
        Record(dir, HelpFilePathUnicode, []);
        Record(dir, HelpContext, Int32Bytes(0));
        Record(dir, LibFlags, Int32Bytes(0));
        dir.Write(Int16Bytes(Version));
        dir.Write(Int32Bytes(4));
        dir.Write(Int32Bytes(1));
        dir.Write(Int16Bytes(0));
        Record(dir, Constants, []);
        Record(dir, ConstantsUnicode, []);
        foreach ((string name, string libId) in References)
        {
            Record(dir, ReferenceName, Mbcs.GetBytes(name));
            Record(dir, ReferenceNameUnicode, Encoding.Unicode.GetBytes(name));
            byte[] lib = Mbcs.GetBytes(libId);
            var body = new byte[4 + lib.Length + 6];
            BinaryPrimitives.WriteInt32LittleEndian(body, lib.Length);
            lib.CopyTo(body, 4);
            Record(dir, ReferenceRegistered, body);
        }

        Record(dir, Modules, Int16Bytes((ushort)modules.Count));
        Record(dir, ProjectCookie, Int16Bytes(0xFFFF));
        foreach (VbaModule module in modules)
        {
            Record(dir, ModuleName, Mbcs.GetBytes(module.Name));
            Record(dir, ModuleNameUnicode, Encoding.Unicode.GetBytes(module.Name));
            Record(dir, ModuleStreamName, Mbcs.GetBytes(module.Name));
            Record(dir, ModuleStreamNameUnicode, Encoding.Unicode.GetBytes(module.Name));
            Record(dir, ModuleDocString, []);
            Record(dir, ModuleDocStringUnicode, []);
            Record(dir, ModuleOffset, Int32Bytes(0));
            Record(dir, ModuleHelpContext, Int32Bytes(0));
            Record(dir, ModuleCookie, Int16Bytes(0xFFFF));
            Record(dir, module.IsDocument ? ModuleTypeDocument : ModuleTypeProcedural, []);
            Record(dir, ModuleTerminator, []);
        }

        Record(dir, Terminator, []);

        var project = new StringBuilder();
        project.Append("ID=\"").Append(id).Append("\"\r\n");
        foreach (VbaModule module in modules)
        {
            project.Append(module.IsDocument ? $"Document={module.Name}/&H00000000" : $"Module={module.Name}").Append("\r\n");
        }

        project.Append("Name=\"").Append(projectName).Append("\"\r\n");
        project.Append("HelpContextID=\"0\"\r\n");
        project.Append("VersionCompatible32=\"393222000\"\r\n");
        project.Append("CMG=\"").Append(Encrypt(id, new byte[4], 0x58)).Append("\"\r\n");
        project.Append("DPB=\"").Append(Encrypt(id, new byte[1], 0x5A)).Append("\"\r\n");
        project.Append("GC=\"").Append(Encrypt(id, [0xFF], 0x5C)).Append("\"\r\n");
        project.Append("\r\n[Host Extender Info]\r\n&H00000001={3832D640-CF90-11CF-8E43-00A0C911005A};VBE;&H00000000\r\n");
        project.Append("\r\n[Workspace]\r\n");
        foreach (VbaModule module in modules)
        {
            project.Append(module.Name).Append("=0, 0, 0, 0, C\r\n");
        }

        var wm = new MemoryStream();
        foreach (VbaModule module in modules)
        {
            wm.Write(Mbcs.GetBytes(module.Name));
            wm.WriteByte(0);
            wm.Write(Encoding.Unicode.GetBytes(module.Name));
            wm.Write([0, 0]);
        }

        wm.Write([0, 0]);

        var vba = new CompoundFileStorage { Name = "VBA" };
        vba.Streams.Add(("dir", Compress(dir.ToArray())));
        vba.Streams.Add(("_VBA_PROJECT", [0xCC, 0x61, 0xFF, 0xFF, 0x00, 0x00, 0x00]));
        foreach (VbaModule module in modules)
        {
            vba.Streams.Add((module.Name, Compress(Mbcs.GetBytes(module.Source))));
        }

        var root = new CompoundFileStorage { Name = "Root Entry" };
        root.Storages.Add(vba);
        root.Streams.Add(("PROJECT", Mbcs.GetBytes(project.ToString())));
        root.Streams.Add(("PROJECTwm", wm.ToArray()));
        return CompoundFile.Write(root);
    }

    /// <summary>Reads a project back: its name and id, its modules' sources, its references and the PROJECT stream.</summary>
    /// <exception cref="InvalidDataException">The bytes are not a VBA project this reader takes.</exception>
    public static VbaProjectContents Read(byte[] bytes)
    {
        CompoundFileStorage root = CompoundFile.Read(bytes);
        CompoundFileStorage vba = root.Storage("VBA") ?? throw new InvalidDataException("The compound file has no VBA storage.");
        byte[] dir = Decompress(vba.Stream("dir") ?? throw new InvalidDataException("The VBA storage has no dir stream."));
        string name = string.Empty;
        var references = new List<string>();
        var modules = new List<VbaModule>();
        string? moduleName = null;
        string? streamName = null;
        int offset = 0;
        bool isDocument = false;
        int at = 0;
        while (at + 6 <= dir.Length)
        {
            ushort recordId = BinaryPrimitives.ReadUInt16LittleEndian(dir.AsSpan(at));
            if (recordId == Version)
            {
                at += 12;
                continue;
            }

            int size = BinaryPrimitives.ReadInt32LittleEndian(dir.AsSpan(at + 2));
            if (size < 0 || at + 6 + size > dir.Length)
            {
                throw new InvalidDataException($"The dir stream's record {recordId:X4} is longer than the stream.");
            }

            ReadOnlySpan<byte> body = dir.AsSpan(at + 6, size);
            switch (recordId)
            {
                case ProjectName:
                    name = Mbcs.GetString(body);
                    break;
                case ReferenceRegistered:
                    int libLength = BinaryPrimitives.ReadInt32LittleEndian(body);
                    references.Add(Mbcs.GetString(body.Slice(4, Math.Min(libLength, body.Length - 4))));
                    break;
                case ModuleName:
                    moduleName = Mbcs.GetString(body);
                    break;
                case ModuleStreamName:
                    streamName = Mbcs.GetString(body);
                    break;
                case ModuleOffset:
                    offset = BinaryPrimitives.ReadInt32LittleEndian(body);
                    break;
                case ModuleTypeDocument:
                    isDocument = true;
                    break;
                case ModuleTypeProcedural:
                    isDocument = false;
                    break;
                case ModuleTerminator:
                    byte[] stream = vba.Stream(streamName ?? moduleName ?? string.Empty)
                        ?? throw new InvalidDataException($"The module {moduleName} has no stream.");
                    modules.Add(new VbaModule(moduleName ?? string.Empty, Mbcs.GetString(Decompress(stream[Math.Min(offset, stream.Length)..])), isDocument));
                    moduleName = streamName = null;
                    offset = 0;
                    isDocument = false;
                    break;
                case Terminator:
                    at = dir.Length;
                    continue;
                default:
                    break;
            }

            at += 6 + size;
        }

        string projectStream = Mbcs.GetString(root.Stream("PROJECT") ?? []);
        Guid id = Guid.Empty;
        foreach (string line in projectStream.Split('\n'))
        {
            if (line.StartsWith("ID=\"", StringComparison.Ordinal))
            {
                _ = Guid.TryParse(line.Trim().TrimEnd('"')[4..], out id);
            }
        }

        return new VbaProjectContents(name, id, modules, references, projectStream);
    }

    /// <summary>
    /// Compresses <paramref name="data"/> as MS-OVBA 2.4.1 does: a signature byte, then 4,096-byte
    /// chunks each written as tokens (a flag byte per eight: a literal byte, or a copy token of an
    /// offset and length within the chunk, its bit split by how far into the chunk it stands) or,
    /// when the tokens would not fit the chunk's size field, raw.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        var output = new MemoryStream();
        output.WriteByte(0x01);
        for (int start = 0; start < data.Length; start += 4096)
        {
            ReadOnlySpan<byte> chunk = data.Slice(start, Math.Min(4096, data.Length - start));
            byte[] tokens = CompressChunk(chunk);
            if (tokens.Length >= 4096)
            {
                output.Write(Int16Bytes((ushort)((4096 - 3) | 0x3000)));
                output.Write(chunk);
                output.Write(new byte[4096 - chunk.Length]);
            }
            else
            {
                output.Write(Int16Bytes((ushort)((tokens.Length + 2 - 3) | 0x3000 | 0x8000)));
                output.Write(tokens);
            }
        }

        return output.ToArray();
    }

    /// <summary>Decompresses what <see cref="Compress"/> (or Office) wrote.</summary>
    /// <exception cref="InvalidDataException">The bytes do not begin with the container's signature.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data[0] != 0x01)
        {
            throw new InvalidDataException("Not an MS-OVBA compressed container.");
        }

        var output = new List<byte>(data.Length * 4);
        int at = 1;
        while (at + 2 <= data.Length)
        {
            ushort header = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
            at += 2;
            int size = (header & 0x0FFF) + 3;
            bool compressed = (header & 0x8000) != 0;
            int end = Math.Min(at + size - 2, data.Length);
            int chunkStart = output.Count;
            if (!compressed)
            {
                int raw = Math.Min(4096, data.Length - at);
                output.AddRange(data.Slice(at, raw).ToArray());
                at += 4096;
                continue;
            }

            while (at < end)
            {
                byte flags = data[at++];
                for (int bit = 0; bit < 8 && at < end; bit++)
                {
                    if ((flags & (1 << bit)) == 0)
                    {
                        output.Add(data[at++]);
                    }
                    else
                    {
                        ushort token = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
                        at += 2;
                        int bitCount = BitCount(output.Count - chunkStart);
                        int lengthMask = 0xFFFF >> bitCount;
                        int length = (token & lengthMask) + 3;
                        int offset = (token >> (16 - bitCount)) + 1;
                        for (int k = 0; k < length; k++)
                        {
                            output.Add(output[output.Count - offset]);
                        }
                    }
                }
            }
        }

        return [.. output];
    }

    /// <summary>
    /// Encrypts <paramref name="data"/> for a PROJECT stream property (MS-OVBA 2.4.3: the seed, the
    /// version and the project key xor'd with it, the ignored bytes the seed asks for, the length and
    /// the data each xor'd with the running sum of the last two bytes), as upper-case hex.
    /// </summary>
    public static string Encrypt(string projectId, ReadOnlySpan<byte> data, byte seed)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        byte projectKey = 0;
        foreach (byte b in Mbcs.GetBytes(projectId))
        {
            projectKey += b;
        }

        byte versionEnc = (byte)(seed ^ 2);
        byte keyEnc = (byte)(seed ^ projectKey);
        var output = new List<byte> { seed, versionEnc, keyEnc };
        byte unencrypted1 = projectKey;
        byte encrypted1 = keyEnc;
        byte encrypted2 = versionEnc;
        int ignored = (seed & 6) >> 1;
        var plain = new List<byte>();
        for (int k = 0; k < ignored; k++)
        {
            plain.Add(0x11);
        }

        plain.AddRange(Int32Bytes(data.Length));
        plain.AddRange(data.ToArray());
        foreach (byte b in plain)
        {
            byte enc = (byte)(b ^ (byte)(encrypted2 + unencrypted1));
            output.Add(enc);
            encrypted2 = encrypted1;
            encrypted1 = enc;
            unencrypted1 = b;
        }

        return Convert.ToHexString([.. output]);
    }

    /// <summary>Decrypts a PROJECT stream property written by <see cref="Encrypt"/> (or Office) back to its data.</summary>
    public static byte[] Decrypt(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        byte[] raw = Convert.FromHexString(hex);
        if (raw.Length < 7)
        {
            throw new InvalidDataException("The encrypted property is too short.");
        }

        byte seed = raw[0];
        byte projectKey = (byte)(seed ^ raw[2]);
        byte unencrypted1 = projectKey;
        byte encrypted1 = raw[2];
        byte encrypted2 = raw[1];
        var plain = new List<byte>();
        for (int k = 3; k < raw.Length; k++)
        {
            byte p = (byte)(raw[k] ^ (byte)(encrypted2 + unencrypted1));
            plain.Add(p);
            encrypted2 = encrypted1;
            encrypted1 = raw[k];
            unencrypted1 = p;
        }

        int ignored = (seed & 6) >> 1;
        if (plain.Count < ignored + 4)
        {
            throw new InvalidDataException("The encrypted property is too short.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(plain.ToArray().AsSpan(ignored));
        return [.. plain.Skip(ignored + 4).Take(Math.Max(0, Math.Min(length, plain.Count - ignored - 4)))];
    }

    private static byte[] CompressChunk(ReadOnlySpan<byte> chunk)
    {
        var output = new List<byte>(chunk.Length + chunk.Length / 8 + 1);
        int current = 0;
        while (current < chunk.Length)
        {
            int flagAt = output.Count;
            output.Add(0);
            byte flags = 0;
            for (int bit = 0; bit < 8 && current < chunk.Length; bit++)
            {
                int bitCount = BitCount(current);
                int maxLength = (0xFFFF >> bitCount) + 3;
                int bestLength = 0;
                int bestOffset = 0;
                int limit = Math.Min(maxLength, chunk.Length - current);
                if (limit >= 3)
                {
                    for (int offset = 1; offset <= current; offset++)
                    {
                        int length = 0;
                        int from = current - offset;
                        while (length < limit && chunk[from + length] == chunk[current + length])
                        {
                            length++;
                        }

                        if (length > bestLength)
                        {
                            bestLength = length;
                            bestOffset = offset;
                            if (length == limit)
                            {
                                break;
                            }
                        }
                    }
                }

                if (bestLength >= 3)
                {
                    ushort token = (ushort)(((bestOffset - 1) << (16 - bitCount)) | (bestLength - 3));
                    output.AddRange(Int16Bytes(token));
                    flags |= (byte)(1 << bit);
                    current += bestLength;
                }
                else
                {
                    output.Add(chunk[current++]);
                }
            }

            output[flagAt] = flags;
        }

        return [.. output];
    }

    /// <summary>The copy token's offset bit count for a position <paramref name="difference"/> bytes into the chunk: at least 4, and enough to hold the position.</summary>
    private static int BitCount(int difference)
    {
        int bits = 4;
        while ((1 << bits) < difference)
        {
            bits++;
        }

        return bits;
    }

    private static void Record(Stream stream, ushort id, ReadOnlySpan<byte> body)
    {
        stream.Write(Int16Bytes(id));
        stream.Write(Int32Bytes(body.Length));
        stream.Write(body);
    }

    private static byte[] Int16Bytes(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Int32Bytes(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
