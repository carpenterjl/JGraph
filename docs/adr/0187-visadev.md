# ADR 0187 — visadev over the installed VISA, with a simulated VISA and a HiSLIP peer

## Status

Accepted. Stage D4 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`), after ADR 0186 (95702ba). One commit,
this ADR's.

The stage adds:
- `visadev`, `visadevlist` and `visadevfind`: instruments through VISA;
- `JGraph.Devices.Visa`: the VISA backend interface, and NI-VISA's `visa64.dll` bound at run time;
- `SimulatedVisa` and its test door `jgraph.internal.visasim`;
- two network modes for `tools/devices/peer-sim`: a raw TCP socket and a HiSLIP 1.0 server;
- a small enumeration value for `visalib.InterfaceType`, `visalib.Parity`, `visalib.FlowControl` and
  `matlab.lang.OnOffSwitchState`.

## Context

R2025b's visadev is readable except for its client:
- `visadev.m`, `visadevlist.m` and `visadevfind.m`;
- `visalib.Resource` and its seven subclasses (`GPIB`, `VXI`, `Serial`, `PXI`, `TCPIP`, `USB`,
  `Socket`), `EOIModeSupport` and the enumerations;
- the resource manager, the error proxy, the attribute and property tables;
- `LegacyVisa` and the message catalog `resources/instrument/en/interface/visa.xml`.

`visalib.internal.VisaClient` is p-coded, and so is the C++ device plugin under it. Their reads were
measured through NI-VISA 26.5 by `probe_visa_timing`, which times each kind of read against each kind
of resource and reads VISA's attributes back after it.

NI-VISA 26.5 is installed on this machine. No instrument is: the resources are the com0com pair and
two network peers `tools/devices/peer-sim` now serves.

## Decision

### The VISA layer

`JGraph.Devices.Visa` declares `IVisaBackend` in VISA's own vocabulary:
- find and parse resource names;
- open sessions that read, write, get and set attributes, clear, read the status byte and trigger;
- status codes and attribute numbers as `visa.h` has them.

`NiVisaBackend` binds fourteen functions of `visa64.dll` by address, loaded from the system folder on
first use. A machine without a VISA runs every other device class, and `visadev` alone refuses with
R2025b's `unableToFindPreferredVISA`. The preferred VISA's name is the resource manager's
manufacturer and `VISA`, which gives R2025b's `National Instruments VISA`.

### visadev

`visadev` is transcribed from `visadev.m`, `ResourceFactory` and `visalib.Resource`:
- the inputParser's optional SynchronousRead and Tag, then `mustBeNonzeroLengthText`;
- `"reset"`, which forgets the open resources and answers nothing;
- a name or alias the last `visadevlist` listed, matched by `contains`, then the VISA's parse and an
  open;
- `*IDN?` for anything that is not serial, filling Vendor, Model and SerialNumber (a socket is asked
  and not heard);
- `multipleIdenticalResources` for a second object, naming the resource upper-cased.

Each class's `initResourceHook`, `postConnectHook` and `adjustGroupListHook` is followed:
- a serial resource turns VISA's termination character on;
- a socket turns END suppression off;
- TCPIP, USB, GPIB and VXI set EOIMode on and the terminator to `"off"` for reads, LF for writes.

The Timeout is VISA's, in milliseconds, read back. A value that is not whole milliseconds rounds
and warns `unableToSetTimeoutValue`.

### The synchronous reads

These are measured. The shared client's buffer is filled only by VISA reads; nothing arrives unasked.
- **`read`** turns VISA's termination character off and asks for the bytes. A read that gets
  nothing leaves the client to wait the Timeout again, then warn: twice the Timeout in all. A read that
  times out part-way fails with `operationTimedOut` and drops what came.
- **`readline`** reads until the termination character or END; a timeout fails. With END on and the
  character off, the whole message is the line.
- **`readbinblock`** reads a byte at a time to the `#`, then its header, then the block. A bad header
  is `unableToCompleteOperation`.
- **`writeread`** is `writeline` then `readline`, with the client's error for a missing terminator.

Where the read stops is VISA's:
- a serial read ends at its count, or at the character when `ASRL_END_IN` says so;
- a socket read ends when nothing more is waiting;
- a HiSLIP read ends at the message's END.

A HiSLIP client in synchronized mode drops an answer left unread when it sends the next message.

### The rest of visalib.Resource

- **Terminator.** `configureTerminator` sets the client's terminators, VISA's character and its enable.
  `"off"` keeps the client's read terminator and turns VISA's character off.
- **flush.** With no argument it is a device clear first: a break on a serial line, a HiSLIP device
  clear. `flush(v, "input")` touches only the client's buffer.
- **visastatus** reads the status byte, or `visaStatusUnavailableAsConfigured` where the resource has
  none. READY stayed false in R2025b even after a HiSLIP service request, and so it does here.
- **Serial settings** are VISA attributes, set and read back. NI-VISA refuses one and a half stop
  bits except with five data bits, and two with five.
- **Pins.** The pins answer OnOffSwitchState values.
- **Dropped surface.** `configureCallback`, `NumBytesAvailable` and the `BytesAvailableFcn`
  properties are refused by name.
- **Legacy.** LegacyVisa's methods are the serialport's mixins. They now take any
  `ILegacyTransport`, so on a visadev they call the resource's own `read` and `readline`.
- **Undefined methods.** A method another device class has, called on one that has none, is R2025b's
  "Undefined function … for input arguments of type …" (`serialbreak(v)`, `visatrigger` on TCPIP).

### visadevlist and visadevfind

`visadevlist` is transcribed from `visadevlist.m`:
- Timeout's assert and Identification's validator, in their words;
- `UsingDefaults` counting either parameter;
- the rows of `viFindRsrc "?*::INSTR"`, uniquely named, with RowNames.

With identification, a resource that cannot be opened is left out: NI-VISA leaves out the port the
peer holds. Without it, Alias is empty and Type is `unset`. Serial resources are never asked
`*IDN?`. `visadevfind` is the device session's find.

### The simulator and the peers

`jgraph.internal.visasim('on')` puts a `SimulatedVisa` behind the session:
- the simulated serial ports as ASRL resources;
- the peer engine on `TCPIP0::127.0.0.1::5025::SOCKET`;
- the peer engine on `TCPIP0::127.0.0.1::hislip0::INSTR`.

Both network instruments answer `*IDN?` through a reset, with a standing rule. Each kind of session
keeps the read rules above.

`peer-sim` serves the same engine to R2025b:
- `tcp:PORT` is a raw socket;
- `hislip:PORT` is a HiSLIP 1.0 server in synchronized mode: data, device clear, status query,
  service request, lock, remote/local, trigger.

The engine gains `stb`, `srq` and `inst` (the triggers and clears it has seen). A break reaches the
far end as a NUL, as com0com delivers it. `visa_peer.m` starts the peers in MATLAB, or the simulator
in JGraph.

## Probes

Six probes in `tools/matlab-checklist/device-probes`:
- `probe_visa_env`: the list and the constructor's refusals;
- `probe_visa_serial` and `probe_visa_serial2`: a serial object, and the attributes each step leaves;
- `probe_visa_instr`: the HiSLIP and socket objects;
- `probe_visa_timing`: every read against every resource;
- `probe_visa_misc`: identification in the list, service requests, refusal texts, enumeration setters.

## Measured

- **Fixtures**: `visa_list_ctor` (70 rows), `visa_serial` (220) and `visa_instr` (104), recorded from
  R2025b against NI-VISA and the peers. JGraph agrees on every row in both representations, except
  the two divergences below. The same three fixtures run by JGraph on the installed NI-VISA, with
  the peers and no simulator, agree on every row too.
- **Unit tests**: `tests/JGraph.Tests/Devices/VisaTests.cs` adds 16 (seven of them one theory's cases):
  - the simulator's resource names and refusals;
  - the serial, message and socket read rules;
  - NI-VISA's stop-bit rules;
  - scripts through the list, the reads, status, clear and the enumerations;
  - the installed VISA when there is one.

## Live checks for the user

These need an instrument, and will use the STM32 board's USBTMC firmware when it is written:
- `visadevlist` with a USB instrument attached: Vendor, Model and SerialNumber, and whether it asks
  `*IDN?` or reads VISA's attributes;
- `visastatus` READY after a USBTMC service request;
- GPIB, VXI and PXI resources, and `visatrigger`, need hardware this project does not have.

## Divergences

- **A failed read leaves VISA's termination character off in R2025b.** `read` and `readbinblock`
  turn it off for their VISA read and on again only when the read succeeds, so after a timeout
  `readline` never finds a line until `configureTerminator` is called. JGraph turns it back on either
  way (`visa_serial` `readline_after_failed_read`).
- **A read/write terminator pair sets VISA's character from the write terminator in R2025b.**
  `configureTerminator(v, "CR", "LF")` makes VISA stop at LF while the client looks for CR. JGraph
  gives VISA the read terminator's last character. No recorded row differs: a line with both
  characters reads the same either way.
- **visadevlist's Type column holds the names.** R2025b's is a `visalib.InterfaceType` column
  (`visa_list_ctor` `list_type_class`).
- **The enumeration values display in JGraph's layout**, as every device object does since ADR 0174.
- **An unset ErrorOccurredFcn reads as `[]`**, as the callbacks of ADRs 0184–0186 do.

## Still open

- **Native USBTMC** without a VISA, planned for this stage, waits for the WinUSB transport of stage
  D7 (chips file entry 25).
- **The rest of visalib.Resource's hidden surface**: `saveobj`/`loadobj` (the `noSave` and `noLoad`
  warnings), the attribute methods, and the transfer methods (chips file entry 26).
- **`superclasses`** and **`feature('getpid')`** are not implemented; the fixtures use `isa`
  (chips file entry 27).
- **The network and Bluetooth legacy mixins** (chips file entry 24): `SerialportLegacy` now takes any
  `ILegacyTransport`, the first step that entry names.
