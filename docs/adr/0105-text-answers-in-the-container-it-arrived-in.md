# 0105 — Text answers in the container it arrived in

Date: 2026-08-29 · Milestone: **M104** · Status: accepted

## Context

The everyday-gaps arc (ADR 0100) reaches the strings row: thirteen names from MATLAB's `strfun`
folder — `append` `eraseBetween` `replaceBetween` `extract` `splitlines` `strtok` `strjust`
`strvcat` `str2mat` `strmatch` `isStringScalar` `hex2num` `num2hex` — and the four `mustBe…` names
`validators` still lacked: `mustBeNonsparse` `mustBeValidVariableName` `mustBeFile` `mustBeFolder`.

Almost none of it is specified where a reader would look. The documentation says `eraseBetween`
removes text "between" two things but not whether the things themselves go; it says `splitlines`
splits at eight different characters, which R2024a does not do; it gives `strmatch` no rule at all
for a pattern longer than the list. Every answer below was measured against MATLAB R2024a before
anything was written — eight probe scripts, then a 229-line side-by-side run.

## Decision

**Text answers in the container it arrived in, and that rule is one type.** A private
`TextBundle` records which of MATLAB's three containers a call was handed — a char row, a string
array, a cell of char rows — along with its shape and the pieces inside it, so no verb decides the
question twice. `eraseBetween` of a cell answers a cell; of a string array, a string array;
`replaceBetween` of a char row with a string replacement answers a char row, because the *subject*
names the kind and the replacement does not.

**The one-to-many verbs share a second rule, measured rather than read.** `splitlines` and
`extract` must find the same number of pieces in every element — MATLAB refuses a ragged answer by
name — and the pieces go along a new trailing dimension: one piece of text answers a column, a
column of text answers rows-by-pieces. A char row has no container of its own, so it answers a
cell, which is why `splitlines('a')` is a cell and `splitlines("a")` is a string.

**A marker bounds a span exclusively and a position bounds it inclusively.** That asymmetry is the
whole of the two between-verbs: `eraseBetween('abcdefg', 'b', 'f')` is `abfg` and
`eraseBetween('abcdefg', 3, 5)` is `abfg` as well, the same four characters reached two ways.
`'Boundaries'` names the other reading in each case. Two corners were measured and are deliberate:
a span never nests, because the scan always resumes past the *end* marker — so
`eraseBetween('aXbXcXd', 'X', 'X')` leaves `aXXcXd` and not `aXXXd` — and an empty span is legal at
either end, so `replaceBetween('abc', 4, 3, 'X')` appends.

**`splitlines` breaks on the carriage-return family and nothing else.** The documentation lists the
vertical tab, the form feed, and the Unicode line and paragraph separators alongside CR and LF;
R2024a splits on none of them. Measured with `numel`, four ways, and reproduced as measured.

**`strtok` measures its argument with `length`, not `numel`.** MATLAB's own implementation reads a
char matrix as its column-major characters cut off at its *longer side*, so
`strtok(['a b'; 'c d'])` answers the token `ac` and the remainder `' '` — one space, not the four
characters that follow. That is a quirk of one line of `strtok.m` rather than a design, and it is
reproduced because a script that hands `strtok` a char matrix gets that answer and no other.

**`strvcat` leaves out a blank argument and `str2mat` keeps it.** The two names are otherwise the
same builder, and that single rule is the whole reason both exist: `strvcat('a','','b')` is two
rows and `str2mat('a','','b')` is three, the middle one blank.

**`strmatch` pads both sides to the list's width before comparing.** The list becomes a char matrix
first, which is why a candidate shorter than the text sought never matches at all —
`strmatch('apple', {'ap'})` is empty rather than "no, but nearly" — and why `'ap '` matches `'ap'`
under `'exact'`, both having been padded to five.

**`hex2num` pads a short spelling on the right**, so `hex2num('4')` is 2 and not 4, and cuts off a
long one at sixteen digits. `num2hex` spells a double in sixteen and a single in eight, one row per
element in column order, and refuses anything that is neither with the class named in the message.

**The four validators join a family that now carries MATLAB's own identifiers and sentences.**
ADR 0100's amendment — a documented identifier is raised, an invented one is not — had been applied
to the milestones that came after it and not to the `mustBe…` family that predates it. All
twenty-five now raise `MATLAB:validators:<name>` and say what MATLAB says, word for word, checked
by running the same twenty-five refusals through both engines. Two were wrong in substance and not
only in wording: `mustBeFloat` accepted an integer class, and `mustBeMember` listed its set on one
line without quoting the text in it. The three text-reading validators fail
`mustBeNonzeroLengthText`'s check first, which is why `mustBeFolder(1)` says "Value must be text
with one or more characters" rather than anything about folders.

`isvarname` rides along, because `mustBeValidVariableName` is that question asked twice and the
`lang` folder had neither. It moves the counted total by one name and two forms, and this sentence
is where that is said out loud rather than left to look like arithmetic drift.

### A defect found beside the road

- **A semicolon in a bracket of strings made a double.** `["a"; "b"]` came back as a 2-by-1 double
  whose `class` said `double` and whose elements no text function would touch, while `["a" "b"]`
  had been a string array since M63. The single-row path joins string arrays; the multi-row path
  had no string arm at all and fell through to the numeric block machinery, which reads a string as
  one anonymous element. Found on the first parity line that needed a column of labels. The fix is
  a block join of the same shape the numeric one uses: each row's blocks stand side by side and
  must agree on height, then the rows stack and must agree on width.

### Divergences recorded here

- **A three-dimensional answer from `splitlines` or `extract` is refused by name** — MATLAB spreads
  the pieces of a 1-by-2 string array into a 1-by-2-by-2, and nothing in this build holds a
  three-dimensional container of text. A column, which is what a script actually writes, answers
  rows-by-pieces exactly as MATLAB's does.
- **MATLAB's `pattern` objects are not accepted anywhere here** — `extract(str, lettersPattern)`
  and the `startPat`/`endPat` slots take literal text only. The pattern classes are their own arc
  and no name in this milestone half-implements them.

## What this did not close

- **`strread`**, the last `strfun` name — the legacy delimited-text reader, which belongs with
  `textscan` and the file-reading family rather than with the string verbs; `strfun` stands at 40
  of 41.
- **The char-matrix model** itself was left recorded above rather than begun here; it was closed
  afterwards in M105, which gave a char matrix a tag of its own so that `class` says `char` and
  `size` says rows-by-columns. The divergence this ADR recorded is gone with it.
- **`repmat` of a char row** was recorded above as a divergence and left to its own task. That task
  has since been done: `repmat` repeats the characters of a char row into a longer row, so
  `isvarname(repmat('a',1,63))` is now true here as it is in MATLAB. The bullet is deleted rather
  than struck through, because the divergence harvest lifts a struck-through bullet whole.
- **`genvarname`** and the seven other `lang` names; `isvarname` was taken because
  `mustBeValidVariableName` needed the rule, not because the folder was commissioned.

## Consequences

- The scripting layer gains one partial (`TextParts`) and 20 catalogued names; `Interpreter`'s
  bracket literal gains a string arm; `Validators` gains four names, MATLAB's identifiers on all
  twenty-five, and two corrected checks.
- `strfun` moves 27 of 41 names to 40 and its accepted forms 37 to 60; `validators` moves 24 of 31
  to 28 and 16 forms to 20; `lang` moves 2 of 10 to 3 and 0 forms to 2. The toolbox count is
  **242 of 377 names and 378 of 1,036 accepted forms**, and the across-all-kinds builtin count is
  **1,003 of 2,024**.
- **Every one of M104's 27 documented forms is accepted**, and `str2mat`'s was the last `strfun`
  form the prober could not build a call for: a trailing ellipsis after a numbered series —
  `str2mat(T1, T2, T3, ...)` — is repetition, not the older spelling of `___`. One form in the
  whole dump reads that way, so the rule was widened by exactly the case that proves it.

## Measured

Eight probe scripts and a 229-line side-by-side run against MATLAB R2024a on this machine:
**221 lines identical**, and **8 that differ**, every one of them a line asking a char matrix for
its class or its size, or asking `repmat` for a char row — the two divergences this ADR
recorded, both since closed, and nothing else. All twenty-five validator refusals — identifier and message both — are identical.

## Testing

- `tests/JGraph.Tests/Scripting/MatlabStringsM104Tests.cs` — 60 tests, every answer read off
  R2024a; the defining properties asserted where one exists (a span that never nests, a container
  that survives the verb, `hex2num(num2hex(pi)) == pi`).
- Full suite: 6,237 tests, 0 warnings; the five coverage verifiers exit 0.
- `stess_64.m` (25 checks) passes 25/25 here and 21/25 in MATLAB — the four failures there are
  items 22–25, which assert this build's side of the recorded divergences.

## Live checks for the user

```matlab
eraseBetween('aXbXcXd', 'X', 'X')          % aXXcXd — spans do not nest
splitlines(sprintf('a\vb'))                % one piece: R2024a splits on CR and LF only
[t, r] = strtok(['a b'; 'c d'])            % t = 'ac', r = ' ' — strtok measures with length
strvcat('a','','b'), str2mat('a','','b')   % two rows, then three
hex2num('4')                               % 2 — a short spelling is padded on the right
labels = ["alpha"; "beta"; "gamma"]        % a string array again, not a double
```
