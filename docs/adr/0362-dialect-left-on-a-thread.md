# ADR 0362 — A dialect a session left on its thread no longer reaches an axes or a MAT-file no script is making

## Status

Accepted, 2026-11-14. Open items 155 and 164 (chips wave 5, lane 5D).

## Context

- **Item 155.** `AxisOffAndViewAngleTests.AnInvisibleAxesGivesItsMarginsBackToThePlotBox` failed in the
  full suite with "496 should exceed 496" and passed alone. 496 is 0.775 of the 640-pixel figure the
  test draws: R2025b's default `Position` width. The test builds a `FigureModel` of its own, with no
  script, yet its axes took the MATLAB plot box (`AxesModel.MatlabPlotBoxMargins`), so hiding the
  rulers gave nothing back.
- **Item 164.** Three `MatFileV5CompletionTests` refusals (`AStringArray_IsRefusedByName...`,
  `ADatetimeAndADuration_AreBothRefusedByName`, `ARefusalReachesInsideACellOrAStruct`) read null in the
  full suite. `MatFileWriter.WhyNotWritable` refuses a string array, a datetime and a duration in the
  JGS dialect and lets MATLAB code write them as object elements (open item 131).
- **The shared state.** Both readers asked `JgsRunningDialect.ThreadIsMatlab`, a `[ThreadStatic]` the
  slot's `Current` setter writes. Building a slot writes it too (`JgsBuiltins.CreateGlobals` builds
  one, and the `Interpreter` constructor over a workspace that has none), and `Interpreter.DialectContext` restores the
  interpreter's previous dialect, which is the session's. So a thread that once built a MATLAB session
  - a test's `InitializeAsync` creating a `MatlabScriptEngine` session, `JgsRunner` run on the test
  thread - reads MATLAB from then on, for anything run on it. xUnit 2 runs tests on a small pool of
  reused worker threads, so whichever test ran there last decided. The plot-box hook
  (`JgsGraphicsProperties.GiveMatlabPlotBox`, on `GraphObjectLifecycle.Adopted`) is passed by every
  axes added anywhere in the process once `JgsGraphicsProperties`' static constructor has run.
- **Why the code, not the tests.** The app builds its prompt session on the UI thread
  (`JgsReplSession.Build` from the constructor) while every script runs on a fresh `ScriptThread`. So in
  the app the UI thread reads MATLAB after a MATLAB session is built, and host code there - a `.graph`
  document read, an axes made from a window - took R2025b's margins, the 0.9 furniture font and the
  automatic tick-label angles for no script. On the script thread the flag was only ever true inside a
  body the interpreter entered, so nothing a script does depended on the leftover.

## Decision

### `ThreadRunsMatlab`: MATLAB code running now (`JgsRunningDialect.Enter`, `Leave`)

`JgsRunningDialect` counts, in a second `[ThreadStatic]` (`t_entered`), the bodies of code the thread is
inside: `Interpreter.EnterDialect` calls `Enter`, and `DialectContext.Dispose` calls `Leave`. Every entry
is a `using` on the same thread (a function body, an anonymous body, a script run as the program or by
name, a class default and validator, a constructor, the ODE body, the debugger's prompt).
`ThreadRunsMatlab` is `t_entered > 0 && ThreadIsMatlab`: false outside every run, whatever session the
thread built.

### The two readers host code reaches ask it

`GiveMatlabPlotBox` and the MAT-file writer's two dialect tests (the string-array, datetime and
duration refusal in `WhyNotWritable`, and the calendarDuration object element in the element writer)
ask `ThreadRunsMatlab`. An axes made by a MATLAB script, and a `save` from one, are unchanged: they are
made inside a run.

`ThreadIsMatlab` itself is unchanged, and so are its other 190 readers: they are inside builtins, and
the app's workspace display on the UI thread (`JgsValue`'s nested summaries) reads the session's dialect
through it on purpose. Narrowing it everywhere would need the full suite and a look at each host path.

## Consequences

- New tests `AxisOffAndViewAngleTests.AnAxesMadeOutsideAnyRunIgnoresADialectLeftOnTheThread` and
  `MatFileV5CompletionTests.TheRefusalsIgnoreADialectLeftOnTheThreadOutsideAnyRun` build a MATLAB slot on
  the test thread, as a session does, and run the failing assertions under it. Both failed before
  (`Assert.Null() Failure: ... has a value`, `Assert.NotNull() Failure: Value is null`) and pass now.
- In the app, a figure read from a `.graph` file or made from a window on the UI thread keeps JGraph's
  tight plot box whether or not a MATLAB session was built there (a saved document still brings its own
  `PlotBoxMargins`).
- A builtin called from C# with no interpreter entry (the SDK's `Feval` of a builtin in
  `JGraph.Runtime.Native`, a test calling a builtin's delegate directly) that makes an axes or writes a
  MAT-file now gets the JGS reading where it got the leftover; a user function called that way enters
  its body and is unchanged.
- One thread-static increment and decrement per body entry.

## Divergences

None opened or closed.

## Still open

- **`ThreadIsMatlab` still outlives its session** for its other readers. They are reached from a run,
  or from the UI thread on purpose, today; a builtin reached from host code on a thread that built a
  MATLAB session reads MATLAB.
