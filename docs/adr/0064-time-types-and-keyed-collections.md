# ADR 0064 — Time types and keyed collections

## Status

Accepted (M64, 2026-08-15). The fourth milestone of the M61–M68 language arc, and the second to
introduce a type. Where M63 gave an existing representation a name, this one had to build the
representation as well — and the answer came out the same, for a different reason.

## Context

`datetime` took no arguments and answered with a char row of the current moment. `duration` did not
exist; `seconds(90)` answered `90`, a stand-in M43 recorded honestly at the time. There was no
`containers.Map` and no `dictionary`, so a script had nowhere to keep things by name.

A probe of the whole surface before writing anything found forty-odd absent names — and one defect
that was not a gap at all. `x = now` bound the *function*, not the time, because `now` and `clock`
were never given M37's `AutoCallsBare`. So `datestr(now)`, which is close to the commonest date line
anyone writes, failed complaining that it had been handed a function.

## Decision

### A time is a tagged numeric array, and there is no new `JgsType`

The plan called for a `JgsType.Time`. There is none, and the reasoning is *not* M63's.

M63's sweep found the storage already built and only the identity missing, so a new enum member would
have had to reimplement machinery that existed. Here nothing existed: the storage had to be built
either way. The question was therefore what to build it *as* — and building it as a numeric array
wearing a `JgsTimeTag` buys shape, indexing, growth, reshaping, `end`, logical masks, concatenation,
N-D folding and every reduction kernel a second time, for the price of one nullable field.

The general rule the two milestones share is worth stating plainly, because M65 and M68 will face it
again: **a type here is a meaning attached to storage that already knows how to be an array.** A new
`JgsType` earns itself only when the storage cannot be an array — which is why `Image` and `Sparse`
are members and `string`, `datetime` and `duration` are not.

### Storage is milliseconds, and the epoch is 1899-12-30

Two consequences pay for the choice, and the second is why it is not the plan's bare "milliseconds".

Whole milliseconds are exact in a double out to roughly 285,000 years, so `seconds(1) + seconds(2) ==
seconds(3)` holds. Stored in days it does not: the three quantities land three sixteenths of a
millisecond apart and the comparison a script actually writes comes back false. That alone settles
milliseconds over days.

The epoch then settles itself. 1899-12-30 is the one `DateTimeAxis` has used since M6, so a datetime
reaches the graphics pipeline by a single divide and reaches `datenum` by a divide and an addition.
Choosing any other epoch would have meant a conversion constant in two places that could disagree.

### One choke point, above every numeric reading of the operands

`IsTimeArithmetic` / `TimeBinary` settle what kind of thing the answer is before the numbers are
touched. The table is the whole of time arithmetic — datetime minus datetime is a duration, datetime
plus duration is a datetime, duration over duration is a plain number — and everything not in it is
refused **by name**.

The refusals are the point. Every one of them would otherwise answer with a plausible number, and a
date in the year 4048 looks exactly like a date.

The check sits above the *matrix* operators, not merely above implicit expansion. M63 put string
concatenation above expansion and that was enough; here it was not, because a scalar duration is a
1-by-1 array and `duration * duration` reached matrix multiplication and answered `2` instead of
being refused. **The rule is that a type's own arithmetic resolves before every numeric reading of
its operands, and "every" has to be checked branch by branch.**

### The reductions are taught as one pass, not twenty edits

`TimeAwareReductions` wraps the finished environment: strip the tag, call what was already there, put
the right tag back. Three lists say which rule each name follows — keeps the kind, always answers a
duration, or is refused for datetimes. Only the *first* output is stamped: the second is a position
in the input, and tagging that would claim the index was a date.

### The keyed collections share one representation, and their difference is a named rule

Both are a struct carrying a class name and four fields. The only thing that separates them is what
happens on assignment, and that is expressed once — a list of class names `CopyForBinding` leaves
alone. MATLAB calls these handle classes, and the rule is exactly the one M68 needs for
`classdef Name < handle`, which is why it is a rule rather than a check for one class.

Lookup is a scan of the key cell rather than a hash. A script's map is small; keeping the collection
inside the value model means copying, displaying and saving already work, and a collection big enough
for the difference to show is a table.

## Consequences

Tests move from 4,422 to **4,447**, all green, 0 build warnings, and all **36** stress scripts pass.

### Three defects the type surfaced rather than caused

- **`x = now` bound the function.** Found by the opening probe, before a line was written. `now`,
  `clock`, `date` and `time` never had `AutoCallsBare`, so the value only appeared when the name was
  called with parentheses — and `datestr(now)` failed.
- **The transpose dropped every wrapper tag.** Found because `timetable(seconds(1:3)', …)` stored raw
  milliseconds. It was never a time bug: `class(uint8([1 2])')` answered `'double'` and
  `["a" "b"]'` stopped being a string array, both since those types landed. The three tags now travel
  together through one `CarryValueTags` helper, which is what stops the next path from carrying two of
  them.
- **`isnumeric` had drifted from `class`.** It asked whether the elements were numbers, which a
  datetime's milliseconds are. Pointing it at the shared helper is the same repair `islogical` and
  `class` were given earlier.

That is the M63 pattern for the third time, and it is now the arc's most reliable finding: **a type
that makes a distinction explicit finds the places that were guessing at it.**

### A dotted name now calls itself on mention

`m = containers.Map;` bound the constructor rather than a collection, and because the constructor
self-calls, every later mention of `m` built a fresh empty one — so `m('x') = 10` wrote into a
collection nobody kept and the write vanished without a word. `EvaluateMember` now applies the same
auto-call a bare name gets, and `EvaluateCallee` deliberately does not, so `containers.Map(k, v)`
still hands its arguments to the constructor. **A silent failure is worse than a loud one, and this
one was silent in both directions: nothing errored and nothing was stored.**

### The JGS surface is untouched, and the gate is on two names rather than the milestone

Every name here is new but two, so for JGS the whole of it is a pure addition — which its freeze
allows. The two exceptions are the names whose *meaning* moved: `seconds` answered with its own
argument and `datetime` with a char row of the current moment. Those two are re-declared for JGS
exactly as they were. Gating the milestone instead would have withheld forty new names from JGS for
the sake of two.

### Recorded divergences

- **No `calendarDuration`.** `calmonths`, `calyears`, `calquarters` and `calendarDuration` refuse by
  name and say why: a month is not a fixed length, so it cannot be a count of milliseconds.
  `caldays` and `calweeks` *are* implemented, because an unzoned datetime has no daylight saving to
  shorten a day. `between` refuses for the same reason.
- **Time zones are not carried.** The tag has a slot for one and `datetime(…, 'TimeZone', …)` records
  it, but no arithmetic consults it. An unzoned datetime is what a measurement log holds.
- **A timetable's row times are numbers.** A `Table` column holds doubles, so a duration row-time
  column is stored as its count of seconds and a datetime one as its serial date number — the two
  readings those row times had before the types existed.
- **`dateshift(…, 'dayofweek', …)` refuses**, naming the two-step spelling that works.
- **A tag a builtin does not know about is lost**, as in M63. The failure is visible — `class` says
  `double` — rather than silent.
- **Sub-millisecond precision is not kept.** A round trip through a serial date number is exact to
  about a hundredth of a millisecond, which is the precision a double has left at that magnitude.

### The coverage table does not move

`datetime`, `duration`, `containers.Map` and the forty-odd names around them are documented by MATLAB
as *functions* and classes, not as kind **builtin**, so the 514-name table is untouched. The
across-every-kind total moves, and that is the number this milestone is measured by.

## Live checks for the user

Batch cannot see these:

- A datetime and a `containers.Map` in the Workspace pane and the Data Viewer — both are ordinary
  values underneath, so the worst case is that they read as an array and a struct.
- The tick labels on a figure drawn with `plot(t, y)` where `t` is a datetime: batch confirms the
  ruler is a date ruler, but only the window shows what it writes along it.
- Completion and signature help on `containers.Map` and on a line holding a `datetime` variable, now
  that a dotted name can call itself.
