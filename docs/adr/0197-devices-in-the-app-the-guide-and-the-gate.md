# ADR 0197 — Devices in the app, the guide, and the gate

## Status

Accepted. Stage D13 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`, plan stage 14), after ADR 0196 (3c6be84).
One commit, this ADR's. It is the last stage of the plan.

The stage adds no device class. It puts the twelve stages before it in front of the user:
- a **Devices pane** and a **Serial Explorer pane** in the app, and `serialExplorer`;
- **completion** for `jgraph.` and its packages, and for port names inside `serialport("…`;
- **Workspace pane summaries** that ask no device anything;
- five **scripting guide** sections, with the safety notes;
- a **stress script** and a **timing run** against R2025b.

## Context

Until now every device function was reachable only by typing its name. The plan's last stage asks
for the app to show what is attached and to write the line that opens it, for the editor to know the
`jgraph.usb` names, and for the guide to say which operations cannot be undone.

Two rules from earlier ADRs bound the work:
- the Workspace pane's line for a value must not run device I/O (ADR 0183's rule for external
  values);
- a test or a click must never open, claim or write to a real device (the plan's safety rules).

## Decision

### The panes are models first

`JGraph.Scripting.Devices` holds the two panes' logic with no UI type in it, so the tests read what
the app binds to.

**`DevicePaneModel`** turns one read of the machine into two groups:
- **Serial ports**: the ports `serialportlist` answers. Each says whose it is: the USB device's IDs
  and name where it has one, else what Windows calls the port.
- **USB**: the tree of `jgraph.usb.tree`, hubs and devices in port order, each with its class, driver,
  speed and COM ports.

Each node carries its actions. An action is text, in one of three kinds:
- a **line of code**, which the host writes at the console prompt;
- text to **copy**;
- a **port for the Serial Explorer**.

| Node | Actions |
|---|---|
| A serial port | `s = serialport("COM3", 9600)`; open in the Serial Explorer |
| A USB device with a COM port | the same two, under the port's name |
| With one HID collection | `h = jgraph.usb.hid(VendorID="…", ProductID="…")` |
| With several | `T = jgraph.usb.hidlist(VendorID="…", ProductID="…")`, since `jgraph.usb.hid` wants the one named |
| With WinUSB bound | `d = jgraph.usb.device("<instance ID>")` |
| Any device | `desc = jgraph.usb.descriptors("<instance ID>")`; copy the instance ID |

Two devices with the same IDs get `SerialNumber=` in their HID line. DFU has no action: a firmware
update is not something to offer on a right-click.

**A click opens nothing.** The line is written at the prompt and not run. The user reads it, edits
the baud rate or the variable's name, and presses Enter. So the pane cannot touch a device.

**`SerialTerminal`** is the Serial Explorer's connection: one `ISerialTransport`, opened with what
`serialport` takes, whose received bytes are handed on as they come. `TerminalText` turns them into
what is shown:
- as text: UTF-8, one line break for CR, LF or the pair, and ANSI colour sequences left out (ESP-IDF
  logs with them);
- as hex, sixteen bytes a line.

A character or a sequence split across two reads still comes out whole.

### The Devices pane

`DevicesPane` shows the model as a tree. It reads nothing until it is first shown and nothing while
hidden, because reading the USB tree asks every device for its descriptors. It registers the
notification `jgraph.usb.watch` uses, for USB devices and for COM port interfaces, and reads again
600 ms after the last one, since a device arriving raises several.

A right-click offers the node's actions, and a double-click on a leaf does the first. What the user
collapsed stays collapsed across a read.

### The Serial Explorer, and `serialExplorer`

R2025b's `serialExplorer` opens its Serial Explorer app, takes no argument and answers nothing
(`nargin` and `nargout` are both 0, measured). JGraph's does the same with a pane:
- the port (the list is `serialportlist`'s, read again each time it opens), baud rate, data bits,
  parity, stop bits and flow control;
- DTR and RTS boxes, which say how the port is opened and drive the pins once it is;
- a line to send, with its terminator, or as hex bytes;
- what came back, as text or hex;
- **Code → prompt**, which writes the `serialport` line with the same settings.

Two choices:

**The pane holds the port itself.** It is not a `serialport` object in the workspace. So while it is
connected, `serialport` on the same port is refused as in use, and the other way round, as with any
two programs. The alternative, a workspace variable the pane shares with scripts, would have a
script's `read` and the pane's display taking each other's bytes.

**A session with no window refuses the call**, as `JGraph:serialExplorer:NoWindow`, naming
`serialport` as what a script uses. A host offers the pane through `IScriptDeviceWindows` on the
script context, as it offers `methodsview` its table window.

### Completion

In a MATLAB buffer:
- a dot after `jgraph`, `jgraph.usb`, `jgraph.pcsc` or `jgraph.net` offers the package's members,
  each function with its call shape and a one-line summary. Choosing one inserts the call with its
  required arguments;
- an open bracket after a package function shows its signature, found by the whole dotted name;
- the text inside `serialport("…` offers the serial ports, each with what Windows calls it. A console
  session adds its simulated ports;
- `jgraph` and `NET` complete to their names, for the dot that follows. They used to complete to
  `jgraph()`.

`jgraph.internal` is test-only and offered nowhere.

The signatures are a table written by hand in `DeviceCompletion`, because the registered functions
carry none. A test compares its names with the `jgraph` struct of a live session, so a function added
without a line fails the tests.

### Workspace summaries

Every device class now has a one-line summary read from the object's own fields. Three changed:
- `vrjoystick` asked WinMM for the controller's name each time; it keeps the name from when it was made;
- `audioplayer` and `audiorecorder` had none: "100 samples, 8000 Hz, stopped";
- a BLE descriptor had none.

### The row a find answers has a row's shape

The stress script found that `numel(serialportfind)` was 1 with two ports open. R2025b's is 2: its
device objects are ordinary handle-object arrays (`probe_dev_arrays`). `numel`, `size`, `length`,
`isempty`, `ndims`, `isscalar`, `isvector`, `isrow`, `iscolumn` and `ismatrix` now answer for the row
as for a 1-by-N array. The rest of what an object array does is a divergence, below.

### `write` no longer walks a packed array an element at a time

The timing run found `write(t, data, "uint8")` taking 0.15 to 0.39 s for a megabyte, against
R2025b's 0.01 to 0.03 s. The data's elements were read through a recursive iterator, one boxed value
each. A packed real array is now its buffer, copied once.

## Measured

- **Unit tests**: `tests/JGraph.Tests/Devices/DevicePanesTests.cs` adds 20, over the tree, the
  terminal on the simulated port, the text and hex display, completion, `serialExplorer`, the
  summaries and the row's shape.
- **The machine's own devices** are read by one test, by enumeration alone.
- **The gate**:
  - packed: the device tests, every parity fixture, and the completion, builtin and startup tests,
    448 of 449. The one failure was `serial_callbacks`' `term_crlf_calls`, which counts callbacks
    over a fixed one-second pause; another session's tests were loading the machine, and the fixture
    passed alone three times of three straight after (chips 37);
  - unpacked: the device tests and every parity fixture, 299 of 299;
  - the coverage verifiers and the divergence harvest (456 from 99 ADRs).

  The lanes were not run: nothing here changes a math function, and the one change near value
  storage, the read of a packed buffer in `write`, is covered by the device fixtures in both
  representations.
- **Stress**: `stess_89.m` (outside git) passes 14 of 14 on the Release CLI, on the com0com pair:
  - two hundred lines each way at 115200;
  - terminator and byte callbacks counting while a loop runs;
  - the pins through the null-modem wiring;
  - a timeout;
  - ports freed by `clear` and reopened at 3 Mbaud;
  - a 10 MB binblock;
  - a megabyte through `echotcpip`;
  - a hundred datagrams;
  - twenty USB enumerations that agree.
- **Timing** against R2025b, both on this machine, both ends of COM20<->COM21 held by one script
  (`tools/devices/timing/serial_timing.m`; JGraph is the Release CLI, the better of two runs):

| Row | R2025b | JGraph |
|---|---|---|
| `writeline` + `readline` | 13,808 µs | 121 µs |
| `write` + `read`, 1 byte | 11,325 µs | 138 µs |
| query and answer | 24,072 µs | 235 µs |
| `write` + `read`, 64 doubles | 13,251 µs | 162 µs |
| `NumBytesAvailable` | 37.8 µs | 5.4 µs |
| 1 MiB `write` + `read` | 5.1 MB/s | 38.6 MB/s |
| 1 MiB binblock | 2.9 MB/s | 26.3 MB/s |
| 2000 queued lines | 717 µs a line | 106 µs a line |
| `serialportlist` | 15.1 ms | 0.4 ms |
| open and close | 293 ms | 2.7 ms |
| TCP `writeline` + `readline` | 12,473 µs | 108 µs |
| TCP 1 MiB echo | 63.5 MB/s | 57.5 MB/s |

  R2025b's round trips sit near 12 ms because its reads wait on a polled channel. No row is three
  times slower than R2025b, so none goes to the open-items file. Before the `write` change the two
  megabyte rows were 2.3 and 3.9 MB/s.

- **In the app**, with the user's leave, on the Release build, driven through UI Automation and
  ended without closing so that no layout was saved:
  - the View menu lists Devices and Serial Explorer. A layout saved before they existed put Devices
    beside Files and the Serial Explorer beside the Console;
  - the Devices tree showed COM20 and COM21, and the two root hubs with the webcam, the keyboard's
    HID device and the Bluetooth radio under the second;
  - the Serial Explorer connected to COM20 at 115200. A script on COM21 wrote a line, which the pane
    showed, and read back the line the pane sent. The counters said 20 bytes each way;
  - *Code → prompt* wrote `s = serialport("COM20", 115200); configureTerminator(s, "LF");` at the
    prompt and ran nothing, and the status bar said to disconnect first.

  The picture showed a narrow pane cutting a device's line off after its name, so the class, driver
  and speed now lead its tooltip as well.

## Not measured

- **The context menus and the double-click** of the Devices pane were not exercised in the app: UI
  Automation read the tree and did not open a menu. Their actions are the model's, which the tests
  read, and *Code → prompt* took the same road to the prompt.
- **Hot-plug refresh** of the Devices pane: nothing was plugged in.
- **The dark theme** was not looked at.
- The **board-gated rows** of the plan's test strategy (line errors, a baud mismatch, unplugging
  mid-read, 3 Mbaud on a real UART): no board is connected.

## Live checks for the user

In the app:
- **View → Devices.** The serial ports and the USB tree appear. Right-click the webcam, then *Show
  descriptors*: a line appears at the prompt and nothing runs until Enter.
- **Plug in a USB stick or a board.** The tree gains it within a second, with no click.
- **View → Serial Explorer**, or type `serialExplorer`. Choose COM20 at 115200 and connect. At the
  prompt: `p = serialport("COM21", 115200); writeline(p, "hello")`. The pane shows `hello`. Type a
  line in the pane and `readline(p)` answers it.
- **While connected**, `serialport("COM20", 115200)` at the prompt is refused as in use.
- **In the editor**, type `jgraph.usb.` and then `serialport("`.
- **With the ESP32**: its boot log in the Serial Explorer at 115200, without colour codes. Clear the
  DTR and RTS boxes first to connect without resetting it.

The hardware live checks of the earlier stages still stand, each in its own ADR: the ESP32's serial
bridge and Bluetooth (0184, 0186), the STM32's vendor interface and DfuSe loader (0190, 0191), a USB
stick (0192), the support package for `webcam` (0195), and the long tail's (0196).

## Divergences

- **`serialExplorer` shows a pane of the JGraph app**, where R2025b opens its Serial Explorer app in
  Hardware Manager. In a session with no window it is refused (`JGraph:serialExplorer:NoWindow`).
- **An array of device objects is only the row a find answers.** R2025b's device objects are
  handle-object arrays (`probe_dev_arrays`): `[a b]` is 1-by-2, `[a; b]` 2-by-1, a vector of subscripts
  and `end` index one, a loop walks one, and one holds its objects open. JGraph refuses `[a b]`
  (`MATLAB:class:concatenationScalar`), indexes the row by one whole number only, and does not count
  the row as a holder of its objects (chips 36).

## Still open

- **Arrays of device objects** (chips 36).
- **The dispatch sweep has no rows for the device names.** `verify-method-classes.py` reports 77
  catalog names with no row, the device classes' among them (chips 12, which predates this plan).
- **Completion after `s.`** for a device object's own methods and properties is not offered; the
  editor does not know a variable's class.
- **The Serial Explorer shows what it receives and not what it sends**, and has no log to a file.
- **The Devices pane lists serial ports and USB only.** Audio devices, MIDI ports, cameras,
  Bluetooth peripherals and VISA resources are reached by their list functions.
