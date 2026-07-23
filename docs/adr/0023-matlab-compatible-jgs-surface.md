# ADR 0023 — MATLAB-compatible JGS surface

## Status

Accepted (M21, 2026-07-18). §3 (1-based paren indexing and 1-based `find`) superseded by
[ADR 0028](0028-uniform-zero-based-indexing.md) (M25) — JGS is now uniformly 0-based. The rest of
this ADR stands.

## Context

The milestone's acceptance criterion was two real MATLAB lab scripts (an FM modulation/demodulation
lab and an FFT audio-compression lab) running in JGS with only minimal edits. Beyond the builtins
they call (ADR 0024), the scripts lean on core MATLAB language habits JGS lacked: `;` output
suppression with echo otherwise, colon ranges (`0:1/fs:3`), 1-based parenthesis indexing with `end`
and slice writes (`X(1:N/8) = 0`), `for k = 2:N … end` block syntax, `~=`/`.*`/`^` operator
spellings, `[a; b]` matrix rows / vertical concatenation, command-style `figure;`, and complex
numbers (`fft` results).

## Decision

1. **`;` is its own token and suppresses echo.** The lexer previously folded `;` into the newline
   separator; it is now `TokenType.Semicolon` — still a statement separator, but the parser marks a
   statement ending in `;` as `Suppressed`. The interpreter gains an echo sink (wired to the console
   by `JgsRunner`): unsuppressed `let`/assignments echo `name = value`, a bare variable displays
   itself, and any other unsuppressed non-null expression result binds `ans` (assigned even when
   suppressed, like MATLAB) and echoes `ans = value`. Echo output is budgeted — long arrays truncate
   with an element count — so echoing a million-sample signal costs O(line length). Builtins that
   return nothing (`title(...)`) never echo. Adding/removing a `;` counts as a live edit
   (`Suppressed` participates in `AstEquals`).
2. **Colon ranges.** `start:stop` and `start:step:stop` parse at a new precedence level between
   comparison and additive, evaluating to an inclusive sequence with MATLAB's floating-point
   endpoint tolerance (`0:0.001:3` is exactly 3001 points). Inside a paren index, a lone `:` is
   "everything" and `end` is the target's length (tracked by an interpreter stack, so
   `a(b(end))` nests correctly).
3. **Paren indexing is 1-based; brackets stay 0-based; `find` follows the parens.** Calling an
   array with `()` was already indexing (M18) but 0-based. It is now 1-based — reads, writes
   (`x(k) = v`), slice and mask writes (`x(1:n) = 0`, `x(mask) = v`, `x(:) = v`, compound
   operators included), all with single evaluation of the target and index. **Breaking:** `find`
   now returns 1-based indices so the canonical `volt(find(temp > 85))` gather stays aligned;
   bracket-composed uses (`volt[find(...)]`) must switch to parens.
4. **MATLAB blocks coexist with braces.** A `for`/`while`/`if`/`fn` header not followed by `{`
   collects statements to a closing `end`; `elseif` chains share the single `end`; `for k = 2:n`
   is accepted alongside `for k in xs`. The existing AST nodes are reused, so the debugger's block
   tracking and live edit are untouched. `end` and `elseif` become reserved words.
5. **Operators.** `~` and `~=` alias `!`/`!=`; `.*` `./` `.^` alias `*` `/` `^` (JGS operators
   already broadcast elementwise); `^` is MATLAB power — tighter than unary minus (`-2^2 = -4`),
   left-associative, unary signs allowed on its right operand (`2^-3`).
6. **`[a; b]`.** Semicolons inside an array literal separate rows: all-scalar rows build a matrix
   (nested row arrays, rectangular); rows containing arrays vertically concatenate into one flat
   array (`[audio; zeros(k, 1)]`).
7. **Command form and auto-display.** A statement consisting of a bare builtin name calls it with
   no arguments (`figure;`). After a successful run, every registered figure the script never
   `show()`ed is displayed automatically — the MATLAB expectation that `figure; plot(...)` opens a
   window by itself. Explicit `show()` calls are not repeated.
8. **Complex numbers are a first-class value.** `JgsType.Complex` boxes a
   `System.Numerics.Complex`; zero-imaginary results normalize back to plain numbers so real-valued
   outcomes of complex math flow into every numeric path. Literals `2i`/`1.5j`; `+ - * / ^`
   promote; `%` and ordering comparisons error with guidance; `abs` is magnitude and
   `real`/`imag`/`conj`/`angle` are new elementwise builtins. Feeding complex data to a plotting
   verb errors with "take abs(), real(), or imag() first". Constants `pi`, `e`, `inf`, `nan` are
   value bindings (the builtin catalog gained an `IsConstant` flag so editors render them bare).

## Deliberate divergences (documented, not implemented)

- Arrays keep reference semantics: `X2 = X1; X2(1:k) = 0` also mutates `X1` (MATLAB copies on
  write). Harmless in the acceptance scripts; noted in the scripting guide.
- Array literals require commas — `[0 20 30]` is not accepted (MATLAB's whitespace rules are
  ambiguous); the demo's two literals gained commas.
- `%` remains the modulo operator, not a comment (user decision); comments stay `//` and `#`.
  `mod(a, b)` covers MATLAB code.
- `let` remains required for first bindings (user decision) — the typo safety net stays.
- `sqrt`/`log` stay real-domain and error on complex input.

## Consequences

- **Breaking:** paren indexing and `find` moved from 0-based to 1-based (tests, examples, and the
  guide were revised); `end`/`elseif` are reserved words.
- Every unsuppressed statement now echoes, so pre-M21 scripts (which rarely use `;`) become chatty
  in the console; add semicolons to quiet them.
- The sound demo holds several ~1.25M-element `JgsValue[]` arrays with boxed complex values
  (hundreds of MB transiently). Accepted for M21; a packed numeric-array representation would be a
  separate milestone.
