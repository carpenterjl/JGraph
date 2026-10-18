# ADR 0242 — Tags through this build's document, a legend's title as a text object, and `imshow`'s axes: open items 140, 141 and 142

## Status

Accepted, 2026-10-18. The twenty-ninth batch of the open-items work
(`docs/plans/open_task_chips_09_12_2026.md`, gitignored): item 140, found in batch 27 (ADR 0240),
and items 141 and 142, found in batch 28 (ADR 0241). The recordings are the probes in
`tools/matlab-checklist/ui-probes/oi140/`, `oi141/` and `oi142/`, all headless, 2026-10-17 and 18:
`oi140_tags` (23 tagged objects through `savefig` and `openfig`), `oi141_title`, `oi141_font` and
`oi141_follow` (a legend's title: its class, properties, defaults, what it follows, its refusals),
`oi141_classes` (its rows for the three recorded class tables), `oi142_imshow` (50 forms of
`imshow` and what each does to its axes and figure) and `oi142_errors` (30 refusals and warnings,
and two exported pictures). R2025b's `imshow` is base MATLAB (`toolbox/matlab/images`), and its
`imshow.m`, `imageDisplayParsePVPairs.m`, `imageDisplayValidateParams.m`, `basicImageDisplay.m`,
`isSingleImageDefaultPos.m` and `initSize.m` were read; so was `observeFigureNextPlot.m`.

## Context

Item 74 (ADR 0217) gave a figure's, an axes' and a plot's `Tag` a place in this build's `.graph`
document. A text, an annotation, a legend, a colour bar and a light still came back from `savefig`
and `openfig` untagged, and a `rectangle` came back a `patch` (item 140). A legend's `Title` read as
its words (batch 28), where R2025b's is a text object of the legend's own (item 141). And `imshow`
in a MATLAB-dialect script drew through JGS's own image verb: automatic limits on the picture's
`[0 4]`, the axes visible, no aspect, no colour limits (item 142).

## Decision

### 140: tags and kinds through this build's document

The document now carries the `Tag` of every annotation (`AnnotationDto.Tag`: a text in an axes and
each figure annotation), of a legend, a colour bar and a light (`LegendDto.Tag`, `ColorbarDto.Tag`,
`LightDto.Tag`). A rectangle's box and curvature, which made a patch answer as a rectangle, moved
from a table on the scripting side into the model (`PatchPlot.RectanglePosition`,
`RectangleCurvature`) and into the document (`PatchPlotDto`), so a rectangle opens as the rectangle
it was, its `Position` and `Curvature` answering and reshaping it, a scalar curvature reading back
as written. All the new fields are optional: a document from before opens as it did. R2025b keeps
every tag and type through its own file (`oi140_tags`), and this build's `findall` now finds the
same objects by the same tags after the trip.

### 141: a legend's title is a text object

`lg.Title`, `get(lg, 'Title')` and `title(lg, …)` answer one object per legend, a
`JgsLegendTitle`, which is `matlab.graphics.illustration.legend.Text` to `isa` (its class row,
names, set words and members were recorded and appended to the three class tables; the members
recording had lost `ConstantRegion` and the root, which were rebuilt into it from the generated table
so the generator again reproduces it). It has R2025b's 21 properties in R2025b's order and nothing
else: no `Type`, `Tag`, `UserData` or `Position`, refused in R2025b's SetGet words, and `Position` and
`HorizontalAlignment` refused as not public.

- **What it follows** (`oi141_font`, `oi141_follow`): its size, angle, font, interpreter and colour
  are the legend's (`FontSize`, `FontAngle`, `FontName`, `Interpreter`, `TextColor`) until written,
  its weight is bold until written, and each has its mode. A colour may be `'none'`, which keeps the
  title's room and draws nothing. The values live on the legend model (`LegendModel.TitleFontSize`,
  `TitleBold`, … as nullable overrides, `ResolveTitleStyle`), so the document carries them.
- **Its words**: a row cell or a string array is a column cell of lines, a number its digits, a
  string scalar characters. `title(lg, words, Name, Value)` writes them and the options and answers
  the object; `title(lg)` is R2025b's `MATLAB:title:InvalidNumberOfInputs`.
- **Its life**: it is not among the legend's children (R2025b's `allchild(lg)` is empty), so no
  search finds it; the handle registry keeps it alive while its legend is there. `delete(lg.Title)`
  empties it and keeps the object, as R2025b's does. `legend('again')` keeps it; a legend deleted
  (`legend off`, `delete`, a replacing plot) takes it with it, and the legend made next has an empty
  title of its own.
- **It is drawn.** The legend renderer had never drawn a title: it now draws it centred over the rows
  in its own style, the box growing by its height and widening to it, its room kept while it is
  hidden (R2025b's box keeps it too, `oi141_font` F24).

### 142: `imshow` in a MATLAB-dialect script

MATLAB-dialect `imshow` is now R2025b's (`JgsBuiltins.MatlabImshow`); JGS keeps its own, which takes
JGraph's image values. The forms, the input parser and every refusal are R2025b's, word for word
(`oi142_errors`). The picture is made by the high-level `image` call, so on an axes not holding its
limits are the pixel edges set by hand (ADR 0241); a grey or binary picture is mapped through the
axes' colours, an indexed or true-colour one directly. Then the axes is set as `basicImageDisplay`
sets it: `YDir` reverse, `TickDir` out, no grids, `DataAspectRatio` `[1 1 1]`,
`PlotBoxAspectRatioMode` auto, `Visible` off; a grey picture's 256 greys go on its colormap and its
display range on `CLim` (the class's range by default - `[0 1]`, `[0 255]`, `[-32768 32767]`, … - the
data's for `[]`, the class's again for a constant picture); an indexed picture's map on its
colormap, its `CLim` left automatic. The image's `BusyAction` is `cancel` and `Interruptible` off, its
`CData` keeps its class. The title and the two labels are set visible, so they are drawn on the
invisible axes (`oi142_errors` P1; a plain axes made invisible draws neither, P2): each ruler now
holds its label's own visibility (`AxisModel.LabelVisible`), which the renderer and the document
honour, and a label's `Visible` writes it. A lone picture in a lone axes at the default place makes
the figure's `NextPlot` `'replacechildren'`, and with `'fit'` and `'tight'` the axes fills the
figure. `'Parent'` draws into that axes and leaves the current one alone; held, the limits stay
automatic and `Layer` bottom. The output is the image, and none is echoed.

**Found with it.** R2025b obeys a figure's `NextPlot` `'replace'` or `'replacechildren'` once and
sets it back to `'add'` (`observeFigureNextPlot.m`; B26); this build had kept the word, so every
plot after `imshow` would have cleared the figure. A replacing plot now gives back what `imshow` and
the image family set and R2025b's replacing `newplot` resets: `Visible` on, the data aspect free,
`Layer` bottom, both directions normal, the ticks' side automatic, the labels' visibility following
the axes. A true-colour image no longer sways the automatic `CLim` (B8 reads `[0 1]`). An image of no
pixels reads `XData` and `YData` 1-by-0 (B41). `image(imread(…))` in a MATLAB-dialect script is the
image call R2025b makes of the array, not `imshow`.

## Measured

- **`oi140_tags`:** 23 objects (figure, axes, line, text, rectangle, patch, surface, image, light,
  group, a line in it, scatter, bar, legend, colour bar, seven annotations, a uicontrol): their
  `findobj` and `findall` counts and types by tag before and after `savefig`/`openfig`, the
  rectangle's shape and the `UserData` after.
- **`oi141_title`, `oi141_font`, `oi141_follow`:** 55, 24 and 21 readings of a legend's title: class,
  `properties`, `get`, defaults, `title(lg, …)` and its options, the words' shapes, every refusal,
  what follows the legend before and after an update, the modes, `'none'`, display, delete, a legend
  made again, the box under one and two lines, `savefig`. **`oi141_classes`:** its rows for the class
  tables.
- **`oi142_imshow`:** 50 forms (each class, true colour, indexed, a display range given, `[]` and
  constant, every option, a named axes, a subplot, hold on, replaced, twice, `'fit'`, `'tight'`, a
  file, a figure with `NextPlot` `'new'`, a panel, a uiaxes), each with the figure's size and
  `NextPlot`, the axes' limits, directions, ticks, grids, aspects, visibility, `CLim`, colormap,
  position and layer, and the image's class, size, mapping, data and interpolation.
  **`oi142_errors`:** 30 refusals and warnings, and the two pictures.
- **Unit tests:** `FigureDocumentTagsTests` (every tag and type found again, the rectangle's shape,
  the title's style and a label's visibility through the file, an older document opening without
  them); `LegendTitleObjectTests` (the object, its words, what it follows, the refusals, its life,
  its drawing); `MatlabImshowTests` (the grey, colour and indexed forms, the options, the parent and
  hold, the figure's `NextPlot`, 27 refusals, what a replacing plot resets, the labels drawn, an
  image value given to `image`).
- **Fixture `oi_tags_legend_title_imshow`** (248 rows, 6 divergences): the tags and types through
  each engine's own file; the title object end to end; `imshow` over 32 forms, the placements, the
  parent, the warnings, what follows a plot, the figure's `NextPlot`, a file, and 27 refusals.
  **Fixture `oi_image_limits_fig_leftovers`:** its divergence row on the title's class is now an
  exact `isa` row.

## Not measured

- **Any release but R2025b**, and MATLAB's window on any of it.
- **A legend title's pixels.** The box's growth was read in pixels (`oi141_title` T47, `oi141_font`
  F24); this build lays out its own legend and draws the title by its own metrics.
- **`imshow`'s numeric magnifications and `Reduce`** past the refusals: the first sizes a window,
  the second subsamples a large TIFF; neither has an effect here.
- **An indexed PNG through `imshow(file)`**: this build reads it as true colour.

## Consequences

- A script that tags what it draws finds it again after `savefig` and `openfig` of this build's own
  file, rectangles included.
- A script that styles a legend's title (`lg.Title.FontWeight = 'normal'`, `title(lg, 'x', 'Color',
  'r')`) runs, and the title is drawn.
- `imshow` shows a picture as R2025b does and leaves its axes as R2025b's, so the script around it
  (`hold on; plot(…)`, `title`, `xlabel`, `colorbar`, the next `plot`) behaves as there.
- A figure's `'replace'` and `'replacechildren'` hold for one plot, in both dialects.
- Items 143 (annotations' types and their visibility to `findobj`), 144 (groups out of
  `findobj`'s reach) and 145 (`jet(n)` and the other colormap generators) are filed, and item 136
  names the makers found refusing `'Tag'` here.

## Divergences

- **`imshow` does not size the figure or move the axes** to show the picture at 100% (R2025b's
  `initSize`, from the screen's work area; fixture row `is_place_alone`). The axes keeps its place
  and the figure its size; the figure's `NextPlot` becomes `'replacechildren'` as there.
- **An annotation's `Type` is its kind here** (`textbox`, `rectangle`, `ellipse`, `line`, `arrow`,
  `doublearrow`, `textarrow`) where R2025b's say `…shape` (`textboxshape`, `doubleendarrowshape`, …),
  **and `findobj` reaches the annotations here**, which R2025b keeps behind a hidden pane for `findall`
  (rows `tag_shape_types_after`, `tag_shapes_findobj`; open item 143).
- **`lg.Title = 'words'` titles the legend here**; R2025b refuses the write as read-only
  (`MATLAB:class:SetProhibited`; row `lt_assign`). This build has always taken it.
- **A fresh legend title answers its settled values at once**; R2025b's first read of a fresh title
  answers the plain text defaults (10, `normal`) until the legend's next update (`oi141_font` F1).
- **A replacing plot keeps the axes' colormap and colour limits here**; R2025b's replacing `newplot`
  resets both. This build's `colormap(map)` writes the axes' own map, which a reset would lose.
- **`imshow` into a uiaxes sets the limits by hand here** (ADR 0241's uiaxes rule); R2025b leaves
  them automatic, reading `[0 5]` with the aspect manual (B50).
- **`imshow`'s `Reduce` and numeric `InitialMagnification` have no effect** (above).

## Still open

- Annotations' types and visibility to `findobj` (143); groups out of `findobj`'s reach (144);
  colormap generators (145); `plot`'s and the other makers' options (136); the primitives' group
  parent (137); chart kinds in the `.fig` reader (76).
