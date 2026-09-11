# ADR 0150 — What a folder of legacy scripts needed

## Status

Accepted. A follow-up to ADR 0149, not a milestone: six repairs found by running one user's
folder of MATLAB scripts, each measured against R2025b before it was made.

## Context

`D:\Temp_Broken_Scripts\test1` holds a surface-metrology script written for MATLAB years ago:
`test1.m` is a function file that loads a `.sdf` height map (`loadsdf`), fits 231 Zernike terms
(`zfit`, `zernike`), masks the residual with `cirmask` and `nanmask`, and draws a mesh of the
roughness, a histogram of it, and a lit surface of the original. It runs in R2025b. Here it
stopped at the first helper:

```
Warning: query
cirmask.m(16,31): upper expects argument 1 to be a string, but got a number.
```

Both lines were defects, and four more sat behind them.

- `cirmask` asks `strcmp(upper(dia), 'NAN')` of an argument that is usually a number. MATLAB's
  `upper` hands a number back untouched, so the question is simply false; JGraph's refused it.
- `nanmask` saves the warning state with `prevState = warning('query', 'MATLAB:divideByZero')`
  and switches the warning off. JGraph's `warning` knew only `'on'` and `'off'`, which it ignored,
  and printed everything else as a message — so `query` was printed as a warning and the assignment
  got nothing.
- `hist(hs(:), 50)`, the legacy histogram, did not exist. Only `histogram` did, and it bins by a
  different rule.
- `mesh` drew dark grey wires over faces the colour of the axes. MATLAB colours the wires by
  height; the mesh of the roughness read as a black hedge where MATLAB's reads as a parula field.
- `run('…\test1.m')` hoisted the file's functions and returned. MATLAB calls a function file's
  main function; the batch launcher and the console here already did, `run` alone did not.
- `jgraph -batch …\test1.m` from another working directory found `extract.m` for `which` and
  then called the built-in `extract`. The launcher hands the runner the file's code with no
  source id, so the file's folder was never recorded as the running script's folder, and the file
  index — which decides whether a built-in's name is shadowed — never scanned it.

The numbers behind the pictures — the loaded map, the Zernike coefficients, the residual, the
mask counts, the histogram counts and centres — agree with R2025b to the last printed digit once
the five are fixed; the three figures were exported from both and compared by eye.

## Decision

### `upper` and `lower` map text and hand everything else back

Measured in R2025b: a char row or matrix is mapped, a string array element by element, a cellstr
element by element with its shape kept, and a number, logical, struct, function handle or empty
comes back exactly as it went in. A cell holding anything but char rows is refused with
`MATLAB:upper:CellsMustContainChars` — `MATLAB:lower:…` for `lower` — "Cell elements must be
character arrays." One function, `CaseMapped`, answers both names; the elementwise text wrapper
still maps string arrays and cellstrs above it, so the only cells that reach it are the ones it
must refuse.

### `warning` keeps a state per identifier, on the host

`JgsWarningState` lives on `JGraphScriptGlobals`: a default (`'all'`), the identifiers a script
has set, and the message and identifier of the last warning raised. `warning` is declared with the
ordinary builtins, which have the host; `lastwarn` beside `eval`, which has both. One object both
can reach replaced the wrapper `lastwarn` used to put round `warning` to learn what it said.

`warning('off' | 'on', id)` switches one identifier or `'all'` and answers the state it replaced
— a struct with `identifier` and `state` — when an output is wanted, and says nothing as a
statement. `warning('query', id)` answers that struct, or prints *The state of warning 'id' is
'on'.* as a statement; `'all'` answers the table as a column of structs. `warning(s)` puts a struct
or struct array back. A message is raised through the reader `error` uses, so `warning('pkg:id',
fmt, …)` is an identified warning (it used to print `pkg:id` as the message) and is silenced by
its identifier or by `'all'`; a silenced warning is still what `[msg, id] = lastwarn` reports,
which is MATLAB's rule and the one `nanmask`'s commented-out restore line relies on. `lastwarn`
gained its second output and its two-argument setter. An identifier must carry a colon and no
whitespace, else `MATLAB:warning:unknownSettingOrId`; a non-text option is `MATLAB:badopt`.
`verbose` starts off and everything else on, which is where a fresh R2025b session starts —
`MATLAB:divideByZero` included, although the warning it names was retired long ago.

Every warning the interpreter raises on its own — rank deficiency, a bad format escape — goes
through the same builtin, so `warning off` now silences those too.

### `hist` counts by MATLAB's arithmetic

`[n, x] = hist(y, m)` cuts the finite range of the whole of `y` into `floor(m)` equal bins —
`edges = lo + width * (0:m)` with the last edge pinned to `hi`, centres half a width above each
edge — and a constant range is widened by `floor(m/2) + 0.5` below and `ceil(m/2) - 0.5` above,
one unit per bin. A vector is the centres: each interior edge is a centre plus half the gap to the
next, and the outer edges reach to the data. A value goes to the first bin whose upper edge it
does not exceed, so bins are `(lower, upper]` and the outer two take everything beyond them; NaN
is skipped; ±Inf lands in the end bins. A matrix is counted column by column over one set of
centres, which is answered as a column. Empty data numbers the bins `(1:m)'` and counts nothing.
Ten bins is the default. Non-numeric data or bins is `MATLAB:hist:InvalidInput`; no arguments is
`MATLAB:narginchk:notEnoughInputs`.

As a statement `hist` draws the bins as a `HistogramPlot` built from its counts and edges,
black-edged and filled with the low end of the axes' colormap, which is the patch MATLAB draws.

### A mesh colours its wires through the colormap

`SurfacePlot` gains `ColormapEdges`: when true and no `EdgeColor` is named, the edges over filled
faces take the cell's colormap colour, as a bare wireframe's always have. `JG.Mesh` sets it beside
the opaque faces it already used for hidden lines; `surf` leaves it false and keeps its dark lines.
`set(h, 'EdgeColor', 'flat')` or `'interp'` turns it on and a named colour turns it off, and the
`.graph` format carries it.

### `run` calls a function file's main function

After hoisting, `run` invokes `InvokeMainIfFunctionFile` — the same call the batch launcher and
the console make — inside the file's own folder and dialect.

### A file run by the launcher runs in its own folder's name

`JgsRunner.Run` records the context's `ScriptPath` as the running script when the source id it
was given is empty, which is the launcher's file form. The folder joins the implicit path the file
index scans, so a sibling that shares a built-in's name shadows it from any working directory,
and `mfilename` answers under `-batch`. Diagnostics keep their bare `(line, col)` form.

## Consequences

`JgsWarningState.cs` is new; `Interpreter.LastWarning` is gone. `adr0150_legacy_script.m` pins
**77 lines** recorded from R2025b, every one agreeing by its rule, including a function file
written under `tempdir` and run. `LegacyScriptRepairsTests` covers what a fixture cannot see: what
reaches the console, the bars a statement-level `hist` leaves on the axes, the mesh's flag and its
survival through the `.graph` format. `docs/matlab-builtin-coverage.md` gains `hist` — `datafun`
is complete, 1,117 of 2,024 across every callable kind — and the guide and catalogue describe the
new forms.

### Divergences

- **`warning('query', 'all')` lists only what this session has set** — the default row,
  `verbose`, and whatever the script switched — where a fresh R2025b table has seven rows: the
  default and six identifiers of MATLAB's own, switched off, with `verbose` and `backtrace` kept
  outside it as settings. The row for any one identifier is the same on both sides.
- **`hist` answers double centres for single data** where MATLAB keeps them single. The counts are
  double on both sides.
- **`hist(y, m, extra)` is refused** where R2025b accepts and ignores a third argument.
- **A negative bin count is refused by name** — *the number of bins must be a non-negative number*
  — where MATLAB fails inside its own indexing with `MATLAB:badsubscript`.
