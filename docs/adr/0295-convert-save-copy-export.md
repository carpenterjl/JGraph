# ADR 0295 — Convert, Save Copy As and Export

## Status

Accepted, 2026-10-31. Stage W8c of the App Designer window plan: Convert to and from auto-reflow, Save Copy As and Export to .m File. W8a has App Details and Help Text, W8b Run with inputs.

## Context

W3 made new auto-reflow apps, and nothing changed an existing app into one. A copy of an app could only be had by Save As, which moves the document to the new file; Export existed only in the script editor (`AppDesignerDocument.Export`).

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`): `appdesigner/action/ResponsifyAppAction`, `appdesigner/util/AppDataCopyUtil`, `appdesigner/util/PositionUtil`, `appdesigner/model/factory/ResponsiveAppTemplateData`, `appdesigner/command/SaveCopyAsCommand`, `ExportModelCommand`, `_SaveAppModelCommand`, `appdesigner/model/AppModel`'s `saveCopy` and `exportCode`, `appdesigner/onboarding/tutorial/responsify/ResponsifyTutorial`; MATLAB's side too: `appdesigner.internal.model.AppModel.copy`, `serialization.converter.MLAPPExporter`.

## Decision

- **Convert makes a new app and leaves this one alone**: not saved, modified, in a tab of its own, in the same file format, named by `_createAppName`: the name with an earlier `_converted` and its number taken off, `_converted` on it, and a number from 2 when an open app has that.
- **To panels** (`_processResponsifyConversion`): the template's grid and regions; the figure's properties but `SizeChangedFcn`, `AutoResizeChildren`, `Resize` and `ContextMenu`, which stay the template's; the regions' widths shared out of the figure's by the template's ratios (`resizeRegionPanels`: a rounded share less a pixel, what is left over to the last), into the grid's columns; each component in the region its middle falls in, moved by that region's left edge, its box and every box under it brought inside its parent (`clampToParent`); what has no box (menus, a toolbar, a context menu) on the figure; a name the template has already gets a number.
- **Back to none** (`_processStandardConversion`): the regions become panels at the boxes they have, their layout gone, with the figure's other children beside them, on a blank figure.
- **What comes over** (`createCallbacks`, `copyCustomCode`, `createGroups`, `copyAppSettings`): the editable section; the callbacks, with their bindings, but the generated `updateAppLayout`; the single instance and the input arguments; a group whose members all came over and still share a parent; App Details under the new name, with a new id and no picture.
- **A figure's own `SizeChangedFcn`** stands unbound in the new app, its code kept, and an alert says so, in `responsify_sizeChanged`'s words.
- **The items' state** (`_updateAction`): to panels with no regions, back with regions. Convert asks of `isResponsiveApp`, whether the app has regions.
- **Save Copy As** (`SaveCopyAsCommand`, `AppModel.copy`): the app as it stands, unsaved changes too, written under the new file's name and in the format its extension names, details and argument sets with it. This app keeps its file, its name and its modified state, and no tab opens. Its own file is refused, in `Cannot copy over existing app`, and the dialog is offered again on OK.
- **Export to .m File** (`ExportModelCommand`, `MLAPPExporter`): the class alone under the new file's name, starting at `app_exported`, which does not reopen in App Designer. An `.mlapp`'s class as it stands. A plain-text app's as the class a package holds, deriving from `matlab.apps.AppBase`, since `matlab.apps.App` makes its components from an appendix the exported file does not have.

## Consequences

- `DesignConversion` holds the rules free of a document, `DesignDocument.Convert` the moving, over a clone so that the old app's objects are not touched. The new app is read again from its own text, so it stands as any opened one does.
- A converted app takes the figure's code name as well, so the template's generated code follows it.

## Measured

- **R2025b's own code**: `ui-probes/w8/oracle/convert.js` loads `AppDataCopyUtil` whole, with `ResponsiveAppTemplateData` and `PositionUtil`, over stand-ins: 42 names, the shares, 400 sets of boxes, 600 regions and 800 clamps in `DesignConversionOracle.json`, replayed by `DesignConversionOracleTests` (5).
- **R2025b's MATLAB side, headless**: `w8_read_back_convert.m` loads a converted app JGraph saved with `loadApp`: outcome success, `Responsive`, the grid, each component in its region at its moved place.
- **Tests**: `AppDesignerConvertTests` (12).
- **The window check** (`run-w8cdesigner.ps1`, JGraph.Application in Release over a plain-text app with two components): all 49 checks pass, each read back from the file. I looked at pictures of the drop-down and both new apps.

## Not measured

- **R2025b's Convert itself in a window**: its tutorial and what it draws were read, not seen.
- **R2025b's Save Copy As over a plain-text app**: JGraph's follows the extension, as R2025b's filter offers both.
- **A conversion of an app with groups in a window**: the tests have them.

## Divergences

- **A component right on a boundary goes to the first region.** R2025b's `getComponentsWithinBounds` counts both ends, so it makes the component in both regions under two names (read). Not kept.
- **The help text comes over.** R2025b's `copyCustomCode` carries the editable section alone, so a converted app loses its help text (read). Not kept.
- **No tutorial.** R2025b walks through the panels in a guided overlay, a kind JGraph leaves out (X8): the status line says the app was converted, and the alert what a figure's callback lost.
- **Convert's tooltip** is the action's, not `responsifyDescription` with the app's name in it.
