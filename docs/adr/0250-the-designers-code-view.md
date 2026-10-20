# ADR 0250 — The designer's code view

## Status

Accepted, 2026-10-19. Stage DS5 of the visual app designer plan (`docs/plans/app-designer-plan.md`,
gitignored). App Designer's code generation, code browser, callbacks tab, editor rules and dialogs
were read from R2025b's own code - its canvas bundle (`appdesigner/codegen/*`: the code engine, its
builders, behaviors and configurations; `model/codegen/CallbackModel`, `CallbackCollection`,
`CallbackBinding`; `util/CallbackUtil`, `CodeEditUtil`; `hmishared/util/UniqueNameGenerator`;
`appdesigner_interface/model/ApplicationComponentModel`; `view/codebrowser/*`,
`view/propertyview/callback/*`, `view/dialogs/callback/*`, `view/toolstrip/EditorTab`, the editor
plugins; the per-class component models of `uicomponents_appdesigner_plugin`) and its MATLAB side
(`appdesigner.internal.codegeneration.*`, `MLAPPSerializer`, `matlab.apps.AppBase`). Nothing of it is
kept. The generated text was run under node in App Designer's own modules, unmodified, and the answers
kept in the tests. R2025b read and ran the app saved here (`ds6_verify`, `ds5_run`, headless).

## Context

The designer edited components (DS2-DS4b) and left the code to the editor: a save spliced the
component properties and `createComponents` into the class and left every other line as it was.
Nothing in the designer showed the code, added a callback, or knew what App Designer writes around
the components - the callbacks' comments and signatures, the constructor with its startup call,
`varargin` and single-instance check, `delete`. App Designer's code view is where an app's behaviour
is written: its code browser lists callbacks, functions and properties; its inspector binds a
component's callback properties; a dialog adds a callback; the app's input arguments and single
running instance are settings of the code. DS1 left those parts of the generated code to this stage.

## Decision

### The code around the components is a model, and the class is written whole

`DesignCode` holds what App Designer's code model holds: the class name; the help comment lines under
`classdef`, kept as read (App Details is not edited here); the component properties block as read,
kept while it still declares the design's components (older files hold an older order); any further
generated properties block as read, with the empty lines before it (a Simulink app's have two); the
editable section's lines; every function of the callbacks block in its order - the component
callbacks, the startup function where it stands among them, a responsive app's `updateAppLayout` -
each with its body verbatim; the input parameters as written (`a, b`); and whether the app runs as a
single instance, read from the constructor (`JgsMlappLayout.SingleInstance`). It is read with
`JgsMlappLayout`'s cut (ADR 0205), whose callbacks block is now cut by the lines App Designer
generates rather than by the balance of the blocks inside a body - as App Designer's editor tells its
lines apart by the ones it marked read-only and never parses a body: the block's `end` stands at its
`methods` line's indentation, a function begins at a `function` line at the first function's
indentation and ends at the last `end` at that indentation before the next function or the block's
`end`. A body the user is typing an `if` or a `for` into is still a body, a stray `end` inside it is
part of it, and a function nested in a body is too. The model is null for text that is not laid out
as an app, whose block is not closed, or a function of which has no `end` of its own: the design is
then view only, and the code view takes no typing.

The model is joined to the design as App Designer's loader joins them: each callback's bindings in
the order its reader finds them (`findall` over the figure: the figure, then each container's
children newest first), the first of which gives a callback read from a file its default code
(`_onGenerateDefaultCode` runs on the first binding, `LoadedFromDisk` only keeps it out of the
body); and a responsive template's `updateAppLayout`, which App Designer makes again from the
figure's binding, is put back last when the class has lost it and is App Designer's own again when the
comment over it no longer says so. A component moved to another container takes its declaration, with
its descendants', out of the component properties block as read and puts it back after the component
before it in the browser's order - each child at the line after its parent, so a moved container's
children come out last first, as `_reOrderProperties` inserts them - the names padded again.

`DesignClassWriter.Render` writes the whole class as App Designer's `CodeEngine` with
`AppCodeEngineConfig` does, group by group: the class line and head comments; one empty line; the
component properties; the generated blocks; the editable section, followed by one empty line when it
has lines; the callbacks block when there are functions, each under its comment, with its signature
and body and one empty line before it; `createComponents` (DS1); the lifecycle block. The constructor
is `function app = Name`, or `Name(varargin)` when the app takes input parameters; its body creates the
components, registers the app, runs the startup function (`runStartupFcn(app, @startupFcn)`, or
`@(app)startupFcn(app, varargin{:})` with parameters) and clears `app` when nothing takes it; as a
single instance it asks `getRunningApp` first, makes the app inside `if isempty(runningApp)` and
focuses the running one's figure in the `else`. `delete` deletes the figure. Every line of this was
checked against App Designer's own builders run under node (48 lifecycle cases) and against the 44 apps
R2025b ships, which come back line for line but for forms of older releases (below).

A callback's comment is `CallbackModel._getGeneratedComment` transcribed: the property's own comment
when every binding is to the same property - the first binding's class's, and the strings carry their
comment sign, as the resources hold them - else `Callback function`; then the bound components' code
names sorted by character code, joined by commas, within a budget of 75 columns of which eight are
counted used whatever the file's indentation is (the model measures with a fresh formatter); a first
name that does not fit goes on a line of its own; names that do not fit after one are replaced by
`...and N other components`; then `MethodBuilder` wraps each line again by words at 75 less the
method's indentation counted at the indent size - the unit's width in spaces, four for a tab - which
is where the file's indentation shows. A callback no component names reads `Callback function` alone
when it was read so, and `Callback function: not associated with a component` once a binding was
taken from it, by an unbinding or by its component's deletion - App Designer's comment is generated
before its constructor's listeners exist, and again on every binding change. Five properties of
R2025b name a resource that does not exist and fall back to the
mixed comment (`PushTool`/`ToggleTool.ClickedCallback`, `CheckBoxTree.CheckedNodesChangedFcn`,
`ColorPicker.ValueChangedFcn`, `Hyperlink.HyperlinkClickedFcn`); the table embedded from the models
(`ds5_callbacks.json`) has no entry for them, so they do here too.

The document's save writes `Render`'s text; the splice of DS2 and DS4b is no longer the save's road.
`AppDesignerDocument.NewAppText` and the responsive templates are the writer's.

### Edits of the code, as App Designer's commands make them

- **Add callback** (`AddCallbackCommand`): the function named as `getDefaultCallbackName` names it -
  the code name, then the property without its trailing `Fcn` or `Callback`, the class's default code
  name taken out of the property once when both hold it (a Button's `ButtonPushedFcn` is
  `ButtonPushed`, a `StartButton`'s `StartButtonPushed`, a Menu's `MenuSelected`) - made unique by
  `UniqueNameGenerator` (the next number above the highest in use; `ButtonPushed3` alone gives
  `ButtonPushed4`); its body the property's default code with the function's indentation (one
  whitespace line for most; `delete(app)` for `CloseRequestFcn`, `value = app.X.Value;` for
  `ValueChangedFcn`, each followed by an empty line); put last in the block, before a generated one;
  the property bound. The startup function is `startupFcn`, first.
- **Bind** (`AttachCallbackCommand`, `ReassignCallbackCommand`, `DetachAppCallbackFunctionCommand`):
  a property bound to an existing callback, moved to another, or unbound - the function kept.
- **Rename** (`RenameAppCallbackFunctionCommand`): the function, its bindings, and the user's code
  that names it by `app.Name` or calls it as `Name(app`. A component renamed takes with it a callback
  bound to it alone whose name holds the old code name and is still the name App Designer gave it,
  the code name replaced (`CodeController.renameComponent`: every loaded callback counts as named by
  App Designer; one the user renamed no longer does). A name every app has from
  `matlab.apps.AppBase` (`delete`, `registerApp`, `runStartupFcn`, `getRunningApp`, `TimingFields`...)
  is refused for a callback, a function, a property and a component alike: "Name cannot be an App
  Designer keyword."
- **Delete** (`DeleteAppCallbackFunctionCommand`): the function with its code, every property bound to
  it unbound; the startup function deleted takes the input parameters with it. A component deleted
  takes with it a callback bound to it alone whose code is still what it was made with - a loaded
  callback's being its first binding's default, so a field's untouched `value = app.Field.Value;`
  goes with the field; one with code of its own stays, no longer associated. Two taken away by one
  deletion come back in their order when it is undone.
- **Input parameters** (`updateAppParams`): the names, split at spaces and commas and joined by
  `, `, each a MATLAB name and named once; set, they make a startup function when the app has none;
  cleared, they take away a startup function whose code is still blank.
- **Single running instance** (`SingletonModeCommand`): the constructor's form, and `SingletonMode`
  `'FOCUS'` written into App Designer's copy of the code where its serializer puts it, taken out when
  cleared (`JgsMlappModel`).
- **Add property, add function** (`UserEditableBlockBehavior`): `Name % Description` into the
  `properties` block of that access before its `end`, or a new block - at the top, or after the other
  access's; `function results = name(app)` with an empty body into the `methods` block of that access,
  or a new block at the end of the section; each with App Designer's whitespace-only lines, named
  `Property`, `Property2`... and `func`, `func2`... (24 oracle cases). The code browser deletes a
  function through its `end` and a property's line, and renames them through the user's code
  (`app.Old`, `Old(app`; a property also by its bare word on the line that is its declaration alone -
  a longer word, a string and a bare use elsewhere stay, as `_renameUserCode` leaves them). Undone,
  these edits take back the lines they put in or the words they changed, where those still stand;
  code typed in the code view since stays.
- **The user's code** typed in the code view is taken into the document a moment after each edit
  (App Designer's 70 ms): the editable section and each body, matched by the class's structure, which
  the view keeps. It is not an edit of the design - App Designer's editor keeps its own undo - and the
  app is dirty until saved. Text whose functions no longer match the model's is not taken: it stays
  in the editor for the user to put right, the status line says so, and a save or a close stops to
  say so rather than write or lose it.

Every edit of the design leaves the selection as it was - the properties an edit of the code unbinds
along the way select nothing either - and the class is written again from the model after every
edit, undo and redo, the editor's text replaced by the one stretch that differs so that the caret
keeps its place in the text around it.

### The window

Each app's tab has two views, switched by the buttons above it and Shift+F7: the Design View, the
canvas with the component library on the left; and the Code View, the class in the script editor
with the code browser on the left. The component browser, over the inspector's two tabs - Inspector
and Callbacks - is on the right in both. The code view shades the generated lines and keeps them
read-only (`LineReadOnlySectionProvider`: nothing typed on a generated line - the empty line on each
side of the editable section among them - nothing of one deleted, and the line break joining the
user's last line to the `end` below not deleted either; the lines asked for again on every change of
the document, so a new line at the end of a body is open and indented at once), and keeps the caret
through a rewrite. The code browser lists the callbacks in their order (one no component names
marked), the user's functions as `name(app, x)` and properties as `app.Name`, each tab searched and
with its add button; a row picked scrolls the code to it, Go to (Ctrl+D) puts the caret there, a
double-click opens the rename as App Designer's browser opens its in-place editor, Delete and Rename
are on the row and its keys, Insert at Cursor puts `Name(app);` or `app.Name` in the code. The
Callbacks tab shows the selected component's callback properties in App Designer's groups (the
figure's titled), each with a box offering `<no callback>`, `<add ... callback>` and the app's
callbacks, and a button to go to the function; the figure's begin with the app's own `StartupFcn`;
the figure's `SizeChangedFcn` row and a responsive app's region panel's are never editable, as App
Designer's are not; a choice is committed from the open list, by Enter or by leaving the box, not by
the arrow keys walking a closed one; a function made here goes to the Code View, a property bound to
one the app has switches nothing (`AttachCallbackCommand`). The toolbar adds Callback (the Add
Callback Function dialog: a component with an unbound property - the app itself while it has no
startup function - the property, the name), Function and Property (private; the new name selected on
its own line), App Input Arguments (App Details' Code Options: Single Running Instance, Input Arguments
as `arg1, arg2, ...`, tidied and checked as typed; a change of the arguments lands in the Code View
at the startup function, the setting alone switches nothing) and the Single Running Instance toggle,
which follows the document through an undo. A rename from the component browser (of the node under
the mouse) or the code browser shows, before it happens, each line of the user's code that changes
with it, as it is and as it will read - the calls of the callbacks renamed with a component among them.

## Consequences

- A save writes the class whole. Lines a person typed into the editor's shaded regions (ADR 0205) are
  written over by the designer's save, as App Designer writes them over; the editor said so once.
- An app whose generated code is an older release's form is shown and saved in R2025b's form, as App
  Designer regenerates it on load.
- `JgsMlappLayout` gains `SingleInstance`; `JgsMlappModel` writes `SingletonMode`, placed in the
  serializer's field order; `ScriptEditorControl` gains read-only lines, caret-keeping replacement,
  go-to and selection by line.
- The designer's selection no longer resets to the figure after an edit of the code.

## Measured

- **App Designer's own code under node** (`tools/matlab-checklist/ui-probes/ds5/oracle`, kept in the
  tests as `CodeLifecycleOracle.json`, `CodeCallbacksOracle.json`, `CodeTemplatesOracle.json`): the
  lifecycle block for both indent sizes, singleton or not, startup none/plain/with parameters, two
  figure and class names (48 cases); 86 callback comment and signature cases, the sorting, budget,
  wrapping and truncation among them; 22 default callback names; 17 unique names; 24 editable-section
  templates; all equal.
- **Every app R2025b ships** (44, read in place): written back line for line; 8 hold an older
  release's form - six a bare `Callback function` over a callback no component names, one a wrapped
  comment, two Simulink apps' component order (DS1's test holds those against R2025b's generator).
- **The document's edits** (`AppDesignerCodeTests`, 25 tests): callbacks added, bound, renamed and
  deleted with undo; parameters and the startup function; a component's rename and deletion taking
  its callback along; the code view's edits taken back; the single-instance copy field.
- **A review of the stage** (six reviewers over the diff, every finding put to two skeptics reading
  the code against the specs and App Designer's bundle, 30 confirmed and fixed, 13 refuted; each fix
  under a test above): the callbacks block cut by its generated lines, so an open `if` in a body is
  taken (the old cut, by block balance, threw the typed line away 70 ms later and locked the body);
  a loaded callback's default code and its bindings' order; the properties block's reorder on a move
  (`_reOrderProperties`, read in the bundle: builders removed and re-inserted after the previous
  component in view order, each child at its parent's index plus one); the re-wrap width at the
  indent size; a responsive template's layout callback put back on the whole-class road, which DS4b's
  splice had done; a deletion's unbinding generating the comment again; two orphaned callbacks undone
  in order; the Callbacks tab's locked rows, closed-list arrow keys, and switch on a new function
  only; the rename preview's callback lines; App Designer's reserved names; the Code Options switch
  on the arguments alone; the single-instance toggle after an undo; the code browser's selection
  after Add Callback and its double-click; the component browser's right-click; the selection kept
  through a bound callback's deletion; a property rename's bare word on its declaration alone; the
  line-wise undo of Add Property, Add Function and the renames; the separators' read-only lines and
  the provider asked again inside an update; the editor's text replaced by its differing stretch; the
  new property's name selected on its own line; a view-only app's code view taking no typing; a save
  or a close stopping on text the document would not take.
- **R2025b, headless** (`CallbacksApp`, an app the designer's edits built - a button with its
  callback, an edit field bound to a second callback and to the button's as well, `startupFcn(app,
  a, b)`, a single running instance, a private property and a function - written by
  `ANewAppHoldsAppDesignersWholeModel` with `JGRAPH_DS6_OUT`): `ds6_verify DS6_PART=new
  DS6_ONLY=CallbacksApp` reads it `same` (4 components, no finding of its own); `ds5_run` (14
  checks, 0 failed): `CallbacksApp(2, 3)` runs the startup function with its arguments, the button's
  callback runs and calls the user's function, the edit field's shared callback runs, a second
  `CallbacksApp(1, 1)` is the running instance, `delete(app)` deletes it; App Designer's reader
  gives the code copy's class, callbacks, startup function with `a, b`, `SingletonMode` FOCUS and
  the editable section, the tree's button and edit field name their callbacks, and the reader binds
  the callbacks to 2 and 1 components.
- **In a window** (`ds5w.ps1`, UI Automation patterns, nothing typed; the Release app opened a copy
  of the U7b fixture app by `appdesigner(...)`): the Callbacks tab shows the button's
  `ButtonPushedFcn` bound to `GoButtonPushed` with its go-to button on; the Code View switch shows
  the class with the generated lines shaded, the code browser listing the three callbacks, the
  user's `note(app, text)` and `app.Log`; the Add Callback Function dialog, its component set to the
  edit field, offers `AmpFieldValueChanged` and adds it; App Details shows the input arguments `amp`,
  takes `amp, beta` and the single running instance; Save writes the file: the class has the new
  callback with its comment, `function app = U7bApp(varargin)`, `getRunningApp`, `startupFcn(app,
  amp, beta)`; `workspace.json` is unchanged.
- All 588 designer tests pass; the seven verify scripts pass.

## Not measured

- Multi-selection in the Callbacks tab (App Designer edits the common callbacks of several
  components); the App Layout thumbnail of the Code View; App Help Text; the Go To list of the
  toolstrip; Insert at Cursor from the component browser; the context menus' Callbacks sub-menu.
- Any release but R2025b.

## Divergences

- **A rename shows what changes before it happens.** App Designer renames in place and shows nothing.
- **`app.Name` is renamed with the dot as a dot.** App Designer's pattern takes the character after
  `app` as any character, so `app_Name` would be renamed too.
- **The startup function's row is on the figure's Callbacks tab.** App Designer shows it on its app
  node, which the component browser here does not have.
- **Several components selected show no Callbacks tab.** App Designer shows their common callbacks.
- **App Details shows its Code Options alone.** App Designer's dialog has the Sharing Details too.
- **Add Function, Add Property and the code browser's Delete and Rename of them are edits the design
  undoes.** App Designer leaves them to the editor's own undo.
- **The code view refuses text whose functions no longer match the model's.** A `function` line typed
  at a function's indentation inside a body, or a function's `end` taken away, keeps the text in the
  editor, untaken, until it is put right; App Designer's editor takes any text by the lines it marked
  read-only and never looks at the body.
