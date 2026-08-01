# MATLAB Image Processing Toolbox coverage

**55 of 409 documented** Image Processing Toolbox names are implemented, as of M46 wave A.

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

## Implemented — 55

`adaptthresh`, `bwareaopen`, `bwlabel`, `edge`, `fft2`, `fftshift`
`fspecial`, `graythresh`, `histeq`, `hough`, `houghlines`, `houghpeaks`
`ifft2`, `ifftshift`, `im2double`, `im2gray`, `im2int16`, `im2single`
`im2uint16`, `im2uint8`, `imabsdiff`, `imadd`, `imadjust`, `imapplymatrix`
`imbinarize`, `imclose`, `imcomplement`, `imcrop`, `imdilate`, `imdivide`
`imerode`, `imfill`, `imfilter`, `imfinfo`, `imgradient`, `imgradientxy`
`imhist`, `imlincomb`, `immultiply`, `imnoise`, `imopen`, `imread`
`imresize`, `imrotate`, `imshow`, `imsubtract`, `imwrite`, `intlut`
`mat2gray`, `medfilt2`, `otsuthresh`, `regionprops`, `rgb2gray`, `strel`
`stretchlim`

## Not implemented — 211

Planned across M46 waves B–K. Nothing here is refused on principle; it is work not yet done.

`activecontour`, `adapthisteq`, `affine2d`, `affineOutputView`, `applylut`, `bestblk`
`bfscore`, `blockproc`, `boundarymask`, `bwarea`, `bwareafilt`, `bwboundaries`
`bwconncomp`, `bwconvhull`, `bwdist`, `bwdistgeodesic`, `bweuler`, `bwferet`
`bwhitmiss`, `bwlabeln`, `bwlookup`, `bwmorph`, `bwmorph3`, `bwperim`
`bwpropfilt`, `bwselect`, `bwselect3`, `bwskel`, `bwtraceboundary`, `bwulterode`
`checkerboard`, `chromadapt`, `cmap2gray`, `col2im`, `colfilt`, `colorangle`
`conndef`, `convmtx2`, `corr2`, `dct2`, `dctmtx`, `deconvblind`
`deconvlucy`, `deconvreg`, `deconvwnr`, `decorrstretch`, `deltaE`, `demosaic`
`dice`, `edge3`, `edgetaper`, `entropy`, `entropyfilt`, `fibermetric`
`fitgeotrans`, `freqspace`, `freqz2`, `fsamp2`, `fspecial3`, `ftrans2`
`fwind1`, `fwind2`, `gabor`, `gradientweight`, `gray2ind`, `graycomatrix`
`grayconnected`, `graycoprops`, `graydiffweight`, `graydist`, `grayslice`, `hsv2rgb`
`idct2`, `illumgray`, `illumpca`, `illumwhite`, `im2col`, `imadjustn`
`imapprox`, `imbilatfilt`, `imbothat`, `imboxfilt`, `imboxfilt3`, `imclearborder`
`imcolordiff`, `imcontour`, `imcrop3`, `imdiffuseest`, `imdiffusefilt`, `imextendedmax`
`imextendedmin`, `imfindcircles`, `imflatfield`, `imfuse`, `imgaborfilt`, `imgaussfilt`
`imgaussfilt3`, `imgradient3`, `imgradientxyz`, `imguidedfilter`, `imhistmatch`, `imhistmatchn`
`imhmax`, `imhmin`, `imimposemin`, `imlocalbrighten`, `immse`, `imnlmfilt`
`imoverlay`, `impixel`, `improfile`, `impyramid`, `imquantize`, `imreconstruct`
`imreducehaze`, `imref2d`, `imregcorr`, `imregionalmax`, `imregionalmin`, `imresize3`
`imrotate3`, `imsegfmm`, `imsegkmeans`, `imsegkmeans3`, `imsharpen`, `imshowpair`
`imsplit`, `imtophat`, `imtranslate`, `imwarp`, `ind2gray`, `ind2rgb`
`integralBoxFilter`, `integralBoxFilter3`, `integralImage`, `integralImage3`, `iptcheckconn`, `iptgetpref`
`iptsetpref`, `iradon`, `jaccard`, `lab2double`, `lab2rgb`, `lab2uint16`
`lab2uint8`, `lab2xyz`, `label2idx`, `label2rgb`, `labelmatrix`, `labeloverlay`
`lin2rgb`, `makelut`, `maxhessiannorm`, `mean2`, `medfilt3`, `modefilt`
`montage`, `multissim`, `multissim3`, `multithresh`, `nlfilter`, `normxcorr2`
`ntsc2rgb`, `obliqueslice`, `offsetstrel`, `ordfilt2`, `otf2psf`, `padarray`
`phantom`, `poly2label`, `poly2mask`, `projective2d`, `psf2otf`, `psnr`
`qtdecomp`, `qtgetblk`, `qtsetblk`, `radon`, `rangefilt`, `reducepoly`
`regionfill`, `regionprops3`, `rgb2hsv`, `rgb2ind`, `rgb2lab`, `rgb2lightness`
`rgb2lin`, `rgb2ntsc`, `rgb2xyz`, `rgb2ycbcr`, `rigid2d`, `roicolor`
`roifilt2`, `roipoly`, `ssim`, `std2`, `stdfilt`, `superpixels`
`superpixels3`, `transformPointsForward`, `transformPointsInverse`, `visboundaries`, `viscircles`, `watershed`
`whitepoint`, `wiener2`, `xyz2double`, `xyz2lab`, `xyz2rgb`, `xyz2uint16`
`ycbcr2rgb`

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
