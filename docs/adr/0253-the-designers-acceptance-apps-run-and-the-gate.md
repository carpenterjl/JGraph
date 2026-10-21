# ADR 0253 — The designer's acceptance apps, Run and the gate

## Status

Accepted, 2026-10-20. Stage DS8, the last, of the visual app designer plan
(`docs/plans/app-designer-plan.md`, gitignored). It closes the arc ADRs 0243–0252 built: the code
reader and writer (DS1), the saved component tree (DS1b), the whole model (DS6), the canvas (DS2),
the library, names and inspector (DS3), grids (DS4), responsive apps (DS4b), the code view (DS5), the
plain-text format (DS7a) and custom components (DS7b). App Designer's Run was read from R2025b's
own sources (`appdesigner.internal.model.AppModel.runApp` and `runAppHelper`,
`AppManagementService.runApp`, `runMApp` and `prepareCommand`, `DesktopAppRunner.fireAppError` and
`isBadNumberOfArguments`, `AppInstanceCloser`, `isSameNameAppRunning`, `isAppNameShadowed`, and the
message texts in `resources/MATLAB/en/appdesigner/appdesigner.xml`); nothing of it is kept. The
acceptance apps were run in R2025b headless and in this build's batch runner
(`tools/matlab-checklist/ui-probes/ds8`).

## Context

The plan gave DS8 three parts: a guide chapter, acceptance apps built in the designer and run in
R2025b `-batch`, and the gate. Three rows of the plan's workbook were still open from DS1 (ADR 0243,
"Found with it"): the component coercions the writer could not make (L5), the figure's theme in the
writer (L6) and the generated code around the components (L7). DS3 (ADR 0247) had taken the theme
and the coercions a set goes through, and DS4b and DS5 (ADRs 0249, 0250) the generated code, so the
rows wanted checking and closing rather than building; the check found one coercion the design did
not take back. And the window had no Run: the plan's ask ("press Run") and its architecture sketch
("Run saves, clears the class, and runs the app exactly as a user call would, which is MATLAB's own
`AppManagementService.runApp` behaviour") were not yet met, since every stage had run its apps from
the editor or a test.

## Decision

### The coercions on set are the components' (L5)

A value set in the designer goes through the component and the design keeps what the component
kept (ADR 0247). Of DS1's four coercions, three already came back that way: a spinner with
`RoundFractionalValues` on holds 4 for 3.5, a date picker keeps its `DisabledDates` as a column, and a
button group keeps one button selected, so a lone toggle button reads `Value = true` and refuses
`false`. The fourth did not: `CheckedNodes` was never read back (a list of components has no
expression the runtime read), so checking a node's only child left the design holding the child
alone while the component held the parent too, as R2025b keeps it (probe `u9`: the nodes written,
their descendants, then the parents whose children are all checked). `DesignRuntime.Read` now reads a
check-box tree's `CheckedNodes` from the component - each handle mapped back to the component it was
made for, in the component's order, bracketed when more than one - and the design, the generated
line and the tree follow. The theme (L6) and the generated code (L7) are DS3's and DS4b/DS5's as
built, and their rows are closed with this stage.

### Run

`AppDesignerWindow` gains Run (F5) at the front of its toolbar. It takes the Code View's text first,
saves the app when it has changes - a new app through Save As, as App Designer asks for a file before
its first run - and hands the file to the workspace, which runs it in the MATLAB session under the
prompt's ceremony (the session taken while idle, the result shown in the console as a statement's,
the status line saying what the session answered). The session's side is `IScriptAppRunner`, a
capability of `JgsReplSession` beside `IGraphicsEventSession`, and `DesignRun.Run` is App
Designer's `runApp` transcribed:

- the file must be there: one deleted or renamed on disk is saved again by the window first, silently,
  as `runApp` saves it, and only a save that fails refuses
  (`MATLAB:appdesigner:appdesigner:RunFailedFileNotFound`, "Error running 'X.mlapp'. ... not found");
- an app of the same class running from another file refuses (`SameNameAppRunning`: "An app named
  'X' is already running. Please close it before running a different app with that name."), the
  instance the designer itself launched passed by as `isSameNameAppRunning` passes `RunningApp`;
- `clear(command)` and `addpath(appPath)`: the file's name is unloaded and its folder put at the
  front of the path, so the name finds this file unless something outranks it;
- the name must then resolve to this file, as `isAppNameShadowed` asks of `mdbfileonpath`: a file
  of the name in the current folder refuses with `AppNameShadowedByCWD` ("Unable to run the app
  because \n<file> shadows it in the current working folder.\n\nTo run the app, change the MATLAB
  current folder."), anything else of higher precedence with `AppNameShadowedByPrecedence`;
- the instance the designer's last Run of this file made (App Designer's `RunningApp`, kept by the
  session per file) is deleted first (`AppInstanceCloser`: `delete(app)`, the class's destructor,
  which deletes its figure); an instance started at the prompt is left up, unless the app's code
  allows one running instance and the designer has none of its own, when the prompt's is closed so
  that the design environment takes over (`closeExternallyExecutedSingletonApp`);
- the constructor is evaluated in the base workspace, `evalin('base', 'Name();')`: the instance is
  answered and nothing is bound there, a plain-text app the same way through `matlab.apps.App`;
  whatever the run registered to a figure of the file is the designer's running app, whether or not
  the constructor got to the end (`getRunningApp`);
- a startup function that wanted arguments Run did not give - the error raised by the startup
  function itself, as `isBadNumberOfArguments` reads the stack - leaves a half-made app, which is
  deleted, and the error is App Designer's over the cause (`TooFewAppArgumentsError`: "Cannot run
  with provided input arguments." - `fireAppError`, `MATLAB:minrhs`); an error from deeper, or
  before the app was registered, is reported as it is and the app stays up, as there.

A path said with forward slashes, as a command line says one, names the same file as the session's
backslashes (the first window check ran the app twice side by side for that).

### The acceptance apps

`AppDesignerAcceptanceTests` builds three apps through the designer's own edits, as its window makes
them - add from the library, set in the inspector, callbacks from the Callbacks tab, their bodies
typed into the Code View over App Designer's default lines, Apply Grid Layout and cells, the two-panel
template - saves them and opens them again whole: `AcceptForm` (absolute layout: a numeric edit
field, a spinner that rounds, a drop-down of two operations, a check box, a date picker with two
disabled dates, a check-box tree, a Compute button and a result label; `startupFcn(app, n)` puts the
argument in the field, the button's callback computes into the label), `AcceptGrid` (a slider, a
gauge and an axes in the cells of the gridified figure, the slider's callback moving the gauge and
plotting), `AcceptPanels` (the responsive template with a button and a label) and `AcceptFormPlain.m`
(the form saved as plain text). With `JGRAPH_DS8_OUT` they are left in the probe folder, where
`ds8_accept.m` constructs each, calls its callbacks as the window would, prints its state and
deletes it - in R2025b's `-batch` through `run-probe.ps1`, and in this build's through
`jgraph.exe -batch` - and the two outputs are held line for line.

### The guide

`docs/jgs-scripting-guide.html` loses the note that JGraph has no layout designer and gains
"Designing an app": how App Designer opens here, the canvas, the library and custom components, the
names, the inspector and the coercions, grids and responsive apps, callbacks and the Code View,
Save, Save As and Run, what the acceptance apps showed, and what is not there yet (the open items
150-163 in a line).

## Consequences

- The designer plan is complete: an app can be laid out, wired, saved in either format, run from
  the designer, and opened and run in MATLAB R2025b.
- A check-box tree's checked nodes are written as the component keeps them, parents included.
- `IScriptAppRunner` is how a host runs an app as App Designer does; the editor's Run button is
  unchanged (it runs a document as a file).
- No script behaves differently.

## Measured

- **Both engines, headless** (`ds8_accept`, `ds8_accept.out` against `ds8_accept.jgraph.out`): the
  13 printed lines are the same in R2025b and JGraph - the startup argument in the field, the spinner
  at 4, Add 9 and Multiply doubled 40, the disabled dates a 2-by-1 `datetime` column, the checked
  nodes `Node2` alone (its parent has other children), 12 children of the figure; the grid's 8 rows
  and 3 columns with the spans written, the slider's callback setting the gauge to 40 and plotting a
  line whose last y is 400; the panels app with `AutoResizeChildren` off and 2 columns, the button
  writing `2 columns`, and after narrowing to 500 px with `updateAppLayout` run 2 rows and 1 column
  with the right panel in row 2; the plain-text form running with 7 and adding to 11. Two misses in
  the driver before the lines matched were identical in the two engines: App Designer's naming rules
  had renamed the check box from its text and the figure from its title, so a callback typed against
  the old names failed the same way in R2025b.
- **The tests**: `AppDesignerAcceptanceTests` (2: the four coercions, with the checked-node read-back
  and its bracketed line; the three apps built, saved and reopened, the plain-text copy too) and
  `AppDesignerRunTests` (5: one running instance of the saved app with its folder on the path and
  nothing bound in base, a second run closing the first, the plain-text form run; the three refusals
  in App Designer's words; a startup wanting arguments deleting the app and saying so, a
  forward-slash path closing the instance; an instance started at the prompt left up through two
  runs and a single-instance app's prompt instance closed; a missing-argument error from an anonymous
  function in the startup reported as it is with the app kept, a shadowing file in the `cd` folder,
  a file deleted on disk saved again and run). Every designer test passes (648); the
  full suite passes (11,945 in the run before the review's fixes; the run after had 11,944 pass and
  three `MatFileV5CompletionTests` refusals fail that pass alone and passed in the run before, a
  suite-held state like open item 155, recorded as open item 164).
- **Review before the commit** (a workflow of two Opus reviewers - the Run transcription against
  R2025b's sources with the session refactor, and the window, workspace, read-back, tests and
  documents - every finding sent to two Opus skeptics; 9 confirmed, 1 refuted as a leftover): Run
  deleted every running instance of the file, an instance started at the prompt included, where
  App Designer closes only the one it launched (and an external one for a single-instance app);
  every `MATLAB:minrhs` from the construction became `TooFewAppArgumentsError` with the app deleted,
  where App Designer converts only an error raised by the startup function itself; the same-name
  check refused the designer's own instance after a Save As; the current folder was read from the
  app's name rather than the session's `cd`; several checked nodes kept a single node's unbracketed
  form, which writes a comma list; a file deleted on disk refused instead of being saved again; the
  status line showed a multi-line message whole; the guide and the ADR claimed what the code did not
  do (the instance closed, a variable refusing). All fixed under tests; the package-folder case is
  recorded (open item 163).
- **In a window** (`scratch/ds8w/ds8w.ps1`, the Release app opened by `appdesigner(...)`, UI
  Automation patterns only, nothing typed): Run on `AcceptGrid.mlapp` brings up its window in the
  same process with the slider, the gauge and the axes drawn, and a second Run leaves one window, the
  first instance closed; Run on `AcceptForm.mlapp`, whose startup takes `n`, leaves no window and the
  status line reads "Cannot run with provided input arguments." over its cause; neither run rewrote
  the file (the apps had no changes); `workspace.json` unchanged. The first version of this build
  left the first instance standing (the builtin `delete` takes graphics handles, not a class
  object) and did not match a forward-slash path to the session's: both found by the window check.
- The seven `verify-*.py` scripts, the divergence harvest and the guide at the gate.

## Not measured

- **App Designer's Run in R2025b** runs only in its window; its rules were read, not probed, and the
  message texts are its resource strings. Run with arguments, its error alert, the screenshot it
  captures on the first run after a save, Run of a custom component's class and the close of a
  singleton started outside the designer are left (open item 163).
- **Precedence shadowing by another path folder**: `addpath` puts the app's folder first, as App
  Designer's does, so only a built-in, a function of the name or the current folder can outrank the
  file here; a variable of the app's name is not in the name resolution asked (the MATLAB dialect's
  function layers), as it is not in R2025b's `mdbfileonpath`, so the run fails indexing the variable
  in both.
- **An app saved inside a `+package` or `@class` folder**: R2025b runs it by its qualified name with
  the path split at the MCOS folders (`PathUtil.getAppRunCommandFromFileName`, `getPathToApp`); the
  designer runs the bare name from the file's own folder (open item 163).

## Divergences

- **A run-time error from Run goes to the console and the status line.** App Designer shows an
  alert in its own window (`addErrorAlertListener`, the trimmed exception) besides the Command
  Window; here the error is printed as a statement's and the status line repeats its first line.
