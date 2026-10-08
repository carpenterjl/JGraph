# ADR 0214 — Error records in R2025b's words: open items 13, 16 and 79

## Status

Accepted, 2026-10-08. The third batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored): the divergence remediation plan's item 7, "error records match MATLAB's". It retires
divergences recorded by ADRs 0174 and 0193 and narrows one of ADR 0167's. Every identifier and
sentence was recorded from R2025b (probe `probe_b4` in the open-items scratch, then the fixture
`oi_error_records`).

## Context

A ported script that branches on `ME.identifier` — `catch e, if strcmp(e.identifier,
'MATLAB:badsubscript')` — took the wrong branch for every error the runtime raised itself, because
those errors carried no identifier, and JGraph's sentences differed from R2025b's. An error at a
script's top level had an empty `ME.stack`, so `getReport` could not say where it came from.

## Decision

All of it is the MATLAB dialect's; JGS keeps its own sentences, a 1-based JGS script included
(the wording follows the running dialect, `JgsRunningDialect.ThreadIsMatlab`, not the index base).

### Subscripts (item 79)

Every subscript refusal is `MATLAB:badsubscript` in R2025b's words (`PackedOps.SubscriptRefusal`): one
subscript past the end is "Index exceeds the number of array elements. Index must not exceed N.",
one below 1 or fractional (NaN and Inf too) "Array indices must be positive integers or logical
values.", and a subscript of several names its position — "Index in position k exceeds array bounds.
Index must not exceed N." or "Index in position k is invalid. …" (`SubscriptPicks` sets the position
for whatever it reads). A write takes the same words. A logical subscript shorter than the array
picks from the front, a longer one is taken while its extra entries are false, and a true past the
end is "The logical indices contain a true value outside of the array bounds." — R2025b's rules,
where a mask of another length was refused.

### Braces (item 16)

A brace read of anything but a cell or a string array is `MATLAB:cellRefFromNonCell`, "Brace
indexing is not supported for variables of this type."; a brace write `MATLAB:cellAssToNonCell`.

### Sizes, brackets, products and assignments

- Two arrays whose shapes do not expand into each other under an elementwise operator:
  `MATLAB:sizeDimensionsMustMatch`, "Arrays have incompatible sizes for this operation."
- A bracket whose pieces do not fit: `MATLAB:catenate:dimensionMismatch`.
- A matrix product whose inner dimensions disagree: `MATLAB:innerdim` with R2025b's sentence. The
  MATLAB dialect had kept JGS's reading of a vector's orientation as incidental, and stood a row up
  as a column when it did not conform: `[1 2; 3 4] * [1 1]` answered `[3; 7]` and
  `[1;2;3] * [1;2]` an outer product, where R2025b refuses both. The shapes are now taken as
  written, and the tests that pinned the old reading (M88, `MatlabSemanticsTests`,
  `ShapedArrayTests`) assert R2025b's refusal.
- A one-subscript write with another count of elements: `MATLAB:matrix:singleSubscriptNumelMismatch`;
  a two-subscript write that does not fit: `MATLAB:subsassigndimmismatch`, naming both sizes.

### Unknown names and arity

A name nothing answers is "Unrecognized function or variable 'x'."; a call of one with arguments
evaluates them first, as R2025b does (measured: an argument's side effect happens), and is "Undefined
function 'f' for input arguments of type 'double'." with the first argument's class. A builtin
called with too many or too few arguments is `MATLAB:maxrhs` "Too many input arguments." or
`MATLAB:minrhs` "Not enough input arguments.".

### The top-level stack (item 13)

An error at a script's top level has a frame naming the script and line (`StackOf`), and one raised
deeper ends with it. `getReport(ME, 'basic')` heads an error that `error` or a throw verb raised
with "Error using <frame> (line N)", a local function as `script>local`, and leaves a runtime
refusal (an index past the end) as its message alone, as R2025b does. The mark that tells the two
apart rides on the caught value's payload in a weak table, so `fieldnames(ME)` is unchanged.

## Measured

- `oi_error_records`: 119 lines, all exact.
- Retired to exact lines: `net_exceptions` `custom_report_basic` (ADR 0174, the line item 13 named)
  and `audio_player` `method_upper` (ADR 0193). `errorhandler_records`'s `h_message_builtin_failure`
  now agrees on the identifier and still differs on the header (ADR 0167, narrowed).

## Divergences

- A builtin's arity refusal is `MATLAB:maxrhs` or `MATLAB:minrhs` for every builtin. R2025b's own
  built-ins say that (measured with `sin`); its functions written in MATLAB say
  `MATLAB:narginchk:notEnoughInputs` or `MATLAB:TooManyInputs` with the same sentences.
- Under an `ErrorHandler`, a runtime refusal's message lacks R2025b's "Error using" header
  (`errorhandler_records`, `h_message_builtin_failure`, ADR 0167).
- `zeros` with an unknown class word keeps JGraph's sentence ("JGraph has no 'e' class").
- A bracket that stacks a row over a column of the same length (`[a; zeros(2, 1)]`) is still read as
  one column, the leniency `ShapedArrayTests` documents; R2025b refuses it. Valid MATLAB code never
  meets it.

## Consequences

`try`/`catch` code that branches on these identifiers takes R2025b's branch. A MATLAB-dialect script
that leaned on JGraph's turning of a vector in a product now meets R2025b's refusal, as it would in
MATLAB.
