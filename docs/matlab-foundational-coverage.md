# Foundational core function coverage

Status of every item in *Foundational Core Functions for a MATLAB Replacement* (the minimum-viable
command set for the MATLAB dialect), as of **M69**. ✓ means the feature exists and behaves as MATLAB
does for the inputs JGraph's value model can express; notes call out the deliberate differences,
which ADR 0039 records in full.

**This file was stamped "as of M36" until M69 — thirty-two milestones — and several of its notes had
quietly stopped being true.** It is the only document in the repository that tracks *features* rather
than names, which is exactly why nothing caught the drift: the three coverage documents beside it
count names, and a name that gained an argument does not move a name count. Every note below was
re-probed through `jgraph.exe -batch` before this update rather than reasoned about, and the five
that had gone stale are marked. The remaining M36 labels are correct — they say when a row landed,
not when it was last checked.

| Subsystem | Item | Status | Notes |
|---|---|---|---|
| Matrix generation | zeros | ✓ | `zeros(n)` is n×n in the MATLAB dialect (JGS keeps its flat form) |
| | ones | ✓ | same square rule |
| | eye | ✓ | M36 |
| | rand | ✓ | `rand()`, `rand(n)` n×n, `rand(r, c)` (M36); `rng(seed)` since M52 (**was recorded as missing until M69**) |
| | randn | ✓ | matrix shapes since M36 |
| | linspace | ✓ | |
| | logspace | ✓ | M36, incl. the `logspace(a, pi)` special case |
| | meshgrid | ✓ | `[X, Y] = meshgrid(x, y)` works since M36 (multi-output builtins) |
| | diag | ✓ | M36, both directions + offset k |
| | magic | ✓ | M36, all three constructions match MATLAB exactly |
| Array inspection | size | ✓ | `[r, c] = size(A)` with MATLAB's fold/pad rules (M36) |
| | length | ✓ | |
| | numel | ✓ | |
| | isempty | ✓ | |
| | ndims | ✓ | M36; a true dimension count since N-D arrays landed in M41 (**the old note said 2 for everything but images**) |
| Shape manipulation | reshape | ✓ | M36, column-major, `[]` wildcard |
| | transpose / `'` | ✓ | `'`, `.'` operators; `transpose`/`ctranspose` function forms (M36) |
| | cat | ✓ | M36; any dimension since M41 (**the old note said dims 1 and 2**) |
| | horzcat / `[ ]` | ✓ | M36 (literals since M21b) |
| | vertcat / `;` | ✓ | M36 |
| | flip / fliplr / flipud | ✓ | M36 |
| | repmat | ✓ | |
| | squeeze | ✓ | M36; drops singleton dimensions for real since M41 (**the old note called it a no-op**) |
| | permute | ✓ | M36; any order since M41 (**the old note said 2-D orders only**) |
| Basic math | + − .* ./ .^ | ✓ | elementwise operators |
| | abs, sqrt, exp, log, log10 | ✓ | |
| | sin / cos / tan | ✓ | plus the inverse family |
| | round / floor / ceil | ✓ | plus fix |
| Matrix math | mtimes `*` | ✓ | real matrix multiplication |
| | mrdivide `/` | ✓ | M36, via the transposed solve |
| | mldivide `\` | ✓ | M36: LU (square), least squares (tall), minimum-norm (wide) |
| | mpower `^` | ✓ | M36, integer exponents (negative inverts first) |
| | inv | ✓ | M36; a singular matrix errors rather than warning + Inf |
| | det | ✓ | M36 |
| | rank | ✓ | M36, MATLAB's default tolerance |
| | norm | ✓ | M36: vector p-norms, matrix 1/2/inf/'fro' |
| | eig | ✓ | M36: symmetric (Jacobi) and general real (Hessenberg + shifted QR); complex input errors |
| | lu | ✓ | M36: `[L,U]`, `[L,U,P]` forms |
| | qr | ✓ | M36; economy-size Q |
| | svd | ✓ | M36: values or `[U,S,V]` (economy-size), one-sided Jacobi |
| Reduction & statistics | sum | ✓ | column-wise over matrices + dim + 'all' (M36) |
| | prod | ✓ | M36, same semantics |
| | mean, median, std | ✓ | column-wise over matrices since M36 |
| | min / max | ✓ | column-wise, `[m, i]` indices, `max(A, [], dim)`, elementwise 2-arg (M36) |
| Sorting & searching | sort | ✓ | column-wise + `[s, i]` permutation (M36) |
| | find | ✓ | `[r, c]` / `[r, c, v]` subscript forms since M36 |
| | any / all | ✓ | column-wise over matrices since M36 |
| | unique | ✓ | |
| | ismember | ✓ | M36 |
| Logical & comparison | == ~= < <= > >= | ✓ | elementwise |
| | & && \| \|\| ~ | ✓ | elementwise and short-circuit forms |
| Control flow | if / elseif / else | ✓ | |
| | for, while | ✓ | |
| | switch / case | ✓ | incl. cell-of-alternatives cases |
| | try / catch | ✓ | |
| Environment & state | clear | ✓ | |
| | clc | ✓ | |
| | whos | ✓ | M36 |
| | help | ✓ | M36, reads the builtin catalog |
| | tic / toc | ✓ | |
| | format | ✓ | M36: short/long/shortE/longE; compact/loose accepted (spacing is already compact) |
| File I/O | load / save | ✓ | M36: real MAT-file v5 (round-trips with MATLAB) + `-ascii` |
| | fopen / fclose | ✓ | M36; `fclose('all')` included |
| | fread / fwrite | ✓ | M36, uint8…double precisions |
| | fprintf | ✓ | console since M24c; `fprintf(fid, …)` to files since M36 |
| Rudimentary graphics | figure | ✓ | |
| | plot | ✓ | |
| | subplot | ✓ | |
| | hold | ✓ | per-axes since M35 |
| | title / xlabel / ylabel | ✓ | |
| | grid | ✓ | |
| | image / imshow | ✓ | `image` (M36) for matrices, `imshow` for image values |

Known gaps adjacent to this list (deliberate, tracked), re-checked in M69: **complex-matrix input
to the two- and three-output decompositions** — `e = eig(A)` and `s = svd(A)` take a complex `A`, but
`[V, D] = eig(A)` and `[U, S, V] = svd(A)` refuse it by name. Three of the four gaps this paragraph
used to list have closed and were still being advertised: `rng(seed)` landed in M52, N-D arrays in
M41, and struct arrays in MAT-files in M65.

The long-term tracker for the full documented command set lives in the demo workspace:
`matlab-r2021b-documented.html`, regenerated by `tools/matlab-checklist/build-checklist.py`. Since
M69 the part of it that matters to a checker is also **in the repository** —
`tools/matlab-checklist/matlab-r2021b-forms.csv` and `matlab-r2021b-args.csv` carry every documented
syntax form and argument, so the counts can be re-derived from a clean clone rather than from a
25 MB file that lives outside version control.
