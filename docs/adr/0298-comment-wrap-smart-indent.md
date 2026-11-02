# ADR 0298 — The editor's Comment, Uncomment, Wrap Comments, Smart Indent and Indent

## Status

Accepted, 2026-11-01. Stage W9b of the App Designer window plan: the EDITOR tab's CODE section. W9a (ADR 0297) has Find and Replace, Go To's line and bookmarks; W9c has code folding, the display toggles, zoom, the split document and Print.

## Context

The CODE section's six buttons were disabled, and the shell's editor had none of them. R2025b's rich text component holds them (`toolbox/matlab/rich_text_component/release/bundle.mwBundle.rtc_R2025b.js`): `rtc/plugins/language/common/CommentCode`, `rtc/plugins/language/matlab/commentCode/WrapComments` with `CommentSection`, `LineCommentSection`, `BlockCommentSection`, their `DeleteEventInfo`, `ReplaceEventInfo` and `InsertEventInfo`, `WrapCommentLineCache` and `CommentUtilities`, `rtc/plugins/indentation/Indentation`, `rtc/plugins/indentutilities/IndentUtilities`, `rtc/plugins/language/common/smartindenting/AbstractSmartIndentingPlugin`, `rtc/plugins/language/matlab/smartindenting/MatlabSmartIndenting`, `rtc/plugins/editortabservice/EditorTabService` (the tab arithmetic) and `rtc/plugins/language/matlab/tokenizer/MatlabTokenUtilities` (which tokens open, close and stand mid-block). Comment, Uncomment and Wrap Comments were run under node, cut out of the bundle as they are (`ui-probes/w9/oracle/comment.js`). Smart Indent has a MATLAB-side oracle: `indentcode`, which calls `matlab.codeanalyzer.internal.formatCode` with the editor's settings, runs headless (`ui-probes/w9/w9_indent.m`). The settings read headless: `comments.MaxWidth` 75, `comments.FromCommentStart` false, `FunctionIndentingFormat` MixedFunctionIndent, `tab.InsertSpaces` true, `tab.IndentSize` 4, `tab.TabSize` 4, `indent.PadEmptyLines` false, `indent.SmartIndentEntireDocument` false.

## Decision

- **Comment** (Ctrl+R, `CommentCode`): `% ` on each line of the selection - a selection ending at a line's first column leaves that line out - at the lines' common indent: the least indent of the lines with text, none at all as soon as one has none, or the least indent of the empty lines when every line is one. A line of white space is emptied and takes the indent and the sign; on a line with text the sign goes at the index its indent reaches, past a tab that straddles it. **Uncomment** (Ctrl+Shift+R, Ctrl+T): `% ` off the front of a line that starts so, else a bare `%`, and a line that was only a sign becomes empty.
- **Wrap Comments** (Ctrl+J, `WrapComments`): the run of line comments the caret is in - those with the same sign and indent, not a bullet, not a sign alone - or its block comment, or every run in the selection. A run's lines are joined, each line's trailing white space cut and its sign made one space, then broken at white space so that no line passes the width, counted from the line's start (or from the sign with `FromCommentStart`); a word too long for the room stands on its own line. A block comment is wrapped paragraph by paragraph at the block's indent, its `%}` put at that indent. R2025b's sections and their events are carried over one by one, the line numbers live through the edits as its nodes are.
- **Smart Indent** (Ctrl+I, `IndentUtilities` and `formatCode`): each line of the selection, or the caret's, by the statement before it as it now stands: one indent more after a line that opens a block or stands as `else`, `elseif`, `case`, `otherwise` or `catch`, one less for each block a line closes; a closing `end` at its opener's indent, a middle keyword at its block's (`case` one in from `switch`); a continued line one indent in and the lines after it level with it; a comment line as a statement, a block comment's lines left as they are; `end` inside brackets an index, `arguments` a block only at a function's start, `properties`, `methods`, `events` and `enumeration` blocks only in a class; a nested function one indent past the function it is in, and after its `end` the parent's contents again; a function's contents by the mode - Mixed (the default) indents a nested function's and a method's and not a top-level function's, All every function's, Classic none; in a file whose functions have no `end`, every function's line at the margin and the later ones' contents as if nested. Trailing white space is cut; an empty line is emptied (`PadEmptyLines` off).
- **Increase Indent** (Ctrl+]) puts four spaces after each line's leading white space; **Decrease Indent** (Ctrl+[) takes a tab or up to four spaces off its front. Tab over a selection spanning lines indents it as a block, Shift+Tab unindents the caret's line or the selection's. The Tab key inserts spaces (`InsertSpaces`), in every JGraph editor.
- **Generated code stays**: each tool skips a line that may not be edited, the buttons are disabled with the caret on one, and Wrap Comments does nothing when a line it would change is one.
- **Both windows**: the shell's Edit menu has the six, and App Designer's CODE section is live (`AD_EDITOR_COMMENT`, `AD_EDITOR_UNCOMMENT`, `AD_EDITOR_WRAP_COMMENT`, `AD_EDITOR_SMART_INDENT`, `AD_EDITOR_INDENT_MORE`, `AD_EDITOR_INDENT_LESS`), each forwarded to the Code View's editor.

## Consequences

- The rules stand free of a window in `JGraph.Scripting/Editing`: `EditorTabs` (the tab arithmetic), `CommentRules`, `WrapComments`, `IndentRules`, `SmartIndent` (a lexer that knows strings, transposes, brackets, continuations and block comments, and the token walk). `ScriptEditorControl.Code` binds them with their keys, as one undo step each, the selection kept over the lines.
- `SmartIndent` has an `IndentMode`; the mode, the width and `FromCommentStart` are R2025b's defaults, with no setting of JGraph's own.
- Every JGraph editor now inserts spaces for Tab.

## Measured

- **R2025b's own code under node** (`ui-probes/w9/oracle/comment.js`, `CommentOracle.json`): 500 line ranges commented and uncommented, 500 documents wrapped from the caret or over a selection at widths 20 to 75, from the line's start and from the sign, with bullets, bare signs, blank lines, code and block comments among them. `CommentOracleTests` (2) replays them all to the character; both matched at the first run.
- **R2025b's formatter headless** (`ui-probes/w9/w9_indent.m`, `SmartIndentOracle.json`): 360 seeded programs - scripts, function files with and without ends, nested functions, classes with properties, events, enumeration and methods, `arguments` blocks, every block kind, one-liners, continued lines, comments and block comments - with their indentation spoiled, in each of the three modes, with blank lines emptied or padded. `SmartIndentOracleTests` replays every whole-text case to the character, the Classic-mode classes left out (below).
- **Tests**: `EditorToolsTests` gains three. With the designer, App Designer, toolstrip, settings and editing suites, 1,127 pass.
- **The window check** (`run-w9bdesigner.ps1`, JGraph.Application in Release over an `.mlapp` in its Code View): all 43 checks pass - the six buttons disabled on a generated line, Comment and Uncomment read back from the file at the line's own indent, Increase and Decrease Indent, Smart Indent putting a line back where the function's body stands, and a comment written past the width by the find strip's Replace All broken in two by Wrap Comments, neither line past 75 and together the text as it was. I looked at pictures of the CODE section and of the wrapped comment.
- Every older designer window check passes on the final source (see the commit).

## Not measured

- **R2025b's own windows**: nothing was compared on screen.
- **Smart Indent over a selection against R2025b's editor**: the editor's `IndentUtilities` reads each line's indent from the lines above as they stand, which is what JGraph does; `formatCode`'s result for a range of lines was measured to depend on where the range ends (`w9_probe5`) and was not matched. The oracle holds whole texts only.
- **Smart indent while typing** (`SmartIndentWhileTyping`, the Enter key and a closing keyword typed): not built in this stage.
- **In a window**: Ctrl+T, Tab and Shift+Tab over a selection, a selection spanning lines, a block comment wrapped, the shell's editor - the tests cover their rules.

## Divergences

- **A class's methods in Classic mode stay level**: `formatCode` puts a class's second method one indent left of its first (a function's `end` in a class block de-indents the line after it), which the rich text component's `IndentUtilities` does not do; JGraph follows the component, so those cases are left out of the oracle.
- **No setting of its own** for the indenting mode, the comment width or counting from the sign: R2025b's defaults stand.
- **No smart indent while typing**, no `SmartIndentEntireDocument`.
- **Decrease Indent leaves the selection as it is** where R2025b moves a selection that starts in the leading white space past it.
- **One caret**: R2025b's tools run over every cursor.
