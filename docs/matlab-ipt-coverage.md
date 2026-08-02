# MATLAB Image Processing Toolbox coverage

**266 of 409 documented** Image Processing Toolbox names are implemented, as of M46 (complete).

## Where this list comes from

Unlike `matlab-builtin-coverage.md`, this one has no scraped install behind it. The R2021b dump in the
demo workspace came from a MATLAB that did not have the toolbox installed — it holds zero
`toolbox/images` rows — so `build-checklist.py` cannot see the toolbox at all, and filtering that dump
would yield a handful of base-MATLAB image functions rather than a toolbox.

The list is therefore transcribed once, from MathWorks' online function reference, into
`tools/matlab-checklist/matlab-r2021b-ipt.csv`, with every entry the reference marks `Since R2022a` or
later left out so the mirror stays R2021b. A hand-maintained doc over a hand-transcribed list is
exactly where counts rot, so `tools/matlab-checklist/verify-ipt-coverage.py` checks on demand that
every listed name sits in exactly one bucket below, that no bucket names a function that does not
exist, that everything called implemented is really registered in `JgsBuiltinCatalog.cs`, and that the
headline count matches the tables:

```bash
python tools/matlab-checklist/verify-ipt-coverage.py
```

## Counting rule

Each name appears once, under its primary category — MathWorks lists `montage`, `imcrop`, `bwperim`
and a dozen others under several. Whole families excluded for one structural reason (DICOM, camera
RAW, deep learning) are carried as a single `family` row rather than itemized, so the denominator
counts individual documented functions and classes only. `fft2`, `ifft2`, `fftshift` and `ifftshift`
are base-MATLAB builtins JGraph implemented in M38; the toolbox reference lists them, so they are
counted here too.

JGraph-only names — `mat2im`, `im2mat`, `imcentroid` — are in neither MATLAB nor these counts.

## Implemented — 266
`activecontour`, `adapthisteq`, `adaptthresh`, `affine2d`, `affineOutputView`, `applylut`
`bestblk`, `bfscore`, `blockproc`, `boundarymask`, `bwarea`, `bwareafilt`
`bwareaopen`, `bwboundaries`, `bwconncomp`, `bwconvhull`, `bwdist`, `bwdistgeodesic`
`bweuler`, `bwferet`, `bwhitmiss`, `bwlabel`, `bwlabeln`, `bwlookup`
`bwmorph`, `bwmorph3`, `bwperim`, `bwpropfilt`, `bwselect`, `bwselect3`
`bwskel`, `bwtraceboundary`, `bwulterode`, `checkerboard`, `chromadapt`, `cmap2gray`
`col2im`, `colfilt`, `colorangle`, `conndef`, `convmtx2`, `corr2`
`dct2`, `dctmtx`, `deconvblind`, `deconvlucy`, `deconvreg`, `deconvwnr`
`decorrstretch`, `deltaE`, `demosaic`, `dice`, `edge`, `edge3`
`edgetaper`, `entropy`, `entropyfilt`, `fft2`, `fftshift`, `fibermetric`
`fitgeotrans`, `freqspace`, `freqz2`, `fsamp2`, `fspecial`, `fspecial3`
`ftrans2`, `fwind1`, `fwind2`, `gabor`, `gradientweight`, `gray2ind`
`graycomatrix`, `grayconnected`, `graycoprops`, `graydiffweight`, `graydist`, `grayslice`
`graythresh`, `histeq`, `hough`, `houghlines`, `houghpeaks`, `hsv2rgb`
`idct2`, `ifft2`, `ifftshift`, `illumgray`, `illumpca`, `illumwhite`
`im2col`, `im2double`, `im2gray`, `im2int16`, `im2single`, `im2uint16`
`im2uint8`, `imabsdiff`, `imadd`, `imadjust`, `imadjustn`, `imapplymatrix`
`imapprox`, `imbilatfilt`, `imbinarize`, `imbothat`, `imboxfilt`, `imboxfilt3`
`imclearborder`, `imclose`, `imcolordiff`, `imcomplement`, `imcontour`, `imcrop`
`imcrop3`, `imdiffuseest`, `imdiffusefilt`, `imdilate`, `imdivide`, `imerode`
`imextendedmax`, `imextendedmin`, `imfill`, `imfilter`, `imfindcircles`, `imfinfo`
`imflatfield`, `imfuse`, `imgaborfilt`, `imgaussfilt`, `imgaussfilt3`, `imgradient`
`imgradient3`, `imgradientxy`, `imgradientxyz`, `imguidedfilter`, `imhist`, `imhistmatch`
`imhistmatchn`, `imhmax`, `imhmin`, `imimposemin`, `imlincomb`, `imlocalbrighten`
`immse`, `immultiply`, `imnlmfilt`, `imnoise`, `imopen`, `imoverlay`
`impixel`, `improfile`, `impyramid`, `imquantize`, `imread`, `imreconstruct`
`imreducehaze`, `imref2d`, `imregcorr`, `imregionalmax`, `imregionalmin`, `imresize`
`imresize3`, `imrotate`, `imrotate3`, `imsegfmm`, `imsegkmeans`, `imsegkmeans3`
`imsharpen`, `imshow`, `imshowpair`, `imsplit`, `imsubtract`, `imtophat`
`imtranslate`, `imwarp`, `imwrite`, `ind2gray`, `ind2rgb`, `integralBoxFilter`
`integralBoxFilter3`, `integralImage`, `integralImage3`, `intlut`, `iptcheckconn`, `iptgetpref`
`iptsetpref`, `iradon`, `jaccard`, `lab2double`, `lab2rgb`, `lab2uint16`
`lab2uint8`, `lab2xyz`, `label2idx`, `label2rgb`, `labelmatrix`, `labeloverlay`
`lin2rgb`, `makelut`, `mat2gray`, `maxhessiannorm`, `mean2`, `medfilt2`
`medfilt3`, `modefilt`, `montage`, `multissim`, `multissim3`, `multithresh`
`nlfilter`, `normxcorr2`, `ntsc2rgb`, `obliqueslice`, `offsetstrel`, `ordfilt2`
`otf2psf`, `otsuthresh`, `padarray`, `phantom`, `poly2label`, `poly2mask`
`projective2d`, `psf2otf`, `psnr`, `qtdecomp`, `qtgetblk`, `qtsetblk`
`radon`, `rangefilt`, `reducepoly`, `regionfill`, `regionprops`, `regionprops3`
`rgb2gray`, `rgb2hsv`, `rgb2ind`, `rgb2lab`, `rgb2lightness`, `rgb2lin`
`rgb2ntsc`, `rgb2xyz`, `rgb2ycbcr`, `rigid2d`, `roicolor`, `roifilt2`
`roipoly`, `ssim`, `std2`, `stdfilt`, `strel`, `stretchlim`
`superpixels`, `superpixels3`, `transformPointsForward`, `transformPointsInverse`, `visboundaries`, `viscircles`
`watershed`, `whitepoint`, `wiener2`, `xyz2double`, `xyz2lab`, `xyz2rgb`
`xyz2uint16`, `ycbcr2rgb`

## Not implemented — 0

Every documented name outside the exclusions below is implemented. What remains is recorded in
Excluded, with the reason.

## Excluded — 143 names and 8 families

Recorded rather than pending: each needs a subsystem JGraph deliberately does not have, or would
shadow something that already works.

- **`blockedimages`** — Out-of-core tiled-image framework; JGraph's buffers already tier to disk (M22) and blockproc covers the scripting-level use.
- **`deeplearning`** — Needs a neural-network runtime JGraph does not ship.
- **`dicom`** — DICOM: a medical object model and data dictionary, not an image decoder.
- **`gpu`** — Needs a GPU array type.
- **`hdr`** — High-dynamic-range capture and tone mapping: unbounded radiance samples and .hdr/.exr codecs sit outside the [0, 1] value model.
- **`hyperspectral`** — Needs a spectral data-cube type.
- **`raw`** — Camera RAW: needs a libraw-class decoder per sensor.
- **`specializedformats`** — Analyze / DPX / Interfile / NIfTI / NITF / multi-frame TIFF: format stacks Skia does not decode.

### Individually excluded — 143

`AssistedFreehand`, `Circle`, `Crosshair`, `Cuboid`, `Ellipse`, `Freehand`
`Line`, `LocalWeightedMeanTransformation2D`, `MattesMutualInformation`, `MeanSquares`, `OnePlusOneEvolutionary`, `PiecewiseLinearTransformation2D`
`Point`, `Polygon`, `Polyline`, `PolynomialTransformation2D`, `Rectangle`, `RegularStepGradientDescent`
`Warper`, `affine3d`, `applycform`, `axes2pix`, `beginDrawingFromPoint`, `bringToFront`
`brisque`, `brisqueModel`, `burstinterpolant`, `bwpack`, `bwunpack`, `colorChecker`
`colorcloud`, `cpcorr`, `cpselect`, `cpstruct2pairs`, `createMask`, `displayChart`
`displayColorPatch`, `draw`, `drawassisted`, `drawcircle`, `drawcrosshair`, `drawcuboid`
`drawellipse`, `drawfreehand`, `drawline`, `drawpoint`, `drawpolygon`, `drawpolyline`
`drawrectangle`, `esfrChart`, `fan2para`, `fanbeam`, `findbounds`, `fitbrisque`
`fitniqe`, `fliptform`, `geometricTransform2d`, `geometricTransform3d`, `getimage`, `getimagemodel`
`grabcut`, `iccfind`, `iccread`, `iccroot`, `iccwrite`, `ifanbeam`
`imageinfo`, `imagemodel`, `imattributes`, `imcolormaptool`, `imcontrast`, `imdisplayrange`
`imdistline`, `imgca`, `imgcf`, `imgetfile`, `imhandles`, `immagbox`
`immovie`, `imoverview`, `imoverviewpanel`, `impixelinfo`, `impixelinfoval`, `impixelregion`
`impixelregionpanel`, `imputfile`, `imref3d`, `imregconfig`, `imregdemons`, `imregister`
`imregmtb`, `imregtform`, `imsave`, `imscrollpanel`, `imseggeodesic`, `inROI`
`inpaintCoherent`, `inpaintExemplar`, `iptGetPointerBehavior`, `iptPointerManager`, `iptSetPointerBehavior`, `iptaddcallback`
`iptcheckhandle`, `iptcheckmap`, `iptgetapi`, `ipticondir`, `iptprefs`, `iptremovecallback`
`iptwindowalign`, `isicc`, `lazysnapping`, `localcontrast`, `locallapfilt`, `localtonemap`
`makeConstrainToRectFcn`, `makecform`, `makeresampler`, `maketform`, `measureChromaticAberration`, `measureColor`
`measureIlluminant`, `measureNoise`, `measureSharpness`, `niqe`, `niqeModel`, `orthosliceViewer`
`piqe`, `plotChromaticity`, `plotSFR`, `reduce`, `rgbwide2xyz`, `rgbwide2ycbcr`
`rigid3d`, `sliceViewer`, `tformarray`, `tformfwd`, `tforminv`, `truesize`
`volshow`, `wait`, `warp`, `xyz2rgbwide`, `ycbcr2rgbwide`

By reason:

- **Wide-gamut and ICC colour management** — the `rgbwide*` and `*2rgbwide` pairs need BT.2020/2100
  signal types the value model does not carry; the five ICC entries need a binary profile parser and
  a profile store. `applycform` and `makecform` are the legacy cform objects the same documentation
  supersedes with the direct conversions, which are planned.
- **The pre-R2013 spatial transform system** — `maketform` and its apply/query verbs, plus the
  transformation classes. Superseded by fitgeotrans and imwarp, both planned. `affine3d`, `rigid3d`
  and `imref3d` are deferred with them: M46's volumes get resize, rotate and crop, not general 3-D
  warping.
- **Iterative intensity-based registration** — `imregister`, `imregtform`, `imregconfig`,
  `imregdemons` and `imregmtb`, with their optimizer and metric classes. A registrar that converges
  differently from MATLAB's under the same name misleads more than a missing one; the deterministic
  cases are covered by normxcorr2 and imregcorr, which are planned. `cpselect`, `cpcorr` and
  `cpstruct2pairs` are the interactive control-point workflow.
- **Interactive tools, viewers and ROI objects** — the whole image-tool ecosystem, the `draw*` family
  and its ROI classes, `volshow`, `sliceViewer`, `orthosliceViewer`, `colorcloud` and `iptprefs`.
  JGraph edits figures through its own inspector, and JGS has no graphics-handle value. `immovie`
  has no movie type to build. `warp` needs a texture-mapped surface the renderer cannot yet draw,
  and is deferred rather than refused.
- **Research-grade algorithms where a partial mirror would mislead** — `locallapfilt`,
  `localcontrast` and `localtonemap` (fast local Laplacian pyramids); `grabcut`, `lazysnapping` and
  `imseggeodesic` (interactive graph-cut segmentation); `inpaintCoherent` and `inpaintExemplar`;
  `burstinterpolant`.
- **Learned, no-reference image quality** — `brisque`, `niqe` and `piqe` with their model classes are
  pretrained models JGraph will not ship; the test-chart metrology family is a physical-chart
  workflow built around `esfrChart` and `colorChecker`.
- **Fan-beam CT** — `fanbeam`, `ifanbeam` and `fan2para` are geometry rebinning on top of the
  parallel-beam transform, which is planned.
- **Bit-packed binary images** — `bwpack` and `bwunpack` exist in MATLAB for memory and speed.
  JGraph's buffers already tier their storage, and a packed value no other builtin accepts would be
  a trap.

## Recorded divergences

- **A class tag rides on a picture, not on a plain array of numbers.** `imread` answers `uint8`,
  `class(I)` says so, `I(1, 1)` reads 0–255 and `im2double`/`im2uint16` re-tag as they convert. But
  MATLAB also carries a class on an ordinary array, so its `class(uint8(7))` is `'uint8'` where this
  answers `'double'`: integer conversion here rounds and saturates the values on double storage
  (ADR 0045) without recording what it did. `intlut` is the one toolbox function that therefore
  needs a real picture rather than an array, since its table is indexed by the sample's own integer
  value and an untagged array cannot say what those values mean.
- **A picture and a plain matrix are interchangeable almost everywhere, and the exceptions are
  named.** Every function that measures or filters takes either. `imwrite` takes either. The ones
  that genuinely need a picture are `intlut` (above) and `im2mat`, which exists to unwrap one.
- **`imgaussfilt`'s `'FilterDomain'` is accepted but never changes the answer.** MATLAB offers
  `'auto'`, `'spatial'` and `'frequency'` because its spatial path slows down as the kernel grows.
  The one here is separable, so it already costs `kh + kw` multiplies per pixel rather than
  `kh · kw`, and filtering in the frequency domain would only change the rounding. The option is
  taken so a MATLAB script runs unchanged, and an unrecognized value is still an error.
- **`edge`'s `'nothinning'` is accepted and does nothing.** Canny's non-maximum suppression is not
  optional in this implementation, and the gradient methods have no separate thinning pass to switch
  off. Accepting the word keeps scripts running; silently producing a differently-thinned map would
  not.
- **`'approxcanny'` is Canny on a quantized magnitude**, not MATLAB's own approximation. It is
  faster and blockier than full Canny, which is what the name promises, but the two will not agree
  pixel for pixel.
- **`bestblk`'s tie-breaking is stated rather than matched.** MATLAB documents the goal — blocks that
  divide the image evenly, or as evenly as possible — but not how it chooses between candidates. The
  rule here searches `[ceil(k/2), k]`, prefers an exact divisor, and otherwise takes the size leaving
  the largest final block. Confining the search matters: every number divides by one, and a block
  size of one divides perfectly while being useless.
- **The transform classes are tagged structs.** MATLAB ships `affine2d`, `projective2d`, `rigid2d`
  and `imref2d` as classes; JGraph has no object system to put them in, so each is a struct with a
  `Type` field naming the class it stands for. `tform.T`, `tform.Rotation`, `R.XWorldLimits` and
  `class(tform)` all read as they do in MATLAB, and every consumer here can tell a transform from a
  spatial reference. What is missing is method syntax: there is no `tform.invert()` or
  `R.worldToIntrinsic(...)`. `transformPointsForward` and `transformPointsInverse` take the transform
  as their first argument, which is the form MATLAB documents anyway, and the extra `Type` field is
  visible when a script displays the struct.
- **`fitgeotrans` refuses the local transformation types by name.** `'polynomial'`, `'pwl'` and
  `'lwm'` have no 3×3 matrix form and no `imwarp` path here, so they error saying so rather than
  falling back to something that would silently be a different transform.
- **`checkerboard` returns an image, not a double array.** MATLAB draws no line between the two;
  JGraph does, and `imshow` takes only images. Since the pattern exists to be displayed and warped,
  the image form is the useful one. Everything else about it matches: `2·p·n` by `2·q·n`, with the
  light squares in the right half at 0.7 rather than 1.
- **`imcrop` with no rectangle returns the whole image.** MATLAB opens a window and waits for one to
  be drawn. There is no window to draw on in a batch run, so the honest answer is the whole picture
  and the rectangle that describes it.
- **`imwarp` accepts `'SmoothEdges'` and implements it as fill-value interpolation** at the border
  rather than by padding the input first. The visible effect is the same — the edge fades into the
  fill instead of being extended — but a pixel-for-pixel comparison against MATLAB may differ in the
  outermost row and column.
- **An indexed image's indices are one-based doubles.** MATLAB's `gray2ind` and `rgb2ind` hand back
  `uint8` when the palette fits in 256 entries, and a `uint8` index array counts from zero — so
  `max(X(:))` there is `n − 1`. JGraph has no integer array class, so `X` is always the other form
  MATLAB itself accepts: a double array counting from one. `ind2rgb`, `ind2gray` and `imapprox` read
  it that way, so the round trip is identical; a script that inspects `X`'s raw values or its class
  will differ by one. Under JGS the indices count from zero, matching that dialect's own subscripts
  (ADR 0028).
- **`illumgray`, `illumwhite` and `illumpca` normalize on the largest channel.** MathWorks documents
  what each estimator measures but not how the returned triple is scaled. The scale does not reach
  the answer: `chromadapt` normalizes the illuminant to unit luminance before building its adaptation
  matrix, so any positive multiple of an estimate gives the same correction.
- **`chromadapt`'s 'simple' method preserves brightness.** MATLAB documents it only as "scale the
  colour channels by the illuminant". Here each linear channel is scaled by the illuminant's mean
  over that channel, which turns the illuminant grey without also making the picture brighter.
- **The colour functions take three shapes and answer in kind**: a three-channel image value, a plain
  `h×w×3` numeric array (which is what MATLAB calls an RGB image, and what a script that wrote
  `zeros(h, w, 3)` is holding), or an `n×3` colormap. That is wider than MATLAB, not narrower — but
  it means `rgb2lab` of an array gives an array back rather than an image value.
- **A picture argument takes three shapes and answers in kind**: an image value, a plain matrix, or
  an `h×w×3` array. MATLAB has no separate image type, so a script that wrote `zeros(h, w, 3)` and
  filled the planes is holding what MATLAB calls an RGB image, and every function that takes a colour
  picture has to take that too. The rule is uniform across the filtering, geometry and enhancement
  families: whatever arrived comes back, except that an operation returning one value per pixel —
  `fibermetric`, a transmission map — is a plain matrix whatever went in.
- **`adapthisteq`'s Rayleigh and exponential distributions are derived rather than matched.** Each is
  its own inverse cumulative distribution applied to the tile's cumulative histogram, normalized so a
  cumulative fraction of one lands exactly on white. Without that normalization the two shapes would
  stop short of white by an amount depending on alpha, and neighbouring tiles would disagree about
  what white is. MathWorks documents which distributions are offered, not the constants.
- **`imhistmatch`'s `'polynomial'` method is a monotone cubic through the mapping `'uniform'`
  produces.** MathWorks says the polynomial method gives a smoother transformation but not the
  polynomial's order or its knots. A Fritsch–Carlson fit through every eighth entry is smooth and,
  crucially, still monotone — an ordinary spline would overshoot at each step and map a lighter input
  to a darker output, which reads as a band rather than a smoother gradient.
- **`imsharpen`'s `'Threshold'` is measured against the largest local difference in the picture.** The
  documentation calls it "the minimum contrast required for a pixel to be considered an edge pixel"
  on a `[0, 1]` scale, which fixes the meaning but not the reference; taking it relative to the
  strongest edge present makes one setting behave the same on a flat scene and a contrasty one.
- **`imbilatfilt` measures colour distance in L\*a\*b\*, divided by 100.** MATLAB filters colour in
  L\*a\*b\* too, so that equally visible differences weigh equally whatever the hue. Dividing by the
  span of L\* is what lets one `degreeOfSmoothing` default mean the same thing for a colour picture as
  for a grayscale one.
- **`imguidedfilter` with a colour guide filters channel by channel.** He's paper gives a colour form
  that fits each window against all three guide channels at once through a 3×3 covariance; the one
  here fits each channel against its own. The difference shows only where an edge exists in
  chrominance but not in any single channel.
- **Anisotropic diffusion steps by one over the total neighbour weight**, with the diagonal
  neighbours at half — the square of their distance. That keeps the diagonals from diffusing faster
  than the axes, which would turn a round blob square, and keeps the update stable at every
  connectivity. MathWorks documents the conduction functions and the connectivity choice, not the
  step size.
- **`imdiffuseest`'s rule is stated rather than matched.** The first threshold is the ninetieth
  percentile of the gradient magnitude and the rest fall linearly to a fifth of it over five
  iterations, so each pass conducts across less than the last. What MathWorks documents is what the
  estimate is for.
- **`imreducehaze`'s `'boost'` contrast enhancement adds back a share of what a wide blur removed**,
  scaled by `'BoostAmount'`. The documentation says only that the option increases contrast by that
  amount. `'global'` and `'none'` are unambiguous and match.
- **`decorrstretch` uses the symmetric whitening `V·Λ^(−½)·Vᵀ`.** Any square root of the inverse
  covariance decorrelates the bands, and MathWorks specifies the result — uncorrelated bands with the
  requested means and spreads — rather than the factorization. The symmetric one is the whitening
  closest to doing nothing, which is what keeps the output recognizable as the same scene rather than
  a differently-coloured one.
- **`lab2double` and `xyz2double` need a class tag.** They undo an integer encoding, and the class is
  the only record of which encoding was used, so they take an image and refuse a bare colormap.
- **16-bit PNG degrades to 8 bits.** Measured on SkiaSharp 2.88.8, in both directions: the PNG
  encoder accepts the 16-bit colour type and copies the pixels, then writes a depth-8 IHDR anyway,
  and the decoder will not return 16-bit samples from a file that genuinely has them. `imwrite` with
  `'BitDepth', 16` therefore writes an 8-bit file, and `imread` of a real 16-bit PNG reports `uint8`.
  The codec checks the *encoded bytes* rather than the return codes, so the class a script sees
  always matches the precision the file actually holds — an image tagged `uint16` over 8-bit data
  would be the worse outcome. The path stays and begins working if a future Skia encodes 16 bits.
- **TIFF is not supported.** Skia carries no TIFF codec; `imread` names the formats that do work.
- **`[X, map] = imread(...)` always returns an empty map.** Skia decodes a palettized file straight
  to truecolour and never exposes the palette. That is MATLAB's own answer for a non-indexed file;
  for a genuinely indexed one it is a divergence.
- **Image samples are `[0, 1]` doubles carrying a class tag**, not integer storage. Arithmetic
  saturates at the class range and integer classes are snapped to their sample grid after every
  operation, so `immultiply(uint8Image, 0.5)` lands on whole 1/255 steps as MATLAB's does; the
  intermediate within a single operation is computed in double.
- **Script-visible intensities are per dialect.** A `.m` script sees MATLAB's native scale — a
  `uint8` picture's pixels read 0–255, `class` answers `'uint8'`, and an added constant is a grey
  level. JGS keeps its documented `[0, 1]`, 0-based surface (ADR 0028), because that is what its
  shipped examples and scripts are written against. The class tag itself is the same in both.
- **A `.m` file included from a JGS session keeps JGS-flavoured returns.** Builtins learn the dialect
  when they are registered, not when they are called, so `run('file.m')` from a JGS script gets the
  JGS shapes. `sprintf` has behaved this way since M28; it is recorded rather than fought.
- **`strel` and `offsetstrel` are tagged structs**, the same device as the transform classes above:
  a struct whose `Type` field names the class it stands for, with `Neighborhood`, `Dimensionality`
  and — for a non-flat element — `Offset`. `se.Neighborhood` and `class(se)` read as they do in
  MATLAB; `se.decompose()`, `se.reflect()` and the rest of the method surface do not exist. Every
  operation that takes an element also still takes a plain 0/1 matrix, which is what JGS scripts
  written before this wave hand over.
- **`strel('disk', r)` is the exact disk**, the shape MATLAB gives for `strel('disk', r, 0)`. By
  default MATLAB approximates the disk with a decomposition into periodic lines, which is faster and
  a slightly different shape; the decomposition count is accepted and ignored here. The same holds
  for `offsetstrel('ball', r, h)`, which is the exact half-ellipsoid.
- **`bwmorph`'s thinning operations follow published algorithms rather than MATLAB's tables.**
  `'thin'` is Zhang–Suen, `'skel'` is Guo–Hall, `'shrink'` is Zhang–Suen without the rule that
  preserves free ends, and `'thicken'` is `'thin'` run on the background. All four are
  topology-preserving — a ring stays a ring — and all four reduce a stroke to single-pixel width,
  but they will not agree with MATLAB pixel for pixel. MATLAB documents what these do and ships the
  512-entry table that does it; the table itself is not published.
- **The single-pixel rules follow the documented effect.** `'bridge'` sets a background pixel whose
  neighbours form two or more separate runs; `'diag'` fills the elbow between two diagonally joined
  pixels; `'branchpoints'` is a skeleton pixel with three or more incoming strokes, counted by
  transitions round the ring; `'endpoints'` is one with at most one neighbour. Each is the rule
  MATLAB's documentation states, arrived at independently of its table.
- **`bwskel` is two-dimensional** and is that thinning followed by branch pruning, where MATLAB uses
  Lee's algorithm, which also handles volumes. `'MinBranchLength'` prunes iteratively: taking a short
  spur off can shorten the branch that carried it.
- **`bwulterode` takes the regional maxima of the whole distance transform** rather than working
  object by object. For separated objects the two agree; two objects joined by a narrow neck can
  differ, because a maximum on one side of the neck is measured against the whole field.
- **`imimposemin` marks the imposed minima with 0, not −∞.** MATLAB genuinely returns `-Inf` there.
  Images here carry `[0, 1]`, so the imposed minima land on the bottom of that range instead — still
  strictly below everything else, because the reconstruction raises the rest of the picture by a grey
  step first, which is the property the operation exists to provide.
- **`bwdist`'s second output is quoted per dialect.** MATLAB's `idx` is a 1-based column-major linear
  index; that is what a `.m` script gets. JGS gets the 0-based row-major index it uses everywhere
  else (ADR 0028). Pixels with no seed anywhere report 0.
- **`bwdistgeodesic` and `graydist` return infinity for what they cannot reach**, including pixels
  outside the mask. Their default metric is `'chessboard'`, MATLAB's.
- **Non-flat morphology saturates at the ends of the range.** Dilating by an `offsetstrel` adds its
  heights, which can push a sample past 1; the result is clamped, which is what MATLAB does for every
  class but `double` — and `[0, 1]` is the only range an image carries here.
- **`conndef(3, …)` returns a real 3×3×3 array, and nothing consumes it yet.** The 3-D connectivities
  and the `'cube'`, `'cuboid'` and `'sphere'` structuring elements are built and readable; the volume
  operations that take them arrive in wave K, and the two-dimensional morphology refuses a
  three-dimensional element by name rather than silently flattening it.
- **A mask-shaped result follows its input's shape, but never its class.** `bwperim`, `bwmorph`,
  `imregionalmax` and the rest hand back a plain matrix for a matrix and a `logical` image for an
  image: thresholding a `uint8` photograph does not produce a `uint8` answer.
- **`regionprops` returns a struct array under MATLAB and a Table under JGS.** Only one of the two
  can hold a pixel list, a convex hull or a cropped mask, so the Table form carries the scalar
  properties as columns and leaves the list-valued ones out rather than flattening them into
  something a column cannot mean. `regionprops('table', …)` gives the Table in either dialect.
- **`stats.Area` on a struct array yields a row array, not a comma-separated list.** MATLAB's
  cs-list exists only in argument and bracket positions and JGraph has no value for it; the nearest
  honest answer is the collection itself — a row when every field is a number, a cell otherwise.
  `[stats.Area]`, which is the form scripts actually write, therefore comes out right, and
  `stats.Area` alone yields the row instead of printing each value in turn.
- **`bwconncomp` returns a struct in both dialects**, with `PixelIdxList` as a cell of index vectors
  numbered the way that dialect numbers pixels — column-major from one under MATLAB, row-major from
  zero under JGS. The plan called for a Table under JGS; a Table cannot be fed back to
  `labelmatrix`, which is the whole reason the struct exists.
- **`watershed` is two-dimensional**, by Meyer's flooding from every regional minimum. Ridge pixels
  come back as 0, as MATLAB's do. Volumes arrive with wave K.
- **`activecontour` evolves a mask rather than a signed-distance level set.** The interface is where
  the mask's sign changes and it is re-derived each step, which removes the reinitialization pass a
  distance-function level set needs. Both of MATLAB's methods are here — Chan–Vese by region means
  and `'edge'` by gradient — and both converge on the same objects, but the contour arrives along a
  slightly different path and an iteration count that stops MATLAB mid-motion will not stop this one
  in the same place.
- **`superpixels` is SLIC with MATLAB's `'slic0'` default**, adapting the compactness per cluster.
  The tiling is equivalent, not identical: SLIC's result depends on where the initial centres land,
  and the seeding here is a plain grid rather than one perturbed to the lowest gradient nearby.
- **`imsegfmm` is Dijkstra over the weight's reciprocal**, not an Eikonal solver. The front travels
  along the grid rather than across it, which costs a little accuracy diagonally and buys the
  shortest-path guarantee outright. The threshold is on the normalized arrival time, as MATLAB's is.
- **`imfindcircles` implements the two-stage method only**, which is MATLAB's default; the
  phase-code method is not offered. Peaks are genuine local maxima of the blurred accumulator, and
  the radius comes from histogramming the edge distances round each accepted centre.
- **`regionfill` solves Laplace's equation by relaxation** rather than by assembling and factoring
  the Laplacian. It reproduces a plane exactly, which is the property that matters; the matrix would
  be enormous for a large hole and the iteration converges in a few hundred sweeps at the sizes this
  is used on.
- **`viscircles` and `visboundaries` draw ordinary lines on the current axes.** They are not handle
  objects and there is nothing to delete afterwards; `label2rgb`, `labeloverlay` and `imoverlay`
  likewise bake to an RGB image rather than adding anything to the figure model.
- **`bwmorph`-style tie-breaking in `bwareafilt` and `bwpropfilt` follows MATLAB's documented rule**:
  asking for the three largest when four regions tie returns all four, because refusing to choose
  arbitrarily between equals is more useful than returning whichever the sort happened to put first.
- **`roipoly` with no polygon returns the whole picture**, for the same reason `imcrop` with no
  rectangle does: MATLAB opens a window and waits for one to be drawn, and there is no window in a
  batch run.
- **`radon` and `iradon` project about the picture's geometric centre**, at `((rows−1)/2,
  (cols−1)/2)`, where MATLAB's inverse uses a `ceil(n/2)` grid that sits half a pixel off centre for
  an even size. Sharing one convention is what makes the pair invert each other exactly, which is
  the property a script can check; against a sinogram MATLAB produced, an even-sized reconstruction
  will be half a pixel out.
- **`iradon`'s ramp is built from its impulse response**, transformed, rather than written down as
  `|ω|` and sampled. Sampling the ramp in frequency gives it a spurious DC term — the continuous
  ramp is zero at zero but its sampled inverse is not — and the visible result is a reconstruction
  sitting on a constant offset. This is MATLAB's own approach, stated here because the two differ by
  a hair at the top of the band where the impulse response is truncated.
- **`phantom` rasterizes by pixel centre**, so an ellipse edge falls between two pixels rather than
  being anti-aliased across them. MATLAB does the same; what differs is that the three small
  low-contrast ellipses can gain or lose a pixel at very small sizes, where the shell is thinner
  than the sampling.
- **`qtdecomp` requires the picture's side to be the smallest block size times a power of two**, and
  says so by name when it is not. MATLAB's message is vaguer; the constraint is the same one, since
  halving has to land exactly on both the floor and the ceiling.
- **`qtdecomp`'s test function is called once per level, not once per block**, with every block of
  that size stacked as pages of an `m×m×k` array — which is MATLAB's own contract. What is not
  supported is passing extra arguments through to it (`qtdecomp(I, fun, P1, P2)`); a handle that
  captures what it needs does the same job, and the error says so.
- **`qtdecomp` returns a sparse map in both dialects.** It is genuinely sparse — one entry per block,
  so a picture split into a hundred blocks has a hundred entries however many pixels it holds — and
  `qtgetblk` and `qtsetblk` both read it back, so there is no reason for the two dialects to differ.
- **`imregcorr` recovers rotation and scale by log-polar phase correlation**, with Reddy and
  Chatterji's high-pass emphasis on the spectrum. The angle is quantized to the 180 bins it resamples
  on, so a degree is its resolution rather than its error, and a picture with no high-frequency
  content has little for the match to work on. `'Window'` is accepted; the log-polar stage always
  tapers, because a rectangular window leaks a cross along both axes of the spectrum that would
  dominate any polar match.
- **`imregcorr` resolves the half-turn ambiguity by trying both.** A magnitude spectrum is symmetric
  through the origin, so a turn and the same turn plus 180° are indistinguishable there; both
  candidates are applied and the one that actually lines the pictures up is kept.
- **`freqz2` evaluates the response from its definition rather than by zero-padding and
  transforming.** It costs one multiply-add per tap per sample, which for the sizes involved is
  nothing, and it buys the form where the caller names the frequencies — a response along one line
  through the plane, say — which a transform cannot give at all. The kernel's origin is its middle
  tap, so a symmetric kernel answers with a real response and no phase to unwrap.
- **`fsamp2`, `fwind1` and `fwind2` in their `(f1, f2, Hd, …)` form evaluate the inverse transform as
  a sum at the frequencies given**, rather than interpolating the response onto a regular grid first
  and transforming that. Handed the grid `freqspace` produces, the sum reduces term for term to the
  transform, which is the identity the two forms are tested against; handed scattered points it is
  the frequency-sampling design those points describe, with no interpolation error in between.
- **`fwind1`'s two-window form takes the first window along the rows.** MathWorks documents that the
  two are multiplied out and not which goes where; the common case passes the same window twice,
  where it cannot matter.
- **`convmtx2` refuses to build a matrix above sixty-four million entries.** Filtering is linear so
  it has a matrix, but the matrix for a modest 64×64 picture already has sixteen million entries.
  The error says to filter the picture directly instead of building the thing that would do it.
- **`deconvlucy` is accelerated by Biggs and Andrews' vector extrapolation**, as MATLAB's is. Two
  successive corrections that point the same way are read as a direction and stepped further along,
  which changes how fast the same answer is reached and not what the answer is; a given iteration
  count therefore lands further along the path than plain Richardson–Lucy would. `SUBSMPL` is not
  implemented — the spread function must be given at the picture's own resolution — and a value
  other than one is an error rather than a silent single-rate run.
- **`deconvreg` solves for its multiplier by bisecting in the log.** The residual a given multiplier
  leaves is monotone in it, so there is exactly one multiplier at which the residual equals the noise
  power the caller stated, and it is found rather than guessed. With no noise stated the answer is the
  bottom of the range, which is inverse filtering with just enough regularization to stay finite.
- **`deconvblind` improves the picture and the blur in turn, sharing one ratio between the two
  half-steps.** Both sequences are extrapolated. A guess that is far wider than the true blur lets the
  picture over-sharpen faster than the blur can narrow, so the useful reading is after a few rounds
  rather than a few hundred; handed the true blur it stays on it and reduces to `deconvlucy`.
- **`edgetaper` uses the spread function as given.** Every spread function in this family sums to one,
  and one that does not will change the border's brightness rather than only its sharpness.
- **`gabor` returns one tagged struct for one filter and a cell of them for a bank**, so
  `g = gabor(4, 90); g.Wavelength` reads the way it does in MATLAB rather than needing `g{1}`.
  `class(g)` answers `'gabor'`.
- **`imgaborfilt`'s envelope is normalized to unit volume.** MathWorks does not document the scaling
  of its kernel, so magnitudes agree in shape and in relative strength across a bank, but an absolute
  magnitude is not comparable between the two. The filter is applied by convolution with the border
  extended by repetition, and `imgaborfilt(I, wavelength, orientation)` takes one of each — a bank
  goes through `gabor`, which is where MATLAB puts it too.
- **The metrics are quoted in the units the dialect quotes.** `immse` on a pair of `uint8` pictures
  answers in grey levels squared, as MATLAB's does; the same pair converted to double answers in the
  same units divided by 255², which is the same measurement in different units and not a different
  answer. `psnr` divides by the peak the class can hold, so it lands on the same decibel figure
  either way, which is the property that makes it worth quoting at all. Under the JGS dialect
  everything stays in the documented `[0, 1]` (ADR 0028), so a JGS script's `immse` reads as though
  the picture were double — which it is, there.
- **`ssim` and `multissim` score a colour picture channel by channel and average.** MATLAB filters
  the three planes as one volume, so its window reaches across channels; here each channel is scored
  on its own. The two agree exactly for a grey picture and differ in the third decimal for a colour
  one. `ssim`'s second output is the map for a grey picture and an `h×w×3` stack for a colour one.
- **`multissim` downsamples with a two-by-two average**, which is what the multiscale method was
  published with, rather than with `imresize`. The point of the low-pass is to stop the next level
  seeing aliases of detail it is meant to have left behind, not to look good, and MathWorks does not
  document which filter it uses. The default scale weights are the published ones.
- **`bfscore`'s outline is the set of region pixels that touch something outside the region**, with
  the picture's own border counting as outside. MATLAB does not document its boundary extraction, and
  the tolerance — three quarters of one percent of the diagonal, as MATLAB documents — is what makes
  the choice immaterial for anything but an exact match.
- **`dice` and `jaccard` read a pair of maps as masks when nothing in either exceeds one**, and as
  label maps otherwise, answering once per label. A label neither map used scores one, since two
  empty regions agree perfectly and any other reading would drag the average down for a label that
  was never in question.
- **`graycomatrix`'s default grey limits depend on the class**, as MATLAB's do: an integer picture is
  quantized against everything its class can hold, so two exposures of one scene give comparable
  tables, and a floating-point one falls back to its own extremes. `'GrayLimits', []` asks for the
  picture's own range explicitly.
- **`improfile` always returns the profile and never draws it.** MATLAB plots when it is called with
  no output arguments; a builtin here cannot see that it was called for its effect rather than its
  value, so the drawing form is absent. `imcontour`, `montage` and `imshowpair` are display verbs and
  return nothing, which is the same distinction drawn the other way.
- **`montage` takes a cell of pictures, a four-dimensional stack, or a three-dimensional one.** The
  third of those is not a MATLAB form — MATLAB wants `h×w×1×n` — but `h×w×n` is what a script that
  built a stack of grey pictures actually has, and refusing it would be pedantry. Filenames are not
  accepted; read the pictures first.
- **`imfuse` and `imshowpair` pad to the larger of two different sizes** rather than taking a pair of
  spatial references, and `imfuse` returns the picture alone. MATLAB's second output is an `imref2d`
  for the fused frame, which is only meaningful once the two pictures carry world coordinates.
- **The `ipt*` preferences last for the session, not for the user.** MATLAB's survive a restart;
  these do not, because every preference here changes only how a picture is shown and a persisted
  copy would be one more thing to migrate. `iptgetpref` with no argument answers a struct of all
  seven, and a name that is not one of them is an error listing the ones that are.
- **A volume is a plain N-D array, and the volume functions refuse an image value.** MATLAB has one
  numeric array type and so draws no line here at all. JGraph has a distinct image value, and an
  image and a volume are both three-dimensional arrays of numbers that mean opposite things by the
  third dimension — colour against depth. Passing an image to `medfilt3` therefore errors rather than
  filtering its channels as though they were slices. The one place the ambiguity remains is a plain
  `h×w×3` array: `bwconncomp` and `bwareaopen` read that as a volume, because MATLAB documents them
  as N-D and a mask has no colour to read, while `padarray` reads it as a volume only when the pad
  size names all three dimensions. Under JGS a volume is built with `reshape(zeros(1, n), [h w d])`
  rather than `zeros(h, w, d)`: ADR 0028 keeps the JGS constructors at their documented one- and
  two-argument forms, where a single size is a length rather than a square, and the volume functions
  did not seem like a reason to move that.
- **`fspecial3('laplacian')`'s two shape parameters weight the edge and corner neighbours.** MATLAB
  documents the arguments as controlling the operator's shape in `[0, 1]` but not the formula. Here
  the six face neighbours weigh 1, the twelve edge neighbours γ₁, the eight corner neighbours γ₂, the
  centre the negated total, and the kernel is divided by `1 + γ₁ + γ₂`. At the default of zero it is
  the plain six-neighbour second difference, and it sums to zero at every setting.
- **`fspecial3('ellipsoid')` grades its boundary by supersampling** — each voxel's weight is the
  share of it inside the ellipsoid, estimated on a 3×3×3 subgrid — rather than by MATLAB's
  unpublished antialiasing. The kernel sums to one either way; individual boundary weights will
  differ in the third decimal.
- **`imsegkmeans3` seeds deterministically.** The initial centres are spread evenly through the value
  range instead of being drawn at random, so the same volume always segments the same way. MATLAB
  runs `'NumAttempts'` random restarts and keeps the best; that option is accepted and ignored, since
  with a fixed start there is nothing to restart.
- **`obliqueslice` chooses the in-plane axes and states the rule.** A plane has no preferred direction
  within itself, so the two axes of the returned slice are a choice MATLAB does not document. Here
  they are built from whichever coordinate axis the normal leans on least, ordered so that
  `(u, v, normal)` is right-handed — which makes a cut along the plane axis come back the same way up
  as the plane itself, and leaves any other orientation rotated within its own frame by an amount
  that depends on the normal.
- **`regionprops3` returns a table with one column per number and omits what a column cannot hold.**
  That is the same rule `regionprops` follows in JGS, applied in both dialects here because MATLAB's
  own `regionprops3` returns a table too. `EigenVectors`, `VoxelList`, `VoxelIdxList`, `VoxelValues`
  and `Image` are measured but not returned, and the convex-hull trio — `ConvexHull`, `ConvexImage`,
  `ConvexVolume` and the `Solidity` derived from them — is absent outright, because a three
  dimensional hull is a different algorithm from the two-dimensional one wave G built.
- **`regionprops3`'s `SurfaceArea` counts outward-facing voxel faces**, so a 4×4×4 cube measures 96.
  MATLAB estimates the area of a smoothed surface through the voxels instead, which reads lower for
  anything curved. The count here is exact for what it measures and is comparable between regions of
  the same shape; it is not comparable with MATLAB's number.
- **`edge3`'s Sobel form takes `'alpha'` as a smoothing width.** MATLAB documents it as a smoothing
  factor without giving units; here it is the standard deviation of a Gaussian applied before the
  gradient, and zero means no smoothing at all.
- **`multissim3` filters with a spherical Gaussian and downsamples by a 2×2×2 average**, the direct
  reading of the two-dimensional method rather than a separately published three-dimensional one.

## Answers corrected while building this mirror

Not divergences — places where JGraph was giving a different answer from MATLAB and now does not.
Each changed an existing result, so each is written down.

- **`imrotate` turned the wrong way** (M46 wave C). The inverse map's sine terms had the sign that
  reads as a clockwise turn on screen, so `imrotate([1 2; 3 4], 90)` gave `[3 1; 4 2]` where MATLAB
  gives `[2 4; 1 3]`. Multiples of 180° were unaffected, which is why it survived this long. The
  default method also moved from bilinear to `'nearest'`, which is MATLAB's.
- **`imresize` sampled on the wrong grid** (M46 wave C). It mapped output pixel centres onto input
  corners; MATLAB samples output pixel `x` at `x/scale + ½(1 − 1/scale)`, folding past the border
  through the mirror `[1…n, n…1]`. The default method also moved from bilinear to `'bicubic'`, and
  a scale now rounds the output size up rather than to nearest, both of which are MATLAB's.
- **`imnoise`'s Gaussian noise read its mean as a variance** (M46 wave E). MATLAB's third argument is
  the mean and its fourth the variance, so `imnoise(I, 'gaussian', 0.1)` shifts the picture by a tenth
  and leaves the variance at its 0.01 default. Here the third argument was taken as the variance and
  the mean was pinned at zero, which meant that call added roughly three times the noise it should
  have and no offset at all.
- **`histeq` equalized with a plain cumulative distribution** (M46 wave E). MATLAB does not map each
  level to its own cumulative fraction; it picks, for each of 256 input levels, the output level whose
  cumulative count comes closest — a minimization, because a discrete histogram can rarely be reshaped
  exactly. The two agree on a well-populated picture and differ at the ends and wherever a level holds
  a large share. Stating it as MATLAB does is also what makes `histeq(I, hgram)` and the second output
  `T` possible at all, since both are the same machinery.
- **An option that documents "a size or a pair" refused the size** (M46 wave E). `'FilterSize', 5`
  errored where `'FilterSize', [5 5]` worked, because the reader behind every such option asked for an
  array. It has taken a bare number since wave B's `imgaussfilt`; this is the first wave whose own
  tests exercised the scalar form.
- **`imdilate` did not reflect the structuring element** (M46 wave F). Dilation is the Minkowski sum:
  laying the element down on each foreground pixel, offsets and all. Reading the neighbourhood as
  given instead is correlation, which gathers from the opposite side — so an element extending to the
  right moved the picture left. Every element that existed before this wave was symmetric (the 3×3
  square, the disk), so the two agreed and nothing showed it; `strel('line', 5, 30)` is the first
  argument that can tell them apart. Erosion is unchanged: it is the one that does not reflect, and
  the reflection is exactly what makes the two duals of each other.
- **`&` and `|` dropped an array's shape** (M46 wave G). Every other elementwise operator carried it
  — `A > 2` on a 4-by-6 picture gave a 4-by-6 mask — but the two logical ones handed back a flat row
  of twenty-four bools. That made `(X - a).^2 + (Y - b).^2 <= r | ...` , which is how a script draws
  two discs, produce something no imaging function could read as a picture. This is a base-language
  builtin rather than a toolbox one; it is recorded here because the imaging work is what found it.
- **`imshow` refused a plain matrix** (M46 wave G), pointing at `imagesc` instead. MATLAB draws no
  line between a picture and a matrix, and by this wave nothing else in the imaging surface did
  either — `imbinarize` of a matrix gives a matrix, and showing it is the obvious next line.
- **`imfindcircles`' votes went the wrong way** (M46 wave G, before release). At the left edge of a
  bright disc the intensity rises to the right, so the gradient points at the centre and the centre
  lies a radius along `+∇`. The sign was inverted, which put every vote a full diameter out on the
  far side — a ring of four phantom centres round each real circle rather than nothing at all, which
  is why it looked like a result. Found by the test that asks what the wrong polarity should do.
- **`repmat` ignored all but its last count** (M46 wave C). It predates shaped arrays (M40) and laid
  the copies end to end, so `repmat([1 2; 3 4], 2, 1)` came back as a flat four-element row and
  `repmat(A, 3, 1)` did not repeat at all. It now tiles in two dimensions. This one is a base-MATLAB
  builtin rather than a toolbox function; it is recorded here because the imaging work is what found
  it.
- **A picture could not be sliced** (M46 wave L). Every subscript slot on an image value had to be a
  single number, so `BW(:, 19:22)` on a mask an imaging builtin had just returned was an error while
  the same expression on the matrix that produced it worked. Reads now go through the same
  subscript-resolution path an array uses, so a range, a mask or `:` selects a submatrix and only a
  single-sample selection comes back as a number. `img(:)` and the one-based, native-scale scalar
  read from wave A are unchanged.
- **`cat` refused any dimension past the second** (M46 wave L). `cat(3, R, G, B)` is how MATLAB
  documents building a colour picture out of its planes, and it is what wave K's own error message
  tells a script to do to build a volume — so the message was pointing at something that did not
  work. It now joins along any dimension. This is a base-MATLAB builtin; it is recorded here because
  the imaging surface is what depends on it.
- **Nine point and threshold functions refused a plain matrix** (M46 wave L). `imhist`, `graythresh`,
  `stretchlim`, `adaptthresh`, `imbinarize`, `imadjust`, `imcomplement`, `imabsdiff`, `imapplymatrix`,
  `im2gray`, `imcrop`, `imcentroid`, `bwareaopen` and `imwrite` all required a picture value where
  the hundred-odd functions around them took either. MATLAB draws no such line, and wave G had
  already had to fix the same thing for `imshow`. A matrix in now means a matrix out, and colour
  planes in mean colour planes out.
- **A submatrix write walked off the end of its own storage** (M46 wave L). A JGS matrix is stored as
  an array of row arrays; the two-subscript write path computed a flat column-major slot regardless,
  which indexed the list of rows. `A(1:2, 1:2) = 5` on a JGS 4×4 threw an `IndexOutOfRangeException`
  out of the interpreter rather than raising a script error, and any selection reaching past the row
  count did the same. The read path had always known both storage forms; now the write path does too.
  This is base-language rather than toolbox, and it was found by writing the milestone's own script.
