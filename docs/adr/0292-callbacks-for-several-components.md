# ADR 0292 — Callbacks for several components

## Status

Accepted, 2026-10-31. Stage W7d of the App Designer window plan, the last of W7: one function for several components, the Callbacks tab over a selection, the Callbacks submenus. W7c has the inspector's editors.

## Context

JGraph's Callbacks tab said `Select one component to edit its callbacks.` for several, and put the app's `StartupFcn` on the figure's rows. No menu had Callbacks or Help on Selection, so a callback could be reached only from the tab or the Code View.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`): `appdesigner_interface/model/_ComponentContextMenuInfo`, `appdesigner/view/contextmenu/ComponentMenuItemsProvider`, `appdesigner/view/designarea/DesignAreaContextMenuController`, `appdesigner/util/CallbackUtil`, `appdesigner/command/MassEditCallbackCommand` and the commands it runs (`AddCallbackCommand`, `AttachCallbackCommand`, `ReassignCallbackCommand`, `DetachAppCallbackFunctionCommand`, `DeleteAppCallbackFunctionCommand`, `RenameAppCallbackFunctionCommand`), `appdesigner/view/propertyview/callback/CallbackSection`, `CallbackPropertyView`, `CallbackPropertyViewFactory` and `CallbackMultiselectView`, `appdesigner/view/dialogs/callback/SharedCallbackDialog`, `appdesigner/widget/CallbackFunctionNameEditField`, `appdesigner/model/inspectableapp/InspectableAppNodeModel`, `appdesigner/util/CodeEditUtil`'s `isVarName` and `isAppAttributeNameUnique`, and each class's `CallbackFcnData` in `uicomponents_appdesigner_plugin/init/derived/ComponentDefaultsProduction`.

## Decision

- **What several have in common** (`CallbackUtil.getCommonCallbacks`): each list's that the ones before it have, so they stand in the order of the last. R2025b compares each item's whole JSON; the oracle shows a property reads the same in every class, so names are enough.
- **The menu's Callbacks** (`buildCallbackMenuItemsInfo`), asked of the first selected: an item says `Add X callback` or `Go to f callback` by what the first holds, a trailing `Callback` left off the word. A class with several groups has its first group's items and a submenu of each of the others' before `Select existing callback...`. That item is greyed out while the first group's are all bound or the app has no function to bind, and always on the app's row.
- **Add** names the function for the first selected (`getDefaultCallbackName`, unique among the app's names) and goes to all of them. **Go to** goes to the function. Only a function made takes the user to the Code View: R2025b's Attach and Reassign set a position (`setCodeViewPosition`) nothing in its client reads.
- **An edit of several is one step** (`MassEditCallbackCommand`, `DesignCallbackRules.Plan`), **the list reversed and gone through last first**: nothing bound, the function bound, made when there is none; another bound, moved to the function, and where there is none the old one kept (`detachPrevious`, the tab) or **renamed, whoever else holds it following** (the menu); no name, unbound with the function kept. A function bound first gives it its code, so the last selected's `value = app.X.Value;` is what a new one starts with.
- **The Add item can rename.** With a first component free and a later one holding `f`, Add goes to the later one first, so `f` takes the new name and the first binds to it. Nothing is lost and they all share the code; kept as R2025b does it, and no Code View follows.
- **The tab** (`CallbackPropertyViewFactory`): one component or a labelled component's field, its groups; several, one untitled section of what they have in common (`CallbackMultiselectView`), with labels of labelled components left out, showing the **last one's** values, where the menu asks the **first**. `The selected components have no callbacks in common.` otherwise.
- **A row's edit** (`_handleCallbackNameEdited`): nothing when the value is what the last holds, **whatever the others hold**; a name refused in an `Invalid Name` alert; otherwise all of them, the default's name being the last one's. The section keeps its list, which the command reverses in place, so its edits go each way in turn.
- **The app's own row has `StartupFcn`** (`InspectableAppNodeModel`), which moves from the figure's rows: `Add StartupFcn callback`, `Go to startupFcn callback`, and Select existing never offered.
- **Select existing callback...** (`SharedCallbackDialog`): `Select Callback Function`, the first's free properties and the app's functions sorted. It binds, so nothing switches view.
- **The provider's order** (`ComponentMenuItemsProvider`, `DesignAreaContextMenuController`): Callbacks, the Context Menu submenu with a line under it (not in the Code View), Help on Selection. On the canvas after the figure's display options and before Apply Grid Layout and Replace With; on a menu, a toolbar, a tool, a context menu and a tree node after Delete; in the browser after Insert at Cursor and before the tab order. The Context Menu submenu now joins the browser's menu too, and asks of the first selected: a plain group's first member, where W5d offered none.
- **A class with no row in R2025b's table has no callbacks** (a label, a lamp, a gauge, a toolbar, a tree node): JGraph was taking any property of the catalog whose name ends `Fcn` or `Callback` for one.
- **A refused name says R2025b's words** (`invalidAttributeNameMessage_*`, and the row's `CannotShareStartup`, `StartupCannotShare`), which W7a's browser rename shares: `Name cannot be a MATLAB keyword.`, `Name is already defined as component.`, and so on. JGraph had its own.
- **An empty row says `Function name`** (`callbackPlaceHolderText`).

## Consequences

- `DesignCallbackRules` holds the rules free of a document; `DesignDocument.Callbacks` the sheet and the edit; `DesignGestures.Callbacks` the items, raising `CallbackGoTo`, `CallbackSelectAsked` and `HelpAsked`, which `DesignCanvas` passes to the window.
- Every test that spells out a menu now has these items. The window check selects several with Ctrl+A, so its fixture has only sliders and a knob at the figure's top once the panel is deleted.
- A row's own text box taking the keyboard no longer commits it, the trap W7c found in the font lists: the row had it since DS5, and a value set through automation then committed before Enter.

## Measured

- **R2025b's own code**: `ui-probes/w7/oracle/callbacks.js` loads `ComponentDefaultsProduction`, `InspectableAppNodeModel`, `CallbackUtil`, `_ComponentContextMenuInfo`, `GroupUtil` and `MassEditCallbackCommand` whole, and cuts `_getMenuItemsInfo`, `_wrapMenuItemOnClick`, `_buildCallbacksList`, `_shouldCallbackBeAddedToCallbackList`, `_handleCallbackNameEdited`, `_validateCodeName`, `CallbackMultiselectView.showContent` and `SharedCallbackDialog._getCallbacksForSelectedComponent` out of the bundle, over stand-ins for the peer nodes and the commands. `DesignCallbacksOracle.json`: the table, 500 common lists, 1,200 menus with each item clicked, 1,000 edits, 1,200 rows, 400 selections of several and 300 dialogs. `DesignCallbacksOracleTests` (9) replays them, differing only where the divergences below say.
- **Tests**: `AppDesignerCallbacksTests` (21). With the designer, App Designer, toolstrip and settings suites: 995 pass.
- **The window check** (`run-w7ddesigner.ps1`, JGraph.Application in Release over `w4/W4App.m` with two sliders, a knob and a panel holding a tree and a lamp in its place): all 118 checks pass, each edit read back from the file. Pictures of the shared rows, the figure's groups, nothing in common, the submenu, the dialog and the alert show them. The eleven older checks still pass on the final source: `run-w7cdesigner.ps1` (136), `run-w7bdesigner.ps1` (90), `run-w7adesigner.ps1` (103), `run-w2designer.ps1` (39), `run-w4designer.ps1` (23), `run-w5designer.ps1` (34), `run-w5bdesigner.ps1` (55), `run-w5cdesigner.ps1` (41), `run-w5ddesigner.ps1` (69), `run-w5edesigner.ps1` (53) and `run-w6designer.ps1` (34).

## Not measured

- **R2025b's App Designer itself in a window**: its code was run under node over stand-ins, not in its own client. In particular `MouseMouse Callbacks` below was run, not seen.
- **A bar's menu and a tree node's in a window**: the tests have their items; the window check reaches the canvas's and the browser's.
- **Help on Selection's click in a window**: it opens the guide in the machine's browser.
- **A mouse in the row's list**: rows are set through patterns.

## Divergences

- **With several selected, a group's items are those they all have.** R2025b lists every item of the first's groups, so a Tree and a Table offer `NodeExpandedFcn` and bind it on a Table, which has no such property. Not kept.
- **Select existing offers only what they all have**, for the same reason. R2025b offers any of the first's free properties. A property one lacks is refused whole.
- **A group's submenu says its title once.** R2025b's fallback writes it twice: `MouseMouse Callbacks` on a Tree, `Mouse and KeyboardMouse and Keyboard Callbacks` on a Table. Keyboard and Window have words of their own there. Not kept.
- **A region's locked `SizeChangedFcn` stays with the region.** R2025b's `CallbackPropertyView` writes `enabled` onto the class's shared data, so after one region panel is inspected every panel's row is locked (read, not run). Not kept.
- **Help on Selection opens the guide's App Designer chapter**, as F1 does, where R2025b opens `doc` on the class. JGraph has no page per class.
- **A new name on the app's row is two steps**, the delete and the add, where R2025b's command makes one.
- **Rows have JGraph's look**: a shared column width over the groups rather than R2025b's 134, 119 and 27 pixels on a figure, an arrow for the go-to button, an expander's heading, no help on a row's name.
