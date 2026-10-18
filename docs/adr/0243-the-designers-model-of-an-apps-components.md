# ADR 0243 — The designer's model of an app's components: `createComponents` read and written as R2025b writes it

## Status

Accepted, 2026-10-18. Stage DS1 of the visual app designer plan (`docs/plans/app-designer-plan.md`,
gitignored), after DS0 (probes only, no ADR). The recordings are the probes in
`tools/matlab-checklist/ui-probes/ds1/`, all headless, 2026-10-18: `ds1_catalog` (what App
Designer's generator knows about each component class), `ds1_oracle` (R2025b's generator over every
component of the 44 apps it ships, into the ignored `rt/`), `ds1_generate` (the generator over this
project's fixture app) and `ds1_quirks` (two generator branches that write code which does not read
back). R2025b's `ComponentCodeGenerator`, `VisualComponentAdapter`, the 45 per-class adapters under
`toolbox/matlab/uicomponents/plugin/appdesigner`, `DesignTimeController` and the code engine in App
Designer's JavaScript bundle (`appdesigner/codegen/…`: `MatlabComponentInitBehavior`,
`ComponentPropertiesBlockBehavior`, `CreateComponentsBehavior`, the context-menu behaviours,
`CodeFormatter`, `PropertyBuilder`) were read.

## Context

The designer plan's DD1 (decided 2026-10-18) requires an app to stay editable in both JGraph's
designer and MATLAB's App Designer. DS0 found that App Designer builds its canvas from the component
tree in `appModel.mat` alone (that tree is DS1b's to write) and that its component code generator is
readable MATLAB. DS1 is the designer's model of an app's components: read from the generated code
every App Designer app carries, and written back as App Designer writes it, so that the code a
JGraph-designed app holds is the code App Designer would generate from the same components. This
code is also what App Designer stores in each component's `DesignTimeProperties.ComponentCode`,
which DS1b will write. The code engine reuses that stored code when the release matches, and
regenerates and marks the app dirty when it differs.

## Decision

### The model

`JGraph.Scripting.Designer` (WPF-free) holds the design:

- `AppDesign` is the tree `createComponents` builds, plus whether the app is responsive.
- `DesignComponent` is one component: its code name, its class, its creation call, its parent and
  children, and the properties set on it in the order they were set.
- `DesignValue` is a property's value:
  - a double or logical matrix, a char row, a string, a cell array, an on/off value, or a date array;
  - a callback bound to an app method, other components by name, or an image beside the app;
  - for anything else, the expression's text, kept whole.

A property is held under the name App Designer's generator uses:

- the property itself, or the generator's alias: `TitleString` for `title(app.UIAxes, …)`,
  `ToolbarVisible`, `AD_ColormapString`, and `AD_AliasedThemeChangedFcn`;
- a grid child's place as `Layout.Row` and `Layout.Column`;
- a context menu assigned to a component as its `ContextMenu`.

Only what is set is held. Whether a set property is written is decided as App Designer's generator
decides it.

### The catalog

`DesignCatalog.R2025b` holds what the generator knows about each of the 45 uifigure component
classes App Designer offers in R2025b, the figure included:

- the creation call;
- the property order (`getCodeGenPropertyNames` on a design-time component);
- which properties have a `Mode` sibling;
- each property's run-time default under the unthemed, light and dark themes.

The probe `ds1_catalog` records these, and the build embeds its JSON from the probe's folder
(115 KB). The `uiscope` class from a toolbox is left out.

### Reading

`DesignCodeReader.Read` takes the class text. It reads:

- the component properties block, for each component's declared class;
- `createComponents`, one statement per line: a creation call, a property set, a grid child's
  `Layout.Row`/`Column`, an axes' `title`, `xlabel`, `ylabel`, `zlabel` and `subtitle` calls,
  `colormap`, `Toolbar.Visible`, and a context menu's assignment.

The writer makes the rest again, so the reader passes over:

- comments and blank lines;
- the `pathToMLAPP` line;
- the figure's closing `Visible = 'on'`.

Any other line is a problem the result lists, with its line number. A class the catalog does not know
(a Simulink component) is kept as written, its properties in the order read.

`DesignValueText` reads every form the generator writes:

- `mat2str` output, a char with `''` doubled, and `['a' newline 'b']`;
- a cell vector with `,` or `;`, numbers in it by `%g`;
- `datetime([y m d])` and `NaT`;
- `createCallbackFcn(app, @M, true)`, `[app.A, app.B]`, `app.X`, and `fullfile(pathToMLAPP, …)`.

It also reads a matrix written by hand with spaces after its separators. Anything else is kept as
text and written back unchanged.

### Writing

`DesignCodeWriter.ComponentCode` transcribes the generator: the creation call, then each catalog
property in order that is set and not at its default under the app's theme. App Designer passes the
generator the figure's theme, and all 44 shipped apps read `light`, so `light` is the default style.

- A grid child's `Position` is never written; its `Layout.Row` and `Layout.Column` always are.
- A property with a `Mode` sibling is written only once set. Setting it turns the mode to manual,
  and a value equal to the default is still dropped.
- The adapters' `isDefault` overrides are kept:
  - a `Text` held as a one-element cell is written as its char, and equals the default the same way;
  - the figure's `CloseRequestFcn` is at its default when empty or `closereq`, and its `Theme` is
    written whenever set;
  - an axes' labels are at their default when empty and its toolbar when visible, and its
    `InnerPosition` is written whenever set;
  - a slider's `Step` is written whenever set;
  - a date picker's `Value` compares `NaT` equal;
  - a table's `ColumnEditable` and `ColumnSortable` are at their default when false.

Values are written as `propertyValueToString` writes them (`DesignValueText.Format`).

`DesignCodeWriter.Method` transcribes the code engine around the components:

- the method comment and signature;
- the image folder's two lines first, when an image beside the app is used;
- each component, before its children, under a blank line and `% Create Name` (the figure's says
  "and hide until all components are created");
- a check box tree's checked nodes after its nodes, under `% Assign Checked Nodes`;
- a context menu's assignments after its items, under an indented blank line and
  `% Assign app.Name`;
- the figure shown last.

`DesignCodeWriter.PropertiesBlock` writes the block the way the component browser lists the
components (measured over the 44 shipped apps):

- each component comes before its children, and within a container the most recently made first, as
  MATLAB's `Children` are;
- menus, a context menu's items, tabs, tree nodes and toolbar tools keep the order they were made;
- a figure's menus and toolbars come ahead of its other components, and its context menus after them;
- a responsive app's own panels keep the order they were made;
- each class is padded to two spaces past the longest name of 30 characters or fewer.

## Measured

- **`ds1_catalog`:** 45 classes.
  - Their property lists run from 6 entries (Toolbar) to 103 (UIAxes).
  - Many colour and tag properties have hidden `Mode` siblings.
  - Light defaults equal unthemed ones for every property. The probe records one, a date picker's
    `Value`, only because it is `NaT`, which `isequal` calls unequal to itself.
  - The dark theme changes 68 defaults: colours, an axes' colour order and grid alphas, the
    figure's `Theme`, and the date picker's `NaT` again.
- **The 44 shipped apps**, read in place (`EveryShippedAppsComponentCodeIsWrittenBackAsItWas`):
  - 1108 components are read with no problem lines.
  - Each app's properties block and `createComponents` are written back line for line, except in
    two Simulink templates.
  - Those two order one axes' properties as an older release did; R2025b's generator orders them as
    the designer does.
- **`ds1_oracle`** (`EveryShippedComponentIsWrittenAsR2025bsGeneratorWritesIt`): R2025b's own
  generator over the same components, under each figure's theme (all `light`).
  - All 1080 components whose saved tree and code agree on what is set are written as the generator
    writes them.
  - In the other 28, the tree holds values the code never sets: an older release's unthemed defaults
    kept in the tree, such as `FontColor = [0 0 0]`, which the light theme no longer takes for a
    default.
  - The code is what runs, and the designer reads the code.
- **`ds1_generate`:** the fixture `DesignerSpecApp.m`, written here, covers all 45 classes in 55
  components. Their properties are set out of order, some to their defaults, through every value
  form. Its lines run in R2025b and the generator's 224 lines are recorded beside it. The designer
  writes every component's lines exactly (`TheFixtureAppsComponentsAreWrittenAsR2025bsGeneratorWritesThem`).
- **`ds1_quirks`:**
  - R2025b's generator writes a string with a line break as `"a" + "b"`, losing the break, and leaves
    a property's line empty or drops it for a cell it cannot write.
  - No stock component reaches either. Every text property tried keeps a char, cells of strings
    become cells of char, and the cells that could not be written are refused or never generated
    (15 cases).
  - So App Designer never writes broken code this way, and no MATLAB-wrong row is recorded. The
    designer writes the forms that read back: `"a" + newline + "b"`, and a cell literal of its items.
- **Unit tests** (`AppDesignerDesignModelTests`, 53):
  - 33 value forms read and written unchanged, and four kept as text;
  - matrices written by hand, and `isequal` semantics;
  - the reader's problem lines;
  - a design made in code written in full;
  - an edited design (renamed, unset, moved) written with its edits;
  - theme defaults;
  - the catalog.

## Found with it (for later stages)

- **A component's coercions on set are not the writer's.** The fixture had to say what each
  component keeps:
  - `RoundFractionalValues` rounds 3.5 to 4;
  - `DisabledDates` is kept as a column;
  - a button group always has one selection, so a lone toggle button is `Value = true`;
  - checking a node's only child checks the node.

  The inspector (DS3) must apply them, through the runtime's own components.
- **The theme comes from the figure.** App Designer passes the generator the figure's theme. The
  writer takes it from its style (light), and a design whose figure sets `Theme` needs that theme's
  defaults (DS3 or DS5).
- **The rest of the generated code** depends on the callbacks, the startup function, input
  parameters and the single-instance setting rather than on the components: the callbacks' comments
  and signatures, the constructor, `delete`, and a responsive app's second properties block and
  layout function. It is DS5's.
- **A saved tree can say more than its code** (the 28 above). When DS1b writes the tree for an app
  made in MATLAB, the tree it writes from the code drops those values. The values were never in
  effect when the app ran.

## Not measured

- **Any release but R2025b.** The catalog and both generators are R2025b's.
- **App Designer's window** opening an app the designer wrote. That waits for DS1b's tree.
- **Custom components** (`UserComponentAdapter`, DS7) and Simulink components past keeping them as
  read.
- **A `ThemeChangedFcn` on the figure:** read and written through its alias, but in no shipped app
  and not in the fixture.

## Consequences

- Every App Designer app opens in the designer's model, and its generated component code is written
  back as R2025b writes it. A JGraph-made app's generated code is what App Designer would generate
  from the same components.
- `DesignCodeWriter.ComponentCode` is the component code DS1b stores in each component's
  `DesignTimeProperties`.
- No script behaves differently. The designer has no window yet (DS2).

## Divergences

- **A string written by hand into `createComponents` stays a string here.** App Designer loads it
  into a component that keeps a char, and writes the char back. The designer reads the code as
  written and has no component to coerce through until DS3.
- **A string with a line break and a cell R2025b's generator cannot write are written so that they
  read back** (`"a" + newline + "b"`, a cell literal of the items). R2025b writes `"a" + "b"` or an
  empty line, but no stock component holds such a value (`ds1_quirks`).
