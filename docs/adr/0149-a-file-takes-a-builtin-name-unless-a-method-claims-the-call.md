# ADR 0149 — A file takes a built-in's name unless a method of the built-in claims the call

## Status

Accepted (M145). Closes the first divergence of ADR 0062.

## Context

ADR 0062 put the built-ins ahead of the files: a `max.m` in the working folder lost to the real
`max`, on the argument that a half-finished file silently changing what every script in the folder
computes is the worse failure. It was a deliberate divergence, pinned by a test and by a stress
section, and it held for eighty-odd milestones.

Two things ended it. Scripts written for MATLAB ship helpers under built-in names — a `builtin.m`
in a folder of the user's own broken scripts was the case that opened the milestone — and every one
of them ran differently here. And the rule MATLAB actually follows turned out to be neither "the
file wins" nor "the built-in wins". Seven probe scripts, two systematic sweeps and a step-0 run
against R2025b (`tools/matlab-checklist/precedence-probes`) measured it, and every row of what
follows is one of their answers.

**The built-in's class methods sit above the files, and the rest of it sits below.** `max([1 5 3])`
with `max.m` in the current folder is 5, because `@double/max` is a method and a method of an
argument's class outranks a file; `max({1})` with the same file is the file, because no method of
`max` takes a cell. `mean([1 2 3])` with `mean.m` present is the file, because MATLAB's `mean` is a
`.m` in `datafun` and not a method of anything. Which method applies is per name, per argument class
and per position: a string in second position blocks `numel`'s built-in and not `plus`'s; a
`containers.Map` claims `keys(m)` and not `keys(1, m)`; `datetime` claims `minus` in either position
while a `table` loses `height` to a current-folder file. No rule reproduces that. A table does.

**`private/` splits the built-in methods from the user's.** A built-in class method beats a private
function, and a private function beats a method of a user's `classdef` — the two kinds of method sit
on opposite sides of the private folder, which the documentation's ordered list does not say.

**A handle captures the file that answered, and the built-in's method inside it.** `hm = @max`
taken beside `max.m`, then `cd` elsewhere: `hm({1})` is the file and `hm([1 5 3])` is the built-in,
and `cellfun(hm, {{1}, [1 5 3]})` is `[-999 5]`. An anonymous body, by contrast, asks the current
folder *when it is called*: `f = @() max({1})` answers the file beside it and the built-in after a
`cd` away.

**`builtin` is an ordinary built-in and is shadowed like any other.** A `builtin.m` in the current
folder takes `builtin('max', …)`, `feval('builtin', …)` and `@builtin`; MATLAB's own library reaches
`builtin` through the name table, so the shadow reached its graphics and message services and
R2025b died in an access violation. What is true of `builtin` is only that, while unshadowed, it
skips every layer above the built-ins.

**`evalin` changes the workspace and nothing else.** Text evaluated in the caller's workspace
resolves its function names from the file of the function that called `evalin`, never from the
target's file. A property default evaluates in a private, writable, discarded workspace that is not
a stack frame. An anonymous body is a stack frame, named by its text and its file, and it is static:
`assignin('caller', …)` into it errors with *Attempt to add "seen" to a static workspace*.

**A miss looks at the disk.** A file written by `fopen` on one pass of a loop is called on the next.
A loaded file that is deleted mid-run errors with *Previously accessible file … is now inaccessible*
rather than falling through.

Against that, JGraph resolved a name as variables, then the script's own functions, then the
built-ins, then the path, with class dispatch as a pre-check before any of it; a path function
closed over the script's workspace and read its variables and its local functions; a called
script's `helper` overwrote the caller's for good; and a user method beat a bound name, a nested
function and a private file alike. Twelve sites in the interpreter each decided an order of their
own.

## Decision

### One resolver decides every name, in nine layers

`JgsNameResolver` owns the order and every site asks it: the expression, statement and multi-output
call paths, the bare name, `@name`, `str2func`, `feval` by text, `which`, `exist`, `nargin(name)`
and both JIT guards. It has four modes, because the sites ask four questions — **Value** for a bare
name, **Invoke** for a call with arguments, **Handle** for `@name` (from layer 2, so `max = 7; @max`
is the function), **Query** for the tools (every layer holding the name, in order) — and each
answers a `Resolution` naming the layer that answered, so `which` and `exist` read the layer and
never re-derive it.

| # | Layer | What answers |
|---|---|---|
| 1 | bound names | `LookUp`'s rule: the global workspace when a `global` declaration reaches the frame, else the environment walk, accepting only non-function bindings |
| 2 | nested functions | the call frame's own declarations |
| 3 | local functions | the current file's storage |
| 4 | built-in class methods | the dispatch table says the built-in answers this call |
| 5 | private | `<current file's folder>/private/<name>.m`, visible only from that folder's files |
| 6 | user class methods | dispatch on the dominant `classdef` object; a class without the method falls through |
| 7 | current folder | the working directory, then the running script's folder, then the workspace root — JGraph's implicit path, which MATLAB does not have |
| 8 | path | the `addpath` folders in order |
| 9 | built-ins | the sealed layer, last |

Invoke mode runs in two phases. Phase one looks the name up with no argument evaluated; a bound
value goes to the existing classification of what parentheses mean for it — an array is indexed
with its *unevaluated* arguments so `A(end)` and `C(:)` keep their index context, a handle in a
variable is called through its own callable — and only an unbound name evaluates its arguments and
runs layers 2 to 9 with their classes. Class dispatch stopped being a pre-check and became layer 6,
which is what puts a user method below a bound name, a local function and a private file.

### Which method applies is a table, not a rule

`tools/matlab-checklist/matlab-r2025b-dispatch.csv` holds R2025b's verdict for **2,159 names under
37 argument patterns** — every single-argument class the sweep could construct, every ordered pair
of a double with a high class and back, the string and cell mixes, and the two three-argument
forms — measured by putting a file of each name beside a call of each pattern and reading which
answered. `JgsDispatchTable.KeepsBuiltin` is a bit-set lookup: the first argument's class and the
first high class after it name a column, a low first class (`int8`, `logical`, `char`) with an
unmeasured pair reads the double pairing, and a high first class with an unmeasured pair reads its
own column alone. `verify-method-classes.py` refuses a row the sweep did not produce and re-checks
every verdict the plan's tables cite against the lookup; `dispatch-class-map.csv` maps every answer
`ClassOf` can give onto the pattern vocabulary, with `struct` as the fallback column.

Six names could not be measured by the first harness because the harness reached its own tools
through them, and one of them was `size`, the most-called name in the language, whose default row
would have said "file". A second harness that reaches its tools through `builtin()` measured all
six: `fopen`, `fprintf`, `fclose`, `feval` and `rehash` go to the file under every pattern, and
`size` keeps the built-in under twenty-two of the thirty-seven. No unmeasured row remains.

### The built-ins are a sealed layer under the workspace

`JgsBuiltinLayer` is a root environment flagged `IsBuiltinLayer`, and the base workspace is its
child. Every registrar declares into it through one `Register` seam — the operator functions, the
run, eval, session, save/load, workspace and path registrars, the REPL's copies — and after the
last one the layer is sealed: `Declare` on it throws, which is what stops a debugger prompt or a
`global` statement from landing a user binding in built-in storage by walking too far. `TryAssign`
and `DeclareGlobal` stop at the layer, so `max = 7` declares in the base workspace and `clear max`
forgets it there with no snapshot to restore; the base workspace is an explicit reference
(`Interpreter.Globals`) and is never found by walking. `builtin()` and layer 9 read the same layer,
so there is no second table to drift from it.

### Function storage lives with the file, and the file is an explicit context

`FunctionFile` holds a source's file-level functions and its load generation — the main script,
each path file, each private file, each script run by name has one — and `UserFunction` carries a
reference to its file. Nested functions do not move: they are declared into the call frame per
invocation, as before. The running file is its own piece of interpreter state, pushed by a scoped
`EnterFile` at every entry that evaluates code from a file — a function call, a script run by
name, an anonymous body (as a pair with the body's own workspace), a class default — and read by
the resolver and by nothing else.

A path file's scope, and the scope the main script's own functions close over, is parented to the
builtin layer and not to the script's workspace. That cut is what removes four leaks at once, each
a behaviour change made on purpose and each a test: a path function no longer reads the calling
script's variables or its local functions; a local function of the script no longer reads the
script's variables; a script run from inside a function no longer has its local functions behave as
nested functions of the caller; and two scripts' same-named helpers coexist, because each lives in
its own file's storage. Globals, persistents and a called script's access to the caller's workspace
are untouched, and are tested separately so the cut cannot widen.

### A handle captures a layer and a target, and shares the resolver's dispatch

`@name` produces a `NamedHandle` holding the name, the layer that answered at creation, what it
answered (a local function, a loaded file's callable, or nothing when only the built-in existed)
and the file's generation. On every invocation — direct, `cellfun`, `feval`, a callback — the
handle runs the resolver's own layer walk with its captures standing in for the layers it
captured, so `hm({1})` after `cd` is the captured file and `hm([1 5 3])` is the built-in's method,
and `h = @sum; h(o)` agrees with a written `sum(o)`. Equality is structural over the four fields —
`@max` inside `max.m` and `@max` beside it are different values with different behaviour — and
nothing is interned, so no table roots a discarded closure.

An anonymous body does not capture a built-in. `AnonymousFunction.Create` snapshots the body's
free names from the defining workspace, and until the stress scripts ran, that walk reached the
builtin layer and made `max` a local function of every `@() max({1})`; the body now leaves a
built-in-layer name unbound, so its own walk reaches the layer when the handle is called and the
folders are asked then, which is what R2025b does.

### The file index answers "is this built-in shadowed" without touching the disk

With the folders ahead of the built-ins, Invoke mode runs on the way to every built-in call.
`JgsFileIndex` keeps, per folder it knows, the `.m` stems and the folder's write time, and derives
one shadowing set: stems that are also built-in names. A built-in call pays one lookup in a set that
is empty in almost every session. A name that is neither a built-in nor indexed still probes the
disk, which is what makes "written on one pass, called on the next" hold with no refresh at all.
Every file mutation the interpreter performs invalidates the index through the one seam every
writer resolves through (`ResolveForWrite`, plus the mutators that do not write: `delete`,
`movefile`, `copyfile`, `mkdir`, `rmdir`, `run`); external changes are seen at the next top-level
statement; `addpath`, `rmpath`, `path`, `cd` and `rehash` invalidate immediately, so `rehash` is no
longer a no-op. An unreadable folder keeps its last set and warns once, never treating an outage as
an empty folder.

### `builtin(name, args…)` is a transparent forwarder

`JgsBuiltinForwarder` is registered like any other name — a `builtin.m` or a variable shadows it
exactly as in R2025b — but it is not a wrapper over a delegate. It looks the name up in the builtin
layer and never the environment, forwards the arguments as written so `builtin('class', "abc")` and
`class("abc")` agree, forwards the requested output count including zero so `[m, n] =
builtin('size', A)` and `builtin('ecdf', x);` both do what the target would, and forwards the call
site with the selector removed so `builtin('table', A, B)` names its variables. Anything not in the
layer errors with MATLAB's words and identifier, *Cannot find built-in function 'helper'*.

### `which`, `exist` and the warning

`which(name)` names the file when a file would answer, the private file included, and says
`built-in` otherwise; `which(name, '-all')`, in either argument order, lists every layer holding
the name as a cell column, files first, 0-by-0 for none. `exist(name)` is 1 for a variable, 5 when
the builtin layer holds the name whether shadowed or not, 2 for whatever the resolver would run
from a file, 7 for a folder; `exist(name, 'file')` reaches the file past a built-in. When the index
finds a stem that shadows a built-in it prints MATLAB's own sentence — *Function max has the same
name as a MATLAB built-in. We suggest you rename the function to avoid a potential name conflict.*
— through the ordinary `warning` channel, at `addpath` and at the first `cd` into the folder, once
per name per session. That keeps ADR 0062's reason — a stray `mean.m` must not fail in a way nobody
can read — while giving the file the name.

### The cost

Phase one's walk and storage miss were already paid; phase two adds a dominant-object scan (one
boolean while no class is loaded), the shadowing set's epoch compare and count check, and the
running file's private stems through the entry its storage keeps. A `while` loop the JIT cannot
compile, calling `sum` a million times, measured 3.5 % slower than the pre-milestone build over two
interleaved batches of five pairs, inside the 5 % the plan allowed. Two things were tried and kept
on the way: a per-file index entry, because the first cut hashed the folder path on every call, and
count checks before any hashing.

## Consequences

Seven new files under `JGraph.Scripting/Jgs` — `JgsNameResolver`, `JgsBuiltinLayer`,
`JgsDispatchTable`, `JgsFileIndex`, `JgsBuiltinForwarder`, `FunctionFile`, `NamedHandle` — and
eighty-nine tests across five new test files, `JgsPrecedenceTests` the largest with forty-two. The
milestone ran as eleven gated steps, each committed on `main` with the four lanes green, in the
order the plan's eight rounds of adversarial review settled: the table and its verifier, the sealed
layer, the index, the resolver under the old order, storage with the file, the flip, `builtin` and
the tools, the stress scripts, the parity fixture, this document.

`m145_precedence.m` pins **145 lines** recorded from R2025b, eight of them `div=ADR0149`. It builds
its own folders under `tempdir` and removes them, and it keeps the shadows MATLAB's own `.m` library
cannot survive — `numel`, `plus`, `mod`, `eps`, `mean` — in a folder entered only for their rows,
because `fullfile`, `mat2str` and `table` are `.m` files that call `numel`. On the first recording
every line agreed by its rule. `stess_85.m` adds twenty-two sections to the stress suite, which now
runs eighty-five scripts, and `stess_34.m`'s section 6 asserts the opposite of what it froze for
ADR 0062. Two older stress sections had frozen the leak of a script's variables into `eval` inside
a local function; R2025b agreed with the fix and both were rewritten.

**ADR 0062's first divergence is closed.** "A built-in wins a name a path file also claims" was
recorded there with the reason that a stray file silently changing a computation is worse than an
override that fails to take. The reason still stands, and the warning above is what answers it now:
the file takes the name, and the session says so in MATLAB's own words. `ABuiltinBeatsAFileOfTheSameName`
is gone and the tests that replaced it say which layer answers and why. ADR 0062's sentence calling
`rehash` an accepted no-op stopped being true with the file index: `rehash` re-reads the folders'
file lists, which is what a script needs when another program dropped a file mid-statement.

`docs/matlab-builtin-coverage.md` struck `builtin` from the OOP section, the one name the milestone
added to the catalogue; the count across every callable kind is 1,116 of 2,024.

### Divergences

- **`exist('mean')` answers 5 and `exist('mean', 'builtin')` answers 5** where MATLAB answers 2 and
  0, because `mean` — and every other name MATLAB keeps in a `.m` file — is a built-in here. A file
  named `mean.m` still takes the call; only the report differs.
- **`builtin('mean', x)` works** where MATLAB errors *Cannot find built-in function 'mean'*.
  Reaching JGraph's own implementation is the whole point of the call for a user who has shadowed
  `mean`; refusing it by consulting a kind column would be parity for its own sake.
- **`which(name, '-all')` lists the layers and not the class methods.** For `max` beside a `max.m`
  it answers two entries, the file and the built-in, where MATLAB answers twenty-seven — the file,
  the built-in, and one line per `@class` folder holding a `max`. JGraph's methods are not folders.
- **A folder added with `addpath(d, '-end')` still shadows a built-in.** In MATLAB the built-ins
  sit on the path at their toolbox folders' positions, so a folder appended after them loses;
  here the built-ins are the last layer and every path folder is ahead of them.
- **A deleted shadowing file falls through to the next layer.** `eps` after `delete('eps.m')` is
  `2.2204e-16` here and *Previously accessible file … is now inaccessible* in MATLAB. An
  interpreter that keeps a dead binding and errors on it is the stale-cache behaviour the
  correctness rule exists to prevent; a file that is merely unreadable does get MATLAB's error.
- **JGraph's library is immune to a shadow that breaks MATLAB's.** `table(1)` and `which('numel',
  '-all')` both error inside R2025b's own `.m` code under a user `numel.m`; here both answer,
  because the built-ins are C# calling C# and never reach their own helpers through the name
  table. The same immunity is why a `builtin.m` shadow cannot crash the session.
- **A shadowing file created by another process during one long statement is seen one statement
  late.** The folder's write time is re-read at most once per top-level statement; a file the
  script itself writes is seen at once through the invalidation seam.
- **Argument patterns the sweep did not measure take a documented fallback.** Two different high
  classes in one call, or a class outside the pattern vocabulary, read the first argument's column
  alone, with `struct` standing in for an unknown class.
- **JGraph's implicit path has no MATLAB counterpart.** The running script's own folder and the
  workspace root are searched after the working directory, as ADR 0062 chose; a `max.m` beside a
  script shadows here without a `cd`.
- **Imports, packages, `@Class` folders and old-style class objects are not implemented**;
  `import` is refused by name. File types other than `.m` are not read: a folder holding `x.m`
  and `x.mlx` answers `x.m` where MATLAB answers the `.mlx`.
- **`which` prints no annotation.** MATLAB's `% Shadowed`, `% Private to cur` and `(loc) % Local
  function of main` suffixes and its `built-in (path)` phrasing are not reproduced; `exist(name,
  'class')` and `which` of a variable are not answered.
- **`warning off` does not silence the shadowing warning**, because JGraph keeps no warning state;
  the message goes through the channel that will honour it when it does.
- **`func2str(@sin)` answers `'@sin'`** where MATLAB answers `'sin'` — a named handle prints
  with its `@`, an anonymous one with its text, and `MatlabSessionBuiltinTests` pins the form.
  Found by the stress scripts, older than the milestone, left as it is.

## Still open

- `run('other\folder\s.m')` runs the script without putting its folder on the implicit path, so a
  `max.m` beside such a script is not seen unless the script `cd`s there. MATLAB's `run` changes
  folder for the duration; worth its own fix.
- The head-to-head timing rows join the arc's deferred set, per the standing decision recorded
  in ADR 0131; the micro-benchmark above is the milestone's own measurement.
- The step-5 note stands as the next saving if one is ever needed: phase one's storage miss and
  phase two's shadowing read could be folded into one lookup.
