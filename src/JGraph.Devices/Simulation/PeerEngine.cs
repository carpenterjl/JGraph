using System.Globalization;
using System.Text;

namespace JGraph.Devices.Simulation;

/// <summary>What the peer engine drives: the far end of a serial line.</summary>
public interface IPeerLink
{
    /// <summary>Sends bytes toward the port under test.</summary>
    void Send(ReadOnlySpan<byte> bytes);

    /// <summary>Raises or lowers the far end's RTS (the near end's CTS, through the null-modem wiring).</summary>
    void SetRts(bool on);

    /// <summary>Raises or lowers the far end's DTR (the near end's DSR and DCD).</summary>
    void SetDtr(bool on);

    /// <summary>The far end's input pins: the near end's RTS as CTS, its DTR as DSR and DCD.</summary>
    SerialPins Pins { get; }

    /// <summary>Holds the far end's line in break for <paramref name="milliseconds"/>.</summary>
    void SendBreak(int milliseconds);
}

/// <summary>
/// A link to a message-based instrument (a HiSLIP or USBTMC session, or the simulated VISA
/// instrument): what the engine's <c>stb</c> command sets is the status byte a status query reads.
/// </summary>
public interface IInstrumentLink
{
    /// <summary>The status byte a service-request status query answers.</summary>
    byte StatusByte { get; set; }

    /// <summary>Signals a service request (HiSLIP's AsyncServiceRequest, USBTMC's SRQ interrupt).</summary>
    void RequestService();
}

/// <summary>
/// The scriptable device on the far end of a serial line (device classes plan, test strategy): the
/// same engine behind <c>peer-sim.exe</c> on a com0com port for R2025b and behind JGraph's in-process
/// simulated port, so a fixture prints the same lines in both.
/// </summary>
/// <remarks>
/// <para>
/// The device is driven in band. Bytes the port under test writes are <em>data</em>: logged, echoed
/// when echo is on, and matched against the reply rules. A command is framed as
/// <c>ESC ESC {</c> text <c>}</c>; its text is never data. The commands:
/// </para>
/// <list type="table">
/// <item><term><c>send HEX</c></term><description>send those bytes now</description></item>
/// <item><term><c>later MS HEX</c></term><description>send them after MS milliseconds</description></item>
/// <item><term><c>chunks MS N HEX</c></term><description>send them N bytes at a time, one chunk every MS milliseconds</description></item>
/// <item><term><c>echo on|off</c></term><description>send every data byte back</description></item>
/// <item><term><c>recv</c></term><description>answer the data logged since the last <c>recv</c>: eight hex digits of its length N, then 2N hex digits</description></item>
/// <item><term><c>status</c></term><description>answer eight characters: the far end's CTS, DSR, DCD and RI as <c>0</c>/<c>1</c>, then the breaks received as four hex digits</description></item>
/// <item><term><c>pins rts=0|1 dtr=0|1</c></term><description>set the far end's output pins</description></item>
/// <item><term><c>break MS</c></term><description>send a break MS milliseconds long</description></item>
/// <item><term><c>on HEX HEX</c></term><description>whenever the data received ends with the first bytes, send the second</description></item>
/// <item><term><c>stb HEX</c></term><description>set the status byte a status query reads (instrument links only)</description></item>
/// <item><term><c>srq</c></term><description>signal a service request (instrument links only)</description></item>
/// <item><term><c>inst</c></term><description>answer eight hex digits: the triggers, then the device clears, received since the reset</description></item>
/// <item><term><c>reset</c></term><description>forget the log, the rules and echo, lower the pins, zero the status byte and the counts</description></item>
/// <item><term><c>quit</c></term><description>raise <see cref="QuitRequested"/></description></item>
/// </list>
/// <para>An unknown or malformed command is answered with <c>?</c> and nothing else happens.</para>
/// </remarks>
public sealed class PeerEngine : IDisposable
{
    private static readonly byte[] Start = [0x1B, 0x1B, (byte)'{'];

    private readonly object _gate = new();
    private readonly IPeerLink _link;
    private readonly List<byte> _log = new();
    private readonly List<byte> _window = new();
    private readonly List<(byte[] Match, byte[] Reply)> _rules = new();
    private readonly List<(byte[] Match, byte[] Reply)> _standing = new();
    private readonly List<Timer> _timers = new();
    private readonly List<byte> _held = new();
    private readonly StringBuilder _command = new();
    private bool _inCommand;
    private bool _echo;
    private int _breaks;
    private int _triggers;
    private int _clears;
    private bool _disposed;

    public PeerEngine(IPeerLink link)
    {
        _link = link;
    }

    /// <summary>Raised when the far end is told to quit.</summary>
    public event Action? QuitRequested;

    /// <summary>Every data byte received since the engine started, for tests.</summary>
    public long DataReceived { get; private set; }

    /// <summary>The far end saw a break (called by the link's owner).</summary>
    public void BreakReceived()
    {
        lock (_gate)
        {
            _breaks++;
        }
    }

    /// <summary>The instrument was triggered (a GET, a HiSLIP Trigger message; called by the link's owner).</summary>
    public void TriggerReceived()
    {
        lock (_gate)
        {
            _triggers++;
        }
    }

    /// <summary>The instrument was cleared (a device clear; called by the link's owner).</summary>
    public void ClearReceived()
    {
        lock (_gate)
        {
            _clears++;
        }
    }

    /// <summary>
    /// Adds a reply rule that <c>reset</c> keeps: an instrument's answer to <c>*IDN?</c>, which a
    /// connection may ask before a fixture can speak to the engine.
    /// </summary>
    public void AddStandingRule(ReadOnlySpan<byte> match, ReadOnlySpan<byte> reply)
    {
        lock (_gate)
        {
            _standing.Add((match.ToArray(), reply.ToArray()));
        }
    }

    /// <summary>Takes bytes that reached the far end.</summary>
    public void Receive(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            foreach (byte b in bytes)
            {
                Step(b);
            }
        }
    }

    private void Step(byte b)
    {
        if (_inCommand)
        {
            if (b == (byte)'}')
            {
                _inCommand = false;
                string text = _command.ToString();
                _command.Clear();
                Run(text);
            }
            else if (_command.Length < 1 << 20)
            {
                _command.Append((char)b);
            }

            return;
        }

        if (b == Start[_held.Count])
        {
            _held.Add(b);
            if (_held.Count == Start.Length)
            {
                _held.Clear();
                _inCommand = true;
            }

            return;
        }

        if (_held.Count > 0)
        {
            // Not a command after all: what was held is data, and this byte starts over.
            byte[] held = _held.ToArray();
            _held.Clear();
            foreach (byte h in held)
            {
                Data(h);
            }

            Step(b);
            return;
        }

        Data(b);
    }

    private void Data(byte b)
    {
        DataReceived++;
        _log.Add(b);
        _window.Add(b);
        if (_window.Count > 4096)
        {
            _window.RemoveRange(0, 2048);
        }

        if (_echo)
        {
            _link.Send([b]);
        }

        foreach ((byte[] match, byte[] reply) in _standing.Concat(_rules))
        {
            if (EndsWith(_window, match))
            {
                _link.Send(reply);
            }
        }
    }

    private static bool EndsWith(List<byte> window, byte[] match)
    {
        if (match.Length == 0 || window.Count < match.Length)
        {
            return false;
        }

        for (int i = 0; i < match.Length; i++)
        {
            if (window[window.Count - match.Length + i] != match[i])
            {
                return false;
            }
        }

        return true;
    }

    private void Run(string text)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            Refuse();
            return;
        }

        try
        {
            switch (words[0].ToLowerInvariant())
            {
                case "send":
                    _link.Send(Hex(words, 1));
                    break;
                case "later":
                    Schedule(Int(words, 1), Hex(words, 2));
                    break;
                case "chunks":
                {
                    int every = Int(words, 1);
                    int size = Math.Max(1, Int(words, 2));
                    byte[] all = Hex(words, 3);
                    for (int at = 0, k = 1; at < all.Length; at += size, k++)
                    {
                        Schedule(every * k, all.AsSpan(at, Math.Min(size, all.Length - at)).ToArray());
                    }

                    break;
                }

                case "echo":
                    _echo = words.Length > 1 && words[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    break;
                case "recv":
                {
                    var reply = new StringBuilder(8 + (2 * _log.Count));
                    reply.Append(_log.Count.ToString("X8", CultureInfo.InvariantCulture));
                    foreach (byte b in _log)
                    {
                        reply.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                    }

                    _log.Clear();
                    _link.Send(Encoding.ASCII.GetBytes(reply.ToString()));
                    break;
                }

                case "status":
                {
                    SerialPins pins = _link.Pins;
                    string reply = $"{Bit(pins.ClearToSend)}{Bit(pins.DataSetReady)}{Bit(pins.CarrierDetect)}{Bit(pins.RingIndicator)}{Math.Min(_breaks, 0xFFFF):X4}";
                    _link.Send(Encoding.ASCII.GetBytes(reply));
                    break;
                }

                case "pins":
                    foreach (string word in words.Skip(1))
                    {
                        string[] pair = word.Split('=');
                        bool on = pair.Length == 2 && pair[1] == "1";
                        if (pair[0].Equals("rts", StringComparison.OrdinalIgnoreCase))
                        {
                            _link.SetRts(on);
                        }
                        else if (pair[0].Equals("dtr", StringComparison.OrdinalIgnoreCase))
                        {
                            _link.SetDtr(on);
                        }
                    }

                    break;
                case "break":
                    _link.SendBreak(Int(words, 1));
                    break;
                case "on":
                    _rules.Add((Hex(words, 1), Hex(words, 2)));
                    break;
                case "stb":
                {
                    byte[] stb = Hex(words, 1);
                    if (stb.Length != 1 || _link is not IInstrumentLink instrument)
                    {
                        throw new FormatException();
                    }

                    instrument.StatusByte = stb[0];
                    break;
                }

                case "srq":
                    if (_link is not IInstrumentLink requester)
                    {
                        throw new FormatException();
                    }

                    requester.RequestService();
                    break;
                case "inst":
                    _link.Send(Encoding.ASCII.GetBytes($"{Math.Min(_triggers, 0xFFFF):X4}{Math.Min(_clears, 0xFFFF):X4}"));
                    break;
                case "reset":
                    ResetLocked();
                    break;
                case "quit":
                    ThreadPool.QueueUserWorkItem(_ => QuitRequested?.Invoke());
                    break;
                default:
                    Refuse();
                    break;
            }
        }
        catch (FormatException)
        {
            Refuse();
        }
    }

    private void ResetLocked()
    {
        _log.Clear();
        _window.Clear();
        _rules.Clear();
        _echo = false;
        _breaks = 0;
        _triggers = 0;
        _clears = 0;
        if (_link is IInstrumentLink instrument)
        {
            instrument.StatusByte = 0;
        }

        foreach (Timer timer in _timers)
        {
            timer.Dispose();
        }

        _timers.Clear();
        _link.SetRts(false);
        _link.SetDtr(false);
    }

    private void Schedule(int milliseconds, byte[] bytes)
    {
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            lock (_gate)
            {
                if (_disposed || !_timers.Remove(timer!))
                {
                    return;
                }

                timer!.Dispose();
                try
                {
                    _link.Send(bytes);
                }
                catch (Exception)
                {
                    // A timer thread must not end the process: the port went away under the send.
                }
            }
        });
        _timers.Add(timer);
        timer.Change(Math.Max(0, milliseconds), Timeout.Infinite);
    }

    private void Refuse() => _link.Send("?"u8);

    private static char Bit(bool on) => on ? '1' : '0';

    private static int Int(string[] words, int at) =>
        at < words.Length && int.TryParse(words[at], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0
            ? value
            : throw new FormatException();

    private static byte[] Hex(string[] words, int at)
    {
        if (at >= words.Length)
        {
            return [];
        }

        string hex = words[at];
        return hex.Length % 2 == 0 ? Convert.FromHexString(hex) : throw new FormatException();
    }

    /// <summary>Encodes a command frame, for the tests and the fixtures' helpers.</summary>
    public static byte[] Frame(string command) => [.. Start, .. Encoding.ASCII.GetBytes(command), (byte)'}'];

    /// <summary>Cancels what is scheduled.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (Timer timer in _timers)
            {
                timer.Dispose();
            }

            _timers.Clear();
        }
    }
}
