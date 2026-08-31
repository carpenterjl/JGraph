# ADR 0123 — Text that arrives in a container, quadrature, and a panel that drew nothing

## Status

Accepted (M121, 2026-08-31).

## Context

The head-to-head suite's fourteenth script is a capability probe rather than a timing: it runs 262
forms inside a `try`/`catch` and reports which ones each engine accepts. MATLAB accepted all 262;
JGraph accepted 247. Two of the fifteen it refused were named for this milestone — the "String
arrays" group, where five forms failed, and "Solvers and quadrature", where three did — and one
picture in the text script was reported as having lost its data.

The three groups turned out to have three different causes, and only one of them was the one the
report described.

**The solvers group was simply missing three names.** `integral`, `quadgk` and `odeset` did not
exist. `odeset`'s absence was the interesting one, because `ode45` has been here since M43: it could
be called but not configured, so its accuracy was whatever the default happened to be and `Refine`,
`MaxStep` and `InitialStep` could not be asked for at all.

**The string-array group was not missing anything.** Every one of the five names existed and worked;
each refused a *container* of text by name — `split expects argument 1 to be a string, but got a
string array`. Probing the whole family rather than the five found the same refusal in `strfind`,
and a second and different one in `contains`, `replace`, `erase` and `count`, which refused a *list
of patterns*. M104 had already written down the rule these names break ("text answers in the
container it arrived in") and built the machinery that keeps it; six names had simply never been put
on the list.

**The blank panel was not about text at all.** `plot(1:20000, strlength(keys(1:20000)), '.')` drew
an axes with the right limits, the right ticks and nothing inside them. Bisecting on the marker
style found nothing; bisecting on the *count* found the cliff at once — 2,000 points drew, 6,000 did
not — and the cause was a constant in the renderer that suppressed markers above five thousand
samples. For a series with a line that is a degradation. For `'.'`, where `LineStyle` is `none`, it
is the whole picture.

## Decision

### The last six text verbs learn that their subject may be a container

`MapTextSubjects` is a fourth retrofit beside the three M63–M105 already installed, and it differs
from `MapOverText` in exactly two places.

It maps over the **first** argument by name rather than over the first container it finds. For these
names a container in any later position is a set of patterns rather than a partner to pair with:
`replace(s, ["a";"c"], "z")` applies both patterns to `s` and answers once, where mapping over the
first container found would have answered twice.

And it puts the per-element answers back three different ways, because these verbs answer three
different things. `regexprep` and `extractBetween` answer one piece of text, and go back into the
container the subject arrived in. `split` answers several, and goes through `SpreadPieces` — the
one-to-many rule M104 wrote for `splitlines`, including its refusal of the three-dimensional case
this build has no container for. `regexp`, `regexpi` and `strfind` answer things that are not text
at all, and go into a cell shaped like the subject.

A char matrix is not a container here, and that is measured rather than assumed: MATLAB refuses one
to every name in the file, so mapping over its rows would have invented an answer rather than
matched one.

One consequence is worth stating as a fix rather than as a detail. `split` now builds its answer
through `SpreadPieces` even for one piece of text, which is what tells `split("a,b", ",")` from
`split('a,b', ',')` — a string array of two against a cell of two. Before this it built a bare array
of strings and left the tag off, so `class(split('a,b', ','))` answered `double`.

### A pattern argument is a list, and each verb does its own thing with the list

MATLAB lets every search-and-edit verb take several patterns, and what it does with them is not
uniform. `contains`, `startsWith`, `endsWith` and `matches` ask whether **any** matched. `count`
**adds them up**. `erase` and `replace` apply them **all in one pass**. `regexprep` applies them
**one after another**.

The last two are the ones that had to be measured rather than reasoned about, because they read as
the same operation and are not: `replace("ab", ["a";"b"], ["b";"a"])` is `"ba"` — a simultaneous
swap — while `regexprep("a", ["a";"b"], ["b";"c"])` is `"c"`, because the `b` the first expression
writes is found by the second. Two verbs that look interchangeable cannot share a body.

### `join` runs a dimension together, and the caller may say which

`join` collapsed an N-by-M array along its rows and everything else into a single string. A column of
text therefore came back as one string where MATLAB leaves it a column — the head-to-head text
script carries a comment saying so and works around it.

It now takes the dimension, as `join(str, dim)` or `join(str, delimiter, dim)`, and defaults to the
last dimension that is not a singleton. The delimiter expands over the gaps the way any other
operand expands — a column of delimiters gives each row its own, a row of them gives each gap its
own — which is implicit expansion against `size(str)` with the joined dimension one shorter.

### A bracket of string arrays stands them side by side

Fixing `join` exposed the thing underneath it: `[s s]` where `s` is 2-by-1 was 1-by-4, because a
single-row bracket ran every piece into one flat list. The multi-row arm had had the correct block
machinery since M63 and the single-row arm did not use it. It does now.

`horzcat`, `vertcat` and `cat` are the function spellings of the same operation and refused a string
array outright, so the machinery was lifted out of the interpreter into the text family where both
callers can reach it. Two spellings of one operation get one implementation, and it is the one that
was already right.

### `integral` and `quadgk` are one engine

They are one method behind two interfaces in MATLAB, and they are here: an adaptive Gauss–Kronrod
(7, 15) pair, differing only in what each may be asked and what each hands back.

The nodes and weights were **derived rather than transcribed** — Laurie's algorithm on the Legendre
recurrence, then the symmetric eigenproblem — and then checked the way a quadrature rule can check
itself, which is the part worth keeping: the Kronrod rule integrates every polynomial up to degree
22 exactly and must fail at 24, and the Gauss rule nested inside it is exact to degree 13. A mistyped
node breaks that at low degree and unmistakably. A table copied out of a book carries no such alarm,
and this repository has been caught by a recalled constant before (M120).

Three decisions inside it were each made by measurement and each corrected something:

- **The endpoint transform is applied once per stretch, not once per panel.** Both are correct; only
  one is accurate. Re-bending every panel makes an integrand the rule was about to handle exactly
  into one it handles approximately, which loosens the error estimate and costs panels. The C# had
  drifted from the verified prototype on exactly this point, and comparing the two is what found it.
- **The mesh starts at ten panels rather than one**, as MATLAB's does. A single fifteen-point panel
  can step over a narrow feature and answer confidently about an integral it never saw.
- **The error estimate is QUADPACK's, not the raw Kronrod-minus-Gauss difference.** At a corner both
  rules are wrong in the same direction, so their difference says the panel is fine: `|sin x|` over
  [0, 10] came out 2.5e-6 wrong against a 1e-6 contract on the raw difference, and 2.8e-9 with the
  difference scaled by how much the integrand actually varies over the panel.

Scaling the estimate made the adaptation work harder near a singularity, which found the fourth
decision: **a split that produces a value no longer representable has told us nothing.** Such a
panel keeps its parent's estimate and stops being split, so an overflow at the five-hundredth
bisection cannot replace an answer that thirty bisections already had right.

### `odeset` and `odeget`, and an `ode45` that reads them

`odeset` holds MATLAB's 22 fields in MATLAB's order, which is neither alphabetical nor derivable and
was read off R2024a because a script that walks `fieldnames(odeset)` sees it. Every unset field is
`[]` rather than absent, which is what lets a solver tell "not asked" from "asked for nothing" —
`optimset`'s convention, kept.

`ode45` reads four of them: `RelTol`, `AbsTol`, `Refine` and `MaxStep`, plus `InitialStep` in the
engine. The other eighteen are stored and read back and nothing acts on them, which is recorded here
rather than hidden; the alternative is refusing a structure over a field the solve does not need.

### Markers are collapsed by pixel, not suppressed by count

The five-thousand-sample constant is kept, and now means something different: above it, samples that
land on the same device pixel are drawn once. Two hundred thousand marks over a plot area of half a
million pixels is bounded by the picture rather than by the data, and it is the same picture, because
a second opaque mark on a pixel already marked adds no ink.

Below the threshold nothing is merged at all. That is deliberate: every series that drew markers
before draws exactly the same ones now, so no existing figure can move.

## Consequences

**The capability probe: 255 of 262 forms accepted, up from 247 — and both named groups are
complete.** "String arrays" is 14 of 14 and "Solvers and quadrature" is 9 of 9. The suite's
`d14_forms_accepted` checksum moves from 247 to 255, which is the one checksum this milestone is
*meant* to move.

**The picture.** `d12_text`'s right-hand panel draws the five rows of dots MATLAB draws, at the same
densities.

**Accuracy.** Over twenty integrands with closed forms — polynomials, endpoint singularities,
infinite ranges, corners, narrow spikes — the worst disagreement with MATLAB is 1.1e-7 against a
1e-6 contract, and none is outside it. `ode45` under `odeset` reproduces MATLAB's own step counts
exactly: 41 points by default, 11 at `Refine` 1, 101 at `RelTol` 1e-8, 401 at `MaxStep` 0.01.

**Tests.** 6,960, up from 6,917, and 69 of 69 stress scripts. Two of the new ones are guards rather
than checks: that a sub-threshold marker series is still drawn one mark per sample, and that a char
matrix is still refused by all six text verbs.

**The report.** Rebuilt with one run per engine rather than a run beside a frozen baseline, and
with what each script cost the machine: processor time, the cores that time spread over, the peak
thread count and the peak working set. Two of the 188 checksums moved and both were meant to —
`d14_forms_accepted` from 247 to 255, and `d14_join_elements` from 1 to 2, onto MATLAB's answer.

Sampling that turned out to be three bugs deep, and all three produce a plausible number rather
than an obvious failure, which is why they are written down here. On Windows `matlab.exe` is a
**launcher**: watching the process the runner started reports six threads and nine megabytes for a
run that used a hundred and ninety and most of two gigabytes, so the whole process tree has to be
walked. Process ids are **recycled** fast enough that a recycled one is read as the process being
watched, which adds a stranger's threads to the peak and makes a processor-time delta come out
negative; each id is now checked against the start time recorded when it was first seen. And
`TotalProcessorTime` after a process exits does not always throw — sometimes it answers **zero** —
so a reading is taken only when it is larger than one already in hand. Before that last guard, one
script's processor time came out as 0.00 s, on a different script each run.

## Divergences

Two are added and one is closed.

- **`integral` of a strongly singular integrand answers a finite number where MATLAB answers
  `Inf`.** `integral(@(x) x.^-0.9, 0, 1)` is 9.79 here, `Inf` from MATLAB's `quadgk` and 9.9934 from
  MATLAB's `integral`; the true value is 10 and none of the three is within its own tolerance. The
  panel that stops being split is what makes this finite rather than infinite.
- **A `split` that would answer a three-dimensional container is refused by name.** MATLAB answers
  1-by-2-by-2 for a row of text; nothing in this build holds a three-dimensional container of text,
  so the refusal names the shape rather than flattening it. This is the same limit `splitlines` and
  `extract` have recorded since M104, now reached by one more verb.

Closed: `join` of an N-by-2 array collapsed the whole array to one string instead of one string per
row. The head-to-head text script's comment describing that behaviour can be deleted.

## Still open

None of these is a difference in what JGraph answers, so none belongs in the list above.

- **`strcat` refuses a string array.** It is the one name in the family still taking scalar text
  only, and it is left because its rule is genuinely different: `strcat` expands *every* argument
  against every other, where the six names here map one subject. It was found by probing rather than
  reported, and is not in either group this milestone was asked for.
- **Seven capability forms remain refused**, all in groups this milestone did not touch: `residue`,
  `nargin('size')`, `histogram2`, `reshape` of a char array, and the `'-o'` marker form of
  `semilogy`, `semilogx` and `loglog`.
- **`^` on a non-square array is elementwise here and refused by MATLAB.** Found while writing a
  test that expected `integral(@(z) z^2, 0, 1)` to be refused and discovering it is not. That is the
  power operator's business rather than quadrature's, and it is wider than this milestone.
