# ADR 0203 — A class that inherits, and members with access

## Status

Accepted, 2026-10-04. Stage U6 of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It extends ADR 0068 (M68: `classdef`) and
ADR 0167 (V6: events, accessors, `Dependent`), and is the language half of what an App Designer
class needs; `matlab.apps.AppBase` and the `.mlapp` loader are U7.

## Context

A class here could inherit from `handle` or `event.EventData` and from nothing else, and every
member was public: the parser refused `classdef B < A` and any `Access` other than `public` by
name. That was ADR 0068's decision, taken so that nothing half-worked. It now stood between the
app-building plan and every App Designer file, which is written

```matlab
classdef MyApp < matlab.apps.AppBase
    properties (Access = public)   % the components
    properties (Access = private)  % the app's state
    methods (Access = private)     % the callbacks, wired with @ButtonPushed
```

and between JGraph and most classes people write for themselves.

R2025b's behaviour was recorded before any code changed, as five parity fixtures
(`u6_inherit`, `u6_access`, `u6_handles`, `u6_attrs`, `u6_more`; 493 lines) over 58 small class
files in `fixtures/helpers`.

## Decision

### A class is built over its superclasses

- The header takes any number of superclasses joined with `&`: class files on the path, `handle`,
  `event.EventData`, and the mixins this build supplies.
- A `JgsClass` holds **every** property, method and event an instance has: its own first, then
  each superclass's. Each member still points at the class that declared it (`Owner`), whose file
  its body, its default, its validators and its accessors run in. Nothing outside `JgsClass`
  walks a hierarchy.
- The orders are R2025b's: `properties` lists the class's own first; `superclasses` lists each
  superclass followed by its own (`{'U6MixA'; 'handle'; 'U6MixB'}`).
- The refusals made when a class is built are R2025b's, raised where the class is first used:
  a superclass that is missing (`MATLAB:class:InvalidSuperClass`), `Sealed`
  (`MATLAB:class:sealed`) or closed by `AllowedSubclasses`; handle and value superclasses mixed
  (`MATLAB:class:inconsistentSuperclasses`); a property a superclass already has
  (`MATLAB:class:RedefinedProperty`); an override of a `Sealed` method; a method two superclasses
  both define (`MATLAB:class:methodAmbiguous`).
- An abstract class, or one that still has an abstract method or property, cannot be made
  (`MATLAB:class:abstract`, in R2025b's two sentences). An abstract method is its signature, with
  no body.

### Constructors and destructors

- `obj@Super(args)` in a constructor runs the superclass's constructor on the object being built.
  `obj = obj@Super(args)` and the bare statement mean the same.
- A superclass the constructor does not call by name is constructed first with no arguments.
- A class with no constructor hands its arguments to its one superclass (measured: `U6Cube(5)`).
- The object is of the class being made throughout: `class(obj)` inside a superclass's
  constructor answers the subclass.
- Deleting a handle object runs its class's `delete` and then each superclass's (measured:
  `delMid;delBase`). A subclass's destructor does not replace its superclass's. The same chain
  runs when the last holder goes (ADR 0171).

### Methods dispatch on the object

- `name@Super(obj, …)` in a method calls the superclass's method of that name.
- A method called by bare name from its own class's code still dispatches on the object, so a
  superclass method that calls `area(obj)` runs the subclass's `area`. Until now a class's own
  methods were found lexically first, which was invisible while no class could inherit.
- A private method is not overridden: where a subclass defines a method with the name of its
  superclass's private one, the superclass's own code still calls its own (measured).
- A static method may be read off an instance (`obj.kind()`), as R2025b allows.
- `InferiorClasses` decides which class answers an operator or a call that mixes two, by exact
  class: a subclass of an inferior class is not inferior (measured).

### Access

- Properties take `Access`, `GetAccess` and `SetAccess`; methods take `Access`; events take
  `ListenAccess` and `NotifyAccess`. Each is `public`, `protected`, `private`, a class list
  (`?Name` or `{?A, ?B}`), or for `SetAccess` `immutable`.
- **Access is judged by the class the running code belongs to**, found by walking the scopes
  outward from the running frame to the nearest class scope. A method's frame, a local function
  of the class file and an anonymous function made in either all sit under that scope.
- The rules were measured:
  - private is the class alone;
  - protected is the class and its subclasses, and a superclass that declares the same member
    (so a superclass method can call a protected method a subclass overrides);
  - a list is the class, the classes it names and their subclasses, and **not** the class's own
    subclasses;
  - immutable is the class's own constructor.
- A superclass's private member is no member of the subclass at all: reaching for it is
  `MATLAB:noSuchMethodOrField` or `MATLAB:UndefinedFunction`, not a refusal of access.
- The refusals are R2025b's identifiers and sentences, doubled quotes included:
  `MATLAB:class:GetProhibited`, `MATLAB:class:SetProhibited` (two sentences: "read-only" when the
  caller can read the property, "not supported" when it cannot), `MATLAB:class:MethodRestricted`,
  `MATLAB:class:ListenRestricted`, `MATLAB:class:NotifyRestricted`.
- A write that goes past a property (`obj.p(2) = v`, `obj.s.f = v`) reads it and then sets it, so
  it needs both accesses, the read first (measured). When what the property holds is a handle, the
  write lands in the handle and the property is only read.
- Listings show what anyone may reach: `properties`, `fieldnames` and the display leave out a
  property that is not publicly readable or is `Hidden`; `methods` and `ismethod` leave out a
  method that is not public or is `Hidden`; `events` leaves out an event that is not publicly
  listenable or is `Hidden`. `isprop` is true for any property (measured).

### Handles to methods

- `@name` written inside a class is a handle to the method, private ones included. It keeps the
  class's access wherever it is called from: the handle records the class whose code made it.
- `@obj.method` is the anonymous function R2025b makes of it,
  `@(varargin)obj.method(varargin{:})`. It captures the object and so keeps it alive.
- An anonymous function made inside a class runs with the class's access.
- A handle made in a script is held to the script's access, even when it is called from inside
  the class.
- With a class loaded, `@name` for a name nothing answers yet is made rather than refused: it may
  be a method of the object it is later called with.

### Attributes

- Class: `Sealed`, `Abstract`, `Hidden`, `HandleCompatible`, `InferiorClasses`,
  `AllowedSubclasses`.
- Property: `Constant`, `Dependent`, `SetObservable` (already there), and now `Abstract`,
  `Hidden`, `Transient` (`save` leaves it out), `NonCopyable`, `AbortSet` (a write of a value
  `isequal` to the one held does nothing).
- Method: `Static` (already there), `Abstract`, `Sealed`, `Hidden`.
- Event: `Hidden`.
- An attribute is written bare, `= true`, or `= false`. One this build does not know is still
  refused by name.

### The mixins

- `matlab.mixin.Copyable` gives `copy` and the protected `copyElement`. The copy holds the same
  property values, shares any handle a property holds, and starts a `NonCopyable` property from
  its default. A class customises it by overriding `copyElement` and calling
  `copyElement@matlab.mixin.Copyable(obj)`.
- `matlab.mixin.SetGet` gives `set` and `get` by property name, matched without regard to case
  and by any unambiguous beginning.
- Both are ordinary `JgsClass` objects built from a declaration made in C# (`JgsBuiltinClasses`)
  whose methods carry a native body. A subclass inherits, overrides, lists and reaches them by the
  rules every other class follows. `matlab.apps.AppBase` joins them in U7.

### What the stage changed elsewhere

- An unknown member of an object is refused in R2025b's words (`MATLAB:noSuchMethodOrField`,
  `MATLAB:noPublicFieldForClass`), and a call nothing answers for an object names its class
  (`Undefined function 'f' for input arguments of type 'C'.`).
- A constructor given too many arguments is `MATLAB:TooManyInputs`.
- `isequal` compares two objects: one object, or one class with equal properties.
- A property typed with a class takes an instance of a subclass, and converts anything else
  through the class's constructor, as R2025b does.
- A refused property value keeps the validator's identifier, and a wrong size is
  `MATLAB:validation:IncompatibleSize`.
- A char row in an object's display is quoted.
- `superclasses` is new; `metaclass` and `?Name` answer `Abstract`, `Sealed`, `HandleCompatible`
  and `SuperclassList`.

## Consequences

- An App Designer class's language needs are met except for its superclass, which is U7.
- `classdef` files written for MATLAB with inheritance or private members now load.
- A script that relied on a class's private member being reachable is now refused, as in MATLAB.
  No shipped script did.
- A public member costs what it did: one dictionary lookup. The class that is running is looked
  for only when a member is not public.

## Measured

Everything in the five fixtures: 493 lines, all `exact`, agreeing in both representations.

## Not measured

- Whether an immutable property may be set by a subclass's constructor. Here it may not.
- Whether `set` and `get` of `matlab.mixin.SetGet`, called from inside the class, reach its
  private properties. Here they have the caller's access.
- Whether a superclass may reach a protected member a subclass introduces under a new name. Here
  it may not.

## Divergences

- **A subclass cannot reuse the name of a superclass's private property.** R2025b keeps the two
  apart; here one name is one slot, and the class is refused as R2025b refuses a property that is
  not private.
- **A superclass constructor may be called anywhere in the constructor.** R2025b requires the
  call at the top level of the constructor, before the object is used, and refuses the class
  otherwise.
- **Only two mixins are supplied.** A class that names `matlab.mixin.Heterogeneous`,
  `matlab.mixin.CustomDisplay`, `dynamicprops` or any other MATLAB class is refused, and the
  refusal says the class is not supplied.
- **`GetObservable`, `ConstructOnLoad` and the remaining attributes are refused by name.** So are
  an `enumeration` block, a method whose body is in another file, class folders and packages.
- **A class's methods are found by bare name inside its own file whatever the arguments.**
  R2025b dispatches on the arguments alone (ADR 0068); a helper called with a plain number is
  found here and is undefined there.
- **A handle to a bare method name reads `@name` in `func2str`**, as every named handle does here
  (ADR 0149); R2025b's reads `name`.
- **A subclass built before its superclass's file was edited keeps the old superclass** until
  its own file is read again. Each run starts fresh, so this shows only within one session.
- **`findprop`, `findobj` and the ordering comparisons of a handle object are listed and not callable.**
  `methods` and `ismethod` name the thirteen methods every handle class has in R2025b; of those
  `findprop`, `findobj`, `lt`, `le`, `gt` and `ge` have no body here (open item 52).
- **`metaclass` is still a plain description**: names, the four flags and the superclasses, not
  R2025b's `meta.property` and `meta.method` objects.

## Still open

- `matlab.apps.AppBase` and typed empty graphics properties (U7).
- Object arrays, `enumeration`, packages and class folders, which ADR 0068 left out and this
  stage did not need.
