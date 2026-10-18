# ADR 0244 — App Designer's saved component tree, written by the designer

## Status

Accepted, 2026-10-18. Stage DS1b of the visual app designer plan (`docs/plans/app-designer-plan.md`,
gitignored), after DS1 (ADR 0243). The recordings are the probes in
`tools/matlab-checklist/ui-probes/ds1b/`, all headless, 2026-10-18. R2025b's own saved trees were
read with `ds1b_mcos.py`, which rewrites them byte for byte from its parsed model, and changed and
loaded back to learn what `load` does with a tree. R2025b's sources for the serialization hooks
(`modifyIncomingSerializationContent` in the colour, font and tick mixins), App Designer's
`ImageLoader`, `DesignTimeProperties`, `DesignTimeUIAxesController`, `UIAxesAdapter` and
`getColormapString` were read.

## Context

DD1 (2026-10-18) requires an app to stay editable in both JGraph's designer and MATLAB's App
Designer. DS0 found that App Designer builds its canvas only from `appModel.mat`'s `components`, a
tree of MCOS objects that saves each class's private storage and helper objects, and that a stale
tree shows stale. So a JGraph-made or JGraph-edited app reaches App Designer's canvas only if
JGraph writes that tree. DS1 gave the designer its model of the components (`AppDesign`) and the
code App Designer generates from it.

## Decision

### The format (`MatMcosWriter`)

`MatMcosWriter.Write` writes a level-5 MAT-file whose one variable is a struct holding a MATLAB
object, with the object and everything it refers to in the file's subsystem. An object
(`McosObject`) is a class name, its stored names in order and its dynamic properties. A name
holds one of four things:

- a word from the file's name table, which MATLAB stores for an enumerated value such as `'manual'`;
- a literal number;
- a stored value: a number array of any class, a char array, a cell or a struct (`MatData`);
- references to other objects.

The subsystem's layout is the one `MatMcos` reads, measured further against R2025b's files:

- the byte table: names, classes, objects, property lists, dynamic-property lists, a list per
  object of names its serialization hook renamed, and a last segment of `1` and its own offset
  plus eight;
- the stored values;
- three cells with one entry per class, the last of them the class's defaults at save time.

The writer leaves out two things, and R2025b loads the result the same:

- **Every class's saved-time defaults.** The three per-class cells are written empty, so the
  enumeration objects those defaults refer to are left out too.
- **Every object the root does not reach.**

Strings are written as char, because MATLAB saves a string through `saveobj`.

### What `load` does with a tree (probes `ds1b_reduced`, `ds1b_rules`)

- **It accepts a tree that stores only some of each object's names.** A name left out keeps the
  class's own default. That default is the storage's default, not what the constructor sets: a
  button with nothing stored loads with empty text and a 20-by-20 size, and a figure with nothing
  stored loads as a classic figure with menus.
- **It assigns the stored names in the order stored.** It goes through the class's set methods and
  serialization hooks, so a public name works where MATLAB stores a private one: `Text` reaches
  `PrivateText`, and `MinorTicks` sets its mode as code would.
- **Order matters.** `Value` before `Items` is refused, as it would be in code.
- **A name stored twice is assigned once.**
- **Without saved-time defaults, a colour's mode follows its setter.** A colour stored on its own
  turns its mode to `'manual'`; a mode stored after it wins. The colour and font mixins'
  `modifyIncomingSerializationContent` hooks rename `BackgroundColor` to `BackgroundColor_I`. When
  no mode is stored, the hook picks `'auto'` or `'manual'` from the value, and it warns when the
  colour is missing altogether.

### What JGraph writes (`DesignTreeWriter`)

`DesignTreeWriter.Write` writes `components`, a struct whose field `UIFigure` is the figure.

**The base.** Each component starts as its class's base: what a fresh component of the class,
made by App Designer's creation call, stores in R2025b. The probe `ds1b_fresh` saved one of each
of the 45 classes, and `ds1b_base.py` recorded them into `ds1b_base.json` (72 KB), which the build
embeds. The recording leaves out:

- what every component stores that is not its state (`Internal`, the dirty-property strategy,
  `Copyable` and the like);
- the component's children, its `Layout` and a tab group's selected tab, which the writer stores;
- references to any object but a position store (`UnitPos`, `UnitPosReadOnly`) and the figure's
  dynamic properties. An axes' rulers, camera and data space and a tree's strategies are left out,
  and a component loaded without them makes them again.

The recording adds:

- **A colour's mode.** For each colour the fresh component stores without its mode, the mode is
  stored as `'auto'`.
- **The figure's default map.** A panel, button group, tab group, tab and table get what the
  figure's default map gives them as they are made in code (their border type, font units and
  font size), stored in the `_I` form so their modes stay `'auto'`. A component loaded from a tree
  takes the map only when the figure is its parent.
- **An axes' position modes.** These are stored as `'auto'`.

The recording also changes or drops three things:

- **A radio or toggle button is stored unselected**, because the generated code selects one.
- **An axes' camera and plot box are dropped.** They are worked out from the axes' default size,
  and the axes works them out again.
- **An axes' parts.** The title, the subtitle and the three rulers with their label text are
  recorded whole from an axes given all five, less their tick values and cached extents.

**The design on top.** On the base go the properties the generator writes for the component
(`DesignCodeWriter.WrittenProperties`, DS1), under their public names and in the generator's
order, so that `load` sets them as `createComponents` would:

- A property with a `Mode` sibling also stores its mode as `'manual'`.
- A `Text` held as a one-element cell is stored as its char, as the code line sets it.
- A string and an on/off value are stored as char.
- A callback is stored as its method's name, as App Designer holds it at design time.
- A date is a `datetime` object storing `data`, its milliseconds since 1970 (NaN for `NaT`).
- A grid child's place is its `Layout`: a `GridLayoutOptions` object with `Row` and `Column`.
- Another component (a context menu, a tree's checked nodes) is a reference to its object.
- An axes' title, subtitle and labels are its recorded parts with the design's text in `String_I`.
- A hidden axes toolbar is a custom `AxesToolbar` with `Visible` off and one button. That is the
  form App Designer saves for one (the default toolbar is not saved). The one button lets App
  Designer see the toolbar as a custom one and remove it when the toolbar is shown again.

**The tree around them:**

- **Children.** A container's children are its `SerializableChildren`, in MATLAB's `Children`
  order (the last made first).
- **Properties that name the children.** A tree's checked nodes come after the children, as the
  code assigns them after making the nodes.
- **A tab group's selection.** A tab group stores its first tab as `SelectedTab` unless the design
  selects one.
- **A tab's size.** It is its tab group's size when the tab is made, which is how R2025b sizes one
  made in code (probe `ds1b_tabs`).
- **An axes the design positions** keeps no position from its base. An axes' position stores are
  applied after the stored names, so the base's 400-by-300 would undo the design's `Position`.

Every component carries the dynamic property `DesignTimeProperties` App Designer gives it, a
`meta.DynamicProperty` with App Designer's attributes. Its fields are:

- `CodeName`;
- an empty `GroupId`;
- `ComponentCode`: the component's lines as App Designer stores them
  (`DesignCodeWriter.StoredComponentCode`). These are the generator's lines before the code model
  finishes them: `app.ad_CODENAME_ad`, `app.ad_PARENTCODENAME_ad`, a callback's name in quotes, and
  an image by its full path under the app's folder;
- `ImageRelativePath`, for the classes that can show an image. App Designer's `ImageLoader` sets
  `Icon` or `ImageSource` from it when it opens the app. Where the writer knows the app's folder, it
  also stores the image's full path as the property, as App Designer's own tree holds it.

What the tree cannot hold is listed in `DesignTreeResult.Problems`, and the rest is still written:

- a component class the catalog does not know (Simulink's);
- an axes' colormap other than parula. App Designer names a colormap by comparing its 256 rows with
  each map it offers (`getColormapString`), so the rows must be MATLAB's exactly. Open item 146,
  with item 145.

## Measured

- **R2025b's own trees** (five, from `ds0_tree`) are rewritten byte for byte from `ds1b_mcos.py`'s
  parsed model, table included.
- **A fresh component loaded with nothing stored** (`ds1b_blanked`) lacks only what its
  constructor sets: the text, size, alignment, `Value` and `ValueIndex`, tick labels, a tree's
  editability, and the figure's uifigure settings. Three classes (Hyperlink, Image, Label) warn
  that their hook finds no colour.
- **Trees transcribed from scratch** (`ds1b_scratch`) load the same as the MATLAB-saved trees they
  came from. These keep only the components, their plain values, children, Layout options,
  position stores and dynamic properties, with no defaults. That holds for 44 of the 45 fresh
  components, the U7b app and the shipped DataExport app with its `DesignTimeProperties`. The
  UIAxes' two position modes come back `'manual'`, which is why the base states them `'auto'`.
- **Against the code** (`ds1b_verify`): each tree the designer writes is loaded and compared,
  component by component, with the tree that its `createComponents` (as the designer writes it)
  makes in R2025b, given the `DesignTimeProperties` App Designer would give it. The comparison
  covers every property public to `get`, hidden ones included, the axes' label texts and toolbar,
  and each container's children in order.
  - 40 of the 44 apps (the fixture and 39 shipped, 937 components) load the same.
  - In two Simulink apps, the Simulink components are missing (not written), and so are their
    places in their grids.
  - In two more apps (three axes), an axes' `PlotBoxAspectRatio` differs in the fourth digit,
    0.6411 against 0.64094. The written tree's is its final inner size's (382.27/596.27); the
    code's tree keeps one worked out before the axes reached its final size.
- **App Designer's full load** (`ds1b_appdesigner`): each tree is saved as an `.mlapp` by App
  Designer's own serializer and opened by App Designer's own reader. The same round trip is run on
  the tree the code makes, as a control. Each app is run from its own folder, where a file it names
  without a path is found.
  - All 44 apps open with every component's code name: 1127 components and 372 callbacks bound.
  - On every component, R2025b's generator gives back the stored `ComponentCode`.
  - App Designer reports no warnings, and its load changes nothing that it does not also change in
    the code's tree.
  - The 34 findings both trees share are MATLAB's own:
    - an axes' scalar `Alphamap` is lost when the serializer saves it (R2025b warns while
      converting the axes);
    - a panel's or button group's `InnerPosition` comes back as a fresh one's;
    - in two apps, callbacks the class names but no component in `createComponents` uses are bound
      to nothing;
    - the fixture's image, whose full path changes when the app is saved elsewhere.
  - This probe checks each tree against itself before the round trip, so the two Simulink apps
    open with no findings. Their missing Simulink components show only against the code's tree
    (`ds1b_verify`, above, and the window check below).
- **App Designer's window** (`ds1b_viewbuild`, `ds1b_view`, `ds1b_viewdiff.py`; window session
  with the user's leave, 2026-10-18). Each app was saved twice by App Designer's serializer with
  its class text: once from JGraph's tree and once, as the control, from its code's tree. Both
  were opened in R2025b's App Designer window through its document API, and the window captured
  and compared pixel for pixel, title bar aside.
  - All 88 open, in 4.6 to 16.4 s.
  - App Designer marks every one modified on opening, control included.
  - 40 of the 44 apps match their control. Of the other four:
    - `MultiPaneSimApp` and `aurora` lack the Simulink run controls, variable tuner and progress
      bar that the writer leaves out;
    - `ASAP2CDFGen` and `mlexpdlg`, the first app opened in each of the two runs, differ by a
      1-pixel shift of the app inspector panel; their canvases match.
  - The serializer re-saves the objects JGraph's file loads as, so the component bytes in these
    `.mlapp` files are R2025b's. JGraph writing the whole `appModel.mat` itself is DS6.
- **Load artifacts.** A tree loaded from any saved file has `ParentMode` `'auto'` and
  `HasAppBuildingDefaults` `'off'`, MATLAB's own included, since neither is stored. The
  comparisons skip them.
- **Unit tests** (`AppDesignerTreeWriterTests`, 7):
  - an object graph read back through `MatMcos`;
  - a design made in code, as objects;
  - the stored component code;
  - dates;
  - the problems list;
  - the fixture read back;
  - the local test that leaves the trees for the probes.

## Not measured

- **The rest of `appModel.mat`.** App Designer's file also holds `code` (the code view's copy, ADR
  0239), `appData` (an `AppData` object with three empty properties and
  `MinimumSupportedVersion`), and `components.Groups`, its component groups. Writing these with
  the tree is DS6's save.
- **A dark-themed figure**, and any release but R2025b.

## Consequences

- A JGraph-made app's components can be saved as App Designer's tree, which App Designer opens with
  its code names, callbacks bound and images set, and whose stored code is R2025b's generator's.
- The tree comes from the code, as DS1 reads it. So an app made in MATLAB and saved by JGraph loses
  what only its old tree said:
  - its component groups;
  - values its tree kept that its code never set (ADR 0243's 28 components).

  DS6 decides whether a save keeps an untouched component's old objects.
- No script behaves differently.

## Divergences

- **A hidden axes toolbar is a one-button custom toolbar.** App Designer saves a copy of the full
  default toolbar. Nothing of either shows while the toolbar is hidden.
- **A one-element `Text` cell is stored as its char.** App Designer's design-time component keeps
  the cell. The code sets the char either way.
- **Component groups are not kept.** `GroupId` is empty and `components` has no `Groups`, because
  the code does not hold them.
- **An axes' colormap other than parula is not written** (open item 146).
