# ADR 0124 — The last seven forms, and the families behind four of them

## Status

Accepted (M122, 2026-08-31).

## Context

The head-to-head suite's fourteenth script asks both engines the same 262 documented call forms
inside a `try`/`catch` and records which ones each accepts. After M121 that stood at 255 for JGraph
and 262 for MATLAB. This milestone was asked to close the remaining seven.

Seven forms, and four different causes — which is the same shape M121 found and worth stating again,
because the report cannot see it. A capability probe names *forms*, and a form is the smallest thing
a defect can wear. Three of these were names that did not exist. Three were one verb's argument list
being too short. And one, `reshape` of a char array, was a single member of a family of twelve
failures, four of which were not refusals at all.

## Decision

### The log-scale verbs are `plot` with a ruler changed afterwards

`semilogx`, `semilogy` and `loglog` read their own arguments and read at most three of them, so
`semilogy(x, y, '-o', 'LineWidth', 1.2)` — the form the documentation opens with — was refused for
having five.

They are not a different kind of drawing from `plot`. They are `plot` with one ruler changed, so they
now run `PlotCore` and inherit every form it accepts: repeated `(x, y, spec)` groups, the whole
option list, a table and two column names, and datetimes along either axis. Three verbs stopped
having an argument grammar of their own, which is three grammars that can no longer fall behind
`plot`'s.

Two details are deliberate. The scale is set **after** the drawing, because a verb drawn with `hold`
off clears the axes and would take the ruler with it. And the implicit x stays **1-based in both
dialects**, which is what these three have always counted from — `plot`'s 0-based JGS numbering would
put a sample on a logarithmic x axis at a coordinate it has no room for.

### A shape verb is taught that what it rearranges may not be numbers

`reshape(char([65 66 67 68]), 2, 2)` was the form in the probe. Probing the family it belongs to —
twenty-one calls across three containers — found **twelve** failures, and the four worst were not
refusals. `permute` and `transpose` hand back their argument untouched when it is not a numeric
array, so `permute({1,2;3,4}, [2 1])` answered the cell it was given, unrotated, and said nothing.
A verb that quietly does nothing is harder to find than one that stops.

Two mechanisms, in one place, because the two containers are not the same problem.

A **char row** is characters — MATLAB's `'ABCD'` is 1-by-4, not one value — so it is *promoted* to
the char matrix of its code points, the verb runs on numbers exactly as it always has, and the answer
is re-tagged on the way out. That lane also serves the verbs that read values rather than only moving
them: sorting characters is sorting their code points, which is MATLAB's own rule.

A **string array or cell** cannot be promoted; its elements are not numbers and no arrangement of
them is. But every verb in the second list is a *permutation of positions*: what it does to a value
depends on where the value sits and never on what it is. So the verb is run on the positions
themselves — 1 to N in the source's shape — and its answer is read as where each element went. One
gather then puts the real elements there. No verb learns what a cell is, and the fourteenth verb
added to that list gets the behaviour for free.

The value-reading verbs are deliberately absent from the second list. A sort of positions sorts the
positions, which says nothing about the text at those positions, and MATLAB refuses `triu` of a
string array outright rather than inventing a zero for text.

### Two transposes that disagreed with each other

Probing the family found a defect the report does not mention and that has nothing to do with text:
`transpose(v)` on a row vector answered the row, while `v'` answered the column MATLAB answers. One
engine cannot hold two readings of one operation. The function carried a comment saying it matched
the operator, which had stopped being true at some point that left no other trace.

The operator had its own half of the same bug: a char row is one `String` value here rather than an
array, so `keys'` was a no-op — a picture-free failure a script has no way to notice.

### `residue`, worked out one pole at a time

The expansion is computed per pole rather than by solving for every residue at once. A pole of
multiplicity *M* contributes the first *M* terms of a power series about itself, and those terms are
a local question: shift both polynomials to that pole and divide their series in ascending powers.
Building the n-by-n system whose columns are `a(s)/(s-p)^i` would give the same answer where it is
well conditioned and a much worse one where it is not — a triple root makes that matrix nearly
singular, and then the *whole* answer degrades rather than the one group that is genuinely delicate.

Poles that are close together are moved to their mean and read as one repeated pole, which is
MATLAB's rule and not a rounding convenience. The roots of a polynomial with a double root come back
from any eigenvalue solver as a conjugate pair a whisker off the real axis — the square root of the
working precision, so about 1e-8 — and reading those as two distinct simple poles gives two enormous
residues that cancel instead of the two modest ones the expansion has.

The pole order is `roots`'s own, so `residue` and `roots` agree about a polynomial with nothing
repeated in it, and only a repeat moves anything.

### `nargin` and `nargout` given a function rather than a running call

Inside a function body these are the counts the call passed, bound when the frame opens. Given an
argument they ask about a function that is not running, and the local binding shadows the builtin —
MATLAB's arrangement, not a collision to be resolved.

MATLAB's sign convention is kept: a declaration ending in `varargin` answers `-(fixed + 1)`, so `-1`
means "any number" and `-3` means "two, then any number".

For a function written in a script or a file the answer is read from its own header, so it is the
same answer MATLAB gives for the same file. For a builtin it is read from the catalog — the signature
`help` prints and the editor completes — because a builtin here has no header to read: it validates
its arguments as it runs. **That is a divergence, and it was measured rather than assumed**: of the
1,641 names both engines carry, 671 answer the same number. The rest differ because MATLAB's number
describes MATLAB's implementation of the name and JGraph's describes JGraph's, and the two are not
the same function. The alternative was a 1,641-row table transcribed out of MATLAB, which would have
been a claim about this engine copied from another one.

`nargout` of a builtin is the one answer not read from the catalog, because the catalog does not
describe outputs. It is read from the registration, where it is a fact rather than a guess: a builtin
with no multiple-output form has exactly one output, and one that has such a form answers however
many the caller asks for.

### `histogram2`, and the box painter lifted out of `bar3`

The chart owns its bins and does none of its own counting: the edges and the counts come from the
same `Binning` code `histcounts2` answers from, so a script that draws the histogram and then checks
it against `histcounts2` is comparing two readings of one rule rather than two rules that happen to
agree today. The automatic choice divides by the fourth root of the sample count rather than the cube
root a one-dimensional histogram uses, because the same readings are spread over bins in two
directions at once.

`bar3`'s box painting was **lifted into a shared renderer rather than copied**, and the thing worth
sharing is not the drawing but the *sort*. Every face of every box goes into one depth order, not one
order per box: boxes interleave as soon as the camera is off an axis, and sorting them box by box
puts a near face behind a far one. That is a mistake a second implementation would have made again,
and it is invisible from any angle you would think to check from.

One object, two pictures, and the axes changes dimension underneath them: the box field is a 3-D
chart and the tile is a flat one, so the display style decides what kind of axes this is. That is the
one chart here whose *appearance* setting changes the axes, and it is MATLAB's arrangement.

### `mat2str` writes the quotes its container takes

Found while checking `histogram2`'s `FaceColor`, and worth its own line because the defect was
sitting under a comment describing the correct behaviour. `mat2str('abc')` answered `"abc"` — text
`eval` reads back as a *string*, not as the char row it was given — and the line above it said "a
char row reads back as a char row, which means the quotes are part of the answer". A char matrix came
back as its code points. The one function whose whole contract is that `eval` reads its answer back
as the same value got the type wrong for every char row it was ever handed.

## Consequences

**The capability probe: 262 of 262.** Every documented call form the suite asks for is now accepted
by both engines. The suite's `d14_forms_accepted` checksum moves from 255 to 262, which is the one
checksum this milestone is meant to move.

**What the family probe found that the report did not.** Twelve failures behind one reported form,
four of them silent; a `transpose` function that disagreed with the `transpose` operator on every
vector, text or not; `repmat` of a cell answering a 1-by-2 double; and `mat2str` of a char row
answering the wrong type. None of these is in any report, and none would have been found by fixing
the seven forms named.

**Tests.** 7,048, up from 6,962, and 69 of 69 stress scripts. Two are guards rather than checks: that
a char matrix still reaches the shape verbs unchanged, and that a verb which reads values still
refuses a string array.

**Accuracy.** The partial-fraction expansion is checked against what it means rather than against a
table of digits — evaluating the expansion and the ratio it expands at points off the real axis, over
nine polynomial pairs including repeated and complex poles. The reference values that do appear are
R2024a's, and the repeated-pole case matches it to the last printed digit including its rounding
dust.

## Divergences

Two are added and one is closed.

- **`nargin` and `nargout` of a builtin answer about JGraph's builtin, not MATLAB's.** Of the 1,641
  names both engines carry, 671 answer the same number. For a function written in a script or a file
  the two agree exactly, because both engines read the same header.
- **`histogram2`'s tile style makes the axes flat, where MATLAB keeps a 3-D axes seen from
  directly above.** The picture is the same and `get(gca, 'View')` answers `[0 90]` either way; what
  differs is that a script asking a JGraph tile axes for a camera property finds a flat axes.

Closed: `fliplr` refused a char row (ADR 0063). It, and every other shape verb, now takes one.

## Still open

None of these is a difference in what JGraph answers, so none belongs in the list above.

- **`sortrows` of a string array is still refused**, where MATLAB sorts it. It is the one
  rearranging verb the position gather cannot serve: sorting positions by their *number* says nothing
  about the text at those positions, so it needs a comparison over the elements rather than a
  permutation of them.
- **`unique` of a string array answers the source's shape where MATLAB answers a column.** A
  pre-existing difference in `unique` rather than anything this milestone touched; found while
  probing the family.
- **`strcat` still refuses a string array**, carried over from M121 for the same reason: its rule
  expands *every* argument against every other, where the verbs here map one subject.
- **`^` on a non-square array is elementwise here and refused by MATLAB**, carried over from M121.
