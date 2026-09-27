# ADR 0177 — .NET arrays, generics, enums and dictionaries meet MATLAB's verbs

## Status

Accepted. Stage I4 of the .NET and shared-library interop plan
(`docs/plans/net-and-shared-library-interop-plan.md`), after stage I3 (ADR 0176, 52db48a). One
commit, this ADR's.

## Context

Stage I3 left three fixtures to this stage, each stopped by its `RUN` line at the first missing
piece: `net_arrays` at `r(1)` ("Array indexing is not supported for objects of class
'System.Double[]'"), `net_generics` at `NET.GenericClass` (a field the `NET` struct did not have),
`net_enums` at `enumeration` (no such function). Eight lines were pending on I4.

Three R2025b probes (`probe4`, `probe4b`, `probe4c`, scratch, not committed) settled what the
fixtures leave open: subscripts of every kind on 1-D and 2-D arrays, writes of every kind of value,
rectangular jagged arrays, which conversions each array and enum takes, `NET.GenericClass` and
`NET.invokeGenericMethod` with their refusals, `dictionary` of a .NET dictionary, and the text
verbs, relations, `switch` and bit operations on enum members. `probe4` crashed R2025b outright at
`dictionary(System.Collections.Hashtable())` ("Bad optional access"); the rest ran. What they
settled became about 110 new fixture lines, recorded from R2025b with the three fixtures.

## Decision

**A .NET array takes one scalar subscript per dimension, from 1.** `arr(i)` and `g(i, j)` read an
element as a member declared with the element type answers it (`Char` as a char, `Object` holding a
string as a `System.String`, `null` as `[]`); `arr(i) = v` writes the array itself, which every name
for it sees, a function's parameter included. The subscripts are evaluated with an `end` that
refuses (`MATLAB:NET:UnsupportedEndIndexingArray`) wherever it stands in them. A subscript count
other than the rank — `g(4)` on a 2-D array, `r(1, 1)`, `r()` — is "No method '()' with matching
signature found for class …" (`MATLAB:class:UndefinedMethod`); a subscript that is not a positive
whole number in some numeric class — `0`, `1.5`, `true`, `'a'`, `1:2`, `:`, `[]` — is
`MATLAB:NET:InvalidArrayIndex`; one past the bounds is the array's own `IndexOutOfRangeException` as
a `NET.NetException` (`MethodInvoke`). A write takes what an argument of the element type takes, one
element only: a vector, an empty, and a char into a number slot are "No method 'Set' …". Braces are
R2025b's `MATLAB:cellRefFromNonCell`. In JGS the same array takes brackets from 0.

**Conversions out of an array follow R2025b's table.** `double`, the integer classes, `single` and
`logical` take a numeric or `Boolean` array of rank 1 (a row) or 2, and a jagged array whose rows are
numeric and of one length, as a matrix (a `null` row is a row of none); rows of two lengths are
`MATLAB:NET:NetConversion:NonRectJaggedArray`. `string` takes a `String[]` only; `char` takes neither
a `Char[]` nor a `String[]`; `cellstr` takes neither. `cell` takes a one-dimensional array of a
reference type — `String[]` (as char rows), `Object[]` (each element as a member declared `Object`
answers it) and a jagged array (each row as a MATLAB array) — and refuses a numeric, logical, char or
enum array and any 2-D one. A `Double[]` and a `Double[,]` convert without boxing each element.

**Arithmetic on a .NET value** is `MATLAB:math:mustBeNumericCharOrLogical` when neither operand's type
declares an operator (an array, an enum member, a `StringBuilder`), and the missing overload
(`MATLAB:UndefinedFunction`) when one does (`JGTest.Vector2`). `sum` and `mean` of one are
`MATLAB:sum:wrongInput`, `max` its own `MATLAB:max:wrongInput`.

**`NET.createArray`, `NET.createGeneric`, `NET.convertArray` are complete.** An element or type
argument is named by text — an array type with its brackets (`'System.Double[]'` for a jagged array,
`'System.Int32[,]'`) — or by a `NET.GenericClass`; `createArray` takes lengths one by one or as a
vector. The refusals are R2025b's: `createArray` of a type that does not resolve is
`CLRException:TypeError`; a generic definition or type argument that does not resolve is
`CLRException:BuildGenericType`, a generic argument written in text (`'…List<System.Double>'`)
`MATLAB:NET:InvalidClassName`, type arguments that are not a cell `MATLAB:class:RequireClass`, a broken
constraint the runtime's `CLRException`; `convertArray` of a cell is
`MATLAB:NET:UnsupportedInputArrayTypeError` and to an unknown type `UnsupportedOutputArrayTypeError`.

**`NET.GenericClass(definition, typeArg…)`** is a value of class `NET.GenericClass` holding the closed
type, with no properties, for a type argument that is itself generic. A non-text definition is
`MATLAB:class:RequireString`, no type argument `MATLAB:minrhs`.

**`NET.invokeGenericMethod(objOrType, name, {typeArgs}, args…)`** closes the generic methods of that
name (an object's instance and static ones, a type's static ones) over the type arguments and
chooses among them as any call does, with their outputs. R2025b's refusals: no generic method of the
name is `MATLAB:NET:NoGenericMethod`; none that fits the arity, constraints or arguments
`MATLAB:NET:NoMatchingGenericMethod`; type arguments that are not a cell of names — a
`NET.GenericClass` among them — `MATLAB:NET:InvalidGenericParameterType`; a name that resolves to no
type the runtime's `CLRException:InvokeGenericMethod`, and a type named by text that does not resolve
`CLRException:TypeError`.

**A generic nested in a generic writes its `+` as `*`** and drops each arity:
`System.Collections.Generic.Dictionary*KeyCollection<System*String,System*Double>`. A plain nested
type keeps its `+` (`JGTest.Outer+Inner`).

**`dictionary(d)` of a .NET dictionary is a copy of its entries.** Any `IDictionary` converts: a
`System.String` key becomes a string, any other key and every value what a member declared with the
dictionary's key and value types answers (`Dictionary<Int32, String>`: an `int32` key and a
`System.String` value). An empty one is unconfigured, and adding to the .NET dictionary afterwards
leaves the copy as it was. A MATLAB `dictionary` passed where .NET takes `Object` is
`MATLAB:NET:NetConversion:ObjectConversion`. Two general fixes came with it: `keys` keeps an integer
key class as `values` already did, and `values` of a dictionary holding one .NET value answers the
value rather than a cell of it.

**Enum members.** A member converts to `double`, to its underlying class only (`uint8` of a byte enum,
`int64` of a long one; `int32(JGTest.Big.Negative)` is refused), to `char`, `string` and `cellstr` (its
name). Two members compare by value under every relational operator, across two enum types too; a
member and anything else are unequal and unordered (`c == 1`, `c == 'Green'` are false). The text
verbs compare a member by its name: `strcmp`, `strcmpi`, `isequal(c, 'Green')`, `ismember(c,
{'Red', 'Green'})` and against a string array are true. `switch` matches as `==` does — a member case
hits, its name does not. `bitand`, `bitor` and `bitxor` of two members of one `[Flags]` type answer a
member of it; a first operand that is no `[Flags]` member is `MATLAB:Bitor:operandsNotNumeric`,
`MATLAB:Bitand:…` and `MATLAB:bitxor:…` in each builtin's own spelling, and a second operand of
another kind, or a third argument, is "No method 'bitor' with matching signature found for class …".

**`enumeration(type)`** — by name, by member, or as a command — prints a blank line, "Enumeration
members for class 'T':", a blank line, the members in value order indented four, and a blank line;
"No enumeration members for class T." for a type that is no enum and "No class 'T'." for a name that
is none. Asked for an output it answers the one member there is, and for two or more R2025b's
`MATLAB:class:concatenationScalar`, since a .NET object is a scalar. JGraph's `classdef` has no
enumeration block, so a .NET type is all `enumeration` can name.

**`NET.interfaceView`** is `NET.explicitCast`'s view under R2025b's other name (the refusal
identifiers were already `MATLAB:NET:interfaceView:*`).

**`properties`, `fieldnames` and the display list a type's own statics.** A static property or field
inherited from a base type is left out (R2025b: a `Double[]` does not list `Array.MaxLength`).

**A built-in call with an object among its arguments no longer probes the disk.** When the object's
class (a classdef class or a .NET type) declines the call and no file shadows the built-in — which
the M145 file index knows — the built-in answers without the folder probe that cost 60 µs a call:
`class(r)`, `double(r)` and `numel(r)` of a .NET array went from 61–64 µs to 1.4–2.8 µs.

## Measured

Release CLI, warm, 20,000 calls per row, the quietest of several runs on a loaded machine:
`r(5)` 0.4 µs, `r(5) = k` 0.8 µs, `c == JGTest.Color.Green` 1.0 µs, `double(r)` of 100 elements
1.4 µs (63 µs before the two fixes above), `System.Math.Max(3, 4)` 2.9 µs and an instance call 0.7 µs.

## What moves

- `net_arrays` agrees on all 120 lines (1 divergence, `echo`), `net_generics` on all 72 (3: two
  displays and `dict_keys`), `net_enums` on all 93 (3 displays). Each was re-recorded from R2025b with
  its new lines; the lines recorded in step 0 came back unchanged. The six display lines are ADR 0174
  divergences, as stage 1 decided for every display row. No line is pending on I4; the ratchet's
  pending lines are I5 2, I8 1, I9 3.
- `net_jgs_smoke` gains two rows: a .NET array takes JGS's brackets from 0, and `NET.GenericClass`
  answers through `feval`.
- Coverage: 428 of 514 builtins and 1,139 of 2,024 callables (`enumeration`, `NET.invokeGenericMethod`);
  the interop section of the builtin coverage document is 23.
- Tests: `NetInteropM177Tests` (18).

## Consequences

- A MATLAB script can index, fill and convert .NET arrays, build and call generics, and use enums
  and dictionaries as R2025b lets it. Stage I5 adds events, delegates and threads.
- The resolver answers an unshadowed built-in before the folder probe whenever an object's class
  declined the call; a classdef object passed to a built-in gains the same saving.
- `bitand`, `bitor` and `bitxor` with a .NET operand, and `ismember`, `strcmp`, `isequal` and `switch`
  with an enum member, take a branch of their own; a script without .NET values never reaches it.

## Divergences

- **`dictionary` of a .NET dictionary makes its `System.String` keys strings.** R2025b keeps them
  `System.String` objects, whose `keys` it then cannot combine (`MATLAB:dictionary:CannotCombineKeys`,
  net_generics `dict_keys`, `div=ADR0177`); JGraph's dictionary holds text and number keys, and its
  `keys` answers the string column.
- **`dictionary` of a non-generic `IDictionary` converts it.** R2025b crashed on
  `dictionary(System.Collections.Hashtable())`; JGraph converts each key and value as a member declared
  `Object` answers it.
- **`values` of a dictionary holding two or more .NET values answers a cell of them.** R2025b would
  concatenate them, which a .NET object refuses; not probed.
- **The messages of a runtime-resolution refusal name no assembly.** R2025b's `TypeError` and
  `BuildGenericType` messages read "Could not resolve type 'X' in assembly 'MathWorks.MATLAB.Interface,
  …'" with that assembly as the source; JGraph's read "Could not resolve type 'X'." with no source.
