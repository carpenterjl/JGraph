# ADR 0337 — A figure nothing placed answers a corner on the screen, and a lone ':' on [] grows by the right side's count

## Status

Accepted, 2026-11-11. Two migration bugs reported by the user, fixed together: `get(gcf, 'Position')`
on a new figure answered `[0 0 640 480]`, and `A(:, 1) = zeros(1, 5)` on `A = []` was refused.

## Context

**The figure's corner.** `FigureModel.Position` is a corner that something has said. Until a script
writes `Position` (or `movegui` or a dialog places the figure) it stays at its default, (0, 0), and
the window places itself. The script getter read it as it stood, so every unplaced figure reported the
origin. R2025b never does:
- a uifigure is `[680 578 560 420]` on a 1920-by-1200 screen;
- a classic figure is `[363 278 1194 719]` on the same screen (R2025b sizes it by the screen);
- the documented classic default is `[680 458 560 420]` on 1920 by 1080.

All three are across the middle of the primary screen, with the top 202 or 203 pixels below the
screen's.

**The colon on `[]`.** A `:` on the shapeless 0-by-0 has nothing to mean "every one of", and MATLAB
takes its extent from the right side (M96b). JGraph took it from the right side's extent in the
colon's own place, so `A(:, 1)` read the row count of `zeros(1, 5)`: one, against five values, refused.
R2025b's answers, measured headless:

| Write on `A = []` | R2025b |
|---|---|
| `A(:, 1) = zeros(1, 5)` and `A(:, 1) = 1:5` | 5-by-1 |
| `A(1, :) = (1:5).'` | 1-by-5 |
| `A(:, 2) = 1:3` | 3-by-2 |
| `A(:, [1 2]) = ones(1, 3, 2)` | 3-by-2 |
| `A(:, 1, 2) = 1:5` | 5-by-1-by-2 |
| `A(:, :) = 1:6`, `A(:, :) = ones(2, 3)` | 1-by-6, 2-by-3 (the right side's shape) |
| `A(:, 1, :) = ones(3, 2)` | 3-by-1-by-2 |
| `C = {}; C(:, 1) = {1, 2, 3}` and `S = strings(0); S(:, 1) = ["a" "b"]` | 3-by-1, 2-by-1 |
| `zeros(0, 3)` then `A(:, 1) = 1:5` | refused: it has a shape |

## Decision

### The corner

`JgsGraphicsProperties.FigureCorner` answers for every reader of a figure's pixel rectangle
(`Position`, `InnerPosition`, `OuterPosition` headless, `movegui`, the dialogs placed over a figure,
a component event's screen location):
- a placed figure: its `Position`, as before;
- an unplaced figure with a window up: the window's corner. The binding already reports this for
  `OuterPosition`, and it is the convention a placed figure keeps (`movegui`: "a shown figure's
  Position is its window's corner");
- otherwise, R2025b's default: `x = floor((W - w) / 2)` and `y = H - h - 202` on the primary screen.

Nothing is written into the model, so an unplaced figure stays unplaced. Its window still places
itself, a saved `.graph` file does not gain a position, and a person dragging the window does not
make it placed. A script that changes part of the rectangle (`f.Position(3) = 300`) places the figure
at the corner it read, as it would in R2025b.

### The colon

`Interpreter.ShapelessPicks` (two subscripts, numeric, string and cell roads) and the N-subscript
road use one rule:
- a **lone** colon on an empty target takes `numel(rhs)` over what the other subscripts select, when
  that divides (`LoneColonLength`);
- **several** colons take the right side's extents in turn, as before;
- where the lone count does not divide, the colon falls back to the positional extent, which is the
  refusal R2025b gives.

The N-subscript road now counts the non-colon subscripts before it sizes the colon. A target with a
shape (`zeros(0, 3)`, `zeros(3, 0)`) is unchanged.

## Consequences

- `figure(1); disp(get(gcf, 'Position'))` prints `640 518 640 480` headless on 1920 by 1200. The
  uifigure answers `[680 578 560 420]`, R2025b's own.
- Every row of the user's case and its neighbours matches R2025b. That includes the `int8` and
  logical right sides: a logical written into `[]` is double, as measured.
- Two parity fixtures were recorded from R2025b 25.2 (both green in the packed and boxed lanes, none
  divergent):
  - `colon_on_empty_growth`: 27 rows;
  - `fig_default_corner`: 12 rows, recorded with `-noFigureWindows`. It checks the corner as it stands
    to the screen, because the two engines size a classic figure differently.
- The MatlabParity suite passes (287 of 287). So do the indexing, assignment, figure, window, UI and
  dialog classes (323 + 748).

## Divergences

- **An unplaced figure's corner before its window is up is R2025b's default, and its window may go
  elsewhere.** R2025b puts the window where it reports. JGraph's figure window still places itself
  (Windows' default position, at the window's own size with its panels). Once the window is up,
  `Position` reports where it went, so a script can read two corners for one figure, before and after
  the window appears. Headless the default corner is the only answer.
- **The default figure is 640 by 480; R2025b sizes a classic figure by the screen** (1194 by 719 on
  1920 by 1200, unchanged by this ADR). The corner follows the engine's own size, so the top gap is
  202 in JGraph and 203 for R2025b's odd height.

## Still open

- **Open item 285.** For a target that is not empty, JGraph still accepts a two-subscript write whose
  element count matches but whose non-singleton extents do not. Examples are
  `A = zeros(3, 4); A(2:3, 2:3) = 1:4`, `A = zeros(2, 3); A(:, :) = 1:6` and a 2-by-5 into a 5-by-2
  selection. R2025b refuses each with `MATLAB:subsassigndimmismatch`. This is an old leniency, left
  alone here.
- **Open item 286.** `s = struct('a', {}); s(:, 1) = struct('a', {1, 2})` is refused: "Cannot assign
  by index into a struct". R2025b makes a 2-by-1 struct array.
