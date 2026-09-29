# ADR 0186 — bluetooth and ble, over WinRT, with a simulated twin of the ESP32 peer

## Status

Accepted. Stage D3 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`), after ADR 0185 (8d58619). One commit,
this ADR's.

The stage adds:
- `bluetooth` and `bluetoothlist`: Bluetooth classic's serial port profile;
- `ble` and `blelist`, with the Characteristic and Descriptor objects: Bluetooth Low Energy GATT;
- a new project, `src/JGraph.Devices.Bluetooth`: the Windows backend;
- `JGraph.Devices.Bluetooth` types in `JGraph.Devices`: the backend interface, RFCOMM, SDP and the
  GATT names;
- the simulated radio, and its test door `jgraph.internal.btsim`;
- `tools/devices/esp32-peer`: the ESP32 firmware the simulator twins.

## Context

Much of R2025b's Bluetooth is readable:
- `bluetooth.m`, `bluetoothlist.m` and `matlab.bluetooth.ChannelClient`;
- `ble.m` and `blelist.m`;
- blelib's `Characteristic`, `Descriptor`, and the read and write interface classes;
- blelib's three JSON name tables;
- the message catalogs.

The native transports and blelib's internals (Constants, the UUID resolver, validateDataRange, the
scan cache) are p-coded. They were measured by `probe_ble_internals` 1–3, which call the p-coded
functions directly.

R2025b itself uses WinRT's DeviceWatcher on Windows, as its messages name it. BLE scanning has no
Win32 API. `JGraph.Scripting` targets net8.0 and must not take a Windows TFM.

This machine's radio was off for the first probe and on for the rest. The neighbourhood offers many
peripherals and one classic device, none of them a test board.

## Decision

### The backend

`JGraph.Devices` declares `IBluetoothBackend`:
- the radio's state;
- classic discovery and the paired list;
- RFCOMM channels;
- the advertisement scan;
- a peripheral's GATT tree by index.

`BluetoothBackends.Current` answers a simulated backend when a session installed one, or loads
`JGraph.Devices.Bluetooth.dll` from the application folder.

`JGraph.Devices.Bluetooth` targets `net8.0-windows10.0.19041.0` and implements the backend over
Windows.Devices.Bluetooth:
- **Radio.** `Radio.GetRadiosAsync`.
- **Classic discovery.** The paired devices, plus a DeviceWatcher for the inquiry's length, 5 s
  unless Timeout says otherwise. Each device's status comes from its serial port services, and its
  channel from the SDP protocol descriptor list, which `SdpRecord` parses.
- **Scan.** A BluetoothLEAdvertisementWatcher in active mode. It merges scan responses into R2025b's
  eleven Advertisement fields and returns peripherals strongest first.
- **GATT.** Read uncached at connection.

The project is built but not linked: `JGraph.Scripting` references it with
`ReferenceOutputAssembly="false"`. A target adds the backend and its WinRT projection
(`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`) as copy-to-output and copy-to-publish items,
so every host carries them. The installer anchors all three files.

RFCOMM needs no WinRT. `RfcommTransport` is a Winsock AF_BTH stream socket in `JGraph.Devices`
(SOCKADDR_BTH, packed), with a reader thread into the shared input buffer.

### bluetooth

`bluetooth` is transcribed from `ChannelClient`:
- `bluetooth()` reconnects to the last device a deleted object used; with none, `noLastConnection`;
- an identifier that is not text is `invalidIdentifierType`;
- an address with separators is normalised;
- the device is found by name or address in the paired list (`invalidIdentifierValue`, with the
  documentation link);
- `connectionExists`, `invalidChannel`, and the channel defaulting to 1;
- the name-value pairs ByteOrder and Timeout, where Timeout must be at least 1;
- the connection's failures as `failedConnect*`.

The channel client registers no error map, so the client's refusals keep their
`transportlib:client:*` and `MATLAB:GenericClient:*` identifiers. Its callback event is the shared
`ByteAvailableInfo`. RemoteID and RemoteName are there as LegacyBluetooth's hidden names.

### bluetoothlist

`bluetoothlist` checks Timeout the way R2025b's inputParser validator does (`invalidTimeout` below
5). Its refusals:
- no adapter: `noBluetoothAdapter`;
- radio off: `winBluetoothNotPoweredOn`.

It answers Name, Address, Channel and Status, with Status in R2025b's words and links. It prints its
pointer to `blelist` only when called with no output, and warns `noDeviceFound` for an empty list.

### blelist

`blelist` is transcribed from `blelist.m`:
- name, timeout and services, matched partially and case-blind;
- each refusal's identifier;
- the name filter by prefix, and the two warnings;
- the pointer line only with no output.

Every peripheral a scan hears is kept on the session: its name, and whether it accepts connections.
That is blelib's Utility cache, which `ble` resolves against.

### ble

`ble` is transcribed from `ble.m`:
- an address, matched by validatestring over the cache's keys, then a name that must be unique
  (`ambiguousDeviceName`);
- a 2 s scan when neither is known, then `undiscoveredDevice`;
- `unconnectableDevice` and `connectionExists`;
- `failToConnect`.

Services and Characteristics are tables named from the name tables. `characteristic` resolves the
service, then the characteristic within it:
- a UUID in any of the three forms, a 16-bit number, or a name;
- `Custom` is refused first;
- the answer is the one object per characteristic.

### The UUID forms and names

`GattNames` holds R2025b's three JSON tables, generated by `tools/devices/gen-gatt-names.py`. A UUID
is written as four hex digits, as eight starting 0000, or as 128 bits. The base UUID's members show
their four digits and every other UUID shows all 128 bits. An unnamed attribute is `Custom`. A
characteristic is named only under the service its table lists it in.

### Characteristic and Descriptor

These are transcribed from their classes and interfaces. The Attributes choose the behaviour:
- **Reading.** ReadOnly, NotifyOnly, ReadNotify, or the Default that refuses
  (`unsupportedOperation`). The read modes latest and oldest follow, with `invalidModeReadNotify`
  before a subscription. NotifyOnly's ten-second wait pauses, so callbacks run meanwhile.
- **Writing.** WriteCommon's type and precision options, including `duplicateWriteOptions` and
  `unsupportedWriteType`.
- **Data.** validateDataRange as measured:
  - integer-valued doubles, char, a string scalar, or the precision's own class;
  - anything else is `invalidDataType`;
  - out of range is `invalidDataRanged`, naming the precision's range;
  - little-endian bytes.
- **Callbacks.** DataAvailableFcn with `invalidDataAvailableFcn`; setting it subscribes.
  `handleData` runs one call per value not yet handed to a callback, at the device queue's drain
  points, with `DataAvailableEventData`. A callback's error goes to stderr in R2025b's words.
- **Descriptors.** Read and write. A written Client Characteristic Configuration moves the
  characteristic's subscription.
- **GATT failures.** Named by R2025b's `gattCommunication*` identifiers, from WinRT's status and
  protocol error byte. A link that is down is `failToExecuteDeviceDisconnected`. The Connected
  property warns `deviceDisconnected`.

A characteristic holds its peripheral, and a descriptor its characteristic, through the exact count
(ADR 0171). Clearing `b` while `c` lives keeps the connection, as blelib's Node parents do. Deleting
a characteristic puts back the subscription the peripheral had.

### The simulator and the board

`SimulatedBluetooth` has:
- a radio whose state a test sets;
- the paired classic device `JGraphPeer`, whose channel 1 is a `SimulatedLine` driven by the peer
  engine, with the same `dp` protocol as the serial fixtures;
- a paired device with no serial port;
- an unpaired one;
- the peripheral `JGraphPeer`:
  - Battery Level: Read, Notify, holding 100;
  - Heart Rate Measurement: notifies `[0 60+k]` every 100 ms while subscribed;
  - Body Sensor Location;
  - FFE1: an echo, with 2901 and 2902.

`jgraph.internal.btsim` installs it for the session; it ends with the run. `tools/devices/esp32-peer`
is the same device on an ESP32: the peer protocol over SPP, and the same GATT server.

## Probes

Five probes in `tools/matlab-checklist/device-probes`:
- `probe_bt_env`: the refusals with the radio on and off;
- `probe_bt_shape`: the two lists' shapes on real radios;
- `probe_ble_internals`, `2` and `3`: blelib's constants, the UUID resolver in every form, the name
  lookups, and validateDataRange.

## Measured

- **`bt_offline`**: 63 rows, recorded from R2025b on this machine's radio and replayed against the
  simulator. It covers every refusal before a connection, and the lists' variables and classes.
  Recording needs a classic device in range; a second recording found none.
- **Pending fixtures**: `bt_spp` (the channel, reads, writes, lines, callbacks, setters, the
  constructor with a paired device) and `ble_gatt` (the scan's entry, the tables, characteristic
  resolution, reads, writes and ranges, subscriptions, callbacks, descriptors, the heart rate). Both
  wait in `tools/devices/esp32-peer/pending-fixtures`. They run in JGraph against the simulator; they
  are recorded when the board is flashed.
- **Unit tests**: `tests/JGraph.Tests/Devices/BluetoothTests.cs` adds 15: the SDP parser, the UUID
  forms, the names, the RFCOMM channel with the peer protocol, the radio refusals, reads, writes,
  notifications, callbacks, descriptors, a dropped link, and the name refusals.
- **The real backend on this machine**: `blelist` heard 19 peripherals in 3 s, with the
  Advertisement in R2025b's shape. `bluetoothlist` listed four classic devices in 18 s for a 5 s
  inquiry, the rest spent on each device's uncached SDP query.

## Live checks for the user

These need the ESP32 flashed with `tools/devices/esp32-peer` and paired as JGraphPeer:
- record `bt_spp` and `ble_gatt`;
- reconcile the simulator's GATT tree and advertisement with what R2025b reports for the board
  (Bluedroid's 1800 and 1801 services);
- compare `bluetoothlist`'s run time and statuses with R2025b's;
- switch the board off during a `read` and compare ErrorOccurredFcn and the connection-lost
  messages.

## Divergences

- **`bluetoothlist`'s Channel is a cell of names.** R2025b's is a categorical, which JGraph holds as
  its cell (`bt_offline` `btlist_channel_class`).
- **The objects display in JGraph's layout**, as every device object does since ADR 0174.
- **An unset DataAvailableFcn reads as `[]`**, as the callbacks of ADRs 0184 and 0185 do.
- **A non-vector write, and a non-scalar UUID number, are refused plainly.** R2025b's
  validateDataRange and getServiceUUID fail on them with `MATLAB:nonLogicalConditional` from an `&&`.
  JGraph refuses the data as `invalidDataType` and keeps R2025b's refusal for the UUID.

## Still open

- **Legacy methods.** LegacyBluetooth's `fopen`, `fread`, `fprintf` and the rest; the network
  classes lack theirs too (chips file entry 24).
- **Hex literals.** `0x180D` in a script needs the hexadecimal literal JGraph's parser lacks (chips
  file entry 23).
- **`table2cell`** is not implemented.
