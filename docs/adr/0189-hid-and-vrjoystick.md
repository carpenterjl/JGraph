# ADR 0189 — HID collections under jgraph.usb, and vrjoystick over WinMM

## Status

Accepted. Stage D6 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`), after ADR 0188 (a13479a). One commit,
this ADR's.

The stage adds:
- `jgraph.usb.hidlist` and `jgraph.usb.hid`: HID collections and their reports, a JGraph extension;
- `vrjoystick`: Simulink 3D Animation's joystick, R2025b's name;
- `JGraph.Devices.Hid` over `hid.dll`, and `JGraph.Devices.Joystick` over WinMM with a simulated
  controller for tests.

## Context

MATLAB has no general HID interface. Its one HID-shaped function is `vrjoystick`, from Simulink 3D
Animation, which is licensed on this machine. R2025b marks it for removal in favour of
`sim3d.io.Joystick`; `vrjoystick.p` and its methods are p-coded.

`probe_vrjoystick` measured it with no controller attached:
- every id, and every second argument, is `sl3d:vrjoystick:notconnected`;
- the first construction in a session warns `sl3d:init:hardwaredeprecationwarning`;
- too many inputs are refused before its body runs;
- `vrjoystick(0)` crashes R2025b with an access violation.

Help lists `read`, `axis`, `button`, `pov`, `caps`, `force` and `close`.

Windows gives user mode every HID collection through `hid.dll`. It opens keyboards and mice
exclusively for itself, so their input reports cannot be read. It keeps the report descriptor to
itself and gives out preparsed data, which the `HidP_*` functions decode.

This laptop has eleven collections:
- the ASUS keyboard's eight: keyboard, mouse, consumer control, system control, four vendor-defined;
- the I2C touchpad's three.

It has no game controller.

## Decision

### HID

`HidDevice.List` enumerates the HID interface class. For each collection it gives:
- its attributes and its caps (usage page, usage, the three report lengths);
- its manufacturer, product and serial strings, which the HID driver caches;
- the USB device it belongs to, found by walking up the devnodes (empty for Bluetooth and I2C
  collections);
- whether Windows keeps it: usage page 1, usages 2 and 6.

`HidDevice.Open` opens a collection for overlapped I/O. A reader thread queues input reports with
their report ID first, as hidapi gives them. It also offers:
- output reports through the interrupt pipe (`WriteFile`) or the control pipe (`HidD_SetOutputReport`);
- feature and input reports through the control pipe;
- every button and value capability from the preparsed data;
- a usage's value, raw or scaled, and the buttons pressed, read out of a report.

`jgraph.usb.hidlist(Name=Value…)` answers a table:
- VendorID and ProductID as hex text, as `jgraph.usb.devices` gives them;
- Manufacturer, Product, SerialNumber;
- UsagePage and Usage;
- the three report lengths;
- SystemOwned, USBInstanceID and Path.

It filters by VendorID, ProductID, UsagePage and Usage (numbers or hex text) and SerialNumber.

`h = jgraph.usb.hid(Name=Value…)` takes the same filters, Path and InputBuffers, and opens the one
collection they leave. It refuses no match, several matches, and a collection Windows keeps, each by
name. On the object:
- `read(h[, timeout])` takes the oldest report, or warns and answers `[]`;
- `write`, `getFeature`, `setFeature`, `getInput` and `setOutput` handle reports;
- `caps(h)` answers a table of capabilities;
- `getValue` and `getButtons` read a report;
- `configureCallback(h, "report", fcn)` calls `fcn(h, evt)` per report at the device queue's drain
  points;
- `flush`, NumReportsAvailable, NumInputBuffers, Timeout, UserData and Tag.

Reports are uint8 rows.

### vrjoystick

`vrjoystick` follows the measurements:
- too many inputs refused at once;
- the warning once per session, before a missing id is refused (`MATLAB:minrhs`);
- `notconnected` for an id that is not a positive integer no greater than the number of connected
  controllers.

`vrjoystick(k)` is the k-th controller WinMM's `joyGetPosEx` answers. On it:
- `[a, b, p] = read(joy)` answers axes in [-1, 1] (X, Y, then Z, R, U and V as the controller has
  them), buttons as a logical row, and points of view in degrees, -1 when centred;
- `axis`, `button` and `pov` pick by 1-based index;
- `caps` answers Axes, Buttons, POVs and Forces;
- `close` ends it;
- a controller that goes away is `notconnected` again.

`JoystickBackends.Current` answers a simulated set a test installed on its thread, or WinMM's.

## Measured

- **`vrjoystick_offline`**: 14 rows, recorded from R2025b with no controller attached, where JGraph's
  WinMM sees none either. They cover the argument counts, the once-a-session warning, and
  `notconnected` for every id form and option.
- **`hid_smoke`**: 12 rows, JGraph-only and written from the rule: the empty list's shape and
  classes, hex usages, and every refusal of `hidlist` and `hid`.
- **Unit tests**: `tests/JGraph.Tests/Devices/HidTests.cs` adds 7 (four of them one theory's
  cases):
  - WinMM's scaling;
  - `vrjoystick` on a simulated controller: read, the picks, caps, the refusals, close;
  - a controller that goes away after it was opened;
  - the HID list, and one collection opened for its capabilities on whatever this machine has.
- **On this laptop** `jgraph.usb.hidlist` lists the eleven collections. The keyboard's are refused
  as Windows' own, and the consumer-control collection opens, answers its capabilities, and times
  out a read.

## Live checks for the user

- The STM32 board's TinyUSB HID interfaces (generic in/out and a gamepad), once written: reports
  both ways, feature reports, and `vrjoystick` on the gamepad compared with R2025b's axis order and
  scaling.

## Divergences

- **`vrjoystick` has no force feedback.** JGraph reads controllers through WinMM, which has none:
  `force`, and `read` given a force, are refused with `JGraph:vrjoystick:NoForceFeedback`, and caps
  answers Forces 0. R2025b drives force-feedback axes through DirectInput.
- **`vrjoystick(0)` is `notconnected`.** It crashes R2025b.
- **A second argument other than `'forcefeedback'`** is refused by name when the controller is
  there. R2025b's answer with a controller attached is not measured; with none it is
  `notconnected`, as JGraph's is.
- **The objects display in JGraph's layout**, as every device object does since ADR 0174.

## Still open

- **The axis order** of R2025b's DirectInput reading against WinMM's X, Y, Z, R, U, V needs a
  controller to compare.
- **XInput-only controllers** (Xbox pads through their XInput driver) show their triggers as one
  axis through WinMM. The plan's XInput path waits for a controller to test with.
