# ADR 0338 — A plain-text app's bytes are R2025b's, its appendix follows the class, and its components carry their code names

## Status

Accepted, 2026-11-12. Open items 159 and 161, both found in the DS7a review (ADR 0251, "Not
measured" and "Divergences"). Settled from R2025b's sources under `toolbox\matlab\appdesigner`
(`writePlainTextAppFile`, `getAppendixByGrammarName`, `PlainTextDeserializer`, `AppAppendixParser`,
`initializeMApp`, `createInitCodeLinesForComponent`, `addDynamicProperties`) and two headless probes
(`-noFigureWindows -batch`, kept out of the repository): `p159` for the bytes and `p161` for the
marker lines and `AD_CodeName`. No App Designer window was opened.

## Context

**The bytes (item 159).** The DS7a review read `writePlainTextAppFile`'s `fwrite` as writing one
byte per character, so a label such as `é` would be stored as Latin-1 and come back here, where a
plain-text app was read as UTF-8, as U+FFFD. The source opens the file with
`fopen(path, 'w', 'n', 'UTF-8')`, and the probe called the writer itself on a text holding `é`, `€`,
a CJK character and an emoji:

| What | R2025b |
|---|---|
| `writePlainTextAppFile`, and its `fopen`/`fwrite` pair alone | UTF-8: `C3 A9`, `E2 82 AC`, `E4 B8 AD`, `F0 9F 98 80`; `fwrite`'s count is the character count |
| `readAppFile` (a builtin: App Designer's reader and run), `fileread`, the parser on a function's literal, of `C3 A9 E2 82 AC` | `é€` |
| the same three, of `E9 80` (not UTF-8) | `é€`: the whole file in Windows-1252 |
| the same three, of `C3 A9` beside a lone `E9` | `Ã©é`: the whole file in Windows-1252, not the valid part as UTF-8 |
| `readAppFile` of a UTF-8 byte-order mark | dropped (`fileread` keeps it as U+FEFF) |
| `readAppFile` of `81 8D 8F 90 9D 80 9F FF` | U+0081, U+008D, U+008F, U+0090, U+009D, `€`, `Ÿ`, `ÿ` |

So the inference was wrong: R2025b writes UTF-8, as this build does. What this build lacked was the
reader's fall-back. A file in Windows-1252 (a file written by another editor, or by a release before
MATLAB's code files went UTF-8) came back with U+FFFD in place of each accented letter. A save then
wrote U+FFFD back.

**The marker lines (item 161).** `JgsMlappLayout` began the appendix at the first line reading
`%[appendix]` once trimmed, so the line in a callback's comment cut the class there. No column-0
`end` stood before it, the layout was not an app's, and the app opened view-only. `DesignPlainText`'s
section reader began at the same line. R2025b's MATLAB side never cuts by that line: its loader and
its run take each section with `getAppendixByGrammarName`. That function finds the section's grammar
text, such as `%[app:layout]`, with `strfind` over the whole file, then hands `extractAfter` every
position found. What `p161` measured, with the loader under the feature off and the run under the
feature on:

| In a callback of an otherwise good app | Cutter | Loader | Run |
|---|---|---|---|
| `%[appendix]` | cuts | loads, the line in the callback's body | runs |
| `% see %[app:layout]`; `s = '%[app:layout]';`; `%[app:layout]` before a block comment; `%[app:layout]` and a `'</Components>'` string | throws `MATLAB:string:InvalidArgumentSize` | fails, the same | fails, the same |
| `%[app:runConfiguration]` (with the section, or without it, or before a block comment) | the run configuration's cut throws | fails | fails |
| `% %[app:appDetails]`, `%[app:internalData]`, `%[app:componentGroups]`, `%[app:thumbnail]`, `% %[app:layout` | cuts | loads | runs |

**`AD_CodeName`.** R2025b's generator writes, after each component's constructor,
`AppManagementService.addDynamicProperties(app.X, 'AD_CodeName', 'X')`. The probe read it back as a
`matlab.metadata.DynamicProperty`, transient, not hidden, holding a char, on the figure and on the
button. JGraph's graphics handles already hold added properties (`JgsHandleEntry.AddedProperties`,
which carry an app figure's `RunningAppInstance`), so the property could be added.

## Decision

### A code file is read as R2025b reads one

`JgsSourceText.Decode` is R2025b's rule. The bytes are read as strict UTF-8, with a leading UTF-8
byte-order mark dropped. Bytes that are not valid UTF-8 send the whole file to Windows-1252: its 27
defined high bytes by the code page's table, and the five it leaves undefined as the control
characters of the same value, as measured. A UTF-16 or UTF-32 byte-order mark keeps the reading
`File.ReadAllText` gave it before.

These readers now use it:
- `DesignDocument.Open` of a `.m`;
- the `matlab.apps.App` constructor's read of the class file;
- the thumbnail write-back (`DesignDocument.RunArguments`);
- the app comparison's reads (`DesignCompare`);
- `JgsMlapp.ReadSource`, the reader behind every code file this build runs, lists or shows. The
  class half of a plain-text app is therefore decoded as its appendix is.

Writing is unchanged: UTF-8 without a byte-order mark, as R2025b writes. A file read in Windows-1252
is saved as UTF-8, which is what App Designer's own save would make of what its reader read.

### The appendix begins after the class's code

`JgsMlappLayout.AppendixLine` is the first `%[appendix]` line after the class's last line of code.
In a class that parses, that line is the closing `end`. Every line after it is blank or a comment: a
`%` line, or a line of a `%{` … `%}` block, nested blocks counted, where the sections' XML stands.
`JgsMlappLayout.AppendixOffset` gives the same place as a character index. Four readers take the
appendix from these two:
- the layout, which now anchors the class's `end` only before that line;
- `DesignPlainText.Sections`, `IsPlainTextApp` and `RunConfiguration`;
- the runtime's read of the run configuration;
- the comparison's cut of a plain-text app's class (`DesignCompare.CodeTextOf` and its save, which
  keeps the appendix).

A marker or grammar line in the user's code, whether in a comment, a string or a block comment of
its own, is the code's and cuts nothing.

### A grammar line R2025b refuses is named on save, not refused

`DesignPlainText.GrammarLinesInUserCode` lists every line before the appendix that holds
`%[app:layout]` or `%[app:runConfiguration]` anywhere in it, with its one-based line number. Those
are the two grammar texts measured to break R2025b; the others were measured harmless and are not
listed. A plain-text `Save` or `SaveAs` writes the file as it stands and adds to its status:
`R2025b will not open or run it: line N holds '%[app:layout]', ... before the appendix, which App
Designer's section cutter takes for the section's own.`

The save is not refused. The code is the user's, nothing in it is lost, the file opens and runs
here, and a refusal would hold unsaved work hostage to a comment. A file the save writes cannot be
made readable to R2025b without editing the user's code, since the cutter matches the text anywhere.

### Every component carries its code name

The `matlab.apps.App` constructor's generated function now calls a helper after the components are
made and before `registerApp`. The helper, `ad_addCodeNameProperty(app.X, 'X')`, is declared in the
function's own scope, as `registerApp` is. It adds `AD_CodeName`, a char, to each component that is a
graphics handle, and only when the handle has no such property yet (`addDynamicProperties`'s
`~isprop` guard). `isprop`, dot reading and `findall(..., '-property', 'AD_CodeName')` see it.

## Consequences

- A plain-text app with a `%[appendix]` line in its code opens, edits and saves here as one, and its
  text is written again as it was read.
- A plain-text app whose code holds `%[app:layout]` or `%[app:runConfiguration]` opens and runs
  here, and every save of it says R2025b will not.
- A Windows-1252 app file, or any code file that is not UTF-8, keeps its accented letters. This
  covers labels in the appendix, comments and literals in the code, and the code view. Valid UTF-8
  files read as before, byte for byte.
- `isprop(app.GoButton, 'AD_CodeName')` is true in a plain-text app, as in R2025b. An `.mlapp`'s
  `createComponents` is the user's class code and gets none, as in R2025b.
- Tests, `AppDesignerPlainTextTests` and `AppDesignerPlainTextRunTests`:
  - the appendix line in a callback, in a block comment there, and at column 0;
  - a file whose only marker is in its code;
  - grammar lines in comments, strings and block comments: read, named on `Save` and `SaveAs`, absent
    from a clean save's status;
  - the decode table, from each row above;
  - a non-ASCII label opened from UTF-8 and from Windows-1252, and saved as UTF-8;
  - `AD_CodeName` on four components;
  - an app with both marker lines and a Windows-1252 label, run.

## Divergences

- **A grammar line R2025b takes a section by, standing in a plain-text app's code, does not stop the
  app here.** An `%[app:layout]` or `%[app:runConfiguration]` anywhere before the appendix, even in a
  comment or a string, makes R2025b's loader and run fail with `MATLAB:string:InvalidArgumentSize`
  (`getAppendixByGrammarName` hands `extractAfter` two positions). Here the app opens and runs, and
  a save names the line.
- **A code file that is not UTF-8 is read in Windows-1252 whatever the machine.** R2025b falls back
  to Windows-1252 on this machine, whose code page that is. Its fall-back on a machine with another
  code page was not measured, and is presumably that code page.

## Still open

- `fileread` here still decodes bytes that are not UTF-8 as U+FFFD (`JgsBuiltins.FileIo.cs`), where
  R2025b's `fileread` falls back to Windows-1252 as its code reader does. R2025b's `fileread` also
  turned an emoji into U+001A, its own defect. Left for an item of its own.
- `AD_CodeName` is not given to a custom component whose instance is not a graphics handle in this
  build, and its `Transient` and `Hidden` attributes are not modelled (`findprop` does not describe
  it). Not checked against a custom component in R2025b.
- The editor's own save of a plain-text app, outside the designer, writes the text without the note.
