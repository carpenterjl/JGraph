# ADR 0280 — The designer window on R2025b's App Designer toolstrip

## Status

Accepted, 2026-10-28. Stage W2 of the App Designer window plan, after the toolstrip control (W1,
ADR 0279).

## Context

The designer window (designer plan DS2–DS8, ADRs 0243–0254) had a flat WPF `ToolBar`: Run, Open,
Save, Save As, Undo, Redo, Delete, the grid toggles, the interval, alignment hints, Grouping, and
the Code View's insertions. Stage W0 recorded App Designer's toolstrip and its 202 actions from
R2025b's client; stage W1 built a control that renders such a layout. W2 puts App Designer's
toolstrip in the window, with the window-level pieces the plan gives it: the document tab's menu,
the reload prompt for a file written outside, Ctrl+Q, the Keyboard Shortcuts dialog, F1, App
Designer's settings with a page in Options, the window's place remembered, and the library's
search box.

## Decision

- **The recording is re-run with the rich-text editor's icon provider real.** W0's node host
  stubbed every `rtc/` module but the feature enum, so the editor buttons (Pause, Continue, Step,
  Step In, Step Out, Stop, the breakpoint items, Print, Go To, Find, Bookmark, Comment, Uncomment,
  Wrap Comments, the three indents, the splits, the display toggles, the folds, the Code View's
  zoom) came out without icons. `rtc/RTCToolstripMetaDataProvider` has no dependencies, so
  `oracle/host.js` now runs it unmodified; `toolstrip.js` and `actions.js` were re-run and every
  control has R2025b's icon name (`stepForward`, `comment`, `indentCodeAuto`,
  `splitDocHorizontally`, ...). The provider's web set is the one App Designer uses (its
  `useJavaIcons` is never called). `ToolstripIcons` gains 22 drawings and renames W1's guesses
  (`step`, `stepIn`, `stepOut`, `print`, `goTo`) to the recorded names. The words those buttons
  lack are R2025b's too: CODE's buttons are icon-only beside the row labels "Comment" and "Indent".
- **`DesignToolstrip`** (`JGraph.Scripting/Designer`, UI-free) reads the embedded `w0_toolstrip.json`
  through `ToolstripConfigReader.ReadRecording`, sets the grid interval spinner's limits and step
  from `DesignGeometry` (10 to 50 by 5), names the tags the window needs, gives the two views' tab
  groups from the recording's contexts, and transcribes `ExecutionControlsToolstrip`: one of Run,
  Pause and Continue shows in RUN's first column - Pause while the app's code runs, else Continue
  while debugging, else Run - and Step In and Step Out only while debugging, the rest taken out of
  their columns so the one left draws large. `${0}` becomes "App" (a component's "Component" waits
  for W15).
- **`DesignActions`** registers all 202 actions from the embedded `w0_actions.json` with their
  words and the stage that makes JGraph run each: 45 run now (the file commands JGraph had, Run,
  the grid and grouping commands, the grid layout actions of the canvas menu, the Code View's
  insertions and Code Options, Find through the code editor's own search panel, Cut, Copy and Paste
  in the Code View, Help, the shortcuts dialog and the tab menu's actions); the rest wait for W3
  (New, the Start Page, examples), W4 (Align, Same Size, Space, Reorder, Tab Order, Add Label, the
  font keys), W5 (Duplicate, Select All, menus, trees, toolbars), W6 (zoom, Fit to View, resizing
  hints), W8 (App Details, Help Text, Save Copy As, Export, Convert), W9 (the editor's tools), W11
  (execution and breakpoints), W12/W13 (Share), W14 (Compare To) and W15 (Configure for Apps,
  events, managed properties), or stay out (X1 Simulink, X2 Web App, X3 GUIDE, X9 Spelling). A
  control whose action waits is shown, disabled, with R2025b's words; a drop-down whose popup has
  nothing JGraph runs is disabled too.
- **The window** (`AppDesignerWindow`, now partial: `.Toolstrip`, `.Tabs`): the toolstrip replaces
  the toolbar; each control's action id resolves to a command of the window (the application
  commands where WPF has them, routed commands otherwise), sent to the window as W1's
  `CommandTarget`. The view sets the context: DESIGNER, CANVAS, VIEW in the Design View; DESIGNER,
  EDITOR, VIEW in the Code View; no contextual tab with no app; SIMULINK never. The app's Callback
  button shows, the custom component's split Callback, Event and Configure for Apps do not. The
  toggles and the spinner keep settings and answer through `Invoked`; Alt+G, Alt+P and Alt+Page
  Up/Down do the same from the keyboard. Cut, Copy, Paste and Find reach the Code View's editor
  surface and are disabled on the canvas until W5. Run shows Pause while the runner's call is
  under way. The single-instance toggle is gone from the window, as R2025b has none on its
  toolstrip: App Input Arguments opens Code Options, which has it.
- **The title** is R2025b's `_getAppTitle`: "App Designer - " and the full file name, its folder
  dropped when `window.ShowTitleFilePath` is off and its extension when
  `window.ShowTitleFileExtension` is off, "*" when the app has changes; "App Designer" with no app.
- **The document tab's menu** (`DesignTabMenu`) is R2025b's two halves: `AppTabActions` gives a
  saved app's Change Current Folder to (disabled for the folder this window last made current),
  Add to or Remove from Search Path, Show in Explorer, Copy Full Path to Clipboard, then New App and
  Compare To (both waiting for their stages); `DocumentContainer._populateMenu` adds a separator,
  Close, and with more than one tab Close All Except, Close All to the Right (when there are tabs to
  the right) and Close All, then Alphabetize. The folder is named as `PlatformUtil.abbreviateAppFilePath`
  names it: whole under 30 characters, else its leading parts under 15 characters, "...", and its
  trailing parts under 15. The current folder and path items run `cd`, `addpath` and `rmpath` in
  the MATLAB session, unechoed, through the host's `SessionStatement`.
- **The reload prompt**: a watch on each app's file asks R2025b's "{0} has been modified outside of
  App Designer.\n\nReload the file?" (with "\n\nUnsaved changes will be lost." when the app has
  changes; `appdesigner.xml`'s `mlappFileModifiedOutside` and `...WhileAppIsDirty`) and reopens the
  app in its tab's place on Yes. The window's own saves are not asked about, and one write is asked
  about once however many changes the watch reports for it (a write's time is remembered).
- **Ctrl+Q** closes the window (and asks about each app's changes); **Ctrl+/** shows the Keyboard
  Shortcuts dialog, transcribed from `ShortcutsDialogContent`: "App Designer Keyboard Shortcuts",
  R2025b's fixed table of actions and keys (not built from the action list, as the plan had
  supposed), "For more shortcuts, see the Documentation", "Do not show again on session start" kept
  in the `confirmationdialogs.ShowShortcutsDialog` setting, Close. R2025b shows it at session start
  only behind its `ShortcutsDialogSupport` feature, off by default, so JGraph never does. **F1** opens
  the scripting guide's App Designer chapter.
- **Settings** (`DesignSettings`, WO7) by R2025b's names with W0's factory values: the eight its
  groups list (`AutoSaveOnClickAway`, `compbrowser.IncludeComponentLabels`, `designview.GridInterval`,
  `ShowAlignmentHints`, `ShowGrid`, `ShowResizingHints`, `SnapToGrid`, `history.MaxFileListSize`), the
  ten the client reads by name, and the shortcuts dialog's. The host keeps the values that differ
  from the factory in `settings.json` (`AppDesigner`, a map by name) and the window's place
  (`AppDesignerPlacement`). Options gains an App Designer group in the words of R2025b's settings
  page (`resources/MATLAB/en/appdesigner/preferences.xml`): Canvas (Show grid with interval: ...
  pixels, Snap to grid, Show alignment hints, Show resizing hints), Component Browser (Include
  component labels in Component Browser), Most recently used file list (Number of entries: 0 to 12),
  Automatic file changes (Save changes upon clicking away from an app). An open designer takes a
  change made there at once. With "Save changes upon clicking away" on, every app with a file and
  changes is saved when the window loses the focus.
- **The library's search box** ("Search") shows the entries whose names hold what is typed,
  ignoring case, and hides the sections left empty. The "Layout Managers" label the plan named is
  not used: R2025b's library puts Grid Layout under Containers (DS3), and the catalog's
  `layoutmanagers` key goes unread.
- **The Options dialog's OK keeps what it does not edit**: it built a new `UserSettings` from its
  own fields, so App Designer's settings and the window's place are now carried through it.

## Consequences

- Each later stage turns its actions live by adding them to `DesignActions`' live set and a
  command to the window; the toolstrip, the tab menu and the keys follow.
- W3 makes New, the Start Page and Recent Files (whose length the MRU setting already keeps).
- W11 drives `ExecutionControls` with a real debugging state.
- `SessionStatement` is the door later stages use for the session (W8's Run with inputs, W11's
  `dbstop`).

## Measured

- **Tests**: `AppDesignerToolstripTests` (48): the embedded layout and the interval's limits; the
  quick access bar; the editor buttons' recorded icons and no button without one; the two views'
  tab groups; the execution controls in the four states; `${0}`; the 202 actions and every id the
  toolstrip names registered; 28 actions' stages; the W15 set exactly; the shortcuts table; the
  settings' factory values, overlay, clamp and change events; the title's four forms; a saved
  app's tab menu, with the folder current and on the path; several tabs' close items; four
  abbreviations. With W1's `ToolstripTests` and the designer, settings-format and App Designer
  suites: 744 pass.
- **The window check** (`run-w2designer.ps1`, UI Automation patterns, JGraph.Application in Release
  on a copy of the `U7MlApp.mlapp` fixture opened by `-r "appdesigner(...)"`): the title; DESIGNER,
  CANVAS, VIEW; the quick access bar; Run alone in its column with Pause, Continue, Step In and Step
  Out out of it; Save enabled; App Details, New and SHARE disabled with R2025b's words; Align Left
  disabled and Grouping enabled; Grid off then on; the interval set to 30; Resizing Hints disabled;
  both kept in `settings.json` by R2025b's names; the library searched for "gauge"; the Code View's
  DESIGNER, EDITOR, VIEW with EDITOR selected; Comment disabled with R2025b's name; Private
  Function chosen from the Function popup, the title gaining "*", Undo from the quick access bar
  and the "*" gone; the file written from outside, the prompt in R2025b's words, answered No, no
  second prompt, the app as it was. All 39 checks pass; the user's settings file is copied before
  and put back after (there was none, and none is left). W1's `run-w1toolstrip.ps1` still passes
  over the re-run recording (54).

## Not measured

- **The keys** (Ctrl+Q, Ctrl+/, F1, Alt+G, Alt+P, Alt+Page Up/Down): the check synthesizes no input,
  so the shortcuts dialog and help are not opened in it.
- **The document tab's menu in the window**: UI Automation does not open context menus; the items
  are checked by `DesignTabMenu`'s tests, the window's handlers are not.
- **Run's Pause swap** while an app runs, and Run itself: the check starts no MATLAB session run.
- **"Save changes upon clicking away from an app"**: R2025b's own trigger was not read; JGraph saves
  when the window loses the focus.
- **The window's place** across a real close and reopen (the check ends the process).
- **The Options page** in a window.

## Divergences

- **The Keyboard Shortcuts dialog leaves out "Perform a universal search"** (Ctrl+Shift+Space):
  JGraph has no universal search.
- **Change Current Folder knows only the folder the window made current.** R2025b's tab menu
  follows every change of the current folder; JGraph's session does not report a `cd` typed at the
  prompt, so the item stays enabled until the window itself changes to the app's folder.
- **The document tab's menu has no Maximize, Undock, tiling or tab position items**: JGraph's
  designer window has none of them.
- **Help (F1 and the shortcuts dialog's Documentation link) opens JGraph's scripting guide** at its
  App Designer chapter, not MathWorks' documentation.
- **The library's search matches an entry's name only**, ignoring case: R2025b's search rule was
  not read.
- **The App Designer settings page has no Editor group**: R2025b's page shows "Font size" and
  "Enable app coding alerts", but neither is an App Designer setting in R2025b's `settings()` (W0);
  the code view's font is the editor's.
