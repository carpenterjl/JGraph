# ADR 0283 — Zoom and view: the canvas's zoom, pan, Fit to View and Resizing Hints

## Status

Accepted, 2026-10-28. Stage W6 of the App Designer window plan, after arranging on the canvas (W4,
ADR 0282); it was the designer leftovers plan's C3 (open item 151, part 3). W5 comes later in the
plan's own order.

## Context

After W4 the canvas was drawn at the figure's own size only: VIEW > ZOOM (Fit to View, Zoom In, Zoom
Out, Reset Zoom) and DISPLAY's Resizing Hints were shown disabled with their stage, and the canvas
had no way to pan but its scroll bars. R2025b's App Designer zooms the design area in steps, fits it
to the view, pans it with Space held or the middle button, shows a floating zoom panel and, with
Resizing Hints on, the size of what is being resized. W6 builds them.

R2025b's client was read for every rule (`toolbox/matlab/appdesigner/web/release`):
`appdesigner/view/zoom/ZoomConstants`, `ZoomHandler`, `CanvasZoomStrategy`, `appdesigner/util/ZoomUtil`,
`ZoomAction`, `AppZoomViewModel`, `DesignAreaView`, `DesignAreaViewController`,
`appdesigner/view/keyboard/DesignViewKeyboardHandler`, `CanvasPanPlugin` and the canvas pan service,
`hmishared/floatingzoompanel/ZoomPanel` and its template and style rules, `ZoomMenuItemProvider`,
`FigureMenuItemsProvider`, `SizeDisplayController` and the gestures that show it, and the Toolstrip
catalog's words (`panTooltip`, `FloatingZoomButtons`).

## Decision

- **The steps** (`DesignZoom`, UI-free) are `ZOOM_STEPS`: 0.5, 0.55, 0.6, 0.7, 0.8, 0.9, 1, 1.12,
  1.25, 1.37, 1.5, 1.62, 1.75, 1.87, 2, 2.12, 2.5, 2.75, 3, 3.5 and 4. Zoom In and Zoom Out take the
  level to its hundredth and go a step (`calculateZoomLevel`); from below the steps the first, from
  past them the last. Zoom In is disabled at 4, Zoom Out at 0.5 and Reset Zoom at 1 (`ZoomAction`).
- **Fit to View** (`calculateFitToViewZoomLevel`, `fitAppToView`): the level at which the figure and
  30 pixels round it fill the view across or down, whichever is less, then the step below the
  nearest step to it - the level itself when that is the first step or it is past the steps; the
  figure centred in the view.
- **Where a zoom is about**: the view's centre for the toolstrip, the keys and the floating panel;
  the pointer for Ctrl+wheel, up to zoom in (`CanvasZoomStrategy`, `ZoomHandler._mouseWheelHandler`).
  The canvas is scaled whole - the figure, its components and every mark - so the gestures' points
  stay the surface's; the figure's edges, the handles and the guides scale with it, as theirs do in
  R2025b's transformed page.
- **The keys**: Ctrl+Plus and Ctrl+Minus (with Shift, and the keypad's) and Ctrl+Alt+0 wherever the
  window has the keyboard, as `ZoomHandler` listens on the whole page, doing something only in the
  Design View; Space on the canvas: a tap (let go within 200 ms) fits the view, a hold pans until it is
  let go (`DesignViewKeyboardHandler`).
- **Pan**: a drag with Space held or with the middle button scrolls the canvas (`CanvasPanPlugin`),
  the pointer a hand.
- **The floating zoom panel** (`hmishared/floatingzoompanel/ZoomPanel` as `DesignAreaView` makes it:
  no fit button, no zoom icons): at the canvas's bottom-right, 25 pixels in, a magnifier that opens
  it to the percentage, "-", "+" and "Reset"; a press on the canvas closes it. The percentage is the
  level in hundredths, rounded (`updateZoomLevel`).
- **The pan message**: "Hold **Space** and drag to pan" over the panel for 2 seconds after a zoom, the
  first five zooms of a session, and never again once Space has been held (`PanMessageCount`).
- **The canvas's menu** gains `ZoomMenuItemProvider`'s Zoom submenu (Zoom In, Zoom Out, Reset Zoom,
  Fit to View, each enabled as its action is) after Delete, and, with the figure selected,
  `FigureMenuItemsProvider`'s check items Grid, Snap to Grid, Alignment Hints and Resizing Hints,
  which turn their settings on and off.
- **Resizing Hints** (`DESIGN_VIEW_DISPLAY_SIZE`, the setting
  `matlab.appdesigner.designview.ShowResizingHints`, off in R2025b's factory settings): while a
  component is resized or drawn or the figure's edge is dragged, the width along the top edge - the
  bottom for a resize from the top and for the figure - and the height along the left edge - the
  right for a resize from a left corner and for the figure's bottom-right corner - in whole pixels
  of the design (`SizeDisplayController`).
- **The gridifier reads the zoom**: Apply Grid Layout and Apply Auto Tab Order pass the canvas's
  zoom to DS4's `DesignGridify`, as R2025b reads its boxes off the zoomed page
  (`DesignDocument.CanvasZoom`).
- **The window**: the zoom actions are commands of the window, which VIEW > ZOOM and the keys reach;
  Resizing Hints is a setting like the other DISPLAY toggles. `DesignActions` runs 79 of the 202
  actions. Each app's canvas has its own zoom.

## Consequences

- W9's Code View zoom (the rich-text editor's, `ZoomFactorSettingProvider`) takes the same keys in
  the Code View.
- W5's menus and toolbars on the canvas are drawn inside the zoomed canvas.

## Measured

- **Tests**: `AppDesignerZoomTests` (5): the steps, Zoom In and Out from steps, ends, between steps and
  past them, the hundredth, what is enabled, the percentage; Fit to View's step below the nearest,
  the first step and past the steps; the canvas menu's Zoom submenu and requests, the figure's
  display check items and their toggles, Zoom In disabled at 4, a component's menu; Resizing Hints'
  size while a component is resized and the figure's edge dragged, nothing with the setting off; the
  gridifier at a zoom. `AppDesignerArrangeTests`, `AppDesignerGridTests` and
  `AppDesignerToolstripTests` follow the menu's Zoom and the live actions. With the designer, App
  Designer, toolstrip and settings suites: 787 pass.
- **The window check** (`run-w6designer.ps1`, UI Automation patterns, JGraph.Application in Release
  opening `w4/W4App.m`): on the VIEW tab Fit to View, Zoom In and Zoom Out enabled and Reset Zoom
  disabled at 100%, Resizing Hints enabled and off; the floating panel opened from its magnifier at
  100%; Zoom In to 112% with the pan message showing and Reset enabled; Zoom Out twice to 90%; Reset
  Zoom to 100%; the panel's + and Reset; Fit to View (70% for that window, the figure centred); Zoom In
  stopping at 400%, disabled there; Resizing Hints turned on and kept by R2025b's name. All 34 checks
  pass. W2's `run-w2designer.ps1` (39, its Resizing Hints line now enabled), W3's (43) and W4's (23)
  still pass.

## Not measured

- **Ctrl+wheel, Space's tap and hold, the middle button's pan, the zoom keys, the canvas's menu and a
  resize showing its size, in the window**: they take a mouse or keys, which the checks do not
  synthesize.
- **The look of the floating panel and the size readout against R2025b's**: no picture of R2025b's
  canvas was taken; both follow its templates and style rules.

## Divergences

- **Zoom In and Zoom Out from a level between two steps** (only Fit to View gives one) go to the
  next step up or down: R2025b finds no step for it and goes to the first step, 50%, either way; its
  keys also zoom when the action is disabled, so Ctrl+Minus below 50% goes up to 50%.
- **A pan stops at the canvas's edges**: R2025b moves the figure within its page so that it can be
  panned past them (`CanvasPanService`).
- **After Fit to View the figure is centred by a margin until the next zoom**: R2025b keeps the
  figure's place on its page.
- **The floating panel closes on a press on the canvas**: R2025b's closes on a click anywhere on its
  page. Its magnifier is a glyph, not R2025b's icon.
- **The resizing readout is JGraph's own drawing** (a line with end ticks and a numbered label), in
  R2025b's places; a grid child's span resize shows none.
- **The zoom keys do nothing in the Code View** until W9 gives its editor a zoom.
