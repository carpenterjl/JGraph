# ADR 0018 — Current Folder browser, hide-not-close panes, and filename completion

Date: 2026-07-17
Status: Accepted

## Context

Real use of the M13–M16 scripting workspace surfaced three UX gaps: the Files tree was a static
listing (no navigation; a `.graph` document opened as raw JSON text), a tool pane dismissed via its
title-bar button was unrecoverable without restarting the app, and typing the file argument of
`readcsv("…` got no help even though the workspace knows every file in it. M17 closes all three,
MATLAB-style, plus one language nicety the workflow kept bumping into: MATLAB hands write `'strings'`.

## Decisions

### 1. The Files pane is a Current Folder browser; re-rooting *is* reopening the workspace

The pane gains an address bar (editable — Enter navigates, a bad path is a status note), an Up
button, double-click-a-folder navigation, and a folder context menu ("Set as workspace root",
"Refresh"). All of them funnel into the one method that already existed, `OpenWorkspace(path)`:
the watcher re-arms, the tree rebuilds, `ScriptContext.ResolvePath` follows automatically (bare
names in scripts resolve against the new root), and the root persists across sessions — navigation
needed **zero new state**. The workspace root and the browsed folder are deliberately the same
thing, exactly like MATLAB's Current Folder. The tree stays eagerly built; browsing into a
subfolder is the escape hatch for huge directories.

### 2. Files open by what they are

Double-click dispatch by extension: scripts → editor tabs; `.csv`/`.tsv`/`.xlsx` → the Data Viewer;
`.graph` → `GraphFormat.Load` routed through the window's existing figure channel, so a saved figure
document opens **as a figure** in the main window (previously it opened as JSON text);
`.txt`/`.md`/`.json` → plain-text tabs (`ScriptDocumentModel` maps unknown extensions to a "Text"
language, which matches no highlighting definition, no word list, and no engine — Run is disabled
for such tabs); anything else → a status-bar note. `GraphFormatException` keeps System.Text.Json
contained in JGraph.Serialization.

### 3. Panes hide, never close — and a View menu brings them back

The mystery of the vanishing panel: every anchorable is `CanClose="False"`, but AvalonDock's default
`CanHide` leaves the title-bar button live — it **hides** the pane (it stays in the layout tree and
serializes with it). That default is kept deliberately: because a hidden pane still exists,
reopening needs no bookkeeping (`Show()` re-docks it where it lived), persistence keeps working
unchanged, and the restore-missing-panes pass (`EnsureKnownPane`, which also now counts hidden panes
as present) never resurrects a pane the user deliberately dismissed. The toolbar's new **View** menu
lists the five tool panes and calls `ShowPane(contentId)` — find in the layout (visible or hidden),
recreate only if truly absent, then `Show()`. One pane registry now backs the View menu, layout
restore, and recreation.

### 4. Filename completion inside file-function strings

`PathCompletion` (JGraph.Scripting, UI-free) is **engine-agnostic by construction** — the same
helpers exist in all three languages, so detection is a single-line lexical scan (both quote kinds,
`#`/`//` comments — a slight over-approximation of C#/Python syntax that is fine for a completion
heuristic), never a parse. The caret qualifies when it sits inside the string that is **argument 0**
of a known file function — `readcsv` (.csv/.tsv/.txt), `readxlsx` (.xlsx), `readtable` (both sets),
and `run` (.jgs, JGS only) — found by walking left from the opening quote over `(` to an identifier.
A rooted path or one with `..` segments offers nothing: outside the workspace, the workspace has no
knowledge. The typed content splits at the last separator: the folder part selects which directory's
children to offer (folders always, with a trailing `/`, so `lib/helpers.jgs` composes; files
filtered by the function's accepted extensions), and the replace span is just the last segment, so
completing never mangles the path already typed. Insertion is plain text — no closing quote, no
call template. The UI plumbing mirrors the M16 workspace-symbols pattern: `CompletionSupport` asks a
`WorkspaceFiles` provider the window fills from `EnumerateAll()`, path lists additionally
auto-trigger on quotes/`/`/digits, and while a path list is open only quotes, separators,
whitespace, or `)` close it (so `-`, `.`, and digits keep filtering file names).

### 5. Single-quoted JGS strings

The lexer accepts `'text'` interchangeably with `"text"` — same escapes (plus `\'`), same
no-newline rule, same tolerant-mode end-of-line recovery — and the generated XSHD highlights both.
One token type; the parser and interpreter never knew the difference.

## Consequences

- 26 new tests: detection across languages/quotes, prefix segmentation, extension filtering,
  subfolder listing, rooted/`..` rejection, tree flattening, single-quote lexing through the engine
  (values, escapes, unterminated diagnostics), string suppression in completion, "Text" language
  mapping, and re-rooted resolution.
- The path detector deliberately understands only argument 0 and single-line strings; a path split
  across concatenation or a variable won't complete. Stated limitation, invisible in practice.
- `WorkspaceFileEntry`/`PathCompletion.Flatten` give any future host (an LSP, a palette) the same
  completion data without touching WPF.
