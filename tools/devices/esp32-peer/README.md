# esp32-peer

The Bluetooth peer for the device fixtures (device classes plan, stage D3). It is the board-side twin
of `jgraph.internal.btsim` (`src/JGraph.Devices/Simulation/SimulatedBluetooth.cs`). Fixtures are
recorded in R2025b against the board and replayed in JGraph against the simulator.

## Flash it

1. **Board:** an original ESP32. The S2, S3, C3 and C6 have no Bluetooth classic.
2. **IDE:** Arduino IDE 2 with the Espressif board package (Arduino-ESP32 2.x or 3.x). Select:
   - Board: `ESP32 Dev Module`;
   - Partition Scheme: `Huge APP (3MB No OTA/1MB SPIFFS)`, because the classic and LE stacks together
     do not fit the default;
   - Port: the board's USB-UART bridge.
3. **Upload:** upload `esp32-peer.ino`. The serial monitor at 115200 prints
   `JGraphPeer ready: SPP and BLE advertising`.
4. **Pair:** in Windows, Settings → Bluetooth & devices → Add device → Bluetooth → `JGraphPeer`.
   Pairing is for the classic side; BLE needs none.

## What it offers

- **Classic SPP.** The in-band protocol of `PeerEngine`:
  - `send`, `later`, `chunks`, `echo`, `recv`, `on` and `reset`;
  - `status` answers `00000000`, because an RFCOMM channel has no modem pins;
  - an unknown command answers `?`.
- **BLE**, in this order:
  - `180F` Battery Level `2A19` (Read, Notify), holding 100;
  - `180D` Heart Rate Measurement `2A37` (Notify), sending `[0 60+k]` every 100 ms while subscribed;
    Body Sensor Location `2A38` (Read), holding 1;
  - `FFE0`/`FFE1` (Read, WriteWithoutResponse, Write, Notify), with `2901` "JGraph echo" and
    `2902`. A write becomes the value and is notified back.

## The live round

The GATT tree Windows reports for the board has not yet been seen. Bluedroid adds its own Generic
Access (`1800`) and Generic Attribute (`1801`) services, and Windows may list them. The same goes for
the advertisement's fields.

The first recording against the board settles it. Reconcile the simulator with what R2025b records:
- `Services()`, `Characteristics()` and `Descriptors()` in `SimulatedBluetooth`;
- the advertisement `Scan` returns.

Then run the pending fixtures in `pending-fixtures/`: move each into
`tests/JGraph.Tests/MatlabParity/fixtures/`, record it with `tools/parity/record-matlab.ps1`, and run
it in JGraph.
