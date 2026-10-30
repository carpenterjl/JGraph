// How R2025b's App Designer moves a tab, a tree node or a component among its siblings, and where it
// puts a radio or toggle button added to a button group, from its own code run unmodified - for the
// App Designer window plan's stage W5c, whose DesignStructure transcribes them:
//   node structure.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignStructureOracle.json
//
// The moves: ReorderAction's _checkAbilityToReorder, _getReorderableComponents, _filterAndOrderByIndex
// and _performReorder, cut out of the bundle as they are and given its utilities, over
// appdesigner/command/ReorderCommand loaded whole - both the internal order (Move First, Left, Right,
// Last and a tab heading's drag, DESIGN_REORDER_OFFSET with the {Offset, DropIndex} TabDragController
// passes) and the stack order (Bring to Front ... Send to Back). The peer nodes and the widget data
// manager are stand-ins: a parent's children in a list, a move taking a child out and putting it in
// at its new index.
//
// The buttons: appdesigner/util/MutualExclusiveButtonUtil loaded whole, its PositionUtil giving the
// group's content height, its code name generator giving no names (DesignNames is checked elsewhere).
'use strict';
const path = require('path');
const { load, stub, moduleText } = require(path.join(__dirname, '..', '..', 'w0', 'oracle', 'amd.js'));

stub('dojo/_base/lang', { clone: v => (v === undefined ? v : JSON.parse(JSON.stringify(v))), mixin: Object.assign });
stub('appdesigner/command/_UndoableCommand', class { constructor(appModel) { this._appModel = appModel; } destroy() {} });
stub('appdesigner/util/PositionUtil', { getContentAreaHeight: d => d.ContentHeight });

const ReorderCommand = load('appdesigner/command/ReorderCommand');
const ButtonUtil = load('appdesigner/util/MutualExclusiveButtonUtil');

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

// ReorderAction's imports as its factory names them: i MenuUtil, o AxesUtil, e ToolbarUtil,
// d ReorderCommand, s DesignGesturesUtil. The scenes hold no menus, axes, toolbars or context menus.
const A = 'appdesigner/action/ReorderAction';
const closure = {
  i: { isMenu: () => false, isContextMenu: () => false, numberOfChildMenusInPeerNode: () => 0, numberOfChildContextMenusInPeerNode: () => 0 },
  o: { isAxes: () => false, getFirstChildAxesIndex: () => 0, numberOfChildAxesInPeerNode: () => 0 },
  e: { isToolbar: () => false, numberOfToolbarsInPeerNode: () => 0 },
  d: ReorderCommand,
  s: { supports: (n, g) => n.ComponentAdapter.DesignGestures[g](n) },
};
const action = {
  _IS_STACK_ORDER: true,
  _performReorder: method(A, '_performReorder', closure),
  _filterAndOrderByIndex: method(A, '_filterAndOrderByIndex', closure),
  _checkAbilityToReorder: method(A, '_checkAbilityToReorder', closure),
  _getReorderableComponents: method(A, '_getReorderableComponents', closure),
  _isReStackAbleComponent: method(A, '_isReStackAbleComponent', closure),
};

let seed = 20261029;
function rand() {
  seed = (seed * 1103515245 + 12345) % 2147483648;
  return seed / 2147483648;
}
const int = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));

function sceneOf(n, selected) {
  const parent = { id: 'P', kids: [] };
  parent.getId = () => 'P';
  parent.getChildren = () => parent.kids.slice();
  parent.getChildIndex = c => parent.kids.indexOf(c);
  parent.getNumberOfChildren = () => parent.kids.length;
  const byId = { P: parent };
  for (let k = 0; k < n; k++) {
    const node = { id: 'c' + k };
    node.getId = () => node.id;
    node.getParent = () => parent;
    node.ComponentAdapter = { DesignGestures: { CanReorder: () => true, CanStack: () => true } };
    parent.kids.push(node);
    byId[node.id] = node;
  }
  const selection = selected.map(k => parent.kids[k]);
  const appModel = {
    getSelectionModelManager: () => ({ getComponentSelection: () => selection.slice(), setComponentSelection() {} }),
    getViewState: () => ({ isDesignerMode: () => true }),
    getUndoRedoManager: () => ({ addCommand() {} }),
    getPeerNodeById: id => byId[id],
    getWidgetDataManager: () => ({
      moveComponents(nodes, into, index) {
        for (const c of nodes) {
          into.kids.splice(into.kids.indexOf(c), 1);
          into.kids.splice(index, 0, c);
        }
      },
    }),
  };
  return { parent, appModel };
}

const moves = [];
const hows = ['FIRST', 'PREVIOUS', 'NEXT', 'LAST', 'OFFSET'];
for (let c = 0; c < 900; c++) {
  const n = int(2, 7);
  const stack = rand() < 0.3;
  const count = stack ? (rand() < 0.6 ? 1 : int(2, n)) : int(1, Math.min(n, rand() < 0.6 ? 1 : n));
  // The selection in the order it was made, which the action sorts by place.
  const order = [...Array(n).keys()];
  for (let k = n - 1; k > 0; k--) {
    const j = int(0, k);
    [order[k], order[j]] = [order[j], order[k]];
  }
  const selected = order.slice(0, count);
  const how = stack ? hows[int(0, 3)] : hows[int(0, 4)];
  const drop = int(0, n - 1);
  const { parent, appModel } = sceneOf(n, selected);
  const can = action._checkAbilityToReorder.call(action, appModel, stack);
  const enabled = { FIRST: can.canFirst, PREVIOUS: can.canPrevious, NEXT: can.canNext, LAST: can.canLast, OFFSET: can.canOffset }[how];
  let after = null;
  if (enabled) {
    action._appDesignerModel = { activeAppModel: appModel };
    action._performReorder(stack, how, how === 'OFFSET' ? { Offset: drop - selected[0], DropIndex: drop } : undefined);
    after = parent.kids.map(k => +k.id.slice(1));
  }
  moves.push({
    count: n, selected, stack, how, drop: how === 'OFFSET' ? drop : null,
    can: [can.canFirst, can.canPrevious, can.canNext, can.canLast, can.canOffset], after,
  });
}

// The buttons: a group of a content height with some of its children radio or toggle buttons at
// their locations, and a label or two among them, which do not count.
const buttons = [];
const codeNames = { makeCreateCodeNameGenerator: () => ({ generateCodeNameData: () => [{ PropertyValues: {} }] }) };
for (let c = 0; c < 300; c++) {
  const toggle = rand() < 0.5;
  const type = toggle ? 'matlab.ui.control.ToggleButton' : 'matlab.ui.control.RadioButton';
  const height = int(30, 400);
  const kids = [];
  const ys = [];
  for (let k = int(0, 5); k > 0; k--) {
    const label = rand() < 0.2;
    const y = int(-40, height);
    if (!label) ys.push(y);
    kids.push({
      getType: () => (label ? 'matlab.ui.control.Label' : type),
      getProperty: p => (p === 'Location' ? [int(1, 50), y] : undefined),
    });
  }
  const group = { ContentHeight: height, getChildren: () => kids };
  const made = toggle
    ? ButtonUtil.createToggleButtonComponentData(group, {}, codeNames)
    : ButtonUtil.createRadioButtonComponentData(group, {}, codeNames);
  const v = made.PropertyValues;
  buttons.push({ toggle, height, bottoms: ys, location: v.Location, size: v.Size, value: v.Value });
}

process.stdout.write(JSON.stringify({ moves, buttons }, null, 1) + '\n');
