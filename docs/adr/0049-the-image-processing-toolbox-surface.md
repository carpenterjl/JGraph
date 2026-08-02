# ADR 0049 — The Image Processing Toolbox surface: 266 documented names, twelve waves

## Status

Accepted (M46, 2026-08-01). Builds on [ADR 0027](0027-image-value-and-codec-layering.md) (M24's
curated imaging core), [ADR 0028](0028-uniform-zero-based-indexing.md) (the dialect-scoped index
base) and [ADR 0043](0043-shaped-arrays.md) with M41's N-D arrays, which is what let a volume be an
ordinary value.

## Context

M24, M24c and M24d built a curated imaging core: 41 builtins over `JGraph.Imaging` and
`JGraph.Imaging.Codecs`, chosen because a real script needed them. The ask for M46 was the opposite
shape — the toolbox as MathWorks documents it, with the documented options rather than the ones the
first caller happened to want.

Three findings set the milestone's shape before any code was written.

**There was no list to mirror.** The R2021b dump the base tracker reads
(`matlab-r2021b-commands.html`) came from an install without the toolbox: zero `toolbox/images`
rows. Filtering it yields a handful of base-MATLAB image functions, not a toolbox. The list is
therefore transcribed once from MathWorks' online reference into
`tools/matlab-checklist/matlab-r2021b-ipt.csv`, with everything stamped `Since R2022a` or later left
out. A hand-maintained doc over a hand-transcribed list is precisely where counts rot, so
`verify-ipt-coverage.py` was written before the first wave, not after the last.

**Every existing imaging builtin was positional-only.** Not one parsed a name-value pair. `imresize`
had no bicubic, `edge` no `[low high]` for Canny, `imbinarize` no `'adaptive'`, `regionprops` no
property selection and about thirty missing properties. Upgrading the 41 was as much of the milestone
as adding the ~215 new names, and it is why the option parser is architecture rather than a detail.

**Two live bugs surfaced during design**, both of which had been wrong since the imaging core
shipped: a `.m` script's `img(1, 1)` read the pixel diagonally in from the corner, because
`Interpreter.IndexImage` never consulted `Dialect.IndexBase`; and `L = bwlabel(BW)` under the MATLAB
dialect returned the `[L, n]` pair, because imaging multi-output rode JGS array destructuring rather
than the `MultiOutput` seam M36 built for exactly this.

## Decisions

### 1. The class tag lives on the picture; the visible scale is dialect-scoped

`ImageBuffer` gained one mutable `Class` property (`Double`/`Single`/`UInt8`/`UInt16`/`Int16`/
`Logical`), copied by `Clone()`. The ~40 algorithm sites that allocate a buffer were left alone:
algorithms stay class-agnostic on `[0, 1]` doubles, and **the builtin layer stamps the tag** through
one helper, which also quantizes integer classes onto the k/255 (k/65535) grid so
`immultiply(uint8, .5)` lands on whole steps the way MATLAB's visible integer arithmetic does.

The user's decision was "indexing reads return native-scale values". That was taken **per dialect**,
which is the one resolution inside a locked decision and is stated here because it was not free.
The class tag is universal data; the script-visible scale is not. Under MATLAB a `uint8` picture
reads 0–255, subscripts are one-based, `class(I)` answers `'uint8'`. Under JGS the documented
`[0, 1]`, zero-based surface is untouched — because the shipped `laser-center.jgs` example
(`imbinarize(I, 0.15 * max(I))`) and the whole `JgsImageBuiltinTests` suite depend on it, and
MATLAB fidelity is wanted where MATLAB fidelity is measured. That existing JGS tests pass unchanged
is the proof the JGS surface did not move.

The tag never enters serialization — `imshow` bakes to ARGB — so `.graph` stays v5 across the whole
milestone.

### 2. A volume is a plain N-D array, and an image is refused where depth is meant

M41 gave arrays real dimensions over column-major storage with M22's tiered allocator behind them,
so `zeros(500, 500, 500)` already worked and already spilled to disk. A third value type would have
bought nothing and cost every "takes some numbers" function a third case.

It is deliberately **not** an `ImageBuffer` with a third size. An image's third dimension holds
colour — different measurements of the same place; a volume's holds depth — the same measurement at
different places. A filter must therefore reach through the stack exactly as it reaches across the
rows, which is the whole reason `imgaussfilt3` is a separate function rather than a loop around
`imgaussfilt`. So every volume builtin refuses an image value outright and says why, rather than
quietly filtering the channels as though they were slices.

The residual ambiguity is a bare `h×w×3` array, which the colour work reads as RGB planes. It is
settled per function on MATLAB's own documentation instead of by a global rule: `bwconncomp` and
`bwareaopen` read it as a volume (both are documented N-D, and a mask has no colour), while
`padarray` reads it as a volume only when the pad size names all three dimensions.

### 3. Options are parsed once, and an unknown one lists the documented spellings

`JgsBuiltins.Imaging.Options.cs` holds `ImgOptionSpec` and `ImgArgs`. The option region starts at the
first string matching a declared flag or name, matched case-insensitively and **exactly** — no prefix
matching, because `'Sen'` for `'Sensitivity'` is a guess about which option was meant. Mutually
exclusive flags are validated per builtin. `plot`'s bespoke splitter was left alone: unifying it
would have put the whole plotting surface at risk for no gain here.

The same file holds `ImgLike`/`ImgLikeOut`/`ImgMaskOut`, which is where the milestone's most-repeated
decision lives: **a picture and a plain matrix are the same thing to a toolbox function**. A matrix
in means a matrix out; colour planes in mean colour planes out; an image in keeps its class. Wave G
had to apply this to `imshow`, and wave L found fourteen more functions that had never been told.

### 4. Struct arrays under MATLAB, tables under JGS — decided at registration time

The dialect reaches the builtins when they are declared, following the `sprintf` precedent, so each
dual-shape builtin branches once rather than on every call. `regionprops` returns an n×1 struct array
under MATLAB with one-based centroids and MATLAB's half-pixel bounding-box convention, and a Table
under JGS. `bwconncomp` returns MATLAB's struct with one-based column-major `PixelIdxList`.
`houghlines` returns a 1×n struct array. `regionprops3` returns a **table in both**, because MATLAB's
own `regionprops3` does.

`RegisterImagingMultiOutputForms` puts every `[a, b] = f(…)` on M36's real seam, which is what fixes
the `bwlabel` bug: a single output is now the label map alone.

The inherited limitation is recorded rather than fought: a `.m` file reached through `run()` from a
JGS session registers under the JGS dialect. That is the sprintf behaviour, and the CLI's `-batch
file.m` form does not go through it.

### 5. Where a method is unpublished, state the rule rather than guess quietly

The coverage doc's divergence list is long on purpose. `fspecial3('laplacian')`'s two shape
parameters weight the edge and corner neighbours by a formula MATLAB documents only as "shape";
`obliqueslice`'s two in-plane axes are a choice MATLAB does not document at all, taken from whichever
coordinate axis the normal leans on least and ordered right-handed; `regionprops3`'s `SurfaceArea`
counts outward voxel faces where MATLAB estimates a smoothed surface, so a 4×4×4 cube measures 96 and
MATLAB will answer lower; `imsegkmeans3` seeds deterministically, because a segmentation that came
back different every run would make the same script unreproducible. Each is written down with what it
does and how it differs, which is the only honest form for a mirror of an unpublished method.

`activecontour` was the milestone's declared risk item, with an include-or-exclude-honestly fallback.
It is **implemented** (Chan–Vese and the edge variant), so the fallback was not needed.

## Consequences

**266 of 409 documented names**, verified by `verify-ipt-coverage.py` against 417 checked rows, with
**zero pending**: everything outside the recorded exclusions is implemented. The exclusions are 143
individual names plus 8 families, each with a structural reason — DICOM and camera RAW are object
models and per-sensor decoders rather than image formats; the interactive tool, viewer and ROI-object
ecosystems need a handle-graphics UI; the legacy `tform` system is superseded by the implemented
`fitgeotrans`/`imwarp`; iterative intensity registration is excluded because a registrar that
converges differently under the same name misleads.

The algorithm layer grew from 12 files to 51 in `JGraph.Imaging`, and the builtin layer from one
871-line file to thirteen partials. `.graph` stays **v5** — every display addition bakes to RGB and
rides the existing `RgbImagePlot`.

**2,545 tests pass** at 0 build warnings, and **all 22 stress scripts exit 0** under `jgraph -batch`.
`stess_22.m` is new: 28 self-checking sections that assert numbers wherever a number can be asserted —
bicubic resize reproducing a straight ramp exactly, `bwdist` matching the analytic hypotenuse to
machine precision, watershed cutting two touching discs into exactly two basins, `dctmtx`
orthonormality, `otsuthresh(counts)` agreeing with `graythresh`, `psnr(A, A) == Inf`, erosion and
dilation staying dual for three structuring elements, and `imfindcircles` locating three drawn discs
to within two pixels.

**Eighteen answers were corrected rather than added**, each listed in `docs/matlab-ipt-coverage.md`
under its wave. The ones that were silently wrong before this milestone are worth naming here:
`imrotate` turned the wrong way, `imresize` sampled on the wrong grid, `imdilate` did not reflect its
structuring element, `imnoise`'s Gaussian read its mean as a variance, `histeq` used a plain
cumulative distribution, `eig` on a general matrix returned wrong eigenvalues (found by the wave-K
work), and `&`/`|` dropped an array's shape. Several are base-language rather than toolbox — `repmat`
ignored all but its last count, `&`/`|` above — and they are recorded in the toolbox doc because the
imaging work is what found them.

**Four of those corrections came from wave L's own script**, which is the argument for writing it:
a picture could not be sliced (`BW(:, 19:22)` was an error while the same expression on a matrix
worked), `cat` refused any dimension past the second (so `cat(3, R, G, B)` — the documented way to
build a colour picture, and what wave K's own error message tells a script to do — did not work),
fourteen point and threshold functions refused a plain matrix, and a two-subscript write on a JGS
matrix threw an `IndexOutOfRangeException` out of the interpreter rather than raising a script error.
None of the four would have been found by the unit suites, which had always fed each function the
shape it expected.

**Three gaps were found and left, with reasons.** `class(uint8(7))` answers `'double'`: integer
conversion rounds and saturates on double storage (ADR 0045) without recording what it did, so a
plain array carries no class where MATLAB's does. `for x = {'a', 'b'}` does not iterate a cell array
into 1×1 cells. `height`/`width` on a table are absent. All three are base-language rather than
toolbox, none blocks the imaging surface, and each is written down instead of being folded into a
milestone that was not about them.

The base tracker moved by **nine, to 605 of 2,027 callable** — `cmap2gray`, `hsv2rgb`, `im2double`,
`im2gray`, `imapprox`, `imfinfo`, `ind2rgb`, `rgb2hsv`, `rgb2ind`. That is not an understatement of
M46: those nine are simply the only names of the ~215 that MATLAB documents under a base folder
rather than `toolbox/images`, and the dump the tracker reads cannot see the toolbox at all.

## Alternatives considered

- **A separate `VolumeBuffer` value type.** Rejected in the plan and never regretted: it would have
  given every existing numeric builtin a third case to handle, to buy storage that N-D arrays
  already provide with the tiered allocator behind them. What the milestone actually needed was not
  a new type but a refusal — the pointed error when an image is passed where depth is meant.
- **Native scale everywhere, JGS included.** The cleaner-sounding reading of the locked decision. It
  would have changed the shipped JGS examples, the catalog texts and `JgsImageBuiltinTests`, and
  broken every `.jgs` script in the wild that thresholds against a fraction. The cost falls on
  working code to make an unmeasured surface more MATLAB-like.
- **Prefix matching on option names**, as MATLAB's own argument parsers often allow. Rejected: the
  failure mode is silent. `'Sen'` matching `'Sensitivity'` today matches something else the moment a
  second option starting with those letters is added, and the script that relied on it changes
  meaning without changing.
- **Implementing `imregister` and friends approximately.** An iterative registrar that converges to a
  different transform under the same name is worse than one that is absent, because the caller has no
  way to tell. The deterministic cases are covered by the implemented `normxcorr2` and `imregcorr`.
- **Leaving the base-language gaps alone in wave L.** `cat(3, …)` and picture slicing are not toolbox
  functions, and a strict scope reading would have deferred both. But wave K's own error text told
  scripts to call `cat(3, …)`, and the toolbox returns masks that a script then has to slice — the
  toolbox surface does not work without them, so they belong to this milestone rather than the next.
