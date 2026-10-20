// Shared stubs for the codegen oracle. Everything App Designer-specific is loaded unmodified from the bundle;
// only the host (dojo, mw-event-emitter, the service provider, the UI-only module families) and the two
// data holders the engine reads (the CodeModel and a component peer node) are stubbed here.
'use strict';
const { load, stub, stubWhere } = require('./amd');

// ---- host stubs -------------------------------------------------------------------------------------------
class Evented {
  constructor() { this._l = Object.create(null); }
  on(type, fn) {
    (this._l[type] || (this._l[type] = [])).push(fn);
    return { remove: () => { const a = this._l[type]; const i = a ? a.indexOf(fn) : -1; if (i >= 0) a.splice(i, 1); } };
  }
  once(type, fn) { const h = this.on(type, (...a) => { h.remove(); fn(...a); }); return h; }
  emit(type, ...args) { const a = this._l[type]; if (a) a.slice().forEach(f => f.apply(this, args)); }
  // dojo/on and CodeEditUtil.createObservableHandler reach for these on peer nodes.
  addEventListener(type, fn) { this._h = this._h || new Map(); this._h.set(fn, this.on(type, fn)); }
  removeEventListener(type, fn) { const h = this._h && this._h.get(fn); if (h) h.remove(); }
}
stub('mw-event-emitter/Evented', Evented);

function clone(src) { // dojo/_base/lang.clone: functions by reference, arrays/objects deep
  if (!src || typeof src !== 'object' || typeof src === 'function') return src;
  if (src instanceof Date) return new Date(src.getTime());
  if (src instanceof RegExp) return new RegExp(src);
  if (Array.isArray(src)) return src.map(clone);
  const r = {};
  for (const k of Object.keys(src)) r[k] = clone(src[k]);
  return r;
}
stub('dojo/_base/lang', {
  mixin: (d, ...s) => Object.assign(d || {}, ...s),
  clone,
  hitch: (ctx, fn, ...a) => (typeof fn === 'string' ? ctx[fn] : fn).bind(ctx, ...a),
  isFunction: f => typeof f === 'function',
});
stub('dojo/_base/declare', function (bases, props) {
  if (!Array.isArray(bases) && typeof bases !== 'function') { props = bases; bases = []; }
  const C = function (...a) { if (props && props.constructor !== Object.prototype.constructor) props.constructor.apply(this, a); };
  Object.assign(C.prototype, props || {});
  return C;
});
stub('dojo/on', (target, type, fn) => target.on(type, fn));
stub('dojo/string', {
  substitute(template, map) { return String(template).replace(/\$\{([^}]+)\}/g, (_, k) => map[k]); },
  trim: s => s.trim(),
});
// CodeFormatter asks the service provider for the CodeFormatManager; with none it keeps its defaults
// (IndentSize 4, InsertSpaces, MixedFunctionIndent) merged with the settings handed to its constructor.
stub('appdesigner/application/AppDesignerServiceProvider', { getService: () => null, services: new Proxy({}, { get: (_, k) => k }) });
stub('mw-filename-utils/FileNameUtil', {
  getFileSeparator: p => (p.indexOf('\\') !== -1 ? '\\' : '/'),
  getFileNameExtension: p => p.split('.').pop(),
});
stub('settings/settingsService', {});
// UI-only module families reached by the component model mixins (property sheets, palette entries, views,
// commands, toolstrip constants, dojo DOM helpers). They are only used inside functions codegen never calls.
class Stub {}
stubWhere(n => /\/propertysheet\/|\/componentlibrary\/|^appdesigner\/view\/|^appdesigner\/command\/|^appdesigner\/flux\/|^MW\/|^mw-remote\/|^dojo\/(topic|query|NodeList-traverse|Deferred)$|^appdesigner\/util\/(CanvasUtil|AppDetailsUtil)$|^appdesigner\/model\/codenamegenerator\//.test(n), () => Stub);
// GridUtil pulls dojo/dom-geometry (DOM); the same two predicates ds4's run.js stubbed are all codegen asks of it.
stub('appdesigner/util/GridUtil', { isGrid: n => !!n && n.getType && n.getType() === 'matlab.ui.container.GridLayout', isGridType: t => t === 'matlab.ui.container.GridLayout' });
stubWhere(n => /^dojo\//.test(n) && !n.startsWith('dojo/i18n!'), () => Stub);
// The component defaults registry calls the global dojo.mixin.
global.dojo = { mixin: (d, ...s) => Object.assign(d, ...s) };

// ---- peer node stub ---------------------------------------------------------------------------------------
let nodeCounter = 0;
function makePeerNode(codeName, type, adapter, parent) {
  const n = new Evented();
  n._props = { CodeName: codeName };
  n._id = 'node_' + (++nodeCounter);
  n._children = [];
  n.ComponentAdapter = adapter || null;
  n.getId = () => n._id;
  n.getType = () => type;
  n.getProperty = k => n._props[k];
  n.setProperty = (k, v) => {
    const old = n._props[k];
    n._props[k] = v;
    n.emit('propertiesSet', { data: { newValues: { [k]: v }, oldValues: { [k]: old }, key: k, newValue: v, oldValue: old }, target: n });
  };
  n.getParent = () => parent || null;
  n.getChildren = () => n._children;
  n.getChildIndex = c => n._children.indexOf(c);
  return n;
}

// ---- code model stub --------------------------------------------------------------------------------------
// o: { indentSize, className, inputParameters ('' | 'a, b'), singleton ('' | 'FOCUS'), figureCodeName,
//      help: {summary, description}, startup: null | { params: [...] } }
function makeCodeModel(o) {
  const CodeFormatter = load('appdesigner/codegen/util/CodeFormatter');
  const AppCodeEngineConfig = load('appdesigner/codegen/configurations/AppCodeEngineConfig');
  const CallbackCollection = load('appdesigner/model/codegen/CallbackCollection');
  const CallbackModel = load('appdesigner/model/codegen/CallbackModel');
  const CustomCodeModel = load('appdesigner/model/codegen/CustomCodeModel');
  const FunctionTypes = load('appdesigner/model/codegen/FunctionTypes');
  const BinaryFileFormat = load('appdesigner/model/fileformat/BinaryFileFormat');
  const _Behavior = load('appdesigner/codegen/behaviors/_Behavior');
  const nls = load('dojo/i18n!appdesigner/resources/appdesigner/application_js/nls/CodeGeneration');

  const cm = new Evented();
  cm._formatter = new CodeFormatter({ IndentSize: o.indentSize || 4 });
  cm._config = new AppCodeEngineConfig();
  // The figure's own createComponents lines come from MATLAB (MatlabComponentInitBehavior asks the server);
  // the oracle only needs the frame, so that one behavior is replaced by the empty base behavior.
  cm._config._config.ComponentInitBehavior = function () { return new _Behavior(); };
  cm._callbacks = new CallbackCollection();
  cm._custom = new CustomCodeModel(cm);
  cm._fileFormat = new BinaryFileFormat();
  cm._superClass = cm._config.createSuperClassStrategy(cm._fileFormat);
  cm.fig = makePeerNode(o.figureCodeName || 'UIFigure', 'matlab.ui.Figure');
  cm._componentProperties = [{ Id: cm.fig.getId(), Name: cm.fig.getProperty('CodeName'), ComponentType: 'matlab.ui.Figure', Component: cm.fig }];
  cm._props = { GeneratedClassName: o.className || 'app1', InputParameters: o.inputParameters || '' };
  cm._help = { summary: (o.help && o.help.summary) || '', description: (o.help && o.help.description) || '' };
  cm._singleton = o.singleton || '';
  cm.bindingCollection = Object.assign(new Evented(), { length: 0 });

  cm.getCodeFormatter = () => cm._formatter;
  cm.getCodeEngineConfig = () => cm._config;
  cm.getCallbackCollection = () => cm._callbacks;
  cm.getCustomCodeModel = () => cm._custom;
  cm.getComponentProperties = () => cm._componentProperties;
  cm.getAppProperties = () => [];
  cm.getProperty = k => cm._props[k];
  cm.getSingletonMode = () => cm._singleton;
  cm.getAppTypeDataInterface = () => ({ getOutputMode: () => true });
  cm.checkIfPathToMLAPPVariableNeeded = () => false;
  Object.defineProperty(cm, 'fileFormat', { get: () => cm._fileFormat });
  cm.getSuperClassNameProvider = () => cm._superClass;
  cm.getHelpComments = () => Object.assign({}, cm._help);
  cm.getComponentByCodeName = name => { const p = cm._componentProperties.find(x => x.Name === name); return p ? p.Component : null; };
  cm.getCodeNameTypeHashmap = () => { const h = {}; cm._componentProperties.forEach(p => h[p.Name] = 'AppComponentProperty'); cm._callbacks.forEach(c => h[c.getProperty('Name')] = c.getProperty('CallbackType')); return h; };
  cm.autoFormatEditableCode = () => {};
  cm.firePeerEvent = () => {};

  if (o.startup) {
    // CodeController.updateAppParams builds the startup callback exactly so (Arguments = [app, ...params]).
    const args = [cm._config.getMethodObjectArgumentName()].concat(o.startup.params || []);
    const m = new CallbackModel({ Name: 'startupFcn', Comment: nls.startUpCallbackComment, CallbackType: FunctionTypes.StartupCallback, Arguments: args, Order: 0, ComponentInfo: {} });
    cm._callbacks.addCallback(m);
  }
  return cm;
}

// ---- render: the walk CodeEngine._generateAppCode makes -----------------------------------------------------
function collectLines(builder, out) {
  const BuilderState = load('appdesigner/codegen/util/BuilderState');
  if (builder.state === BuilderState.REMOVED) return out;
  out.push(...builder.getStartCode());
  builder.children.forEach(c => collectLines(c, out));
  out.push(...builder.getEndCode());
  return out;
}
function render(cm, builder) {
  return cm.getCodeFormatter().createAppCode(collectLines(builder, [])).split('\n');
}

// The component model registry (type string -> model mixed with its default values), as App Designer builds it.
function loadRegistry() {
  return load('uicomponents_appdesigner_plugin/init/derived/ComponentDefaultsProduction');
}

module.exports = { Evented, makePeerNode, makeCodeModel, render, collectLines, loadRegistry, load };
