namespace JGraph.Devices.Visa;

/// <summary>
/// A VISA library (device classes plan, stage D4): a resource manager that finds and parses resource
/// names and opens sessions. <see cref="NiVisaBackend"/> is the installed VISA (<c>visa64.dll</c>, loaded
/// at run time); <c>SimulatedVisa</c> is the test double with the fixtures' instruments behind it.
/// Everything R2025b's visadev decides — its refusals, terminators, reads — stays in JGraph.Scripting;
/// this layer speaks VISA's own vocabulary of status codes and attributes.
/// </summary>
public interface IVisaBackend : IDisposable
{
    /// <summary>The VISA's name as R2025b's conflict manager gives it: <c>National Instruments VISA</c>.</summary>
    string PreferredVisa { get; }

    /// <summary><c>viFindRsrc</c> and <c>viFindNext</c>: the resource names matching <paramref name="expression"/>; empty when none.</summary>
    IReadOnlyList<string> Find(string expression);

    /// <summary><c>viParseRsrcEx</c>; throws <see cref="VisaException"/> for a name VISA cannot parse.</summary>
    VisaParsedName Parse(string name);

    /// <summary><c>viOpen</c>; throws <see cref="VisaException"/> when the resource is missing or busy.</summary>
    IVisaSession Open(string name, int openTimeoutMilliseconds);
}

/// <summary>What <c>viParseRsrcEx</c> answers for a resource name.</summary>
/// <param name="InterfaceType">VI_INTF_GPIB (1), VXI (2), ASRL (4), PXI (5), TCPIP (6), USB (7).</param>
/// <param name="InterfaceNumber">The board number: <c>20</c> for ASRL20, <c>0</c> for TCPIP0.</param>
/// <param name="ResourceClass">INSTR, SOCKET, INTFC, RAW, …</param>
/// <param name="ExpandedName">The canonical name: <c>TCPIP0::127.0.0.1::hislip0::INSTR</c> for <c>tcpip::127.0.0.1::hislip0::instr</c>.</param>
/// <param name="Alias">The alias that names it, <c>COM20</c> for ASRL20; empty when it has none.</param>
public sealed record VisaParsedName(ushort InterfaceType, ushort InterfaceNumber, string ResourceClass, string ExpandedName, string Alias);

/// <summary>What one <c>viRead</c> returned: the bytes, and the status it ended with.</summary>
public readonly record struct VisaReadResult(byte[] Data, int Status)
{
    public bool TimedOut => Status == VisaStatus.ErrorTimeout;
}

/// <summary>An open VISA session. Every call runs on the script thread and may block for the session's timeout.</summary>
public interface IVisaSession : IDisposable
{
    /// <summary><c>viRead</c> of at most <paramref name="count"/> bytes. A timeout is a result, not an exception; other failures throw.</summary>
    VisaReadResult Read(int count, CancellationToken cancel);

    /// <summary><c>viWrite</c> of every byte; throws <see cref="VisaException"/> on failure.</summary>
    void Write(ReadOnlySpan<byte> data, CancellationToken cancel);

    /// <summary><c>viGetAttribute</c> of a numeric attribute; throws <see cref="VisaException"/>.</summary>
    ulong GetAttribute(uint attribute);

    /// <summary><c>viGetAttribute</c> of a string attribute; throws <see cref="VisaException"/>.</summary>
    string GetStringAttribute(uint attribute);

    /// <summary><c>viSetAttribute</c>; throws <see cref="VisaException"/>.</summary>
    void SetAttribute(uint attribute, ulong value);

    /// <summary><c>viClear</c>: a device clear (a break on a serial line).</summary>
    void Clear();

    /// <summary><c>viReadSTB</c>; throws <see cref="VisaException"/> where the resource has none.</summary>
    ushort ReadStatusByte();

    /// <summary><c>viAssertTrigger</c> with the default protocol.</summary>
    void AssertTrigger();
}

/// <summary>A VISA call failed with <see cref="Status"/>.</summary>
public sealed class VisaException(int status, string description) : Exception(description)
{
    public int Status { get; } = status;
}

/// <summary>The VISA status codes JGraph tells apart (visa.h).</summary>
public static class VisaStatus
{
    public const int Success = 0;
    public const int SuccessTermChar = 0x3FFF0005;
    public const int SuccessMaxCount = 0x3FFF0006;
    public const int ErrorResourceLocked = unchecked((int)0xBFFF000F);
    public const int ErrorResourceNotFound = unchecked((int)0xBFFF0011);
    public const int ErrorInvalidResourceName = unchecked((int)0xBFFF0012);
    public const int ErrorTimeout = unchecked((int)0xBFFF0015);
    public const int ErrorAttributeNotSupported = unchecked((int)0xBFFF001D);
    public const int ErrorAttributeStateNotSupported = unchecked((int)0xBFFF001E);
    public const int ErrorAttributeReadOnly = unchecked((int)0xBFFF001F);
    public const int ErrorInvalidSetup = unchecked((int)0xBFFF003A);
    public const int ErrorIO = unchecked((int)0xBFFF003E);
    public const int ErrorOperationNotSupported = unchecked((int)0xBFFF0067);
    public const int ErrorResourceBusy = unchecked((int)0xBFFF0072);
    public const int ErrorConnectionLost = unchecked((int)0xBFFF00A6);
    public const int ErrorLibraryNotFound = unchecked((int)0xBFFF009E);
}

/// <summary>The VISA attributes visadev drives (R2025b's VISAAttribute and VISAUnclassifiedAttribute).</summary>
public static class VisaAttribute
{
    public const uint TermCharEnabled = 0x3FFF0038;
    public const uint TermChar = 0x3FFF0018;
    public const uint AsrlEndIn = 0x3FFF00B3;
    public const uint AsrlEndOut = 0x3FFF00B4;
    public const uint TimeoutValue = 0x3FFF001A;
    public const uint SuppressEndEnabled = 0x3FFF0036;
    public const uint SendEndEnabled = 0x3FFF0016;
    public const uint InterfaceType = 0x3FFF0171;
    public const uint InterfaceNumber = 0x3FFF0176;
    public const uint GpibPrimaryAddress = 0x3FFF0172;
    public const uint GpibSecondaryAddress = 0x3FFF0173;
    public const uint VxiLogicalAddress = 0x3FFF00D5;
    public const uint Slot = 0x3FFF00E8;
    public const uint AsrlBaud = 0x3FFF0021;
    public const uint AsrlDataBits = 0x3FFF0022;
    public const uint AsrlStopBits = 0x3FFF0024;
    public const uint AsrlParity = 0x3FFF0023;
    public const uint AsrlFlowControl = 0x3FFF0025;
    public const uint AsrlCtsState = 0x3FFF00AE;
    public const uint AsrlDcdState = 0x3FFF00AF;
    public const uint AsrlDsrState = 0x3FFF00B1;
    public const uint AsrlDtrState = 0x3FFF00B2;
    public const uint AsrlRiState = 0x3FFF00BF;
    public const uint AsrlRtsState = 0x3FFF00C0;
    public const uint PxiBusNumber = 0x3FFF0205;
    public const uint PxiDeviceNumber = 0x3FFF0201;
    public const uint PxiFunctionNumber = 0x3FFF0202;
    public const uint PxiChassis = 0x3FFF0206;
    public const uint ManufacturerId = 0x3FFF00D9;
    public const uint ModelCode = 0x3FFF00DF;
    public const uint UsbInterfaceNumber = 0x3FFF01A1;
    public const uint TcpipDeviceName = 0xBFFF0199;
    public const uint TcpipAddress = 0xBFFF0195;
    public const uint TcpipPort = 0x3FFF0197;
    public const uint TcpipIsHislip = 0x3FFF0303;
    public const uint TcpipNoDelay = 0x3FFF019A;
    public const uint TcpipKeepAlive = 0x3FFF019B;
    public const uint ResourceManufacturerName = 0xBFFF0174;

    /// <summary>Whether an attribute is a string (the 0xBFFF block).</summary>
    public static bool IsString(uint attribute) => (attribute & 0xFFFF0000) == 0xBFFF0000;
}

/// <summary>Where a script's VISA comes from: a simulated one a test installed, or the installed library.</summary>
public static class VisaBackends
{
    private static readonly object Gate = new();
    private static NiVisaBackend? _installed;
    private static string? _failure;

    /// <summary>The installed VISA, loaded once; null with the reason when there is none.</summary>
    public static IVisaBackend? Installed(out string? failure)
    {
        lock (Gate)
        {
            if (_installed is null && _failure is null)
            {
                try
                {
                    _installed = NiVisaBackend.Load();
                }
                catch (VisaException e)
                {
                    _failure = e.Message;
                }
            }

            failure = _failure;
            return _installed;
        }
    }
}
