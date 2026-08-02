# MATLAB Image Processing Toolbox coverage

**120 of 409 documented** Image Processing Toolbox names are implemented, as of M46 wave D.

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

## Implemented — 120
`adaptthresh`, `affine2d`, `affineOutputView`, `bestblk`, `blockproc`, `bwareaopen`
`bwlabel`, `checkerboard`, `chromadapt`, `cmap2gray`, `col2im`, `colfilt`
`colorangle`, `deltaE`, `demosaic`, `edge`, `entropyfilt`, `fft2`
`fftshift`, `fitgeotrans`, `fspecial`, `gray2ind`, `graythresh`, `histeq`
`hough`, `houghlines`, `houghpeaks`, `hsv2rgb`, `ifft2`, `ifftshift`
`illumgray`, `illumpca`, `illumwhite`, `im2col`, `im2double`, `im2gray`
`im2int16`, `im2single`, `im2uint16`, `im2uint8`, `imabsdiff`, `imadd`
`imadjust`, `imapplymatrix`, `imapprox`, `imbinarize`, `imboxfilt`, `imclose`
`imcolordiff`, `imcomplement`, `imcrop`, `imdilate`, `imdivide`, `imerode`
`imfill`, `imfilter`, `imfinfo`, `imgaussfilt`, `imgradient`, `imgradientxy`
`imhist`, `imlincomb`, `immultiply`, `imnoise`, `imopen`, `impyramid`
`imread`, `imref2d`, `imresize`, `imrotate`, `imshow`, `imsplit`
`imsubtract`, `imtranslate`, `imwarp`, `imwrite`, `ind2gray`, `ind2rgb`
`integralBoxFilter`, `integralImage`, `intlut`, `lab2double`, `lab2rgb`, `lab2uint16`
`lab2uint8`, `lab2xyz`, `lin2rgb`, `mat2gray`, `medfilt2`, `modefilt`
`nlfilter`, `ntsc2rgb`, `ordfilt2`, `otsuthresh`, `padarray`, `projective2d`
`rangefilt`, `regionprops`, `rgb2gray`, `rgb2hsv`, `rgb2ind`, `rgb2lab`
`rgb2lightness`, `rgb2lin`, `rgb2ntsc`, `rgb2xyz`, `rgb2ycbcr`, `rigid2d`
`stdfilt`, `strel`, `stretchlim`, `transformPointsForward`, `transformPointsInverse`, `whitepoint`
`wiener2`, `xyz2double`, `xyz2lab`, `xyz2rgb`, `xyz2uint16`, `ycbcr2rgb`

## Not implemented — 146
Planned across M46 waves E–K. Nothing here is refused on principle; it is work not yet done.

`activecontour`, `adapthisteq`, `applylut`, `bfscore`, `boundarymask`, `bwarea`
`bwareafilt`, `bwboundaries`, `bwconncomp`, `bwconvhull`, `bwdist`, `bwdistgeodesic`
`bweuler`, `bwferet`, `bwhitmiss`, `bwlabeln`, `bwlookup`, `bwmorph`
`bwmorph3`, `bwperim`, `bwpropfilt`, `bwselect`, `bwselect3`, `bwskel`
`bwtraceboundary`, `bwulterode`, `conndef`, `convmtx2`, `corr2`, `dct2`
`dctmtx`, `deconvblind`, `deconvlucy`, `deconvreg`, `deconvwnr`, `decorrstretch`
`dice`, `edge3`, `edgetaper`, `entropy`, `fibermetric`, `freqspace`
`freqz2`, `fsamp2`, `fspecial3`, `ftrans2`, `fwind1`, `fwind2`
`gabor`, `gradientweight`, `graycomatrix`, `grayconnected`, `graycoprops`, `graydiffweight`
`graydist`, `grayslice`, `idct2`, `imadjustn`, `imbilatfilt`, `imbothat`
`imboxfilt3`, `imclearborder`, `imcontour`, `imcrop3`, `imdiffuseest`, `imdiffusefilt`
`imextendedmax`, `imextendedmin`, `imfindcircles`, `imflatfield`, `imfuse`, `imgaborfilt`
`imgaussfilt3`, `imgradient3`, `imgradientxyz`, `imguidedfilter`, `imhistmatch`, `imhistmatchn`
`imhmax`, `imhmin`, `imimposemin`, `imlocalbrighten`, `immse`, `imnlmfilt`
`imoverlay`, `impixel`, `improfile`, `imquantize`, `imreconstruct`, `imreducehaze`
`imregcorr`, `imregionalmax`, `imregionalmin`, `imresize3`, `imrotate3`, `imsegfmm`
`imsegkmeans`, `imsegkmeans3`, `imsharpen`, `imshowpair`, `imtophat`, `integralBoxFilter3`
`integralImage3`, `iptcheckconn`, `iptgetpref`, `iptsetpref`, `iradon`, `jaccard`
`label2idx`, `label2rgb`, `labelmatrix`, `labeloverlay`, `makelut`, `maxhessiannorm`
`mean2`, `medfilt3`, `montage`, `multissim`, `multissim3`, `multithresh`
`normxcorr2`, `obliqueslice`, `offsetstrel`, `otf2psf`, `phantom`, `poly2label`
`poly2mask`, `psf2otf`, `psnr`, `qtdecomp`, `qtgetblk`, `qtsetblk`
`radon`, `reducepoly`, `regionfill`, `regionprops3`, `roicolor`, `roifilt2`
`roipoly`, `ssim`, `std2`, `superpixels`, `superpixels3`, `visboundaries`
`viscircles`, `watershed`

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
- **`repmat` ignored all but its last count** (M46 wave C). It predates shaped arrays (M40) and laid
  the copies end to end, so `repmat([1 2; 3 4], 2, 1)` came back as a flat four-element row and
  `repmat(A, 3, 1)` did not repeat at all. It now tiles in two dimensions. This one is a base-MATLAB
  builtin rather than a toolbox function; it is recorded here because the imaging work is what found
  it.
