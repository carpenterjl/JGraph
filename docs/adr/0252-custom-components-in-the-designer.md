# ADR 0252 — Custom components in the designer

## Status

Accepted, 2026-10-20. Stage DS7b of the visual app designer plan (`docs/plans/app-designer-plan.md`,
gitignored; DS7 was split into DS7a, the plain-text format, ADR 0251, and this stage in revision 12).
App Designer's handling of a user-authored component - its registration file, library entries,
defaults, design-time view, inspector, callbacks, generated code, metadata and missing-class dialogs -
was read from R2025b's canvas bundle (`appdesigner/application/UserComponentRegistrationService`,
`userauthored/*`, `model/UserComponentClassTracker`, `application/AppLoader`,
`UserComponentModificationDetector`) and its MATLAB side (`appdesigner.internal.usercomponent.metadata.*`,
`UserComponentAdapter`, `ComponentCodeGenerator`, `inspector.internal.UserComponentPropertyView`,
`DesignTimeWebUIContainerController`, `MLAPPUserComponentValidator`, `restoreCallbackComponentData`,
the plain-text generator's `isUacCallbackFunction`). Nothing of it is kept. R2025b was measured
headless by the probe `ds7_uac` (`tools/matlab-checklist/ui-probes/ds7`, eight sections over
`SpinnerGauge`, `U10Probe`, a packaged `ds7pkg.PkgGauge` and `Ds7Types` with one property of each
kind), and the app this build saves with a `SpinnerGauge` was read, loaded and run there by
`ds7_uac_verify`.

## Context

A custom component is a class under `matlab.ui.componentcontainer.ComponentContainer`, which this
build has run since U10 (ADR 0209). App Designer puts such a class in its library once
`appdesigner.customcomponent.configureMetadata` has written `resources/appDesigner.json` beside it,
makes an instance by a call of the class, writes its properties as its adapter reads them from the
running class, shows them in an inspector group named by the class, lists its `HasCallbackProperty`
events' callbacks in the Callbacks tab, keeps the instance in the `.mlapp`'s tree as an object of the
class and the class's name in the package's metadata, and - when a saved app names a class it cannot
find or make - asks before opening the app without those components. The designer (DS2-DS6) knew the
45 classes of its catalog alone: a component of any other class came back from code as a call of its
name with its properties in the order read, with no library entry, no inspector, no tree object and
no metadata. The plan took the stage after the plain-text format, whose appendix names a component by
its class's last segment, since both ride on the same question: what the designer knows of a class it
has no recording of.

## Decision

### The class is described from the running class

- `DesignRuntime.UserClassOf(className)` describes a custom component's class the first time it is
  asked for, as `UserComponentAdapter` and the design-time controller describe it: a fresh instance
  is made in the design's figure by a call of the class (its `setup` runs, as App Designer's
  `createDesignTimeComponentWithCleanup` runs it), `properties()` is read in order, every property
  the generator never writes (`Children`, `CreateFcn`, `DeleteFcn`, `DesignTimeProperties`,
  `InnerPosition`, `OuterPosition`, `Parent`, `UserData`, `Interactions`, `ContextMenuId`,
  `ContextMenu`, `ButtonDownFcn`, `Clipping`, `Units`) and every property only the class may set
  (`SetAccess` not public; a dependent property with a getter and no setter) is left out, `Position`
  goes last, and each remaining property takes the fresh instance's value as its default - the
  background colour by theme, as App Designer's defaults have it. The inherited properties App
  Designer governs by a hidden `Mode` sibling (`Visible`, `BackgroundColor`, `SizeChangedFcn`,
  `HandleVisibility`, `Tag`) are written only once set. The instance is deleted. The result is a
  `DesignComponentClass` marked `IsUserComponent`, made by a call of the class's full name, which
  the generator, the plain-text writer, the inspector and the Callbacks tab use as they use a
  catalog class: `app.Gauge = SpinnerGauge(app.UIFigure);`, then the set properties in
  `properties()` order, a callback as `createCallbackFcn(app, @Name, true)`, `Position` last - the
  lines R2025b's generator wrote for the same gauge (probe section 2).
- A class is found as the session finds it: in the app's folder, and on the session's path, which
  the designer's window takes from the MATLAB session (`IScriptSession.PathFolders`) and hands to
  the document (`DesignDocument.Open(path, pathFolders)`). A component read from code as a call of
  a class the catalog does not know is given its class when the runtime can describe it
  (`DesignComponent.Class` is set after the read); the Simulink components, made by functions, are
  not custom components.
- The inspector's groups (`UserComponentPropertyView`, the controller's editor filter): the class's
  own public properties the view shows - a declared class or a non-empty default, not a callback, a
  cell only as a vector, a char only as a row, a string only as a scalar - each edited by what it
  holds (numbers, a logical, text, a cell) and shown without editing when only the class may set it,
  under the class's short name, expanded; then Color, Interactivity, Position, Callback Execution
  Control, Parent / Child and Identifiers as every component has them. A property the inspector
  has no editor for - a struct, a date, an object, an enumeration - is left out, as the controller
  drops it.
- The callback properties are the class's `HasCallbackProperty` events' `NameFcn`, in the class's
  order, then `SizeChangedFcn` (`getCallbackPropertyNames`); the Callbacks tab's one group is titled
  by the class's full name; a new callback is named by the code name and the property as every
  component's is, with the class's short name as the default code name; its first lines are those
  App Designer keys by the property's name alone (`value = app.Gauge.Value;` for a `ValueChangedFcn`).

### The library and the canvas

- `DesignUserComponents.Discover` reads every `resources/appDesigner.json` in the app's folder and
  on the path as App Designer's registration service reads them: the `components` array, each
  entry with the schema's defaults where a key is missing (the version as a number or a string,
  the default position as numbers or the schema's own text); a class registered twice is the later
  set's. The entries make library sections after App Designer's own, one per category titled
  `Category (Custom)`, in case-insensitive order of their titles, each section's entries by name in
  ordinal order; an entry's tooltip carries the name, version, description, author and file
  location, as App Designer's does.
- An entry makes a component of its class as the runtime describes it, named by the class's short
  name made unique among the app's names (`Gauge`, `Gauge2`), sized by the registration's default
  position - or as drawn - with no label (`_UserComponentEntryDefinition`); a class the runtime
  cannot make refuses the entry with why. The canvas shows the instance the runtime makes, which
  is the class's own `setup` drawn.

### The files

- The `.mlapp`'s tree holds the instance as an object of its class (`DesignTreeWriter`): what every
  `ComponentContainer` subclass stores besides its own properties, recorded by `ds7_base` from the
  `SpinnerGauge` the probe saved (`ds7_uac_base.json`, embedded: the position stores, the
  background colour, the child validation function, the modes), under the class's name, with the
  class's own properties as the code set them, its bound callbacks in the
  `SerializableCallbackPropertyFcns` struct its mixin saves - written whole again whenever one of
  them is set or set back over a saved object - and App Designer's `DesignTimeProperties`. Loaded
  in R2025b it is the same as a gauge made there (`ds1b_objdiff`), at the base's position and off
  it, its callback bound, and App Designer's reader restores the callback's component data.
- The package's `metadata/appMetadata.xml` lists the classes the app uses as
  `<userComponents><userComponent class="X"/></userComponents>` (`JgsMlapp.WriteApp` for a new
  file, `FollowUserComponents` on every save), and a plain-text app's internal data lists them as
  `UserComponents`; both from the design, as App Designer's class tracker keeps them.
- A plain-text appendix names a custom component by its class's last segment, as App Designer's
  writer does, and reads it by the declared class, which wins over the element's name; an element
  whose name is no class is refused at run time as R2025b refuses it (DS7a).

### A class that cannot be loaded

- Opening an app whose code names a custom component class the runtime cannot find reports it
  missing; one it finds whose component could not be made (a `setup` that fails) errored
  (`DesignDocument.MissingUserComponents`, `ErroredUserComponents`, as `MLAPPUserComponentValidator`
  tells them apart). The window shows App Designer's dialog - "Some components in this file could
  not be loaded.", the classes with an error, the missing classes with the word to put them on the
  path, the warning that opening removes them - with Open Anyway and Cancel; Open Anyway takes
  those components out of the design before the app opens, their callbacks staying in the code
  unbound, nothing to undo and nothing marked changed, so that the next Save writes the app without
  them, as App Designer's does. Cancel leaves the app closed (`appdesigner` reports `cancelled`).

## Consequences

- An app holding a custom component opens on the canvas with the component drawn, is edited with
  its own inspector group and callbacks, is saved as an `.mlapp` App Designer opens with the
  component in its tree, and as a plain-text file; the library offers every class registered in the
  app's folder or on the session's path.
- `DesignComponent.Class` is settable within the assembly; `DesignComponentClass` gains
  `IsUserComponent`; `DesignPaletteEntry` gains `UserComponent` and `Key`; `DesignDocument` gains
  `UserComponents`, `UserPaletteEntries`, `FindPaletteEntry`, `InspectorGroupsOf`,
  `MissingUserComponents`, `ErroredUserComponents` and `RemoveUnavailableUserComponents`;
  `DesignRuntime` gains `UserClassOf`, `KnowsClass` and the session's path; `IScriptSession` gains
  `PathFolders`; `JgsClass` gains `HasGetter` and `HasSetter`; the interpreter `FindClass`.

## Measured

- **R2025b, headless** (`ds7_uac`, 707 lines): the metadata file `configureMetadata` writes (its
  keys and order, `"version": "1.0"`, `defaultPosition` as four numbers, `avatar` empty headless,
  `[]` once the last class is removed, a packaged class's root folder); `properties()` order,
  `SetAccess` and `Dependent` of every property of the four classes; the adapter's code-generation
  list, the non-public exclusions, the callback names (`ValueChangedFcn, SizeChangedFcn`), the mode
  siblings, the run-time defaults in each theme, the generated lines under a figure, moved, in a grid
  and spanning, and the dirty properties; `Ds7Types`'s lines per kind - `int32`, `single` and
  `struct` generate no line, a 2-D cell generates `app.Types.Grid2 = ;`, a string drops its line
  break and keeps its quotes unescaped, `{3.14159, 2}` loses digits; the inspector's groups and each
  property's editor (struct and datetime dropped); the design-time defaults; the saved tree's
  `SpinnerGauge` object (21 stored names) and `<userComponents><userComponent class="SpinnerGauge"/></userComponents>`;
  the validator's token (every declaration, not the last alone); an errored class reported
  `ErroredUserComponents` and dropped on load; the plain-text file with two custom components read
  and run, a packaged class refused by its short tag and taken by its full one; a renamed and an
  off-path class reported `MissingUserComponents` and dropped, a plain-text app with the class gone
  refused.
- **The apps this build saves** (`ds7_uac_verify` over `UacDesigned.mlapp`, written whole from its
  code, `UacEdited.mlapp`, the same app saved again over its tree with the gauge moved off the
  recorded base's place to `[30 40 200 70]`, its value set to 55 and its callback bound to
  `GoButtonPushed`, and `UacDesignedPlain.m`; all left by `TheProbesAppIsWrittenWhenAsked`): App
  Designer's reader loads each with no warning, the gauge of its class with its values and bound
  callback, the callback's component data restored (the rebound one listed under `GoButtonPushed`
  beside the button's); `load` of each model gives a gauge the same as one made in MATLAB with those
  values (`ds1b_objdiff`); each app runs with the gauge and its callback; the plain-text file reads
  and runs the same.
- **The tests** (`AppDesignerUserComponentTests`, 9): the metadata file with its defaults, the
  sections' order, a class registered twice; `SpinnerGauge` described as the adapter describes it,
  the probe's generated lines, the Callbacks tab, the inspector's groups; a library entry adding
  `SpinnerGauge` and `SpinnerGauge2`, undone and redone, its drop box, and one added into a grid's
  cell; the classes in the package's metadata and the plain-text internal data, the tree's object
  with its struct of callbacks, the plain-text file reopened with the class present; a class found
  on the path alone, a save over the saved object with a changed value, a moved component and a
  rebound callback, a class file that does not parse reported errored; a missing and an errored
  class reported, the missing one taken out with its callback kept; typed numbers and text in the
  inspector and the code; a packaged class by its last segment and its declaration. Every designer
  test passes (624); the full suite passes (11940).
- **Review before the commit** (a workflow of four Opus reviewers - the described class and the
  registry, the files, the integration, the tests and probes - every finding sent to two Opus
  skeptics in turn; 32 confirmed, several the same defect seen from two sides, 5 refuted): the
  session's path folders were thrown away by the eval builtins' registration, so a class on the
  path alone was never found; a class file that did not parse threw out of Open instead of being
  reported errored; a rebound callback was not written over a saved object; dragging a library
  entry or drawing one into a grid threw for a custom component; the Callbacks tab listed every
  `Fcn` property rather than the described class's; a custom `ClickedFcn` started with a check-box
  tree's code; Open Anyway deleted the blank callbacks it said it kept; the session's current
  folder was not among the folders the designer searched; a plain-text app's Simulink components
  were taken for custom ones; a failed construction left its parts in the figure; `int32` and
  `single` properties vanished from the inspector; the background colour was the theme's even
  when the class set its own; the recorded base's position stood in for a component's own; a
  user's own `FooMode` property was written as a mode; Cancel in the dialog read as a failure.
  All fixed under tests; what remains different is recorded below.
- **In a window** (`scratch/ds7bw/ds7bw.ps1`, UI Automation patterns, nothing typed; the Release
  app opened `UacDesigned.mlapp` by `appdesigner(...)` with `SpinnerGauge.m` and its
  `resources/appDesigner.json` beside it): the library shows the section `My Components (Custom)`
  with the `SpinnerGauge` entry; the gauge is drawn on the canvas by its own `setup` and `update`;
  selected in the component browser it shows the inspector's `SpinnerGauge` group with `Value`,
  `Limits` and the read-only `SetupCalls` and `UpdateCalls`, then the six common groups, and the
  Callbacks tab titled `SpinnerGauge` with `ValueChangedFcn` and `SizeChangedFcn`; the Code View
  lists the two callbacks; Save rewrites the file with App Designer's tree brought up to date;
  `workspace.json` unchanged. With the class file taken away: the dialog "Some components in this
  file could not be loaded. Missing custom UI components: SpinnerGauge ..." comes up before the
  window, Open Anyway opens the app with the gauge gone from the canvas and the browser and nothing
  marked changed.

## Not measured

- App Designer's window on an app this build saved with a custom component (open item 160 covers
  the plain-text side; the `.mlapp` side waits with it).
- A licensed (toolbox) component set, which goes to `RequiredProducts` rather than
  `UserComponents` and keeps its bare category; the avatar drag image; an entry disabled on its own
  class's canvas; copy and paste of a custom component.
- A custom component whose non-transient property holds a handle (`U10Probe`'s parts): what R2025b
  stores for it is not probed, and this build stores the class's own properties only as the code
  sets them, or at their defaults when the class has been described.

## Divergences

- **A custom component's own properties are stored as the code set them.** R2025b stores every
  non-transient property of the instance, at its default or not (`SetupCalls`, `UpdateCalls` among
  them); the tree here is written from the code, which has no class to give the defaults. Loaded,
  the rest take the class's defaults, which is what the code gives them too.
- **A property whose value App Designer cannot write is written here.** R2025b's generator skips a
  property of class `int32`, `single` or `struct` (and the inspector refuses the plain-text save),
  writes a 2-D cell as `app.X.P = ;`, drops a string's line break and leaves its quotes unescaped,
  and rounds a cell's numbers to six digits; this build writes a typed number as a plain number
  (the class's setter types it again) and each other value as the design value's text.
- **An enumeration property is left out of the inspector.** R2025b shows a drop-down of its members.
- **A class is described once per document.** R2025b's adapter reads the class again on every
  generation, so a class edited on disk takes effect there without reopening the app; here the app
  is reopened.
- **A class on the path but not registered stays in the app.** App Designer removes the components
  of a class its library has no entry for (`AppLoader._getUserComponentsWithoutDefinition`) even
  when the class is on the path; here the class runs, so the component stays, and the library alone
  lacks it.
- **A class registered in two folders has one library entry**, the later folder's; App Designer
  shows one entry per set.
- **The registry is read once, as the app opens.** App Designer re-checks its component sets when
  one is registered, removed, enabled or disabled during the session, and shows its "can no longer
  be found" dialog for an app that loses one; here the app is reopened.
- **A registration file is taken as it is.** App Designer's client drops every entry of a file in
  which one entry lacks its `defaultPosition`, `description` or `icon`, and shows no version for a
  numeric one; here the schema's defaults fill in what is missing, and a numeric version is shown.
- **A custom component's position is always stored**, with its mode `manual`, as the design or the
  described class gives it - the recorded base holds the probe gauge's place, which must never be
  loaded as a component's own. R2025b stores the position where the class's `setup` set it, with
  the mode `auto` when nothing set it; a component saved from code alone that sets no position
  and whose class is not described takes the base's place when loaded.
- **A user's class named like a built-in element reads as the user's in a plain-text file.** The
  declared class wins over the element's name here; R2025b's generator takes the built-in
  (`isUac = strcmp(informalInterface, className)`, which its own source marks as an assumption to fix).
- **The `userComponents` list is the design's order.** App Designer's tracker keeps the order each
  class was first seen in the session, a class never leaving the map.
- **A plain-text app whose custom component class is missing opens with the dialog.** R2025b's
  plain-text loader refuses the file (`AppAppendixUnknownComponent`), its validators being the
  `.mlapp` reader's alone.
- **The default code name is the class's short name, unique by number** (`SpinnerGauge2`); App
  Designer's generator does the same, and this build counts the code names and the code's names
  together.
