# ADR 0223 — A script's colours survive the theme, Default and Factory values, a normalized text, a parentless component, and a visible uifigure as the current figure: open items 40, 44, 46, 48 and 55

## Status

Accepted, 2026-10-10. The tenth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), with ADRs 0221 and 0222. Recorded from R2025b (probes `probe_44`, `probe_44b`,
`probe_46`, `probe_48` and `probe_55` in the open-items scratch, then the fixture
`oi_defaults_places`); item 40 has no R2025b side, as R2025b has no theme to overwrite anything.

## Context

- **Item 40.** `Theme.Apply` wrote a figure's background, every axes' background and both title
  styles whenever a window showed a figure, so `figure('Color', [0.94 0.94 0.94])` — a GUI script's
  usual first line — was drawn on the theme's page, a GUIDE figure's saved grey and an `openfig`'d
  `Color` were lost, and `waitbar`'s plain 10-point title came out bold at the theme's size. Grid
  colours already carried a manual flag the theme respected.
- **Item 44.** The root had no `Default*` or `Factory*` names beyond a read-only answer for five
  classes, and `set(0, 'DefaultAxesFontSize', 14)` was refused. R2025b holds a default on the root, a
  figure or an axes, gives it to new objects made beneath it, reads it back through the parents, forgets
  it on `'remove'`, and takes `'factory'` and `'default'` as a value of any property.
- **Item 46.** A text's `'normalized'` meant a fraction of the figure; R2025b's is a fraction of the
  plot box, and changing `Units` moves `Position` into the new unit (normalized [0.5 0.5] is data
  [5 50] and pixels [201 151] in a 400-by-300 box over [0 10] by [0 100]).
- **Item 48.** `uibutton('Parent', [])` was refused; R2025b makes a component that belongs to nothing
  until a `Parent` write places it. `matlab.ui.layout.GridLayoutOptions` could not be constructed.
- **Item 55.** A `uifigure` was never the current figure. R2025b's one made with `HandleVisibility`
  `'on'` is `gcf` and the root's `CurrentFigure` at once, `gca` and a bare `plot` reach it, until
  `figure(f)` raises another; and `runStartupFcn` turns a `'callback'` figure `'on'` so that the
  startup function's `gcf` finds it.

## Decision

### A colour or title style a script chose is manual (item 40)

`FigureModel` and `AxesModel` carry `BackgroundManual` and `TitleStyleManual`, set where a script
writes a figure's or an axes' `Color`, a title's style through its text object or `sgtitle`, or an
axes' font size, name or title weight; the document keeps them. `Theme.Apply` leaves a manual
background or title style alone, as it leaves a manual grid colour. A `uifigure` kept its own grey
already (U2).

### Default and Factory values (item 44)

`JgsGraphicsProperties.Defaults.cs` takes over the five-class read the old code made in `Get`.
`Factory<Class><Prop>` reads what a fresh object of the class answers in the script's dialect;
`Default<Class><Prop>` is held by the root, a figure or an axes, and read up through the parents to
the factory value; an unknown class word is R2025b's `MATLAB:hgutils:InvalidClassName` and an unknown
property its `MATLAB:hg:InvalidProperty` with the class's short name. A value written as a default is
checked by writing it to a fresh object. New objects take the nearest defaults as they join their
parent — the model now announces that (`GraphObjectLifecycle.Adopted`) — before the creating call
applies its own options, and `figure` gives a new figure the root's. `'remove'` forgets a default,
and `'factory'` and `'default'` resolve as values. `get(h, 'Default')` is a struct of what the object
holds, `[]` for a figure or axes holding none and an empty struct for the root; `get(0, 'Factory')`
lists every writable property of the ten classes this build makes fresh. The root's defaults are
forgotten with the handle registry, at the start of each run.

### Normalized is a fraction of the plot box (item 46)

In an axes `'normalized'` is a device unit (`TextAnnotation.DeviceUnits`) whose reference is the plot
box, drawn and sized as a data label is; a text in a figure keeps the figure's fraction. A `Units`
write moves `Position` through the fraction of the plot box it stands at — the limits for data, logs
and reversals included, the plot box's pixel size for the device units, pixels counting from 1 —
while a `Units` given to `text` itself moves nothing, as x and y were given in it. A pixel position
is drawn counting from 1, as R2025b's are.

### No parent yet, and GridLayoutOptions (item 48)

A component, a panel, a grid or a uiaxes made with `'Parent', []` waits in a panel (a uiaxes in a
figure) that no figure holds and nothing draws, `JgsDetachedComponents`; its `Parent` reads `[]`, and
a `Parent` write moves it out as it moves any component. A dotted name nothing else claims may now
name a builtin registered under the whole name, which is how `matlab.ui.layout.GridLayoutOptions('Row',
2, 'Column', [1 2])` is made: the classed struct a `Layout` write already took, each of its two
checked as that write checks them, 1 when left out.

### A visible uifigure is current (item 55)

`uifigure` makes the figure current (`JG.Figure` of its hidden number) when its handle is visible
once the options are set, and `runStartupFcn` does when it turns a `'callback'` figure `'on'`; the
hidden-handle rule `gcf` already followed (U2) does the rest.

## Divergences

- **The factory values are this build's own**: a line's width 1.5 (decision D1), an axes' font 11, a
  text's 12, a figure white (`factory_line_width`, `factory_axes_font`).
- **A colour written is kept in 8 bits**, so a default figure colour of `[0.25 0.5 0.75]` reads
  `[0.251 0.502 0.749]` (`figure_colour_default`), as every colour here does.
- **R2025b's root starts holding three defaults** (`defaultFigureMenuBar`, `defaultFigurePosition`,
  the machine's own, and `defaultFigurePaperPositionMode`); this build's starts with none.
- **`DefaultLineColor` is held but given to no line**, since every line here is `plot`'s, whose colour
  comes from the `ColorOrder` (R2025b's `line` takes it).
- **A figure a plot makes for itself takes no `DefaultFigure*`**; only `figure` gives them.
- **A bad default value is refused in this build's words** (`LineWidth expects a number`), where R2025b
  says `MATLAB:datatypes:invalidPositive`; and `get(0, 'Factory')` covers ten classes, not R2025b's 2402 names.
- **A normalized or data text's z reads 0** where R2025b's reads 7.1e-15 after a conversion.
- **A detached component's `Parent` is `[]`, a double**, where R2025b's is an empty
  `GraphicsPlaceholder`; this build's placeholders are numbers (`gobjects`).
- **A component cannot be detached by writing `Parent = []`**, and a menu or toolbar cannot be made
  without a parent (ADRs 0199 and 0206, unchanged).

## Consequences

A classic GUI script keeps the grey it set; a GUIDE figure opens in its saved colour; `waitbar`'s
title is plain; `set(0, 'DefaultAxesFontSize', 12)` at the top of a script reaches every axes after
it; `text(ax, 0.05, 0.95, 'a)', 'Units', 'normalized')` stays in the corner whatever the limits; App
Designer's generated `createComponents` with `'Parent', []` and a later placement runs; an app's
startup function plots into its own figure. Tests: `OpenItemsBatch10Tests` (theme pass, document
round trip, a second run's root), `MatlabBubbleChartTests` rewritten to `bubblelim` and `bubblesize`;
fixtures `oi_defaults_places` (42 lines, 3 stamped) and `u7_app`'s `callback_seen_current` (now
exact). The divergences ADRs 0201, 0202 and 0204 recorded for these are lifted from them.

## Still open

- `Parent = []` on a placed component, and `uimenu('Parent', [])` (ADRs 0199, 0206).
- `DefaultLineColor` for `line`, and `DefaultFigure*` for a figure a plot makes.
