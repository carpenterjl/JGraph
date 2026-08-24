# 0084 — The dialogs, the printer, and uiaxes

Date: 2026-08-24 · Milestone: M84 · Status: accepted

## Context

Six names stood on the graphics exclusion list as one group: `printdlg`, `printpreview`,
`pagesetupdlg`, `exportsetupdlg`, `exportapp` and `uiaxes`, excluded because they are *app building —
the same non-goal as the `ui*` family*.

That reason was true when it was written and had quietly stopped being true:

- **M71 built `uicontextmenu` and `uimenu`** for the callback seam. The `ui*` family the exclusion
  points at is no longer wholly a non-goal.
- **M75 made every `Paper*` property real** — `PaperType`, `PaperSize`, `PaperOrientation`,
  `PaperPosition`, `PaperPositionMode`, `PaperUnits` — and wrote in that file's own header that they
  had waited for `print` and `saveas` rather than *describing a page nothing ever printed on*. And
  then nothing printed on one. A whole page geometry existed that no person could see.
- **M80 put a strip of buttons over an axes**, and put it in `JGraph.Controls` precisely so that no
  export could carry it.

So these six describe a figure this build already has: a page it can lay out, a window it can show,
and an axes with one more colour on it than a plain one. The argument is the one the coverage
document already makes twice — for `refreshdata` in M77 and for `axtoolbar` in M80 — and this is its
third and largest application: **an exclusion is a decision, and a decision whose grounds have gone
is not a decision any more.** Where the first two took a name off, this takes a group.

## Decisions taken before any code

1. **Five of the six want a window, and each asks the host for one.** `IScriptFigureFiles` gains five
   members with **default implementations that answer false** — so a host written before M84 goes on
   compiling, a batch run gets the right behaviour without being taught it, and the WPF host is the
   only place that overrides. The verb turns the false into a refusal that names the non-interactive
   verb which does the job. That is M60's fourth answer for a verb that wants a window, and it is what
   keeps `jgraph.exe -batch` and the 56-script stress gate free of a modal dialog nobody could dismiss.

2. **The refusal asks about the window, not about the host's file services.** The first draft went
   through `RequireFigureFiles` and said "printdlg is not supported by this host" in a host that
   merely had no file services. A verb's problem here is the window; a host without file services also
   has no window, so both give the same answer and it is the true one.

3. **The print job reuses `FigureExporter`.** The figure is rendered at the printer's own resolution
   and placed on the page its `Paper*` properties describe, which makes *the printed page is the same
   picture `print -dpng` writes* a property a test can check without owning a printer. No new
   rendering path was written for printing, and none should be.

4. **`exportapp` goes through the control, and it is the only thing that does.** M80 kept the axes
   toolbar out of `FigureRenderer` so that no export could carry it. `exportapp`'s whole point is a
   picture of the *application* — so it is the one verb that must render the window, chrome and all,
   and the one with no non-interactive spelling at all. The two decisions are the same decision read
   from both ends.

5. **`uiaxes` is an axes with the app-building defaults, and its `Type` is `'axes'`** — which is
   MATLAB's own answer. MATLAB documents `matlab.ui.control.UIAxes` as its own class differing from
   `Axes` by exactly one property, measured against the CSV: **148 names, which is Axes' 147 plus
   `BackgroundColor`.** No new object was needed for one property.

6. **`BackgroundColor` is served on every axes, and unset by default.** The first draft gated it on a
   `MadeByUiAxes` marker so a plain axes would not grow a name MATLAB never gave it. Two things killed
   that: a property whose *reader throws* breaks `get(h)` wholesale, so the census of a plain axes
   stopped working; and the premise was already false — this table has always served the union, and a
   plain axes answers `RLim`, `ThetaLim` and `ThetaDir` today. What `uiaxes` gives the property is a
   **default**, not the property itself.

7. **The export preset is consulted only where the caller said nothing.** A preset that overrode an
   explicit `'Resolution'` would be action at a distance — a script's own argument losing to a dialog
   someone opened once — and it would be invisible in the script that suffered it. So the preset is
   read as the *default* of each option rather than applied over the answer, and the figure's
   background is put back after an export rather than left changed.

8. **The three dialogs are built in code rather than in XAML.** Each is a handful of rows over
   properties that already exist. The import wizard is in XAML because it has a live preview grid and
   a view model with real decisions in it; these read and write a page rectangle.

## Found by probing rather than by reading

- **`ax = uiaxes` bound the verb rather than making the axes.** `AutoCallsBare` again — the rule
  `bubblesize` wrote, `now` paid for in M64, `cm = uicontextmenu` in M71 and `nexttile` in M80. It
  only ever bites once a verb starts returning something, and every wave that adds one meets it.
- **A property whose reader throws breaks the whole census.** `fieldnames(get(gca))` on a plain axes
  stopped working the moment `BackgroundColor` refused there, because `get(h)` reads every name. This
  is what turned decision 6 around, and it is worth recording as a rule: a property in a shared table
  may refuse a *write*, but a reader that throws is not a refusal — it is an outage.

## Verification

- 0 warnings in Release and Debug; **5,226 tests**; **56 of 56 stress scripts**, including the new
  `stess_56.m`, which passed all eleven sections on its first run.
- **Properties 1,421 of 1,437 across 30 kinds → 1,569 of 1,585 across 31**, with `uiaxes` measured at
  **148/148**. Everything still unanswered is geographic, as it has been since M80.
- **Graphics functions 249 of 277 → 255**; the exclusion list **28 → 22**, and the paragraph's own
  arithmetic sentence was rewritten rather than patched — "seven plus eight plus six plus one is 22".
  `verify-builtin-coverage.py` caught both the 249 and the 919 before the prose was told, which is the
  third wave running in which it has.
- **Syntax forms 1,333 of 2,441 → 1,336 of 2,453.** The denominator grew by the six names' twelve
  documented forms, and only three of them are accepted: `uiaxes`' two probeable forms and nothing
  else. The five dialogs' eight probed forms read **`error`**, and that is the honest verdict — a
  prober with no window asks them for a window and is told there is none. Two of `uiaxes`' four are
  `Name,Value`, which this prober never probes.

## Divergences recorded

- **A `uiaxes` lives in an ordinary figure**, because this build has no `uifigure` and will not grow
  one for this. It is an axes with app-building defaults: the cell fill set to the figure's colour and
  the toolbar showing.
- **`BackgroundColor` reads on every axes**, not only on one `uiaxes` made. It is unset by default, so
  a plain axes draws exactly as it always has; what it answers when unset is the plot box's `Color`.
- **`exportapp` has no answer without a window at all.** Every other verb in this milestone names a
  non-interactive alternative; this one names `exportgraphics` and says plainly that it writes the
  figure *without* the window, which is a different picture.
**Not a divergence, and kept outside the list so the harvest does not lift it as one:** five of these
six are documented graphics functions, so the group is the largest single movement that table has had
since M60 — six names in one milestone against M80's two and M77's one.

## What is not done

- **The export preset is only reachable through `exportsetupdlg`**, so a headless script cannot set
  one. That is deliberate — it is a dialog's state — but it means the pixel proof that an export reads
  it needs a window, and `stess_56.m` proves the half it can: an explicit argument is obeyed and the
  figure is put back afterwards.
- **`printpreview` shows one page.** MATLAB's has a page navigator, a zoom and a set of margin
  handles; this shows the page the figure would print on with a button to print it and a button to
  change the setup.
- **No `uifigure`, and no other `ui*` control.** `uiaxes` came off the list because an axes is a thing
  this build has. A button, a slider and a grid layout are not, and the exclusion for those stands.
