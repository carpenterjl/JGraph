# Runtime window checks

Window checks of the JGraph Runtime (compiler and SDK plan). They open JGraph's own windows only
and drive them through UI Automation patterns (invoke, close), never through synthesized input, so
nothing reaches another window. Each check builds what it runs.

| File | What it is |
|---|---|
| `k5window/` | Stage K5's client (ADR 0261): two deployed components in one process, a uifigure app with a Press button and a plotted figure, on the Windows desktop framework. With no argument the runtime starts its own window thread; with `-wpf-host` the client runs a WPF application that the windows join. |
| `run-k5window.ps1` | Runs the client both ways and `JGraph.Application -batch <script> -showfigures`: the windows' titles, the toolbar's buttons in a deployed program and in the application, a press whose callback runs between the client's calls, `WaitForFiguresToClose` ending as each window closes, and the exit codes. |

```
powershell -File run-k5window.ps1 -WorkRoot <a scratch folder outside the repo>
```

A WPF client's windows open after the call that made them, and their components after that. UI
Automation answers ahead of that queue, and a window it reads before its components arrive keeps
showing it empty (open item 181), so the client waits for its window thread to be idle before it
says `ready`.
