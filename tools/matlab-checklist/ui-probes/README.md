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
| `u2/` | The U2 probes (ADR 0199): headless `u2_*`, and the window session `u2w_resize`. |
| `u3/` | The U3 probes (ADR 0200): headless `u3_*`, and the window session `u3w_clicks` with `fgtitle.ps1`, its foreground check. |
| `u4/` | The U4 probes (ADR 0201): headless `u4_*`. |
| `u5/` | The U5 probes (ADR 0202): headless `u5_*`, with their helpers `tryp`, `v2s`, `oneline` and `u5_makers`. |
| `u7/` | The U7 probes (ADR 0204): headless `u7_*`, the class table generator, and `mlapp/`, which builds the text-only `.mlapp` fixtures. |
| `u7b/` | The U7b probes (ADR 0205): headless `u7b_*`. `u7b_build` writes the fixture app `U7bApp.mlapp` with R2025b's own serializer; `u7b_verify` reads what JGraph saved. `rt/` (ignored) holds what they write, copies of shipped apps included. |
| `u8/` | The U8 probes (ADR 0206): headless `u8_*` for tables, tabs, menus and toolbar tools. `summarize.py` prints one kind of `u8_matrix.out` property by property. |
| `oi57/` | Open items 57 and 58 (ADR 0239): headless. `oi57_build` writes the fixture app `U7bApp73.mlapp` with R2025b's serializer under the version 7.3 MAT-file preference (an HDF5 model); `oi57_verify` loads, reads and runs the apps JGraph spliced an edit into (the shipped one with a version 7.3 model among them) and puts a code-only `.mlapp` to App Designer's readers. `rt/` (ignored) holds what they write. |
| `oi138/` | Open item 138 (ADR 0241): headless. `oi138_image` records what 28 forms of the image family do to their axes (limits, modes, `YDir`, `Layer`, aspect, view, `CLim`); `oi138_auto` the automatic limits over an image alone and beside a line, a scatter, a text, a patch or a second image. |
| `oi139/` | Open item 139 (ADR 0241): headless. `oi139_shapes` has R2025b's `hgsave` write figures with a colour bar label, a legend title, a subtitle and a polar title, dumps and reopens each from its tree, then opens nine hand-built forms; `oi139_helper` writes the tree-only file the fixture `oi_image_limits_fig_leftovers` reads; `oi139_make` (run in JGraph: `jgraph.exe -batch oi139_make.m -sd <this folder>`) writes a figure of capturing callbacks that `oi139_verify` opens and runs in R2025b. `rt/` (ignored) holds what they write. |
| `oi140/` | Open item 140 (ADR 0242): headless. `oi140_tags` tags 23 kinds of object (a text, a rectangle, a light, a group, a legend, a colour bar, seven annotations, …) and prints each one's `findobj` and `findall` count and `Type` by tag before and after `savefig`/`openfig`, with the rectangle's shape and the `UserData` after. |
| `oi141/` | Open item 141 (ADR 0242): headless. `oi141_title` records what a legend's `Title` is (class, properties, `get`, defaults, `title(lg, …)`, the words' shapes, the refusals, display, delete, a legend made again, the box under it, `savefig`); `oi141_font` and `oi141_follow` what its size, weight, angle, font, interpreter and colour follow, read after the legend's update; `oi141_classes` prints its rows in the three recorded class tables' formats. |
| `oi142/` | Open item 142 (ADR 0242): headless. `oi142_imshow` records what 50 forms of `imshow` do to the figure, the axes and the image (each class, true colour, indexed, display ranges, every option, a named axes, subplot, hold, replaced, twice, a file, a panel, a uiaxes); `oi142_errors` its refusals and warnings word for word, and exports two pictures (`rt/`) showing that labels written after `imshow` are drawn on its invisible axes while a plain invisible axes draws none. |
| `ds0/` | Stage DS0 of the designer plan: headless. `ds0_stale` writes apps whose component tree disagrees with their code and shows what App Designer's load and the class make of each; `ds0_shipped` reads the 44 shipped apps' trees against their `createComponents`; `ds0_codegen` runs App Designer's component code generator over them; `ds0_tree` saves component trees for `ds0_tree_dump.py` to list their subsystem objects; `ds0_plaintext` writes a plain-text `.m` app and loads and runs it with the feature off and on; the window session `ds0w_dock` (`.m` + `.ps1`, user's leave) opens App Designer in the full desktop and lists and captures MATLAB's windows. `rt/` (ignored) holds what they write. |
| `ds1/` | Stage DS1 of the designer plan (ADR 0243): headless. `ds1_catalog` records, per uifigure component class, App Designer's generated property order, mode siblings and themed run-time defaults into `ds1_catalog.json`, which the build embeds; `ds1_oracle` runs R2025b's generator over every component of the 44 shipped apps into `rt/ds1_oracle.txt` (ignored: MathWorks' lines, read by the local test); `ds1_generate` runs the fixture app `tests/JGraph.Tests/Scripting/Designer/DesignerSpecApp.m` through the generator and writes `DesignerSpecApp.generated.txt` beside it; `ds1_quirks` tries two generator branches that write code which does not read back on 15 real properties. |
| `ds1b/` | Stage DS1b of the designer plan (ADR 0244): headless. `ds1b_mcos.py` reads and writes a level-5 MAT-file's MCOS subsystem as a model (byte for byte); `ds1b_reduce.py` + `ds1b_reduced`, `ds1b_rules.py` + `ds1b_rules` and `ds1b_setsaves` learn what `load` does with a tree that stores fewer or other names; `ds1b_fresh` saves a fresh component of each of the 45 classes (and an axes' labelled parts) into `rt/fresh`, `ds1b_blank.py` + `ds1b_blanked` load each with nothing stored, `ds1b_transcribe.py` + `ds1b_scratch` (over `ds1b_build.py`) rewrite saved trees from scratch, and `ds1b_base.py` records the bases into `ds1b_base.json`, which the build embeds; `ds1b_dates`, `ds1b_tabs` and `ds1b_cmap` record a datetime's stored form, a tab's size and App Designer's colormap names; `ds1b_verify` (with `ds1b_reference`, `ds1b_appsource`, `ds1b_objdiff`) loads every tree JGraph writes against the tree its code makes, and `ds1b_appdesigner` runs each through App Designer's serializer and reader against the same for the code's tree; `ds1b_viewbuild` saves both as `.mlapp` files for the window session `ds1b_view` (`.m` + `.ps1`, user's leave), which opens each in App Designer's window and captures it for `ds1b_viewdiff.py`. `rt/` (ignored) holds the files, the trees JGraph writes among them (`rt/jgraph`, left by the local test `AppDesignerTreeWriterTests`). |
| `ds2/` | Stage DS2 of the designer plan (ADR 0246): headless. `ds2_sizes` makes one component of each class App Designer offers and records its default `Position` and the `Position` it keeps after a set to `[10 10 200 50]` into `ds2_sizes.json` (the round components, switches and semicircular gauge keep their proportions, the sliders their height - the runtime's rule from `u9_extra`); `ds2_appdesigner` records what R2025b's `appdesigner` says before it opens anything - its web-window check under `-batch`, its file-name validator on each kind of name, its arity - without ever calling it where it would open App Designer. `rt/` (ignored) holds the copy of a shipped app it names. |
| `ds3/` | Stage DS3 of the designer plan (ADR 0247): headless. `ds3_inspector` builds, for each class App Designer offers, the property view its inspector shows (`inspector.internal.<Class>PropertyView`) and records its groups in order - id, title, opened or not, rows (a property, an editor group, a sub-group) - and each row's property's class and members or enumerated values into `ds3_inspector.json`; `ds3_palette` asks each class's adapter for its design-time component and records R2025b's generated lines for it and its `Position` into `ds3_palette.json`. Both are embedded in JGraph.Scripting. |
| `ds6/` | Stage DS6 of the designer plan (ADR 0245): headless. `ds6_verify` opens, through App Designer's own reader, the files the local test `AppDesignerModelSaveTests` leaves in `rt/` when run with `JGRAPH_DS6_OUT`: each shipped app as R2025b ships it and as JGraph saved it after an edit (`rt/edits`), and apps JGraph wrote whole (`rt/new`) against a control saved by App Designer's serializer from the code's own tree (`rt/control`). `DS6_PART` (`edits` or `new`), `DS6_ONLY` and `DS6_SHARD` pick what runs (`ds6_new1`-`ds6_new3` run the new apps in three shards); `ds6_verify_edits.out` and `ds6_verify_new.out` keep the results. `rt/` (ignored) holds the files. |
| `ds7/` | Stage DS7a of the designer plan (ADR 0251): node and headless. `oracle/appendix.js` runs App Designer's own plain-text file writer - `PlainTextClassBehavior`, `XMLAppendixBehavior` and the XML builders, unmodified from the bundle through `amd.js` and `stubs.js` - over stub view models and writes `appendix.json` (34 cases: the sections, escaping, indentation, lists, whole files), which the tests' expected texts were taken from. `ds7_plain` opens, through App Designer's own reader (not gated by the feature), the plain-text apps the local test `AppDesignerPlainTextTests.TheProbesAppsAreWrittenWhenAsked` leaves in `rt/plain/` with `JGRAPH_DS7_OUT`, holds each against the `.mlapp` of the same design, then switches the feature `AppDesignerPlainTextFileFormat` on for the session and runs each app through `matlab.apps.App`. `DS7_ONLY` picks one app. `rt/` (ignored) holds the files. |
| `oi77/` | Open item 77 (ADR 0240): headless. `oi77_shapes` has R2025b's `hgsave` write a battery of figures, strips each to its struct tree and reopens it; `oi77_hand` opens trees built by hand (what the loader needs, tolerates and crashes on; `OI77_ONLY`/`OI77_FROM` pick cases after a crash); `oi77_fh` shows how R2025b writes function handles; `oi77_make` (run in JGraph: `jgraph.exe -batch oi77_make.m -sd <this folder>`) writes the `.fig` files `oi77_verify` opens, lists and prints in R2025b. `rt/` (ignored) holds what they write. |

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

## U2 findings (R2025b, headless)

`u2/run-probe.ps1` is U1's. Every `u2_*` probe ran `-noFigureWindows -batch`; none opens a window.

**`uipanel`** (`u2_panel`): 38 names in both kinds of figure. In a classic figure it starts
`normalized [0 0 1 1]`, 8-point MS Sans Serif, `AutoResizeChildren` off; in a `uifigure`, pixels
`[20 20 260 221]`, 12-pixel Helvetica, `AutoResizeChildren` on. `BorderType` lists `none` and
`line`, still takes the four older words and warns (`MATLAB:Uipanel:UnsupportedBorderType`).
`BackgroundColor` refuses `'none'`; the other colours take it. `Enable` is on or off.
`InnerPosition` is read-only. `ResizeFcn` is `SizeChangedFcn`. `Visible` and `Enable` on a panel do
not change what its children answer.

**The inner area** (`u2_panel`, both kinds agree): the border takes its width from every edge —
twice that when etched, nothing when `none` — and a title takes its font's pixel size, rounded,
from the edge it sits on, in place of the border there if larger. A titled 200×150 line panel at
8 points has an inner area of 198×138.

**Units** (`u2_units`): pixel positions are 1-based and every other unit counts from 0, so pixel x
is `(x − 1)` units along. A character is 5.6×15 px. `normalized` is of the parent's inner area, or
of the screen for a figure. Setting `Units` re-expresses `Position`, so the order of `'Units'` and
`'Position'` among a call's options matters. An axes placed in pixels keeps them through a figure
resize. `getpixelposition(h, true)` adds where each container's inner area begins;
`setpixelposition` keeps the object's `Units`.

**`movegui`** (`u2_units`, `u2_more`): works on the whole window, estimated without a display as
8 px of border all round, 23 px of title bar, 27 px of toolbar and 22 px of menu bar where the
figure has them; `'onscreen'` keeps 30 px plus the border.

**The tree** (`u2_tree`): `Children` lists every component before every axes, newest first within
each. `uistack` and `set(f, 'Children', …)` move within a kind; an order putting an axes above a
component warns (`MATLAB:hg:default_child_strategy:IllegalPermutation`) and changes nothing.
`findobj` answers level by level, the starting object first, and with no handle starts at the
root. A hidden handle is left out of `Children` and `findobj` with everything under it, but an
object named as the start is searched even when hidden. A new parent puts a child at the front of
its kind and keeps its `Position` numbers. `DeleteFcn` runs for the object first, then for its
children in `Children` order. R2025b keeps an annotation pane tagged `scribeOverlay` beside every
set of axes, which `allchild` and `findall` report.

**A resize with a window** (`u2w_resize`, run with the user's leave; it opens two windows and
resizes them from the script, typing and clicking nothing):
- A figure's `SizeChangedFcn` runs once per change of size, for a script's `Position` write too;
  not for a move, not for the same size again, and not when the figure is first shown. Its event
  data is a `matlab.ui.eventdata.SizeChangedData` (`Source`, `EventName` `SizeChanged`).
- A container's runs when its pixel size changes — its figure was resized and it is normalized, or
  a script wrote its `Position` — and several times when it is first shown. On a figure resize a
  normalized panel's ran once before the figure's and twice after.
- `AutoResizeChildren` on: the `SizeChangedFcn` of that figure or container is silent. Growing a
  `uifigure` from 400×300 to 800×600 left every pixel-placed child where it was. Shrinking it to
  200×150 moved and resized them (a `uicontrol` at `[101 51 100 50]` went to `[62 11 100 50]`, a
  panel `[201 151 100 100]` to `[175 75 64 73]`), and going back to 400×300 did not restore them:
  the rule depends on the sizes passed through. A pixel-placed axes changed size on growing.
- A classic figure with `AutoResizeChildren` turned on reflows the same way.
- `OuterPosition` equals `Position` with a window too.

**`uifigure`** (`u2_uifigure`): no `Number`, a non-integer handle, `HandleVisibility` `'off'` — so
it is not in the root's `Children`, never `gcf`, `figure(uf)` does not make it current and `close
all` leaves it (`close all force` does not). `AutoResizeChildren` is on, and setting
`SizeChangedFcn` then warns (`MATLAB:ui:containers:SizeChangedFcnDisabledWhenAutoResizeOn`). With
no window nothing resizes children and no `SizeChangedFcn` runs, in either kind of figure.

## U3 findings (R2025b)

Every `u3_*` probe ran `-noFigureWindows -batch`. `u3w_clicks` opens a figure and clicks in it with
`java.awt.Robot`, so it runs only with the user's leave; before every click it asks Windows which
window is in front (`fgtitle.ps1`) and stops unless it is its own figure.

**The names** (`u3_styles`, `u3_more`, `u3_set`). `get(c)` lists 40 names and `set(c)` 37. Hidden
and still answering: `TooltipString`, `TooltipStr`, `Tooltips`, `UIContextMenu`, `Selected`,
`SelectionHighlight`, `HitTest` (on or off, any number counting as on). `TooltipString` takes one
line of text only. `set(h)` is a struct of the writable names, each a column cell of the words it
takes or an empty cell; `set(h, name)` is one name's words. `uicontrol(h)` is the focus form for
every style, a frame included; `uicontrol('Parent', frame)` is refused.

**`Value`** is the same for every style: any numeric or logical array, held as double, a column
turned into a row, an empty as 0×0; a matrix is refused (`UIControlValueDimensions_M`), and a cell,
a string and a complex number each have their own refusal. Nothing checks it against `Min`, `Max`
or the items. A `listbox` and a `popupmenu` start at 1, and a control that becomes one keeps that 1
whatever it becomes next.

**`String`**. A newline starts a new line: a row becomes a padded character matrix, a cell element
several elements. A `listbox` or `popupmenu` reads `'a|b|c'` as a 3-row character matrix, whenever
it became a list; a cell's elements are not split. `{}` stays a 0×0 cell.

**`SliderStep`**: not numeric, not two elements, a first step outside [0, 1] are three refusals; a
second step below the first is kept with a warning. A `single` or an integer class is read as
garbage (`single([0.1 0.3])` → `[3.8e-07 0]`), which is R2025b's fault.

**`CData`**: an m×n×3 array of a numeric class, a floating one within [0, 1] or NaN; `[]` clears it
and reads back as 0×0×3. Logical, char and cell are refused by type, a 2-D or 4-page array by size.

**`Extent`** (`u3_extent`, `u3_metrics`, `u3_wrap`). Whole points: the widest line plus 4 points,
the lines' height plus 6, each rounded. A `text` control, and any style with `Max - Min > 1`,
measures every line; the rest measure the first (a list its first item). No lines at all is
`[0 0 4 6]` points, and an empty line measures as a space. In other units it converts like a size.
The text itself is measured through GDI at the display's scaling: at 125 % an 8-point MS Sans Serif
`'a'` is 4.2 points wide and a line 11.25 points; widths scale with the size asked for while line
heights step (the same from 4 to 10.5 points); `'Arial'`, `'Helvetica'` and a name no font has all
measure alike, as the default font.

**`textwrap`** (`u3_extent`, `u3_wrap`). To a count of characters: words are gathered with the
spaces after them while they fit, the last word counting one space; a word longer than the count is
cut into pieces of that many characters, a piece of spaces alone dropped; the paragraph's last line
loses its trailing spaces; a paragraph with no words is `' '`. The count must be a positive whole
scalar. To a control: lines whose `Extent` is no wider than the control, with no spaces at their
ends; a word wider than the control keeps a line. The second output is `[x y w h]` in the control's
units, the size being the `Extent` of the lines in a `text` control; without a control it is four
zeros. Three outputs are refused.

**`listfonts`**: a column cell, unique, sorted without regard to case. `listfonts(h)` adds the font
`h` names when it is not listed; any other argument is ignored.

**`uibuttongroup`** (`u3_bgroup`, `u3_more`). A `matlab.ui.container.ButtonGroup`, type
`uibuttongroup`: a panel's names plus `Buttons` (always empty), `SelectedObject` and
`SelectionChangedFcn` (older name `SelectionChangeFcn`). In a classic figure its `BorderColor` and
`HighlightColor` are white; in a `uifigure` it starts at `[20 20 260 210]`.
- It watches a radio button or a toggle button that joins with its `Value` at its `Min` or `Max`,
  by being made in it or moved into it. A push button turned radio afterwards, a button in a panel
  inside the group, and one made with a `Min` or `Max` its `Value` is not at, are not watched.
- The first watched button to join is selected, and so is one that joins at its `Max`.
- Writing a `Value` starting with 1 to a watched button selects it; writing 0 to the selected one
  selects nothing. Selecting writes 1 into the button and 0 into the one that was selected,
  whatever their `Min` and `Max`.
- `SelectedObject` takes a radio or toggle button of the group, the first of several handles, or
  `[]`; a check box, a push button and a figure are refused by type, a button elsewhere by parent.
- A selected button that leaves the group reads 0 and nothing is selected. A script's writes run no
  callback. A watched button that became a check box refuses a `Value` of 1 after it is written.
- `DeleteFcn`: the group's first, then its children's in `Children` order; `SelectedObject` is
  still set while they run.

**Clicks** (`u3w_clicks`, with a window).
- A toggle button, a check box and a radio button outside a group read 1 after a click and 0 after
  the next — 1 and 0 even for a check box with `Min` 2 and `Max` 7.
- A slider's arrow moves by `SliderStep(1)` of the range and its trough by `SliderStep(2)`.
- A list's `Callback` runs for every click, the selected row again included.
- In a button group a click on another button runs `SelectionChangedFcn`
  (`matlab.ui.eventdata.SelectionChangedData`: `OldValue`, `NewValue`, `Source`, `EventName`
  `SelectionChanged`) and then the button's `Callback`. A click on the selected radio button runs
  nothing. A click on the selected toggle button writes 0 and runs its `Callback`; the group's
  callback does not run and the button is still the group's `OldValue` at the next change.
- A click on a control whose `Enable` is `'inactive'` or `'off'`, and a right click on an enabled
  one, runs `WindowButtonDownFcn` and then the control's `ButtonDownFcn`, handed a
  `matlab.ui.eventdata.MouseData` (`ButtonDown`). The `Callback` does not run. A left click on an
  enabled `text` or `frame` runs nothing.
- Every `Callback` gets an `ActionData`, and `gco` is the control.

## U4 findings (R2025b, headless)

Every `u4_*` probe ran `-noFigureWindows -batch`; none opens a window. The layouts of the three
blocking dialogs were recorded in U1's window session (`u1/u1w_dialogs`).

**`uiwait`, `uiresume`, `WaitStatus`** (`u4_wait`, `u4_wait2`). A figure has a hidden `WaitStatus`:
`[]` until something waits on it, then `'waiting'` or `'inactive'`. It takes its two words in any
case and abbreviated; anything else is `MATLAB:gbtdatatypes:WrongFormat`. `uiwait` warns that it
has no display **before** it looks at its arguments, then: what is not one figure is
`MATLAB:uiwait:InvalidInputType`; a timeout that is not numeric is `InvalidSecondInputType`; a
timeout under one second becomes one second with a warning; a vector, an empty and a NaN are
refused in the words of the timer the timeout is. It makes the figure visible. `uiresume` on a
handle that is no object is `MATLAB:class:InvalidHandle`, on one object that is not a figure
`MATLAB:uiresume:InvalidInputType`; a figure that is not waiting, and several figures, pass.
A timeout, a timer's `uiresume`, a `WaitStatus` written by hand, a `delete` and a `close` each end
the wait; a timeout leaves `'inactive'`. Timer callbacks do not nest: a `uiwait` started inside a
timer's callback is never ended by another timer, which is where `u4_wait` stops (its output ends
in the runner's TIMEOUT line, and `u4_wait2` is the rest of it). One reading is not understood: a
bare `uiwait` on the current figure, resumed by a timer after 0.5 s, took about 4 s.

**`waitfor`** (`u4_wait2`). No argument is `MATLAB:minrhs`, four are `MATLAB:maxrhs`. What is not a
live object — a number, text, a deleted handle — returns at once without complaint. A property
the object lacks, or a name that is not text, is `MATLAB:waitfor:BadProperty`. The name is taken
in any case; the value is compared exactly (text by case, a `Value` of 0 by `false` too). A
property set to the value it already has is no change.

**`guihandles`, `guidata`, application data** (`u4_guidata`). `guihandles` walks `findall`'s
order — the figure, its children front first, then theirs — and makes one field for each `Tag`
that is a valid name; objects sharing a tag share a field as a row; hidden handles are included;
a figure with nothing tagged answers `[]`. `guidata` is the figure's application data
`UsedByGUIData_m`, and storing an empty value removes it. `rmappdata` of a name not there is
`MATLAB:HandleGraphics:Appdata:InvalidPropertyName`; a name that is not text is
`...:InvalidSecondArgumentName` (a cell of names too); a name that cannot be a field is
`MATLAB:AddField:InvalidFieldName`; what is not an object is `MATLAB:hg:InvalidArray`. The root
takes application data.

**The message boxes** (`u4_dialogs`). The tree is the one `u0_dialogs` found; this probe adds the
forms. A dialog's pixel rectangle is centred across the screen and two thirds of the way up it,
each edge rounded to a whole pixel: `x = 1 + (W - w)/2`, `y = 1 + (H - h)*2/3`. The message text's
`String` is always a cell; a newline in the message starts a new line; text is wrapped at 75
characters. `'replace'` and `'modal'` take over the newest box of the same title and tag and
close the others; `'non-modal'` adds one. `helpdlg` always replaces; `errordlg` and `warndlg` add
unless told `'replace'` (`errordlg` also takes `'on'`). An icon word that is none of the four
warns and shows none. The OK button's `Callback` is the text `delete(gcbf)`; Return, Space and
Escape delete the box through `KeyPressFcn`.

**`dialog`** is a figure with `WindowStyle` modal, `Resize` off, `MenuBar` none, `NumberTitle`
off, `IntegerHandle` off, `HandleVisibility` callback, and a `ButtonDownFcn` that closes it while
it is empty. A figure's `WindowStyle` takes `normal`, `modal`, `docked` and `alwaysontop`.

**`waitbar`** is a 270 by 56.25 point figure (360 by 75 pixels) in the middle of the screen, tagged
`TMWWaitbar`, with an invisible axes at `[13.5 16.875 243 11.25]` points whose title is the
message, and a `uiprogressindicator` at `[19 23.5 324 6]` pixels (`Value` 0 to 1, `Indeterminate`,
`ProgressColor` `[0.149 0.549 0.867]`, `HandleVisibility` off). Its application data are
`TMWWaitbar_handles` (figure, axes, axesTitle, progressbar, container) and `TMWWaitbar_value`
(0 to 100). `waitbar(x)` moves the newest one; `waitbar(x, h, message)` changes the title, and a
message that is not text is taken without complaint. With `'CreateCancelBtn'` the axes move up by
the button's height, but the figure grows only to fit the title, so the extra height asked for is
lost (61.625 points, not 73.5).

**The ones that block** refuse without a display before they read their arguments
(`MATLAB:hg:NonInteractiveFunctionSupport`): `questdlg`, `inputdlg`, `listdlg`, `uigetfile`,
`uiputfile`, `uigetdir`, `uisetcolor`, `uisetfont`, `uiopen`, `uisave`, `uiload`. Only MATLAB's own
count of arguments comes first (`questdlg` with seven, `uiload` with one). `exportapp` without a
display is `MATLAB:print:HeadlessFigureUnsupported`, and on what is not a figure
`MATLAB:print:ExportHandleNotValid`.

**Words** (`u4_messages`): the dialogs' messages as R2025b's catalogue has them, and
`u4_nargout` the output counts the generator reads.

## U5 findings (R2025b, headless)

Every `u5_*` probe ran `-noFigureWindows -batch` through `u5/run-probe.ps1`; none opens a window.
`tryp` prints a value or an error's identifier and sentence, `u5_dialogs2` uses a form of it for
functions with no output.

**The components** (`u5_matrix`, `u5_forms`). Sixteen classes from thirteen functions. A refusal
is `MATLAB:ui:<Class>:<id>` and a plain sentence; a graphics datatype's refusal (a colour, an
on/off state, a callback) has the prefix "Error setting property 'X' of class 'C':". Inside the
call that makes a component every refusal becomes `MATLAB:ui:<Class>:unknownInput`. `Value` and
`ValueIndex` are applied after the other pairs of a making call. An enumerated word is matched
whole in any case; only `InputType` takes an abbreviation. A string scalar is not a char row for
`Items`, `ItemsData`, `ValueIndex`, `MajorTickLabels`, a numeric `Value`, `RowHeight` and
`ColumnWidth`. A leaf component has no `Children`, and a radio button no `Layout`.

**Button groups in a `uifigure`.** The first radio or toggle button made is selected whatever its
`Value` was asked to be. Writing false to the selected one selects the first; with one button it is
`noButtonSelected`. Mixing the two kinds is refused ("Mutual exclusivity violated…"). A radio
button's `Parent` must be a `ButtonGroup` (`invalidClassForParent`). After a button is moved out
of a group, the group's `SelectedObject` still names it.

**Fit sizes** (`u5_grid`), with `fs` the font size in pixels and widths taken with pair kerning,
each rounded up to a sixty-fourth:

| component | width | height |
| --- | --- | --- |
| a line of text | sum of glyph widths | 1.23 fs |
| label, hyperlink | text + 2 | lines + 2 |
| check box, radio button | text + 19 | line + 2 |
| button, state and toggle button | ceil(text) + 10 | ceil(lines) + 8 |
| edit field | 10 + 9.4388 fs | line + 8 |
| numeric edit field | width of "0123456789" + 10 | line + 8 |
| spinner | numeric + 22 | max(22, line + 4) |
| text area | 10 + 14.1589 fs | 4 lines + 6 |
| drop-down | widest item + 33 | line + 8 |
| list box | ceil(widest item) + 10 | items × 1.5 fs + 2 |
| slider | 164 (the track and 16) | 39 (the track and 36) |
| an empty grid | 30 | 30 |

A slider's track sits 7.5 from the left and 6 from the top of that rectangle.

**The grid** (`u5_grid`). Tracks are laid from the left and the top, inside the padding. A `'fit'`
track is the largest fit of a child that sits in it alone; a child that spans shares what it
still needs among the `'fit'` tracks it spans; a `'fit'` track of size 0 takes no spacing. Weighted
tracks share the rest and stop at zero. A grid asked for its own fit sizes its weighted tracks to
their content. A new child takes the cell after the row-major maximum of the cells in use.
`RowHeight` and `ColumnWidth` read back as the declared list with `'1x'` added to cover the
children, and the growth is not kept when the children go. A child moved between grids keeps its
cell. Writing a child's `Position` warns
(`MATLAB:ui:components:noPositionSetWhenInLayoutContainer`); a grid's own `Position` is read-only.
A grid in a titled `uifigure` panel loses a line and 6 at the top and a pixel on each other edge.
`uicontrol` is refused in a grid. **The layout is asynchronous**: positions settle 0.1 to 0.5 s
after `drawnow`, so the probe waits before it reads.

**A slider's ticks** are computed by R2025b's view and read back only once it has settled;
`MinorTicks` reads `[]` until then. Thirty-four ranges are recorded in `u5_matrix.out.txt`.

**The dialogs** (`u5_dialogs`, `u5_dialogs2`). `uialert` and `uiprogressdlg` on a figure that is
not shown are `MATLAB:uitools:uidialogs:InvisibleFigure`; on what is not a figure,
`InvalidFigureHandle`. `uiconfirm` waits only when an output is asked for, and without a display
is `MATLAB:hg:NonInteractiveFunctionSupport`. Defaults: `uialert` `Icon` `'error'`; `uiconfirm`
`Icon` `'question'`, `Options` `{'OK', 'Cancel'}`, `DefaultOption` 1, `CancelOption` the last. The
`CloseFcn` structures, read from R2025b's installed source: an alert's has `Source`, `EventName`
(`'AlertDialogClosed'`) and `DialogTitle`; a confirmation's adds `SelectedOptionIndex` and
`SelectedOption` and is named `'ConfirmDialogClosed'`. A `ProgressDialog` has ten properties
(`Value`, `Message`, `Title`, `Indeterminate`, `Icon`, `ShowPercentage`, `Cancelable`,
`CancelText`, `Interpreter`, `CancelRequested`) and refuses in the words of a property validator.
A property of a closed one is `MATLAB:class:InvalidHandle`.

**`uiaxes`** is `matlab.ui.control.UIAxes`: `Units` pixels, `Position` equal to `OuterPosition`
(`[10 10 400 300]`), `NextPlot` `'replacechildren'`, `BackgroundColor` `'none'`, never `gca`. In
a grid its `Position` is its cell. `focus` on it is `MATLAB:UndefinedFunction`, as on a label, a
panel and a classic control; on a figure that is not shown `focus` warns and returns.

Not measured: any callback's event data (no component can be worked headless), and scrolling.

## U7 findings (R2025b, headless)

`u7/u7_classes` makes one object of each kind JGraph makes and prints its class, its
`superclasses`, and which of a list of known bases `isa` answers true for. `u7/gen_classes.py`
turns the output into `src/JGraph.Scripting/Jgs/JgsGraphicsClasses.Table.cs`. `u7/u7_small` asks
the small questions below. `u7/mlapp/build_mlapp.py` builds the `.mlapp` files the parity
fixtures `u7_app` and `u7_mlapp` run, from text; they hold the three parts R2025b needs and no
`appModel.mat`.

**`superclasses` is not the whole of `isa`.** `isa(ax, 'matlab.graphics.axis.AbstractAxes')` is
true and the name is in no `superclasses` answer; the same holds for `matlab.graphics.primitive.Data`,
`matlab.graphics.mixin.Legendable`, `matlab.ui.container.Container`, `matlab.ui.control.Component`
and the other bases MATLAB builds in. Every graphics object is a `handle`, a
`matlab.graphics.Graphics`, a `matlab.mixin.SetGet`, a `dynamicprops` and an `hgsetget`. A
`uiaxes` is an `Axes`; a button group is a `Panel`.

**A property typed with a graphics class** starts as an empty of the class, refuses `[]`
(`MATLAB:validation:UnableToConvert`) and a number that is no handle
(`MATLAB:graphics:CannotConvertDoubleToHandle`, "Cannot convert double value 5.5 to a handle"),
and takes a subclass's object.

**Destruction.** For a class with its own `delete`: the `ObjectBeingDestroyed` listener runs
first and the `delete` method second, and `isvalid` is false in both. For a graphics object: its
listeners newest first, then its `DeleteFcn`, the parent before its children, `isvalid` false in
both. `events(uibutton)` is `ButtonPushed, ObjectBeingDestroyed, PropertyAdded, PropertyRemoved`.

**`matlab.apps.AppBase`.** `saveobj` warns `MATLAB:appdesigner:appdesigner:SaveObjWarning`
("Unable to save App Designer app object. Save not supported for matlab.apps.AppBase objects.")
and answers `[]`. The handle `createCallbackFcn` answers has two inputs: one is
`MATLAB:minrhs`, three `MATLAB:TooManyInputs`. An app whose startup function fails keeps its
figure and is not deleted. `getRunningApp` answers the app on the newest figure that has one of
the class. Too many arguments to a class method are `MATLAB:maxrhs`, to a function
`MATLAB:TooManyInputs`.

**The `.mlapp` file.** Found before an `.m` of its name in one folder; `exist` 2, `exist(…,
'class')` 8; `type` prints its code; a function or a plain class in one runs; the document may be
any part the relationships name; a zip with the document and no relationships is `exist` 2 and
`MATLAB:fileio:cantOpenFile` when called; a class under the wrong file name is
`MATLAB:m_class_filename`; an error's stack names the frame `Class.method` and the `.mlapp` file,
with the line counted in the code.

Not measured: a file App Designer itself saved (the fixtures' files are built from text), and
what `run` does with a classdef file beyond raising no error.

## U7b findings (R2025b, headless)

`u7b/u7b_shape` reads every `.mlapp` R2025b ships (44) in place and tallies where each piece of
`appModel.mat`'s `code` variable sits in the class text, and the exact form of each value.
`u7b/u7b_build` builds `U7bApp.mlapp` from `U7bApp.txt` with
`appdesigner.internal.serialization.MLAPPSerializer`, the class App Designer's save goes through,
so the file holds a real component tree and metadata; the copy in
`tests/JGraph.Tests/MatlabParity/fixtures/helpers` is that file. `u7b/u7b_verify` hands R2025b the
files JGraph saved an edit into. `u7b/u7b_char` and `u7b_char7` ask how char data past ASCII is
stored in a MAT-file.

**The text.** Line feeds only, no newline at the end, in all 44. Four marker comments are in every
app: `% Properties that correspond to app components`, `% Callbacks that handle component events`,
`% Component initialization`, `% App creation and deletion`.

**The editable section** is every line from the second line after the component properties
block's `end` to the second line before the callbacks comment: one empty line (length 0) is left
on each side and is not part of it. Its own first and last lines are often whitespace-only
(`'    '`), stored as written; an empty line is stored as a 0-by-0 char. The value is a 1-by-N cell.
An app with nothing there has no `EditableSectionCode` field and one empty line between the two
blocks. A responsive app has a second generated block first (`% Properties that correspond to apps
with auto-reflow`), and three Simulink apps a third kind (`% Public properties that correspond to
the Simulink model`); the section starts after it - except in one Simulink app, whose record
takes the block in.

**Callbacks.** `Callbacks` is a 1-by-N struct array with the fields `Name` and `Code`, in text
order. Every one of the 383 is written `function Name(app, event)` under a comment line, inside
the callbacks block, and its `Code` is the lines between that line and the function's `end`, a
1-by-N cell. One callback with no line between the two has `Code` as a 0-by-0 char. A function
nested inside a callback is part of its `Code`. A responsive app's `updateAppLayout`, under
`% Changes arrangement of the app based on UIFigure width`, is listed and its `Code` is not the
text's lines. `StartupCallback` is a 1-by-1 struct of the same two fields, for the function under
`% Code that executes after component creation`, always the first in the block; `InputParameters`
is what follows `app, ` in its signature, a char row.

**Which fields there are.** `MLAPPSerializer` writes `ClassName` and then each of
`EditableSectionCode`, `Callbacks`, `StartupCallback`, `InputParameters`, `SingletonMode`,
`AppTypeData` and `Bindings` that is not empty: a field is there exactly when it holds something.
`SingletonMode` is `'FOCUS'` where present; `AppTypeData` a 1-by-1 struct with no fields.

**The model file.** `appModel.mat` holds `appData` (an
`appdesigner.internal.serialization.app.AppData` object), `code` and `components` (a struct of
`UIFigure`, a `matlab.ui.Figure`, and `Groups`). It is a level-5 MAT-file with compressed
elements in 43 of the 44; **one shipped app's is version 7.3** (HDF5), which a splice cannot
rewrite.

**What R2025b makes of a file JGraph saved** (`u7b_verify`). Each shipped app was copied, a
comment line put first in a callback, in the startup function and in the editable section, and
saved by JGraph. For all 43 with a level-5 model, `readAppCodeData` returns each edit in its
place, `matlab.internal.getCode` returns the edited text, and `readAppDesignerData`,
`readAppMetadata` and App Designer's own full load
(`DeserializerFactory.createDeserializer(...).getAppData()`) succeed. The fixture app with real
edits is read the same way and runs: its new property, its edited startup function and its edited
callback all act. The full load adds `ComponentData` to each callback and `codeFormatting` and
`HelpComments` to `code`, for the unedited file too.

**Char data past ASCII.** R2025b's default save writes a char with a unit above 127 as miUTF16
(type 17). Its `-v6` save writes miUINT16 units in the machine's code page: U+4E2D is stored as
0x1A and U+20AC as 0x80. Reading follows: miUINT16 units are taken in the code page, so a UTF-16
unit above 255 written as miUINT16 comes back as its low byte, and the same units typed miUTF16
come back whole. A CDATA terminator in the code (`]]>`), written as two CDATA sections, is read
back by R2025b as written.

Not measured: App Designer opening a saved file in its window (the full load it starts with is
measured; the designer itself needs a display and a person), and a file saved by any release but
R2025b.

## U8 findings (R2025b, headless)

**One class each, in both kinds of figure.** `uitable`, `uitabgroup`, `uitab`, `uimenu`,
`uicontextmenu`, `uitoolbar`, `uipushtool` and `uitoggletool` answer to the same names, take the
same words and refuse in the same sentences in a classic figure and in a `uifigure`
(`u8_matrix`: the two halves differ only in a table's font - 8-point MS Sans Serif against
12-pixel Helvetica - and in a tab group's place and units, `[0 0 1 1]` normalized against
`[20 20 250 210]` pixels). They refuse as the newer components do (on/off, word, colour and text
coercions), not as a `uicontrol` does; a table keeps a classic control's `Units` and `FontUnits`.

**A table's data** is a numeric array of any class, a logical array, a string array, a cell or
a table; it reads back as given, class included. Refused: text (`BadDataType`; the empty `''`
is taken), a datetime, a categorical, a duration, a struct, three dimensions
(`BadDataDimension`), and in a cell a nested cell, a handle or a string (`BadDataCellArrayType`)
or a vector (`BadDataCellArraySize`). `ColumnName` and `RowName` read `'numbered'` until
written; with a table for data they read its variable names and its row names (an empty cell
when it has none). `DisplayData` is the data. `Extent` was `[0 0 340 340]` for every table asked.

**Tabs.** The first tab made shows. Deleting or moving away the one that shows hands the page to
the tab after it, or the one before when it was last. A group's `Children` are its tabs in
heading order, first first. A tab's `Position` is read-only and `[1 1 w h]` in pixels; the group's
`InnerPosition` says where that is. **Settled sizes** in a `uifigure`, for a 300-by-200 group: a
tab is 298 by 175 with the strip along the top or the bottom (a 24-pixel strip and a pixel of
border on the other three sides); with the strip along a side it is 232 by 198 for one tab named
`Alpha` (a 66-pixel strip) and 197 by 198 once a tab has a long title (a 101-pixel strip). **The
values are racy**: a tab answers the group's whole rectangle until the layout has run, which
takes more than half a second for the first group of a figure, and never happens in a classic
figure that is not shown; a group goes on answering its old `InnerPosition` after a longer title
has widened its strip.

**Menus.** A menu's `Position` is its place among its siblings, and `Children` lists them last
place first. Writing a place moves the menu there. Writing any other number - `0`, `1.5`, `9` of
three - moves it to where the number falls among the others and reads back as written; the
others count round it, a number below 1 taking no place from the count. `Label` and `Callback`
are answered and not listed. A classic figure with `MenuBar` `'figure'` has six menus and a
toolbar of its own as objects with hidden handles (`findall` finds 89 menus); a script's first
menu has `Position` 7 there, and 1 with `MenuBar` `'none'`.

**A figure's children** are one list, newest first, whatever they are - menus, context menus,
toolbars, panels, tables, tab groups, controls - with the axes after them (`u8_more`).

**Tools.** `CData` takes an m-by-n-by-3 array of any numeric class, floating ones within
`[0, 1]` or NaN; `Icon` takes a file's name or such an array, keeps a name that is no file and
warns only when the name has an extension. With both set the `Icon` shows and a warning says
so. A toggle tool's `State` written by a script runs `OnCallback` or `OffCallback` at the next
`drawnow`, and not `ClickedCallback`.

**Parents.** `uitab` takes a tab group, a tool a toolbar, a toolbar and a context menu a figure,
a menu a figure, a context menu or a menu; each names what it takes in its refusal
(`MATLAB:uitab:InvalidParent` and its like). What can hold nothing is refused the same way by
every maker (`MATLAB:gbtobjects:Component`, "PushTool cannot be a parent."). A wrong name or an
odd number of arguments is refused before a wrong parent is. With no parent named `uitab` makes
a tab group in the current figure, and a tool uses the current figure's toolbar or makes one.

**Event classes** (`meta.class`, `u8_behave`): `CellEditData` (`Indices`, `DisplayIndices`,
`PreviousData`, `EditData`, `NewData`, `Error`), `CellSelectionChangeData` (`Indices`,
`DisplayIndices`), `TableSelectionChangedData` (`Selection`, `PreviousSelection`,
`SelectionType`, `DisplaySelection`, `PreviousDisplaySelection`), `SelectionChangedData`
(`OldValue`, `NewValue`), `MenuSelectedData` (`ContextObject`, `InteractionInformation`),
`TableInteraction` (`DisplayRow`, `DisplayColumn`, `Row`, `Column`, `RowHeader`,
`ColumnHeader`, `Location`, `ScreenLocation`).

Not measured: any callback of these objects, which needs a window in R2025b, and how a table
draws its cells.

## U9 findings (R2025b, headless)

**One class each, in both kinds of figure** (`u9_matrix`, 47,015 lines): `uiknob` (Knob,
DiscreteKnob), `uiswitch` (Switch, RockerSwitch, ToggleSwitch), `uigauge` (Gauge, LinearGauge,
NinetyDegreeGauge, SemicircularGauge), `uilamp`, `uidatepicker`, `uicolorpicker`, `uitree` (Tree,
CheckBoxTree) and `uitreenode` answer to the same names and refuse in the same sentences in a
classic figure and in a `uifigure`. The makers follow U5's forms (`unknownInput`; a STYLE word
refused with the list of styles; `uitree('v0')` refused as removed); `uitreenode` takes a Tree or
a TreeNode as its parent and nothing else, checking the pairs before the parent.

**Every write goes through `isequal` first.** A value equal to the one held is not written:
`false` lands on a knob at 0 where `true` is refused, `[]` on a picker with no disabled dates where
it is refused once there are some, `{}` on an empty `DisabledDates`; the `AutoConvertStrings`
warning is `isequal`'s, when a datetime meets text that is no date (`u9_extra`, `u9_props`).

**Values.** A knob's `Value` is a double within `Limits`, which clamp it; a gauge's any finite
number, not clamped; `ScaleColors` rows or names, `ScaleColorLimits` increasing pairs, equal bands
until written; `MajorTicks`/`MinorTicks` a 1-by-n numeric array. A switch has two `Items` and at
most two `ItemsData`, a discrete knob at least two; items rewritten under a value take the first
when the value is gone; data first given keep the index, data replaced look the datum up. A date
picker's `Value` is a datetime scalar within `Limits` or NaT (time dropped, years 0 to 9999, a
disabled weekday refused); a limit or a disabled weekday that excludes the value leaves a NaT in
datetime's default format, a disabled date one in the picker's; `DisplayFormat` drops a time of
day and refuses a time alone, an empty, or an unknown letter (`'A'` is unknown). A colour
picker's `Icon` takes `'default'` and `'text'` by any beginning. Blanks round an on/off or an
enumeration word are ignored.

**Shapes.** A knob, a discrete knob, a lamp, a circular and a ninety-degree gauge keep a square, a
semicircular gauge 120:65, a switch 45:20: a rectangle written is shrunk to the largest of the
shape inside it (`[10 10 80 40]` on a knob is `[10 10 40 40]`), with a warning from the view that a
script does not see. Fit sizes (`u9_grid`): gauges, lamp and colour picker their Position; date
picker about 8.33·fs+34 by 1.23·fs+8; tree about 14.16·fs+20 by 9.51·fs+97.9; knob and switch
by their labels. Automatic ticks before a window settles them: a knob `[0 20 40 60 80 100]`, a
ninety-degree gauge `[0 50 100]`.

**Trees.** `Children` are the nodes first-first; `SelectedNodes` keep the order written and lose a
node deleted or moved out; `CheckedNodes` are the nodes written, then descendants, then parents
whose children are all in, in one pass; a child added under a checked parent is checked. A node
has fifteen properties (`Text` char or string only); `copyobj` of a node outside a tree is
`MATLAB:ui:TreeNode:invalidParent`; `expand([n1 n2])` runs, `move(n, m, 'bef')` takes the
beginning of a word. `scroll(lb, 2)` is refused (text or an end only); a container that does not
scroll warns `MATLAB:uicontainer:ScrollableOff`; `focus` refuses a gauge, a lamp, a node, a tab and
a menu.

**Styles.** `uistyle` is `matlab.ui.style.Style`, ten properties, `MATLAB:ui:Style:invalid*`;
`set`/`get` on one are `SetMethodUnknown`/`GetMethodUnknown`. `addStyle` checks the target word
then the index (`invalidStyleTarget`, `invalid<Word>TargetIndex`, `invalidTargetIndex` for the
whole, `invalidNumberOfInputs` for three arguments); `removeStyle` `invalidRemovalIndex`,
`removalIndexOutOfBounds`. `StyleConfigurations` is a table (Target categorical, TargetIndex cell,
Style object array); an index past the data is kept, a column of indices reads as a row, the
style is copied by value, rows stay when data shrink or a node is deleted; a drop-down takes
`addStyle`.

Not measured: any callback of these objects, which needs a window in R2025b, how it draws them,
and the identifier of the aspect-ratio warning.

## U9b findings (R2025b, headless)

**`uihtml` is `matlab.ui.control.HTML`** (`u9b_matrix`, `u9b_forms`): 21 properties in either kind
of figure, no `Enable` and no font; `Data` takes anything as given; `HTMLSource` is text, a URL is
refused, a file is found beside the current folder, on the path or in full (any type), and other
text ending in lower-case `.html`/`.htm` is a missing file; anything else is markup.
`sendEventToHTMLSource` is a method: refusals are `MATLAB:UndefinedFunction` for the class, a name
must be text, no output.

**Two encoders** (`u9b_bridge`, `u9b_bridge2`, `u9b_sends`, `u9b_more`). `Data` travels as
`jsonencode`/`jsondecode`; an event goes out through another encoder (`NaN` → `"NaN"`, a single's
NaN → `null`, `"s"` → `["s"]`, a missing string → `[""]`, datetime → `{Month,Day,Year}`, NaT →
`"NaT"`, on/off → a logical, `@sin` → `"sin"`, a duration or a `containers.Map` → `[]`), and a
page's event data come back through `jsondecode` with vectors as rows (a cell's too, not inside
it; a struct array keeps its shape, its fields are laid out). An event with no data arrives as
`[]`. A complex or sparse value warns, and ends the bridge for the whole session.

**Order and dedupe.** Data written after a ping is sent before it; MATLAB's Data is not sent when
its JSON is the page's already (whichever side set it); a page's every `Data` write runs
`DataChangedFcn`. After each `HTMLSource`, callbacks run once more per message (a bug).
`setup(htmlComponent)` runs after `load`; a `DataChanged` follows it when `Data` is not `[]`.

**The page side** (`u9b_bridge`, `site/shape.html`): own properties `addEventListener`,
`removeEventListener`, `sendEventToMATLAB`, `Data`; listeners newest first; event objects
`{Source, EventName, Data, PreviousData}` and `{Source, Data}`; a `Data` that cannot be written
(a cycle) throws in the page; `undefined`, a function or no name make MATLAB print an internal
error and do nothing.

**The page server** (`u9b_types`, 143 requests): the page's folder and below, nothing above; a
folder is its `index.html`; GET and HEAD, POST 501; served: `.html .htm .js .json .css .xml .svg
.png .jpg .jpeg .gif .ico .cur .psd .woff .woff2 .ttf .otf .mp4 .webm .ogv .ogg .oga .opus .wav
.vtt .pdf .wasm .glb .gltf .md .map .zip .bin .mlapp` and no extension; everything else (`.txt
.mjs .cjs .webp .bmp .mp3 .csv .tif`, ...) is a 404.

**`jsonencode`/`jsondecode`** (`u9b_json`, `u9b_num`): numbers are the digits of `%.15g`, or
`%.17g` when those do not read back (`%.6g`/`%.9g` for single), from 1e6 up as `d.dddE+n` with
at least one decimal, below 1e-4 as `dE-n`; vectors either way are flat, the rest nest first
dimension outermost; decoding stacks equal-sized numeric children along a new first dimension and
makes columns; names: `x` prefix, blanks dropped with the next letter upper case, other characters
`_`, duplicates `_1`; `jsondecode` takes the text alone. `jsonencode` of a duration fails with an
internal warning.

Not measured: anything in a window.

## U10 findings (R2025b, headless)

**Construction** (`u10_matrix`): `setup` runs inside the constructor with the parent set (a
`'Parent'` pair too) and before the other pairs; pairs match names without regard to case, not by
beginnings, take string names and one struct; an odd count is `UnmatchedNameValuePairs` before
`setup`, a pair that cannot be set `ErrorWhileSettingNameValuePairs` after it, each deleting the
object (its `delete` runs); a failing `setup` is `ErrorWhileExecutingSetup` with the class's error
as cause and leaves the component in its figure; no parent makes a `uifigure`; another component
is `invalidParent`; a `'Type'` pair writes the read-only `Type` (a bug).

**`update`** is owed after construction and after any property write (own, private, same value,
`Tag`, `Position`, `Visible`) but not a callback property's, and runs once at the next `drawnow` or
`pause` (not `figure(f)`); a write inside `update` owes nothing; a failing `update` prints "Unable
to execute 'update' method." and is not raised. After reparenting into a figure not yet drawn,
R2025b updates once more a few drains later.

**The object** is the graphics object: `f.Children` holds it, a part's `Parent` and `gcbo` are
it, `ishghandle`/`isgraphics`/`ishandle` are true, `isa` Graphics and Component are true, `Type`
is the class name in lower case (`isgraphics(c, type)` is nonetheless false), `get`/`set` work;
`properties` lists the class's own, then the callback properties, then the 22 inherited ones;
`events` ends with `ObjectBeingDestroyed`, `PropertyAdded`, `PropertyRemoved`.

**Children only in `setup`** (`u10_more`): `uibutton(c)`, a `'Parent'` pair, `b.Parent = c` and a
child made in `update` are refused (`<Class> cannot be a parent of Button.`); `Children`,
`allchild` and `findall` reach nothing inside; a grid made in `setup` takes children later.

**`HasCallbackProperty`** makes a public Dependent `NameFcn` ('' at first, a graphics callback
property's forms and refusal); `notify` runs it after the listeners; a failure is printed, not
raised; on a plain class the attribute is `MATLAB:class:UnrecognizedAttribute`.

Not measured: anything in a window.

## U11 findings (R2025b, headless)

Probes in `u11/` (`run-probe.ps1` as before): `u11_figs` and `u11_hgm` (what `savefig` and
`hgsave` write, `openfig`/`hgload`), `u11_lte` (the struct tree of the shipped LTE GUIDE figure and
an `hgsave` file with labels), `u11_guide` (GUIDE pairs under R2025b's `gui_mainfcn`, with
`guide/myguide.m` and `guide/myguidex.m`), `u11_open` and `u11_guiderun` (read-only, run in both
engines and diffed), and `u11_makefix`, which writes the parity fixtures' figure files.

**R2025b's `savefig` writes no struct tree**: `hgS_080000` is an empty 0×0 struct and the figure
is only in `hgM_080000.GraphicsObjects.Format3Data`, a `matlab.ui.Figure` in the MAT-file's MCOS
subsystem. `hgsave` (and R2024a's `savefig`) write both `hgS_070000` and `hgM_070000`; MATLAB
reads `hgM` first and a file with `hgS` alone too.

**The struct tree** keeps public names; an axes's `special` is `[title; xlabel; ylabel; zlabel]` as
1-based places among its children; children are stored **oldest first**; a legend is a
figure-level `scribe.legend`; callbacks are MATLAB function elements whose anonymous text starts
`sf%N`.

**The subsystem's objects** save `Name_I`/`Name_IS` beside `NameMode`; a container's and a
figure's place is in a `UnitPos`, an axes's limits in its data space (`XLimWithInfsMode` marks a
limit `imagesc` set), its labels on its rulers, `SerializableChildren` newest first; a function
handle is `{0xDD000000, struct}` with its captures in a `function_handle_workspace` object.

**`openfig`**: a new figure, current, `FileName` the full path, visible as saved unless
`'visible'`/`'invisible'`; `'reuse'` answers the first figure open from the file;
`MATLAB:openfig:InvalidOption`, `MATLAB:load:couldNotReadFile` (the name as given),
`MATLAB:loadFigure:InvalidFigFile`. `hgload`'s second output is a cell holding the old values
(`Visible` left out). CreateFcns on open: the struct form alone runs parent first in `Children`
order; the subsystem form runs one axes's lines before the figure.

**`gui_mainfcn`**: a callback only when the name contains the source's `Tag` and `_` (or
`_CreateFcn`); the figure is invisible through the opening function (`HandleVisibility` `'on'`,
`InGUIInitialization` set), shown before the output function only if saved visible and not asked
otherwise; pairs naming figure properties are set, others ignored; the output function gets the
caller's `nargout`, zero included; a singleton reuses the open figure and reruns the opening
function; a layout function gets `'reuse'`/`'new'` and must set `GUIDEOptions` itself.
`guide` is `MATLAB:guide:GUIDEHasBeenRemoved`.

Not measured: anything in a window; a `uifigure` saved by `savefig`; chart objects in a file.

## U12 findings (R2025b, headless)

Probes in `u12/` (`run-probe.ps1` as before): `u12_props` (each name R2021b documents that the
regenerated property coverage found missing, asked of R2025b by `get(h)`, `isprop` and
`get(h, name)`), `u12_more` (the root's four names and the axes toolbar buttons' callbacks and event
data classes) and `u12_examples` (the guide's two app examples, run in both engines and diffed).

**Hidden, not missing**: a `uicontrol`'s `HitTest`, `Selected`, `SelectionHighlight` and
`TooltipString`, the panels' and toolbar tools' `HitTest`, the tools' `TooltipString` and a
`uitable`'s `Extent`, `HitTest` and `RearrangeableColumns` are left out of `get(h)` and answer
`get(h, name)` and `isprop`. A `ProgressDialog` has no `Type`; a `Style` has no `get` (its names are
its `properties`); `s(1)` on one is the style.

**The root** lists `CallbackObject` (empty outside a callback, the source inside one, read-only:
`MATLAB:class:SetProhibited`), `FixedWidthFontName` (`'Courier New'`, text only:
`MATLAB:class:RequireString`), `PointerLocation` (1×2 double; another shape is
`MATLAB:datatypes:Point2dDataType:ArrayShape`, "Input must be 1x2") and `ScreenDepth` (32, and a
write is kept).

**Axes toolbar buttons**: a `ToolbarPushButton` lists `ButtonPushedFcn` first and a
`ToolbarStateButton` `ValueChangedFcn`, each `''` until set and unknown to the other class
(`MATLAB:noPublicFieldForClass`); a bad value is `MATLAB:datatypes:callback:CreateCallback`.
Their event data are `matlab.graphics.controls.eventdata.ButtonPushedEventData` (`Source`, `Axes`,
`EventName`) and `ValueChangedEventData` (and `Value`, `PreviousValue`); the toolbar's is
`SelectionChangedEventData` (`Axes`, `Selection`, `PreviousSelection`, `Source`, `EventName`).

Not measured: a press in a window, so which of a button's callback and the toolbar's
`SelectionChangedFcn` runs first (open item 78).

## DS0 findings (designer plan, R2025b, headless)

The probes in `ds0/` answer the designer plan's DS0 questions (`docs/plans/app-designer-plan.md`,
gitignored), with decision DD1 in force: an app must stay editable in both JGraph's designer and
MATLAB's App Designer. R2025b's App Designer sources were read in place
(`toolbox/matlab/appdesigner`, `toolbox/matlab/uicomponents/plugin/appdesigner`).

**The canvas is the saved component tree, never the code.** `MLAPPDeserializer.getAppData` is
`load` of `appModel.mat` passed through `Version2Loader`, which returns the data unchanged, and
decorators that touch callbacks, images, modes and unknown components; nothing compares the tree
with `createComponents`. `ds0_stale` built five apps with R2025b's own serializer from the U7b class
text and a tree made here: a tree lacking a component the code makes (the canvas lacks it, and its
callback is bound to nothing), a tree with a component the code does not make (the canvas has it),
a button whose text differs (the canvas shows the tree's), and a tree with no
`DesignTimeProperties` (no canvas component has a code name and no callback is bound). In every
case the class itself runs from the code. So a layout change JGraph makes reaches App Designer only
through `components`.

**What App Designer's tree holds** (`ds0_shipped`, the 44 shipped apps). Every canvas component,
the figure included, carries `DesignTimeProperties`, a dynamic property (`meta.DynamicProperty`)
holding a struct: `CodeName`, `GroupId` (a char, empty in 815 of 1108), `ComponentCode` (a cell
of the component's generated lines, never empty), and in some `ImageRelativePath`, `DirtyProps` or
`AppTypeProperties`. The tree's code names are exactly the components `createComponents` makes in
all 44. Objects without it are an axes' text children and annotation panes.

**The code generator is MATLAB code** (`ds0_codegen`).
`appdesigner.internal.codegeneration.ComponentCodeGenerator.getComponentGenerationCode` over the
per-class adapters (`appdesigner.internal.componentadapter.uicomponents.adapter.*Adapter`) makes
a component's lines. The adapters provide the constructor (`getCodeGenCreation`), the property
order (`getCodeGenPropertyNames`: the adapter's `OrderSpecificProperties`, then `get`'s order,
then `Position`, then the value property, less read-only, ignored and mode properties), and
`isDefault` against `getComponentRunTimeDefaults`. Run headless over all 1108 components, it
reproduces the stored `ComponentCode` and the file's lines in order for 987. A callback comes out
as its name in quotes (`app.X.ButtonPushedFcn = 'XPushed';`), which the file writes as
`createCallbackFcn(app, @XPushed, true)`. The rest differ in explainable ways:
- the figure's final `Visible = 'on'` and a `ContextMenu` assignment are written by the code model,
  not the component generator (48);
- apps saved before R2025a store a `Position` for a grid layout's children, which R2025b no longer
  generates (44);
- 29 differ in the stored copy and the text both.

`ds0_shipped` also compared each component with a fresh one of its class: the lines assigned are
the properties that differ, except `Layout.Row`/`Column` (always written), properties that follow
others (`InnerPosition`, `ValueIndex`, `OuterPosition`), and a grid child's `Position`.

**The tree as MAT-file data** (`ds0_tree`, `ds0_tree_dump.py`). A saved component tree is MCOS
objects in the subsystem, each saving its own internal storage rather than its public properties:
a button saves 15-17 names such as `PrivateText`, `PrivateOuterPosition`, `PrivateInnerPosition`,
`PrivateButtonPushedFcn`, `BackgroundColor`, `Layout`, `Internal` and `DirtyPropertyStrategy`; a
figure 33 (`StoragePosition`, `Visible_I`, `SerializableChildren`, …). Helper objects ride along:
two `UnitPos` per figure, a `GridLayoutOptions` per grid child, an `OnOffSwitchState` per on/off
value, a `meta.DynamicProperty` per `DesignTimeProperties` (and two on every figure), dirty-property
strategy objects. The U7b app's four components are 50 objects and 182 names. Each saved tree
reloads as a `matlab.ui.Figure` with its code names.

**The plain-text format already works in R2025b's loader** (`ds0_plaintext`). R2025b carries the
format MathWorks announced for R2026b: an ordinary `classdef ... < matlab.apps.App` file ending in
`%[appendix]` `%[app:layout]` and a `%{ … %}` block of XML (`<Components><UIFigure name="…">`,
one element per component named by its class's short name, one child element per property holding
its MATLAB value, `Children` elements for containers, a callback property holding the method's
name). The feature `AppDesignerPlainTextFileFormat` is 0 by default.
- With it off, the app refuses to run (`MATLABApp:unknownFileType`), but
  `DeserializerFactory.createDeserializer('DsPlain.m').getAppData()` still builds the full canvas
  (four components with code names, the callback bound) by generating and running
  `createComponents` from the XML. No MAT-file and no MCOS are involved.
- With it on, the app runs, and the callback is wired through `AppManagementService.executeCallback`.
- App Designer's own file checks admit `.m` only with the feature on
  (`validateAppFileExtension`, `DesignTimeController`).

**App Designer is its own window** (`ds0w_dock.m` + `ds0w_dock.ps1`, a window session run with the
user's leave on 2026-10-18; captures in `rt/`, ignored). The desktop was started with `-r ds0w_dock`
and App Designer opened with no app. MATLAB's process then showed two top-level windows: `MATLAB
R2025b`, the desktop, maximised; and `App Designer`, 1416 by 839, with no owner, showing the start
page. The App Designer window had a plain title bar (minimise, maximise, close) and no dock
control. The source agrees: `AppDesignerWindowController.startBrowser` launches its own browser
window titled from `MATLAB:appdesigner:appdesigner:AppDesigner`, and stores its position and state
under the preference group `appdesigner` so the next launch reopens it there. Nothing under
`toolbox/matlab/appdesigner` mentions docking.

Not measured:
- App Designer opening any of these apps in its window;
- whether `load` accepts a component tree that saves fewer internal names than R2025b writes;
- the mapping from each public property to the internal names a class saves;
- App Designer with an app open, and its tabs when several are open;
- any release but R2025b.

## DS1 findings (designer plan, R2025b, headless)

The probes in `ds1/` back stage DS1, the designer's model of an app's components and its
`createComponents` reader and writer (ADR 0243, `src/JGraph.Scripting/Designer`).

**What the generator knows** (`ds1_catalog`). App Designer offers 45 uifigure component classes in
R2025b, and the probe records one entry for each. Each property list is the adapter's
`getCodeGenPropertyNames` on a design-time component; they run from 6 entries (Toolbar) to 103
(UIAxes). Many colour and tag properties have hidden `Mode` siblings, so they are written only once
set. The light theme's defaults equal the unthemed ones. The dark theme changes 68 defaults, nearly
all colours.

**The theme.** With the WebUI on, `DesignTimeController` passes the generator the figure's
`Theme.BaseColorStyle`. All 44 shipped apps loaded in R2025b report `light`.

**The code around the components is JavaScript.** App Designer's code engine
(`appdesigner/codegen/…` in `web/release/bundle.mwBundle.appdesigner.js`) writes the parts of
`createComponents` the generator does not:

- the comments and blank lines;
- the figure's closing `Visible = 'on'`;
- a check box tree's `CheckedNodes` after its nodes;
- a context menu's assignments after its items, under an indented blank line;
- `pathToMLAPP` and the `fullfile` image lines.

It writes the components in the order the tree holds them, each before its children. It reuses a
component's stored `ComponentCode` when the app was saved by the same release, and regenerates and
marks the app dirty when that code differs.

**The properties block lists components in the component browser's order**, not the creation order
(measured over the 44 apps):

- within a container, the most recently made first;
- menus, a context menu's items, tabs, tree nodes and toolbar tools in the order made;
- a figure's menus first and its context menus last;
- a responsive app's panels in the order made.

**The oracle** (`ds1_oracle`). R2025b's generator was run over all 1108 shipped components. In 1080,
the saved tree and the code agree on what is set. In the other 28, the tree holds values the code
never sets: older releases' unthemed defaults that the light theme no longer takes for defaults.

**The fixture** (`ds1_generate`) runs a hand-written `createComponents` in R2025b. The values have to
be ones the components keep, because components coerce on set:

- `RoundFractionalValues` rounds;
- `DisabledDates` becomes a column;
- a button group selects one button;
- checking a node's only child checks the node.

**Two generator branches write broken code** (`ds1_quirks`): a string with a line break comes out as
`"a" + "b"`, and a cell the generator cannot write gets an empty line. No stock component reaches
either one. In 15 cases, text properties kept a char and cells of strings became cells of char;
the other values were refused, or their property is never generated.
## DS1b findings (designer plan, R2025b, headless)

The probes in `ds1b/` back stage DS1b: JGraph writing App Designer's saved component tree
(`appModel.mat`'s `components`) as R2025b's MCOS objects (ADR 0244, `MatMcosWriter`,
`DesignTreeWriter`).

**The subsystem can be written from a model.** `ds1b_mcos.py` parses a level-5 file's subsystem into
names, classes, objects, property lists, dynamic-property lists and stored values. It writes back
R2025b's own trees byte for byte. Two parts `MatMcos` did not need are a second uint8 stream after
the `FileWrapper__` struct (a struct whose `MCOS` is empty), and a last table segment of `1` and
its own offset plus eight. In R2025b's files the int32 per-class cell holds, for
`meta.DynamicProperty` alone, the name `matlab.metadata.DynamicProperty`. Written that way in a
file without saved-time defaults, R2025b warns that `meta.DynamicProperty` "cannot be loaded as an
enumeration". Written as zeros, the file loads without the warning.

**What `load` does with stored names** (`ds1b_reduced`, `ds1b_rules`, `ds1b_setsaves`):

- A tree that stores only some of an object's names loads, with no error. A name left out keeps the
  storage's class default, not what the constructor sets: a button with nothing stored loads with
  empty text and a 20-by-20 size, and a figure with nothing stored is a classic figure with menus.
- Names are assigned in the order stored, through the class's set methods and serialization
  hooks, so a public name works where MATLAB stores a private one. `Value` before `Items` is
  refused as in code, and a name stored twice is assigned once.
- The last per-class cell is each class's defaults at save time, and `load` re-applies them after
  the stored names. Written empty, a stored colour turns its mode to `'manual'` (its setter); a
  mode stored after it wins. The colour and font mixins' `modifyIncomingSerializationContent`
  rename `BackgroundColor` to `BackgroundColor_I`, pick the mode from the value when none is
  stored, and warn when the colour is missing.

**What a fresh component stores** (`ds1b_fresh`, `ds1b_blanked`). Loaded with nothing stored, a
component lacks only what its constructor sets: text, size, alignment, `Value`/`ValueIndex`, tick
labels, a tree's editability, and the uifigure's settings (`IsAppBuilding`, `DefaultTools_I`, no
menu bar, ...). Every MATLAB-saved tree loads with `ParentMode` `'auto'` and
`HasAppBuildingDefaults` `'off'`, since neither is stored.

**Trees written from scratch** (`ds1b_scratch`, over `ds1b_transcribe.py` and `ds1b_build.py`).
They keep only the components, their plain stored values, children, Layout options, position
stores and dynamic properties, with every class's defaults empty. They load as the trees they came
from for 44 of the 45 classes, the U7b app and DataExport. A UIAxes' `PositionMode` and
`PositionConstraintMode` come back `'manual'`.

**What the bases needed beyond a fresh component's names** (found by `ds1b_verify`):

- **The figure's default map.** A uifigure gives the panels, button groups, tab groups, tabs and
  tables made in it its default-property map's values (`InternalDefaultPropMap`) with modes left
  `'auto'`. A component loaded from a tree gets them only when the figure is its parent as it loads.
- **Selection.** A fresh radio or toggle button is its group's selection; in a tree the last one
  attached would win.
- **A tab's size** is its group's when the tab is made, and is not worked out again headless
  (`ds1b_tabs`).
- **An axes** stores its camera and plot box for its default size. Its position stores are applied
  after the stored names, so they would undo a `Position` stored by name.

**Values in their stored forms:**

- A datetime is an MCOS `datetime` storing `data`, milliseconds since 1970 (NaN for `NaT`), and
  `fmt` only when set (`ds1b_dates`).
- An axes' title and labels are text objects: `Title_IS`, `Subtitle_IS`, and each ruler's
  `Label_IS`.
- The default axes toolbar is not saved, so App Designer gives an axes whose toolbar is hidden a
  custom `axtoolbar` (`DesignTimeUIAxesController`).
- The colormap is `Colormap_I` with `ColormapMode` `'manual'`. App Designer names it by `isequal`
  against the 256 rows of each of 19 maps; the setter refuses a name, and `hot(64)` reads as
  `'parula'` (`ds1b_cmap`).

**What App Designer stores per component.** `DesignTimeProperties` holds `CodeName`, `GroupId`,
`ComponentCode` (an N-by-1 cell of the generator's lines before the code model finishes them:
`app.ad_CODENAME_ad`, `app.ad_PARENTCODENAME_ad`, a callback's name in quotes), and
`ImageRelativePath`. The last is added empty by App Designer's `ImageLoader` where missing, and
used to reset `Icon`/`ImageSource`. A shipped `appModel.mat` also holds `code`, `appData` (an
`AppData` object of three empty properties and `MinimumSupportedVersion`) and
`components.Groups`.

**Against the code** (`ds1b_verify`). 40 of the 44 apps (the fixture and 39 shipped, 937
components) load the same as the tree their `createComponents` makes. The other four:

- two Simulink apps, whose Simulink components are not written;
- two apps where an axes' plot-box ratio differs in the fourth digit. The written tree's ratio is
  its final size's; the code's tree keeps one worked out mid-way.

**App Designer's full load** (`ds1b_appdesigner`). All 44 open through App Designer's serializer and
reader with every code name: 1127 components and 372 callbacks bound, every stored
`ComponentCode` equal to the generator's on the loaded component, and no warnings. Their findings
are only the 34 that the code's own tree, run the same way, shares:

- R2025b loses a scalar `Alphamap` when saving an axes;
- a panel's inner position comes back as a fresh one's;
- callbacks no component in `createComponents` uses are bound to nothing;
- the fixture image's moved path.

**App Designer's window** (`ds1b_viewbuild`, then the window session `ds1b_view.ps1` +
`ds1b_view.m`, with the user's leave, 2026-10-18; `ds1b_viewdiff.py` compares). Each app was saved
twice by the serializer: once from JGraph's tree (`rt/view/jgraph`) and once, as the control,
from its code's tree (`rt/view/code`). Each was then opened in the window through App Designer's
document API (`appdesigner.internal.document.AppDocument.open`, which returns once the canvas has
made every component, then `closeNoPrompt`), and the window was captured with PrintWindow.
- All 88 open, and App Designer marks every one modified on opening, control included.
- 40 of 44 match their control pixel for pixel, title bar aside (`ds1b_viewdiff.out`). Of the
  other four:
  - `MultiPaneSimApp` and `aurora` lack the Simulink components that are not written;
  - the first app opened in each run (`ASAP2CDFGen`, `mlexpdlg`) differs only by a 1-pixel settle
    of the app inspector panel.
- Some shipped apps show no text on the canvas in either version, because they set it at
  startup. The fixture's figure is larger than the canvas, so its menu bar and toolbar sit over
  the scrolled content in both.
- The `.mlapp` files hold R2025b's re-save of the loaded objects, not JGraph's bytes. A first
  build stopped at the 900 s limit after 37 apps; a build skips apps already written.

## DS6 findings (designer plan, R2025b, headless)

The probe in `ds6/` backs stage DS6: App Designer's whole model written by JGraph, and a save of an
app made in MATLAB that keeps what App Designer saved (ADR 0245, `MatMcosFile`,
`DesignTreeWriter.Update`, `JgsMlapp.WriteApp`). Every file is opened as it is, through App
Designer's own reader (`DeserializerFactory.createDeserializer(fn).getAppData()`), with no
serializer in between: unlike DS1b's checks, R2025b never re-saves what JGraph wrote.

**Edits to shipped apps** (part 1, `ds6_verify_edits.out`). In each of the 43 apps with a level-5
model, the local test changes the first label's, button's or check box's `Text`, removes the last
label no other component names, adds a button to the figure, regenerates the code and saves it
through the editor's save. All 43 load as saved: every component the edit did not touch (923 in
all) has every property, hidden ones included, as the original's (`ds1b_objdiff`). The changed one
differs in its `Text` alone, the removed one is gone, and the added button has its text and place.
The changed and added components' stored code is R2025b's generator's on the loaded component.
Callbacks are bound as before, and there is no warning the original lacks. The two Simulink apps'
Simulink components, which DS1b cannot write, come through the save untouched.

**What the save must state** (found on the way):

- **A file MATLAB saved keeps each class's values at saving**, beside each object's stored names.
  An object whose hook stores a public name for its `_I` storage lists the storage as renamed (the
  table's last segment, per object id). A new button that stores `FontColor` without that list,
  in a file whose button class keeps `FontColor_I`, makes R2025b warn that the name "already
  exists in the serialization content".
- **Old tables.** Nine shipped models have a version 3 table (two per-class cells, no
  renamed-names segment). One version 4 table leaves that segment out (its offsets are zero).
- **Older saves** stored a grid child's `Position` in its component code, and some containers'
  children in the order made.

**New apps** (part 2, `ds6_verify_new.out`). The local test writes 46 apps whole: the shipped
apps' code, the fixture app and the blank app. Each is opened as written, from its source app's
folder, where an image or page named without a path is found. It is held against a control: the
tree that `createComponents`, as App Designer's generator writes it, makes when run, saved by App
Designer's serializer with the same callbacks and opened the same way. 41 of the 46 have no
finding the control lacks. Of the other five:
- two Simulink apps lack the Simulink components;
- two apps' axes differ in the last digits of the camera view angle and plot-box ratio (DS1b's
  finding);
- the responsive app holds App Designer's responsive-container record, which the control lacks.

The first run found that App Designer's reader refuses a responsive app
(`MLAPPResponsiveAppValidator`, "IncompatibleAppVersion") unless the grid on its figure, and each
panel in that grid, has `DesignTimeProperties.AppTypeProperties.IsResponsiveContainer` true.
It also found that an image or HTML page named in the code without a path loads only from the
app's folder. The part takes about three minutes an app, so it runs in three shards
(`ds6_new1`-`ds6_new3`, `DS6_SHARD`).

**What the headless reader does not show.** App Designer's reader does not lay out a grid, so a
grid child's position is whatever its tree holds: for a new component, its base's placeholder,
which the canvas replaces. The comparison leaves a grid child's position family out. App
Designer's window over these files is open item 149.

## DS7a findings (designer plan, R2025b; node and headless)

The plain-text app format (ADR 0251). App Designer's writer of it is JavaScript in the canvas
bundle, so the oracle is again the bundle's own modules under node (`ds7/oracle/appendix.js` over
`amd.js` and `stubs.js`, copied from `ds5/oracle`): `appendix.json` holds 34 cases - the class part,
each section, escaping, indentation, lists, whole files - whose texts the tests hold the writer to.
The loader and the runtime are MATLAB (`appdesigner.internal.artifactgenerator`, `PlainTextDeserializer`,
`matlab.apps.App`), read for the rules and run by `ds7_plain` over the apps the local test leaves in
`rt/plain/` (`JGRAPH_DS7_OUT`): each as the `.m` written here and as the `.mlapp` of the same design.

- **The format, as the bundle writes it.** The class under `matlab.apps.App` with the component
  properties, the editable section and a bare `methods` callbacks block (the text format's access
  control is undefined); one blank line; `%[appendix]`; the layout section at once (`%---`,
  `%[app:layout]`, `%{`, the XML declaration, `<Components>`, the figure as `<UIFigure name='...'>`
  with its set properties as child elements sorted by name in ordinal order, `<Children>` after them,
  `</Components>`, `%}`); then each other section after a blank line, only when it has content: the
  run configuration (`SingleRunningInstance` only ever `true`, `StartupFcn` the name alone), the app
  details (the five, those set), the internal data (`AppId` for the uuid, `AppType`, the releases,
  the lists as `Product` children whatever the list). MATLAB's save appends two line endings and
  the thumbnail section (its comment line before its own declaration) and writes no final newline.
  Indentation is the class's unit per depth; `& < >` are escaped always, inner quotes as `&apos;`
  and line breaks as `\n` only inside a quote-wrapped value; a component with nothing inside still
  takes two lines; attributes are not escaped.
- **What `DirtyProps` holds.** `getComponentDirtyProps` is the `.mlapp` generator's list
  (`getCodeGenPropertyNames` + `ContextMenu`; a grid child's `Layout.Row`/`Layout.Column` in place
  of `Layout`, no `Position`), each kept by `shouldShowPropertySetCode`, aliased properties under
  their user-facing names (`Title.String`, `Toolbar.Visible`, `Colormap`, `ThemeChangedFcn`), valued
  by `getPropertyValueString`: a name ending `Fcn` as the bare function name, `ContextMenu` as the
  code name, `Colormap` as the map's name, a char wrapped in quotes with its inner quotes left single
  (the loader's `sanitizeComponentPropertyValue` doubles them), a one-char cell unwrapped (but
  `Items`, `RowHeight`, `ColumnWidth`), the rest `propertyValueToString`. The client sorts by name.
- **How R2025b runs one.** `matlab.apps.App`'s constructor refuses unless the feature is on, then
  `initializeMApp` reads the file, cuts the layout and run configuration by `getAppendixByGrammarName`
  (the first `%{` after the grammar line to the `%}` after the last root close tag), answers the
  running instance of a single-instance app (any `SingleRunningInstance` element, whatever its text),
  generates a function into its cache (`tempdir/matlab_<user>/R2025b/.../cache/<sha1 of path + xml>/server/ad_<sha1>.m`)
  - each component by its informal interface with name-value arguments, callbacks wired as
  `@(src,e)ad_ams.executeCallback(app, @(obj, event)app.Name(event), true, e)`, every component given
  a transient `AD_CodeName` - runs it, registers the app and runs the startup function with every
  constructor argument (no name-value parsing at all). The components are assigned and the callbacks
  called from outside the class, so the class's members must be public: that is why the callbacks
  block has no access clause.
- **How it loads one.** `DeserializerFactory...getAppData()` is not gated by the feature: the same
  generator with `isLoad` (the figure `Visible='off'`, `DesignTimeProperties` with the code name,
  callbacks as char-valued constructor arguments), then `PlainTextCodeParser` cuts the class with
  `mtree` under stated assumptions (the components block first, one blank line, the editable section,
  one blank line, the comment, the callbacks block last).
- **R2025b's own defects, met here**: the parser drops the editable section's first line
  (`findEditableSectionCode` cuts from the properties block's `end` keyword and removes three lines);
  a component bound to two callbacks cannot load (`buildCallbackData` hands `strcmp` a comma list:
  `MATLAB:maxrhs`); an axes' `Subtitle.String` and `Toolbar.Visible`, which its own writer emits,
  become constructor arguments MATLAB cannot parse; a quoted `...Callback` name, which its writer
  emits, cannot be parsed at load; a callback whose body is empty - how its own template starts one -
  stops the load (`getCallbackCode` takes the body's last statement: `MATLAB:string:InvalidArgumentSize`);
  `readMATLABCodeText` searches the text for a number instead of
  slicing, so code formatting and help comments fall back to defaults; the exception wrapper of a
  failed load uses the file's path as a format string and loses the message; the run configuration
  is written twice when an app opens with both a startup function and a single instance, and the
  reader then fails on the file. The writer sets `label='...'` on a labelled component while the
  loader reads `labelName`; the loader's `autoReflow` attribute is never written.
- **Results** (`ds7_plain.out`): `DsPlain.m` loads the same as its `.mlapp` (4 components, the
  callback, startup and single instance) and runs: 4 components by code name, the callback bound and
  run, a second construction the running one, `delete(app)` taking the figure; `CallbacksApp` runs
  so (3 callbacks) and cannot load (two callbacks on its edit field, R2025b's own); `DesignerSpecApp`
  runs with its 55 components and 8 callbacks bound (its fixture has no callback code but an empty
  `ButtonPushed`), and the component function R2025b generates from its appendix runs on its own
  (55 components) while `getAppData` stops after it on that empty body.
- **The run half runs the `.m` from a folder of its own.** Beside its `.mlapp`, `feval(app)`
  resolves the name to the package (U7's rule), so a run from the pair's folder is the control's,
  not the plain-text file's - the review caught the first version of this probe doing that. The
  probe copies the `.m` to `tempname/<App>`, puts that folder alone on the path, and checks
  `which(app)` before it counts a run.
- **R2025b's run-side component map lacks a tree's `ClickedFcn` and `DoubleClickedFcn`** while
  its writer writes them like any callback: an app with either cannot run there (the generator
  evaluates `ClickedFcn=TreeClicked` as a call). This build writes and binds them (ADR 0251).

## DS5 findings (designer plan, R2025b; node and headless)

The code view (ADR 0250). App Designer's class code generation lives in its canvas bundle, not in
MATLAB, so the oracle is the bundle's own modules run under node (`ds5/oracle`: `amd.js` loads them
raw from `bundle.mwBundle.appdesigner.js`, `stubs.js` the smallest host stubs, `lifecycle.js`,
`callbacks.js`, `templates.js` the cases); the JSON they write is kept in the tests
(`CodeLifecycleOracle.json`, `CodeCallbacksOracle.json`, `CodeTemplatesOracle.json`) and the
per-class table they read from the component models is embedded (`ds5_callbacks.json`: callback
properties in the inspector's groups, the comment per property with its sign, the default code
with its tokens). `ds5_run` runs the app the designer's edits built (`CallbacksApp`, left in
`ds6/rt/new/` by `ANewAppHoldsAppDesignersWholeModel` with `JGRAPH_DS6_OUT`) and reads it with App
Designer's reader; `ds6_verify DS6_PART=new DS6_ONLY=CallbacksApp` holds its tree against the code's.

- **The startup function keeps its place in the callbacks block.** `CodeController._formatCallbackData`
  gives a loaded startup function Order 0 only when its data carries no Order; App Designer's files
  hold it wherever it stood, and 33 of the 44 shipped apps have it after other callbacks.
- **A callback's comment is measured twice.** `CallbackModel._getGeneratedComment` budgets 75 columns
  with eight counted used by a fresh `CodeFormatter` (four-space indents whatever the file's); then
  `MethodBuilder` wraps each line again by words at 75 less the method's own indentation. The
  per-class comment strings (`uicomponents` `nls/CodeGeneration`) carry their `% `, the mixed
  `Callback function` does not, so the two forms fill a line differently. Five properties name a
  resource R2025b does not have (`PushTool`/`ToggleTool.ClickedCallback`,
  `CheckBoxTree.CheckedNodesChangedFcn`, `ColorPicker.ValueChangedFcn`, `Hyperlink.HyperlinkClickedFcn`)
  and take the mixed comment.
- **A callback no component names reads bare `Callback function`** when loaded so: the model's
  constructor generates the comment before its own listeners exist, and `not associated with a
  component` is written only after a binding is added or removed. Six shipped apps show the bare form.
- **A fresh callback's body is one line of the function's indentation** (12 spaces); one loaded with
  no code is a truly empty line and never takes its property's default code (`LoadedFromDisk`).
  The editable section's templates hold whitespace-only lines (`"    "` around a block) and the
  section is followed by one real blank line.
- **R2025b binds callbacks from the tree on load** (`restoreCallbackComponentData`): the code
  copy's `Callbacks` hold `Name` and `Code` alone; the component's callback property holding the
  function's name is the binding. `SingletonMode` is `'FOCUS'` or absent; `InputParameters` the text `a, b`.
- `ds6_verify`'s `sourceOf` lists JGraph's own apps by name; a new designer-built app is added there.
- **App Designer's editor never parses a body.** `RTCUserEditManager` cuts the user's blocks by the lines it marked
  read-only and hands each callback its lines as typed, so an `if` with no `end` yet is a body line. The layout's
  cut (`JgsMlappLayout.ReadCallbacks`) follows: a function runs from its `function` line, at the first function's
  indentation, to the last `end` at that indentation before the next function or the block's `end` (found in the
  review of DS5; the old cut by block balance made the typed line vanish 70 ms later).
- **`_reOrderProperties` (read in the bundle):** on a move, the component's builder and its descendants' are marked
  removed and re-inserted after the previous component in `componentViewOrder`, each child at its parent's index
  plus one (so a moved container's children come out last first); `_padNames` pads every name again.
