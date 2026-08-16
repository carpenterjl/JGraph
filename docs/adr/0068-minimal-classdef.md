# ADR 0068 — Minimal classdef

## Status

Accepted (M68). Last milestone of the M61–M68 arc.

## Context

M61 through M67 rebuilt the language a ported MATLAB script actually needs: comma-separated lists,
function files on a path, string arrays, time, struct arrays, numerics, living graphics. What was
left was the one construct that consumes all of them — `classdef`, which the parser had refused by
name since M28 with "class definitions are not supported".

It was planned last on purpose. A class file lives on the search path (M62), its properties are
declared with the grammar of an `arguments` block (M62), a method hands back several outputs through
`varargout` (M61), an overloaded operator dispatches from the shim time arithmetic generalized (M64),
and an exception's stack is a struct array (M65). Doing it earlier would have meant building each of
those twice.

The characteristic failure of a class system is a *convincing shell*: `classdef` parses, an object
prints, and then the first script that asks a real question — is this a struct? does this property
refuse what it said it would? does assigning to one name change the other? — gets an answer that was
never thought about.

## Decision

### A property is an argument with a different home

MATLAB writes a property line and an `arguments` line with the same grammar — `name (dims) Class
{validators} = default` — and means the same thing by both. So they share a parser (`ParseArgumentSpec`
serves both) and, more usefully, a checker: `JgsBuiltins.CheckArgument` enforces a property's declared
size and class, and the same `mustBe…` validators run on a property write that would run on an
argument. Every validator written for a function was already written for a class.

The check runs on **every write and on the defaults at construction**, not once at declaration. A
property whose declaration is only honoured the first time is a declaration that stops being true.

### The class name means the constructor

What the search path hands back for a class file is the constructor — an ordinary callable, with
`AutoCallsBare` set so `c = Circle;` builds a default instance. The interpreter therefore never
learned a new kind of callee: `Circle(2)` is a call, resolved the way `helper(2)` is.

A constructor's declared output starts out **fully defaulted**, which is what lets a constructor set
two properties and leave the rest alone — MATLAB's own rule, and the reason the constructor is run
here rather than through the ordinary `UserFunction` path.

### A method reached through the dot is the object's method already in hand

`obj.area` evaluates to a `BoundMethod` — the method with the receiver at the front of its arguments.
That one representation makes `obj.area`, `obj.area()`, `area(obj)` and `[a, b] = obj.extent()` all
the same call written four ways, with no special case in the multi-output path beyond asking the same
question there.

`f(obj, …)` dispatches on the class of the first argument **before the name is looked up at all**,
because a class method has to beat a builtin: `area(c)` on a Circle is the class's own method and not
the chart verb. The guard is three cheap checks — some class is loaded, the callee is a plain name,
the name is not a variable holding data — so a script that defines no classes fails the first of them
and pays nothing.

A class's methods also see **each other by bare name**, which is MATLAB's rule and not a convenience:
a helper like `value(x)` inside `plus` has to work for a plain number too, and dispatch cannot help
there because a plain number belongs to no class.

### A handle class is one line

`classdef Name < handle` gives reference semantics by being the one thing `CopyForBinding` does not
copy. That is the rule M64 stated for `containers.Map` against `dictionary` and wrote down as the one
M68 would need. Nothing else in the object model knows which kind it is holding.

### The class decides its operators, and refusing is deciding

An operand that is an instance dispatches to the named method MATLAB names for that operator
(`plus`, `mtimes`, `eq`, `uminus`, …), resolved before any numeric reading of the operands — the same
lesson M63 and M64 each learnt one branch lower down. When the class has **not** defined one, the
operator is refused **by name**: "`*` is not defined for Money; give the class a `mtimes` method to
define it." Falling through would have handed the object to the numeric machinery, which would have
complained about arrays.

Display works the same way: a class that defines `disp` is asked by `disp(obj)` *and* by the echo of a
bare `obj`, so the two cannot disagree. A class that does not gets its name and its properties.

### MException stays a tagged struct — the plan for this was re-checked, not followed

The plan called for turning MException into a real `JgsType.Object`. Checking what that would buy
before doing it showed the answer was one predicate. `class(ME)`, `isa(ME, 'MException')`, every field
read, `throw` and `rethrow` already answer as an object, and `ME.stack` became a true struct array the
day M65 made struct arrays real — a second recorded plan item that had quietly completed itself.

The one thing that answered wrongly was **`isstruct(ME)`, which said true**. That is fixed as a rule
about tagged values rather than about MException: a struct carrying a class name is not a plain
struct. One line, and it corrects `containers.Map`, `dictionary` and the spatial-reference types at
the same time. Converting MException instead would have routed the error path — the one that runs
when something has already gone wrong — through brand-new machinery to win that single predicate.
`addCause` is added, and answers a *new* exception rather than writing into the old one, because an
MException is a value here.

### `classdef`, `properties` and `methods` are not lexer keywords

They are recognised only where they can appear — `classdef` at the start of a statement followed by a
name, `properties` and `methods` only inside a class body. That is what keeps `properties` and
`methods` available as the names of the two builtins that ask an object what it has, and it follows
the rule `persistent` and `arguments` already used. The cost is that `classdef` does not highlight as
a keyword in the editor, which is recorded rather than fixed.

## Consequences

**Five new names** — `isobject`, `properties`, `methods`, `metaclass`, `addCause` — plus the
`classdef` construct itself. The builtin table moves to **413 of 514** (`metaclass` is the only one
of the five MATLAB documents as kind *builtin*), and the across-every-kind total to **926 of 2,027**.
Three of those seven are a correction rather than new work: `classdef`, `persistent` and `arguments`
are documented *keywords* that the checklist tool never counted, because they are deliberately not
lexer keywords here and so never reach the catalog. `persistent` has been implemented since M41 and
`arguments` since M62, and both went uncounted the whole time.

**What the milestone found that was not its own.** Three things, and all three were silent:

- **A function's locals could reach into its caller.** `TryAssign` walked the scope chain outward, so
  a helper assigning `x` found and overwrote the *script's* `x` whenever the two happened to share a
  name — a MATLAB function has a workspace of its own, and this one did not. It was invisible until
  the names collided, and M68 makes collisions ordinary: a method's locals are short words like
  `by`, `n`, `obj`. The fix is a call boundary the walk stops at, with two deliberate exceptions —
  a *nested* function shares its parent's variables by design, and JGS is a lexically-scoped language
  whose closures write to what they captured, so the boundary is MATLAB's alone and the frozen JGS
  surface is untouched.
- **An empty cell left a phantom element behind.** `[{}, {x}]` came back 1-by-**2**, because the
  block measurement asked only whether a piece was a numeric array and read every cell as one
  element whatever its size. Two 1-by-1 cells joined correctly by accident; everything else was
  wrong, and the case that mattered is `acc = [acc, {value}]` — the ordinary way a MATLAB script
  grows a cell from nothing. Cells now concatenate as containers, on the rules M65 established for
  struct arrays, and joining a cell with a non-cell is refused as MATLAB refuses it.
- **`ME.stack` was already a struct array**, and the plan still listed making it one.

**Deliberately excluded**, each because it would need machinery this milestone would then owe tests
and documentation for: events and listeners; `Dependent` and any `Access` beyond public; `Abstract`
and `Sealed`; user superclasses (only `handle`); packages and `+dirs`; `subsref`/`subsasgn`
overloading — the interpreter's indexing pipeline stays authoritative, which is why the four
subscript-customization builtins remain in the missing table; enumerations; and property `set`/`get`
methods.

**Recorded limits.** A class file holds one class and is named after it. An object is a scalar: there
are no object *arrays*, so `objs(2)` and `[a b]` on instances are not indexing and concatenation of
them. Objects are not saved to `.graph` or to MAT files. `obj.Log{end+1} = x` does not grow a cell
held by a property — the same limitation a struct field has, which the stress script works around
with `obj.Log = [obj.Log, {x}]`. A method's own nested functions are parsed as nested rather than as
further methods. `classdef` does not highlight as a keyword in the editor.

**Deliberate test flip.** `MatlabParserTests.UnsupportedConstructs_AreNamedInTheError` asserted that
`classdef Widget` raised a syntax error naming the word. It now asserts the same of `spmd`, and
`classdef`'s own parsing is covered by `MatlabClassdefTests`.

**Live checks for the user**, which batch cannot see:

- Open a class file in the Script Workspace and confirm the editor is usable — the body highlights as
  ordinary MATLAB, and `classdef`, `properties` and `methods` show as plain identifiers rather than
  keywords, which is the recorded limit above.
- Type `obj.` in the editor with an instance in scope and confirm completion offers something
  sensible rather than nothing (the completion engine has not been taught about classes; this is a
  look at how it currently behaves, not a claim that it should).
- Run `stess_40.m` with F5 and confirm section 15's figure draws the tank levels.
