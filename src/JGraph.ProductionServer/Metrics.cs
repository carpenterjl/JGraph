using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace JGraph.ProductionServer;

/// <summary>
/// The server's metrics (compiler and SDK plan, stage K15): <c>GET /api/metrics</c> in Prometheus text
/// as R2025b's muserve writes it (k15_server: metrics.*). The server's own ten
/// (<c>matlabprodserver_up_time_seconds</c> and its kin) come first; then the custom ones a function
/// emitted through <c>prodserver.metrics.incrementCounter</c> and <c>setGauge</c>, which are lines a
/// worker prints, <c>[metric:counter] archive|name value</c>, read off its standard output and standard
/// error (rawstderr): a counter adds, a gauge sets, a type word that is neither is <c>untyped</c>, and an
/// event with the name of a series of the other type is passed over (rawgaugeascounter). The series
/// without an archive come first (rawnoarchive), then each archive's in the order the archives were
/// first seen, its names in ordinal order; a value is written as C's <c>%g</c> writes it.
/// </summary>
internal sealed class MetricsRegistry
{
    private readonly Stopwatch _up = Stopwatch.StartNew();
    private readonly object _lock = new();
    private readonly List<string> _archives = [];
    private readonly Dictionary<string, SortedDictionary<string, Series>> _custom = new(StringComparer.Ordinal);
    private long _accepted;
    private long _succeeded;
    private long _failed;
    private long _canceled;
    private int _inQueue;
    private int _processing;
    private double _queueSeconds;

    private sealed class Series(string type)
    {
        public string Type { get; } = type;

        public double Value { get; set; }
    }

    /// <summary>A request arrived.</summary>
    public void Accepted()
    {
        Interlocked.Increment(ref _accepted);
        Interlocked.Increment(ref _inQueue);
    }

    /// <summary>A request left the queue for a worker, after waiting <paramref name="queued"/>.</summary>
    public void Started(TimeSpan queued)
    {
        Interlocked.Decrement(ref _inQueue);
        Interlocked.Increment(ref _processing);
        Volatile.Write(ref _queueSeconds, queued.TotalSeconds);
    }

    /// <summary>A request that had started finished: a 200 with a result or an error succeeded, anything else failed.</summary>
    public void Finished(bool succeeded)
    {
        Interlocked.Decrement(ref _processing);
        if (succeeded)
        {
            Interlocked.Increment(ref _succeeded);
        }
        else
        {
            Interlocked.Increment(ref _failed);
        }
    }

    /// <summary>A request that never reached a worker was refused.</summary>
    public void Refused()
    {
        Interlocked.Decrement(ref _inQueue);
        Interlocked.Increment(ref _failed);
    }

    /// <summary>A request was cancelled before it finished.</summary>
    public void Canceled(bool wasProcessing)
    {
        if (wasProcessing)
        {
            Interlocked.Decrement(ref _processing);
        }
        else
        {
            Interlocked.Decrement(ref _inQueue);
        }

        Interlocked.Increment(ref _canceled);
    }

    /// <summary>
    /// Reads a line a worker printed; true when it was a metric event. The line is
    /// <c>[metric:TYPE] [ARCHIVE|]NAME VALUE</c>, leading blanks allowed (rawspace), nothing before the
    /// bracket (rawmid), and VALUE a number as MATLAB's <c>%g</c> prints one, <c>Inf</c> and <c>NaN</c> included.
    /// </summary>
    public bool ReadLine(string line)
    {
        string text = line.TrimStart();
        if (!text.StartsWith("[metric:", StringComparison.Ordinal))
        {
            return false;
        }

        int close = text.IndexOf(']', StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        string type = text[8..close];
        string rest = text[(close + 1)..].Trim();
        int space = rest.LastIndexOf(' ');
        if (space <= 0)
        {
            return false;
        }

        string series = rest[..space].TrimEnd();
        string valueText = rest[(space + 1)..];
        if (!TryParseValue(valueText, out double value))
        {
            return false;
        }

        int bar = series.IndexOf('|', StringComparison.Ordinal);
        string archive = bar < 0 ? "" : series[..bar];
        string name = bar < 0 ? series : series[(bar + 1)..];
        if (name.Length == 0 || name.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        string kind = type is "counter" or "gauge" ? type : "untyped";
        lock (_lock)
        {
            if (!_custom.TryGetValue(archive, out SortedDictionary<string, Series>? group))
            {
                _custom[archive] = group = new SortedDictionary<string, Series>(StringComparer.Ordinal);
                _archives.Add(archive);
            }

            if (!group.TryGetValue(name, out Series? found))
            {
                group[name] = found = new Series(kind) { Value = value };
                return true;
            }

            if (found.Type != kind)
            {
                return true;
            }

            found.Value = kind == "counter" ? found.Value + value : value;
        }

        return true;
    }

    private static bool TryParseValue(string text, out double value)
    {
        switch (text.ToLowerInvariant())
        {
            case "inf" or "+inf" or "infinity":
                value = double.PositiveInfinity;
                return true;
            case "-inf" or "-infinity":
                value = double.NegativeInfinity;
                return true;
            case "nan" or "-nan":
                value = double.NaN;
                return true;
            default:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>The Prometheus text.</summary>
    public string Text()
    {
        var sb = new StringBuilder();
        void Line(string name, string type, double value, string? archive = null)
        {
            sb.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
            sb.Append(name);
            if (archive is { Length: > 0 })
            {
                sb.Append("{archive=\"").Append(archive).Append("\"}");
            }

            sb.Append(' ').Append(G(value)).Append('\n');
        }

        using Process self = Process.GetCurrentProcess();
        Line("matlabprodserver_up_time_seconds", "counter", _up.Elapsed.TotalSeconds);
        Line("matlabprodserver_queue_time_seconds", "gauge", Volatile.Read(ref _queueSeconds));
        Line("matlabprodserver_cpu_time_seconds", "counter", self.TotalProcessorTime.TotalSeconds);
        Line("matlabprodserver_memory_working_set_bytes", "gauge", self.WorkingSet64);
        Line("matlabprodserver_requests_accepted_total", "counter", Interlocked.Read(ref _accepted));
        Line("matlabprodserver_requests_in_queue", "gauge", Math.Max(0, Volatile.Read(ref _inQueue)));
        Line("matlabprodserver_requests_processing", "gauge", Math.Max(0, Volatile.Read(ref _processing)));
        Line("matlabprodserver_requests_succeeded_total", "counter", Interlocked.Read(ref _succeeded));
        Line("matlabprodserver_requests_failed_total", "counter", Interlocked.Read(ref _failed));
        Line("matlabprodserver_requests_canceled_total", "counter", Interlocked.Read(ref _canceled));
        lock (_lock)
        {
            foreach (string archive in _archives.OrderBy(static a => a.Length == 0 ? 0 : 1))
            {
                foreach ((string name, Series series) in _custom[archive])
                {
                    Line(name, series.Type, series.Value, archive);
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>A number as C's <c>%g</c> prints it: six significant digits, an exponent of two digits at least, <c>inf</c> and <c>nan</c>.</summary>
    internal static string G(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }

        string text = value.ToString("G6", CultureInfo.InvariantCulture);
        int e = text.IndexOf('E', StringComparison.Ordinal);
        if (e < 0)
        {
            return text;
        }

        string mantissa = text[..e];
        string exponent = text[(e + 1)..];
        char sign = exponent[0] is '+' or '-' ? exponent[0] : '+';
        string digits = exponent.TrimStart('+', '-').PadLeft(2, '0');
        return mantissa + "e" + sign + digits;
    }
}
