# ADR 0255 — The graphics makers take any property of what they make, and a group as their parent

## Status

Accepted, 2026-10-21. Open-items batch 30: items 136 and 137.

## Context

- **Item 136.** `plot(ax, x, y, 'r-o', 'Tag', 'first')` was refused here ("plot: unknown option
  'Tag'") where R2025b takes any property of the object as an option of `plot`, `stem`, `scatter`,
  `bar`, `rectangle`, `light` and the rest. Each maker read a fixed list of its own names and refused
  the others; `patch` with `FaceAlpha` among its options dropped a `Tag` given after it, without a
  word. The oi77 and oi140 probes and their fixture set every tag afterwards with `set`.
- **Item 137.** `line(g, [0 1], [1 0])` with `g` an `hggroup` read the group as data, where R2025b
  takes a group or a transform first on every primitive and on most of the high-level makers.
  `line(x, y, 'Parent', g)` worked here, but the line's `Parent` still read its axes, and a text or a
  light could not join a group at all.

The probes (`tools/matlab-checklist/ui-probes/oi136/`, headless in R2025b and in this build's
`jgraph.exe -batch`):

- `oi136_options`: 44 makers given `'Tag'` and `'UserData'` with an axes first, 10 without one, other
  properties (`Visible`, `Clipping`, `SeriesIndex`, `XData`, `MarkerFaceAlpha`, `BarWidth`,
  `BaseValue`, `FaceAlpha`, `Curvature`), names that are not right, and 27 makers given a group or a
  transform first.
- `oi136_order`: what a refused option leaves, which of two settings of one name wins, `'Parent'` as
  an option, and verbs that are not makers given a group.
- `oi136_unheld`: each of 22 makers given a group or a transform first when the axes is not held.
- `oi136_create`: when a `CreateFcn` given in the call runs, and a group's children.

What they showed:

- Every maker takes any property of its object after the data and the line spec; a start of a name
  stands for the name (`'Ta'`, `'UserD'`); an unknown or ambiguous name is refused as
  `Unrecognized property Foo for class Line.` (`MATLAB:hg:InvalidProperty`), the class by its last
  word, and the call leaves nothing made. Two settings of one name: the last wins.
- A `CreateFcn` given in the call runs after every other option, whichever side of it they stand.
- `plot(x, y, 'Tag')` is refused: `Invalid color, marker, or line style.`
- A group or a transform first is taken by `line`, `text`, `patch`, `surface`, `rectangle`, `image`,
  `light`, `hggroup`, `hgtransform`, `plot`, `plot3`, the log plots, `stairs`, `stem`, `stem3`,
  `scatter`, `bar`, `barh`, `area`, `errorbar`, `histogram`, `fill`, `fill3` and `imagesc`. `quiver`,
  `contour`, `mesh`, `surf`, `scatter3` and `pcolor` read it as data and fail on it.
- Given a group, a maker draws into it and leaves the rest of the axes as it was, whatever the axes'
  `NextPlot`, which it does not change; so does one given `'Parent', g`. An axes named by `'Parent'`
  is where the maker draws, over an axes given first.
- A member's `Parent` is its group; a group's `Children` are what was made in it or moved into it,
  groups made in it among them, newest first; a member given another parent leaves the group.

## Decision

### One wrapper round every maker (`JgsBuiltins.MadeProperties`)

Each maker's registration goes through `Making(shape, body)`; the shape names the model class of what
it makes and the option names it reads itself. Teaching twenty parsers the whole property table would
be twenty edits that drift apart; the wrapper leaves each parser as it was.

- **The trailing pairs** are found from the end, a pair at a time, while the word in the name's place
  is one the maker reads or a property of what it makes: the whole name in either dialect; in the
  MATLAB dialect also the start of one, and any capitalised word a line spec or a colour could not be.
  The maker's own stay where they stand; the rest are taken out and set on every object the call made
  through the property table, which resolves a start of a name by R2025b's ranking and refuses an
  unknown one in R2025b's words. A JGS script takes whole names and keeps each maker's own refusal for
  any other.
- **A refusal takes back what the call made**: what the axes it draws in holds that it did not hold
  before is taken out quietly, its DeleteFcn not run, whether the maker or a property refused.
- **`CreateFcn`** is always taken out and set, and run on each object after every other option.
  `line` had only stored it.
- **A group or a transform first**, or named by `'Parent'`, is taken off: the maker runs in the
  group's axes with the hold on for the call and put back after, and what it made joins the group.
  An axes named by `'Parent'` replaces an axes given first. The makers whose R2025b forms read a group
  as data (`quiver`, `scatter3`, `pcolor`) do not take one; `surf`, `mesh` and `contour` are not
  wrapped.
- `line`, `text`, `surface` and `image` set every option themselves, in order (a text's `Units`
  before its `Position`), so the wrapper takes only the group and the parent from them.

### The groups (`JgsGraphicsGroup`, `JgsGraphicsProperties`)

- A group's members are any drawn object, a text in an axes or a light, and the groups made in it, in
  the order they joined; `Children` lists them newest first and leaves out a member deleted since.
- `Parent` reads a member's group before its axes.
- Given a group as its parent, an object moves to the group's axes, leaves the group it was in and
  joins; given an axes, it leaves its group. A transform moves a text's position as it moves the
  others' data.

### A line spec in the MATLAB dialect

`plot`'s spec, in the `(y, spec)` and `(x, y, spec)` forms and in a group, is refused in R2025b's
words when it is not one (`MATLAB:plot:UnrecognizedLineStyleParameter`), where `LineSpec.Parse` read
the letters it knew and drew.

## Consequences

- `JgsBuiltins.Making` and `MadeShape` are the place a new maker gets its options and its parent;
  a maker that reads a property itself lists it in its shape.
- Every maker's refusal of a known property for a bad value is unchanged; what changed is that a
  property name reaches the table instead of the maker's list.
- `JgsGraphicsGroup.Admit` (was `Adopt`) and `Release`; `JgsGraphicsProperties.GroupOwning` is
  internal.

## Measured

- **The probes** in both engines: `oi136_unheld` and `oi136_create` print the same lines; of
  `oi136_options`' 139 lines, the six that differ are outside these items: two are open item 167
  and three open item 168 (below), and one is `surf`'s own words for a group read as data.
- **The fixture** `oi_maker_options_group_parent`: every maker with an axes first and without, the
  values arriving, starts of names, a name twice, the maker's own around another's, nine refusals and
  the axes left as it was, `CreateFcn`, `'Parent'` among the options, 20 makers given a group or a
  transform, 44 rows of 22 makers drawing into a group as though held, a group's children as members
  come and go; one row a divergence.
- **The tests** (`MakerOptionsTests`): 22 makers taking `Tag` and `UserData`, values and own names,
  seven refusals leaving the axes as it was, the line spec, `CreateFcn`, a JGS script, 13 makers given
  a group or a transform, the hold, `'Parent'`, a group's children, a text in a transform. The
  graphics test classes and the parity fixtures pass.

## Divergences

- **A refusal on an axes that is not held.** R2025b refuses an unknown option before it clears the
  axes and keeps what was there; here the maker clears it, then the refusal takes back what it made,
  and the axes is left empty.
- **`pcolor`'s object is an `image` here**, a `surface` in R2025b (open item 167).

## Still open

- Open item 166: a replacing plot or `cla` deletes the axes' groups in R2025b, and an axes lists a
  group among its children rather than the group's members; here a group lives until its axes does,
  and the axes lists the members.
- Open item 167: `pcolor`'s object type.
- Open item 168: the leftovers the probes found next to these items: `title`, `hold` and the other
  axes verbs given a group (R2025b: `Using title with Group is not supported.`), a three-element colour
  read as a colour by `patch(x, y, c)` for three vertices, and R2025b's own words for a malformed
  `plot` (`MATLAB:plot:InvalidFirstInput`, `MATLAB:plot:InvalidPropertyValue` for a read-only name, a
  pair after the spec read as a property).
