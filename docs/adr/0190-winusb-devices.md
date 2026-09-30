# ADR 0190 — Raw and vendor-specific USB over WinUSB, as jgraph.usb.device

## Status

Accepted. Stage D7 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`), after ADR 0189 (2b03ffe). One commit,
this ADR's.

The stage adds:
- `jgraph.usb.device`: a device, or one interface of it, bound to WinUSB, a JGraph extension;
- `JGraph.Devices.WinUsb`: `winusb.dll`, the finding of WinUSB interface paths, and the
  driver-binding refusal that later stages reuse.

## Context

A device whose class has no Windows driver of its own is reached through WinUSB. Examples are a
vendor-specific board, USBTMC without a VISA, and a DFU bootloader. WinUSB is in the box. Windows
binds it:
- to a device whose firmware carries MS OS 2.0 descriptors, which the plan's test firmware will;
- to any device a person binds with an INF or Zadig.

libusb itself uses WinUSB on Windows, so JGraph goes to it directly and ships no native library.

A WinUSB-bound interface is found through the device interface GUIDs its driver registered under
the devnode's `Device Parameters` key.

This laptop has one such interface: the webcam's DFU interface (13D3:56EB, interface 4) is bound to
WinUSB, with GUID `{ecceff35-1463-4ff3-acd9-8f992d09acdd}`.

## Decision

### The Win32 layer

`WinUsbDevice.PathsOf(instance)` answers each WinUSB path of a device, with the interface it opens:
- the device's own devnode (-1: the whole device is WinUSB's);
- each `&MI_nn` child devnode, walked through the configuration manager.

`WinUsbDevice.Open(path)` opens the file overlapped, as `WinUsb_Initialize` requires, and takes:
- the first interface;
- a composite's other WinUSB interfaces through `WinUsb_GetAssociatedInterface`;
- each interface's pipes in its current alternate setting.

On the device:
- control transfers on the default pipe;
- bulk and interrupt reads and writes on any pipe, each bounded by `PIPE_TRANSFER_TIMEOUT`;
- pipe policies, reset, abort, flush and clear-halt;
- alternate settings, whose pipes are read again;
- a continuous reader: a thread that reads one IN pipe and hands each transfer to a callback.

A timeout is a `TimeoutException`. A stall is a `DeviceIOException` that says so. A device gone is
`DeviceConnectionLostException`.

`WinUsbDevice.NotBoundSentence` refuses a device that is not WinUSB's. It names:
- each interface's driver, grouped ("interfaces 0, 1 and 3 are bound to HidUsb; interface 2 is bound
  to usbvideo");
- how to bind WinUSB: Zadig, an INF, or MS OS 2.0 descriptors in the firmware.

### jgraph.usb.device

`u = jgraph.usb.device(dev)` takes a row of `jgraph.usb.devices`, an instance ID, or its filters.
`Interface=n` picks one WinUSB interface of a composite; without it, the first is opened. On the
object:
- `control(u, Direction=, Type=, Recipient=, Request=, Value=, Index=, Length=|Data=)`: an IN
  request answers a uint8 row;
- `read(u, endpoint, count[, precision])` and `write(u, endpoint, data[, precision])` use the
  transport client's precisions and ByteOrder. A short transfer warns;
- `clearHalt`, `resetPipe`, `abortPipe`, `flushPipe` and `setPolicy` act on a pipe;
- `setAlternate` and `getAlternate`;
- `configureCallback(u, endpoint, count, @fcn)` reads continuously and calls `fcn(u, evt)` at the
  device queue's drain points, with evt.Data, evt.Endpoint and evt.AbsTime. `configureCallback(u,
  "off")` stops it.

Its properties:
- VendorID and ProductID as hex text, Manufacturer, Product (Windows' name when the device does not
  answer), SerialNumber;
- the Endpoints table and the Interfaces row;
- Timeout, ByteOrder, UserData and DataFcn.

### Finding a root hub by class

A root hub has no descriptors of its own, so `jgraph.usb.devices("Class", "Hub")` and `Class = 9`
now match by the Class column and by being a hub.

## Measured

- **Fixture**: `usb_device_smoke` (8 rows), JGraph-only and written from the rule. It covers the
  opening refusals and the not-bound refusal of a root hub, which no machine binds to WinUSB.
- **Unit tests**: `tests/JGraph.Tests/Devices/WinUsbTests.cs` adds 3:
  - the refusal's grouping, for a whole device and for an interface;
  - an unknown device with no paths;
  - on a machine with a WinUSB interface, opening it and reading the device descriptor through a
    standard GET_DESCRIPTOR, which changes nothing. It must equal what the hub reported.
- **On this laptop** the webcam's DFU interface opens with interface 4 and no endpoints. It answers
  its 18-byte device descriptor over the default pipe, the same bytes `jgraph.usb.descriptors`
  reads through the hub.

## Live checks for the user

- The STM32 board's vendor interface, once its TinyUSB firmware has MS OS 2.0 descriptors:
  - a bulk loopback, reads and writes with precisions, and short packets;
  - an interrupt endpoint;
  - vendor control requests both ways;
  - a continuous read at speed.

## Divergences

None: the names have no MATLAB counterpart.

## Still open

- **Isochronous transfers** (`WinUsb_RegisterIsochBuffer`, `WinUsb_ReadIsochPipeAsap`) wait for a
  device with an isochronous endpoint; the board's audio or video firmware would have one.
- **Several requests in flight** for the continuous reader. One synchronous request at a time is
  enough for full-speed bulk; high-speed throughput needs overlapped requests queued ahead.
