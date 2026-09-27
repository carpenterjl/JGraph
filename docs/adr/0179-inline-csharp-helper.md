# ADR 0179 — The inline C# helper, `jgraph.net.compile`

## Status

Accepted. Stage I6 of the .NET and shared-library interop plan
(`docs/plans/net-and-shared-library-interop-plan.md`), after stage I5 (ADR 0178, 470570b). One
commit, this ADR's. It ends the .NET half of the plan; stages I7–I9 are the native half.

## Context

A MATLAB user who wants a small piece of C# next to a script has to build a DLL in another tool and
`NET.addAssembly` it, and then restart MATLAB to load a changed build, since MATLAB cannot unload an
assembly (stage 3 found that R2025b `-batch` does not even accept `Unloadable=true`). The plan's user
decisions settled the name (`jgraph.net.compile`, not a `NET.*` name a later MATLAB could claim), and
that it takes files and strings, with no `%{ … %}` block form.

JGraph already carries Roslyn (`Microsoft.CodeAnalysis.CSharp`, through the C# scripting engine), and
the stages before this one built everything an assembly needs once it is loaded: the type catalog,
members, arrays, generics, events and delegates. There is no R2025b answer to record, so the fixture
is JGraph-only and written from the rule.

## Decision

**`jgraph.net.compile(source, Name=Value, …)` compiles C# and answers the `NET.Assembly`.** The types
are visible at once, exactly as those of an assembly `NET.addAssembly` loaded: `IxA.M.Mean3(1, 2, 6)`
right after the call. `jgraph` is a built-in root registered as `NET` is. `which` says
"jgraph.net.compile is a built-in function.". The JGS dialect, which has no dotted names, reaches it
with `feval("jgraph.net.compile", …)`, as it reaches `NET.*`.

**The source.** The first input is text or a string array of texts, and each text is one of two
kinds:

- **A file.** One line ending in `.cs` (no newline, `;` or `{`). It is found as a script's file is
  found: the folder `cd` moved to, the script's folder, then the path. A missing one is
  `JGraph:NET:compile:FileNotFound`.
- **C# source.** Anything else. It is named `<string>` in diagnostics, or `<string N>` when there are
  several.

A file and text can be mixed in one build.

**The options:**

| Option | Default | Meaning |
|---|---|---|
| `AssemblyName` | the first file's name; otherwise `Inline_` and 16 hex digits of the text's hash | the build's name; recompiling under it replaces the earlier build |
| `References` | none | more assemblies to compile against (below) |
| `Unloadable` | `true` | a collectible load context, unloaded when the build is replaced |
| `AllowUnsafe` | `false` | lets `unsafe` code compile |
| `LanguageVersion` | `"latest"` | anything Roslyn's `LanguageVersionFacts.TryParse` reads |
| `Optimize` | `true` | a Release build |

An unknown option is `JGraph:NET:compile:UnknownOption`, which lists the six. A bad value is
`JGraph:NET:compile:InvalidOption`.

**What a build compiles against:**

- the framework JGraph runs on: every managed assembly of the runtime folder, and any framework
  assembly already loaded (the desktop framework in the app);
- every assembly the session added with `NET.addAssembly`, without being named (`JGTest.Invoker.Apply`
  from compiled code);
- each `References` entry: an assembly this session compiled (by its `AssemblyName`), a framework
  name (accepted, already there), a `.dll` path, or the name of an assembly the process has loaded.
  Anything else is `JGraph:NET:compile:ReferenceNotFound`.

At run time the build's load context resolves those same assemblies to the copies the session holds.
An earlier compiled build is referenced only when it is named, so recompiling one build never pulls
its previous version into the next.

**Diagnostics.** A build with errors is `JGraph:NET:CompileFailed`. The message is "C# compilation
failed with N errors:" and then every error, one per line, in the compiler's own layout: a file's
full path, or `<string>`, then `(line,col): error CSxxxx: …`. The path and line are in the message for
the editor to jump to; wiring the app's error link to them is stage I10's.

Each compiler warning is a MATLAB warning, `JGraph:NET:CompileWarning`, with the same layout. A public
top-level type in no namespace compiles, but no dotted name reaches it, so it warns too
(`JGraph:NET:CompileNoNamespace`, "Put it in a namespace.").

**Images are cached per process, assemblies per session.** A build's hash covers:

- its sources and their names;
- its options;
- each named reference (a compiled one by its own hash, a file by its time and length);
- every assembly the session added.

The emitted image is kept process-wide by that hash. Asking again in the same session answers the
same `NET.Assembly` (`a1 == a2`) and warns nothing new. Another session, such as a fresh run in the
app or another test, loads the cached image into a context of its own with no compile. One session's
recompilation therefore never reaches another's objects.

**Recompiling replaces.** A build retires every earlier build of the session that:

- has the same `AssemblyName`; or
- is not referenced by the new build and defines a public type the new build also defines (edited
  text under its default name).

A retired build is taken out of the session's catalog, which is rebuilt from what is left. Its types
leave the process-wide member, delegate and event-handler caches, and its collectible context is
unloaded.

An object of a retired build that the script still holds refuses every use: a member, a conversion,
or being passed to .NET, where it is refused before overloads are matched. The refusal is
`JGraph:NET:AssemblyRecompiled`: "This IxC.Box object belongs to an earlier build of assembly 'IxC',
which was recompiled." A test holds a weak reference to a replaced build's context and sees it
collected once its objects are gone.

## Measured

Release CLI, one process:

| Compile | Cost |
|---|---|
| The first compile in a process (Roslyn warming up) | 1.1 s |
| A new build after that | 16 ms |
| The same build again in the session | 0.4 ms |
| The same build in a new session (the cached image, loaded) | under 200 ms, asserted by a test; no compile |

A script that never calls `jgraph.net.compile` pays one volatile read per .NET member (`Live`) and per
.NET call's arguments, both false until a build is retired.

## What moves

- **New files:** `Net/NetCompiler.cs` (the request, Roslyn, the image cache, the load context,
  retirement) and `JgsBuiltins.NetCompile.cs` (the builtin, sources, options, references).
- **`NetCatalog`:** the session's `Compiled` builds; `Add(assembly, overrides)`, so a compiled build
  wins a full name; `Remove`; and `LoadShared`, split out of `AddFromPath`.
- **Other source files:**
  - `NetObject.Live` refuses a retired build's object.
  - `NetInvoke.Call` and `Construct` refuse such arguments first.
  - `NetSignature`, `NetDelegates` and `NetEventSubscription` gain `Forget`.
  - `which` answers for any built-in package function.
  - `jgraph` is added to `EvalBuiltinNames` and to the catalog.
- **New JGraph-only fixture `jgnet_compile.jgs` (19 lines):**
  - 18 cases in `helpers/ix_compile.m`, plus a JGS `feval` row;
  - `helpers/ix_compile_helper.cs`, the file found on the path;
  - `JGraph.Tests.csproj` copies fixture `.cs` files as content and removes them from `Compile`.
- **Tests:** `NetInteropM179Tests` (6): the image cache across sessions, two sessions' independence,
  the unload, refusals of a replaced build's object, warnings once, and `AssemblyName` validation.
- The `net_*` fixtures are unchanged; no ratchet line is pending on I6 (I8 1 and I9 3 remain).

## Consequences

- A script can keep a C# helper next to itself, as a file or as text, and edit and recompile it
  without restarting. That is the thing MATLAB users of `NET.addAssembly` cannot do.
- A build lives until it is replaced or the process ends. A session that ends does not unload the
  builds it made; a fresh run of the same script loads the cached image again, a small assembly per
  run.
- Compiled code runs in process with the script's trust, as any assembly `NET.addAssembly` loads
  does. It can crash JGraph through `unsafe` code or P/Invoke, exactly as it could crash MATLAB.

## Divergences

- **`jgraph.net.compile` is an extension with no MATLAB counterpart.** Pre-registered in the plan.
  MATLAB compiles no C#; the nearest thing is building a DLL elsewhere and calling `NET.addAssembly`.
- **A compiled build can be replaced and unloaded without a restart.** R2025b cannot unload an
  assembly, and its `NET.addAssembly` takes no `Unloadable` option (ADR 0176). An object of the old
  build is `JGraph:NET:AssemblyRecompiled` in JGraph; MATLAB has no such state.
