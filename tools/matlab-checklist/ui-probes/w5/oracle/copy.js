// How R2025b's App Designer names copies and finds them cells in a grid, from its own modules run
// unmodified (appdesigner/model/codenamegenerator/CopyCodeNameGenerator over its
// AbstractCodeNameGenerator and hmishared/util/UniqueNameGenerator, and appdesigner/util/GridUtil's
// findLocation) over seeded-random scenes - for the App Designer window plan's stage W5, whose
// DesignDocument.Clipboard transcribes them:
//   node copy.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignClipboardOracle.json
// A component info is what the paste commands hand the generator: Type, PropertyValues (CodeName,
// a label's Text), Children, SourcePeerNodeId; a group is Type "Group" with Members. A grid peer
// node holds RowHeight, ColumnWidth and children with LayoutConstraints.
'use strict';
const { load, stub } = require('../../w0/oracle/amd');

// dojo's lang as the two modules use it (clone, mixin), and no DOM geometry, which findLocation does not reach.
stub('dojo/_base/lang', { clone: v => (v === undefined ? v : JSON.parse(JSON.stringify(v))), mixin: Object.assign });
stub('dojo/dom-geometry', {});

const CopyCodeNameGenerator = load('appdesigner/model/codenamegenerator/CopyCodeNameGenerator');
const GridUtil = load('appdesigner/util/GridUtil');

let seed = 20261029;
function rand() {
  seed = (seed * 1103515245 + 12345) % 2147483648;
  return seed / 2147483648;
}
const int = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));
const pick = list => list[Math.floor(rand() * list.length)];

// The classes the scenes use, with App Designer's default code names and texts.
const infos = {
  'matlab.ui.control.Button': { CodeNameGeneration: { DefaultCodeName: 'Button', DefaultText: 'Button' } },
  'matlab.ui.control.EditField': { CodeNameGeneration: { DefaultCodeName: 'EditField', DefaultText: 'Edit Field' } },
  'matlab.ui.control.NumericEditField': { CodeNameGeneration: { DefaultCodeName: 'EditField', DefaultText: 'Edit Field' } },
  'matlab.ui.control.Slider': { CodeNameGeneration: { DefaultCodeName: 'Slider', DefaultText: 'Slider' } },
  'matlab.ui.control.Label': { CodeNameGeneration: { DefaultCodeName: 'Label', DefaultText: 'Label' } },
  'matlab.ui.container.Panel': { CodeNameGeneration: { DefaultCodeName: 'Panel', DefaultText: 'Panel' } },
};
const fieldTypes = ['matlab.ui.control.EditField', 'matlab.ui.control.NumericEditField', 'matlab.ui.control.Slider'];
const plainTypes = ['matlab.ui.control.Button', 'matlab.ui.control.Button', 'matlab.ui.control.Slider', 'matlab.ui.control.Label'];

let nextId = 0;
const bases = ['Button', 'Button_2', 'Button2', 'Button_12', 'myButton', 'my_btn_3', 'Slider', 'EditField', 'EditField_2', 'Label', 'Panel', 'x_1_2', 'A'];

function component(type, name) {
  const c = { Type: type, PropertyValues: { CodeName: name }, Children: [], SourcePeerNodeId: 'p' + nextId++ };
  if (type === 'matlab.ui.control.Label') c.PropertyValues.Text = ['Label'];
  return c;
}

function labeled() {
  const type = pick(fieldTypes);
  const def = infos[type].CodeNameGeneration;
  const n = pick(['', '2', '3', '_2', '7']);
  const handNamed = rand() < 0.3;
  const field = component(type, handNamed ? pick(['amount', 'Amount_4', 'speed', 'nameField']) : def.DefaultCodeName + n.replace('_', ''));
  const label = component('matlab.ui.control.Label', handNamed ? pick(['amountLabel', 'Label_3', 'speedLabel']) : field.PropertyValues.CodeName + 'Label');
  // A label's Text is a list of lines, as its peer node holds it.
  label.PropertyValues.Text = [pick([def.DefaultText + n.replace('_', ''), 'Amount', 'Speed (m/s)', def.DefaultText, '12 ' + def.DefaultText])];
  return { Type: 'Group', Members: rand() < 0.8 ? [label, field] : [field, label] };
}

function tree(depth) {
  const c = component(rand() < 0.25 && depth < 2 ? 'matlab.ui.container.Panel' : pick(plainTypes), pick(bases));
  if (c.Type === 'matlab.ui.container.Panel') {
    for (let i = int(0, 3); i > 0; i--) c.Children.push(tree(depth + 1));
  }
  return c;
}

const cases = [];
for (let i = 0; i < 800; i++) {
  nextId = 0;
  const top = [];
  for (let k = int(1, 5); k > 0; k--) {
    const r = rand();
    if (r < 0.25) top.push(labeled());
    else if (r < 0.35) top.push({ Type: 'Group', Members: [tree(1), tree(1)] });
    else top.push(tree(0));
  }
  const inUse = [];
  for (let k = int(0, 8); k > 0; k--) inUse.push(pick(bases.concat(['Button_3', 'EditField_5', 'Label_2', 'amount_2', 'ButtonLabel', 'EditField2Label'])));
  const before = JSON.parse(JSON.stringify(top));
  const names = new CopyCodeNameGenerator(top, inUse.slice(), infos, 63).generateCodeNameData();
  cases.push({ op: 'names', components: before, inUse, names: names.map(n => [n.PeerNodeId, n.PropertyValues.CodeName]) });
}

// findLocation(groups, components, grid, row, column): each component's LayoutConstraints and GroupId.
for (let i = 0; i < 800; i++) {
  const rows = int(1, 5), columns = int(1, 5);
  const children = [];
  for (let k = int(0, 5); k > 0; k--) {
    const r = int(1, rows), c = int(1, columns);
    const r2 = Math.min(rows, r + (rand() < 0.3 ? 1 : 0)), c2 = Math.min(columns, c + (rand() < 0.3 ? 1 : 0));
    children.push({ LayoutConstraints: { Type: 'Grid', Row: r2 > r ? [r, r2] : r, Column: c2 > c ? [c, c2] : c } });
  }
  const grid = {
    getProperty(name) { return name === 'RowHeight' ? new Array(rows).fill('1x') : new Array(columns).fill('1x'); },
    getChildren() { return children.map(ch => ({ getProperty: () => ch.LayoutConstraints })); },
  };
  const groups = [];
  const components = [];
  for (let k = int(1, 4); k > 0; k--) {
    if (rand() < 0.4) {
      components.push({ PropertyValues: { LayoutConstraints: { Type: 'Absolute' } } });
      continue;
    }
    const grouped = rand() < 0.35;
    const id = grouped ? 'g' + groups.length : '';
    if (grouped) groups.push({ Id: id, IsLabeledComponent: rand() < 0.7 });
    for (let m = grouped ? 2 : 1; m > 0; m--) {
      const r = int(1, 4), c = int(1, 4);
      const tall = rand() < 0.3, wide = rand() < 0.3;
      components.push({ GroupId: id || undefined, PropertyValues: { LayoutConstraints: { Type: 'Grid', Row: tall ? [r, r + 1] : r, Column: wide ? [c, c + 1] : c } } });
    }
  }
  const start = rand() < 0.4 ? [int(1, rows), int(1, columns)] : [undefined, undefined];
  const found = GridUtil.findLocation(groups, JSON.parse(JSON.stringify(components)), grid, start[0], start[1]);
  cases.push({
    op: 'cells', rows, columns, occupied: children.map(ch => ch.LayoutConstraints), groups, components, start,
    found: found && found.map(f => [f.row, f.column]),
  });
}

process.stdout.write(JSON.stringify({
  source: 'R2025b appdesigner/model/codenamegenerator/CopyCodeNameGenerator and appdesigner/util/GridUtil.findLocation, run by ui-probes/w5/oracle/copy.js',
  cases,
}) + '\n');
