# ADR 0199 — Containers, units, and the `uifigure`

## Status

Accepted, 2026-10-04. Stage U2 of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It builds on ADR 0198 (U1: `uicontrol`,
frames, the layout and the component layer).

It lifts two rows of ADR 0075 (`docs/matlab-divergences.md`):

- Axes `Units` answered `'normalized'` and refused every other word. An axes now takes R2025b's
  six units.
- Figure `Units` and `IntegerHandle` each answered one word and refused every other. Both are now
  R2025b's. (`Renderer`, `RendererMode`, `WindowStyle`, `DockControls` and figure `Clipping` stay as
  they were.)

It retires two divergences of ADR 0198 and half of a third: a component's `Units` is no longer
pixels only, the order `Children` lists components and axes in was never a divergence (see "The
tree"), and a resize hands its callback R2025b's event data.

## Context

After U1 a script could put a `uicontrol` in a figure, in pixels. Everything else a GUI is laid out
with was refused or missing: panels, any unit but pixels, a real screen size, `uifigure`, the
placement verbs. R2025b's behaviour for all of it was measured headless first
(`tools/matlab-checklist/ui-probes/u2`), and the three parity fixtures of this stage were recorded
from it.

## Decision

### The container tree

- `IUiContainer` is what holds components: a figure, or a `UiContainerModel`. `UiPanelModel` is
  the first container; button groups and tabs will be others.
- A container holds its **components** in its own collection, in creation order (back to front).
- **Axes stay in their figure's list.** An axes placed in a panel names it as its `Container`.
  The renderer, the document format, every drawing verb and `gca` know an axes as a figure's, and
  none of them had to change; MATLAB's `Parent`, `Children`, `ancestor`, `findobj` and `delete`
  answer from `Container`, so a script sees the tree R2025b has.
- A panel that moves to another figure, is copied or is deleted takes the axes placed in it along.

### Units

`UiUnitConverter` is the units engine. A pixel is 1/96 inch, a DIP. Pixel positions are 1-based and
every other unit counts from 0, so pixel `x` is `(x − 1)` units along; sizes convert by the factor
alone. `normalized` is a fraction of the parent's inner area (of the primary screen, for a figure).
A character is 5.6 by 15 pixels.

| Object | What the model keeps | `Units` write |
|---|---|---|
| component | `Position` in its `Units` | re-expresses `Position`; the component stays put |
| figure | pixels (`Position`, `Size`) | changes what reads and writes are converted to |
| axes | fractions of the area it is placed in; in any other unit also a pinned pixel rectangle | pins the rectangle in pixels, or turns the pin back into fractions |

- Because a `Units` write re-expresses `Position`, the order of `'Units'` and `'Position'` among a
  creating call's options matters, as it does in R2025b. `axes` applies them in call order.
- An axes placed in absolute units keeps its pixels when the figure is resized: the renderer
  converts the pin against the area at every draw (`AxesModel.PlacementIn`).
- The root's `ScreenSize` and `MonitorPositions` are real (`UiScreen`): every monitor, in DIPs, in
  MATLAB's upward-Y coordinates, in the root's own `Units`. They are read from Windows, in the app
  and headless alike. A host or a test can install its own.
- `get(0, …)` reaches the root. `ishandle(0)` stays false, because `gobjects` hands out zeros as
  blanks.

### `uipanel`

R2025b's 38 names with its words, coercions, refusals and warnings, in both kinds of figure. A
panel made in a `uifigure` starts with that kind's defaults.

The **inner area** is what children and axes are placed in and clipped to. Measured in both kinds:
the border takes its width from every edge (twice that when etched, nothing when `none`), and a
title takes its font's pixel size, rounded, from the edge it sits on in place of the border there,
whichever is larger. The model computes it (`UiPanelModel.Insets`), the frame carries it, and
`InnerPosition`, `getpixelposition`, the layout and the renderer all use that one answer.

### Frames, the layout, and who draws what

- A `UiFrame` is now the container tree. `UiLayout.Compute(frame, size)` gives every panel its
  box, inner area, clip and effective visibility (its own and every ancestor's), and every control
  its box, clip and the panels stacked over it.
- **The renderer draws panels** (background, border, title) and the axes placed in them, clipped to
  the inner area, over the figure's own axes — MATLAB stacks every component above every axes. It
  reads the figure's last frame where a host delivers frames, so the interface thread never reads
  components a script is writing, and takes one from the model headless.
- **The component layer** places controls from the same layout. It is still one flat canvas: each
  control is clipped to what its containers leave it, and cut away where a panel stacked over it
  crosses it. That gives what nested clipping panels would, without a second tree to keep in step.
- A window resize lays the last frame out again, for both.

### `uifigure`

- A figure with `IsUiFigure`, R2025b's defaults (grey background, `AutoResizeChildren` on,
  `NumberTitle` off, no toolbar) and a plain window: no plotting toolbar, status bar, plot browser
  or inspector.
- It has **no figure number**. It is registered under a key of a hidden range
  (`JG.FirstHiddenNumber`), because windows, flushes and touch stamps are kept by number; no number
  names it, its handle is minted like any object's, `Number` is empty, and `figure` goes on
  numbering from 1. This is in place of re-keying the window service by handle.
- Its `HandleVisibility` is `'off'`, so it is not in the root's `Children`, is never `gcf`, and
  `close all` leaves it.

### `HandleVisibility`, three states

`on`, `callback` (seen only while a callback runs) and `off`; the root's `ShowHiddenHandles` shows
everything. `Children`, `findobj`, `gcf`, `clf` and `close all` honour it; `allchild` and `findall`
do not. A figure whose handle is hidden cannot be current: `gcf` makes a new one, and a verb aimed
at an axes of such a figure no longer leaves it current or conjures Figure 1.

### The tree, as a script sees it

- `Children` lists every component before every axes, newest first within each. U1's ADR recorded
  this as a divergence; R2025b does the same (`u2_tree`).
- `uistack` and `set(h, 'Children', …)` move within a kind.
- `findobj` and `findall` answer level by level, the starting object first; with no handle they
  start at the root, which is itself found. A hidden handle met on the way down is passed over with
  everything under it. `-property` is understood.
- `ancestor` starts at the object itself.
- A new `Parent` puts a child at the front of its kind and keeps its `Position` numbers.
- `DeleteFcn` runs for the object first, then for its children in `Children` order.
- `allchild` is new.

### Placement verbs

`getpixelposition`, `setpixelposition`, `movegui` and `uistack`, with R2025b's argument forms,
identifiers and messages. `movegui` works on the whole window: with a window, its real bounds;
without one, R2025b's own estimate (8 pixels of border, 23 of title bar, 27 of toolbar), so a
headless run lands where R2025b's does.

### A resize

What a change of size tells a script, as R2025b does it with a window on screen (`u2w_resize`). It
is settled on the script thread (`JgsContainerResize`):

- a figure whose size changed runs its `SizeChangedFcn`, once per change. A move does not, and the
  same size again does not. A script's own `Position` write counts, once the window has taken it;
- a container whose pixel size changed runs its own: because its figure was resized and it is
  placed in normalized units, because a script wrote its `Position`, and once when it is first
  shown. The figure's resize is looked at when the window's event is drained; a script's write and
  a first appearance are looked at when the figure's frame is flushed to its window;
- `AutoResizeChildren` silences the `SizeChangedFcn` of the figure or container it is on;
- children placed in normalized units follow by themselves, and children placed in absolute units
  stay where they are (see "Divergences" for what R2025b does when a container shrinks);
- the callback is handed R2025b's `matlab.ui.eventdata.SizeChangedData` (`EventName`
  `SizeChanged`), which retires the resize half of ADR 0198's "`[]` event data" divergence.

Headless none of this runs, as in R2025b, where a figure with no window resizes nothing and calls
nothing.

### A script's placement of a shown figure

A `Position` write to a figure that has a window is a request (`FigureModel.RequestPlacement`): it
carries the size asked for, and the window acts on it even when the corner did not move. Before,
the window acted only on a change of the corner, and read the size from a property its own layout
writes back — so `set(f, 'Position', [same corner, new size])` did nothing to a shown figure.
`OuterPosition` is answered from the bounds the window last reported on its own thread; it used to
read the window from the script thread, which WPF refuses.

### The document format

Decision Q9. Components are in the `.graph` document: `uicontrol` and `uipanel` as one
`UiComponentDto` with a `Kind`, panels nesting their own; an axes carries its `Units`, its pinned
rectangle and the path to its panel; a figure carries what makes it a `uifigure`. Additive, so no
format version changes. `copyobj` copies components through it, a panel with what is in it.
Callbacks are script-side state and are not saved, as with every other object.

## Measured

- **Headless probes** (`tools/matlab-checklist/ui-probes/u2/`): `u2_panel`, `u2_units`, `u2_tree`,
  `u2_uifigure`, `u2_more`. Findings are in the probes' README.
- **Parity fixtures**, recorded with `-noFigureWindows`: `u2_panel` (459 lines), `u2_units` (154)
  and `u2_tree` (156) pass, with one stamped divergence. `u1_uicontrol`'s `units_normalized` line
  is no longer a divergence.
- **Unit tests**: `UiContainerU2Tests` (the layout, clipping and occlusion, the renderer placing an
  axes in its panel and holding a pixel-placed one through a resize, both resize paths, the
  `uifigure`'s place among the figures, `copyobj`, and a two-monitor screen) and
  `UiComponentSerializationTests`.

- **The MATLAB window session, with the user's leave** (`u2w_resize`): R2025b opened a `uifigure`
  and a classic figure and resized them from the script; nothing was typed or clicked. It gave the
  resize rules above, and showed that the first draft's rule for `AutoResizeChildren` (scale in
  proportion) was wrong.
- **The JGraph window check, with the user's leave**: the Release app ran a script with a classic
  figure (a pixel panel of controls, a normalized panel holding an axes) and a `uifigure` (a panel
  and two buttons) under `-batch -showfigures`, driven through UI Automation patterns only
  (`InvokePattern`, `ValuePattern`). Every control sat where the layout puts it, to the pixel; a
  button running past its panel was cut at the panel's edge; the `uifigure` window was plain and
  grey; a script's `Position` write resized each window; the figure's and the normalized panel's
  `SizeChangedFcn` ran, the pixel panel's and the `uifigure`'s did not, and pixel-placed children
  stayed put. The process was stopped, not closed, and `workspace.json` was unchanged. The check
  found three faults, all fixed: `OuterPosition` read the window from the script thread; a
  `Position` write with an unmoved corner did not reach the window; the theme painted over a
  `uifigure`'s grey.

## Not measured

- No real keystroke or mouse click was sent to either application (memory "never synthesize input
  blind"). A person dragging a window's edge goes through the same event the script's write does.
- The IDE path (Run in the editor) was not driven in a window; it shares the session, the queue,
  the frames and the window with the batch path that was.
- R2025b's look of a panel was not compared pixel for pixel; fixtures pin its rectangles.

## Divergences

- **`AutoResizeChildren` does not reflow children.** With a window, R2025b leaves the children of
  a growing container where they are, and when it shrinks moves and sizes those placed in pixels
  by a rule that depends on the sizes it has passed through (`u2w_resize`: back at its first size,
  a child was not back at its first position). JGraph leaves pixel-placed children where they are
  in both directions.
- **A container's `SizeChangedFcn` runs once where R2025b runs it several times.** Shown for the
  first time R2025b called a panel's two to five times, and on a figure resize once before the
  figure's own and twice after; JGraph calls each once, the figure's first.

- **`allchild` and `findall` do not report R2025b's `scribeOverlay`.** R2025b keeps an annotation
  pane beside every set of axes and lists it; JGraph has none (`u2_tree`,
  `allchild_count_with_furniture`).
- **`Scrollable` is kept and nothing scrolls.** R2025b shows scroll bars in a `uifigure` container
  whose children overflow it. It arrives with the grid (U5).
- **A component cannot be left with no parent.** R2025b takes `Parent = []` and keeps the object;
  JGraph refuses, because a handle's life is its place in a figure.
- **A `uifigure` cannot be given a figure number.** R2025b's `IntegerHandle 'on'` numbers it;
  JGraph refuses. A classic figure whose `IntegerHandle` is turned off keeps its slot — the next
  `figure` does not reuse its number, and the number still names it, so that a script's variables
  holding it stay good.
- **A panel's `Layout` is empty.** R2025b answers a `LayoutOptions` object; it arrives with
  `uigridlayout` (U5).
- **`exportgraphics`, `print` and `saveas` draw panels.** R2025b omits every UI component with a
  warning, or refuses the figure. The export rules are stage U4's.
- **A figure's `Position` cannot have a zero width or height.** R2025b keeps the zero; JGraph
  keeps one pixel.
- **`getpixelposition` of anything but a figure, a component or an axes is `[0 0 0 0]`.**

## Still open

- A classic figure's `Color` is painted over by the theme when the figure is shown (open item 40).
  A `uifigure` keeps its own.
- The whole suite has three failing audio tests that predate this stage (open item 39).
- `WindowStyle` still answers one word; modal figures arrive with the dialogs (U4).
- The seven `uicontrol` styles U1 did not draw were labels until U3 (ADR 0200).
