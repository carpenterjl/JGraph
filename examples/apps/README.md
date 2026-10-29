# Example apps

JGraph's example apps for App Designer's Start Page (App Designer window plan, stage W3, ADR 0281).
Each folder is one example; its apps are kept as the class text App Designer's code view shows, so
they read here and run as they are (`addpath` the folder, then call the app by name). The Start
Page's "Show examples" lists them under R2025b's titles for their topics; a tile copies its example
into `Documents\JGraph\Examples\<folder>` as `.mlapp` files and opens the main app in the designer.

| Folder | Tile | What it shows |
|---|---|---|
| `NumericInputExample` | Respond to Numerical Input | `SavingsApp`: numeric fields with limits, a shared value-changed callback, a plot of the balance |
| `GridLayoutExample` | Lay Out Controls in a Grid | `PulseApp`: a grid layout with a nested grid of labels and spinners beside an axes |
| `TreeExample` | Link Data to a Tree | `PartsTreeApp`: tree nodes made in code with `NodeData`, selection and in-place renaming |
| `ImageExample` | Analyze an Image | `ImageApp`: an image made in code or loaded with `uigetfile`, a histogram per channel |
| `TimerExample` | Configure a Timer | `TimerApp`: a `timer` made in the startup function, started, stopped and deleted with the window |
| `PolarAxesExample` | Display Specialized Axes | `PolarApp`: polar axes made in code in a panel |
| `TableExample` | Create a Table | `TableApp`: a table of a `table`, a cell edit refused and rows added |
| `MultiWindowExample` | Pass Data Between Apps | `MainApp` opens `OptionsDialog`, which passes the choices back through public functions |

Each app must stay as the designer writes it: `AppDesignerStartPageTests.EachExampleAppIsAsTheDesignerWritesIt`
holds it to the writer, and with `JGRAPH_W3_NORMALIZE` set to this folder it rewrites the files in
the writer's form. `TheExampleAppsRunAndRespond` runs them in JGraph, and
`tools/matlab-checklist/ui-probes/w3/w3_examples.m` runs the same drive in R2025b.
