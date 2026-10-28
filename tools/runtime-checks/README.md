# Runtime window checks

Window checks of the JGraph Runtime (compiler and SDK plan). They open JGraph's own windows only
and drive them through UI Automation patterns (invoke, close), never through synthesized input, so
nothing reaches another window. Each check builds what it runs.

| File | What it is |
|---|---|
| `k5window/` | Stage K5's client (ADR 0261): two deployed components in one process, a uifigure app with a Press button and a plotted figure, on the Windows desktop framework. With no argument the runtime starts its own window thread; with `-wpf-host` the client runs a WPF application that the windows join. |
| `run-k5window.ps1` | Runs the client both ways and `JGraph.Application -batch <script> -showfigures`: the windows' titles, the toolbar's buttons in a deployed program and in the application, a press whose callback runs between the client's calls, `WaitForFiguresToClose` ending as each window closes, and the exit codes. |
| `run-k21window.ps1` | Stage K21's legacy deployment project in the app (ADR 0276): a `<deployment-project>` standalone project written by the script, then `JGraph.Application -r "deploytool('<file>')"`: the project upgraded (the file renamed `.bak`, the folder a MATLAB project) and the Standalone Application Compiler window up over the task `<name> Standalone` with the legacy main file, author and version; Build ending in "Process completed successfully" with the program under the legacy `for_testing` folder; a picture of the window. |
| `run-k20window.ps1` | Stage K20's Compiler Task Manager and a compiler app over a project's task (ADR 0275): a project with a standalone task made by the CLI, then `JGraph.Application -r "openProject('<root>'); standaloneApplicationCompiler"`: the manager's heading and task list, Open bringing up the Standalone Application Compiler over the task (its title carrying the task's name, the main file and status line), an author typed into the window landing in the project's definition file at once, Show Code with the task's output folder, Build ending in "Process completed successfully" with the program under `<root>\myApp\output\build`, and New Task making a second task the project then holds; pictures of both windows. |
| `run-hello-app.ps1` | The Hello World app of `examples\hello-app` (ADR 0278) built by a published runtime's `jgraph -batch` with `compiler.build.standaloneWindowsApplication`, then `HelloApp.exe` started with `JGRAPH_RUNTIME_ROOT` naming that runtime: the window's title label, the first plot's status line, a name set through ValuePattern and Greet invoked, the greeting, a frequency set and Plot invoked, the status line again, a picture of the window with its `uiaxes` plot, the window closed through WindowPattern, exit code 0, and the seven lines the app logged through `HELLO_LOG` ending in `closing` (the app's `delete` method, run after the user closed the window). |
| `run-k19window.ps1` | Stage K19's compiler apps (ADR 0274), in the Release build of `JGraph.Application` started with `-r "standaloneApplicationCompiler('<file>')"` and `-r "productionServerArchiveCompiler('<file>')"`: the window's title, main file and status line, Show Code with the `compiler.build` script, Build with its packaging dialog ending in "Process completed successfully" and the program or archive in the task's output folder, the archive's Test Client (Start on a free port, a request, the reply, Stop), and a picture of each window taken with PrintWindow into the work folder. Every control is reached by its automation id through UI Automation patterns; the app is ended with Stop-Process, so nothing is saved. |
| `toolstrip-sample/` | Stage W1's host for the toolstrip control (ADR 0279): R2025b's App Designer toolstrip as stage W0 recorded it (`w0_toolstrip.json`), over a text box, with Design View, Code View and Debugging buttons that switch its contexts and execution controls; every command, invocation and check is a line in the `-log` file. `-selftest` runs the checks that need WPF's real layout (the CANVAS tab fitted at nine widths, Undo with the text box focused going to the window, key tips, contexts, a button group, the spinner's limits) and exits 0 or 1; `-dark` starts in the dark theme. |
| `run-w1toolstrip.ps1` | Builds the sample, runs its self-test in both themes, then drives its window through UI Automation patterns only: tabs selected, App Details invoked, SHARE and Save's arrow expanded and an item of each popup invoked, Grid toggled, the grid interval set through Value, Code View's tabs, Debugging's swap of Run for Continue, the window narrowed through Transform until RUN collapses and its popup's Run invoked, the toolstrip minimized and a tab opened over the document; pictures of each, and of the dark theme, into the work folder. |

```
powershell -File run-k5window.ps1 -WorkRoot <a scratch folder outside the repo>
powershell -File run-k19window.ps1 -WorkRoot <another scratch folder>   # needs a Release build of JGraph.Application and a Debug build of jgraph-mps
powershell -File run-k20window.ps1 -WorkRoot <another scratch folder>   # needs a Release build of JGraph.Application and a Debug build of jgraph-mps
powershell -File run-hello-app.ps1 -WorkRoot <another scratch folder> -Runtime <a runtime laid out by installer\publish-runtime.ps1>
powershell -File run-w1toolstrip.ps1 -WorkRoot <another scratch folder>
```

A WPF client's windows open after the call that made them, and their components after that. UI
Automation answers ahead of that queue, and a window it reads before its components arrive keeps
showing it empty (open item 181), so the client waits for its window thread to be idle before it
says `ready`.
