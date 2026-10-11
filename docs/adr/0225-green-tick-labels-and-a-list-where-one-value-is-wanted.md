# ADR 0225 — Green, tick labels as R2025b writes them, and a comma-separated list where one value is wanted: open items 96, 97 and 98

## Status

Accepted, 2026-10-11. The twelfth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), the items batch 11 filed (ADR 0224). Every answer was recorded from R2025b (probes
`probe_96`, `probe_97`, `probe_97b`, `probe_97c`, `probe_98`, `probe_98b` and `probe_98c` in the
open-items scratch, then the fixture `oi_lists_ticks_green`). Each item turned out wider than filed.

## Context

- **Item 96.** `LineSpec` mapped `'g'` to `Colors.Green`, the web's `#008000` (`[0 0.502 0]`); R2025b's
  `'g'` and `'green'` are `[0 1 0]`. Every route a colour name takes — a spec string, `'Color', 'g'`,
  `set`, a dot, `patch`, `text`, `bar`, `stem`, `rectangle`, an axes' `XColor`, `validatecolor`, a
  figure's and a uifigure's `Color` — goes through `OptionColor` and `LineSpec`, so all of them drew
  and read back half-bright green. The other seven letters and names were already R2025b's. ADR 0056
  had left this "to be decided once, deliberately".
- **Item 97.** Tick labels did not factor out the `Exponent` batch 11 began answering. The probes
  found more. A ruler's `TickLabelFormat` was not in its property table, so
  `ax.XAxis.TickLabelFormat = '%.1f'` landed raw in the model's .NET format string and drew labels such
  as `%10000001f`. The `xtickformat` road translated printf into .NET custom formats, which cannot say
  several of MATLAB's: `'percentage'` (`'%g%%'`) labelled 1 as `100%`, because .NET's `%` scales by a
  hundred; `'%+g'` and `'%5.1f'` lost their flags; `'%,.2f'` was refused; `'%.0e'` was not written as
  R2025b writes it. R2025b's automatic labels drop trailing zeros (`0, 0.5, 1` where this build wrote
  `0.0, 0.5, 1.0`), a log ruler's decades read `10^{n}`, and `XTickLabel` is a column there and was a
  row here.
- **Item 98.** A dot on a struct array in the MATLAB dialect gave the collected row (M41, M65)
  wherever one value was wanted: `x = u.f` on a 1-by-2 struct array was `[1 2]` where R2025b's is `1`,
  a silent wrong answer, and on an empty array `zeros(1,0)` where R2025b refuses. A brace list `c{:}`
  was refused in JGraph's words wherever one value was wanted, where R2025b's assignment takes the
  first value. Neither spread into subscripts, so the common `A(idx{:})` and `B(idx{:}) = 0` failed.
  `struct('f', cell(0, 3))` was 0-by-0, not 0-by-3.

## Decision

**Item 96.** `'g'` is `[0 1 0]` in `LineSpec`, so in every dialect and every verb. `Colors.Green` itself
is unchanged: it is the model's named web colour, which saved figures and the inspector use by name.

**Item 97.**

- `JGraph.Maths.Ticks.TickFormats` writes MATLAB's printf tick formats itself: one conversion of f F g
  G e E d i with the flags `+ - space 0 # ,`, a width and a precision, `%%`, and `\xHHHH` escapes;
  `%e` is written `1.5\times10^{3}`, as R2025b writes it, which the TeX the tick labels are drawn
  through shows as a power. A format holding no printf conversion is the .NET custom format a figure
  saved before this kept in the same property, and is still honoured.
- A ruler keeps the format as R2025b spells it: `xtickformat('usd')` is `'$%,.2f'`, `'eur'`
  `'\x20AC%,.2f'`, `'gbp'` `'\x00A3%,.2f'`, `'jpy'` `'\x00A5%,d'`, `'degrees'` `'%g\x00B0'` and
  `'percentage'` `'%g%%'`; `'auto'` clears it, and the automatic format reads `'%g'` (`'%g°'` on a θ
  ruler). The ruler's `TickLabelFormat` property answers the same and takes a printf format only,
  refusing the named words and a bad format with R2025b's identifiers
  (`MATLAB:datatypes:PrintfFormatDataType:UnsupportedType` and `:InvalidFormat`) and words.
- R2025b's automatic labels and its `%g` without a precision are one rule, written once
  (`TickFormats.Labels`): four decimals, or as many more as tell the row's ticks apart, trailing zeros
  dropped (1/3 is `0.3333`; ticks half a step of 10⁻⁵ apart are `1.234565`). A log ruler writes an
  exact decade as `10^{n}` automatically and under `'%g'`, chosen or given.
- `AxisModel.Exponent` (null: automatic) is the ruler's `Exponent`, saved with the figure.
  `ExponentTickGenerator` wraps a linear ruler's generator in `TickGenerators.For`: it writes the
  labels over 10ᵉ — the given power, or the rule batch 11 fitted, now in `JGraph.Math` — and says e
  on the `TickSet`. The renderer draws `×10ᵉ` once per ruler: past the 2-D x ruler's right end, above
  a y ruler's top (the yyaxis side rulers' too), past a 3-D floor ruler's high end, above the 3-D z
  ruler and past the polar r labels' spoke. Labels a ruler was given turn the power off, as R2025b's do.
- `XTickLabel`, the ruler's `TickLabels` and the `xticklabels` query read as a column in the MATLAB
  dialect.

**Item 98.** In the MATLAB dialect a comma-separated list where one value is wanted answers by where it
stands (`Interpreter.Lists.cs`, `ListUse`), measured in `probe_98b` and `probe_98c`:

- an assignment, a parenthesis or a function's output takes the first value; a list of none is
  `MATLAB:needMoreRhsOutputs` from a dot and `MATLAB:TooManyOutputsDueToMissingBraces` from a brace,
  and an anonymous function answering none is `MATLAB:needMoreRhsOutputs`;
- an operator (binary, unary, transpose) is a call with the list as its arguments, so too many is
  `MATLAB:maxrhs` and none `MATLAB:minrhs` (this takes in open item 30's `maxrhs` rule);
- a condition (`if`, `while`, `switch`, `for`, `&&`, `||`) takes exactly one, or refuses with
  `MATLAB:index:expected_one_output_from_reference`;
- a target a subscript, a dot or a brace goes on to index takes exactly one, or refuses with
  `MATLAB:index:expected_one_output_from_intermediate_indexing`;
- subscripts, read and written, and a colon's parts spread the list: `A(idx{:})`; `m(u.f)` is
  `m(1, 2)`; `m(s.f)` over an empty array is `m()`, which is `m`; `m(s.f) = 5` is
  `MATLAB:atLeastOneIndexIsRequired`; `1:u.f` is `colon(1, 1, 2)`. A char `':'` subscript is the colon;
- a list of none written as a statement shows nothing and leaves `ans` as it was.

The JGS dialect keeps its collected row and its own brace refusal, frozen since M41. `struct` gives an
empty cell value's shape to the empty struct array.

## Divergences

- **Which ticks a ruler chooses is this build's**: R2025b takes about twice as many (`[0 50000]` in
  steps of 5000 where this build steps 10000), and where the ticks differ the labels do. The fixture
  sets its ticks by hand for that reason; open item 99.
- **A ruler's hidden `SecondaryLabel`**, the text object R2025b draws the `×10ⁿ` with, is not answered.
- **The `×10ⁿ` mark is placed by this build's layout**: above a y ruler's top even when the ruler is
  reversed, and in 3-D past the high end of each floor ruler; where R2025b puts it in those cases was
  not measured.
- **`numel(A, idx...)`**, the legacy form `numel(u.f)` spreads into, is refused; R2025b answers it.
- **A list on both sides of one operator** (`c0{:} + c2{:}`, which R2025b reads as `plus(1, 2)`) is
  refused by its first side's count.
- **The JGS dialect's tick labels follow the new rule too** (`0, 0.5, 1`, `10ⁿ` decades, the `×10ⁿ`
  mark), because the model does not know which dialect drew it; JGS's struct-array row and brace
  refusal are kept.

## Consequences

- Silent wrong answers fixed: half-bright green everywhere a script said `'g'` or `'green'`;
  `percentage` tick labels a hundred times too large; `x = u.f` on a struct array; garbage labels from
  a ruler's `TickLabelFormat`.
- Lifted: ADR 0054's and ADR 0056's "the tick format queries answer this build's (.NET) spelling", and
  ADR 0224's "a dot on an empty struct array is `zeros(1,0)`" and "the tick labels do not factor out
  the `Exponent`".
- Fixture `oi_lists_ticks_green`, 89 lines, all exact. `OpenItemsBatch12Tests`. Tests that pinned
  the old answers now hold R2025b's: the `'g'` colour (`JGFacadeTests`, `MatlabM86MarkerNameTests`,
  `MatlabBubbleChartTests`), `0.00` labels (`ManualTickTests`), the format read-backs
  (`MatlabRulerTickTests`, `MatlabPolarRulerTests`), and `x = s.a` and `x = c{:}`
  (`MatlabCommaSeparatedListTests`, `MatlabSetOperationTests`).
- A heatmap's `CellLabelFormat` still goes through the .NET translation (`JgsRulerTicks.ToNetFormat`),
  so it shares the `percentage` scaling; open item 103.

## Still open

Filed in the open-items list: 99 (R2025b's tick choice), 100 (a line's `Color` `'none'` reads back
empty), 101 (`scatter(x, y, 'g')`'s `CData`), 102 (a bubble chart's alpha read back in eight bits),
103 (a heatmap's `CellLabelFormat`), 104 (`numel(A, idx...)` and a ruler's `SecondaryLabel`).
