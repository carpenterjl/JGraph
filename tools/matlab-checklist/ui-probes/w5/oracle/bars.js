// What R2025b's App Designer does with a figure's menus, toolbars and tools on its canvas, from its own
// code run unmodified - for the App Designer window plan's stage W5d, whose DesignBars transcribes it:
//   node bars.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignBarsOracle.json
//
// Each scene is a seeded figure - top-level menus with items under them, toolbars with tools, a few
// components and context menus with items - with one component or several siblings selected. Over it
// these run as they stand, cut out of the bundle and given their module's own names for their imports,
// over MenuUtil and ToolbarUtil loaded whole:
//   MenuAction._performAddMenu, _performAddMenuAtEnd and _updateAction;
//   ToolAction._performAddTool and ToolbarAction._performAddTool;
//   the keys of MenuHierarchyNavigateReorderPlugin with its MenuBar and ContextMenu subclasses,
//   ToolKeyboardPlugin and ToolbarKeyboardPlugin, and MenuBarInPlaceEditPlugin's arrows;
//   ReorderAction._performReorder and _checkAbilityToReorder over ReorderCommand loaded whole;
//   a drop by MenuHierarchyDragModel, ToolbarDragModel and ToolDragModel _handleCommitDragHoveredIndexChange.
// AddComponentCommand and MoveCommand are stand-ins recording what they were given.
'use strict';
const path = require('path');
const { load, stub, moduleText } = require(path.join(__dirname, '..', '..', 'w0', 'oracle', 'amd.js'));

const FIGURE = 'matlab.ui.Figure';
const FigureUtil = {
  isFigure: n => !!n && n.getType() === FIGURE,
  getFigureType: () => FIGURE,
  getFigurePeerNode: app => app.figure,
};
stub('dojo/_base/lang', { clone: v => (v === undefined ? v : JSON.parse(JSON.stringify(v))), mixin: Object.assign });
stub('appdesigner/command/_UndoableCommand', class { constructor(appModel) { this._appModel = appModel; } destroy() {} });
stub('appdesigner/util/FigureUtil', FigureUtil);

const MenuUtil = load('appdesigner/util/MenuUtil');
const ToolbarUtil = load('appdesigner/util/ToolbarUtil');
const ReorderCommand = load('appdesigner/command/ReorderCommand');
const AxesUtil = { isAxes: () => false, getFirstChildAxesIndex: () => 0, numberOfChildAxesInPeerNode: () => 0 };

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

let seed = 20261030;
function rand() { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; }
const int = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));

// --- the scene --------------------------------------------------------------------------------------
function node(id, type, parent) {
  const n = {
    id, type, kids: [], parent,
    getId: () => n.id,
    getType: () => n.type,
    getParent: () => n.parent,
    getChildren: () => n.kids.slice(),
    getChild: k => n.kids[k],
    getChildIndex: c => n.kids.indexOf(c),
    getNumberOfChildren: () => n.kids.length,
    ComponentAdapter: { DesignGestures: {
      CanReorder: () => [MenuUtil.getMenuType(), ToolbarUtil.TOOLBAR_MCOS_NAME, ToolbarUtil.PUSHTOOL_MCOS_NAME, ToolbarUtil.TOGGLETOOL_MCOS_NAME].includes(n.type),
      CanStack: () => false,
    } },
  };
  if (parent) parent.kids.push(n);
  return n;
}

function sceneOf() {
  let serial = 0;
  const fresh = p => `${p}${++serial}`;
  const figure = node('Figure', FIGURE, null);
  const tree = (parent, depth) => {
    for (let k = int(depth === 0 ? 0 : 0, depth === 0 ? 3 : 3); k > 0; k--) {
      const item = node(fresh('M'), MenuUtil.getMenuType(), parent);
      if (depth < 2 && rand() < 0.5) tree(item, depth + 1);
    }
  };
  tree(figure, 0);
  for (let k = int(0, 3); k > 0; k--) {
    const bar = node(fresh('B'), ToolbarUtil.TOOLBAR_MCOS_NAME, figure);
    for (let t = int(0, 4); t > 0; t--) node(fresh('T'), rand() < 0.5 ? ToolbarUtil.PUSHTOOL_MCOS_NAME : ToolbarUtil.TOGGLETOOL_MCOS_NAME, bar);
  }
  for (let k = int(0, 2); k > 0; k--) node(fresh('C'), 'matlab.ui.control.Button', figure);
  for (let k = int(0, 2); k > 0; k--) {
    const menu = node(fresh('X'), MenuUtil.getContextMenuType(), figure);
    tree(menu, 1);
  }
  return figure;
}

function walk(n, out = []) { out.push(n); n.kids.forEach(k => walk(k, out)); return out; }
function describe(n) { return { id: n.id, type: n.type, kids: n.kids.map(describe) }; }

function appOf(figure, selection) {
  const byId = {};
  walk(figure).forEach(n => { byId[n.id] = n; });
  const state = { selection, added: null, moved: null };
  const tools = {
    getTopLevelMenus: () => figure.kids.filter(c => MenuUtil.isMenuBarMenu(c)),
    hasTopLevelMenus: () => figure.kids.some(c => MenuUtil.isMenuBarMenu(c)),
    getContextMenus: () => figure.kids.filter(c => MenuUtil.isContextMenu(c)),
    getToolbars: () => figure.kids.filter(c => ToolbarUtil.isToolbar(c)),
  };
  const selectionManager = {
    getComponentSelection: () => state.selection.slice(),
    getComponentSelectionCount: () => state.selection.length,
    setComponentSelection: s => { state.selection = Array.isArray(s) ? s.slice() : [s]; state.selected = state.selection.map(x => x.id); },
  };
  const app = {
    figure, state,
    getSelectionModelManager: () => selectionManager,
    getToolsModel: () => tools,
    getComponentInfos: () => ({}),
    getUndoRedoManager: () => ({ addCommand() {} }),
    getPeerNodeById: id => byId[id],
    getViewState: () => ({ isDesignerMode: () => true, isInPlaceEditingCanvasMode: () => false }),
    getWidgetDataManager: () => ({
      moveComponents(nodes, into, index) {
        for (const c of nodes) { into.kids.splice(into.kids.indexOf(c), 1); into.kids.splice(index, 0, c); }
      },
    }),
  };
  return app;
}

// --- the actions --------------------------------------------------------------------------------------
function AddCommand(data) { this.data = data; }
AddCommand.prototype.execute = function () {
  this.data.AppModel.state.added = { parent: this.data.ParentPeerNode.id, index: this.data.IndexToInsertAt === undefined ? null : this.data.IndexToInsertAt };
};

const MA = 'appdesigner/action/MenuAction';
const maClosure = { E: null, p: null, c: null, d: null, o: AddCommand, s: FigureUtil, i: MenuUtil };
const performAddMenu = method(MA, '_performAddMenu', maClosure);
const performAddMenuAtEnd = method(MA, '_performAddMenuAtEnd', maClosure);
const updateMenuAction = method(MA, '_updateAction', maClosure);

const addTool = method('appdesigner/action/ToolAction', '_performAddTool', { E: null, p: null, c: null, d: AddCommand, o: ToolbarUtil });
const addToolAtEnd = method('appdesigner/action/ToolbarAction', '_performAddTool', { E: null, p: null, c: null, d: AddCommand, o: ToolbarUtil });

// The keys: each subclass's methods over its base's, with the actions they run recorded.
const NAV = 'appdesigner/view/designarea/plugins/menuhierarchy/MenuHierarchyNavigateReorderPlugin';
const ActionIds = new Proxy({}, { get: (_, k) => String(k) });
const navClosure = { E: ActionIds, p: null, c: null };
const navNames = ['_addItemRight', '_addItemRightEnd', '_addItemBelow', '_addItem', '_navigateLeft', '_navigateUp', '_navigatePrevious', '_navigateRight', '_navigateDown', '_navigateNext', '_shiftOnlyEnter', '_noCommandEnter', '_escape', '_noCommandEnd', '_noCommandHome', '_shiftOnlyEnterOnTopLevelItem', '_shiftOnlyEnterOnNonLeafItem', '_shiftOnlyEnterOnLeafItem', '_noCommandEnterOnTopLevelItem', '_noCommandEnterOnNonLeafItem', '_noCommandEnterOnLeafItem'];
const navBase = {};
navNames.forEach(n => { navBase[n] = method(NAV, n, navClosure); });
const MB = 'appdesigner/view/designarea/plugins/menubar/MenuBarNavigateReorderPlugin';
const mbClosure = { E: MenuUtil, p: null };
const menuBarNav = { ...navBase };
['_isTopLevelItem', '_isLeafItem', '_isNonLeafItem', '_getSiblingItems'].forEach(n => { menuBarNav[n] = method(MB, n, mbClosure); });
const CM = 'appdesigner/view/designarea/plugins/uicontextmenu/ContextMenuNavigateReorderPlugin';
const cmClosure = { E: MenuUtil, p: {}, c: {}, d: null };
const contextNav = { ...navBase };
['_isTopLevelItem', '_isLeafItem', '_isNonLeafItem', '_escape', '_shiftOnlyEnterOnTopLevelItem', '_noCommandEnterOnTopLevelItem', '_getSiblingItems'].forEach(n => { contextNav[n] = method(CM, n, cmClosure); });

const KB = 'appdesigner/view/designarea/plugins/toolbarhierarchy/ToolbarHierarchyKeyboardPlugin';
const kbClosure = { E: null, p: ActionIds, c: null, d: null };
const kbBase = { _escape: method(KB, '_escape', kbClosure), setSelection: method(KB, 'setSelection', kbClosure), getSelection: method(KB, 'getSelection', kbClosure) };
const toolKeys = { ...kbBase };
const TK = 'appdesigner/view/designarea/plugins/tool/ToolKeyboardPlugin';
const tkClosure = { E: ToolbarUtil, p: ActionIds, c: null };
['_navigateLeft', '_navigateUp', '_navigateRight', '_navigateDown', '_navigateLast', '_navigateFirst'].forEach(n => { toolKeys[n] = method(TK, n, tkClosure); });
const barKeys = { ...kbBase };
const BK = 'appdesigner/view/designarea/plugins/toolbar/ToolbarKeyboardPlugin';
['_navigateLeft', '_navigateUp', '_navigateRight', '_navigateDown', '_navigateLast', '_navigateFirst'].forEach(n => { barKeys[n] = method(BK, n, tkClosure); });

const IP = 'appdesigner/view/designarea/plugins/menubar/MenuBarInPlaceEditPlugin';
const ipClosure = { E: ActionIds, p: null, c: MenuUtil };
const editorDown = method(IP, '_downArrowNavigation', ipClosure);
const editorUp = method(IP, '_upArrowNavigation', ipClosure);

const RA = 'appdesigner/action/ReorderAction';
const raClosure = { i: MenuUtil, o: AxesUtil, e: ToolbarUtil, d: ReorderCommand, s: { supports: (n, g) => n.ComponentAdapter.DesignGestures[g](n) } };
const reorderAction = {
  _IS_STACK_ORDER: true,
  _performReorder: method(RA, '_performReorder', raClosure),
  _filterAndOrderByIndex: method(RA, '_filterAndOrderByIndex', raClosure),
  _checkAbilityToReorder: method(RA, '_checkAbilityToReorder', raClosure),
  _getReorderableComponents: method(RA, '_getReorderableComponents', raClosure),
  _isReStackAbleComponent: method(RA, '_isReStackAbleComponent', raClosure),
};

// The drops: what each commits, recorded; the reorder it asks for run by the action.
function sign(e) { return (e > 0) - (e < 0) || +e; }
const provider = run => ({ getService: () => ({ executeAction: run }), services: { ActionManager: 'ActionManager' } });

function MoveCommandStandIn(app, nodes, from, to, _unused, index) { this.a = { app, nodes, from, to, index }; }
MoveCommandStandIn.prototype.execute = function () { this.a.app.state.moved = { to: this.a.to.id, index: this.a.index }; };

function dropOf(module, closure) { return method(module, '_handleCommitDragHoveredIndexChange', closure); }

// --- the run --------------------------------------------------------------------------------------------
const scenes = [];
for (let c = 0; c < 500; c++) {
  const figure = sceneOf();
  const all = walk(figure);
  const candidates = all.filter(n => n.type !== 'matlab.ui.control.Button');
  const pick = candidates[int(0, candidates.length - 1)];
  let selection = [pick];
  if (pick.parent && rand() < 0.25) {
    const same = pick.parent.kids.filter(k => k.type === pick.type && k !== pick);
    selection = [pick, ...same.filter(() => rand() < 0.5)];
  }
  const ids = selection.map(s => s.id);
  const scene = { figure: describe(figure), selected: ids };
  const fresh = () => {
    const app = appOf(figure, selection.slice());
    return app;
  };
  const isMenuLike = MenuUtil.isMenu(pick) || MenuUtil.isContextMenu(pick);
  const isFigure = pick === figure;

  // The enabled states.
  {
    const app = fresh();
    const enabled = {};
    const self = {
      _actionDefinition: [{ id: 'right' }, { id: 'below' }, { id: 'end' }],
      _actionDataService: { updateAction: (id, v) => { enabled[id] = v.enabled; } },
    };
    updateMenuAction.call(self, app);
    scene.ability = [enabled.right, enabled.below, enabled.end];
  }

  // Add Menu to Right and Below, as their keys reach them.
  if (selection.length === 1 && isMenuLike) {
    for (const right of [true, false]) {
      const app = fresh();
      performAddMenu.call({ _appDesignerModel: { activeAppModel: app } }, right);
      scene[right ? 'addRight' : 'addBelow'] = app.state.added;
    }
  }

  {
    const target = all[int(0, all.length - 1)];
    if (isFigure || MenuUtil.isMenu(target) || MenuUtil.isContextMenu(target) || target === figure) {
      const app = fresh();
      const tops = app.getToolsModel().getTopLevelMenus();
      if (target !== figure || tops.length > 0) {
        performAddMenuAtEnd.call({ _appDesignerModel: { activeAppModel: app } }, { PeerNodeId: target.id });
        scene.atEndTarget = target.id;
        scene.addAtEnd = app.state.added;
      }
    }
  }

  // Add Tool.
  if (selection.length === 1 && ToolbarUtil.isToolbarOrTool(pick)) {
    const app = fresh();
    addTool.call({ _appDesignerModel: { activeAppModel: app } }, 'x');
    scene.addTool = app.state.added;
    const end = fresh();
    addToolAtEnd.call({ _appDesignerModel: { activeAppModel: end } }, 'x', { parent: null });
    scene.addToolAtEnd = end.state.added;
  }

  // The keys.
  const keysOf = {};
  if (isMenuLike) {
    const base = MenuUtil.isContextMenuItem(pick) || MenuUtil.isContextMenu(pick) ? contextNav : menuBarNav;
    const run = (name) => {
      const app = fresh();
      let action = null;
      const self = { ...base, _appModel: app, _actionManager: { executeAction: (id, data) => { action = data ? `${id}:${data.PeerNodeId}` : id; } }, _addItem(o, s) { action = s ? `${o}:${s.PeerNodeId}` : o; } };
      self[name]();
      return { selected: app.state.selected ? app.state.selected : null, action };
    };
    keysOf.left = run('_navigateLeft');
    keysOf.up = run('_navigateUp');
    keysOf.right = run('_navigateRight');
    keysOf.down = run('_navigateDown');
    keysOf.home = run('_noCommandHome');
    keysOf.end = run('_noCommandEnd');
    if (selection.length === 1) {
      keysOf.enter = run('_noCommandEnter');
      keysOf.shiftEnter = run('_shiftOnlyEnter');
      if (MenuUtil.isMenuBarMenu(pick)) {
        const go = (fn) => {
          const app = fresh();
          let action = null;
          fn.call({ _appModel: app, _actionManager: { executeAction: (id, data) => { action = data ? `${id}:${data.PeerNodeId}` : id; } } }, pick);
          return { selected: app.state.selected ? app.state.selected : null, action };
        };
        keysOf.editorDown = go(editorDown);
        keysOf.editorUp = go(editorUp);
      }
    }
  } else if (ToolbarUtil.isTool(pick) && pick.parent.kids.length > 0) {
    const safe = (fn) => { try { return fn(); } catch (e) { return { threw: true }; } };
    const run = (name) => safe(() => {
      const app = fresh();
      const self = { ...toolKeys, _appModel: app };
      self[name]();
      return { selected: app.state.selected ? app.state.selected : null };
    });
    Object.assign(keysOf, { left: run('_navigateLeft'), right: run('_navigateRight'), up: run('_navigateUp'), down: run('_navigateDown'), home: run('_navigateFirst'), end: run('_navigateLast') });
  } else if (ToolbarUtil.isToolbar(pick)) {
    const safe = (fn) => { try { return fn(); } catch (e) { return { threw: true }; } };
    const run = (name) => safe(() => {
      const app = fresh();
      const self = { ...barKeys, _appModel: app };
      self[name]();
      return { selected: app.state.selected ? app.state.selected : null };
    });
    Object.assign(keysOf, { left: run('_navigateLeft'), right: run('_navigateRight'), up: run('_navigateUp'), down: run('_navigateDown'), home: run('_navigateFirst'), end: run('_navigateLast') });
  }
  scene.keys = keysOf;

  // Reorder: Move's four, with what each makes, and a drop.
  const canReorder = selection.every(s => s.ComponentAdapter.DesignGestures.CanReorder());
  if (canReorder && pick.parent) {
    const parent = pick.parent;
    const kids = parent.kids.slice();
    const app = fresh();
    const can = reorderAction._checkAbilityToReorder.call(reorderAction, app, false);
    scene.can = [can.canFirst, can.canPrevious, can.canNext, can.canLast, can.canOffset];
    scene.moves = {};
    for (const how of ['FIRST', 'PREVIOUS', 'NEXT', 'LAST']) {
      const enabled = { FIRST: can.canFirst, PREVIOUS: can.canPrevious, NEXT: can.canNext, LAST: can.canLast }[how];
      if (enabled) {
        parent.kids = kids.slice();
        const moving = fresh();
        reorderAction._performReorder.call({ ...reorderAction, _appDesignerModel: { activeAppModel: moving } }, false, how);
        scene.moves[how] = parent.kids.map(k => k.id);
      }
    }
    parent.kids = kids.slice();

    // A drop along its siblings: hovered at some sibling, at its end or not.
    if (MenuUtil.isMenu(pick) || ToolbarUtil.isToolbar(pick)) {
      const among = MenuUtil.isMenu(pick) ? parent.kids.filter(k => MenuUtil.isMenu(k)) : parent.kids.filter(k => ToolbarUtil.isToolbar(k));
      const hovered = int(0, among.length - 1);
      const atEnd = hovered === among.length - 1 && rand() < 0.5;
      const dropApp = fresh();
      let asked = null;
      const svc = provider((id, data) => { asked = data; });
      if (MenuUtil.isMenu(pick)) {
        dropOf('appdesigner/view/canvas/mousegestureservices/menuhierarchydrag/model/MenuHierarchyDragModel', { E: null, p: ActionIds, c: svc, d: null, o: null, s: null, i: sign })
          .call({ _selectionModelManager: dropApp.getSelectionModelManager() }, atEnd, hovered);
      } else {
        dropOf('appdesigner/view/canvas/mousegestureservices/toolbarhierarchydrag/model/ToolbarDragModel', { E: null, p: null, c: svc, d: ActionIds, o: MenuUtil })
          .call({ getSelections: () => selection.slice() }, null, atEnd, { v: hovered, h: -1 });
      }
      scene.drop = { hovered, atEnd, asked: asked ? { drop: asked.DropIndex, direction: asked.Direction } : null };
      if (asked) {
        const moving = fresh();
        reorderAction._performReorder.call({ ...reorderAction, _appDesignerModel: { activeAppModel: moving } }, false, 'OFFSET', asked);
        scene.drop.after = parent.kids.map(k => k.id);
        parent.kids = kids.slice();
      }
    } else if (ToolbarUtil.isTool(pick)) {
      const bars = figure.kids.filter(k => ToolbarUtil.isToolbar(k));
      const v = int(0, bars.length - 1);
      const toolsThere = bars[v].kids.length;
      const h = toolsThere === 0 ? -1 : int(0, toolsThere - 1);
      const atToolEnd = toolsThere === 0 || (h === toolsThere - 1 && rand() < 0.5);
      const dropApp = fresh();
      let asked = null;
      const svc = provider((id, data) => { asked = data; });
      dropOf('appdesigner/view/canvas/mousegestureservices/toolbarhierarchydrag/model/ToolDragModel', { E: null, p: null, c: ActionIds, d: svc, o: MoveCommandStandIn, s: MenuUtil, i: ToolbarUtil })
        .call({ getSelections: () => selection.slice(), _appModel: dropApp }, atToolEnd, null, { v, h });
      scene.toolDrop = { bar: v, tool: h, atToolEnd, moved: dropApp.state.moved, asked: asked ? { drop: asked.DropIndex, direction: asked.Direction === undefined || Number.isNaN(asked.Direction) ? null : asked.Direction } : null };
    }
  }

  scenes.push(scene);
}

process.stdout.write(JSON.stringify({ scenes }, null, 0) + '\n');
