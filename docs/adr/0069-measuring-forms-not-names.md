# ADR 0069 — Measuring forms, not names

## Status

Accepted (M69). An audit milestone: it adds measurement, not MATLAB features. Partially delivered —
see "What is not done" at the end, which is part of the decision rather than an apology for it.

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
   runtime. **Not measured in M69.**
3. **Composition.** Features that work alone and fail together. About sixty are recorded by hand
   across ADRs 0005–0068 with no index; the survey found more recorded nowhere. **Not indexed in M69**,
   though six were confirmed and one fixed.

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

### Two defects fixed, four recorded

`feval` was two wrong answers in one entry. It refused `feval('sin', x)` — the form MATLAB documents
*first* — by type, and `[a, b] = feval(@f, x)` silently produced one value because the registration
carried no `MultiOutput` body. Both are fixed: the target may be a handle or a name, and the
multi-output body forwards `wanted` to any `IJgsMultiCallable`.

`readmatrix` was listed in the `UnsupportedFunctions` table *and* implemented since M65. The probe
settled it: the registration wins, the table entry was unreachable, and it is removed. Checking the
other ten entries the same way found no further staleness.

Four remain recorded and unfixed, three of them silent:

- **`global` is interpreter-wide, not per-workspace** (`Interpreter.cs:54`, `:620`, `:870`, `:2219`).
  `_globalNames` is one `HashSet<string>` with no scope key, so once *any* function runs `global x`
  the name resolves to the global slot in every scope for the rest of the session. No comment, no
  test, no ADR before this one. Deferred because it touches name resolution for every script and
  wants the care M68's call boundary got.
- **`evalin('caller', …)` reads the wrong workspace** (`JgsBuiltins.Eval.cs:276`) — it resolves to
  the current frame. Commented at the site, recorded in no ADR until now. It needs one frame of call
  history the interpreter does not keep.
- **`[a,b] = handles{1}(x)` and `[a,b] = s.fn(x)` take one output** — `EvaluateForOutputs` does not
  special-case a handle reached through a brace or a field.
- **`int64`/`uint64` lose precision above 2^53.** This one is a design consequence, not a slip:
  storage is always `double` and the class is a tag (`JgsNumericClass`). Recorded rather than fixed,
  with the bound stated, because a script author is better served knowing it than not.

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

## What is not done

M69 was planned as six waves and delivers three and a half. Stated plainly so the next milestone
starts from the truth:

- **Wave C, the property prober** — not built. Axis 2 remains unmeasured.
- **Wave D, the composition index and `stess_41.m`** — not built. The sixty scattered limits are
  still scattered, and nothing yet regression-tests a recorded divergence.
- **Wave E** — two of six defects fixed (`readmatrix`, `feval`). The `global` and `evalin` fixes and
  multi-output through a brace or field handle are not done.

The gate is green as it stands: 0 warnings, **4,676 tests**, 40/40 stress scripts, all three coverage
verifiers OK.
