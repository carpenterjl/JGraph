# ADR 0317 — The Excel add-in's conversion layer and JGraph's COM utility library

## Status

Accepted, 2026-11-07. Compiler and SDK plan, stage K17d (decision KO10).

## Context

Stage K17c (ADR 0316) built the add-in's COM DLL and served its functions with the conversions
stage K17a had measured under MathWorks' default flags; it refused, with a message naming this
stage, a Range object, a struct, complex or sparse value, and MathWorks' value classes, and it had
no utility library: `k17com.ps1`'s `util.*` rows failed for want of `MWComUtil.MWUtil25.2`. R2025b's
add-in converts under twelve flags (`MWFlags`, `IMWArrayFormatFlags` and `IMWDataConversionFlags`,
`extern\include\mwcomtypes.idl`), reads a `Range` through its value, writes an output into one, and
hands structs, complex and sparse values across as `MWStruct`, `MWComplex` and `MWSparse` objects of
MATLAB Runtime's `mwcomutil.dll`, whose `MWUtil` an add-in's VBA module creates first
(`MWInitApplication`, `MWPack`, `MWUnpack`). Decision KO10 chose JGraph's own ProgIDs for that
library (`JGraphComUtil.MWUtil25.2`, `JGraphComUtil.MWFlags`, ...) with MathWorks' class and member
names, so a VBA client ports by changing its `CreateObject` strings alone.

The flags' effect, Range objects and the value classes were measured here, not inferred: the K17a
oracle gained an extra phase (`src/k17client/k17com_extra.ps1`, about 1,000 rows run against
R2025b's add-in by `record-k17d.ps1`: `recordings/k17_com_extra.out.txt`) varying each flag over
every shape, reading and writing Ranges in every form, and driving each value class member by
member. Seven runs were needed: an empty Variant array under the AsIs format, an MWArg inside an
array of Variants, an MWSparse with no `NumRows`, a zero sparse index and a second
`MWInitApplicationWithMCROptions` each ended R2025b's host (an access violation in its conversion),
so those rows are left out or last.

## Decision

- **JGraph's COM utility library is `jgcomutil.dll`** (`native/jgcomutil`: `jgcomutil.idl`, `.rc`,
  `.def`, `build.ps1`; checked in as `native/win-x64/jgcomutil/jgcomutil.dll` with `SOURCE.md`,
  205 KB). Its type library is MathWorks' `mwcomutil.tlb` and `mwcomtypes.idl` transcribed under
  JGraph's ids and library name (`JGraphComUtil`, version 25.2, the release the dialect follows):
  the five enumerations with MathWorks' values, the three flags interfaces under the same ids an
  add-in's own type library declares them with (so a flags object made by either library is taken
  by the other), `IMWField`, `IMWStruct`, `IMWComplex`, `IMWSparse`, `IMWArg` and `IMWUtil` member for
  member and DISPID for DISPID, and the nine coclasses R2025b's library registers. `IMWEnumVararg`
  and `IMWMethodArgInfo`, which R2025b declares for its Function Wizard and never registers a class
  for, are not declared. The DLL is the add-in template's source (`native/jgxl/jgxl.cpp`) built a
  second time with `JGRAPH_COMUTIL`: it finds `jgrt.dll` beside itself (the runtime's
  `runtime\win64`, or JGraph's own folder) or in a PATH folder, hands every class to jgrt's generic
  object, and registers every coclass of its library from the type library alone, machine-wide when
  it may and else for the current user, under MathWorks' ProgID form for a utility library
  (`JGraphComUtil.MWUtil` and `JGraphComUtil.MWUtil25.2`, `CurVer`, the AppID, no component
  category, as R2025b's are registered). The runtime's installer registers it (`Package.wxs`,
  `SelfRegCost` on the one file taken out of the harvest); a build of an add-in registers it too when
  it finds it unregistered (`ExcelAddIn.RegisterUtility`, called where mcc registers the add-in), so
  JGraph run from its own folder serves its add-ins' VBA modules; a runtime carried in a folder
  registers it with `regsvr32`.
- **jgrt's module holds every coclass of a type library** (`com.cpp`: `Module.classes`, the class
  factory per coclass, `ComCreate` given the library's and the class's names, `jgrtComLoadModule` for
  the engine to wrap objects it makes of another library's classes; a failure the engine reports
  with neither source nor description is the bare HRESULT, no error info, which is how R2025b's
  `MWStruct` answers a bad index). The engine side (`JGraph.Runtime/Com`): `ExcelComUtilityLibrary`
  (the library's module, found once from the DLL beside `jgrt.dll`; `Create` by class name),
  `ExcelComUtility` (MWUtil), `ExcelField`, `ExcelStruct`, `ExcelComplex`, `ExcelSparse` and
  `ExcelArg` (`ExcelValueObjects.cs`, each with its own `MWFlags`, `Clone` and the measured members),
  and `ExcelConversion`, the conversion rules themselves, used by `ExcelComponent` for every argument
  and output under the component's flags.
- **In, by the measured rules** (`k17_com.out.txt`, `k17_com_extra.out.txt`):
  - A scalar to its class as K17c had it; a `DATE` a datenum through `DateBias`, or its text as
    `oleaut32` writes it under `InputDateFormat` 1 (`3/15/2024 10:30:00 AM`, `3/16/2024`,
    `6:00:00 AM`); a typed `SAFEARRAY` a matrix of its element class; a `DATE` array under
    `InputDateFormat` 1 a cell of texts.
  - **`InputArrayFormat`** over an array of Variants: the Matrix format (1, the default) collapses
    numbers of one class into a matrix, booleans into a logical, one-character strings into a char
    matrix, else a cell; the AsIs format (0) always a cell; the Cell format (2) a cell, a typed
    array split into a cell of its elements, and a scalar a 1-by-1 cell; a value past 2 is AsIs.
    Nesting: a client's own array of Variants collapses at the top and at the depth
    `InputArrayIndFlag` names, a Range's cells at that depth alone (so a Range under
    `InputArrayIndFlag` 1 is a cell); under the Cell format a Variant element at depth d is wrapped in
    (ind - d + 1) one-element cells, a typed array's or a Range's elements once at most, a scalar
    argument once whatever the flag says.
  - **`CoerceNumericToType`** over numbers, logicals, dates (their datenums), error codes and the
    elements of a cell, never over text and never over a client's own array of Variants that
    collapsed to a matrix (a Range's is coerced): 3 logical (nonzero true), 4 char (the VARIANT's
    text: `3.7`, `True`, a date's text; an array a cell of texts), 6 double, 7 single, 8 to 13 the
    integer classes rounded to even and saturated; a logical true is VARIANT_TRUE, -1, so -1 as a
    signed integer or a double and the type's maximum as an unsigned one (255, 65535, 4294967295);
    the undefined codes 1, 2, 5 and anything past 13 coerce nothing.
  - **`ReplaceMissing`** applies to a Range's blank cells alone (0, or NaN under 1, in a numeric
    matrix; false in a logical one; `[]` in a cell): a client's own array with an empty element is
    0 whatever the flag says.
  - **A Range** (any object with `Value2`) is read through `Value2` and converted as that value,
    marked as the Range's; an object without one answers its default property (Excel's `Application`
    is `Microsoft Excel`); an object with neither is refused.
  - **`MWArg` and `MWField`** convert their value under their own `DataConversionFlags` and the
    caller's array format; an unset value is `[]`. **`MWStruct`** converts each field so, an unset
    field `[]`. **`MWComplex`** converts its parts under the caller's data conversion (its own flags
    are kept but not applied, as R2025b's), a part left unset zeros, both unset nothing (a missing
    argument), the parts combined element by element in `Real`'s shape when their counts agree and
    refused when they do not, then the caller's array format (the Cell format makes a cell of complex
    scalars); integer parts become double, since JGraph keeps no complex integer (ADR 0266).
    **`MWSparse`** is `sparse(RowIndex, ColumnIndex, Array, NumRows, NumColumns)`: typed arrays or
    scalars (an array of Variants refused, as R2025b refuses it), the values double or logical,
    duplicates summed, a fractional index rounded, nothing when its `Array` is unset (a missing
    argument, in the middle too). A flags object or an add-in object as an argument is refused.
  - **`varargin`** is converted as any argument; a cell is spread into the arguments it holds (so a
    typed array under the Cell format spreads), anything else is one argument.
- **Out:**
  - **`OutputArrayFormat`** 2 (Cell) makes every numeric, logical or char value an array of Variants
    of its raw elements (NaN nothing, no date, no replacement; a scalar a 1-by-1), a struct's fields
    the same, a complex matrix an array of one-element `MWComplex` objects, and reaches into a cell's
    elements to the depth `OutputArrayIndFlag` says, `varargout` one deeper; 0, 1 and anything past 2
    are the as-is format.
  - **`ReplaceMatlabNaN`**: a NaN scalar or element is nothing (0), 0 (1) or `#N/A` (2; Excel's #N/A
    error value when written into a Range); a struct's fields are raw (NaN nothing).
  - **`OutputAsDate`**: a double whose value less `DateBias` is one OLE holds (-657434 to 9999-12-31)
    is a DATE, else that biased double; a double array element by element (an array of dates when
    every element is one, else Variants). R2025b builds an array its own client cannot read
    (`SafeArrayTypeMismatchException`); a struct's fields and the Cell format's elements are raw.
  - **A struct** is an `MWStruct` with `Dims` and `FieldNames` one-based arrays (`FieldNames`
    nothing when there are none), its fields converted raw under the component's flags, each
    `Item(i..., name)` the same `MWField` object every time and writable, so a struct read, changed
    and passed back carries the change; a complex value an `MWComplex` with scalar or one-based
    array parts; a sparse one an `MWSparse` with `NumRows`, `NumColumns`, one-based `Long()` row and
    column indices column by column and a one-based `Double()` (a logical's `Boolean()`) of the
    stored values.
  - **An output into a Range** (a slot holding one): nothing for an empty value or a NaN replaced
    by nothing; a scalar or text into the range's first cell, the range resized to one cell under
    `AutoResizeOutput`; an array transposed under `TransposeOutput`, then into the range as it is when
    its cells are as many as the array's elements (a row fills a column), else into the range resized
    from its first cell to the array's shape, else (no resizing) into the range as given, Excel
    filling what fits and `#N/A` beyond; a cell's nested array or object a blank; an `MWComplex`
    with a scalar `Real` writes it (Excel reads the object's default property), a struct, sparse or
    array complex value writes nothing and raises no error; what Excel refuses (a 3-D array, an array
    of bytes) writes nothing either. The slot then holds the range written.
- **`MWStruct`'s members:** `Initialize(dims, names)` sets the size (a scalar n is 1-by-n, a
  fraction truncated, a negative refused as "Error initializing array, invalid dimensions", a zero
  with field names refused as R2025b words it) and adds field names (an array of them in column
  order, duplicates dropped, a second call adding to the first, a name that is no text refused);
  `Item(i..., name)`: the indices first and the field's name last, none for a 1-by-1, one linear or
  one per dimension, DISP_E_BADINDEX with no text for anything else (a name alone on an array, an
  unknown or differently cased name, an index out of range, a number last, no argument) and
  DISP_E_TYPEMISMATCH for an index that is text; `NumberOfFields`, `NumberOfDims`, `Dims`,
  `FieldNames`, `Clone` (a deep copy), `MWFlags`.
- **`MWUtil`'s members:** `MWPack` packs its arguments into a one-based array of Variants, a VT_EMPTY
  or missing one left out, a `DBNull` kept, a matrix spread element by element (row by row, as
  R2025b lays them out) and a one-dimensional array kept whole; `MWUnpack(VarArg, nStartAt,
  bAutoResize, slots...)` hands the elements of an array of Variants (any rank, column by column; a
  typed array or anything else one element) to its slots from `nStartAt`, a slot that holds a Range
  written through it (resized to a matrix under `bAutoResize`); `MWDate2VariantDate` takes 693960 off
  a double alone and leaves every other type; `MWInitApplication(app)` keeps the Excel Application
  the object names through its `Application` property (a Worksheet or Workbook serves, as R2025b
  measured; a `Scripting.Dictionary` is refused); `MWInitApplicationWithMCROptions` refuses a list
  that is no string array ("The JGraph Runtime option list must be a string array.", E_INVALIDARG)
  and otherwise does what `MWInitApplication` does; `CreateRemoteObject(progId)` the object of a
  registered ProgID made in this process, nothing for an unknown one; `IsMCRJVMEnabled` false;
  `IsMCRInitialized` true once a component has been opened in the process, and stays true;
  `MWTerminateApplication` ends nothing.
- **Where R2025b's conversion is demonstrably wrong, JGraph answers the true value** and records
  it (`docs/MATLAB_Behavioral_Fixes.md`): a NaN coerced to an integer class is 0 (MATLAB's own
  `int8(NaN)`), not the type's minimum; a NaN or an infinity coerced to single stays NaN or Inf, not
  -FLT_MAX and FLT_MAX; a NaN coerced to char is `NaN`, not the C runtime's `-nan(ind)`; a double
  array under `OutputAsDate` is an array of dates, not one the client cannot read.
- **The oracle, runner and comparer:** `k17com.ps1` takes `-UtilPrefix` (MathWorks' `MWComUtil` or
  JGraph's `JGraphComUtil`), `-UtilDll` (the utility library created through its own class factory
  by coclass name under `-Dll`, so the unit tests register nothing) and `-Phase extra`;
  `record-k17d.ps1` builds R2025b's add-in once more and records the extra phase; `run-k17.ps1`
  registers `jgcomutil.dll` for the current user before the oracle, runs all three phases and
  unregisters it in the cleanup; `compare-k17.py` compares `k17_com_extra` too.

## Measured

- **`run-k17.ps1`** (`recordings-jgraph/k17_*.out.txt`, `k17_differences.txt`): excel 1,004 keys, 258 agree, 746 recorded divergences, 0 unexplained; com_preinit 26, 21, 5, 0; com 559 keys, 508 agree, 51 recorded divergences, 0 unexplained;
  com_extra 1,076 keys, 1,021 agree, 55 recorded divergences, 0 unexplained; pe 2,452 keys, 720 agree, 1,732 recorded divergences, 0 unexplained; registry 15, 9, 6, 0. The registry after the run is the one before plus JGraph's component
  category.
- **Tests.** `ExcelAddInServerTests` (7): the add-in's type library; the DLL's resources; the served
  object's members in this process; the K17a oracle against the unregistered DLL and the utility
  library through their class factories, 425 rows equal to R2025b's; the extra phase the same
  way, 810 rows equal; the utility library's type library (nine coclasses, their ids, MathWorks'
  members in MathWorks' order); and the measured conversion rules in this process (coercion's
  rounding and saturation, the formats' nesting by depth, dates in and out, NaN's replacements, an
  `MWStruct`'s `Item` order, an `MWSparse` summed and rounded, an `MWComplex` combined in `Real`'s
  shape, `MWPack`, `MWUnpack` and `MWDate2VariantDate`). The Deploy, Runtime and touched Scripting
  areas: 406 of 406.

## Divergences

- **The utility library is JGraph's** (`JGraphComUtil.*`, its own ids, version 25.2; decision KO10): an error's
  source names `JGraphComUtil.MWUtil25.2` where R2025b's names `MWComUtil.MWUtil25.2`; `IMWEnumVararg` and
  `IMWMethodArgInfo` are not declared; `MWArrayFormatFlags` and `MWDataConversionFlags` can be created on their own
  (R2025b registers them but its class factory answers E_NOTIMPL); every `CreateObject` of `MWUtil` is a new
  object (R2025b's is one per process); the three flags interfaces stay declared in each add-in's own type
  library too (open item 246's question, decided: identical interfaces under one id, so either library's flags
  object serves either).
- **`MWInitApplication` and `MWTerminateApplication` do nothing but keep the Application**: JGraph's add-in
  needs no Excel and its objects live by their references (R2025b's refuses every call before the one and ends
  its host after the other); an object that is no Excel object is refused with JGraph's text (R2025b's with none).
- **`CreateRemoteObject` makes the object of any registered ProgID in this process**; R2025b's asks DCOM and
  fails for all but its own classes.
- **`MWUnpack` into a Range of several cells writes the array over the range resized from its first cell**, as
  an add-in's output does; R2025b writes the first cell alone.
- **A value class filled with what it cannot hold is refused with JGraph's text** (an MWComplex with a logical
  or text part or parts of different counts, an MWSparse with Variant indices, integer values, a matrix for its
  `Array` or indices of another count, an MWStruct field name that is no text); R2025b refuses with no text or
  another.
- **A complex value with integer parts is double**, and **a sparse matrix is double** (a logical MWSparse becomes
  a sparse double; a sparse logical answers `Double()`), as the engine holds them (ADR 0266); a complex value
  whose imaginary part is zero is real (ADR 0316).
- **The MATLAB-wrong rows above**: NaN to an integer class 0, NaN and Inf to single kept, NaN to char `NaN`, a
  double array under `OutputAsDate` an array of dates.
- **Carried from ADR 0316:** a string answers its text (and is written into a Range), an int64 its value, a
  value no VARIANT holds is refused with a message (R2025b writes nothing into a Range and raises no error), a
  refusal of the call's own carries JGraph's text.

## Not measured

A struct array's `Item` with one index per dimension (R2025b's rows asked with the name first, which it refuses;
JGraph takes one index per dimension as MATLAB subscripts), the Cell format over an array of booleans or dates,
`CoerceNumericToType` over a Range of booleans, `MWUnpack` of a one-dimensional typed array into a range of
several cells, `MWInitApplicationWithMCROptions` with a string array in a fresh process (R2025b's second
initialization fails or ends its host), an early-bound VBA client of the utility library, a figure shown
from inside Excel's process, Excel's own unload, a re-entrant recalculation (stage K17g), and what the five
calls that end R2025b's host would answer.

## Still open

Open items 246 (amended) and 247 (what this stage leaves), stages K17e to K17g.
