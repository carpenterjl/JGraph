# 0075 — The figure's own properties, and where an axes sits

Date: 2026-08-21 · Milestone: M75 · Status: accepted

## Context

Two gaps were left after the axes waves. A figure answered to twenty-four of MATLAB's sixty-six
property names — the largest single hole in the property table — and an axes answered to a hundred
and thirty-nine of a hundred and forty-seven, missing the five of the layout family. The two belong
together: `InnerPosition`, `OuterPosition` and `TightInset` are answers about pixels the renderer
chose, and the renderer draws into a figure whose size and page are the figure's own properties.

Measured after this wave: figure **24 → 66/66**, axes **139 → 144/147** (only `Interactions`,
`Toolbar` and `Layout` remain, unanswered by the decision ADR 0073 recorded), polaraxes **82 → 87**,
properties overall **878 → 930 of 1,394**.

## Position means the plot box, in both dialects

This is the one behaviour change in the wave, and it was a user decision taken explicitly.

MATLAB's axes `Position` is the **inner** rectangle — the plot box, without the margins the ticks
and labels claim — measured in fractions of the figure with **Y counting up from the bottom**.
JGraph's was the outer cell with Y counting down from the top, because that is what
`AxesModel.NormalizedBounds` holds and the property was a thin wrapper over it. Both dialects now
answer MATLAB's rectangle. The JGS dialect surface is otherwise frozen; this is an authorized
exception, taken because two dialects disagreeing about what the commonest layout property means
would be worse than one of them changing once.

The model keeps its own convention. `NormalizedBounds` is still the outer cell with Y downward —
subplot identity, `tiledlayout`, the document format and the renderer all read it, and none of them
had to change. The flip happens once, in both directions, in the property layer.

Four rectangles, and the arithmetic that ties them:

- **`Position` and `InnerPosition`** are two names for the plot box, as they are in MATLAB. Reading
  either takes the plot area the renderer measured; writing either pins it.
- **`OuterPosition`** is the cell. While nothing is pinned it is `NormalizedBounds` exactly — no
  measurement, and none of a measurement's error.
- **`TightInset`** is read-only, because it is a measurement: the part of the margins the ticks,
  labels and titles claimed. It excludes the colorbar and the rulers `yyaxis` stands outside the
  right edge, which is what MATLAB's excludes too, and is why `DecorationMetrics` now carries what it
  reserved for those separately from what the text cost.
- **`PositionConstraint`** says which of the first two a placement fixes. Writing `Position` moves it
  to `'innerposition'`, which is MATLAB's own rule.

`outer = inner + inset` holds to nine decimals, and a section of `stess_47` asserts exactly that.

### The channel the three read through

Three of the four are answers about a drawing, and only the renderer knows them. `AxesModel` gained
`LastLayout`, an `AxesLayoutSnapshot` the renderer files after each frame: the cell it was given, the
plot box it ended with, the inset between them, and the canvas those are fractions of. It is a
report, never an instruction — writing it back into the model would make each frame depend on the
last one. It is deliberately silent and never serialized.

A pinned plot box is measured against itself and then inflated, because the margins stand between
the two rectangles and neither is known before the other. One pass is enough: measuring against the
inner rectangle costs a few pixels on the tick lengths a ruler states as a fraction, and buys a plot
box that lands exactly where it was asked for.

Before the first frame there is nothing to report, and an axes still has to answer. `AxesLayoutSnapshot.Estimate`
does the same arithmetic with a guess at the text sizes — the estimate `LayoutEngine` already had,
moved into the core so both callers read one description of it rather than two.

## The figure's forty-two names

They divide into six groups, and only the last is answered rather than implemented.

- **The window.** `Position` and `InnerPosition` carry where the window is as well as how big — the
  x and y that were always zero are real. `WindowState`, `Resize`, `ToolBar`, `Pointer` and
  `NumberTitle` really drive the WPF window through `FigureWindowBinding`, and the traffic runs both
  ways: a script that maximizes the window maximizes it, and a person who maximizes it sets
  `WindowState`. Both directions pass one echo guard.
- **The page.** `PaperUnits`, `PaperType`, `PaperSize`, `PaperOrientation`, `PaperPosition` and
  `PaperPositionMode`, held in inches and reported in whatever units were asked for, so changing the
  units changes the numbers without moving the page.
- **The events.** Eight callback slots over the six new `GraphicsEventKind` members, plus
  `ResizeFcn` as a second name over the `SizeChangedFcn` slot — which is what MATLAB's "not
  recommended, use SizeChangedFcn" amounts to. `CurrentPoint`, `CurrentCharacter` and
  `SelectionType` are the state those callbacks read.
- **The maps.** Figure-level `Colormap` and `Alphamap` are what an axes that never chose its own
  falls back on, which is where MATLAB keeps them; `AxesModel.ResolveColormap` and
  `ResolveAlphamap` are the read-through, and `colormap(fig, map)` reaches every axes at once.
- **`NextPlot`**, with all four words real, consulted at the one seam every drawing verb passes.
- **The truths.** `Renderer`, `RendererMode`, `WindowStyle`, `MenuBar`, `DockControls`,
  `IntegerHandle`, `Units` and `Clipping` each answer what is actually so and refuse to be told
  otherwise. A property that lies is worse than one that says no.

## Print, saveas, and the page state's only consumers

The Paper family would have described a page nothing printed on, so it landed with the two verbs
that use it. `saveas` is a new name in both dialects and purely additive. `print` means two different
things: JGS has always used it for the console, and its own scripts are written against that, so the
paper verb replaces it **only under the MATLAB dialect**, where a script writing to the console says
`fprintf` and `print` can only mean the printer.

An automatic paper position is the size the figure is on screen at ninety-six pixels to the inch,
which makes an unconfigured `print` produce what `exportgraphics` would; a manual one is the
rectangle a script chose. `-rN` and `exportgraphics`' `Resolution` both become a scale of `N/96` —
the second of those had been read for its spelling since M52 and acted on by nothing, which is the
defect class this project keeps finding and is the reason every visual property in this wave was
proved by pixels before it was written down.

## What the wave found on the way

- The alpha cache stamps keyed on the axes' own `Alphamap` rather than the resolved one, so a
  figure-level alphamap changed nothing: the lookup saw it, the cache did not, and the old palette
  was handed back. Caught by `stess_47` §18 reporting zero moved pixels — the M73 `_builtLogColor`
  lesson arriving a third time, and the reason the pixel proof is worth its cost.
- `IScriptFigureFiles.Export` had no way to say how big or how finely to draw, which is why
  `Resolution` was inert. It takes a scale and a size now, and both implementations pass them on.
- `InvertHardcopy` has to put the figure's colour back before it returns. Exporting is not a change
  to the figure, and a script that reads `Color` afterwards must see what it set.
- **A figure's size is the size a script asked for only until the window lays out.** The control
  writes the viewport's own size back into the model on its first arrange, so a window binding that
  read `Size` after being loaded read the size the window already was, resized itself to exactly
  that, and looked for all the world as though nothing had been wired at all. The binding is
  therefore built in the window's constructor and captures the requested rectangle before the window
  is shown. Found only by measuring a real window from outside the process — three rounds of reading
  the code had each produced a plausible and wrong explanation.

## Verification

4,922 tests (39 new in `MatlabM75FigurePropertyTests`, 10 in `WindowEventCallbackTests`, 3 in the
serialization suite), 47/47 stress scripts including the new `stess_47.m`, all four verifiers OK, a
zero-warning Release build, and the property probe re-measured: 930 of 1,394 across 28 kinds. Every
visual property was proved by pixels — a pinned plot box 27,113 changed pixels, `InvertHardcopy`
110,235, `GraphicsSmoothing` 29,180, a figure colormap 121,611, a figure alphamap 119,331 — and
`print` at 192 dots per inch produces exactly twice the pixels of the same figure at 96.

The frozen scripts were the real test of the `Position` pivot, and all forty-six pass unchanged.

The window wiring is the one part no headless test can reach, so it was measured from outside the
process: a figure asked for at `[200 200 500 380]` with `ToolBar` off opens a window whose drawable
area is 500 by 380, at x = 200 and with its bottom edge 200 above the bottom of the screen, titled
`Figure 1: M75 window smoke` — the number from `NumberTitle`, the name from `Name`, and the missing
toolbar visible in the chrome being twenty-six pixels shorter than the same window with it. A second
figure at `[340 300 300 200]` lands where and at what size it asked for too. `Pointer` and
`WindowState` are wired the same way through the same binding but were not separately measured.

### Recorded divergences

- **A figure window is bigger than its figure.** It carries a toolbar, a status bar and two side
  panels a MATLAB figure has not got, so `Position` sizes the window such that the *drawable area*
  comes to the figure's size, with the chrome measured at the moment rather than assumed.
- **`Position` is placed in device-independent units, not physical pixels.** On a display with a
  scaling factor the window lands where those units put it, which is not where MATLAB's pixels would.
- **Headless, `OuterPosition` equals `Position`.** With no window there is no border to add, and a
  batch run inventing one would be a worse answer than the honest one.
- **`InvertHardcopy` defaults off**, where MATLAB defaults on, so that what is exported is what was
  on screen. Turning it on gets MATLAB's behaviour.
- **`MenuBar` answers `'none'`** and refuses `'figure'`, where MATLAB defaults to `'figure'`. This
  window's menus live in its toolbar and its panels.
- **`Renderer`, `RendererMode`, `WindowStyle`, `DockControls`, `IntegerHandle`, figure `Units` and
  figure `Clipping` each answer one word and refuse every other.** Painters is the only renderer,
  figures are ordinary numbered windows, and a figure is measured in pixels.
- **Axes `Units` answers `'normalized'` and refuses every other.** An axes is placed in fractions of
  its figure and nothing here measures one in points or centimetres.
- **`print` with no file name is refused.** MATLAB sends it to the default printer; there is none to
  send it to, and quietly doing nothing would look like success.
- **Holding overrides the figure's `NextPlot`.** MATLAB's `hold off` sets the figure's `NextPlot` to
  `'replace'`, which here would turn the commonest line in any script into an instruction to wipe
  the figure. Reading hold as the override it plainly is gets the behaviour without the trap.
- **The wheel keeps zooming.** `WindowScrollWheelFcn` hears the turn; it does not take it over, so a
  figure with a wheel callback still zooms under the pointer.
- **`CurrentObject` is process-wide rather than per figure.** It answers what `gco` answers, because
  one pointer clicks one thing at a time.
- **A key press fires the figure's callback and the window's together.** With no uicontrols in this
  build a figure has the focus whenever its window does, so the two cannot be told apart.
- **`CurrentPoint` on a 2-D axes follows the pointer, and on a figure only a press or a move over
  the canvas.** A figure nobody has pointed at answers the origin rather than a stale reading.
- **An axes read before the first frame answers an estimate.** Nothing has been measured yet, and an
  estimate is closer to the truth than a refusal or a row of zeros.
- **Normalized rectangles are fractions of the figure's content area.** A figure title takes a strip
  off the top, and everything below is placed in what is left.
- **`PointerShapeCData` and `PointerShapeHotSpot` are read-only.** Nothing here draws a custom
  pointer, so they answer the all-transparent grid and the top-left hot spot MATLAB starts with.
- **`PaperType` accepts the twenty-five standard names and refuses the rest**, and a size no
  standard page has makes the type read back as custom rather than being rejected.

## What is not done

`Interactions`, `Toolbar` and `Layout` on an axes remain unanswered by the decision ADR 0073
recorded: each describes an editing surface JGraph already has, in the plot browser, the inspector
and the mode toolbar, and none of them is a MATLAB object this build could hand back. That leaves
axes at 144 of 147, which is the ceiling that decision set.
