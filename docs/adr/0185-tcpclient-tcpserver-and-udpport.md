# ADR 0185 — tcpclient, tcpserver and udpport on the device layer

## Status

Accepted. Stage D2 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`), after ADR 0184 (2481628). One commit,
this ADR's.

The stage adds:
- `tcpclient`, `tcpserver` and `udpport` (byte and datagram modes);
- `tcpclientfind`, `tcpserverfind` and `udpportfind`;
- `echotcpip`, `echoudp` and `resolvehost`;
- `src/JGraph.Devices/Network`: `TcpTransport`, `TcpServerTransport`, `UdpTransport` and
  `EchoServer`.

## Context

ADR 0184 built the device layer under `serialport`: the declared device object, exact lifetime,
the device event queue and the transport client transcribed from R2025b's `GenericClient`.
R2025b's network interfaces stand on the same client, so D2 puts them on the same layer.

`tcpclient` belongs to MATLAB; `tcpserver` and `udpport` to the Instrument Control Toolbox.
`echotcpip.m`, `echoudp.m` and `resolvehost.m` are readable and were transcribed.

What is not readable is the p-coded part: the network transports and the echo server
(`matlabshared.network.internal.EchoServer`). Their behaviour was measured on the loopback.

## Decision

### The transports

- **`TcpTransport`** connects within ConnectTimeout, and a reader thread fills the shared input
  buffer.
- **`TcpServerTransport`** takes one client at a time. While a client is connected the server does
  not listen, so a second client is refused, as R2025b refuses it. When the client goes, it listens
  again on the same endpoint.
- **`UdpTransport`** works in two modes:
  - byte mode appends each datagram's bytes to the input buffer;
  - datagram mode keeps each datagram whole, with its sender.

  A send is split by OutputDatagramSize. Two sockets share a port only when both ask
  (EnablePortSharing), and nothing binds exclusively.
- **`EchoServer`** is one dual-stack socket on every interface. That matches what R2025b's echo
  servers do:
  - one starts on a port an IPv4 socket holds;
  - an IPv4 `udpport` or `tcpserver` may bind its port afterwards;
  - the IPv4 socket then gets the port's IPv4 traffic.

### The shared client

`TransportClient` now takes a `TransportInterface` declaration in place of serialport's constants.
The declaration says:
- the identifier prefix, and which transportlib and GenericClient identifiers are renamed;
- whether reads answer the precision's class (tcpclient) or double (udpport, tcpserver);
- how read and write failures are wrapped;
- whether a write without a precision writes the data's own class;
- the read syntax lines;
- whether event data is the shared `ByteAvailableInfo`/`TerminatorAvailableInfo` (the network
  classes) or serialport's `DataAvailableInfo`;
- the word flush's refusal uses for its second input (`buffer`, or `BUFFER` in udpport's datagram
  mode).

### tcpclient

- **`read(t)` reads what is waiting.** With nothing waiting it answers `[]` double.
- **There are no partial reads.** A timeout is `MATLAB:networklib:tcpclient:readFailed`.
- **Every constructor refusal is `MATLAB:networklib:tcpclient:cannotCreateObject`**, carrying the
  resolver's or the socket's reason.
- **A connect cut short by a finite ConnectTimeout** gives R2025b's `get_option: The file handle
  supplied is not valid`. Windows retries a refused loopback connect for about two seconds, so
  ConnectTimeout 1 on a closed port gives that text in both engines.
- **ConnectTimeout and EnableTransferDelay** are read-only after construction.

### tcpserver

- **Reads answer double.**
- **A write with no client** is `instrument:interface:tcpserver:NotConnectedWrite`.
- **ConnectionChangedFcn** runs with a `tcpserver.internal.ConnectionInfo` event. Its properties
  are Connected, ClientAddress, ClientPort, AbsoluteTime, Source (an `EventHandler`, as in R2025b)
  and EventName.
- **On a disconnect** ClientAddress is `''` and ClientPort `[]`.
- **A bind refusal** is `instrument:interface:tcpserver:cannotConnect`, with the socket's sentence
  and the documentation link.

### udpport

- **Byte mode reads through the shared client, in double.**
- **A write must name a destination** until one has been named (`EmptyRemoteHostAndPort`), and then
  reuses it.
- **The destination address is resolved before the port is checked.** So
  `write(u, 1, "uint8", "127.0.0.1")` fails resolving "uint8", as R2025b's does.
- **Refusals:**
  - udpport's own methods refuse their argument counts with
    `Incorrect number of input arguments for "name"`;
  - every read-only property refuses with `instrument:interface:udpport:ReadOnly`, naming the
    function or the constructor that sets it;
  - the setters' checks are `InvalidEntry`.
- **`configureMulticast` keeps a group already joined.** Windows refuses a second membership.
- **Datagram mode's `read`** answers a 1xN `udpport.datagram.Datagram`:
  - Data is double for a numeric precision, and char or string for those two;
  - `read(d, 0)` answers `[]`;
  - fewer datagrams than asked gives udpport's own ReadWarning;
  - the precision is only looked at once datagrams have come;
  - a datagram that does not divide into the precision is refused (`network:udp:receiveFailed`,
    with typecast's sentence) and is consumed.
- **The "datagram" callback** runs once per Count datagrams, counted from those already waiting
  when it was set. It runs only as a datagram arrives, and a read does not lower the count
  (probe_net_dgcb: with Count 2, three waiting and one more is two calls).

### echotcpip, echoudp and resolvehost

- **Each refusal keeps its own identifier**, including where the two echo functions differ
  (`invalidSyntaxOff` against echoudp's `invalidSyntax`, `invalidSyntax` against its
  `invalidSyntaxPortRange`).
- **The echo server's measured answers:**
  - a fractional port opens at its integer part;
  - a port that is not double is `createError`;
  - a second "on" names the port already running.
- **The echo servers belong to the session.** They end with the run, or with the console session,
  as fopen's files do. R2025b's end with MATLAB.
- **`resolvehost`:**
  - a part outside 0..255 warns `invalidIPaddress`;
  - a name keeps its lowercased spelling;
  - an address is looked up in reverse, so 127.0.0.1 names this machine.

### Output counts

The nargout audit (ADR 0170) now covers the twelve device names:
- `echotcpip` and `echoudp` take no output (`MATLAB:TooManyOutputs`);
- the constructors and the finds take one, so `[a, b] = serialport(...)` is refused as in R2025b.

## Probes

Six probes in `tools/matlab-checklist/device-probes`, on the loopback:
- `probe_net_tcp`: tcpclient and tcpserver;
- `probe_net_udp`: udpport in both modes;
- `probe_net_echo` and `probe_net_echo2`: the echo servers, resolvehost, udpport's write and
  datagram edges;
- `probe_net_dgcb`: when the datagram callback runs, and a second tcpserver client;
- `probe_net_ports`: who may bind a port another socket holds.

## Measured

Five parity fixtures, 345 rows, recorded from R2025b on the loopback:

| Fixture | Rows |
|---|---|
| `net_tcpclient` | 84 |
| `net_tcpserver` | 53 |
| `net_udpport` | 94 |
| `net_udp_datagram` | 55 |
| `net_echo_resolve` | 59 |

- **Ports:** every fixture's port is one the OS hands a fresh udpport, so concurrent runs do not
  collide.
- **Callback logging:** the helper `dv_netevt` logs a network callback's event without its
  OS-chosen ports.
- **Divergences:** three rows diverge (below). All five fixtures pass packed and boxed.
- **Unit tests:** `tests/JGraph.Tests/Devices/NetworkLayerTests.cs` adds seven:
  - the TCP echo;
  - the server refusing a second client;
  - datagrams kept whole and split by OutputDatagramSize;
  - port sharing;
  - the echo servers ending with the run;
  - a cleared tcpclient closing before the next statement;
  - the datagram callback's count.

The four lanes were not run for this stage: it changes no arithmetic and no value storage.

## Live checks for the user

These need the ESP32 on Wi-Fi:
- **tcpclient to a server on the board.** Stop the server mid-`read`. Expected: ErrorOccurredFcn,
  then a refusal on the next call.
- **udpport datagrams to and from the board**, including broadcast.
- **Multicast between two hosts.**

## Consequences

- `TransportInterface` is the pattern for bluetooth and visadev: a transport, and a declaration.
- `JGraph.Devices.Network` has no dependency on the scripting layer.
- The echo servers are available to every later fixture that needs a peer on the network.

## Divergences

- **An unset callback property reads as `[]`**, as under ADR 0184. The properties are
  BytesAvailableFcn, ErrorOccurredFcn and ConnectionChangedFcn. `isempty` agrees; the rows that ask
  the class diverge (`prop_BytesAvailableFcn_class` in `net_tcpclient`, `net_tcpserver` and
  `net_udpport`).
- **A Datagram displays as a struct.** Its fields, `class` and `properties` agree; no row displays
  one.
- **The echo servers end with the run.** R2025b's end with MATLAB.

## Still open

- **IPv6 udpport** is exercised only by its constructor.
- **A tcpclient losing its server** has not been measured against R2025b.
