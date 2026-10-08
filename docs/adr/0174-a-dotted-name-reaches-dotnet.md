# ADR 0174 — A dotted name reaches .NET

## Status

Accepted. Stage I1 of the .NET and shared-library interop plan
(`docs/plans/net-and-shared-library-interop-plan.md`), after step 0 (41efe4a) recorded the stage's
four fixtures from R2025b before any interpreter code. One commit, this ADR's.

## Context

A MATLAB script calls .NET by naming it: `System.String('Hello')`, `System.Math.Max(3, 7)`,
`m.Describe()`, `NET.addAssembly(path)`. JGraph had no value a .NET object could live in, no way for a
dotted name to mean anything but a struct field or a class member, and no conversion between the two
type systems. Step 0 recorded what R2025b does in four fixtures owned by this stage — `net_basics`
(95 lines: the runtime, a BCL object, statics, instance members, class queries, name resolution),
`net_conversions` (1,006: every MATLAB class against 25 .NET parameter types alone and in every pair,
and every .NET return type), `net_exceptions` (85: `NET.NetException` from every kind of member) and
`net_display` (22) — against a netstandard2.0 test assembly both engines load, with R2025b pinned to
.NET 8 by `dotnetenv("core", Version="8")`, the runtime JGraph itself runs on.

## Decision

**A new value kind, `JgsType.External`.** Its payload is an `IJgsExternal`, which answers the few
questions every value must: its class name, whether it is a handle, `isa`, what a new binding holds
(itself for a handle, a copy for a value type), and its display. Three kinds implement it: `NetObject`
(any .NET instance — a boxed value type, an array, an enum member, a `System.String` — carrying the
type MATLAB reports, which for a `Nullable<T>` a member declared is the Nullable, because the runtime
boxes it as a bare `T` or null), `NetMetaClass` (`?System.String`) and `NetAssemblyValue`
(`NET.Assembly`). A kind of its own rather than `JgsType.Object`, because code that reads `Object`
goes on to read its `JgsClass`; a kind nothing knows falls into the refusal each site already has.

**The audit of every site that branches on a value's kind.** Handled explicitly now: `TypeName`,
`Display`, `ClassName`, `IsTruthy` (refuses, as R2025b's conversion to logical does), `CopyForBinding`,
`MemberOf`, `AssignToMember`, `CallOrIndex` (`m(1)` refused as `MATLAB:NET:UnsupportedIndexingCustom`,
`m()` is `m`), `ApplyBinary`, the bracket joins (refused as `MATLAB:class:concatenationScalar`), the
resolver's dominant object and user-method dispatch, `DeepEquals`, `ClassOf`, `isa`, `isobject`,
`properties`, `fieldnames`, `isprop`, `ismethod`, `methods`, `events`, `numel`, `length`, `isvalid`,
the numeric, `logical`, `char`, `string` and `cell` conversions, `num2str`, `getReport`, `rethrow`,
the catch that binds a carried exception, and the MAT-file writer (refuses: a .NET value lives in the
process that made it). Falling into an existing refusal, probed: unary minus, `abs`, `sum`, `max`,
`zeros`, `repmat`, `mat2str`, `sprintf('%d')`. Benign defaults, probed: `size` `[1 1]`, `isempty`
false, `isnumeric`/`ischar`/`isstruct`/`iscell` false, `isscalar`/`isvector` true, `ndims` 2.
Deliberately untouched: `JgsLifetime` does not track a .NET value (its lifetime is the garbage
collector's; listeners come with stage 5), and `IsHoldable` is false for one, since a value type is
copied at binding instead of counted.

**The type catalog.** The framework's types are one process-wide index built on the first .NET name
a session reads (about 0.2 s): every managed assembly of the shared framework folder, and every
framework assembly the process already loaded, so the WPF app sees `System.Windows.*` and the CLI
does not. An assembly `NET.addAssembly` adds is loaded once per path per process into one
non-collectible load context, and is visible to the session that added it alone. A path is read as a
PE image first, which is where R2025b's refusals of a native DLL and of a non-image come from, word
for word. An argument that is not a rooted path is an assembly name, as R2025b reads it.

**Where .NET sits in name resolution.** A dotted name is .NET's when its head is claimed by nothing —
no variable, no function in scope, no built-in, no loaded class, no file on the path (asked through
the M145 file index, not the disk: a probe per folder cost 170 µs on every mention) — and the head is
a namespace the catalog knows. The walk goes namespace by namespace to a type; a type in front of a dot
names its static members without being constructed, as `TryClassInFront` does for a user class; a
type with nothing after it constructs (bare, `class(JGTest.Members)` answers `JGTest.Members`). A
chain that ends on a namespace or names no type, or an unclaimed head that is no namespace, is
R2025b's `MATLAB:undefinedVarOrClass`, "Unable to resolve the name 'a.b.c'." `@System.Math.Max`,
`feval('System.Math.Max', …)` and `str2func` reach a static method group; `which` calls a type a
built-in method, `exist` answers 8 for a type and 7 for a namespace, and `?Name` is a metaclass
literal the parser now reads.

**Members.** A dot on a .NET object reads a property or field — static ones included, as
`m.Constant` shows — or a method, called when the mention is bare and handed back as a callable
otherwise; an indexer is a method under its name (`list.Item(0)`). `Describe(m)` finds the method on
the .NET object among the arguments, as a user method is found. `m.Value = v` writes through the
property's setter with the value converted; a read-only one is R2025b's `MATLAB:class:SetProhibited`.

**Overloads are chosen by the recorded order.** The candidates take exactly as many arguments as were
given. Each argument ranks each parameter by its position in R2025b's preference row for the
argument's MATLAB class and shape (`NetConvert.Orders`, from `net_conversions`: every pair recorded
there forms a strict order, `tools/interop/summarize-overloads.py`); a candidate some argument cannot
reach drops out, the lowest total wins, the first declared on a tie. Types the probe did not cover are
placed by rule: `Nullable<T>` just after `T`, a class's own array type first among its arrays, a .NET
object by its distance up its base chain. A NaN or complex double ranks as a real one and fails later,
at the conversion — the recording shows `Byte` beating `Object` for NaN and the chosen `Byte` failing;
`Decimal` alone takes a complex, as its real part. This reproduces all 1,006 lines; stage 2 fits the
several-argument rule, `params`, optional, `ref` and `out`.

**Returns.** Integer returns keep their class, a `System.String` stays a .NET object (`char` converts
it), `null` is `[]`, a boxed primitive under an `object` return converts by its runtime type, and
`Decimal`, `DateTime`, `IntPtr`, `Guid`, enums, structs and arrays come back as objects. A reference
type is shared by a second binding; a value type is copied (`RuntimeHelpers.GetObjectValue`), and a
method called on one changes the variable's own copy (`p.Move(10)`).

**`NET.NetException`.** Whatever a member throws is caught where it is called — a .NET
`NullReferenceException` must not read to the interpreter as a defect of its own — and raised as an
MException of class `NET.NetException`: identifier `MATLAB:NET:CLRException:` plus the context
(`MethodInvoke`, `CreateObject`, `PropertyGet`, `PropertySet`, `AddAssembly`, or none for
`NET.createGeneric`), message `Message: …` / `Source: …` / `HelpLink: None`, and the exception as
`ExceptionObject`, inner exception included. `isa(e, 'MException')` holds, the catch keeps the class,
`rethrow` carries it whole, `properties(e)` lists `ExceptionObject` ahead of MException's own, and
`getReport(e, 'basic')` keeps its "Error using" line, as R2025b's does for this class alone.

**Operators.** An operator a type overloads (`op_Equality`, `op_Addition`, …) is called with the
other side converted as an argument is, which is how `System.String('Hello') == 'Hello'` is true.
Without one, `==` is identity for a reference type and `Equals` for a value type; `isequal` compares
two objects of one class by their public properties and fields (R2025b: two `Members` built alike are
equal); every other operator is refused.

**The builtins.** `NET.isNETSupported`, `NET.addAssembly` (by path and by name), `NET.createArray`,
`NET.createGeneric` (the forms the fixtures build their inputs with; stage 4 completes both),
`dotnetenv` (the running runtime; see below), `meta.class.fromName`, `isjava` (false: JGraph hosts no
Java) and `ismethod` (a classdef method or a .NET one). Registered in both dialects. JGS has no dotted
names, so a JGS script reaches .NET through `feval` with a dotted name and through a `.m` helper
(V11's crossing); what it holds is the same value (`net_jgs_smoke`).

**64-bit integers (the user's decision on open question 2).** A .NET `Int64` or `UInt64` past 2^53 is
held to a double's precision and says so with `JGraph:interop:int64Precision`, through the session's
own warning state.

**Fixes the stage's fixtures surfaced, not .NET-specific.**
- An undefined name in the MATLAB dialect carries R2025b's identifier, `MATLAB:UndefinedFunction`.
- A statement's echo goes where `disp` writes, so `evalc('x')` captures it (it answered `''` while the
  echo reached the console) and the diary records it, as MATLAB's does.
- A lone `true` or `false` as an index is a one-element mask (`x(true)` is `x(1)`).
- `strjoin` of one string is that string (the body saw it demoted to a char row and refused it).
- `methods(x, '-full')` is accepted and lists names until stage 2 writes the signatures.
- The parity harness joins a multi-line run failure onto one line, as `ix_flat` joins a display: a
  recording line cannot hold a break, and a .NET message is three lines.

## Measured

Release CLI, warm: a static property read 3 µs (175 µs before the file index answered the head
check), `System.Math.Max(3, 4)` 17–20 µs (overload ranking over thirteen candidates and reflection's
invoke), an instance call 15 µs, R2025b not yet measured beside them (stage 10's timing row).

## What moves

- `net_basics` agrees on all 95 lines, `net_conversions` on 1,005 of 1,006, `net_exceptions` on 84 of
  85, `net_display` on 4 of 22 with 13 recorded divergences (the display rows), and 5 lines re-owned:
  `methods_list` and the three `methods_full_*` to I2 (the listing layout and the signatures),
  `events_disp` to I5. No line is pending on I1.
- The fixtures changed and were re-recorded from R2025b: `Sqrt` and `PI` carried a `rel=` rule on a
  value `ix_show` prefixes with its class, which no engine could pass; the `methods -full` rows are
  filtered to the type's own lines and sorted, because R2025b lists overloads of one name — handle's
  `addlistener` and the type's own `Twice(int32)`/`Twice(double)` alike — in an order it does not keep
  between runs.
- The later stages' fixtures run further and their baselines moved: `net_assembly` now runs to its
  end (12 lines pending I3), the others stop at their own stage's first missing piece. Both lanes
  agree with no boxed overlay.
- `net_jgs_smoke` (new, JGraph-only) pins the JGS road.
- Coverage: 422 of 514 builtins and 1,132 of 2,024 callables (`NET.isNETSupported`,
  `NET.addAssembly`, `NET.createArray`, `NET.createGeneric`, `isjava`, `ismethod`,
  `meta.class.fromName`); the interop section of the builtin coverage document is 29.
- Tests: `NetInteropM174Tests`.

## Consequences

- A script can construct and call .NET types, catch their exceptions and pass values both ways;
  stages 2–6 add members in full, assemblies and `import`, arrays and generics, events and the inline
  C# helper.
- The framework index is built on the first .NET name a session reads, and never for a script that
  names none: the head of every dotted name is checked against the workspace, the file index and the
  loaded classes first.
- `verify-method-classes.py` reports the same nine class-map rows and eighteen catalog names it did
  before this stage (chip task_525c9c69); the names this stage adds to the catalog join its sweep.

## Divergences

- **A .NET value displays in JGraph's layout.** The echo and `disp` of a .NET object read like a
  `classdef` object's in JGraph (`m = Members with properties:` on one line, `1.5` rather than
  `1.5000`, `[1x1 System.String]` for a value that stays .NET, `Color enumeration: Green`), because
  JGraph displays every value in its own layout and MATLAB's is not implemented anywhere
  (`net_display`, 13 lines, `div=ADR0174`).
- **A 64-bit integer past 2^53 comes back rounded, with a warning.** `JGTest.Returns.Int64()` is
  2^53 + 1 in R2025b and 2^53 in JGraph, which holds integers in doubles (ADR 0069, 0156);
  `JGraph:interop:int64Precision` says so (`net_conversions`, `ret_Int64`, `div=ADR0174`).
- **The runtime is always JGraph's own .NET 8.** `dotnetenv` reports `core` and `loaded` before any
  .NET call, where R2025b reports `framework` and `notloaded`; a request naming the running runtime is
  accepted, and any other is R2025b's `MATLAB:netenv:NETLoaded` refusal, the answer it gives once .NET
  is loaded (the `net_assembly` rows are stage 3's).
