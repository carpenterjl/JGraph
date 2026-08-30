# ADR 0111 — A value is what it holds, not how it was written

Milestone: **M110**
Status: accepted

## Context

This came from the task-chip backlog rather than a plan row, as M108 and M109 did. Twenty-three
chips stood open, spawned across four months of milestones by whichever session had walked into the
defect while doing something else.

The first decision was not to trust any of them. A chip is a claim about a build, and the build has
moved since; the claim has not. So every one of the twenty-three was reproduced against `HEAD`
before anything was spent on it, which took one CLI build and eighteen short scripts. **Nine of the
twenty-three were already closed** — `max(A, [], dim)` and the N-D reductions and `diff(X, n, dim)`
by the milestones that followed the chips, the empty literal's shape by M96b, the char matrix's
class and size by M105, `gradient`'s per-dimension spacing somewhere between, and the 3-D tick
labels and the folder listing by the two sessions immediately before this one. Working those would
have produced nine confident reports of nothing.

That ratio is the reason the triage is written down here rather than assumed. It is the sixth time
in this project's history that a recorded gap turned out to have been closed before anyone read the
record again.

The fourteen live chips were then given to seven agents working in parallel, one git worktree each,
partitioned so that no two agents held the same file. What that partition could not do is stop two
agents from disagreeing about *behaviour*, and one pair did; the story is under **Testing**.

## Decision

Almost every one of these defects is the same sentence said in a different place: **a value is what
it holds, and not how it was written.**

`[1]` is the number one, so a builtin that wants a scalar takes it. A complex scalar is a one-by-one
array, so a subscript reads it. A char row repeated is a longer char row, not a row of pieces. A
diagonal of a matrix is a column, because that is the shape it has, not the shape that falls out of
the loop that collected it. An integer array holds only what its class can hold, so that is what
goes into it. `Inf` names a value, and in MATLAB naming a value with a size after it builds an array
of it.

Reading them that way is what let each fix be small. Not one of them needed a new algorithm; every
one of them is an existing path being reached by a value that had been falling past it.

### The one-element array

`Num`, the shared reader every builtin uses for a scalar argument, refused an array of one — while
`isscalar([1])` was true, `numel([1])` was 1, `size([1])` was `[1 1]` and `zeros([1])` worked. It was
this program disagreeing with itself, and MATLAB draws no distinction at all.

It now unwraps a one-element numeric array to the number it holds. The refusals that stay are the
ones that mean something: an empty, an array of two or more, a cell, a complex, and text. The
descent through nested one-element arrays is bounded at four levels, and the bound is not
decoration — in the boxed lane `a = [1]; a(1) = a` really does store the array inside itself, and an
unbounded descent hangs on it.

The two readings that must still tell a scalar from an array — the empty second argument of
`max(A, [], dim)`, and a size vector like `zeros([2 3])` against `zeros(2, 3)` — reach their values
through different helpers, which was confirmed rather than assumed before the change was made.

### The complex scalar

A complex array had always indexed. Every real scalar had always indexed. Only the shape in between
fell past both tests to the arm that reports the value is not a function, so `z(1)` and the
idiomatic `z(:)` were refused for it. The fix is a two-line predicate and its use at the two places
that promote a scalar to the one-by-one array a subscript reads out of — the paren form and the
bracket form. No indexing code was written; the complex scalar simply now reaches the code that was
already there.

### Inf and NaN take a size

In MATLAB these two are constructors as well as constants. Here they were constants with
`AutoCallsBare`, so `Inf(2)` *indexed* the scalar and answered that 2 is out of range for length 1.

They now borrow `NdConstructorValue`, the same shape reader `zeros` and `ones` use, rather than
growing a second one beside it — so every size form those accept, these accept. Only the class tail
is narrower, because no integer class holds an infinity or a NaN. Made bare, so a mention with no
parentheses is the plain scalar it always was, and only in the MATLAB dialect: nobody writes
`Inf(2, 2)` in JGS.

### The diagonal and the eigenvalues

`diag` and `eig` answered rows where MATLAB answers columns. Under implicit expansion that is not a
shape error a script can see — `eig(A) - b` against a column becomes a plausible outer product
rather than a complaint — which is what made this the most valuable item in the wave.

A second defect stood behind the reported one. The vector-or-matrix reading was made by asking
whether the argument was a matrix, and a column is one, so `diag([1; 2; 3])` took the extract branch
and answered its own first element instead of building the three-by-three. The reading is now made
by shape alone: one row or one column builds, anything wider than one in both directions extracts.
Correcting only the orientation would have broken `tril(A) + triu(A) - diag(diag(A))`, because the
inner `diag` would then have fed a column to the outer one.

`EigenvalueList`'s `asColumn` parameter was removed rather than flipped — there is one shape now, so
there is nothing to choose. A sweep of twenty linear-algebra verbs' shapes against MATLAB then found
`ordeig` answering a row as well, which no chip had named.

### What is stored is what the class can hold

This one was not in any chip. It was found while reproducing the chip next to it, and it is the most
serious defect in the wave.

An indexed write into an integer-class array stored the value unclamped. The clamp was applied when
a single element was read and when arithmetic ran over the array, but never on the way into storage,
so every verb that read the array wholesale saw a value the class cannot hold. After
`x = uint8([10 20 30]); x(2) = 300`, reading `x(2)` gave 255 while `sum(x)` gave 340, `max(x)` gave
300 and `double(x)` kept the 300. MATLAB gives 295 and 255. It was identical in both storage lanes,
so it was not the lane split the chip was about — it was a silent wrong answer in every integer
array that had ever been assigned past its range.

The clamp is now applied at the write, in one place each for the element path and the bulk path,
rather than by teaching every reader to clamp. Growing an array past its end carries the class
across the rebuild — deliberately only the class, because growth fills with a numeric zero, which is
a `uint8` zero and a `single` zero at once but is not a char, a string or a date. Deleting from an
array carries all four tags, which is safe there because deletion invents no cells; that was a third
divergence, in both lanes, that nothing had recorded.

The boxed lane's two known losses went with it. Elementwise arithmetic dropped an N-D shape because
the model was read for a shaped array only and reshaped to MATLAB's two-dimensional *view* of it;
the `1×1×4` case was worse than reported, since one row meant it was not recognised as a model at
all and the answer came back a bare row.

### The printf flags

`+`, `-`, space, `#` and `0` were unsupported; `+`, space and `#` were never parsed at all, and were
read as the conversion character.

The rule that decides this implementation, and the one that a from-memory port of C's printf gets
wrong, is what MATLAB does with a value the named conversion cannot hold. A fraction, or a negative
reaching `%u`, `%o` or `%x`, is rewritten as `%e` — **keeping every flag, the width and the
precision**. So `%+u` of 2.5 gains a sign that `%u` itself ignores. Two smaller rules matter as much:
a precision on an integer conversion is a minimum digit count and switches the `0` flag off, without
which `%#o` cannot be right; and MATLAB zero-pads *text*, so `%06s` of `'ab'` is `0000ab`, which is
not C's rule either. The re-conversion is gated to the MATLAB dialect, because JGS's own rounding is
asserted elsewhere.

### The spelling of an infinity

An infinity displayed as `Infinity`, which is .NET's word, where MATLAB and JGraph's own `num2str`
both write `Inf`.

Where that fix went is worth recording, because the obvious place is wrong. One level below the
display formatter sits the helper `writematrix` and `writecell` share, and **JGraph's own
`readmatrix` parses `Infinity` and does not parse `Inf`** — so naming it there silently broke
JGraph's own CSV write-then-read round trip. The naming went into the value formatter instead, and
CSV output is unchanged. That `readmatrix` cannot read an infinity at all, and so cannot read
MATLAB's own CSV containing one, is a defect in its own right and is listed below rather than worked
around quietly.

`num2str` of a negative zero was the other half of that chip. MATLAB drops the sign there and keeps
it in `sprintf`, so the builtin normalises rather than the formatter — and it normalises the value
rather than the text, which is visible in `num2str([-0 1])` being one character narrower than a
sign-stripped string would give.

### Taking the buffer rather than copying it

The last chip is the only one here that is not a defect. Assignment copies a container, because
`b = a; b(1) = 0` must leave `a` alone — but it does not have to copy `y = sin(x) .* exp(-x)`, whose
buffer is held by nothing except the expression that made it.

The chip proposed carrying a "freshly allocated" mark on the value, or a count on the buffer. **The
audit ruled both out, and that is the useful result.** A builtin here can durably store the very
wrapper it is handed — `setappdata` puts one in a figure's application data, and a graphics object's
`UserData` takes one raw. So a mark that outlives the statement can be stored along with the value
and come back, on some later line, attached to something a name still holds; and a count cannot tell
one wrapper from one wrapper a name is bound to, which would elide `b = a` itself.

So the freshness is a `bool` on the stack, threaded from the operator's evaluation to the assignment
and to nothing else. Nothing is written on the value, which is the whole safety argument. The
elision then applies only to a binary operator, a unary minus or not, or a transpose, on the plain
assignment path, and only when the answer is packed and non-empty, both operands are ordinary
numbers rather than anything that would send the operator off to an overload or to calendar
arithmetic, and the answer is a different wrapper over different storage from either operand. A call
is never elided, which is what keeps the builtins that hand back their own argument out of it.

Two of those conditions cannot fire on the code as it stands, and were kept deliberately: a user
function cannot return a wrapper another name holds, because its parameters are bound by copy and
its outputs are assigned. They are there so that the claim stays true of roads not yet built, and
the code says so rather than leaving a later reader to discover that they carry nothing.

## Divergences recorded

- **A repeated or misplaced `printf` flag is refused rather than silently abandoning the format.**
  MATLAB, given `sprintf('a%##db', 5)`, returns `a` — it stops at the malformed specifier and drops
  the rest of the format including its literal text. Here it raises, naming the specifier as written
  and listing the legal flags, which is what this file already did for every other misuse.
- **A one-by-one complex is refused where a scalar argument is wanted.** MATLAB is not consistent
  with itself here: `round` and `circshift` refuse it too, while `linspace` warns and takes the real
  part. There is no single rule to copy, so the refusal is kept for all of them rather than picking
  one of MATLAB's two answers and calling it parity.
- **A one-by-one char is refused where a scalar argument is wanted.** A bare `'a'` never was
  accepted here, and accepting `['a']` while refusing `'a'` would only move the inconsistency rather
  than close it. Excluded by name rather than by element type, because a char matrix stores its code
  points as plain numbers.
- **An integer conversion of a complex is refused.** MATLAB accepts `int32(1.6 + 2.4i)` and answers
  `2 + 2i`, rounding and saturating each part — but `int32(1 + 2i) + int32(1 + 1i)` then raises
  "Complex integer arithmetic is not supported", so the value is unusable the moment it exists. There
  is no storage here for a complex carrying an integer class, and inventing one to hold a value
  nothing may then do arithmetic with is not worth the machinery.
- **A nought over a nought in a pencil answers `NaN`.** Carrying the sign of an infinite eigenvalue
  through ADR 0108's β-snap leaves one case that snap did not have before: a β at rounding scale
  against an α of exactly zero. MATLAB, snapping nothing, answers `0`. Reachable only for a
  genuinely singular pencil.

## Divergences retired

Six, five of which were recorded and one of which nothing had written down.

`repmat` of a char row answering separate pieces, from ADR 0105. `Inf(n)` and `NaN(n)` building
nothing, from ADR 0069 — moved into that ADR's own "closed by a later milestone" section, which
exists because the harvester reads the list above it. `eig` answering a row, from ADR 0076, where
the bullet was rewritten rather than deleted because it carried `fscanf` and `sscanf` too and those
still answer rows. The boxed lane dropping an N-D shape through elementwise arithmetic, and the
boxed lane dropping an integer class on a grow — both of which lived in XML comments on the two
cross-lane tests that documented them, now folded back into the parity form with the comments gone.
And deletion losing the numeric class, in both lanes, which no ADR and no comment had.

`stess_38`, `stess_41`, `stess_48` and `stess_64` each held an item asserting one of these, and each
now asserts the corrected behaviour. Three of the four had been failing in MATLAB by design; they
pass in both engines now.

## Measured

The full suite goes from **6,401 tests to 6,647**, all passing, the 246 new ones being the seven
agents' own.

**The boxed lane is green for the first time.** It has carried 57 failures for as long as this
project has recorded it, and a control run of the base commit with `JGRAPH_JGS_PACKED=0` reproduces
exactly that: 57 failed of 6,401. On the merged tree the same lane passes all 6,647. They were the
shape loss above, the whole `MatlabVolumeTests` family — those tests build arrays of three
dimensions and elementwise arithmetic had been folding them to two, so a fix written for a chip
about an integer class closed a family nothing had connected to it. The control was run because a
lane reporting no failures where 57 were expected is as likely to be an environment variable that
did not reach the test host as it is to be good news, and the difference is not something to guess
at. `dotnet build JGraph.sln` is clean with warnings as errors. The five coverage verifiers
pass. The stress corpus is **68 of 68**, with the runner's script count matching the file count —
which is checked rather than read off the summary, because a single warning once ended a run at 37
of 64 while printing that all 37 had passed.

The divergence ledger stands at **221 across 43 ADRs**, five of them this one's.

The only performance claim in the milestone is the elision's, and it is made with a control row in
the same run that must not move. Twenty assignments of an 80 MB array: the control drifts 9 % across
runs, while the assignment's before and after ranges do not overlap at all, 0.514–0.558 s against
0.307–0.371 s. The internal check is what makes it believable rather than merely favourable — the
saving is 9.2 ms per assignment and the control says one copy of that array costs 11.1 ms, so what
was saved is one copy, which is exactly the copy that stopped happening. One run in sixteen came
back an outlier and is reported rather than dropped.

Every behavioural question in this milestone was settled against MATLAB R2024a running headless on
this machine rather than from memory, and the volume is the point: 293 format cases for the printf
flags alone, 42 forms for the scalar argument, a 20-verb shape sweep for the linear algebra, and
every integer-storage case at both saturation ends of five classes.

The form sweep was re-run once the box was free rather than estimated, because an estimate written
into a coverage document is exactly the kind of unmeasured claim this project's machinery exists to
prevent. **Accepted forms move from 1,344 to 1,358 of 2,454**: seven names that had been undefined
now resolve, eight rows that had been errors are answers, and one more is a deliberate refusal.

The toolbox slice does not move at all, and the reason is worth writing down because it looks like a
mistake and is not. The prober records whether a call *returns*, not whether it returns the right
thing. `diag`, `eig`, `qr` and `ordeig` answering the wrong shape never cost anybody a form, so
correcting them cannot buy one back; what moved are only the calls that used to fail outright, and
those are builtins rather than toolbox names. A form count measures reach, not correctness, and this
milestone was almost entirely about correctness.

No performance figure beyond the elision's is claimed. This box ran seven agents all day and its
timings drift further than most effects worth measuring.

## Found on the way, not fixed here

Each is a chip of its own rather than a line in this milestone.

`cumsum`, `cumprod` and `diff` do not apply the integer class, so `cumsum(uint8([10 200 100]))` is
`[10 210 310]` where MATLAB saturates to `[10 210 255]`; and `sum` or `prod` over a `single` answers
a double holding a value no single can represent. Silent wrong answers, in the reduction family
rather than the storage path this milestone fixed.

A char or complex right-hand side is not converted on an indexed write, so `x(2) = 'A'` stores the
char and the next `sum` throws. Writing 5 into a logical array makes it double, where MATLAB keeps it
logical and reads it as true — a different mechanism, and one that changes what logical indexing
means downstream.

`readmatrix` cannot parse `Inf`, so JGraph cannot read a CSV that MATLAB itself wrote containing an
infinity. `mat2str(-0)` keeps the sign where MATLAB drops it, as `num2str` did. `disp(-0)` and a bare
`-0` show the sign too.

Nine of fourteen shape verbs drop the numeric class, which wants one shared retrofit rather than a
patch in each; eight of them refuse a char row that MATLAB accepts; `repmat` of a cell answers a
double array. `a = [1]; a(1) = a` stores the array inside itself, and displaying the result blows the
stack at some twenty-eight thousand recursions where MATLAB prints `1`. No scalar of any type
accepts an indexed write, `z(1) = 5` included, and the obvious promotion collides with the
one-element-array reading this milestone just settled, so it wants deciding rather than patching. A
scalar logical index is refused where a logical array index works. And `fprintf('%g ', [])` prints
nothing where MATLAB emits the format's literal text once.

## Testing

Five new test files, one per agent, named for the chip rather than the milestone so the next reader
can find the defect from the fix: `ChipComplexAndConstantTests`, `ChipLinalgShapeTests`,
`ChipPrintfFlagTests`, `ChipScalarArgumentTests`, `ChipIntegerStorageTests` and
`ChipDisplayAndCharTests`. `MatlabPackedClassM97Tests`' two divergence-documenting tests were folded
back into the cross-lane parity form.

**One regression escaped the parallel work and was caught only by the merge.** Each agent's suite was
green in its own worktree; the union was not. The complex-scalar agent had pinned JGS printing
`Infinity`, deliberately, as a way of showing that JGS keeps its own constants — and the display
agent, which checked correctly that no *existing* test asserted the old spelling, then changed that
word everywhere. Neither could see the other, because both branched from the same commit before
either test existed. It was resolved in favour of the display change: how an infinity is spelled on
the way out is a display question and not a dialect one, and the test's actual subject — that JGS's
`inf` is a value and not the constructor the MATLAB dialect gained — is untouched.

The lesson is worth more than the fix. Partitioning parallel work by *file* prevents conflicts in the
tree and does nothing about conflicts in behaviour. A shared assertion about a shared observable is
a shared file in every sense that matters.

Two tests, `JgsDebugSessionTests.Pause_InterruptsATightLoop` and
`JgsFigureWindowTests.ReRun_ResetsNumbering`, failed intermittently across three agents' runs and
were nearly written off three separate times as load. They are not load alone. `Pause_InterruptsATightLoop`
is a ten-second timeout over a spinning loop, and when it times out it throws before cancelling, so
it leaks a running thread that takes the figure-numbering test down about four seconds later. A busy
box makes the timeout fire; so does anything that shifts what runs before it. Both pass on a quiet
box, and both pass in the final run. Hardening them is its own chip.

## Live checks

`stess_38` item 18, `stess_41` item 14, `stess_48` item 24 and `stess_64` item 23 each asserted a
divergence this milestone retired, and each was rewritten to assert the corrected behaviour. All
sixty-eight stress scripts pass. `stess_38` and `stess_48` had additionally been failing in real
MATLAB before this — they were stale rather than divergent — and item 23 of `stess_64` and item 14
of `stess_41` were marked as failing in MATLAB by design and now pass in both engines.
