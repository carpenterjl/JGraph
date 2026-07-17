# ADR 0017 — Code completion: one builtin catalog, tolerant lexing, signature help, word lists

Date: 2026-07-17
Status: Accepted

## Context

M16 closes the scripting arc with the editor smarts: context-aware completion and signature help for
JGS (the language we own end to end), and curated word lists for C# and Python (external runtimes whose
real semantic completion belongs to real IDEs). The hard constraints: everything above the WPF layer
must be UI-free and testable (the test project is net8.0), and the M12 problem of the hand-duplicated
XSHD builtin list — which had to be maintained in lockstep with `JgsBuiltins` by hand — must die.

## Decisions

### 1. `JgsBuiltinCatalog`: one registry, three consumers, one sync test

`JgsBuiltinCatalog` (JGraph.Scripting, public) describes every builtin once: parameters (with an
`Optional` flag) and a one-line summary; the `Signature` string is derived from the parameter list so
they cannot disagree. Three consumers read it: completion, signature help, and the JGS `.xshd`
highlighting definition, whose keyword and builtin word lists `JgsSyntax` now **generates at runtime**
from the catalog (JGraph.Controls gained a project reference to JGraph.Scripting — WPF referencing a
UI-free layer, layering-legal). `Keywords` comes straight from the lexer's keyword table. A test pins
the catalog to `JgsScriptEngine.BuiltinNames()` — the names derived from the live registration — with
set equality, so a builtin added without catalog metadata (or removed but still documented) fails CI.
Highlighting, completion, and the language can no longer drift apart.

### 2. Tolerant lexing, never parsing

A buffer mid-keystroke is routinely broken, and our parser throws on the first error — so the
completion engine never parses. `Lexer.Tokenize` gained a `tolerant` flag: an unterminated string
becomes a string token to end of line, an unexpected character is skipped. Everything in
`JgsCompletionEngine` (JGraph.Scripting, UI-free) works on that token stream:

- **Completions** (`GetCompletions(code, offset, workspaceSymbols)`): keywords + catalog builtins +
  buffer symbols + workspace symbols, prefix-filtered (ordinal, ignoring case) against the identifier
  being typed, deduped by name, with the replace-span start returned for the editor. Scope rules are
  deliberately simple and honest: `let` bindings and loop variables only offer **below** their
  declaration; `fn`s offer anywhere (they hoist); the identifier being typed never offers itself;
  inside a string, comment, or number the result is empty.
- **Signature help** (`GetSignatureHelp`): a forward token walk to the cursor keeps a bracket stack;
  an identifier followed by `(` (not preceded by `fn` — a declaration is not a call) opens a call
  frame whose top-level commas count the active argument. The innermost *named* frame wins, so help
  tracks into `sin(` inside `plot(` and back out after it closes; array-literal commas don't advance
  the argument; extra arguments clamp to the last parameter (variadic-friendly). Callees resolve
  against the catalog, then `fn`s in the buffer, then workspace symbols.
- **Workspace symbols** (`HarvestFunctions`): the `fn`s of a file as completion items with rendered
  signatures — the currency hosts pass back in for cross-file completion.

### 3. Word lists for C# and Python

`WordListCompletion` (UI-free): language keywords, the `JG` facade members **via reflection** (new API
surface appears in completion automatically), and the script-globals helpers (`readcsv`, `print`, …).
No pretense of semantic completion — that is what the curated list is for.

### 4. `CompletionSupport` (Controls): the only WPF in the feature

One class attaches to each editor: auto-trigger while typing an identifier plus Ctrl+Space
(`CompletionWindow`), `(`/`,` trigger an `OverloadInsightWindow` whose active parameter renders bold.
Completing a JGS function inserts `name(p1, p2)` (required parameters only) with the first parameter
selected, ready to overtype. Two lifecycle rules learned the hard way in live verification:

- The completion list closes the moment a non-identifier character is typed (`TextEntering`) —
  without this AvalonEdit leaves it open as an empty shell that eats keys.
- The signature tooltip tracks the caret (`Caret.PositionChanged`): the bold parameter advances and
  retreats, the tooltip re-targets when the caret enters a different call, and it closes when the
  caret leaves the call — and it is only *recreated* when the callee or active parameter actually
  changed, so typing inside an argument doesn't flicker.

The workspace window supplies cross-file symbols per document: other **open** JGS tabs contribute
their live buffers (an unsaved `fn` completes immediately), remaining workspace `.jgs` files are read
through a last-write-time cache — cheap enough to run on every completion request, fresh after every
save, no watcher plumbing.

## Consequences

- 25 new tests cover the catalog sync, prefix filtering and scope rules, harvesting through broken
  buffers, nested/clamped/declaration signature help, workspace symbols, and the word lists — all
  black-box through the public engine. Verified live: filtering, placeholder insertion, bold active
  parameter advancing on commas, `smooth3` completing in `main.jgs` from `helpers.jgs`.
- The M12 hand-maintained XSHD word list is gone permanently.
- The engine is offset-based and stateless, so a future LSP-style host could reuse it unchanged.
- Completion knows nothing about types or user-fn return values — by design; JGS is dynamically
  typed and the honest scope rules above are what the tokens can prove.
