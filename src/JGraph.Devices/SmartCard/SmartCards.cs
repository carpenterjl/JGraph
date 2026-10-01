using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace JGraph.Devices.SmartCard;

/// <summary>A PC/SC failure, with the SCARD_ code behind it.</summary>
public sealed class SmartCardException(string message, int code) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>One reader as the resource manager sees it: whether a card is in it, and the card's ATR.</summary>
/// <param name="Reader">The reader's name.</param>
/// <param name="State">"empty", "present", "inuse", "exclusive", "mute", "unpowered" or "unavailable".</param>
/// <param name="Present">Whether a card is in the reader.</param>
/// <param name="Atr">The card's answer to reset; empty with no card.</param>
public sealed record SmartCardReader(string Reader, string State, bool Present, byte[] Atr);

/// <summary>A card or a reader arriving or leaving.</summary>
/// <param name="Type">"CardInserted", "CardRemoved", "ReaderAdded" or "ReaderRemoved".</param>
/// <param name="Reader">The reader's name.</param>
/// <param name="Atr">The inserted card's ATR; empty otherwise.</param>
public sealed record SmartCardEvent(string Type, string Reader, byte[] Atr);

/// <summary>A connection to the card in one reader (or to the reader alone, when direct).</summary>
public interface ISmartCard : IDisposable
{
    string Reader { get; }

    /// <summary>"T0", "T1", "raw", or "none" for a direct connection that negotiated nothing.</summary>
    string Protocol { get; }

    /// <summary>The card's answer to reset.</summary>
    byte[] Atr();

    /// <summary>"absent", "present", "swallowed", "powered", "negotiable" or "specific".</summary>
    string State();

    /// <summary>Sends a command APDU; answers the whole response, its two status bytes last.</summary>
    byte[] Transmit(ReadOnlySpan<byte> apdu);

    /// <summary>A reader control code (SCardControl); answers what the reader returned.</summary>
    byte[] Control(uint code, ReadOnlySpan<byte> input);

    void BeginTransaction();

    /// <param name="disposition">"leave", "reset", "unpower" or "eject".</param>
    void EndTransaction(string disposition);

    /// <summary>Connects again after the card was reset or changed, keeping the reader.</summary>
    /// <param name="share">"shared", "exclusive" or "direct".</param>
    /// <param name="protocol">"any", "T0", "T1" or "raw".</param>
    /// <param name="initialization">"leave", "reset" or "unpower".</param>
    void Reconnect(string share, string protocol, string initialization);

    /// <param name="disposition">"leave", "reset", "unpower" or "eject".</param>
    void Disconnect(string disposition);
}

/// <summary>
/// The smart-card readers a script reaches (device classes plan, stage D12): the machine's through
/// PC/SC, or the simulated ones.
/// </summary>
public interface ISmartCardBackend
{
    /// <summary>The readers and their cards; none when the machine has no reader.</summary>
    IReadOnlyList<SmartCardReader> Readers();

    ISmartCard Connect(string reader, string share, string protocol);

    /// <summary>Calls <paramref name="changed"/>, on a thread of the backend's, until the answer is disposed.</summary>
    IDisposable Watch(Action<SmartCardEvent> changed);
}

/// <summary>
/// winscard.dll: PC/SC, which is how Windows exposes CCID smart-card readers. The constants are
/// winscard.h's and winsmcrd.h's (Windows SDK 10.0.26100.0).
/// </summary>
internal static unsafe partial class PcscNative
{
    public const uint SCARD_SCOPE_USER = 0;
    public const uint SCARD_SHARE_EXCLUSIVE = 1;
    public const uint SCARD_SHARE_SHARED = 2;
    public const uint SCARD_SHARE_DIRECT = 3;
    public const uint SCARD_PROTOCOL_T0 = 1;
    public const uint SCARD_PROTOCOL_T1 = 2;
    public const uint SCARD_PROTOCOL_RAW = 0x10000;
    public const uint SCARD_LEAVE_CARD = 0;
    public const uint SCARD_RESET_CARD = 1;
    public const uint SCARD_UNPOWER_CARD = 2;
    public const uint SCARD_EJECT_CARD = 3;
    public const uint SCARD_STATE_UNAWARE = 0;
    public const uint SCARD_STATE_CHANGED = 0x2;
    public const uint SCARD_STATE_UNAVAILABLE = 0x8;
    public const uint SCARD_STATE_EMPTY = 0x10;
    public const uint SCARD_STATE_PRESENT = 0x20;
    public const uint SCARD_STATE_EXCLUSIVE = 0x80;
    public const uint SCARD_STATE_INUSE = 0x100;
    public const uint SCARD_STATE_MUTE = 0x200;
    public const uint SCARD_STATE_UNPOWERED = 0x400;
    public const int SCARD_E_CANCELLED = unchecked((int)0x80100002);
    public const int SCARD_E_TIMEOUT = unchecked((int)0x8010000A);
    public const int SCARD_E_NO_SERVICE = unchecked((int)0x8010001D);
    public const int SCARD_E_SERVICE_STOPPED = unchecked((int)0x8010001E);
    public const int SCARD_E_NO_READERS_AVAILABLE = unchecked((int)0x8010002E);

    [StructLayout(LayoutKind.Sequential)]
    public struct SCARD_IO_REQUEST
    {
        public uint dwProtocol;
        public uint cbPciLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SCARD_READERSTATE
    {
        public nint szReader;
        public nint pvUserData;
        public uint dwCurrentState;
        public uint dwEventState;
        public uint cbAtr;
        public fixed byte rgbAtr[36];
    }

    [LibraryImport("winscard.dll")]
    public static partial int SCardEstablishContext(uint scope, nint reserved1, nint reserved2, nint* context);

    [LibraryImport("winscard.dll")]
    public static partial int SCardReleaseContext(nint context);

    [LibraryImport("winscard.dll", EntryPoint = "SCardListReadersW")]
    public static partial int SCardListReaders(nint context, char* groups, char* readers, uint* length);

    [LibraryImport("winscard.dll", EntryPoint = "SCardConnectW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SCardConnect(nint context, string reader, uint share, uint protocols, nint* card, uint* activeProtocol);

    [LibraryImport("winscard.dll")]
    public static partial int SCardReconnect(nint card, uint share, uint protocols, uint initialization, uint* activeProtocol);

    [LibraryImport("winscard.dll")]
    public static partial int SCardDisconnect(nint card, uint disposition);

    [LibraryImport("winscard.dll")]
    public static partial int SCardTransmit(nint card, SCARD_IO_REQUEST* sendPci, byte* send, uint sendLength, SCARD_IO_REQUEST* receivePci, byte* receive, uint* receiveLength);

    [LibraryImport("winscard.dll")]
    public static partial int SCardControl(nint card, uint code, byte* input, uint inputLength, byte* output, uint outputLength, uint* returned);

    [LibraryImport("winscard.dll", EntryPoint = "SCardStatusW")]
    public static partial int SCardStatus(nint card, char* readerNames, uint* readerLength, uint* state, uint* protocol, byte* atr, uint* atrLength);

    [LibraryImport("winscard.dll")]
    public static partial int SCardBeginTransaction(nint card);

    [LibraryImport("winscard.dll")]
    public static partial int SCardEndTransaction(nint card, uint disposition);

    [LibraryImport("winscard.dll", EntryPoint = "SCardGetStatusChangeW")]
    public static partial int SCardGetStatusChange(nint context, uint timeout, SCARD_READERSTATE* states, uint count);
}

/// <summary>The words the PC/SC backends share: the SCARD_ codes in a sentence, and the option names as numbers.</summary>
public static class SmartCardWords
{
    /// <summary>SCARD_CTL_CODE(function): the control code of a reader function number (3400 is CM_IOCTL_GET_FEATURE_REQUEST).</summary>
    public static uint ControlCode(int function) => (0x31u << 16) | ((uint)function << 2);

    /// <summary>What a SCARD_ code means, with its name.</summary>
    public static string Describe(int code) => (uint)code switch
    {
        0x80100002 => "the action was cancelled (SCARD_E_CANCELLED)",
        0x80100003 => "the handle is invalid (SCARD_E_INVALID_HANDLE)",
        0x80100004 => "a parameter is invalid (SCARD_E_INVALID_PARAMETER)",
        0x80100008 => "the answer does not fit the buffer (SCARD_E_INSUFFICIENT_BUFFER)",
        0x80100009 => "the reader is unknown (SCARD_E_UNKNOWN_READER)",
        0x8010000A => "the time ran out (SCARD_E_TIMEOUT)",
        0x8010000B => "the card is in use by another connection (SCARD_E_SHARING_VIOLATION)",
        0x8010000C => "there is no card in the reader (SCARD_E_NO_SMARTCARD)",
        0x8010000F => "the card does not speak the protocol asked for (SCARD_E_PROTO_MISMATCH)",
        0x80100010 => "the reader or card is not ready (SCARD_E_NOT_READY)",
        0x80100013 => "the card did not answer as the protocol requires (SCARD_E_COMM_ERROR)",
        0x80100016 => "no transaction is open (SCARD_E_NOT_TRANSACTED)",
        0x80100017 => "the reader is no longer available (SCARD_E_READER_UNAVAILABLE)",
        0x8010001D => "the smart-card service is not running (SCARD_E_NO_SERVICE)",
        0x8010001E => "the smart-card service has stopped (SCARD_E_SERVICE_STOPPED)",
        0x80100022 => "the reader does not support that (SCARD_E_UNSUPPORTED_FEATURE)",
        0x8010002E => "no reader is attached (SCARD_E_NO_READERS_AVAILABLE)",
        0x8010002F => "the data was lost on the way (SCARD_E_COMM_DATA_LOST)",
        0x80100066 => "the card is not responding (SCARD_W_UNRESPONSIVE_CARD)",
        0x80100067 => "the card has no power (SCARD_W_UNPOWERED_CARD)",
        0x80100068 => "the card was reset (SCARD_W_RESET_CARD)",
        0x80100069 => "the card was removed (SCARD_W_REMOVED_CARD)",
        < 0x10000 => new Win32Exception(code).Message.TrimEnd('.', ' ') + $" (error {code})",
        _ => $"SCARD error 0x{(uint)code:X8}",
    };

    internal static uint Share(string share) => share switch
    {
        "exclusive" => PcscNative.SCARD_SHARE_EXCLUSIVE,
        "direct" => PcscNative.SCARD_SHARE_DIRECT,
        _ => PcscNative.SCARD_SHARE_SHARED,
    };

    internal static uint Protocols(string protocol, string share) => protocol switch
    {
        "T0" => PcscNative.SCARD_PROTOCOL_T0,
        "T1" => PcscNative.SCARD_PROTOCOL_T1,
        "raw" => PcscNative.SCARD_PROTOCOL_RAW,
        _ => share == "direct" ? 0 : PcscNative.SCARD_PROTOCOL_T0 | PcscNative.SCARD_PROTOCOL_T1,
    };

    internal static uint Disposition(string word) => word switch
    {
        "reset" => PcscNative.SCARD_RESET_CARD,
        "unpower" => PcscNative.SCARD_UNPOWER_CARD,
        "eject" => PcscNative.SCARD_EJECT_CARD,
        _ => PcscNative.SCARD_LEAVE_CARD,
    };

    internal static string ProtocolName(uint protocol) => protocol switch
    {
        PcscNative.SCARD_PROTOCOL_T0 => "T0",
        PcscNative.SCARD_PROTOCOL_T1 => "T1",
        PcscNative.SCARD_PROTOCOL_RAW => "raw",
        _ => "none",
    };

    /// <summary>The one word for a reader's dwEventState.</summary>
    internal static string StateName(uint state) =>
        (state & PcscNative.SCARD_STATE_UNAVAILABLE) != 0 ? "unavailable"
        : (state & PcscNative.SCARD_STATE_MUTE) != 0 ? "mute"
        : (state & PcscNative.SCARD_STATE_EXCLUSIVE) != 0 ? "exclusive"
        : (state & PcscNative.SCARD_STATE_INUSE) != 0 ? "inuse"
        : (state & PcscNative.SCARD_STATE_UNPOWERED) != 0 ? "unpowered"
        : (state & PcscNative.SCARD_STATE_PRESENT) != 0 ? "present"
        : "empty";
}

/// <summary>
/// The machine's readers through winscard.dll (device classes plan, stage D12). A machine with no
/// reader usually has the smart-card service stopped, and Windows then answers SCARD_E_NO_SERVICE:
/// that is "no readers" here, not a failure. Each connection has a context of its own.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class WinSmartCards : ISmartCardBackend
{
    public static WinSmartCards Instance { get; } = new();

    private WinSmartCards()
    {
    }

    internal static void Check(int result, string what)
    {
        if (result != 0)
        {
            throw new SmartCardException($"{what}: {SmartCardWords.Describe(result)}.", result);
        }
    }

    /// <summary>Whether a code says the machine has no reader, or no service because it has none.</summary>
    internal static bool MeansNoReaders(int result) =>
        result is PcscNative.SCARD_E_NO_READERS_AVAILABLE or PcscNative.SCARD_E_NO_SERVICE or PcscNative.SCARD_E_SERVICE_STOPPED;

    /// <summary>A context, or 0 when the machine has no reader for the service to run for.</summary>
    internal static nint Establish()
    {
        nint context = 0;
        int result = PcscNative.SCardEstablishContext(PcscNative.SCARD_SCOPE_USER, 0, 0, &context);
        if (MeansNoReaders(result))
        {
            return 0;
        }

        Check(result, "The smart-card service could not be reached");
        return context;
    }

    internal static string[] ReaderNames(nint context)
    {
        uint length = 0;
        int result = PcscNative.SCardListReaders(context, null, null, &length);
        if (MeansNoReaders(result) || length == 0)
        {
            return [];
        }

        Check(result, "The readers could not be listed");
        char[] buffer = new char[length];
        fixed (char* p = buffer)
        {
            result = PcscNative.SCardListReaders(context, null, p, &length);
            if (MeansNoReaders(result))
            {
                return [];
            }

            Check(result, "The readers could not be listed");
        }

        return new string(buffer, 0, (int)length).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Each reader's event state and ATR; waits up to <paramref name="timeout"/> ms for one to differ from <paramref name="known"/>.</summary>
    internal static (uint State, byte[] Atr)[] States(nint context, IReadOnlyList<string> readers, IReadOnlyList<uint>? known, uint timeout, out bool timedOut)
    {
        timedOut = false;
        var states = new PcscNative.SCARD_READERSTATE[readers.Count];
        var names = new nint[readers.Count];
        try
        {
            for (int i = 0; i < readers.Count; i++)
            {
                names[i] = Marshal.StringToHGlobalUni(readers[i]);
                states[i].szReader = names[i];
                states[i].dwCurrentState = known is null ? PcscNative.SCARD_STATE_UNAWARE : known[i] & ~PcscNative.SCARD_STATE_CHANGED;
            }

            fixed (PcscNative.SCARD_READERSTATE* p = states)
            {
                int status = PcscNative.SCardGetStatusChange(context, timeout, p, (uint)states.Length);
                if (status == PcscNative.SCARD_E_TIMEOUT)
                {
                    timedOut = true;
                }
                else
                {
                    Check(status, "The readers' states could not be read");
                }

                var result = new (uint, byte[])[readers.Count];
                for (int i = 0; i < readers.Count; i++)
                {
                    uint state = timedOut && known is not null ? known[i] : p[i].dwEventState;
                    result[i] = (state, new ReadOnlySpan<byte>(p[i].rgbAtr, (int)Math.Min(p[i].cbAtr, 36)).ToArray());
                }

                return result;
            }
        }
        finally
        {
            foreach (nint name in names)
            {
                if (name != 0)
                {
                    Marshal.FreeHGlobal(name);
                }
            }
        }
    }

    public IReadOnlyList<SmartCardReader> Readers()
    {
        nint context = Establish();
        if (context == 0)
        {
            return [];
        }

        try
        {
            string[] readers = ReaderNames(context);
            if (readers.Length == 0)
            {
                return [];
            }

            (uint State, byte[] Atr)[] states = States(context, readers, null, 0, out _);
            return readers.Select((r, i) => new SmartCardReader(r, SmartCardWords.StateName(states[i].State),
                (states[i].State & PcscNative.SCARD_STATE_PRESENT) != 0, states[i].Atr)).ToList();
        }
        finally
        {
            PcscNative.SCardReleaseContext(context);
        }
    }

    public ISmartCard Connect(string reader, string share, string protocol)
    {
        nint context = Establish();
        if (context == 0)
        {
            throw new SmartCardException($"The reader '{reader}' could not be reached: {SmartCardWords.Describe(PcscNative.SCARD_E_NO_READERS_AVAILABLE)}.", PcscNative.SCARD_E_NO_READERS_AVAILABLE);
        }

        try
        {
            nint card = 0;
            uint active = 0;
            Check(PcscNative.SCardConnect(context, reader, SmartCardWords.Share(share), SmartCardWords.Protocols(protocol, share), &card, &active),
                share == "direct" ? $"The reader '{reader}' could not be reached" : $"The card in '{reader}' could not be reached");
            return new WinSmartCard(context, card, reader, active);
        }
        catch
        {
            PcscNative.SCardReleaseContext(context);
            throw;
        }
    }

    public IDisposable Watch(Action<SmartCardEvent> changed) => new WinSmartCardWatch(changed);
}

/// <summary>A connection through winscard.dll.</summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WinSmartCard(nint context, nint card, string reader, uint protocol) : ISmartCard
{
    private nint _context = context;
    private nint _card = card;
    private uint _protocol = protocol;

    public string Reader { get; } = reader;

    public string Protocol => SmartCardWords.ProtocolName(_protocol);

    private (uint State, byte[] Atr) Status()
    {
        char* names = stackalloc char[512];
        byte* atr = stackalloc byte[36];
        uint atrLength = 36;
        uint readerLength = 512;
        uint state = 0;
        uint protocol = 0;
        WinSmartCards.Check(PcscNative.SCardStatus(_card, names, &readerLength, &state, &protocol, atr, &atrLength), "The card's status could not be read");
        return (state, new ReadOnlySpan<byte>(atr, (int)Math.Min(atrLength, 36)).ToArray());
    }

    public byte[] Atr() => Status().Atr;

    public string State() => Status().State switch
    {
        1 => "absent",
        2 => "present",
        3 => "swallowed",
        4 => "powered",
        5 => "negotiable",
        6 => "specific",
        _ => "unknown",
    };

    public byte[] Transmit(ReadOnlySpan<byte> apdu)
    {
        var pci = new PcscNative.SCARD_IO_REQUEST { dwProtocol = _protocol, cbPciLength = (uint)sizeof(PcscNative.SCARD_IO_REQUEST) };
        byte[] response = new byte[65538];
        uint length = (uint)response.Length;
        fixed (byte* send = apdu)
        fixed (byte* receive = response)
        {
            WinSmartCards.Check(PcscNative.SCardTransmit(_card, &pci, send, (uint)apdu.Length, null, receive, &length), "The APDU could not be sent");
        }

        return response[..(int)length];
    }

    public byte[] Control(uint code, ReadOnlySpan<byte> input)
    {
        byte[] output = new byte[65538];
        uint returned = 0;
        fixed (byte* i = input)
        fixed (byte* o = output)
        {
            WinSmartCards.Check(PcscNative.SCardControl(_card, code, input.Length == 0 ? null : i, (uint)input.Length, o, (uint)output.Length, &returned), "The control code failed");
        }

        return output[..(int)returned];
    }

    public void BeginTransaction() => WinSmartCards.Check(PcscNative.SCardBeginTransaction(_card), "The transaction could not begin");

    public void EndTransaction(string disposition) =>
        WinSmartCards.Check(PcscNative.SCardEndTransaction(_card, SmartCardWords.Disposition(disposition)), "The transaction could not end");

    public void Reconnect(string share, string protocol, string initialization)
    {
        uint active = 0;
        WinSmartCards.Check(PcscNative.SCardReconnect(_card, SmartCardWords.Share(share), SmartCardWords.Protocols(protocol, share),
            SmartCardWords.Disposition(initialization), &active), $"The card in '{Reader}' could not be reached again");
        _protocol = active;
    }

    public void Disconnect(string disposition)
    {
        if (_card != 0)
        {
            PcscNative.SCardDisconnect(_card, SmartCardWords.Disposition(disposition));
            _card = 0;
        }

        if (_context != 0)
        {
            PcscNative.SCardReleaseContext(_context);
            _context = 0;
        }
    }

    public void Dispose() => Disconnect("leave");
}

/// <summary>
/// The watch for cards and readers: a thread that lists the readers and waits on their states half a
/// second at a time, so a reader plugged in is seen without the service's notification reader, and a
/// machine whose service is not running yet is asked again until it is.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WinSmartCardWatch : IDisposable
{
    private readonly Action<SmartCardEvent> _changed;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;

    public WinSmartCardWatch(Action<SmartCardEvent> changed)
    {
        _changed = changed;
        _thread = new Thread(Run) { IsBackground = true, Name = "JGraph smart-card watch" };
        _thread.Start();
    }

    private void Run()
    {
        var known = new List<(string Reader, uint State)>();
        nint context = 0;
        bool first = true;
        try
        {
            while (!_stop.IsSet)
            {
                try
                {
                    if (context == 0)
                    {
                        context = WinSmartCards.Establish();
                    }

                    string[] readers = context == 0 ? [] : WinSmartCards.ReaderNames(context);
                    SmartCardDiff.Readers(known, readers, first ? null : Raise);
                    if (known.Count == 0)
                    {
                        first = false;
                        _stop.Wait(500);
                        continue;
                    }

                    // New readers have state 0 (unaware), which answers at once with what is there.
                    bool fresh = known.Exists(static k => k.State == 0);
                    (uint State, byte[] Atr)[] now = WinSmartCards.States(context, known.Select(static k => k.Reader).ToList(),
                        known.Select(static k => k.State).ToList(), fresh ? 0u : 500u, out bool timedOut);
                    if (!timedOut)
                    {
                        SmartCardDiff.Cards(known, now, first ? null : Raise);
                    }

                    first = false;
                }
                catch (SmartCardException)
                {
                    // The service stopped, or a reader left between two calls: start over from nothing.
                    if (context != 0)
                    {
                        PcscNative.SCardReleaseContext(context);
                        context = 0;
                    }

                    SmartCardDiff.Readers(known, [], first ? null : Raise);
                    first = false;
                    _stop.Wait(500);
                }
            }
        }
        finally
        {
            if (context != 0)
            {
                PcscNative.SCardReleaseContext(context);
            }
        }
    }

    private void Raise(SmartCardEvent e)
    {
        try
        {
            _changed(e);
        }
        catch (Exception)
        {
            // A handler's failure must not end the watch, nor the process.
        }
    }

    public void Dispose()
    {
        _stop.Set();
        _thread.Join(2000);
    }
}

/// <summary>What changed between two looks at the readers, as events; shared by the real watch and its tests.</summary>
internal static class SmartCardDiff
{
    /// <summary>Brings <paramref name="known"/> to the readers now listed; a reader that left takes its card with it.</summary>
    public static void Readers(List<(string Reader, uint State)> known, IReadOnlyList<string> readers, Action<SmartCardEvent>? raise)
    {
        for (int i = known.Count - 1; i >= 0; i--)
        {
            if (!readers.Contains(known[i].Reader))
            {
                if ((known[i].State & PcscNative.SCARD_STATE_PRESENT) != 0)
                {
                    raise?.Invoke(new SmartCardEvent("CardRemoved", known[i].Reader, []));
                }

                raise?.Invoke(new SmartCardEvent("ReaderRemoved", known[i].Reader, []));
                known.RemoveAt(i);
            }
        }

        foreach (string reader in readers)
        {
            if (!known.Exists(k => k.Reader == reader))
            {
                known.Add((reader, 0));
                raise?.Invoke(new SmartCardEvent("ReaderAdded", reader, []));
            }
        }
    }

    /// <summary>Takes the readers' new states; a card that came or went is an event.</summary>
    public static void Cards(List<(string Reader, uint State)> known, IReadOnlyList<(uint State, byte[] Atr)> now, Action<SmartCardEvent>? raise)
    {
        for (int i = 0; i < known.Count; i++)
        {
            bool was = (known[i].State & PcscNative.SCARD_STATE_PRESENT) != 0;
            bool present = (now[i].State & PcscNative.SCARD_STATE_PRESENT) != 0;

            // A state of 0 would read as "never seen" on the next pass, so an answered reader keeps a bit.
            uint state = now[i].State & ~PcscNative.SCARD_STATE_CHANGED;
            known[i] = (known[i].Reader, state == 0 ? PcscNative.SCARD_STATE_EMPTY : state);
            if (present && !was)
            {
                raise?.Invoke(new SmartCardEvent("CardInserted", known[i].Reader, now[i].Atr));
            }
            else if (was && !present)
            {
                raise?.Invoke(new SmartCardEvent("CardRemoved", known[i].Reader, []));
            }
        }
    }
}
