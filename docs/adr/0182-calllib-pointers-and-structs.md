# ADR 0182 — calllib, pointers and structs

## Status

Accepted. Stage I9 of the .NET and shared-library interop plan
(`docs/plans/net-and-shared-library-interop-plan.md`), after stage I8 (ADR 0181, a74f809). One
commit, this ADR's. It completes `calllib`: every argument kind R2025b's type table has, the
outputs, `libpointer` and `lib.pointer`, `libstruct` and its `lib.<struct>` objects, enums by name
and by value, and the refusals, each in R2025b's words. The app's windows, the documentation and the
gate are stage I10.

## Context

Stage I8 left `calllib` able to pass numbers, logicals, C strings and enums and to return a struct
by value. Every other argument type was refused with a JGraph identifier that named this stage.
Step 0 had recorded three fixtures for it (`shrlib_types`, `shrlib_pointers`, `shrlib_structs`,
which between them hold the rows the plan listed under `shrlib_enums` and `shrlib_errors`), and
`probe_shrlib_calls` had recorded most sentences. What the fixtures did not settle was:

- what `calllib` hands back for a pointer argument given as a `lib.pointer` or a libstruct rather
  than as a MATLAB array;
- how a pointer-to-pointer argument treats a NULL pointer, a pointer of the inner type and `[]`;
- the sentences of refusals step 0 recorded by identifier only;
- `lib.pointer`'s `+`, `reshape`, `setdatatype` and `Value` writes at their edges;
- libstruct field writes, methods and display.

A new probe, `probe_shrlib_pointers2`, answers them (below).

Two defects of R2025b were pre-registered in the plan (step 0, finding 9), and a third turned up in
the probe. Under the standing rule that correctness outranks parity where MATLAB is demonstrably
wrong, JGraph gives the true answer and records each one as a divergence:

- `UINT64_MAX` returned from a function arrives as **-1**;
- a libstruct whose type has an array field (`jg_mixed`) or non-default packing (`jg_packed`) is
  passed as **NULL**, and its array fields read `[]`;
- `libpointer('int32Ptr', [2.5 -2.5 1e10])` holds **[2 -2 -2147483648]**: truncation and a C
  overflow, not MATLAB's own conversion into `int32`.

## Decision

**Two value classes over host memory.** Both are external values (ADR 0174) and handles:

- `LibPointer` is `lib.pointer`. It holds:
  - an address in the session's native host;
  - its `DataType`;
  - what its `Value` reads: an element type (a scalar type, `cstring`, a C string array, or a
    struct) and a size.

  A pointer a library returned has no size until `setdatatype` or `reshape` gives one, and a
  `voidPtr` has no element type until a value does.
- `LibStructValue` is `lib.<struct>`: a struct's bytes in the host, laid out by the library's model
  (ADR 0181).

Every field read and write goes to host memory. So a libstruct handed to a call sees what the
library wrote, and a library that keeps a pointer writes into what the script reads later.

**Ownership without the V10 count.** What JGraph allocates for a pointer or a libstruct is a
`NativeBlock`. Every pointer made from one keeps it alive:

- `p + n` shares it;
- a pointer-to-pointer's cell holds its target;
- a C string array's block owns its strings.

When the last holder is collected, the block's finalizer queues the free, and the host makes it
before its next call or allocation. The finalizer thread never touches the pipe.

ADR 0171's exact holder count is for handle objects with destructors that scripts can observe.
Nothing observes when host memory is freed, so collection is enough. It is also safe: memory is never
freed while any value can still reach it.

A pointer a library returned never frees what it points at, as in MATLAB. A libstruct holds its
library, as R2025b's outstanding objects do. `unloadlibrary` refuses (`MATLAB:unloadlibrary:IsInUse`)
while one is alive; it collects first, so a libstruct nothing names any more does not keep the library.
A plain `lib.pointer` does not hold its library.

**Arguments** follow R2025b's rules, measured in `shrlib_types`, `shrlib_pointers`,
`shrlib_structs` and `probe_shrlib_pointers2`:

- **A scalar** takes a real numeric or logical scalar, rounded and saturated into the type. The
  refusals, in R2025b's order:
  - `RequireNumeric`, or `RequireLogical` for `bool`;
  - `RequireReal`;
  - `RequireScalar`;
  - `RequireNumber` for NaN into an integer type, `nologicalnan` for NaN into `bool`.

  A `bool` is whether the value is nonzero.
- **An enum** takes a member's name (char or string) or a number, rounded. An unknown name is
  `MATLAB:class:InvalidEnumValue`.
- **A struct by value** takes a MATLAB struct, or a libstruct of its type:
  - fields are matched by name, case-sensitively;
  - a missing field is zero;
  - a field no member has is `FieldMismatch`;
  - a member that cannot take its field's value is `InvalidFieldValue` ("Cannot convert data value
    for field … due to error:" and the inner sentence);
  - a struct array passes its first element.
- **`T*`** takes one of three things:
  - a numeric or logical array, converted into T and read back after the call in T's class and
    the array's shape (`boolPtr` takes a scalar only);
  - a `lib.pointer` of T, of `voidPtr`, or with no type;
  - `[]` for NULL.

  Anything else is `IllegalConversion`, and a pointer of another type is `PointerTypesMustMatch`.
- **`void*`** takes a pointer, a libstruct, an array in its own class, or `[]`.
- **`char*`** takes a character vector, a C string pointer, or `[]`. A string or a number is
  `MustBeString`, and an `int8Ptr` pointer is `PointerTypeMustMatch`.
- **`char**`** takes a cell of character vectors, a pointer, or `[]`. Anything else is
  `MustBeCellStringsParameter`.
- **`S*`** takes a struct, a libstruct, a pointer, or `[]`. Anything else is `RequireStruct`.
- **`T**`** takes three kinds of argument:
  - a `T*` pointer is passed by the address of its address, and moves to wherever the call points
    it;
  - a NULL `T**` pointer, or `[]`, gets a cell of the call's own;
  - a non-NULL `T**` pointer is passed as it is.
- **`error`, `FcnPtr` and `T***`** are `MATLAB:invalidConversion`, "Conversion to unknown from
  <class> is not possible.".

**Outputs** are the return value, then one per pointer argument in argument order. Each is the
value that argument's memory holds after the call:

- **An array** comes back in the pointee's class and its own shape.
- **A struct** comes back as a struct.
- **A C string** comes back as char, and **a C string array** as a cell.
- **A `lib.pointer` argument** comes back as its `Value`. When the pointer has no value, it comes back
  as a new pointer to the same place, and for `voidPtr` always as one.
- **A `T**` argument** comes back as a new `T*` pointer to where the call left it, or, for a struct,
  as the struct there.

Returned pointers are `lib.pointer`s. An exported variable (`calltype` `data`) answers a pointer to
itself.

**`lib.pointer`:**

- **Properties.** `Value` and `DataType`; `DataType` is read-only.
- **Methods.** `isNull`, `plus`, `reshape`, `setdatatype`, `get`, `set`, `delete` and `isvalid`,
  reached through the user-method layer of the search order, so `reshape(p, 3, 1)` is the pointer's.
- **`p + n`.** A new pointer n elements on, sharing the memory. A value `libpointer` made has its
  extent shortened by n; a library's pointer keeps its size.
- **Writing `Value`.** A write replaces memory the pointer owns, sized to the new value, but writes
  a library's memory in place and keeps its size.
- **`setdatatype`.** Refused to another type for a pointer `libpointer` made (`IllegalConversion`,
  measured).
- **Reading `Value`.** It needs a type and a size (`ValueNotDefined`). A C string array reads one
  string until a size says more. A NULL struct pointer reads a struct of zeros, and a NULL pointer to
  a pointer reads an empty.
- **`methods` and `fieldnames`.** They list what R2025b lists, handle's methods included.

**libstruct:**

- **Its fields are properties.** A scalar reads as double (`bool` as logical). An array member reads
  as a row of its own class, and a nested struct as a struct.
- **Writes** round and saturate into the member (`MustBeNumeric`, `MustBeScalar`). An unknown
  field is `noPublicFieldForClass`.
- **`structsize`, `get`, `set`, `delete` and `isvalid`** are its methods.
- **It shows itself** as "`<type>` with properties:" and its fields.
- **An unknown type** is `MATLAB:UndefinedFunction` for `lib.<type>`.

**Returns past 2^53.** A 64-bit integer return past 2^53 is held to a double's precision with the
interop warning `JGraph:interop:int64Precision`. That is the user's plan decision (question 2),
applied here as the .NET stages applied it.

**One interpreter fix on the way.** A dot naming no field was JGraph's own error in the MATLAB
dialect ("This struct has no field …", no identifier). It is now R2025b's `MATLAB:nonExistentField`,
"Unrecognized field name "…"." An empty struct array refuses a field it does not have, where it used
to answer an empty list. The JGS dialect keeps its own sentence. `shrlib_structs` needed this (a NULL
pointer to a struct pointer reads a 0-by-1 struct, and `.Value` on it is refused), and no other
fixture changes.

## Probes

`probe_shrlib_pointers2` (new, beside the others, `run-probes.ps1`) covers:

- the output of every pointer argument kind given as a pointer, a libstruct, an array and `[]`;
- whether a `T**` output is the pointer passed in;
- about 45 refusal sentences;
- `lib.pointer`'s operators, `reshape`, `setdatatype`, `Value` writes on owned and library memory,
  `get`, `set`, `isvalid`, `delete`, `methods`, `fieldnames` and display;
- libstruct field writes, methods, display and nested members;
- `sprintf('%d', 2^64)`, which R2025b prints as `1.844674e+19` as JGraph does, so the `uint64` row
  differs only in the value.

## Measured

Release, this machine, warm, R2025b beside it. The script, `jgtestlib` through its prototype file:

| What | JGraph | R2025b |
|---|---|---|
| `calllib(lib, 'jg_double', k)`, per call | 34–35 µs | 2.7–16.5 µs |
| `jg_scale_double` on a 1e6-double array, in and back out | 12.7–14.0 ms | 4.5–7.5 ms |
| The same on a 1e6-double `libpointer` (nothing copied back) | 5.1–5.2 ms | 3.5–3.7 ms |
| `p.Value` of a 1e6-double pointer | 3.9–4.7 ms | 2.3–2.5 ms |

The array path was 22.6 ms until doubles were copied as a block rather than element by element. The
scalar call is the round trip to the host process over its pipe (ADR 0180), which the plan's stage
I10 timing takes up.

## What moves

- **New `Jgs/Native/LibValues.cs`:** `NativeBlock`, `LibPointer` and `LibStructValue`.
- **`NativeHostProcess`:** `ReleaseLater` and the drain before each call and allocation.
- **`SharedLibrary`:** the weak list of live libstructs.
- **New `JgsBuiltins.LibPointers.cs`:**
  - `libpointer` and `libstruct`;
  - the memory conversions and the struct encoder and decoder;
  - `calllib`'s argument kinds and outputs;
  - the entry points the interpreter calls for a dot, a dot assignment, a method and an operator.
- **`JgsBuiltins.SharedLibraries.cs`:** `calllib` is rebuilt on those. It also answers an exported
  variable's pointer and refuses `unloadlibrary` while a libstruct is alive. Stage I8's
  `JGraph:calllib:UnsupportedType` is gone.
- **The interpreter:**
  - `ExternalMember`, `TryAssignToNet`, `TryUserMethod` and `ApplyBinary` each gain the
    lib-value branch;
  - `methods`, `properties`, `fieldnames` and `isprop` list its names;
  - loading a library or making a pointer marks the session as one whose external values dispatch
    methods;
  - `NoSuchField` holds the MATLAB dialect's missing-field error.
- **`get`, `set`, `delete` and `isvalid`** answer a `lib.pointer` or a libstruct themselves
  (`TryLibBuiltin`). That is the JGS dialect's road, since JGS dispatches no methods (ADR 0172);
  `net_jgs_smoke` gains three rows.
- **Registered and catalogued:** `libpointer` and `libstruct`, in `EvalBuiltinNames` and the catalog.
- **Fixtures:**
  - `shrlib_types`, `shrlib_pointers` and `shrlib_structs` are re-recorded with their divergent rows
    marked and stamped in both lanes;
  - every line agrees or is `div=`, and none is pending on I9.
- **Tests:** `LibPointerM182Tests` (10):
  - a block freed after its last holder;
  - a `plus` pointer keeping its memory;
  - the past-the-end refusal;
  - integer rounding;
  - a libstruct released on `clear`;
  - a pointer into a crashed host;
  - `UINT64_MAX` with its warning;
  - array-field and packed libstructs passed as memory;
  - a struct by pointer and a libstruct seeing a call's writes;
  - the missing-field error.

## Consequences

- A C library's whole interface is callable from both dialects: arrays in and out, strings and
  string arrays, structs by value and by pointer, memory the library allocates or keeps, and opaque
  handles.
- A crash still costs only the host. Every pointer into it then refuses with
  `JGraph:libpointer:HostExited`, and new pointers go into the next host.
- Host memory JGraph allocates is freed some time after nothing reaches it. It is never freed while
  something does.

## Divergences

- **`UINT64_MAX` returned from a function is 1.8447e19, with the `int64Precision` warning.** R2025b
  answers -1 (`shrlib_types` `uint64_max_ret`).
- **A libstruct whose type has an array field or non-default packing is passed as its memory.**
  R2025b passes NULL, so the library sees nothing and writes nothing. Its array fields read as rows
  of their own class from the start (`int32 [0 0 0]`), where R2025b reads `[]` (`shrlib_structs`,
  six rows).
- **A value written into integer pointer memory rounds and saturates as `int32()` does.** R2025b
  truncates and wraps: `libpointer('int32Ptr', [2.5 -2.5 1e10])` holds `[2 -2 -2147483648]`, and
  JGraph holds `[3 -3 2147483647]`. No fixture row.
- **A pointer that owns its memory refuses a `Value` that would read past it**
  (`JGraph:libpointer:PastEnd`). R2025b reads past the allocation. No fixture row.
- **A libstruct nothing names does not keep its library loaded.** R2025b still counts one after the
  names holding it are cleared. No fixture row: the fixture keeps its libstruct alive.
- **The echo of a `lib.pointer` and of a libstruct uses JGraph's display layout**, the value on the
  name's line (`lp_echo`, `libstruct_echo`), as for every other value (ADR 0174).
