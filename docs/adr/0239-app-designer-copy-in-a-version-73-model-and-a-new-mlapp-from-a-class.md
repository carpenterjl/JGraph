# ADR 0239 — App Designer's copy of the code in a version 7.3 model, and a new `.mlapp` from a class that never was one: open items 57 and 58

## Status

Accepted, 2026-10-17. The twenty-sixth batch of the open-items work
(`docs/plans/open_task_chips_09_12_2026.md`, gitignored): items 57 and 58, both found in
app-building stage U7b (ADR 0205). The recordings are `probe_57a` (the shape of the one shipped
app's HDF5 model, read in place), `oi57_build` (this project's U7b app written by R2025b's own
serializer under MATLAB's version 7.3 MAT-file preference) and `oi57_verify` (R2025b loading,
reading and running what JGraph wrote), all headless, 2026-10-17.

## Context

**57.** An `.mlapp` holds its code twice: the class file MATLAB runs, and in
`appdesigner/appModel.mat` the variable `code` App Designer rebuilds its code view from. ADR 0205
keeps the two in step by rewriting `code` inside the MAT-file around the MCOS objects beside it
(the component tree, a legacy object) that this build cannot decode. That rewrite was a splice of a
level-5 file's elements. One of the 44 apps R2025b ships (`serdes/SignalIntegrityLink.mlapp`) keeps
its model as a version 7.3 file, which is HDF5, and so does any app saved while MATLAB's MAT-file
preference is version 7.3 (`oi57_build`: `MLAPPSerializer` follows the preference). Such an app's
code was saved and its copy left as it was, the status line saying App Designer would show the code
it had. The reader side existed (`MatV73Reader`); the writer ADR 0235 added writes whole files of
plain values and refuses objects, which the model is mostly made of.

**58.** The editor saved an app's code back into the package it came out of, or into a copy under a
new name, and refused to save any other document as an `.mlapp`: an exported `.m` app or a class
written by hand had no package to go in. A package MATLAB runs needs three parts (the U7 fixtures
are built that way by `build_mlapp.py`); one App Designer opens needs a component tree, which only
App Designer writes.

## Decision

### 57: splice the new `code` into the HDF5 file, touching nothing else

`probe_57a` read the shipped model as HDF5: a version 0 superblock after the 512-byte MAT-file
userblock, an old-style root group whose entries are `#refs#`, `#subsystem#`, `code`, `components`,
`appData` and `runConfigurations`; `/code` a group carrying `MATLAB_class` struct and the field
order in `MATLAB_fields`, its char fields `uint16` datasets, its cell a dataset of object references
to one char dataset per line, its struct arrays (`Callbacks`, 1-by-19) groups of two reference
datasets and its 1-by-1 struct (`StartupCallback`) a group of the two values, an empty struct
(`AppTypeData`) a dataset marked `MATLAB_empty`; every child also carries an `H5PATH` attribute that
nothing needs; the stored end-of-file is the file's whole length, as ADR 0235 found.

- **`MatV73Splice`** continues the file: `MatV73Writer`'s builder is started at the file's end (a
  `Builder(long start)` whose addresses count from there), the new `code` group and whatever it
  needs are appended in the layout the writer already writes, the root group's symbol-table entry
  for `code` is re-pointed at the new group - its object header, and the B-tree and heap the entry
  caches - and the stored end-of-file moves to the new length. Not a byte of the rest is written;
  the old `code` objects stay where they were, unreachable. Objects the new value refers to (the
  lines of a cell) are appended without a link in `#refs#`: a reference is an address, and HDF5
  opens an object by its address whether or not a group names it. Only R2025b's own shape is
  spliced (`CanSplice`: superblock at 512, version 0, eight-byte addresses, base 512, an old-style
  root); anything else is left as ADR 0205 left it, and said to be.
- **One model of the `code` struct for both forms.** `JgsMlappModel` reads the struct into
  `CodeFields` - text, lines, functions (a `Name`/`Code` array), empty, or opaque - each value
  remembering how the file held it (a level-5 matrix element's bytes; a version 7.3 object's
  address). The rules of ADR 0205 run on the model unchanged: a field is rewritten only when the
  text it was cut from accounts for it, a field is present exactly when it holds something, and a
  field or a callback body the text never matched is written back as App Designer wrote it. The
  two codecs are `Level5Copy` (the element splice of ADR 0205, moved) and `V73Copy`.
- **What a version 7.3 save appends is the change.** A field this did not change keeps its
  object (the new group lists the old address). A cell is a reference dataset over one object per
  line, and a line whose text the field already held as a line keeps that object: the shipped app's
  editable section is 2,268 lines, and an edit to it appends the lines that changed, the
  reference dataset, the names and reference datasets of a rewritten struct array, the group, and
  the 4,096-byte global heap the field names need (ADR 0235). A callback whose body is unchanged
  keeps its body object. A one-element `Name`/`Code` array is written as a group of the two values
  and a longer one as a group of two reference datasets, which is what MATLAB writes for each.
- **Reading the copy** (`JgsMlappModel.Read`, the tests' check) goes through the same model, so the
  shipped-apps test now holds the version 7.3 app to the same rules as the 43 others.

### 58: a class saved as an `.mlapp` gets a package of the code alone, and is told so

- **`JgsMlapp.WritePackage`** writes the three parts MATLAB needs - `[Content_Types].xml`,
  `_rels/.rels` and `matlab/document.xml`, the code in one CDATA run in line feeds - byte for byte
  as `build_mlapp.py` writes the U7 fixtures (test
  `ANewPackageHoldsThePartsTheFixtureBuilderWrites_ByteForByte`), beside the target and moved onto
  it once complete.
- **`AppDesignerDocument.SaveNew`** renames the class to the file, as App Designer's Save As and
  MATLAB's export do, refuses text that is not a class ("would hold no class … Save it as a .m file
  instead") and a name a class cannot have, and leaves nothing behind when it refuses.
- **The editor** offers the `.mlapp` type in Save As for any MATLAB document, writes the package
  for one that is not already an app, re-homes the document to it (it is an App Designer file from
  then on: a later save goes back into the package, Export is offered), and says on the status
  line that the file holds the code alone, runs here and in MATLAB and opens here, and that App
  Designer cannot open it. No dialog: the note is the whole of what there is to know.
- **Why not more.** A `code` variable could be written beside the document (the level-5 writer can),
  but without a component tree it is nothing App Designer can use, and the tree is MCOS. The
  visual designer's plan (`docs/plans/app-designer-plan.md`, DS0) asked what App Designer does with
  an `.mlapp` that has no model; `oi57_verify` answers the headless half: its `FileReader` refuses
  the package at every reader (`MATLAB:appdesigner:appdesigner:LoadFailed`, "Error loading
  'U7MlApp.mlapp'."), `matlab.internal.getCode` reads it and MATLAB runs it. That is the plan's
  outcome (c) for a file without a model, and the designer's own writer is for the plan.

## Measured

- **R2025b reading what JGraph spliced** (`oi57_verify`, 2026-10-17). The shipped app with a comment
  line put into a callback, the startup function and the editable section by the shipped-apps
  test, and this project's `U7bApp73` with a real edit in each region (a line with `]]>`, `é` and
  `中` among them): `load` of the whole model gives `appData`, `code`, `components` (and the
  shipped app's `runConfigurations`) with `components.UIFigure` a `matlab.ui.Figure`; `h5info`
  lists the same root and the new `code` fields; `readAppCodeData`, `matlab.internal.getCode`,
  `readAppDesignerData` and the deserializer's full `getAppData` give every edit in place and the
  same callbacks as the copy; the sample runs with its edited startup function and callback
  acting; the stored end-of-file is the file's length. The shipped model grew from 2,366,773 to
  2,400,656 bytes for the three edits (its first splice, before lines kept their objects, had
  taken it to 3,210,656) and the sample's from 326,592 to 337,016.
- **Every shipped app** (44, read in place; test
  `EveryShippedAppsCopyIsWhatItsTextGives_AndSavingItsOwnTextChangesNothing`): the version 7.3 app's
  copy is what the layout cuts from its text, as the 43 others' are, and saving its own text
  leaves every part byte for byte.
- **The splice writes in two places** (`AppDesignerV73ModelTests`): after an edit in every region,
  the old bytes of the model differ from the new file's prefix only in the 40-byte root entry of
  `code` and the eight-byte end-of-file; twelve of the section's thirteen lines and three of the
  edited callback's four are the objects they were, the untouched callback's body is its old object,
  and the generic reader describes the other variables as before. A callback added, the list cut
  to one (the one-element form), every function and the section removed (the field list is
  `ClassName` alone) and the text restored, each save splicing in turn, leave a model that reads
  whole.
- **Fixture `oi_app_v73_package`** (22 rows, no divergence): the version 7.3 app is found, typed and
  run alike in both engines, and the code-only package is found as a class beside it.
- **A new package** (`AppDesignerNewPackageTests`): the parts equal the fixture builder's byte for
  byte; a class saved under a new name takes it, opens here as an App Designer file, runs, and a
  later save goes back into it; a function, a script and a name a class cannot have are refused
  with nothing left; `]]>` in the code survives.

## Not measured

- **MATLAB's App Designer window opening a spliced model.** Its full load is measured headless
  (`getAppData` with the component tree built); the window needs a display and a person, as ADR
  0205 left it.
- **A version 7.3 model in any shape but R2025b's** (a version 2 superblock, another base address,
  a new-style root group): `CanSplice` refuses it and the save says the copy was left.
- **Any release but R2025b**, reading or writing.

## Consequences

- A spliced model grows on every save that changes the copy, by the changed lines, the rewritten
  names and reference datasets, the group and a 4,096-byte heap; the old objects stay in the file
  unreachable. App Designer's own save writes the file afresh and compacts it.
- `JgsMlappModel` is three files: the model and the rules, the level-5 codec, the version 7.3
  codec. `MatV73Writer.Builder` and its `Message` are internal to the assembly now, for the splice.
- `Hdf5Object` knows its address and an old-style group's symbol table; `Hdf5File` finds an
  entry's place in the bytes.
- The fixture app `U7bApp73.mlapp` is a binary made by MATLAB. `oi57_build` rebuilds it; the
  rebuilt file differs in its timestamps and identifier, and is copied into `fixtures/helpers` by
  hand.

## Divergences

- **An `.mlapp` a version 7.3 model is spliced into differs from the one App Designer would
  write:** the new `code` sits past the old objects, its cells' elements are not named in
  `#refs#`, and its datasets carry no `H5PATH` attribute. R2025b loads and reads it alike
  (`oi57_verify`); App Designer's next save writes its own form over it.
- **A class saved as an `.mlapp` here is a file App Designer refuses** (`MATLAB:appdesigner:appdesigner:LoadFailed`,
  "Error loading '<name>.mlapp'."): MATLAB offers no such save, runs the file, and reads its code
  through `matlab.internal.getCode`; this editor opens it as the class it holds and says what it
  holds when it writes it.

## Still open

- The visual designer (`docs/plans/app-designer-plan.md`), whose DS6 now knows that a package
  without a model is refused by App Designer's readers; the visible half of DS0 still wants the
  user's leave.
