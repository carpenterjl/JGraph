# ADR 0245 — App Designer's whole model, and a save that keeps what App Designer saved

## Status

Accepted, 2026-10-18. Stage DS6 of the visual app designer plan (`docs/plans/app-designer-plan.md`,
gitignored), after DS1b (ADR 0244). The plan left one question to the user, and the user answered
it on 2026-10-18: when JGraph saves an app made in MATLAB, **keep the old default values** - a
component the save did not change keeps the objects App Designer saved for it. The recordings are
the probe `tools/matlab-checklist/ui-probes/ds6/ds6_verify`, headless, 2026-10-18, over files the
local tests `AppDesignerModelSaveTests` wrote. R2025b's 44 shipped apps were read in place.

## Context

DS1b writes App Designer's component tree, the variable `components` of `appdesigner/appModel.mat`,
from the designer's model. Two things were still missing for decision DD1 (an app must be editable
in both JGraph's designer and MATLAB's App Designer):

- **The rest of the model.** App Designer refuses a package with no model (ADR 0239) and keeps
  `code` (its copy of the code) and `appData` beside the tree. DS1b's window check (7782129) opened
  JGraph's trees only after App Designer's own serializer had re-saved them.
- **A save of an app made in MATLAB.** The editor already saved such an app's code and App
  Designer's copy of it (ADRs 0205, 0239), but left the tree. DS0 showed that App Designer then
  shows the old canvas. Rewriting the tree from the code would lose what only the tree holds: ADR
  0243 found 28 shipped components whose trees hold older releases' defaults, and component groups
  live only there.

DS6 also lists New, Save, Save As, Run, Export to .m and the file browser. Run, Export and opening
an `.mlapp` came with U7 and U7b, and JGraph has no file browser with a New menu; New and Save are
this stage's.

## Decision

### Reading a model whole (`MatMcosFile`, `MatRaw`)

`MatMcosFile.Read` reads a level-5 model as objects that can be changed and written back:

- every object of the subsystem's table becomes a `McosObject` in the form it was saved in (the
  `saveobj` list or the plain one), its stored names in order;
- a word is its text and a literal its number;
- a stored value is kept as the element the file held (`MatData.Kept`), with each reference to
  objects found inside it (`MatRaw.References`: a `uint32` array of the reference mark, its
  dimensions, ids and class id, at any depth) pointing at those objects;
- the variables are kept elements too;
- dynamic properties and the names each object's serialization hook renamed go with their object.

`MatMcosWriter.WriteFile` writes several variables. Seeded with a read file's tables (`McosSeed`),
it keeps the names and classes in their places and appends new ones, because each class's saved
values (the third per-class cell) and MATLAB's enumeration records name classes and names by
index. Objects are numbered as the variables reach them, then the per-class values; every id
inside a kept value is written anew, and its class word is left as it was. Each object's dependency
id is its own id. An object nothing reaches any more is left out. Each element is compressed as
MATLAB writes it.

Layout facts, measured on the 43 level-5 shipped models (one more keeps its model as HDF5):

- The table's last segment is, per object whose hook renamed names, its id, a count and the
  names. The records are not padded; the segment is padded at its end.
- A version 3 table (9 shipped models, from older releases) has two per-class cells, not three,
  and no renamed-names segment. A version 4 table may also leave that segment out, its last two
  offsets zero (`borrowLicenses`).
- Written back, every table is version 4, as R2025b writes.

### Bringing a saved tree to the design (`DesignTreeWriter.Update`)

A component is matched to its saved object by code name and class. What App Designer stored with
the object (`DesignTimeProperties.ComponentCode`) is held against the component's lines in the same
form (`DesignCodeWriter.StoredComponentCode`):

- **The same lines, in any order: the object is kept as it is.** Every value only the tree holds
  stays, an older release's defaults among them. An older generator's line order is no change.
- **Changed lines: the object is kept, and only the changed properties are written over it.**
  Each property is stored as a new object of its class would store it (`Fill`, which records which
  stored names each property wrote), moved last so that `load` assigns it after everything the
  object held. An axes whose `Position` changed loses its position stores, as in DS1b.
- **A line that went: its property is set back to its default**, its mode to `'auto'`. It is not a
  change when the generator here leaves the line out but the value is the same:
  - a grid child's `Position`, which the grid sets and App Designer stored before R2025a;
  - a line setting the value the design holds, or the default (two shipped Simulink apps' axes
    state their tick rotations).
- **A change that cannot be written over the object makes it anew** from its class's base, as a
  new component is made, keeping its component group: a line this cannot name, or a property
  that cannot be set back over the old object (a grid place, an axes toolbar shown again, a
  colormap).
- **New components** are made from DS1b's bases.
- **A saved component the design no longer has leaves**, with every stored name of a kept object
  that refers to it.
- **References follow the design.** Each container keeps its saved children in their saved order:
  an older release saved some containers' children in the order made, not MATLAB's reversed order.
  A child new to a container goes in front, the last made first, where MATLAB puts a new child.
  A context menu follows the design, and so does a tab group's selection when its selected tab
  left. A tab's size follows its group's changed `Position`.
- **Renamed names.** In a file MATLAB saved, each class keeps its saved values beside the stored
  names. An object whose hook stored a public name for its `_I` storage lists that storage as
  renamed. An object written here that stores `FontColor` while the class's values hold
  `FontColor_I` lists it too. Without that, R2025b warns that the name "already exists in the
  serialization content" (found by `ds6_verify` on the first new button in a shipped app).
- **A responsive app's containers.** In a responsive app, App Designer records
  `AppTypeProperties.IsResponsiveContainer` (logical true) in the `DesignTimeProperties` of the
  grid on the figure and of each panel in it. Its reader refuses a responsive app without them.
  DS1b's tree writer now gives them, so a new responsive app and a component made anew both have
  them.
- Component groups (`components.Groups`), `code`, `appData` and any other variable stay as they
  were. A tree the design does not change is the model as it was, to the byte.

The tree is left as it was, and the save says so, when:

- the model is a version 7.3 file (open item 147);
- the text cannot be read as an app;
- a line of its `createComponents` cannot be read: an unread line would read as a property set
  back.

A changed component of a class the designer has no base for (the Simulink components, open item
148) keeps its saved object unchanged, and the save lists it.

### A new app, written whole (`JgsMlapp.WriteApp`)

A new `.mlapp` holds what App Designer writes:

- **the code**, as before;
- **the model**, in one subsystem:
  - `components`, a struct whose `UIFigure` is DS1b's tree;
  - `code`, App Designer's copy made by the rules a save follows when it has no old text to hold
    the copy against (`JgsMlappModel.NewCode`): the class name, the editable section, each
    callback and the startup function with their bodies, and the input parameters, each field
    present only when it holds something;
  - `appData`, an `AppData` object of three empty values and `MinimumSupportedVersion` (`R2018a`,
    `R2019a` for a responsive app, as the shipped apps have);
- **the metadata parts:**
  - the app's metadata: description, MLAPP version 2, oldest release, screenshot mode, a new
    UUID, the app type, empty product, image and user-component lists;
  - the OPC core properties, with the time;
  - MathWorks' core properties, naming R2025b, the release whose format this writes;
  - their extension, with a new UUID.

Two parts App Designer writes are left out: the screenshot, and the part recording MathWorks' own
build number. A text whose `createComponents` cannot be read is written as its code alone
(ADR 0239).

### Saving, and the editor

- **`JgsMlapp.WriteCode`**, the save into an `.mlapp`, splices the code copy as before. When the
  save changed generated code and the text reads as an app, it then brings the tree to the text's
  `createComponents`. `MlappSaveResult.Tree` says what it did: the components kept, changed with
  the properties written over each, added, made anew, removed, and the problems. Save As of an app
  copies the package and saves into the copy, so the tree follows there too; a rename alone
  changes nothing in it.
- **File > New Script > MATLAB app (.mlapp)** opens App Designer's blank app: a class `app1` whose
  figure is its only component, its component code written by DS1's writer. The tab is
  `NewApp.mlapp`, generated regions are shaded, and Save offers the `.mlapp` type first.
- **Saving a class as a new `.mlapp`** (`AppDesignerDocument.SaveNew`) writes it whole when its
  `createComponents` reads, and as its code alone otherwise. The status line says which: "MATLAB's
  App Designer opens it with its canvas", or why it cannot.
- **The status line after a save** tells what the tree save did, for example "App Designer's
  component tree was brought up to date (1 component changed, 1 added, 1 removed); every other
  component is as App Designer saved it". If the tree was left, it says why and that App Designer
  will show the components it had.
- **The once-only notice** about generated code now says that a change to `createComponents`
  reaches App Designer's canvas. The rest of the generated code is still App Designer's to write
  again.

## Measured

- **Reading and writing back** (`EveryShippedModelWritesBackAsTheSameObjects`). All 43 level-5
  shipped models read, and write back as the same variables and the same objects in the same forms
  with the same stored names and values, numbered as reached. Writing what was written changes
  no byte.
- **Under each app's own code** (`EveryShippedTreeKeepsItsComponentsUnderItsOwnCode`). All 1067
  saved components of the 43 apps are kept, none written over, and each model comes back to the
  byte. Before the rules above, 49 components were written over: 47 for a grid child's `Position`
  line and 2 for an axes' tick rotations. The fixture apps made by MATLAB's serializer from a
  figure built in code (`U7bApp`) hold no `DesignTimeProperties`, so nothing matches by code name
  and the tree is made anew (`ATreeWithoutAppDesignersRecordsIsWrittenAnew`).
- **Edits** (`AnEditedShippedAppKeepsEveryOtherComponentAsSaved`). In each of the 43 apps, the first
  label, button or check box has its `Text` changed, the last label no other component names is
  removed, and a button is added to the figure. The text is regenerated and saved through
  `WriteCode`. Each tree lists exactly those three things. Every other saved component is, object
  for object, as App Designer saved it (its stored names and values, nested helper objects
  included), apart from the children of the figure and of the removed label's container.
- **Setting back and making anew** (`ALineThatWentSetsItsPropertyBack_AndAMoveOutOfAGridMakesTheComponentAnew`).
  A label whose `FontColor` line went keeps its saved object, with `FontColor` and then
  `FontColorMode` `'auto'` stored last. A label moved out of its grid is made anew, is listed as
  made anew and not as removed, and stands in front of the grid among the figure's children.
- **App Designer's reader on the edits** (`ds6_verify`, part 1). Each original and each edit was
  opened through App Designer's own reader (`DeserializerFactory ... getAppData`), with no
  serializer in between. In all 43 apps, every component the edit did not touch (923 in all) loads
  with every property, hidden ones included, as the original's (`ds1b_objdiff`). The changed
  component differs only in its `Text`, and its stored code is R2025b's generator's on the loaded
  component. The removed one is gone, and the added button has its text, place, parent and the
  generator's stored code. Each edit has the same class name and as many callbacks bound, and no
  warning the original lacks. This includes the two Simulink apps, whose Simulink components DS1b
  cannot write: a save keeps them.
- **New apps** (`ANewAppHoldsAppDesignersWholeModel`; `ds6_verify`, part 2). The shipped apps'
  code, the fixture app and the blank app were written whole: 46 apps. Each holds the eight parts,
  the three variables and a code copy that reads back. Brought to its own design again, nothing
  changes. Opened as written through App Designer's reader (`ds6_verify`, part 2,
  `ds6_verify_new.out`), each was held against a control: the tree that
  `createComponents`, as App Designer's generator writes it, makes when run, saved by App
  Designer's serializer and opened the same way. Each was opened from its source app's folder,
  where an image or page named without a path is found. 41 of the 46 have no finding the control
  lacks: every code name, every component as the code makes it, the generator's stored code, and
  every callback bound. Of the other five:
  - two Simulink apps lack the Simulink components (open item 148);
  - in two apps an axes' camera view angle and plot-box ratio differ in the last digits, DS1b's
    finding (the written tree holds its final size's ratio);
  - the responsive app has App Designer's responsive-container record on its grid and panels,
    which the control, made by running code, lacks.

  The first run found the record missing from JGraph's tree. Without it, App Designer's reader
  refused the app (`MATLAB:appdesigner:appdesigner:IncompatibleAppVersion`, from
  `MLAPPResponsiveAppValidator`), so the writer now gives it.
- **The editor's pieces** (`ABlankAppIsLaidOutAsAppDesignerLaysItOut_AndIsSavedWhole`,
  `AVersion73ModelsTreeIsLeft_AndTheSaveSaysSo`, `TheStatusLineSaysWhatTheTreeSaveDid`, and
  `AppDesignerNewPackageTests`):
  - the blank app reads as App Designer lays it out, and its component code is DS1's writer's;
  - saved under a new name, it takes the file's name and is written whole;
  - an edit to a version 7.3 app's `createComponents` is saved and its copy spliced, while its
    tree is left and the status line says so;
  - a plain class still gets the code alone.

## Not measured

- **App Designer's window over these files** (open item 149). The reader is App Designer's own,
  but a grid child's position is the grid's to lay out, and the reader does not lay out a grid
  headless. Such a component's stored position is its base's, which the canvas replaces.
  `ds6_verify` leaves a grid child's position family out of its comparison.
- A dark-themed app, and any release but R2025b.

## Consequences

- A JGraph-made app opens in App Designer as JGraph wrote it, and an app made in MATLAB and edited
  here keeps everything App Designer saved that the edit did not change. A change to
  `createComponents` made in the code editor reaches App Designer's canvas.
- `MatMcosWriter.Write` (DS1b) is now `WriteFile` with one variable. `McosObject` knows its saved
  form and renamed names. `MatData` has objects and kept elements.
- `DesignTreeWriter` is partial: the tree writer, and the update.
- `MlappSaveResult` carries the tree save. `AppDesignerDocument.SaveNew` returns what it wrote, and
  `ScriptDocumentModel` knows a new app.
- No script behaves differently.

## Divergences

- **A model this rewrites is not byte for byte what App Designer would write.** Its table is
  version 4 whatever it was, its objects are numbered as reached with each dependency id its own
  id, and its elements are compressed by .NET's zlib. R2025b's reader loads it as the original
  (`ds6_verify`).
- **A version 7.3 model's tree is not rewritten** (open item 147). The code and its copy are saved,
  and App Designer shows the components the tree had. The status line says so.
- **A new app has no screenshot**, and no part recording MathWorks' build number. App Designer
  makes the screenshot when it next saves the app.
- **A changed component of a class the designer cannot make** (a Simulink component, open item
  148) keeps its saved object unchanged, and the save lists it among its problems.
