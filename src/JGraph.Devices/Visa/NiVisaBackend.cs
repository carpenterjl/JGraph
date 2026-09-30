using System.Runtime.InteropServices;
using System.Text;

namespace JGraph.Devices.Visa;

/// <summary>
/// The installed VISA (NI-VISA, Keysight IO Libraries, R&amp;S VISA): <c>visa64.dll</c> from the system
/// folder, found at run time as R2025b's device plugin finds it, so a machine with no VISA runs every
/// other device class and <c>visadev</c> alone says there is none. The fourteen functions are bound
/// by address; <c>ViSession</c> is 32 bits and <c>ViAttrState</c> 64 bits on Win64 (visatype.h).
/// </summary>
public sealed unsafe class NiVisaBackend : IVisaBackend
{
    private readonly nint _library;
    private readonly uint _manager;
    private readonly delegate* unmanaged<uint, byte*, uint*, uint*, byte*, int> _findRsrc;
    private readonly delegate* unmanaged<uint, byte*, int> _findNext;
    private readonly delegate* unmanaged<uint, byte*, ushort*, ushort*, byte*, byte*, byte*, int> _parseRsrcEx;
    private readonly delegate* unmanaged<uint, byte*, uint, uint, uint*, int> _open;
    private readonly delegate* unmanaged<uint, int> _close;
    private readonly delegate* unmanaged<uint, byte*, uint, uint*, int> _read;
    private readonly delegate* unmanaged<uint, byte*, uint, uint*, int> _write;
    private readonly delegate* unmanaged<uint, uint, void*, int> _getAttribute;
    private readonly delegate* unmanaged<uint, uint, ulong, int> _setAttribute;
    private readonly delegate* unmanaged<uint, int> _clear;
    private readonly delegate* unmanaged<uint, ushort*, int> _readStb;
    private readonly delegate* unmanaged<uint, ushort, int> _assertTrigger;
    private readonly delegate* unmanaged<uint, int, byte*, int> _statusDesc;

    private NiVisaBackend(nint library)
    {
        _library = library;
        nint Bind(string name) => NativeLibrary.TryGetExport(library, name, out nint address)
            ? address
            : throw new VisaException(VisaStatus.ErrorLibraryNotFound, $"The VISA library has no entry point '{name}'.");

        var openDefaultRm = Bind("viOpenDefaultRM");
        _findRsrc = (delegate* unmanaged<uint, byte*, uint*, uint*, byte*, int>)Bind("viFindRsrc");
        _findNext = (delegate* unmanaged<uint, byte*, int>)Bind("viFindNext");
        _parseRsrcEx = (delegate* unmanaged<uint, byte*, ushort*, ushort*, byte*, byte*, byte*, int>)Bind("viParseRsrcEx");
        _open = (delegate* unmanaged<uint, byte*, uint, uint, uint*, int>)Bind("viOpen");
        _close = (delegate* unmanaged<uint, int>)Bind("viClose");
        _read = (delegate* unmanaged<uint, byte*, uint, uint*, int>)Bind("viRead");
        _write = (delegate* unmanaged<uint, byte*, uint, uint*, int>)Bind("viWrite");
        _getAttribute = (delegate* unmanaged<uint, uint, void*, int>)Bind("viGetAttribute");
        _setAttribute = (delegate* unmanaged<uint, uint, ulong, int>)Bind("viSetAttribute");
        _clear = (delegate* unmanaged<uint, int>)Bind("viClear");
        _readStb = (delegate* unmanaged<uint, ushort*, int>)Bind("viReadSTB");
        _assertTrigger = (delegate* unmanaged<uint, ushort, int>)Bind("viAssertTrigger");
        _statusDesc = (delegate* unmanaged<uint, int, byte*, int>)Bind("viStatusDesc");

        uint manager;
        int status = ((delegate* unmanaged<uint*, int>)openDefaultRm)(&manager);
        if (status < 0)
        {
            throw new VisaException(status, "Unable to create or open a session for the default resource manager.");
        }

        _manager = manager;
        string vendor;
        try
        {
            vendor = GetString(manager, VisaAttribute.ResourceManufacturerName);
        }
        catch (VisaException)
        {
            vendor = "";
        }

        PreferredVisa = vendor.Length == 0 ? "Unable to determine preferred VISA." : vendor + " VISA";
    }

    /// <summary>Loads <c>visa64.dll</c> and opens the default resource manager; throws <see cref="VisaException"/> when there is no VISA.</summary>
    public static NiVisaBackend Load()
    {
        if (!OperatingSystem.IsWindows()
            || !NativeLibrary.TryLoad("visa64.dll", typeof(NiVisaBackend).Assembly, DllImportSearchPath.System32, out nint library))
        {
            throw new VisaException(VisaStatus.ErrorLibraryNotFound, "No VISA library (visa64.dll) is installed.");
        }

        return new NiVisaBackend(library);
    }

    public string PreferredVisa { get; }

    public IReadOnlyList<string> Find(string expression)
    {
        var found = new List<string>();
        byte* description = stackalloc byte[256];
        uint list;
        uint count;
        fixed (byte* expr = Z(expression))
        {
            int status = _findRsrc(_manager, expr, &list, &count, description);
            if (status < 0)
            {
                return found;
            }
        }

        try
        {
            found.Add(Text(description));
            for (uint i = 1; i < count; i++)
            {
                if (_findNext(list, description) < 0)
                {
                    break;
                }

                found.Add(Text(description));
            }
        }
        finally
        {
            _close(list);
        }

        return found;
    }

    public VisaParsedName Parse(string name)
    {
        ushort type;
        ushort number;
        byte* cls = stackalloc byte[256];
        byte* expanded = stackalloc byte[256];
        byte* alias = stackalloc byte[256];
        cls[0] = expanded[0] = alias[0] = 0;
        fixed (byte* resource = Z(name))
        {
            Check(_manager, _parseRsrcEx(_manager, resource, &type, &number, cls, expanded, alias));
        }

        return new VisaParsedName(type, number, Text(cls), Text(expanded), Text(alias));
    }

    public IVisaSession Open(string name, int openTimeoutMilliseconds)
    {
        uint session;
        fixed (byte* resource = Z(name))
        {
            Check(_manager, _open(_manager, resource, 0, (uint)Math.Max(0, openTimeoutMilliseconds), &session));
        }

        return new Session(this, session);
    }

    private void Check(uint session, int status)
    {
        if (status < 0)
        {
            throw new VisaException(status, Describe(session, status));
        }
    }

    private string Describe(uint session, int status)
    {
        byte* text = stackalloc byte[256];
        text[0] = 0;
        _statusDesc(session, status, text);
        return Text(text);
    }

    private string GetString(uint session, uint attribute)
    {
        byte* text = stackalloc byte[256];
        text[0] = 0;
        Check(session, _getAttribute(session, attribute, text));
        return Text(text);
    }

    private static byte[] Z(string text) => [.. Encoding.ASCII.GetBytes(text), 0];

    private static string Text(byte* z) => Marshal.PtrToStringAnsi((nint)z) ?? "";

    public void Dispose()
    {
        _close(_manager);
        NativeLibrary.Free(_library);
    }

    private sealed class Session(NiVisaBackend visa, uint session) : IVisaSession
    {
        private bool _closed;

        public VisaReadResult Read(int count, CancellationToken cancel)
        {
            var buffer = new byte[Math.Max(0, count)];
            uint got;
            int status;
            fixed (byte* p = buffer)
            {
                status = visa._read(session, p, (uint)buffer.Length, &got);
            }

            if (status < 0 && status != VisaStatus.ErrorTimeout)
            {
                throw new VisaException(status, visa.Describe(session, status));
            }

            return new VisaReadResult(buffer.AsSpan(0, (int)got).ToArray(), status);
        }

        public void Write(ReadOnlySpan<byte> data, CancellationToken cancel)
        {
            uint written;
            fixed (byte* p = data)
            {
                visa.Check(session, visa._write(session, p, (uint)data.Length, &written));
            }
        }

        public ulong GetAttribute(uint attribute)
        {
            ulong value = 0;
            visa.Check(session, visa._getAttribute(session, attribute, &value));
            return value;
        }

        public string GetStringAttribute(uint attribute) => visa.GetString(session, attribute);

        public void SetAttribute(uint attribute, ulong value) => visa.Check(session, visa._setAttribute(session, attribute, value));

        public void Clear() => visa.Check(session, visa._clear(session));

        public ushort ReadStatusByte()
        {
            ushort status;
            visa.Check(session, visa._readStb(session, &status));
            return status;
        }

        public void AssertTrigger() => visa.Check(session, visa._assertTrigger(session, 0));

        public void Dispose()
        {
            if (!_closed)
            {
                _closed = true;
                visa._close(session);
            }
        }
    }
}
