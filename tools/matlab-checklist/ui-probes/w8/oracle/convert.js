// What R2025b's App Designer does when it converts an app, from its own code run unmodified - for the App Designer window plan's
// stage W8c, whose DesignConversion transcribes it:
//   node convert.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignConversionOracle.json
//
// appdesigner/util/AppDataCopyUtil loaded whole, with its ResponsiveAppTemplateData and PositionUtil, over stand-ins for the factories and the app models:
// the names (_createAppName), the shares (getRegionPanelDimensionRatios), the regions' boxes (resizeRegionPanels), which region (getComponentsWithinBounds), and clampToParent.
'use strict';
const path = require('path');
const { load, stub, stubWhere } = require(path.join(__dirname, '..', '..', 'w0', 'oracle', 'amd.js'));

function clone(x) { return x === undefined ? x : JSON.parse(JSON.stringify(x)); }
stub('dojo/_base/lang', { mixin: (d, ...s) => Object.assign(d || {}, ...s), clone, hitch: (c, f, ...a) => (typeof f === 'string' ? c[f] : f).bind(c, ...a) });
stub('mw-event-emitter/Evented', class { on() { return { remove() {} }; } emit() {} });
const stand = n => function () {};
for (const n of ['appdesigner/application/AppCreator', 'appdesigner/model/factory/ResponsiveAppFactory', 'appdesigner/view/factory/ResponsiveAppViewControllerFactory', 'appdesigner/model/codegen/CallbackModel', 'appdesigner/model/codegen/CallbackBinding', 'appdesigner/flux/intent/AppIntentCreator', 'hmishared/util/UniqueNameGenerator', 'mw-utils/Utils', 'appdesigner/util/MenuUtil', 'appdesigner/util/CopyPasteUtil', 'appdesigner/model/SingletonModeEnum', 'appdesigner/model/OutputModeEnum', 'appdesigner/util/AppDetailsUtil', 'appdesigner/model/fileformat/FileFormatFactory', 'appdesigner/model/fileformat/FileFormatOptions', 'mw-log/Log', 'appdesigner/util/FigureUtil', 'appdesigner/manifest/PluginManifestsKey', 'appdesigner/model/codegen/FunctionTypes']) stub(n, n.endsWith('FigureUtil') ? { getFigureType: () => 'matlab.ui.Figure' } : stand(n));
stub('appdesigner/view/canvas/layouttemplate/LayoutTemplateConstants', { TWO_COLUMN_TEMPLATE: 'two', THREE_COLUMN_TEMPLATE: 'three' });
stub('appdesigner/util/GridUtil', {});
stubWhere(n => (/^(dijit|dojox|mw-|appdesigner_interface)/.test(n) || (n.startsWith('dojo/') && !n.startsWith('dojo/i18n!'))) && !/nls/.test(n), () => stand());

const util = load('appdesigner/util/AppDataCopyUtil');
const position = load('appdesigner/util/PositionUtil');

function rng(seed) { let s = seed >>> 0; return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; }; }
const random = rng(20261031);
const pick = a => a[Math.floor(random() * a.length)];
const int = (a, b) => a + Math.floor(random() * (b - a + 1));

const names = [];
for (const name of ['app1', 'app1_converted', 'app1_converted2', 'app1_converted12', 'my_converted_app', 'a_converted3b', 'x']) {
  for (const open of [[], ['app1_converted'], ['app1_converted', 'app1_converted2'], ['x_converted'], ['my_app_converted'], ['a_converted3b_converted']]) {
    const models = open.map(o => ({ getName: () => o }));
    names.push({ name, open, made: util._createAppName({ getName: () => name }, models, '_converted') });
  }
}

const ratios = { two: util.getRegionPanelDimensionRatios('two'), three: util.getRegionPanelDimensionRatios('three') };

const boxes = [];
for (let n = 0; n < 400; n++) {
  const kind = pick(['two', 'three']);
  const size = [int(200, 2000), int(100, 1500)];
  const panels = (kind === 'two' ? [0, 1] : [0, 1, 2]).map(() => {
    const p = { Size: [0, 0] };
    return { p, getProperty: k => p[k], setProperties(o) { Object.assign(p, o); } };
  });
  util.resizeRegionPanels(panels, ratios[kind], size);
  boxes.push({ kind, size, boxes: panels.map(x => [x.p.Location[0], x.p.Size[0], x.p.Size[1]]) });
}

const within = [];
for (let n = 0; n < 600; n++) {
  const width = int(200, 1500);
  const a = pick([0, 0.25, 221 / 640, 221 / 860]);
  const b = pick([1, 641 / 860, 0.5]);
  const box = [int(-50, width + 50), int(1, 300), int(1, 300), int(1, 100)];
  const found = util.getComponentsWithinBounds([{ PropertyValues: { Position: box } }], a, b, width).length === 1;
  within.push({ width, a, b, box, found });
}

const clamps = [];
for (let n = 0; n < 800; n++) {
  const at = [int(-200, 900), int(-200, 900)];
  const size = [int(1, 900), int(1, 900)];
  const parent = [int(10, 700), int(10, 700)];
  const r = position.clampToParent(at, size, { getProperty: () => parent });
  clamps.push({ at, size, parent, position: r.position });
}

process.stdout.write(JSON.stringify({ names, ratios, boxes, within, clamps }, null, 0));
