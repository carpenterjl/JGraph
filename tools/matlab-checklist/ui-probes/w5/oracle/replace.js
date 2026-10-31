// What R2025b's App Designer does for Replace With, from its own code run unmodified - for the App
// Designer window plan's stage W5e, whose DesignReplacement transcribes it:
//   node replace.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignReplacementOracle.json
//
// The table: every class's family, name and defaults come from
// uicomponents_appdesigner_plugin/init/derived/ComponentDefaultsProduction loaded whole, and what each
// class is offered from appdesigner/util/CompReplacementUtil over them.
//
// The menu: appdesigner/view/contextmenu/CompReplacementProvider loaded whole, its inclusionCriteria
// asked of a selection of one of each class, a labelled component's label, and a plain group.
//
// The replacement: appdesigner/command/ReplaceComponentCommand loaded whole and its execute() run, over
// the real ComponentFamilyReplacerFactory, its replacers, CodeNameGeneratorFactory and its generators
// and CompReplacementUtil. The commands it runs to change the tree (DeleteCommand, AddComponentCommand,
// AddLabelToComponentCommand, AddToGroupCommand, RenameAppCallbackFunctionCommand) and the peer nodes
// are stand-ins over a small tree. They do what CodeController and the commands do to names and
// callbacks: a delete takes bindings off and removes a callback left with none whose code is its
// default; an add binds each callback property that holds a name, or makes a callback for it.
//
// A scene that throws records the error instead of a result (`threw`): R2025b's replacement fails there.
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
stub('appdesigner/application/AppDesignerServiceProvider', { getService: () => null, services: new Proxy({}, { get: (_, k) => k }) });
stub('settings/settingsService', {});
stub('appdesigner/util/GridUtil', { isGrid: () => false, isGridType: t => t === 'matlab.ui.container.GridLayout' });
stub('appdesigner/flux/intent/AppIntentCreator', { contextMenuAssignment() {} });
global.dojo = { mixin: (d, ...s) => Object.assign(d, ...s) };

// ---- the tree ------------------------------------------------------------------------------------------
let nextId = 0;
const byId = new Map();
function node(type, props, parent, adapter) {
  const n = {
    id: 'n' + (++nextId), type, props, parent, children: [], ComponentAdapter: adapter, group: null, label: null, comp: null,
    getId() { return this.id; },
    getType() { return this.type; },
    getParent() { return this.parent; },
    getChildren() { return this.children; },
    getProperty(k) { return this.props[k]; },
    getProperties() { return this.props; },
    setProperties(p) { Object.assign(this.props, p); },
    isGroup() { return this.type === 'Group'; },
    isGroupMember() { return !!this.group; },
    setComponentToPreferredSize() {},
  };
  byId.set(n.id, n);
  return n;
}

const GroupUtil = {
  isLabelOfLabeledComponent: c => !!c.group && c.group.label === c,
  isComponentOfLabeledComponent: c => !!c.group && !!c.group.label && c.group.comp === c,
  isLabeledComponent: g => !!g.label,
  getComponentOfLabeledComponent: g => g.comp,
  getLabelOfLabeledComponent: g => g.label,
  getGroupForMember: m => m.group || undefined,
};
stub('appdesigner/util/GroupUtil', GroupUtil);
stub('appdesigner/util/FigureUtil', {
  getFigurePeerNode: app => app.fig,
  getIndexInPeerNodeListById: (list, id) => list.findIndex(c => c.getId() === id),
});

// Each stand-in records what the real command is asked to do, and does it to the tree.
stub('appdesigner/command/_UndoableCommand', class { constructor(appModel) { this._appModel = appModel; } destroy() {} });

function unbind(app, n) {
  for (const c of app.callbacks.slice()) {
    c.bindings = c.bindings.filter(b => b.peer !== n);
    if (c.bindings.length === 0 && c.isDefault) app.callbacks.splice(app.callbacks.indexOf(c), 1);
  }
}
function bind(app, n) {
  for (const entry of app.callbackData(n.type)) {
    const name = n.props[entry];
    if (!name) continue;
    let c = app.callbacks.find(x => x.name === name);
    if (!c) { c = app.callback(name, name, true); app.callbacks.push(c); }
    c.bindings.push({ peer: n, prop: entry });
  }
}
stub('appdesigner/command/DeleteCommand', class {
  constructor(app, target) { this.app = app; this.target = target; }
  execute() {
    const remove = n => {
      unbind(this.app, n);
      const at = n.parent.children.indexOf(n);
      if (at >= 0) n.parent.children.splice(at, 1);
    };
    if (this.target.isGroup()) { remove(this.target.label); remove(this.target.comp); } else remove(this.target);
  }
});
stub('appdesigner/command/AddComponentCommand', class {
  constructor(args) { this.args = args; }
  execute() {
    const e = this.args.EntryData[0];
    const n = node(e.Type, e.PropertyValues, this.args.Parent, e.Adapter);
    const siblings = this.args.Parent.children;
    siblings.splice(Math.min(this.args.IndexToInsertAt, siblings.length), 0, n);
    bind(this.args.AppModel, n);
    return [n];
  }
});
stub('appdesigner/command/AddLabelToComponentCommand', class {
  constructor(app, comp, labelProps) { this.app = app; this.comp = comp; this.labelProps = labelProps; }
  execute() {
    const label = node('matlab.ui.control.Label', this.labelProps, this.comp.parent, this.app.getComponentInfos()['matlab.ui.control.Label']);
    const siblings = this.comp.parent.children;
    const at = siblings.indexOf(this.comp);
    siblings.splice(at > 0 ? at - 1 : 0, 0, label);
    const group = node('Group', { GroupId: '' }, null, null);
    group.label = label;
    group.comp = this.comp;
    label.group = this.comp.group = group;
    return [group];
  }
});
stub('appdesigner/command/AddToGroupCommand', class { constructor(app, group, members) { this.group = group; this.members = members; } execute() { this.members.forEach(m => { m.outerGroup = this.group; }); } });
stub('appdesigner/command/AddBindingCommand', class { execute() {} });
stub('appdesigner/command/RenameAppCallbackFunctionCommand', class {
  constructor(app, callback, _kind, name) { this.callback = callback; this.name = name; }
  execute() { this.callback.name = this.name; this.callback.generatedName = this.name; }
});
stubWhere(n => /\/propertysheet\/|\/componentlibrary\/(?!LabeledComponentPositionConfig$)|^appdesigner\/view\/(?!contextmenu\/CompReplacementProvider$)|^appdesigner\/command\/(?!ReplaceComponentCommand$)|^appdesigner\/flux\/|^MW\/|^mw-remote\/|^visualcomponents\/|^dojo\/(topic|query|NodeList-traverse|Deferred)$|^appdesigner\/util\/(CanvasUtil|AppDetailsUtil)$/.test(n), () => Stub);
stubWhere(n => /^dojo\//.test(n) && !n.startsWith('dojo/i18n!'), () => Stub);
stub('appdesigner/view/contextmenu/AbstractComponentMenuItemsProvider', class {});
stub('appdesigner/view/contextmenu/Criteria/CustomCriteria', class { constructor(f) { this.test = f; } });

const infos = load('uicomponents_appdesigner_plugin/init/derived/ComponentDefaultsProduction');
const Util = load('appdesigner/util/CompReplacementUtil');
const ReplaceComponentCommand = load('appdesigner/command/ReplaceComponentCommand');
const Provider = load('appdesigner/view/contextmenu/CompReplacementProvider');
const Config = load('appdesigner_interface/componentlibrary/LabeledComponentPositionConfig');

// The class's defaults as a new component in the light theme holds them, and the label place its entry names.
const DESIGN_DEFAULTS = {};
for (const [type, a] of Object.entries(infos)) {
  DESIGN_DEFAULTS[type] = Object.assign({}, a._DefaultValues.unthemed, a._DefaultValues.light);
  a.getDefaultValues = () => DESIGN_DEFAULTS[type];
}
const PLACE = {};
for (const name of ['Knob', 'DiscreteKnob', 'NumberField', 'NumericEditField', 'TextField', 'EditField', 'TextArea', 'Slider', 'RangeSlider', 'Spinner', 'DropDown', 'ListBox', 'Switch', 'RockerSwitch', 'ToggleSwitch', 'SliderSwitch', 'Gauge', 'CircularGauge', 'LinearGauge', 'NinetyDegreeGauge', 'SemicircularGauge', 'Lamp', 'ColorPicker', 'DatePicker']) {
  const text = moduleText('uicomponents_appdesigner_plugin/componentlibrary/' + name + 'EntryDefinition');
  if (!text) continue;
  const type = /this\.TYPE="([^"]+)"/.exec(text);
  const cfg = /_labelConfig=\w+\.(\w+)/.exec(text);
  if (type && cfg) PLACE[type[1]] = cfg[1];
}
for (const [type, a] of Object.entries(infos)) {
  a.EntryDefinition = class { getLabelConfig() { return PLACE[type] ? Config[PLACE[type]] : undefined; } };
}

// ---- the app -------------------------------------------------------------------------------------------
function makeApp() {
  const figureInfo = infos['matlab.ui.Figure'];
  const app = {
    nameLengthMax: 63,
    callbacks: [],
    selection: null,
    renames: [],
    getComponentInfos: () => infos,
    getPeerNodeById: id => byId.get(id),
    getWidgetDataManager: () => ({}),
    getSelectionModelManager: () => ({ setComponentSelection: s => { app.selection = s; } }),
    getViewState: () => ({ updateCanvas() {} }),
    getDispatcher: () => ({}),
    isValid: () => true,
    getCodeModel: () => ({
      bindingCollection: { getBindingsForComponent: () => [] },
      getCallbackCollection: () => app.callbacks,
      getController: () => ({ renameComponentFromReplacement: (a, b) => app.renames.push([a, b]) }),
      getCodeNameTypeHashmap: () => {
        const h = {};
        const walk = n => { h[n.props.CodeName] = 'Component'; n.children.forEach(walk); };
        walk(app.fig);
        app.callbacks.forEach(c => { h[c.name] = 'Callback'; });
        return h;
      },
    }),
    callbackData: type => {
      const data = infos[type].getCallbackDataWithoutCategorization ? infos[type].getCallbackDataWithoutCategorization() : [];
      return data.map(d => d.Label);
    },
    callback: (name, generated, isDefault) => {
      const c = {
        name, generatedName: generated, isDefault, bindings: [],
        isDefaultName() { return this.generatedName === this.name; },
        getBindings() {
          const list = c.bindings;
          return {
            forEach: f => list.map(b => ({ getComponentPeerNode: () => b.peer, getCallbackProperty: () => b.prop })).forEach(f),
            isAllSameProperty: () => list.every(b => b.prop === list[0].prop),
            getProperty: () => (list.length ? list[0].prop : null),
          };
        },
      };
      return c;
    },
  };
  app.fig = node('matlab.ui.Figure', { CodeName: 'UIFigure', Name: 'MATLAB App' }, null, figureInfo);
  return app;
}

// A seeded generator, so the oracle is the same on every run.
let seed = 20261030;
function rand() {
  seed = (seed * 1103515245 + 12345) % 2147483648;
  return seed / 2147483648;
}
const int = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));
const pick = list => list[int(0, list.length - 1)];

const familyTypes = Object.keys(infos).filter(t => Util.getFamily(infos[t]) !== 'NONE');
const textOf = v => (Array.isArray(v) ? v.join() : v);

function sourceProps(type, codeName) {
  const p = clone(DESIGN_DEFAULTS[type]);
  p.CodeName = codeName;
  p.GroupId = '';
  if (!p.Location) return p; // a tool has no place of its own

  const dx = p.Location[0] - p.OuterLocation[0];
  const dy = p.Location[1] - p.OuterLocation[1];
  const dw = p.OuterSize[0] - p.Size[0];
  const dh = p.OuterSize[1] - p.Size[1];
  const fixed = p.IsSizeFixed || [false, false];
  p.Size = [fixed[0] ? p.Size[0] : int(30, 300), fixed[1] ? p.Size[1] : int(10, 200)];
  p.Location = [int(1, 500), int(1, 400)];
  p.OuterLocation = [p.Location[0] - dx, p.Location[1] - dy];
  p.OuterSize = [p.Size[0] + dw, p.Size[1] + dh];
  p.Position = [...p.Location, ...p.Size];
  p.OuterPosition = [...p.OuterLocation, ...p.OuterSize];
  p.CodeName = codeName;
  return p;
}

function vary(type, p) {
  if ('Limits' in p && rand() < 0.6) {
    // Only a spinner's and a numeric field's limits can be infinite.
    const open = type === 'matlab.ui.control.Spinner' || type === 'matlab.ui.control.NumericEditField';
    const kind = open ? int(0, 3) : 0;
    const lo = int(-50, 50);
    p.Limits = kind === 0 ? [lo, lo + int(1, 100)] : kind === 1 ? ['-Inf', 'Inf'] : kind === 2 ? [lo, 'Inf'] : ['-Inf', lo];
  }
  if (Array.isArray(p.Value) && typeof p.Value[0] === 'number') {
    if (rand() < 0.5) p.Value = [int(-20, 20), int(21, 60)];
  } else if (typeof p.Value === 'number' && rand() < 0.7) {
    p.Value = rand() < 0.1 && p.Limits && p.Limits[1] === 'Inf' ? 'Inf' : int(-20, 60);
  } else if (typeof p.Value === 'string' && type !== 'matlab.ui.control.DropDown' && rand() < 0.5 && !p.Items) {
    p.Value = pick(['', 'abc', 'two words']);
  } else if (Array.isArray(p.Value) && typeof p.Value[0] === 'string' && rand() < 0.5) {
    p.Value = pick([['one'], ['one', 'two'], ['']]);
  }

  if ('FontColor' in p && rand() < 0.3) p.FontColor = [rand() < 0.5 ? 1 : 0, 0.5, 0];
  if ('Text' in p && rand() < 0.5) p.Text = pick(['Go', 'Start Now', 'Button', 'Label', 'Hyperlink', 'Label2']);
  if ('Orientation' in p && rand() < 0.3) p.Orientation = p.Orientation === 'horizontal' ? 'vertical' : 'horizontal';
  if ('Items' in p && rand() < 0.3) { p.Items = ['A', 'B']; p.Value = 'A'; }
  if ('MajorTicks' in p && rand() < 0.2) p.MajorTicks = [0, 50, 100];
  return p;
}

const out = {
  defaults: {},
  places: PLACE,
  families: [],
  menu: [],
  conversions: [],
  names: [],
  callbacks: [],
};

for (const type of Object.keys(infos)) {
  const d = DESIGN_DEFAULTS[type];
  out.defaults[type] = { Location: d.Location, OuterLocation: d.OuterLocation, Size: d.Size, OuterSize: d.OuterSize, AspectRatioLimits: d.AspectRatioLimits, IsSizeFixed: d.IsSizeFixed };
  const fam = Util.getFamily(infos[type]);
  const offered = fam === 'NONE' ? [] : Util.getComponentsOfFamily({ getComponentInfos: () => infos }, { getType: () => type }, fam);
  out.families.push({ type, name: infos[type].Name, family: fam === undefined ? 'TOOL' : fam, offered: offered.map(o => o.Type), offeredNames: offered.map(o => o.Name) });
}

// The menu's own test.
{
  const app = makeApp();
  const provider = new Provider('id', { getWidgetDataManager: () => ({}), getComponentInfos: () => infos });
  const test = provider._MenuItemsInfo[0].inclusionCriteria.test;
  for (const type of Object.keys(infos)) {
    const n = node(type, {}, app.fig, infos[type]);
    out.menu.push({ selection: type, shown: test([n]) });
  }
  const two = [node('matlab.ui.control.Slider', {}, app.fig, infos['matlab.ui.control.Slider']), node('matlab.ui.control.Slider', {}, app.fig, infos['matlab.ui.control.Slider'])];
  out.menu.push({ selection: 'two sliders', shown: test(two) });
  out.menu.push({ selection: 'nothing', shown: test([]) });
  const comp = node('matlab.ui.control.Slider', {}, app.fig, infos['matlab.ui.control.Slider']);
  const label = node('matlab.ui.control.Label', {}, app.fig, infos['matlab.ui.control.Label']);
  const group = node('Group', {}, null, load('appdesigner/group/GroupModel'));
  group.label = label; group.comp = comp; label.group = comp.group = group;
  out.menu.push({ selection: 'the label of a labelled slider', shown: test([label]) });
  out.menu.push({ selection: 'a labelled slider', shown: test([group]) });
  out.menu.push({ selection: 'the field of a labelled slider', shown: test([comp]) });
  const plain = node('Group', {}, null, load('appdesigner/group/GroupModel'));
  out.menu.push({ selection: 'a plain group', shown: test([plain]) });
}

function runScene(sourceType, targetType, labelled, makeProps, extra) {
  const app = makeApp();
  extra = extra || {};
  // Components already in the app, for the names to count against.
  for (const [code, text] of extra.others || []) {
    node('matlab.ui.control.Button', { CodeName: code, Text: text }, app.fig, infos['matlab.ui.control.Button']);
    app.fig.children.push(byIdLast());
  }

  const props = makeProps();
  const source = node(sourceType, props, app.fig, infos[sourceType]);
  let selection = source;
  if (labelled) {
    const lp = clone(DESIGN_DEFAULTS['matlab.ui.control.Label']);
    lp.CodeName = extra.labelCode;
    lp.Text = extra.labelText;
    const label = node('matlab.ui.control.Label', lp, app.fig, infos['matlab.ui.control.Label']);
    app.fig.children.push(label);
    const group = node('Group', { GroupId: '' }, null, null);
    group.label = label; group.comp = source;
    label.group = source.group = group;
    selection = group;
  }

  app.fig.children.push(source);
  for (const c of extra.callbacks || []) {
    const cb = app.callback(c.name, c.generated, c.isDefault);
    source.props[c.prop] = c.name;
    cb.bindings.push({ peer: source, prop: c.prop });
    app.callbacks.push(cb);
  }

  const cmd = new ReplaceComponentCommand(app, source, [selection], targetType);
  try {
    const made = cmd.execute();
    return { app, made, source };
  } catch (e) {
    return { app, threw: e.message, source };
  }

  function byIdLast() { return [...byId.values()].pop(); }
}

// Conversions: every class to everything it is offered, varied.
for (let k = 0; k < 900; k++) {
  const sourceType = pick(familyTypes);
  const fam = Util.getFamily(infos[sourceType]);
  const offered = Util.getComponentsOfFamily({ getComponentInfos: () => infos }, { getType: () => sourceType }, fam);
  const targetType = pick(offered).Type;
  const labelled = !!PLACE[sourceType] && rand() < 0.6;
  let given;
  const scene = runScene(sourceType, targetType, labelled, () => {
    given = vary(sourceType, sourceProps(sourceType, 'Comp' + k));
    return clone(given);
  }, { labelCode: 'Comp' + k + 'Label', labelText: 'Speed' });
  const record = {
    source: sourceType, target: targetType, labelled,
    in: { OuterLocation: given.OuterLocation, OuterSize: given.OuterSize, Size: given.Size, Value: given.Value, Limits: given.Limits, Text: given.Text, FontColor: given.FontColor, Items: given.Items, Orientation: given.Orientation, MajorTicks: given.MajorTicks },
  };
  if (scene.threw) {
    record.threw = scene.threw;
  } else {
    const made = scene.made.isGroup() ? scene.made.comp : scene.made;
    const p = made.props;
    record.out = { Position: p.Position, Location: p.Location, Size: p.Size, Value: p.Value, Limits: p.Limits, Text: p.Text, FontColor: p.FontColor, Items: p.Items, Orientation: p.Orientation, MajorTicks: p.MajorTicks };
  }
  out.conversions.push(record);
}

// Names: what each is called and says, with its label; among others in the app.
const LABEL_TEXTS = ['Speed', 'Max Speed', '', '2 fast'];
for (let k = 0; k < 900; k++) {
  const sourceType = pick(familyTypes);
  const fam = Util.getFamily(infos[sourceType]);
  const offered = Util.getComponentsOfFamily({ getComponentInfos: () => infos }, { getType: () => sourceType }, fam);
  const targetType = pick(offered).Type;
  const naming = infos[sourceType].CodeNameGeneration;
  const labelled = !!PLACE[sourceType] && rand() < 0.75;
  const n = rand() < 0.4 ? int(2, 4) : '';
  const hand = rand() < 0.2;
  let codeName, labelCode, labelText, ownText;
  const defaults = DESIGN_DEFAULTS[sourceType];
  if (labelled) {
    const custom = rand() < 0.5 ? pick(LABEL_TEXTS) : null;
    labelText = custom === null ? naming.DefaultText + n : custom;
    const stem = custom === null ? naming.DefaultCodeName + n : (custom.replace(/\W/g, '').replace(/^[0-9_]*/, '') + naming.DefaultCodeName);
    codeName = hand ? 'myField' + k : stem;
    labelCode = rand() < 0.15 ? 'myLabel' + k : codeName + 'Label';
  } else if (naming.DefaultTextPropertyName) {
    ownText = rand() < 0.5 ? pick(['Go', 'Start Now']) : naming.DefaultText + n;
    const stem = ownText.replace(new RegExp(naming.DefaultText, 'g'), naming.DefaultCodeName).replace(/\W/g, '') + (ownText.includes(naming.DefaultText) ? '' : naming.DefaultCodeName);
    codeName = hand ? 'myField' + k : stem;
  } else {
    codeName = hand ? 'myField' + k : naming.DefaultCodeName + n;
  }

  const others = [];
  const count = int(0, 3);
  const taken = [naming.DefaultText, infos[targetType].CodeNameGeneration.DefaultText, 'Knob', 'Slider', 'Button', 'Switch', 'Gauge'];
  for (let i = 0; i < count; i++) {
    const base = pick(taken).replace(/\W/g, '') + pick(['', '2', '3', '_2']);
    others.push([base, pick(['Other', base, taken[int(0, taken.length - 1)] + pick(['', '2', '3'])])]);
  }

  const scene = runScene(sourceType, targetType, labelled, () => {
    const p = sourceProps(sourceType, codeName);
    if (ownText !== undefined) p[naming.DefaultTextPropertyName] = ownText;
    return p;
  }, { labelCode, labelText, others });
  const record = { source: sourceType, target: targetType, labelled, codeName, labelCode, labelText, ownText, others };
  if (scene.threw) {
    record.threw = scene.threw;
  } else {
    const made = scene.made.isGroup() ? scene.made.comp : scene.made;
    record.out = { codeName: made.props.CodeName, text: made.props.Text };
    if (scene.made.isGroup()) {
      record.out.labelCode = scene.made.label.props.CodeName;
      record.out.labelText = textOf(scene.made.label.props.Text);
    }
    record.renames = scene.app.renames;
  }
  out.names.push(record);
}

// Callbacks: which stay bound, and how they are named.
const CALLBACK_PAIRS = [
  ['matlab.ui.control.Slider', 'matlab.ui.control.Knob', 'ValueChangedFcn'],
  ['matlab.ui.control.Slider', 'matlab.ui.control.Knob', 'ValueChangingFcn'],
  ['matlab.ui.control.Spinner', 'matlab.ui.control.NumericEditField', 'ValueChangingFcn'],
  ['matlab.ui.control.Button', 'matlab.ui.control.StateButton', 'ButtonPushedFcn'],
  ['matlab.ui.control.StateButton', 'matlab.ui.control.Button', 'ValueChangedFcn'],
  ['matlab.ui.control.DropDown', 'matlab.ui.control.ListBox', 'ValueChangedFcn'],
  ['matlab.ui.control.DropDown', 'matlab.ui.control.ListBox', 'DropDownOpeningFcn'],
  ['matlab.ui.control.DiscreteKnob', 'matlab.ui.control.DropDown', 'ValueChangedFcn'],
  ['matlab.ui.container.Tree', 'matlab.ui.container.CheckBoxTree', 'SelectionChangedFcn'],
  ['matlab.ui.container.CheckBoxTree', 'matlab.ui.container.Tree', 'CheckedNodesChangedFcn'],
  ['matlab.ui.control.Hyperlink', 'matlab.ui.control.Label', 'HyperlinkClickedFcn'],
  ['matlab.ui.container.toolbar.PushTool', 'matlab.ui.container.toolbar.ToggleTool', 'ClickedCallback'],
  ['matlab.ui.container.toolbar.ToggleTool', 'matlab.ui.container.toolbar.PushTool', 'ClickedCallback'],
];
for (const [sourceType, targetType, prop] of CALLBACK_PAIRS) {
  const naming = infos[sourceType].CodeNameGeneration;
  for (const [generated, isDefault, codeName] of [[true, true, naming.DefaultCodeName], [true, false, naming.DefaultCodeName], [false, false, naming.DefaultCodeName], [true, true, 'myField']]) {
    const defaultName = infos[sourceType].getDefaultCallbackName(codeName, prop);
    const name = generated ? defaultName : 'myCallback';
    const scene = runScene(sourceType, targetType, false, () => sourceProps(sourceType, codeName), {
      callbacks: [{ name, generated: generated ? name : defaultName, isDefault, prop }],
    });
    const record = { source: sourceType, target: targetType, property: prop, name, isDefaultName: generated, isDefaultCode: isDefault, codeName };
    if (scene.threw) {
      record.threw = scene.threw;
    } else {
      const made = scene.made;
      record.out = {
        codeName: made.props.CodeName,
        callbacks: scene.app.callbacks.map(c => ({ name: c.name, bound: c.bindings.map(b => b.prop) })),
      };
    }
    out.callbacks.push(record);
  }
}

process.stdout.write(JSON.stringify(out, (k, v) => (v === undefined ? undefined : v), 0) + '\n');
