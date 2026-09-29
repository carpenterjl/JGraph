using System.Globalization;
using System.Net;
using System.Net.Sockets;
using JGraph.Devices;
using JGraph.Devices.Network;

namespace JGraph.Scripting.Jgs.Devices;

/// <summary>
/// <c>echotcpip</c>, <c>echoudp</c> and <c>resolvehost</c> (device classes plan, stage D2), after
/// R2025b's <c>echotcpip.m</c>, <c>echoudp.m</c> and <c>resolvehost.m</c>, with the p-coded echo
/// server's answers measured by probe_net_echo and probe_net_echo2: a fractional port opens at its
/// integer part, a port another socket holds is taken anyway, a second "on" names the running port.
/// </summary>
internal static class NetworkUtilities
{
    /// <summary><c>echotcpip(state, port)</c> or <c>echoudp(state, port)</c>.</summary>
    public static JgsValue Echo(DeviceSession session, bool tcp, IReadOnlyList<JgsValue> args, int line, int col)
    {
        string function = tcp ? "echotcpip" : "echoudp";
        string prefix = $"instrument:{function}:";
        string syntax = $"Incorrect input combination. Valid syntaxes are {function}(\"on\",port) and {function}(\"off\").";
        JgsRuntimeException Refuse(string key, string message) => new(line, col, prefix + key, message);

        if (args.Count == 0)
        {
            throw Refuse("invalidSyntaxState", syntax);
        }

        if (args.Count > 2)
        {
            throw Refuse("invalidSyntaxArgv", syntax);
        }

        JgsValue stateArg = TransportClient.Str2Char(args[0]);
        string state = DeviceChecks.IsText(stateArg) && DeviceChecks.Match(DeviceChecks.Text(stateArg), ["off", "on"], out string? found, out _)
            ? found!
            : throw Refuse(tcp ? "invalidSyntaxStateBool" : "invalidSyntaxBool", "State must be either \"on\" or \"off\".");
        if (args.Count == 1)
        {
            if (state == "on")
            {
                throw Refuse("invalidSyntaxPort", "Not enough input arguments. Specify port number.");
            }

            if (tcp)
            {
                session.EchoTcp?.Dispose();
                session.EchoTcp = null;
            }
            else
            {
                session.EchoUdp?.Dispose();
                session.EchoUdp = null;
            }

            return JgsValue.Null;
        }

        if (state == "off")
        {
            throw Refuse(tcp ? "invalidSyntaxOff" : "invalidSyntax", syntax);
        }

        JgsValue portArg = args[1];
        string portClass = DeviceChecks.ClassOf(portArg);
        double port = DeviceChecks.Count(portArg) == 1 && DeviceChecks.NumericClasses.Contains(portClass) ? DeviceChecks.Numbers(portArg).First() : double.NaN;
        if (!(port >= 1 && port <= 65535))
        {
            throw Refuse(tcp ? "invalidSyntax" : "invalidSyntaxPortRange", "Port number must be an integer between 1 and 65535, inclusive.");
        }

        if (portClass != "double")
        {
            throw Refuse("createError", "Port number must be a positive double that is less than 2^16.");
        }

        if ((tcp ? session.EchoTcp : session.EchoUdp) is { } running)
        {
            throw Refuse("running", tcp
                ? $"TCP/IP echo server at port {running.Port} is already on."
                : $"UDP echo server at port {running.Port} is already on.");
        }

        try
        {
            if (tcp)
            {
                session.EchoTcp = EchoServer.Tcp((int)port);
            }
            else
            {
                session.EchoUdp = EchoServer.Udp((int)port);
            }
        }
        catch (DeviceOpenException e)
        {
            throw Refuse("createError", e.Message);
        }

        return JgsValue.Null;
    }

    /// <summary>
    /// <c>name = resolvehost(host)</c>, <c>[name, address] = resolvehost(host)</c>,
    /// <c>resolvehost(host, "name"|"address")</c>: '' for a host that does not resolve.
    /// </summary>
    public static JgsValue[] ResolveHost(JGraphScriptGlobals host, IReadOnlyList<JgsValue> args, int wanted, int line, int col)
    {
        JgsRuntimeException Refuse(string key, string message) => new(line, col, "instrument:resolvehost:" + key, message);
        if (wanted > 2)
        {
            throw Refuse("invalidSyntaxRetv", "Too many output arguments.");
        }

        if (args.Count == 0)
        {
            throw Refuse("invalidSyntaxHost", "HOST must be specified.");
        }

        if (args.Count > 2)
        {
            throw Refuse("invalidSyntaxArgv", "Too many input arguments.");
        }

        JgsValue[] Answer(string first, string second) => [JgsValue.Str(first), JgsValue.Str(second)];
        JgsValue hostArg = TransportClient.Str2Char(args[0]);
        if (!IsCharRow(hostArg))
        {
            throw Refuse("invalidSyntaxHostString", "Invalid HOST. HOST must be a character vector or string.");
        }

        string name = DeviceChecks.Text(hostArg).ToLowerInvariant();
        if (name.Length == 0)
        {
            return Answer("", "");
        }

        string returnType = "both";
        if (args.Count > 1)
        {
            JgsValue flag = TransportClient.Str2Char(args[1]);
            if (!IsCharRow(flag))
            {
                throw Refuse("invalidSyntaxFlagString", "Invalid FLAG. FLAG must be a character vector or string.");
            }

            returnType = DeviceChecks.Match(DeviceChecks.Text(flag), ["name", "address"], out string? chosen, out _)
                ? chosen!
                : throw Refuse("invalidSyntaxFlag", "Invalid FLAG. FLAG must be either 'name' or 'address'.");
        }

        if (!LooksResolvable(host, name))
        {
            return Answer("", "");
        }

        (string hostName, string address) = Resolve(name);
        if (address.Length == 0)
        {
            return Answer("", "");
        }

        if (hostName.Length == 0)
        {
            return returnType == "both" ? Answer(address, address) : Answer(address, "");
        }

        return returnType switch
        {
            "address" => Answer(address, ""),
            "name" => Answer(hostName, ""),
            _ => Answer(hostName, address),
        };
    }

    private static bool IsCharRow(JgsValue value) => DeviceChecks.ClassOf(value) == "char" && !value.IsCharMatrix;

    /// <summary>
    /// resolvehost.m's localVerifyIPAddress: a name with any part that is not a number is looked up;
    /// four numbers are an address, and one out of 0..255 warns and answers ''; anything else answers ''.
    /// </summary>
    private static bool LooksResolvable(JGraphScriptGlobals host, string text)
    {
        if (text.Trim().Length == 0)
        {
            return false;
        }

        string[] parts = text.Split('.');
        var numbers = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return true;
            }
        }

        if (numbers.Length != 4)
        {
            return false;
        }

        if (numbers.Any(static n => n < 0 || n > 255))
        {
            JgsBuiltins.Warn(host, "instrument:resolvehost:invalidIPaddress",
                "Invalid IP address. A valid IP address has the format x.x.x.x where x ranges between 0 and 255.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The resolver's host name and IPv4 address: an address is looked up in reverse (127.0.0.1 names
    /// this machine, as in R2025b), a name keeps its own spelling and gets its first address.
    /// </summary>
    private static (string HostName, string Address) Resolve(string text)
    {
        if (IPAddress.TryParse(text, out IPAddress? ip))
        {
            try
            {
                return (Dns.GetHostEntry(ip).HostName, ip.ToString());
            }
            catch (SocketException)
            {
                return ("", ip.ToString());
            }
        }

        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(text);
            IPAddress? first = addresses.FirstOrDefault(static a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
            return first is null ? ("", "") : (text, first.ToString());
        }
        catch (Exception e) when (e is SocketException or ArgumentException)
        {
            return ("", "");
        }
    }
}
