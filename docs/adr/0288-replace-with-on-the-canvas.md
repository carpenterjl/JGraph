# ADR 0288 — Replace With on the canvas

## Status

Accepted, 2026-10-30. Stage W5e of the App Designer window plan, the last part of W5 (clipboard and
structure), after the clipboard (W5a, ADR 0284), in-place text (W5b, ADR 0285), tabs, button groups and trees
(W5c, ADR 0286) and menus, toolbars and context menus (W5d, ADR 0287).

## Context

R2024a added Replace With to App Designer's canvas: a component is made into another class of its family, a
list box a drop-down, a push button a state button, a tree a check box tree, keeping what the two share. The
stage's job is the menu item and the edit behind it, by R2025b's rules.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`):
`appdesigner/view/contextmenu/CompReplacementProvider`, `DesignAreaContextMenuController`,
`appdesigner/util/CompReplacementUtil`, `appdesigner/command/ReplaceComponentCommand`, `DeleteCommand`,
`AddComponentCommand`, `AddLabelToComponentCommand`, `appdesigner/componentreplacement` (the factory, the base
replacer and one replacer per family), `ComponentFamilyTypes`, each model's `Family`,
`uicomponents_appdesigner_plugin/init/derived/ComponentDefaultsProduction` (every class's defaults, and the
order the menu offers them in), `ReplaceComponentCodeNameGenerator`, `CodeNameGeneratorFactory`,
`CreateLabeledComponentCodeNameGenerator`, `ModifyCodeNameGenerator`, `AbstractDesignTimeController`'s
`setComponentToPreferredSize`, `DesignerWidgetDataManager._createWidget` and `CodeController`'s binding of an
added component.

## Decision

- **The menu** (`CompReplacementProvider`): last in the canvas's menu, and last in a tool's, Replace With and
  its submenu, shown for one selected component with a family. A labelled component and its field count as the
  field; its label does not count. No family, several selected, a plain group: no item.
- **The families** (each model's `Family`, in `ComponentDefaultsProduction`'s order): Tree with Check Box Tree;
  Push Tool with Toggle Tool; Button with State Button; Discrete Knob, Drop Down and List Box; Edit Field with
  Text Area; four gauges; Hyperlink with Label; Knob, Numeric Edit Field, Range Slider, Slider and Spinner;
  three switches. Edit Field and Numeric Edit Field are offered to each other through `Family.Cross`, but a text
  area is not offered to a numeric field nor the rest of the numeric family to a text field. The tools' family is
  undefined in R2025b (`ComponentFamilyTypes` has no `Tool`), which works as the two alone share it.
- **What it holds** (`_ComponentFamilyReplacer.replaceComponent`): every property both classes have takes the old
  one's value, and the rest are a new one's. A property with a mode that was never set keeps its automatic mode.
  Callbacks and a context menu go with them. Then the family's conversions: a numeric class's infinite limits
  are the value's 100 either side in a class whose limits must be finite, a range slider's first goes to one
  number and one number goes up to the upper limit; a text area's lines run together for a field, and a numeric
  field and a text field start from 0 and nothing; a hyperlink and a label use the other's default where theirs
  is still the default; a gauge keeps its own orientation.
- **Where it goes** (`_convertLocation`, `_convertSize`): a new one's size, at the old outer box's corner, the
  top kept beside a label on the left. A gauge stays as wide and is shaped as its class is; a spinner and a numeric
  field keep each other's size; a knob and a slider stand within the box a new one takes. A label, a hyperlink, a
  button and a state button are then sized to their text, kept to the side their text is aligned to
  (`setComponentToPreferredSize`, `_getLocationBasedOnNewSize`). A grid's child keeps its cells, a slider its first
  row (`_convertLayoutConstraints`); a tool has no place.
- **What it is called** (`ReplaceComponentCodeNameGenerator`): a name given by hand is kept; one that follows from
  the text is made again for the new class, as a labelled one's is (the generator asks for that whether or not
  there is a label). A label still saying its class's word says the new class's, and one with its own words keeps
  them, the names following from them (`SpeedSlider` becomes `SpeedKnob`). A text the new name does not follow from
  counts as a name by hand (`Go` on a button `GoButton` gives a state button `Button`, by R2025b's own code).
- **What follows the name**: the design's references and the user's code (`renameComponentFromReplacement`).
- **Callbacks** (`CompReplacementUtil.updateCallbackDefaultNames`, `CodeController`): one on a property the new class
  has stays bound; with its default name and every binding on one property it takes the new default name; its
  default code is made for the new component. One on a property the new class does not have is dropped if it held
  only its default code and nothing else is bound to it, and otherwise stays in the code bound to nothing.
- **What is inside it** moves over: a tree's nodes go to the new tree.
- **One edit**: the new one takes the old one's place among its siblings and in its group, and is selected, or its
  labelled component is; Undo puts back the same objects, selection and names.
- `DesignReplacement` holds the rules, and `DesignDocument.Replace` the edit.

## Consequences

- W5 is finished. R2025b's component browser menu (`_createComponentTreeContextMenu`) has no Replace With, so W7 adds none.
- W8's Callbacks join the canvas's and the tool's menus before Replace With.

## Measured

- **R2025b's own code**: `ui-probes/w5/oracle/replace.js` loads `ComponentDefaultsProduction`, `CompReplacementUtil`,
  `CompReplacementProvider`, `ReplaceComponentCommand`, the replacers and the name generators whole, and runs
  `execute()` over stand-ins for the peer nodes and the commands it runs, into `DesignReplacementOracle.json`: 45
  classes, 51 menu questions, 900 conversions, 900 name scenes and 52 callback scenes, each from a seeded figure.
  `DesignReplacementOracleTests` (6) replays what is offered, a new one's box, where it goes and its size, limits and
  values, and the names; `AppDesignerReplaceTests.TheCallbacksAreR2025bs` replays the callbacks through the document.
- **Tests**: `AppDesignerReplaceTests` (15). With the designer, App Designer, toolstrip and settings suites: 881 pass.
- **The window check** (`run-w5edesigner.ps1`, JGraph.Application in Release over `w4/W4App.m` with a slider, a tree and
  a toolbar added): components selected in the component browser, the menu key posted to the designer's own window
  once it is checked to be in front, the menu's items expanded and invoked through their patterns, each result read
  back from the file. A labelled slider to a knob, undone and redone; a button, a label, an edit field, a tree and a
  tool each to another class. All 53 checks pass; pictures show the knob, its label under it, the hyperlink and the
  check box tree. `run-w2designer.ps1` (39), `run-w4designer.ps1` (23), `run-w5designer.ps1` (34), `run-w5bdesigner.ps1`
  (55), `run-w5cdesigner.ps1` (41), `run-w5ddesigner.ps1` (69) and `run-w6designer.ps1` (34) still pass.

## Not measured

- **R2025b's App Designer itself in a window**: its commands were run under node over stand-ins, not in its own
  client. What the widgets then do (`setComponentToPreferredSize`, a linear gauge's computed size) was read.
- **Which of `Location` and `Position` R2025b uses**: read from `_createWidget`, which applies them only for a loaded
  app, so an added component's client takes `Location`, the value this stage follows.

## Divergences

- **A replaced labelled component keeps its place among its siblings, and so its tab order.** R2025b takes the index
  before deleting the label as well, so the new one lands one further on, past the next sibling, and its label one
  before the sibling before it. Read, not run.
- **A callback whose name would not change keeps its name.** R2025b counts the callback's own name as taken, and so makes
  `rateValueChanged` `rateValueChanged2` when `rate` keeps its name.
- **A label named by hand that says its class's word does not fail.** R2025b's `ReplaceComponentCodeNameGenerator` reads
  a generated label that was not asked for, and throws a TypeError; here the label says the new class's word and keeps
  its name.
- **The user's code follows a label to the name it ends up with.** R2025b renames `app.SpeedSliderLabel` to the generator's
  `EditFieldLabel`, while the label itself becomes `SpeedEditFieldLabel`, leaving the code naming nothing.
- **A spinner and a numeric field that keep a height of their own stay where they are** beside a label. R2025b's top kept
  takes a new one's height, so it moves them by the difference.
- **An infinite value replaced for a range slider is a new one's pair.** R2025b nests the pair in a pair, and the limits
  come out NaN.
- **A label's and a button's width after a replacement use JGraph's text measure** and the label's own font is not
  measured; a linear gauge keeps its converted size rather than its widget's computed one; a spinner, numeric field, drop
  down and text field keep their converted height rather than what their widget's preferred size would give.
- **A callback with only its default code stays where it is in the code.** R2025b removes it and makes it again, so that
  it moves to the end of the callbacks.
- **A member of a plain group keeps its place among the group's members**, where R2025b adds it at the end.
- **A label stays the same object, with its text, place and name edited**, where R2025b deletes and makes it again; nothing
  differs in the file but the order rule above.
- **No Simulink bindings carry over** (`AddBindingCommand`), as JGraph has none.
