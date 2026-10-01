# ADR 0195 — Cameras: webcamlist, webcam, snapshot and preview over Media Foundation

## Status

Accepted. Stage D11 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`), after ADR 0194 (922134d). One commit, this
ADR's.

## Context

`webcam` is not part of MATLAB: it comes with the MATLAB Support Package for USB Webcams. R2025b on
the recording machine does not have the package, and `probe_video_env` shows what that leaves:
- `webcamlist` and `webcam` are "Unrecognized function or variable".
- The install holds no source and no message catalog for them. It holds only Hardware Manager's
  pointer to the package, and `webcammfenumerator.dll`, which says that R2025b lists cameras on
  Windows through Media Foundation.
- Image Acquisition Toolbox is licensed, but no adaptor is installed: `imaqhwinfo` warns "No Image
  Acquisition adaptors found", and `videoinput('winvideo')` is refused.

So nothing about `webcam` can be recorded from R2025b here. This stage is written from the
package's documentation: its functions, its property names and what each does. Every refusal's
wording, the order of the device-specific properties and the display are JGraph's own until the
package is installed and a fixture is recorded (chips file).

`videoinput` and `imaqhwinfo` stay out, as the plan's open question 7 was answered.

## Decision

### The device layer (JGraph.Devices.Video)

**`MediaFoundationCameras`** lists the video capture sources `MFEnumDeviceSources` answers, by their
friendly names, in its order. Listing opens no camera.

**`MediaFoundationCamera`** is one open camera:
- It is a source reader over the device source, asked for 32-bit RGB. The reader's video processor
  converts the camera's own format (YUY2, NV12, MJPG).
- A capture thread reads samples from the moment the camera is opened until it is closed, and keeps
  the newest. A picture is copied out of a sample only when someone asks for one.
- **Resolutions** are the frame sizes of the camera's native media types, each once, in the
  camera's order. The first is the size of the type the camera starts in. Setting one picks the
  native type of that size with the highest frame rate; the capture thread makes the change between
  two samples, and puts the old size back if the camera refuses the new one.
- **Controls** are DirectShow's `IAMVideoProcAmp` and `IAMCameraControl`, which a capture source
  answers to: Brightness, Contrast, Hue, Saturation, Sharpness, Gamma, ColorEnable, WhiteBalance,
  BacklightCompensation and Gain; Pan, Tilt, Roll, Zoom, Exposure, Iris and Focus. A camera has the
  ones whose range and value it answers.
- **Closing** stops the thread, releases the reader and shuts the source down, on the statement that
  deletes the object. A camera that has stopped sending is shut down under the blocked read.

The COM interfaces are called through their vtable slots on raw pointers, not through runtime-callable
wrappers, so that release is exact. Every slot number and GUID was read from the Windows SDK headers
(10.0.26100.0): mfobjects.h, mfidl.h, mfreadwrite.h and strmif.h.

**`SimulatedCameras`**, behind `jgraph.internal.camsim('on')`:
- "JGraph Test Camera": four resolutions; Brightness, Contrast and Zoom; Exposure, Focus and
  WhiteBalance, which it can drive itself.
- "JGraph Second Camera": one resolution and a Brightness.
- A frame is a known picture: red rises left to right, green top to bottom, blue steps by eight a
  frame, and Brightness away from its default lifts or lowers all three.
- `camsim('unplug', name)` and `camsim('plug', name)` take a camera away and bring it back;
  `camsim('open')` counts the cameras held open.

### The script surface

- **`webcamlist`** answers the names as a cell column (0-by-0 when there is none).
- **`webcam`**, `webcam(index)`, `webcam(name)`, each followed by name-value pairs:
  - an index counts from 1 in `webcamlist`'s order;
  - a name matches in any case, or as the one camera whose name contains it (`webcam('Logitech')`);
  - one camera has one object at a time: a second is refused until the first is cleared;
  - a pair sets a property as an assignment would, and a refused pair leaves the camera free.
- **Properties.** `Name` and `AvailableResolutions` (a 1-by-N cell) are read-only. `Resolution` is
  text such as `'640x480'`, one of the camera's. After them come the camera's controls in
  alphabetical order, each a whole number in its range, and each control the camera can drive
  followed by its `…Mode`, `'auto'` or `'manual'`.
- **A control under `'auto'`** refuses a value and says to set its Mode to `'manual'` first.
- **`[img, time] = snapshot(cam)`** waits for the next frame to arrive (10 s at most) and answers it
  as height-by-width-by-3 `uint8`, with the time it arrived as a `datetime`. The array is plain, as
  `getframe`'s `cdata` is, so arithmetic on it works.
- **`preview(cam)`** opens a figure of its own and shows the camera's frames in it;
  **`closePreview(cam)`** closes it.
  - The figure is numbered from 1001, so `figure(1)` and a bare `figure` never pick it.
  - It is never left as the current figure: the figure a script was drawing into stays current, and
    with none open the next plot opens figure 1.
  - Frames reach it on the script thread at the points where callbacks run, at most 25 a second.
  - Closing its window ends the preview; deleting the camera closes it.
- **The camera streams** from the object's creation to its deletion, and its light is on for that
  time. `clear cam` releases it at once (ADR 0171).
- `get`, `set`, `properties`, `methods`, `isvalid` and `delete` answer as for the other device objects.

`JG.CurrentFigureNumberOrZero` and `JG.ClearCurrentFigure` are new on the figure facade, for a verb
that opens a figure for its own use.

## Measured

**Live check (2026-09-30), with the user's leave.** The laptop's camera, "USB2.0 HD UVC WebCam"
(13D3:56EB), run through `webcam` in the CLI for about twenty seconds:
- it opened in 1.2 s at 1280x720, the size it starts in;
- it lists six resolutions, and `snapshot` answered each at its own size;
- it has twelve control properties: BacklightCompensation, Brightness, Contrast, Exposure, Gain,
  Gamma, Hue, Saturation and Sharpness, WhiteBalance, and the Modes of Exposure and WhiteBalance,
  both `'auto'`;
- `snapshot` answered a 720-by-1280-by-3 `uint8` in about 70 ms, 30 of them in 2 s;
- Brightness took the value it already had;
- `clear cam` released it in 0.4 s, and it opened again at once.

One snapshot was written to a file. The user opened it and found it upright with natural colours. No
other picture was kept, and no test or fixture opens a real camera.


**`webcam_sim`** (34 rows) is JGraph-only, on the simulated cameras, written from the rule. It covers
the list, each way to pick a camera, name-value pairs, one object a camera, snapshot's picture, frame
count, time and argument checks, resolutions, control values and their refusals, the modes, `get` and
`set`, deletion and `clear`, an unplugged camera, no cameras at all, and the preview's figure. It
agrees in both representations.

`tests/JGraph.Tests/Devices/WebcamTests.cs` adds 6 unit tests:
- the machine's list, read without opening a camera;
- the simulated camera's picture, resolutions and controls;
- an unplugged camera;
- frame events starting and stopping with their listener;
- a frame as snapshot's array and as an image plot's pixels.

## Live checks for the user

- In the app: `cam = webcam; preview(cam)` shows live video in its own window while the session is
  idle; `closePreview(cam)`; `clear cam` turns the camera's light off.
- The STM32 board's UVC interface, when its firmware has one: it lists, opens, and `snapshot` answers
  its test pattern.
- Once the USB Webcams support package is installed in R2025b: record `webcam_objects` and reconcile
  (chips file).

## Unmeasured

Nothing about `webcam` could be measured, so none of these is a known difference from R2025b. They
are the choices made without it to check against, for chips entry 32 to settle:
- every refusal's identifier and wording (`MATLAB:webcam:…`);
- the device-specific properties listed alphabetically;
- a control under `'auto'` refusing a value rather than switching to manual;
- a control's value being a whole number in range, which the camera rounds to its own step;
- a name matching in any case and by any part;
- `get` and `set` working on a webcam;
- the camera streaming for the object's whole life;
- the display in JGraph's layout (ADR 0174), every property listed.

## Divergences

Differences from the package's documented behaviour:
- **The preview is a figure**, so `findobj('Type', 'figure')` counts it and `close all` closes it.
  R2025b's is a window of its own.
- **The preview holds still while a script computes** and moves at `pause`, `drawnow` and when idle.
  R2025b's keeps moving (chips 33).
- **`preview(cam, image)` is not provided**: the form that draws into an image object the caller gives.
- **`snapshot(5)` is "not recognized"** rather than "Undefined function … for input arguments of type
  'double'", as for every device method.

## Still open

- `videoinput` and `imaqhwinfo` (Image Acquisition Toolbox), declined in the plan.
- IP cameras (`ipcam`, another support package).
- Recording video from a camera to a file; `VideoWriter` takes snapshots a frame at a time.
