# Hello World app

A small App Designer-style app, and the script that builds it into a standalone Windows
application with JGraph.

| File | What it is |
|---|---|
| `HelloApp.m` | The app class: a `uifigure` with a grid layout, a title, a name field and a Greet button that answers it, a shape drop-down, a frequency field and an amplitude slider that drive a plot in a `uiaxes`, and a status line. |
| `hello_wave.m` | The function the plot calls, in a file of its own, so the build shows a dependency found by analysis. |
| `build_hello_app.m` | Builds `HelloApp.exe` with `compiler.build.standaloneWindowsApplication`. |

## Run it in JGraph

At the JGraph prompt, in this folder:

```
HelloApp
```

Type a name and press Greet; change the shape, the frequency or the amplitude and the wave is
drawn again. The status line names what the app last did.

## Build it

```
build_hello_app
```

writes `output\HelloApp.exe`, a windowed program with the app's code archived inside it. It runs
on a machine with the JGraph Runtime of the same version installed (the JGraph Runtime installer
under `installer\`), or with `JGRAPH_RUNTIME_ROOT` naming a runtime laid out by
`installer\publish-runtime.ps1`. The same script builds the same app with MATLAB Compiler under
R2025b, where the program then needs the MATLAB Runtime.

With `HELLO_LOG` set to a file name, the built program appends each status line to that file,
since a windowed program's output goes nowhere. `tools\runtime-checks\run-hello-app.ps1` builds
the app with a published runtime, starts the program, drives it through UI Automation (a name
typed into the field, Greet, a frequency, Plot), saves a picture of the window and closes it.

The guide's "Deploying code" chapter in `docs\jgs-scripting-guide.html` says what a build makes
and how a deployed app behaves.
