// Host stubs for the W0 toolstrip oracle: App Designer's action and toolstrip modules run unmodified from
// R2025b's bundle; only the host (dojo, the action data service, the services, the app model, the toolstrip
// builder and modules from other bundles) is stubbed here.
'use strict';
const { load, stub, stubWhere, moduleText } = require('./amd');

Object.defineProperty(global, 'navigator', { value: { userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' }, configurable: true });
global.window = global;
global.document = { createElement: () => anything('element'), body: anything('body') };
global.dojo = { mixin: (d, ...s) => Object.assign(d, ...s) };

// A value that answers everything: callable, constructible, every property itself, a no-op on any call.
// Used for the app model's deep reaches and modules outside the bundle; never for what the oracle records.
function anything(label) {
  const target = function () {};
  const p = new Proxy(target, {
    get(t, k) {
      if (k === Symbol.toPrimitive) return () => '';
      if (k === 'then') return undefined;
      if (k === Symbol.iterator) return function* () {};
      if (k === 'toString' || k === 'valueOf') return () => '';
      if (k === 'length') return 0;
      if (k === 'prototype') return t.prototype;
      if (!(k in t)) t[k] = anything(label + '.' + String(k));
      return t[k];
    },
    apply() { return p; },
    construct(t, args, newTarget) { return Reflect.construct(function () {}, [], newTarget); },
  });
  return p;
}

class Evented {
  constructor() { this._l = Object.create(null); }
  on(type, fn) {
    (this._l[type] || (this._l[type] = [])).push(fn);
    return { remove: () => {} };
  }
  emit(type, ...args) { const a = this._l[type]; if (a) a.slice().forEach(f => f.apply(this, args)); }
}
stub('mw-event-emitter/Evented', Evented);

// The action data service records what every action registers and every later update.
class ActionDataService {
  constructor() { this.actions = new Map(); this.order = []; }
  addAction(def) {
    if (this.actions.has(def.id)) throw new Error('Action id must be unique! ' + def.id);
    this.actions.set(def.id, Object.assign({ enabled: true }, def));
    this.order.push(def.id);
    return def.id;
  }
  loadActions(defs) { defs.forEach(d => this.addAction(d)); }
  updateAction(id, props) { const a = this.actions.get(id); if (a) Object.assign(a, props); }
  enableAction(id) { this.updateAction(id, { enabled: true }); }
  disableAction(id) { this.updateAction(id, { enabled: false }); }
  getAction(id) { return this.actions.get(id) || null; }
  getAllActions() { return this.order.map(id => this.actions.get(id)); }
  actionHasProperty(id, k) { const a = this.actions.get(id); return !!a && k in a; }
  executeAction() { return false; }
  addActionCallback() {}
  addActionChangeCallback() {}
  removeActionChangeCallback() {}
  removeActionCallback() {}
  on() { return { remove: () => {} }; }
  destroy() {}
}
stub('mw-actiondataservice/ActionDataService', ActionDataService);

stub('dojo/_base/lang', {
  mixin: (d, ...s) => Object.assign(d || {}, ...s),
  clone: x => JSON.parse(JSON.stringify(x)),
  hitch: (ctx, fn, ...a) => (typeof fn === 'string' ? ctx[fn] : fn).bind(ctx, ...a),
  isFunction: f => typeof f === 'function',
  isArray: Array.isArray,
});
stub('dojo/_base/declare', function (bases, props) {
  if (!Array.isArray(bases) && typeof bases !== 'function') { props = bases; bases = []; }
  const C = function (...a) { if (props && props.hasOwnProperty('constructor')) props.constructor.apply(this, a); };
  Object.assign(C.prototype, props || {});
  return C;
});
stub('dojo/on', (target, type, fn) => (target && target.on ? target.on(type, fn) : { remove: () => {} }));
stub('dojo/topic', { publish() {}, subscribe() { return { remove() {} }; } });
stub('dojo/string', { substitute(t, map) { return String(t).replace(/\$\{([^}]+)\}/g, (_, k) => map[k]); }, trim: s => s.trim() });

const services = new Proxy({}, { get: (_, k) => k });
stub('appdesigner/application/AppDesignerServiceProvider', {
  services,
  getService: name => anything('service.' + String(name)),
});

// Families the actions reach only inside callbacks or listeners (commands, views, flux, dialogs, remote
// calls), and any module that is not in this bundle.
stubWhere(n => /^appdesigner\/command\/|^appdesigner\/view\/(?!toolstrip\/|documenttypeconfigs\/|UIContainerConstants$|editor\/plugins\/toolstrip\/AppDesignerExecutionControlsToolstripTags)|^appdesigner\/flux\/|^mw-remote\/|^MW\/(?!toolstrip\/constants\/)/.test(n), n => anything(n));
stubWhere(n => /^(dijit|dojox|mw-notifications|mw-dialogs|mw-webwindow|mw-webwindow-dialogs|appdesigner_interface\/dialog|appdesigner_interface\/componentlibrary|equationrenderercore|gbtshared|visualcomponents|mw-form|mw-colorpicker|mw-datepicker)\//.test(n), n => anything(n));
// The settings service records each setting an action reads; every read answers no value.
const settingsRead = [];
stub('settings/settingsService', new Proxy({}, { get: (_, k) => (...a) => { settingsRead.push({ call: String(k), args: JSON.parse(JSON.stringify(a.filter(x => typeof x !== 'function'))) }); return Promise.resolve(undefined); } }));
stubWhere(n => /^rtc\/(?!RichTextComponentFeatureEnum$|resources\/)|^settings\/|^mw-messageservice\/|^mw-log\//.test(n), n => anything(n));
stubWhere(n => /^dojo\//.test(n) && !n.startsWith('dojo/i18n!'), n => anything(n));
stubWhere(n => !n.startsWith('dojo/i18n!') && moduleText(n) === null, n => anything(n));

// The app model the actions and tabs are handed: no app open, sharing buttons shown.
function makeAppDesignerModel() {
  const m = new Evented();
  m.getAppModels = () => [];
  m.activeAppModel = null;
  m.getViewState = () => ({ ShowSharingButtons: true, ShowCompilerButtons: true, ShowWebAppButtons: true });
  return new Proxy(m, { get: (t, k) => (k in t ? t[k] : anything('appDesignerModel.' + String(k))) });
}

module.exports = { settingsRead, anything, Evented, ActionDataService, makeAppDesignerModel, load };

// A module outside what the oracle records that cannot run under node (DOM, the message service) is
// replaced by anything() and listed in the report's fellBack.
require('./amd').setFallback(n => !/^appdesigner\/(action|view\/toolstrip|view\/documenttypeconfigs)\/|^appdesigner\/view\/UIContainerConstants$|\/nls\/|^rtc\/RichTextComponentFeatureEnum$|^MW\/toolstrip\/constants\//.test(n), n => anything(n));
// A stubbed service's promise that rejects after the oracle has written its output is not the oracle's.
process.on('unhandledRejection', () => {});
