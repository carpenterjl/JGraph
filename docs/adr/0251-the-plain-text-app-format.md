# ADR 0251 — The plain-text app format

## Status

Accepted, 2026-10-19. Stage DS7a of the visual app designer plan (`docs/plans/app-designer-plan.md`,
gitignored; DS7 was split into DS7a, this format, and DS7b, custom components, in revision 12).
App Designer's plain-text file writer was read from R2025b's canvas bundle
(`appdesigner/codegen/behaviors/groups/PlainTextFileContentGroupBehavior`, `app/PlainTextClassBehavior`,
`appendix/*`, the `XML*Builder`s, `model/fileformat/TextFileFormat`), its loader and runtime from
its MATLAB side (`appdesigner.internal.artifactgenerator.*`, `PlainTextDeserializer`,
`PlainTextCodeParser`, `AppAppendixParser`, `matlab.apps.App`, `AppManagementService.initializeMApp`,
`VisualComponentAdapter.getComponentDirtyProps`). Nothing of it is kept. The writer's text was run
under node in App Designer's own modules, unmodified (`tools/matlab-checklist/ui-probes/ds7/oracle`),
and R2025b read and ran the files written here (`ds7_plain`, headless).

## Context

An App Designer app has always been an `.mlapp`: a package holding the class and, beside it,
App Designer's own model of the app - the component tree as MCOS objects, a copy of the code - which
DS1b and DS6 taught this build to write. MathWorks announced a plain-text format for R2026b, and
R2025b already carries it behind the feature `AppDesignerPlainTextFileFormat` (DS0): an ordinary
`.m` class file under `matlab.apps.App`, holding the user's code alone, and after the class an
`%[appendix]` of XML sections - the components with their set properties, the run configuration,
the app details, the internal data, a thumbnail - from which MATLAB generates the component code
when the app runs or opens. It is the format a source-control tool can read and merge, and the
long-term road to an app both designers edit. The plan took it as a second output for the designer:
written and read here, checked against R2025b's loader now, opened by App Designer's window only when
its feature is on.

## Decision

### The file is App Designer's writer transcribed

- `DesignPlainText.Render` writes the whole file: the class as `DesignClassWriter` writes it for
  this format (`classdef Name < matlab.apps.App`; the component properties block, the editable
  section and the callbacks block as for an `.mlapp`; the callbacks block a bare `methods`, since the
  format's access control is none; no `createComponents`, constructor or `delete`), one empty line,
  `%[appendix]`, and the sections `AppendixLines` writes in the order `XMLAppendixBehavior` adds
  them: the layout section (no empty line before its `%---`), then, each after an empty line and
  only when it has content, the run configuration, the app details and the internal data; then,
  after one more empty line, the thumbnail section App Designer's save appends (its comment line
  before its own declaration, `autoCapture` and the data URI kept as read). LF, no final line
  ending, as `writePlainTextAppFile` writes; a file read keeps its own.
- A component is an element named by its type's last segment (`UIFigure` for the figure) with a
  `name` attribute, its set properties as child elements sorted by name in ordinal order, and a
  `Children` element for what it holds, omitted when empty; the indentation is the class's own unit
  per depth (two levels per nesting). The properties are the generator's (`DesignCodeWriter.WrittenProperties`,
  DS1: a grid child's `Layout.Row` and `Layout.Column`, no `Position`) under App Designer's
  user-facing names (`Title.String`, `XLabel.String`, `Toolbar.Visible`, `Colormap`, `ThemeChangedFcn`)
  with the context menu appended, as `getComponentDirtyProps` lists them. A value is written as
  `getPropertyValueString` writes it: a callback by its function's bare name, a context menu by its
  code name, the figure's colormap by its name, a char in single quotes with its inner quotes left
  single (the loader doubles them), an on/off state as its char, anything else as the generated line's
  right-hand side; then escaped as `XMLElementBuilder` escapes: `&`, `<`, `>` always, and inside a
  value that starts and ends with a quote the inner quotes as `&apos;` and line breaks as `\n`.
- The run configuration holds `SingleRunningInstance` (`true`) and `StartupFcn` (the function's name
  alone; its parameters are in the class). The app details hold `Author`, `Description`, `Name`,
  `Summary` and `Version`, those set. The internal data holds `AppId`, `AppType`, `MATLABRelease`,
  `MinimumSupportedMATLABRelease` (R2025a for a new plain-text app, R2019a for a responsive one, as
  App Designer's factories set them) and the lists `ImageRelativePaths`, `RequiredProducts` and
  `UserComponents` as `Product` children; the images kept beside the app are listed from the design.
- `DesignPlainTextDetails` holds what the appendix carries besides the components, read from a file
  and written back as it was, or made for an app first saved as plain text (its class name, version
  1.0, a fresh id).

### The reader does what MATLAB's loader does

- `DesignPlainText.Read` cuts the sections as `getAppendixByGrammarName` cuts them, parses the XML,
  and reads the layout into an `AppDesign`: each element's class by its short name through the
  catalog, the declared class of the properties block winning, an element the catalog does not know
  kept as a custom component of its declared class, made by a call of that name; each property by
  the loader's cases - the text sanitized as `sanitizeComponentPropertyValue` does, a callback's name
  bare, in quotes or in the generated `createCallbackFcn` form, a context menu's code name, a tree's
  checked or selected nodes as bare names (the generator's placeholder and `app.` forms taken by their
  last part), an image's name as an image beside the app when the internal data lists it and as the
  file's name otherwise, the figure's colormap by name (another component's as the design's
  `colormap(app.X, map)` line), the dotted axes names and `Toolbar.Visible` under their design
  aliases, anything else as the design value its text makes; an empty callback element leaves the
  property unset. The figure's own `Visible` is the writer's to decide and is passed over, as is a
  labelled component's `label` attribute (the designer has no groups, open item 150). A figure
  element with no name is refused, as R2025b refuses any element without one; the last `Children`
  element of a component is the one read, as R2025b's XPath takes it. A responsive app is one
  whose class says so (DS4b).
- `JgsMlappLayout` reads a plain-text class (`IsPlainText`), told by its class line naming
  `matlab.apps.App` (a `% Component initialization` line in the user's code does not cut it as an
  `.mlapp`): the class's own `end` before the appendix is the anchor the editable section and the
  callbacks block are cut against, and the run configuration says which function is the startup one
  and whether the app runs as a single instance. Its word outranks the comment over a function, as
  the app runs the function it names: the commented one is then a callback like any other, and a
  startup function it names outside the callbacks block keeps its name through a save
  (`DesignCode.UnplacedStartup`). Everything from the last callback's `end` on is generated, the
  appendix with it, which is what the code view shades. `DesignCode.Read` needs nothing more.

### The app runs here through `matlab.apps.App`

- `matlab.apps.App` is a class this build supplies, as `AppBase` and `ComponentContainer` are
  (`JgsBuiltinClasses.App.cs`). Its constructor does what `initializeMApp` does: reads the class's
  file, reads the appendix into the design, and, for a single-instance app, answers the running
  instance with its figure made current when there is one (`JgsClass.RunConstructor` now takes a
  native constructor's answer when it is another object of the class); else it makes the components
  by the same generated lines an `.mlapp`'s `createComponents` holds (`DesignCodeWriter.Method`, the
  app's folder in place of `pathToMLAPP`), registers the app with its figure and runs the startup
  function with every argument the constructor was given - all in a function closed over a scope that
  carries `AppBase`'s natives (`createCallbackFcn`, `registerApp`, `runStartupFcn`) and the class's
  access, so a callback's `@Name` and the startup call reach the class's methods whatever their
  access. `delete(app)` deletes the app's figure, which deletes the app (`unregisterMApp`);
  `saveobj` and `loadobj` refuse as `AppBase`'s do.
- A malformed appendix is refused as R2025b refuses it (its spec's table of refusals): a file with
  no layout section or no figure, `MATLAB:appdesigner:appdesigner:AppAppendixException` with its
  words (`Unable to process app layout due to incorrect XML.`); layout XML that is not well-formed,
  `MATLABApp:appendixError` with the parser's message; an element whose name is neither a class of
  the catalog nor a class the interpreter can find, `...AppAppendixUnknownComponent`
  (`Unrecognized component: 'X'.`); the generated lines failing to parse,
  `...AppAppendixComponentException`. An appendix the reader could not take whole - an element
  with no name, a name used twice, a value it does not read - is refused with
  `AppAppendixException` and the problems, rather than run with parts missing (a divergence, below).
- The generated function is parsed under a file of its own beside the app, `ad_<Class>.m`, as
  R2025b runs it from `ad_<sha1>.m` in its cache: its line numbers are not the class file's, so a
  breakpoint or an error location in the app file never lands on a generated line. An image named
  in quotes that the internal data does not list is found beside the app when it is there, as the
  generator's `fullfile` fallback finds it; a preset icon or a file on the path stays the name. A
  class that is the file being run (`jgraph -batch App.m`) finds its file through the batch path.

### The designer opens, saves and converts

- `DesignDocument` knows its `Format` by the file's extension: `Open` of a `.m` reads it as a
  plain-text app (`FromPlainText`), `CurrentText` renders the whole file, `Save` writes it whole
  (a copy moved onto the file once written), and `SaveAs` writes the app under another name in the
  format the new name says - as plain text with the class renamed and the App Details name following
  it when it was the class's (`PlainTextSerializationModel.save`'s rule), as an `.mlapp` written
  whole with App Designer's model when the app was not one (DS6) or copied when it was (App
  Designer's Save As). The App Details go with the app across the formats: into the plain-text
  file from the package's metadata (`JgsMlapp.ReadMetadata`: `dc:title`, `dc:creator`,
  `dc:description` as the summary, `cp:version`, the app's `uuid` and `description`), with R2025a as
  the oldest release for a converted app, as `AppLoader` converts one; into the package from the
  plain-text details (`JgsMlapp.WriteApp` takes them). An image kept beside the app is copied beside
  the new file when that is another folder. Line endings follow the format written (a plain-text
  file ends as App Designer's does, after `%}`). The write comes first and the document becomes the
  new file's only once it is written: a write that fails puts everything back (format, class name,
  details) and leaves no `.saving` file, so the next Save goes to the file the app came from, in its
  format. A refusal names what it is about (the appendix's entries, not `createComponents`). The
  window's Open takes both formats and gains Save As…, which refuses a target open in another tab;
  the editor knows a plain-text app by its appendix (`IsPlainTextApp`) for Open in App Designer, the
  shading of generated lines and its own Save As, which saves such an app as an app (the class
  renamed for a `.m`, the package written whole for an `.mlapp`); `appdesigner('x.m')` opens one,
  and `x.M` is refused for its case as `x.MLAPP` is.

## Consequences

- A new app is still an `.mlapp`: the plain-text file is a second output, chosen with Save As…, and
  its road into App Designer's window is R2025b's feature or R2026b.
- Every app written here as plain text runs here and in R2025b with its feature on, and opens in
  App Designer's reader without it; the 42 shipped apps whose components the catalog knows come back
  from the format with the same `createComponents`.
- The two Simulink apps' `uisimprogress`, `uisimvartuner` and `uisimcontrols` components, which the
  catalog does not know, come back as calls of their class with their properties by name: the format
  holds no maker's name and no property order for a class outside the catalog.
- `DesignClassWriter.Lines` takes a format; `JgsMlappLayout` gains `IsPlainText`; `DesignCatalog`
  gains `ByShortName`; `JgsClass.RunConstructor` takes a native constructor's answer.

## Measured

- **App Designer's own writer under node** (`ds7/oracle/appendix.js`, its output `appendix.json`):
  34 cases over the unmodified `PlainTextFileContentGroupBehavior`, class and appendix behaviors and
  XML builders - a figure alone, unsorted properties, grid children, a labelled pair, escaping, a
  context menu, four nested containers, a tree, the run configuration in seven arrangements, the app
  details in five, the internal data in six, canvas groups, whole files at indent 4, indent 2 and
  tabs, a responsive shape, empty and one-element values. The tests
  (`AppDesignerPlainTextTests`, 30) hold the writer to those texts for the figure, the sorted
  properties, the grid, escaping, the context menu, the nesting, the whole file at both indents, the
  details, the internal data and the thumbnail, and to the review's findings below (a failed Save
  As, the details across formats, the reader's refusals and fallbacks, the run configuration's
  word, CRLF, a responsive app).
- **Round trips**: the designer's fixture of 45 classes and 42 of the 44 shipped apps (read in place)
  written as plain text, read back and written again: the same `createComponents`, the same callbacks,
  bodies, startup function, parameters, single instance and editable section, the same file.
- **The runtime** (`AppDesignerPlainTextRunTests`, 4): `DsPlain(7)` builds its grid, field and button
  from the appendix, runs `startupFcn(app, amp)` with 7, answers its button with
  `ButtonPushedData`, keeps one running instance, is deleted with its figure; an app with no run
  configuration runs twice over and finds an unlisted image beside it from another folder; a
  nameless figure, malformed XML, an unknown element and a name used twice are refused with
  R2025b's identifiers and words.
- **R2025b, headless** (`ds7_plain` over the three apps `TheProbesAppsAreWrittenWhenAsked` leaves
  in `ds7/rt/plain`, each as the `.m` written here and as the `.mlapp` of the same design; the run
  half copies the `.m` to a folder of its own first, since beside its `.mlapp` MATLAB resolves the
  name to the package, and checks `which`): with the feature off, App Designer's reader loads
  `DsPlain.m` the same as its `.mlapp` (4 components, the callback, the startup function, the single
  instance); with the feature on, `DsPlain(7)`, `CallbacksApp(2, 3)` and `DesignerSpecApp` run
  through `matlab.apps.App` - every component by its code name (4, 4 and 55), every callback the
  code names bound as a function handle and run (1, 3 and 8, the fixture's failing in its own
  undefined functions), a second construction of a single-instance app the running one,
  `delete(app)` taking the figure.
- **Review before the commit** (a workflow of five Opus reviewers - format, runtime, integration,
  robustness, tests - every finding sent to two Opus skeptics in turn; 45 confirmed, 9 refuted):
  a Save As that failed to write left the document switched to the new format at the old path, so
  the next Save wrote plain text over the `.mlapp`; Save As between the formats threw the App
  Details and the app's id away; `ImageRelativePaths` only ever grew; a figure element with no name
  threw out of the reader and ended the session from File > Open; a second `Children` element was
  dropped where R2025b reads the last; an empty callback element made the app unsaveable; the
  startup function was taken from the comment ahead of the run configuration, and one named
  outside the callbacks block lost its `StartupFcn` on the next save; a `% Component
  initialization` line in a plain-text app's user code cut the class as an `.mlapp`; the runtime
  ran an app whose appendix the reader had not taken whole, with parts missing, and its refusals
  had the wrong identifiers and words; the generated function was parsed under the class file's
  own source id, so breakpoints and error lines collided; a batch-run class could not find its
  file; an unlisted relative image failed from another folder where R2025b's `fullfile` fallback
  finds it; a CRLF file got mixed endings in a multi-line detail; the editor's Save As treated a
  plain-text app as plain MATLAB (the class not renamed, a code-only package that cannot run);
  the designer's Save As onto a file open in another tab left two tabs on one file; `.saving` was
  left behind by a failed write; `appdesigner('x.M')` was refused for its extension rather than
  its case; the refusal and status wording blamed `createComponents`; the probe's run half had
  been running the `.mlapp` control, not the `.m`; and the stale `appdesigner('x.m')` test row.
  All fixed under tests; the rest of the confirmed findings are recorded below as divergences or
  as open items.
  Found on the way, R2025b's own: its parser drops the editable section's first line
  (`findEditableSectionCode` cuts from the properties block's `end` and removes three lines); it
  cannot load a file with a component bound to two callbacks (`buildCallbackData` hands `strcmp` a
  list, `MATLAB:maxrhs`); its loader passes an axes' `Subtitle.String` and `Toolbar.Visible`, which
  its own writer writes, to the constructor as dotted names MATLAB cannot parse; it cannot parse a
  quoted `...Callback` name its writer writes; its code parser fails on a callback whose body is
  empty, which is how App Designer's own template starts one (`getCallbackCode` takes the body's last
  statement; the fixture's `ButtonPushed` is such a one, so its load stops after the 55 components
  are made); its exception wrapper masks a load error's message with the file path as a format
  string.
- **In a window** (`scratch/ds7w/ds7w.ps1`; the Release app opened a copy of `DsPlain.m` by
  `appdesigner(...)`; the designer driven by UI Automation patterns, the Win32 Save dialog by
  messages to its own window handles - its lower pane is not in the automation tree the app
  process exposes, and it takes a name only as typed into its visible name box while it is the
  active window, so the dialog is activated from the script's process with its input thread
  attached, the name sent as character messages to that box's handle, and Save clicked by
  `BM_CLICK` on its handle; nothing goes to whatever window is in front): the designer shows the
  app in a tab with its grid, field and button on the canvas and in the component browser; the
  Code View lists the startup function and the callback; Save writes the file again unchanged;
  Save As… to `DsPlainCopy.mlapp` writes App Designer's package (the class renamed under
  `AppBase` with `createComponents` and the singleton constructor, `appModel.mat`, the metadata
  parts) and the tab follows the new file; `workspace.json` unchanged.
- The full suite passes (11931); the seven verify scripts pass; the harvest adds 18 rows for this ADR.

## Not measured

- App Designer's window on a file written here, which needs R2025b's feature on in a window
  session (open item 160).
- Canvas groups (`app:componentGroups`), which the designer does not have (open item 150); a
  thumbnail captured here (the section is carried as read, empty for a new app).
- The bytes R2025b writes for non-ASCII text in a plain-text file (its `fwrite` with `uint8`): a
  file is read and written as UTF-8 here (open item 159).
- A `%[appendix]` line inside the user's code: the first such line, wherever it stands, begins the
  appendix here, so the app opens view-only, and the writer passes a grammar line typed into a
  callback through to a file R2025b cannot cut (open item 161).
- Any release but R2025b; R2026b's own form of the format.

## Divergences

- **A plain-text app runs and opens here without R2025b's feature.** R2025b refuses to run one
  (`MATLABApp:unknownFileType`, `Unknown FileType`) and `appdesigner('x.m')` refuses the file
  (`...InvalidFileExtension`) unless `AppDesignerPlainTextFileFormat` is on.
- **The run configuration is written once.** App Designer writes it twice when an app opens with
  both a startup function and a single running instance, and its own reader then fails on the file.
- **A `...Callback` callback's name is written bare.** App Designer's char rule quotes it, and its
  loader cannot parse the line; its run strips the quotes.
- **A char with a line break is written `['a' newline 'b']`.** App Designer writes the two
  characters `\n`, which its loader never turns back.
- **A cell of one char stays a cell.** App Designer writes the char alone.
- **A tree's selected nodes are written as bare code names.** App Designer writes the generator's
  placeholder form (`[ad_OBJECTNAME_ad.N1, ...]`), which its loader cannot run; checked nodes are
  written bare by both.
- **An image is written as its path relative to the app and listed in `ImageRelativePaths`.** App
  Designer writes the design-time value (a full path, or a bare name found on the path) and lists
  no image for a plain-text app; the loader takes the relative path through `fullfile` with the app's
  folder.
- **A labelled component's `label` attribute is neither written nor kept.** The designer has no
  component groups; App Designer's loader reads `labelName`, which its writer never writes.
- **The figure is made hidden and shown once every component is made.** R2025b's run creates it
  visible and adds the components to it.
- **A single-instance app is one whose `SingleRunningInstance` reads `true`**, at run time as in
  the designer. R2025b's run takes any such element, whatever its text (`false`, empty), while its
  loader reads the text: the loader's rule is kept for both.
- **A component gets no `AD_CodeName` property.** R2025b's generated function gives every component
  a transient dynamic property holding its code name; `isprop(app.X, 'AD_CodeName')` is false here.
- **An appendix the reader cannot take whole is refused** (`AppAppendixException` with the
  problems: a name used twice, a value the reader does not read). R2025b's generator makes what it
  can and fails, or not, in the generated code.
- **The generated function is `ad_<Class>.m` beside the app**, the file an error in a component's
  line names; R2025b's is `ad_<sha1>.m` in its cache, and its `AppAppendixComponentException`
  names the element's line in the app file.
- **A component's colormap other than the figure's is written as `<Colormap>`**, the design's
  `colormap(app.X, map)` line, and read back as it. App Designer writes no colormap for a
  component other than the figure, so such a line is lost in its file.
- **A tree's `ClickedFcn` and `DoubleClickedFcn` are written bare and bound here.** App Designer
  writes them the same way, and its run then fails on them: its component map lacks the two names,
  so the generator evaluates `ClickedFcn=TreeClicked` as a call.
- **The code view shows the whole appendix**, shaded and read-only. App Designer's editor hides it.
- **The App Details name of a converted app.** An `.mlapp` whose `dc:title` is its class name gets
  the new file's name as its App Details name, as a plain-text Save As renames it; a title of its
  own is kept.
- **The two Simulink apps' components come back as calls of their class** with their properties by
  name, where the `.mlapp` holds `uisimprogress(...)` and the generator's order.
