using JGraph.Devices.Printing;

namespace JGraph.Devices.Simulation;

/// <summary>A job a simulated printer took.</summary>
public sealed record SimulatedPrintJob(int Id, string Printer, string DocumentName, byte[] Data, string? OutputFile);

/// <summary>
/// Printer queues for the tests and <c>jgraph.internal.printsim</c> (device classes plan, stage D12):
/// "JGraph Test Printer", a USB receipt printer on USB001 that answers an IEEE 1284 device ID and is
/// the default, and "JGraph Document Writer", a queue with no USB device behind it. A job is kept as
/// it was sent, and written to its output file when it names one. A queue made offline refuses jobs.
/// </summary>
public sealed class SimulatedPrinters : IPrinterBackend
{
    public const string UsbPrinter = "JGraph Test Printer";
    public const string OtherPrinter = "JGraph Document Writer";
    public const string UsbInstanceId = @"SIM\VID_1209&PID_7001\SIM-PRN";
    public const string DeviceId = "MFG:JGraph;MDL:Test Printer;CMD:ESC/POS;CLS:PRINTER;";

    private readonly object _gate = new();
    private readonly List<SimulatedPrintJob> _jobs = [];
    private readonly HashSet<string> _offline = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every job taken, oldest first.</summary>
    public IReadOnlyList<SimulatedPrintJob> Jobs
    {
        get
        {
            lock (_gate)
            {
                return _jobs.ToArray();
            }
        }
    }

    /// <summary>Takes a queue offline, or brings it back.</summary>
    public void SetOffline(string printer, bool offline)
    {
        lock (_gate)
        {
            _ = offline ? _offline.Add(printer) : _offline.Remove(printer);
        }
    }

    public IReadOnlyList<PrinterInfo> Printers()
    {
        lock (_gate)
        {
            return
            [
                new PrinterInfo(OtherPrinter, "PORTPROMPT:", "JGraph Document Writer v4", false, _offline.Contains(OtherPrinter) ? "offline" : "ready", 0, "", ""),
                new PrinterInfo(UsbPrinter, "USB001", "Generic / Text Only", true, _offline.Contains(UsbPrinter) ? "offline" : "ready", 0, UsbInstanceId, DeviceId),
            ];
        }
    }

    public int PrintRaw(string printer, ReadOnlySpan<byte> bytes, string documentName, string? outputFile)
    {
        lock (_gate)
        {
            PrinterInfo queue = Printers().FirstOrDefault(p => p.Name.Equals(printer, StringComparison.OrdinalIgnoreCase))
                ?? throw new DeviceOpenException($"The printer '{printer}' could not be opened: The printer name is invalid.", 1801);
            if (_offline.Contains(queue.Name))
            {
                throw new DeviceIOException($"The printer '{queue.Name}' refused the job: The printer is offline.", 1906);
            }

            var job = new SimulatedPrintJob(_jobs.Count + 1, queue.Name, documentName, bytes.ToArray(), outputFile);
            if (outputFile is not null)
            {
                try
                {
                    File.WriteAllBytes(outputFile, job.Data);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    throw new DeviceIOException($"The printer '{queue.Name}' refused the job: {e.Message}");
                }
            }

            _jobs.Add(job);
            return job.Id;
        }
    }
}
