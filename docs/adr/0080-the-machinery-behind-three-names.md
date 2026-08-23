# 0080 — The machinery behind three names

Date: 2026-08-23 · Milestone: M80 · Status: accepted

## Context

Three property names have been unanswered since M73, on every kind that documents them:
`Layout` (axes, polaraxes, legend, colorbar, heatmap, bubblelegend), `Interactions` (axes,
polaraxes, text) and `Toolbar` (axes, polaraxes). Eleven names in all, and each wave since has
restated them as a ceiling rather than closed them.

The ceiling was honest, and it was always the same sentence: *the property names a thing this build
does not have.* What M80 found on looking again is that two of the three were nearly false.

- **`Layout` names a cell in a tiled layout, and this build had `tiledlayout`** — as three integers
  and a flag in a closure inside the script layer. There was no object, so there was nothing for
  `t.TileSpacing`, `nexttile(span)` or `ax.Layout.Tile` to be. Four documented forms failed for the
  same reason the property did.
- **`Interactions` names the gestures an axes answers to without a tool being chosen, and every one
  of them was already happening**: dragging pans, the wheel zooms, a click pins a data tip. What was
  missing was any way to say so, or to say no — which is why `disableDefaultInteractivity` had been
  accepted and doing nothing since M71.
- **`Toolbar` was the one genuinely missing thing**: no strip of buttons existed over an axes.

## Decisions taken before any code

1. **A tiled layout becomes an object owned by the figure.** The closure state moves onto it, and
   both verbs become doors to it. `tiledlayout` answers the layout; `nexttile` answers its tile.

2. **A tiled layout gets its own geometry, and `subplot` keeps `SubplotBounds` untouched.** This is
   the M78 rule — when a default differs for one path, it belongs on that path — applied where it
   costs something: a tiled figure now sits a little inside the frame where a subplot figure meets
   it, because that is what `Padding` means and the only way for the word to have a setting. Dozens
   of frozen scripts draw through `subplot` and not one of them moves.

3. **The shared bands are arithmetic used twice.** A layout's title, subtitle and two shared labels
   each reserve a fraction of the figure, and the renderer draws into exactly the band the layout
   reserved. One source of truth, so a title can never overlap the tiles it displaced.

4. **`Interactions` gates the default gestures and nothing else.** A tool the user chose from the
   window's own toolbar is a different question, and MATLAB's list does not describe it either. What
   the list gates is the pointer's own reading of a press, the wheel, and the click that pins a tip.

5. **The axes toolbar is drawn by the control and never by the renderer.** A toolbar is window
   chrome: an export, a saved document and the `-batch` CLI must not carry it. `stess_52` §12 pins
   exactly that — hiding the toolbar leaves an exported picture byte-identical, because an exported
   picture never had it.

6. **Every default button is one this build acts on.** MATLAB's default set opens with a brush
   button and this one does not, because there is no data-brushing mode here and a button that did
   nothing when pressed would be the failure this whole wave exists to undo.

## What each part is built on

| Part | Built on |
|---|---|
| The grid's arithmetic | a generalisation of `FigureModel.SubplotBounds`, left where it was |
| A tile's placement | `AxesModel.NormalizedBounds`, written by `Arrange` rather than by the verb |
| `ax.Layout` | a small view object over the axes' own cell and span — no second copy of the state |
| `Layout` on furniture | the owner delegate `AddFurniturePosition` already takes |
| The gesture gates | `PointerMode`, `PanDragGesture` and `InteractionController.Wheel`, each asking the axes first |
| `Dimensions` | one more argument to `Navigation.Pan` and `Navigation.ZoomAboutPixel` |
| The toolbar's callback | the M71 callback queue, with one more event kind |
| The toolbar's actions | `ResetView`, `SetMode` and `Wheel` — the control's own, reached a second way |

## Verification

**Properties answered:**

| kind | before | after | what is left |
|---|---:|---:|---|
| axes | 144/147 | **147/147** | — |
| polaraxes | 104/107 | **107/107** | — |
| text | 40/41 | **41/41** | — |
| legend | 38/39 | **39/39** | — |
| colorbar | 41/42 | **42/42** | — |
| heatmap | 38/39 | **39/39** | — |
| bubblelegend | 36/37 | **37/37** | — |
| tiledlayout | *new kind* | **28/28** | — |
| axestoolbar | *new kind* | **15/15** | — |

**Totals: 1,367 of 1,394 → 1,421 of 1,437.** The denominator grew by 43 because two kinds joined the
table; the numerator grew by 54, which is those 43 plus the 11 names the ceiling had held.

**Twenty-six of the thirty measured kinds now answer every documented name, and every one of the
eighteen that do not is geographic** — `LatitudeData`, `LongitudeData` and their sources and
variables, on line, scatter and bubblechart. That is the whole of what this table has left.

Gate: 0 warnings in Release and Debug · 5,178 tests · 52 of 52 stress scripts.

`axtoolbar` and `axtoolbarbtn` come off the plot-tool exclusion list, so builtins read 916 → 918 and
the plot-tool group is six verbs and a `cameratoolbar` rather than eight and one.

## Divergences recorded

- **A tiled figure is laid out a little inside the frame where a subplot figure meets it.** The two
  grids have their own arithmetic since M80, which is what makes `Padding` a setting rather than a
  word. Nothing drawn through `subplot` moved.

- **A layout inside a layout is not nested.** MATLAB lets a `tiledlayout` hold another; a figure here
  has at most one, and the layout's own `Layout` answers empty.

- **A layout's `Toolbar` answers empty and refuses a write.** MATLAB gives a layout none by default
  either, so the empty answer is faithful; the refusal is this build's, because the strip it draws
  hovers over an axes and a layout is not one.

- **The default toolbar has six buttons where MATLAB's has seven.** The missing one is `brush`:
  there is no data-brushing mode here, and a button that did nothing when pressed would be worse
  than a button that is not there.

- **`SnapToDataVertex` answers `'on'` and refuses `'off'`.** A tip here is always pinned to a data
  point — the placement walks the data to find the nearest one — so a tip between two points would
  name a reading nobody took.

- **A text object answers no interactions of its own.** It is moved with the pointer and edited in
  the plot browser, and neither is a gesture MATLAB's list has a word for. The empty answer is the
  honest one, and a write is refused rather than remembered.

- **The three remaining interactivity toggles are still accepted and still change nothing.**
  `enableLegacyExplorationModes`, `addToolbarExplorationButtons` and
  `removeToolbarExplorationButtons` name a legacy mode to restore and a toolbar button to add, and
  this build has neither. Their two companions stopped being no-ops in this wave.

- **`tiledlayout(parent, …)` reads its parent from the shape of the call.** A figure's handle is its
  number here, so in `tiledlayout(3, 3)` the first 3 *is* a handle to figure 3 once three figures are
  open. What tells a parent from a row count is what follows it: two numbers, or the word `'flow'`.

- **A toolbar's buttons and its callbacks are not serialized; only whether it is shown.** The buttons
  are rebuilt as the default set unless a script asked for others, and no callback in this build has
  ever been written to a document.

## What is not done

- **The geographic family**, 18 names across three kinds. It needs a basemap tile service, which is
  a product rather than a milestone, and it is now the entirety of what the property table has left.
