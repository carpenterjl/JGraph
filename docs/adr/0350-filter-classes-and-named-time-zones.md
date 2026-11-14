# ADR 0350 — filter keeps R2025b's classes and refusals, and a named time zone is matched, stored and abbreviated as R2025b does it

## Status

Accepted, 2026-11-13. Open items 305 and 311, the leftovers of ADR 0345 (filter's conditions) and
ADR 0344 (fixed-offset zones).

## Context

- **Item 305.** After ADR 0345, `filter` refused a complex `b`, `a`, `x` or `zi`, and worked a single
  call in double and rounded once. Both were divergence rows of `filter_conditions`. It also filtered
  with the elements of a matrix `b`, read a sparse argument, and worded a char signal, an empty `b`
  or `a`, and `a(1) = 0` in its own words. Probes `probe305` to `probe305f` (agent T's scratch) put
  about 130 calls through R2025b:
  - **Complex.** Any complex argument is filtered in complex arithmetic, `zf` in ADR 0345's layout.
    Each output comes back real when every imaginary part is zero, the other output not minding: a
    `complex(x)` with real coefficients answers two real outputs, and `filter([1 2], 1, [1 2], 1i)`
    answers a complex `y` and a real `zf`.
  - **Single.** A single anywhere among `b`, `a`, `x` and `zi` makes the call single. The dimension
    does not count: `filter(b, a, x, [], single(2))` is double. R2025b works it in single arithmetic,
    the transposed direct form II with `b` and `a` divided by `a(1)` first. A MATLAB-language model of
    that recurrence in single reproduces R2025b's `y` to the bit on six random filters of orders 1 to
    6, 200 samples each, with `zi`. Rounding a double computation differs from it in 8 of 20 samples.
  - **Integers and logicals** are read as their values and answer double, a mix of int8 and int16
    included. With a single they answer single.
  - **The refusals and their order.** The arguments are read one at a time, the dimension too. Each
    is checked for its kind (`MATLAB:filter:inputType`, "Invalid data type. Input arrays must be
    numeric or logical.") and for sparseness (`MATLAB:filter:sparseInput`, "Sparse inputs are not
    supported."). The first one that fails either check decides. Then `b` and `a` must be vectors
    (`MATLAB:filter:notVectorInput`, "First two arguments must be vectors."). A 1-by-0 counts as a
    vector and `[]` does not. Then the dimension is checked, then `a(1)`
    (`MATLAB:filter:firstElementOfDenominatorFilterZero`, "First denominator filter coefficient must
    be non-zero.", or `...NotFinite`, "...must be finite." for a NaN or an Inf), then `zi`.
  - **Empty coefficients.** A 1-by-0 or 0-by-1 `b` or `a` answers a `y` of zeros. With `b` empty
    `zf` is zeros too, `zi` or no `zi`. With `a` empty `zf` is the tail sum of `b` on the signal's
    last samples, undivided: `filter([1 2 3], zeros(1, 0), [1 4 2 8])` leaves `[22; 24]`.
- **Item 311.** After ADR 0344, a named zone's `z` printed the zone's name. Named zones were looked
  up by .NET case-insensitively, and Windows ids were accepted. A name was kept as written, and
  `'local'` was stored as `local`. `string(t, fmt)` was refused. R2025b was probed on about 60
  names (`probe311`) and then on every name `datetime.allTimeZones()` lists: 637, tz 2024b
  (`tools/timezones/record_zone_names.m`). Each name went through the constructor in January and in
  July 2026. The probe recorded what each name is stored as, its warning, and its `z` and `isdst`.
  - **Matching.** A name matches only with its case: `america/new_york` and `etc/gmt-2` are refused.
    So is a Windows id (`Eastern Standard Time`), and so are `Factory` and the ten `SystemV/` names.
  - **The stored name** is ICU's canonical name, which is CLDR's (the `CanonicalName` column of
    `datetime.allTimeZones`). `Asia/Kolkata` is stored as `Asia/Calcutta` and `Europe/Kyiv` as
    `Europe/Kiev`, with no word said. The same goes for `EST5EDT` (to `America/New_York`) and
    `America/Argentina/Buenos_Aires`. An old-style alias is stored canonically with
    `MATLAB:datetime:NonstandardTimeZone`: "'US/Eastern' does not specify a single time zone
    unambiguously. It will be treated as 'America/New_York'. See the datetime.TimeZone property for
    details about specifying time zones." (the property is a link). That covers the three-letter
    Java ids, the `US/`, `Canada/`, `Brazil/`, `Mexico/` and `Chile/` names, and a few country names:
    `EST` becomes `America/Panama`, `CET` and `MET` become `Europe/Brussels`, `GB` becomes
    `Europe/London`. Of the 637 names, 579 are named zones, 219 of them stored under another name and
    92 warned about.
  - **The `z` letter** (one to three) is the 'en' locale's short name for the zone at that instant
    where it has one, and the offset otherwise. Only the North American metazones and GMT have one:
    EST/EDT, CST/CDT, MST/MDT, PST/PDT, AKST/AKDT, HST, HAST/HADT, AST/ADT and GMT, over 117 zones.
    London is `GMT` in winter and `UTC+1` in summer. Everywhere else the letter is the offset:
    `UTC+5:30`, `UTC-3:30`, `UTC+13:45`, and `UTC` alone at zero (Lisbon in winter). A fixed offset's
    `z` at zero is still `GMT` (ADR 0344).
  - **`'local'`**, in any case, stores the system's zone by name: `America/Denver` on the recording
    machine, through the constructor and the setter alike.
  - **`string(t, fmt)`, `string(t, fmt, locale)` and `char(t, fmt, locale)`** format as the `Format`
    setter would and refuse its letters (`O`, `zzzz`). `''` and `[]` keep the value's format. A
    number is `MATLAB:datetime:InvalidFormat`.

## Decision

### filter's refusals, in order (`JgsBuiltins.FilterClasses.cs`)

`FilterAnswer` now checks the arguments' kinds (`FilterArgumentKinds`), then the coefficient shapes
(`FilterCoefficientShapes`), then the dimension, then `a(1)` (`FilterLeadingCoefficient`), then `zi`.
`FilterInitialConditions` was split: `FilterInitialLayout` reads which of ADR 0345's three forms `zi`
takes, and the caller reads the values. The double kernels still answer a real double call, untouched.

### A wide road for complex, single and empty calls

`FilterNeedsWideRoad` sends a call with a complex or single argument among the first four, or an
empty coefficient vector, to `FilterWide`. That road reads every argument as a real plane and an
optional imaginary one (`FilterPlanes`), cuts the signal into slices as the boxed road does, and walks
each slice through `FilterLine`. `FilterLine` divides `b` and `a` by `a(1)` (a plane at a time when
`a(1)` is real, `Complex` division when it is not). It then runs `FilterLineIn<T>`, the double
kernels' recurrence in `T`'s own arithmetic: `float` for a single call, `double` otherwise, with the
complex products written out. `FilterOutput` makes each output real when its imaginary plane is all
zeros and stamps it single when the call was. An empty `a` takes `FilterTail` for `zf`.

### filter decides its own class

`filter` is no longer wrapped by `CarryNumericClass`. The wrapper took its class from the first
classed argument, the dimension included, and so made `filter(b, a, x, [], single(2))` single.

### Named zones from R2025b's own answers (`JgsTimeZoneNames`)

`tools/timezones/record_zone_names.m` records R2025b's answer for every listed name, and
`tools/timezones/gen_zone_names.py` writes it into `JgsTimeZoneNames.cs` as two tables:
- name, stored name and warning flag, for the 579 named zones;
- the standard and daylight abbreviation of the 117 zones that have one.

The tables carry R2025b's own provenance (tz 2024b, MATLAB 25.2) in their header. .NET carries no
zone abbreviations, and Windows' ICU is a different version from R2025b's (ICU 74,
`bin/icutzdata`), so reading either at run time would answer differently on different machines.
The table is the measurement, and it is regenerated by running the two scripts.

`ReadTimeZoneArgument` looks a name up with its case (`TryLookup`), warns where R2025b warns, and
stores R2025b's name. A name the table lacks, or one this machine's zone data cannot resolve, is
refused. `'local'` stores `JgsTimeZoneNames.LocalName()`: the system zone's IANA name (on Windows
through .NET's CLDR mapping), read through the table. `JgsTime.TryResolveZone`, which loads saved
values and resolves stored tags, still resolves whatever .NET resolves.

### The z letter

`JgsTime.WithZoneTokens` asks `NamedZoneLetters` for a named zone. It answers the table's
abbreviation for whether the instant is in daylight saving, by .NET's zone data. Otherwise it answers
the offset as `ShortGmt` writes it, or `UTC` at zero.

### string and char with a format

`string` hands a datetime or duration with more than one argument to `FormattedTimeStrings`. That
and `char` share `TimeFormatArguments`, which reads the format and the locale and refuses the zone
letters as the setter does.

## Consequences

- **New parity fixture** `oi_filter_classes` (258 rows from R2025b 25.2, all agreeing, both lanes):
  - 15 complex calls, with class, size, realness and a digest of each plane;
  - 13 single calls with every value to the bit (the first final delay to two ulps, below);
  - 11 integer and logical calls and 9 empty-coefficient calls;
  - 24 refusals and 14 orderings of refusals.
- **New parity fixture** `oi_timezone_named` (151 rows, all agreeing, both lanes):
  - 36 zones across both hemispheres, each with its stored name, `z` in January and July, UTC wall
    clock and `isdst`;
  - two daylight-saving turns and London's;
  - ten refused names;
  - 20 links through the constructor and both setters, the warning's text, and four `'local'` rows
    that print only what holds on any machine;
  - 19 `string`/`char` format rows.
- **`filter_conditions`**: its two div rows are agreeing lines (re-recorded). ADR 0345's two
  divergences are closed.
- **New test classes** `FilterClassesTests` and `TimeZoneNamedTests`.
- **Scripts that used to work may now be refused or answer differently:**
  - a script that wrote a zone in the wrong case, or as a Windows id, is refused;
  - one that read back `t.TimeZone` after `Asia/Kolkata`, `US/Eastern` or `'local'` gets R2025b's
    name;
  - a matrix `b` and a sparse argument are refused;
  - a single filter's last bits change;
  - `filter(b, a, int8(x))` stays double.

## Divergences

- **A single filter's first final delay can differ from R2025b's in its last bit.** R2025b's `zf(1)`
  is not always its own running delay: its `y`, and every other delay, agree with the single
  recurrence to the bit, but its reported `zf(1)` differs from the delay that recurrence carries in
  roughly half the cases probed, by one ulp. No order of the same operations (probes `probe305e` and
  `probe305f`: fused, double-held, tail sums ascending and descending) reproduces it in every case.
  Here `zf(1)` is the running delay. The fixture holds that row to two ulps of a single, and the rest
  to the bit.
- **Four zones R2025b names are refused here**: `America/Ciudad_Juarez`, `Antarctica/Troll`,
  `Antarctica/Vostok` and `Asia/Urumqi` (with its alias `Asia/Kashgar`). This machine's Windows zone
  data does not have them. `TimeZoneNamedTests` lets a newer Windows resolve them.
- **Seven zones' offsets differ in 2026**: `Africa/Casablanca`, `Africa/El_Aaiun`, `America/Asuncion`,
  `America/Scoresbysund`, `Antarctica/Casey`, `Asia/Almaty` and `Asia/Qostanay` (Almaty and Qostanay
  are +6 here and +5 in tz 2024b). The offsets come from the Windows zone data and not from tz
  2024b. The fixture leaves these zones out.

## Still open

- **`cellstr(t, fmt)`** formats in R2025b and is refused here (`MATLAB:maxrhs`), as `string(t, fmt)`
  was.
- **A locale's abbreviated month** is `janv.` here and `janv` in R2025b (`string(t, [], 'fr_FR')`):
  .NET's culture data, not ICU's. This is older than this ADR (`char` had it too).
- **The abbreviation table is the present one.** A historical instant gets today's metazone (an
  Indiana zone's past Central years print EST), where R2025b's ICU follows the metazone's history.
- **R2025b warns once about a non-standard system zone** the first time `'local'` is used. That is
  not modelled here.
- Seen while probing, and not this item's: `num2str(z, 17)` of a complex row prints the shortest
  form of each part here (`0.7`), where R2025b prints 17 digits (`0.69999999999999996`).
