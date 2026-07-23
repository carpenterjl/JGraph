# ADR 0028 — Uniform 0-based indexing

## Status

Accepted (M25, 2026-07-22). Supersedes [ADR 0023](0023-matlab-compatible-jgs-surface.md) §3.

## Context

JGS carried two indexing forms with two different bases and very different capabilities:

| | `a(i)` parens | `a[i]` brackets |
|---|---|---|
| base | 1-based (ADR 0023 §3) | 0-based (original JGS form) |
| scalar read, mask, gather | yes | yes |
| `end`, `:` | yes | no |
| slice / mask write | yes | no (single element only) |
| image subscripts | yes | no |

The library disagreed with itself too. `find` and `houghpeaks` returned 1-based indices while `slice`
and `indexof` returned 0-based ones, so `a(find(m))` and `a(slice(b, 0, 2))` could not both be right.
Pixel coordinates were worse: `Regions.Measure` and `HoughTransform` baked `+ 1` into
**`JGraph.Imaging`** itself, putting a MATLAB convention inside a .NET library that the `JG` facade
and C# scripts also use.

ADR 0023 §3 made parens 1-based to buy MATLAB parity — pasted lab code indexed correctly. The cost
was a language with two bases, where "what does index 1 mean" depends on which bracket you typed,
and where every index-returning builtin had to pick a side.

The user, who writes both the MATLAB-shaped scripts and the JGS ones, asked for consistency and
accepted that the shipped examples would need editing.

## Decision

1. **One base: 0.** `a[i]` and `a(i)` are the same operation. `ToIndex`/`ComputePicks`/`GatherOrIndex`
   in `Interpreter` and `PackedOps` lost their `oneBased` parameter outright rather than having its
   default flipped — a dead flag is an invitation for the two forms to drift apart again. Both
   out-of-range messages now read `(indexing is 0-based)`, identically on the boxed and packed paths.
2. **`end` is `length - 1`.** The interpreter's `_indexTargetLengths` stack still holds lengths (it is
   pushed from one place and that is the natural quantity); the single `EndExpr` case subtracts one.
   `a(end)` is still the last element and `a(0:end)` is still everything, so most `end` code ports
   unchanged — `x(end - N/8 + 1 : end)` in the audio example needed no edit at all.
3. **Brackets reach full parity.** `IndexExpr` now carries a subscript *list* (mirroring
   `CallExpr.Arguments`), parsed by the same `ParseSubscripts` helper, so `a[end]`, `a[:]`,
   `a[1:3] = 0`, `a[mask] = v`, and `img[r, c]` all work. Reads route through one `IndexInto` helper
   and writes through one `AssignThroughIndex`; the old single-element `ResolveElement` is gone.
4. **The two spellings still differ in exactly one way:** `f(x)` invokes a function value, `f[x]`
   errors. That keeps brackets an unambiguous "index this" and preserves command-style calls.
5. **Index-returning builtins are 0-based.** `find` and `houghpeaks` changed; `slice` and `indexof`
   were already 0-based and are untouched. The two that *moved* take an optional trailing base —
   `find(mask, 1)` numbers from 1 — because a ported `volt(find(temp > 85))` would otherwise return
   the wrong elements *silently* rather than erroring. The two that did not move get no such
   parameter: the argument exists to soften a base change, not as general surface.
6. **Pixel coordinates are 0-based in `JGraph.Imaging` itself,** not translated at the JGS boundary,
   so C# callers and the `JG` facade agree with scripts. `Regions.Measure` (Centroid,
   WeightedCentroid, BoundingBox origin), `Regions.WeightedCentroid`, `HoughTransform.Accumulate` and
   `.Lines`, and `Geometry.Crop` all dropped their `+ 1`/`- 1`.

## Deliberate exceptions

These are 1-based and stay that way, because they are domain conventions rather than array offsets.
Each carries a one-line reason in its catalog summary so the inconsistency reads as intentional:

- `figure(n)` — a figure *handle*/identity. There is no figure 0.
- `subplot(rows, cols, index)` — a grid cell *number*, row-major, matching every other plotting tool.
- `rfparam(net, i, j)` — RF *port numbers*. `s11` is `rfparam(net, 1, 1)`; renumbering from 0 would
  make every line read wrong against its datasheet.

## Consequences

- **Breaking.** Every paren index in a pre-M25 script is off by one. The five shipped examples were
  updated (`laser-center`, `audio-compression`, `fm-demod`); the two verbatim demo fixtures in
  `JgsMatlabDemoEndToEndTests` were re-synced from them.
- Ported MATLAB code no longer indexes correctly as-pasted. The scripting guide gained a porting
  callout, and `find(mask, 1)` is the one-token fix where rewriting a call site is unwelcome.
- `regionprops`/`imcentroid` results now sit in the same coordinate system as `img(r, c)` and as
  `imshow`'s axes, which is what made the laser example's overlay arithmetic explainable in one line.
- The `IndexExpr` shape change touches live edit (`AstEquals`) — both it and `CallExpr` now compare
  their lists through one `ListsEqual` helper.
