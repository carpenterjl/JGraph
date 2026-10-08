# ADR 0217 — App odds and the stale stress scripts: open items 19, 45, 50, 74, 80 and 81

## Status

Accepted, 2026-10-08. The sixth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored). The items are small, and each sits where a script meets the application. What
R2025b does was recorded headless (probes `probe_b7`, `probe_b7b` and the `stress81` probes in the
open-items scratch, then the fixture `oi_app_odds`).

## Decision

### A `CreateFcn` on `uifigure` and `uiaxes` (item 80)

R2025b runs a `CreateFcn` named among `uifigure`'s or `uiaxes`'s options once the other options are
set. During it, `gcbo` is the new object and the event is `[]`. Both makers now fire it as every
other maker does (`FireCreateFcn`), and both are back on `ScriptRunningBuiltins`, where the
ownership audit agrees. `dialog` stores a `CreateFcn` and never runs it, in R2025b as here. It
stays off the list.

### `uiconfirm` asked for nothing (item 50)

Asked for no output, `uiconfirm` now shows its question and returns at once, leaving the answer
to a `CloseFcn`. Asked for an output, it waits as before. The builtin takes the call's output count
(`TakesOutputCount`). With nobody to answer, both forms are still refused. R2025b's plain `-batch`
refuses both with `MATLAB:hg:NonInteractiveFunctionSupport` (`oi_app_odds`, `confirm_invisible`).
Under `-noFigureWindows` it returned at once, so the two modes disagree. The recorder's mode is the
reference.

### `Tag` through this build's figure files (item 74)

The document carries the `Tag` of a figure, an axes and every plot (`FigureDto.Tag`,
`AxesDto.Tag`, `PlotDto.Tag`), so `findobj(openfig(f), 'Tag', 'mine')` finds what `savefig`
wrote. A document from before has none, and its objects open untagged as before. `UserData` is not
carried: it is a script value, and the serialization layer cannot write one.

### `exportapp` to a PDF (item 45)

`exportapp(fig, 'x.pdf')` writes the window's picture on one page of the window's size
(`FigureExporter.WritePicturePdf`). The page size comes from the capture's resolution, so a
192-dpi capture prints at the window's size.

### The data viewer's "too large" (item 19)

A value too large to copy for the data viewer carries a `ScriptOversizeValue` marker in place of
its value:

- an array past 2,000,000 elements;
- a matrix, cell or struct past `ScriptValueGrid.MaxCells`.

The viewer says "too large" from the marker, rather than guessing from the Type column, which has
said `double` since the panel took MATLAB's class names.

### The stale stress scripts (item 81)

Each failing section of `stess_39`, `40`, `43`, `47` and `56` was read against R2025b, and so were
`stess_34` and `41` after ADR 0214. Each section's assertion was updated where R2025b agrees with
today's JGraph:

- `waitfor`'s `MATLAB:waitfor:BadProperty`;
- a static method through an instance;
- `CloseRequestFcn`'s `'closereq'`;
- `uimenu` with no parent;
- `MenuBar`, docking, `IntegerHandle`, figure units and axes points;
- `exportapp`'s headless refusal;
- `uiaxes` making its own uifigure;
- the script frame of a top-level error.

Three sections showed real faults, fixed here:

- **A validated property's refusal** is R2025b's sentence in the MATLAB dialect: "Error setting
  property 'Value' of class 'M68_Sample'. Value must be finite.", where JGraph said
  `M68_Sample.Value: …`. JGS keeps its own.
- **A write to a Constant property** through an instance is `MATLAB:class:SetProhibited`, "Unable to
  set the 'Reference' property of class ''M68_Sample'' because it is read-only.".
- **`classdef` in `eval`'d text** is `MATLAB:m_illegal_reserved_keyword_usage`, "Error: Illegal use
  of reserved keyword "classdef".", whole or not. It had been parsed and refused for its content.

Three more are filed as items 82–84: the model's `Name` answering on axes, `delete` of a deleted
handle, and the headless print dialogs' identifiers with `pagesetupdlg`'s removal.

## Measured

- `oi_app_odds`: 9 lines, all exact.
- `OpenItemsBatch6Tests`: the oversize markers, `uiconfirm` returning with a stand-in that never
  answers, the `CreateFcn`s and `dialog`'s silence, and the tags through `savefig` and `openfig`.
- The stress suite: all of `stess_34`, `39`, `40`, `41`, `43`, `47` and `56` pass.

## Divergences

- **A misnamed class file** is refused with R2025b's identifier `MATLAB:m_class_filename` and
  JGraph's sentence, which names both classes. R2025b's names the file, line and column and says
  "Class name and filename must match.".
- **`Renderer`, `RendererMode` and `DockControls` keep JGraph's truthful answers.** They are
  `painters`, `auto` and `off`, and setting another value is refused. R2025b answers `opengl` and
  `on` and takes the writes (`stess_47`, section 9).
- **`UserData` does not travel through this build's figure files** (item 74's other half).

## Consequences

A script that names a `CreateFcn` on a uifigure, branches on a Constant write's identifier, or saves
tagged objects and finds them again after opening the file behaves as in R2025b. The stress suite
is green again, so its failures mean something at the next gate.
