# Runtime window checks

Window checks of the JGraph Runtime (compiler and SDK plan). They open JGraph's own windows only
and drive them through UI Automation patterns (invoke, close), never through synthesized input, so
nothing reaches another window. Each check builds what it runs.

| File | What it is |
|---|---|
| `k5window/` | Stage K5's client (ADR 0261): two deployed components in one process, a uifigure app with a Press button and a plotted figure, on the Windows desktop framework. With no argument the runtime starts its own window thread; with `-wpf-host` the client runs a WPF application that the windows join. |
| `run-k5window.ps1` | Runs the client both ways and `JGraph.Application -batch <script> -showfigures`: the windows' titles, the toolbar's buttons in a deployed program and in the application, a press whose callback runs between the client's calls, `WaitForFiguresToClose` ending as each window closes, and the exit codes. |
| `run-k19window.ps1` | Stage K19's compiler apps (ADR 0274), in the Release build of `JGraph.Application` started with `-r "standaloneApplicationCompiler('<file>')"` and `-r "productionServerArchiveCompiler('<file>')"`: the window's title, main file and status line, Show Code with the `compiler.build` script, Build with its packaging dialog ending in "Process completed successfully" and the program or archive in the task's output folder, the archive's Test Client (Start on a free port, a request, the reply, Stop), and a picture of each window taken with PrintWindow into the work folder. Every control is reached by its automation id through UI Automation patterns; the app is ended with Stop-Process, so nothing is saved. |

```
powershell -File run-k5window.ps1 -WorkRoot <a scratch folder outside the repo>
powershell -File run-k19window.ps1 -WorkRoot <another scratch folder>   # needs a Release build of JGraph.Application and a Debug build of jgraph-mps
```

A WPF client's windows open after the call that made them, and their components after that. UI
Automation answers ahead of that queue, and a window it reads before its components arrive keeps
showing it empty (open item 181), so the client waits for its window thread to be idle before it
says `ready`.
