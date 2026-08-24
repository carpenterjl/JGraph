# 0082 — Time, told exactly

Date: 2026-08-24 · Milestone: M82 · Status: accepted

## Context

ADR 0064 gave this build `datetime` and `duration` and recorded three limits, each honest when it was
written:

- **Time zones are not carried.** The tag had a slot for one and `datetime(…, 'TimeZone', …)` put a
  name in it, and nothing ever read it.
- **Sub-millisecond precision is not kept.**
- **No `calendarDuration`.** A month is not a fixed length, so it cannot be a count of milliseconds.

The first was the one worth closing first, and not because of what it made impossible. It made
something *wrong*: two moments given different zones compared, sorted and subtracted as though both
were wall-clock readings, with no error and no sign — and ADR 0064's own §8 names that failure as the
worst available. A verb that refuses is a verb a script can work around; a verb that answers with a
plausible wrong number is one it cannot.

Re-reading the other two, as this session's first milestone had just been rewarded for doing:

- **Sub-millisecond was a reporting loss, not a storage one.** `JgsTime.ToDateTime` rounds to whole
  *ticks*, and `FromDateTime` divides ticks by ten thousand as a double — both keep the fraction. It
  was thrown away afterwards, in four places: the field accessors asking a `DateTime` for its
  whole-number `Millisecond`, two hard-coded format strings (`"00.###"` and `"0.####"`), and
  `dateshift` subtracting a literal `1`.
- **`calendarDuration`'s reason was right and its conclusion was not.** ADR 0064's own rule says what
  to do: *a type here is a meaning attached to storage that already knows how to be an array.* A
  calendar duration needs three numbers per element, and M65 made struct arrays real — storage that
  holds several numbers per element and already indexes, grows, reshapes, masks and concatenates.

## Decisions taken before any code

1. **A zoned datetime stores the instant in UTC; the zone is a lens applied on the way out.** This is
   the only representation in which subtraction, comparison and sorting are right without every
   operator learning about zones — the same one-choke-point move as M63's string demotion and M64's
   `PrepStrip`. An unzoned datetime stores wall clock exactly as before, so nothing that never
   mentions a zone changes at all.

2. **One lens, in front of every calendar reader.** `JgsTime.WallClock(ms, tag)` is what `year`,
   `month`, `second`, `ymd`, `hms`, `datevec`, `yyyymmdd`, `timeofday`, `dateshift` and the display
   all read through. For an unzoned value it is the identity, which is what makes it safe to put in
   front of all of them.

3. **The epoch conversions stay on the storage.** `posixtime`, `juliandate` and `exceltime` are
   questions about an instant, not about a calendar, and a zoned datetime's storage already *is* the
   instant. Recorded below, because the split between the two groups is a decision.

4. **An unknown zone is refused by name.** Until M82 any string was accepted and ignored. Accepting
   `'Not/AZone'` and then doing wall-clock arithmetic is the same failure the whole wave exists to
   end, one level up.

5. **Setting `TimeZone` attaches or converts, and MATLAB means both.** On an unzoned datetime it
   attaches — the stored wall clock is read as a reading in that zone. On a zoned one it converts —
   the instant is kept and the lens changes. Setting it to `''` strips, keeping the reading.

6. **`calendarDuration` is a struct array wearing a time tag.** Fields `months`, `days`, `millis`;
   `JgsTimeKind` gains a third member. `isstruct` answers false, which is the tagged-value rule M68
   wrote for `MException` and `containers.Map` applied to a fourth type — one line in
   `IsStructValue`, not a second predicate.

7. **The components are applied in order and never collapse.** Months, then days, then time. Adding a
   month to the 31st of January is the 29th of February; adding thirty-one days is the 3rd of March.
   That difference is the entire reason the type exists.

8. **Two calendar durations have no order.** Is a month longer than thirty days? Only once you say
   which month. Refused by name rather than answered from whichever component the struct storage
   happened to compare first.

9. **`caldays` and `calweeks` become calendar units.** They were plain durations, on ADR 0064's
   argument that an unzoned datetime has no daylight saving to shorten a day. Wave A made that
   argument expire in the same milestone that could act on it.

## Two things found by running it rather than by reading it

**`dateshift(t, 'end', unit)` answered with the *first* of the next month.** Replacing the literal
`- 1` millisecond with one tick looked like the whole fix and was not: a double's spacing at a
datetime's magnitude is about nine tenths of a microsecond, so subtracting a tenth of one changes
nothing whatsoever. The end of a unit is now `Math.BitDecrement` of the boundary — the largest value
the storage can hold that is still inside it, at whatever resolution that magnitude has. A fixed step
is wrong in one direction or the other at every scale; the storage's own next value down is right at
all of them.

**Concatenation dropped the tag, and so did binding a name.** `[t1 t2]` came back as two plain
millisecond counts with `class` answering `double`, and `y = x` on a calendar duration handed back a
bare struct array. Both are the failure M64 recorded as "a tag a builtin does not know about is lost"
and M64 itself fixed for transpose — found here because `caldiff` and `between` take a datetime
*array* and `[t1 t2]` is how a script writes one, and because a struct's copy carried a class name
and nothing else, which was true for exactly as long as a class name was the only tag a struct had.
Neither was caused by this milestone. Both had been silently wrong since M64 and M65.

## Verification

- 0 warnings in Release and Debug; **5,208 tests** (5,192 + 16); **54 of 54 stress scripts**, including
  the new `stess_54.m`.
- **`stess_36.m` was run in full before a line of it was touched**, and failed in exactly one section
  — §8, the `calmonths` refusal — with §3 (`seconds(1)+seconds(2)==seconds(3)`,
  `milliseconds(1500)==seconds(1.5)`), §11 (the exact display text), §12 (the `1e-9` and `1e-3`
  round-trip tolerances) and §15 (`dateshift 'end'`) all passing unchanged. That is the measurement
  the whole wave was steered by: sub-millisecond and zones had to be invisible to a script that never
  mentions either.
- The frozen amendment to `stess_36.m:82` is **authorized by the user**, the third such exception
  after stess_26 §17 in M74 and stess_38 §20 in M76. The line became a positive assertion and §8's
  other seven refusals are untouched — plus one new refusal, that two calendar lengths have no order,
  because the section exists to pin what has no meaning and that is now what has none.
- `stess_41.m` §11 was rewritten (editable band): the divergence it pinned is gone, and what it pins
  now is what is still refused.

## Divergences recorded

- **The epoch conversions read the storage, not the wall clock.** `posixtime`, `juliandate` and
  `exceltime` of a zoned datetime answer about the instant; `datenum`, `datevec`, `yyyymmdd` and the
  field accessors answer about the calendar reading. Both groups are right about the question they
  are asked, and a script mixing them needs to know which is which.
- **Precision stops at about a tenth of a microsecond for an absolute datetime**, which is a double's
  spacing at this epoch, and at whatever the magnitude allows for a duration — `seconds(1.000001)`
  round-trips exactly. MATLAB keeps nanoseconds on a datetime by storing more than one number;
  ADR 0064's milliseconds-in-a-double is the choice that buys every array operation for free, and this
  is its price. `datenum` and the OLE conversions stay millisecond-ish because `ToOADate` is.
- **Two calendar durations cannot be compared**, and the refusal names the comparison that does have
  an answer: add each to a datetime.
- **`timezones` answers a cell of names**, where MATLAB's answers a table with the offsets in it. The
  offset a zone has is a question about a *moment*, so a column of them would have to pick one moment
  and would not say which.
- **The zone names are this machine's.** On Windows without ICU that is `'W. Europe Standard Time'`
  rather than `'Europe/Berlin'`; .NET 8 resolves an IANA id when the platform can, and refuses by name
  when it cannot. `'UTC'`, `'local'` and a fixed offset like `'+05:30'` always resolve.
- **A calendar duration's display is MATLAB's composition** — `1y 2mo 3d 05:00:00` — and its storage
  is three numbers, so `calquarters(2)` shows as `6mo` rather than remembering it was asked in
  quarters.

## What is not done

- **A timetable's row times are still numbers**, ADR 0064's divergence, untouched: a `Table` column
  holds doubles.
- **`dateshift(…, 'dayofweek', …)` still refuses**, naming the two-step spelling that works.
- **`datetime` cannot be saved to a MAT file**, which `stess_37.m` §19 pins and which is about the v5
  writer rather than about time.
- **A zoned datetime plots in its own zone by reading its wall clock**, because doubles travel through
  the graphics bridge and only the axis remembers the type. A zone is not pushed onto the ruler, so
  two series in different zones on one axes are drawn against their own clocks rather than against a
  common instant. Recorded here rather than fixed, because the right fix is a zone on `AxisModel` and
  that belongs with a wave that has a reason to draw one.
