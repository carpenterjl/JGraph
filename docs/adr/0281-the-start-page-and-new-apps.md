# ADR 0281 — The Start Page, new apps and the shell's entry points

## Status

Accepted, 2026-10-28. Stage W3 of the App Designer window plan, after the designer window on the
toolstrip (W2, ADR 0280).

## Context

After W2 the designer window opened only a file it was given: `appdesigner` with no argument showed
an empty window, DESIGNER > New and the tab menu's New App were disabled, a new app was made in the
shell's script editor as class text, and closing the last app hid the window. R2025b's App Designer
starts on its Start Page, makes new apps there and from its New, lists recent files there and under
Open, and offers example apps. W3 builds those, JGraph's example apps (decision WO2), and the shell's
ways in.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`: the main bundle's
`appdesigner/view/dialogs/startscreen/*`, `StartScreenActionDefinitions`, `RecentlyOpenedFilesManager`,
`MRUArray`, `_AppFactory`, `AppViewController`, `DesignerTab`, `UIContainerManager`; the start
screen's own bundle `startscreen/release/bundle.startscreenindex.js`; `createNewApp.m` and
`AppDesignEnvironment.m`).

## Decision

- **The Start Page** (`DesignStartPage`, UI-free; `DesignStartPageView`, WPF) is R2025b's
  `StartScreenLayout`: a header; on the left the open menu (`OpenMenu`) - "Open..." and "Recent
  Files", each file by its name without extension with "App" or "Custom UI Component" under it and
  its full name as its tooltip, or "No Recent Files"; in the centre the links bar and a part per
  section (`AppTypeSectionWidget`) with its title, a tile per new document, and "Show examples" /
  "Hide examples" over its example groups, hidden at first. The sections are `StartScreenConfig`'s
  less Simulink Apps (X1): **Apps** (Blank App, 2-Panel App with Auto-Reflow, 3-Panel App with
  Auto-Reflow; then "Examples: General" and "Examples: Programming Tasks") and **Custom UI
  Components** (Blank Component, Blank Component with Grid Layout; no examples, so the examples part
  is dropped as R2025b drops an empty one). Every word is R2025b's (`StartScreenActionDefinitions`
  and the `Dialogs` catalog).
- **Where the page shows.** R2025b's window starts on the page when `appdesigner` has no file (the
  start screen's own bundle), and opens it as the modal "App Designer Start Page" dialog
  (`StartScreenDialogController`) for DESIGNER > New, the tab menu's New App and when the last app
  closes. JGraph shows it in the window in place of the panes whenever no app is open - at start and
  after the last app closes, the window no longer hides - and as that dialog for New and New App
  while apps are open. A choice on the dialog closes it first.
- **New apps** (`DesignStartPage.NewText`, `DesignNewNames`): Blank App as `StandardAppFactory` makes
  it and the 2- and 3-panel apps as `ResponsiveAppFactory` does (designer plan DS4b), in a tab of
  their own and never written until saved. They are named as `_AppFactory._createDefaultAppName`
  names them: "app" and a count all app kinds share, "comp" and a count of their own for components,
  from 1 each session. The tab and the title read R2025b's: `app1.mlapp`, "App Designer -
  app1.mlapp", with "*" (no space; `AppViewController`'s title, which W2's " *" now follows) when it
  has changes. Save, Run and a close's "Yes" ask for the file first. A new app is an `.mlapp`:
  `fileformat.CreateNewAppsAs` is "m" but R2025b keeps the plain-text format behind a feature.
- **A new custom component waits for W15.** Its class text comes from App Designer's component
  code generator, which R2025b runs in MATLAB (`ComponentCodeGenerator`); W15 builds component
  authoring, so the two tiles are shown disabled with their stage, as W2 shows every action whose
  stage has not landed. (The plan had said they would open code-only in W3.)
- **JGraph's example apps** (WO2), in `examples/apps`, written for JGraph on the topics of R2025b's
  examples and shown under R2025b's titles and descriptions for them: Respond to Numerical Input
  (`SavingsApp`), Lay Out Controls in a Grid (`PulseApp`), Link Data to a Tree (`PartsTreeApp`),
  Analyze an Image (`ImageApp`), Configure a Timer (`TimerApp`), Display Specialized Axes
  (`PolarApp`), Create a Table (`TableApp`), Pass Data Between Apps (`MainApp` and `OptionsDialog`),
  in R2025b's order (its tiles for the timer and the image swap their definitions and their
  examples alike, so the page reads "Analyze an Image" before "Configure a Timer"). Each is kept as
  the class text the designer writes (a test holds it to the writer's output) and embedded; a tile
  copies its example into `Documents\JGraph\Examples\<folder>` as `.mlapp` files, as `openExample`
  copies R2025b's into the user's MATLAB folder, makes that folder current in the session and opens
  the main app. A copy already there is opened as the user left it.
- **The links** open the scripting guide's sections for R2025b's four topics JGraph's guide has:
  Getting Started (`#apps-designer`), GUIDE Migration Strategies (`#apps-guide`), Display Graphics
  in App Designer (`#apps-uifigure`), Find and Create UI Components (`#apps-design`).
- **Recent files** (`DesignRecentFiles`) are R2025b's `MRUArray`: newest first, one entry per file
  (by name, ignoring case on Windows), the oldest dropped when full, nothing kept at size 0, an
  entry that is not JSON read as a standard app's path and an empty one dropped. They are kept as
  `matlab.appdesigner.history.FileList`, each entry `{"apptype":...,"filepath":...}` as
  `getMRUEntry` and `JSON.stringify` write it, held to `history.MaxFileListSize`. An app goes in
  when it is opened and on every save (`RecentlyOpenedFilesManager`); a recent file no longer there
  is taken off the list, as R2025b removes one whose open failed for its name.
- **DESIGNER > Open's drop-down** is `DesignerTab._buildRecentFilesList`: after Open..., a "Recent
  Files" heading and each file by its name with extension, its full name under it, filled each
  time the drop-down opens, nothing with none. The GUIDE App heading and its migration tool are left
  out (X3).
- **Actions**: `OPEN_STARTSCREEN_DIALOG`, `AD_NEW`, `AD_NEW_2COL_APP` and `AD_NEW_3COL_APP` run (49
  of 202); the two component tiles wait for W15; R2025b's own example and tutorial actions are
  MathWorks' (X8).
- **Each tab has a close button**, as R2025b's document tabs do.
- **The shell**: a Design App button on its toolbar and File > Design App bring the designer up, on
  its Start Page when no app is open, as R2025b's Design App runs `appdesigner`. File > New Script's
  three app entries make the new app in the designer, unsaved, as R2025b's desktop New > App does
  (`createNewApp` makes a blank app there); opening an `.mlapp` as a file still shows its class in
  the script editor.
- **No Welcome dialog.** R2025b's catalog has "Welcome to App Designer" and its three steps, but no
  module of its client shows them; JGraph does not either.

## Consequences

- W15 turns the component tiles live with the component code generator.
- W12 and W13 share apps made from the Start Page; W16's acceptance builds its apps from it.
- `DesignStartPage.Examples` is the place a further example goes: its folder under `examples/apps`,
  embedded by the wildcard, held to the writer by the test.

## Measured

- **Tests**: `AppDesignerStartPageTests` (10): the sections, tiles and words; the tiles' words equal
  to the registry's; the examples' titles and order; the components' stage; the links; new
  documents' names and texts; a new app's title; `MRUArray`'s order, moves, size and removal; the
  entries as R2025b writes and reads them through the settings; the Open drop-down's recent files
  and no GUIDE item; each example app as the designer writes it, read whole with its canvas made;
  an example copied out as `.mlapp` files and an edited copy kept; and the examples run in a MATLAB
  session with their callbacks driven (the balance, the pulse, the tree's details and a rename, the
  image's histogram, the timer started, stopped and deleted with its window, the polar axes made in
  code, the table's refused edit and added row, the dialog's choice passed back to the main app and
  the dialog gone). The same drive in R2025b, headless (`ui-probes/w3/w3_examples.m`), gives the
  same answer on every line ("Balance after 2 years: 2529.09", 24 points, 1, "Bearing 608 Bearings
  16", the rename, 40000 pixels, the buttons' states, 0 timers left, polaraxes, 0.5, "Total:
  15.85", "0 0 0 3 on", the dialog deleted). `AppDesignerToolstripTests` follows the new live set.
  With the designer, App Designer, toolstrip and settings suites: 763 pass.
- **The window check** (`run-w3designer.ps1`, UI Automation patterns, JGraph.Application in Release
  started with `-r "appdesigner"`): the window on the Start Page with only DESIGNER; the three app
  tiles enabled with R2025b's words, Blank Component disabled until W15; "No Recent Files"; the
  links; the examples hidden, then shown; Blank App making `app1.mlapp` (title, tab, Design View
  tabs, Save enabled, the page gone); DESIGNER > New's dialog, its 3-Panel tile making
  `app2.mlapp` and closing it; Create a Table from the dialog, copied as `.mlapp` and opened; Open's
  drop-down listing it under Recent Files; the settings keeping it as `history.FileList`; every tab
  closed by its close button, the page back with the example as its recent file, which opens again;
  the shell's Design App button and File > New Script > "MATLAB app (.mlapp)" making `app3.mlapp`.
  All 43 checks pass; the user's settings file is moved aside and put back, the examples go into the
  check's folder. W2's `run-w2designer.ps1` (39, New now enabled) and W1's `run-w1toolstrip.ps1` (54)
  still pass.

## Not measured

- **The example apps in R2025b's App Designer**: they run in R2025b as classes, but the `.mlapp`
  files JGraph writes from them were not opened in R2025b's designer (`ds6_verify` covers the
  writer; W16's acceptance opens apps made from the Start Page there).
- **Save As of a new app in the window** (a file dialog the check does not drive), and Run of an
  unsaved app.
- **The links and Open... in the window** (they open the guide and a file dialog).
- **The page's look against R2025b's**: no picture of R2025b's page was taken; the layout follows its
  modules.

## Divergences

- **The Start Page's header is JGraph's own band, with no slideshow**: R2025b's carousel is
  MathWorks' (its blog feed, Simulink apps, community components, web apps; X8).
- **The links bar keeps four of R2025b's seven links**, opening JGraph's guide: App Building Onramp,
  How-to Videos and Release Notes are MathWorks' pages and are left out.
- **The examples are JGraph's own eight apps**, under R2025b's titles for their topics; R2025b's
  App Tutorial and Custom Component Tutorial (guided tours), its examples that need a toolbox or a
  device or the web (HTML, serial GPS, website data, user selections) and its component examples
  are not shown. They are copied into `Documents\JGraph\Examples`, not R2025b's
  `<userpath>\Examples\R2025b\matlab`.
- **With no app open the page is shown in the window, under the toolstrip's DESIGNER tab**, where
  R2025b starts on a page of its own without the toolstrip and opens the modal dialog when the last
  app closes.
- **A recent file's tooltip is its full name only**: R2025b also shows the app's thumbnail.
- **New custom components wait for W15** (shown disabled).
