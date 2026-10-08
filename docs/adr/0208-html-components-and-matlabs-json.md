# ADR 0208 — HTML components (`uihtml`), and MATLAB's JSON

## Status

Accepted, 2026-10-05. Stage U9b of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored; section L, decision Q4). It builds on
ADR 0198 (U1: callbacks, frames and the component layer), ADR 0202 (U5: the first `uifigure`
components), ADR 0207 (U9: the isequal-first write) and ADR 0186 (a backend on a Windows SDK
framework, loaded at run time).

## Context

Decision Q4 put `uihtml` in: a component that shows a web page whose script talks to MATLAB. The
page defines `setup(htmlComponent)`; the object it gets has a `Data` property, listeners for
`DataChanged` and for events MATLAB sends with `sendEventToHTMLSource`, and a
`sendEventToMATLAB(name, data)`. MATLAB's side has `Data`, `HTMLSource`, `DataChangedFcn` and
`HTMLEventReceivedFcn`. U0 had measured the outline (`u0_uihtml`) and spiked the host: Edge
WebView2 in `WebView2CompositionControl`, which draws inside WPF's surface so a `uialert` sits
over it.

R2025b runs a uihtml's page headless, so the stage was measured first
(`tools/matlab-checklist/ui-probes/u9b`): `u9b_matrix` (every property against a battery, both
figure kinds), `u9b_forms` (maker and `HTMLSource` forms), `u9b_types` (what its page server
serves, 143 requests), `u9b_bridge` and `u9b_bridge2` (both directions of every conversion, the
order of things, the event objects), `u9b_sends` (one session per value that kills the bridge,
driven by `run-sends.ps1`), `u9b_more`, `u9b_json` and `u9b_num` (`jsonencode` and `jsondecode`
themselves), and `u7_classes` extended with `uihtml`. What they showed shaped the stage:

- **Everything crosses as JSON, through two encoders.** `Data` goes to the page as
  `jsonencode` writes it and comes back as `jsondecode` reads it; `sendEventToHTMLSource` uses a
  different encoder (`NaN` is the text `"NaN"`, a string scalar an array of one, a datetime its
  month, day and year, an on/off state a logical, a named function its name), and the data a page
  sends with an event come back through `jsondecode` with their vectors laid as rows.
- **JGraph's `jsonencode` and `jsondecode` were nowhere near R2025b's.** A JSON array read back as
  a row, matrices and struct arrays had no shape, names were never made valid, numbers were
  written in .NET's round-trip form. R2025b writes numbers its own way: the digits of `%.15g`,
  or `%.17g` when those do not read back; a magnitude from a million up as `d.dddE+n` with at
  least one decimal (`1.0E+6`); one under 1e-4 as `dE-n` (`1E-5`).
- **R2025b's page side has a definite shape**: `htmlComponent`'s own properties are
  `addEventListener`, `removeEventListener`, `sendEventToMATLAB` and `Data`; listeners run newest
  first; `setup` runs after the window's `load`; the event objects are plain objects,
  `{Source, EventName, Data, PreviousData}` and `{Source, Data}`; a page sets `Data` and
  MATLAB's `DataChangedFcn` runs every time, while MATLAB's own writes reach the page only when
  their JSON changes.

## Decision

### The component

- `uihtml` makes `matlab.ui.control.HTML` (Type `uihtml`, 21 properties: the component's common
  ones less `Enable` and a font, plus the four of its own), in a `uifigure` or a classic figure,
  refusing as the U5 makers do under `MATLAB:ui:HTML:*` (`UiHtmlModel`,
  `JgsGraphicsProperties.UiHtml.cs`).
- `Data` takes anything and keeps it as given; a string scalar stays one.
- `HTMLSource` is a character row or a string scalar (`[]`, `""` and a missing string are none).
  A URL (`http://`, `https://`, `ftp://`, `mailto:`, `www.`) is refused. Text naming a file — beside
  the current folder, on the path, or in full, `file:` URLs included — is that file, whatever its
  type; other text ending in `.html` or `.htm`, in lower case, is a file that cannot be found;
  anything else is markup (`hello world`, `missing.HTM`, `data:text/html,...`). The same text
  written again does not reload the page (the isequal-first rule).
- `sendEventToHTMLSource(h, name, data)` is a method in R2025b, and refuses as one: a value that
  is not a uihtml is `MATLAB:UndefinedFunction` for its class, the name must be text, an output
  asked for is `MATLAB:TooManyOutputs`. An event with no data reaches the page as `[]`.

### MATLAB's JSON

`JgsJson` is one tree and five ways through it, and `jsonencode`/`jsondecode` now go through it:

- **`jsonencode`**: a scalar is a value; an empty array of any shape is `[]`; a vector, row or
  column, is flat; anything else nests by its dimensions, the first outermost (a 2-by-2 is
  `[[1,2],[3,4]]`). A character matrix is an array of its rows, a cell is an array of its elements
  in memory order, a struct array an array of objects (nested like a matrix), a datetime its
  display text (`null` for NaT), a table an array of row objects, a `containers.Map` with text
  keys an object. `NaN` and `Inf` are `null` unless `ConvertInfAndNaN` is false; `PrettyPrint`
  indents by two. Complex, sparse and function handles are R2025b's three refusals. Text escapes
  only quotes, backslashes and control characters.
- **`jsondecode`**: numbers and nulls in an array are a column of doubles (null is NaN), booleans a
  logical column, strings a column cell, objects with the same names in the same order a struct
  column; otherwise each element is decoded and, when all are numbers (or logicals, or struct
  arrays) of one size, stacked along a new first dimension, else they make a column cell. A name
  is made a field as R2025b makes it (`1a` → `x1a`, `a b` → `aB`, `a-b` → `a_b`, `""` → `x`,
  `a` twice → `a`, `a_1`). Syntax errors carry R2025b's identifiers and messages, with line,
  column and character.
- **The event encoder** (`EncodeEvent`) and **the event decoder** (`DecodeEventData`), for
  `sendEventToHTMLSource` and a page's `sendEventToMATLAB`, as listed under Context.

### The bridge

- A write of `Data` and a `sendEventToHTMLSource` only queue. At every drain point (`drawnow`,
  `pause`, a wait — the dispatcher's `Drain`) `JgsUiHtml.Flush` encodes them, Data before events
  whatever the order they were written in (R2025b sends a Data written after a ping first), and
  posts them to the model. Data is not sent when its JSON is the page's already, whichever side
  set it. A value an encoder refuses is its warning at the drain point, and nothing is sent.
- What a page does comes back through `ScriptGraphicsCallbacks.NotifyComponent` and the script
  queue: its `Data` is written before the callback is decided, even with no `DataChangedFcn`.
- The page side, run before the page's own scripts (`Bridge/bridge.js`), is R2025b's shape as
  measured. On a load the page's `setup` sees the model's `Data`; a `DataChanged` follows when that
  `Data` is not `[]`; events posted while the page loaded are delivered after `setup`. A `Data`
  JSON cannot write (a cycle) throws in the page, as in R2025b.

### Hosting the page

- **`JGraph.Controls.Web`** (`net8.0-windows10.0.19041.0`, `Microsoft.Web.WebView2`) holds the
  host and is loaded by path, as the Bluetooth backend is: `IncludeHtmlHost.targets` copies it,
  the two WebView2 assemblies and the native loader beside the CLI, the application and the
  tests. Nothing that links to it needs a Windows SDK framework.
- **In a figure's window**, the component is a `WebView2CompositionControl` (`HtmlComponentView`,
  made through `UiHtmlViews` in `JGraph.Controls`), so a `uialert` draws over it.
- **With no window** — under `-batch`, or in a figure that is not shown — the page runs in a
  hidden window of `HiddenHtmlPageHost`: WebView2 controllers on one thread of their own with a
  plain Win32 message loop, so the console launcher, which has no WPF, can host pages. Its
  environment keeps timers running in pages nobody sees. A window's page replaces a hidden one;
  a hidden one never replaces a window's.
- **The page's folder** is served from `https://uihtml.jgraph.localhost/static/<token>/` through
  `WebResourceRequested`, by R2025b's rules (`UiHtmlFolderServer`): the folder and the folders
  under it, nothing above; a folder as its `index.html`; only the file types R2025b serves, with
  its content types (`.txt`, `.mjs`, `.webp`, `.mp3`, `.bmp`, `.csv` are 404s there too); GET and
  HEAD, 501 for anything else. Markup goes through `NavigateToString`, by way of a file when it is
  too long for that. The page's network access is not restricted, as in R2025b.
- WebView2's data goes to `%LOCALAPPDATA%\JGraph\WebView2`, not beside the program. DevTools and
  the default context menu are off.
- **With no WebView2 runtime** a uihtml keeps its state, draws a blank rectangle, and the session
  is told once (`JGraph:uihtml:noBrowser`, with where to get the runtime).

### What the stage changed elsewhere

- `jsonencode` and `jsondecode` are R2025b's (above); a unit test that asserted the old row
  shape of `jsondecode('[1,2,3]')` was wrong and now asserts a column.
- A character row read with two subscripts (`v(1, :)`, `v(:, 1)`, `v(1, end)`) is read as the
  1-by-N character matrix it is; it was refused.
- `isa(e, 'event.EventData')` is true for every component event (`matlab.ui.eventdata.*`); it was
  true only for U1's classes.
- A component's empty text property takes only a 0-by-0 `[]` as "no change"; `zeros(1,0)` is
  refused, as R2025b's isequal-first write refuses it.
- A builtin can declare its MATLAB output count where the measured table has none (a method):
  `sendEventToHTMLSource` declares none.
- The parity fixtures' runner installs a stand-in page (`EchoHtmlPageHost`) that plays the echo
  page and the bridge, with a browser's `JSON.parse` and `JSON.stringify`, so the bridge fixtures
  run with no browser and no window.

## Found on the way

- **R2025b runs a uihtml's callbacks once more for every further `HTMLSource` it is given**: after
  the eighth source, each event the page sends runs `HTMLEventReceivedFcn` eight times. The
  fixtures load each component's page once.
- **One complex or sparse value in a `sendEventToHTMLSource` ends R2025b's bridge for the
  session**: a warning, and no uihtml answers again, a new one included. `u9b_sends` needed a
  driver that starts a session after each value that does this.
- **`jsonencode` of a duration fails in R2025b** with an internal warning about the class's
  `Format` property; `sendEventToHTMLSource` sends it as `[]`.
- A page's `sendEventToMATLAB()` with no name, or a `Data` that JSON writes as nothing
  (`undefined`, a function), makes R2025b's MATLAB side print an internal error and do nothing.
- The first copy target for the host gave its `None` items a `Link` made from their own metadata,
  which MSBuild applied to every other `None` item of the application: the build wrote the host's
  DLL over the output's `jgraph.ico`, `make-splash.m` and `splash.apng`. The target now gathers its
  files as an item of their own first; the three outputs were removed and rebuilt from source.
- A label's UI Automation name does not follow its text, so the window check read results from
  the window's picture.
- `record-matlab.ps1 -Fixtures a,b` run through `powershell -File` takes the list as one name.

## Consequences

- MATLAB's documented use — a page that shows `Data`, sends its own changes back, and answers an
  event from MATLAB — runs headless in R2025b and JGraph alike and in JGraph's window
  (`ui-probes/research/apps/examples/ex7_html.m` and `.html`; `u9b/ex7_drive.m` presses its
  buttons headless).
- Any script that read JSON now gets R2025b's shapes: columns, matrices, struct arrays, valid
  field names.
- The callable count rises by one to 1,221 of 2,024 (`uihtml`; `sendEventToHTMLSource` is a
  method in R2025b and not in the list).

## Measured

- **Headless probes** (`ui-probes/u9b/`): `u9b_matrix`, `u9b_forms`, `u9b_types`, `u9b_bridge`,
  `u9b_bridge2`, `u9b_sends` (through `run-sends.ps1`), `u9b_more`, `u9b_json`, `u9b_num`,
  `u9b_nargout`, `ex7_drive`; `u7_classes` extended and the class table regenerated. The pages
  they load are in `u9b/site` (`make_site.py` writes the file types).
- **Parity fixtures**, recorded with `-noFigureWindows`: `u9b_json` (305 lines), `u9b_props`
  (919), `u9b_forms` (137) and `u9b_bridge` (270). Lines that could not agree are recorded
  divergences (`div=`), or were taken out and are listed below.
- **Unit tests** (`UiHtmlU9bTests`): the page server's rules, the number layout, which page a
  component keeps, what is sent at a drain point and in what order, the warning at the drain
  point, the warning with no browser, a page's Data and event coming back — and the echo page in
  a real WebView2 in a hidden window (it runs when the runtime is installed, as it is here).
- **The JGraph window check**, in the Release app under `-batch -showfigures`, by UI Automation
  patterns only (Invoke), the window captured alone with PrintWindow, `workspace.json` unchanged:
  `ex7_html`'s page drew in the window with MATLAB's `Data` (0, then 10 from "Count from MATLAB");
  "Greet the page" showed the greeting in the page and the page's answer in MATLAB ("greeted:
  17"); "Alert" put the `uialert` and its shade over the page.

## Not measured

- **R2025b's look**, and what its page shows of a markup source's address: no MATLAB window
  session was run.
- **A page's own button in JGraph's window**: the composition control does not show the page to
  UI Automation, and no input was synthesized. A page's `Data` coming back is covered by the real
  WebView2 test and by `ex7_drive`.
- **`exportapp` and `getframe` of a figure with a page**: the U0 spike showed WPF's capture
  includes the composition control's page; this stage did not run either on one.
- **The cost of an animating page in a window** was the spike's (14–18 % of a core), not
  measured again.

## Divergences

- **Pages run in Edge (WebView2), not in MATLAB's bundled Chromium 128.** A page that reads its
  browser or its address sees Edge and `https://uihtml.jgraph.localhost/static/<token>/` rather
  than `https://127.0.0.1:<port>/static/<id>/<uuid>/`.
- **Without the WebView2 runtime** a uihtml keeps its state, draws nothing, and the session is
  told once.
- **Callbacks run once per message.** R2025b runs them once more for every further `HTMLSource`.
- **An event no encoder can write is a warning and nothing more.** R2025b's bridge also stops for
  the rest of the session.
- **An event sent while a page loads is delivered once it has run `setup`.** R2025b drops one sent
  while a new source loads (and delivers one sent while the component had no parent).
- **A figure shown after its uihtml's page started gets the page again in its window**: the
  hidden page is closed and the window's runs `setup` anew. R2025b keeps one page.
- **A page's event with no name, or a `Data` JSON writes as nothing**, is ignored quietly; R2025b
  prints an internal error.
- **`jsonencode` of a duration** is its display text; R2025b fails with an internal warning.
- **Handles are numbers** (ADR 0051): `jsonencode` of a figure is its number where R2025b refuses
  the recursion; `sendEventToHTMLSource` of a deleted uihtml is refused as a number is
  (`u9b_forms`, `send_deleted`).
- **A categorical is a cell here**, so a 2-by-2 one is written in memory order and a scalar one is
  sent as an array of one.
- Taken out of the fixtures, with the gap filed as open items: a component's class name and
  `properties(h)` (handles are numbers), dot access of an unknown name on a component (R2025b's
  `MATLAB:noSuchMethodOrField` and `MATLAB:noPublicFieldForClass`), and a component left with no
  parent (ADR 0202, open item 48, a `div=` line in `u9b_forms`).

## Still open

- A MATLAB window session for `uihtml` (open item 67).
- Methods and unknown names through the dot on graphics handles: `h.expand()`,
  `h.sendEventToHTMLSource(...)`, `methods(h)`, `properties(h)`, and R2025b's errors for an unknown
  name (open item 68).
- `exportapp`/`getframe` with a page, markup pages' relative addresses, and DevTools in Options
  (open item 70).
