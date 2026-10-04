# MATLAB_Behavioral_Fixes

Places where JGraph **deliberately gives a different answer from MATLAB because MATLAB's own answer
is wrong**. These divergences are permanent. They must not be "fixed" toward parity, and a parity
fixture should never pin MATLAB's answer for them.

The governing rule was set on 2026-09-08 for `s^A` (ADR 0146): JGraph is a MATLAB replacement, not a
bug-for-bug clone. Where MATLAB is demonstrably wrong, JGraph answers correctly and records the
difference. The ADR must show *why* MATLAB is wrong, ideally with a second MATLAB road that agrees
with JGraph, so the claim is measured rather than asserted.

**Source.** This list comes from a row-by-row review on 2026-10-01 of all 456 rows of
[matlab-divergences.md](matlab-divergences.md), HEAD 5970a85. The per-row tables are in
`docs/plans/notes/divergence-review-2026-10-01/`. A row is listed here only if its ADR, or plain
mathematics, shows MATLAB to be wrong. Being merely different, idiosyncratic, or one valid choice
among several does not qualify. The prioritised plan for every other divergence is
`docs/plans/divergence-remediation-plan.md`.

**18 of 456 rows qualify.** Section A holds the 6 where MATLAB's *mathematics* is wrong. Section B
holds the 12 where MATLAB's *behaviour* is wrong: a crash, a hang, a stale state, or MATLAB
contradicting itself. Section C lists 3 places where MATLAB is wrong and JGraph currently copies the
error. Section D lists borderline rows that were considered and rejected.

---

## A. MATLAB's mathematics is wrong (6)

| Index line | ADR | Expression | MATLAB answers | JGraph answers (correct) | Why MATLAB is wrong | Evidence |
|---|---|---|---|---|---|---|
| 333 | [0146](adr/0146-a-scalar-raised-to-a-matrix-is-a-matrix-function.md) | `s ^ A`, A defective, e.g. `2^[2 1;0 2]` | `[4 0; 0 4]` | `[4 4·ln2; 0 4]` ≈ `[4 2.7726; 0 4]` | MATLAB computes `V*diag(s.^diag(D))/V`. For a defective A, V is singular, so the nilpotent term `ln(s)·s^λ·N` is dropped. That term is the largest off-diagonal entry, not roundoff. MATLAB only gets any answer because LAPACK leaves `det(V) ≈ 4.4e-16` instead of the exact 0. | Second road in R2025b: `expm(log(2)*A)` and `funm(A, @(x,k) log(2).^k.*2.^x)` both give JGraph's answer. 82 expressions agree to ≤ 4.9e-16 relative. JGraph uses Schur–Parlett. |
| 368 | [0153](adr/0153-one-transform-where-it-lies.md) | `dct(7)` | `7.0000000000000009` | `7` | The orthonormal DCT-II of a length-1 signal is the identity (scale √(1/1) = 1, cos 0 = 1). MATLAB's 1-ulp error comes from routing a scalar through its FFT. | Fixture `m153_transforms` row `dct_scalar`, `div=ADR0153`. |
| 213 | [0103](adr/0103-a-named-matrix-is-a-rule-about-two-indices.md) | `wilkinson(n,'uint8')` (also `uint16`, `uint32`) | error `MATLAB:sizeDimensionsMustMatch` | the documented Wilkinson matrix in that class (n = 5 diagonal `[2 1 0 1 2]`) | MATLAB counts the diagonal from −m in an unsigned class, which saturates at 0. The diagonal comes out too short and assembly fails. The documented signature promises the matrix. | MATLAB's documented `wilkinson(n, classname)`. Every entry fits the class. |
| 428 | [0182](adr/0182-calllib-pointers-and-structs.md) | `libpointer('int32Ptr', [2.5 -2.5 1e10])` | `[2 -2 -2147483648]` (C truncation plus overflow wrap) | `[3 -3 2147483647]` | MATLAB's own conversion into int32 rounds and saturates. The pointer write uses C semantics instead, so MATLAB contradicts itself. | Second road: `int32([2.5 -2.5 1e10])` in R2025b gives JGraph's answer. |
| 426 | [0182](adr/0182-calllib-pointers-and-structs.md) | a native function returning `UINT64_MAX` | `-1` | `1.8447e19`, with the int64Precision warning | MATLAB reinterprets an unsigned bit pattern as signed, so the value has the wrong sign and the wrong magnitude. *Caveat:* JGraph's value is right but rounded to a double. Exactness waits on real 64-bit integer storage (plan item 15). | Pre-registered R2025b defect, fixture `shrlib_types` row `uint64_max_ret`. |
| 212 | [0103](adr/0103-a-named-matrix-is-a-rule-about-two-indices.md) | `toeplitz([])` | error `MATLAB:badsubscript` | 0×0 empty | MATLAB's `toeplitz.m` reads element 1 before checking that one exists. *Weak case:* an accidental error, not a wrong number. | Second road: `hankel([])` answers 0×0 in MATLAB. No documented restriction exists. |

## B. MATLAB's behaviour is wrong: crash, hang, stale state, or self-contradiction (12)

| Index line | ADR | Situation | MATLAB (R2025b) | JGraph | Why JGraph's answer stays |
|---|---|---|---|---|---|
| 436 | [0184](adr/0184-serialport-and-the-device-objects.md) | `serialport` write held back by hardware flow control (CTS low) | `write` never returns; the probe hung for 400 s | errors after `Timeout` | `Timeout` is documented as the time allowed for reads **and writes**. MATLAB ignores its own property. |
| 447 | [0187](adr/0187-visadev.md) | `visadev` `readline` after a failed `read`/`readbinblock` | VISA's termination character is left off, so every later `readline` times out while `v.Terminator` still says LF | termination restored whatever the outcome | A failed call leaves the object contradicting its own `Terminator` property. Fixture `visa_serial` row `readline_after_failed_read`. |
| 448 | [0187](adr/0187-visadev.md) | `configureTerminator(v,"CR","LF")` | VISA stops on the **write** terminator (LF) while the client looks for CR | VISA stops on the read terminator | A device that answers with CR only is cut wrong or times out. |
| 453 | [0189](adr/0189-hid-and-vrjoystick.md) | `vrjoystick(0)` | MATLAB crashes | refused as `notconnected`, as MATLAB itself does for every other invalid id | A crash is not an answer. |
| 407 | [0177](adr/0177-dotnet-arrays-generics-enums-and-dictionaries.md) | `dictionary(System.Collections.Hashtable())` | crash, "Bad optional access" | converts keys and values as `Object` members | A crash is not an answer. |
| 411 | [0178](adr/0178-dotnet-events-delegates-and-threads.md) | a queued .NET event reaches a since-disabled or deleted listener | access-violation crash (twice); delivery nondeterministic | the event is dropped | Dropping is the only coherent outcome for a listener that no longer exists. |
| 406 | [0177](adr/0177-dotnet-arrays-generics-enums-and-dictionaries.md) | `dictionary` of a .NET dictionary with `System.String` keys | keeps `System.String` keys, then its own `keys()` fails with `MATLAB:dictionary:CannotCombineKeys` | keys become strings; `keys()` works | MATLAB builds a dictionary whose keys it cannot list, a contradiction between two of its own roads. Fixture `net_generics` row `dict_keys`. |
| 427 | [0182](adr/0182-calllib-pointers-and-structs.md) | libstruct with an array field or non-default packing passed to `calllib` | passes NULL; the library sees nothing and array fields read `[]` | passes the struct's memory; fields read typed rows | Silently passing NULL for a valid struct is a defect. Fixture `shrlib_structs`, six rows. |
| 429 | [0182](adr/0182-calllib-pointers-and-structs.md) | `p.Value` read past the end of memory the pointer owns | reads past the allocation (undefined behaviour) | refuses with `JGraph:libpointer:PastEnd` | Memory safety. |
| 423 | [0181](adr/0181-loadlibrary-and-the-prototype-model.md) | `loadlibrary(...,'mfilename','dir/x')` | ignores the folder and writes into the current folder | writes where it was told | MATLAB ignores its own argument. Pre-registered in the interop plan, step 0, finding 9. |
| 432 | [0183](adr/0183-interop-views-completion-and-gate.md) | `methodsview` "Inherited From" column | blanks some inherited rows, and which ones changes from run to run | names the defining class on every inherited row | A nondeterministic column is wrong by construction. Fixture `views_methods`, four rows. |
| 348 | [0149](adr/0149-a-file-takes-a-builtin-name-unless-a-method-claims-the-call.md) | call a name after deleting the file that shadowed it (`delete('eps.m'); eps`) | error "Previously accessible file … is now inaccessible" | falls through to the next layer (the built-in) | MATLAB errors on a stale path-cache entry. The shadow no longer exists, so the built-in is the right answer. A file that exists but cannot be read still gets MATLAB's error. |

## C. MATLAB is wrong and JGraph currently copies it (3)

**Approved for correction on 2026-10-03.** JGraph must not reproduce a MATLAB bug: correct
mathematics outranks parity. Plan item 24 implements the correct answer for each row, and each row
then moves into section A with a fixture that pins the correct value.

| Index line | ADR | Behaviour | What is wrong | Correct answer |
|---|---|---|---|---|
| 304 | [0139](adr/0139-designfilt-is-a-table-and-a-cascade-is-a-closed-form.md) | odd-order `bandstopfir` (window design) | Both engines answer ~1e13 garbage, 18% apart. MATLAB normalises an antisymmetric filter by its tap sum, which is algebraically 0, and JGraph copies that. | Refuse the order, or raise it to even as the Kaiser path already does. |
| 291 | [0138](adr/0138-five-designs-are-one-pipeline-and-one-table.md) | Bessel highpass and bandstop numerator | A 4-fold repeated root is found to only eps^(1/4), so everything past the leading coefficient is noise in both engines. | Build the numerator from the known zero multiplicity (binomial in z∓1). This is exact. |
| 257 | [0123](adr/0123-text-containers-quadrature-and-a-blank-panel.md) | `integral(@(x) x.^-0.9, 0, 1)` (true value 10) | MATLAB's `integral` gives 9.9934 and `quadgk` gives Inf. JGraph gives 9.79 with no warning, the furthest of the three. | The true value (10) within the requested tolerance, via `integral`'s endpoint transformation and adaptive Gauss–Kronrod. Warn "singularity possible" only when the tolerance genuinely cannot be met. Neither engine is right today, so the target is the closed form, not MATLAB's 9.9934. |

## D. Considered and rejected (not MATLAB-wrong)

These were examined and rejected because MATLAB's answer is valid, documented, or merely different.
They stay in the ordinary plan or are accepted as they are.

- **Root and eigenvalue order** (282, 290) and **eigen/singular vector sign** (194): both answers are valid. Document them instead.
- **Rank-deficient `\` basic solution** (329): a different valid basic solution with the same residual.
- **Cascade section order** (303): JGraph's ordering is better conditioned, but MATLAB's is a valid factorisation of the same filter.
- **`single` solved in double** (331, 334): JGraph is more accurate. MATLAB's answer is correct for single arithmetic.
- **`0^A`, `Inf^A`, the sign of zero in `1^A`** (335, 336): neither side has a defensible limit, and −0 == 0.
- **`rcond` estimate** (189), **last-bit `detrend`** (217), **FFT rounding** (216): documented estimates or vendor rounding.
- **Strict `printf` flags** (240), **1×1 complex as a scalar** (241): JGraph is louder by choice, but MATLAB's answer is not a wrong number.
- **BLE identifier leak** (446), **audio callback source object** (456), **`dct(zeros(0,1))` shape** (369): different, but not shown to be wrong.

JGraph also has deliberate *robustness* improvements recorded as DESIGN rather than MATLAB-wrong. They
also stay as they are:

- 349: the library is immune to user shadows.
- 410: a deadlocked delegate times out instead of hanging.
- 417: a native crash is an error, not a dead session.
- 186: waits give up after an hour.
- 439: Stop interrupts a blocking device read.

## Keeping this list honest

- When a new ADR records a MATLAB-wrong divergence, add a row here and say why in the ADR, with the
  second MATLAB road.
- Parity fixtures for these rows must carry `div=ADR####` and test the **correct** answer. Generate
  the expected value from R2025b by asking the correct question (e.g. `expm(log(s)*A)`), never by
  hand.
- If a later milestone makes JGraph agree with MATLAB on one of these rows, treat it as a regression,
  not a parity win.
