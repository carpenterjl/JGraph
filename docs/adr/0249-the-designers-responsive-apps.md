# ADR 0249 — The designer's responsive apps

## Status

Accepted, 2026-10-19. Stage DS4b of the visual app designer plan (`docs/plans/app-designer-plan.md`,
gitignored), split from DS4 (ADR 0248) as DS1b was from DS1. App Designer's "2-Panel App with
Auto-Reflow" and "3-Panel App with Auto-Reflow" were read from R2025b's own code - its canvas bundle
(`ResponsiveAppTemplateData`, `ResponsiveAppFactory`, `LayoutTemplateManager`, `LayoutResizeEngine` and
its edge calculators, `LayoutTemplateResizeUtils`, `ChildrenPositionsCalculator`, `RegionPanelModel`,
`RegionPanelController`, `ResponsiveAppGridLayoutModel`, `ResponsiveAppGridLayoutController`,
`TemplateComponentMove`, `BeginTemplateComponentResize`, `ArrowKeyMoveReorderPlugin`,
`AppPropertiesBlockBehavior`, `ReadonlyCallbackCodeBehavior`, `FigureUtil`, `UIFigureModel`) and its
MATLAB side (`MLAPPResponsiveAppValidator`, `DesignTimeController`, `MLAPPSerializer`). Nothing of it is
kept. The splitter's arithmetic was run under node in App Designer's own modules, unmodified, and its
answers kept in the tests. R2025b's App Designer read the apps saved here (`ds6_verify`, headless,
2026-10-19).

## Context

A responsive app is a figure holding one grid of one row and two or three columns, with a panel in
each column - the regions - and code App Designer writes and never lets the user edit: a private
properties block of the figure widths the layout changes at, and the figure's `SizeChangedFcn`
callback `updateAppLayout`, which stacks the regions when the figure narrows. DS1 recognised one by
its properties comment and DS6 wrote its type and its containers' flags, but the canvas treated it as
any app with a grid: the regions could be deleted and moved between cells, the grid configured and
given rows, and a save left `updateAppLayout` with the widths it was opened with. Nothing made a new
one.

## Decision

### What a responsive app is (`DesignResponsive.Grid`, `Panels`, `Regions`)

An app is responsive when its code says so (`AppDesign.IsResponsive`): by the properties block App
Designer writes for a responsive app alone, the text's counterpart of the `.mlapp`'s `AppType`, which
App Designer never infers from the components. Everything follows that one answer - the canvas's
locks, the code written again, the code's copy in the file, the tree's flags and the file's type.
The figure's first grid is the app's grid; the panels in its columns are the regions, leading,
centre and trailing by `Layout.Column`; and when the grid has one row and two or three columns with a
panel in each (`DesignResponsive.IsTemplate`, `FigureUtil.getTemplateName`) the regions can be split.
Only an edit of the code can make a responsive app anything else - the canvas locks its grid and
regions - and it stays responsive, its grid locked, with nothing to split. The figure's
`SizeChangedFcn` names the layout callback whatever the code says, as App Designer's template gives
it on load. The canvas lays the centre column out taking the rest of the figure, as App Designer
places its centre region at design time (`ResponsiveAppFactory._adjustRegionPanelData`), whatever
pixels the code gives it.

### A new one (`DesignResponsive.NewAppText`)

File > New has "MATLAB 2-panel app with auto-reflow" and "MATLAB 3-panel app with auto-reflow",
making App Designer's template: a figure 640 or 860 by 480 that does not resize its children itself,
its `SizeChangedFcn` bound to `updateAppLayout`; a grid of one `'1x'` row, side columns of 220 pixels
and a `'1x'` centre, no padding or spacing, scrolling; the panels `LeftPanel`, `CenterPanel` and
`RightPanel`; the widths block (`onePanelWidth = 576`, and `twoPanelWidth = 768` for three panels);
and `updateAppLayout` from App Designer's template. The component code is the designer's own writer's.

### The code App Designer writes (`DesignResponsive.Splice`, `DesignDocument.Save`)

Every save writes the widths block and the body of the figure's `SizeChangedFcn` callback again from
App Designer's template, as App Designer does whenever it opens or saves an app: the figure's, the
grid's and the regions' names, every region's height the figure's, and the side regions' widths their
columns'. The callback is found by its blocks, as the code's copy in the file finds it, and its
signature and comment are App Designer's; a layout callback, or the figure's binding to it, that the
code has lost is put back, last among the callbacks where App Designer keeps it - App Designer's is
not deletable. The rest of the text is kept, and so are its line endings. A save into an `.mlapp` of
code that no longer says it is responsive takes the responsive flags off the tree and makes the file
standard, which R2025b's validator would otherwise refuse; the other way is App Designer's Convert
action (open item 154).

### The locks (`DesignDocument.IsRegion`, `IsEditable`, `CanDelete`, `ComponentAt`)

As App Designer keeps them:

- **The grid is the user's to see only through its regions**: a click never reaches it, it takes no
  drop and shows no cells, the component browser lists the regions under the figure in its place,
  Select All and a rubber band leave it out, and the inspector edits none of it
  (`ResponsiveAppGridLayoutModel.IsInvisibleToUser`, `ResponsiveAppGridLayoutController`,
  `SelectableController`). It is the figure's first grid, and the tree written for R2025b flags it and
  its panels only.
- **A region** cannot be deleted, moved or resized, the arrow keys do nothing to it, and the inspector
  does not edit its `Position`, `Visible` or `AutoResizeChildren` (`RegionPanelModel`).
- **The figure's** `Position` and `AutoResizeChildren` are not edited in the inspector
  (`UIFigureModel.adjustPropertyInspector`), and it takes no Grid Layout (`PlacementUtil.canPlaceGrid`),
  nor anything else: what no region takes - dragged, dropped or drawn outside every region - is
  refused, since the regions cover the figure.

### Components in a region (`DesignDocument.PlacementFor`, `DesignGestures.InRegion`)

A component dragged over a region's edge - inside no region whole, touching one - is refused
(`TemplateComponentMove.isOverEdgeOfRegion`), so nothing straddles two regions, and the alignment
hints are cleared while it is; a drag into a grid, or of a grid, is not refused. A resize is held 6
pixels inside the component's region after it is snapped, to the hints or to the grid, as App
Designer's `ResizeCalculator` holds it with `BeginTemplateComponentResize`'s offsets: the most any
moved edge passes its limit is taken back from every edge that moved - both sides of a resize from
the centre, and across and down together through the ratio of a shape that keeps one. The arrow keys
stop 6 pixels inside too (`_buildArrowData`).

### The splitters (`DesignDocument.Splitters`, `ResizeRegions`, `CommitRegions`)

A splitter 7 pixels wide stands on the leading region's right edge, and in a 3-panel app on the
trailing region's left edge. Dragged, it moves the edge by App Designer's engine
(`LayoutResizeEngine.resizeBySplitter`): the region on the splitter's side grows by what the region it
takes from can spare - its width less what its components need, 6 pixels from each edge
(`calculateAvailableSize`) - the leading splitter of a 3-panel app then taking from the trailing region
with the centre moving along, and stops where nothing is left. A region's components keep their place
on the canvas while an edge passes them and are pushed along within 6 pixels of it
(`ChildrenPositionsCalculator.adjustByBorder`). A drag is App Designer's steps, one for each move of
the pointer (`_HandleDoingLayoutTemplateResize`): each from where the last left the regions and their
components, by what the pointer moved past the part of the drag the regions have taken - so that the
splitter stays at its limit while the pointer goes on, comes back from it at once, and a drag out and
back leaves components where the passing edge pushed them. While the regions stop short of the pointer
the splitter shows App Designer's alert. The drag shows on the canvas as it goes, and nothing is undone
or redone meanwhile; its release is one step - the grid's side columns, whole pixels, the centre `'1x'`
taking the rest of the figure as App Designer's centre region does, and the components of each region
the drag left elsewhere moved across; a region back where it began keeps its components where they
were, whatever the drag did on the way (`cacheRegionsResizeCommand`'s `isSame`), and a drag that ends
where it began is no edit. The selection stays the user's through the step, its undo and its redo.
Undo takes the step back whole, and the next save writes `updateAppLayout` with the new widths. A grid
whose columns the code gives as numbers splits the same.

## Measured

- `AppDesignerResponsiveTests` (12 tests, 13 cases): a shipped responsive app's generated code written
  again character for character as App Designer wrote it; a new 2- and 3-panel app is App Designer's
  template; the regions, the grid and the figure locked; a component kept in its region by a move,
  a resize and the arrows; the splitters against App Designer's own engine (a drag of 50, of 200
  stopped at 92 short, of -150 stopped at 42 short, a child kept in place and one pushed, the save's
  `{112, '1x'}`) and undone; **64 drags through paths of pointer moves - 5 scenes of 2 and 3 regions,
  empty, holding components and tight, each splitter, 8 paths out, back, past the limit and zigzag -
  every one of 248 moves checked against App Designer's `LayoutResizeEngine` run unaltered under node
  with its gesture's anchor rule** (`ResponsiveSplitterOracle.json`): the regions, each component's
  place, the alert, and on release the columns, the positions and the undo; undo held off and the alert
  shown during a drag, and a cancelled one; the grid never selected by a band, edited, or given a grid
  or a button outside the regions; what the code says - a grid edited out of a template's shape, a
  region's panel gone, the auto-reflow block taken out and the `.mlapp`'s type following it, the layout
  callback and its binding lost and put back line for line, a centre in pixels short of the figure; a
  resize held inside against the hints, from the centre and for a knob, and no hints over a region's
  edge; numeric columns; a new 3-panel app built on the canvas, its trailing splitter dragged, saved
  and opened again. With the rest of the designer's tests, 559.
- A review of the stage by readers of App Designer's specs, each finding checked by another,
  confirmed 9 defects: the splitter's drag applied the whole drag again on every move of the pointer,
  so that it ran away from it (found three times); a grid given by numbers crashed it; undo ran in the
  middle of it; the alert was not shown; a band selected the hidden grid; the snap moved a resized edge
  past the region's limit, and a resize from the centre or of a fixed-ratio shape escaped it; the hints
  stayed over a region's edge; a grid moved out of a region went onto the figure, where the tree then
  flagged two grids. All fixed and tested. A second review, of the fixes, confirmed 7 more: a drag back
  to where it began still moved the components it had pushed on the way; a grid given by numbers kept
  its centre in pixels, so that the splitter had no limit; the step lost the user's selection; the move
  over a region's edge was judged unsnapped; and whether an app was responsive was answered two ways -
  the canvas by the grid's shape, the saved file by the comment - so that an app with a column's panel
  gone locked its panels in an editable grid, a track edit could turn an app responsive in the middle
  of a session, and one edited as standard was saved as a responsive `.mlapp` R2025b refuses. All fixed
  and tested. A third review, of those fixes, confirmed 5 more, each in code edited by hand: a centre
  given in pixels short of the figure was drawn so, and a drag out and back still moved components; a
  save into an `.mlapp` kept its type when its code had changed it; and the fix had answered whether an
  app was responsive by its grid, where App Designer goes by the type its file says - opening as
  responsive apps the designer then called standard, and writing the code's copy of an app called
  standard with its auto-reflow block still App Designer's - and a lost layout callback made a file App
  Designer cannot open. Hence the one rule above, by the code's word. All fixed and tested. A fourth
  review, of the rule, confirmed 6 more: a file made standard kept its tree's responsive flags, which
  R2025b reads whatever the file's type (the save now takes them off, as App Designer's own conversion
  does, and makes the file standard only with a tree it rewrote); the figure was bound to a layout
  callback no save could put back; the callback was found by its exact text, so that a space after its
  `end` lost createComponents (it is now found by its blocks, as the code's copy finds it); it was put
  back first among the callbacks, where App Designer keeps it last; and the browser's order took every
  figure grid for the responsive one. All fixed and tested. A last check of each of those fixes found
  three of them closed and three leaving a case in hand-edited code - a binding where createComponents'
  comment had gone, a callback the class had outside its callbacks block made twice, and a callbacks
  block closed with a comment taking the put-back callback inside its last function - now closed too:
  a callback is put back only where the class has none, after the last callback as its blocks end it.
- The full suite: 11,875 passed. An earlier run failed one rendering test of an invisible axes' plot
  box, which passes alone and with its class and runs no code this stage touches (open item 155). The
  seven verify scripts pass.
- **R2025b's App Designer reading apps saved here** (`ds6_verify`, `DS6_PART=new`, headless): a new
  3-panel app made here with components in its regions and a splitter dragged (`new ResponsiveApp:
  same`, 8 components), and the shipped `SystemLogViewer` written again (`same`, 9 components), each
  open the same as the tree their code makes, saved by App Designer's own serializer as a responsive
  app; both read as responsive apps (`AppType` Responsive, no warnings), their grid and regions flagged
  as App Designer's own file flags them. The check's control now carries the flags App Designer's
  template sets and the responsive type, which no line of code can; before, the flags were counted
  against JGraph's file, and the DS6 record of `SystemLogViewer` differing by them is withdrawn. On the
  final code the two again, DS4's grid app (`same`, 15 components) and `SystemLogViewer` edited and
  saved over App Designer's tree (`as saved`: its untouched components load as the original's).
- **The window** (the Release app started with `-r "appdesigner(...)"` on a copy of that 3-panel app,
  UI Automation patterns only, captured with PrintWindow, ended by Stop-Process; the app's file and
  `workspace.json` unchanged): the component browser listing the three regions under the figure and
  no grid; a region selected with no handles, its `Position`, `Visible` and `AutoResizeChildren` shown
  and not edited, its `Title` edited, and the toolbar's Delete off; the figure's `Position` and
  `AutoResizeChildren` not edited; a region's button deleted and undone back into its region.

## Not measured

- The mouse in the real window: the splitter's drag is tested through `DesignGestures`, which the
  canvas only feeds.
- The breakpoints at run time: `updateAppLayout` is App Designer's text, run by the interpreter like
  any callback; that it reflows is the runtime's grid, not the designer's.

## Consequences

- `JGraph.Scripting.Designer` has `DesignResponsive` and `DesignDocument`'s regions and splitters;
  `DesignGestures` has the splitter's drag and the region's clamp; the canvas draws the splitters;
  `DesignPlacement` says when a move is over a region's edge.
- The code reader gives a responsive app's figure its layout callback when the code has none and a
  save can put it back, and a save into an `.mlapp` whose code is no longer responsive takes the
  responsive flags off its tree and makes its `AppType` standard.
- File > New makes App Designer's two responsive templates.
- The tests keep App Designer's splitter engine's answers for 64 drags (`ResponsiveSplitterOracle.json`).
- `ds1b_reference` gives a responsive app's grid and regions the template's flag, and `ds6_verify`
  saves a responsive app's control as one.
- No running script behaves differently.

## Divergences

- **`updateAppLayout` names the app's own grid**: App Designer's template always writes
  `app.GridLayout`, whatever the grid is called, and renames the regions by a chain of
  first-occurrence replacements that can give a region another's width; the designer writes each
  name where it belongs.
- **A resize in a region counts only the edges that move**: App Designer's `ResizeCalculator` also counts
  an edge the handle does not move, so that a component already nearer its region's edge than 6 pixels
  shrinks when dragged away from it, by as much as it is too near.
- **The arrow keys do not push a component in**: App Designer's arrows move a component nearer its
  region's edge than 6 pixels out to that line whichever key is pressed.
- **Nothing goes onto a responsive app's figure outside its regions**: R2025b's override that refuses a
  library drop outside every region is never called, and its ordinary rules apply there.
- **A stopped splitter is red while it is stopped**, where App Designer flashes it in its error colour
  for a second.
- **A splitter's drag writes the centre column `'1x'`**: App Designer keeps whatever the code gave it,
  which it ignores at design time and `updateAppLayout` replaces at run time.

## Still open

- App Designer's Convert actions - a standard app made responsive, a responsive one made standard
  (open item 154).
- Resizing a responsive app's figure on the canvas, which gives the growth to the trailing region
  (open item 151 notes it; the designer resizes no figure on the canvas yet).
- A region's `SizeChangedFcn` disabled in the callbacks list, when the designer has one.
