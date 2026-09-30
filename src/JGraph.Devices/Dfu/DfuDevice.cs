using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JGraph.Devices.Dfu;

/// <summary>DFU_GETSTATUS's answer (DFU 1.1, 6.1.2), with the status and state named as the specification names them.</summary>
public readonly record struct DfuStatus(int Status, int PollTimeout, int State, int StringIndex)
{
    public string StatusName => StatusNameOf(Status);

    public string StateName => StateNameOf(State);

    public static string StatusNameOf(int status) => status switch
    {
        0 => "OK",
        1 => "errTARGET",
        2 => "errFILE",
        3 => "errWRITE",
        4 => "errERASE",
        5 => "errCHECK_ERASED",
        6 => "errPROG",
        7 => "errVERIFY",
        8 => "errADDRESS",
        9 => "errNOTDONE",
        10 => "errFIRMWARE",
        11 => "errVENDOR",
        12 => "errUSBR",
        13 => "errPOR",
        14 => "errUNKNOWN",
        15 => "errSTALLEDPKT",
        _ => $"status {status}",
    };

    public static string StateNameOf(int state) => state switch
    {
        0 => "appIDLE",
        1 => "appDETACH",
        2 => "dfuIDLE",
        3 => "dfuDNLOAD-SYNC",
        4 => "dfuDNBUSY",
        5 => "dfuDNLOAD-IDLE",
        6 => "dfuMANIFEST-SYNC",
        7 => "dfuMANIFEST",
        8 => "dfuMANIFEST-WAIT-RESET",
        9 => "dfuUPLOAD-IDLE",
        10 => "dfuERROR",
        _ => $"state {state}",
    };
}

/// <summary>The DFU functional descriptor (DFU 1.1, 4.1.3): its attributes and sizes, and bcdDFUVersion (0x011A for ST's DfuSe).</summary>
public readonly record struct DfuFunctional(bool CanDownload, bool CanUpload, bool ManifestationTolerant, bool WillDetach,
    int DetachTimeout, int TransferSize, int Version)
{
    /// <summary>The functional descriptor among an interface's class-specific descriptors (type 0x21), or null.</summary>
    public static DfuFunctional? From(IEnumerable<byte[]> classSpecific)
    {
        foreach (byte[] d in classSpecific)
        {
            if (d.Length >= 7 && d[1] == 0x21)
            {
                int attributes = d[2];
                int version = d.Length >= 9 ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(7)) : 0x0100;
                return new DfuFunctional((attributes & 1) != 0, (attributes & 2) != 0, (attributes & 4) != 0, (attributes & 8) != 0,
                    BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(3)), BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(5)), version);
            }
        }

        return null;
    }

    /// <summary>The functional descriptor's bytes, as a device carries it.</summary>
    public byte[] ToBytes() =>
    [
        9, 0x21,
        (byte)((CanDownload ? 1 : 0) | (CanUpload ? 2 : 0) | (ManifestationTolerant ? 4 : 0) | (WillDetach ? 8 : 0)),
        (byte)DetachTimeout, (byte)(DetachTimeout >> 8),
        (byte)TransferSize, (byte)(TransferSize >> 8),
        (byte)Version, (byte)(Version >> 8),
    ];
}

/// <summary>One run of equal pages of a DfuSe memory: [Start, End), and what the loader lets a host do there.</summary>
public sealed record DfuSeSegment(uint Start, uint End, int PageSize, bool Readable, bool Erasable, bool Writeable)
{
    public int Pages => (int)((End - Start) / (uint)PageSize);
}

/// <summary>
/// A DfuSe memory layout, the text ST's loaders give each alternate setting as its interface string
/// (UM0424, "@Internal Flash  /0x08000000/04*016Kg,01*064Kg,07*128Kg"): a name, then for each address
/// runs of <c>count*size</c> pages, the size's unit (none, K or M) and a letter whose bits say readable
/// (1), erasable (2) and writeable (4). Parsed as dfu-util parses it.
/// </summary>
public sealed record DfuSeLayout(string Name, IReadOnlyList<DfuSeSegment> Segments)
{
    private static readonly Regex Run = new(@"^\s*(\d+)\s*\*\s*(\d+)\s*([BKM]?)\s*([a-g]?)\s*$", RegexOptions.CultureInvariant);

    public static DfuSeLayout? Parse(string text)
    {
        if (!text.StartsWith('@'))
        {
            return null;
        }

        string[] parts = text[1..].Split('/');
        if (parts.Length < 3)
        {
            return null;
        }

        var segments = new List<DfuSeSegment>();
        for (int p = 1; p + 1 < parts.Length; p += 2)
        {
            string address = parts[p].Trim();
            if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                address = address[2..];
            }

            if (!uint.TryParse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint at))
            {
                return null;
            }

            foreach (string run in parts[p + 1].Split(','))
            {
                Match m = Run.Match(run);
                if (!m.Success)
                {
                    return null;
                }

                int count = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int size = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * m.Groups[3].Value switch { "K" => 1024, "M" => 1024 * 1024, _ => 1 };
                int type = m.Groups[4].Value.Length == 0 ? 0 : m.Groups[4].Value[0] - 'a' + 1;
                if (count <= 0 || size <= 0)
                {
                    return null;
                }

                uint end = at + (uint)((long)count * size);
                segments.Add(new DfuSeSegment(at, end, size, (type & 1) != 0, (type & 2) != 0, (type & 4) != 0));
                at = end;
            }
        }

        return new DfuSeLayout(parts[0].Trim(), segments);
    }

    public DfuSeSegment? SegmentAt(uint address) => Segments.FirstOrDefault(s => address >= s.Start && address < s.End);

    /// <summary>The start of each page [address, address + length) touches.</summary>
    public IEnumerable<uint> PagesTouched(uint address, int length)
    {
        uint end = address + (uint)length;
        uint at = address;
        while (at < end)
        {
            DfuSeSegment segment = SegmentAt(at) ?? throw new ArgumentOutOfRangeException(nameof(address));
            uint page = segment.Start + (at - segment.Start) / (uint)segment.PageSize * (uint)segment.PageSize;
            yield return page;
            at = page + (uint)segment.PageSize;
        }
    }

    /// <summary>Why [address, address + length) cannot be written, or null when every byte of it is writeable.</summary>
    public string? Unwriteable(uint address, int length)
    {
        uint end = address + (uint)length;
        for (uint at = address; at < end;)
        {
            DfuSeSegment? segment = SegmentAt(at);
            if (segment is null)
            {
                return $"{DfuFiles.Hex(at)} is outside {Name}";
            }

            if (!segment.Writeable)
            {
                return $"{DfuFiles.Hex(at)} in {Name} is not writeable";
            }

            at = segment.End;
        }

        return null;
    }
}

/// <summary>A device's DFU status other than OK, or a request the device stalled: the transfer stopped.</summary>
public sealed class DfuException(string message, DfuStatus? status = null) : Exception(message)
{
    public DfuStatus? Status { get; } = status;
}

/// <summary>The control pipe a DFU interface is reached through: WinUSB for a device, or a simulated one.</summary>
public interface IDfuTransport : IDisposable
{
    byte[] Control(byte requestType, byte request, ushort value, ushort index, ReadOnlySpan<byte> data, int readLength, TimeSpan timeout);

    void SetAlternate(int interfaceNumber, int setting);
}

/// <summary>
/// DFU 1.1 and ST's DfuSe extensions (UM0391, AN3156) over a control pipe (device classes plan, stage D8,
/// ADR 0191): the class requests, a download that polls GETSTATUS as long as the device asks between
/// blocks, the zero-length block that starts manifestation, an upload, and DfuSe's address pointer,
/// page erase, mass erase, read unprotect and leave. Whole images go the way dfu-util sends them.
/// </summary>
public sealed class DfuDevice : IDisposable
{
    private const byte Out = 0x21;
    private const byte In = 0xA1;

    private readonly IDfuTransport _transport;
    private readonly ushort _interface;
    private readonly IReadOnlyList<string> _alternateNames;
    private int _alternate;

    /// <param name="transport">The control pipe.</param>
    /// <param name="interfaceNumber">The DFU interface's number.</param>
    /// <param name="functional">Its functional descriptor.</param>
    /// <param name="dfuMode">Whether the interface is in DFU mode (protocol 2) rather than run-time (protocol 1).</param>
    /// <param name="alternateNames">Each alternate setting's interface string; DfuSe's are memory layouts.</param>
    public DfuDevice(IDfuTransport transport, int interfaceNumber, DfuFunctional functional, bool dfuMode, IReadOnlyList<string> alternateNames)
    {
        _transport = transport;
        _interface = (ushort)interfaceNumber;
        Functional = functional;
        DfuMode = dfuMode;
        _alternateNames = alternateNames.Count == 0 ? [""] : alternateNames;
    }

    public DfuFunctional Functional { get; }

    public bool DfuMode { get; }

    /// <summary>Whether the device speaks DfuSe (bcdDFUVersion 1.1a).</summary>
    public bool IsDfuSe => Functional.Version == 0x011A;

    public int InterfaceNumber => _interface;

    public IReadOnlyList<string> AlternateNames => _alternateNames;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The alternate setting in use; setting it sends SET_INTERFACE.</summary>
    public int Alternate
    {
        get => _alternate;
        set
        {
            if (value < 0 || value >= _alternateNames.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"The DFU interface has alternate settings 0 to {_alternateNames.Count - 1}.");
            }

            if (value != _alternate)
            {
                _transport.SetAlternate(_interface, value);
                _alternate = value;
            }
        }
    }

    /// <summary>The DfuSe memory layout of the alternate setting in use, or null.</summary>
    public DfuSeLayout? Layout => DfuSeLayout.Parse(_alternateNames[_alternate]);

    public DfuStatus GetStatus()
    {
        byte[] s = Request(In, 3, 0, [], 6);
        if (s.Length < 6)
        {
            throw new DfuException("DFU_GETSTATUS answered fewer than six bytes.");
        }

        return new DfuStatus(s[0], s[1] | (s[2] << 8) | (s[3] << 16), s[4], s[5]);
    }

    public int GetState()
    {
        byte[] s = Request(In, 5, 0, [], 1);
        return s.Length > 0 ? s[0] : -1;
    }

    public void ClearStatus() => Request(Out, 4, 0, [], 0);

    public void Abort() => Request(Out, 6, 0, [], 0);

    /// <summary>DFU_DETACH: asks a run-time interface to re-enumerate in DFU mode within <paramref name="milliseconds"/>.</summary>
    public void Detach(int milliseconds) => Request(Out, 0, (ushort)Math.Clamp(milliseconds, 0, 0xFFFF), [], 0);

    private byte[] Request(byte requestType, byte request, ushort value, ReadOnlySpan<byte> data, int length)
    {
        try
        {
            return _transport.Control(requestType, request, value, _interface, data, length, Timeout);
        }
        catch (DeviceIOException e)
        {
            throw new DfuException($"The device refused DFU request {RequestName(request)}: {e.Message}");
        }
    }

    private static string RequestName(byte request) => request switch
    {
        0 => "DETACH",
        1 => "DNLOAD",
        2 => "UPLOAD",
        3 => "GETSTATUS",
        4 => "CLRSTATUS",
        5 => "GETSTATE",
        6 => "ABORT",
        _ => request.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>GETSTATUS until the device is idle again, waiting the poll timeout it asks between tries.</summary>
    private DfuStatus WaitIdle(string doing, bool manifest = false)
    {
        for (int tries = 0; tries < 10000; tries++)
        {
            DfuStatus status = GetStatus();
            if (status.Status != 0)
            {
                throw new DfuException($"The device reported {status.StatusName} in {status.StateName} while {doing}.", status);
            }

            if (status.State is 2 or 5 || (manifest && status.State is 8))
            {
                return status;
            }

            if (status.State is not (3 or 4 or 6 or 7))
            {
                throw new DfuException($"The device went to {status.StateName} while {doing}.", status);
            }

            Thread.Sleep(Math.Clamp(status.PollTimeout, 1, 5000));
        }

        throw new TimeoutException($"The device stayed busy while {doing}.");
    }

    /// <summary>Brings the device to dfuIDLE from an error or a half-finished transfer, as dfu-util does before each operation.</summary>
    public void Idle()
    {
        DfuStatus status = GetStatus();
        switch (status.State)
        {
            case 2:
                return;
            case 10:
                ClearStatus();
                break;
            case 5 or 9:
                Abort();
                break;
            case 0 or 1:
                throw new DfuException("The device is in its application (run-time mode); detach it into DFU mode first.");
        }

        status = GetStatus();
        if (status.State != 2)
        {
            throw new DfuException($"The device stayed in {status.StateName} with {status.StatusName}.", status);
        }
    }

    /// <summary>One DFU_DNLOAD block, then GETSTATUS until the device is idle.</summary>
    public void Download(int block, ReadOnlySpan<byte> data)
    {
        Request(Out, 1, (ushort)block, data, 0);
        WaitIdle($"writing block {block}");
    }

    public byte[] Upload(int block, int length) => Request(In, 2, (ushort)block, [], length);

    // --- DfuSe -----------------------------------------------------------------------------------------

    private void RequireDfuSe(string what)
    {
        if (!IsDfuSe)
        {
            throw new DfuException($"{what} is a DfuSe command, and this device speaks plain DFU {Functional.Version >> 8}.{Functional.Version & 0xFF:X2}.");
        }
    }

    private void DfuSeCommand(string what, byte command, uint? address)
    {
        RequireDfuSe(what);
        byte[] bytes = address is { } a ? [command, (byte)a, (byte)(a >> 8), (byte)(a >> 16), (byte)(a >> 24)] : [command];
        Request(Out, 1, 0, bytes, 0);
        WaitIdle(what);
    }

    public void SetAddress(uint address) => DfuSeCommand($"setting the address to {DfuFiles.Hex(address)}", 0x21, address);

    public void ErasePage(uint address) => DfuSeCommand($"erasing the page at {DfuFiles.Hex(address)}", 0x41, address);

    public void MassErase()
    {
        Idle();
        DfuSeCommand("erasing all of the memory", 0x41, null);
    }

    /// <summary>DfuSe's read unprotect: the loader erases the whole flash, then resets.</summary>
    public void ReadUnprotect()
    {
        Idle();
        RequireDfuSe("Read unprotect");
        Request(Out, 1, 0, [0x92], 0);
        try
        {
            GetStatus();
        }
        catch (Exception e) when (e is DfuException or DeviceConnectionLostException)
        {
            // The loader resets once the flash is erased.
        }
    }

    /// <summary>Leaves DfuSe: the address pointer, a zero-length download, and the GETSTATUS that starts the application there.</summary>
    public void Leave(uint address)
    {
        Idle();
        SetAddress(address);
        Request(Out, 1, 2, [], 0);
        try
        {
            GetStatus();
        }
        catch (Exception e) when (e is DfuException or DeviceConnectionLostException)
        {
            // The device may already be gone into its application.
        }
    }

    // --- whole images ------------------------------------------------------------------------------------

    /// <summary>
    /// Downloads <paramref name="data"/>. Plain DFU sends it in blocks from 0, then the zero-length block
    /// that starts manifestation; a device that is not manifestation tolerant may reset and go away there.
    /// DfuSe writes at <paramref name="address"/>: each page the image touches is erased, then each block
    /// goes after its own address pointer, as dfu-util sends them. <paramref name="progress"/> hears the
    /// bytes sent so far.
    /// </summary>
    public void DownloadImage(ReadOnlySpan<byte> data, uint address, Action<long>? progress = null)
    {
        int size = Math.Max(1, Functional.TransferSize);
        Idle();
        if (IsDfuSe)
        {
            DfuSeLayout layout = Layout ?? throw new DfuException($"Alternate setting {_alternate} names no DfuSe memory layout.");
            if (layout.Unwriteable(address, data.Length) is { } why)
            {
                throw new DfuException($"The image cannot go at {DfuFiles.Hex(address)}: {why}.");
            }

            foreach (uint page in layout.PagesTouched(address, data.Length))
            {
                if (layout.SegmentAt(page)!.Erasable)
                {
                    ErasePage(page);
                }
            }

            for (int at = 0; at < data.Length; at += size)
            {
                SetAddress(address + (uint)at);
                Download(2, data.Slice(at, Math.Min(size, data.Length - at)));
                progress?.Invoke(Math.Min(at + size, data.Length));
            }

            Abort();
            return;
        }

        int block = 0;
        for (int at = 0; at < data.Length; at += size, block++)
        {
            Download(block & 0xFFFF, data.Slice(at, Math.Min(size, data.Length - at)));
            progress?.Invoke(Math.Min(at + size, data.Length));
        }

        Request(Out, 1, (ushort)(block & 0xFFFF), [], 0);
        try
        {
            WaitIdle("finishing the download (manifestation)", manifest: true);
        }
        catch (Exception e) when (!Functional.ManifestationTolerant && e is DfuException { Status: null } or DeviceConnectionLostException)
        {
            // A device that is not manifestation tolerant may reset itself here.
        }
    }

    /// <summary>
    /// Uploads up to <paramref name="maximum"/> bytes: plain DFU in blocks from 0 until a short block ends
    /// the image; DfuSe from <paramref name="address"/>.
    /// </summary>
    public byte[] UploadImage(int maximum, uint address)
    {
        int size = Math.Max(1, Functional.TransferSize);
        var result = new List<byte>(Math.Min(maximum, 1 << 20));
        Idle();
        int block = 0;
        if (IsDfuSe)
        {
            SetAddress(address);
            Abort();
            block = 2;
        }

        while (result.Count < maximum)
        {
            int want = IsDfuSe ? Math.Min(size, maximum - result.Count) : size;
            byte[] chunk = Upload(block++ & 0xFFFF, want);
            result.AddRange(chunk.Take(maximum - result.Count));
            if (chunk.Length < want)
            {
                break;
            }
        }

        Abort();
        return result.ToArray();
    }

    public void Dispose() => _transport.Dispose();
}
