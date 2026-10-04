# UI probes (app-building plan, U0)

Probes of MATLAB R2025b's app-building behaviour, recorded for the `uicontrol`/`uifigure`/App
Designer work. Every probe runs headless:

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
