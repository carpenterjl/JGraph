# ADR 0237 — an array of device objects as MATLAB's object array: open item 36

## Status

Accepted, 2026-10-16. The twenty-fourth batch of the open-items work
(`docs/plans/open_task_chips_09_12_2026.md`, gitignored): item 36, found when the device classes
plan's stage D13 stress script saw `numel(serialportfind)` answer 1 with two ports open (ADR 0197).
The recordings are `probe_dev_arrays` (ADR 0197, the com0com pair, 2026-09-30) and the new
`probe_dev_arrays2` and `probe_dev_arrays3` (2026-10-16, headless on the same pair and the audio
constructors): the display text, the generic verbs and methods on a row, eq at its edges, growth,
member and foreign writes, conversions, what holds the ports open, and which classes join at all.

## Context

R2025b's device objects are ordinary handle-object arrays: `[a b]` is 1-by-2, subscripts, loops,
comma lists, `get`/`set` and the rearranging verbs all work, and an array holds its objects open.
JGraph had only the row `serialportfind` and `tcpclientfind` answer for several matches
(`DeviceArray`), which indexed by one whole number and answered the questions about its shape
(stage D13); brackets refused with `MATLAB:class:concatenationScalar`, nothing else indexed it, a
loop did not walk it, and the lifetime count (`JgsLifetime`, ADR 0171) did not see it, so
`f = serialportfind; clear a b` closed both ports where R2025b keeps them open.

## Decision

`DeviceArray` is now a real two-dimensional `IJgsExternalArray` (stage D10b's machinery, which
runs every subscript, growth, deletion, loop and comma list through a numeric proxy), with handle
elements and its `DeviceClass` kept beside them so an empty slice still answers `class` and `isa`:

- **A scalar is a 1-by-1 array.** `a([1 1])` is 1-by-2 of the same handle, `a'`, `a(:)` and
  `a(true)` are the scalar; `repmat`, `cat`, `reshape`, `flip`, `fliplr`, `flipud`, `horzcat` and
  `vertcat` take one device object as the array it is. A one-element answer is unwrapped to the
  `DeviceObject` itself (`ExternalArrays.Value`), so the scalar machinery — methods, callbacks,
  `get`/`set` — never sees a wrapper. A midimsg 1-by-1 stays an array, as a value class's always
  was.
- **The brackets join by class.** A bracket with a device piece joins as that piece's class; a
  foreign piece converts through the class's constructor called with the piece, and the join
  quotes the constructor's own refusal under `MATLAB:UnableToConvert` ("The following error
  occurred converting from double to internal.Serialport: …"). An indexed **assignment** refuses
  flat — "value of type 'tcpclient' is not convertible to 'internal.Serialport'" — without running
  the constructor (`CoerceForStore`). tcpclient now validates its ADDRESS argument before counting
  its arguments, which is the order the two recordings pin (`tcpclient("x")` wants more arguments,
  `tcpclient(u1)` names the udpport's class).
- **Growth fills a gap with the class's default object.** serialport's is `serialport()` — the
  saved settings, or `serialport:serialport:NoSavedPreferences` when there are none; every other
  class refuses with R2025b's `MATLAB:class:MissingGetDefaultScalarElement`, the udpport classes
  naming `udpport.UDPPortBase`. A member write past the end (`g(3).Timeout = 5`) grows the same
  way; a member write through one element writes the shared handle, so `g(2).Timeout = 7` is seen
  through `f`.
- **`==` broadcasts identity.** Element against element with a scalar broadcast; a number, text or
  foreign class is simply never equal (`[false false]`, not an error); mismatched shapes are
  `MATLAB:dimagree`; an empty side answers the empty logical.
- **The generic verbs answer the row.** `get(f)` is a struct an element, N-by-1; `get(f, name)` a
  cell, elements down and names across; `set` writes every element; `isvalid` is a logical of the
  array's shape; `delete(f)` deletes every element and refuses an output with `MATLAB:maxlhs`;
  `properties`, `methods` and `isprop` (one logical an element) answer from the class; the numeric
  conversions refuse with `MATLAB:invalidConversion`. `arrayfun` runs over the elements, and a
  uniform result of string scalars now collects into a string array, as R2025b collects the Ports.
- **The lifetime count holds each element.** Binding the array retains every element once;
  releasing it releases them, so `f = serialportfind; clear a b` keeps both ports open and
  `clear f` closes them before the next statement; a cell or container holding the array holds its
  elements through the ordinary scan roads (`MayTrack`, `Retain`/`Release`, `AnyHandleChild`).
- **The display** is R2025b's shape: `1×2 Serialport array with properties:` and each element's
  display-property block; an empty array lists the property names; a deleted element shows the
  bare line `Invalid or deleted object.`; an all-deleted array is `1x2 Invalid or deleted object`
  (ASCII x, as R2025b writes that one line).
- **Which classes join.** Every device class joins by MATLAB's default-for-handles rule except
  those that override their brackets away: audioplayer and audiorecorder keep their
  `noconcatenation` refusals on `[p q]`, `horzcat` and `vertcat` — yet form arrays through
  `repmat`, `cat` and indexing, exactly as R2025b does (probe_dev_arrays3).
- **The JGS dialect is frozen**: its row still indexes by one whole number and its brackets still
  refuse; the MATLAB-dialect roads are gated where they were.

Fixture `dev_arrays` (107 rows, 4 divergences stamped) runs the recorded corpus on the simulated
ports in JGraph and the com0com pair in R2025b, the preferences cleared so the growth rows are
deterministic; `DeviceArrayTests` (12 tests) covers the same ground in the suite.

## Consequences

- `DeviceArray` implements `IJgsExternalArray`; `ExternalArrays.Value` unwraps one-element device
  answers; `ExternalArrays.Flipped` serves flip/fliplr/flipud for every external array.
- `JoinExternalArrays` triggers on a device piece (skipping the classes whose brackets refuse);
  `ExternalArrayWrite` and the member-write roads take a scalar device holder as a 1-by-1 array.
- `JgsLifetime` counts a device array's elements wherever it counted a device object.
- `JgsBuiltins.DeviceOperator` broadcasts `==`/`~=`; the device check runs before the midi road.
- `TryDeviceBuiltin` answers `get`/`set`/`delete`/`isvalid` for arrays; `TryDeviceMethod` refuses
  a class method on an array in one voice; `DeviceClass` carries `Constructor`, `GapFill`,
  `DefaultElementOwner` and the serialport, udpport, tcpserver and visadev declarations name them.
- `CollectResults` (cellfun/arrayfun) gathers uniform string scalars into a string array in the
  MATLAB dialect.

## Divergences

- A class method called on an array (`flush(f)`, `configureTerminator(f, …)`) errors in R2025b
  through each method's own argument validation, quoting internal types
  (`internal.SerialportClient`); JGraph refuses in one voice,
  `JGraph:deviceArray:ScalarMethod` (fixture rows flush_row, cfg_row).
- The full array display keeps JGraph's flat property layout under R2025b's aligned columns
  (ADR 0174; rows disp_full, disp_mixed). The headers, the empty array's property list and the
  deleted displays match exactly.
- visadev, bluetooth, ble and the other hardware-bound classes could not be probed (no instrument
  on this machine); they join by MATLAB's default-for-handles rule, assumed to behave as
  serialport's family does. mididevice and midicontrols are likewise unmeasured.
- `string(midimsg)` and `cellstr(midimsg)` now refuse with `MATLAB:invalidConversion` like the
  other conversions; R2025b's exact words for those two are unmeasured.
- A deleted-and-live mixed array's `isvalid` and indexing follow the recording; what `get(f)`
  answers over a part-deleted row is unmeasured (JGraph refuses through the deleted element's
  `LiveOrThrow`).
