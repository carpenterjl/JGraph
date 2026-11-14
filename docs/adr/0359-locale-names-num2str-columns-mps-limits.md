# ADR 0359 — a locale's month and weekday names are R2025b's, cellstr takes a format, num2str lays out complex and formatted columns as num2str.m does, and mps.json writes the 64-bit maxima and takes long field names

## Status

Accepted, 2026-11-14. Open items 316 and 212 (chips wave 4, agent Y).

## Context

- **Item 316, names.** `string(t, fmt, locale)`, `char(t, fmt, locale)` formatted through .NET's
  culture data, which is a different CLDR from R2025b's ICU. Probe `probe_names` put R2025b's
  `datetime.Locale` suggestions and 22 more locales through every name field. They differ:
  - French, Portuguese, Swedish, Danish, Hungarian and Polish abbreviations carry a period in .NET
    (`janv.`, `lun.`) and none in R2025b (`janv`, `lun`); German's are `März`, `Juni`, `Sept` in
    R2025b and `Mär`, `Jun`, `Sep` in .NET; Russian `мар` is `март` in .NET;
  - a Russian, Polish, Czech, Greek or Finnish wide month is the genitive in R2025b (`ноября`) and
    the nominative in .NET (`ноябрь`), whatever the rest of the format; Finnish wide weekdays too;
  - Mexican Spanish's AM/PM is `a.m.` in R2025b and `a. m.` in .NET;
  - .NET has no narrow month or weekday: `MMMMM` and `eeeee` printed the wide name, R2025b `N`, `T`.
  Probe `probe_tok` found the number fields wrong too: `M` alone printed .NET's month-day pattern
  (`November 03`, a one-letter format being a standard format to .NET), `MMMMMM` the wide name where
  R2025b zero-pads the number (`000011`), and `e`/`ee` the weekday name where R2025b prints its
  number counted from Sunday as 1 in every locale (`3`, `03` for a Tuesday). `d`, `h`, `m`, `s`, `y`
  alone hit the same .NET standard-format trap.
- **Item 316, locales.** A locale .NET cannot resolve was refused in .NET's words ("Culture is not
  supported."); R2025b says `MATLAB:datetime:InvalidLocaleString`, "Unrecognized locale: 'french'.".
  `'system'` was refused; R2025b reads it as the machine's locale.
- **Item 316, cellstr.** `cellstr(t, fmt)` and `cellstr(t, fmt, locale)` were `MATLAB:maxrhs`; R2025b
  formats as `string(t, fmt, locale)` does, NaT written `NaT`.
- **Item 316, num2str.** `num2str` of a complex array answered the console's display of it, whatever
  the precision or format: `num2str([0.7+0.1i 0.3-0.2i], 17)` was `[0.7+0.1i, 0.3-0.2i]`. R2025b's
  num2str.m (readable in toolbox/matlab/strfun) prints each part through sprintfc and lays the parts
  out in columns (probes `probe_num2str`, `probe_sprintfc`, `probe_dbg`). It also showed three real
  gaps: an integer class ignores a digit count (`num2str(int8([1 -2 3]), 17)` is `1 -2  3`); a format
  with one conversion lays each element out in a right-justified column and trims the result
  (`num2str([1; 10], '%d')` is `[' 1'; '10']`, `num2str(5, '%10.2f')` is `5.00`); and a format with
  no `%` is `MATLAB:num2str:fmtInvalid`.
- **Item 212.** Since ADR 0355 `intmax('int64')` and `intmax('uint64')` are the doubles 2^63 and
  2^64 tagged with their class, and `mps.json.encode` wrote them as those numbers, one past
  R2025b's `9223372036854775807` and `18446744073709551615` (fixture rows `i_09`, `i_10`,
  `e_f_uint64_max_*`, `y_uint64_max`, `y_uint64_over`). A field name past 63 characters was refused
  by `decode` as R2025b refuses a name that is not a variable name. Probes `probe_mps` to
  `probe_mps4`: R2025b's `namelengthmax` is 2048, its `decode` keeps a name of up to 2049
  characters whole, small notation or large, and ends its process at 2050 ("Invalid field name ...
  specified."), as it does for a name that is not a variable name.

## Decision

### A table of R2025b's names (`JgsLocaleNames`)

`tools/locales/record_locale_names.m` records R2025b's `MMM`, `MMMM`, `MMMMM`, `eee`, `eeee`, `eeeee`
and `a` texts for 66 locales (the suggestions, the languages near them, their bare language codes),
and `tools/locales/gen_locale_names.py` writes them into `JgsLocaleNames.Table.cs`. Arabic, Persian,
Thai and Hindi are left out: .NET formats their dates in another calendar, or R2025b in other
digits. The table is the measurement, as ADR 0350's zone table is; reading Windows' ICU at run
time would answer differently on different machines.

### The name fields are replaced before .NET sees the format (`JgsTime.WithNameTokens`)

After the zone letters, `FormatDatetime` replaces, quoted, `M` (the month's number), six or more
`M` (zero-padded), `e` and `ee` (the weekday from Sunday as 1), a lone `y` or `u` (the whole year,
where .NET's `y` is the year mod 100), and, from the table, `MMM`, `MMMM`,
`MMMMM`, `eee`, `eeee`, `eeeee` and `a`. Without a locale only the narrow forms are taken from the
table (the invariant culture's other names are R2025b's en_US ones), so the default display formats
build nothing new. A locale the table lacks keeps .NET's names. A format that is still one letter
after translation gets .NET's `%` prefix, so `d` is the day and not the short date.

### Locales (`TimeLocale`)

`'system'` is `CultureInfo.CurrentCulture`; a name .NET cannot resolve is R2025b's refusal.

### cellstr with a format (`CellStrFormattedTime`)

Both `cellstr` registrations hand a datetime or duration with more than one argument (MATLAB dialect)
to `TimeFormatArguments`, as `string` and `char` do, and answer a cell in the array's shape.

### num2str's columns (`JgsBuiltins.Num2StrColumns.cs`)

`Num2StrComplex` transcribes num2str.m's complex road: the digit count or format as num2str.m picks
it (the width of the largest modulus, NaN and Inf counting as 314; `%d` for whole parts below
2^53, else `%.{d}g`), the forced width `2 * (d + 6) + 2`, real parts right-justified, imaginary
parts left-justified, a blank after each column unless a format was given, and the shared blank
columns trimmed. An imaginary part is sprintfc's: each conversion without its flags and width,
signed, then `i` (`'%10.5f'` of 0.1 is `+0.10000i`); the sign is a plus unless the text has a minus,
always a plus for `u o x X`, none for `c s`. `Num2StrFormatted` lays out a real array under a format
with one conversion the same way (left-justified when the conversion is left-aligned in a width);
any other format keeps the row-at-a-time printing. An integer class's digit count is dropped.

### mps.json (`JgsMpsJson`)

An int64 at 2^63 is written `9223372036854775807` and a uint64 at 2^64 `18446744073709551615`: no
other value of the class is held there. `CheckFieldName` takes names up to 2049 characters.

## Consequences

- **New parity fixture** `oi_datetime_locale_names` (R2025b 25.2, both lanes): 13 locales' seven
  name fields, the number fields and one-letter formats, ten composite formats, 15 `cellstr` rows,
  and the locale spellings, with two recorded divergences.
- **New parity fixture** `oi_num2str_forms` (R2025b 25.2, both lanes): complex arrays under every
  form, real arrays under a digit count and under one-conversion formats, integer classes, the two
  refusals.
- **`k14_mps_json`**: six div rows are agreeing lines (`i_09`, `i_10`, `e_f_uint64_max_small`,
  `e_f_uint64_max_large`, `y_uint64_max`, `y_uint64_over`), and new rows for the 64-bit limits and
  long field names; re-recorded and restamped.
- **Scripts that used to work may now answer differently:** `num2str` of anything complex; a
  formatted `num2str` of a column or a matrix, or of a scalar under a width (now trimmed); a
  formatted datetime under a locale; `MMMMM`, `eeeee`, `M`, `e`, `ee` and the one-letter formats
  without one.

## Divergences

- **A locale R2025b does not recognize is accepted when .NET resolves it**: `xx_YY` (ICU's .NET
  makes a culture of any well-formed tag) and `zh` (R2025b refuses the bare code), fixture rows
  `sp_unknown` and `sp_zh`. R2025b's list of locales is not recorded.

## Still open

- `eeeeee` and longer, `E`, `L` and `c` are refused by R2025b (`MATLAB:datetime:UnsupportedIdentifier`,
  `:UnsupportedSymbol`); here they print a name.
- A locale outside the table keeps .NET's names.
- `num2str` under a format with two or more conversions keeps the row-at-a-time printing; R2025b's
  sprintfc consumes several elements a cell (`'%d,%d'`), unmeasured beyond one row.
- The rest of item 316 stays as ADR 0350 left it: the abbreviation history for past dates, the
  once-per-session warning about a non-standard system zone, the zones this Windows lacks and the
  seven whose 2026 offsets differ.
- What item 212 recorded stays: complex integers and complex sparse arrays (ADR 0266), int64 past
  2^53, the four inputs R2025b's decode ends its process on (a name of 2050 characters or more is
  one of them now), `mps.json.decode`'s dispatch row.
- Seen while probing, not this item's: `Inf*1i` is `NaN+Infi` here and `0+Infi` in R2025b;
  `namelengthmax` is 63 here and 2048 in R2025b, and `isvarname` of 64 to 2048 characters is true
  there; the protobuf reader (`JgsMpsProtobuf`) still refuses a field name past 63;
  `datetime(2026, 1, 1:0)` is refused here, where R2025b answers a 1-by-0 datetime.
