# ADR 0257 — Dependency analysis: what a build takes along, and `requiredFilesAndProducts`

## Status

Accepted, 2026-10-23. Compiler and SDK plan, stage K2.

## Context

Every compiler target packs a program's code and data into an archive (stage K3). What goes in is
decided by a dependency analysis. In R2025b that analysis is `matlab.depfun`, with the rules in
`mcr.rdl` and the list of files the runtime leaves out in `el.win64.txt`. The same analysis answers
`matlab.codetools.requiredFilesAndProducts` in an ordinary session.

The plan's K2 section listed the rules from MathWorks' documentation. Before writing any code, four
probes measured them in R2025b (`tools/matlab-checklist/compiler-probes`):

- **`probe_k2_depend`** writes a tree (`k2_make_tree.m`) with one file per question. It asks
  `requiredFilesAndProducts` about the tree, and builds the tree with `mcc -m -C` ten ways:
  plain, without the library, `-X`, `-I`, `-a` (files, a folder, a wildcard), a second file of
  one name, `-N`, `-N -p` and an added script. It records what each archive held and the five
  log files each build wrote. Nothing built was run.
- **`probe_k2_rules`** measures 65 small cases, one main each: which literals the data rule takes,
  the pragmas' forms, variables, calls through a dot, scripts and class files.
- **`probe_k2_names`** answers two questions over every name JGraph has (the dispatch table's
  2,348):
  - which products R2025b's `requiredFilesAndProducts` says the name needs;
  - which public files `el.win64.txt` and `mcr.rdl` leave out that `which` resolves to. `mcc`
    warns about those when code names one.

Three things the documentation said turned out not to be so:
- Two files of one name do not stop a build.
- A variable assigned from a literal is followed, not only a literal.
- `%#exclude` also changes `requiredFilesAndProducts`' answer, not only `mcc`'s.

## Decision

### Where it lives

The plan put the analyser in a new `JGraph.Deploy` library. It walks JGS syntax trees, which are
internal to `JGraph.Scripting`, and a builtin calls it, so it lives there instead:
`JgsDependencyAnalyzer`.

`src/JGraph.Deploy` is created here as a headless library with no dependency on the engine.
`JGraph.Scripting` references it. It holds:
- `DeploymentRules`: R2025b's tables, `k2_names.out.txt` embedded as it was recorded, and the 25
  functions that read a data file;
- `DependencyOptions` and `DependencyReport`;
- `BuildLogs`: the five log files.

Later stages add the archive and the target writers to the same library.

### The rules (`JgsDependencyAnalyzer`)

- **What is walked.**
  - Every root is walked, along with every code file it reaches. That includes the file's local and
    nested functions, a class's property defaults, validators and methods, and its superclasses
    (`handle` and dotted names aside).
  - With `TopOnly` (`'toponly'`) only the roots are walked. The files they name are taken, but not
    walked in turn.
- **How a name resolves**, statically, in a call's order without argument classes:
  1. A local or nested function of the file. It is walked with the file.
  2. A variable. That is a name the function assigns anywhere in its body, a parameter, an output,
     a loop variable, a `global`, a `persistent` or a `catch` variable. A nested function shares its
     parents' variables, and a script's are what the script assigns. So `fn(1); fn = 3;` and
     `fa = 1; fa(1)` name no file.
  3. The file's `private/` folder.
  4. A file in the current folder, the `-I` folders, then the search path. A folder's `.mlapp`
     comes before its `.m`, and the name is matched case-sensitively (`FC(1)` does not reach `fc.m`).
  5. The built-ins, which the runtime ships, so they bring products and no file.
- **Things that name nothing:**
  - A handle `@f` is never a variable.
  - A call through a dot on a variable (`o.fi(1)`), a field read, a name only in a string
    (`feval('f')`, `str2func`, `eval`, `cellfun('f', …)`) and a dotted name.
  - A name nothing answers, which R2025b leaves out silently too.
- **The roots' own folders are not searched.**
  - Asked about a file off the path from another folder, R2025b finds only its `private/` files,
    not its neighbours, and not its `.fig`.
  - `DependencyOptions.SearchRootFolders` turns the search on for the stages that build a main;
    K6 decides whether `mcc` uses it.
- **The `.fig` beside a code file.** `mcr.rdl`'s Insert rule takes it beside any `.m` the build
  takes, when the `.m`'s folder is one names are looked for in.
- **Data files.**
  - A call to one of the 25 readers, whatever the argument position, takes the file a literal
    names, or the literal a variable was last assigned before the call (`fn = 'd.txt'; fn =
    'd.json'; fileread(fn)` takes `d.json`).
  - The name must be bare, with no folder: `'sub/s.csv'`, `'sub\s.csv'` and an absolute path take
    nothing. It must not be a code file's name (`fileread('fb.m')` takes nothing), and it is found
    where `which` would find it.
  - `load` of a name with no extension takes its `.mat`.
  - A cell literal, a concatenation, and a reader that is not one of the 25 (`exist`, `dir`,
    `openfig`, `writematrix`) take nothing.
  - `-X` (`DetectDataFiles = false`) turns the rule off; the `.fig` rule stays.
- **Pragmas.** The lexer now hands back every `%#word` comment. A pragma counts wherever it stands:
  indented, after code, in a subfunction, in a script. `% #function` is an ordinary comment.
  - `%#function a b` and `%#function a, b` take functions, or data files written with their
    extension. A name written with `.m` takes nothing (measured).
  - `%#exclude` drops a function, or a data file named with its extension, from everything that
    file or a later one names. A name written with `.m` drops nothing. A root and an `-a` file are
    taken whatever it says.
- **`-a`** (`AdditionalFiles`). A file is taken, relative to the current folder. A folder is taken
  whole, with every folder under it. A wildcard takes one folder's matches. Code taken this way is
  walked as a root's is.
- **`-N` and `-p`.**
  - `-N` takes the toolbox folders off the path. A built-in whose R2025b file is under a toolbox
    other than MATLAB's (`butter`, under `signal`) is then unresolved, with reason `mcc_flag`, and
    brings no product.
  - `-p signal` puts that folder back.
  - User folders on the path stay, as they did in R2025b.
- **The answer.** Files come in order of their full paths. Products come MATLAB first, then as
  the walk met them. Each non-deployable function the code names is listed once, in the order it
  was first named (`edit`, then `help`).

### The builtins

- **`matlab.codetools.requiredFilesAndProducts(files, 'toponly')`**, registered under its full
  dotted name. Its answers, from the fixture `k2_required_files`:
  - `files` is a char row, a string array, or a cell of char rows and string scalars. Each entry is
    resolved as `which` and `exist` would resolve it: a file or folder as written, relative to the
    current folder; a code file on the path with or without its extension; or a file of that name
    on the path.
  - A folder brings every file under it. A `.fig` brings the code file of its name beside it.
  - A built-in brings its products and no file (`'butter'` gives MATLAB and Signal Processing
    Toolbox).
  - The files are a 1-by-n cell, or 0-by-0 when there are none.
  - The products are a 1-by-m struct of `Name` (char), `Version` (char, `'25.2'`), `ProductNumber`
    (double) and `Certain` (logical, true).
  - `'toponly'` is matched without regard to case.
  - Its refusals are R2025b's:
    - `MATLAB:depfun:req:NameNotFound`;
    - `…requiredFilesAndProducts:invalidType` for a type that is not text or a cell;
    - `MATLAB:requiredFilesAndProducts:NameMustBeChar` with the bad elements numbered;
    - `MATLAB:requiredFilesAndProducts:BadStringFlag`;
    - `MATLAB:narginchk:notEnoughInputs` and `MATLAB:narginchk:tooManyInputs`;
    - `MATLAB:TooManyOutputs`.
- **`compiler.codetools.deployableSupportPackages(files)`** answers a 0-by-0 string, because
  JGraph has no support packages. A name it cannot find is refused as
  `Compiler:utils:shared:cannotFindFile`.

### The log files (`BuildLogs`)

A build will write five files beside its target. They are laid out byte for byte as R2025b's mcc
writes them:

- **`mccExcludedFiles.log`**: two header lines, then one line per non-deployable function, with
  `MATLAB:depfun:req:ExcludedBy` and R2025b's reason, the rule column empty, and line feeds. A
  `%#exclude` is not logged, as R2025b does not log it.
- **`unresolvedSymbols.txt`**: path, symbol and reason, with CR LF line ends.
- **`requiredMCRProducts.txt`**: the numbers, each followed by a tab, with no line end.
- **`includedSupportPackages.txt`**: empty.
- **`buildresult.json`**: R2025b's tabs and CR LF.

`mcc` and `compiler.build.*` call them in K6.

## Consequences

- K3's archive takes `DependencyReport.Files`. K6's `mcc -m` and `compiler.build.*` build the
  options from their flags and call `BuildLogs.WriteAll`. They also print the
  `Compiler:compiler:COM_WARN_EXCLUDED_FILE` warning for each `Excluded` entry, in R2025b's words
  (recorded in `k2_mcc.out.txt`).
- `Parser.Parse(source, sourceId, dialect, pragmas)` and `Lexer.Tokenize(…, pragmas)` are where
  any later reader of `%#` comments starts. That includes `%#ok`, `%#codegen` and the editor's
  checks.
- The R2025b names table is the dispatch table's names. A name added to the catalog later counts as
  MATLAB's until `probe_k2_names` is run again.
- **Not followed:** packages (`+pkg`), `@Class` folders and imports, as JGraph does not have them
  (ADR 0149, plan item K-P1). A program that uses them fails to build here, as it fails to run.

## Measured

- **The probes' JGraph runs.** `probe_k2_depend`'s `requiredFilesAndProducts` part and
  `probe_k2_rules` were run here under `jgraph -batch`.
  - All 65 rule cases print R2025b's lines.
  - The first probe matches but for one character, a space in `deployableSupportPackages`' message,
    which was fixed.
- **`mcc`'s archives.** `DependencyAnalyzerTests` (19) builds the tree with each of the ten flag
  sets and compares against the files R2025b's archives held. The tests also cover:
  - the reasons, the order, `TopOnly`, a file off the path, case, and a file that does not parse;
  - the lexer's pragmas, the tables, and the five files' bytes.
- **The fixture** `k2_required_files` (45 lines, recorded in R2025b) passes in both
  representations.
- **The sweep.** Dispatch sweep 19 found a package-folder file shadowing both names under every
  pattern. Their nargout rows are in `nargout-r2025b.tsv`; `exist` gives 0 for a dotted name, so no
  output-count row is generated.

## Divergences

- **The excluded-files log and the warning name the function, not a file.** R2025b names its file
  under its own install (`…/toolbox/matlab/codetools/edit.m`). JGraph's built-ins have no file, so
  the log names `edit`. The report keeps R2025b's file as `R2025bFile`.
- **One runtime product.** R2025b's `requiredMCRProducts.txt` and `buildresult.json` list its
  runtime's add-ons for what the code uses (35000, 35002, 35003, 35010, 35108 for a program that
  calls `butter` and `readtable`). JGraph's runtime is one install, so a build lists 35010 alone,
  named `JGraph Runtime`.
- **`mcrcachedir` is not reported as non-deployable.** R2025b's mcc leaves its MEX file out and
  warns, yet its runtime answers the name, as JGraph's deployed session does (ADR 0256).
- **A name JGraph has and R2025b does not needs MATLAB alone.** `requiredFilesAndProducts` lists no
  product for it beyond MATLAB, where R2025b would refuse the name or list the product it ships it in.

## Still open

- `mcc`, `compiler.build.*` and the warning lines are stage K6.
- Whether `mcc` searches the main's own folder when it is built from another folder is K6's to
  measure (`SearchRootFolders`).
