using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JGraph.Devices;
using JGraph.Devices.Serial;
using JGraph.Scripting.Devices;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Serial Explorer pane (device classes plan, stage 14, ADR 0197), which <c>serialExplorer</c>
/// shows: a terminal on one serial port, set up with what <c>serialport</c> takes, with a button
/// that writes the equivalent <c>serialport</c> line at the console prompt. The connection is a
/// <see cref="SerialTerminal"/>; this class is its controls.
/// </summary>
public partial class SerialExplorerPane : UserControl, IDisposable
{
    /// <summary>Keep at most this many characters on display; older text is dropped.</summary>
    private const int MaxDisplayChars = 500_000;

    private static readonly int[] BaudRates = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 74880, 115200, 230400, 460800, 921600, 1000000, 2000000, 3000000];

    private readonly SerialTerminal _terminal = new();
    private readonly TerminalText _text = new();

    // Received text is coalesced, as the console's is: the port's reader thread appends and one
    // scheduled flush moves everything queued since onto the display.
    private readonly object _gate = new();
    private readonly StringBuilder _pending = new();
    private bool _flushScheduled;

    private readonly List<string> _history = new();
    private int _historyIndex;
    private bool _busy;

    /// <summary>Creates the pane, not connected.</summary>
    public SerialExplorerPane()
    {
        InitializeComponent();
        foreach (int rate in BaudRates)
        {
            BaudBox.Items.Add(rate.ToString(CultureInfo.InvariantCulture));
        }

        BaudBox.Text = "115200";
        Fill(DataBitsBox, "8", "8", "7", "6", "5");
        Fill(ParityBox, "none", "none", "even", "odd", "mark", "space");
        Fill(StopBitsBox, "1", "1", "1.5", "2");
        Fill(FlowBox, "no flow", "no flow", "hardware", "software");
        Fill(EndingBox, "LF", "LF", "CR", "CR/LF", "none");
        _terminal.Received += OnReceived;
        _terminal.Lost += why => Dispatcher.BeginInvoke(() => OnLost(why));
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && PortBox.Items.Count == 0)
            {
                FillPorts();
            }
        };
    }

    /// <summary>Raised with a line of code to write at the console prompt.</summary>
    public event Action<string>? CodeRequested;

    /// <summary>Raised with a sentence for the window's status bar.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Puts <paramref name="port"/> in the port box, ready to connect; refused with the reason while connected to another.</summary>
    public void SelectPort(string port)
    {
        if (_terminal.Connected && !string.Equals(_terminal.Port, port, StringComparison.OrdinalIgnoreCase))
        {
            StatusChanged?.Invoke($"The Serial Explorer is connected to {_terminal.Port}; disconnect it to choose {port}.");
            return;
        }

        FillPorts();
        PortBox.Text = port;
    }

    /// <inheritdoc />
    public void Dispose() => _terminal.Dispose();

    private static void Fill(ComboBox box, string selected, params string[] choices)
    {
        foreach (string choice in choices)
        {
            box.Items.Add(choice);
        }

        box.SelectedItem = selected;
    }

    /// <summary>Reads the port names again, keeping what is typed. The registry only; no port is opened.</summary>
    private void FillPorts()
    {
        string typed = PortBox.Text;
        PortBox.Items.Clear();
        if (OperatingSystem.IsWindows())
        {
            foreach (string port in SerialPortList.All())
            {
                PortBox.Items.Add(port);
            }
        }

        PortBox.Text = typed.Length > 0 ? typed : PortBox.Items.Count > 0 ? (string)PortBox.Items[0] : "";
    }

    private void OnPortDropDownOpened(object? sender, EventArgs e) => FillPorts();

    // --- the connection ------------------------------------------------------------------------------

    /// <summary>The settings the boxes hold, or null with <paramref name="problem"/> saying which is wrong.</summary>
    private SerialSettings? Settings(out string problem)
    {
        problem = "";
        if (!int.TryParse(BaudBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int baud) || baud <= 0)
        {
            problem = "The baud rate is a positive whole number.";
            return null;
        }

        return new SerialSettings
        {
            BaudRate = baud,
            DataBits = int.Parse((string)DataBitsBox.SelectedItem, CultureInfo.InvariantCulture),
            Parity = (string)ParityBox.SelectedItem switch
            {
                "even" => SerialParity.Even,
                "odd" => SerialParity.Odd,
                "mark" => SerialParity.Mark,
                "space" => SerialParity.Space,
                _ => SerialParity.None,
            },
            StopBits = (string)StopBitsBox.SelectedItem switch
            {
                "1.5" => SerialStopBits.OnePointFive,
                "2" => SerialStopBits.Two,
                _ => SerialStopBits.One,
            },
            FlowControl = (string)FlowBox.SelectedItem switch
            {
                "hardware" => SerialFlowControl.Hardware,
                "software" => SerialFlowControl.Software,
                _ => SerialFlowControl.None,
            },
        };
    }

    private TerminalEnding Ending => (string)EndingBox.SelectedItem switch
    {
        "LF" => TerminalEnding.LF,
        "CR" => TerminalEnding.CR,
        "CR/LF" => TerminalEnding.CRLF,
        _ => TerminalEnding.None,
    };

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_terminal.Connected)
        {
            string was = _terminal.Port;
            _terminal.Disconnect();
            ShowConnected(false);
            StateText.Text = "Disconnected from " + was;
            return;
        }

        string port = PortBox.Text.Trim();
        if (port.Length == 0)
        {
            StateText.Text = "Choose a port first.";
            return;
        }

        if (Settings(out string problem) is not { } settings)
        {
            StateText.Text = problem;
            return;
        }

        bool dtr = DtrBox.IsChecked == true;
        bool rts = RtsBox.IsChecked == true;
        _busy = true;
        ConnectButton.IsEnabled = false;
        StateText.Text = $"Opening {port}…";
        string? failure = null;
        try
        {
            // Off the UI thread: a Bluetooth port can take seconds to open.
            await Task.Run(() => _terminal.Connect(port, settings, dtr, rts)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // An event handler is the end of the road: whatever opening the port threw is shown, not rethrown.
            failure = ex.Message;
        }

        _busy = false;
        ConnectButton.IsEnabled = true;
        if (failure is not null)
        {
            ShowConnected(false);
            StateText.Text = failure;
            return;
        }

        lock (_gate)
        {
            _text.Reset();
        }

        ShowConnected(true);
        StateText.Text = $"Connected to {port} at {settings.BaudRate.ToString(CultureInfo.InvariantCulture)} baud";
        SendBox.Focus();
    }

    private void ShowConnected(bool connected)
    {
        ConnectButton.Content = connected ? "Disconnect" : "Connect";
        SendButton.IsEnabled = connected;
        foreach (Control box in new Control[] { PortBox, BaudBox, DataBitsBox, ParityBox, StopBitsBox, FlowBox })
        {
            box.IsEnabled = !connected;
        }
    }

    private void OnLost(string why)
    {
        ShowConnected(false);
        StateText.Text = "Connection lost: " + why;
        StatusChanged?.Invoke("The Serial Explorer lost its port: " + why);
    }

    private void OnPinClick(object sender, RoutedEventArgs e)
    {
        if (!_terminal.Connected)
        {
            return; // the boxes then say how the port will be opened
        }

        try
        {
            if (ReferenceEquals(sender, DtrBox))
            {
                _terminal.SetDtr(DtrBox.IsChecked == true);
            }
            else
            {
                _terminal.SetRts(RtsBox.IsChecked == true);
            }
        }
        catch (Exception ex) when (ex is DeviceIOException or DeviceSettingsException or InvalidOperationException)
        {
            StateText.Text = ex.Message;
        }
    }

    // --- sending -------------------------------------------------------------------------------------

    private void OnSendClick(object sender, RoutedEventArgs e) => SendLine();

    private void OnSendBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                SendLine();
                break;
            case Key.Up:
                e.Handled = Recall(-1);
                break;
            case Key.Down:
                e.Handled = Recall(+1);
                break;
        }
    }

    private bool Recall(int direction)
    {
        if (_history.Count == 0)
        {
            return false;
        }

        int target = Math.Clamp(_historyIndex + direction, 0, _history.Count);
        if (target == _historyIndex)
        {
            return false;
        }

        _historyIndex = target;
        SendBox.Text = target == _history.Count ? string.Empty : _history[target];
        SendBox.CaretIndex = SendBox.Text.Length;
        return true;
    }

    private async void SendLine()
    {
        if (_busy || !_terminal.Connected)
        {
            return;
        }

        string line = SendBox.Text;
        byte[] bytes;
        try
        {
            bytes = SendHexBox.IsChecked == true
                ? SerialTerminal.ParseHex(line)
                : Encoding.UTF8.GetBytes(line + SerialTerminal.EndingText(Ending));
        }
        catch (FormatException ex)
        {
            StateText.Text = ex.Message;
            return;
        }

        if (bytes.Length == 0)
        {
            return;
        }

        if (line.Length > 0 && (_history.Count == 0 || _history[^1] != line))
        {
            _history.Add(line);
        }

        _historyIndex = _history.Count;
        SendBox.Clear();
        _busy = true;
        SendButton.IsEnabled = false;
        string? failure = null;
        try
        {
            // Off the UI thread: with flow control on, a write waits for the far end.
            await Task.Run(() => _terminal.Send(bytes)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failure = ex is TimeoutException ? "The write timed out: nothing is taking the bytes." : ex.Message;
        }

        _busy = false;
        SendButton.IsEnabled = _terminal.Connected;
        StateText.Text = failure ?? Counters();
    }

    private string Counters() => string.Create(CultureInfo.InvariantCulture,
        $"{_terminal.Port}: {_terminal.BytesSent} bytes sent, {_terminal.BytesReceived} received");

    // --- the display ---------------------------------------------------------------------------------

    /// <summary>Runs on the port's reader thread.</summary>
    private void OnReceived(byte[] bytes)
    {
        bool schedule;
        lock (_gate)
        {
            _pending.Append(_text.Format(bytes));
            schedule = !_flushScheduled;
            _flushScheduled = true;
        }

        if (schedule)
        {
            Dispatcher.BeginInvoke(Flush);
        }
    }

    private void Flush()
    {
        string batch;
        lock (_gate)
        {
            _flushScheduled = false;
            batch = _pending.ToString();
            _pending.Clear();
        }

        if (batch.Length == 0)
        {
            return;
        }

        ReceivedBox.AppendText(batch);
        if (ReceivedBox.Text.Length > MaxDisplayChars)
        {
            string text = ReceivedBox.Text;
            int cut = text.IndexOf('\n', text.Length - (MaxDisplayChars / 2));
            ReceivedBox.Text = text[(cut < 0 ? text.Length - (MaxDisplayChars / 2) : cut + 1)..];
        }

        ReceivedBox.CaretIndex = ReceivedBox.Text.Length;
        ReceivedBox.ScrollToEnd();
        if (!_busy && _terminal.Connected)
        {
            StateText.Text = Counters();
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        lock (_gate)
        {
            _pending.Clear();
            _text.Reset();
        }

        ReceivedBox.Clear();
    }

    private void OnShowHexClick(object sender, RoutedEventArgs e)
    {
        lock (_gate)
        {
            _text.Hex = ShowHexBox.IsChecked == true;
            _text.Reset();
            if (ReceivedBox.Text.Length > 0 || _pending.Length > 0)
            {
                _pending.Append('\n'); // a change of notation starts on a line of its own
            }
        }

        Dispatcher.BeginInvoke(Flush);
    }

    private void OnCodeClick(object sender, RoutedEventArgs e)
    {
        string port = PortBox.Text.Trim();
        if (port.Length == 0)
        {
            StateText.Text = "Choose a port first.";
            return;
        }

        if (Settings(out string problem) is not { } settings)
        {
            StateText.Text = problem;
            return;
        }

        CodeRequested?.Invoke(SerialTerminal.CodeFor(port, settings, Ending));
        if (_terminal.Connected)
        {
            StatusChanged?.Invoke($"Disconnect the Serial Explorer from {port} before running the line: a port has one owner.");
        }
    }
}
