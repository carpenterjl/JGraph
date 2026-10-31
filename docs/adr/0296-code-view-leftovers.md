# ADR 0296 — The Code View's App Layout, Go To list, folds and undo

## Status

Accepted, 2026-10-31. Stage W8d of the App Designer window plan, the last of W8: what the Code View still lacked. W8a has App Details and Help Text, W8b Run with inputs, W8c Convert, Save Copy As and Export.

## Context

The Code View had the code browser and the editor. It had no picture of the app to find components by, its Go To showed nothing, every generated block stood open, and any change of the text wiped the editor's undo, including the two the user asks for by name: Add Function and Add Property.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`): `appdesigner/view/app/CodeView.rescaleThumbnailAppWindow`, `appdesigner/view/designarea/strategy/ThumbnailInteractionStrategy`, `appdesigner/view/toolstrip/EditorTab._generateFunctionList`, `appdesigner/model/codegen/CodeModel.getClassFunctions`, `appdesigner/model/codegen/AppTypeMethodEnum`, `appdesigner/view/editor/plugins/defaultfolds/DefaultFoldsPlugin`, `appdesigner/codegen/CodeController.getDefaultFoldedLineNumbers`, `appdesigner/codegen/builders/XMLRootBuilder`, `appdesigner/codegen/CodeEngine._processPostRender`, `appdesigner/codegen/behaviors/UserEditableBlockBehavior`.

## Decision

- **App Layout** under the code browser (`thumbnail`): the canvas scaled to the pane less the figure's margin on each side, never above 1 or below 0.4. Clicking selects what is under the point (`ThumbnailInteractionStrategy`: component select and no keys).
- **The picture follows the design**: taken on entering the Code View and a moment after any change of the design there, from the canvas shown just long enough to lay out, so that nothing is drawn on the screen meanwhile.
- **Go To's FUNCTIONS** (`getClassFunctions`, `_generateFunctionList`): the callbacks, the user's functions, the constructor, `createComponents` and `delete`, code-unit sorted. Choosing one goes to it. The LINE item waits for W9.
- **Default folds** (`DefaultFoldsPlugin`, `XMLRootBuilder`): the component properties, `createComponents`, the constructor, `delete` and a read-only callback start closed, and so does each section's root in a plain-text app's appendix. A block the caret stands in is not closed, a block the caret goes to opens, and after that a fold stays as the user left it.
- **Add Function and Add Property keep the editor's undo** (`clearRTCUndoStack: false`): Ctrl+Z takes back what they added. Every other render still clears it (the builders' default), as it would undo into text the design no longer has.
- **A new plain-text app's tab** says `.m`, as a converted plain-text app first showed it should.

## Consequences

- `DesignCodeFolds` holds both lists free of a window; `ScriptEditorControl.SetFolds` installs AvalonEdit's folding with a margin. W9's full folding builds on it, and only the default blocks have folds until then.
- `DesignCanvas.Snapshot` draws the figure through a `VisualBrush`, so the element's place in its parent does not shift the picture.

## Measured

- **Tests**: `AppDesignerCodeViewLeftoversTests` (9 with its theories). With the designer, App Designer, toolstrip and settings suites, all pass.
- **The window check** (`run-w8ddesigner.ps1`, JGraph.Application in Release over an `.mlapp` and a plain-text app): all 33 checks pass, including `Add Function` taken back by Ctrl+Z and read from the file. I looked at pictures of App Layout with components, the folds, and Go To's list.

## Not measured

- **R2025b's own Code View in a window**, and which blocks it folds: the list comes from reading `XMLRootBuilder` and from DS5's earlier reading of the builders, not from seeing it.
- **Clicking in App Layout**: posted mouse messages do not reach a WPF element, so the selection a click makes is untested in a window.
- **The picture following a change made in the Code View**, which needs a change there to wait on.

## Divergences

- **App Layout is a picture, retaken after a change**, where R2025b scales the live canvas. The difference shows only for the moment after an edit, and in anything a component animates.
- **Clicking the picture selects a component** and nothing more: R2025b's has the menu bar's, toolbar's, tree's and context menu's select plugins as well.
- **Only the default blocks have folds** until W9.
