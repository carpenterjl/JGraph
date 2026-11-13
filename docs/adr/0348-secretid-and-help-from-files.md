# ADR 0348 — secretID is a class here, and help reads a code file's comments, a P-file's from its source

## Status

Accepted, 2026-11-12. Open items 173 and 174, both left by the compiler and SDK plan's stage K3b
(ADR 0259).

## Context

**secretID.** R2025b's secret API has a value class beside its seven functions,
`toolbox/matlab/authnz/secretapis/publicBuiltins/secretID.m` (read in place). It is Sealed and inherits
`matlab.mixin.CustomDisplay` and `matlab.mixin.CustomCompactDisplayProvider`. It has one property,
`Name (1,1) string`, which only the class may set. Its constructor checks its argument with
`validateattributes(name, ["string", "char"], ["scalartext", "nonempty"])`. Its `getSecret` method
calls `getSecret(obj.Name)`. Three public display methods (`annotation`,
`compactRepresentationForSingleLine`, `compactRepresentationForColumn`) give its compact display in a
struct field or a cell. K3b built the functions and not the class, so `secretID('x')` was an undefined
function. K3b's probes recorded `class`, `Name`, `disp` and four refusals in a session, and the
environment's value in a compiled program (`k3b_secrets_run`).

Probes `probe_k_secret` and `probe_k_round2` (scratch, R2025b 25.2 under `-batch -noFigureWindows`)
measured about 120 cases more. The ones that settle the design:
- **The checks run in validateattributes' order.** First the class: char or string, where a cell of
  char passes as text (`{}` and `{'a'}` pass, `{1}` does not) and anything else is `MATLAB:invalidType`
  naming its class. Then `scalartext`: a char row, `''` or a string scalar that is not missing
  (`MATLAB:expectedScalartext`). Last `nonempty`: an empty char fails (`MATLAB:expectedNonempty`), but
  `""` is a 1-by-1 string and is taken.
- **Arrays are allowed.** `[s t]`, `[s; t]`, `repmat`, `secretID.empty` and indexing all work. Growing
  one (`a(3) = t`) calls the constructor with no argument, which is `MATLAB:minrhs`.
- **getSecret on an array** hands every name over as its own argument, since `obj.Name` is a list.
  Two are `MATLAB:TooManyInputs`, and none (the empty array) is `MATLAB:minrhs` with getSecret's own
  "requires 1 more input(s)".
- **No operators, no conversions.** `==` and `~=` are `MATLAB:math:mustBeNumericCharOrLogical`.
  `string`, `char`, `cellstr`, `double` and `sprintf('%s')` are `MATLAB:invalidConversion`. `sort` is
  undefined for the class. `isequal` compares the names, so a char and a string make equal objects.
- **The display** is the default one with the name in quotes: `secretID with properties:` and
  `Name: "x"`. An array lists the property names, as every array of objects does.

**help.** The packages plan's stage P1 (ADR 0307) taught `help` a code file's first comment block, and
P2 and P4 taught it the no-help forms of a class, a method and a class folder. Its rules for where the
block is were a guess, and several were wrong. Probes `probe_k_help` and `probe_k_help2` measured about
90 files and names against R2025b. They found:
- **Where the block is.** It is the comments at the top of the file. If the file starts with a function
  or classdef line instead, it is the comments under that line, past the header's continued lines and
  past any blank lines (JGraph looked only at the next line). A script whose first line is code has no
  help, even with comments after it (JGraph took them).
- **Where the block ends.** A blank line, or a line of spaces, ends it.
- **How a line reads.** Each line's leading `%` is read as a space. `%%` counts as one `%`, so `%%two`
  is " two" and `%%%three` is " %three". `%{` is a line reading " {", and the next line ends the block.
  An empty comment line inside the block stays; one at its end is dropped.
- **A class's block** is followed by the "Documentation for C" and "doc C" lines, as its no-help form
  already was. So is a block written before `classdef`.
- **A function with no help and no arguments** is "f is a function." with no header line. The header
  would only repeat the name.
- **Members.** A method's help is the block under its function line. The constructor's is followed by
  "Documentation for C/C" and "doc C". `C.m` and `C/m` both reach a method of a class file, where
  JGraph reached only a package's class or a class folder's method file. A property's help is the
  block right above its line, else " P - " and the comment at the end of its line, else "C/P is a
  property.".
- **Other names.** `f>g` is a local function's block, or "g is a local function." with its header.
  `f.m` and `f.p` are the file's help. `folder/f` and `folder/f.m` reach a file in a folder on the
  path. A name in the wrong case (`help HFLOCAL`) is "--- HFLOCAL not found. Showing help for hflocal
  instead. ---", a blank line, and the file's help.
- **`help folder`** answers the folder's `Contents.m` help if it has one. Otherwise it is "Contents of
  folder:", a blank line, then one line per code file. Each line is the file's H1 line with the file's
  own name left off its start (any case), or "is a function." (script, class). The names are
  right-aligned to the longest and sorted by character code. A folder with no code file is "folder is
  a folder.".
- **A P-file.** R2025b's `help` reads the `.m` beside a P-file. With no source, it answers "f is a
  function." (no header), "f is a script." or "f is a class." with the documentation lines.
- **A user file shadows a builtin's help.** An `alpha.m` on the path answers `help alpha`.
- **Not found.** `h = help('nosuch')` is a 0-by-0 char. Printed, it is "nosuch not found.", a blank line,
  "    Search the documentation for nosuch" and "       docsearch nosuch". JGraph answered "No help
  found for 'nosuch'." both ways.
- **Printed help** is the returned text and one blank line after it. In `-batch` R2025b's links print
  as their text. Only `evalc` keeps the hotlinks there, and it also bolds an H1 line's name in lower
  case. JGraph trimmed the text's last newlines and printed no blank line.

## Decision

### secretID (`JgsBuiltinClasses.SecretId`)

The class is declared in C#, as the compiler's option classes are. It is a value class, Sealed, with
the property `Name` typed string and `SetAccess` private. Its constructor is native and calls
`JgsBuiltins.SecretIdName`, which makes the checks above in R2025b's order and words.
`getSecret` is native too and calls `JgsBuiltins.SecretOfIds`. That takes the names of every element
and hands them to the secret function, so its own argument checks give R2025b's errors. In an
ordinary session the answer is VaultNotFound. In a deployed program it is the environment's value,
because the function already reads it there. `annotation` answers R2025b's column of "secretID". The
two compact-representation methods are listed and refuse when called, since only MATLAB's display
machinery passes them the configuration object they need.

`secretID` is registered as a builtin (`RegisterSecretIdBuiltin`, among the interpreter's builtins)
that constructs the class. `ClassNamed` now falls back to the classes this build supplies, so
`secretID.empty` reaches the class. secretID is the one such class with a plain name.

R2025b's two mixins are not classes here. `JgsBuiltinClasses.ShownSuperclassesOf` names them for
secretID, and `JgsClass` adds them to the names `superclasses` and `isa` answer. Nothing is inherited
from them.

An object's display now shows a string scalar property in double quotes, as R2025b does for every
class. `Name: "x"` is the first such property the fixtures display.

### help reads the file (`JgsBuiltins.Packages`)

`FileHelp` follows the rules above:
- `SkipBlankLines`, `FirstCodeLine` and `BlockAfterDeclaration` find the block;
- `HelpBlock` reads it;
- `HeaderText` and `CodePart` give a header with its continued lines joined and its comment dropped.

A class file's block gains the documentation lines. `MethodHelp` reads a method's block and answers
for properties (`PropertyHelp`). `UserHelp` routes the new forms:
- `C.m` and `C/m` for a class file;
- `f>g` (`LocalFunctionHelp`);
- `f.m` and `f.p`;
- `folder/f` and `folder/f.m`;
- a plain name found in another case, with R2025b's line before it (`DiskName` reads the file's own
  spelling);
- a folder last (`FolderHelp`).

`PcodeHelp` answers for a P-file. `KindOfFile` reads a P-file's first statement to say what it is.

### help prints as R2025b's -batch does

In the MATLAB dialect:
- a code file on the path is asked before the catalog, so a user file shadows a builtin's help;
- a name nothing answers returns empty text, and printed, the not-found lines;
- printed help is the text and a blank line.

Built-ins keep answering the catalog's text. The JGS dialect keeps "No help found for 'x'." and its
trimmed printing.

## Consequences

- `secretID` works in a session and in a deployed program. It has R2025b's class answers, refusals,
  arrays, equality, save and load, and getSecret.
- `help` answers every user code file as R2025b does. The exceptions are hotlinks and the cases under
  Still open.
- `MatlabEnvironmentBuiltinTests.Help_ShowsACatalogEntry_AndKnowsWhatItDoesNotKnow` now expects
  R2025b's "nonsensename not found." where it expected "No help found".
- New parity fixture `oi173_secretid_help` (183 rows from R2025b 25.2, one div row). It covers about 95
  secretID cases and about 60 help names and printed forms.
- `DeployedSessionTests.SecretId_ADeployedProgramReadsItsSecretFromTheEnvironment` covers a compiled
  program reading the environment through secretID, a missing name, and an array of two.
- Catalog entry `secretID`, EvalBuiltinNames, dispatch sweep 43k (`dispatch_probe43k.m`, every pattern
  goes to the shadowing file), `nargout-r2025b.tsv` (1, a file) and the regenerated tables. secretID is
  newer than the 2,024 names, so the coverage count does not move.

## Divergences

- **`secretID(obj)` with a secretID argument is `MATLAB:minrhs` here.** R2025b's constructor runs with
  the object as its name and refuses it as `MATLAB:invalidType`. Here the call goes to the object's
  class first, and the constructor, as a method, gets the object as the instance being made.
  Fixture row `sid_object`.

## Not measured

- A compiled R2025b program was not run for this ADR. The deployed answer rests on K3b's
  `k3b_secrets_run` recording (`secretID from-environment`, and SecretValueNotFound without the
  variable).
- `help` in the desktop's Command Window, where R2025b prints hotlinks. Only `-batch` and `evalc` were
  measured.

## Still open

- **secretID's compact display**, in a struct field (`x: kname (secretID)`) or a cell
  (`{[kname] (secretID)}`). This build's struct and cell displays do not ask a class for one.
- **`exist('secretID')` is 5 here and 2 in R2025b, and `which` says "built-in function".** R2025b's is a
  file in the toolbox. `nargin('secretID')` is -1 here and 1 there.
- **`isvalid` of a value object** has no identifier here. R2025b's is `MATLAB:UndefinedFunction`.
- **`help` with two names** answers empty text in R2025b. Here it is refused.
- **A property with both a comment above it and one at its end** was not measured. The block above is
  taken.
