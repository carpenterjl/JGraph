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