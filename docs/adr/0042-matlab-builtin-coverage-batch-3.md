# ADR 0042 — Builtin coverage, batch 3

## Status

Accepted (M39, 2026-07-28).

## Context

M38 left 46 documented builtins recorded as implementable and 142 excluded by family. This milestone
takes the implementable remainder — **326 → 363 of 514** — and, in the course of doing so, has to
build the two numerical kernels M38 declined to write and settle where the line falls between "a
builtin JGraph does not have" and "a capability JGraph's figure model does not have".

## Decision

### The Bessel functions get a real kernel

M38 recorded the Bessel family as planned rather than done, on the grounds that a version good to
eight digits under a name used for filter design would be worse than nothing. That reasoning was
about the *tractable* approaches — a power series, a Hankel asymptotic expansion — each of which
leaves a band in the middle of the order/argument plane where most of the digits are gone.

Steed's method does not have that band. A continued fraction gives the logarithmic derivative, a
second fraction (or, at small argument, Temme's series) fixes the normalization through the
Wronskian, and stable recurrence carries the answer to the order asked for. `BesselFunctions` is
written that way, and the tests assert the properties that a nearly-right implementation fails: the
Wronskians, the three-term recurrence, and the collapse to elementary functions at half-integer
order, which holds at every argument rather than at the few a table lists.

The I/K routine works in exponentially scaled terms throughout and applies e<sup>±x</sup> once at the
end. That is what makes `besselk(0, 800, 1)` an ordinary number where `besselk(0, 800)` is a
correctly underflowed zero, and it is the same trick that made `erfcx` honest in M38.

Temme's series needs (1/Γ(1−x) − 1/Γ(1+x))/2x for |x| ≤ ½, which is a difference of two numbers that
both tend to 1 — it loses a digit for every decade x is below 1, and published implementations carry
a Chebyshev fit to avoid it. Writing it as e<sup>−s</sup>·sinh(d)/x, with s and d the half sum and
half difference of the two log-gammas, removes the cancellation using the log-gamma that already
exists: d ≈ −γx is itself a sum of same-signed terms, and s, which does cancel, is only ever
exponentiated to something near 1.

### The Schur decomposition, and the eigenvalue bug it found

`schur` is the Francis double-shift QR iteration over the Hessenberg reduction M38 added, and
`ordschur` moves chosen eigenvalues to the top by exchanging adjacent diagonal blocks. Each exchange
solves the small Sylvester equation naming the second block's invariant subspace and rotates onto an
orthonormal basis for it — three lines that work for every combination of 1×1 and 2×2 blocks,
instead of a case for each of the four pairings.

Validating it turned up a real bug: **`eig` on a general non-symmetric matrix was returning wrong
eigenvalues.** M36's complex shifted-QR path produced a set that was not closed under conjugation and
whose product was nowhere near the determinant — for one 7×7 example, 2.24 + 13.9i against a
determinant of 177.68. `Eigen.FactorGeneral` now reads its values off the real Schur form, which is
produced by an orthogonal similarity that reassembly checks; the values reproduce the trace and the
determinant to fourteen digits, and the conjugate pairs come out exactly paired without the
symmetrizing pass that used to paper over the drift. The eigenvector inverse iteration also starts
from correct eigenvalues now.

The tests for this assert the trace and the determinant rather than comparing against another
routine, because that is what caught it: two implementations agreeing proves nothing when one of
them is wrong.

`qz` and `ordqz` — the generalized problem — stay recorded. The real QZ iteration is its own piece
of work to write and validate, and the same argument that applied to Bessel before this milestone
applies to it now.

### qr(A) is the full factorization

`qrupdate` needs Q to be a basis for the whole space, not just A's range, and found that
`[Q, R] = qr(A)` was returning MATLAB's *economy* form. It now returns the full one, with `qr(A, 0)`
for the economy form, which is what MATLAB means by each spelling. `QrDecomposition` gained `FullQ`
and `FullR` alongside the existing economy properties.

### A bare name that is a zero-argument question has to answer

`disp(computer)` must print the platform, not hand `disp` a function. M37 built `AutoCallsBare` for
exactly this and applied it to the constants; this milestone applies it to every zero-argument
query, which fixes `pwd`, `filesep`, `ispc`, `cputime` and their neighbours from M38 as well as the
new ones. Callee position is still exempt, so `computer('arch')` reaches the function.

A keyword after a dot is now a field name, because `functions()` returns a struct whose first field
is called `function` and there is nothing else the word could mean in that position.

### func2str prints from the tree

`AstPrinter` renders an expression back to source. The alternative — keeping a slice of the original
text on every anonymous function — would echo the caller's spacing back, at the cost of carrying
byte offsets through the lexer, the parser, and every node, for one builtin. Printing from the tree
costs nothing at parse time and normalizes: `@(x)x.^2` and `@(x) x .^ 2` come back identical, which
is the more useful answer when comparing two handles.

`inputname` needs the caller's argument expressions, so the interpreter hands the `CallExpr` to the
frame the call is about to create — one field write, where building a list of argument names would
be an allocation on every call in the language.

### Where a setting exists but the behaviour does not, keep the setting and say so

`echo`, `more`, `beep` and `fftw`'s planner describe a teletype session and a planning transform that
JGraph does not have. Each keeps and reports what it was set to, so a ported script runs and reads
back its own setting, and the coverage document says plainly that nothing happens. `recycle('on')`
is the deliberate exception: it is refused, because a caller turns recycling on precisely so that a
mistyped name stays recoverable, and reporting success there would be a lie with consequences.

### Six of what is left are figure-model work, not builtins

`fill`, `fill3`, `patch`, `plot3`, `line` and `text` are in the documented builtin list, but each
needs a drawing primitive the figure model does not have — a filled polygon, a 3-D line — which
means a plot object, a renderer branch, `.graph` serialization, and inspector support. That is a
figure slice rather than builtin coverage, and doing it half way (a builtin with nothing behind it)
would be worse than recording it. `docs/matlab-builtin-coverage.md` names them as the most useful
thing left.

## Consequences

- Builtin coverage goes from 326/514 to **363/514**; across every callable kind, 529/2,027. The
  remaining 151 are accounted for one by one in the coverage document.
- `eig` returns correct eigenvalues for general non-symmetric matrices, which it did not before.
- `[Q, R] = qr(A)` changes shape: Q is now m-by-m and R m-by-n. `qr(A, 0)` gives the old result.
- `JGraph.Numerics` gains `BesselFunctions` and `LinearAlgebra/Schur`, and `Factorizations` gains
  `RankOneUpdates` — all usable from C# and Python scripts too.
- `JGraph.Maths` gains `Contours/ContourPaths` (which assembles marching-squares segments into
  polylines, useful well beyond `contourc`) and `Geometry/Delaunay`.
- `ScriptContext` gains an init-only `ScriptPath` so the batch launcher, which runs whole files
  through an entry point that carries no source id, can still tell `mfilename` where it is.
- A keyword can be used as a struct field name after a dot.

## Testing

Two kernel suites — `BesselFunctionsTests` and `SchurTests`/`RankOneUpdateTests` — plus three
script-level ones: `MatlabBesselBuiltinTests`, `MatlabSchurBuiltinTests`, `MatlabGeometryExtraTests`,
and `MatlabSessionBuiltinTests`.

The assertions are chosen so that a plausible wrong implementation fails. The factorizations are
checked by reassembling the matrix they came from and by orthogonality, never against stored factors,
because the factorization is not unique. The spectrum is checked against the trace and the
determinant. The special functions are checked against closed forms, their own identities, and — for
one value where the tabulated digits were in doubt — against the defining integral evaluated by a
trapezoid rule, which converges spectrally because the integrand is even.
