# ADR 0254 — The designer's component groups

## Status

Accepted, 2026-10-21. Stage G1 of the designer leftovers plan (gitignored
`docs/plans/designer-leftovers-plan.md`, revision 1), which takes up open items 150-152 after the
designer plan closed (ADR 0253). This stage closes open item 150; what it leaves is item 165.

## Context

App Designer groups components on its canvas: a group is selected, moved and deleted as one, and
every labelled entry of its library - an edit field with its label, a gauge with its label - is a
group of the kind `LabeledComponent`. The file holds a group as an id and its parent group's id
(`components.Groups`) and each member's `DesignTimeProperties.GroupId`. The designer's stages DS2
to DS4 (ADRs 0246-0248) found this and did not build it: a label and its field moved separately,
the names paired them for a rename (`XLabel` beside `X`), a labelled pair dropped in a grid moved
and spanned apart, and a save kept a MATLAB-made app's groups only where their objects were kept.

The probe `oi150_groups` read every shipped app with App Designer's own loader: 27 of 44 have
groups, 293 grouped components in 143 groups, 133 of them labelled pairs and 3 inside another
group; `components.Groups` is a 1-by-N cell of `Id`/`ParentGroupId` structs; nothing marks a
labelled group, which App Designer's client works out from the members. The rules were read from
App Designer's own modules (`GroupUtil`, `GroupSelectionRules`, `WidgetSelectionRules`,
`GroupWidgetData`, the group commands, `DeleteCommand`, `LabeledComponentResizeCalculator`,
`GroupingAction`, `AddComponentCommand`, `XMLCanvasGroup*Behavior`, `CellLocationValidator`),
formatted for reading under the session's scratch folder.

## Decision

### The model (`DesignGroup`, `AppDesign.Groups`)

A group holds its members in order - components, or groups inside it - and its parent; a group of
exactly two components, one a label and the other not, is a labelled component and keeps its label
first (`_setLabeledComponentProperty`). The design holds every group and each component's group;
`AttachGroups` builds them from a file's records as App Designer's loader does (members in the
design's order, a member group where its first component is met, a parent the file lacks or a
loop of parents making a top group, a group of fewer than two members not made - `_createGroups`);
`GroupRecords` gives them back as the file holds them.

### The files

- **`.mlapp`.** `DesignTreeWriter.ReadGroups` reads `components.Groups` (the cell, or a struct of
  one) and each saved object's `GroupId`; a document opened from a package attaches them. On save
  each object's `GroupId` is set to its group's (a kept object's record read and rewritten only when
  it changes) and `components` is written again with the groups as `MLAPPSerializer` saves them when
  they differ from the file's; the tree is brought up when the groups changed (by id, parent or
  members in any order - a file lists a group's members in its objects' order) though the generated
  code did not. An editor-only save writes the file's own groups back as they are, over the
  components the code still has.
  A new file written whole carries them; a design with none writes no `Groups` field. R2025b's loader
  reads the files back (`oi150_verify`).
- **Plain text.** A labelled component's field is written with `label='<LabelCodeName>'`, the label
  itself with no attribute, and the groups that are not labelled pairs in the `app:componentGroups`
  section between the layout and the run configuration, a labelled pair inside one as its field's
  name and a group inside one nested, as DS7a's oracle rendered App Designer's builders (cases 4, 4b
  and 12). The reader here reads both back, with a problem for a label the layout lacks, a member in
  a group already or a group of one. R2025b's own loader reads neither (its `labelName` and
  `getComponentGroupData` stubs), so a plain-text app's groups are this designer's own until it does.

### The canvas (`DesignGroupRules`, `DesignDocument.Groups`, `DesignGestures`)

- **The selection** holds picks as App Designer's holds peer nodes: a component on its own, or a
  group selected whole (`Picks`); `Selection` is what they come to. A press follows
  `GroupSelectionRules` and `WidgetSelectionRules`: on a member it selects the member's own group -
  the member alone when the group or another member is selected already - and waits for the release
  when a group around it is selected, so that a drag moves what is selected; Alt alone drills up,
  past the outermost selected group of the chain; Ctrl alone or Shift alone adds within one
  container, takes a selected pick out, and swaps a member for its selected ancestor group; any
  other combination of keys does nothing, as `EventUtils` tests them. A right press selects a
  member's group unless a group around it is selected. A press on a group's own area, its members'
  union, selects the group. An edit that touched just what was selected keeps the picks.
- **Moving.** A group moves as one; from a grid into a grid, its own or another, a selection holding
  a group member moves by whole cells by `CellLocationValidator`'s rule (the anchor cell under the moving box's top-left corner,
  found through 5-pixel windows, the box moving to the next cell when its leading edge is past the
  cell's centre and less than half of it is left - for columns, not while its right edge is still in
  the box's last column; the whole box of cells must fit), transcribed into `DesignGridRules.GroupMove`
  and held to DS4's oracle of it, 15 cases.
- **Resizing.** A labelled component has side handles only, by its field's `ResizeType`: left and
  right for text and number fields, spinners, drop-downs, date and colour pickers; the free axis for
  a slider; all four otherwise. The field resizes by the dragged side (from the centre with Ctrl,
  snapped to a shown grid, never under its least size, a ratio kept) and the label follows by where
  it stands - before the field it moves only from the centre, after it with the edge, centred it
  stays centred, over the field it keeps within it (`LabeledComponentResizeCalculator`). In a grid
  the pair spans together (`GridSpanResize.LabeledEntries`, DS4) and commits as one edit.
- **Deleting.** A member leaves its group; a group left with one member or none goes, its last
  member joining the group it was in, and the group around is not looked at in turn
  (`DeleteComponentInGroupCommand`); a group selected whole goes with every group inside it
  (`findGroupsInSelection`); a selection of labelled fields alone deletes the pairs whole
  (`_filterAllAreComponentMembers`); undone, all back in their places (members put back from the
  first, inner groups after the outer).
- **The actions.** Group (Ctrl+G) needs two or more picks and no member among them; Add to Group one
  group among them; Remove from Group members of one top group, which keeps two; Ungroup groups
  that are in none - `GroupUtil`'s rules. `GroupingAction`'s grid exclusion sits behind a test of
  a property its selection manager does not have, so R2025b groups in a grid, and so does this
  designer (its shipped apps hold such groups). Each is one edit that sets the selection as its
  command does, every group's members when several are taken apart; the context menu and the
  toolbar's Grouping menu carry the four.
- **The library** groups a labelled entry's label and field as it makes them, on the canvas and in a
  grid; the inspector shows a labelled component's field with the label's text on top and sets the
  font, colours, `Enable` and `Visible` on both; a rename from a label's text pairs by the group,
  never by name any more.

## Consequences

- `JGraph.Scripting.Designer` has `DesignGroups` (`DesignGroup`, `DesignGroupMember`,
  `DesignGroupRecord`), `DesignGroupRules` (`DesignPick`, the press rules, the enabling rules, the
  labelled resize), `DesignDocument.Groups` (picks, boxes, the actions and their edits) and
  `DesignTreeWriter.Groups`; `DesignGridRules.GroupMove`; `MatMcosWriter.Keep`;
  `JgsMlapp.ReadGroups`; `WriteCode`, `WriteApp`, `AppDesignerDocument.Save` and `SaveNew` take the
  groups; `DesignDocument.SelectPicks`, `SetGridSpans`; `DesignKey.Group` and `Ungroup`.
- `DesignDocument.LabelledBy` pairs by the group only; the names no longer pair a label with a field.
- A selected group draws its outline four pixels outside its box and its members dashed; the status
  line names a group and a labelled component.
- The guide's "Designing an app" chapter has a Groups entry.
- No running script behaves differently.

## Measured

- **The files** (`AppDesignerGroupTests`, 9): the model's bookkeeping; the 26 shipped level-5 apps'
  136 groups, 279 members, 126 labelled pairs and 3 nested read as the probe counted them, bertool's
  three-member and six-member groups and its nested pair among them; a shipped app saved with its own
  text and groups left to the byte, with groups given or not; DataExport regrouped in the designer
  and saved, read back by this reader and by R2025b's (`oi150_verify`); a new file with groups and
  one without; the oracle's plain-text cases 4, 4b and 12 to the character, read back to the same
  groups; the reader's refusals, and a Group of one labelled component leaving the labelled
  component; records compared with the members in any order; a loop of parents and a record one
  component carries.
- **The canvas** (`AppDesignerGroupCanvasTests`, 8 + 15 oracle cases): the press rules over a group
  and nested groups, Alt and Ctrl; a group moved as one and undone; a labelled edit field from the
  library resized by its right and left sides with the label following, undone past its making and
  redone; a rename through the group; members deleted, a group of one dissolved into its parent,
  undone; a labelled field's delete taking its label, a group's taking its members; the four
  actions, their rules, keys, menu and undo; members deleted out of index order and put back in
  their places, a nested group deleted whole and back whole, a dissolve that does not climb, two
  groups taken apart at once, a right press, the odd modifiers, grouping in a grid; `GroupMove`
  against DS4's oracle.
- Every designer test (`AppDesigner*`): 670 pass; two were changed - the grid menu now ends with
  the grouping items, and a knob resized by its corner is selected on its own (a labelled knob has
  side handles).
- **The window** (`oi150w.ps1`, UI Automation patterns only, the Release app on a copy of
  DataExport): the Grouping menu's items, a member chosen in the browser selecting its group,
  Ungroup and Undo, the labelled drop-down selected as a pair with the inspector's label row; the
  file not rewritten; `workspace.json` unchanged.
- **The review** (an Opus workflow, three lenses, each finding verified): 14 findings, 12 confirmed
  and fixed under tests (a nested group's delete undone partway, members put back out of order,
  Remove from Group undone out of order, two groups taken apart selecting one's members, a save
  with no change rebuilding the tree over member order, a loop of parents overflowing the stack, a
  plain-text Group of one dissolving its labelled component, a right press selecting the member,
  Ctrl+Shift and Alt on a plain component selecting, Group refused in a grid on a check that never
  runs in R2025b, a dissolve climbing to the group around, a group refused into another grid); two
  rejected (a struct-array `Groups` nothing writes; the labelled resize snapping to the figure's
  grid, which R2025b's calculator does not do either).

## Not measured

- The mouse in the real window: the rules are tested through `DesignGestures`, which the canvas only feeds.
- A labelled component whose field is drawn larger than its Position (a slider's track): App
  Designer's `outerArtOffsets` are zero here, so the label follows the field's drawn box.
- App Designer's rubber band over a group's members (`_processRubberBand` not read): here it selects
  their group.
- A labelled entry dropped on a grid from the library goes by DS4's cell rules, not by
  `CellLocationValidator`'s `FromPalette` branch (the oracle's case m); a group from a container
  that places by position is refused by a grid (App Designer's `SelectionValidator` refuses
  several members from there, and a group selected whole was not measured).

## Divergences

None in a running script: groups are design time. R2025b's App Designer reads no groups from a
plain-text app and never links a `label` attribute (its loader reads `labelName`); this designer reads
what it writes.
