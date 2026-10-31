// What R2025b's App Designer does in its component browser, from its own code run unmodified - for the
// App Designer window plan's stage W7a, whose DesignBrowser transcribes it:
//   node browser.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignBrowserOracle.json
//
// The table: what each class's model says of itself (ComponentTreeBehavior, ReversedComponentViewOrder,
// IsSameTypeSelection, canMoveToParent's kind), read out of the model's own text.
//
// The rows: ComponentTreeViewModel.getTreeData over the three strategies, loaded whole, and AppViewModel's
// _initializeComponentViewOrder cut out of the bundle as it is.
//
// The drags: ComponentTreeView's _validateDrag and _validateDrop, cut out as they are.
//
// The drops: appdesigner/command/TreeDragReorderCommand loaded whole over ReorderCommand loaded whole, with
// the target's children as the tree store's move leaves them (mw-data-model's place rule).
//
// The selection: ComponentTreeView's _setSelection and what it calls, cut out as they are, over a stand-in
// for the selection model manager with its set and add as they are written.
//
// The layout: ComponentBrowserLayoutManager's _layoutChangeHandler, cut out as it is.
//
// The peer nodes are stand-ins. A scene that throws records the error instead of a result (`threw`).
'use strict';
const path = require('path');
const { load, stub, moduleText } = require(path.join(__dirname, '..', '..', 'w0', 'oracle', 'amd.js'));

class Stub {}
stub('dojo/_base/lang', { clone: v => (v === undefined ? v : JSON.parse(JSON.stringify(v))), mixin: Object.assign });
stub('dojo/on', Object.assign(() => ({ remove() {} }), { pausable: () => ({ remove() {} }) }));
stub('dijit/Destroyable', class { own() {} destroy() {} });
stub('appdesigner_interface/uiframework/viewmodel/_UIViewModel', class { constructor() {} emit() {} destroy() {} });
stub('appdesigner/command/_UndoableCommand', class { constructor(appModel) { this._appModel = appModel; } destroy() {} getIdsFromPeerNodes(l) { return l.map(p => p.getId()); } getPeerNodesByIds(l) { return l.map(i => this._appModel.getPeerNodeById(i)); } });
stub('appdesigner/command/MoveCommand', class { constructor(appModel, sources, from, to, offset, index) { this.kind = 'move'; this.appModel = appModel; this.sources = sources; this.to = to; this.index = index; } execute() { for (const s of this.sources) { s.parent.kids.splice(s.parent.kids.indexOf(s), 1); this.to.kids.splice(this.index, 0, s); s.parent = this.to; } } });
stub('appdesigner/command/DeleteAndCopyCommand', class { constructor(appModel, source, to, index) { this.kind = 'recreate'; this.source = source; this.to = to; this.index = index; } execute() { const s = this.source; s.parent.kids.splice(s.parent.kids.indexOf(s), 1); this.to.kids.splice(this.index, 0, s); s.parent = this.to; } });
stub('appdesigner/flux/intent/AppIntentTypes', new Proxy({}, { get: (_, k) => String(k) }));
stub('appdesigner/util/AppDetailsUtil', { isInspectableApp: n => n.type === 'App', getMlappFileIconUri: () => 'icon', getInspectableAppType: () => 'App' });
stub('appdesigner/util/CodeEditUtil', { getMethodObjectArgumentName: () => 'app' });
stub('appdesigner/view/canvas/layouttemplate/LayoutTemplateConstants', {});
const FigureUtil = load('appdesigner/util/FigureUtil');
const GroupUtil = load('appdesigner/util/GroupUtil');
const ReorderCommand = load('appdesigner/command/ReorderCommand');
const TreeDragReorderCommand = load('appdesigner/command/TreeDragReorderCommand');
const ViewModel = load('appdesigner/model/viewmodel/ComponentTreeViewModel');
const Complete = load('appdesigner/view/browser/strategy/CompleteComponentTreeDisplayStrategy');
const Exclude = load('appdesigner/view/browser/strategy/ExcludeLabelsComponentTreeDisplayStrategy');
const TabOrder = load('appdesigner/view/browser/strategy/TabOrderComponentTreeDisplayStrategy');

// A method of a module, as `function(args){body}` closed over the module's own names for its imports.
function method(module, name, closure) {
  const text = moduleText(module);
  const re = new RegExp('[,{}]' + name + '(?::function)?\\(([\\w,]*)\\)\\{');
  const m = re.exec(text);
  if (!m) throw new Error(module + ' has no ' + name);
  let i = m.index + m[0].length - 1, d = 0, q = null;
  const start = i;
  for (; i < text.length; i++) {
    const c = text[i];
    if (q) { if (c === '\\') { i++; continue; } if (c === q) q = null; continue; }
    if (c === '"' || c === "'" || c === '`') { q = c; continue; }
    if (c === '{') d++;
    else if (c === '}') { d--; if (d === 0) break; }
  }
  const names = Object.keys(closure);
  // eslint-disable-next-line no-new-func
  const make = new Function(...names, 'return function(' + m[1] + '){' + text.slice(start + 1, i) + '}');
  return make(...names.map(k => closure[k]));
}

// --- the table ---------------------------------------------------------------------------------------
const MODELS = {
  'matlab.ui.Figure': 'UIFigureModel', 'matlab.ui.control.Button': 'PushButtonModel', 'matlab.ui.control.StateButton': 'StateButtonModel',
  'matlab.ui.control.Label': 'LabelModel', 'matlab.ui.control.EditField': 'TextFieldModel', 'matlab.ui.control.NumericEditField': 'NumberFieldModel',
  'matlab.ui.control.TextArea': 'TextAreaModel', 'matlab.ui.control.Slider': 'SliderModel', 'matlab.ui.control.RangeSlider': 'RangeSliderModel',
  'matlab.ui.control.Knob': 'ContinuousKnobModel', 'matlab.ui.control.DiscreteKnob': 'DiscreteKnobModel', 'matlab.ui.control.Lamp': 'LampModel',
  'matlab.ui.control.Gauge': 'CircularGaugeModel', 'matlab.ui.control.LinearGauge': 'LinearGaugeModel', 'matlab.ui.control.NinetyDegreeGauge': 'NinetyDegreeGaugeModel',
  'matlab.ui.control.SemicircularGauge': 'SemicircularGaugeModel', 'matlab.ui.control.Switch': 'SliderSwitchModel', 'matlab.ui.control.ToggleSwitch': 'ToggleSwitchModel',
  'matlab.ui.control.RockerSwitch': 'RockerSwitchModel', 'matlab.ui.control.CheckBox': 'CheckBoxModel', 'matlab.ui.control.DropDown': 'DropDownModel',
  'matlab.ui.control.ListBox': 'ListBoxModel', 'matlab.ui.control.Spinner': 'SpinnerModel', 'matlab.ui.control.Table': 'TableModel', 'matlab.ui.control.Image': 'ImageModel',
  'matlab.ui.control.Hyperlink': 'HyperlinkModel', 'matlab.ui.control.HTML': 'HTMLModel', 'matlab.ui.control.DatePicker': 'DatePickerModel', 'matlab.ui.control.ColorPicker': 'ColorPickerModel',
  'matlab.ui.control.UIAxes': 'UIAxesModel', 'matlab.ui.container.Panel': 'PanelModel', 'matlab.ui.container.TabGroup': 'TabGroupModel', 'matlab.ui.container.Tab': 'TabModel',
  'matlab.ui.container.ButtonGroup': 'ButtonGroupModel', 'matlab.ui.container.Tree': 'TreeModel', 'matlab.ui.container.CheckBoxTree': 'CheckBoxTreeModel',
  'matlab.ui.container.TreeNode': 'TreeNodeModel', 'matlab.ui.container.Menu': 'MenuModel', 'matlab.ui.container.ContextMenu': 'ContextMenuModel',
  'matlab.ui.container.Toolbar': 'ToolbarModel', 'matlab.ui.container.toolbar.PushTool': 'PushToolModel', 'matlab.ui.container.toolbar.ToggleTool': 'ToggleToolModel',
  'matlab.ui.container.GridLayout': 'GridLayoutModel', 'matlab.ui.control.RadioButton': 'RadioButtonModel', 'matlab.ui.control.ToggleButton': 'ToggleButtonModel',
};
function lastFlag(text, name, dflt) {
  const re = new RegExp(name + ':\\(\\)=>!([01])', 'g');
  let m, v = dflt;
  while ((m = re.exec(text)) !== null) v = m[1] === '0';
  return v;
}
function lastValue(text, name, dflt) {
  const re = new RegExp(name + ':!([01])', 'g');
  let m, v = dflt;
  while ((m = re.exec(text)) !== null) v = m[1] === '0';
  return v;
}
const behaviours = {};
for (const [type, model] of Object.entries(MODELS)) {
  const text = moduleText('uicomponents_appdesigner_plugin/model/' + model) || '';
  behaviours[type] = {
    canReorder: lastFlag(text, 'canDragReorderInTree', true),
    canReparent: lastFlag(text, 'canDragReparentInTree', false),
    tabOrder: lastFlag(text, 'isShownInTabOrderTree', true),
    adjacent: lastFlag(text, 'shouldComponentsOfSameTypeBeAdjacent', false),
    reversed: lastValue(text, 'ReversedComponentViewOrder', true),
    sameType: lastValue(text, 'IsSameTypeSelection', false),
    single: lastFlag(text, 'isSingleSelection', false),
  };
}
// A check box tree's node is selected alone (TreeNodeModel's isSingleSelection reads its tree).
const canMove = {
  'matlab.ui.container.Menu': p => ['matlab.ui.container.Menu', 'matlab.ui.container.ContextMenu', 'matlab.ui.Figure'].includes(p.type),
  'matlab.ui.container.ContextMenu': p => ['matlab.ui.container.Menu', 'matlab.ui.container.ContextMenu', 'matlab.ui.Figure'].includes(p.type),
  'matlab.ui.container.toolbar.PushTool': p => p.type === 'matlab.ui.container.Toolbar',
  'matlab.ui.container.toolbar.ToggleTool': p => p.type === 'matlab.ui.container.Toolbar',
  'matlab.ui.container.Toolbar': p => p.type === 'matlab.ui.Figure',
  'matlab.ui.container.Tab': p => p.type === 'matlab.ui.container.TabGroup',
  'matlab.ui.container.TreeNode': p => ['matlab.ui.container.Tree', 'matlab.ui.container.CheckBoxTree', 'matlab.ui.container.TreeNode'].includes(p.type),
};
const recreate = {
  'matlab.ui.container.Menu': (to, from) => to.type === 'matlab.ui.Figure' || from.type === 'matlab.ui.Figure' || to.kids.length === 0 || from.kids.length === 1,
  'matlab.ui.container.Tab': () => true,
};

let seed = 20261030;
function rand() { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; }
const int = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));
const pick = a => a[int(0, a.length - 1)];

// --- the tree ----------------------------------------------------------------------------------------
function adapter(type) {
  const b = behaviours[type] || { canReorder: true, canReparent: false, tabOrder: true, adjacent: false, reversed: true, sameType: false, single: false };
  return {
    IsInvisibleToUser: false,
    ReversedComponentViewOrder: b.reversed,
    IsSameTypeSelection: b.sameType,
    isSingleSelection: n => (type === 'matlab.ui.container.TreeNode' ? treeOf(n).type === 'matlab.ui.container.CheckBoxTree' : b.single),
    canMoveToParent: canMove[type] || (() => true),
    ComponentTreeBehavior: {
      canDragReorderInTree: () => b.canReorder,
      canDragReparentInTree: () => b.canReparent,
      isShownInTabOrderTree: () => b.tabOrder,
      shouldComponentsOfSameTypeBeAdjacent: () => b.adjacent,
      shouldRecreateViewOnDragReparent: (to, from) => (recreate[type] ? recreate[type](to, from) : false),
    },
  };
}
function treeOf(n) { let u = n.parent; while (u.type === 'matlab.ui.container.TreeNode') u = u.parent; return u; }

function scene() {
  const ids = new Map();
  let count = 0;
  const make = (type, name, parent) => {
    const n = {
      id: name, type, parent, kids: [], name, group: null,
      getId() { return this.id; }, getType() { return this.type; }, getParent() { return this.parent; },
      getChildren() { return this.kids.slice(); }, getChildIndex(c) { return this.kids.indexOf(c); },
      getNumberOfChildren() { return this.kids.length; },
      getProperty(p) { return p === 'CodeName' ? this.name : p === 'GroupId' ? this.group && this.group.id : undefined; },
      getProperties() { return { Metadata: { AppType: 'Standard' } }; },
      isGroup() { return false; }, isGroupMember() { return !!this.group; },
      getController() { return null; },
      ComponentAdapter: adapter(type),
    };
    if (parent) parent.kids.push(n);
    ids.set(name, n);
    return n;
  };
  const appModelNode = { getType: () => 'AppModel', getId: () => 'AppModel', getChildren: () => [] };
  const inspectable = make('App', 'MyApp', null);
  inspectable.ComponentAdapter = { IsInvisibleToUser: false, isSingleSelection: () => true, canMoveToParent: () => false, ReversedComponentViewOrder: false, IsSameTypeSelection: false, ComponentTreeBehavior: { canDragReorderInTree: () => false, canDragReparentInTree: () => false, isShownInTabOrderTree: () => true, shouldComponentsOfSameTypeBeAdjacent: () => true, shouldRecreateViewOnDragReparent: () => false } };
  inspectable.parent = appModelNode;
  const figure = make('matlab.ui.Figure', 'UIFigure', null);
  figure.parent = appModelNode;
  const groups = [];
  const name = stem => stem + (++count);

  const LEAVES = ['Button', 'Label', 'EditField', 'Slider', 'Lamp', 'Knob', 'Switch', 'Gauge', 'CheckBox', 'DropDown', 'Table', 'Image', 'Hyperlink', 'Spinner'];
  const full = s => 'matlab.ui.control.' + s;

  // In the order R2025b's figure holds them: menus, toolbars, axes, the rest, context menus.
  function fill(parent, depth) {
    if (parent === figure) {
      for (let k = int(0, 2); k > 0; k--) {
        const m = make('matlab.ui.container.Menu', name('Menu'), parent);
        for (let j = int(0, 2); j > 0; j--) make('matlab.ui.container.Menu', name('Item'), m);
      }
      for (let k = int(0, 2); k > 0; k--) {
        const t = make('matlab.ui.container.Toolbar', name('Toolbar'), parent);
        for (let j = int(0, 3); j > 0; j--) make(pick(['matlab.ui.container.toolbar.PushTool', 'matlab.ui.container.toolbar.ToggleTool']), name('Tool'), t);
      }
    }
    for (let k = int(0, 1); k > 0; k--) make('matlab.ui.control.UIAxes', name('Axes'), parent);
    for (let k = int(1, 5); k > 0; k--) {
      const r = rand();
      if (r < 0.2 && depth < 2) { fill(make('matlab.ui.container.Panel', name('Panel'), parent), depth + 1); continue; }
      if (r < 0.3 && depth < 2) {
        const g = make('matlab.ui.container.TabGroup', name('TabGroup'), parent);
        for (let j = int(1, 3); j > 0; j--) fill(make('matlab.ui.container.Tab', name('Tab'), g), depth + 1);
        continue;
      }
      if (r < 0.4) {
        const tr = make(pick(['matlab.ui.container.Tree', 'matlab.ui.container.CheckBoxTree']), name('Tree'), parent);
        for (let j = int(0, 3); j > 0; j--) {
          const a = make('matlab.ui.container.TreeNode', name('Node'), tr);
          for (let i = int(0, 2); i > 0; i--) make('matlab.ui.container.TreeNode', name('Node'), a);
        }
        continue;
      }
      if (r < 0.55) {
        // A labelled component: the label before the field, as R2025b makes them.
        const stem = pick(['EditField', 'Slider', 'Knob', 'Lamp']);
        const label = make('matlab.ui.control.Label', name(stem + 'Label'), parent);
        const field = make(full(stem), name(stem), parent);
        const group = { id: 'g' + groups.length, label, field };
        label.group = field.group = group;
        groups.push(group);
        continue;
      }
      const s = pick(LEAVES);
      make(full(s), name(s), parent);
    }
    if (parent === figure) {
      for (let k = int(0, 1); k > 0; k--) {
        const c = make('matlab.ui.container.ContextMenu', name('ContextMenu'), parent);
        for (let j = int(1, 2); j > 0; j--) make('matlab.ui.container.Menu', name('Item'), c);
      }
    }
  }
  fill(figure, 0);

  // GroupUtil's group peer nodes for the labelled ones, enough for isLabelOfLabeledComponent.
  const manager = {
    getDescendant: id => {
      const g = groups.find(x => x.id === id);
      return g && { getProperty: p => (p === 'LabeledComponent' ? true : undefined) };
    },
    getChildren: () => [],
  };
  const widgets = { getGroupManagerPeerNode: () => manager, getAppWindowWidgetData: () => figure };
  const selection = [];
  const manager2 = {
    current: selection,
    _setting: null,
    getComponentSelection() { return this.current; },
    setComponentSelection(list) { this.current = Array.isArray(list) ? list : [list]; },
    addComponentSelection(list) { for (const r of list) if (this.current.indexOf(r) === -1) this.current.push(r); },
    isSelected(id) { return this.current.some(c => c.getId() === String(id)); },
    isAppWindowSelected() { return this.current.length === 1 && this.current[0].type === 'matlab.ui.Figure'; },
  };
  const appModel = {
    getInspectableAppPeerNode: () => inspectable,
    getWidgetDataManager: () => widgets,
    getPeerNodeById: id => ids.get(String(id)),
    getSelectionModelManager: () => manager2,
    getUndoRedoManager: () => ({ addCommand() {} }),
    hasGroups: () => groups.length > 0,
  };
  return { ids, inspectable, figure, groups, appModel, manager2, widgets };
}

const dump = n => {
  const o = { n: n.name, t: n.type };
  if (n.kids.length) o.k = n.kids.map(dump);
  return o;
};
const groupsOf = s => s.groups.map(g => [g.label.name, g.field.name]);

// --- the view order and the rows ---------------------------------------------------------------------
const AV = 'appdesigner/model/viewmodel/AppViewModel';
const avClosure = {
  i: FigureUtil,
  e: { numberOfChildMenusInPeerNode: p => p.kids.filter(c => c.type === 'matlab.ui.container.Menu').length, numberOfChildContextMenusInPeerNode: p => p.kids.filter(c => c.type === 'matlab.ui.container.ContextMenu').length },
  r: { numberOfToolbarsInPeerNode: p => p.kids.filter(c => c.type === 'matlab.ui.container.Toolbar').length },
  n: {
    isAxes: c => c.type === 'matlab.ui.control.UIAxes',
    getFirstChildAxesIndex: p => p.kids.findIndex(c => c.type === 'matlab.ui.control.UIAxes'),
  },
};
function flat(s) {
  const vm = {
    _flatComponentViewOrderList: [s.inspectable, s.figure],
    _addPeerNodeListeners() {},
    _initializeComponentViewOrder: method(AV, '_initializeComponentViewOrder', avClosure),
    _calculateInitialViewOrderInsertAtIndex: method(AV, '_calculateInitialViewOrderInsertAtIndex', avClosure),
    _adjustIndexRelativeToViewOrder: method(AV, '_adjustIndexRelativeToViewOrder', avClosure),
    _getComponentsUnderSameParent: method(AV, '_getComponentsUnderSameParent', avClosure),
    _isComponentOrderReversed: method(AV, '_isComponentOrderReversed', avClosure),
  };
  vm._initializeComponentViewOrder(s.figure);
  return vm._flatComponentViewOrderList;
}

const STRATEGIES = { COMPLETE: Complete, EXCLUDE: Exclude, TAB: TabOrder };
function treeData(s, which) {
  const list = flat(s);
  const vm = Object.create(ViewModel.prototype);
  vm._appModel = s.appModel;
  vm._flatComponentViewOrderList = list;
  vm._strategy = new STRATEGIES[which]();
  return vm.getTreeData();
}

const rows = [];
for (let c = 0; c < 200; c++) {
  const s = scene();
  const one = { tree: dump(s.figure), groups: groupsOf(s) };
  for (const which of Object.keys(STRATEGIES)) {
    try {
      one[which] = treeData(s, which).map(d => ({ label: d.label, parent: d.parent, readOnly: d.isReadOnly }));
    } catch (e) { one[which] = { threw: e.message }; }
  }
  rows.push(one);
}

// --- the drags ---------------------------------------------------------------------------------------
const TV = 'appdesigner/view/browser/ComponentTreeView';
// Its factory's names: D FigureUtil.
const tvClosure = { D: FigureUtil, M: GroupUtil };
const validateDrag = method(TV, '_validateDrag', tvClosure);
const validateDrop = method(TV, '_validateDrop', tvClosure);
const drags = [];
for (let c = 0; c < 300; c++) {
  const s = scene();
  const all = [...s.ids.values()];
  const parents = all.filter(n => n.kids.length > 0);
  const sourceParent = pick(parents);
  const sources = sourceParent.kids.filter(() => rand() < 0.5);
  if (sources.length === 0) sources.push(pick(sourceParent.kids));
  // Sometimes a member of another parent as well, as a drag's nodes can only come from the selection.
  const target = pick(all);
  const me = { _appModel: s.appModel };
  const ids = sources.map(n => n.getId());
  let canDrag, canDrop;
  try { canDrag = validateDrag.call(me, { nodeIds: ids }); } catch (e) { canDrag = { threw: e.message }; }
  try { canDrop = validateDrop.call(me, { nodeIds: ids }, { nodeId: target.getId() }); } catch (e) { canDrop = { threw: e.message }; }
  drags.push({ tree: dump(s.figure), sources: ids, target: target.getId(), canDrag, canDrop });
}

// --- the drops ---------------------------------------------------------------------------------------
// Where the tree store's move leaves the moved rows (_calcPlacementIndex's before and after), one source
// at a time, among the target's rows in the tree.
function moved(children, sources, place, at) {
  let list = children.slice();
  for (const s of sources) {
    list = list.filter(n => n !== s);
    if (place === 'first') list.unshift(s);
    else if (place === 'last') list.push(s);
    else {
      const k = list.indexOf(at);
      list.splice(place === 'before' ? k : k + 1, 0, s);
    }
  }
  return list;
}

const drops = [];
for (let c = 0; c < 1200; c++) {
  const s = scene();
  const which = pick(['COMPLETE', 'EXCLUDE', 'TAB']);
  const data = treeData(s, which);
  const shown = data.map(d => s.ids.get(d.id));
  const childrenInTree = p => data.filter(d => d.parent === p.getId()).map(d => s.ids.get(d.id));
  const parents = [...new Set(data.map(d => d.parent).filter(Boolean))].map(id => s.ids.get(id)).filter(p => p && p.type !== 'App' && childrenInTree(p).length >= 2);
  if (parents.length === 0) { c--; continue; }
  const parent = pick(parents);
  const rowsIn = childrenInTree(parent);
  const sources = rowsIn.filter(() => rand() < 0.35);
  if (sources.length === 0) sources.push(pick(rowsIn));
  // A drag's nodes: of one kind, as _validateDrag lets them be.
  const lead = sources[0];
  const same = lead.ComponentAdapter.ComponentTreeBehavior.shouldComponentsOfSameTypeBeAdjacent()
    ? sources.filter(n => n.type === lead.type)
    : sources.filter(n => !n.ComponentAdapter.ComponentTreeBehavior.shouldComponentsOfSameTypeBeAdjacent());
  const others = rowsIn.filter(n => !same.includes(n));
  if (others.length === 0) { c--; continue; }
  const at = pick(others);
  const place = pick(['before', 'after']);
  const before = parent.kids.map(k => k.name);
  const after = moved(rowsIn, same, place, at);
  let result;
  const strategyName = { COMPLETE: 'COMPLETE_COMPONENT_TREE', EXCLUDE: 'EXCLUDE_LABELS_COMPONENT_TREE', TAB: 'TAB_ORDER_COMPONENT_TREE' }[which];
  s.appModel.getWidgetDataManager = () => ({
    moveComponents(nodes, into, index) {
      for (const n of nodes) {
        into.kids.splice(into.kids.indexOf(n), 1);
        into.kids.splice(index, 0, n);
      }
    },
    getGroupManagerPeerNode: () => s.widgets.getGroupManagerPeerNode(),
    getAppWindowWidgetData: () => s.figure,
  });
  s.manager2.current = same.slice();
  try {
    const cmd = new TreeDragReorderCommand(s.appModel, same, parent, after, strategyName);
    cmd.execute();
    result = parent.kids.map(k => k.name);
  } catch (e) { result = { threw: e.message }; }
  drops.push({
    tree: null, which, parent: parent.getId(), before, types: parent.kids.map(k => k.type),
    rows: rowsIn.map(n => n.name), sources: same.map(n => n.name), rowsAfter: after.map(n => n.name), after: result,
  });
}

// --- reparents ---------------------------------------------------------------------------------------
const reparents = [];
for (let c = 0; c < 200; c++) {
  const s = scene();
  const data = treeData(s, 'COMPLETE');
  const movable = data.map(d => s.ids.get(d.id)).filter(n => n.type !== 'App' && n.type !== 'matlab.ui.Figure'
    && n.ComponentAdapter.ComponentTreeBehavior.canDragReparentInTree());
  if (movable.length === 0) { c--; continue; }
  const source = pick(movable);
  const targets = [...s.ids.values()].filter(t => t !== source && t !== source.parent && t.type !== 'App' && source.ComponentAdapter.canMoveToParent(t));
  if (targets.length === 0) { c--; continue; }
  const target = pick(targets);
  // Not into itself.
  let u = target, inside = false;
  while (u && u.getId && u.type !== 'App' && u.parent && u.parent.getId) { if (u === source) inside = true; u = u.parent; }
  if (inside) { c--; continue; }
  const rowsIn = data.filter(d => d.parent === target.getId()).map(d => s.ids.get(d.id));
  const after = moved(rowsIn, [source], 'first');
  const from = source.parent;
  const fromName = from.name, targetName = target.name;
  const fromBefore = from.kids.map(k => k.name);
  const toBefore = target.kids.map(k => k.name);
  const cmd = new TreeDragReorderCommand(s.appModel, [source], target, after, 'COMPLETE_COMPONENT_TREE');
  const recreateIt = source.ComponentAdapter.ComponentTreeBehavior.shouldRecreateViewOnDragReparent(target, from);
  let threw = null;
  try { cmd.execute(); } catch (e) { threw = e.message; }
  reparents.push({
    source: source.name, type: source.type, from: fromName, to: targetName, targetType: target.type, fromBefore, toBefore,
    recreate: recreateIt, toAfter: target.kids.map(k => k.name), threw,
  });
}

// --- the selection -----------------------------------------------------------------------------------
const setSelection = method(TV, '_setSelection', tvClosure);
const special = method(TV, '_getCommandIfSelectedItemIsFigureOrSingleSelectionOnly', tvClosure);
const first = method(TV, '_selectComponentIfNoPreviousSelectionPresent', tvClosure);
const verify = method(TV, '_verifyCanAddToMultiSelect', tvClosure);
const selections = [];
for (let c = 0; c < 800; c++) {
  const s = scene();
  const all = [...s.ids.values()].filter(n => n.type !== 'App');
  const parent = pick(all.filter(n => n.kids.length > 0));
  const mix = rand() < 0.25;
  const pool = mix ? all : parent.kids;
  const start = rand() < 0.2 ? [] : pool.filter(() => rand() < 0.3).slice(0, 3);
  const asked = pool.filter(() => rand() < 0.4).slice(0, 5);
  if (rand() < 0.1) asked.push(s.figure);
  // The order they are asked in is the tree's, or a Ctrl-click's last.
  s.manager2.current = start.slice();
  const me = {
    _appModel: s.appModel, _selectionModelManager: s.manager2,
    _getCommandIfSelectedItemIsFigureOrSingleSelectionOnly: special,
    _selectComponentIfNoPreviousSelectionPresent: first,
    _verifyCanAddToMultiSelect: verify,
  };
  let after;
  try {
    setSelection.call(me, asked.map(n => n.getId()));
    after = s.manager2.current.map(n => n.name);
  } catch (e) { after = { threw: e.message }; }
  selections.push({
    types: Object.fromEntries(all.map(n => [n.name, n.type])), parents: Object.fromEntries(all.map(n => [n.name, n.parent.name || 'App'])),
    checkTree: Object.fromEntries(all.filter(n => n.type === 'matlab.ui.container.TreeNode').map(n => [n.name, treeOf(n).type === 'matlab.ui.container.CheckBoxTree'])),
    start: start.map(n => n.name), asked: asked.map(n => n.name), after,
  });
}

// --- the layout --------------------------------------------------------------------------------------
const layoutChange = method('appdesigner/view/browser/ComponentBrowserLayoutManager', '_layoutChangeHandler', {});
const layouts = [];
for (let c = 0; c < 400; c++) {
  const height = int(100, 900);
  const manual = rand() < 0.3;
  const manualTop = int(60, 500);
  const previous = rand() < 0.5 ? 65 : int(58, 400);
  const newHeight = pick([0, 25, 21, 42, 63, 84, 105, 210, 420, 840, int(1, 40) * 21]);
  const me = {
    domNode: { clientHeight: height }, _manuallyResized: manual, _manualSizeTop: manualTop, _previousHeight: previous,
    setFreeDimension(which, value) { this.set = value; },
  };
  layoutChange.call(me, { newHeight });
  layouts.push({ height, manual, manualTop, previous, newHeight, set: me.set === undefined ? null : me.set, previousAfter: me._previousHeight });
}

process.stdout.write(JSON.stringify({ behaviours, rows, drags, drops, reparents, selections, layouts }) + '\n');
