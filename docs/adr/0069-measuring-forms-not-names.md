# ADR 0069 — Measuring forms, not names

## Status

Accepted (M69). An audit milestone: it adds measurement, not MATLAB features. Delivered in two
sittings — the first covered forms and two defects; the second added the property axis, the
divergence index, `stess_41.m`, and the rest of the defects. "What is not done" at the end is
current as of the second.

## Context

M1–M68 measured MATLAB compatibility one way: by **name**. A command counted as implemented when
`JgsBuiltinCatalog.cs` held an `Add("name", …)` for it. Every number the project reports is that
count — 413 of 514 builtins, 270 of 409 IPT, 385 of 589 stats.

`docs/matlab-builtin-coverage.md` has said what is wrong with this for several milestones, in its
own words:

> this file counts names, and a script that fails to run rarely fails for want of one.

`sort` is one implemented name. MATLAB documents five syntax forms for it — `sort(A)`,
`sort(A,dim)`, `sort(___,direction)`, `sort(___,Name,Value)`, `[B,I] = sort(___)`. Nothing in the
repository knew whether four of those five worked, and nothing could have: no artifact recorded that
the forms existed.

They did exist, in a file already in use. The R2021b dump that `build-checklist.py` reads for names
carries, per command, every documented syntax form and every documented argument with its role and
value types. Only the names were being read.

## Decision

### Three axes, and only the first is now measured

1. **Syntax forms.** 659 implemented commands in scope (413 builtins + 246 graphics functions) carry
   **2,422 documented forms**; 492 commands have more than one, and 99 take `Name,Value`.
2. **Object properties.** MATLAB documents 5,758 — 147 on `Axes`, 74 on `Scatter`, 66 on `Figure`.
   `JgsGraphicsProperties` builds its table by reflection over the model's CLR types plus about 430
   hand-written aliases, so coverage is whatever the model happens to expose and is knowable only at
   runtime — which is why `probe-properties.py` **builds each object and asks it** rather than reading
   the source. **436 of 1,361 documented properties** are answered across the 26 object kinds it can
   construct. See "The question underneath the property table" below.
3. **Composition.** Features that work alone and fail together. `harvest-divergences.py` now collects
   what the ADRs recorded into `docs/matlab-divergences.md`, and `stess_41.m` asserts a chosen subset
   of it against the running interpreter.

### The form data is versioned, and the base document finally has a verifier

`build-checklist.py` reads a 25 MB HTML dump that lives **outside version control**, in the demo
workspace, so 413 of 514 could not be re-derived from a clean clone. `build-forms-csv.py` now lifts
the part a checker needs into `matlab-r2021b-forms.csv` (5,484 forms over 2,024 documented callables)
and `matlab-r2021b-args.csv` (4,706 arguments), both in the repository and both small enough to diff.

`verify-builtin-coverage.py` joins the two verifiers that have existed since M46 and M53. The base
document — the largest, the most quoted, and the one recording **five** corrections to its own
arithmetic — was the only one with no machine check. It now checks the three headline counts, the
implemented-plus-missing partition, the name count under each missing heading against the number in
that heading, and that no name listed as missing is registered in the catalog.

### The sixth correction, which the new verifier found immediately

The headline "**926 of 2,027** across every callable kind" was counting two different populations.
The 926 came from the checklist tool's pre-checked set, computed over **all 11,063 documented rows** —
properties, methods and classes included — while the 2,027 beside it counted callable rows only. The
honest pair is **910 of 2,024**, the denominator falling to distinct names because three appear twice
and this document has always counted distinct names elsewhere.

That makes six corrections, and it is M60's kind rather than an arithmetic slip: not a miscount, a
wrong statement of what was being counted. It is the first that a machine will catch next time.

### One column measures; the rest are a worklist

The prober runs each documented form through `jgraph.exe -batch` and sorts the outcome into
accepted / refused / undefined / error / unprobed. Of 2,422 forms:

| Verdict | Forms |
|---|---:|
| accepted | 949 |
| refused | 26 |
| undefined | 4 |
| error | 526 |
| unprobed | 917 |

**949 forms are confirmed working by execution.** 154 commands accept every form they document;
**157 accept some and not others** — the number a name count cannot express, and the one worth
working from.

**The other columns were spot-checked, and mostly do not mean what their names suggest.** Twenty
forms were re-run by hand. The `accepted` ones held. Most `refused` verdicts turned out to be the
prober's own generic text argument being correctly rejected — it hands a `character vector` argument
`'a'`, and `xtickformat('a')` answers, rightly, that `'a'` is not a tick format. All four `undefined`
verdicts are `eval('a')` evaluating the sample as code.

So the decision is to **publish one trustworthy column and label the rest as leads**, rather than
report 555 failures the evidence does not support. `docs/matlab-form-coverage.md` says this in the
document itself, because a number quoted out of a table outlives the caveat beside it.

### `unprobed` is a bucket, never a rounding

917 forms — 37% — could not be called at all: a `Name,Value` form, because the dump records *that* a
command takes pairs but not *which* pairs; a form whose arguments have no sample; and the commands
that wait for a person. Folding these into either success or failure would flatter or libel the
build, and the document next door has been corrected six times for exactly that.

### What the prober found, confirmed by hand

| Finding | Evidence |
|---|---|
| `Inf(n)`, `Inf(sz)`, `NaN(n)`, `NaN(sz)` build nothing | MATLAB makes an n-by-n matrix. Here `Inf` is a constant with `AutoCallsBare`, so `Inf(2)` *indexes* the scalar: "Index 2 is out of range for length 1". `zeros(2)` and `ones(2)` work, so this is these two names, not the shape family. |
| A reduction takes one dimension, never a vector of them | `sum(A,[1 2])`, `all(A,[1 2])` and `max(A,[],[1 2])` all refuse. MATLAB's `vecdim` collapses several at once. One gap across the whole reduction family. |
| `regexp`/`regexpi` do not take `'forceCellOutput'` | Refused by name, listing what they do take — the house style working. |
| `axis('state')` is not read | The legacy three-output query form. |

### The question underneath the property table

A property is only reachable through a handle, so before counting properties the prober had to ask
whether the verb hands one back. Four did not: **`colorbar`, `quiver`, `image` and `light` drew and
returned nothing**, so `h = colorbar; h.Label.String = 'Depth'` — ordinary MATLAB — had no object to
write to, and every property those four objects carry was out of reach at once. `colorbar` had a
second half to it: without `AutoCallsBare` the bare name bound the *builtin*, so `h` was a function.

All four now return a handle, and a sweep of **45 drawing verbs** says **39 do and 5 do not**
(`surfl`, `waterfall`, `ribbon`, `trisurf`, `trimesh`). The sweep is hand-written and covers the
families the four were found in; a verb absent from it is unmeasured, not passing, and the report
says so. The same sweep found `pcolor` refusing `pcolor(C)`, a form MATLAB documents.

This is why the axis was worth measuring by execution. Reading `JgsGraphicsProperties` would have
shown a well-populated table for every one of those four objects and said nothing about the fact
that no script could reach it.

### The divergence index is a harvest, and says so

`harvest-divergences.py` reads the ADRs and lifts each recorded divergence into
`docs/matlab-divergences.md`. It collects **40 across 8 ADRs**, of which 35 predate this one — not
the sixty the plan estimated. The
gap is not missing divergences so much as missing *format*: three ADR generations wrote them three
ways (a heading, a bolded lead-in paragraph, plain bullets), the harvester reads all three, and
anything recorded in ordinary prose or in a code comment is not collected at all. The index says
this about itself in its own first section.

**A harvest is not a verification**, which is what `stess_41.m` is for. Fourteen sections: six
asserting the defects M69 fixed have stayed fixed, five asserting a recorded divergence is still
exactly that divergence, three asserting a recorded limit still has the shape it was recorded with.
A divergence that is silently closed now fails the stress gate, which is the point — the ADR and the
index have to move with the behaviour.

One divergence could not be asserted, and the reason is the divergence: an `arguments` block
declaring a name-value argument is refused when the file is **parsed**, so a script cannot contain
one and go on to test that it was refused. That is written where the check would have been.

### Two defects fixed, four recorded

`feval` was two wrong answers in one entry. It refused `feval('sin', x)` — the form MATLAB documents
*first* — by type, and `[a, b] = feval(@f, x)` silently produced one value because the registration
carried no `MultiOutput` body. Both are fixed: the target may be a handle or a name, and the
multi-output body forwards `wanted` to any `IJgsMultiCallable`.

`readmatrix` was listed in the `UnsupportedFunctions` table *and* implemented since M65. The probe
settled it: the registration wins, the table entry was unreachable, and it is removed. Checking the
other ten entries the same way found no further staleness.

### The two silent ones, now fixed

**`global` was interpreter-wide rather than per-workspace.** One `HashSet<string>` held every
declared name with no scope key, so the first function to write `global counter` rewired that name in
every other scope for the rest of the session. A helper's `global counter = 100` overwrote a script's
own `counter`, silently, and nothing tested it.

The declaration now belongs to the workspace that made it: `JgsEnvironment.DeclareGlobal` records it
on the frame, and `IsGlobal` walks outward and stops where an assignment's walk stops — at a call
boundary. That is one rule, reused, rather than a second rule about globals.

It also needed a second, less obvious change. Globals lived in the *base* environment, and in MATLAB
the global workspace and the base workspace are two different places. With them sharing one
dictionary, a top-level `counter` and a `global counter` were the same variable, so the per-frame fix
alone left the top-level case wrong. `_globalWorkspace` is now a scope with no parent, unreachable
except through a declaration.

JGS is gated by where the declaration is recorded rather than by a branch in the lookup: JGS has no
call boundary at all, so recording on the frame would make the declaration die with the block that
wrote it. It records on the globals, and the run-wide meaning JGS has always had is unchanged.

**`evalin('caller', …)` and `assignin('caller', …)` read the current frame.** The interpreter kept no
call history, so `'caller'` resolved to the workspace of the function asking rather than of whoever
called it. `ExecuteFunctionBody` already held the caller's frame in a local to restore it on the way
out; `CallerFrame` now keeps it for the duration of the body. One frame is the whole of what MATLAB's
workspace words can name, so a stack would be machinery for nothing. At the top level there is no
caller and the answer is the base workspace, which is MATLAB's answer too.

### The one that did not reproduce

The plan recorded **`[a,b] = handles{1}(x)` and `[a,b] = s.fn(x)` take one output**, read out of
`EvaluateForOutputs` without a probe. It is not true. Both work, and so do `h{i}(v)` with a variable
subscript, `s.inner.fn(v)`, `t(1).fn(v)`, and all of them wrapped in an anonymous handle. The code
read missed that `EvaluateCallee` falls through to `Evaluate` for a callee it does not special-case,
which resolves a brace or field handle perfectly well.

Recorded here rather than quietly dropped, because it is the milestone's own lesson pointed the other
way: a defect that has never been reproduced is a claim, not a defect.

### The one deliberately left

**`int64`/`uint64` lose precision above 2^53.** A design consequence, not a slip: storage is always
`double` and the class is a tag (`JgsNumericClass`). Probing it found the bound is sharper than
recorded — `intmax('int64')` prints *negative*, because 9223372036854775807 rounds up to 2^63 as a
double and wraps when formatted as an integer. Recorded rather than fixed, with the bound stated and
asserted in `stess_41.m`, because a script author is better served knowing it than not.

## Consequences

**A new document.** `docs/matlab-form-coverage.md` carries the form numbers and is regenerated by
re-running the prober. `docs/matlab-builtin-coverage.md` keeps the name numbers and now carries a
corrected total.

**A document that was thirty-two milestones stale is current.**
`docs/matlab-foundational-coverage.md` — the only one tracking *features* rather than names, which is
why nothing caught its drift — was stamped "as of M36". Five of its notes had stopped being true
(`rng(seed)` since M52; `ndims`, `cat`, `squeeze` and `permute` since M41's N-D arrays), and three of
the four gaps in its closing paragraph had closed while still being advertised. Every note was
re-probed through the CLI rather than reasoned about; the two complex-decomposition gaps it claims
are real and still there.

**A prober is a thing that can be wrong, and this one was, three times.** Each was found by reading
its output rather than trusting it: it read a value-type phrase by table order instead of by which
type the documentation names first, and handed `accumarray` a cell it rightly refused; it recorded
120 forms as errors when a neighbour in the same batch took the process down, which is a statement
about position in a file; and it let fifty forms share one workspace, so `save(filename)` failed on
an `ME` an earlier form's catch block had left lying about. All three are fixed and commented at the
site. The lesson is the milestone's, not the tool's: **a measurement that has never been checked
against reality is a claim, not a measurement**, which is the same sentence as the one about names.

### Recorded divergences

- **`int64` and `uint64` are exact only to 2^53.** Storage is `double` and the class is a tag, so
  `int64(2^53 + 1)` answers `2^53` and `intmax('int64')` formats as a negative number. Asserted in
  `stess_41.m` so the bound cannot drift unnoticed.
- **`Inf(n)` and `NaN(n)` do not build a matrix.** Both names are constants with `AutoCallsBare`, so
  a subscript indexes the scalar. `zeros(n)` and `ones(n)` are unaffected, so this is these two names
  rather than the shape family.

### Closed by a later milestone

These were recorded above when this ADR was written and are no longer true. They sit here rather
than in the list above because `harvest-divergences.py` reads that list to build
`docs/matlab-divergences.md`, and an index that still names a closed divergence is exactly the kind
of stale claim this milestone's machinery exists to catch. The heading deliberately avoids the word
the harvester matches on. What each one became is in ADR 0070.

- **A reduction took one dimension, never a vector of them.** `sum(A, [1 2])`, `all(A, [1 2])` and
  `max(A, [], [1 2])` refused; only the nested spelling worked. **Closed in M70.D**, which gave the
  column-wise reduction wrapper a vector of dimensions and walks it one dimension at a time.
  `stess_41.m` section 13 turned from an assertion that this refuses into an assertion that it
  answers 136 — the mechanism M69 built, working as designed.
- **`surfl`, `waterfall`, `ribbon`, `trisurf` and `trimesh` returned no handle.** **Closed in
  M70.C.** Each now answers with the object it drew, registered silently so a bare unsuppressed call
  still echoes nothing.
- **`pcolor(C)` was refused**, where MATLAB documents the one-argument form. **Closed in M70.B**,
  which generates the implicit grid from the matrix's own row and column numbers.

## What is not done

The six planned waves are delivered. What the milestone did **not** do, so the next one starts from
the truth rather than from the table of contents:

- **The form probe covers base and graphics only.** IPT and stats were a deliberate second pass and
  remain one: 2,940 forms across all 869 implemented callables, of which 2,422 were probed.
- **1,361 of 5,758 documented properties are in scope**, because 26 object kinds are what the prober
  can build. The other 434 documented classes are largely App Designer and hardware objects this
  build has no model for, but that is a judgement and not a measurement.
- **The divergence index harvests 40 of an unknown total.** Anything an ADR wrote in ordinary prose,
  or a code comment recorded, or nobody wrote down, is not in it. M69's own probes found gaps in all
  three categories.
- **`stess_41.m` asserts 14 rows, not 35.** The rest of the index is still prose.
- **The 526 `error` and 917 `unprobed` forms are untouched.** They are the worklist the next
  milestone should be chosen from, and choosing from them means re-running them by hand first.

The gate is green: 0 warnings, **4,678 tests**, **41/41 stress scripts**, all three coverage
verifiers OK.
