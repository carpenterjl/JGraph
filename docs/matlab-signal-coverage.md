# MATLAB Signal Processing Toolbox coverage

Where JGraph stands against the Signal Processing Toolbox of R2024a: the 351 public names under
`toolbox/signal/signal`, harvested by `tools/matlab-checklist/build-signal-csv.py` into
`matlab-r2024a-signal.csv` (name, toc section, kind, documented call forms). Checked by
`verify-signal-coverage.py`, which refuses a name in no bucket, in two buckets, called implemented
without a catalog entry, or registered without being called implemented.

## Where it stands

**6 of 351 documented names implemented**; 272 are planned across six milestones
(M132-M137; ADR 0126 holds the plan) and 73 are excluded by name.

| Bucket | Names |
|---|---:|
| Implemented | 6 |
| Not implemented, M132 | 55 |
| Not implemented, M133 | 36 |
| Not implemented, M134 | 55 |
| Not implemented, M135 | 6 |
| Not implemented, M136 | 65 |
| Not implemented, M137 | 55 |
| Excluded | 73 |
| **Total** | **351** |

A name is *implemented* when every call form its help block documents (the `forms` column of the
CSV) is accepted and its parity fixture under `tests/JGraph.Tests/MatlabParity` passes against the
recorded R2024a output. Names MATLAB keeps outside this folder but the plan implements beside these
(`chirp`, `db2mag`, `mag2db`, `tf2zp`, `zp2tf`, `ss2tf`, `tf2ss`, `zp2ss`, `ss2zp`, `ellipap`,
`freqspace`, `xcov`, `emd`, `hht`, `wvd`, `xwvd`) are counted by the toolbox-function doc, not here.

## Implemented

`butter`, `db`, `dct`, `firpm`, `freqz`, `idct`

## Not implemented

### M132: Windows, waveform generators, transforms, unit conversions (55)

`barthannwin`, `bartlett`, `bitrevorder`, `blackman`, `blackmanharris`, `bohmanwin`, `boxcar`, `buffer`, `cceps`, `chebwin`, `czt`, `datawrap`, `db2pow`, `demod`, `dftmtx`, `digitrevorder`, `diric`, `dpss`, `flattopwin`, `framesig`, `fwht`, `gauspuls`, `gausswin`, `gmonopuls`, `goertzel`, `hamming`, `hann`, `hanning`, `hilbert`, `icceps`, `ifwht`, `kaiser`, `marcumq`, `modulate`, `nuttallwin`, `parzenwin`, `pow2db`, `pulstran`, `rceps`, `rectpuls`, `rectwin`, `sawtooth`, `seqperiod`, `shiftdata`, `sinc`, `square`, `taylorwin`, `triang`, `tripuls`, `tukeywin`, `udecode`, `uencode`, `unshiftdata`, `vco`, `window`

### M133: Filtering, coefficient conversions, multirate (36)

`cell2sos`, `decimate`, `downsample`, `envelope`, `eqtflength`, `fftfilt`, `fillgaps`, `filtfilt`, `filtic`, `filtstates`, `hampel`, `interp`, `latc2tf`, `latcfilt`, `medfilt1`, `polyscale`, `polystab`, `resample`, `residuez`, `scaleFilterSections`, `sgolay`, `sgolayfilt`, `sos2cell`, `sos2ctf`, `sos2ss`, `sos2tf`, `sos2zp`, `sosfilt`, `ss2sos`, `tf2latc`, `tf2sos`, `tf2zpk`, `upfirdn`, `upsample`, `zp2ctf`, `zp2sos`

### M134: Filter design and analysis (55)

`besselap`, `besself`, `bilinear`, `buttap`, `buttord`, `cfirpm`, `cheb1ap`, `cheb1ord`, `cheb2ap`, `cheb2ord`, `cheby1`, `cheby2`, `cremez`, `ellip`, `ellipord`, `filternorm`, `filtord`, `fir1`, `fir2`, `fircls`, `fircls1`, `firgauss`, `firls`, `firpmord`, `firrcos`, `firtype`, `freqs`, `gaussdesign`, `gaussfir`, `grpdelay`, `impinvar`, `impz`, `impzlength`, `intfilt`, `isallpass`, `islinphase`, `ismaxphase`, `isminphase`, `isstable`, `kaiserord`, `lp2bp`, `lp2bs`, `lp2hp`, `lp2lp`, `maxflat`, `phasedelay`, `phasez`, `rcosdesign`, `remez`, `remezord`, `stepz`, `yulewalk`, `zerophase`, `zplane`, `zplaneplot`

### M135: `designfilt`, the `digitalFilter` value, and the four one-line filters (6)

`bandpass`, `bandstop`, `designfilt`, `digitalFilter`, `highpass`, `lowpass`

### M136: Spectral estimation and measurements (65)

`alignsignals`, `bandpower`, `cconv`, `cohere`, `convmtx`, `corrmtx`, `cpsd`, `csd`, `cusum`, `dtw`, `dutycycle`, `edr`, `enbw`, `falltime`, `findchangepts`, `finddelay`, `findpeaks`, `findsignal`, `instbw`, `instfreq`, `meanfreq`, `medfreq`, `midcross`, `mscohere`, `obw`, `overshoot`, `pburg`, `pcov`, `peak2peak`, `peak2rms`, `peig`, `periodogram`, `plomb`, `pmcov`, `pmem`, `pmtm`, `pmusic`, `poctave`, `powerbw`, `psd`, `pspectrum`, `pulseperiod`, `pulsesep`, `pulsewidth`, `pwelch`, `pyulear`, `risetime`, `rooteig`, `rootmusic`, `rssq`, `settlingtime`, `sfdr`, `sinad`, `slewrate`, `snr`, `specgram`, `spectrum`, `statelevels`, `tfe`, `tfestimate`, `thd`, `toi`, `undershoot`, `xcorr2`, `zerocrossrate`

### M137: Time-frequency, signal modelling, vibration (55)

`ac2poly`, `ac2rc`, `arburg`, `arcov`, `armcov`, `aryule`, `envspectrum`, `fsst`, `ifsst`, `invfreqs`, `invfreqz`, `is2rc`, `iscola`, `istft`, `kurtogram`, `lar2rc`, `levinson`, `lpc`, `lsf2poly`, `modalfit`, `modalfrf`, `modalsd`, `orderspectrum`, `ordertrack`, `orderwaveform`, `pentropy`, `pkurtosis`, `poly2ac`, `poly2lsf`, `poly2rc`, `prony`, `rainflow`, `rc2ac`, `rc2is`, `rc2lar`, `rc2poly`, `rlevinson`, `rpmfreqmap`, `rpmordermap`, `rpmtrack`, `schurrc`, `spectralCrest`, `spectralEntropy`, `spectralFlatness`, `spectralKurtosis`, `spectralSkewness`, `spectrogram`, `stft`, `stftmag2sig`, `stmcb`, `strips`, `tachorpm`, `tfridge`, `tsa`, `xspectrogram`

## Excluded

Declined by name, so a later inventory counts them as decided rather than missing.

| Family | Names | Why |
|---|---|---|
| Internal helpers and toc pages | `aboutsignaltbx`, `bscost`, `ChkIfBlockReusable`, `completefreqresp`, `computepsd`, `crmz_grid`, `drawpznumbers`, `extract_phase`, `fastreshape`, `filt2block`, `filtdes`, `filterAnalysisOptions`, `filtgraph`, `findfreqvector`, `firpmmex`, `freqz_freqvec`, `freqzparse`, `freqzplot`, `genplotdata`, `getinterpfrequencies`, `getTranslatedString`, `getTranslatedStringcell`, `kratio`, `local_max`, `psdoptions`, `scopext`, `signalpolyutils`, `sigprivate`, `specplot`, `timezparse`, `tocanalogfilters`, `toccorrandconv`, `tocfilteranalysis`, `tocfilterdesign`, `tocfiltering`, `tocfilters`, `tocmeasurements`, `tocmultiratesignalproc`, `tocsiggenandpreprocess`, `tocsiggpu`, `tocsigmodeling`, `tocspectral`, `toctimefrequency`, `toctransforms`, `tocvibration`, `vratio` | not user-facing: no toc page lists them, and their help blocks say they serve another function |
| EDF files | `edfheader`, `edfinfo`, `edfread`, `edfwrite` | a medical file format, excluded with the DICOM family of the IPT doc |
| Signal ROI, labelling, datastores, feature extractors | `binmask2sigroi`, `extendsigroi`, `extractsigroi`, `framelbl`, `mergesigroi`, `removesigroi`, `scalarFeatureOptions`, `shortensigroi`, `signalDatastore`, `signalFrequencyFeatureExtractor`, `signalMask`, `signalTimeFeatureExtractor`, `signalTimeFrequencyFeatureExtractor`, `sigrangebinmask`, `sigroi2binmask`, `tall`, `timeFrequencyScalarFeatureOptions` | the machine-learning pipeline layer, excluded with the Stats model objects |
| `fdesign`, `cascade`, the Slepian-sequence database | `cascade`, `dpssclear`, `dpssdir`, `dpssload`, `dpsssave`, `fdesign` | `fdesign` is the DSP System Toolbox object tree, which is not installed; `cascade` builds its objects; the four `dpss*` names manage a disk cache of Slepian sequences |

## Keeping this current

1. Register the names, then move them from their milestone's list to **Implemented**, never the
   other way round; the verifier refuses a registered name that the doc still calls missing.
2. `python tools/matlab-checklist/verify-signal-coverage.py` must exit 0 before the commit.
3. `build-signal-csv.py` is rerun only when the MATLAB install changes; the CSV is committed.
