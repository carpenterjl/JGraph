# ADR 0294 — Run with inputs

## Status

Accepted, 2026-10-31. Stage W8b of the App Designer window plan: Run's drop-down with an app's argument sets, the errors of a run in the window, the thumbnail Run takes, and an app in a package. W8a has App Details and Help Text.

## Context

Run ran an app with no arguments. An app that took some could run only if its startup function did not need them, and what went wrong reached only the status line and the console. A thumbnail in `auto` mode, which W8a could store, was never taken.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`): `appdesigner/model/RunArgumentsModel`, `appdesigner/view/toolstrip/RunSection`, `appdesigner/action/RunAction`, `AppDetailsAction`, `appdesigner/command/SaveAndRunAppModelCommand`, `DeleteRunCommandWithArgs`, `appdesigner/model/AppModel`'s `_sendRunAppEvent`, `_runCallback` and `_handlePeerEvent`, `appdesigner/codegen/CodeController.updateAppParams`; MATLAB's side too: `appdesigner.internal.model.AppModel.runAppHelper`, `apprun.DesktopAppRunner`, `apprun.AppInstantiator`, `appalert.AppArgumentException`, `controller.AppController`, `application.figure.DesktopFigureService.captureScreenshot`, `serialization.MLAPPSerializer`, `application.loadApp`.

## Decision

- **The sets** (`RunArgumentsModel`): at most 8, the most recent first. One chosen again moves to the front. The empty set is always kept, last when it is not the most recent, and is never taken out. A set that changes what comes first modifies the app; choosing the one already first does not.
- **Where they are kept** (`MLAPPSerializer`, measured): an `.mlapp`'s `appModel.mat` as `runConfigurations`, only when there is more than the empty set. R2025b's `loadApp` reads JGraph's back as a 4-by-1 cell. A plain-text app keeps none, as R2025b's parser answers `{''}`, though choosing a set still marks it modified, as the peer node's change does.
- **The drop-down** (`RunSection._buildPopupList`): each set as `Run: app1` or `Run: app1(1, 2)`, a button taking out any but the empty one; `Run: app1(` with a field and `)`, enabled once the app takes arguments, showing their names until something is typed; `Add App Input Arguments...` or `Edit App Input Arguments...`; then the breakpoints' items, which W11 brings to life.
- **Taking a set out is one step** (`DeleteRunCommandWithArgs`), the drop-down made again at once. Undo puts it back as the most recent.
- **What Run runs with**: the most recent set, evaluated in the base workspace (`evalin('base', 'Name(arguments);')`), blanks alone being none. The Run buttons, large and in the quick access bar, draw another icon while a set is first.
- **An app whose arguments go runs with none** (`updateAppParams`): emptying its input arguments makes the empty set the most recent, on undo as well.
- **Errors of a run say where they belong** (`DesktopAppRunner.fireAppError`): too many arguments for the startup function, or an error evaluating them that is neither in the startup function nor names the app's file, is the arguments' fault. The drop-down opens by itself with the set in the field and the message on it. Too few is `Cannot run with provided input arguments.` over the cause. Every other error, and every refusal, is an alert in the window, on top of the status line and the console.
- **Change Folder** (`AppModel._runCallback`, `AppController`): a name shadowed by the current folder offers Change Folder or Cancel; Change Folder makes the app's folder current and runs again.
- **The thumbnail** (`runAppHelper`, `captureScreenshot`): when the mode is `auto` and the app has not run since it was saved, a picture of its window goes straight into the file once it stands, an `.mlapp`'s PNG part or a plain-text app's JPEG in the section marked `autoCapture='true'`. The app is not modified by it. An app as opened counts as having run, so its first Run takes nothing unless a save came first. A capture that fails goes unnoticed.
- **An app in a `+package` folder** is called by its whole name from the folder above the packages (`getAppRunCommandFromFileName`, `getPathToApp`).

## Consequences

- `ScriptRunResult` gains `ErrorIdentifier`, and `IScriptAppRunner` a `RunAppAsync` with arguments and `CaptureAppAsync`. The window's `Runner` answers a `DesignRunOutcome`.
- The toolstrip's popups gain `Removable`, `IsEnabled` and a `FieldItem` kind; `OpenPopupOf` opens the arrow in view, since a control on several tabs has one on each, and `SetIcon` redraws a split button and a quick access button.
- **A menu's late close no longer closes the next.** A closed menu's event arrived after another had opened from the same button, unchecking it, so that one shut at once. The button now answers only to the menu it last opened.
- The status line has an automation id.
- Window checks read `tools/runtime-checks/w8/W8bApp.mlapp` too, written by `JGRAPH_W8A_FIXTURE` with its startup function reading its argument.

## Measured

- **R2025b's own code**: `ui-probes/w8/oracle/run.js` loads `RunArgumentsModel` whole and cuts `_buildPopupList`, `_buildRunArgumentItem` and `_buildRunEditableArgumentItem` out of the bundle, over a stand-in toolstrip builder: 600 runs of 1 to 25 operations and 300 drop-downs in `DesignRunArgumentsOracle.json`, replayed by `DesignRunArgumentsOracleTests` (2).
- **R2025b's MATLAB side, headless**: `w8_read_back_run.m` loads what the window check saved with `loadApp` (outcome success, `RunConfigurations` a 4-by-1 cell of the sets) and reads its thumbnail with `MLAPPMetadataReader`.
- **Tests**: `AppDesignerRunArgumentsTests` (12). With the designer, App Designer, toolstrip and settings suites: 1072 pass.
- **The window check** (`run-w8bdesigner.ps1`, JGraph.Application in Release over two `.mlapp`s): all 48 checks pass. I looked at pictures of the drop-down with no argument, with its sets and with an argument's error.

## Not measured

- **R2025b's Run drop-down itself**: its code was run under node over stand-ins, not in its own client.
- **Change Folder in a window**: the refusal's identifier is in the tests, and the dialog and its second run were not driven.
- **An app in a `+package` folder running in a window**: `CommandOf` is tested; a run from one is not.
- **F5 with a set first**: the check runs through the items and the field; F5 goes the same way.

## Divergences

- **A run's error is an alert in the window**, where R2025b's `RuntimeAlertPlugin` marks the line in the Code View, moving the cursor there. JGraph has no such mark yet, and the console has the whole message.
- **The argument sets are not kept in a model in a version 7.3 MAT-file**, since a splice can only replace a root variable it already has. R2025b writes the default form, and the sets still work until the app is closed.
- **A drop-down's separator**: R2025b appends one at the end of the popup list, after the breakpoints' items, where nothing follows it. Not kept.
- **Run's icon** is JGraph's own drawing of `runConfiguration`, like the rest of the toolstrip's.
