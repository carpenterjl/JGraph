# ADR 0247 — The designer's library, inspector and names

## Status

Accepted, 2026-10-18. Stage DS3 of the visual app designer plan (`docs/plans/app-designer-plan.md`,
gitignored), after DS2 (ADR 0246). App Designer's library, naming and click-and-draw were read
from R2025b's own code (`toolbox/matlab/appdesigner/web/release/bundle.mwBundle.appdesigner.js`:
`uicomponents_appdesigner_plugin/init/ComponentLibraryEntries` and the per-class entry
definitions, `LabeledComponentEntryDefinition` and `LabeledComponentPositionCalculator`,
`CompoundComponentEntryDefinition`, the per-class models' `CodeNameGeneration`,
`appdesigner/model/codenamegenerator/*`, `rendererseditors/utils/UniqueNameGenerator`,
`CodeEditUtil.isAppAttributeNameValid`, `ComponentLibraryView`'s sorting, `componentdraw/*`, the
component defaults in `ComponentDefaultsProduction`); its inspector from R2025b's MATLAB code
(`uicomponents/plugin/appdesigner/+inspector/+internal/*PropertyView.m`, `CommonPropertyView`);
the theme from `DesignTimeController.getCurrentTheme`. Nothing of it is kept. The recordings are
the probes `tools/matlab-checklist/ui-probes/ds3/ds3_inspector` and `ds3_palette`, headless,
2026-10-18.

## Context

DS2 drew an app and moved its components. DS3 is the rest of what App Designer's design view does
to components: its component library, to add them; its inspector and component browser, to set
their properties and rename them; and deleting them. DS1 found that a component keeps what it is
given its own way (a string becomes a char, a drop-down's value follows its items, a button group
keeps one selection), so the plan asks that every edit go through the runtime's component and be
read back, and that a value equal to its default be unset. The plan also leaves the figure's own
theme to this stage.

## Decision

### The library (`DesignPalette`)

App Designer's four sections - Common, Containers, Figure Tools, Instrumentation - with its 39
entries, each section sorted by name as its view sorts them (JavaScript's code-unit order: HTML
before Hyperlink). An entry makes:

- **its class's design-time component**: what the class's adapter gives one at design time
  (`createDesignTimeComponentWithCleanup`), recorded by running R2025b's generator over it
  (`ds3_palette`) and read back with the designer's own reader - a panel's 260-by-221 size and
  title, a table's four columns and no row names, an axes' labels; a radio or toggle button's
  `Value` set back to false, as the adapter's design-time defaults do;
- **named** as App Designer names it (below), its text in the class's text property;
- **with its label** for the 16 labelled entries: a label sized to its text, right-aligned beside the
  component 15 pixels away - centred on it, or level with its top less 2 pixels (list box, text
  area) - or centred under it 15 pixels below its ticks (gauges, knobs, switches). The outer boxes
  of the components whose ticks or captions stand outside their position are App Designer's
  defaults;
- **with what it holds** for the compound entries: a radio or toggle button group's three buttons
  where App Designer's definition puts them, the first selected; a tab group's two tabs; a tree's
  node holding three more; a menu bar's two menus first among the figure's children; a context
  menu's two items; a toolbar's push tool. The figure tools go in the figure whatever they are
  dropped on.

A label's width is its text's width in its font (12-pixel Helvetica) taken up to a whole pixel, at
least 20, and the larger of 5 and 3% of it more; 22 high (`updateLabelSize`). App Designer measures
in its browser; the shared text measure (`UiFit.TextWidth`) taken up to a whole pixel gives App
Designer's width for 73 of the 128 one-line default-font labels in the apps R2025b ships, the rest
being wider, as labels resized by hand are (half a pixel added gives 71, nothing added 40).

The Grid Layout entry is shown and cannot be chosen: a grid lays out by rows and columns, DS4's.

### Adding and deleting (`DesignDocument`, `DesignGestures`)

- **Click and draw**, App Designer's: a click on an entry chooses it, a second lets it go; the next
  press on the canvas places it with its box's top-left corner there at its own size, or a drag
  draws the component's size; either ends the mode, as does Escape.
- **Drag from the library**: the entry is added where it is dropped.
- Either way it goes into the container under the pointer, or the nearest around it that can hold
  it (the figure, a panel, a tab, a button group; a radio or toggle button only in a button group),
  in whole pixels, snapped to a shown grid, and kept inside a container that is not the figure.
  What is added is selected; it is one edit.
- **Delete** (the key, Backspace, or the toolbar) deletes the selection and what it holds as one
  edit; the figure never. A deleted context menu comes off the components it was assigned to; a
  deleted tree node leaves its tree's checked nodes. Callbacks stay in the code, as App Designer's
  delete leaves them.
- A new component is made in the runtime by its generated lines, as `createComponents` would; a
  deleted one is deleted there; undo makes it again.

### Names (`DesignNames`)

Transcribed from App Designer, quirks and all:

- **A new component**: its text is the class's (`Button`, `Edit Field`) counted against every text
  the app shows (`UniqueNameGenerator`: the next number above any name in use holding it, so
  `Button Group2` makes a new button `Button3`); its code name is the class's with the text's
  trailing digits, and an `_N` when taken (`Menu_2`). A labelled component's label is named after
  it (`EditField2Label`, text `Edit Field2`); a class with no text is counted on code names alone
  (`EditField3`). A compound entry's children are counted among themselves, from App Designer's
  default texts (`Radio Button`, `Tree Node`; tabs emptied first), so a radio group's buttons are
  `Button`, `Button2`, `Button3`.
- **An edited text** renames a component named from its text (`ModifyCodeNameGenerator`): `Go`
  makes `GoButton`, unless the name was given by hand; a labelled component's label's text names
  the component and the label after it (`SpeedEditField`, `SpeedEditFieldLabel`), in the same edit.
- **A name typed** in the inspector must be a letter followed by letters, digits and underscores,
  not a keyword, at most `namelengthmax` (63), and not the name of anything in the app - its
  components, its callbacks, and the functions, properties and class its code declares. The design's
  references to it and every `app.Name` in the code follow.

### Properties (`DesignDocument.SetProperty`, `DesignInspector`)

A value goes through the component's own set and the design keeps what the component kept, read
back from it; a value equal to the property's default under the app's theme is unset. What follows
from it is kept in the same edit: the component's `Value` where its items, limits or rounding
moved it, and a button group's other buttons' selection. A refused value is the runtime's message,
and nothing changes. A null context menu takes the component's off.

The inspector is App Designer's: for each class, the groups R2025b's property view for it shows,
in order, titled and opened as there, with their rows (`ds3_inspector`). A property the generator
writes is edited - on/off and logical as a check box, a choice of the view's members or enumerated
values, a colour as `[r g b]` with a swatch, text and file paths as text, a context menu from the
app's, anything else as MATLAB writes it - and the rest (modes, read-only properties, objects) is
shown and not edited, since a save could not keep it. The figure's theme is a choice of light and
dark. Above the inspector, App Designer's component browser lists the components in its order;
selecting one there selects it, which is the way to the components the canvas does not draw.

### The theme

The code is written under the figure's `Theme` when set, else light, as App Designer's design-time
controller takes the figure's theme for its generator: the reader sets it, the save writes under
it, and the runtime compares defaults under it; an edit of the figure's theme changes it.

### The window

`AppDesignerWindow` has the library on the left and the browser over the inspector on the right;
the toolbar gained Delete, and its commands go to the window, not to a focused text box's own
undo.

## Measured

- `AppDesignerPaletteTests` (21 tests, 24 cases): the library's order and the design-time components; label widths
  against four labels of a shipped app; naming of new, labelled, compound and renamed components,
  and typed names; a button, an edit field, a list box and a knob added where App Designer puts them,
  line for line; compound entries; the figure tools; a drop into a panel; click and draw, a drop and
  the Delete key; deleting with a context menu assigned, and undo; a text that renames, a label that
  renames its field; read-back (a string kept as a char, a default unset, a drop-down's value
  following its items, a radio group's selection), a refusal; a rename and the code following it;
  the theme; the inspector's groups; a save and reopen of an app built, set, renamed and deleted in;
  an app built from every entry, saved and opened whole.
- The 44 shipped apps' one-line default-font labels: 73 of 128 the width App Designer gave them.
- **The window** (the Release app started with `-r "appdesigner(...)"` on a copy of a shipped app,
  UI Automation patterns only, captured with PrintWindow, ended by Stop-Process; the app's file and
  `workspace.json` unchanged): the library shown with Grid Layout off; a button selected in the
  browser showing App Designer's seven groups; its text set in the inspector renamed it
  `BorrowNowButton` on the canvas and in the browser; the toolbar's Delete took it out and two
  undos brought back `BorrowButton`; an entry chosen and let go.
- The full suite: 11,512 passed; the designer's 197 again after the window check's fixes. The seven
  verify scripts pass.
- **R2025b's App Designer reading an app built here** (`ds6_verify`, `DS6_PART=new`, headless): the
  app built from every library entry and saved by the designer (79 components) opens through App
  Designer's own reader the same as the tree its code makes, every component's stored code R2025b's
  generator's for the component as loaded, no warning: `new PaletteApp: same`. The probe now takes
  `PaletteApp` among JGraph's own apps.

## Not measured

- The mouse in the real window: click and draw, a drag from the library and the Delete key are
  tested through `DesignGestures`, which the canvas only feeds.
- R2025b's App Designer adding the same components side by side.

## Consequences

- `JGraph.Scripting.Designer` has `DesignPalette`, `DesignNames` and `DesignInspector`;
  `DesignDocument` adds, deletes, sets, renames and keeps the theme; `DesignRuntime` makes and
  deletes components and reads values back; `DesignCodeStyle.ThemeOf`; `DesignCodeWriter.BrowserOrder`
  is public. `JGraph.Controls.Designer` has `DesignPaletteView`, `DesignBrowserView` and
  `DesignInspectorView`.
- The probes' recordings are embedded (`designer-inspector.json`, `designer-palette.json`).
- No running script behaves differently.

## Divergences

- **A component added is placed by its box's top-left corner** at the pointer, for a click and a
  drop alike; App Designer places a drop by its avatar.
- **Drawing a component shows no alignment guides**, and a drawn labelled entry's size is its
  component's.
- **A component dropped where it does not fit is moved inside its container**, not narrowed.
- **A labelled component is its label's by their names** (`XLabel` beside `X`), where App Designer
  keeps the two as a group (open item 150): a text edit renames the pair only while the names pair
  them.
- **The inspector shows one component at a time**, and edits values as text where App Designer has
  editors of its own (items, ticks, dates, a colour picker).
- **A rename changes every `app.Name` in the class's text**, comments and strings included.

## Still open

- The inspector's own editors and several components at once (open item 152).
- Component groups, and a labelled component moving as one (open item 150); adding a tab, a node or
  a menu item to what is there, copy and paste (open item 151).
- Grids (DS4); the code view and callbacks (DS5).
