# UI probes (app-building plan, U0)

Probes of MATLAB R2025b's app-building behaviour, recorded for the `uicontrol`/`uifigure`/App
Designer work. Every probe but `u0_dpi` runs headless:

```powershell
powershell -ExecutionPolicy Bypass -File .\u0\run-probe.ps1 -Name u0_layout   # writes u0\u0_layout.out
```

`run-probe.ps1` starts `matlab.exe -noFigureWindows -batch <name>` in the `u0` folder and stops only
the process it started if the probe overruns. **Keep `-noFigureWindows`.** Plain `-batch` displays
figure windows (MathWorks documents this), so a probe without the flag puts windows on the screen.

Recorded outputs sit beside their probes as `*.out.txt` (`research/` keeps the `.out` names it was
recorded with). Scratch paths in them are shortened to `<u0>`, `<research>` and `<scratch>`.

## Layout

| Folder | What |
|---|---|
| `research/matlab/` | 2026-10-03 inventory: `get()` of 69 component variants (`get/`), ~140 metaclass dumps (`meta/`), validation errors and coercions (`behave.txt`), toolbox usage counts. |
| `research/apps/` | The `.mlapp`/AppBase/GUIDE probes, the minimal `.mlapp` builders (`build_mlapp.py`, `build_dup.py`) and what they built (`built/`), the acceptance scripts `examples/ex1`–`ex5` + `SpinnerGauge.m`, and a hand-built GUIDE pair (`guide_pair/`). |
| `u0/` | The U0 probes below. MathWorks' own `.mlapp` files are read in place from `matlabroot` and never copied here. |
| `u1/` | The U1 probes (ADR 0198): headless `u1_*`, and the window session `u1w_*`. |

## U0 findings (R2025b, `-batch -noFigureWindows`)

**Gestures.** `matlab.uitest.TestCase` refuses invisible figures (`MATLAB:uiautomation:Driver:VisibleHierarchy`)
and does not drive `uicontrol` at all (`GestureNotSupportedForClass`). User gestures can only be
recorded with a visible window (`u0_uitest`).

**Classic dialogs are figures** (`u0_dialogs`). `msgbox`/`errordlg`/`warndlg`/`helpdlg` return a
`matlab.ui.Figure`: `Units` points, `Resize` off, `HandleVisibility` callback, `IntegerHandle` off,
`Tag` `Msgbox_<Title>`, an invisible full-size axes holding the message `text` (`Tag` MessageBox),
an `IconAxes` with a 32×32 image when there is an icon, and an `OKButton` pushbutton, all with
exact point positions. `'modal'` only changes `WindowStyle`. `waitbar`'s bar is a
`uiprogressindicator` (`matlab.ui.control.internal.ProgressIndicator`), **not a patch**, and its
message is the axes title. `dialog` is a modal figure.

**Blocking dialogs refuse without a display.** `questdlg`, `inputdlg`, `listdlg`, `uiconfirm`,
`uigetfile`, `uiputfile`, `uigetdir`, `uisetcolor` and `uisetfont` raise
`MATLAB:hg:NonInteractiveFunctionSupport` ("Creating dialog boxes that block execution is not
supported when MATLAB is in a configuration that is non-interactive or disables window display.").

**`uiwait` without a display** warns ("uiwait is not supported when MATLAB is started with the
-nodisplay or -noFigureWindows option…") and **still waits**: a timeout, a timer's `uiresume` or a
deleted figure ends it. A wait that nothing ends hangs.

**`uialert`/`uiprogressdlg` on an invisible figure** raise `MATLAB:uitools:uidialogs:InvisibleFigure`
("Figure handle 'Visible' value must be 'on'."). `focus` warns and returns.

**Units** (`u0_layout`). The root is in pixels of 1/96 inch (`ScreenPixelsPerInch` 96 at 100 %), with 1-based
origins: pixel x 20 is point 14.25 = (20−1)·0.75. A character is 5.6 × 15 px. Root `ScreenSize`
and `MonitorPositions` are the real display. A titled 200×150 panel's inner area is `[1 1 198 138]`;
untitled and borderless, `[1 1 200 150]`. `Extent` is fractional (text "a" in MS Sans Serif 8 is
10.667 × 22.667).

**At 125 % display scaling** (`u0_dpi`, the one probe run **with windows**, plain `-batch`, while
`winrect.ps1` read the real window rectangles). `ScreenSize` is `[1 1 1536 960]` on a 1920×1200
panel, and `ScreenPixelsPerInch` stays 96. A figure or uifigure of `Position` 400×300 has a
500×375-pixel client area (window DPI 120). So a MATLAB pixel is a device-independent 1/96 inch,
not a physical pixel. `getframe` and `exportapp` return 300×400 images, downsampled to MATLAB
pixels, while `print -r0` returns 375×500 physical pixels. With a display too, `print` refuses a
figure holding a `uicontrol` (`MATLAB:print:ExportappForPrintFigureWithUIControl`).

**Grid layout** (`u0_grid`). An invisible uifigure lays out asynchronously: positions change some
100–500 ms after `drawnow`. `'fit'` sizes are fractional browser text metrics (label 31.375×16.766,
button 45×23, edit field 123.27×22.77, …). Padding 10 gives a first cell at x 11. A child placed at
`Layout.Row` 5 in a 3-row grid grows the grid with `'1x'` rows. Auto-placement into a full grid adds
rows.

**Export** (`u0_export`). `exportgraphics` never includes UI components (warning "UI components will
not be included in the output. To include UI components, use the 'exportapp' function."), not even
panels, in a `figure` or a `uifigure`. `print` and `saveas` of a figure holding UI components raise
`MATLAB:print:ExportappForPrintFigureWithUIControl`. `exportapp` and `getframe` need a display
(`MATLAB:print:HeadlessFigureUnsupported`, `MATLAB:hg:NoDisplayNoFigureSupport`).

**`uihtml`** (`u0_uihtml`, `u0_uihtml2`). The page runs headless in MATLAB's bundled Chromium 128,
served from `https://127.0.0.1:<port>/static/<id>/<uuid>/`.
- MATLAB → page: `Data` goes through `jsonencode`. The page's `DataChanged` fires only when the JSON
  changes, so `[1 2 3]` then `[1;2;3]`, `[]` then `zeros(1,0)`, and `NaN` then `Inf` fire once.
  Complex values fail to encode (a warning; the page keeps its value). `datetime` arrives as its
  display text through `Data`, but as `{"Month":10,"Day":3,"Year":2026}` through
  `sendEventToHTMLSource`.
- Page → MATLAB: `Data` set in JavaScript goes through `jsondecode`, so arrays arrive as columns
  (`[1,2,3]` → 3×1). `sendEventToMATLAB` data arrive as rows (`[1,2,3]` → 1×3), `null` and no data as
  `[]`.
- `DataChangedFcn` runs only for page-side changes, with `PreviousData`
  (`matlab.ui.eventdata.DataChangedData`). Events carry `HTMLEventReceivedData`.
- `HTMLSource`: a path (relative or full) or markup. Any text that is not a file name, such as
  `hello world`, is markup. An `.html` name that does not exist and any URL raise
  `MATLAB:ui:HTML:invalidHTMLSource`.
- Sandbox: the page's folder and subfolders are served, nothing above. The server serves by type:
  `.js` and `.json` load, `.txt` is a 404. Network fetches from the page succeed.

**`.mlapp` code regions** (`u0_mlapp_map`). In 43 of the 44 shipped apps, `appModel.mat`'s `code`
maps onto the class text exactly: each `Callbacks(i).Code` is the body of `function Name(app, event)`
up to its `end`; `StartupCallback.Code` is the startup body and `InputParameters` its extra
arguments; `EditableSectionCode` is one contiguous run of lines (the user's properties and helper
methods). The exception is a Responsive app's generated `updateAppLayout`. Fields vary:
`InputParameters`, `SingletonMode` and `AppTypeData` are optional, and one app has no `Callbacks`.

**`.mlapp` save-back works** (`u0_rt_a`, `u0_rt_splice.py`, `u0_rt_c`). Edits to a callback, the
editable section and the startup body were written into `document.xml` and into an uncompressed
`code` element spliced among the original compressed elements. R2025b's `readAppCodeData`,
`readAppDesignerData`, App Designer's `MLAPPDeserializer.getAppData` and the class itself all see
the edits. **The MAT header's subsystem offset (bytes 116–123) must be moved with the element it
points to.** Leaving it stale makes App Designer's full load fail (`MATLAB:load:cantReadFile`) while
`readAppCodeData` still works.

## WebView2 spike (`webview2-spike/`, run with the user's leave at 125 %)

A throwaway WPF app, not part of the solution. It shows the same animated page in a
`WebView2CompositionControl` and in the `HwndHost` `WebView2`, each under a solid WPF panel standing
in for a `uialert`. It measures both and closes itself after about 40 s. `results.txt` and
`screenshot.png` are from the 2026-10-03 run (SDK 1.0.4258.31, runtime 154.0.4258.53).
- **Overlay.** The composition control's page sits under the panel. The `HwndHost` page paints over
  it.
- **Cost of the host process.** Animating at 60 fps: 14–18 % of one core for the composition
  control, against 1–2 %. A static page: about 3 % against 0.3 %.
- **Messages.** The round trip is about 0.2 ms median.
- **Capture.** `RenderTargetBitmap` of the window includes the composition page but not the
  `HwndHost` page. `CapturePreviewAsync` returns physical pixels.
- **Target framework.** The composition control needs the Windows SDK projection: on plain
  `net8.0-windows` it throws `FileNotFoundException` for `Microsoft.Windows.SDK.NET`. Hence the
  spike's `net8.0-windows10.0.17763.0`.

## U1 findings (R2025b)

`u1/run-probe.ps1` runs headless by default. `-WithWindows` runs plain `-batch`, which displays
figures. `-Interactive` runs `-nodesktop -r` with a `-logfile`: **`-batch` is non-interactive even
with a display**, so `questdlg`, `inputdlg` and `listdlg` refuse there. The `u1w_*` probes open
windows, and `u1w_keys` moves the mouse and types with `java.awt.Robot`, so they run only with the
user's leave.

**`uicontrol`, headless** (`u1_uicontrol`): defaults per style; words in any case or by a unique
prefix; `String` coercions (a number is its `num2str`, an array one line per element, a cell or a
string array a column cell, a logical refused); colours as doubles, names and hex codes; R2025b's
identifiers and sentences for every refusal. Refusals through `set` start "Error setting property
'X' of class 'UIControl':"; inside the creating call they do not. Every graphics callback takes a
handle, text or a cell. Cells are stored as columns, and `{@f}` is stored as `@f`.

**Event data** (`u1_eventdata`, `u1w_keys`): `DeleteFcn` gets `event.EventData`
(`ObjectBeingDestroyed`); `CloseRequestFcn` a `WindowCloseRequestData` (`Close`); a `uicontrol`
`Callback` an `ActionData` (`Action`). Keys get a `KeyData` (`KeyPress`, `WindowKeyPress`, …), and a
component's own key callbacks a `UIClientComponentKeyEvent`. Window buttons get a `WindowMouseData`
(`WindowMousePress`/`Release`), a figure's own `ButtonDownFcn` a `MouseData` (no
`IntersectionPoint`), and the wheel a `ScrollWheelData`.

**`CloseRequestFcn`** (`u1_closereq`): `''`, `[]`, `{}` and `""` are all stored as `''`, which
keeps the figure open; `'closereq'` closes it.

**Keys and clicks in a classic figure** (`u1w_keys`):
- A press reaches `WindowKeyPressFcn` first, then the holder's `KeyPressFcn`; a release goes the
  other way. A component holding the keyboard keeps the figure's `KeyPressFcn` from running.
- An edit field's `Callback` runs on Enter, after its `KeyPressFcn`. It also runs on a click
  elsewhere after typing, before that click's own callback.
- A click on a `uicontrol` does not run `WindowButtonDownFcn`.

**uifigure gestures** (`u1w_uitest`): a click on a component does run `WindowButtonDownFcn`.
`ValueChangingFcn` runs with the old `Value` still in place, and `ValueChangedData` carries
`PreviousValue` (a drop-down's also carries `ValueIndex` and `Edited`). `type` refuses a figure.

**Dialog layouts** (`u1w_dialogs`, for U4): `questdlg` buttons `Btn1`.. are 56×31.73 px, 10 px
apart; `inputdlg` buttons are `OK`/`Cancel`, 53 px wide, with `Edit` fields 26.67 px tall;
`listdlg` has a 160×300 `listbox` and `ok_btn`/`cancel_btn` 76×22, plus `selectall_btn` in
multiple mode. All are modal pixel figures. `questdlg` errors (`MATLAB:hg:DeletedObject`) when its
figure is deleted from outside.
