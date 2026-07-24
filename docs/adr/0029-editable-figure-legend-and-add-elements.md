# ADR 0029 — A fully editable figure: composite properties, an editable legend, and adding elements

## Status

Accepted (M26, 2026-07-23).

## Context

The property inspector and plot browser could only edit *part* of a figure, and only objects that
already existed:

1. **Whole property types were invisible.** The inspector's reflection walk mapped a fixed set of
   scalar types (string, number, bool, enum, `Color`, `Color?`, `DataRange`) to editors and dropped
   everything else. Every `TextStyle`, `LineStyle`, `Rect2D` and `Size2D` property therefore had no
   UI at all — an axes title's *text* was editable but its font, size, weight and colour, which live
   inside `TitleStyle`, were not.
2. **The legend was not an editable object.** Its rows were recomputed from the plots on every paint;
   there was no way to rename a row without renaming the series, hide a row, reorder rows, or place
   the legend anywhere but the eight presets. Its box was never published to the interaction layer,
   so it could not be selected or dragged.
3. **Nothing could be added from the UI.** The model had `AddAxes`/`AddSubplot`/`AddXAxis` and the
   annotation constructors, but no menu reached them; the legend and colorbar existed on every axes
   but, starting hidden, could only be revealed from script.

## Decision

**Composite properties expand in place.** `EditableProperty` became accessor-based: a descriptor can
address a *member of a struct-valued property*, reading the whole struct, swapping one member, and
writing it back. An explicit table in `EditablePropertyFactory` lists, per composite type
(`TextStyle`, `LineStyle`, `Rect2D`, `Size2D`, `Point2D`), each member's editor and a one-line
rebuilder. A composite renders as a collapsible header row plus one child row per member, all in the
root property's category. `MarkerStyle` is deliberately absent — no model property is of that type,
so an entry would be dead code. A new editable font-family combo backs the `TextStyle` font member.

Undo is recorded against the **root** property, so changing a font restores the whole style in one
step. The `ValueType` on each descriptor (not the root `PropertyInfo`'s type) drives parsing, so an
enum nested in a `LineStyle` resolves its own values.

**The legend is a first-class object.** A `LegendModel` now owns an ordered list of
`LegendEntryModel` rows, each bound to a plot with a label override and an include flag. Rows are
reconciled with the plots by `LegendModel.SyncEntries`, run from the renderer's existing pre-layout
pass; it is idempotent and only invalidates when the rows actually changed, so a steady-state repaint
costs nothing. A row's swatch always comes from the plot's draw-order palette index, so it can never
disagree with what is drawn. The legend gained a `Custom` position plus a fractional `Location`, is
draggable in edit mode (published bounds → hit-test → one composite undo step), and its box is drawn
with the standard selection handles.

**Elements are added from a menu.** A UI-free `FigureElementCommands` wraps the model APIs; a
descriptor-based `ElementMenuBuilder` produces the applicable add-menu for the selected object,
rendered by both the plot browser's context menu and an "Add ▾" header button. Each subplot already
has its own tree branch, which now reaches its legend entries and colorbar.

**Undo policy is unchanged (ADR 0005).** Property edits, moves, and annotation add/delete are
undoable. Creating or removing an axes — and re-tiling a subplot grid, which is part of creating the
axes — is structural and *not* undoable; a removal is confirmed with a dialog, exactly as plot
removal already was, because tearing down an axes destroys every plot in it.

## Serialization

`LegendDto` gained an optional `Entries` list (each `{ PlotIndex, Label, Visible }`, referencing a
plot by its index within the axes — plots carry no id) and `LocationX`/`LocationY`. Both are optional
with defaults, so a pre-M26 v5 document still loads: it simply has no entries, and the first paint's
sync pass rebuilds them. **No `GraphFormat.CurrentVersion` bump.**

## Alternatives considered

- **Reflection over the struct constructors** instead of an explicit member table — rejected: the
  table makes an omitted member visibly omitted rather than silently mis-bound, and a rebuilder is
  one line.
- **Free-standing legend rows with hand-picked swatches** — rejected: a swatch that can drift from
  the series it legends is a bug waiting to happen. Rows stay series-bound.
- **Snap-to-nearest-preset on drag** — rejected: free placement with the presets retained is
  simpler and reclaims the box when a preset is re-selected.
- **Merging the two panes / nesting property rows in the tree** — rejected with the user: property
  rows would sit several indent levels deep in a narrow pane. The browser stays the tree; the
  inspector stays the property grid.

## Consequences

- Every visible part of a figure — text styling included — is now reachable and editable by hand.
- The published-legend-bounds mechanism is the seam for later dragging of titles, axis labels, or the
  colorbar (out of scope here).
- `CompositeAction` (an ordered, reverse-undoing group) exists for the first time, for the legend
  drag; future multi-property gestures can reuse it.
