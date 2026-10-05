# ADR 0205 — Editing an App Designer file's code and saving it back into the `.mlapp`

## Status

Accepted, 2026-10-05. Stage U7b of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored; decision Q6). It stands on ADR 0204 (U7:
the `.mlapp` reader, and an editor that opened an app's code and would not save it).

## Context

An `.mlapp` holds its code twice. `matlab/document.xml` holds the class file MATLAB runs.
`appdesigner/appModel.mat` holds what MATLAB's App Designer works from: the component tree, a
legacy object, and a variable `code` that is the user's part of the class cut into pieces - the
editable section, each callback's body, the startup function's body and its parameters. App
Designer does not read the class file when it opens an app. It rebuilds its code view from `code`
and regenerates everything else from the component tree, and its next save writes that over the
document. So an edit written to the document alone runs, here and in MATLAB, and is silently
thrown away the first time the app is saved from App Designer.

The user decided (Q6) that an `.mlapp`'s code is editable in JGraph's editor all the same, and U0
proved the way on one shipped app: write the document, and rewrite `code` inside the MAT-file
around the elements this build cannot decode.

What R2025b stores was measured before any code was written (probe `u7b_shape`, all 44 `.mlapp`
files R2025b ships, read in place), and a sample this project can keep was made: `U7bApp.mlapp`, an
app of our own written to disk by R2025b's serializer (`u7b_build`), so it holds a real component
tree, the real metadata parts and App Designer's copy of the code.

## Decision

### Whose lines are whose (`JgsMlappLayout`)

App Designer writes a class in a fixed order and marks each part with a comment. The layout is
read from those comments and from the block structure of the code between them:

- the component properties block, under `% Properties that correspond to app components`, and any
  further generated properties block after it (a responsive app's, a Simulink app's), each under
  a comment of the same wording;
- **the editable section**: every line from there to `% Callbacks that handle component events`,
  or to `% Component initialization` in an app with no callbacks. App Designer records it without
  the one empty line it leaves on each side;
- **each function of the callbacks block**: its body is the lines between its signature and its
  own `end`. The one under `% Code that executes after component creation` is the startup
  function, and what follows `app` in its signature the app's input parameters. The one under
  `% Changes arrangement of the app based on UIFigure width` is a callback App Designer writes
  itself.

A function's `end` is found by counting blocks over the lexer's tokens, not by indentation: an
`end` inside an index or after a dot is not a block's, `parfor`, `spmd` and an `arguments` block
open one, and a function nested in a callback is part of its body. A function left open takes
the methods block's `end` for its own; the block is then never closed, and the layout says the
callbacks cannot be told (`IsComplete` false) instead of guessing. Text with none of the comments
is not an app's, and has no regions.

Everything outside the editable section and the callbacks' bodies is generated. This class is the
reusable piece the plan asks for: the editor shades by it, the save derives by it, and the visual
designer (`app-designer-plan.md`) regenerates around it.

### App Designer's copy, kept in step (`JgsMlappModel`)

The save re-derives `code` from the text and rewrites that one variable.

- **Nothing else in the MAT-file is decoded.** The file is a run of elements, compressed in every
  shipped app. Each element but `code` is copied as the bytes it is, compressed or not. `code`
  is written uncompressed, which MATLAB reads unconditionally. The header's subsystem offset
  (bytes 116-123) is moved to where the MCOS element now starts; left stale, App Designer cannot
  load the file (measured in U0).
- **Inside `code`, a field that is not rewritten is its own bytes too.** The struct is taken apart
  into its fields' elements and put back together, so `AppTypeData`, `SingletonMode`, `Bindings`
  and anything a later release adds pass through untouched.
- **A field is rewritten only when the old text accounts for it.** What the layout cuts from the
  text the file was saved with must be what the field holds; then the field becomes what the
  layout cuts from the new text. A field the text never matched is left as App Designer wrote
  it. Over the 44 shipped apps exactly one kind of value fails the test: a responsive app's
  `updateAppLayout`, which App Designer records in another form. One Simulink app's editable
  section is recorded with the generated Simulink block in it; the record is held against both
  cuts and keeps the one it was made with.
- **A field is present exactly when it holds something**, which is `MLAPPSerializer`'s own rule:
  an app whose last callback was deleted loses `Callbacks`, and gets it back when one is written.
  A callback body with no lines is a 0-by-0 char, as the one such body among the shipped apps is.
- **Text that cannot be read leaves the copy as it was**, and the save says so. The next save of
  text that can be read has no old text to hold the copy against, and brings every derived field
  to the new text outright, keeping only a generated callback's record.
- **A version 7.3 model is not rewritten.** One shipped app keeps `appModel.mat` as HDF5. Its
  code is saved, its copy is left, and the save says so.

### The package, rewritten whole and swapped in (`JgsMlapp.WriteCode`)

The document part keeps everything around its text - the declaration, the paragraph style - and
gets the new text in one CDATA run, in the line endings the document had (line feeds, in every
shipped app). A `]]>` in the code is cut across two CDATA sections. Every other part is copied as
its bytes, in its order, with its timestamp and its stored-or-deflated choice. The new package is
written beside the old one and moved onto it once complete, so a save that fails at any point
leaves the app as it was and nothing beside it.

Saving the text the file already holds changes no part: the document comes out the same bytes and
the model is not touched.

### The editor

- **Save** on an `.mlapp`'s tab saves back into the package. The read-only prompt applies as for
  any file.
- **Generated lines are shaded**, as App Designer's code view shades them, by a background
  renderer that reads the layout when the text has changed. They stay editable; the shading says
  whose a line is.
- **The first save that changes generated code says what that means, once**: the change runs,
  and App Designer writes its own version over it when it next saves the app. The fact that it
  was said is kept with the workspace state. Every such save, and every save that left the copy
  behind, says so on the status line.
- **Save As** offers the `.mlapp` type for an app: the package is copied and the class renamed
  for the new file, in the class line and the constructor, as App Designer's Save As does it.
  Picking `.m` saves the class under that file's name. No other document can be saved as an
  `.mlapp`: there is no package to put its text in.
- **File > Export to .m File** writes the class as `<name>_exported.m` or whatever name is
  given, the class named for the file, and opens it; the `.mlapp` and its tab are left alone.
  This is MATLAB's export.

## Found on the way

- **Text past ASCII in a MAT-file was written in a form MATLAB misreads.** `save` wrote char
  data as miUINT16. R2025b reads miUINT16 units in the machine's code page: `'中'` saved by JGraph
  loaded in MATLAB as `'-'`, its low byte. R2025b's own default save writes such text as miUTF16
  (type 17). The writer now does, for any char with a unit above 127, and plain text is written
  as it was.
- **The MAT reader refused miUTF16**, so a MAT-file MATLAB saved with any non-ASCII text in it
  did not load ("unsupported encoding (17)"). It reads types 17 and 18 now.
- **The struct writer's field-name-length tag gave its size as the slot length (32), not 4.**
  MATLAB read the files regardless. It is the format's now.
- **ADR 0204's "the editor opening an `.mlapp` was not checked in a window"** is lifted: the
  file dialog's name box and default button are addressed directly, and the open is measured
  below.

## Consequences

- An `.mlapp` saved here differs from the one App Designer wrote in two ways that MATLAB does
  not see: `code` is uncompressed, and the zip's deflate streams are this runtime's.
- The layout is a reading of App Designer's comments. An app whose marker comments were edited
  away is saved as text and its copy is left; the status line says so.
- `ScriptWorkspaceStateDto` has one more field (`GeneratedCodeNoticeShown`); a state file
  without it reads as "not yet said".
- The fixture app is a binary made by MATLAB. `u7b_build` rebuilds it; the rebuilt file differs
  in its timestamps and identifier, and is copied into `fixtures/helpers` by hand.

## Measured

- **Every shipped app** (44, read in place where R2025b is installed; test
  `EveryShippedAppsCopyIsWhatItsTextGives_AndSavingItsOwnTextChangesNothing`): the layout is read
  from each; for the 43 with a level-5 model the copy is what the layout cuts from the text, but
  for `updateAppLayout`; saving an app's own text leaves every part byte for byte.
- **R2025b reading what was saved** (probe `u7b_verify`): 43 of 43 apps with an edit in a
  callback, the startup function and the editable section are read by `readAppCodeData` with each
  edit in place, by `matlab.internal.getCode`, by `readAppDesignerData`, and by App Designer's own
  full load. The fixture app with a new property, an edited startup function and an edited
  callback is read the same way and runs in R2025b `-batch` with all three acting.
- **Fixture `u7b_real`** (25 lines): the app as R2025b's serializer wrote it is found, typed and
  run alike in both, in both representations.
- **An injected failure** between writing the new package and swapping it in leaves the file
  byte for byte and nothing beside it.
- **In a window** (UI Automation patterns, and a window message to the file dialog's own two
  controls; nothing typed, no mouse): File > Open File opened the `.mlapp` in a tab with its
  generated lines shaded and its editable section and callback bodies not; an edit in a callback
  was saved into the package with no dialog; an edit to `createComponents` was saved and the
  notice shown, and a second such save showed none; Export wrote `U7bApp_exported.m` with the
  class renamed, opened it in a tab and left the `.mlapp` unchanged; Export was disabled with no
  app open.

## Not measured

- **MATLAB's App Designer window opening a saved file.** Its full load is measured headless; the
  designer needs a display and a person.
- **Save As to a new `.mlapp` in a window.** The copy-and-rename is unit-tested; the dialog's
  type list was not driven.
- **Any release but R2025b**, reading or writing.

## Divergences

- **An `.mlapp`'s code is edited as text, every line of it.** MATLAB opens an `.mlapp` only in
  App Designer, whose code view locks the generated lines. Here they are shaded and editable,
  and a save that changes one says once that App Designer will overwrite it.
- **A callback's record follows the text, not the component tree.** A callback renamed or added
  in the text is renamed or added in App Designer's list of callbacks, while the component in the
  tree still names what it named; App Designer keeps the two together.
- **An app whose model is a version 7.3 MAT-file is saved without its copy.** The code runs as
  saved and App Designer shows the code it had (open item 57).

## Still open

- Open item 57: rewriting `code` in a version 7.3 (HDF5) model.
- Open item 58: a new `.mlapp` from a class that never was one, which needs a component tree and
  is the visual designer's to make.
- The visual designer has its own plan (`docs/plans/app-designer-plan.md`), which starts here.
