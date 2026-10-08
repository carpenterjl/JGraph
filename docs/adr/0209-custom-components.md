# ADR 0209 — Custom components (`ComponentContainer`)

## Status

Accepted, 2026-10-07. Stage U10 of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It builds on ADR 0203 (U6: superclasses,
access, the classdef machinery a built-in superclass slots into), ADR 0204 (U7: `AppBase` as a
class this build supplies, `isa` on handles, `ObjectBeingDestroyed` on graphics objects), ADR 0202
(U5: the `uifigure` components and `uigridlayout`) and ADR 0051 (a graphics handle is a number).

## Context

A custom component is a class written under `matlab.ui.componentcontainer.ComponentContainer`:
the class declares its properties, builds its parts in a protected `setup`, keeps them in step in a
protected `update`, and may declare `events (HasCallbackProperty)`, each of which gives it a
public `NameFcn` property. Research C (`C-app-model.md` §5, `SpinnerGauge.m`) had the outline:
`setup` once inside construction, `update` deferred and coalesced into `drawnow`, `Type` the class
name in lower case, the component gone with its figure.

The object is two things at once, and that is what the stage had to settle: it is an instance of
the script's class — its own properties, its methods, `class(c)` its class name — and it is a
graphics object in a figure, with a `Position`, a `Parent`, a place in its parent's `Children`,
and children of its own that its `setup` made. In JGraph a script's object is a `JgsObject` and a
graphics object is a model object named by a number (ADR 0051).

R2025b runs custom components headless, so the stage was measured first
(`tools/matlab-checklist/ui-probes/u10`): `u10_matrix` (construction order, what owes an
`update`, the object's identity and properties, callback properties, events, the constructor's
forms and refusals, failures, deletion, `SpinnerGauge`) and `u10_more` (children after `setup`,
`findall`, `gcbo`, reparenting, `HasCallbackProperty` on a plain class, the display). What they
showed shaped the stage:

- **`setup` runs inside the constructor**, with the parent already set — a `'Parent'` pair
  included — and before the other name-value pairs are applied; `update` is owed after
  construction and after **any** write to a property of the object (its own, a private one, the
  same value again, an inherited `Tag`, `Position` or `Visible`) except a callback property, and
  runs once however many writes, at the next `drawnow` or `pause` — not at `figure(f)`. A write
  inside `update` owes nothing.
- **A component takes children only while its `setup` runs.** `uibutton(c)` afterwards, a
  `'Parent'` pair naming it, `b.Parent = c` and a `uilabel(comp)` in `update` are all refused
  (`… cannot be a parent of Button.`), and what `setup` made is no one's to find: `c.Children` is
  empty, `allchild(c)` and `findall(c)` reach nothing inside, `findall(f)` lists the component and
  not its parts. A grid it made still takes children afterwards.
- **The object is the graphics object.** `f.Children` holds it (class `U10Probe`), a part's
  `Parent` is it, `gcbo` in its callback is it, `ishghandle`, `isgraphics`, `ishandle` and
  `isa(c, 'matlab.graphics.Graphics')` answer true, `get` and `set` work by name without regard
  to case, and `f.Children(1) == c`.
- **A `HasCallbackProperty` event's property** is public and Dependent, listed after the class's
  own properties and before the 22 inherited ones; it starts `''`, takes what a graphics callback
  property takes and refuses the rest in that property's words; `notify` runs it after the
  event's listeners, with the component and the event data; a callback that fails is reported and
  not raised. On a class that is no component the attribute is refused.
- **The constructor's forms**: an optional parent first, then name-value pairs (names matched
  without regard to case, not by their beginnings; string names; one struct) — an odd count is
  refused before `setup`, a pair that cannot be set after it, each deleting the object (the
  class's `delete` runs); a failing `setup` is refused as `ErrorWhileExecutingSetup` with the
  class's error as its cause, and leaves the component in its figure. With no parent it makes a
  `uifigure`. Another component is no parent; a classic figure, a panel and a grid are.

## Decision

### The class

- `matlab.ui.componentcontainer.ComponentContainer` is a class this build supplies, declared as
  `AppBase` is (`JgsBuiltinClasses.ComponentContainer.cs`): an abstract handle class with the
  abstract protected `setup` and `update`, R2025b's 22 public properties in R2025b's order — each
  Dependent, with native `get` and `set` methods that read and write the component's area, `Type`,
  `BeingDeleted` and `InnerPosition` written by the class alone — a native constructor and a native
  `delete`. A subclass inherits, overrides, lists and reaches them by the rules every class
  follows (ADR 0203), so `properties(c)`, `get(c)`, `isprop`, `metaclass`-free code and
  `comp@matlab.ui.componentcontainer.ComponentContainer(varargin{:})` in a class's own constructor
  all work as written. A class built over it knows it is one (`JgsClass.IsComponentContainer`),
  and `isa` answers for R2025b's graphics superclasses too.
- **`events (HasCallbackProperty)`** is read by the parser and, on a component, makes a public
  Dependent `NameFcn` per event, kept beside the object; on any other class it is R2025b's
  `MATLAB:class:UnrecognizedAttribute`. `notify` runs the callback after the listeners, with the
  area as the object whose callback runs (`gcbo` is the component); a failure is written to the
  error stream (`… Error while evaluating U10Probe ValueChangedFcn.`), not raised.

### The area

- A component stands for a `UiComponentContainerModel` in its figure: a borderless
  `UiPanelModel` with R2025b's defaults (pixels, `[100 100 100 100]`, the uifigure grey) that
  remembers the class's name and answers `Type` as it in lower case. Being a panel, it is laid
  out, drawn, placed in a grid, saved and opened by everything that already handled panels; its
  parts are drawn inside it. It has `InSetup`, which is what lets the makers, the table and tab
  makers and reparenting put something in it — and only then.
- **The object and its area are one handle.** The registry's entry for the area names the object
  (`JgsHandleEntry.Owner`), and `JgsHandleRegistry.TryGet` takes the object as the area's entry,
  so every verb that takes a handle takes the component. The makers and the placement verbs are
  handed the area's number in its place (`JgsComponentContainers.AsHandles`). The other way, a
  single handle read — a part's `Parent`, `gcbo`, `gco` — is the object
  (`JgsComponentContainers.ValueFor`), and a dot on the area's number, read or write, is a dot on
  the object (`f.Children(1).Value = 11`). `==` and `isequal` between the object and the area's
  number compare the handle. `get` and `set` on the object read and write its class's properties.
  The interpreter's own dot never takes the object for a handle: its dots are its class's.
- The object is held by its figure (`JgsLifetime.Pin`); the figure's going deletes it (the
  `ObjectBeingDestroyed` hook U7 added for apps), and its `delete` takes the area out — a
  subclass's `delete` first, as every destructor chain runs.

### When `update` runs

- `JgsComponentContainers` keeps the components that owe an `update`, in the order they came to
  owe one. A write to any property of a component — the interpreter's store, or one of the
  inherited properties' `set` methods — owes one, unless the component's own `update` is
  running; construction owes one; a callback property's write does not.
- The owed updates run at `drawnow`, `pause`, `getframe`, the waits, every callback drain, and —
  through `JGraphScriptGlobals.ShowTouchedFigures` — at the end of a script, of a statement at
  the prompt, and of an idle drain in the app, which is when MATLAB draws what a callback left.
  A session runs its own components' updates only (a test host runs several sessions; the app
  runs a Python console beside the prompt), and none once its run is being cancelled. A failing
  `update` is written to the error stream in R2025b's words (`Unable to execute 'update'
  method.` and its cause) and the others still run.

### What the stage changed elsewhere

- `ClassWord` names a UI axes `UIAxes` (R2025b's `UIAxes cannot be a parent.`).
- `allchild`, `findobj` and `findall` do not go inside a component; the registry's liveness walk
  still does.
- `events(c)` of a component ends with `PropertyAdded` and `PropertyRemoved`.
- A failing `setup`'s exception carries the class's error as its `cause`
  (`JgsRuntimeException.Carried`).
- The object display of a component shows the class's own properties and `Position`, then
  "Show all properties".

## Found on the way

- **A component's first draw needs a drain after the script.** The window check found the spinner
  and the gauge empty after `ex8_custom` returned, and a callback's write a step behind: updates
  ran only at the start of a drain. They now also run where the run, the statement and the idle
  drain end.
- **The owed list cannot be process-wide in a test host**: two fixtures in one process ran each
  other's updates. It is keyed by the session's host.
- **R2025b updates a component again after a new figure's first layout** — once at the next
  `drawnow` after reparenting into a figure that had not been drawn, and again a few drains
  later. The fixtures let a new figure settle first; JGraph does not copy the second update.

## Consequences

- Research C's `SpinnerGauge.m` runs unchanged, headless in R2025b and JGraph alike and in
  JGraph's window (`ui-probes/research/apps/examples/ex8_custom.m`; `u10/ex8_drive.m` presses its
  button headless and the two engines print the same lines).
- A class written under `ComponentContainer` is a component wherever a component is taken: in a
  `uifigure`, a classic figure, a panel, a grid cell, as the parent named by a maker inside its
  own `setup`.
- The callable count is unchanged: `ComponentContainer` is a class, not one of the 2,024.

## Measured

- **Headless probes** (`ui-probes/u10/`): `u10_matrix`, `u10_more`, `ex8_drive`, with the classes
  `u10Probe`, `u10Bad`, `u10Ctor`, `u10Half`, `u10Late` and `u10Plain`.
- **Parity fixtures**, recorded with `-noFigureWindows`: `u10_component` (88 lines) and
  `u10_forms` (87), over the helper classes `U10Probe`, `U10Bad`, `U10Ctor`, `U10Half`,
  `U10Late`, `U10Plain` and research C's `SpinnerGauge`. Three lines are recorded divergences.
- **Unit tests** (`ComponentContainerU10Tests`): the area and what `setup` built in it, the order
  owed updates run in, deletion both ways, a failing `update` and callback reported, a failing
  `setup`'s cause; `UiComponentSerializationTests` saves and opens a figure with a component.
- **The JGraph window check**, in the Release app under `-batch -showfigures`, by UI Automation
  patterns only (Invoke, Value, RangeValue), the window captured alone with PrintWindow,
  `workspace.json` unchanged: `ex8_custom`'s spinner and gauge showed 40; "Add 10" took both to
  50 through the component's `update`; the spinner's up arrow took both to 51 through the
  component's own callback, `notify` and the app's `ValueChangedFcn`, which wrote
  "ValueChanged: 51" in the label.

## Not measured

- **R2025b's look** of a component in a window: no MATLAB window session was run.
- **A component in App Designer** (`<userComponents/>` in an `.mlapp`'s metadata): none of the
  shipped apps uses one, and the stage did not make one.
- **`metaclass` of a component** beyond what `properties` lists: R2025b's hidden storage
  properties (`InnerPositionStorage`, `UnitsService`, …) are not modelled.
- **`copyobj` of a component**.

## Divergences

- **A component in a list of handles is its area's number** (ADR 0051): `class(f.Children)` is
  `double` where R2025b's is the component's class, and a `findobj` result is numbers
  (`u10_component`, `f_children_class`). A single handle read — a part's `Parent`, `gcbo` — is the
  object, and a dot on the number reaches it.
- **`isgraphics(c, 'u10probe')` is true**: the component's `Type` is `u10probe`. R2025b answers
  false (`u10_component`, `isgraphics_type`).
- **A `'Type'` pair to the constructor is taken and changes nothing.** R2025b writes it into the
  read-only `Type` (`u10_forms`, `type_pair`).
- **No second `update` after a new figure's first layout** (see Found on the way).
- **The display** is JGraph's object display with R2025b's choice of properties, without
  R2025b's links and alignment.
- **An uncaught failing `setup`** shows R2025b's message; the class's error is in
  `ME.cause` rather than printed under "Caused by:".
- **A component reached through its number answers `class` as `double`**, as every handle does.

## Still open

- `metaclass` of a component, and `copyobj` of one (open item 71).
- A component in App Designer's `<userComponents/>` (open item 72).
