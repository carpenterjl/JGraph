# ADR 0148 — An escape the format does not know ends the format

## Status

Accepted (M144).

## Context

`sprintf` passed an unrecognised escape through and carried on:

```
jgraph -batch "s = sprintf('B \ C'); disp(length(s))"
5
```

R2025b answers `2`, and says why:

```matlab
>> s = sprintf('B \ C');
Warning: Escaped character '\ ' is not valid. See 'doc sprintf' for supported special characters.
>> double(s)
    66    32
```

So MATLAB does two things JGraph did neither of: it warns, and it **stops producing output at the
offending escape**, discarding the rest of the format string. The backslash and everything after it
are gone, not printed.

This is a formatting bug and not a lexer one, which is easy to get backwards. A MATLAB single-quoted
literal decodes nothing at all — `'a\n'` is three characters, an `a`, a backslash and an `n` — so
every escape a script writes is handed to `sprintf` as two characters and decoded, or not, by the
printf family. JGraph already knew this: `UnescapeFormat` ran over the format string on the MATLAB
side only, because JGS decodes escapes in the literal itself. What it did with an escape it did not
recognise was to write both characters out and keep going, with a comment saying so.

**The whole rule was measured before anything was touched**, in nine `matlab -batch` runs over some
three hundred calls. Almost none of it is guessable from C's rules, and four separate parts of it
were wrong here.

### The accepted set is not C's

```matlab
>> double(sprintf('A\"B'))     % 65 34 66   — accepted, though MATLAB's quotes never needed it
>> double(sprintf('A\''B'))    % 65 39 66   — likewise
>> sprintf('A\%B')             % 'A'        — REJECTED; '%%' is how a per cent sign is written
```

`\n \t \r \a \b \f \v \\ \" \'` are the whole named set. `\%` is not in it, and `\e` — which GNU C
has — is not either. JGraph accepted `\%` by passing it through, which then fed a bare `%B` to the
conversion reader and drew *"sprintf does not support the specifier"* — a refusal where MATLAB warns.

### Both numeric forms run to as many digits as follow

```matlab
>> double(sprintf('A\x41B'))       % 65 1051    — 0x41B, one character, not 'A' then 'B'
>> double(sprintf('A\1234B'))      % 65 668 66  — 0o1234, then a literal 'B'
>> double(sprintf('A\177777Z'))    % 65 65535 90
>> sprintf('A\200000Z')            % 'A' — Warning: The octal value specified is outside …
```

C stops a `\x` at two digits and an octal at three. MATLAB stops at neither: it reads every hex or
octal digit that follows, then complains if the value it built is above `0xFFFF`. JGraph had C's caps,
so `'\x41B'` was three characters where MATLAB makes one and `'\177777Z'` was six where MATLAB makes
three. The ceiling is `0xFFFF` for both forms, measured from either side — `\xFFFF` and `\177777` are
the highest each will take, `\x10000` and `\200000` are out of range.

A `\` followed by an `8` or a `9` is a *bad octal digit* rather than a bad escape, but only in the
first position: `'\8'` complains and `'\08'` is a NUL followed by a literal `8`. Uppercase `\X` is not
a hex escape at all.

### The truncation ends the pass, not the call

This is the part that would have been guessed wrong. MATLAB's format repeats until the values run
out, and the truncation applies to **each pass**:

```matlab
>> sprintf('[%d]\q[%d]', [1 2 3 4])   % '[1][2][3][4]' — four short passes, not '[1]'
>> sprintf('%d\q%d', 5, 6)            % '56'
>> sprintf('%d%%\q%d', 5, 6)          % '5%6%'
>> sprintf('\q%d', 5)                 % ''  — a pass that consumes nothing ends the call
```

Conversions already reached in the pass do print; conversions after the escape never run, in that
pass or any other. And the warning is raised **once for the call** however many passes there were:
four values through `'%d\q'` produce one warning, not three.

### Not every function in the family answers a fault the same way

| call | on a bad escape |
| --- | --- |
| `sprintf`, `fprintf`, `sscanf` | warns, format cut short |
| `error`, `warning`, `assert`, `MException` | warns, message cut short |
| `compose` | **raises** `MATLAB:printf:BadEscapeSequenceInFormat` |

`compose` is the odd one and it was measured rather than assumed: `compose('B \ C')` throws where
`sprintf('B \ C')` warns and answers `'B '`.

`printf` is not a MATLAB function — `exist('printf')` is `0` in R2025b — and JGraph does not define
one either, so there was nothing to reconcile.

### A message is a format only when something says so

Probing `error` for the escape rule turned up a nearer question: **when is the message argument a
format at all?**

```matlab
>> error('B \n C')              % message is 'B \n C' — backslash and 'n', untouched
>> error('B \n C', 1)           % message breaks the line
>> error('my:id', 'B \n C')     % message breaks the line
>> error('B \ C %d')            % message is 'B \ C %d' — the '%d' is literal too
```

The message is read as a format when data follows it, or when an identifier came before it, and is
used exactly as written otherwise. JGraph decoded escapes unconditionally, so `error('B \n C')` broke
the line where MATLAB does not. That is not a detail: without this rule the escape warning cannot be
raised at all, because **the warning's own text contains a backslash** — `Escaped character '\ ' is
not valid.` — and raising it through the script's `warning` would have re-entered the decoder and
complained about the complaint.

The same probe showed these four read their data the way `sprintf` does, which JGraph did not:

```matlab
>> error('a%d ', 1, 2)     % 'a1 a2 ' — the format repeats
>> error('a%d %d', 1)      % 'a1 '    — and stops where the values run out
```

JGraph used the strict JGS reading here, which refuses a call with values left over. That mattered
the moment truncation arrived: a format cut short usually has no conversions left, so every
`error('B \ C %d', 5)` would have become *"sprintf got 1 more argument(s) than the format uses."*

## Decision

### The walker becomes its own file, and hands back the fault

`UnescapeFormat` — sixty lines inside `JgsBuiltins.Matlab.cs` — becomes `JgsFormatEscapes.Decode`, in
a file next to `JgsSprintf.cs`, which is where the other half of the format string is read. It
answers the text decoded so far and, through an `out` parameter, a `Fault` carrying MATLAB's own
identifier and MATLAB's own message.

Both are MATLAB's own and not invented, which is the test ADR 0062 sets for carrying an identifier at
all: the objection there is to promising spellings a script would take the wrong branch on, and
`MATLAB:printf:HexCharCodeOutOfRange` is the spelling R2025b uses. All six messages are transcribed to
the character, spacing included — `A lone trailing backslash, '\' , is not a valid control character.`
has a space before its comma, and a script matching on that text should find what it would find in
MATLAB.

The digit accumulation saturates rather than wrapping. A format may write as many digits as it likes,
and `'\x1FFFFFFFFFFFFFFFF'` has to survive being read to the end and *then* rejected, rather than
overflowing into a value that looks legal.

### Truncating the format string is what makes the repetition right

The fault ends the format, and the shortened format is what `FormatMatlab` is then handed. Nothing in
the cycling loop had to learn about escapes: stopping every pass at the same point is exactly what
cutting the format once does, and `'[%d]\q[%d]'` over four values comes out `'[1][2][3][4]'` because
the format it actually runs is `'[%d]'`. The one-warning-per-call rule falls out of the same shape —
the decode happens once, before the loop.

`sprintf('\q%d', 5)` answering empty rather than looping forever is also already handled: the
truncated format is empty, a pass over it consumes nothing, and `FormatMatlab` already breaks on a
pass that consumes nothing.

### The warning goes through the script's own `warning`

As the rank-deficiency warning does, and for the same reason: it is what lets `lastwarn` report it and
what a script that has redefined `warning` will see.

That exposed an ordering bug in the wrapper that records `lastwarn`. It recorded the message *before*
calling the warning, so an escape complaint raised from inside `warning('B \ C %d', 5)` became the
warning `lastwarn` reported, displacing the script's own. It records after the call now, which is the
order MATLAB's own output is in — complaint first, warning second, and the second is the one that
stands.

### `compose` raises, because `compose` raises

The one call site that differs, and it differs by measurement rather than by design taste. It throws a
`JgsRuntimeException` carrying the fault's identifier and message; every other site warns.

### A message is decoded only when it is a format, and only in MATLAB

`FormatMessage` gains the dialect and a flag saying whether an identifier was read off the front of
the call, and decodes only when `identified || data follows`. It also picks `FormatMatlab` over
`Format` for the MATLAB dialect, so `error`, `warning`, `assert` and `MException` repeat their format
and drop surplus values the way R2025b does.

Gating the decode on the dialect is new — it used to run in both — and it is the same gate `compose`
and `sscanf` already had. A JGS literal arrives with its escapes decoded by the lexer, so decoding
again was reading a backslash the script had asked to keep.

## Consequences

Ninety-five assertions are new in `MatlabFormatEscapeM144Tests`, and not one of them was typed: a
generator holds the calls, runs them through `matlab -batch`, prints `mat2str(double(...))` of each
answer with `lastwarn` beside it, and writes the `InlineData` rows from what comes back. The file
covers all thirty-one accepted forms, all twenty-two faults across the four kinds, the ten
cycling-and-truncation cases, the thirteen `error`/`assert` message forms, `MException`, `warning`,
`compose`, `sscanf` and `fprintf`'s byte count.

Separately, a seventy-three-line script was run through R2025b and through JGraph and the two outputs
diffed. Seventy of the seventy-three agree; the three that do not are the two divergences below, both
of which predate this milestone. Before the change, forty-eight of the seventy-three disagreed.

`compose`, `sscanf` and the `error` family are pinned by the new file even where only one of them was
reported, because all of them now reach one decoder and a future change to it would move all of them
at once.

### Divergences

- **`sprintf` answers a 0-by-0 empty where MATLAB answers 1-by-0.** `size(sprintf('\q'))` is `[0 0]`
  here and `[1 0]` in R2025b, so `mat2str(double(sprintf('\q')))` reads `[]` here and `zeros(1,0)`
  there. Nothing about escapes causes it — `size(sprintf(''))` shows the same `[0 0]` against
  MATLAB's `[1 0]`, with no backslash in sight — and it is the general question of what shape an
  empty char row carries rather than anything this milestone decided. It is invisible to `isempty`,
  to `length`, to concatenation with another row, and to every comparison against `''`; it bites a
  script that asks `size` directly or that concatenates vertically.
- **`lastwarn` reports the format a warning was given, not the message it printed.**
  `warning('x%d', 5); lastwarn` is `'x%d'` here and `'x5'` in R2025b. The warning itself is right in
  both — `Warning: x5` goes out — and only the recorded copy differs, because the wrapper that
  records it sees the arguments rather than the text the warning builtin made from them. This one
  predates M144 and is untouched by it; what M144 changed is that an escape complaint raised on the
  way can no longer be the thing `lastwarn` keeps.
- **`num2str` does not decode escapes in a format at all.** `num2str(pi, 'B \ C %f')` is
  `'B \ C 3.141593'` here, where R2025b warns and answers `'B '`. `num2str` never went through
  `UnescapeFormat` and still does not: it is a question of *which* functions decode, not of what the
  decoder does with a bad escape, and widening it would change what every `num2str` with a `\n` in
  its format already answers. Named here so it is not mistaken for something M144 covered.
