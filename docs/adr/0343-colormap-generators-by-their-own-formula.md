# ADR 0343 — Each colormap generator computes R2025b's rows, App Designer's tree holds them, and a 1-bit PNG reads back logical

## Status

Accepted, 2026-11-12. Open items 145 and 146, and part (b) of open item 273, taken together because
146 waits on 145 and 273 (b) was a short one beside them. Part (a) of 273, imread's speed, is a timing
item and is untouched.

## Context

**The generators.** `jet(8)` was jet's nine-stop drawing gradient resampled to eight rows and passed
through eight-bit colour. It came out as `[0 0 0.498; 0 0.0706 1; …]`. R2025b's `jet.m` builds three
overlapping ramps over the m rows asked for, so `jet(8)` is `[0 0 1; 0 0.5 1; 0 1 1; 0.5 1 0.5; …]`.
The generator's rows sit on its own breakpoints, and a table resampled to m rows cannot land there.
Every other generator had the same fault. Some had more:
- `prism`'s sixth colour is two-thirds red (`[2/3 0 1]`), not half;
- `colorcube` was a construction written to its description, not to `colorcube.m`;
- `white`, `sky`, `abyss` and `nebula` did not exist;
- `jet(0)` and the other zero-row calls were refused (R2025b answers a 0-by-3);
- with no size, a generator was always 256 rows. R2025b sizes it from the current figure's colormap
  (`matlab.graphics.internal.colormapheight`).

R2025b's sources were read in `toolbox\matlab\graphics\graphics\color`, and every generator was
probed at m = 0 to 13, 16, 17, 27, 63, 64, 65, 100, 255, 256 and 257 (probe `cmap_probe`, 17 digits a
value). There are four kinds:
- **A formula** in the .m file. These are `jet`, `hsv` (through `hsv2rgb`'s sectors), `hot`, `cool`,
  `spring`, `summer`, `autumn`, `winter`, `gray`, `bone`, `copper`, `pink`, `colorcube`, `prism`,
  `flag`, `white` and `lines` (the default colour order, repeated).
- **A 256-row table** read with `interp1(1:P, values, linspace(1, P, m), 'linear')`. These are
  `parula` (`parulaColormapValues`, a built-in), `turbo` (the table in `turbo.m`) and `nebula`
  (`nebulaColormapData.m`). The interpolation has to be spelled `(1 - t)*a + t*b` to match.
  `a + t*(b - a)` misses by an ulp in a few hundred places.
- **A built-in with no source**: `sky` and `abyss` (`blueColormapValues`). Their rows are linear in
  RGB over one blue. Spellings were compared with the built-in's output until one matched at every
  size: `f.*c + (1 - f)` for sky and `c.*f` for abyss, where `f = min(1, low + (k*(1 - low))/(m - 1))`
  with `low` 0.1 and 0.25. `linspace`'s put-back ends miss at m = 10, and the unclamped ramp misses
  at 27 and 100.
- **colorcube's colon.** `0:1/3:1` has to count its middle element down from the end, as R2025b's
  colon does (`1 - 1/3`, not `1/3 + 1/3`).

**App Designer's tree.** `DesignTreeWriter` listed any colormap other than parula as a problem and
left it out. App Designer names an axes' colormap by `isequal` of its rows against each of its 22 maps
(`inspector.internal.getColormapString`, `ColormapEditor.PREDEFINED_COLORMAPS`). So the rows must be
R2025b's to the bit, and until item 145 they were not. R2025b serialises an axes whose colormap is set
with two stored names (probe `cmap_tree`: `colormap(ax, 'hot')` and `ax.Colormap = hot(256)` store
the same):
- `ColormapMode`, the word 'manual';
- then `Colormap_I`, a 256-by-3 double.

**A logical PNG.** `imwrite(A > 100, p)` wrote an eight-bit PNG of 0 and 255. `imread` answered it as
a uint8 picture value, and `G > 0` was refused ("Operator '>' needs two numbers, but got image and
number"). R2025b's `writepng` writes a logical as a 1-bit greyscale PNG, and `imread` answers that as
a logical matrix. With `BitDepth` 1, any class writes its nonzero samples (probe `bd1_probe`).

## Decision

### R2025b's rows, by kind

`JGraph.Core.Drawing.ColormapRows` computes each R2025b generator's m rows as an m-by-3 double. It
works by its .m file's arithmetic, its 256-row table (`ColormapTables`, written from R2025b's
`map(256)` at the shortest round-tripping literal), or the probed ramp. The script generators
(`JgsBuiltins.Graphics3D` and `colorcube` in `.Appearance`) answer those rows. The checks on the size
are its `arguments` block's checks: scalar, then `mustBeInteger`, then `mustBeNonnegative`, with
R2025b's identifiers and messages. JGraph's own `viridis` still resamples its gradient.

`white`, `sky`, `abyss` and `nebula` are new generators. `colorcube` and those four are also new named
maps (`colormap('sky')`). Each one's drawing stops are its 256 rows. `prism`'s violet is now two-thirds
red in the drawing palette too.

### The figure's height

With no size, a generator is the current figure's map's length. This is the current axes' resolved map
when that axes is in the current figure (an unaddressed `colormap(map)` lands on the axes here, where
R2025b lands it on the figure), else the figure's own map, else 256. A named map is 256 rows long. A
table set by hand is its own length.

`Colormap` now keeps the rows a table was made from (`SourceRows`, set by `FromRows`). `colormap` with
no map reads back:
- those rows;
- for a named map, R2025b's 256 rows of it;
- for anything else, the gradient sampled at 256.

So `colormap(jet(5)); c = colormap` is `jet(5)`, and `colormap('hot'); c = colormap` is `hot(256)`.

### The tree

For an axes whose `AD_ColormapString` names one of App Designer's maps, the tree writer stores
`ColormapMode` 'manual' and `Colormap_I` as `ColormapRows.Of(name, 256)`. `lines` uses R2025b's own
colour order (`ColormapRows.MatlabLineColors`), because App Designer compares against R2025b's
`lines`, not this build's. A name outside the 22 is still listed as a problem.

### The 1-bit PNG

`ImageCodec.Write` writes a 1-bit greyscale PNG for a one-channel picture with no alpha when the
picture is logical, or when `BitDepth` is 1. A sample is on when it is above zero, which is writepng's
`~= 0` after its clamp. `ImageCodec.ReadWithAlpha` marks a 1-bit greyscale PNG's picture logical (Skia
decodes it to 0 and 255, so the samples are already 0 and 1). The MATLAB dialect's `imread` answers a
logical picture as a logical matrix (`ImageNumbers`). JGS keeps the picture value its image verbs take.
`imfinfo` reports such a file's `BitDepth` as 1.

## Consequences

- All 21 generators R2025b ships (`lines` aside) are bit-exact against R2025b at all 24 sizes probed.
  That is 534 rows of 17-digit values, of which only the `lines` rows and an unrelated `sprintf` quirk
  on empty input differ.
- Values a script reads from `jet(n)`, `hot(n)` and the rest change for every n, and so does any
  picture drawn through `colormap(jet(n))`. Named maps (`colormap('jet')`) draw as before: their
  drawing stops are unchanged, except prism's violet.
- New parity fixture `oi_colormap_generators` (383 rows from R2025b 25.2): each generator at 0 to 3,
  8, 9 and 10 rows in full, and sampled rows plus a sum at 27, 64, 100, 256 and 257. It also checks
  the size errors, the figure's height and the read-back.
- New parity fixture `oi_imread_logical_png` (15 rows).
- An app's axes colormap now reaches App Designer's tree. `AppDesignerTreeWriterTests` covers five
  maps, `lines` among them, and the bytes read back through the subsystem reader. Opening such an app
  in App Designer itself was not tried, because it needs a MATLAB window session.

## Divergences

- **`lines` repeats the classic colour order here and R2025b's newer palette there.** This is the
  divergence of ADR 0226 (open item 105). The fixture's `lines` rows are marked against it. The tree
  writer stores R2025b's palette, as App Designer needs.

## Still open

- **`colormap('hot')` after a 5-row map is set builds a 256-row hot here.** R2025b builds `hot(5)`, the
  current height. Named maps here are drawing gradients without a length.
- **Figure and axes `Colormap` property reads** (`f.Colormap`, `get(gca, 'Colormap')`) still answer a
  map's drawing stops (33 rows for parula) at eight bits. Only `colormap` with no map was moved to the
  rows above.
- **`prism` with no argument and no output recolours the axes' lines** in R2025b. Here it answers the
  table.
- **imwrite leftovers**, seen while probing and not imread's: `BitDepth` 2 and 4 are ignored (R2025b
  quantises and reads back scaled uint8). A logical written with `BitDepth` 8 is 0 and 1 in R2025b and
  0 and 255 here. A double's samples round half to even here (`Math.Round`) where R2025b rounds half
  away, two counts in 36 on the probe's `magic(6)*7/252`.
