# ADR 0342 — A group is a child of its axes, and a figure's annotations sit in a hidden pane under R2025b's shape words: open items 143, 144 and 166

## Status

Accepted, 2026-11-12. Open items 143, 144 and 166 (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), found in batches 29 and 30 (ADRs 0242, 0255). The recordings are three headless probes
of 2026-11-12 (kept outside git): `pa_groups` (62 readings of groups in the tree, what deletes one,
the annotations' types and reach), `pa_pane` (19 readings of the annotation pane) and
`pa_figwrite`/`pa_figread` (a figure with a group, a nested group, a transform and an annotation,
written by each engine and read by R2025b).

## Context

M67 put `hggroup` and `hgtransform` **beside** the render tree (`JgsGraphicsGroup`): a book of
members whose verbs reach through to them, kept alive by the handle sweep's `KeepGroupsIn` for as long
as its axes lived. That saved the renderer, the hit test, autoscaling, the plot browser and the
document from a container that draws nothing. A script, though, saw the members where R2025b shows
the group:

- **Item 144.** `findobj(f, 'Tag', 'group')`, `findobj(f, 'Type', 'hggroup')` and `findall` found no
  group, because the walk never reached one.
- **Item 166.** `ax.Children` listed a group's members, not the group. A replacing plot and `cla`
  left the group alive (the members went), so a later `line(g, …)` drew into an axes R2025b had
  cleared. `delete(g)` did nothing at all.

The annotations had the opposite fault (item 143). `get(annotation('textbox'), 'Type')` answered the
kind, `'textbox'`, where R2025b answers `'textboxshape'`. And `findobj(f, 'Tag', t)` found an
annotation that R2025b's does not: there the annotations sit in a pane whose handle is hidden.

R2025b, measured:

- A group is its axes' child like any other. `findobj(ax)` goes level by level through it
  (`'' xform group tmember inner member deep`). `ax.Children` is newest first, groups by when they
  were made (`tx p2 gmid p1`).
- A replacing plot, `cla`, `cla reset` and `delete(g)` delete the group, the groups made in it and
  every member. A held plot, a `hggroup` made with no parent and a deleted member leave it alone.
- A member taken out of its group into the same axes comes to the front of `ax.Children`.
- A group whose `HandleVisibility` is off hides its subtree from `findobj`; `findall` still reaches
  the members.
- The seven annotation types are `textboxshape`, `rectangleshape`, `ellipseshape`, `lineshape`,
  `arrowshape`, `doubleendarrowshape` and `textarrowshape`. Each annotation's `Parent` is a
  `matlab.graphics.shape.internal.AnnotationPane`:
  - its `Type` is `annotationpane`, its `HandleVisibility` is off and its `Tag` is `scribeOverlay`;
  - its parent is the figure, and `allchild(f)` lists it in front of the axes;
  - it is made with the first annotation;
  - it survives the annotations a script deletes, and a plain `clf` (which deletes the annotations);
  - `clf reset` and `delete` of it take it and its annotations, and the next annotation makes a new
    pane.
- R2025b reads a `.fig` this build writes with groups in it as groups: the group, the nested group and
  the transform come back with their members (`pa_figread` over JGraph's file).

## Decision

### Groups stay beside the render tree and join the script's tree

The members stay in the axes' `Plots`, `Annotations` and `Lights`. That is what draws them, saves
them in the document and autoscales the axes, so none of M67's five surfaces changes. What changes is
the tree a script walks:

- `JgsGraphicsProperties.ChildrenOf(axes)` lists the axes' groups made straight in it
  (`GroupsIn`, from the book of groups). It leaves out every object a group in it holds, and puts each
  group among the drawn objects by its `CreationOrder` (`AddDrawnChildren`). `ChildrenOf(group)` was
  already its members.
- `DescendantsOf`, `findobj`, `findall`, `allchild`, `Children` and the handle sweep all walk
  `ChildrenOf`, so they reach a group and through it its members.
- The sweep no longer keeps groups by hand. `KeepGroupsIn` becomes `ForgetGroupsOutside`: the book
  forgets the groups the walk did not reach.
- `ParentOf` answers a member's group (open item 137 had done this for the `Parent` property only),
  so `ancestor(h, 'hggroup')` finds it.

### What deletes a group

- **A replacing plot.** `JG.ResetAxesForReplace` raises a new hook, `JG.AxesEmptying`, before it
  empties an axes. The script layer installs `JgsGraphicsProperties.DeleteGroupsIn` on it (in
  `JgsBuiltins.LivingGraphics`). It announces each group's deletion while the group is still in the
  tree, so `DeleteFcn`s run, marks every one of them, and forgets them.
- **`cla`** calls `DeleteGroupsIn` itself, and so does `cla reset`. Under `yyaxis` a plain `cla`
  empties one side and leaves groups alone, as before.
- **`delete(g)`** (`DeleteGroup`): the group, its inner groups and every member, each member removed
  as `delete` removes any drawn object.
- **A member set to its own axes** leaves its group and moves to the front of its collection
  (`BringToFront`), as R2025b puts it at the front of `ax.Children`.

### The annotation pane

`JgsAnnotationPane` is a stable handle, one per figure, held in a `ConditionalWeakTable` as
`JgsLegendTitle` is. It exists for the MATLAB dialect only:

- `ChildrenOf(figure)` lists it in place of the annotations once the figure has an annotation, or
  once it has had a pane. `ChildrenOf(pane)` is the figure's `Annotations`. The annotations stay in
  `FigureModel.Annotations` for the renderer and the document.
- Its handle is minted hidden, beside the rulers' rule in `JgsHandleRegistry.For`. So `findobj`, a
  figure's `Children` and `gcf`-style listings pass it and its annotations by; `findall` and
  `ShowHiddenHandles` reach them.
- `ParentOf(annotation)` is the pane, and the pane's is the figure.
- A plain `clf` keeps the pane and deletes the visible annotations in it. `clf reset` and `delete(pane)`
  delete the annotations and forget the pane, so the next annotation makes a new one.
- In the JGS dialect a figure lists its annotations directly, as before. A pane a MATLAB-dialect
  script reached stays among the figure's descendants, so a sweep run outside a script keeps its
  handle.

### Type words

`JgsGraphicsProperties.TypeWordOf` is what a script reads from `Type`, and what `findobj`,
`isgraphics(h, type)` and `ancestor` match against. In a MATLAB-dialect script a figure's annotation
answers R2025b's shape word. `TypeNameOf` keeps the internal word that this build's own tables key on:
the class rows (`JgsGraphicsClasses.KeyOf`), the `.fig` writer's words, the defaults and the error
texts. So the class tables, their generator and ADR 0240's writer need no new rows.

JGS keeps its kind words (`'textbox'`, `'arrow'`, …) and its `findobj` reach. A JGS script that finds
annotations by type goes on working.

Found with it: the drawn `rectangle` and the rectangle annotation shared the class row key
`'rectangle'`, so `isa(rectangle(), 'matlab.graphics.primitive.Rectangle')` was false and
`isa(…, 'matlab.graphics.shape.Rectangle')` true. `KeyOf` now gives a drawn rectangle the recorded row
`rectangleprim`.

### The `.fig` writer

ADR 0240's writer walks `ChildrenOf`, so a group is written as hgsave writes one: an `hggroup` or
`hgtransform` node with its members as children. A transform's `Matrix` is left out, because this
build's members already hold the coordinates the matrix gave them, and R2025b would apply it twice.
R2025b reads the result with the groups, the nesting and the transform's members where they were
drawn, under an identity matrix. The annotations are gathered from the pane into the one overlay as
before.

## Measured

- **`pa_groups`, `pa_pane`, `pa_figwrite`/`pa_figread`** (above).
- **Fixture `oi_group_tree_annotation_pane`** (75 rows, 1 divergence): the searches over a group and
  a transform, the axes' and the group's children and their order, the parents and ancestors, a
  member moved out and back, a replacing plot, `cla`, `cla reset`, `delete` of a group and of a
  member, a held plot, a parentless group, a closed figure, a hidden group, a group through each
  engine's own file, the seven types, `findobj` and `findall` by tag and type, the pane's properties,
  order, life through `delete`, `clf`, `clf reset` and its own deletion, `ShowHiddenHandles`,
  `isgraphics`, `ancestor`, and the drawn rectangle's class.
- **Fixture `oi_tags_legend_title_imshow`** re-recorded: its rows `tag_shape_types_after` and
  `tag_shapes_findobj` are agreeing lines now.
- **Fixture `oi_onoff_states_save_v73`** restamped: its div row `ob_findobj` (ADR 0235) now counts
  the axes' group, as R2025b does. It still diverges, for other reasons.
- **Unit tests** `GroupTreeAnnotationPaneTests` (16): the searches, the children and their order, a
  member moved out, the four ways a group is deleted, what keeps one, a deleted member, the shape
  words, JGS's kind words, the pane, `clf` and the pane's deletion, the rectangle's class, and the
  annotations and members through a figure file.

## Not measured

- **Any release but R2025b**, and MATLAB's window on any of it.
- **A group's own `DeleteFcn`** order against its members' in R2025b.
- **Reparenting a group** (`set(g, 'Parent', ax2)`), refused here as before.
- **An annotation made in a `uipanel`**, and the pane's own property list.

## Consequences

- A script that finds its groups by tag or type finds them, and one that walks `ax.Children` meets
  the group, as in R2025b.
- A script that draws a fresh plot or calls `cla` and then reuses an old group handle now gets a
  deleted handle where it used to draw into the cleared axes.
- `delete(g)` removes the group's drawing.
- A script that counts what `findobj` returns no longer counts the annotations. One that asks an
  annotation's type gets R2025b's word.
- The handle sweep finds groups by walking the tree like everything else. The book of groups is now
  only a lookup.

## Divergences

- **A group does not survive this build's own figure file.** The `.graph` document keeps a group's
  members and forgets the group, as M67 recorded, so `openfig` of this build's own file finds no
  `hggroup` (row `file_group`). R2025b's file, and R2025b reading this build's `.fig`, keep it.
- **Hiding a group hides its members here** (their own `Visible` reads `'off'`). R2025b leaves a
  member's `Visible` `'on'` and hides it through the group (`pa_groups` `vis_off_member`); this is
  M67's design, since the group is not drawn.
- **A transform moves its members' own coordinates here.** A member reads back its moved data, and a
  `.fig` this build writes holds the moved data under an identity matrix (`pa_figread`: `YData`
  `[5 5]`, where R2025b's own file has `[0 0]` and the translation in `Matrix`).

## Still open

- `line(g, x, y)` on a deleted group reads the dead handle as data here. R2025b refuses it with
  `MATLAB:hg:DeletedObject` ("Cannot set property to a deleted object."). This belongs to the makers'
  wrapper (`JgsBuiltins.MadeProperties`), which the next batch owns with items 167 and 168.
- A group in this build's `.graph` document (the divergence above).
