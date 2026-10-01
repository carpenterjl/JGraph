using System.Runtime.Versioning;
using System.Text;
using JGraph.Devices;
using JGraph.Devices.Mtp;
using JGraph.Devices.Printing;
using JGraph.Devices.Simulation;
using JGraph.Devices.SmartCard;
using JGraph.Devices.Usb;
using JGraph.Scripting.Jgs.Devices;
using Xunit;

namespace JGraph.Tests.Devices;

/// <summary>
/// The long tail (device classes plan, stage D12, ADR 0196): smart cards, printer queues, network
/// adapters and portable devices. The machine's own are only listed, which opens nothing: no card is
/// spoken to, no job is printed and no portable device is opened. Everything else runs on the
/// simulators.
/// </summary>
public class LongTailTests
{
    // --- smart cards ----------------------------------------------------------------------------------------

    [Fact]
    [SupportedOSPlatform("windows")]
    public void TheMachinesReadersAreListedWithoutConnecting()
    {
        // A machine with no reader has the service stopped, which is an empty list and not a failure.
        IReadOnlyList<SmartCardReader> readers = WinSmartCards.Instance.Readers();
        Assert.All(readers, static r => Assert.False(string.IsNullOrWhiteSpace(r.Reader)));
        Assert.Equal(readers.Count, readers.Select(static r => r.Reader).Distinct().Count());
        Assert.All(readers, static r => Assert.Equal(r.Present, r.Atr.Length > 0 || r.State is "mute" or "unpowered"));
    }

    [Fact]
    public void PcscWordsAreTheHeadersValues()
    {
        Assert.Equal(0x00313520u, SmartCardWords.ControlCode(3400));
        Assert.Equal(0x003136B0u, SmartCardWords.ControlCode(3500));
        Assert.Equal("the card was removed (SCARD_W_REMOVED_CARD)", SmartCardWords.Describe(unchecked((int)0x80100069)));
        Assert.Equal("SCARD error 0x80100099", SmartCardWords.Describe(unchecked((int)0x80100099)));
        Assert.EndsWith("(error 6)", SmartCardWords.Describe(6));
    }

    [Fact]
    public void TheWatchTurnsTwoLooksIntoEvents()
    {
        var known = new List<(string Reader, uint State)>();
        var events = new List<string>();
        void Raise(SmartCardEvent e) => events.Add($"{e.Type} {e.Reader} {e.Atr.Length}");

        SmartCardDiff.Readers(known, ["A", "B"], Raise);
        SmartCardDiff.Cards(known, [(0x10, []), (0x22, [0x3B, 0x00])], Raise);
        Assert.Equal(["ReaderAdded A 0", "ReaderAdded B 0", "CardInserted B 2"], events);

        events.Clear();
        SmartCardDiff.Cards(known, [(0x122, [0x3B]), (0x12, [])], Raise);
        Assert.Equal(["CardInserted A 1", "CardRemoved B 0"], events);

        // A reader that leaves takes its card with it; one seen with nothing in it keeps a state other than "never seen".
        events.Clear();
        SmartCardDiff.Readers(known, ["B"], Raise);
        Assert.Equal(["CardRemoved A 0", "ReaderRemoved A 0"], events);
        Assert.Equal([("B", 0x10u)], known);

        // The first look reports nothing: what is already there is not news.
        var fresh = new List<(string Reader, uint State)>();
        SmartCardDiff.Readers(fresh, ["A"], null);
        SmartCardDiff.Cards(fresh, [(0x22, [0x3B])], null);
        Assert.Equal([("A", 0x20u)], fresh);
    }

    [Fact]
    public void TheSimulatedCardAnswersItsApplication()
    {
        var cards = new SimulatedSmartCards();
        Assert.Equal([(SimulatedSmartCards.FirstReader, "present", true), (SimulatedSmartCards.SecondReader, "empty", false)],
            cards.Readers().Select(static r => (r.Reader, r.State, r.Present)));
        Assert.Equal(13, SimulatedSmartCards.CardAtr.Length);
        Assert.Equal(0, SimulatedSmartCards.CardAtr.Skip(1).Aggregate(0, static (x, b) => x ^ b));

        using ISmartCard card = cards.Connect(SimulatedSmartCards.FirstReader, "shared", "any");
        Assert.Equal(("T1", "specific"), (card.Protocol, card.State()));
        Assert.Equal(SimulatedSmartCards.CardAtr, card.Atr());
        Assert.Equal("inuse", cards.Readers()[0].State);

        static string Sw(byte[] response) => $"{response[^2]:X2}{response[^1]:X2}";
        byte[] select = [0x00, 0xA4, 0x04, 0x00, 0x07, .. SimulatedSmartCards.Aid];
        Assert.Equal("6985", Sw(card.Transmit([0x00, 0xCA, 0x00, 0x00, 0x00])));
        byte[] fci = card.Transmit(select);
        Assert.Equal("9000", Sw(fci));
        Assert.Equal([0x6F, 0x09, 0x84, 0x07, .. SimulatedSmartCards.Aid], fci[..^2]);
        Assert.Equal("JGraph", Encoding.ASCII.GetString(card.Transmit([0x00, 0xCA, 0x00, 0x00, 0x00])[..^2]));
        Assert.Equal([1, 2, 3, 0x90, 0x00], card.Transmit([0x80, 0x10, 0x00, 0x00, 0x03, 1, 2, 3]));
        Assert.Equal("6A82", Sw(card.Transmit([0x00, 0xA4, 0x04, 0x00, 0x02, 0xA0, 0x01])));
        Assert.Equal("6D00", Sw(card.Transmit([0x00, 0xB0, 0x00, 0x00])));
        Assert.Equal("6E00", Sw(card.Transmit([0xFF, 0xCA, 0x00, 0x00])));

        // Three wrong PINs block the card, and the right one no longer helps.
        byte[] wrong = [0x00, 0x20, 0x00, 0x00, 0x04, .. "0000"u8];
        byte[] right = [0x00, 0x20, 0x00, 0x00, 0x04, .. "1234"u8];
        Assert.Equal("63C2", Sw(card.Transmit(wrong)));
        Assert.Equal("9000", Sw(card.Transmit(right)));
        Assert.Equal(["63C2", "63C1", "63C0", "6983", "6983"], new[] { wrong, wrong, wrong, wrong, right }.Select(a => Sw(card.Transmit(a))));

        Assert.Equal([0x06, 0x04, 0x00, 0x31, 0x36, 0xB0], card.Control(SmartCardWords.ControlCode(3400), []));
        Assert.Equal([3, 2, 1], card.Control(SmartCardWords.ControlCode(2048), [1, 2, 3]));
        Assert.Equal(unchecked((int)0x80100022), Assert.Throws<SmartCardException>(() => card.Control(1, [])).Code);
    }

    [Fact]
    public void SimulatedConnectionsShareExcludeAndLoseTheCard()
    {
        const int Sharing = unchecked((int)0x8010000B);
        var cards = new SimulatedSmartCards();
        ISmartCard first = cards.Connect(SimulatedSmartCards.FirstReader, "shared", "any");
        ISmartCard second = cards.Connect(SimulatedSmartCards.FirstReader, "shared", "T1");
        Assert.Equal(Sharing, Assert.Throws<SmartCardException>(() => cards.Connect(SimulatedSmartCards.FirstReader, "exclusive", "any")).Code);
        Assert.Equal(unchecked((int)0x8010000F), Assert.Throws<SmartCardException>(() => cards.Connect(SimulatedSmartCards.FirstReader, "shared", "T0")).Code);
        Assert.Equal(unchecked((int)0x8010000C), Assert.Throws<SmartCardException>(() => cards.Connect(SimulatedSmartCards.SecondReader, "shared", "any")).Code);
        Assert.Equal(unchecked((int)0x80100009), Assert.Throws<SmartCardException>(() => cards.Connect("nope", "shared", "any")).Code);

        // A transaction keeps the other connection out; ending one that was not begun is refused.
        first.BeginTransaction();
        Assert.Equal(Sharing, Assert.Throws<SmartCardException>(() => second.Transmit([0, 0xCA, 0, 0])).Code);
        Assert.Equal(unchecked((int)0x80100016), Assert.Throws<SmartCardException>(() => second.EndTransaction("leave")).Code);
        first.EndTransaction("reset");

        // The reset is seen by the other connection until it reconnects.
        Assert.Equal(unchecked((int)0x80100068), Assert.Throws<SmartCardException>(() => second.Transmit([0, 0xCA, 0, 0])).Code);
        second.Reconnect("shared", "any", "leave");
        Assert.Equal([0x69, 0x85], second.Transmit([0, 0xCA, 0, 0]));

        // A direct connection to an empty reader reaches the reader and no card.
        using ISmartCard direct = cards.Connect(SimulatedSmartCards.SecondReader, "direct", "any");
        Assert.Equal(("none", "absent"), (direct.Protocol, direct.State()));
        Assert.Equal([2, 1], direct.Control(SmartCardWords.ControlCode(2048), [1, 2]));
        Assert.Equal(unchecked((int)0x80100010), Assert.Throws<SmartCardException>(() => direct.Transmit([0, 0xCA, 0, 0])).Code);

        // The card moves: both connections lose it, also once it is back where it was.
        var events = new List<string>();
        using (cards.Watch(e => events.Add($"{e.Type} {e.Reader[^1]}")))
        {
            cards.Insert(SimulatedSmartCards.SecondReader);
            cards.Insert(SimulatedSmartCards.FirstReader);
            cards.Unplug(SimulatedSmartCards.FirstReader);
            cards.Plug(SimulatedSmartCards.FirstReader);
        }

        cards.Insert(SimulatedSmartCards.FirstReader);
        Assert.Equal(["CardRemoved 0", "CardInserted 1", "CardRemoved 1", "CardInserted 0", "CardRemoved 0", "ReaderRemoved 0", "ReaderAdded 0"], events);
        Assert.Equal(unchecked((int)0x80100069), Assert.Throws<SmartCardException>(() => first.Transmit([0, 0xCA, 0, 0])).Code);
        first.Reconnect("shared", "any", "leave");
        Assert.Equal([0x69, 0x85], first.Transmit([0, 0xCA, 0, 0]));

        Assert.Equal(3, cards.OpenCount);
        first.Dispose();
        second.Disconnect("leave");
        second.Dispose();
        Assert.Equal(1, cards.OpenCount);
        Assert.Equal(unchecked((int)0x80100003), Assert.Throws<SmartCardException>(() => first.Transmit([0, 0xCA, 0, 0])).Code);
    }

    [Fact]
    public void HexTextAndStatusWordsRead()
    {
        Assert.Equal("00 A4 04 00", PcscShared.HexText([0x00, 0xA4, 0x04, 0x00]));
        Assert.Equal("", PcscShared.HexText([]));
        Assert.Equal([0x00, 0xA4, 0x04, 0x00], PcscShared.ParseHex("00 a4 0400")!);
        Assert.Equal([0x3B, 0x88], PcscShared.ParseHex("0x3B:0x88")!);
        Assert.Empty(PcscShared.ParseHex("  ")!);
        Assert.Null(PcscShared.ParseHex("0A B"));
        Assert.Null(PcscShared.ParseHex("ZZ"));
        Assert.Equal("success", PcscShared.StatusMeaning(0x9000));
        Assert.Equal("verification failed, 2 tries left", PcscShared.StatusMeaning(0x63C2));
        Assert.Equal("16 more bytes are waiting for GET RESPONSE", PcscShared.StatusMeaning(0x6110));
        Assert.Equal("", PcscShared.StatusMeaning(0x6FFF));
    }

    // --- printers ---------------------------------------------------------------------------------------------

    [Fact]
    [SupportedOSPlatform("windows")]
    public void TheMachinesPrintersAreListedWithoutPrinting()
    {
        IReadOnlyList<PrinterInfo> printers = WinPrinters.Instance.Printers();
        Assert.All(printers, static p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.False(string.IsNullOrWhiteSpace(p.Driver));
            Assert.False(string.IsNullOrWhiteSpace(p.Status));
            Assert.True(p.Jobs >= 0);
        });
        Assert.True(printers.Count(static p => p.IsDefault) <= 1);
        Assert.Equal(printers.Count, printers.Select(static p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void APrintersStatusPortAndDeviceIdDecode()
    {
        Assert.Equal("ready", WinPrinters.StatusText(0));
        Assert.Equal("paused", WinPrinters.StatusText(1));
        Assert.Equal("paper out, offline", WinPrinters.StatusText(0x90));
        Assert.Equal("status 0x80000000", WinPrinters.StatusText(0x80000000));

        Assert.Equal("USB001", WinPrinters.PortName("USB", 1));
        Assert.Equal("USB012", WinPrinters.PortName("USB", 12));
        Assert.Equal("", WinPrinters.PortName(null, 1));
        Assert.Equal("", WinPrinters.PortName("USB", "1"));

        // usbprint hands back the printer's answer with or without its two length bytes.
        byte[] text = Encoding.ASCII.GetBytes("MFG:EPSON;CMD:ESC/POS;MDL:TM-T20;CLS:PRINTER;");
        byte[] prefixed = [0, (byte)(text.Length + 2), .. text, 0, 0, 0];
        Assert.Equal("MFG:EPSON;CMD:ESC/POS;MDL:TM-T20;CLS:PRINTER;", WinPrinters.DeviceIdFrom(prefixed, prefixed.Length));
        Assert.Equal("MFG:EPSON;CMD:ESC/POS;MDL:TM-T20;CLS:PRINTER;", WinPrinters.DeviceIdFrom([.. text, 0], text.Length + 1));
        Assert.Equal("", WinPrinters.DeviceIdFrom(new byte[8], -1));

        var printer = new PrinterInfo("p", "USB001", "d", false, "ready", 0, "", "MANUFACTURER:Zebra ; COMMAND SET:ZPL;MODEL:ZD420;");
        Assert.Equal(("Zebra", "ZD420", "ZPL"), (printer.Manufacturer, printer.Model, printer.CommandSet));
        Assert.Equal("", new PrinterInfo("p", "nul:", "d", false, "ready", 0, "", "").Model);
    }

    [Fact]
    public void ASimulatedPrinterKeepsItsJobs()
    {
        var printers = new SimulatedPrinters();
        IReadOnlyList<PrinterInfo> list = printers.Printers();
        Assert.Equal([SimulatedPrinters.OtherPrinter, SimulatedPrinters.UsbPrinter], list.Select(static p => p.Name));
        Assert.Equal(("USB001", true, "JGraph", "Test Printer", "ESC/POS"), (list[1].Port, list[1].IsDefault, list[1].Manufacturer, list[1].Model, list[1].CommandSet));
        Assert.Equal(("", ""), (list[0].UsbInstanceId, list[0].DeviceId));

        Assert.Equal(1, printers.PrintRaw("jgraph test printer", [27, 64, 10], "first", null));
        string file = Path.Combine(Path.GetTempPath(), $"jgraph-printsim-{Guid.NewGuid():N}.bin");
        try
        {
            Assert.Equal(2, printers.PrintRaw(SimulatedPrinters.OtherPrinter, [1, 2, 3, 4], "second", file));
            Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(file));
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Equal([(1, SimulatedPrinters.UsbPrinter, "first", 3), (2, SimulatedPrinters.OtherPrinter, "second", 4)],
            printers.Jobs.Select(static j => (j.Id, j.Printer, j.DocumentName, j.Data.Length)));

        Assert.Throws<DeviceOpenException>(() => printers.PrintRaw("nope", [1], "x", null));
        printers.SetOffline(SimulatedPrinters.UsbPrinter, true);
        Assert.Equal("offline", printers.Printers()[1].Status);
        Assert.Throws<DeviceIOException>(() => printers.PrintRaw(SimulatedPrinters.UsbPrinter, [1], "x", null));
        printers.SetOffline(SimulatedPrinters.UsbPrinter, false);
        Assert.Equal(3, printers.PrintRaw(SimulatedPrinters.UsbPrinter, [], "empty", null));
    }

    // --- network adapters ---------------------------------------------------------------------------------------

    [Fact]
    [SupportedOSPlatform("windows")]
    public void TheMachinesAdaptersAreMatchedToTheirDevnodes()
    {
        IReadOnlyList<NetworkAdapterInfo> all = UsbNetworkAdapters.All();
        Assert.All(all, static a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
            Assert.False(string.IsNullOrWhiteSpace(a.InstanceId));
            Assert.True(a.Speed >= 0);
            Assert.Matches("^[a-z]+$", a.Status);
        });
        Assert.Equal(all.Count, all.Select(static a => a.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // The USB ones are those the USB bus enumerated, each under a USB device.
        IReadOnlyList<NetworkAdapterInfo> usb = UsbNetworkAdapters.List();
        Assert.All(usb, a =>
        {
            Assert.StartsWith(@"USB\", a.InstanceId, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(@"USB\", a.UsbInstanceId, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(a, all);
        });
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void TheKindOfUsbNetworkingIsReadFromTheDescriptors()
    {
        // A configuration with one interface of the given class, subclass and protocol.
        static byte[] Configuration(byte cls, byte sub, byte protocol) => [9, 2, 18, 0, 1, 1, 0, 0x80, 50, 9, 4, 0, 0, 0, cls, sub, protocol, 0];
        Assert.Equal("ECM", UsbNetworkAdapters.Kind(Configuration(0x02, 0x06, 0x00)));
        Assert.Equal("NCM", UsbNetworkAdapters.Kind(Configuration(0x02, 0x0D, 0x00)));
        Assert.Equal("EEM", UsbNetworkAdapters.Kind(Configuration(0x02, 0x0C, 0x07)));
        Assert.Equal("RNDIS", UsbNetworkAdapters.Kind(Configuration(0xE0, 0x01, 0x03)));
        Assert.Equal("RNDIS", UsbNetworkAdapters.Kind(Configuration(0xEF, 0x04, 0x01)));
        Assert.Equal("RNDIS", UsbNetworkAdapters.Kind(Configuration(0x02, 0x02, 0xFF)));
        Assert.Equal("", UsbNetworkAdapters.Kind(Configuration(0x02, 0x02, 0x01)));
        Assert.Equal("", UsbNetworkAdapters.Kind(Configuration(0xE0, 0x01, 0x01)));
        Assert.Equal("", UsbNetworkAdapters.Kind([]));
        Assert.Equal("AA:0B:C0:00:01:FF", UsbNetworkAdapters.MacText([0xAA, 0x0B, 0xC0, 0x00, 0x01, 0xFF]));
        Assert.Equal("", UsbNetworkAdapters.MacText([]));
    }

    // --- portable devices -----------------------------------------------------------------------------------------

    [Fact]
    [SupportedOSPlatform("windows")]
    public void TheMachinesPortableDevicesAreListedWithoutOpeningAny()
    {
        IReadOnlyList<MtpDeviceInfo> devices = WpdDevices.Instance.Devices();
        Assert.All(devices, static d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.False(string.IsNullOrWhiteSpace(d.Id));
        });
        Assert.Equal(devices.Count, devices.Select(static d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(devices.Select(static d => d.Id), WpdDevices.Instance.Devices().Select(static d => d.Id));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void WpdsCodesAreNamed()
    {
        Assert.Equal("phone", WpdDevice.TypeName(3));
        Assert.Equal("camera", WpdDevice.TypeName(1));
        Assert.Equal("unknown", WpdDevice.TypeName(99));
        Assert.Equal("the device is busy", WpdDevices.Describe(unchecked((int)0x800700AA)));
        Assert.Equal("error 0x80004005", WpdDevices.Describe(unchecked((int)0x80004005)));
        Assert.EndsWith("(0x80070003)", WpdDevices.Describe(unchecked((int)0x80070003)));
    }

    [Fact]
    public void APathIsReadThroughStoragesAndFolders()
    {
        Assert.Equal(["a", "b", "c.txt"], MtpPaths.Split(@"/a\b//c.txt"));
        Assert.Empty(MtpPaths.Split("/"));
        Assert.Equal("a/b", MtpPaths.Join(["a", "b"]));

        var mtp = new SimulatedMtp();
        using IMtpDevice phone = mtp.Open(mtp.Devices()[0]);
        Assert.Equal((MtpPaths.Root, ""), MtpPaths.Find(phone, "", out _)!.Value);
        (MtpEntry entry, string path) = MtpPaths.Find(phone, @"internal STORAGE\dcim/camera/img_0002.JPG", out _)!.Value;
        Assert.Equal(("IMG_0002.jpg", false, 4096L, "Internal storage/DCIM/Camera/IMG_0002.jpg"), (entry.Name, entry.IsFolder, entry.Size, path));
        Assert.Null(MtpPaths.Find(phone, "Internal storage/DCIM/Nope/x.jpg", out string missing));
        Assert.Equal("Nope", missing);
        Assert.Null(MtpPaths.Find(phone, "Internal storage/Documents/notes.txt/x", out missing));
        Assert.Equal("x", missing);

        // Two names that differ only in case are told apart exactly, and neither is picked loosely.
        MtpEntry[] twins = [new("1", "Readme", false, false, 0, null), new("2", "README", false, false, 0, null)];
        Assert.Equal("2", MtpPaths.Pick(twins, "README")!.Id);
        Assert.Null(MtpPaths.Pick(twins, "readme"));
    }

    [Fact]
    public void ASimulatedPhoneListsCopiesAndDeletes()
    {
        var mtp = new SimulatedMtp();
        Assert.Equal([SimulatedMtp.PhoneName, SimulatedMtp.CameraName], mtp.Devices().Select(static d => d.Name));
        IMtpDevice phone = mtp.Open(mtp.Devices()[0]);
        Assert.Equal(("Test Phone", "MTP: 1.00", "phone"), (phone.Properties["Model"], phone.Properties["Protocol"], phone.Properties["Type"]));

        IReadOnlyList<MtpEntry> storages = phone.Children(MtpPaths.RootId);
        Assert.Equal([("Internal storage", true, true), ("SD card", true, true)], storages.Select(static s => (s.Name, s.IsFolder, s.IsStorage)));
        Assert.Equal((64_000_000_000L, 64_000_000_000L - 2048 - 4096 - 34), phone.Space(storages[0].Id));

        MtpEntry picture = MtpPaths.Find(phone, "Internal storage/DCIM/Camera/IMG_0001.jpg", out _)!.Value.Entry;
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), picture.Modified);
        var bytes = new MemoryStream();
        phone.Download(picture.Id, bytes);
        Assert.Equal(SimulatedMtp.Picture(1, 2048), bytes.ToArray());
        Assert.Equal([1, 2, 3], SimulatedMtp.Picture(1, 3));

        MtpEntry music = MtpPaths.Find(phone, "Internal storage/Music", out _)!.Value.Entry;
        string song = phone.Upload(music.Id, "song.mp3", new MemoryStream([9, 8, 7, 6, 5]), 5);
        Assert.Equal([("song.mp3", 5L)], phone.Children(music.Id).Select(static c => (c.Name, c.Size)));
        Assert.Throws<MtpException>(() => phone.Upload(music.Id, "SONG.MP3", new MemoryStream([1]), 1));
        Assert.Throws<MtpException>(() => phone.Upload(MtpPaths.RootId, "x", new MemoryStream([1]), 1));
        Assert.Throws<MtpException>(() => phone.Upload(song, "x", new MemoryStream([1]), 1));

        string album = phone.CreateFolder(music.Id, "Album");
        phone.Upload(album, "a.mp3", new MemoryStream([1, 2]), 2);
        Assert.Throws<MtpException>(() => phone.Delete(album, recursive: false));
        Assert.Throws<MtpException>(() => phone.Delete(storages[0].Id, recursive: true));
        phone.Delete(album, recursive: true);
        phone.Delete(song, recursive: false);
        Assert.Empty(phone.Children(music.Id));
        Assert.Throws<MtpException>(() => phone.Delete(song, recursive: false));
        Assert.Throws<MtpException>(phone.Capture);

        // An unplugged device leaves the list and refuses the object still open on it.
        Assert.Equal(1, mtp.OpenCount);
        mtp.Unplug(SimulatedMtp.PhoneName);
        Assert.Equal([SimulatedMtp.CameraName], mtp.Devices().Select(static d => d.Name));
        Assert.Throws<MtpException>(() => phone.Children(MtpPaths.RootId));
        phone.Dispose();
        Assert.Equal(0, mtp.OpenCount);
        mtp.Plug(SimulatedMtp.PhoneName);
        Assert.Equal(2, mtp.Devices().Count);
    }

    [Fact]
    public void ASimulatedCameraTakesAPicture()
    {
        var mtp = new SimulatedMtp();
        using IMtpDevice camera = mtp.Open(mtp.Devices()[1]);
        MtpEntry folder = MtpPaths.Find(camera, "Memory card/DCIM/100JGRPH", out _)!.Value.Entry;
        Assert.Equal(["DSC_0001.JPG"], camera.Children(folder.Id).Select(static c => c.Name));
        camera.Capture();
        camera.Capture();
        Assert.Equal(["DSC_0001.JPG", "DSC_0002.JPG", "DSC_0003.JPG"], camera.Children(folder.Id).Select(static c => c.Name));
        var bytes = new MemoryStream();
        camera.Download(camera.Children(folder.Id)[2].Id, bytes);
        Assert.Equal(SimulatedMtp.Picture(103, 1024), bytes.ToArray());
    }
}
