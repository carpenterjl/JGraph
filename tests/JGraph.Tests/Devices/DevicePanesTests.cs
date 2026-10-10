using System.Runtime.Versioning;
using System.Text;
using JGraph.Devices;
using JGraph.Devices.Serial;
using JGraph.Devices.Simulation;
using JGraph.Devices.Usb;
using JGraph.Scripting;
using JGraph.Scripting.Completion;
using JGraph.Scripting.Devices;
using JGraph.Scripting.Jgs.Completion;
using JGraph.Tests.Scripting;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// The app's device panes and the editor's device completion (device classes plan, stage D13,
/// ADR 0197), through the UI-free models the panes bind to. The machine's own devices are only
/// enumerated, as <c>jgraph.usb.devices</c> enumerates them; the terminal runs on the simulated port.
/// </summary>
[Collection("JG facade")]
public class DevicePanesTests
{
    // --- the Devices pane's tree ---------------------------------------------------------------------

    private static UsbDeviceInfo Usb(string id, string parent, int vid, int pid, string product, int port, bool hub = false, string serial = "") => new()
    {
        InstanceId = id,
        ParentInstanceId = parent,
        VendorId = vid,
        ProductId = pid,
        Product = product,
        SerialNumber = serial,
        Bus = 1,
        Ports = port == 0 ? [] : [port],
        IsHub = hub,
        Driver = hub ? "USBHUB3" : "usbccgp",
        Speed = hub ? UsbSpeed.Unknown : UsbSpeed.Full,
        DeviceDescriptor = hub ? [] : new byte[18],
    };

    private static DeviceSnapshot Snapshot()
    {
        const string Root = @"USB\ROOT_HUB30\4&1";
        UsbDeviceInfo[] usb =
        [
            Usb(Root, @"PCI\VEN_8086", 0, 0, "USB Root Hub (USB 3.0)", 0, hub: true),
            Usb(@"USB\VID_1209&PID_0001\B", Root, 0x1209, 0x0001, "Probe", 4, serial: "B"),
            Usb(@"USB\VID_0483&PID_5740\S1", Root, 0x0483, 0x5740, "STM32 Virtual ComPort", 2, serial: "S1"),
            Usb(@"USB\VID_1209&PID_0001\A", Root, 0x1209, 0x0001, "Probe", 3, serial: "A"),
            Usb(@"USB\VID_0B05&PID_19B6\K", Root, 0x0B05, 0x19B6, "Keyboard", 1),
        ];
        return new DeviceSnapshot(
            ["COM3", "COM7", "COM20"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["COM3"] = "USB Serial Device",
                ["COM7"] = "Standard Serial over Bluetooth link",
            },
            usb,
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) { [@"USB\VID_0483&PID_5740\S1"] = ["COM3"] },
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [@"USB\VID_0B05&PID_19B6\K"] = 3,
                [@"USB\VID_1209&PID_0001\A"] = 1,
                [@"USB\VID_1209&PID_0001\B"] = 1,
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"USB\VID_1209&PID_0001\B" });
    }

    [Fact]
    public void TheSerialGroupSaysWhoseEachPortIsAndHowToOpenIt()
    {
        IReadOnlyList<DeviceNode> groups = DevicePaneModel.Build(Snapshot());
        Assert.Equal(["Serial ports", "USB"], groups.Select(static g => g.Title));
        DeviceNode serial = groups[0];
        Assert.Equal(("3", DeviceNodeKind.Group), (serial.Detail, serial.Kind));
        Assert.Equal(["COM3", "COM7", "COM20"], serial.Children.Select(static c => c.Title));

        // A USB device's port names the device; another says what Windows calls it; a com0com port says nothing.
        Assert.Equal(
            ["0483:5740 STM32 Virtual ComPort", "Standard Serial over Bluetooth link", ""],
            serial.Children.Select(static c => c.Detail));
        Assert.Equal("USB Serial Device\nUSB device USB\\VID_0483&PID_5740\\S1", serial.Children[0].ToolTip);

        DeviceNode port = serial.Children[0];
        Assert.Equal(DeviceNodeKind.SerialPort, port.Kind);
        Assert.Equal(
            [("Open as serialport", "s = serialport(\"COM3\", 9600)", DeviceActionKind.Code), ("Open in Serial Explorer", "COM3", DeviceActionKind.SerialExplorer)],
            port.Actions.Select(static a => (a.Title, a.Text, a.Kind)));
    }

    [Fact]
    public void TheUsbGroupIsTheTreeInPortOrderWithTheCodeThatOpensEachDevice()
    {
        DeviceNode usb = DevicePaneModel.Build(Snapshot())[1];
        Assert.Equal("4", usb.Detail); // four devices; the hub is not one
        DeviceNode root = Assert.Single(usb.Children);
        Assert.Equal(("Bus 1: USB Root Hub (USB 3.0)", DeviceNodeKind.Hub), (root.Title, root.Kind));
        Assert.Equal(
            ["Port 1: 0B05:19B6 Keyboard", "Port 2: 0483:5740 STM32 Virtual ComPort", "Port 3: 1209:0001 Probe", "Port 4: 1209:0001 Probe"],
            root.Children.Select(static c => c.Title));
        Assert.All(root.Children, static c => Assert.Equal(DeviceNodeKind.Device, c.Kind));

        // Three collections are listed, since jgraph.usb.hid wants the one named.
        DeviceNode keyboard = root.Children[0];
        Assert.Equal(
            ["T = jgraph.usb.hidlist(VendorID=\"0B05\", ProductID=\"19B6\")", "desc = jgraph.usb.descriptors(\"USB\\VID_0B05&PID_19B6\\K\")", "USB\\VID_0B05&PID_19B6\\K"],
            keyboard.Actions.Select(static a => a.Text));
        Assert.Equal("List its 3 HID collections", keyboard.Actions[0].Title);
        Assert.Equal(DeviceActionKind.Copy, keyboard.Actions[^1].Kind);

        // A device with a COM port offers the port's two actions under the port's name, and says the port.
        DeviceNode serial = root.Children[1];
        Assert.Equal(["Open COM3 as serialport", "Open COM3 in Serial Explorer", "Show descriptors", "Copy instance ID"], serial.Actions.Select(static a => a.Title));
        Assert.EndsWith(" · full (12 Mbps) · COM3", serial.Detail, StringComparison.Ordinal);
        Assert.Contains("usbccgp", serial.Detail, StringComparison.Ordinal);
        Assert.Equal(serial.Detail + "\nSerial number: S1\nLocation: 1-2\nUSB\\VID_0483&PID_5740\\S1", serial.ToolTip);

        // Two devices with the same IDs are told apart by their serial numbers; one alone is not asked for its.
        DeviceNode first = root.Children[2];
        DeviceNode second = root.Children[3];
        Assert.Equal("h = jgraph.usb.hid(VendorID=\"1209\", ProductID=\"0001\", SerialNumber=\"A\")", first.Actions[0].Text);
        Assert.Equal(["Open as HID", "Show descriptors", "Copy instance ID"], first.Actions.Select(static a => a.Title));
        Assert.Equal(
            ["Open as HID", "Open as USB device (WinUSB)", "Show descriptors", "Copy instance ID"],
            second.Actions.Select(static a => a.Title));
        Assert.Equal("d = jgraph.usb.device(\"USB\\VID_1209&PID_0001\\B\")", second.Actions[1].Text);

        // A hub offers its instance ID and nothing that opens it.
        Assert.Equal(["Copy instance ID"], root.Actions.Select(static a => a.Title));
    }

    [Fact]
    public void AnEmptyMachineHasBothGroupsAndSaysNone()
    {
        var empty = new DeviceSnapshot([], new Dictionary<string, string>(), [], new Dictionary<string, List<string>>(),
            new Dictionary<string, int>(), new HashSet<string>());
        IReadOnlyList<DeviceNode> groups = DevicePaneModel.Build(empty);
        Assert.Equal([("Serial ports", "none", 0), ("USB", "none", 0)], groups.Select(static g => (g.Title, g.Detail, g.Children.Count)));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void TheMachinesDevicesAreReadWithoutOpeningAny()
    {
        // Enumeration only, as jgraph.usb.devices does it: no port, HID collection or WinUSB interface is opened.
        DeviceSnapshot snapshot = DevicePaneModel.Read();
        Assert.Equal(SerialPortList.All(), snapshot.SerialPorts);
        Assert.All(snapshot.PortDescriptions, static p => Assert.DoesNotContain("(COM", p.Value, StringComparison.OrdinalIgnoreCase));
        Assert.All(snapshot.WinUsb, id => Assert.Contains(snapshot.Usb, d => d.InstanceId.Equals(id, StringComparison.OrdinalIgnoreCase) && !d.IsHub));

        IReadOnlyList<DeviceNode> groups = DevicePaneModel.Build(snapshot);
        Assert.Equal(snapshot.SerialPorts, groups[0].Children.Select(static c => c.Title));
        Assert.Equal(snapshot.Usb.Count, Count(groups[1]) - 1);

        // Every code line names something by text a MATLAB string holds: quoted, on one line.
        foreach (DeviceAction action in Flatten(groups).SelectMany(static n => n.Actions).Where(static a => a.Kind == DeviceActionKind.Code))
        {
            Assert.Matches(@"^[A-Za-z]+ = [a-z.]+\(.*\)$", action.Text);
            Assert.DoesNotContain('\n', action.Text);
        }

        static int Count(DeviceNode node) => 1 + node.Children.Sum(Count);
        static IEnumerable<DeviceNode> Flatten(IEnumerable<DeviceNode> nodes) => nodes.SelectMany(static n => Flatten(n.Children).Prepend(n));
    }

    // --- the Serial Explorer's connection ------------------------------------------------------------

    [Fact]
    public void TheTerminalSendsLinesAndHandsOnWhatComesBack()
    {
        using var line = new SimulatedLine("COM20");
        using var terminal = new SerialTerminal((port, settings, dtr, rts) =>
            port == "COM20" ? line.Open(settings, dtr, rts) : throw new DeviceOpenException($"{port} does not exist."));
        var received = new List<byte>();
        using var got = new ManualResetEventSlim();
        terminal.Received += bytes =>
        {
            lock (received)
            {
                received.AddRange(bytes);
                if (received.Count >= 7)
                {
                    got.Set();
                }
            }
        };

        Assert.False(terminal.Connected);
        Assert.Throws<InvalidOperationException>(() => terminal.Send("x", TerminalEnding.None));
        Assert.Equal("COM9 does not exist.", Assert.Throws<DeviceOpenException>(() => terminal.Connect("COM9", new SerialSettings())).Message);

        terminal.Connect(" COM20 ", new SerialSettings { BaudRate = 115200 });
        Assert.True(terminal.Connected);
        Assert.Equal("COM20", terminal.Port);
        Assert.True(line.InUse);

        terminal.Send(PeerEngine.Frame("echo on"));
        terminal.Send("AT+V", TerminalEnding.CRLF);
        terminal.Send([0x00]);
        Assert.True(got.Wait(TimeSpan.FromSeconds(5)));
        byte[] expected = [.. "AT+V\r\n"u8, 0x00];
        lock (received)
        {
            Assert.Equal(expected, received);
        }

        Assert.Equal(7, terminal.BytesReceived);
        Assert.True(terminal.BytesSent > 7); // the command frame is counted with the data

        terminal.Disconnect();
        Assert.False(terminal.Connected);
        Assert.False(line.InUse);
        Assert.Equal(("", 0L), (terminal.Port, terminal.BytesReceived));
    }

    [Fact]
    public void TheTerminalSaysWhenItsPortIsLost()
    {
        using var line = new SimulatedLine("COM20");
        using var terminal = new SerialTerminal((_, settings, dtr, rts) => line.Open(settings, dtr, rts));
        string? why = null;
        using var lost = new ManualResetEventSlim();
        terminal.Lost += message =>
        {
            why = message;
            lost.Set();
        };
        terminal.Connect("COM20", new SerialSettings());
        line.Unplug();
        Assert.True(lost.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(string.IsNullOrWhiteSpace(why));
        Assert.False(terminal.Connected);
        Assert.Throws<InvalidOperationException>(() => terminal.Send("x", TerminalEnding.LF));
    }

    [Fact]
    public void ReceivedBytesBecomeTextALineBreakAtATime()
    {
        var text = new TerminalText();

        // CR, LF and the pair are each one line break, the pair even when it straddles two runs.
        Assert.Equal("a\nb\nc\n", text.Format("a\r\nb\rc\n"u8));
        Assert.Equal("d\n", text.Format("d\r"u8));
        Assert.Equal("e", text.Format("\ne"u8));

        // The colour codes firmware logs with are left out, whole or split; other control characters are dropped.
        Assert.Equal("I (31) boot: ok\n", text.Format("\u001b[0;32mI (31) boot: ok\u001b[0m\r\n"u8));
        Assert.Equal("x", text.Format("x\u001b[1"u8));
        Assert.Equal("y\tz", text.Format(";31my\tz\a\0"u8));

        // A character split across two runs comes out whole: é is C3 A9.
        Assert.Equal("caf", text.Format([0x63, 0x61, 0x66, 0xC3]));
        Assert.Equal("é", text.Format([0xA9]));
    }

    [Fact]
    public void ReceivedBytesBecomeHexSixteenToALine()
    {
        var text = new TerminalText { Hex = true };
        Assert.Equal("00 01 FF ", text.Format([0x00, 0x01, 0xFF]));
        string rest = text.Format(Enumerable.Range(3, 14).Select(static i => (byte)i).ToArray());
        Assert.Equal("03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F\n10 ", rest);
        text.Reset();
        Assert.Equal("41 ", text.Format("A"u8));
    }

    [Fact]
    public void HexToSendIsReadTolerantlyAndRefusedByName()
    {
        Assert.Equal([0x48, 0x65, 0x6C], SerialTerminal.ParseHex("48 65 6C"));
        Assert.Equal([0x48, 0x65, 0x6C, 0x0A], SerialTerminal.ParseHex("0x48,0x65; 6c0A"));
        Assert.Empty(SerialTerminal.ParseHex("  "));
        Assert.Equal("'4' is not whole bytes: hex bytes are two digits each, as in 48 65 6C.", Assert.Throws<FormatException>(() => SerialTerminal.ParseHex("48 4")).Message);
        Assert.Equal("'zz' is not hex: the digits are 0-9 and A-F.", Assert.Throws<FormatException>(() => SerialTerminal.ParseHex("zz")).Message);
    }

    [Fact]
    public void TheTerminalsCodeIsTheSerialportLineOfItsSettings()
    {
        Assert.Equal("s = serialport(\"COM3\", 115200);", SerialTerminal.CodeFor("COM3", new SerialSettings { BaudRate = 115200 }, TerminalEnding.None));
        Assert.Equal(
            "s = serialport(\"COM3\", 9600, DataBits=7, Parity=\"even\", StopBits=2, FlowControl=\"hardware\"); configureTerminator(s, \"CR/LF\");",
            SerialTerminal.CodeFor("COM3", new SerialSettings
            {
                DataBits = 7, Parity = SerialParity.Even, StopBits = SerialStopBits.Two, FlowControl = SerialFlowControl.Hardware,
            }, TerminalEnding.CRLF));
        Assert.EndsWith("StopBits=1.5); configureTerminator(s, \"CR\");",
            SerialTerminal.CodeFor("COM3", new SerialSettings { StopBits = SerialStopBits.OnePointFive }, TerminalEnding.CR), StringComparison.Ordinal);
        Assert.Equal(DevicePaneModel.SerialportLine("COM3", 115200) + ";", SerialTerminal.CodeFor("COM3", new SerialSettings { BaudRate = 115200 }, TerminalEnding.None));
    }

    // --- completion ----------------------------------------------------------------------------------

    private sealed class Ports(params string[] names) : IScriptCompletionSource
    {
        public IReadOnlyList<CompletionItem> Members(string qualifier) => [];

        public IReadOnlyList<string> Libraries() => [];

        public IReadOnlyList<CompletionItem> LibraryFunctions(string library) => [];

        public IReadOnlyList<CompletionItem> DeviceNames(string function) =>
            function == "serialport" ? [.. names.Select(static n => new CompletionItem(n, CompletionItemKind.Device, Description: "a port"))] : [];
    }

    private static JgsCompletionResult Complete(string code, IScriptCompletionSource? live = null) =>
        JgsCompletionEngine.GetCompletions(code, code.Length, matlab: true, live: live);

    [Fact]
    public void ADotAfterJgraphOffersItsPackagesAndTheirFunctions()
    {
        JgsCompletionResult root = Complete("jgraph.");
        Assert.Equal(7, root.ReplaceStart);
        Assert.Equal([("net", CompletionItemKind.Namespace), ("pcsc", CompletionItemKind.Namespace), ("usb", CompletionItemKind.Namespace)],
            root.Items.Select(static i => (i.Text, i.Kind)));

        JgsCompletionResult print = Complete("job = jgraph.usb.pr");
        Assert.Equal(17, print.ReplaceStart);
        Assert.Equal(["printers", "printraw"], print.Items.Select(static i => i.Text));
        CompletionItem raw = print.Items[1];
        Assert.Equal((CompletionItemKind.Builtin, "jgraph.usb.printraw(printer, data, Name?, Value?)"), (raw.Kind, raw.Signature));
        Assert.Equal(["printer", "data", "Name?", "Value?"], JgsCompletionEngine.ParameterLabels(raw.Signature!));

        Assert.Equal(["connect", "ctlcode", "readers", "watch"], Complete("jgraph.pcsc.").Items.Select(static i => i.Text));
        Assert.Equal("compile", Assert.Single(Complete("jgraph.net.").Items).Text);

        // The test-only package is offered nowhere, and a name that is not a package offers nothing of these.
        Assert.Empty(Complete("jgraph.internal.").Items);
        Assert.Empty(Complete("jgraph.usb.hid.").Items);
        Assert.Null(DeviceCompletion.PackageMembers("jgraph.internal"));

        // The root itself completes to its name, for the dot that follows, not to a call.
        CompletionItem jgraph = Assert.Single(Complete("jgr").Items);
        Assert.Equal(("jgraph", CompletionItemKind.Namespace, (string?)null), (jgraph.Text, jgraph.Kind, jgraph.Signature));
    }

    [Fact]
    public void APackageFunctionHasSignatureHelpByItsWholeName()
    {
        const string code = "job = jgraph.usb.printraw(p, ";
        JgsSignatureHelp? help = JgsCompletionEngine.GetSignatureHelp(code, code.Length, matlab: true);
        Assert.NotNull(help);
        Assert.Equal(("jgraph.usb.printraw", 1), (help!.Name, help.ActiveParameter));
        Assert.Equal(["printer", "data", "Name?", "Value?"], help.ParameterLabels);
        Assert.Contains("DocumentName", help.Summary, StringComparison.Ordinal);

        // A method called with a dot is still found by its last name; an unknown dotted name is no help.
        const string method = "s.plot(1, ";
        Assert.Equal("plot", JgsCompletionEngine.GetSignatureHelp(method, method.Length, matlab: true)?.Name);
        const string unknown = "jgraph.usb.nosuch(";
        Assert.Null(JgsCompletionEngine.GetSignatureHelp(unknown, unknown.Length, matlab: true));
    }

    [Fact]
    public void TheSerialportStringOffersThePorts()
    {
        var ports = new Ports("COM3", "COM20", "COM21");
        JgsCompletionResult all = Complete("s = serialport(\"", ports);
        Assert.Equal(16, all.ReplaceStart);
        Assert.Equal([("COM3", CompletionItemKind.Device), ("COM20", CompletionItemKind.Device), ("COM21", CompletionItemKind.Device)],
            all.Items.Select(static i => (i.Text, i.Kind)));

        // What is typed filters, in either quote and any case; the replaced span is what was typed.
        JgsCompletionResult some = Complete("s = serialport( 'com2", ports);
        Assert.Equal(17, some.ReplaceStart);
        Assert.Equal(["COM20", "COM21"], some.Items.Select(static i => i.Text));

        // Only the first argument of serialport itself: not the second, not another function's string.
        Assert.Empty(Complete("s = serialport(\"COM3\", \"", ports).Items);
        Assert.Empty(Complete("s = myserialport(\"", ports).Items);
        Assert.Empty(Complete("s = obj.serialport(\"", ports).Items);
        Assert.Empty(Complete("disp(\"", ports).Items);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WithNoSessionTheMachinesPortsAreOffered()
    {
        Assert.Equal(SerialPortList.All(), Complete("serialport(\"").Items.Select(static i => i.Text));
        Assert.Equal(SerialPortList.All(), DeviceCompletion.MachineNames("serialport").Select(static i => i.Text));
        Assert.Empty(DeviceCompletion.MachineNames("tcpclient"));
    }

    // --- the session's side ---------------------------------------------------------------------------

    private sealed class Windows : IScriptDeviceWindows
    {
        public int Shown { get; private set; }

        public void ShowSerialExplorer() => Shown++;
    }

    private static IScriptSession Session(RecordingScriptOutput output, IScriptDeviceWindows? windows = null) =>
        new JGraph.Scripting.Jgs.MatlabScriptEngine().CreateSession(
            new ScriptContext(output, static (_, _) => { }, null, null) { DeviceWindows = windows });

    [Fact]
    public async Task TheCompletionTableNamesExactlyThePackagesFunctions()
    {
        // The table of signatures is written by hand; this is what keeps it from drifting from the
        // functions the session registers.
        var output = new RecordingScriptOutput();
        IScriptSession session = Session(output);
        try
        {
            ScriptRunResult listed = await session.ExecuteAsync(
                "disp(\"net:\" + strjoin(sort(string(fieldnames(jgraph.net)))', \",\")); "
                + "disp(\"pcsc:\" + strjoin(sort(string(fieldnames(jgraph.pcsc)))', \",\")); "
                + "disp(\"usb:\" + strjoin(sort(string(fieldnames(jgraph.usb)))', \",\"));", "", default);
            Assert.True(listed.Success, listed.Message);
            string[] registered =
            [
                .. output.NormalLines.SelectMany(static l => l.Split(':')[1].Split(',').Select(n => $"jgraph.{l.Split(':')[0]}.{n}"))
                    .Order(StringComparer.Ordinal),
            ];
            Assert.Equal(registered, DeviceCompletion.PackageFunctions);
            Assert.All(DeviceCompletion.PackageFunctions, static name => Assert.NotNull(DeviceCompletion.Find(name)));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task ASessionOffersItsSimulatedPortsWithTheMachines()
    {
        IScriptSession session = Session(new RecordingScriptOutput());
        try
        {
            var live = Assert.IsAssignableFrom<IScriptCompletionSource>(session);
            string[] before = [.. live.DeviceNames("serialport").Select(static i => i.Text)];
            ScriptRunResult simulated = await session.ExecuteAsync("jgraph.internal.devicesim(\"COM987\");", "", default);
            Assert.True(simulated.Success, simulated.Message);

            IReadOnlyList<CompletionItem> after = live.DeviceNames("serialport");
            Assert.Contains(after, static i => i.Text == "COM987" && i.Description == "simulated serial port");
            Assert.Contains(after, static i => i.Text == "COM988"); // the port the simulated peer holds, as serialportlist lists it
            Assert.All(before, name => Assert.Contains(after, i => i.Text == name));
            Assert.Equal(after.Select(static i => i.Text).Order(NaturalOrder.Instance), after.Select(static i => i.Text));
            Assert.Contains(Complete("serialport(\"COM98", live).Items, static i => i.Text == "COM987");
            Assert.Empty(live.DeviceNames("tcpclient"));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task SerialExplorerShowsTheHostsPaneAndTakesAndAnswersNothing()
    {
        var windows = new Windows();
        IScriptSession session = Session(new RecordingScriptOutput(), windows);
        try
        {
            ScriptRunResult shown = await session.ExecuteAsync("serialExplorer", "", default);
            Assert.True(shown.Success, shown.Message);
            Assert.Equal(1, windows.Shown);

            ScriptRunResult argument = await session.ExecuteAsync("serialExplorer(\"COM3\")", "", default);
            Assert.False(argument.Success);
            Assert.Contains("Too many input arguments.", argument.Message, StringComparison.Ordinal);

            ScriptRunResult output = await session.ExecuteAsync("x = serialExplorer;", "", default);
            Assert.False(output.Success);
            Assert.Contains("Too many output arguments.", output.Message, StringComparison.Ordinal);
            Assert.Equal(1, windows.Shown);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task SerialExplorerIsRefusedWhereThereIsNoWindow()
    {
        var output = new RecordingScriptOutput();
        IScriptSession session = Session(output);
        try
        {
            ScriptRunResult refused = await session.ExecuteAsync(
                "try, serialExplorer; catch e, disp(e.identifier); disp(e.message); end", "", default);
            Assert.True(refused.Success, refused.Message);
            Assert.Equal("JGraph:serialExplorer:NoWindow", output.NormalLines[0]);
            Assert.StartsWith("serialExplorer opens the Serial Explorer pane of the JGraph app, and this session has no window.", output.NormalLines[1], StringComparison.Ordinal);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task TheRowAFindAnswersHasARowsShape()
    {
        // R2025b's answers, from tools/matlab-checklist/device-probes/probe_dev_arrays: the row is 1-by-2.
        var output = new RecordingScriptOutput();
        IScriptSession session = Session(output);
        try
        {
            ScriptRunResult shaped = await session.ExecuteAsync(
                "jgraph.internal.devicesim(\"COM981\"); jgraph.internal.devicesim(\"COM985\"); "
                + "a = serialport(\"COM981\", 9600); b = serialport(\"COM985\", 9600); f = serialportfind; "
                + "fprintf('%d %s\\n', numel(f), mat2str(size(f))); "
                + "fprintf('%d %d %d %d %d %d %d %d\\n', isempty(f), isscalar(f), isrow(f), isvector(f), iscolumn(f), ismatrix(f), ndims(f), length(f)); "
                + "fprintf('%d %d\\n', size(f, 1), size(f, 2)); [r, c] = size(f); fprintf('%d %d\\n', r, c); "
                + "fprintf('%s %s %s\\n', class(f), f(1).Port, f(2).Port); "
                + "one = serialportfind(Port=\"COM985\"); fprintf('%d %s\\n', numel(one), mat2str(size(one))); "
                + "fprintf('%d\\n', numel(serialportfind)); clear a b f one r c", "", default);
            Assert.True(shaped.Success, shaped.Message);
            Assert.Equal(
                ["2 [1 2]", "0 0 1 1 0 1 2 2", "1 2", "1 2", "internal.Serialport COM981 COM985", "1 [1 1]", "2"],
                output.NormalLines);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task TheWorkspacePaneSummarisesDeviceObjectsFromWhatTheyHold()
    {
        // Each summary is read from the object's own fields; none asks a device anything (ADR 0183's rule).
        IScriptSession session = Session(new RecordingScriptOutput());
        try
        {
            ScriptRunResult made = await session.ExecuteAsync(
                "jgraph.internal.audiosim('on'); jgraph.internal.devicesim(\"COM987\"); "
                + "p = audioplayer(zeros(100, 1), 8000); r = audiorecorder(44100, 16, 2); s = serialport(\"COM987\", 57600);", "", default);
            Assert.True(made.Success, made.Message);
            string Brief(string name) => made.Variables.Single(v => v.Name == name).DisplayValue;
            Assert.Equal("1×1 audioplayer: 100 samples, 8000 Hz, stopped", Brief("p"));
            Assert.Equal("1×1 audiorecorder: 44100 Hz, 16 bits, stereo, stopped", Brief("r"));
            Assert.Equal("1×1 internal.Serialport: COM987, 57600 baud", Brief("s"));

            ScriptRunResult deleted = await session.ExecuteAsync("delete(s); delete(p);", "", default);
            Assert.True(deleted.Success, deleted.Message);
            Assert.Equal("1×1 internal.Serialport: deleted", deleted.Variables.Single(static v => v.Name == "s").DisplayValue);
            Assert.Equal("1×1 audioplayer: deleted", deleted.Variables.Single(static v => v.Name == "p").DisplayValue);
            await session.ExecuteAsync("clear all; jgraph.internal.audiosim('off');", "", default);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
