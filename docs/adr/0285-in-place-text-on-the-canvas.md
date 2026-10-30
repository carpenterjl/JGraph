# ADR 0285 — In-place text on the canvas: F2, a typed character and a double click

## Status

Accepted, 2026-10-29. Stage W5b of the App Designer window plan: the second part of W5 (clipboard and
structure), after the clipboard (W5a, ADR 0284). W5c (structure), W5d (menus, toolbars and context
menus on the canvas) and W5e (Replace With) follow.

## Context

After W5a a component's text, title, items or limits could be changed only in the inspector. R2025b's
App Designer edits them on the canvas: a box laid over the component, opened by F2, by typing, or by
a double click, saved by Enter or by clicking away, closed unsaved by Escape.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`):
`appdesigner/view/designarea/plugins/general/InplaceEditPlugin` (the double click),
`appdesigner/view/designarea/DesignAreaKeyboardController` (F2, `INPLACE_EDIT_KEY`, and a typed
character's `SeedValue`), `appdesigner/view/canvas/TabStripMouseController` (a tab's heading),
`appdesigner/view/inplace/InPlaceEditorManager`, `InPlaceEditorManifest`, `AbstractInPlaceEditor`,
`SingleLineInPlaceEditor`, `MultiLineInPlaceEditor`, `TextAreaInPlaceEditor`,
`SelectionMultiLineInPlaceEditor`, `HyperlinkInPlaceEditor` and `FileBrowserInPlaceEditor`,
`appdesigner/model/AppViewState` (`canInPlaceEdit`), `appdesigner/util/ImageUtil`
(`showImageFilePicker`), each component model's `InPlaceEditing` (`isInPlaceEditable`,
`isAutoEditable`, `isMultiHotSpots`, `isUsingComponentAsEditor`), and each controller's
`getEditorOptions`, `getEditableHotspotPositions` and `_getEditableHotspotIndex`.

## Decision

- **What is edited on the canvas** (`isInPlaceEditable`): a label - not one whose `Interpreter` is
  LaTeX or HTML - a button, a state and a toggle button, a check box and a radio button (their
  `Text`); a hyperlink (its `Text` and `URL`); a text and a numeric field, a spinner and a text area
  (their `Value`); a drop-down's and a list's `Items`; an image's `ImageSource`; a slider's, a knob's
  and the four gauges' limits; a discrete knob's and the three switches' items; a table's
  `ColumnName`; a panel's, a button group's and a tab's `Title`. Not a responsive app's region.
- **F2 and a typed character** (`DesignAreaKeyboardController`): with one component selected - a
  labelled component's label when the pair is - whose editor a key opens (`isAutoEditable`: any but a
  drop-down's and a list's), F2 opens it on its text, all selected; a character past a space opens it
  on that character, the caret after it.
- **A double click** with no keys held (`InplaceEditPlugin`, `TabStripMouseController`): on a tab's
  heading its title; else on one of a component's places to edit drawn outside it (a scale's end
  labels, a switch's items), that one; else the front component under it, or the nearest container
  round it, that the canvas edits - inside a panel, its title. It is selected first. A double click on
  a component with several places that hits none of them opens nothing.
- **The editors** (`InPlaceEditorManifest`): a single line (a title, a field's value, a limit, an
  item, a column name); lines (a label's and a button's text); a text area; a list's items, a line
  each; a hyperlink's text with its URL box ("Enter URL") and Visit link under it; an image's file,
  chosen in "Open Image File" with R2025b's file types, kept beside the app when it is in the app's
  folder. The box lies over the place, outlined 2 pixels, in the component's font and alignment,
  growing with the text; lines wrap where the component wraps them.
- **The keys** (each editor's handlers): a single line saves on Enter with any key held; lines save on
  Enter alone, Shift+Enter breaks the line, Enter with Ctrl or Alt does nothing; a text area breaks
  the line on Enter; a list's items save on Shift+Enter or Alt+Enter; Escape closes unsaved; Ctrl+Z
  puts the text back (a list's items undo as a text box does); Tab saves and opens the next place of a component with several (Shift+Tab the
  previous, round from the last to the first, a table's round the columns it shows), and leaves a
  text area or a list; in a hyperlink's editor Tab goes from the text to the URL and to Visit,
  Shift+Tab back, Enter in the URL saves both and Enter on Visit opens the URL. Clicking away saves.
  The wheel and the canvas resizing close the editor unsaved. Nothing typed in the editor reaches the
  canvas's or the window's keys.
- **Which place** of a component with several (`_getEditableHotspotIndex`, `getEditorOptions`): a key
  opens a horizontal slider's and linear gauge's minimum and a vertical one's maximum, a clockwise
  circular gauge's minimum, a ninety-degree gauge's by its orientation and direction, a semicircular
  gauge's by its orientation flipped when it runs anticlockwise, a horizontal switch's first item and a
  vertical one's second, a knob's, a discrete knob's and a table's first; the text aligned away from
  the scale or switch as R2025b aligns it. Where the places are is where the canvas drew them: the
  component layer now keeps the boxes of a slider's, knob's and gauge's tick labels, a discrete
  knob's and a switch's items, and gives a table's column headings and a tab's heading.
- **The save** (`_saveWidgetTextChanges`, the property set): nothing when the text is as it opened,
  or a single line fails its pattern (R2025b's patterns kept, their unescaped dots included); a
  labelled component's label emptied is deleted; else one property edit through the component's own
  set - one line as characters and more as a column, a number for a numeric field, a spinner and a
  limit (`Inf` too; a limit keeping the other, what is no number changing nothing), the item or
  column name among the others, a list's lines as its items, a text area's lines as its value - and
  a hyperlink's URL, when it changed and is one it takes (`_validateURL`: not `matlab:` or
  `javascript:`, a dot inside it), as an edit after the text. A component named from its text is
  renamed as the inspector renames it. What the component refuses is said as the inspector's
  refusals are; the image's in App Designer's alert.

## Consequences

- W5c's tree nodes, W5d's menus and toolbar tools, and W7's component browser rename open through
  the same editors (the menus' Up and Down that leave the editor, `ArrowKeyExit`, come with W5d).

## Measured

- **R2025b's own controllers**: `ui-probes/w5/oracle/inplace.js` cuts `_getEditableHotspotIndex` and
  `getEditorOptions` out of the slider's, knobs', gauges', switch's and table's controllers as they
  are and runs them over every orientation and scale direction, a key, Tab and Shift+Tab from each
  place: 176 scenes (DesignInPlaceOracle.json). `DesignInPlaceOracleTests` replays them: every place
  opened, property edited and alignment is R2025b's.
- **Tests**: `AppDesignerInPlaceTests` (13): F2 on a label and its save as one edit, lines as a
  column, undone; typing opening on what was typed, not for a space or two selected; a drop-down's
  items by a double click only, not with Ctrl; a labelled field's label emptied deleted, its field's
  value a single line; a numeric field's number, `-Inf`, and text that is none; a slider's limits one
  at a time, Tab to the other, a pattern-passing non-number and a refused maximum, a double click on a
  drawn label and beside it; a switch's item among the others; a panel's title from a double click
  inside it and its button's text from one on it; a hyperlink's URL taken or not, two edits; a text
  area's lines; a LaTeX label and the figure not edited; each editor's keys; the numbers as the box
  shows them and the URLs a hyperlink takes. With the designer, App Designer, toolstrip and settings
  suites: 814 pass.
- **The window check** (`run-w5bdesigner.ps1`, JGraph.Application in Release over `w4/W4App.m` with a
  slider and a switch added): components selected in the component browser and the canvas given the
  keyboard through UI Automation patterns, the editor read and set through its ValuePattern, and F2,
  a typed Z, Tab, Enter and Escape posted as window messages to the designer's own window once it is
  checked to be in front. F2 on L opening on `L` and Enter saving `Hello`; Escape on A saving nothing,
  which one Undo shows; Z typed on A opening on `Z` and saving `Zed`; F2 on the slider opening `0` on
  its left end label and Tab `100` at its right end, the limits saved `[10 100]`; F2 on the switch
  opening `Off`, saved `{'Stop', 'On'}`; each read back from the file. All 54 checks pass;
  `run-w2designer.ps1` (39), `run-w5designer.ps1` (34) and `run-w6designer.ps1` (34) still pass.

## Not measured

- **A double click, a hyperlink's URL box and the image's file dialog in the window**: a double click
  takes a mouse, and the dialog is a window of the system's; the tests drive the same gestures.
- **R2025b's editors in a window**: their placing, growing and scrolling were read, not run.

## Divergences

- **The editor covers the component's place, not only its text**: a label's, a button's, a
  hyperlink's and a field's box - R2025b's lies over the text's own span. It grows right and down,
  where R2025b's keeps centred or right-aligned text where it was and scrolls the canvas to keep the
  editor in view.
- **A UIAxes' title and axis labels are not edited on the canvas**: JGraph's renderer does not say
  where it drew them; the inspector edits them.
- **A colour picker's and a date picker's value are not picked on the canvas** (R2025b opens the
  component's own picker, `isUsingComponentAsEditor`); the inspector edits them.
- **A list's items editor does not drag whole lines**: R2025b's moves a dragged selection by lines;
  JGraph's drags text as a text box does.
- **Every key that closes an editor gives the canvas the keyboard**; R2025b's list editor leaves it
  where it was after Shift+Enter and Escape.
- **The hyperlink's Visit is enabled for a valid URL as the editor opens**: R2025b's disables it for
  one until a key is let go in the URL box (its test at creation is inverted) - a MATLAB bug not kept.
