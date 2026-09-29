# ADR 0184 — serialport, the device objects under it, and the peer that answers the fixtures

## Status

Accepted. Stage D1 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`), after ADR 0183 (d6d6b42). One commit,
this ADR's. The plan's stages 1 and 2 landed together: stage 1's machinery (device objects, the
event queue, the shared transport client) has no MATLAB name of its own, and `serialport` is what
proves it.

The stage adds:
- `serialport` in all three constructor forms;
- `serialportlist` and `serialportfind`;
- `internal.Serialport.clearPreferences`;
- the test-only `jgraph.internal.devicesim`;
- a new project, `src/JGraph.Devices`;
- the peer simulator `tools/devices/peer-sim`.

## Context

`serialport` is readable in R2025b. `internal.Serialport` is 1,276 lines. The shared client it
stands on is readable too: `GenericClient`, `ClientImpl`, `UserNotificationHandler`, the
`ErrorRegistry` and the `Legacy*` mixins. So are the message catalogs under
`resources/{serialport,transportlib,transportclients}`.

What is not readable is the AsyncIO transport under them (`GenericTransport.p`, the C++
`seriallib`). Its behaviour was measured: property validation, the casts, partial reads, callback
counts and timing.

Two problems had to be solved before anything could be measured:
- **No serial ports.** This laptop had none.
- **No far end.** A serial fixture needs something answering on the other end of the wire, in
  R2025b and in JGraph's lanes alike, and the lanes cannot depend on a driver install.

## Decision

### The test assets: com0com, NI-VISA, and one peer engine with two hosts

**Installed on this machine.**
- **com0com 2.2.2.0**, the signed x64 build from the project's SourceForge page, with the pair
  COM20<->COM21. It loads with Secure Boot and memory integrity on.
- **NI-VISA 26.5**, through winget, for stage 5 (visadev). Its install accepted NI's licence.

**The peer.** `JGraph.Devices.Simulation.PeerEngine` is a scriptable device, driven in band. Bytes
the port under test writes are data: the peer logs them, echoes them when asked, and matches them
against reply rules. A command is framed `ESC ESC {text}`, which keeps it out of the data. The
commands are `send`, `later`, `chunks`, `echo`, `recv`, `status`, `pins`, `break`, `on`, `reset`
and `quit`.

The same engine runs in two hosts:
- **For R2025b**, `tools/devices/peer-sim` runs it on COM21. The fixture helper `device_peer.m`
  starts it through `System.Diagnostics.Process`, once per MATLAB process, and waits for its READY
  line. It exits when MATLAB does.
- **For JGraph**, `jgraph.internal.devicesim('COM20')` registers a simulated line for the session.
  It shadows a real port of that name, and lists COM21 as the port the peer holds. The line models
  com0com's null-modem wiring (RTS to CTS, DTR to DSR and DCD) and outlives the objects a fixture
  opens and clears, as com0com's peer does.

The helpers `dp(s, cmd)` and `dp_hex(bytes)` talk to the peer through the object under test. So a
fixture prints the same lines in both engines, and needs no com0com in the lanes.

### Device objects are a declaration on the external kind

A `DeviceObject` is an `IJgsExternal` whose `DeviceClass` declares these things:
- its name (`internal.Serialport`, as `class` answers);
- its short name (`Serialport`, as R2025b's sentences use);
- its bases, for `isa`;
- its properties, each with a getter, a setter or none, and hidden or not;
- its methods, and the `methods` listing R2025b prints;
- its display properties.

Everything the language asks of such a value is answered once, from the declaration: a dot, a
dotted call, function syntax, `get`, `set`, `delete`, `isvalid`, `properties`, `fieldnames`,
`methods`, `isprop` (hidden ones included), `ismethod`, and `==`.

The interpreter gained one branch each in `ExternalMember`, the dotted assignment, the user-method
layer of the name resolver, the binary operators and the call on an external value. R2025b's
refusals come from the declaration, in its words:
- `SetProhibited` names the class as `''Serialport''`, measured through `set`.
- An unknown name in `set` is `MATLAB:class:InvalidProperty`.
- In `get` it is `setgetPropertyNotFound`.
- By dot it is `noSuchMethodOrField` or `noPublicFieldForClass`.
- A deleted object is `InvalidHandle`.

A method with no output refuses one as `TooManyOutputs` before it runs.

### They close with their last holder

`clear s` must free the port before the next statement, or `s = serialport(...)` on the next line
fails. So the device objects join V10's exact lifetime count (ADR 0171): a bind, a container slot
and a frame each hold one. At zero, a check is queued, and at the statement boundary the object is
deleted. That closes the port and saves the settings the next `serialport()` reads.

A temporary nobody binds is deleted at the end of its statement, as R2025b's is. An example is
`get(serialport(...), 'Parity')`. A statement's answer is held by `ans` until it is cleared.

### The shared client is GenericClient transcribed

`TransportClient` implements, once, for every interface to come (tcpclient, udpport, bluetooth,
visadev):
- `read`, `write`, `readline`, `writeline`, `writeread`, `readbinblock`, `writebinblock`, `flush`,
  `configureTerminator` and `configureCallback`;
- the syntax lines and identifier renaming of `UserNotificationHandler` and the `ErrorRegistry`.

What the interface changes is carried by `TransportInterface`: its name, its object name, whether a
precision is required, and the identifiers it renames.

The transport's own checks carry their own identifiers, in `validateattributes`' and
`validatestring`'s words (`DeviceChecks`):
- `MATLAB:AsyncIOTransportChannel:*` for `read` and `write`;
- `MATLAB:BinBlockClient:*` for the binblocks;
- `MATLAB:Serial:*` for the properties.

The rules were measured, not assumed:
- "Instead its type was" is left out for a cell or a char and kept for the transport's own check of
  `write`'s data.
- An empty string reaches `validatestring` as `''`.
- The casts round half away from zero, saturate, and write NaN as 0. A value written as `char` is
  truncated and saturated to 255.
- A short read answers what came, with `ReadWarning` and its hyperlink, the doc link's stray `"'`
  included. A short read that splits an element is `seriallib:serial:readFailed`.
- `readline` answers `[]` and warns on a timeout, leaving the partial line. `writeread` errors
  instead.
- A binblock that stops short answers what came, with no warning.

### Callbacks run where R2025b runs them

A `BytesAvailableFcn` is queued from the reader thread on the script thread's `DeviceEventQueue`.
R2025b runs it at `pause`, `pause(0)` included, and at `drawnow` (probe_sp_callbacks):
- never in a busy loop or between statements;
- never inside a blocking `read`, although a `timer` does fire there.

JGraph drains the queue at those points and at the app's idle prompt. `read` drains timers while it
waits and nothing else. A post wakes a waiting `pause` at once. Without that wake-up, a callback ran
one byte late in the lanes.

- **"byte" mode** fires once per `Count` bytes received since the mode was set or the input
  flushed, however they arrived: ten bytes at once and ten bytes one at a time both give three calls
  at `Count` 3.
- **"terminator" mode** fires once per terminator, a CR/LF split across two reads included.
- **The event data** is `instrument.internal.DataAvailableInfo`, with `BytesAvailableFcnCount` (1 in
  "terminator" mode) and `AbsTime` as a datetime.
- **Turned off.** An event queued before `configureCallback(s, "off")` never runs.
- **An error inside the callback** is the warning `MATLAB:callback:DynamicPropertyEventError`, and
  the script goes on.
- **A lost connection** runs `ErrorOccurredFcn` with the error's `ID` and `Message`. With none set,
  its sentence is printed on the error stream.

### Serial I/O over the Win32 communications API

`Win32SerialPort` opens `\\.\COMn` overlapped:
- One reader thread fills the input buffer, using timeouts that return from `ReadFile` as soon as a
  byte arrives.
- One event thread watches for breaks and for the device going away.
- Writes are overlapped, with the object's `Timeout` and the run's Stop.

`System.IO.Ports` is not used: it is a NuGet package, and its receive model fights a byte-exact
buffer.

R2025b opens with DTR and RTS raised, and JGraph does the same. Under hardware flow control RTS
belongs to the driver, and `setRTS` changes nothing.

`serialportlist` reads `HKLM\HARDWARE\DEVICEMAP\SERIALCOMM`:
- in natural order;
- `"available"` leaving out ports this session's objects hold and ports another program holds.

### serialport, its preferences, and the legacy surface

The constructor follows `internal.Serialport` step by step:
1. `nargin == 1` is refused.
2. `serialport()` reads the saved settings.
3. `PORT` and `BAUDRATE` are checked with R2025b's `validateattributes`.
4. The `BaudRate` setter runs.
5. The name–value pairs are checked for pairing.
6. `inputParser` runs, partial and case-blind, with its `isscalar` and validator refusals.
7. Each setter runs in R2025b's order.
8. The port is opened. Any failure to open is `ConnectionFailed`, with the doc link, as are
   `DataBits` of 4 or 9 given to the constructor, where a setter refuses them itself.

The settings of a connected object that is deleted or cleared are saved per user, in
`%APPDATA%\JGraph\device-preferences.json`, or in the folder `JGRAPH_PREFDIR` names, which the test
assembly points at a temporary folder. `clearPreferences` answers true, as R2025b's does.

The `Legacy*` mixins are transcribed in terms of the modern methods, as they are written. They
cover `fopen`, `fclose` (which warns and does not close), `fprintf`, `fwrite`, `fread`, `fgetl`,
`fgets`, `fscanf`, `scanstr`, `query`, `binblockread`, `binblockwrite`, `flushinput` and
`flushoutput`, and the eleven unsupported methods and forty-two unsupported properties that warn.
`PinStatus` answers `'on'`/`'off'`.

## Probes

`tools/matlab-checklist/device-probes/` holds five probes (`probe_sp_object`, `probe_sp_io`,
`probe_sp_lines`, `probe_sp_callbacks`, `probe_sp_legacy`). Each runs through `run-probes.ps1`,
one MATLAB per probe, against the peer on com0com. What they settled beyond the source:

1. **`class(s)`** is `internal.Serialport`, and `isa(s, 'serialport')` is false.
2. **Property classes are kept.** `DataBits = int8(7)` stays int8, and `BaudRate = int32(19200)`
   stays int32. `Timeout` refuses an integer class with `Stream:timeout:invalidTime`.
3. **com0com accepts** any baud rate, 1.5 stop bits with 8 data bits, and 5 data bits.
4. **Parity is `none`, `even` or `odd`.** `mark` and `space` are refused.
5. **The callbacks' places and counts** are as above. Eight ticks of a 0.1 s timer fired inside a
   0.7 s `read`.
6. **Under hardware flow control with CTS low, `write` never returns.** The first legacy probe hung
   for 400 s. See Divergences.
7. **`serialportfind`** with nothing to find answers `[]`. A lone name is
   `testmeaslib:ObjectCacher:NVPairsAsPairs`, and property names match case-blind.
8. **`whos -file` of a saved serialport reconnects it**, and warns when the port is held.

## Measured

Seven parity fixtures, 511 rows, every one recorded from R2025b against the peer on com0com and
matched by JGraph against the simulated line:

| Fixture | Rows |
|---|---|
| `serial_ctor` | 84 |
| `serial_props` | 98 |
| `serial_io` | 97 |
| `serial_lines` | 79 |
| `serial_callbacks` | 47 |
| `serial_pins_legacy` | 65 |
| `serial_find_save` | 41 |

Two rows diverge (below). `tests/JGraph.Tests/Devices/DeviceLayerTests.cs` adds 24 unit tests:
- the input buffer;
- the codec against R2025b's recorded bytes;
- the peer's protocol;
- the simulated wiring;
- the natural order;
- `validatestring`'s matching;
- the lost-connection path;
- the exact lifetime;
- a com0com round trip, skipped with its reason where the pair is not installed.

The four lanes: 10,311 tests. `JgsFileIndexTests.AnExternalChange_IsSeen_AtTheNextTopLevelStatement`,
a folder-timestamp test this stage does not touch, failed in three lanes and passed in the fourth
and in a rerun. One run of the managed/boxed lane lost its test host after 3.5 minutes; its blame
rerun passed all 10,311. Handlers that run on reader, timer and thread-pool threads now catch what
they throw, so a device event can never end the process.

## Live checks for the user

These need the boards: the ESP32's USB-UART bridge and the STM32's USB CDC.
- **Unplug the adapter mid-`read`.** Expected: `ErrorOccurredFcn`, or the `ConnectionLost`
  sentence, then `ConnectionLost` on the next call. The `ErrorInfo` event's properties are JGraph's
  reading of the source and still to be confirmed on hardware.
- **Break and line errors on a real UART.**
- **Settings a real driver refuses**, such as 1.5 stop bits with 8 data bits.
- **3 Mbaud throughput** against R2025b.

## Consequences

- The device layer is in place for the sibling interfaces. tcpclient, udpport, bluetooth and
  visadev each need a transport and a `TransportInterface`, not a client of their own.
- `JGraph.Scripting` references `JGraph.Devices`.
- Every fixture of a later stage can drive a device through `dp`.
- `jgraph.internal.devicesim` and `peer-sim` are test assets; neither is documented for users.

## Divergences

- **An unset callback property reads as `[]`.** R2025b answers an empty `function_handle`, which
  JGraph's values cannot hold (`serial_ctor` `BytesAvailableFcn_class`).
- **An error inside a `BytesAvailableFcn` keeps R2025b's identifier and gives its own sentence.**
  R2025b's message is twenty lines of AsyncIO's listener stack. JGraph's is "Error executing the
  BytesAvailableFcn callback:" and the error's message (`serial_callbacks` `cb_error_text`).
- **A write that flow control holds back ends after `Timeout` with an error.** R2025b's never
  returns (probe_sp_legacy). An error is the correct answer to a write that cannot finish, so no
  row pins R2025b's hang.
- **`readbinblock`, `writebinblock` and `writeread` need no Instrument Control Toolbox licence.**
  This machine is licensed, so no row shows it.
- **The display is JGraph's layout**, as for every object since ADR 0174. So are `get(s)` and
  `set(s)` as statements.
- **A blocking `read`, `readline` or `serialbreak` ends at once on Stop.**

## Still open

- `save` and `load` of a serialport (`saveobj` saves the settings, and `loadobj` reconnects).
- `serialportfind` answers a `DeviceArray` for several matches. It indexes, but it is not yet a full
  object array.
- `superclasses` is not implemented for any value.
- `func2str(@disp)` answers `'@disp'` where R2025b answers `'disp'`. This comes from JGraph's own
  tests, not this stage. It is entry 22 in `docs/plans/open_task_chips_09_12_2026.md`.
