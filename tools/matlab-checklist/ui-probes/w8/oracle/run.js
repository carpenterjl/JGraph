// What R2025b's App Designer does with an app's argument sets, from its own code run unmodified - for the App Designer
// window plan's stage W8b, whose DesignRunArguments and DesignToolstrip.RunEntries transcribe it:
//   node run.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignRunArgumentsOracle.json
//
// The sets: appdesigner/model/RunArgumentsModel loaded whole, over a stand-in peer node, through 600 runs of 1 to 25
// operations. The drop-down: appdesigner/view/toolstrip/RunSection's _buildPopupList, _buildRunArgumentItem and
// _buildRunEditableArgumentItem cut out as they are, over a stand-in toolstrip builder that records what they create.
'use strict';
const path = require('path');
const { load, stub, stubWhere, moduleText } = require(path.join(__dirname, '..', '..', 'w0', 'oracle', 'amd.js'));

class Evented { on() { return { remove() {} }; } emit() {} }
stub('mw-event-emitter/Evented', Evented);

function rng(seed) { let s = seed >>> 0; return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; }; }
const random = rng(20261031);
const pick = a => a[Math.floor(random() * a.length)];
const sets = ['', '1', '2', '3', '1, 2', "'a'", 'x', 'y', 'z', 'a, b', 'magic(3)', ' 1', '[1 2]'];

const Model = load('appdesigner/model/RunArgumentsModel');
const runs = [];
for (let n = 0; n < 600; n++) {
  const dirty = { synced: 0 };
  const start = random() < 0.2 ? [''] : pick([[''], ['1', ''], ['a, b', '1', ''], ['1', '2', '3', '4', '5', '6', '7', '']]);
  const peer = { getProperty: () => start, setProperty: () => { dirty.synced++; } };
  const model = new Model(peer);
  const ops = [];
  const count = 1 + Math.floor(random() * 25);
  for (let k = 0; k < count; k++) {
    const set = pick(sets);
    const was = dirty.synced;
    if (random() < 0.75) {
      model.setArgumentSet(set);
      ops.push({ op: 'use', set, changed: dirty.synced !== was, all: model.getAllArguments().slice(), recent: model.getMostRecentArgumentSet() });
    } else {
      model.removeArgumentSet(set);
      ops.push({ op: 'remove', set, all: model.getAllArguments().slice(), recent: model.getMostRecentArgumentSet() });
    }
  }
  runs.push({ start, ops });
}

// The drop-down.
function method(module, name, parameters) {
  const text = moduleText(module);
  const found = new RegExp(name + '(:function)?\\([^()]*\\)\\{').exec(text);
  if (!found) throw new Error('no ' + name);
  const head = found.index;
  const open = text.indexOf('{', text.indexOf(')', head));
  let depth = 0; let i = open; let quote = null;
  for (; i < text.length; i++) {
    const c = text[i];
    if (quote) { if (c === '\\') i++; else if (c === quote) quote = null; continue; }
    if (c === '"' || c === "'" || c === '`') { quote = c; continue; }
    if (c === '{') depth++;
    if (c === '}' && --depth === 0) break;
  }
  const args = text.slice(text.indexOf('(', head) + 1, text.indexOf(')', head));
  return new Function(...parameters, `return function(${args})${text.slice(open, i + 1)};`);
}
const P = ['E', 'p', 'c', 'd', 'o', 's', 'i', 'e', 'n', 'r', 'a', 'l', 'u', 'g', 'C'];
const Module = 'appdesigner/view/toolstrip/RunSection';
const types = { POPUP_LIST_PANEL: 'panel', LIST_ITEM: 'item', LIST_ITEM_WITH_TEXTFIELD: 'field', POPUP_LIST_SEPARATOR: 'separator' };
const words = { runButtonLabel: 'Run' };
const tags = { RUN_SECTION_RUN_WITH_ARGUMENTS_PANEL_TAG: 'panel', RUN_SECTION_RUN_WITH_ARGUMENTS_LIST_ITEM_TAG: 'listItem' };
const scope = [null, types, null, null, words, null, { INPUT_ARGUMENTS: 'AD_INPUT_ARGUMENTS' }, null, { RUN: 'run' }, tags, null, null, null, null, null];
const build = method(Module, '_buildPopupList', P)(...scope);
const item = method(Module, '_buildRunArgumentItem', P)(...scope);
const field = method(Module, '_buildRunEditableArgumentItem', P)(...scope);

const menus = [];
for (let n = 0; n < 300; n++) {
  const name = pick(['app1', 'MyApp', 'W8bApp']);
  const held = [];
  const count = Math.floor(random() * 8);
  for (let k = 0; k < count; k++) { const s = pick(sets); if (s !== '' && !held.includes(s)) held.push(s); }
  held.push('');
  const parameters = pick(['', 'a', 'a, b']);
  const error = random() < 0.2 ? 'Undefined function or variable' : null;
  const created = [];
  let id = 0;
  const widget = { domNode: { insertBefore() {}, children: [] }, inputNode: { addEventListener() {} } };
  const builder = {
    create(o) { created.push(Object.assign({ id: ++id }, o)); return id; },
    add() {},
    addEventCallback() { return { remove() {} }; },
    widgetByTag: () => widget,
    set(i, k, v) { const c = created.find(x => x.id === i); c[k] = v; },
  };
  global.document = { createElement: () => ({ classList: { add() {} } }) };
  const model = { getName: () => name, getArgumentModel: () => ({ getAllArguments: () => held, getMostRecentArgumentSet: () => held[0] }), getCodeModel: () => ({ getInputArgumentNames: () => parameters }) };
  const self = { _ToolstripBuilder: builder, _PopupListeners: [], _buildAndShowPopup() {}, _buildRunArgumentItem: item, _buildRunEditableArgumentItem: field };
  build.call(self, model, 'run', 'popup', error);
  menus.push({ name, held, parameters, error, created: created.filter(c => c.type !== 'panel' && c.type !== 'separator').map(c => ({ type: c.type, text: c.text, label: c.label, removable: c.removable, enabled: c.enabled, placeholder: c.placeholderText, action: c.actionId, field: c.text, errorText: c.errorText, state: c.validationState })) });
}

process.stdout.write(JSON.stringify({ runs, menus }, null, 0));
