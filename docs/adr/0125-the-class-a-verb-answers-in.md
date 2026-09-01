# ADR 0125 — The class a verb answers in, and the report's last defects

## Status

Accepted (M123, 2026-08-31).

## Context

The head-to-head report keeps two lists that name differences from MATLAB. Section 06 is a table of
checks with five divergence markers and two numeric disagreements standing; section 07 is a list of
defects found while making the suite run at all, of which six were still open. This milestone was
asked to close both lists.

Nine items, and — as in M121 and M122 — the number of *causes* is smaller than the number of items
and the number of *defects* is larger. Three markers and one defect were the same thing. Two more
items were the visible corner of something much bigger than the report could see. And one of the two
numeric disagreements, a difference in the fourth decimal of a sum, turned out to be a function
smoothing by two thousand times the amount MATLAB smooths by.

## Decision

### A numeric class is carried by the verbs, not only by the operators

M97 made the numeric class a property of the value and taught the arithmetic operators to combine
two of them: `int8(100) + int8(100)` saturates, `single(1) * 2` stays single. It never told the
**builtins**, and there was no list of them to tell. Every verb mints a fresh wrapper from the
numbers it computed, and a fresh wrapper is a double, so `class(sort(uint8([3 1 2])))` was double —
and so was every reduction, every shape verb and every rounding verb, in every file they live in.

The report named one form of this: `class(sum(single([1 2 3])))`. Its wording said the integer
classes kept their class through the same reduction and single was the one being dropped. **That was
not true.** Probing the family found that every class was dropped by every name, which is why the
answer here is a table rather than a fix to `sum`.

MATLAB's rule was measured rather than recalled — a hundred and thirty expressions evaluated in
R2024a against single, int16, uint8, logical and double, diffed against the same expressions here —
and it comes out as three lists rather than one:

- **Carried.** The class survives whatever it is. These verbs *choose* or *move* elements that are
  already in the class, so the answer is made of the same kind of number the argument was: `sort`,
  `reshape`, `max`, `diff`, `cumsum`, `mod`, `abs`, and about sixty of their neighbours.
- **Floating.** A single survives and an integer becomes double. These verbs *compute* a new
  quantity rather than choose an old one — a sum can leave the range its terms lived in, a mean is
  rarely one of its samples — and MATLAB widens rather than saturate a result nobody asked to be an
  integer.
- **Integer only.** `bitand` and its neighbours, and `idivide`, which MATLAB defines for integers
  and refuses for a single.

A fourth list is narrower than the first: **logical** survives the verbs that only move it and not
the ones that do arithmetic on it. `sort` of a mask is a mask; `diff` of one is a double.

Three details are worth their own lines because each was a wrong answer waiting to happen.

**The class comes from the argument holding the data, not from any argument that has one.** A rule
that scans every argument makes `round(2.567, int32(1))` — one decimal place — answer an int32 3.
An existing test caught that one; the same mistake was available in `movmean(x, int32(3))` and a
dozen others where nothing would have. So the subject is the first argument alone, and the twenty-odd
names that genuinely take a second subject are listed.

**A running total saturates as it runs.** `cumsum(int8([100 100 -100]))` is 100, 127, 27 in MATLAB,
because the third term comes off the ceiling the second was pinned to and not off the 200 that never
existed. Letting the double-precision verb finish and stamping its row afterwards answers 100, 127,
100 — a plausible row, wrong in its last place, saying nothing about being wrong.

**A compiled loop refuses a classed range rather than dropping the class.** The hot loop's registers
are doubles and have nowhere to put a class, so `for i = int16(1):int16(4)` would bind a double `i`
where the walk now binds an int16 — one construct with two answers depending on a threshold nobody
can see. It takes an explicit conversion inside the range to reach that, and re-reading the three
bounds is the whole price of refusing.

### `typecast` reads the bytes of the class it was handed

`typecast(single(1.5), 'uint32')` answered two doubles holding a double's bit pattern. The comment
above it explained why: *"Every JGraph number is a double, so the source bytes are always a double's
eight."* That had been false since M97, and this was the one function whose entire job is to read one
number's bits as another class. It now reads the width from the value's own tag and stamps the answer
with the class it was asked for; all six of the report's cases, and the orientation rule, match
R2024a exactly.

### A named bin count still chooses readable edges

`histcounts(y, 256)` split the exact data range into 256 equal pieces, giving edges at 5.96e-6 and
every multiple of 0.0039062 above it. MATLAB spends the freedom a *count* leaves — it is a count, not
a set of edges — on making the width and the left edge round, exactly as its automatic rule does, and
covers the data by reaching past it rather than stopping on it.

The rule is transcribed from R2024a's own `binpicker` rather than reconstructed from its answers, and
then checked against them. The width is chosen in two passes: the first rounds the raw width *down*
to a whole multiple of its power of ten, purely to have something round to put the left edge on; the
second asks what width from that edge puts the largest reading inside the last bin, and takes the
roundest number in the interval that allows. That is why 256 bins come out 0.00391 wide rather than
0.0039062 — three digits instead of seventeen, for a right edge a thousandth past the data.

JGraph's *automatic* rule already matched MATLAB's exactly; only the named-count path differed. Named
limits are still split exactly, because there the caller has said where the histogram starts and
stops and the count only decides how many bins fit between them.

### `smoothdata` chooses its window from the readings, not from how many there are

The report saw a difference in the fourth decimal of a sum. Behind it: the automatic window was a
tenth of the sample count, which is a rule about how *much* data there is rather than about what is
in it. MATLAB's is a rule about the data — centre the readings, take their periodogram, walk the
normalised cumulative energy until it passes `tau`, and turn the frequency it stopped at into the
width of the moving average whose response is half power there.

A hundred thousand readings of a smooth signal want a window of **four**. The old rule gave them a
window of **ten thousand**. That never showed in a sum, because averaging a symmetric signal over
four samples or over ten thousand gives nearly the same mean — and it was the entire picture. Six
methods share the window, so all six were smoothing by the wrong amount too.

`SmoothingFactor` was wrong in the same place, and for the same reason: it was read as a fraction of
the length, where MATLAB feeds `1 - factor` into the same heuristic as `tau`.

### The Hessenberg reduction moves onto the blocked kernel

M120 already spent what there was to spend on the managed reduction by walking rows instead of
columns. What was left between it and MATLAB was not a cache miss but an algorithm: LAPACK
accumulates a panel of reflectors and applies them as one matrix multiply, so the work lands on
BLAS-3 instead of BLAS-2. `dgehrd` and `dorghr` are in the bundled OpenBLAS and were the only two
LAPACK entry points the project had not declared. At 400 square the reduction goes from 0.196 s to
**0.023 s**, against MATLAB's 0.0115 — thirty-nine times behind becomes twice.

This is the one **optional** method on the dense-backend contract, and it is virtual rather than
abstract for that reason: every other entry there is something the managed kernels owe an answer to,
because a script calling `qr` has to get one whichever backend is loaded. This is an acceleration of
a reduction that already exists in managed form and answers correctly. Writing a managed `dgehrd` to
satisfy an abstract declaration would mean a second implementation of a reduction this project
already has, kept in LAPACK's reflector storage, to be called by nothing.

### A glyph the figure's font lacks is drawn by a font that has it

`title('t \in [0, 60]')` exported as *t □ [0, 60]*. The markup had already turned `\in` into `∈`
correctly and the label read back correctly; Skia draws one string with one face, and puts
`.notdef` where that face has nothing. The label was right and only the picture was wrong, which is
why nothing in the console ever mentioned it.

Text is now cut into stretches, each drawn with a face that can draw all of it, with the system's
font list asked once per missing character and the answer remembered. A string of ASCII takes the
single-call road it always took — every face carries ASCII, so a split would produce one run and one
call anyway — so no figure that already drew can move.

The test for it is written the way the `'.'` marker's own defect was eventually found: **nine symbols
that should look different must not render identically.** Every missing glyph is the same box, so a
run of them collapses to one picture, and an assertion on the label text, the width, or the absence
of an error would have passed on every one of them.

### `dct`, `idct`, and `ode45` asked for one output

The two-dimensional cosine transform has been here since M46, and the line transform underneath it —
an orthonormal DCT-II on one length-2n FFT — is exactly what MATLAB's `dct` computes. So the missing
names were not a missing transform but a missing argument grammar: a length to pad or crop to, a
dimension to run along, and the four types. Types 2 and 3 are the transform and its inverse and were
already written; types 1 and 4 are summed directly, because reaching them takes an explicit `'Type'`
and the fast road for each is a different rearrangement. Inverting a type means asking for another
type, which is what keeps `idct` a wrapper rather than a second implementation.

`ode45` with one output answers a **solution** rather than a table of times: a structure that
remembers the polynomial the pair carried across every step it took. That is why `sol.x` holds
eleven points where `[t, y]` holds forty-one for the same call, and why it is not a coarser answer —
the thirty missing points are still available, they are just not computed until `deval` asks.

The structure is MATLAB's, field for field, including `idata.f3d`: the stage slopes of every step, as
an n-by-7-by-steps array. Keeping them *there* rather than in a handle beside the structure is what
makes a solution an ordinary value — it can be saved, loaded, passed to a function, and read by code
that has never heard of the solver. `deval` rebuilds the steps from those fields alone.

## Consequences

**Section 06: every marker and both disagreements.** The three `typecast` markers and the two
`single_keeps_class` markers now agree, and so do `d11_histcounts_mode` and `d11_smoothdata`. The two
`d09_deconv` checks stay skipped, and they should: dividing a 200,064-term polynomial by a 64-term
one overflows to NaN in **both** engines, which makes the check degenerate rather than divergent.
`deconv` itself was measured against MATLAB on clean and inexact small cases and agrees exactly.

**Section 07: every defect.** All eighteen are closed or withdrawn. The last four were `typecast`,
the reduction over single, the `histcounts` edge rule, and the TeX glyphs — plus `hess`, improved
from thirty-nine times to twice, and the last two of the "seven documented functions": `dct`/`idct`
and `ode45`'s single-output form.

**What the report could not see.** Behind two of its items: every builtin dropped every class, not
one verb dropping one; and `smoothdata` was smoothing by two thousand times the right amount, not by
0.015 per cent too much. Neither would have been found by fixing what was named.

**Tests.** 7,169, up from 7,048, and 70 of 70 stress scripts — `stess_70.m` is new and every one of
its fourteen checks passes identically on both engines. `stess_60.m`'s check 22 has been rewritten:
it used to assert this divergence and said so in its own comment — *"so that the day the classes
propagate, this line is what notices"* — and it was the one check in that file real MATLAB failed. It
now passes on both.

**Coverage.** `deval` takes the builtin count from 1,038 to 1,039 of 2,024 and `funfun` from 5 to 6
of 40. `dct` and `idct` are Signal Processing Toolbox names and are counted in neither.

## Divergences

Two are added and one is closed.

- **A verb MATLAB refuses for an integer answers a double here.** `sqrt(int16(4))` is an error in
  MATLAB and is 2 here, and the same holds for the whole transcendental family, `var`, `std`, `norm`,
  `dot` and `hypot`. Adding the refusal would close the difference by making scripts that run today
  stop running, which is the wrong trade for a difference nobody has reported. The single half of
  each of those names now agrees exactly.
- **A compiled loop drops the class of a *nested* range.** The outer loop refuses a classed range and
  lets the walk have it; a nested one is already running on registers with no way back out. It takes
  an explicit conversion inside a nested range inside an already-compiled scalar loop to reach.

Closed: **a `single` or integer-class argument comes back as a double** (ADR 0101). Every name that
divergence listed — `nextpow2`, `conv`, `roots`, `unwrap`, `cplxpair`, `rectint` — now answers in the
class MATLAB answers in, along with the rest of the library.

## Still open

None of these is a difference in what JGraph answers, so none belongs in the list above.

- **`hess` is twice MATLAB rather than equal to it**, which is the OpenBLAS-against-MKL price ADR
  0089 accepted for every other factorization on this path.
- **Unary plus on a mask answers a mask**, where MATLAB widens it to a double, and
  `setxor(mask, [])` does the same. Both predate this milestone; MATLAB is itself inconsistent about
  the second, since `setdiff(mask, [])` there *is* a mask.
- **`mldivide` combines an integer class where MATLAB refuses it**, carried over from M97's operator
  rule rather than from anything here.
- The M124 list stands unchanged: `sortrows` of a string array, `unique` of one, `strcat`, and `^` on
  a non-square array.
