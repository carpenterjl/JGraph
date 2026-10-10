# ADR 0222 — A graphics handle answers as R2025b's object: its methods, its lists, the names the model lacked, and its events: open items 68, 88 and 56

## Status

Accepted, 2026-10-10. The tenth batch of the open-items work (`docs/plans/open_task_chips_09_12_2026.md`,
gitignored), with ADRs 0221 and 0223. Every answer was recorded from R2025b (probes `probe_68`,
`probe_68b`, `probe_88`, `probe_88b`, `probe_88v` and `probe_56` in the open-items scratch, the
recordings under `tools/matlab-checklist/graphics-probes/recorded/`, then the fixture
`oi_handle_members`).

## Context

A handle is a number here (ADR 0051), and three things R2025b's handle objects do were missing.

- **Item 68.** `t.expand()`, `n.collapse`, `h.sendEventToHTMLSource('x', 1)` were refused as unknown
  properties, `methods(h)` and `properties(h)` refused a number, and an unknown name through the
  dot said `MATLAB:hg:InvalidProperty` in `get`'s sentence where R2025b says
  `MATLAB:noSuchMethodOrField` reading and `MATLAB:noPublicFieldForClass` writing.
- **Item 88.** A sweep of `get(h)` over 37 kinds of object (`probe_88`, the 44 recorded classes'
  makers) found 180 names R2025b lists that JGraph lacked — a figure's `ThemeMode`, an axes'
  `GridLineWidth` and its mode, a bar's `Labels` and `GroupWidth`, a legend's `Direction`, a ruler's
  font names, a title's `Margin` — and, the other way, a rectangle listing a patch's 30 names and an
  animated line a plain line's 14, because both are made from those models and answered as their
  union. An axes' `BubbleSizeLimits` and `BubbleSizeRange` are refused by R2025b's `get` and `set`
  (`MATLAB:class:GetProhibited`, `…SetProhibited`, naming the `_IS` twin). The sweep itself found
  `histogram(ax, x)` reading the axes as the data, and `heatmap(fig, cdata)` refused.
- **Item 56.** Only `ObjectBeingDestroyed` could be listened to on a graphics object; any other event
  and every property listener were refused. R2025b's `events(h)` lists `ObjectBeingDestroyed
  PropertyAdded PropertyRemoved` (and a component's own, `ButtonPushed`), `addlistener` takes every
  event of the class's metaclass (an axes' hidden `Hit` among them), and `addlistener(ax, 'XLim',
  'PostSet', cb)` fires for `xlim(ax, …)` and for `ax.XLim = …`, its first argument the property's
  metadata. An unknown event, an unknown property and one not SetObservable are refused in R2025b's
  three sentences.

## Decision

### The members, recorded (items 68 and 56)

`tools/matlab-checklist/graphics-probes/graphics_class_members.m` records, for 80 graphics and app
component classes, `methods(h)`, `properties(h)` in R2025b's order, the printed `methods` listing's
instance and static names (it leaves out what `handle` gives and nothing overrides), `events(h)`,
every event of the metaclass, and the SetObservable properties. `gen_graphics_class_names.ps1` writes
them to `JgsGraphicsProperties.R2025bMembers.cs` beside the names table; both now read committed
recordings (`recorded/`), so regenerating does not need MATLAB. `JgsGraphicsProperties.MatlabClassOf`
names an object's R2025b class (the recorded classes' first, a component's own), and `MembersOf`
answers its row.

### The dot (item 68)

A dot read on a graphics handle whose name no property answers, and which is one of the class's
methods, binds the builtin of that name to the handle (`BoundMethod`, as `obj.area` does on a user
object): `t.expand()`, `ln.get('Marker')`, `ln.set(…)`, `ln.isvalid`. `Get` and `Set` take a `dot`
flag, and in the MATLAB dialect an unknown name through the dot is refused in the dot's words, the
class in full. `methods(h)`, `properties(h)`, `ismethod(h, name)` and the printed listings answer the
recorded lists; `properties` now knows when it is asked for nothing.

### The names the model lacked (item 88)

`graphics_missing_defaults.m` records R2025b's value of each of the 180 names, and
`gen_graphics_defaults.py` writes the 162 that are the class's own constants to
`JgsGraphicsProperties.R2025bDefaults.cs`. In the MATLAB dialect `TryFind` falls back to them: each
answers R2025b's value until a script writes one, is listed in `get(h)`, and writing a name turns its
`…Mode` partner `'manual'` (`GridLineWidth` then `GridLineWidthMode`). Left out, and listed below: a
value that is an object or a table, and one that depends on the data or the layout.

What `rectangle` and `animatedline` make is marked (`RectangleShapes`, and every animated line in
`AnimatedCaps` with R2025b's default cap of a million points), so each answers as its own class:
`Type` reads `'rectangle'` and `'animatedline'`, `get(h)` lists that class's names alone, and the
model's others answer when named, as R2025b's hidden ones do. A rectangle's `Position` and
`Curvature` redraw its outline (a scalar curvature reads back as written), and `MaximumNumPoints`
trims the line. In the MATLAB dialect an axes' bubble size names are refused as R2025b refuses them
(`get`, `set`; the dot as for any unknown name). `histogram` and `heatmap` take an axes and a figure first.

### Events and property listeners (item 56)

`addlistener(h, 'Event', cb)` takes any event the recorded metaclass has; such a listener is told
where the callback of that name runs (`ButtonPushedFcn` is `ButtonPushed`), with the callback's event
data, after the callback, and also when no callback is set. `addlistener(h, prop, 'PreSet' or
'PostSet', cb)` checks the name against the object and the class's SetObservable list, and makes an
`event.proplistener`. A write through `set` or the dot raises `PreSet`, then `PostSet`, as a classdef
object's does; every other change is caught by `GraphicsPropertyWatch`, which hears the object's
`Invalidated` (raised from anything below it): on the script thread it reads the watched properties
again and tells each that changed, so `xlim(ax, …)` raises `PostSet` before the next statement; from
the window's thread it queues a `PropertyWatch` event, delivered at the next drain point.

## Divergences

- **R2025b's names whose values are objects, tables or layout measurements are still missing**:
  `Theme`, `InteractionOptions`, `Annotation` on a group, `SourceTable`, a ruler's `Exponent`
  (int32) and `MinorTickValues`, a title's `Position` and `Extent`, a quiver's `ScaleFactor`
  (`oi_handle_members`, `names_line`).
- **The names the model lacked change nothing that is drawn** (a bar's `Labels`, an axes'
  `GridLineWidth`, a legend's `Direction`): each reads and writes R2025b's value and keeps what a
  script writes, unchecked. A rectangle's `Position` and `Curvature` and an animated line's
  `MaximumNumPoints` are the exceptions.
- **The printed `methods` listing names `handle`'s methods in plain text**, where R2025b writes two
  hyperlinks (`methods_listing_line`), as for a user class since ADR 0216.
- **A rectangle and an animated line still answer the patch's and the line's names when named**
  (`isprop(r, 'Faces')` is 1), as JGraph's other extras do (ADR 0220).
- **`findprop` through the dot is refused**, as there is no `findprop`; R2025b answers a
  `GraphicsMetaProperty`.
- **A deleted handle forgets its class**: `methods(h)` and a dot on it are refused as on a number,
  where R2025b still lists a `Line`'s methods and refuses the dot in its own words.
- **A change the window makes is heard at the next drain point** (`drawnow`, `pause`, an idle session),
  and one noticed by the watch only when the value changed; R2025b raises `PostSet` inside every set,
  of the same value too.
- **The order of a component callback and its event's listeners is not recorded** (it needs a window
  session); listeners run after the callback.

## Consequences

`n.expand`, `h.sendEventToHTMLSource(...)` and `ln.set(...)` run as written in an App Designer app;
`methods(ln)` and `properties(b)` list what R2025b's do; a script copying `get(h)` from one object to
another sees R2025b's names; `addlistener(ax, 'XLim', 'PostSet', @syncOther)` keeps two axes together.
Tests: `OpenItemsBatch10Tests` (a rectangle's redraw, a window change queued), the rewritten
`AppDesignerU7Tests.APropertyListenerOnAGraphicsObjectHearsEveryWrite`; fixtures `oi_handle_members`
(52 lines, 2 stamped) and `u7_app`'s `running_plain` (now exact). The divergences ADR 0204 and ADR
0220 recorded for the dot, the events and the missing names are lifted from them.

## Still open

- A deleted handle's class and members; `findprop` on a graphics handle.
- The component-callback and listener order, in a MATLAB window session.
