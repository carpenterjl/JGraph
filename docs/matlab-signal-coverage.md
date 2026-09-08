# MATLAB Signal Processing Toolbox coverage

Where JGraph stands against the Signal Processing Toolbox of R2024a: the 351 public names under
`toolbox/signal/signal`, harvested by `tools/matlab-checklist/build-signal-csv.py` into
`matlab-r2024a-signal.csv` (name, toc section, kind, documented call forms). Checked by
`verify-signal-coverage.py`, which refuses a name in no bucket, in two buckets, called implemented
without a catalog entry, or registered without being called implemented.

## Where it stands

**217 of 351 documented names implemented**; 59 are planned or deferred (M137, and the four
M136 left behind; ADR 0126 holds the plan and ADR 0140 says why the four were left), two are the complex equiripple exchange that M134 did not
write (ADR 0138), and 73 are excluded by name.

| Bucket | Names |
|---|---:|
| Implemented | 217 |
| Not implemented, M134 | 2 |
| Not implemented, M136 | 4 |
| Not implemented, M137 | 55 |
| Excluded | 73 |
| **Total** | **351** |

A name is *implemented* when every call form its help block documents (the `forms` column of the
CSV) is accepted and its parity fixture under `tests/JGraph.Tests/MatlabParity` passes against the
recorded R2024a output. Names MATLAB keeps outside this folder but the plan implements beside these
(`chirp`, `db2mag`, `mag2db`, `tf2zp`, `zp2tf`, `ss2tf`, `tf2ss`, `zp2ss`, `ss2zp`, `ellipap`,
`freqspace`, `xcov`, `emd`, `hht`, `wvd`, `xwvd`) are counted by the toolbox-function doc, not here.

## Implemented

`alignsignals`, `bandpass`, `bandpower`, `bandstop`, `barthannwin`, `bartlett`, `besselap`, `besself`, `bilinear`, `bitrevorder`, `blackman`, `blackmanharris`, `bohmanwin`, `boxcar`, `buffer`, `buttap`, `butter`, `buttord`, `cceps`, `cconv`, `cell2sos`, `cheb1ap`, `cheb1ord`, `cheb2ap`, `cheb2ord`, `chebwin`, `cheby1`, `cheby2`, `cohere`, `convmtx`, `corrmtx`, `cpsd`, `csd`, `cusum`, `czt`, `datawrap`, `db`, `db2pow`, `dct`, `decimate`, `demod`, `designfilt`, `dftmtx`, `digitalFilter`, `digitrevorder`, `diric`, `downsample`, `dpss`, `dtw`, `dutycycle`, `edr`, `ellip`, `ellipord`, `enbw`, `envelope`, `eqtflength`, `falltime`, `fftfilt`, `fillgaps`, `filternorm`, `filtfilt`, `filtic`, `filtord`, `filtstates`, `findchangepts`, `finddelay`, `findpeaks`, `findsignal`, `fir1`, `fir2`, `fircls`, `fircls1`, `firgauss`, `firls`, `firpm`, `firpmord`, `firrcos`, `firtype`, `flattopwin`, `framesig`, `freqs`, `freqz`, `fwht`, `gauspuls`, `gaussdesign`, `gaussfir`, `gausswin`, `gmonopuls`, `goertzel`, `grpdelay`, `hamming`, `hampel`, `hann`, `hanning`, `highpass`, `hilbert`, `icceps`, `idct`, `ifwht`, `impinvar`, `impz`, `impzlength`, `interp`, `intfilt`, `isallpass`, `islinphase`, `ismaxphase`, `isminphase`, `isstable`, `kaiser`, `kaiserord`, `latc2tf`, `latcfilt`, `lowpass`, `lp2bp`, `lp2bs`, `lp2hp`, `lp2lp`, `marcumq`, `maxflat`, `meanfreq`, `medfilt1`, `medfreq`, `midcross`, `modulate`, `mscohere`, `nuttallwin`, `obw`, `overshoot`, `parzenwin`, `pburg`, `pcov`, `peak2peak`, `peak2rms`, `peig`, `periodogram`, `phasedelay`, `phasez`, `plomb`, `pmcov`, `pmem`, `pmtm`, `pmusic`, `polyscale`, `polystab`, `pow2db`, `powerbw`, `psd`, `pulseperiod`, `pulsesep`, `pulsewidth`, `pulstran`, `pwelch`, `pyulear`, `rceps`, `rcosdesign`, `rectpuls`, `rectwin`, `remez`, `remezord`, `resample`, `residuez`, `risetime`, `rooteig`, `rootmusic`, `rssq`, `sawtooth`, `scaleFilterSections`, `seqperiod`, `settlingtime`, `sfdr`, `sgolay`, `sgolayfilt`, `shiftdata`, `sinad`, `sinc`, `slewrate`, `snr`, `sos2cell`, `sos2ctf`, `sos2ss`, `sos2tf`, `sos2zp`, `sosfilt`, `specgram`, `spectrum`, `square`, `ss2sos`, `statelevels`, `stepz`, `taylorwin`, `tf2latc`, `tf2sos`, `tf2zpk`, `tfe`, `tfestimate`, `thd`, `toi`, `triang`, `tripuls`, `tukeywin`, `udecode`, `uencode`, `undershoot`, `unshiftdata`, `upfirdn`, `upsample`, `vco`, `window`, `xcorr2`, `yulewalk`, `zerocrossrate`, `zerophase`, `zp2ctf`, `zp2sos`, `zplane`, `zplaneplot`

## Not implemented

### M134: the complex equiripple exchange (2)

`cfirpm`, `cremez`

### M136: the four that rest on pspectrum and on the octave filter bank (4)

`instbw`, `instfreq`, `poctave`, `pspectrum`

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
