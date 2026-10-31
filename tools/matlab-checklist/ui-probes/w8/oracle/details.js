// What R2025b's App Designer does with App Details and an app's help text, from its own code run unmodified - for the
// App Designer window plan's stage W8a, whose DesignAppDetails and DesignHelpText transcribe it:
//   node details.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignAppDetailsOracle.json
//
// The version and the image's extension: appdesigner/util/AppDetailsUtil loaded whole.
// The help text's lines: appdesigner/codegen/util/CodeFormatter loaded whole and appdesigner/codegen/builders/ClassBuilder's
// _resetStartLines run over it; the description the dialog holds, HelpCommentsDialogController's _handleOkButton cut out as it is.
// App Details' OK: AppDetailsDialogController's _handleOkButton and _hasMetaDataContentChanged cut out as they are, over
// stand-ins for the dialog's fields, with the real AppDetailsUtil and CodeEditUtil.
'use strict';
const path = require('path');
const { load, stub, stubWhere, moduleText } = require(path.join(__dirname, '..', '..', 'w0', 'oracle', 'amd.js'));

class Evented { on() { return { remove() {} }; } emit() {} }
stub('mw-event-emitter/Evented', Evented);
stub('dojo/_base/lang', { mixin: (d, ...s) => Object.assign(d || {}, ...s), clone: x => JSON.parse(JSON.stringify(x)), isFunction: f => typeof f === 'function' });
stub('dojo/string', { substitute: (t, m) => String(t).replace(/\$\{([^}]+)\}/g, (_, k) => m[k]), trim: s => s.trim() });
stub('appdesigner/application/AppDesignerServiceProvider', { services: new Proxy({}, { get: (_, k) => k }), getService: () => null });
stub('mw-filename-utils/FileNameUtil', { getFileNameExtension: f => { const i = f.lastIndexOf('.'); return i < 0 ? '' : f.slice(i + 1); } });
stub('appdesigner/codegen/builders/_Builder', class {
  constructor() { this.data = { startLines: [], endLines: [], tabDepth: 0, readOnly: true, isVisible: true }; this.codeFormatter = new (load('appdesigner/codegen/util/CodeFormatter'))(); }
  init() {}
  triggerDirty() {}
});
stub('appdesigner/codegen/util/BuilderState', { CLEAN: 0, DIRTY: 1 });
stubWhere(n => /^(dijit|dojox|dojo\/(?!i18n)|mw-(?!filename)|appdesigner_interface|appdesigner\/(view|command|application|model)\/)/.test(n) && !/nls/.test(n), () => function () {});

function rng(seed) { let s = seed >>> 0; return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; }; }
const random = rng(20261031);
const pick = a => a[Math.floor(random() * a.length)];

// A method as its module has it, with the module's import names as closure parameters.
function method(module, name, parameters) {
  const text = moduleText(module);
  const found = new RegExp(name + '(:function)?\\([^()]*\\)\\{').exec(text);
  if (!found) throw new Error('no ' + name);
  const head = found.index;
  const open = text.indexOf('{', text.indexOf(')', head));
  let depth = 0;
  let i = open;
  let quote = null;
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

const util = load('appdesigner/util/AppDetailsUtil');
const codeEdit = load('appdesigner/util/CodeEditUtil');
const Formatter = load('appdesigner/codegen/util/CodeFormatter');
const ClassBuilder = load('appdesigner/codegen/builders/ClassBuilder');

const versions = ['', '1', '1.', '1.2', '12.34', '1.2.3', '.5', '5.', 'abc', ' 3.0', '3.0 ', '3.0 x', '٣.٠', '1,2', '01.02', '0.0', '1..2', '-1.0', '9', '99', '99.', '99.9'];
for (let n = 0; n < 200; n++) {
  let t = '';
  const len = Math.floor(random() * 6);
  for (let k = 0; k < len; k++) t += pick(['0', '1', '7', '.', '.', 'a', ' ']);
  versions.push(t);
}
const versionRows = [...new Set(versions)].map(v => ({ v, bad: util.validateVersion(v), temp: !!util.isTemporaryVersionValid(v) }));

const images = ['a.png', 'a.PNG', 'a.jpg', 'a.jpeg', 'a.gif', 'a.Gif', 'a.bmp', 'a.tif', 'a', 'a.png.txt', 'a.b.jpeg', '.png', 'C:\\x y\\pic.JPG'];
const imageRows = images.map(f => ({ f, bad: util.validateScreenshotExtension(f) }));

const words = ['a', 'bb', 'ccc', 'word', 'longerword', 'averyveryverylongwordthatgoesonandonandonandonandonandonandonandonandonandonandonandonandon', 'x y', ' ', '  ', '\t'];
function text(limit, lines) {
  const out = [];
  for (let l = 0; l < lines; l++) {
    let line = '';
    const n = Math.floor(random() * limit);
    for (let k = 0; k < n; k++) line += (k > 0 ? ' ' : '') + pick(words);
    out.push(line);
  }
  return out.join('\n');
}

const formatter = new Formatter({ IndentSize: 4 });
const wrapRows = [];
for (let n = 0; n < 300; n++) {
  const comment = pick(['%', '% ', '%   ', '', '%CLASS ']) + text(30, 1 + Math.floor(random() * 3));
  const limit = pick([71, 73, 67, 40, 75]);
  wrapRows.push({ comment, limit, lines: formatter.wrapComment(comment, limit) });
}

// ClassBuilder's lines at the class's depth, for indents of 2, 3 and 4.
const lineRows = [];
for (let n = 0; n < 300; n++) {
  const indent = pick([2, 3, 4]);
  const className = pick(['app1', 'MyApp', 'A_b2', 'W4App']);
  const summary = pick(['', 'One line', text(25, 1)]);
  const description = pick(['', 'A description', text(20, 1 + Math.floor(random() * 4))]);
  const f = new Formatter({ IndentSize: indent });
  const b = Object.create(ClassBuilder.prototype);
  b.codeFormatter = f;
  b.data = { startLines: [], endLines: [], tabDepth: 0, readOnly: true, isVisible: true, className, superClassNameList: ['matlab.apps.AppBase'], summary, description };
  b.state = 1;
  b._resetStartLines();
  const lines = f.generateFormatting(b.data.startLines).map(l => l.formattedText).slice(1);
  lineRows.push({ indent, className, summary, description, lines });
}

// The description the dialog's OK holds.
const okHelp = method('appdesigner/view/dialogs/helpcomments/HelpCommentsDialogController', '_handleOkButton', ['E', 'p', 'c', 'd', 'o', 's', 'i', 'e', 'n', 'r']);
const wrappedRows = [];
for (let n = 0; n < 200; n++) {
  const indent = pick([2, 3, 4]);
  const description = text(25, 1 + Math.floor(random() * 4));
  const f = new Formatter({ IndentSize: indent });
  let result = null;
  const fn = okHelp(null, null, null, null, null, { executeCommand: c => { result = c; } }, function (m, a, b) { this.a = a; this.b = b; }, null, null, Formatter);
  fn.call({ _helpCommentsDialogContent: { getSummary: () => 's', getDescription: () => description }, _codeFormatter: f, _codeModel: {}, _appModel: {} });
  wrappedRows.push({ indent, description, wrapped: result.b });
}

// App Details' OK: what its command is handed.
const ok = method('appdesigner/view/dialogs/appdetails/AppDetailsDialogController', '_handleOkButton', ['E', 'p', 'c', 'd', 'o', 's', 'i', 'e', 'n', 'r', 'a', 'l', 'u', 'g', 'C', 'A']);
const changed = method('appdesigner/view/dialogs/appdetails/AppDetailsDialogController', '_hasMetaDataContentChanged', ['E', 'p', 'c', 'd', 'o', 's', 'i', 'e', 'n', 'r', 'a', 'l', 'u', 'g', 'C', 'A']);
const okRows = [];
for (let n = 0; n < 400; n++) {
  const initial = { Name: 'App', Author: pick(['', 'Me']), Version: pick(['1.0', '1.2.3']), Summary: '', Description: pick(['', 'D']), ScreenshotMode: pick(['auto', 'manual', 'none']), Arguments: pick(['', 'a, b']), Singleton: pick(['NONE', 'FOCUS']) };
  const typed = {
    Name: pick([initial.Name, 'Other']), Author: pick([initial.Author, 'You']), Version: pick([initial.Version, '2.5', 'x', '3', '']), Summary: pick([initial.Summary, 'S']), Description: pick([initial.Description, 'E']),
    ScreenshotMode: pick([initial.ScreenshotMode, 'auto', 'manual', 'none']), Arguments: pick([initial.Arguments, 'x, y', '1a', 'x, x', '']), Singleton: pick([initial.Singleton, 'FOCUS', 'NONE']), Plain: random() < 0.5, Picked: random() < 0.3,
  };
  let handed = null;
  const self = {
    _initialName: initial.Name, _initialAuthor: initial.Author, _initialVersion: initial.Version, _initialSummary: initial.Summary, _initialDescription: initial.Description, _initialScreenshotMode: initial.ScreenshotMode,
    _initialInputArguments: initial.Arguments, _initialSingletonMode: initial.Singleton, _screenshotMode: typed.Picked ? 'manual' : initial.ScreenshotMode, _selectedScreenshotPath: typed.Picked ? 'p.png' : null,
    _appDetailsDialogContent: {
      getName: () => typed.Name, getAuthor: () => typed.Author, getVersion: () => typed.Version, getSummary: () => typed.Summary, getDescription: () => typed.Description,
      getScreenshotMode: typed.Plain ? () => typed.ScreenshotMode : undefined,
    },
    _appDetailsDialogCodeContent: { getInputArguments: () => typed.Arguments, getSingletonMode: () => typed.Singleton },
    _appModel: { getUndoRedoManager: () => ({ addCommand() {} }) },
  };
  self._hasMetaDataContentChanged = changed(null, null, null, null, null, null, null, null, null, null, util, codeEdit, null, null, null, null);
  const fn = ok(null, null, null, null, null, null, null, null, null, null, util, codeEdit, null, null, null, function (m, s) { handed = s; this.execute = () => {}; });
  fn.call(self);
  okRows.push({ initial, typed, handed });
}

process.stdout.write(JSON.stringify({ versions: versionRows, images: imageRows, wraps: wrapRows, lines: lineRows, wrapped: wrappedRows, ok: okRows }, null, 0));
