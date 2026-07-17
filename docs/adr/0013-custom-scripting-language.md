# ADR 0013: A custom scripting language (JGS)

- Status: Accepted
- Date: 2026-07-16

## Context

Milestone 12 completes the data-and-scripting arc (M10 data import, M11 C#/Python hosts). Alongside those
two full-size language hosts, the goal was an **in-app scripting language of our own**: something a user can
write in the same editor, with no external toolchain, that still exposes the full plotting API. The C# and
Python engines each depend on a heavyweight runtime (Roslyn; a native CPython). A built-in language should
have neither dependency, should be safe to run untrusted input, and should slot into the M11 seam without
touching the editor, the service, or the window.

## Decision

1. **A real, hand-rolled language — "JGS" — living inside `JGraph.Scripting`.** It is a small, dynamically
   typed, tree-walking interpreter under `JGraph.Scripting/Jgs/`: a `Lexer` (significant newlines, with
   suppression inside `()`/`[]`; `#` and `//` comments), a recursive-descent `Parser` producing an AST, and
   an `Interpreter` that walks it. No new project and no new package — JGS adds no dependency edge. The whole
   implementation is `internal`; the only public type is `JgsScriptEngine`.

2. **It is a third `IScriptEngine`, and that is the *only* integration point.** `JgsScriptEngine`
   (`Language = "JGS"`, always `IsAvailable`) parses, interprets, and reports failures exactly like the other
   engines — every lexer/parser/runtime error is a `JgsException` carrying a 1-based line/column, mapped to a
   `ScriptDiagnostic`; nothing throws out of `RunAsync`. Registering it in DI and adding one starter template
   is all the host needed. The reusable `ScriptEditorControl` gained a JGS **syntax-highlighting** definition
   (an embedded `.xshd` registered with AvalonEdit's `HighlightingManager`), which is an editor concern in
   `JGraph.Controls`, not an engine concern — confirming the M11 promise that "a third engine drops in with no
   other change."

3. **The language is small but complete enough to be useful:** `let` bindings and assignment, numbers,
   strings, booleans, and arrays; arithmetic, comparison, and short-circuiting logical operators; `if`/`else`,
   `while`, `for … in`, `break`/`continue`; first-class `fn` functions with **closures** and recursion; and
   indexing / indexed assignment. Numeric operators are **element-wise over arrays** (with scalar
   broadcasting), which gives the MATLAB-like feel the framework is modelled on (`sin(x)`, `x * 2`, `a + b`).

4. **Built-ins mirror the `JG` facade — there is no parallel plotting surface (same rule as M11).** A single
   built-in registry bridges to the existing static API: array/math helpers (`linspace`, `range`, `zeros`,
   `sin`…, `sum`, `mean`), the M10 table readers (`readcsv`/`readxlsx`/`readtable`, plus `column` to pull a
   column into a vector), and the plotting verbs (`plot`, `scatter`, `bar`, `stem`, `histogram`, `errorbar`,
   `subplot`, `title`, `xlabel`, `legend`, `xlim`, `grid`, `hold`, `semilogx/y`, `loglog`, `show`, `print`).
   Overloads dispatch on argument type, so `plot(x, y, "b-")` and `plot(table, "x", "y")` both work. Each run
   starts with `JG.Reset()` and builds a WPF-free `FigureModel`, marshalled to the UI only at `show()` — the
   same contract as the C# and Python engines.

5. **JGS is sandboxed by construction, and interruptible.** Because it is our interpreter with a fixed
   built-in set, a script's only IO is the table readers — there is no file, network, reflection, or `import`
   surface to escape through. Runaway scripts are bounded three ways: a per-statement **step budget**, a
   **call-depth limit** (so infinite recursion fails cleanly instead of overflowing the stack), and a
   **cooperative cancellation** check on every statement. That last point is a genuine advantage over the C#
   and Python engines, whose tight CPU loops cannot be interrupted: a JGS `while true {}` stops the moment the
   editor's Cancel button (or a `CancellationToken`) fires.

## Consequences

- The app now offers three languages behind one seam and one editor. The C# and Python engines trade power
  and ecosystem for a runtime dependency; JGS trades breadth for zero dependencies, safety, and
  interruptibility. Users who just want to script a figure — or run in a locked-down environment without a
  Python install — have a first-class option.
- The language is intentionally minimal: fixed-size arrays (no growth/`push`), no maps/objects, no user
  types, no modules. These are deliberate scope limits, not oversights; the built-in set is the extension
  point (add a `BuiltinFunction`), and new plot verbs are one registry entry that forwards to `JG`.
- JGS is unit-tested end-to-end through `JgsScriptEngine` (like the other engines): parsing, vectorized math,
  control flow, closures/recursion, positioned syntax/runtime errors, CSV→plot, and cancellation inside a
  tight loop. Tests touching the static `JG` facade use `[Collection("JG facade")]`.
- The milestone also ships the arc's final deliverables: runnable example scripts for all three languages
  (`examples/example.csx`, `example.py`, `example.jgs`, with `sample-measurement.csv`) and a written
  GUI-import walkthrough ([docs/import-guide.md](../import-guide.md)).
