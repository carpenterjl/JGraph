# ADR 0193 — Audio devices: audiodevinfo, audioplayer, audiorecorder, sound and soundsc

## Status

Accepted. Stage D10 of the device classes plan
(`docs/plans/serialport-and-usb-device-classes-plan.md`), after ADR 0192 (c45f5c9). One commit,
this ADR's. The plan's MIDI half of the stage (open question 9) follows as its own stage, D10b.

## Context

R2025b's audio functions are readable MATLAB:
- `audiodevinfo.m` over `audiovideo.internal.audiodevinfoDesktop`;
- `audioplayer.m` over `Iaudioplayer` and `audioplayerDesktop`;
- `audiorecorder.m` over `Iaudiorecorder` and `audiorecorderDesktop`;
- `sound.m`, `soundsc.m` and `audiodevreset.m`.

Under them sits an asyncio channel with a PortAudio device plugin. Four probes under
`tools/matlab-checklist/device-probes` measured the rest:
- `probe_audio_env`: the device list and the objects' shapes;
- `probe_audio_player`: validation, callbacks and timing, playing zeros only;
- `probe_audio_recorder`: every refusal that comes before a recording starts;
- `probe_audio_names`: how the classdef attributes read names.

What they show:
- **Devices.** R2025b uses PortAudio's DirectSound host alone
  (`DeviceInfo.getDevicesForDefaultHostApi`). It numbers DirectSound's capture list, then its
  playback list, from 0, each list led by its primary driver ("Primary Sound Capture Driver",
  "Primary Sound Driver"), and adds " (Windows DirectSound)" to each name. On this laptop that is
  inputs 0–2 and outputs 3–5. A DeviceID of −1 means the primary driver. A fractional ID is cut
  to its whole part.
- **Callbacks.** `internal.Callback.execute` calls a handle as `fcn(obj, [])`, evaluates text in
  the base workspace, and calls `{fcn, a}` as `fcn(obj, [], a)`.
  - StartFcn runs before the channel opens, so Running still reads `'off'`, and an error in it
    stops `play`.
  - TimerFcn runs every TimerPeriod once the device has started.
  - StopFcn runs when the channel closes: on pause, on stop, and when the last buffer has played.
- **CurrentSample** is the channel's count of samples sent from StartIndex, or 1 once none are left.
  A playback is padded to whole 25 ms buffers, and a whole number of buffers gets one more.
- **Names.** `CaseInsensitiveProperties` and `TruncatedProperties` let a dot, `get` and `set` take
  any case and any prefix that names one property, hidden ones counted. A prefix naming several is
  "Ambiguous audioplayer property". Method names stay exact.

## Decision

### The device layer (JGraph.Devices.Audio)

`WasapiAudio` names the devices as R2025b does:
- `DirectSoundCaptureEnumerateW`, then `DirectSoundEnumerateW`;
- each device mapped to its Core Audio endpoint through `PKEY_AudioEndpoint_GUID`;
- the primary drivers following the default console endpoint;
- a DirectSound device with no active endpoint left out.

The streams are WASAPI in shared mode, 32-bit float at the rate asked, with the audio engine
converting (`AUTOCONVERTPCM` with its default-quality resampler). Each stream runs on an MTA thread
of its own. An output plays one block and reports the frames played. An input hands blocks to a
callback. `audiodevreset` forgets the cached list.

`SimulatedAudio`, behind `jgraph.internal.audiosim('on')`, has:
- two inputs, whose microphone gives a 440 Hz sine of amplitude 0.5 from sample 0 in real time;
- two outputs, which take frames at the rate asked and keep the last block played
  (`audiosim('played')`).

The IDs keep the kinds the fixtures name: 0 an input, 3 an output.

### The script surface

- **`audiodevinfo`** is transcribed from `audiodevinfoDesktop.m`: all devices, a count, a name, an
  ID found by a case-sensitive part of a name, the driver version, and finding or testing a
  device's support for a rate, bits and channels. The support test builds the recorder or player,
  then opens and closes its stream. R2025b records or plays for a moment; JGraph keeps nothing.
- **`audioplayer`** follows `Iaudioplayer` and `audioplayerDesktop` line by line:
  - the constructor's order of checks, and every setter's check and sentence;
  - int8 kept as uint8, a row turned into a column, `size(y, 2)` channels;
  - play with a selection, and its "invalid selection" warning;
  - playblocking's wait, which runs callbacks as a pause does;
  - pause, resume and stop, and changing SampleRate while playing;
  - CurrentSample as above, and the callbacks through the device queue's drain points.
- **`audiorecorder`** follows `Iaudiorecorder` and `audiorecorderDesktop`:
  - samples kept at the recorder's bits, uint8 or int16 or double, as the channel keeps them;
  - `record(r, t)` counting SamplesToRead and stopping itself when they have arrived;
  - pause and resume keeping what came, and stop starting the next recording afresh;
  - TotalSamples and CurrentSample as the desktop recorder counts them;
  - `getaudiodata` in double, single, int16, uint8 or int8;
  - `getplayer` and `play`.
- **`sound`** and **`soundsc`** are transcribed from their files. `sound` keeps its players in the
  session, as `sound.m` keeps them in a persistent list, and prunes the finished ones at the next
  call. On a machine that is not Windows, `sound` stays the host's.

A device class can now declare **loose names** (`DeviceClass.LooseNames`) and its own
**concatenation refusal** (`ConcatenationRefusal`). R2025b's "Audioplayer objects cannot be
concatenated" comes from the second.

**`[p]` is `p`.** A bracket around one object is that object in the MATLAB dialect. Before, it came
back as a double for every device and .NET object.

## Measured

Three fixtures are recorded from R2025b:
- **`audio_devices`** (37 rows), on this machine's devices in both engines. Its names are this
  machine's, so it agrees only where the same devices are.
- **`audio_player`** (101 rows). R2025b plays zeros on the real output and JGraph plays on the
  simulator (helper `audio_env`). Rows that depend on how fast a device starts (TimerFcn counts,
  the sample reached after a given pause) are left out.
- **`audio_recorder`** (54 rows), where nothing records.

One more fixture is JGraph-only:
- **`audio_sim`** (21 rows), written from the rule on the simulator: recording, the conversions,
  pause and resume, callbacks, and the samples `audioplayer`, `sound` and `soundsc` play.

All four agree in both representations. `tests/JGraph.Tests/Devices/AudioTests.cs` adds 4 unit
tests:
- the machine's list in R2025b's order;
- 0.1 s of zeros played on the primary output;
- the simulated output's timing and what it keeps;
- the simulated microphone's sine.

On this laptop JGraph's `audiodevinfo` matches R2025b's list line for line. A silent playback's
callbacks come in R2025b's order: start with Running `'off'`, five TimerFcn calls over half a
second at 0.1 s, then stop with Running `'off'`.

## Live checks for the user

- `recordblocking(audiorecorder(44100, 16, 1), 3)` from the laptop's microphone array, then `play`.
- `sound` of a tone at a comfortable volume, on the speakers and on a USB headset (the STM32
  board's UAC2 interface, when its firmware has one).

## Divergences

- **A callback's first argument is the audioplayer or audiorecorder itself.** R2025b hands its
  internal implementation object, `audiovideo.internal.audioplayerDesktop` or
  `audiovideo.internal.audiorecorderDesktop`. Its properties read the same, and `class` does not.
- **An unknown function called on an audio object** gets JGraph's "is not recognized" sentence,
  where R2025b names the argument's class ("Undefined function 'ISPLAYING' for input arguments of
  type 'audioplayer'.") (`audio_player`, `method_upper`, div=ADR0193).
- **The support test** of `audiodevinfo(io, [id,] rate, bits, channels)` opens and closes a stream
  rather than recording or playing for a moment.
- **The objects display in JGraph's layout**, as every device object does since ADR 0174.

## Still open

- **MIDI** (`mididevinfo`, `mididevice`, `midimsg`, `midisend`, `midireceive`, `midicallback`,
  Audio Toolbox): stage D10b.
- **Exclusive mode** and `audioDeviceReader` or `audioDeviceWriter` (Audio Toolbox) are out, as the
  plan's defaults put them.
- An anonymous function called for no output asks its body for none. R2025b asks for one when the
  body's function declares an output: `f = @() audiodevinfo(1,2,3,4,5,6); f()` fails in R2025b.
  The fixture asks for the output explicitly. This is chips entry 28.
