# ADR 0241 — `image` sets the limits by hand, and what hgsave's tree has no place for: open items 138 and 139

## Status

Accepted, 2026-10-17. The twenty-eighth batch of the open-items work
(`docs/plans/open_task_chips_09_12_2026.md`, gitignored): items 138 and 139, both found in batch 27
(ADR 0240). The recordings are the probes in `tools/matlab-checklist/ui-probes/oi138/` and
`oi139/`, all headless, 2026-10-17: `oi138_image` (28 forms of the image family and what each does
to its axes), `oi138_auto` (30 cases of the automatic limits over an image, alone and beside other
objects), `oi139_shapes` (what R2025b's own `hgsave` writes for a colour bar's label, a legend's
title, an axes' subtitle and a polar axes' title, what it rebuilds from its own tree, and nine
hand-built forms), `oi139_helper` (the tree-only file the fixture reads) and `oi139_make` /
`oi139_verify` (a figure of capturing callbacks written here and run in R2025b).

## Context

ADR 0240 recorded that an image's axes read `[0 4.5]` by `[0 5]` in MATLAB from this build's tree
where R2025b's own `imagesc` sets `[0.5 4.5]` by hand (item 138), and that MATLAB's copy of a
`.fig` written here lacks a colour bar's label, a legend's title, an axes' subtitle, a polar axes'
title and labels, and any callback that is an anonymous function capturing variables (item 139).
Item 139 asked whether those have a place in hgsave's tree that the writer missed, and whether a
captured value could be written into the function's text when it is a literal.

## Decision

### 138: the image family sets the limits by hand

`image` and `imagesc` in their high-level forms, into an axes that is not holding, set the axes'
`XLim` and `YLim` to the **pixel edges** with the modes manual, as R2025b does (`oi138_image`): the
span the pixels cover, half a pixel beyond the first and last centre. With `n` centres from the
first to the last of `XData` the half pixel is `|last − first| / (n − 1) / 2` (`magic(4)` reads
`[0.5 4.5]`, `XData [2 5]` `[1.5 5.5]`, `[5 2]` the same, `[1 2 10]` `[−0.5 11.5]`: the ends alone
place the pixels, as this build already drew them); a lone number puts the centres one apart from
it (`image(3, 7, magic(4))` reads `[2.5 6.5]` by `[6.5 10.5]`); and **one column or row given two
distinct ends spans twice the distance between them** (`image([2 5], y, (1:4)')` reads `[−1 8]`),
which `ImagePlot.PixelExtent` now draws as well. `YDir` reverse, `Layer` top and the view from
above are set as before. The limits are set once: a `CData` written later does not move them (A23).
The low-level form (`image('CData', …)`), an axes holding (`hold on`, before or after a plot) and a
uiaxes (R2025b's `replacechildren`, A27) leave the limits automatic; an image of no pixels sets none
and asks nothing of the automatic limits (`image([])` reads `[0 1]`, A20). Both dialects: the
picture's own edges are not a MATLAB look.

**The automatic limits over an image are nothing special** (`oi138_auto`): an image alone reads
`[0.5 4.5]` after `xlim auto`, `axis tight` or `XLimMode` `auto` because the ordinary tick-aligned
rule (ADR 0231, 0238) over `[0.5 4.5]` lands on its own ends; beside a line to 4.7 it reads
`[0.5 5]`, to 7 `[0 7]`, to 12 `[0 12]`, from −2 `[−2 5]`, and **two images** (`[0.5 4.5]` and
`[5.5 9.5]`) read `[0 10]`, not their edges. This build's rule already gave every one of those, so
nothing changed there; `axis image` is `axis tight` and then the aspect (modes automatic after it,
A19), which it was not here.

### 139: what hgsave's tree has no place for

**R2025b's own `hgsave` writes no colour bar label, legend title, axes subtitle or polar axes title
into `hgS_070000`**, and reopens its own tree without them (`oi139_shapes`, Part A: the six trees
hold `SubtitleMode`, `TitleMode` and nothing else of them; the words `Heat`, `Series`,
`Below the main` and `Polar` are nowhere in the dumps; the reopened figures read them empty, the
axes' title `Main` and its x label back as before). Nine forms built by hand (Part B) are passed
over without a crash: a text child under the colour bar, with or without hgsave's `Description`;
`Label` as a char; a legend's `Title` as a char or as a text child; an axes' `Subtitle` as a char;
a polar axes' title as a text child named by `special` or as a char; a fifth place in an axes'
`special`. So the writer writes none of them - there is no form to write - and **the save's warning
names them** when they say something: `an axes' subtitle`, `a legend's title`, `a colour bar's
label`, `a polar axes' title`, `a polar axes' label` (the r label, a JGraph extra). The document
copy keeps them, so `openfig` here gives them back.

**A callback that captured literals is written with the literals in its place.** A captured value
that is a number, a logical, a complex, a char, a string scalar or a 2-D array of those with at most
256 elements is written into the function's text where the variable stood, in `mat2str`'s spelling
(a negative number in parentheses), so `k = 4; @(s, e) disp(k)` becomes
`feval(str2func('@(s, e) disp(4)'), gcbo, [])`, the command ADR 0240 writes for a capture-free one.
`AstPrinter.Print(function, literals)` does the writing: a parameter of the function or of a
nested one shadows the name (`@(s, e) arrayfun(@(k) disp(k), 1:k)` keeps the inner `k`), and a name
that is called, indexed, assigned or reached into in the body - where a literal cannot stand - makes
the callback not writable, so it is left out and named as before (`a callback capturing
variables`). A captured struct, cell, object or handle is left out the same way. A cell callback
holding an anonymous function is left out and now named for what it is (`an anonymous function in a
cell callback`; ADR 0240 had named it a capturing one). `oi139_verify` had R2025b open the file
`oi139_make` wrote here and run each: the number, the char with a quote in it, the logical, the
string, the matrix, the negative power and the shadowed name all ran to their values; the indexed
one had no callback.

**Found on the way:** `title(lg, 'words')` with a legend handle wrote the handle's number into the
current axes' title (`1E+06`) and `lg.Title` read an empty double; `title` now titles the legend and
`Title` reads and writes its words. R2025b's legend `Title` is a text object (item 141).

## Measured

- **`oi138_image`:** 28 forms (`imagesc`, `image`, two ends, a scalar, a 1-by-1, 3-by-5, decreasing
  ends, non-uniform ends, one column, true colour, the low-level form with and without `XData`, a
  trailing `clims`, a named axes, hold on over a plot, `xlim auto`, a plot added after with and
  without `axis auto`, `axis tight`, `axis image`, `image([])`, the modes set auto, vectors for
  `x` and `y`, `CData` changed after, an empty held axes, `imshow`, a uiaxes, a label added after),
  each with the limits, modes, `YDir`, `Layer`, the aspect ratios, the view and `CLim`.
- **`oi138_auto`:** 30 cases: a line inside, to 4.51, 4.55, 4.6, 4.7, 4.8, 4.9, 5, 5.2, 5.5, 6,
  6.5, 7, 9, 10, 12, from −2 and from 0.9; a line in y; a scatter, a text, a patch, a second image;
  an image with two ends alone; a line alone; `tight`, `padded` and `tickaligned` named.
- **`oi139_shapes`:** six hgsave trees dumped and reopened, nine hand-built trees; **`oi139_helper`:**
  the tree-only file of a titled, subtitled axes with a titled legend and an image axes with a
  labelled colour bar, reopened in R2025b with the title and x label and nothing else.
- **`oi139_verify`:** R2025b opening and running the seven callbacks of `jg_captures.fig`.
- **Unit tests:** `JgsImageLimitsTests` (the high-level forms, the forms that leave the limits
  automatic, the automatic rule beside a line and a second image); `FigFileWriteTests` (the
  literals in place, the shadowed name, the negative number, the struct, the indexed and the cell
  callback left out; the leftovers named and absent from the tree while the document keeps them).
- **Fixture `oi_image_limits_fig_leftovers`** (52 rows, 2 divergences): the 20 image forms and the
  13 automatic cases printed from both engines; each engine's own save and reopen keeping the
  subtitle, the legend's title and the colour bar's label; `helpers/oi139_matlab_tree.fig` (R2025b's
  hgsave stripped to its tree) read in both engines with the title and x label and without the
  three; the image axes' limits manual through both.

## Not measured

- **Any release but R2025b.** The tree's lack of the four is hgsave's in R2025b; an older release's
  hgsave was not run.
- **The pixel a one-column image is drawn over** in R2025b: only the limits were read headless.
  This build draws it over the limits' span.
- **`padded` over an image alone**, and a uiaxes' automatic limits past the one case.
- **MATLAB's window** on any of it.

## Consequences

- An image drawn here fills its box as R2025b's does, and a `.fig` written here reads the same
  limits in MATLAB (the ADR 0240 divergence on an image's axes is retired).
- A `.fig` written here carries a capturing callback whenever its captures are literals, which is
  what a GUIDE-shaped script captures (a count, a name, a table of numbers).
- A save names what MATLAB's form cannot carry, now the furniture words as well; a script that
  titles its legend or labels its colour bar sees one more warning per save.
- `title(lg, …)` titles the legend. `JgsBuiltins.MatrixText` (`mat2str`) is internal for the writer.
- Items 141 (a legend's `Title` as a text object) and 142 (`imshow` setting up its axes as
  R2025b's does: limits manual, `DataAspectRatio` manual, `CLim` manual, `Visible` off, where this
  build reads `[0 4]` automatic and visible, A26) are filed.

## Divergences

- ~~**A legend's `Title` is its words here, a text object in R2025b** (`matlab.graphics.illustration.legend.Text`;
  fixture row `rt_legend_title_class`; open item 141).~~ — **lifted; see
  [0242](0242-tags-legend-title-imshow.md).** It is that text object here too, and the fixture row is
  now `rt_legend_title_isa`, exact; its handle's `class` answers `'double'` as every handle's does (ADR 0051).
- **`savefig` warns here where R2025b saves silently** (`JGraph:savefig:NotInMatlabCopy`; fixture row
  `rt_save_warning`): this build names what MATLAB's form of the file leaves out, which R2025b's
  own `hgsave` leaves out without a word.
- **MATLAB's copy of a `.fig` written here lacks a colour bar's label, a legend's title, an axes'
  subtitle, a polar axes' title and r label** - hgsave's tree has no place for them, and R2025b's
  own hgsave loses them the same way - **and a callback whose captures are not literals or are
  indexed, called or reached into** (narrowing ADR 0240's capturing-callback bullet); the document
  copy keeps them all.
- **An image into a uiaxes sets `Layer` top here**, as into any axes; R2025b leaves a uiaxes' `Layer`
  bottom (A27). The limits stay automatic in both.

## Still open

- A legend's `Title` as a text object (item 141); `imshow`'s axes (item 142); the chart kinds in the
  reader's builder (76); `plot`'s options and the primitives' group parent (136, 137); the tags the
  document drops (140).
