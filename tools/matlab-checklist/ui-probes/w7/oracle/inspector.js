// What R2025b's App Designer shows in its inspector for a selection, from its own code run unmodified - for
// the App Designer window plan's stage W7b, whose DesignInspectorSheet transcribes it:
//   node inspector.js sheet > ../w7b_inspector_sheet.json                                     (embedded in JGraph.Scripting)
//   node inspector.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignInspectorOracle.json
//
// The sheets: ../w7b_inspector_raw.json, the JSON R2025b's InspectorFactory publishes to the client
// (ui-probes/w7/w7b_inspector.m), with PropertyInspectorViewFactory's aliases added by its own methods.
//
// A selection's view: _PropertyViewFactory.getView, PropertyInspectorViewFactory's strategy, PropertyViewController's
// _decorateSelection loaded whole, the strategies loaded whole, and PropertyInspectorView._createInspectorObject with
// what it calls, cut out of the bundle as they are.
//
// What it shows: InspectorPropertySheet's _updateObjectProperties, _intersectObjectProperties, _combineValues,
// _createPropertySheetView, _renderPropertyItem, _getGroupPropertyName, _comparisonFcnForSort and _handleSearch, cut out
// as they are, over stand-ins for the renderers and the page, in the group view and in the alphabetical view.
//
// Its words: PropertiesPanelController._updateContentPaneTitle, InspectorClientManager._getIDForCache, cut out as they are.
//
// The renames an edit brings: ModifyCodeNameGenerator and AbstractCodeNameGenerator loaded whole.
//
// The peer nodes are stand-ins, each holding the properties its class's sheet lists. A scene that throws records
// the error instead of a result (`threw`).
'use strict';
const fs = require('fs');
const path = require('path');
const { load, stub, stubWhere, moduleText } = require(path.join(__dirname, '..', '..', 'w0', 'oracle', 'amd.js'));

const declare = (bases, proto) => {
  const list = Array.isArray(bases) ? bases : bases ? [bases] : [];
  const K = class { };
  for (const b of list) if (b && b.prototype) for (const k of Object.getOwnPropertyNames(b.prototype)) if (k !== 'constructor') K.prototype[k] = b.prototype[k];
  Object.assign(K.prototype, proto);
  return K;
};
stub('dojo/_base/declare', declare);
stub('dojo/_base/lang', { clone: v => (v === undefined ? v : JSON.parse(JSON.stringify(v))), mixin: Object.assign, hitch: (o, f) => o[f].bind(o) });
stub('dojo/on', () => ({ remove() {} }));
stub('dijit/Destroyable', class { own() {} });
stub('dijit/registry', {});
stub('appdesigner/view/propertyview/base/PropertyViewContentPane', class { });
stub('appdesigner/view/propertyview/base/EmptyPropertyView', class { });
stub('appdesigner/view/keyboard/PropertySheetKeyboardHandler', class { });
stub('inspector_client/widget/InspectorPropertySet', class { });
stub('appdesigner/util/PlatformUtil', { isApple: () => false });
stub('appdesigner/view/canvas/layouttemplate/LayoutTemplateConstants', {});
stub('appdesigner/flux/intent/AppIntentTypes', new Proxy({}, { get: (_, k) => String(k) }));
stub('appdesigner/util/AppDetailsUtil', { isInspectableApp: n => n.type === 'App', getInspectableAppType: () => 'App' });
stubWhere(n => n.startsWith('dojo/text!'), () => '<div></div>');

const GroupUtil = load('appdesigner/util/GroupUtil');
const MenuUtil = load('appdesigner/util/MenuUtil');
const Controller = load('appdesigner/view/propertyview/base/PropertyViewController');
const NonLabeled = load('appdesigner/view/propertyview/inspector/NonLabeledComponentStrategy');
const MenuStrategy = load('appdesigner/view/propertyview/inspector/MenuStrategy');
const UserStrategy = load('appdesigner/view/propertyview/inspector/UserComponentStrategy');
const LabeledDesign = load('appdesigner/view/propertyview/inspector/LabeledComponentInDesignViewStrategy');
const Constants = load('inspector_client/factory/InspectorConstants');
const dialogsAppDesigner = load('appdesigner/resources/appdesigner/application_js/nls/AppDesigner').root;
const library = load('uicomponents_appdesigner_plugin/resources/uicomponents/appdesigner_plugin_js/nls/ComponentLibrary').root;
const substitute = (t, a) => t.replace(/\$\{(\w+)\}/g, (_, k) => a[k]);
const inspectorNls = load('inspector_client/l10n/nls/app').root;

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

// --- the sheets --------------------------------------------------------------------------------------
const raw = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'w7b_inspector_raw.json'), 'utf8'));
const proxyMap = { AppDesigner: {} };
for (const c of raw.classes) proxyMap.AppDesigner[c.className] = JSON.parse(c.defaults.replace(/\\"/g, ''));
const factoryModule = 'appdesigner/view/propertyview/inspector/PropertyInspectorViewFactory';
const factoryClosure = {
  M: { getDefaultsForDataType: t => proxyMap.AppDesigner[t] }, R: 'AppDesigner',
  T: { isInspectableApp: n => n.type === 'App' }, I: MenuUtil, l: UserStrategy, n: MenuStrategy, e: class { }, i: NonLabeled,
};
const factory = {};
for (const name of ['_addAxesAliasedProperties', '_addFigureAliasedProperties', '_getTooltipForProperty', '_getStrategyForNonLabeledComponentSelection']) {
  factory[name] = method(factoryModule, name, factoryClosure);
}
factory._addAxesAliasedProperties();
factory._addFigureAliasedProperties();

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
// A word out of a model's own text: a string as it stands, or the key of the nls module the model imports under that name.
function modelWord(text, re) {
  const m = re.exec(text);
  if (!m) return null;
  if (m[1].startsWith('"')) return JSON.parse(m[1]);
  const [v, key] = m[1].split('.');
  const mods = [...text.slice(0, text.indexOf('t.dj.d(m,')).matchAll(/absMids\["([^"]+)"\]/g)].map(x => x[1]);
  const params = /t\.dj\.d\(m,function\(([\w,]*)\)/.exec(text)[1].split(',');
  const nls = mods[params.indexOf(v)];
  return load(nls.replace('dojo/i18n!', '')).root[key];
}
const WORDS = {};
for (const [type, model] of Object.entries(MODELS)) {
  const text = moduleText('uicomponents_appdesigner_plugin/model/' + model);
  WORDS[type] = { name: modelWord(text, /[,{]Name:("[^"]*"|\w+\.\w+)/), text: modelWord(text, /DefaultText:("[^"]*"|\w+\.\w+)/) };
}
const TYPES = Object.keys(MODELS);

if (process.argv[2] === 'sheet') {
  const classes = TYPES.map(type => ({
    type,
    tab: WORDS[type].name,
    properties: Object.fromEntries(proxyMap.AppDesigner[type].propertySheet.properties.map(p => [p.name, [p.displayName, p.className, p.tooltip || '']])),
  }));
  process.stdout.write(JSON.stringify({ release: raw.release, classes }, null, 1));
  process.exit(0);
}

// --- stand-ins ---------------------------------------------------------------------------------------
let serial = 0;
const app = { peers: new Map(), groups: false };
const appModel = {
  hasGroups: () => app.groups,
  getPeerNodeById: id => app.peers.get(id),
  getSelectionModelManager: () => ({ hasSelectedGroup: () => app.selection.some(s => s.getType() === 'Group') }),
  getWidgetDataManager: () => ({ getGroupManagerPeerNode: () => ({ getChildren: () => [...app.peers.values()].filter(p => p.isGroup()) }), getPeerNodeById: id => app.peers.get(id) }),
};
const applicationModelAdjust = method('appdesigner_interface/model/ApplicationComponentModel', 'adjustPropertyInspector', {});
class Peer {
  constructor(type, name, opts = {}) {
    this.type = type; this.name = name; this.kids = []; this.parent = null; this.groupId = opts.group || '';
    const sheet = proxyMap.AppDesigner[type];
    this.props = {};
    if (sheet) for (const p of sheet.propertySheet.properties) this.props[p.name === 'ContextMenu' ? 'ContextMenuID' : p.name] = p.name === 'ContextMenu' ? '' : name + '.' + p.name;
    this.props.CodeName = name;
    if (opts.text !== undefined) for (const k of ['Text', 'Title', 'Name']) if (k in this.props) this.props[k] = opts.text;
    this.ComponentAdapter = {
      IsUserComponent: !!opts.user,
      Name: WORDS[type] ? WORDS[type].name : 'UserComponent',
      canCacheInspectorByClassNames: !opts.user,
      DisabledPropertiesInGrid: ['Position'],
      adjustPropertyInspector: applicationModelAdjust,
      PropertyEditing: { ParticipatesInMassEditing: !opts.user },
      CodeNameGeneration: { DefaultText: WORDS[type] ? WORDS[type].text : 'custom', DefaultCodeName: opts.codeBase || 'X', DefaultTextPropertyName: opts.textProperty },
      getDefaultValues: () => ({}),
    };
    app.peers.set(name, this);
  }
  getType() { return this.type; }
  getId() { return this.name; }
  getProperties() { return this.props; }
  getProperty(k) { return k === 'GroupId' ? this.groupId : k === 'LayoutConstraints' ? this.layout : this.props[k]; }
  getChildren() { return this.kids; }
  getParent() { return this.parent; }
  isGroup() { return false; }
  isGroupMember() { return !!this.groupId; }
  getController() { return { getButtonGroupContentType: () => (this.kids.some(k => k.type === 'matlab.ui.control.ToggleButton') ? 'matlab.ui.control.ToggleButton' : 'matlab.ui.control.RadioButton') }; }
}
class GroupPeer {
  constructor(name, members, labeled) { this.name = name; this.members = members; this.labeled = labeled; this.ComponentAdapter = { IsUserComponent: false }; app.peers.set(name, this); app.groups = true; }
  getType() { return 'Group'; }
  getId() { return this.name; }
  isGroup() { return true; }
  isGroupMember() { return false; }
  getChildren() { return []; }
  getProperty(k) { return k === 'Members' ? this.members : k === 'LabeledComponent' ? this.labeled : undefined; }
  getMemberPeerNodes() { return this.members.map(m => app.peers.get(m)); }
}
function put(parent, kid) { kid.parent = parent; parent.kids.push(kid); return kid; }

// --- the view's cut-outs ------------------------------------------------------------------------------
const viewModule = 'appdesigner/view/propertyview/inspector/PropertyInspectorView';
const lang = { clone: v => JSON.parse(JSON.stringify(v)) };
const viewClosure = { c: lang, e: class { constructor(s) { Object.assign(this, s); } }, a: MenuUtil, A: { getFigurePeerNode: () => ({ getChildren: () => [] }) }, d: null, s: null, l: {}, u: {}, g: class { }, C: {}, D: {}, M: {}, E: null, p: null, o: null, i: null, n: {}, r: {} };
const view = { _AllInspectorInfo: proxyMap };
for (const name of ['_createInspectorObject', '_adjustInspectorInfo', '_removePropertyFromInfo', '_isPropertyRegistered', '_getClassNameForProperty', '_getAllContextMenusInUse']) {
  view[name] = method(viewModule, name, viewClosure);
}
view._convertValueToInspector = (b, name, value) => value;
const JS_UTILS = {
  removeFromClassList: (n, c) => { n.classes.delete(c); }, addToClassList: (n, c) => { n.classes.add(c); },
};
const sheetModule = 'inspector_client/widget/InspectorPropertySheet';
const P = { UNION: 'union', INTERSECTION: 'intersection', FIRST: 'first', LAST: 'last' };
const x = { ALL: 'all', BLANK: 'blank', FIRST: 'first', LAST: 'last' };
const domConstruct = {
  destroy: n => { if (n.parentNode) n.parentNode.kids.splice(n.parentNode.kids.indexOf(n), 1); },
  place: (n, to) => { if (n.parentNode) n.parentNode.kids.splice(n.parentNode.kids.indexOf(n), 1); n.parentNode = to; to.kids.push(n); },
  create: (tag, attrs, to) => { const n = { label: attrs.textContent, cls: attrs.class, parentNode: to, classes: new Set(), kids: [] }; to.kids.push(n); return n; },
};
const query = (sel, root) => {
  if (sel === '.searchMatchesLabel') { const list = (root.containerNode || root).kids.filter(k => k.label !== undefined); return list; }
  if (sel === '.propertyName') return [root.nameNode];
  return [];
};
const sheetRenderer = {
  renderPropertyGroup(sheet, group, into) { const g = { kind: 'group', title: group.displayName, expanded: group.expanded, containerNode: { kids: [] }, startup() {} }; into.kids.push(g); return g; },
  renderPropertySubGroup(sheet, item, into) { const g = { kind: 'sub', containerNode: { kids: [] }, startup() {} }; into.push ? into.push(g) : into.kids.push(g); return g; },
  renderPropertyEditorGroup(sheet, item, into) { const g = { kind: 'editor', containerNode: { kids: [] }, startup() {} }; into.push ? into.push(g) : into.kids.push(g); return g; },
  render() { throw new Error('custom'); },
  renderPropertySet(sheet, name, values, into) {
    const info = sheet.propertySheet.propertySheet.properties.find(p => p.name === name) || {};
    const display = info.displayName || name;
    const set = { kind: 'set', property: name, propertyName: display, PropertyName: display, propertySheet: { tooltip: info.tooltip }, classes: new Set(), kids: [], parentNode: into, startup() {} };
    set.inspectorPropertySet = set;
    set.nameNode = { innerHTML: display };
    (into.kids || into).push(set);
    sheet.propertySets[name] = set;
    return set;
  },
};
function newSheet(inspectorObject, showGroups) {
  const closure = {
    E: null, p: null, c: null, d: null, o: null, s: domConstruct, i: null, e: null, n: query, r: null, a: null, l: class { constructor() { this.kids = []; } placeAt() {} startup() {} }, u: null, g: null, C: null, A: Constants, D: sheetRenderer, M: null, S: JS_UTILS, y: null, T: inspectorNls, I: '', b: 'propertySetOuterContainer', P, x, dijit: {},
  };
  const sheet = {
    showGroups, sortProperties: true, propertySheet: inspectorObject, multiplePropertiesCombinationMode: P.INTERSECTION, multipleValuesCombinationMode: x.LAST,
    groups: [], propertySets: {}, containerNode: { kids: [] }, domNode: { classList: { add() {}, remove() {} } }, own() {}, sortedProperties: [], groupedProperties: [], numOfGroupedProperties: 0, groupContainer: null,
  };
  for (const name of ['_updateObjectProperties', '_consolidateObjectProperties', '_intersectObjectProperties', '_combineValues', '_renderPropertyItems', '_renderPropertyItem', '_createPropertySheetView', '_createGroupPanel', '_getGroupPropertyNames', '_getGroupPropertyName', '_comparisonFcnForSort', '_highlightTooltip']) {
    sheet[name] = method(sheetModule, name, closure);
  }
  sheet._handleSearch = method(sheetModule, '_handleSearch', closure);
  sheet.domNode.containerNode = sheet.containerNode;
  global.dijit = {};
  sheet.objectProps = sheet._updateObjectProperties(inspectorObject.objects);
  sheet._createPropertySheetView();
  return sheet;
}

const cacheId = method('inspector_client/widget/InspectorClientManager', '_getIDForCache', {});
const stringModule = { substitute: (t, a) => t.replace(/\$\{(\d+)\}/g, (_, k) => a[k]) };
const MutualUtil = {
  isButtonGroup: n => n.type === 'matlab.ui.container.ButtonGroup',
  getFullGroupTitleFromPeerNode: n => (n.kids.some(k => k.type === 'matlab.ui.control.ToggleButton') ? library.toggleButtonGroupComponentLibraryName : library.radioButtonGroupComponentLibraryName),
};
const titles = (selection) => {
  const out = {};
  const self = {
    _appModel: { getSelectionModelManager: () => ({ hasSelectedGroup: () => selection.some(s => s.getType() === 'Group') }) },
    _tabContainer: { hide() { out.hidden = true; }, show() {}, setFirstTabTitle(t) { out.tab = t; } },
    _currentContentPane: { resize() {}, set(k, v) { out.panel = v; } },
  };
  appModel.getSelectionModelManager = () => ({ hasSelectedGroup: () => selection.some(s => s.getType() === 'Group') });
  const f = method('appdesigner/view/propertyview/PropertiesPanelController', '_updateContentPaneTitle', {
    E: null, p: null, c: stringModule, d: null, o: { isInspectableApp: n => n.type === 'App' }, s: Object.assign({}, GroupUtil, { replaceGroupsInSelection: (m, g) => GroupUtil.replaceGroupsInSelection(appModel, g) }), i: MenuUtil, e: MutualUtil, n: null, r: dialogsAppDesigner, a: null, l: null,
  });
  f.call(self, [...selection]);
  return out;
};

const getView = (selection) => {
  // _PropertyViewFactory.getView's cases, with the factory's own answers.
  const d = selection;
  if (d.length === 0) return 'empty';
  if (d.isGroup() && d.isLabeledComponent()) return 'labeled';
  if (d.length === 1 && !d.isGroup()) return 'single';
  const components = d.getComponents();
  return components.every(c => c.getType() === components[0].getType()) ? 'homogeneous' : 'heterogeneous';
};

function inspect(selection) {
  app.selection = selection;
  const decorated = Controller._decorateSelection(appModel, selection);
  const kind = getView(decorated);
  const words = titles(selection);
  if (kind === 'empty') return { kind, tab: words.tab, panel: words.panel, hidden: !!words.hidden };
  const anchor = decorated.getAnchorComponent();
  const nonLabeled = factory._getStrategyForNonLabeledComponentSelection(decorated);
  const Strategy = kind === 'labeled' ? LabeledDesign : nonLabeled;
  const strategy = new Strategy({ appModel });
  const shown = strategy._determineComponentsToShow(decorated);
  const out = { kind, tab: words.tab, panel: words.panel, anchor: anchor.getId(), shown: shown.map(c => c.getId()) };
  const anchorSheet = proxyMap.AppDesigner[anchor.getType()];
  if (!anchorSheet) { return out; }
  const info = { objects: [], className: anchor.getType(), showDefaultGroup: false, propertySheet: anchorSheet.propertySheet };
  const object = view._createInspectorObject(lang.clone(info), shown);
  out.cache = cacheId.call({}, object);
  const grouped = newSheet(object, true);
  const alpha = newSheet(view._createInspectorObject(lang.clone(info), shown), false);
  const rowsOf = kids => kids.flatMap(k => (k.kind === 'set' ? [k.property] : k.kind === 'sub' ? [{ sub: rowsOf(k.containerNode.kids) }] : rowsOf(k.containerNode.kids)));
  out.groups = grouped.groupContainer.kids.map(g => ({ title: g.title, expanded: !!g.expanded, rows: rowsOf(g.containerNode.kids) }));
  out.alpha = alpha.containerNode.kids.map(k => k.property);
  // Every value shown is the last object's that has it.
  out.lastWins = Object.keys(grouped.objectProps).every(k => grouped.objectProps[k][0] === object.objects[object.objects.length - 1][k]);
  out._alpha = alpha;
  out._strategy = strategy;
  out._decorated = decorated;
  return out;
}

function search(alpha, text) {
  try {
    alpha._handleSearch({ substring: text });
  } catch (e) {
    return { text, threw: String(e.message).slice(0, 80) };
  }
  const direct = [], help = [];
  let none = false, inHelp = false;
  for (const k of alpha.containerNode.kids) {
    if (k.label !== undefined) {
      if (k.label === inspectorNls.noMatchesFound) none = true;
      else inHelp = true;
    } else if (!k.classes.has('notASearchResult')) {
      (inHelp ? help : direct).push(k.property);
    }
  }
  return { text, direct, help, none };
}

// --- the scenes --------------------------------------------------------------------------------------
let seed = 20261030;
const rnd = n => { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return (seed >> 8) % n; };
const pick = list => list[rnd(list.length)];
const SELECTABLE = TYPES.filter(t => t !== 'matlab.ui.Figure');
// A plain group: a label with one other member would be a labelled component, which the client works out from the members.
const NOT_LABELS = SELECTABLE.filter(t => t !== 'matlab.ui.control.Label');

function fresh() { app.peers = new Map(); app.groups = false; serial = 0; }
function make(type, opts) { return new Peer(type, 'c' + (++serial), opts); }

function words(rowNames, type) {
  const props = proxyMap.AppDesigner[type].propertySheet.properties;
  const list = [];
  for (let k = 0; k < 2; k++) {
    const name = pick(rowNames.length ? rowNames : ['Position']);
    const at = rnd(Math.max(1, name.length - 3));
    list.push(name.slice(at, at + 3 + rnd(3)));
    const tip = (pick(props).tooltip || '').split('||')[0].split(' ').filter(w => /^[A-Za-z]{4,}$/.test(w));
    if (tip.length) list.push(pick(tip));
  }
  list.push('zzq', pick(['COLOR', 'font', 'Value', 'on', 'TIck']));
  return list;
}

function scene(selection, description) {
  const out = { picks: description };
  try {
    const r = inspect(selection);
    Object.assign(out, r);
    if (r._alpha) {
      const names = r.alpha;
      out.searches = words(names, r._decorated.getAnchorComponent().getType()).map(w => search(r._alpha, w));
      out.edits = [];
      const anchorProps = proxyMap.AppDesigner[r._decorated.getAnchorComponent().getType()].propertySheet.properties.map(p => p.name);
      for (const name of [pick(anchorProps), 'FontSize', 'AutoResizeChildren', 'Visible']) {
        const e = r._strategy._getComponentsAndPropertiesForEdit(r._decorated, { [name]: 1 });
        out.edits.push({ property: name, components: e.Components.map(c => c.getId()) });
      }
    }
  } catch (e) {
    out.threw = String(e && e.message || e).slice(0, 160);
  }
  delete out._alpha; delete out._strategy; delete out._decorated;
  return out;
}

const scenes = [];
// Every class on its own.
for (const type of TYPES) {
  fresh();
  const figure = new Peer('matlab.ui.Figure', 'fig');
  const c = type === 'matlab.ui.Figure' ? figure : put(figure, make(type));
  scenes.push(scene([c], [{ t: type, n: c.name }]));
}
// Menus by where they stand.
for (const [parentType, kids] of [['matlab.ui.Figure', 0], ['matlab.ui.Figure', 1], ['matlab.ui.container.Menu', 0], ['matlab.ui.container.Menu', 1], ['matlab.ui.container.ContextMenu', 0], ['matlab.ui.container.ContextMenu', 1]]) {
  fresh();
  const figure = new Peer('matlab.ui.Figure', 'fig');
  const parent = parentType === 'matlab.ui.Figure' ? figure : put(figure, make(parentType));
  const menu = put(parent, make('matlab.ui.container.Menu'));
  if (kids) put(menu, make('matlab.ui.container.Menu'));
  scenes.push(scene([menu], [{ t: menu.type, n: menu.name, parent: parentType, kids }]));
}
// Button groups by what they hold.
for (const inner of [null, 'matlab.ui.control.RadioButton', 'matlab.ui.control.ToggleButton']) {
  fresh();
  const figure = new Peer('matlab.ui.Figure', 'fig');
  const group = put(figure, make('matlab.ui.container.ButtonGroup'));
  if (inner) put(group, make(inner));
  scenes.push(scene([group], [{ t: group.type, n: group.name, holds: inner }]));
}
// A grid's child.
{
  fresh();
  const figure = new Peer('matlab.ui.Figure', 'fig');
  const grid = put(figure, make('matlab.ui.container.GridLayout'));
  const b = put(grid, make('matlab.ui.control.Button'));
  b.layout = { Type: 'Grid' };
  scenes.push(scene([b], [{ t: b.type, n: b.name, grid: true }]));
}
// Several: alike, mixed, with groups, labelled components and a custom component among them.
for (let k = 0; k < 500; k++) {
  fresh();
  const figure = new Peer('matlab.ui.Figure', 'fig');
  const count = 2 + rnd(3);
  const selection = [], description = [];
  const base = pick(SELECTABLE);
  const mode = rnd(10);
  for (let j = 0; j < count; j++) {
    const type = mode < 2 ? base : pick(SELECTABLE);
    if (mode >= 8 && j === 0) {
      const a = put(figure, make('matlab.ui.control.Label', { group: 'g1' }));
      const b = put(figure, make(pick(['matlab.ui.control.EditField', 'matlab.ui.control.Slider', 'matlab.ui.control.Spinner', 'matlab.ui.control.Gauge'])), { group: 'g1' });
      b.groupId = 'g1';
      selection.push(new GroupPeer('g1', [a.name, b.name], true));
      description.push({ g: [{ t: a.type, n: a.name }, { t: b.type, n: b.name }] });
    } else if (mode === 7 && j === 0) {
      const a = put(figure, make(pick(NOT_LABELS), { group: 'g2' }));
      const b = put(figure, make(pick(NOT_LABELS), { group: 'g2' }));
      selection.push(new GroupPeer('g2', [a.name, b.name], false));
      description.push({ g: [{ t: a.type, n: a.name }, { t: b.type, n: b.name }] });
    } else if (mode === 6 && j === 0) {
      const c = put(figure, make('my.Custom', { user: true }));
      selection.push(c);
      description.push({ t: c.type, n: c.name, user: true });
    } else {
      const c = put(figure, make(type));
      selection.push(c);
      description.push({ t: type, n: c.name });
    }
  }
  scenes.push(scene(selection, description));
}
// One group whole: labelled or plain.
for (let k = 0; k < 30; k++) {
  fresh();
  const figure = new Peer('matlab.ui.Figure', 'fig');
  const labeled = k % 2 === 0;
  const a = put(figure, make(labeled ? 'matlab.ui.control.Label' : pick(NOT_LABELS)));
  const b = put(figure, make(pick(NOT_LABELS)));
  scenes.push(scene([new GroupPeer('g', [a.name, b.name], labeled)], [{ g: [{ t: a.type, n: a.name }, { t: b.type, n: b.name }], labeled }]));
}
// An edit reaching what is inside: AutoResizeChildren.
for (let k = 0; k < 20; k++) {
  fresh();
  const figure = new Peer('matlab.ui.Figure', 'fig');
  const panel = put(figure, make('matlab.ui.container.Panel'));
  const inner = put(panel, make('matlab.ui.container.Tab' === pick(SELECTABLE) ? 'matlab.ui.container.Panel' : 'matlab.ui.container.ButtonGroup'));
  put(inner, make('matlab.ui.control.Button'));
  const other = put(figure, make('matlab.ui.container.Panel'));
  const selection = k % 2 ? [panel] : [panel, other];
  app.selection = selection;
  const decorated = Controller._decorateSelection(appModel, selection);
  const strategy = new NonLabeled();
  const e = strategy._getComponentsAndPropertiesForEdit(decorated, { AutoResizeChildren: 'off' });
  scenes.push({ cascade: true, picks: selection.map(s => s.name), nested: { panel: panel.name, inner: inner.name, innerType: inner.type, other: other.name, button: inner.kids[0].name }, targets: e.Components.map(c => c.getId()) });
}

// --- the renames ---------------------------------------------------------------------------------------
const AbstractGenerator = load('appdesigner/model/codenamegenerator/AbstractCodeNameGenerator');
const ModifyGenerator = load('appdesigner/model/codenamegenerator/ModifyCodeNameGenerator');
const TEXTS = ['Run', 'Stop now', 'Go', '3 Way', 'Button', 'Label', 'Save As', 'Off', 'Run', 'Value (V)'];
const NAMED = [
  ['matlab.ui.control.Button', 'Button', 'Button', 'Text'], ['matlab.ui.control.Label', 'Label', 'Label', 'Text'], ['matlab.ui.control.CheckBox', 'CheckBox', 'Check Box', 'Text'],
  ['matlab.ui.control.StateButton', 'Button', 'State Button', 'Text'], ['matlab.ui.container.Panel', 'Panel', 'Panel', 'Title'],
];
const renames = [];
for (let k = 0; k < 200; k++) {
  fresh();
  const figure = new Peer('matlab.ui.Figure', 'fig');
  const count = 2 + rnd(3);
  const peers = [], description = [];
  const manager = { getGroupManagerPeerNode: () => ({ getChildren: () => [...app.peers.values()].filter(p => p.isGroup && p.isGroup()) }), getPeerNodeById: id => app.peers.get(id) };
  const inUse = ['fig'];
  for (let j = 0; j < count; j++) {
    const [type, base, text, prop] = NAMED[rnd(2)];
    const numbered = base + (j === 0 ? '' : '_' + (j + 1));
    const hand = rnd(5) === 0;
    const c = new Peer(type, 'x' + j, { text: hand ? 'Words ' + j : text, codeBase: base, textProperty: prop });
    c.props.CodeName = hand ? 'MyName' + j : numbered;
    c.ComponentAdapter.CodeNameGeneration.DefaultText = text;
    put(figure, c);
    peers.push(c);
    inUse.push(c.props.CodeName);
    description.push({ t: type, n: c.props.CodeName, text: c.props[prop] });
  }
  const newText = pick(TEXTS);
  const out = { picks: description, text: newText };
  try {
    const gen = new ModifyGenerator(peers, { Text: newText, Title: newText }, inUse, manager, 63);
    out.after = gen.generateCodeNameData().map(d => (d.PropertyValues.CodeName === undefined ? null : d.PropertyValues.CodeName));
  } catch (e) {
    out.threw = String(e && e.message || e).slice(0, 160);
  }
  renames.push(out);
}

process.stdout.write(JSON.stringify({ release: raw.release, words: { none: inspectorNls.noMatchesFound, help: inspectorNls.helpDocResults, multipleTab: dialogsAppDesigner.multipleComponentsInspectorTitle, multiplePanel: dialogsAppDesigner.multipleComponentSelected, noneTab: dialogsAppDesigner.noComponentInspectorTitle, nonePanel: dialogsAppDesigner.noComponentSelected, labeled: dialogsAppDesigner.labeledComponentSelection, single: dialogsAppDesigner.singleComponentSelected }, scenes, renames }));
