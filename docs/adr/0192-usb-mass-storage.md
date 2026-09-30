# ADR 0192 — USB mass storage, as jgraph.usb.storage and jgraph.usb.eject

## Status

Accepted. Stage D9 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`), after ADR 0191 (f73b20a). One commit,
this ADR's.

The stage adds, under `jgraph.usb`:
- `storage`: which USB disks there are, and what Windows mounted from them;
- `eject`: "Safely Remove Hardware" from a script.

Both are JGraph extensions with no MATLAB counterpart. The stage also makes a bare package function
answer several outputs.

## Context

A USB stick, card reader, external drive, or the STM32 board's RAM disk is a USB device, driven by
USBSTOR or UASPStor, with a disk below it and volumes on the disk. A script wants to know:
- which drive letter the board came up as;
- how big and how full it is;
- when it can be pulled.

All of it is in the storage stack's IOCTLs, and none of them needs administrator rights or reads the
media. The plan's open question 5 (raw sectors) took its default: no raw sector access.

## Decision

`JGraph.Devices.Storage.UsbStorage` walks every disk interface up to its USB ancestor, as D5 walks COM
ports and HID collections. A disk not under USB is never opened. Each USB disk is opened with no
access rights and asked for:
- its number (`IOCTL_STORAGE_GET_DEVICE_NUMBER`);
- its SCSI identity and bus (`IOCTL_STORAGE_QUERY_PROPERTY`, StorageDeviceProperty);
- its size and sector (`IOCTL_DISK_GET_DRIVE_GEOMETRY_EX`). This is FILE_ANY_ACCESS, unlike
  IOCTL_DISK_GET_LENGTH_INFO; an empty card reader answers 0.

Volumes are enumerated only when there is a USB disk. Each volume's extents
(`IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`) file it under its disks, and it carries its mount points, label,
file system, capacity and free space. A thread error mode keeps an empty card reader from raising the
"insert a disk" box.

`[disks, volumes] = jgraph.usb.storage(filters)` answers two tables:
- **disks**: DiskNumber, VendorID, ProductID, SCSI Vendor, Product and Revision, SerialNumber,
  BusType, Removable, Size, BytesPerSector, Drives, Location and InstanceID. The serial number is the
  USB device's, else the SCSI one;
- **volumes**: DiskNumber, Drive, Label, FileSystem, Capacity, Free and VolumeName.

jgraph.usb.devices' filters apply to the USB device.

`jgraph.usb.eject(x)` takes a drive ("E:"), a row of either table, or an instance ID. It calls
`CM_Request_Device_Eject` on the USB device, with a veto buffer, so Windows answers instead of showing
its box. A veto is `JGraph:usb:EjectVetoed`, carrying:
- the PNP_VETO_TYPE's reason ("a file or handle is open on it");
- what Windows names as holding the device.

A drive that is not on a USB disk is `JGraph:usb:NotUsbDrive`.

**A bare package function answers several outputs.** Before this stage, `[a, b] = f` was a call for a
bare name that auto-calls (`[x, y, z] = sphere`), but `[disks, volumes] = jgraph.usb.storage` read the
dotted name as a comma list and fell short.

`EvaluateForOutputs` now treats a dotted chain of literal names the same way when:
- every link but the last is a scalar struct;
- the last is an auto-calling builtin with several outputs.

The chain is walked field by field, so nothing on the way is called or evaluated twice. A struct
array, a field that holds data, an object and a class name all take the old path.

## Measured

- **Fixture**: `storage_smoke` (9 rows), JGraph-only and written from the rule. It answers the same on
  any machine. It covers:
  - both tables' columns, and an empty filter's shape (logical Removable);
  - the bare two-output form;
  - eject's refusals (no argument, a number, a drive not on USB, a device not there).
  Nothing is ejected.
- **Unit tests**: `tests/JGraph.Tests/Devices/StorageTests.cs` adds 5:
  - STORAGE_DEVICE_DESCRIPTOR and DISK_GEOMETRY_EX decoded from built buffers;
  - the veto reasons;
  - a walk of this machine's USB disks (none here);
  - ejecting a missing device.
- The tests on multiple outputs and bare calls, and the whole parity fixture suite, pass after the
  interpreter change.

## Live checks for the user

- A USB stick: `[d, v] = jgraph.usb.storage` shows its letter, label, file system and sizes. Then:
  - `jgraph.usb.eject("E:")` with a file open on it names the veto;
  - with nothing open, the stick is ejected.
- The STM32 board's TinyUSB RAM disk: the same, with Removable true and the board's IDs.

## Divergences

None: the names have no MATLAB counterpart.

## Still open

- **Raw sectors**: declined by default (open question 5). They would need administrator rights and a
  locked volume.
- **Arrival of a volume**: jgraph.usb.watch reports the USB device, not the moment its volume mounts.
  A script that waits for a drive letter polls storage.
