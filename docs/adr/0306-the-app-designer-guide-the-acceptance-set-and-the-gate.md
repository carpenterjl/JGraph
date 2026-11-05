# ADR 0306 — The guide's App Designer chapter, the acceptance set, and the gate

## Status

Accepted, 2026-11-05. Stage W16, the last, of the App Designer window plan
(`docs/plans/app-designer-window-plan.md`, gitignored). It closes the arc ADRs 0279–0305 built over
the designer plan's ADRs 0243–0253: the toolstrip control (W1) and App Designer's layout and actions
(W2), the Start Page and the new apps (W3), arranging (W4), the canvas's clipboard, text in place,
structure, bars and Replace With (W5a–W5e), zoom (W6), the Component Browser, the inspector over a
selection, its editors and callbacks for several (W7a–W7d), App Details, Run with inputs, Convert and
the App Layout pane (W8a–W8d), the editor's tools (W9a–W9c), the Code Analyzer (W10), the debugger
(W11), Share as a standalone program (W12) and as a MATLAB App (W13), compare and merge (W14) and
custom UI component authoring (W15).

## Context

The plan gave W16 three parts: the guide's App Designer chapter, an acceptance set of apps built in
the designer from the Start Page and taken the whole way - opened by R2025b's App Designer, run in
both engines, packaged as MATLAB Apps and installed, shared as a standalone program that runs - with
one window check walking the toolstrip's every tab, and the gate. Every stage before it had measured
its own feature against R2025b; what was still unmeasured was the whole road at once, from a Start
Page tile through the designer's edits to a file R2025b reads, a program a user runs and a package a
user installs, in both engines, with a custom component of the user's own among the parts.

## Decision

### The guide

`docs/jgs-scripting-guide.html` loses its "Designing an app" section, written at DS8 with a note of
what was not there yet (align, the clipboard, tabs and nodes, the inspector's editors, callbacks for
several, the App Layout pane, registering a component), all of it built since, and gains eight
sections under Apps and GUIs: **App Designer** (how it opens, the Start Page's tiles, recent files and
examples, the window's parts, the keys, the settings, the files it writes and reads), **Designing on
the canvas** (the library, the canvas, arranging, the clipboard, text in place, menus, toolbars and
context menus, Replace With, grids and responsive apps, zoom, the Component Browser, the inspector and
its coercions, callbacks), **Programming the app** (the Code View, the Code Browser's insertions, App
Input Arguments, Single Running Instance, Help Text, App Details, rename, the App Layout pane, the
editor's tools, the Code Analyzer), **Running and debugging**, **Sharing an app** (Standalone Desktop
App, MATLAB App and the Apps menu, Web App refused), **Comparing apps**, **Custom UI components**
(the class as the engine writes it, Add Property, the event pairs, Run, Configure for Apps and the
registry) and **Where App Designer differs from MATLAB** (nothing MathWorks drew, what was left out by
decision, what is not there yet). The components section ends with what the acceptance set showed.

### The acceptance set

`AppDesignerWindowAcceptanceTests` builds four files, each begun as its Start Page tile begins it
(`DesignStartPage.NewText`) and built through the designer's own edits - add from the library, set in
the inspector, rename, callbacks from the Callbacks tab, their bodies typed into the Code View, App
Details - and opens each again whole:

- **W16Counter** (Blank Component with Grid): a Count button in the grid's cell, the event `Counted`
  with its public callback `CountedFcn`, the managed properties `Count` (double, 0) and `Step` (double,
  1, `mustBePositive`), the button's callback counting by the step and firing the event, `update`
  writing the count on the button; then registered as Configure for Apps registers it - the component
  made once in a hidden figure for its default position through `IScriptAppRunner.CheckComponentAsync`,
  the record written into `resources/appDesigner.json` beside it.
- **W16Blank** (Blank App): a numeric edit field, a slider with its limits, a Scale button, a result
  label and an axes the startup function plots into; the button's callback writes the product into
  the label; App Details filled in for the packages.
- **W16Panels** (2-Panel App with Auto-Reflow): a list box of three colours in the left panel, a
  label in the right, the list box's callback writing the choice.
- **W16Composite** (Blank App): the registration beside it puts Counter in the library under
  *W16 (Custom)*; added from there, its `Step` set to 5 in the inspector, its `CountedFcn` wired to a
  callback that writes the count into a label.

With `JGRAPH_W16_OUT` the files go under `tools/matlab-checklist/ui-probes/w16/rt/accept`, where three
probes take them the rest of the way:

- `w16_verify.m` (R2025b): `appdesigner.internal.application.loadApp` reads each - the status, the
  warning, the metadata's app type and name, the callbacks and what each is bound to, the component
  tree node by node with its code names, the component's `AppTypeData` - and
  `appdesigner.internal.usercomponent.metadata.Model` reads the registration.
- `w16_accept.m` (both engines): constructs each app, calls its callbacks as its window would, prints
  its state and deletes it; the component alone in a figure, its refusal of a zero step, its public
  callback; what a property typed with the component's class and no default starts as; the composite
  app's component and label through the public callback.
- `w16_share.m` (both engines, two stages): each app packaged as a MATLAB App with the statements
  Share > MATLAB App's script runs, under a fresh identifier and a fresh name per run; W16Blank built
  into a standalone program with the statements Share > Standalone Desktop App's script runs; then,
  in a second process, each package installed, listed, resolved and run from the install, and
  uninstalled. JGraph's run installs into the folder `JGRAPH_ADDONS` names and builds under
  `W16_RELEASE`, so that the two engines' runs share no folder.

`tools/runtime-checks/run-w16designer.ps1` opens W16Composite.mlapp in App Designer, maximises the
window and walks the toolstrip's every tab through UI Automation patterns - DESIGNER, the library's
registered Counter and the Component Browser's rows, CANVAS with the component selected, EDITOR in the
Code View, VIEW back in the Design View, each pictured - then runs the app from the quick access bar,
pushes the component's button in the app's own window and reads the label and the button, and, with
`-Runtime`, starts the standalone program JGraph built under the staged JGraph Runtime, pushes Scale,
reads the label and closes it.

### What the acceptance showed

- **R2025b's App Designer loads all four files** with status success and no warning: the component
  as `CustomUIComponent` with its `AppTypeData` (the event, the two managed properties with their
  inferred types and the validation function, the update method), the blank app with its startup
  function and its App Details name, the responsive app with `updateAppLayout` bound to the figure's
  `SizeChangedFcn`, the composite app with `W16CounterCounted` bound to `W16Counter.CountedFcn` and the
  tree holding a `W16Counter` node with its `Step`; the metadata Model reads the registration as valid,
  in category W16, with the measured default position.
- **Both engines print the same 14 lines** for the apps: the blank app's figure, field, slider,
  label, the plotted line's last point and two scalings; the responsive app's columns, items, value,
  the choice and the narrow layout's two rows; the component's class, position, defaults, the
  refusal of `Step = 0` in the words "Error setting property 'Step' of class 'W16Counter'. Value must
  be positive.", its public callback called, and that `findall` reaches no button inside it; a typed
  property's empty start; the composite app's component, step, count and label.
- **Both engines package, install and build the same**: three `.mltbx` packages of version 1.0, each
  installed and listed by `installedToolboxes`, resolved under the add-ons `Toolboxes` folder and
  constructed from there, then uninstalled, the list a fieldless 0-by-0 struct after; `matlab.apputil`
  lists no app for a toolbox's app in either; W16Blank built into `build\W16Blank.exe` with
  `readme.txt` and `splash.png` by both.
- **The window** (`run-w16designer.ps1`): 61 checks pass - every tab's controls where R2025b
  puts them, the registered Counter in the library, the composite app run and its component counted
  twice by its button with the label and the button following, the standalone program under the
  runtime scaling 2 by 3 to 6 and ending with exit code 0.

**Found with it and put right.** Three things in JGraph:

- **A property typed with a class of the user's and given no default** started as `[]`, which the
  class check then tried to convert through the constructor: App Designer declares a custom
  component's property `W16Counter W16Counter` with no default, so the composite app could not be
  constructed at all ("'W16Counter' must be W16Counter, and a double cannot be converted to one").
  `JgsClass.DefaultOf` now starts it as a 0-by-0 of the class, as MATLAB's `ClassName.empty` makes
  one without the constructor (measured: `W16Typed.m`), for any loadable class as it already did for
  an enumeration.
- **A component's own callback writing its own property** left its `update` owed to the next event:
  the dispatcher's `Drain` ran owed updates at its start only. It runs them after the callbacks it
  delivered too, so a button inside a component writes its count before the window is drawn again.
  In the window the button still kept its old count after that: the shell's pump drains in its active session, and the app had been run in another, whose host alone `RunUpdates` served, so the update the callback owed was never due there (the trace: `1 owed, 0 due for host`). A component owed by a callback a drain delivered is now updated by that drain whatever session's pump it is, as the callback itself ran there (`JgsComponentContainerState.OwedByCallback`, `JgsComponentContainers.Draining`).
- **`matlab.addons.toolbox.installedToolboxes` with none installed** answered a 0-by-0 struct with
  the three fields; R2025b's has no fields (measured in every uninstall of the set), and so has JGraph's.

Two things in R2025b, kept as they are and recorded: `installToolbox` in a fresh `-batch` process
answers "Unable to find add-on with specified identifier and version" - the files copied, no record
made - whatever the identifier, name, folder or process, until something listed the add-ons once
(`matlab.addons.installedAddons`), after which it installs; and a failed install leaves its
`Toolboxes\<Name>` folder behind, so the next install of that name lands in `<Name>(2)`. The probe's
install stage lists first. And one thing in the user's environment: the designer's status line warned
about a function `extract` in every W15 and W16 window check because the shell's saved workspace root
(`D:\Temp_Broken_Scripts\test1`, on the session's path) holds an `extract.m`; the warning is MATLAB's
own, and the status line's wording for it is item 243.

### The gate

The full suite ran once, 13,133 tests; what it found and what was done is under Measured. The seven `verify-*.py` scripts pass (the builtin coverage unchanged at 1,275 of 2,024,
since the stage adds no builtin), and the divergence harvest is regenerated.

## Consequences

- The App Designer window plan is complete: MATLAB R2025b's App Designer stands in JGraph as a window
  of its own, from the Start Page to a standalone program and a MATLAB App, with its debugger, Code
  Analyzer, Comparison Tool and custom component authoring, every rule read from R2025b's client or
  measured by running it; an app built here opens, runs, packages and builds in R2025b, and the other
  way round.
- The acceptance set is the plan's whole-road measure; the three probes and the window check run it
  again in either engine when a stage changes what the designer writes or runs.
- Three scripts behave differently, all put right: a class-typed property with no default starts as
  an empty of its class rather than refusing, a custom component's update owed by its own callback
  runs in the same drain, and `installedToolboxes` with none installed answers R2025b's fieldless struct.

## Measured

- **Tests**: `AppDesignerWindowAcceptanceTests` (the four files built, saved and opened whole, the
  composite app's component pushed through the event queue and drained twice with the label and the
  button following), `MatlabClassdefTests` (a user-class-typed property with no default is a 0-by-0 of
  the class, a built-in-typed one an empty of its class), `ComponentContainerU10Tests` (an update owed
  by a component's own callback runs in the drain that ran the callback), `ClassInheritanceU6Tests`
  (its unsupported-attribute case moved off `GetObservable`, taken since W15). The full suite once: 13,133 tests, 13,131 passing; the two failures were older than the stage - OwnershipGateTests over the W10, W13 and W14 builders' writes into the instances they mint, which the stages' targeted gates never ran past (each now allowed with its reason: a fresh instance before anything holds it, or the copy ToolboxOptions' Setter minted for the write), and PropertyAccessorsM167Tests expecting GetObservable refused, taken since W15 - and the suites around them green after (the ownership gate, the accessors and U6, 76; the component, acceptance, run and control suites, 25).
- **R2025b headless** (`w16_verify.out`, `w16_accept.out`, `w16_share_package.out`,
  `w16_share_install.out`) against JGraph's (`w16_accept.jgraph.out`, `w16_share_*.jgraph.out`): the
  14 acceptance lines, the 10 package lines and the 19 install lines agree line for line; the loader's
  report is R2025b's alone.
- **The window check** (`run-w16designer.ps1 -Runtime installer\runtime-staging`, JGraph.Application
  in Release, the runtime published from this source): 61 checks pass; I looked at the five
  pictures - the four tabs over the composite app, with the registered Counter under W16 (Custom) in
  the library and the component selected on the canvas, the running app after the push, and the
  standalone program's window with its label at 6.
- **The verify scripts**: builtin 450 of 514 and 1,275 of 2,024, IPT 270 of 409, method classes 2,489
  measured, signal 269 of 351, stats 385 of 589, target forms 101 of 101, toolbox 348 of 377.

## Not measured

- **R2025b's own standalone program** of W16Blank (`release\build\W16Blank.exe`): a MATLAB window, not
  run without the user's leave; its build is recorded, JGraph's program ran.
- **R2025b's App Designer window** over the four files: the loader's report stands for it, as in every
  stage of the plan.
- **The Apps gallery** of R2025b with the installed package: the install, the listing and the run from
  the install are measured; what the gallery shows is not.

## Divergences

- **`installToolbox` in a fresh process**: R2025b refuses until the add-ons were listed once; JGraph
  installs either way (the probe lists first in both).
- **`installedToolboxes` with none installed** is a fieldless 0-by-0 struct in both (R2025b's shape,
  taken).
- **`findall` does not reach a `ComponentContainer`'s inner components** in either engine (U10's
  rule, measured again).
- **The Testing group's "Give Test Cases Access"** is written into a component's class; nothing runs
  tests (W-P1). **An app in a `+package` folder** runs only when packages do (W-P2). **A registered
  component's avatars** are empty (item 242). **The Code Analyzer** raises 73 of R2025b's checks
  (item 241).
- **The designer's status line** reports a path's shadowing warning raised while the canvas builds the
  app as "The canvas could not make everything" (item 243).
- **Nothing MathWorks drew** is in JGraph: the icons, the example apps and the Start Page's images are
  JGraph's own; the window is drawn by WPF with R2025b's layout rules, words and behaviour.
