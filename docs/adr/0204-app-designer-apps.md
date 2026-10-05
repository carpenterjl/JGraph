# ADR 0204 — App Designer apps: `matlab.apps.AppBase`, the `.mlapp` file, and what a handle is

## Status

Accepted, 2026-10-04. Stage U7 of the app-building plan
(`docs/plans/uifigure-app-building-plan.md`, gitignored). It stands on ADR 0202 (U5: the
`uifigure` components) and ADR 0203 (U6: a class that inherits and members with access). Saving
an edit back into an `.mlapp` is U7b.

## Context

App Designer writes one kind of file. Its code is a class,

```matlab
classdef MyApp < matlab.apps.AppBase
    properties (Access = public)
        UIFigure  matlab.ui.Figure
        OKButton  matlab.ui.control.Button
    end
    ...
            app.OKButton.ButtonPushedFcn = createCallbackFcn(app, @OKButtonPushed, true);
    ...
        function app = MyApp(varargin)
            createComponents(app)
            registerApp(app, app.UIFigure)
            runStartupFcn(app, @(app)startupFcn(app, varargin{:}))
            if nargout == 0
                clear app
            end
        end
```

and the file is an `.mlapp`: an OPC package, a zip, whose document part holds that class as text.
"Export to .m" writes the same text under another class name. After U6 the class parsed up to the
first typed property and no further, the superclass did not exist, and an `.mlapp` was a file
`what` listed and nothing ran.

R2025b's behaviour was recorded before any code changed: three parity fixtures (`u7_types`,
`u7_app`, `u7_mlapp`; 300 lines) over four app classes and seven `.mlapp` files in
`fixtures/helpers`, the files built by
`tools/matlab-checklist/ui-probes/u7/mlapp/build_mlapp.py`, and three probes beside it
(`u7_classes`, `u7_small`).

## Decision

### A handle is a number that knows its class (decision Q2)

`class(h)` still answers `'double'` (ADR 0051). What a number that names a live object now also
answers is R2025b's class of that object and every class it derives from:

- `isa(h, name)` is true for the object's class and each superclass, for one handle or an array
  of nothing but handles. It is asked only when `name` is a class some graphics object can be, so
  `isa(x, 'double')` and `isa(x, 'numeric')` cost and answer what they did.
- The table (`JgsGraphicsClasses`) is generated from a recording: `superclasses` of one object of
  each of 74 kinds, plus `isa` against the bases MATLAB builds in and `superclasses` does not list.
  `isa(ax, 'matlab.graphics.axis.AbstractAxes')` is true in R2025b and the name is in no
  `superclasses` answer, which is why the second question is asked. Names in an `.internal.`
  package are left out.
- A `uiaxes` is a `matlab.ui.control.UIAxes` and a `matlab.graphics.axis.Axes`
  (`AxesModel.IsUiAxes`: the axes placed by its outer box, which nothing but `uiaxes` asks for).
- **A property typed with a graphics class** takes handles of that class or of one derived from
  it, one or an array, and refuses the rest in R2025b's words: `MATLAB:validation:UnableToConvert`
  ("Error setting property 'Fig' of class 'C'. Value must be of type matlab.ui.Figure or be
  convertible to matlab.ui.Figure.") and, for a number that names nothing,
  `MATLAB:graphics:CannotConvertDoubleToHandle`. The parser reads a class written with its package
  (`UIFigure matlab.ui.Figure`), which it used to stop at.
- **`addlistener(h, 'ObjectBeingDestroyed', @cb)` and `listener(…)`** work on a graphics handle.
  As the object is deleted its listeners run newest first, then its `DeleteFcn`, the parent before
  its children; `isvalid(h)` is already false in both, as measured. A deleted handle is refused
  as `MATLAB:class:InvalidHandle`, any other number as `MATLAB:addlistener:invalidinput`.

### `matlab.apps.AppBase`

It is a class this build supplies (`JgsBuiltinClasses`, as U6's mixins are), written from what
R2025b's does, read and measured: five protected sealed methods, and a `saveobj` and `loadobj`
that refuse.

- **`createCallbackFcn(app, @method, requiresEventData)`** answers
  `@(source,event)executeCallback(ams,app,callback,requiresEventData,event)`, an anonymous function
  over a scope made for the call, so it has two inputs, refuses one or three as R2025b's does, and
  holds the app. Called, it calls `method(app, event)` or `method(app)`; the component is not
  passed on. The method handle was made inside the class, so a private callback is reachable from
  the window (U6).
- **`registerApp(app, fig)`** gives the figure two added properties, `RunningAppInstance` and
  `RunningInstanceFullFileName` (R2025b uses `addprop`; here `JgsHandleEntry.AddedProperties`),
  and listens for the figure's `ObjectBeingDestroyed`, which deletes the app. The figure holds the
  app, so `clear app` leaves it running, and an app whose startup function fails stays with its
  figure, as in R2025b.
- **`runStartupFcn(app, @fcn)`** calls the function with the app, with a figure whose
  `HandleVisibility` is `'callback'` set `'on'` for the length of the call.
- **`getRunningApp(app)`** answers the app of the same class on the newest figure that has one, or
  an empty: a singleton app's constructor returns it instead of the object being built, which is
  then destroyed.
- **`setAutoResize(app, fig, value)`** sets the figure's `AutoResizeChildren`.

Three things the generated constructor needs were missing from the class model and are general:

- **A constructor is told how many outputs were asked for.** `nargout` was always 1 there. Asked
  for none, a constructor that clears its own output makes nothing, and the statement binds no
  `ans`.
- **`app.Button.Text = 'Go'` writes through a handle an object's property holds.** So does
  `s.h.Text = 'x'` through a struct's field, which used to turn the field into a struct and
  leave the button alone, without a word.
- **`matlab.apps.AppBase`, written as an expression, is the class**: it constructs,
  `matlab.apps.AppBase.loadobj(s)` reaches its static method, and `methods`, `properties`,
  `superclasses` and `exist(…, 'class')` know the name.

### The `.mlapp` file

`JgsMlapp.ReadCode` opens the package, follows `_rels/.rels` to the part whose relationship type
ends `/relationships/document`, and joins the text of every `w:t` run in it. Nothing else in the
package is read. Every reader of a code file goes through `JgsMlapp.ReadSource`:

- `JgsFunctionPath.Find` looks for `name.mlapp` and then `name.m` in each folder, so an `.mlapp`
  is found before an `.m` of its name beside it (measured); `which -all` lists both in that order.
- The file index counts `.mlapp` stems, so the shadowing test and `exist` see them.
- `which('app1.mlapp')`, `exist('app1.mlapp', 'file')` and `type app1` look on the path for a code
  file named with or without its extension; `type` prints the code an `.mlapp` holds.
- `run('app1.mlapp')`, `jgraph -batch app1.mlapp` and the editor's Run read the code. **A class
  file run as a file is its constructor called with nothing**, which is what MATLAB's Run does
  with a classdef and is how an app is started; only a file named for its class is one.
- An `.mlapp` that holds a plain class or a function runs as one (measured). A package with no
  relationships is found and refused when called, as `MATLAB:fileio:cantOpenFile` (measured; the
  plan had proposed falling back to `matlab/document.xml`, and R2025b does not).
- **The editor** opens an `.mlapp` as its code, in the MATLAB language, from the file tree, Open
  and `edit`. It does not write the text over the package: Save explains and offers Save As to a
  `.m` file. Putting an edit back is U7b.
- **`app.` in a class file** completes to the class's properties and methods, read from the
  buffer's text, and to the `AppBase` and `handle` methods when the header names them.

## Found on the way

- **A class's method named for a built-in took every call of that name made from the class's own
  code.** `delete(app.UIFigure)` in an app's `delete` ran the app's `delete` on the figure. A
  method the walk finds now takes a call only when one of its objects is among the arguments;
  otherwise the built-in of that name does. Where there is no built-in the old behaviour stands
  (ADR 0203's row).
- **`delete(obj)` written inside the class ran the method's body and nothing else**: no
  listeners, no superclass destructors, and the object was left valid.
- **`ObjectBeingDestroyed` was raised after the class's own `delete`.** R2025b raises it first,
  the object already invalid to `isvalid` in the listener and in `delete` alike, and its
  properties still readable in both (`JgsObject.Destroying`).
- **A property with no default started as a 1-by-0 empty**; it is MATLAB's 0-by-0.
- **`mfilename` in a method answered the script that called it**, because a method was not
  stamped with its file. An error raised in a method now names its frame `Class.method`.
- **`isvalid(h)` was true inside a graphics object's `DeleteFcn`**; R2025b's is false.
- **Too many arguments to a function written in MATLAB** was refused in this build's words with
  no identifier; it is `MATLAB:TooManyInputs`, and `MATLAB:maxrhs` for a class method (measured).
- **`methods` listed in declaration order**; R2025b lists by character code.
- **`exist(name, 'class')` answered 0 for a class file.**

## Consequences

- A run that has a figure numbered 1 open answers `isa(1, 'matlab.ui.Figure')` and
  `isa(1, 'handle')` true. That is ADR 0051's decision met by Q2's; the row is below.
- `JgsGraphicsClasses.Table.cs` is generated. A new kind of object needs a row: add its maker to
  `u7_classes.m`, run the probe, run `gen_classes.py`. An object with no row is a
  `matlab.graphics.Graphics` and a `handle` and nothing more specific.
- `type` moved from the machine builtins to the path builtins, where the search path is.

## Measured

In a window (`-batch U7MlApp.mlapp -showfigures`, UI Automation patterns only): the app opened
from its `.mlapp` with its grid, fields, button and axes; the button's `ButtonPushedFcn`, a
numeric field's and a text field's `ValueChangedFcn` ran the app's private callbacks with
R2025b's event data; closing the window ran `CloseRequestFcn`, which deleted the app and the
figure, and the process ended.

## Not measured

- **The editor opening an `.mlapp`** was not checked in a window: the only ways to open a file
  there are a dialog and the prompt, and both need typing. The document model and the reader are
  unit-tested.
- **A real App Designer file.** The fixtures' `.mlapp` files are built from text and hold no
  `appModel.mat`. Research C read 44 shipped apps and found the same three parts in each; none of
  them is in the repository.

## Divergences

- **`class(h)`, `isobject(h)` and `isa(h, 'double')` answer for a number.** A handle is a
  `double` here (ADR 0051): `class` is `'double'`, `isobject` false and `isa(h, 'double')` true,
  where R2025b answers the object's class, true and false.
- **A whole number that is an open figure's number is that figure to `isa`.**
  `isa(1, 'matlab.ui.Figure')` and `isa(1, 'handle')` are true while figure 1 is open; R2025b's
  are false for the number 1.
- **`gobjects` makes zeros, which are no class of graphics object.** `isa(gobjects(1),
  'matlab.graphics.Graphics')` is false here.
- **A property typed with a graphics class holds an empty `double`, and takes `[]`.** R2025b's
  default is an empty of the class, for which `isa` is true, and it refuses `[]`.
- **A graphics object raises one event a script can listen to.** `ObjectBeingDestroyed` is
  heard; any other event name is refused as not defined for the class, in R2025b's sentence, and a
  property listener (`addlistener(h, 'XLim', 'PostSet', …)`) is refused as unsupported.
- **A `uifigure` is never the current figure.** With `HandleVisibility` `'on'` R2025b's is what
  `gcf` and the root's `CurrentFigure` answer; here `findobj` finds it and those two do not.
- **An unknown property read by a dot on a figure is refused in this build's words**, without
  R2025b's `MATLAB:noSuchMethodOrField`.
- **`func2str` of a callback from `createCallbackFcn` keeps its spaces**, as every anonymous
  function's text does here (open item 54).
- **An app's figure has `RunningAppInstance` and `RunningInstanceFullFileName` unlisted.** They
  are read, written and found by `isprop`; `get(fig)` and `properties(fig)` do not show them.
- **An `.mlapp`'s code is read with line feeds**, whatever the package stored.
- **A class file run as a file constructs the class.** `run('C.m')` on a classdef file
  constructs it here, as the editor's Run does in both; what R2025b's `run` does with one was not
  measured beyond that it raises no error.

## Still open

- Open item 55: a `uifigure` as the current figure when its handle is visible.
- Open item 56: the other events of graphics objects, and property listeners on them.
- U7b: saving an edit back into the `.mlapp`.
