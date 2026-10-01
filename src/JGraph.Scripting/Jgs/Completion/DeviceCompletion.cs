using System.Text.RegularExpressions;
using JGraph.Devices.Serial;
using JGraph.Devices.Usb;
using JGraph.Scripting.Completion;

namespace JGraph.Scripting.Jgs.Completion;

/// <summary>
/// What the editor offers for devices in a MATLAB buffer (device classes plan, stage 14, ADR 0197):
/// the members of the <c>jgraph</c> packages after a dot, with their call shapes, and the serial
/// ports inside <c>serialport("…</c>.
/// </summary>
/// <remarks>
/// The package table is written by hand, because a signature and a one-line summary are not things
/// the registered functions carry. A test compares its names with the session's <c>jgraph</c> struct,
/// so a function added to a package without a line here fails the build's tests, not a user.
/// <c>jgraph.internal</c> is test-only and is offered nowhere.
/// </remarks>
public static partial class DeviceCompletion
{
    private sealed record Entry(string Name, string? Parameters, string Summary);

    private static readonly Dictionary<string, Entry[]> Packages = new(StringComparer.Ordinal)
    {
        ["jgraph"] =
        [
            new("net", null, "Compiling C# into the session: jgraph.net.compile."),
            new("pcsc", null, "Smart cards through PC/SC: readers, connect, watch, ctlcode."),
            new("usb", null, "USB devices: enumeration, HID, WinUSB, DFU, storage, printers, network adapters and MTP."),
        ],
        ["jgraph.net"] =
        [
            new("compile", "source, Name?, Value?", "Compiles C# files or text into an assembly and makes its types visible. Options: AssemblyName, References, Unloadable, AllowUnsafe, LanguageVersion, Optimize."),
        ],
        ["jgraph.usb"] =
        [
            new("descriptors", "dev", "One device's device, configuration and BOS descriptors, decoded. dev is a row of jgraph.usb.devices or an instance ID."),
            new("device", "dev, Name?, Value?", "Opens a device WinUSB is bound to, for control, bulk and interrupt transfers. dev is a row, an instance ID, or name-value filters; Interface=n picks an interface."),
            new("devices", "Name?, Value?", "The USB devices as a table. VendorID, ProductID, Class, SerialNumber, Driver and Product filter it."),
            new("dfu", "dev?, Name?, Value?", "Opens a device with a DFU interface for firmware download and upload, DFU 1.1 or ST's DfuSe."),
            new("dfufile", "path", "What a firmware file would send: a .dfu file's suffix and targets, an Intel HEX file's segments, or raw bytes."),
            new("dfulist", "Name?, Value?", "The devices with a DFU interface, run-time or DFU mode, as a table."),
            new("dfusuffix", "payload, Name?, Value?", "Appends a DFU suffix (VendorID=, ProductID=, Release=) and its CRC to firmware bytes."),
            new("eject", "x", "Ejects a USB disk: a drive (\"E:\"), a row of jgraph.usb.storage's tables, or an instance ID."),
            new("hid", "Name?, Value?", "Opens one HID collection for its reports, capabilities and usages. VendorID, ProductID, UsagePage, Usage, SerialNumber and Path choose it."),
            new("hidlist", "Name?, Value?", "The HID collections as a table, each with the USB device it belongs to."),
            new("mtp", "name?", "Opens an MTP or PTP device (a phone, a camera) for dir, download, upload, mkdir, deleteObject and capture. deleteObject deletes for good."),
            new("mtplist", "", "The MTP and PTP devices as a table."),
            new("netadapter", "dev?", "The network adapters that are USB devices, with their addresses, link and kind."),
            new("ports", "", "Every hub's ports, connected or not, and what is on each."),
            new("printers", "", "The printer queues as a table; a USB printer's row has its IDs and IEEE 1284 device ID."),
            new("printraw", "printer, data, Name?, Value?", "Sends bytes to a printer as one job the driver does not touch. Options: DocumentName, OutputFile."),
            new("storage", "Name?, Value?", "[disks, volumes] = jgraph.usb.storage: the USB disks and their volumes, as two tables."),
            new("tree", "", "Prints the hub topology; with an output, answers it as nested structs."),
            new("watch", "fcn, Name?, Value?", "Calls fcn(w, evt) when a matching USB device arrives or leaves."),
        ],
        ["jgraph.pcsc"] =
        [
            new("connect", "reader?, Name?, Value?", "Connects to the card in a reader, for transmit, control, status and transactions. Options: Share, Protocol."),
            new("ctlcode", "n", "A reader control code: SCARD_CTL_CODE(n)."),
            new("readers", "", "The smart-card readers as a table: Reader, State, CardPresent, ATR."),
            new("watch", "fcn", "Calls fcn(w, evt) when a card or a reader arrives or leaves."),
        ],
    };

    /// <summary>The dotted names of every package function the table describes: <c>jgraph.usb.hid</c>, …</summary>
    public static IReadOnlyList<string> PackageFunctions { get; } =
    [
        .. Packages.SelectMany(static p => p.Value.Where(static e => e.Parameters is not null).Select(e => p.Key + "." + e.Name))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// What may follow <paramref name="qualifier"/> and a dot when it is <c>jgraph</c> or one of its
    /// packages; null when it is neither, so the caller asks elsewhere.
    /// </summary>
    public static IReadOnlyList<CompletionItem>? PackageMembers(string qualifier)
    {
        ArgumentNullException.ThrowIfNull(qualifier);
        if (!Packages.TryGetValue(qualifier, out Entry[]? entries))
        {
            return null;
        }

        return
        [
            .. entries.Select(e => e.Parameters is null
                ? new CompletionItem(e.Name, CompletionItemKind.Namespace, Description: e.Summary)
                : new CompletionItem(e.Name, CompletionItemKind.Builtin, $"{qualifier}.{e.Name}({e.Parameters})", e.Summary)),
        ];
    }

    /// <summary>The item of a package function by its dotted name, for signature help; null when the table has none.</summary>
    public static CompletionItem? Find(string dotted)
    {
        ArgumentNullException.ThrowIfNull(dotted);
        int dot = dotted.LastIndexOf('.');
        return dot > 0 && PackageMembers(dotted[..dot]) is { } members
            ? members.FirstOrDefault(m => m.Signature is not null && m.Text == dotted[(dot + 1)..])
            : null;
    }

    // --- device names inside a string argument ---------------------------------------------------------

    /// <summary><c>serialport("</c> or <c>serialport('</c> and the name typed so far.</summary>
    [GeneratedRegex(@"(?<![\w.])(serialport)\s*\(\s*['""]([\w.\-]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex NameArgumentPattern();

    /// <summary>
    /// Inside the first string argument of <c>serialport</c>, the serial ports; null anywhere else.
    /// </summary>
    internal static JgsCompletionResult? NameArgument(string code, int offset, IScriptCompletionSource names)
    {
        int lineStart = offset;
        while (lineStart > 0 && code[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        if (NameArgumentPattern().Match(code[lineStart..offset]) is not { Success: true } call)
        {
            return null;
        }

        string typed = call.Groups[2].Value;
        IReadOnlyList<CompletionItem> offered;
        try
        {
            offered = names.DeviceNames(call.Groups[1].Value);
        }
        catch (Exception)
        {
            offered = []; // completion never fails a keystroke
        }

        return new JgsCompletionResult(offset - typed.Length,
            [.. offered.Where(i => i.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase))]);
    }

    /// <summary>
    /// The names the machine has for <paramref name="function"/>'s first argument: for
    /// <c>serialport</c>, the ports <c>serialportlist</c> answers, each with what Windows calls it.
    /// Read from the registry; no port is opened.
    /// </summary>
    public static IReadOnlyList<CompletionItem> MachineNames(string function)
    {
        if (function != "serialport" || !OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            Dictionary<string, string> described = UsbCrossReference.ComPortDescriptions();
            return [.. SerialPortList.All().Select(port => PortItem(port, described.GetValueOrDefault(port, "")))];
        }
        catch (Exception fault) when (fault is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    /// <summary>A serial port as a completion item.</summary>
    internal static CompletionItem PortItem(string port, string description) =>
        new(port, CompletionItemKind.Device, Description: description.Length > 0 ? description : "serial port");
}
