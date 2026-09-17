# ADR 0162 — A payload knows how many hold it

## Status

Accepted. Stage V1 of the value-ownership plan
(`docs/plans/zfit-copies-and-temporaries-plan.md`), which follows V0's fixture and ratchet (ADRs
0162–0173 are the plan's stages; V0 landed as commits 9a39ae4, 2515425, 5d2f30c, 53ec6d8 and
c402ca2 without an ADR of its own, being test material). V1.0 (the count representation) and V1.1
(the audit) landed as 04f1d96 without moving an answer; V1.2 (the change) and V1.3 (the tripwire)
land with this ADR and flip the parity lines V1 owns.

## Context

The plan's model (M1–M17) gives every mutable payload a holder count, and puts a gate in front of
every in-place write: a write detaches when more than one entry holds the payload. That is what
makes both halves of the plan possible — the value-isolation defects of appendix A, and the shared
reads the warm `zfit` gap is made of.

Two questions have to be answered before any of it is written.

**How a count reaches a payload that is a bare array.** `NumericBuffer`, `JgsPackedComplex`,
`JgsStructArray` and `JgsObject` are classes and can carry an `int`. Three payloads cannot: a boxed
element array and a cell slot array are both `JgsValue[]`, and a struct element is a
`Dictionary<string, JgsValue>`. M1 proposes a `ConditionalWeakTable<object, StrongBox<int>>` keyed
by the payload, absence meaning one holder, and names the fallback: change those payloads'
representation to a small holder class, which reaches every `AsCell` and `AsStruct` call site in the
engine. V1.0 measures the table before the model is built on it.

**Which roads reach a payload at all.** The model's safety is the claim that every in-place write
goes through a gate and every second holder is counted. That claim is only as good as the list of
roads, and the list has never been written down. V1.1 writes it.

## Decision — V1.0, the count representation

**The table is kept.** A throwaway rig (`v1bench`, never committed) measured both mechanisms at the
four operations the model performs, against a table populated with 0, 1e3, 1e5 and 1e6 live entries,
because a miss costs what the table's size makes it cost. Medians of five, two agreeing runs on an
idle machine; a third earlier run reported deltas two to three times smaller, which is this
machine's spread, not a different answer.

| live entries | gate, unshared: table | field | delta | share + release: table | field | delta |
|---:|---:|---:|---:|---:|---:|---:|
| 0 | 2.6 | 0.5 | 2.1 | 19.2 | 13.2 | 6.1 |
| 1,000 | 21.0 | 2.0 | 18.9 | 47.3 | 24.3 | 23.1 |
| 100,000 | 26.8 | 1.7 | 25.1 | 43.5 | 24.6 | 18.9 |
| 1,000,000 | 42.6 | 2.1 | 40.5 | 46.4 | 24.5 | 21.8 |

(nanoseconds per operation. The field column rises with the ballast too: that is the heap, not the
mechanism.)

So one count operation costs the table between 2 and 40 nanoseconds more than a field. What that is
a share of was measured in the engine itself, with a second throwaway switch that ran a real
`ConditionalWeakTable` lookup at each of the three gates and a real increment at each binding
(`JGRAPH_V1_PROBE=2`), against the same binary counting only:

| road, one tight loop of it | ns per operation | table adds | the loop's own spread |
|---|---:|---:|---:|
| `c{k} = v`, cell slot write | 426 | 17 | ±469 |
| `s.a = v`, struct field write | 195 | 1 | ±243 |
| `S(k).a = v`, struct element write | 16,755 | 1,250 | ±10,707 |
| `b = C`, cell binding (8 slots) | 327 | 9 | ±278 |
| `a(i) = v`, packed element write | 31 | 3 | ±41 |

Every row's delta is inside its own noise band, which is the plan's decision rule. It is inside it
by two orders of magnitude on the roads that matter, and the machine cannot resolve the difference
at all end to end.

The other half of the measurement is how many such operations real scripts perform. The same probe
counted them over the stress suite's cell- and struct-heavy scripts:

| script | table-keyed gates | shares of a cell or boxed array |
|---|---:|---:|
| `stess_50` | 97 | 11 |
| `stess_40` | 84 | 25 |
| `stess_33` | 38 | 27 |
| `stess_67` | 20 | 20 |
| `stess_37` | 16 | 18 |
| `stess_23` | 9 | 5 |
| `stess_73`, `stess_78`, `stess_85` | 0 | 40, 42, 74 |

Tens of operations per run, against a cost of tens of nanoseconds each: the table is free in every
script the suite holds. What the same run showed about the model's *value* is worth recording beside
it — those scripts pay 18 to 210 per-child copies inside `CopyContainer` per run, and the tight-loop
probe pays 3.6 million, every one of which M2 replaces with a single share.

**Where the table would stop being free, recorded so the fallback has a tripline.** An entry is not
free away from the call: creating one costs 144–535 ns and holds 54–76 bytes, and a million live
entries lengthen every blocking gen2 collection from about 5 ms to 50–150 ms, because the GC walks
a dependent handle per entry. The shape that would create entries in bulk is M3's shallow detach of
a large container, which shares every child at once — for a struct array, one element dictionary
each:

| elements shared at once | table | a field on the element | plain dictionary | a dictionary carrying the field |
|---:|---:|---:|---:|---:|
| 1,000 | 1.2 ms | 0.0 ms | 240 B | 248 B |
| 100,000 | 61 ms | 2.2 ms | 240 B | 248 B |
| 1,000,000 | 637 ms | 25.0 ms | 240 B | 248 B |

No workload measured here reaches that shape; the stress suite's struct arrays are tens of elements.
If one ever does — a write into a shared struct array of more than about 1e5 elements — the fallback
applies to struct element dictionaries alone, and it is cheaper than the plan assumed: a dictionary
that **derives** from `Dictionary<string, JgsValue>` and carries the count as a field costs 8 bytes
an element and changes no reader at all, because `AsStruct` still hands back a
`Dictionary<string, JgsValue>`. Only the 137 places that construct a struct element would move to a
factory. Cell slot arrays and boxed element arrays have no such trick, and keep the table whatever
happens.

## Decision — V1.1, the audit

`tools/ownership/audit-ownership.py` scans `JGraph.Scripting` and `JGraph.Objects` and sorts every
access to a payload into *read*, *fresh*, *write*, *adopt*, *mutate* or *dispose*. It follows a
payload through the locals that hold it, so `var planes = value.AsPackedComplex;` makes `planes`
borrowed and a span store into it two statements later is a write into someone else's storage —
which is how the appendix's live defects are actually spelled, and what a scan of the accessor's own
line cannot see. Parameters are seeded as borrowed when their type is a payload type; a method whose
every return is minted in place is learned and believed at its call sites, in two passes, so that a
`Reshape` on such an answer is not reported. The numeric kernels are not scanned: they write into the
buffer they are handed, and the site that decides whose buffer that is belongs to the engine. They
are read for their signatures instead, so a call that hands one a borrowed payload is caught where
the call is written (34 kernels take a destination).

| category | sites |
|---|---:|
| read | 536 |
| fresh | 2,977 |
| write | 48 |
| unclear | 132 |
| adopt | 11 |
| mutate | 10 |
| dispose | 3 |

Reads and fresh payloads are counted but not recorded: neither can lose a write, and both change
with every builtin written. The other 204 are recorded in `tools/ownership/ownership-audit.csv` with
the rule each must satisfy and a note that settles it, keyed by file, member, road and statement
rather than by line number so the record survives edits above it. Running the tool with no argument
re-scans and fails on any difference, which makes an unclassified write or adopt a gate failure from
here on; V1.3's tripwire reads the same file.

**The 48 writes** are the roads M7's gated setters must own. Six are calls of the wrapper's own
setters, which M7 gates from the inside — `WriteElement`'s and `TryPackedParenWrite`'s
`SetPackedNumber`, ADR 0160's compiled store in `RunHotLoop` (the walk and the loop share the
setter, so they share the gate), and `Grow`/`GrowVector`'s `TryGrowInPlace`. Sixteen more are the
interpreter's own:
`WriteElement`'s boxed and complex stores, `AssignThroughIndex`'s three element roads,
`TryPackedParenWrite`'s scatter, `TryPackedComplexParenWrite`'s plane stores, `AssignToMember` and
`ResolveStructForWrite`'s field stores, `AssignToBraceIndex`'s five slot stores, and
`TryAssignToObject`'s property store. Six more are the keyed-collection verbs `Put` and `RemoveAt`
writing into the argument's own struct — appendix A #20 and #21, live today — and six are
`videoWriter`'s counters doing the same. The rest fill storage the method or its caller allocated.

**The 11 adopts** are where a payload gains a second holder without a count today, and six of them
are one thing: a struct array's element dictionaries handed to a second array. `IndexStruct` is
appendix A #12 exactly; `AssignIntoStruct`, `StructElementValues`, `MapStructElements` and
`ElementsOfPicks` are the same shape. V1.2 shares or copies at each.

**The 10 mutates** are one family: the tag-carrying helpers (`KeepShape`, `CarryValueTags`,
`KeepNumericClass`, `KeepTextKind`, `WrapCharMatrix`, `PromoteText`, `JgsMatrix.Like`) applying an
M4 mutator to a wrapper their caller minted. M4 allows it; V1.2 states the contract on each helper
so the tripwire can hold it.

**The 3 disposes** are `JgsRunner.DisposePackedIn`, the disposal walk M6 rewrites, and one scratch
plane inside a transform's mint.

**The 132 unclear** are sites whose answer is in the callee: a mint helper handed a buffer
(`PackedReduceOps.MintScalars`, `PackedSortOps.Mint`, `JgsMatrix.FromElements`, …), or a mutator on
what another method returned. They are recorded with the rule that V1.2 states the contract on the
helper. Two of them are named here because they are not a family: `WriteElement`'s two
`DemoteToBoxed` calls change the entry's own wrapper, which M2 is what makes safe, and M3's detach
has to run before them.

**The four payloads the model does not name** were classified too, as the plan asks. `Table` is
immutable (every property is `get`/`init`, and a column write rebuilds it through `WithColumn`);
`CscMatrix` is documented immutable and its row view is a cache; `ImageBuffer` is mutable through
`Pixels`, but the engine writes only into images it just allocated (`ImageCompatibility`'s crop and
colour conversions, `ParametricSpectra`'s answer); `containers.Map` and `dictionary` are struct-backed
and reach the audit as the `Keyed.cs` write rows above. None of the four needs a count of its own in
V1; `ImageBuffer` joins the tripwire's watch list so a write into a borrowed one would be caught.

## Decision — V1.2, the change

The count of V1.0 now guards the roads of V1.1. Every piece the audit named is built.

**M1 — the count.** `JgsHolders` (new) holds it: a `ref int HolderSlot` on `NumericBuffer`,
`JgsPackedComplex`, `JgsStructArray` and `JgsObject`, and the `ConditionalWeakTable` V1.0 measured
for the three bare payloads (a boxed array, a cell slot array, a struct element dictionary). Zero
means one holder, so a fresh payload needs no initialisation. Increments and decrements are a
saturating compare-and-swap: a count that reaches `int.MaxValue` never moves again and the payload
is shared for ever, because a conservative count only ever over-states, a dead wrapper never
decrements (M3), and an `int` that wrapped would read a shared payload as unshared — the one error
the model may not make. `HolderSaturationM162Tests` holds that boundary, concurrent shares included.

**M2 — a binding shares.** `CopyForBinding` returns `JgsValue.Share(value)` behind
`JgsOwnership.Enabled` (`JGRAPH_COW=0` restores copying for one milestone). `Share` mints a second
wrapper over the one payload and adds a holder; the two names are distinct wrappers of one payload,
which is what lets a later write tell them apart. A *minting* expression — a literal, a range, an
operator result, an anonymous function (`MintsItsValue`) — is adopted rather than shared, so a
temporary nobody else can reach is not counted as a holder and does not force a copy at the name's
first write. A **call's** answer is still shared, deliberately: only V2.1's audit can say which
builtins hand back a wrapper they kept, so until then the safe reading is that they might, at the
cost of one copy at the name's first write — the copy today's engine already makes at the binding.

**M3/M7 — a write detaches, and the gate lives in the setter.** `JgsValue.Detach` copies the
payload when another entry holds it (a shallow copy for a container: the slot array or element array
is fresh, and each child is `Share`d, so the detach is O(width) not O(tree)), then releases the old.
Every in-place write goes through a gated accessor that runs `Detach` first — `WritableCell`,
`WritableArray`, `WritableBuffer`, `WritablePlanes`, `WritableStructArray`, `WritableStruct`,
`WritableFields`, `WritableElement`, and the setters `SetSlot`, `SetPackedComplex`,
`SetPackedNumber`, `TryGrowInPlace`. The interpreter's sixteen write roads and the keyed-collection
verbs now reach storage only through these. `EvaluateForWrite` (new) resolves a write's target
*through its entry*, detaching down each level of a path, so `t = s; s.x(1) = 5` no longer writes
into both — M16's shape, one milestone before M16's ordering. The audit records each gated road as
`write | gated`, so the census still lists every road that changes a payload; the tripwire and the
Python audit read the same list.

**M6 — disposal frees only what nobody else can see.** The disposal walk (`JgsRunner`) reads a
payload's count without moving it and stops at a container another entry holds, so `clear` never
frees a child a live name still reads. A payload also carries a sticky **exposed** mark, set where a
wrapper is kept without a counted holder — a JGS-dialect binding (M17) and `setappdata` (#152) — and
the walk skips a marked payload outright, because the count cannot speak for a holder it never
counted. Exposure defers a free to the finalizer, which is safe for readers but unbounded for
resources, so the allocator gains a resource bound rather than leaving it to the managed heap:
`BufferAllocator` keeps two budgets, native RAM headroom and a separate mapped budget (outstanding
mapped bytes against the volume's free space less a reserve, plus a live-file ceiling), and
`MappedBuffer` registers memory pressure like `NativeBuffer`. When a request would cross the budget
of the backend it is headed for, the allocator collects and runs finalizers once, re-checks that
budget, and only then serves it (a native request still falling back to mapped) or refuses it with
MATLAB's `Out of memory.` — never disposing a payload a live wrapper can still read.
`ExposedReclamationM162Tests` runs a 200-iteration churn loop on each backend with no forced
collection, asserting the budget holds and every retained reader still reads.

**What moves.** The parity slice's `value_isolation_*` fixtures record the change under the ratchet,
stamped in both lanes. The flips are the appendix lines V1 owns or reaches early: the struct-array
new-field invariant across every entry kind (now printing `[]` as 0-by-0 where the borrowed filler
was 1-by-0), M10's fresh-gather write on logical arrays (appendix #156's crash), the loop-source and
index-target scope lines for cells and struct arrays, `a012` (**#12**, V1's own) and `a013`. Lines
whose output changes without fully flipping — the eight `w_paren_field_sub_*` still V3's, and three
`[]`-shape baselines still V2's — are re-stamped with their new baseline in the same commit, as the
ratchet requires. No line that already agreed regressed.

## Decision — V1.3, the tripwire

`OwnershipGateTests` scans `JGraph.Scripting` line by line and fails on a raw write into a payload
reached from a non-fresh value — an indexed store through an accessor, a span store into a buffer or
a plane, a scatter into a buffer, an element-dictionary store — and on a `JgsValue` constructor or a
payload class minted straight over another value's payload, outside a named allow-list. Each allowed
line carries the reason the model is not broken by it (a payload the method just allocated, or a
handle whose counters are meant to be seen through every name), and the list is checked in both
directions, so an entry that no longer matches any line fails too and cannot rot into permission for
something else. This is the fast guard that runs with the suite and reads one line at a time; the
Python audit is the census that follows a payload through the locals that hold it. The two together
are the standing claim that every write is gated.

## Consequences

- The model is built on the table, and the fallback has a measured tripline rather than a guess.
- The gate gains a step: `python tools/ownership/audit-ownership.py`, beside the four coverage
  verifiers. It fails on a write or an adopt that appears without being classified, and it now
  knows the gated accessors, so a store through one reads as the write the model sanctions rather
  than as a raw one.
- Two guards stand over the write roads from here on: the Python census and `OwnershipGateTests`.
- V1.2 shares where the engine used to copy at a binding, and copies on the first write instead. The
  regression probes (`probe_z_cow_writes`, `d03_loop_2M`, the `d12` rows, the stress suite's slowest
  ten) stay inside their noise bands.

## Divergences

None. V1.0 and V1.1 move no answer; V1.2 flips the appendix lines it owns to agreement and re-stamps
the baselines it changes, and leaves nothing on a `div=` line.
