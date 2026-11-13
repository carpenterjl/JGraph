# ADR 0344 — A fixed-offset time zone is read and named as R2025b reads and names it

## Status

Accepted, 2026-11-12. Open item 209.

## Context

A datetime's `TimeZone` was kept as written. `+0200`, `UTC+2` and `+020030` were refused, `+02:00:00`
was accepted and named `+02:00:00`, offsets stopped at 14 hours, a space before or after was trimmed
away, and a refusal was in JGraph's own words with no identifier. The zone letters of a format were
half there: `Z` printed `+02:00` (R2025b prints `+0200`), `z` printed the zone's name, and `x`, `X`
and `O` were printed as written.

R2025b's `verifyTimeZone` (`@datetime/private`) hands the text to a built-in canonicaliser, so the
rule was measured, not read. Three probes (probe209, probe209b, probe209c, in agent J's scratch)
put about two hundred spellings through `datetime(2026, 1, 2, 3, 4, 5, 'TimeZone', z)`, through the
setter on an unzoned value and through the setter on a UTC value. The three paths always agreed. The
probes also covered the format letters x to xxxxxxx, X to XXXXXXX, Z to ZZZZZZZZ, z to zzzzz, O and
OOOO on fixed offsets, UTC and two named zones, plus `tzoffset`, durations as zones, and conversion
between a fixed offset and a named zone.

**The name.** A fixed offset is named `+HH:mm`, or `+HH:mm:ss` when its seconds are not zero. A zero
offset is `+00:00` whatever its sign, and `-00:00:30` keeps its minus. Every offset may reach
23:59:59 either way, and 24 hours is refused.

**The spellings.**
- `UTC` in any case, and `GMT` written exactly so, are `UTC`. `Z` and `z` are `+00:00`.
- **A sign alone.** Two-digit fields with colons are read (`+HH:mm`, `+HH:mm:ss`). So are one to six
  digits without colons. One or two digits are hours. Three are `Hmm` and four `HHmm`. Five are
  `Hmmss` and six `HHmmss`. So `+020` is `+00:20` and `+02000` is `+00:20` too.
- **`UTC` and a sign.** The hour is one or two digits, then optional two-digit fields with colons
  (`UTC+2`, `UTC+2:00:30`). Digits past two without colons are refused (`UTC+0200`). A zero offset is
  `+00:00`.
- **`UT` and a sign, or `GMT` and a sign** (any case). Both forms are read, `GMT+020` and `UT+0200`
  among them. A zero offset after `GMT` is `UTC`. Any other offset after `GMT` warns
  `MATLAB:datetime:NonstandardTimeZoneFixed` under its `GMT+HH:mm` name. `UT`, `gmt` and `Gmt` with
  nothing after them are `+00:00`, with no warning.
- **The tz database's fixed zones**, matched with their case:
  - `Etc/GMT-N` (N up to 14) and `Etc/GMT+N` (N up to 12) are the offset of the opposite sign.
  - `Etc/GMT`, `Etc/GMT0`, `Etc/GMT±0`, `Etc/Greenwich`, `Etc/UTC`, `Etc/UCT`, `Etc/Universal` and
    `Etc/Zulu` are `+00:00`, with no warning.
  - `UCT`, `Zulu`, `Universal`, `Greenwich`, `GMT0`, `GMT+0` and `GMT-0` are `+00:00`, with the
    warning under the name as written.
- **Refused.** Any other spelling that starts like an offset is refused as
  `MATLAB:datetime:UnknownTimeZone`: "Unable to create the specified time zone: '+2:00'. See the
  datetime.TimeZone property for details about specifying time zones." (the property is a link).
  That covers a space before or after, `+2:00`, `+02:0`, `+02:00:0`, `+02:00:00.5`, `02:00`,
  `+24:00`, `+02:60`, `++2` and seven digits.
- **A duration** is a zone too. It is written `+hh:mm:ss` and read as above, so `hours(-5.5)` is
  `-05:30` and `hours(24)` is refused as `'+24:00:00'`. A fraction of a second is refused first
  (`MATLAB:datetime:InvalidDurationTZPrecision`). Anything that is neither text nor a duration is
  `MATLAB:datetime:InvalidTimeZone`.

**The letters** follow ICU, each run of one letter one field:
- **x.** `x` is `+02` or `+0530`. `xx` is `+0200` and `xxx` is `+02:00`. `xxxx` and `xxxxx` are those
  two with seconds when there are some. Six or more print nothing.
- **X.** The X forms are the same but print `Z` for a zero offset. For the forms that drop seconds,
  zero means zero once the seconds are dropped.
- **The sign.** A minus is printed only when a printed field is not zero, so `x` of `-00:00:30` is
  `+00`.
- **Z.** `Z` to `ZZZ` are `xxxx`. `ZZZZ` (and six or more) is `UTC+02:00`, or `UTC` alone at zero.
  `ZZZZZ` is `XXXXX`.
- **z.** On a fixed offset `z` is `UTC+2`, `UTC-5:30` or `UTC+2:00:30`, and `GMT` at zero. On the UTC
  zone it is `UTC`.
- **Unzoned.** An unzoned value prints a star for each letter.
- **Refused letters.** `O` in any count is refused when the format is set
  (`MATLAB:datetime:UnsupportedSymbol`), and so is four or more `z`
  (`MATLAB:datetime:UnsupportedIdentifier`, naming `zzzz`).

**tzoffset** is an `hh:mm` duration, so `+02:00:30` shows `02:00`. Its second output is the
daylight-saving part (`datetime.m`).

## Decision

### One reader, in its own file

`JgsTimeZones.Read` (new, `JgsTimeZones.cs`) applies the rule above to the untrimmed text. It answers
one of three things:
- `Fixed`, with R2025b's name, the offset, and the name a warning quotes;
- `Refused`;
- `NotFixed`, a name left to the zone database.

`JgsBuiltins.ReadTimeZoneArgument` (new, `JgsBuiltins.TimeZones.cs`) is what the constructor's
`'TimeZone'` option and the `TimeZone` setter both call. It takes text or a duration. It raises the
warning through the run's host (`JgsLifetime.Current`, then the callback dispatcher) and refuses with
R2025b's identifier and message. The tag stores R2025b's name, so `+0200` and `+02:00` are one zone:
they compare equal and concatenate.

### Offsets to the second and past fourteen hours

.NET builds a custom zone only in whole minutes within ±14 hours. `JgsTime.FixedZone` builds the
`TimeZoneInfo` with the offset cut to that and keeps the true offset beside it in a weak table.
`OffsetAt`, `ToUtc` and `FromWallClock` read through `UtcOffset`, which prefers the table, so every
calendar reader, `tzoffset` and the conversions see the true offset. `TryResolveZone` no longer
trims, and its cache is ordinal (`GMT` is UTC, `gmt` is `+00:00`).

### The letters

`JgsTime.WithZoneTokens` now walks the format by runs, skipping quoted text, and writes x, X, Z and z
as above. `JgsTimeZones.Iso`, `LongGmt` and `ShortGmt` do the arithmetic. A named zone's `z` stays its
name. `RefuseUnsupportedZoneLetters` refuses `O` and `zzzz` where a format is set: the constructor's
`'Format'` and the `Format` setter.

### tzoffset

`tzoffset` answers `hh:mm`, and gives a second output (the daylight-saving part, `hh:mm`, NaN when
unzoned).

### The Python crossing keeps its name

K13's datetime from Python crosses with `+HH:mm:ss` (ADR 0269), which R2025b's runtime keeps as
written. `NativeWire.ReadDatetime` puts that name back on after the constructor has named the zone,
so the K13 client's answers are unchanged. The reader still resolves `+02:00:00`.

## Consequences

- **New parity fixture** `oi_timezone_fixed_offsets` (247 rows from R2025b 25.2, none diverging,
  both representations):
  - 101 spellings through the constructor;
  - 16 through the setter on an unzoned and on a UTC value;
  - durations, a string and a cell or number as the zone;
  - the x, X and Z letters on eleven zones and z on fourteen;
  - the refused letters, quoted letters and the unzoned stars;
  - `tzoffset`'s two outputs and conversions both ways between fixed and named zones.
- **New test class** `TimeZoneFixedOffsetTests`, for the reader's names, refusals, warnings and ISO
  forms, and the script-level paths.
- **`MatlabM82TimeTests` updated.** A `Z` letter now prints `-0500`, not `-05:00`. An unknown zone is
  refused in R2025b's words.
- **Scripts that used to work may now be refused or print differently.** A script that wrote a zone
  with a space around it, or as `+2:00`, is now refused. One that read `t.TimeZone` back after
  `+02:00:00` gets `+02:00`. One that printed `Z` gets `+0200`.
- **The K13 client run** (`PythonPackageBuildTests`) still agrees case for case. **Not measured:**
  the JSON crossing fixture `k14_mps_json` was not run in this wave.

## Divergences

None new.

## Still open

- **A named zone's `z`** prints the zone's name here. R2025b prints its abbreviation (`EST`), or
  `UTC+5:30` where the locale has none (Asia/Kolkata). .NET carries no abbreviations.
- **Named zones are looked up by .NET, case-insensitively, and kept as written.** R2025b matches
  them with their case (`america/new_york` and `etc/gmt-2` are refused). It also canonicalises links
  with a `MATLAB:datetime:NonstandardTimeZone` warning: `US/Eastern` becomes `America/New_York` and
  `EST` becomes `America/Panama`. `'local'` is still stored as `local`, where R2025b names the system
  zone.
- **`string(t, fmt)` and `char(t, fmt)`** do not take a format here ("Too many input arguments"), so
  R2025b's refusal of `O` through them is not reached.
