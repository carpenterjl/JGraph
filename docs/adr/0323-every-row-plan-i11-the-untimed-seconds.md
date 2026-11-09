# ADR 0323 — The every-row plan's I11: the untimed seconds in d02, d10 and d11

## Status

Accepted, 2026-11-08. Stage I11 of the every-row plan (`docs/plans/head2head-gui-every-row-plan.md`,
gitignored, rev 3). Measured on the working tree over 2ef216c5 (Release, `runs\i11-final2` in
`JGraph_demo_workspace\head2head_v3`, its manifest hashing the dirty files) against P0's numbers
(`runs\p0-8dbad495`, ADR 0322) on the i7-12700H, nothing else of this work running, the user's word that
the machine was idle.

## Context

P0 (ADR 0322) found that `d02_fft_signal`, `d11_datafun` and part of `d10_matfun` lose the GUI round
between their TIME rows, and named the lines: the windowed app's workstation collector; `ifft` answering
complex where R2025b answers real, so the round-trip check took the complex road; `isnan`, `~`, `sum` and
`nnz` over logicals at 50-500x R2025b; one index vector into a 2048-square matrix; and `trace` of a
3000-square. The plan asks for each script's wall under R2025b's GUI median (d02 1.52 s, d10 0.95 s,
d11 2.29 s) with every CHK line that agrees today unmoved, and the lines' before and after.

## Decision

The plan asked for one ADR per script; the six lines are shared code across the three scripts and land
together, so this ADR takes all three, with each line under the script it moved.

### The app's collector

`src/JGraph.Application/runtimeconfig.template.json` gives the windowed app Server GC, still concurrent,
as `jgraph.exe` has had since M93. Measured with the I11 code in the app (warm-up and three passes, the
instrumented copies, medians; the other two collectors by the runtime's own environment variables over
the same build):

| collector | d02 | d10 | d11 | peak working set | working set after the run |
| --- | --- | --- | --- | --- | --- |
| Server (the new default) | 0.95 | 0.80 | 1.99 | 5.8 GB | 5.2 GB |
| workstation (`DOTNET_gcServer=0`, before) | 0.93 | 0.84 | 2.17 | 3.5 GB | 2.5 GB |
| Server with DATAS (`DOTNET_GCDynamicAdaptationMode=1`) | 1.00 | 0.82 | 2.15 | 3.2 GB | 2.1 GB |

The three runs of the measurement (`i11-wip`, `i11-final`, `i11-final2`) agree: Server GC is 0.14-0.25 s
faster on d11 than the other two, d02 and d10 are within noise, and Server GC holds 2-3 GB more after a
script. With the boxing below gone, the collector is no longer what decides the walls: every collector
puts all three under R2025b's GUI medians. Server GC is kept as the plan proposed; DATAS is the
alternative if the memory matters more than d11's margin (one line in the template).

### d02_fft_signal (app 3.34 s → 0.95 s; original script in the app 0.87 s; R2025b 1.52 s)

- **`ifft` answers real for a conjugate-symmetric input, as R2025b does.** `ifft`, `ifft2` and `ifftn`
  test the input before transforming (`JGraph.Numerics.ConjugateSymmetry`): `ifft` every slice along the
  transform dimension, padded or cut to the transform length, `ifft2`/`ifftn` the whole array through the
  origin of the dimensions they transform; exact, element against element, a NaN never equal. When it
  holds the answer is the `'symmetric'` one, real, with no word given and with `'nonsymmetric'` given
  too (R2025b's own behaviour, recorded). The long single slice is tested across the cores; a proven
  symmetric input skips the mirroring pass and the clear of an imaginary plane nobody keeps.
- **A real input's `fft`, `fft2` and `fftn` are exactly conjugate-symmetric.** R2025b's are at every
  size measured (8 to 4,194,304, 2-D to 2048 square, odd sides, 3-D), and JGraph's were symmetric only to
  rounding, so `ifft(fft(x))` never passed the test. Each pair `X(k)`, `X(n-k)` is now replaced by its
  symmetric part (`FftKernels.Symmetrize`, `ConjugateSymmetry.Force` across dimensions), which drops
  rounding alone; keeping the lower half and copying it over the upper made `d02_roundtrip` 2.66e-15 where
  the pair's average makes it 1.67e-15 (1.79e-15 before, R2025b 1.78e-15; the line differed before and
  differs now).
- **Complex `+` and `-` keep their planes** (`PackedOps.TryComplexAddSubtract`): a packed complex array
  with a packed complex or real array of the same shape, or with a real or complex scalar; each element
  the boxed road's own arithmetic, an answer with no imaginary part anywhere minted real. Complex minus
  real over 4M was 0.91 s, R2025b's 0.003.
- **`abs` of a complex array runs across the cores** (the per-element call unchanged, so the bits are).
- **`M(rows, cols)` with index vectors gathers packed** (`Interpreter.TryPickGather`), real or complex,
  column by column across the cores; the general road built a boxed value per element.

| line (app / headless, s) | P0 | I11 | R2025b `-batch` |
| --- | --- | --- | --- |
| the `d02_roundtrip` CHK, `max(abs(back - sig))` | 2.156 / 0.621 | 0.012 / 0.011 | 0.007 |
| the quadrant swap `Fs([K/2+1:K 1:K/2], [K/2+1:K 1:K/2])` | 0.373 / 0.110 | 0.003 / 0.003 | 0.010 |
| `abs` of the 4M spectrum (probe) | — / 0.017-0.021 | — / 0.004-0.006 | 0.004 |

### d11_datafun (app 3.74 s → 1.99 s; original script in the app 1.98 s; R2025b 2.29 s)

- **A logical mask stays packed** (`PackedMath.Mask`, `JgsBuiltins.TryPackedMask`): `isnan`, `~`,
  `isinf`, `isfinite`, `logical`, `cast(…, 'logical')` and `ismissing` over a packed array answer a
  packed logical array from a vector kernel across the cores; every other `MapToBool` caller reads the
  storage where it lies on its own thread (a test may read the session). The boxed answer had made every
  `sum`, `nnz` and mask gather after it boxed too; with a packed mask they take their existing fast paths.

| line (app / headless, s) | P0 | I11 | R2025b `-batch` |
| --- | --- | --- | --- |
| the `d11_discretize` CHK, `sum(bins(~isnan(bins)))` | 1.100 / 0.401 | 0.040 / 0.039 | 0.057 |
| `nmiss = sum(isnan(g))` | 0.614 / 0.205 | 0.006 / 0.007 | 0.004 |

What d11 still spends untimed is `histogram(y, 120)` (0.29 s against 0.08; open item 252 and I10).

### d10_matfun (app 1.29 s → 0.80 s; original script in the app 0.79 s; R2025b 0.95 s)

- **`trace` of a packed square matrix reads its diagonal** (`TryPackedTrace`), summed from zero in index
  order, the boxed sum's bits; the boxed road copied the whole matrix into a `Complex[,]` first.

| line (app / headless, s) | P0 | I11 | R2025b `-batch` |
| --- | --- | --- | --- |
| the builders' nine CHK lines | 0.363 / 0.353 | 0.001 / 0.002 | 0.001 |

d10's remaining loss against R2025b is in its rows (`logm` 0.28 s against 0.08, `sqrtm` 0.11 against
0.01, `pinv` 0.07 against 0.02: I4), not between them.

### What the transform rows paid

The symmetry work costs the transform rows a few milliseconds (`probes\instrumented\split_i11_fft.m`,
warm medians, P0's binary and I11's alternated three times): `fft` 4M 0.032-0.034 s → 0.038 s, `ifft` 4M
0.045-0.046 → 0.049-0.051, `fft2` 2048 0.029-0.037 → 0.035-0.042 (R2025b's GUI medians 0.040, 0.045,
0.031). The proper answer is a real-input transform of half the length, symmetric by construction and
about twice as fast in both directions; it is added to I12's scope, where the transform kernels already
are.

### Checks

- `tests/JGraph.Tests/MatlabParity/fixtures/i11_ifft_symmetry.m`, 40 lines recorded from R2025b: which
  inputs answer real (vectors, rows, matrices with one slice off, a dimension, padding and cutting, a
  complex DC or Nyquist term, the words, a single, a NaN, a scalar, an empty, `ifft2`, `ifftn`, a padded
  `ifft2`), that the real answer is the `'symmetric'` one, and the round-trip errors; green in both
  storages.
- A 37-case probe (masks over NaN, Inf and ±0, N-D masks, complex ± real and scalars, `abs`, the
  mirrored `fft`, `fft2` and `fftn`, padded and Bluestein lengths) prints every element with `num2hex`
  and is byte-identical between the packed and boxed roads (`JGRAPH_JGS_PACKED=0`).
- Every CHK line of d02, d10 and d11 equal to P0's in every folder but `d02_roundtrip` (above).
- `FftKernelsM96Tests`' per-slice reference symmetrizes a real input's spectrum as the kernels now do.

Found on the way: `ifft(2+3i)` was refused ("expects argument 1 to be an array") and `ifft(zeros(0,1))`
indexed past an empty spectrum; both fixed.

## Consequences

- The plan is at rev 3: I11 done, its rows measured; the half-length real transform added to I12;
  ADR 0322's `ifft` divergence retired.
- `run_session_app.ps1` gained `-EnvVar` and a `memory.txt` per run; `measure_i11.ps1` and
  `compare_i11.py` run and read this measurement (all in the rig, outside the repository).
- The closing round (I14) is what the plan's acceptance is judged on; these numbers are one session per
  engine on the instrumented copies and the originals.

## Divergences

- **When one slice of an `ifft` is not conjugate-symmetric, the symmetric slices keep their rounding.**
  R2025b answers complex for the whole array and an exactly zero imaginary part in each slice that was
  symmetric (`matrix_one_off_col1_imag` 0); JGraph transforms every slice the complex way and those parts
  are rounding (2e-17 in the fixture).

## Still open

- The half-length real-input transform (I12).
- Whether the app should trade d11's 0.15-0.25 s for 2-3 GB less held memory (DATAS); not measured in
  a long interactive session.
- `histogram` (item 252, I10); d10's rows (I4).
