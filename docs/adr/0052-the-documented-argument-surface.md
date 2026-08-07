# ADR 0052 — The documented argument surface, and a seed for the random stream

## Status

Accepted (M52, 2026-08-06). Builds on [ADR 0049](0049-the-image-processing-toolbox-surface.md), whose
option parser this milestone promoted out of the imaging namespace, and on
[ADR 0043](0043-shaped-arrays.md), which is what lets a reduction walk any dimension of any shape.
Supersedes the "a scalar does not reduce" entry recorded in `docs/matlab-builtin-coverage.md` at M49.

## Context

Every coverage milestone so far asked *is this name registered?* None asked *does the registered name
take the arguments MATLAB documents for it?* Those are different questions, and the gap between them
had grown large enough to be the main reason a real MATLAB script still failed here.

The shape of the gap was consistent. A builtin implemented the first sentence of its documentation and
either refused the rest with an arity error or, worse, read it as something else. `std(x, 1)` asked for
the population standard deviation and silently got a reduction along dimension 1, because the wrapper
that gives every reduction a dimension read any number in slot two as one. `unique` had no `'stable'`,
no `'rows'` and no index outputs. `regexprep` understood one option word out of twelve and let the
other eleven fall through unnoticed. `sum(7)` was an error where MATLAB answers `7`. `find(X, k)` read
`k` as an index origin — a JGS convention leaking into the MATLAB dialect, where the documented meaning
is "the first k matches". And there was no seedable random stream anywhere: two unseeded `Random`
instances and no `rng` at all, so nothing stochastic could be asserted about twice.

The milestone was planned as foundations for the toolbox surfaces that follow, so it had to fix the
machinery rather than the symptoms: one option parser, one reduction descriptor, one random source.

## Decision

### The option parser is base-language machinery, moved rather than copied

`OptionSpec`/`ParsedArgs` (M46) were written against `JgsValue` alone and never had anything to do with
pictures. They moved verbatim into `src/JGraph.Scripting/Jgs/JgsBuiltins.Options.cs` under
domain-neutral names, retyping the 77 spec declarations across the eleven imaging files;
`JgsBuiltins.Imaging.Options.cs` keeps only what is genuinely about images.

A rename, not an alias. An alias would have left `ImgArgs` in every signature and two names for one
thing forever, and the point of the move is that a base-language builtin and an imaging builtin parse
their tails the same way. The spec knows every legal word for its builtin, which is what lets an
unrecognized option **name the alternatives** rather than just refuse — the behaviour the hand-rolled
tails could not have, and the reason they were the ones silently ignoring words they did not know.

### One descriptor says what each reduction's argument slots mean

`WrapColumnwise` gives a reduction `dim`, `'all'` and the missing-value words for free by wrapping the
base builtin. It read slot two as the dimension for every name, and M50 had already bolted on a bool
for `diff`. That bool became `ReductionSpec(KeepShape, LeadingArgs, RepeatsInner, TailWords, Identity)`
— one row per name, in one table, at the point of registration:

| Names | Spec | Why |
|---|---|---|
| `sum` `prod` | `Words: Nan \| Outtype`, `Identity` | `sum([], …)` needs the identity |
| `mean` `median` `mode` `rms` | `Words: Nan` (+ `Outtype` for `mean`) | ordinary reductions |
| `std` `var` `variance` | `LeadingArgs: 1` | the weight sits where the dimension sits for everyone else |
| `cumsum` `cumprod` `sort` | `KeepShape` | a whole slice comes back |
| `diff` | `LeadingArgs: 1`, `RepeatsInner`, `KeepShape` | the second argument is a repeat count (M50) |

`var` did not exist before this table; it is `variance` under the name MATLAB documents. The defaults
reproduce the previous behaviour exactly for every name whose row says nothing, which is what made a
rework this wide safe to land in one wave.

### `'omitnan'` is the default for `max` and `min`, which changes answers

`WrapExtreme` used `Math.Max`/`Math.Min`, which propagate NaN, so a single missing reading anywhere
made the maximum of the whole column NaN. MATLAB's default is `'omitnan'`: a NaN is a reading that is
missing, not one that beats everything else. The default is flipped, `'includenan'` asks for the old
answer, and this is recorded here rather than buried because **it changes existing answers on data
containing NaN**. It is the one change in the milestone that does.

### A scalar is a one-by-one array

Four argument helpers — `Arr`, `ToDoubles`, `DoubleArray`, `ArrayOfNumbers` — threw when handed a bare
number, which is why `sum(7)`, `cumsum(5)`, `diff(5)`, `plot(2, 3)` and every sibling were errors. They
now promote instead.

The census that made this safe is short to state: promotion turns errors into answers and **cannot**
change an answer that already existed, because every builtin that means something *different* by a
scalar branches on the type before it reaches these helpers — the elementwise `max(a, b)` form, the
image reductions, the scalar constructors. `diff(5)` answering `[]` falls out of the same rule rather
than needing a special case. Both dialects get it: refusing a scalar was never part of the JGS surface.

`isequal` needed the same reading in two more places, found while testing this: a one-element array and
a bare number are both 1-by-1 and now compare equal (`size` already said so and `==` already agreed —
only `isequal` disagreed), and two cells compare element by element instead of by reference, which is
what makes the natural way to assert about text work at all.

### `find(X, k)` splits by dialect

Under MATLAB, `k` is how many matches to take, with `'first'`/`'last'` choosing the end. Under JGS it
stays the index origin, which is the documented JGS behaviour and what existing JGS scripts write. Both
readings cannot be right for one function, so each dialect keeps its own; the frozen stress scripts and
the example scripts were audited for MATLAB-dialect reliance on the old reading before the change, and
none existed.

### The random stream has a seed and a state

New `JgsRandomSource`: a swappable `System.Random` with a seed and a draw count, threaded through the
six registrars that take a generator (deleting the private `new Random()` in the sparse registrar on
the way). `rng(seed)`, `rng(seed, 'twister')`, `rng('default')`, `rng('shuffle')`, `s = rng` and
`rng(s)` all work, in both dialects.

The contract is **deterministic under a seed, not stream-compatible with MATLAB**. Reproducing
MATLAB's Mersenne Twister bit for bit would pin every future numeric change to a generator we do not
otherwise want; what a script actually needs is that the same seed gives the same run twice, and that a
saved state restores. `Restore` reseeds and fast-forwards by the recorded draw count, which is exact
for this generator and cheap at the scales scripts use.

### A stress runner, because the gate was manual

`tools/run-stress.ps1` loops the scripts, and its gate is two halves: **exit code 0 and no line
beginning `Fail:`**. The second half is not redundant — every self-checking section wraps its work in
`try`/`catch` and prints rather than rethrows, so a script full of failures still exits 0. Checking
only the exit code would have called that a pass.

### The names the base language was missing

`RegisterDataAnalysisBuiltins` (new `JgsBuiltins.DataAnalysis.cs`) is registered **after** every other
builtin file, because three of its names replace an existing registration (`linspace` gains its
optional count, `round` graduates from the one-argument element-wise table, `polyval` gains the second
output that turns a fit into an error bar), and **before** `RegisterMatlabReductions`, so `rms` is
wrapped for a dimension the same way `mean` is rather than carrying its own copy of that machinery.
`bounds` calls the environment's own `min` and `max` at call time with `[]` in the comparand slot, so
it inherits the NaN-default flip above and cannot disagree with them.

`interp1` is the one that needed a kernel. Its two cubics differ only in *which slopes they choose* —
evaluation is the same Hermite cubic — so `JGraph.Numerics/Interpolation.cs` is one file holding
not-a-knot spline slopes, shape-preserving pchip slopes, and the evaluator both share. That answered
the plan's open question ("spline and pchip only if a kernel is reusable, else a named rejection") by
construction. `makima` and `v5cubic` **are** refused by name, because answering them with a different
curve would be wrong quietly.

## The audit

Roughly ninety documented call forms were driven through `jgraph -batch`, one per line, each reporting
OK or GAP with its error text. Its most important result is not a name.

**The coverage tables cannot see most of the base language.** `docs/matlab-builtin-coverage.md` tracks
the 514 commands R2021b documents with kind **builtin**, plus the 263 it documents as graphics
**function**s. Everyday base names documented as plain functions under `toolbox/matlab/…` are in
neither table, so no arithmetic over those tables could ever report them missing. `union`, `intersect`,
`setdiff`, `setxor`, `mat2str`, `int2str` and `deal` were absent with nothing able to say so. Six of
them are worse than that: `union`, `intersect`, `setdiff`, `setxor`, `sortrows` and `normalize` are in
the R2021b dump with kind `function` but **without the documented flag**, so they never entered the
2,027-row callable set at all, and `rms` is not in that install's tree at all. The audit is the only
instrument that can see any of them.

### Fixed in this milestone

| Name or form | Was | Now |
|---|---|---|
| `union` `intersect` `setdiff` `setxor` | not recognized | `'rows'`, `'sorted'`/`'stable'`, `[C, ia, ib]` |
| `ismember(A, B, 'rows')` | arity error | compares whole rows |
| `[tf, loc] = ismember(A, B)` | "returns 1 value" | reports the earliest match |
| `mat2str` `int2str` `deal` | not recognized | implemented |
| `c{:}` on a one-subscript cell | **`NullReferenceException` out of the interpreter** | a script error naming the fix |
| `[r, c] = find(A)` | a row, where the one-output form gave a column | both stand up the same way |

The set quartet shares `unique`'s comparison machinery rather than repeating it: one `SetSide` builder
turns either input into comparable keys (rows, elements, or cells of text), one lower-bound search with
an index tiebreak answers "where is this key in the other set, earliest first", and each operation is
one line saying which side's keys to keep.

### Deferred, with the reason and the cost

| Name or form | What is wrong | Cost |
|---|---|---|
| `arrayfun(…, 'ErrorHandler', f)` | **silently ignored** — the option scan reads one word and drops the rest, so a misspelling is swallowed too. The defect class wave D closed for `cellfun`. | share `cellfun`'s loop |
| `[a, b] = arrayfun(…)` | "returns 1 value" — no multi-output | the same change |
| `strtrim` `strrep` `strcat` `str2double` `contains` `join` over a cell | type error; MATLAB maps over the cell | one shared elementwise wrapper |
| `zeros(n, 'uint8')`, `zeros(n, 'like', x)` | "argument 2 must be a number" | the class tag has existed since M47 |
| `size(A, [1 2])` | "argument 2 must be a number" | small |
| `normalize` `rescale` `discretize` `fillmissing` `rmmissing` `islocalmax` `islocalmin` `smoothdata` `groupsummary` | not recognized — the data-preprocessing family | a wave of its own |
| `pad` `erase` `insertAfter` `insertBefore` `extractBefore` `extractAfter` `extractBetween` | not recognized — the string-editing family | a wave of its own |
| `kron` `perms` `factor` `idivide` | not recognized — odds and ends | trivial each |
| `interp2`, `'native'` output classes, `histogram` object options, `'SamplePoints'` | out of this milestone's scope | named, not silent |

### Correct as documented (spot-checked, unchanged)

`repmat` in all three shapes · `cat` past dim 2 · `nnz` · `nonzeros` · `strfind` `strcmpi` `strncmp`
`blanks` `count` `split` `string` `compose` · `norm(v, p)` and `norm(A, 'fro')` · `cross` · `nchoosek`
both forms · `primes` · `gcd`/`lcm` over arrays · `nthroot` `hypot` · `fix`/`rem`/`mod` on negatives ·
`dec2bin` with a width · `typecast` · `sum(…, 'double')` · `cumsum(…, 'reverse')` · `prod(…, 'all')` ·
`circshift` with a vector shift · `ismissing` · `structfun(…, 'UniformOutput', false)` · `arrayfun`
over several arrays · `[m, n] = size(A)`

## Consequences

- **Answers that changed, not just answers that appeared.** `max`/`min` omit NaN by default;
  `linspace(a, b, 1)` is `b` where it used to be `a`, and the last sample is now exactly `stop` rather
  than the accumulated step; a regular expression's dot spans a newline and a zero-length match is no
  longer replaced (MATLAB's defaults are `'dotall'` and `'noemptymatch'`, .NET's are the opposite);
  splitting on whitespace keeps the empty pieces a leading or trailing delimiter produces, which is
  what makes `strsplit` the same function as `regexp(…, 'split')`; `num2str` on an array spaces its
  columns one wider. Each is a case where the old answer was wrong rather than merely absent.
- **`interp1`'s nearest neighbour takes the later sample at the halfway point.** MATLAB rounds the
  fractional index away from zero, so `interp1([1 2 3], [10 20 30], 1.5, 'nearest')` is 20, not 10.
  The cubics extrapolate by default; the piecewise methods do not, and answer NaN outside the data
  unless given `'extrap'` or a fill value.
- **`histcounts`'s automatic bin width follows the published formula** rather than MATLAB's
  `binpicker` internals, so a call that names neither edges nor a count may choose a different number
  of bins. Named `'BinLimits'` are exact — they are not widened to a nice number, and the rule then
  only chooses how many bins fit between them. The last edge is stretched to reach the largest value,
  because otherwise a rounding hair drops it out of the histogram entirely.
- **Bin numbers, column keys and index outputs are dialect-scoped.** `histcounts`'s third output and
  `sortrows`'s column argument count from `dialect.IndexBase`, with "outside every bin" one below it.
  A negative column means descending under MATLAB; JGS gets a named error pointing at `'descend'`,
  because a negative index is not a JGS idiom.
- **`polyval`'s error estimate** solves `eᵀR = vᵀ` by forward substitution against the fit's `R`
  factor, and is `+∞` where the fit has no degrees of freedom left.
- **`uniquetol`'s tolerance is scaled by the data**, as MATLAB's is: `1e-6` means six significant
  figures rather than a fixed distance, and `'DataScale'` replaces that scale, per column when the
  comparison is by rows.
- **`unique([NaN NaN])` is two values.** A key holding a missing reading is its own group every time,
  which is what keeps `C(ic)` rebuilding the input exactly, and what keeps a NaN out of every
  intersection including one with itself.
- The `mov*` family keeps `'includenan'` as its default, because MATLAB's does; only `max` and `min`
  flipped. `'SamplePoints'` is refused by name.
- **stess_24.m** covers the milestone in twenty-two sections, each argument form written at least twice
  in different shapes, plus negative tests asserting that a misspelt option word errors *and names the
  alternatives*. It found one defect the unit suites could not: the two forms of `find` disagreed about
  the orientation of their outputs, because only the one-output form went through the shaping helper.
  That is the M46 lesson holding — the unit suites fed each function the shape it expected.
- **Four probe scripts, ~150 call forms, run and read against MATLAB's documentation before a single
  test was written.** That pass caught three real bugs (`nearest` at the halfway point, `'BinLimits'`
  being widened, the last bin edge falling short) that the unit tests would otherwise have written
  down as correct. It also crashed the process: `c{:}` reached the index conversion as nothing at all
  and came back out as a `NullReferenceException`, a pre-existing defect that no test had touched.
- The count moves are in `docs/matlab-builtin-coverage.md`. Every M52 name is documented as kind
  *function*, so the builtin table stands still at 372 of 514 and the whole movement is in the
  across-every-kind total.
