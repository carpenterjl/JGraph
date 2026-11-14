# ADR 0353 — A metaclass lists its members as R2025b's metadata objects, a property converts its class before its size, and validateattributes answers in R2025b's catalog words

## Status

Accepted, 2026-11-13. Open items 196, 309 and 310: the compiler and SDK plan's stage K9 leftover
(ADR 0265) and two lists of leftovers from wave 2 (ADR 0348).

## Context

- **Item 196.** `metaclass(x)`, `?C` and `meta.class.fromName` answered a struct whose `MethodList` and
  `PropertyList` were cell columns of names, so `{mc.MethodList.Name}` failed. R2025b's
  `matlab.metadata.Class` has eighteen properties. Its five member lists are columns of
  `matlab.metadata.Method`, `Property`, `Event`, `EnumerationMember` and `Class`. Probes `p196` and
  `p196b` (R2025b 25.2, `-batch -noFigureWindows`) read every property of every member of eight classes:
  - a value class with seven property blocks of mixed attributes and four method blocks;
  - a handle class with events and its own `delete`;
  - a value subclass and a handle subclass;
  - an enumeration and an abstract handle class;
  - a class with list access and validators;
  - `handle` itself, and a class in a package.

  They found these rules:
  - **Methods.** Each `methods` block's methods are listed last first, and the blocks in file order. A
    property's accessors are not methods. Private, hidden, static and abstract methods are listed too.
    - When the class writes no constructor, the one R2025b supplies comes next, abstract class or not.
      Its inputs are `varargin` when a superclass constructor takes an argument, and none otherwise.
    - Then `empty` (static, hidden, "Returns an empty object array of the given size").
    - An enumeration then lists seventeen conversions: `char` to `isequaln`, `colon` hidden, each with
      its description.
    - Then each superclass's own list, in header order, less its constructor, its `empty`, its private
      methods and every name the class has itself.
    - `handle`'s list is 33 long: the comparisons, `delete`, `isvalid` (sealed), `findprop`, `notify`
      twice, and `addlistener` and `listener` ten times each (the last five pairs hidden). Then
      `findobj` and `empty`.
  - **Properties and events.** The class's own come first, in file order and private ones included.
    Then each superclass's that are not private. A handle class ends with `ObjectBeingDestroyed`.
  - **Attributes.**
    - A constant's `SetAccess` is 'none'. A constant and a dependent property are `NonCopyable`.
    - A list access is a row cell of metaclasses.
    - `DefaultValue` is the default evaluated. Without one it is `MATLAB:class:NoDefaultDefined`.
    - `GetMethod` is the getter's handle, or an empty function handle.
    - `Validation` is empty when the line declares nothing. Otherwise it holds `Class`, `Size` and
      `ValidatorFunctions`. `Size` is a row of dimension objects: a `FixedDimension` (uint64 `Length`),
      an `UnrestrictedDimension` for `:`, an `ArrayDimension` array when they mix, and 1-by-0 for none.
  - **Descriptions.** A member's `Description` is the first line of its help block, without a first
    word that is its own name in any case. `DetailedDescription` is the other lines as help reads them.
    A property's comes from the block above its line, else the comment at its end. A constructor
    without help reads the class's.
  - **The class.** `Namespace` is the package's metaclass, or a 0-by-0. `Aliases` is a 0-by-1 string.
    An enumeration's `InferiorClasses` holds `string`. `fromName` of no class is a 0-by-0
    `matlab.metadata.Class`. A write to any member is `MATLAB:class:SetProhibited`.
- **Item 309.**
  - A property's size was checked before its class. So a char row given to a `(1,1) string` property
    was refused, where R2025b converts it.
  - A typed property with no default failed at construction.
  - `validateattributes` took only a cell of class names, answered in its own words, and did not know
    `scalartext`.
  - `isvalid` of a value object raised with no identifier.

  Probe `p309` ran 55 property writes and 104 `validateattributes` calls. Probe `p309b` made 24 classes
  with one typed property and no default. R2025b's message catalog
  (`resources/MATLAB/en/validateattributes.xml`) gives every sentence. `validateattributes` is built in
  there, with no `.m` to transcribe.
- **Item 310.**
  - secretID's compact display in a struct field or a cell was not asked of the class.
  - `exist('secretID')` is 5 here and 2 in R2025b. `which` says built-in. (`nargin('secretID')` already
    agreed.)
  - `help` with several names was refused. A property commented both above and at its end was not
    measured.

  Probes `p310` and `p310b` measured them.

## Decision

### The metaclass (`JgsBuiltins.MetaClasses`)

`MetaClassOf` builds a `MetaModel` of each class it meets. A model is made from the class's
declaration and its file's comments (`FromClass`), from a static table (`HandleModel`), or from a name
alone. Each model is made once per call (`MetaContext`). The context then renders the model as a struct
carrying R2025b's class name, its fields in R2025b's order:
- `matlab.metadata.Class` (`MetaClassFieldNames`);
- `Method`, `Property`, `Event` and `EnumerationMember`;
- `Validation`, and the `FixedDimension`, `UnrestrictedDimension` and `ArrayDimension` dimension classes;
- `Namespace`.

A member's `DefiningClass`, an entry of `SuperclassList`, `InferiorClasses` and a list access are each
the class's own metaclass one level down (`MetaDepth` 2). The member lists stop at the third level,
where R2025b's handles point back at each other. Repeated metaclasses are shared (`JgsValue.Share`).
Help blocks are read with the help reader's own helpers (`HelpBlock`, `BlockAfterDeclaration`,
`CodePart`), and `MetaDescriptionOf` takes the first line apart.

Other paths:
- `metaclass` of a device keeps stage K20's answer through `MetaClassOfDevice`.
- Any other value goes through `MetaClassOfValue`, which lists the names `methods` and `properties`
  give. A value with no listing here, such as a number, lists none.
- `findprop` of a user property answers the metaproperty its class's list holds.
- `?handle` and `meta.class.fromName('handle')` answer handle's metaclass (`MetaClassOfHandle`).
- An unknown name answers `EmptyMetaClass`.

`JgsClass` gained `OwnMethods` (every method the class defines, in declaration order) and
`EvaluateForMeta`, which turns a validator into its handle in the class's file. `IsMetadataValue` marks
these values:
- the interpreter's member write refuses them in R2025b's words, as it refuses a component's event data;
- `IndexStruct` keeps their class name on an element, so `mc.MethodList(1)` is still a metamethod (the
  old body is now `IndexStructElements`).

### A property converts its class, then fits its size (`JgsClass.Check`)

In a class file a property's value goes through `DeclaredArgument.Check`, the path K9 gave an
`arguments` line. So it is converted first and then fitted:
- a scalar expands to a fixed size (5 to `(1,3)` is `[5 5 5]`);
- a vector turns to the declared orientation;
- `[]` fits a `:`.

Refusals keep R2025b's prefix, "Error setting property 'P' of class 'C'.". The JGS dialect keeps the
older check.

### A typed property's implicit default (`JgsClass.DefaultOf`, `JgsBuiltins.ImplicitPropertyDefault`)

With no default, a property declared with a built-in class starts as that class's zeros at the declared
size. A `:` holds none, and no size is 0-by-0. The zeros are false, character 0, "", `{[]}`, or a scalar
struct with no fields. A `(1,1)` property typed with a user class is one object: an enumeration's
first member, or what the constructor makes of no arguments. Its validators run on that value, refused
as "Error using implicit default value of property 'P' of class 'C'.". A class this build makes no
such value of keeps the older reading: datetime, duration, function_handle, table, categorical,
containers.Map.

### validateattributes (`JgsBuiltins.ValidateAttributes`)

It is rewritten from the catalog and the probe:
- The classes are a cell of names, a string array, or a char row naming one. Each is tested as `isa`
  tests it, so `numeric`, `float`, `integer` and a superclass work.
- A failing class is `invalidType`, with `numeric` spelled out as its ten classes. The "Instead its type
  was" sentence is left off for text values, as R2025b leaves it.
- The attributes are a cell or a string array, read in order. Each refusal has its own identifier and
  sentence. An attribute the build does not know is a warning, and the check goes on.
- The input is named in four ways ("input", the variable, "input number N", "input number N, x,").
  A function name puts itself in the identifier (`MATLAB:f:expectedPositive`).
- The fourth to sixth arguments are refused in the catalog's words.

It is registered with `KeepsStringArguments`, so `""` is a nonempty string.

### isvalid, help, secretID

- `isvalid` of a value object, a struct or a cell raises `MATLAB:UndefinedFunction`.
- `help` with several names is one topic, the names joined by spaces (`HelpTopic`). It answers empty
  text, and printed it is "a b not found." with R2025b's lines.
- `NestedDisplay` asks `JgsBuiltinClasses.CompactDisplay` first. For a secretID that gives R2025b's
  fragment: `kname (secretID)`, `[kname    other] (secretID)`, `[2×1 secretID] (secretID)`, and in a
  cell `[kname] (secretID)` and `2×1 secretID (secretID)`. An empty name shows as `<missing>`.

## Consequences

- New parity fixture `oi_metaclass_validation`: 850 rows from R2025b 25.2, all agreeing but one div row.
  - About 520 rows on member lists: eight classes, 67 rows each.
  - About 40 rows on the objects themselves, and a package's namespace.
  - Property defaults and writes: 17 + 54 rows. Implicit defaults: 16 rows.
  - `validateattributes`: 103 calls. `isvalid`: 6 rows. secretID and help: 13 rows.
- New tests: `MetaClassValidationTests` (20 cases).
- `MatlabFunctionsFilesAndErrorsTests.ValidateattributesChecksClassAndAttributes` expected the function
  name in the message. R2025b puts it in the identifier, and the test now asks for that.
- A property's value in a class file is now converted where it was refused: a char row to a string, a
  scalar to a fixed size, a column to a row. A typed property with no default now constructs.

## Divergences

- **`exist('secretID')` is 5 here and 2 in R2025b**, and `which secretID` says built-in. This is the
  standing rule for a built-in that is a file in MATLAB (`exist('mean')`, M109). Fixture row `sid_exist`.

## Still open

- `DefaultValue` of a property with no default is a 0-by-0 here. R2025b raises
  `MATLAB:class:NoDefaultDefined`, or `NoDefaultPermitted` for an abstract property. A struct field
  cannot raise.
- An empty `GetMethod` or `SetMethod` is a 0-by-0 double here, and an empty function handle in R2025b.
  This build has no empty function handle.
- A call validator's handle prints with this build's spacing (`@(X) mustBeInRange(X, 0, 10)`). R2025b
  writes none.
- `GetObservable` is always false, since the parser keeps no such attribute.
- The metaclass of a built-in value lists the names `methods` gives. R2025b lists 279 for a double,
  with signatures. A number, a cell and a char list none here.
- The member lists stop two levels down. `m.DefiningClass.MethodList(1).DefiningClass` has no lists.
- A metadata member is a struct: `isfield(m, 'Name')` and `struct(m)` answer as for a struct, and an
  unknown property is a field's refusal. `mc == ?C` compares values, not handles.
- `meta.class.fromName('double')` is a 0-by-0 here. Not measured: the metaclass of a built-in class by
  name, an enumeration with a superclass, multiple inheritance, and `event.EventData`.
- The implicit default of a `(1,1)` datetime, duration, table, categorical or containers.Map property,
  and R2025b's `MATLAB:class:DefaultPropertyValueRequired` for a function handle.
- The validateattributes warnings' identifiers were not measured.
- secretID's compact display shows inside this build's own struct and cell display, which is not
  R2025b's (ADR 0232's divergence).
