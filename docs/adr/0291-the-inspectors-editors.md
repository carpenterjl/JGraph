# ADR 0291 — The inspector's editors

## Status

Accepted, 2026-10-30. Stage W7c of the App Designer window plan: the editors of each row of the inspector and the rich editors
behind their kebabs. W7b has the inspector over a selection, W7d callbacks for several components.

## Context

JGraph's inspector edited a row as a check box, a choice, or the value as MATLAB writes it. R2025b's has about thirty editors. A
label does not keep its size either when its text is edited there, and a property's mode was not said beside it (open item 232).

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`): `rendererseditors/editors`' `ItemsEditor` and
`ItemsModel`, `TicksDialogEditor`, `TickDialogTableModel` and `HeaderMenuEditor`, `ScaleColorsEditor` and `ScaleColorsModel`,
`DisplayFormatEditor` and `DisplayFormatMode`, `UITableColumnEditor`, its model and table model and `UITableColumnTextBoxEditor`,
`DateTableEditor` and `DateModel`, `CheckboxListEditor` and `DateCheckboxListEditor`, `RGBColorEditor` and `ColorTextBoxEditor`,
`TextAreaEditor`, `FilePickerEditor`, `ToggleButtonGroupEditor`, `BinaryToggleButtonEditor`, `SpinnerEditor`, `FontNameEditor`, the
`_BasePSTable` table and controller and `utils/UniqueNameGenerator`; `inspector_client/editors`' `PropertyFieldsEditor`,
`ResetLimitsEditor` and `DatePickerRichEditor`, and `inspector_client/widget/InspectorPropertyEditorGroup`; `mw-colorpicker`'s
`ColorPicker` and `MColor`; `appdesigner_interface/command/PropertyCommandFactory` and `CompositablePropertySetCommand`;
`appdesigner_interface/controller/AbstractDesignTimeController` and each model's `PreferredSizing`;
`appdesigner/view/propertyview/inspector/InspectorPropertyConversion`. MATLAB's own side is readable:
`toolbox/matlab/datatools/editorconverters` and `toolbox/matlab/uicomponents/plugin/appdesigner/+inspector/+internal`. Which editor a row
has comes from the sheets R2025b's `InspectorFactory` publishes (`ui-probes/w7/w7b_inspector_raw.json`, stage W7b), each property's
`renderer`, `inPlaceEditor` and `richEditorProperties`, now kept in the embedded sheet.

## Decision

- **A row's editor is the one R2025b's sheet names**, where the generator writes the property (`DesignInspector.KindOf`, `DesignEditorInfo`).
  A property it never writes stays shown and not edited.
- **A text row** (`TextBoxEditor` over `TextAreaEditor`): lines while it has the keyboard, Enter making a new one, committed as the
  keyboard leaves it, Escape going back. At rest several lines or items run together with commas. One line is text, several a column; items a
  row. `ItemsData` takes a bracketed value as MATLAB writes it (`ItemsValueEditor`), or a row of its lines.
- **Items** (`ItemsEditor`): a radio button and the words on each row, by `ItemsModel`: a new row after the one picked, named by
  `UniqueNameGenerator` from `Item %1`, `State %1` or `Option %1`, the last row then picked; no plus or minus for a fixed number of items. A
  change of the selected item's words moves the value with it, and the items and the value go as one step.
- **Ticks** (`TicksDialogEditor`, `TickDialogTableModel`): each tick and label. A new row halfway, or one interval past the last; an empty tick
  deletes its row; a tick past the limits at either end takes its label with it. **Ticks out of ascending order stay in the table marked, with
  `Specify tick values in ascending order`, and nothing goes out** until they are right; a label typed meanwhile goes with them. Each column's menu has
  Auto, and the ticks' has `Step by`, refused with `Step value is too small` past 500 rows.
- **A gauge's colours** (`ScaleColorsEditor`): a colour, Start and End on each row. A new row is red over the top tenth of the limits. What is not a number says
  `Value must be numeric.` and goes back. **R2025b's check that Start is below End never fires** (`_getScaleColorLimitsInRow` reads fields a row does not have), so it is
  left to the component, as there.
- **View Format** (`DisplayFormatEditor`): Default, `Exactly` n `decimals`, Integer, Scientific and Custom, by `DisplayFormatMode`; no decimals at all is Integer; a
  bad number of them says `Only positive whole numbers are allowed.`. Typed in the row, a format with no conversion is refused by R2025b's `DisplayFormat` pattern.
- **A table's columns** (`UITableColumnEditor`): Name, Width(px), Auto Width, Editable, Sortable. Auto Width taken off is 75 pixels. What goes out is `_updateBeforeSendToServer`'s: names
  trimmed, a number where a width's text is one, several of one word or flag sent as one.
- **Dates**: a date picked for `Value`; Start and End for `Limits`; a table for `DisabledDates`, a new row today in an empty table, else the next free day
  (`DateModel`); the days of the week ticked, with Select All and Clear All.
- **Fields** (`PropertyFieldsEditor`): a position's x, y, width and height, limits' min and max, `max must be greater than min` holding a bad pair back. Limits with a mode have
  Auto (`ResetLimitsEditor`).
- **A colour** (`RGBColorEditor`): its numbers to two places in a box, taking numbers, a MATLAB colour's name or letter, or `#rrggbb`; the swatch opens the picker: R2025b's seven by four
  standard colours, its recent ones, and R, G and B. A word the sheet offers (`none`) stands above them.
- **"Value Options" is the old property sheet's word** (`numberFieldValueOptionsButtonText`), like W7b's More/Fewer: in R2025b's inspector a numeric field's limits and rounding are rows of their own. **The plan's "tear-away tables" drop down from a kebab** (`InspectorPropertyEditorGroup`, `richeditor_kebab`).
- **Others**: bold and italic are toggle buttons with the sheet's words; alignments and orientations a button for each word, one always on; a font's name the machine's list and
  anything typed; its size `FontSizeEditor`'s and anything typed; a colour map R2025b's 22 names; `BorderWidth` a spinner; `Icon` and `ImageSource` a box and Browse, kept beside the app
  when in its folder; a date format's list takes anything typed.
- **What R2025b gives in two commits is one step here** (`SetInspected` over several): the items and the value, colours and limits. All or nothing, as an edit of one.
- **A property's mode** (`executePropertyEditorCommand`): manual, or automatic when the value says what the class's default says to two places. Then the design holds nothing for it, so the generator writes
  nothing, and the runtime's mode is set beside it in the same step.
- **A component sized to its text** (`setComponentToPreferredSize`, `PreferredSizeProperties`): a label, hyperlink, check box, radio button and the three buttons fit a new text or font; the fields keep their width and take the
  new font's height; a button is no narrower than made; **what was sized by hand is not shrunk**; the side its text is aligned to stays, and its top. In one step with the edit; not for a grid's child.
- **The custom component's enumeration** (open item 162): with a default, a choice of its members, written by name as R2025b's `ComponentCodeGenerator` writes it (`'Fast'`); without one, left out (`UserComponentPropertyView`).
- **Two language changes it needs, measured headless** (`ui-probes/w7/enum/w7c_enum.m`, R2025b's `-batch` with no figure, and the same file in JGraph, line for line): a property typed as an enumeration takes a member's name, char or string, refused in R2025b's words; with no
  default it starts as a 0-by-0 of the class.

## Consequences

- The three table editors share `DesignRichEditors.Table`, which makes its rows anew on each change. A cell is a text box, so each has an automation id and a check can reach it.
- `DesignEditors` holds the models, free of the screen, and `DesignEditorValues` what they read from and give to a component.
- `DesignInspectorView` is split: `DesignInspectorView.Editors` has the row editors, `DesignRichEditors` what opens from a kebab.
- A text row needs its keyboard moved to commit, as Enter is a new line. A font's size is now a list, whose own text box taking the keyboard does not commit it.

## Measured

- **R2025b's own code**: `ui-probes/w7/oracle/editors.js` loads `UniqueNameGenerator`, `ItemsModel`, `TickDialogTableModel`, `ScaleColorsModel`, `DisplayFormatMode`, `UITableColumnEditorModel` and its table model, `DateModel` and
  `PropertyCommandFactory` whole, over stand-ins for the data store and MATLAB, and cuts `ItemsEditor._handleModelUpdate`, `TicksDialogEditor._handleModelUpdate` and `_flagInvalidNumericTicks`, `UITableColumnEditor._handleEditorValueChange` with
  what it calls, `PropertyFieldsEditor._isValidationError` and `setComponentToPreferredSize` with what it calls out of the bundle. `DesignEditorsOracle.json`: 400 names, 300 items tables, 400 ticks tables, 250 colour tables, 250 columns tables, 150 dates tables, each of up to 10 changes, with
  32 View Formats, 300 modes, 500 sizes and 100 fields. `DesignEditorsOracleTests` (10) replays them, with no difference.
- **R2025b's enumeration rules**: headless (`matlab -noFigureWindows -batch`): an enumeration-typed property with no default is `0x0`, takes `'Fast'` and `"Slow"`, and refuses `'Nope'` and `3` with `MATLAB:validation:UnableToConvert`.
- **Tests**: `AppDesignerEditorsTests` (45), one in `AppDesignerUserComponentTests`. With the designer, App Designer, toolstrip, settings and enumeration suites: 1067 pass.
- **The window check** (`run-w7cdesigner.ps1`, JGraph.Application in Release over `w4/W4App.m` with a drop-down, a discrete knob, a gauge, a table, a date picker and a numeric field added): all 136 checks pass, each edit read back from the file. Pictures of the Items, ticks, colours,
  columns, days and View Format editors and of the picker show them. `run-w7bdesigner.ps1` (90), `run-w7adesigner.ps1` (103), `run-w2designer.ps1` (39), `run-w4designer.ps1` (23), `run-w5designer.ps1` (34), `run-w5bdesigner.ps1` (55), `run-w5cdesigner.ps1` (41), `run-w5ddesigner.ps1` (69), `run-w5edesigner.ps1` (53) and `run-w6designer.ps1` (34) still pass on the final source.

## Not measured

- **R2025b's App Designer itself in a window**: its code was run under node over stand-ins, not in its own client.
- **The preferred size's numbers**: R2025b measures its widget in its browser. Here the shared text measure gives the widget's size (`DesignPreferredSize.Widget`); the rule around it is R2025b's own.
- **A mouse, and Browse's dialog**, in a window: what opens from a kebab is reached through patterns, and Browse's dialog not driven.
- **Non-numeric ticks**: `TicksEditorValidator`'s datetime, duration and categorical rulers are MATLAB's, and do not arise for what the catalog writes here.

## Divergences

- **A text row does not split at commas and semicolons.** R2025b's `_getMultiLineEditValue` turns them into new lines when a plain text's row is edited, so a label saying `a, b` would come back as two lines. Read, not run in its client; not kept.
- **Typing a tick's own number again keeps it a number.** R2025b's table is left holding the text, which it marks at the next change. Not kept.
- **What R2025b commits as two steps is one**, so one Undo takes both.
- **A rich editor's kebab stands on the row it belongs to**, not across an editor group's rows, and it drops down in place: the tear-away has no separate window.
- **A table's cells are text boxes**, with no sort, tab-to-next or paste from the clipboard. Tick columns have their menu on a button in the header.
- **The picker's custom colours are R, G and B boxes with OK**, with no gradient, hex or HSV, and no preview.
- **A colour map is a list of names**, with no previews or custom entry. `ColorOrder` and a table's `BackgroundColor` are a list of colours with no Up or Down. `LineStyleOrder` stays as MATLAB writes it, `URL` a text box with no link button.
- **Icons are JGraph's**: bars for an alignment, letters for the rest, B and I, a vertical ellipsis.
- **Limits' Auto sets the class's default**, where R2025b's component works out new ones.
- **A linear gauge is not sized by its properties**, nor a label's `Interpreter` measured.
- **Browse says `Open Image File` for every file**, where R2025b's filter comes from its `filterSpec`.
