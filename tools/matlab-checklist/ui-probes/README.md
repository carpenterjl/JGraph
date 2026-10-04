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
