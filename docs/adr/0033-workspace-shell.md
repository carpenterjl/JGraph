# ADR 0033 — The scripting workspace is the application shell

## Status

Accepted (M30, 2026-07-24).

## Context

JGraph launched into `FigureWindow`. The scripting workspace — the docking window with the file
tree, script tabs, console, variables and data viewer — was a secondary, *owned* window opened from a
**Script…** toolbar button. That inverted the way the tool is actually used: the figure is an output
of a script, not the place you start. The user asked for JGraph to behave like the MATLAB IDE, where
the workspace is the application and figures open beside it.

Three specific complaints came with it: nothing indicated progress during a cold start (which is not
instant — scanning the plugins folder and probing the machine for a CPython runtime both happen
there); the toolbar was a flat row of buttons with keyboard shortcuts advertised in tooltips and
implemented separately in a `PreviewKeyDown` handler; and a first run showed an empty tree with
nothing to open.

## Decision

**The workspace window is the `MainWindow`.** `App.OnStartup` resolves it from the container, and
`ShutdownMode` is `OnMainWindowClose` — closing the workspace ends the session, and WPF closes the
figure windows with it. `FigureWindow` stays transient and number-keyed through `FigureWindowService`;
it is now only ever opened by a script, by the console, by opening a `.graph` file, or by
**Tools → New Figure Window**.

Consequently `ScriptingService` stops being a window factory. The workspace is a DI singleton, and
`Open()` is `Show(); Activate();`. The `Owner` assignment had to go: an owned window can never be the
`MainWindow`, and once figures open *from* the shell it would have created an ownership cycle.

`ShutdownMode` is `OnExplicitShutdown` for the moment between the splash appearing and the shell
being shown, so closing the splash cannot look like the last window closing. The `-batch
-showfigures` path keeps its own `OnExplicitShutdown` → `OnLastWindowClose` handoff and its deferred
exit code, untouched.

**Restoring the session moved out of the constructor** into `RestoreSession()`, called by the startup
sequence between construction and `Show()`. The container builds the shell now, so construction has
to be cheap; and the restore is the thing the splash reports progress against. It is idempotent, so
`ScriptingService.Open()` can call it defensively for any path that did not go through startup.

**A splash reports the warm-up.** `InteractiveStartup` shows it, then resolves the plugin registry
and touches every engine's `IsAvailable` on a background thread before restoring the session. Both
are safe off the UI thread — the registry only reads assemblies, and `PythonScriptEngine`'s
constructor merely probes for an interpreter. Nothing there initialises CPython; that is thread-affine
and stays deferred to the first Python run. The artwork is replaceable without a rebuild
(`SplashArtwork`: `%AppData%\JGraph\splash.png`, then one beside the executable, then a built-in
design drawn in XAML). A missing or corrupt image falls through to the built-in one — startup must
never be blocked by decoration.

**Every action is a `RoutedUICommand`** in `WorkspaceCommands`, bound once in
`ScriptWorkspaceWindow.Commands.cs`. The menu bar (File / Edit / View / Run / Tools / Help), the
toolbar and the keyboard all bind to the same commands, so a shortcut cannot drift from the menu item
advertising it, and enablement lives in one `CanExecute` per action instead of scattered `IsEnabled`
assignments. The hand-written `PreviewKeyDown` handler is gone. The Edit menu's standard commands are
forwarded explicitly to the active editor, because a `MenuItem` loses the focused element as its
command target the moment the menu opens.

**The pane registry became `PaneCatalog`** — `PaneDescriptor(ContentId, Title, DefaultSide, Content)`
— feeding the View menu, `ShowPane`, and the recreation of a pane a saved layout does not mention.
That last case had a real bug: the old code dropped a resurrected pane into
`LayoutAnchorablePane.LastOrDefault()`, i.e. wherever happened to be last in the tree. It now docks on
the side it belongs on. The catalog also owns each pane's caption, so renaming Variables to
**Workspace** took effect for users with a saved layout rather than only for new ones.

**A first run seeds an example workspace.** With no saved state and no configured script directory,
the shipped `examples/` are copied into `Documents\JGraph\Examples` and opened. They cannot be opened
where they ship — an installed `examples/` sits beside the executable and may be read-only. The copy
skips files already present, so re-running never overwrites work; `ExampleWorkspaceSeeder.Plan`
decides what to copy and is unit-tested, while the host does the IO.

## Persistence

`ScriptWorkspaceStateDto` gained window placement and a `LayoutSchema`. **No `CurrentVersion` bump:**
`Deserialize` gates on `FormatVersion <= CurrentVersion`, `System.Text.Json` ignores unknown members,
and absent members take their property initializers, so old state loads with the new fields
defaulted. Bumping for an additive change would make files written by the new build unreadable by an
older one — silently discarding the user's session on a downgrade, for nothing.

`LayoutSchema` is a separate lever from `FormatVersion`, with `MinimumCompatibleLayoutSchema = 0`:
state written before the field existed describes the same five panes under the same content ids, so
discarding it would throw away the user's arrangement for no reason. Raise both constants together
only when a release rearranges panes in a way an older layout cannot express.

Placement is captured in `OnClosing`, not `OnClosed`. `Window.RestoreBounds` is only valid while the
window exists, and reading it after close threw — during `Application.Shutdown`, where an unhandled
exception takes the process down with `0xC000041D` instead of merely losing the layout. The whole save
is now wrapped so persistence can never crash shutdown, matching the best-effort contract
`WorkspaceStateService` already had underneath it.

**Never rename an existing `ContentId`** (`files`, `console`, `variables`, `callstack`,
`dataviewer`). Layouts store panes by that string; a rename orphans the pane in every session anyone
has already saved. Change `PaneDescriptor.Title` instead — that is what it is for.

## Alternatives considered

- **Converting `ScriptWorkspaceWindow` to MVVM.** Rejected for this milestone. Most of its 1450 lines
  are things a view model cannot own: `LayoutDocument` graph surgery, `XmlLayoutSerializer`
  callbacks, direct editor access for breakpoints and live-edit baselines, and a dispatcher-coalesced
  console whose whole purpose is thread marshalling. It was split into partial files by concern
  instead (`.Files`, `.Documents`, `.Run`, `.DataViewer`, `.Console`, `.Layout`, `.Commands`), which
  is what actually made the milestone reviewable.
- **Keeping `FigureWindow` as the main window and merely opening the workspace at startup.** Leaves
  two candidate main windows and an ownership relationship that has to be maintained; and closing the
  figure would end a session whose real content is the scripts.
- **Docking figures into the shell** as MATLAB does. Deferred: `FigureWindow`'s toolbar, plot browser
  and property inspector would all have to be re-hosted as dock panes. A milestone of its own.

## Consequences

- The shell, the splash, `WorkspaceCommands` and `PaneCatalog` live in `JGraph.Application`
  (net8.0-windows) and so are not reachable from the net8.0 test project — the standing limitation.
  The logic underneath **is** tested: `SplashArtwork.Find`, `ExampleWorkspaceSeeder.Plan`,
  `WorkspaceFiles.Classify` (the tree's extension dispatch, lifted out of the code-behind), and the
  state format's round-trip and backward compatibility.
- `IFigureWindowService` gained `OpenBlankFigure()`, since nothing else opened an empty figure once
  the figure window stopped being the startup window. It goes through `JG`, so the new figure joins
  the same numbering scripts use and becomes current — an immediate `plot(...)` lands in it.
- The shell's title names the open workspace, the way an IDE does.
