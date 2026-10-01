using System.Globalization;
using System.Text;
using JGraph.Devices;
using JGraph.Devices.Serial;

namespace JGraph.Scripting.Devices;

/// <summary>What a terminal adds after a line it sends.</summary>
public enum TerminalEnding
{
    /// <summary>Nothing: the text as typed.</summary>
    None,

    /// <summary>A line feed, <c>"LF"</c> to <c>configureTerminator</c>.</summary>
    LF,

    /// <summary>A carriage return, <c>"CR"</c>.</summary>
    CR,

    /// <summary>A carriage return and a line feed, <c>"CR/LF"</c>.</summary>
    CRLF,
}

/// <summary>
/// The Serial Explorer pane's connection (device classes plan, stage 14, ADR 0197): one serial port,
/// opened with the settings <c>serialport</c> takes, whose received bytes are handed on as they come.
/// Free of any UI type. The pane holds the port itself, so while it is connected a script's
/// <c>serialport</c> of the same name is refused as in use, and the other way round, as with any two
/// programs and one port.
/// </summary>
public sealed class SerialTerminal : IDisposable
{
    private readonly Func<string, SerialSettings, bool, bool, ISerialTransport> _open;
    private readonly object _gate = new();
    private ISerialTransport? _port;

    /// <summary>Makes a terminal over the machine's ports.</summary>
    public SerialTerminal()
        : this(static (port, settings, dtr, rts) => OperatingSystem.IsWindows()
            ? Win32SerialPort.Open(port, settings, dtr, rts)
            : throw new DeviceOpenException("Serial ports are supported on Windows only."))
    {
    }

    /// <summary>Makes a terminal over whatever <paramref name="open"/> answers: a test's simulated port.</summary>
    public SerialTerminal(Func<string, SerialSettings, bool, bool, ISerialTransport> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _open = open;
    }

    /// <summary>Raised on the port's reader thread with each run of bytes received.</summary>
    public event Action<byte[]>? Received;

    /// <summary>Raised once, on a background thread, when the port is lost (an adapter unplugged), with why.</summary>
    public event Action<string>? Lost;

    /// <summary>Whether a port is open.</summary>
    public bool Connected
    {
        get
        {
            lock (_gate)
            {
                return _port is { Connected: true };
            }
        }
    }

    /// <summary>The open port's name, or empty.</summary>
    public string Port
    {
        get
        {
            lock (_gate)
            {
                return _port?.Name ?? "";
            }
        }
    }

    /// <summary>Bytes received since the port opened.</summary>
    public long BytesReceived
    {
        get
        {
            lock (_gate)
            {
                return _port?.Input.TotalReceived ?? 0;
            }
        }
    }

    /// <summary>Bytes sent since the port opened.</summary>
    public long BytesSent
    {
        get
        {
            lock (_gate)
            {
                return _port?.BytesWritten ?? 0;
            }
        }
    }

    /// <summary>
    /// Opens <paramref name="port"/>, closing the one open before. Throws <see cref="DeviceOpenException"/>
    /// when the port does not exist, is in use, or refuses the settings.
    /// </summary>
    public void Connect(string port, SerialSettings settings, bool dtr = true, bool rts = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(port);
        ArgumentNullException.ThrowIfNull(settings);
        Disconnect();
        ISerialTransport opened;
        try
        {
            opened = _open(port.Trim(), settings, dtr, rts);
        }
        catch (DeviceSettingsException refused)
        {
            throw new DeviceOpenException(refused.Message, refused.Win32Error);
        }

        // The buffer is emptied as it fills: the terminal shows bytes and keeps none back for a read.
        opened.Input.Appended += (_, _) =>
        {
            byte[] bytes = opened.Input.TakeAll();
            if (bytes.Length > 0)
            {
                Received?.Invoke(bytes);
            }
        };
        opened.ConnectionLost += lost =>
        {
            bool mine;
            lock (_gate)
            {
                mine = ReferenceEquals(_port, opened);
                if (mine)
                {
                    _port = null;
                }
            }

            if (mine)
            {
                try
                {
                    opened.Dispose();
                }
                catch (Exception)
                {
                    // A port that is gone may refuse to close; there is nothing left to release.
                }

                Lost?.Invoke(lost.Message);
            }
        };
        lock (_gate)
        {
            _port = opened;
        }
    }

    /// <summary>Closes the port; nothing when none is open.</summary>
    public void Disconnect()
    {
        ISerialTransport? port;
        lock (_gate)
        {
            port = _port;
            _port = null;
        }

        port?.Dispose();
    }

    /// <summary>Sends text as UTF-8, then the ending. Throws <see cref="InvalidOperationException"/> when no port is open.</summary>
    public void Send(string text, TerminalEnding ending)
    {
        ArgumentNullException.ThrowIfNull(text);
        Send(Encoding.UTF8.GetBytes(text + EndingText(ending)));
    }

    /// <summary>Sends bytes as they are.</summary>
    public void Send(ReadOnlySpan<byte> bytes)
    {
        ISerialTransport port;
        lock (_gate)
        {
            port = _port ?? throw new InvalidOperationException("No port is connected.");
        }

        if (!bytes.IsEmpty)
        {
            port.Write(bytes, TimeSpan.FromSeconds(10), CancellationToken.None);
        }
    }

    /// <summary>Raises or lowers DTR on the open port.</summary>
    public void SetDtr(bool on) => Open().SetDtr(on);

    /// <summary>Raises or lowers RTS on the open port.</summary>
    public void SetRts(bool on) => Open().SetRts(on);

    private ISerialTransport Open()
    {
        lock (_gate)
        {
            return _port ?? throw new InvalidOperationException("No port is connected.");
        }
    }

    /// <inheritdoc />
    public void Dispose() => Disconnect();

    // --- text ----------------------------------------------------------------------------------------

    /// <summary>The characters an ending is.</summary>
    public static string EndingText(TerminalEnding ending) => ending switch
    {
        TerminalEnding.LF => "\n",
        TerminalEnding.CR => "\r",
        TerminalEnding.CRLF => "\r\n",
        _ => "",
    };

    /// <summary>
    /// The bytes hex text names: pairs of hex digits, parted by spaces, commas or nothing, each
    /// optionally written <c>0x48</c>. Throws <see cref="FormatException"/> naming what is wrong.
    /// </summary>
    public static byte[] ParseHex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new List<byte>();
        foreach (string word in text.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string digits = word.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? word[2..] : word;
            if (digits.Length == 0 || digits.Length % 2 != 0)
            {
                throw new FormatException($"'{word}' is not whole bytes: hex bytes are two digits each, as in 48 65 6C.");
            }

            for (int i = 0; i < digits.Length; i += 2)
            {
                if (!byte.TryParse(digits.AsSpan(i, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte value))
                {
                    throw new FormatException($"'{word}' is not hex: the digits are 0-9 and A-F.");
                }

                bytes.Add(value);
            }
        }

        return [.. bytes];
    }

    /// <summary>
    /// The script that does what the terminal is set to do: the <c>serialport</c> line with every
    /// setting that is not its default, and the terminator when one is chosen.
    /// </summary>
    public static string CodeFor(string port, SerialSettings settings, TerminalEnding ending)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(settings);
        var line = new StringBuilder();
        line.Append("s = serialport(\"").Append(port.Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\", ")
            .Append(settings.BaudRate.ToString(CultureInfo.InvariantCulture));
        if (settings.DataBits != 8)
        {
            line.Append(", DataBits=").Append(settings.DataBits.ToString(CultureInfo.InvariantCulture));
        }

        if (settings.Parity != SerialParity.None)
        {
            line.Append(", Parity=\"").Append(settings.Parity.ToString().ToLowerInvariant()).Append('"');
        }

        if (settings.StopBits != SerialStopBits.One)
        {
            line.Append(", StopBits=").Append(settings.StopBits == SerialStopBits.Two ? "2" : "1.5");
        }

        if (settings.FlowControl != SerialFlowControl.None)
        {
            line.Append(", FlowControl=\"").Append(settings.FlowControl.ToString().ToLowerInvariant()).Append('"');
        }

        line.Append(");");
        string terminator = ending switch
        {
            TerminalEnding.LF => "LF",
            TerminalEnding.CR => "CR",
            TerminalEnding.CRLF => "CR/LF",
            _ => "",
        };
        if (terminator.Length > 0)
        {
            line.Append(" configureTerminator(s, \"").Append(terminator).Append("\");");
        }

        return line.ToString();
    }
}

/// <summary>
/// Turns received bytes into what a terminal shows, a run at a time: as text (UTF-8, with a line
/// break for CR, LF or the pair, and the colour codes ESP-IDF and other firmware log with left out),
/// or as hex, sixteen bytes a line. It keeps what a run ends in the middle of, so a character or an
/// escape sequence split across two runs still comes out whole.
/// </summary>
public sealed class TerminalText
{
    private readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();
    private bool _afterCarriageReturn;
    private int _escape; // 0 outside a sequence, 1 after ESC, 2 inside ESC [ … up to its final byte
    private int _hexColumn;

    /// <summary>Whether bytes are shown as hex.</summary>
    public bool Hex { get; set; }

    /// <summary>Forgets a half-read character or sequence and the hex column: after the display is cleared.</summary>
    public void Reset()
    {
        _utf8.Reset();
        _afterCarriageReturn = false;
        _escape = 0;
        _hexColumn = 0;
    }

    /// <summary>What to append to the display for <paramref name="bytes"/>.</summary>
    public string Format(ReadOnlySpan<byte> bytes)
    {
        var shown = new StringBuilder();
        if (Hex)
        {
            foreach (byte b in bytes)
            {
                shown.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                shown.Append(++_hexColumn % 16 == 0 ? '\n' : ' ');
            }

            return shown.ToString();
        }

        char[] decoded = new char[_utf8.GetCharCount(bytes, flush: false)];
        int count = _utf8.GetChars(bytes, decoded, flush: false);
        for (int i = 0; i < count; i++)
        {
            char c = decoded[i];
            if (_escape == 1)
            {
                _escape = c == '[' ? 2 : 0; // ESC [ opens a control sequence; ESC and one other character is dropped whole
                continue;
            }

            if (_escape == 2)
            {
                if (c is >= '@' and <= '~')
                {
                    _escape = 0; // the final byte: the sequence is over
                }

                continue;
            }

            bool afterCr = _afterCarriageReturn;
            _afterCarriageReturn = false;
            switch (c)
            {
                case '\u001b':
                    _escape = 1;
                    break;
                case '\r':
                    shown.Append('\n');
                    _afterCarriageReturn = true;
                    break;
                case '\n':
                    if (!afterCr)
                    {
                        shown.Append('\n');
                    }

                    break;
                case '\t':
                    shown.Append('\t');
                    break;
                default:
                    if (!char.IsControl(c))
                    {
                        shown.Append(c);
                    }

                    break;
            }
        }

        return shown.ToString();
    }
}
