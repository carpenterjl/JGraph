# ADR 0188 — USB enumeration, descriptors, hubs and hot-plug under jgraph.usb

## Status

Accepted. Stage D5 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`), after ADR 0187 (3f08796). One commit,
this ADR's.

The stage adds `jgraph.usb`, a JGraph extension with no MATLAB counterpart:
- `devices`: every USB device as a table;
- `descriptors`: one device's descriptors, decoded;
- `tree`: the hub topology;
- `ports`: every hub port;
- `watch`: a callback when a device arrives or leaves.

It also adds `JGraph.Devices.Usb`, the Win32 layer the later USB stages (HID, WinUSB, DFU, storage)
stand on.

## Context

MATLAB has no general USB interface. Its device functions each cover one class through a
toolbox; the plan's USB stages need the machine's USB tree first, to find a board by its IDs and to
say which driver holds each interface.

Windows offers the tree without opening any device and without administrator rights:
- SetupAPI lists devices by interface class;
- the configuration manager gives each devnode's parent, children and properties;
- a hub answers IOCTLs for each of its ports: the connection, the speed, and descriptors read from
  the device on it. This is how USBView reads a machine.

This laptop's USB devices are:
- two root hubs;
- a UVC webcam with a DFU interface bound to WinUSB;
- a HID keyboard with four collections;
- an Intel Bluetooth radio.

## Decision

### The Win32 layer

`JGraph.Devices.Usb` declares SetupAPI, CfgMgr32 and the hub IOCTLs as source-generated P/Invoke.

`UsbEnumerator.Devices` lists every device exposing the USB device or hub interface. For each one it
reads:
- the device descriptor, speed, configuration and BOS descriptors from its parent hub's port
  (`IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX`, `_V2` for SuperSpeed, and
  `IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION`);
- its languages and every string its descriptors name. A string is asked for with 255 bytes, as
  USBView asks;
- its place, from the location path (`PCIROOT(0)#…#USBROOT(0)#USB(3)#USB(2)` is ports 3 then 2)
  and the root hub's order;
- the service bound to it, and to each interface of a composite device (the `&MI_nn` child devnodes).

`HubPorts` lists every port of every hub: whether it is connected, whether a person can reach it
(`IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES`), its speed, and the device on it, found by driver key.

`UsbDescriptors` decodes a configuration:
- interface associations, interfaces and alternate settings, endpoints;
- each class-specific descriptor named from its class's specification.

Some class-specific descriptors are decoded further:
- HID: version, country, report descriptor length;
- DFU functional: attributes, detach timeout, transfer size, version, with DfuSe's 1.1a;
- CDC: header, call management, ACM, union, Ethernet, NCM;
- audio control units and terminals (1.0 and 2.0), streaming, and MIDI jacks;
- video control, and streaming formats with their FourCC and frames with size and rate.

BOS capabilities are named, and MS OS 2.0 and WebUSB are recognised by their platform UUIDs.

`UsbCrossReference` walks a COM port's or HID collection's devnode up to the USB device it belongs
to.

`UsbWatcher` registers `CM_Register_Notification` on the USB device interface class.

### The script surface

- **`jgraph.usb.devices(Name=Value…)`** answers a table: VendorID and ProductID as four hex digits,
  Manufacturer, Product, SerialNumber, Class, Speed, Location (`2-8`, or the bus for a root hub),
  Driver, Ports (its COM ports), HIDCollections and InstanceID.
  - A composite device's Class lists its interfaces' classes ("Video, DFU").
  - Product falls back to Windows' cached name for a device that does not answer.
  - The filters are VendorID and ProductID (a number, or hex text with or without `0x`), Class (a
    code, or text in a class name), SerialNumber, Driver (the device's or any interface's) and
    Product. Filter names match partially and case-blind.
- **`jgraph.usb.descriptors(dev)`** takes a row of that table, an instance ID, or filters that match
  one device. It answers:
  - the device's fields and descriptor bytes;
  - the configuration: associations, interfaces with their names, drivers, endpoints and decoded
    class-specific descriptors;
  - the BOS capabilities, and the string table.

  An interface in an association has the driver of the association's first interface: the webcam's
  streaming interfaces are `usbvideo`'s.
- **`jgraph.usb.tree`** prints the topology as `lsusb -t` does, one line per device under its hub;
  `t = jgraph.usb.tree` answers it as a cell of root hubs whose Children nest.
- **`jgraph.usb.ports`** answers a table of Hub, Port, Connected, UserVisible, Speed and Device.
- **`w = jgraph.usb.watch(@fcn, Name=Value…)`** calls `fcn(w, evt)` at the device queue's drain
  points. `evt` has:
  - Type: `DeviceArrived` or `DeviceRemoved`;
  - Device: the row, as the device was last seen when it leaves;
  - AbsTime.

  `delete(w)` or its last holder stops it.

The names hang off the `jgraph` root beside `jgraph.net.compile`. Like any package function, each is
called when it is named without parentheses. A builtin that is told its output count gets the bare
statement's count, which is why `jgraph.usb.tree` alone prints. Every name refuses on a system other
than Windows.

## Measured

- **Fixture**: `usb_enum_smoke` (20 rows), JGraph-only and written from the rule. Its rows answer
  the same on any Windows machine: the empty table's shape, the ports table's variables, the tree's
  class, the watcher's properties and lifetime, and every refusal.
- **Unit tests**: `tests/JGraph.Tests/Devices/UsbTests.cs` adds 21 (twelve of them theory cases).
  They decode the laptop's webcam, keyboard and Bluetooth descriptors, captured in `UsbCaptures.cs`:
  - the webcam's 19 interfaces, 3 associations, MJPEG and YUY2 frames and DFU functional descriptor;
  - the keyboard's four HID report lengths;
  - the radio's alternate settings.

  They also cover:
  - a built CDC ACM function and a DfuSe descriptor;
  - a BOS with MS OS 2.0 and WebUSB;
  - location paths, hardware IDs and interface paths;
  - the enumeration and the script functions on this machine.
- **On this laptop** `jgraph.usb.tree` prints the two root hubs, the webcam on port 8 of bus 2, the
  keyboard on 9 and the radio on 10.

## Live checks for the user

- Plug the ESP32 and the STM32 board in and out with a `jgraph.usb.watch` running at the prompt; the
  events arrive at the idle prompt.
- `jgraph.usb.devices` should show the ESP32's USB-UART bridge with its COM port in Ports.

## Divergences

None: the names have no MATLAB counterpart.

## Still open

- **Strings of a sleeping device.** A device in selective suspend answers only the strings Windows
  keeps: every other string request fails with `ERROR_GEN_FAILURE`, as it does in USBView. On this
  laptop Manufacturer is empty for all three devices; Product falls back to Windows' cached name, and
  the serial number is cached.
- **The other cross-references** the plan names join with their stages: Drives with mass storage
  (D9), audio endpoints (D10), cameras (D11), network adapters (D12).
- **The driver-binding refusal sentences** ("bound to HidUsb; open it with jgraph.usb.hid") arrive
  with the first stage that opens a device, D6. `InterfaceDrivers` holds what they need.
- **Hub descriptors** are not decoded; `jgraph.usb.ports` answers what a script needs of a hub.
