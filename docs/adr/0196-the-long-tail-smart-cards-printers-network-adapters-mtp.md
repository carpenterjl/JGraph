# ADR 0196 — The long tail: smart cards, printers, network adapters and MTP

## Status

Accepted. Stage D12 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`, architecture L), after ADR 0195 (4643acb).
One commit, this ADR's.

The stage adds four things, all JGraph extensions with no MATLAB counterpart:
- `jgraph.pcsc`: smart cards through PC/SC, which is how Windows exposes CCID readers;
- `jgraph.usb.printers` and `jgraph.usb.printraw`: the printer queues, and a job sent as it is;
- `jgraph.usb.netadapter`: the network adapter a USB device became;
- `jgraph.usb.mtplist` and `jgraph.usb.mtp`: phones, players and cameras that speak MTP or PTP.

## Context

These are the USB classes a script meets least, and Windows already owns each of them:
- a CCID reader belongs to the smart-card resource manager, and nothing else may talk to it;
- a printer belongs to the spooler;
- a CDC networking device becomes an ordinary network adapter;
- an MTP or PTP device belongs to Windows Portable Devices (WPD).

So each part goes through the owner's API and not through raw USB.

The machine the stage was written on has none of the hardware:
- no smart-card reader, and the smart-card service stopped;
- no portable device;
- five printer queues, all virtual;
- no USB network adapter.

So every part has a simulator behind a test-only `jgraph.internal` function, as the earlier stages
have, and the real backends are checked as far as the machine allows.

## Decision

### Smart cards: `jgraph.pcsc`

`JGraph.Devices.SmartCard` calls winscard.dll. A machine with no reader answers
`SCARD_E_NO_SERVICE`, because Windows stops the service when there is nothing for it to serve. That
is an empty list here, not an error. Each connection has a context of its own.

- `T = jgraph.pcsc.readers` is a table: Reader, State, CardPresent and ATR (hex text).
- `c = jgraph.pcsc.connect(reader, Share=, Protocol=)` connects. The reader is a name (whole, or the
  one reader the text is part of), an index, or a row of the table. With no reader it is the one
  that holds a card. Share is "shared", "exclusive" or "direct"; Protocol is "any", "T0", "T1" or
  "raw".
- `[resp, sw] = transmit(c, apdu)` sends a command APDU. The APDU is bytes or hex text
  (`"00 A4 04 00"`). `resp` is the response data as uint8; `sw` is the status word as hex text
  (`"9000"`), because JGraph has no hex literals to compare a number with.
- `out = control(c, code, data)` reaches the reader. `jgraph.pcsc.ctlcode(n)` is `SCARD_CTL_CODE(n)`.
- `status(c)`, `beginTransaction(c)`, `endTransaction(c, Disposition=)`,
  `reconnect(c, Share=, Protocol=, Initialization=)` and `disconnect(c, Disposition=)` are PC/SC's.
  `disconnect` deletes the object; so does `clear`, leaving the card as it is.
- `w = jgraph.pcsc.watch(@fcn)` calls `fcn(w, evt)` at the device queue's drain points. `evt.Type`
  is "CardInserted", "CardRemoved", "ReaderAdded" or "ReaderRemoved".

Two choices are JGraph's own:

**A status the caller does not take is thrown.** `resp = transmit(c, apdu)` raises
`JGraph:pcsc:Status` unless the card answered 9000, naming the status and what ISO 7816-4 says it
means. `[resp, sw] = transmit(c, apdu)` never throws for a status. A script that forgets the status
therefore stops at the failed command, as `mkdir` and `copyfile` stop when their status is not taken.

**A wait that could never end is refused.** PC/SC makes a second connection wait while another
holds a transaction. A script has one thread, so two Card objects of one session on one reader
would wait forever. `transmit` and `beginTransaction` raise `JGraph:pcsc:Busy` instead.

The watch is a thread that lists the readers and waits on their states half a second at a time. It
needs no notification reader, and it keeps asking a machine whose service has not started yet.

### Printers: `jgraph.usb.printers`, `jgraph.usb.printraw`

`JGraph.Devices.Printing.WinPrinters` reads the spooler.

- `T = jgraph.usb.printers` lists every local and connected queue: Name, Port, Driver, Default,
  Status and Jobs. A queue whose port is a USB printer also has VendorID, ProductID, InstanceID, and
  the IEEE 1284 device ID with its Manufacturer, Model and CommandSet fields. The rest leave those
  empty. Every queue is listed, because a raw job is as useful to a network label printer.
- `job = jgraph.usb.printraw(printer, data, DocumentName=, OutputFile=)` sends bytes as one job the
  driver does not touch: ESC/POS, ZPL, PCL. The data is bytes, or text whose characters are the
  bytes. With OutputFile the spooler writes the job to that file instead of the port.

A USB printer is matched to its queue through the port usbmon gave it ("Base Name" and "Port Number"
under the interface's registry key). Its device ID is asked for with `IOCTL_USBPRINT_GET_1284_ID`
on a handle opened with no access rights.

A version 4 driver takes a raw job as datatype "XPS_PASS" and an older one as "RAW". This is the
rule of Microsoft's own sample for sending raw data.

### Network adapters: `jgraph.usb.netadapter`

`T = jgraph.usb.netadapter` lists the network adapters that are USB devices: Name, Description,
Kind, MACAddress, IPv4, IPv6, Status, Speed, VendorID, ProductID and InstanceID. With a row of
`jgraph.usb.devices`, an instance ID, or its filters, it keeps the adapters of the devices named.
Talking to the device is `tcpclient`'s and `udpport`'s.

Each adapter devnode's NetCfgInstanceId, from its driver key, finds the matching .NET
`NetworkInterface`. Kind is read from the device's descriptors: "ECM", "NCM", "EEM" or "RNDIS"
(any of the three class codes devices use for it), else empty for a vendor-driver adapter.

Only an adapter the USB bus itself enumerated counts. The first version walked every adapter up to a
USB ancestor and so listed the Bluetooth personal-area network under this laptop's USB Bluetooth
radio, which is not a USB network adapter.

### MTP and PTP: `jgraph.usb.mtplist`, `jgraph.usb.mtp`

`JGraph.Devices.Mtp` calls WPD's COM interfaces by vtable slot on raw pointers, as D11 calls Media
Foundation, so a device is released on the statement that deletes its object. Every slot, GUID and
property key was read from the Windows SDK headers (10.0.26100.0). Each call runs on a thread in the
multithreaded apartment.

- `T = jgraph.usb.mtplist` lists the devices: Name, Manufacturer, Description, DeviceID.
- `m = jgraph.usb.mtp(name)` opens one, by name, index or row; with no argument, the only one. Its
  properties are Name, Manufacturer, Model, SerialNumber, FirmwareVersion, Protocol, Type, and
  Storage (a table of Name, Capacity and Free).
- A path names an object by its storage and folders, parted by `/` or `\`:
  `"Internal storage/DCIM/Camera"`. A device has no paths, only objects with parents, so the path is
  walked one name at a time. A name matches exactly, else by the one object that differs only in case.
- `dir(m, path)` answers `dir`'s struct array (name, folder, date, bytes, isdir, datenum), or prints
  the names when no output is taken.
- `file = download(m, path, localFile)` copies a file off the device. The bytes land in a `.part`
  file first, so a failed transfer leaves no half file. A local file of that name is replaced, as
  `websave` replaces one.
- `path = upload(m, localFile, folder)` copies a local file into a folder. A name already there is
  refused.
- `mkdir(m, path)` makes the folder and the missing folders above it.
- `deleteObject(m, path)` deletes a file or an empty folder; `Recursive=true` deletes a folder with
  its contents. **This deletes for good: a device has no recycle bin.** A storage is never deleted.
- `capture(m)` asks a camera to take a picture (PTP's InitiateCapture). It answers nothing; the
  picture is a new object in the camera's storage.

### Simulators

- `jgraph.internal.pcscsim`: two readers and one T=1 card with a small application (SELECT, GET
  DATA, an echo, a PIN that blocks after three wrong tries). Connections share, exclude, lose the card
  and see it reset with PC/SC's own codes.
- `jgraph.internal.printsim`: a USB receipt printer with a 1284 device ID and a queue with no USB
  device; jobs are kept as sent.
- `jgraph.internal.mtpsim`: a phone with two storages and a PTP camera that takes pictures.

## Measured

- **Fixtures**, JGraph-only and written from the rule; all agree in both representations:
  - `pcsc_sim` (33 rows);
  - `mtp_sim` (26 rows);
  - `printers_sim` (15 rows), which also pins the shape of `jgraph.usb.netadapter` on the machine's
    own adapters.
- **Unit tests**: `tests/JGraph.Tests/Devices/LongTailTests.cs` adds 16. One caught a real defect:
  an HRESULT's facility was compared after sign extension, so no Win32 failure of a portable device
  was ever put in words.
- **On this machine's own backends**:
  - `jgraph.pcsc.readers` is an empty table with the service stopped, `connect` says no reader is
    attached, and a watch runs and stops with no event;
  - `jgraph.usb.printers` lists the five queues with the right default;
  - `jgraph.usb.mtplist` is an empty table;
  - `jgraph.usb.netadapter` is an empty table.
- **Live check, with the user's leave**: a 16-byte raw job to the "OrCADPS_25.1" queue with
  OutputFile. The spooler took it as job 2, the file held the 16 bytes exactly, and the queue was
  empty afterwards.

## Not measured

Nothing here could be run against the hardware it is for. Until it is, these are written from the
documentation and the headers alone:
- every winscard call past `SCardEstablishContext` and `SCardListReaders`: connect, transmit,
  control, status, transactions, reconnect, and the watch's state changes;
- every WPD call past creating the device manager and listing no devices: open, the object
  properties, the transfer streams, create, delete and the capture command;
- a USB printer's port name from the registry, its 1284 device ID, and a raw job on a version 4
  driver;
- a USB network adapter's row.

The user declined an MTP check on 2026-09-30 for want of a device to plug in.

## Live checks for the user

- **A phone** (unlocked, set to transfer files) **or a USB stick**, which Windows also lists as a
  portable device: `jgraph.usb.mtplist`, `m = jgraph.usb.mtp`, `m.Storage`, `dir(m)`,
  `dir(m, "<storage>/DCIM")`, then `download` of one file. Then, in a scratch folder only:
  `mkdir`, `upload`, `dir`, `deleteObject`.
- **A camera in PTP mode**: `capture(m)`, then `dir` of its DCIM folder.
- **A smart-card reader** (or the STM32 board's CCID firmware): `jgraph.pcsc.readers`, `connect`,
  a SELECT by `transmit`, and `jgraph.pcsc.watch` while a card goes in and out.
- **A USB receipt or label printer**: its row in `jgraph.usb.printers` (Port, VendorID, the 1284
  fields), then a short `printraw`.
- **The STM32 board's NCM firmware, or a USB Ethernet adapter**: its row in `jgraph.usb.netadapter`.

## Divergences

None: the names have no MATLAB counterpart.

## Still open

- **MTP listing is one request an object**, which is slow for a folder of thousands of pictures.
  WPD's bulk properties interface would do it in one (chips 35).
- **`capture` does not answer the new picture's path.** WPD reports it as an event, which needs a
  COM callback object (chips 35).
- **`download` takes one file**, not a folder (chips 35).
- **An object whose name holds a slash** cannot be named by a path.
- **`transmit` is raw**: it does not fetch the rest of a T=0 response (61xx) or resend with the
  right length (6Cxx).
- **`jgraph.usb.printraw` does not report the job's progress or its end.**
