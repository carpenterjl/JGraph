// What R2025b's App Designer does with callbacks for several components and in its Callbacks submenus, from its
// own code run unmodified - for the App Designer window plan's stage W7d, whose DesignCallbackRules transcribes it:
//   node callbacks.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignCallbacksOracle.json
//
// The table: each class's CallbackFcnData, from uicomponents_appdesigner_plugin/init/derived/ComponentDefaultsProduction
// loaded whole, and appdesigner/model/inspectableapp/InspectableAppNodeModel for the app's own row.
//
// What several have in common: appdesigner/util/CallbackUtil loaded whole.
//
// The menu: appdesigner_interface/model/_ComponentContextMenuInfo loaded whole, and ComponentMenuItemsProvider's
// _getMenuItemsInfo cut out as it is, each item clicked for what it asks of CommandController.
//
// An edit of several: appdesigner/command/MassEditCallbackCommand loaded whole. The commands it runs are stand-ins
// that record what they are asked and do it to the peer nodes and the callback list.
//
// A row of the Callbacks tab: CallbackSection's _buildCallbacksList, _shouldCallbackBeAddedToCallbackList,
// _handleCallbackNameEdited and _validateCodeName cut out as they are; CallbackMultiselectView's showContent as it is.
//
// The dialog: SharedCallbackDialog's _getCallbacksForSelectedComponent and CallbackFunctionNameEditField's _createEditField.
//
// The peer nodes are stand-ins. A scene that throws records the error instead of a result (`threw`).
'use strict';
const path = require('path');
const { load, stub, stubWhere, moduleText } = require(path.join(__dirname, '..', '..', 'w0', 'oracle', 'amd.js'));

class Stub {}
function clone(src) {
  if (!src || typeof src !== 'object' || typeof src === 'function') return src;
  if (Array.isArray(src)) return src.map(clone);
  const r = {};
  for (const k of Object.keys(src)) r[k] = clone(src[k]);
  return r;
}
stub('dojo/_base/lang', { mixin: (d, ...s) => Object.assign(d || {}, ...s), clone, hitch: (c, f, ...a) => (typeof f === 'string' ? c[f] : f).bind(c, ...a), isFunction: f => typeof f === 'function' });
stub('dojo/_base/declare', function (bases, props) {
  if (!Array.isArray(bases) && typeof bases !== 'function') { props = bases; bases = []; }
  const C = function (...a) { if (props && props.constructor !== Object.prototype.constructor) props.constructor.apply(this, a); };
  Object.assign(C.prototype, props || {});
  return C;
});
stub('dojo/string', { substitute(t, m) { return String(t).replace(/\$\{([^}]+)\}/g, (_, k) => m[k]); }, trim: s => s.trim() });
stub('dojo/query', () => ({ parents: () => ({ length: 0 }) }));
const published = [];
stub('dojo/topic', { publish: (...a) => published.push(a), subscribe: () => ({ remove() {} }) });
stub('appdesigner/application/AppDesignerServiceProvider', { getService: () => null, services: new Proxy({}, { get: (_, k) => k }) });
stub('settings/settingsService', {});
stub('appdesigner/util/GridUtil', { isGrid: () => false, isGridType: t => t === 'matlab.ui.container.GridLayout' });
stub('appdesigner/util/AppDetailsUtil', { getInspectableAppType: () => 'App', isInspectableApp: n => n.getType() === 'App' });
stub('appdesigner/util/FigureUtil', { getFigureType: () => 'matlab.ui.Figure' });
stub('appdesigner/view/ViewConstants', { EDITOR_ONLY_VIEW: 'code', DESIGN_ONLY_VIEW: 'design' });
stub('appdesigner/view/dialogs/callback/SharedCallbackDialog', class { constructor(app, peers) { published.push(['dialog', peers.map(p => p.getProperty('CodeName'))]); } });
let massEdits = [];
stub('appdesigner/command/CommandController', { performMassEditCallback: (app, peers, name, prop, options) => massEdits.push({ peers: peers.map(p => p.getProperty('CodeName')), name, prop, detachPrevious: !!(options && options.detachPrevious) }) });

// Each stand-in records what the real command is asked to do, and does it to the peer nodes and the list.
let steps = [];
const stepStub = (kind, run) => class {
  constructor(...a) { this.a = a; }
  execute() { steps.push(run(...this.a)); }
};
stub('appdesigner/command/_UndoableCommand', class { constructor(appModel) { this._appModel = appModel; } destroy() {} });
stub('appdesigner/command/AddCallbackCommand', stepStub('add', (app, peer, name, prop) => { app.callbacks.push({ name, type: 'c' }); peer.props[prop] = name; return ['add', peer.getProperty('CodeName'), '', name]; }));
stub('appdesigner/command/AttachCallbackCommand', stepStub('attach', (app, peer, name, prop) => { peer.props[prop] = name; return ['attach', peer.getProperty('CodeName'), '', name]; }));
stub('appdesigner/command/ReassignCallbackCommand', stepStub('reassign', (app, peer, from, to, prop) => { peer.props[prop] = to; return ['reassign', peer.getProperty('CodeName'), from, to]; }));
stub('appdesigner/command/DetachAppCallbackFunctionCommand', stepStub('detach', (app, peer, callback, prop) => { const was = peer.props[prop]; peer.props[prop] = ''; return ['detach', peer.getProperty('CodeName'), was, callback.name]; }));
stub('appdesigner/command/DeleteAppCallbackFunctionCommand', stepStub('delete', (app, callback) => ['delete', '', callback.name, '']));
stub('appdesigner/command/RenameAppCallbackFunctionCommand', stepStub('rename', (app, callback, field, to) => {
  const from = callback.name;
  callback.name = to;
  app.nodes.forEach(n => { Object.keys(n.props).forEach(k => { if (n.props[k] === from) n.props[k] = to; }); });
  return ['rename', '', from, to];
}));

// UI-only families the component models pull in, as ds5's oracle stubs them; the commands and modules above are their own.
const KEEP = /^appdesigner\/command\/MassEditCallbackCommand$|^appdesigner\/view\/contextmenu\/Criteria\/ConditionTypeEnum$/;
stubWhere(n => !KEEP.test(n) && /\/propertysheet\/|\/componentlibrary\/|^appdesigner\/view\/|^appdesigner\/command\/|^appdesigner\/flux\/|^MW\/|^mw-remote\/|^dojo\/(NodeList-traverse|Deferred)$|^appdesigner\/util\/CanvasUtil$|^appdesigner\/model\/codenamegenerator\//.test(n), () => Stub);
stubWhere(n => /^dojo\//.test(n) && !n.startsWith('dojo/i18n!'), () => Stub);
global.dojo = { mixin: (d, ...s) => Object.assign(d, ...s) };

const registry = load('uicomponents_appdesigner_plugin/init/derived/ComponentDefaultsProduction');
const AppNode = load('appdesigner/model/inspectableapp/InspectableAppNodeModel');
const CallbackUtil = load('appdesigner/util/CallbackUtil');
const UniqueNameGenerator = load('hmishared/util/UniqueNameGenerator');
const MassEdit = load('appdesigner/command/MassEditCallbackCommand');
const GroupUtil = load('appdesigner/util/GroupUtil');
const FunctionTypes = load('appdesigner/model/codegen/FunctionTypes');
const nlsSheet = load('dojo/i18n!appdesigner/resources/appdesigner/application_js/nls/PropertySheet');
const nlsApp = load('dojo/i18n!appdesigner/resources/appdesigner/application_js/nls/AppDesigner');

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

// A small repeatable generator.
let seed = 20261031;
const rnd = n => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return (seed >> 8) % n; };
const pick = a => a[rnd(a.length)];

const adapterOf = t => (t === 'App' ? AppNode : registry[t]);
const labelsOf = t => [].concat(...(adapterOf(t).CallbackFcnData || []).map(g => g[Object.keys(g)[0]].map(x => x.Label)));

// --- the table ---------------------------------------------------------------------------------------
const CLASSES = Object.keys(registry).filter(t => registry[t] && registry[t].CallbackFcnData);
const table = {};
for (const t of CLASSES.concat(['App'])) {
  table[t] = (adapterOf(t).CallbackFcnData || []).map(g => {
    const k = Object.keys(g)[0];
    return { key: k, items: g[k].map(x => JSON.stringify(x)) };
  });
}
const WITH = CLASSES.filter(t => labelsOf(t).length > 0);
const WITHOUT = CLASSES.filter(t => labelsOf(t).length === 0);
const MANY = WITH.filter(t => t !== 'matlab.ui.Figure');

// --- the peer nodes ----------------------------------------------------------------------------------
let ids = 0;
function node(type, name, props) {
  const n = {
    id: 'n' + (++ids), type, name, props: props || {}, group: null, ComponentAdapter: adapterOf(type),
    getId() { return this.id; },
    getType() { return this.type; },
    getName() { return this.name; },
    getProperty(k) {
      if (k === 'CodeName') return this.name;
      if (k === 'GroupId') return this.group && this.group.id;
      if (k === 'Members') return this.members.map(m => m.id);
      if (k === 'LabeledComponent') return this.labeled;
      if (k in this.props) return this.props[k];
      return labelsOf(this.type).includes(k) ? '' : undefined;
    },
    isGroup() { return this.type === 'Group'; },
    isGroupMember() { return !!this.group; },
    getController() { return {}; },
    getMemberPeerNodes() { return this.members; },
    getChildren() { return []; },
  };
  return n;
}

function appOf(nodes, callbacks, startup, groups) {
  const app = {
    nodes, callbacks, groups: groups || [], nameLengthMax: 63,
    getPeerNodeById(id) { return this.nodes.concat(this.groups).find(n => n.id === id); },
    hasGroups() { return this.groups.length > 0; },
    getWidgetDataManager() { return { getGroupManagerPeerNode: () => ({ getDescendant: id => app.groups.find(g => g.id === id) }) }; },
    getViewState() { return { getViewMode: () => 'code', getViewId: () => 'view' }; },
    getMetaDataModel() { return { getProperty: () => 'Standard' }; },
    getAppViewConfig() { return { getHelpTextForAppType: k => ({ capitalizedStartupFunctionName: 'Startup', startupFunctionName: 'startup', defaultLabel: 'app' }[k] || k) }; },
    getCodeModel() {
      const collection = {
        get length() { return callbacks.length; },
        where(q) {
          const list = callbacks.map(wrap);
          return typeof q === 'function' ? list.filter(q) : list.filter(c => c.name === q.Name);
        },
        all() { return callbacks.map(wrap); },
      };
      return {
        getStartupName: () => startup,
        getCallbackCollection: () => collection,
        getCodeNameTypeHashmap() {
          const h = {};
          nodes.forEach(n => { h[n.name] = 'AppComponentProperty'; });
          callbacks.forEach(c => { h[c.name] = c.type === 's' ? FunctionTypes.StartupCallback : FunctionTypes.ComponentCallback; });
          return h;
        },
      };
    },
  };
  return app;
}
const wrap = c => Object.assign(c, { getProperty: k => (k === 'Name' ? c.name : k === 'CallbackType' ? (c.type === 's' ? FunctionTypes.StartupCallback : FunctionTypes.ComponentCallback) : undefined), getName: () => c.name });

const describe = n => ({ t: n.type, n: n.name, b: n.props, g: n.group ? n.group.id : null });

// A selection: some classes, some of their properties bound to some of the callbacks.
function scene(types, callbacks) {
  const nodes = types.map((t, i) => {
    const props = {};
    for (const l of labelsOf(t)) {
      if (l !== 'StartupFcn' && callbacks.some(c => c.type === 'c') && rnd(3) === 0) props[l] = pick(callbacks.filter(c => c.type === 'c')).name;
    }
    return node(t, (t === 'App' ? 'app1' : t.split('.').pop()) + (i + 1), props);
  });
  return nodes;
}
function someCallbacks(startup) {
  const list = [];
  const n = rnd(4);
  for (let i = 0; i < n; i++) list.push({ name: pick(['alpha', 'beta', 'gamma', 'delta', 'omega']) + i, type: 'c' });
  if (startup) list.unshift({ name: startup, type: 's' });
  return list;
}
function typesFor() {
  switch (rnd(8)) {
    case 0: return ['matlab.ui.Figure'];
    case 1: return ['App'];
    case 2: return [pick(WITHOUT)];
    case 3: { const t = pick(MANY); return [t, t, t].slice(0, 1 + rnd(3)); }
    case 4: return [pick(MANY)];
    default: {
      const k = 2 + rnd(3), out = [];
      for (let i = 0; i < k; i++) out.push(rnd(6) === 0 ? pick(WITHOUT) : pick(MANY));
      return out;
    }
  }
}

// --- what several have in common -----------------------------------------------------------------------
const common = [];
for (let i = 0; i < 500; i++) {
  const k = 1 + rnd(4), types = [];
  for (let j = 0; j < k; j++) types.push(rnd(5) === 0 ? pick(CLASSES) : pick(MANY));
  const nodes = types.map((t, j) => node(t, 'c' + j));
  common.push({ types, common: CallbackUtil.getCommonCallbacks(nodes).map(x => x.Label) });
}

// --- the menu ------------------------------------------------------------------------------------------
const getMenuItemsInfo = method('appdesigner/view/contextmenu/ComponentMenuItemsProvider', '_getMenuItemsInfo', { p: load('dojo/_base/lang'), c: GroupUtil });
const wrapClick = method('appdesigner/view/contextmenu/ComponentMenuItemsProvider', '_wrapMenuItemOnClick', { p: load('dojo/_base/lang'), c: GroupUtil });
const shape = items => items.map(it => (it === Object(it) && it.text === undefined ? { separator: true } : Object.assign(
  { text: it.text, disabled: !!it.disabled, property: it.id && it.id.startsWith('adcontextmenu_editcallback_') ? it.id.slice('adcontextmenu_editcallback_'.length) : null },
  it.menu ? { menu: shape(it.menu) } : {})));

const menus = [];
for (let i = 0; i < 1200; i++) {
  const startup = rnd(2) ? 'startupFcn' : '';
  const callbacks = someCallbacks(startup);
  const nodes = scene(typesFor(), callbacks);
  const groups = [];
  // Some of them a labelled component or a plain group.
  if (nodes.length >= 2 && rnd(4) === 0) {
    const g = node('Group', 'g' + i);
    g.members = nodes.slice(0, 2);
    g.labeled = false;
    g.members.forEach(m => { m.group = g; });
    groups.push(g);
  }
  const app = appOf(nodes, callbacks, startup, groups);
  const selected = groups.length && rnd(2) ? groups.concat(nodes.slice(2)) : nodes;
  const provider = {
    _MenuItems: [], _destroyMenuItem() {}, _AppModel: app, _MenuOpenEventData: { currentTarget: {} },
    _getCurrentSelection: () => selected,
    _wrapMenuItemOnClick(o) { return wrapClick.call(this, o); },
  };
  let record;
  try {
    const info = getMenuItemsInfo.call(provider);
    record = { items: shape(info) };

    // Each of them clicked.
    record.clicks = [];
    const walk = list => list.forEach(it => {
      if (it.menu) { walk(it.menu); return; }
      if (!it.onClick || it.text === undefined || it.id === 'adcontextmenu_helpLink') return;
      massEdits = []; published.length = 0;
      it.onClick({});
      record.clicks.push({ text: it.text, edit: massEdits[0] || null, goTo: published.filter(p => p[0] === 'gotoAppFunction').map(p => p[2])[0] || null, dialog: published.filter(p => p[0] === 'dialog').map(p => p[1])[0] || null });
    });
    walk(info);
  } catch (e) {
    record = { threw: String(e.message).split(' | ')[0] };
  }
  menus.push(Object.assign({ startup, callbacks: callbacks.map(c => c.name), components: nodes.map(describe), groups: groups.map(g => ({ id: g.id, members: g.members.map(m => m.name) })), selectedGroups: selected === nodes ? 0 : 1 }, record));
}

// --- an edit of several --------------------------------------------------------------------------------
const edits = [];
for (let i = 0; i < 1000; i++) {
  const callbacks = someCallbacks('startupFcn');
  const type = pick(MANY);
  const k = 1 + rnd(4);
  const nodes = scene(new Array(k).fill(type), callbacks);
  const property = pick(labelsOf(type));
  // Some of them in common on one callback, so that a rename reaches the others.
  if (callbacks.length > 1 && rnd(3) === 0) nodes.forEach(n => { n.props[property] = callbacks[1].name; });
  const names = callbacks.filter(c => c.type === 'c').map(c => c.name);
  const name = pick(['', 'madeNew'].concat(names));
  const detachPrevious = rnd(2) === 0;
  const before = nodes.map(n => clone(describe(n)));
  const namesBefore = callbacks.filter(c => c.type === 'c').map(c => c.name);
  const app = appOf(nodes, callbacks, 'startupFcn');
  steps = [];
  const command = new MassEdit(app, nodes.slice(), name, property, { detachPrevious });
  command.execute();
  edits.push({ property, name, detachPrevious, callbacks: namesBefore, components: before, steps, after: nodes.map(n => n.props[property] || '') });
}

// --- a row of the Callbacks tab ---------------------------------------------------------------------
const closure = { d: load('dojo/string'), a: nlsSheet, r: nlsApp, A: CallbackUtil, C: FunctionTypes, e: UniqueNameGenerator, g: load('appdesigner/command/CommandController'), n: { _showDialogAndRevertControl: m => massEdits.push({ refused: m }) } };
const section = {
  list: method('appdesigner/view/propertyview/callback/CallbackSection', '_buildCallbacksList', closure),
  should: method('appdesigner/view/propertyview/callback/CallbackSection', '_shouldCallbackBeAddedToCallbackList', closure),
  edited: method('appdesigner/view/propertyview/callback/CallbackSection', '_handleCallbackNameEdited', closure),
  validate: method('appdesigner/view/propertyview/callback/CallbackSection', '_validateCodeName', closure),
};
const showContent = method('appdesigner/view/propertyview/callback/CallbackMultiselectView', 'showContent', { c: GroupUtil, p: CallbackUtil });

const rows = [];
for (let i = 0; i < 1200; i++) {
  const startup = rnd(2) ? 'startupFcn' : '';
  const callbacks = someCallbacks(startup);
  const types = rnd(6) === 0 ? ['App'] : rnd(5) === 0 ? ['matlab.ui.Figure'] : (() => { const t = pick(MANY); return new Array(1 + rnd(3)).fill(t); })();
  const nodes = scene(types, callbacks);
  const anchor = nodes[nodes.length - 1];
  const property = pick(labelsOf(anchor.type));
  const app = appOf(nodes, callbacks, startup);
  const current = anchor.getProperty(property) || (property === 'StartupFcn' ? startup : '');
  if (property === 'StartupFcn' && startup) anchor.props[property] = startup;
  const names = callbacks.map(c => c.name);
  const value = pick(['', 'AD_Use_Default', 'madeNew', '1bad', 'for', anchor.name, 'a b', 'x'.repeat(70)].concat(names));
  const self = {
    defaultCallbackEntry: 'AD_Use_Default', detachCallbackEntry: '', _appModel: app, _selectedWidget: anchor, _peerNodesToEdit: nodes.slice(),
    _removePropertySetListener() {},
    _shouldCallbackBeAddedToCallbackList: section.should,
    _validateCodeName: section.validate,
  };
  const choices = section.list.call(self, app.getCodeModel().getCallbackCollection(), anchor.getProperty(property) || '', property);
  massEdits = [];
  let threw = null;
  try {
    section.edited.call(self, { newValue: value, property: 'selectedItem', oldValue: current }, { callbackPropertyName: property, property: 'text', valueErrorDialogTitle: 'x' }, 't');
  } catch (e) { threw = String(e.message).split(' | ')[0]; }
  rows.push({ startup, callbacks: names, components: nodes.map(describe), property, value, choices, result: massEdits[0] || null, threw });
}

// What a selection of several shows.
const multi = [];
for (let i = 0; i < 400; i++) {
  const callbacks = someCallbacks('');
  const types = typesFor().filter(t => t !== 'App');
  if (types.length === 0) types.push(pick(MANY));
  const nodes = scene(types, callbacks);
  const groups = [];
  if (rnd(3) === 0) {
    const l = node('matlab.ui.control.Label', 'Lab' + i);
    const g = node('Group', 'g' + i);
    g.members = [l, nodes[0]];
    g.labeled = true;
    l.group = g; nodes[0].group = g;
    groups.push(g);
    nodes.unshift(l);
  }
  const app = appOf(nodes, callbacks, '', groups);
  const self = {
    _widgetDataManager: app.getWidgetDataManager(), _targetDomNode: { appendChild() { self.message = true; } }, _messageNode: {},
    _buildSection(s, e) { self.section = { components: s.map(c => c.name), common: e.map(x => x.Label) }; },
  };
  showContent.call(self, { getComponents: () => nodes });
  multi.push({ components: nodes.map(describe), labeled: groups.map(g => g.members.map(m => m.name)), message: !!self.message, section: self.section || null });
}

// --- the dialog ------------------------------------------------------------------------------------------
const dialogProps = method('appdesigner/view/dialogs/callback/SharedCallbackDialog', '_getCallbacksForSelectedComponent', { n: CallbackUtil });
const dialog = [];
for (let i = 0; i < 300; i++) {
  const callbacks = someCallbacks(rnd(2) ? 'startupFcn' : '');
  const t = pick(MANY);
  const [anchor] = scene([t], callbacks);
  dialog.push({ type: t, bound: anchor.props, properties: dialogProps.call({}, anchor), callbacks: callbacks.map(c => c.name) });
}

const words = {
  multiselect: nlsApp.multiselectCallbacksMsg,
  noCallbacks: nlsApp.noCallbacksMsg,
  placeholder: nlsApp.callbackPlaceHolderText,
  invalidTitle: nlsApp.errorDialogTitle,
  add: nlsSheet.callbackAddDropdownItem,
  detach: nlsSheet.callbackDetachDropdownItem,
  goTo: nlsSheet.goToCallbackTooltip,
};
const out = { table, words, common, menus, edits, rows, multi, dialog };
process.stdout.write(JSON.stringify(out));
