// appendix.json: App Designer's plain-text (.m) file writer - the class part (PlainTextClassBehavior) and the
// %[appendix] (XMLAppendixBehavior and its sections) - rendered by R2025b's UNMODIFIED modules over a stub
// CodeModel. Run: node appendix.js
//
// What is stubbed (beyond ds5's stubs.js, copied here unchanged):
//   - the CodeModel's fileFormat is the real TextFileFormat (so AppFileBehavior -> PlainTextFileContentGroupBehavior,
//     ClassBehavior -> PlainTextClassBehavior, superclass matlab.apps.App, callback access undefined);
//   - component view models: ds5's makePeerNode + DirtyProps + isGroup()/isGroupMember();
//   - getMetaDataModel(): an Evented over a plain map (getProperty/setProperty emit '<Name>Change');
//   - getWidgetDataManager(): an Evented whose getGroupManagerPeerNode() holds the canvas-group tree;
//   - getContextMenuForComponent(vm): finds the context menu view model named by vm's ContextMenu dirty prop;
//   - getId(): the CodeModel id used by the singleton builder's id.
'use strict';
const fs = require('fs');
const path = require('path');
const { makeCodeModel, makePeerNode, collectLines, loadRegistry, load, Evented } = require('./stubs');
const { report } = require('./amd');

const GroupBuilder = load('appdesigner/codegen/builders/GroupBuilder');
const TextFileFormat = load('appdesigner/model/fileformat/TextFileFormat');
const CallbackModel = load('appdesigner/model/codegen/CallbackModel');
const CallbackBinding = load('appdesigner/model/codegen/CallbackBinding');
const FunctionTypes = load('appdesigner/model/codegen/FunctionTypes');
const FigureUtil = load('appdesigner/util/FigureUtil');
const SingletonModeEnum = load('appdesigner/model/SingletonModeEnum');
const XMLElementBuilder = load('appdesigner/codegen/builders/XMLElementBuilder');

// ---- view models -----------------------------------------------------------------------------------------
function P(Name, Value) { return { Name, Value }; }
function vm(codeName, type, dirty, children, extra) {
  const n = makePeerNode(codeName, type);
  n._props.DirtyProps = dirty || [];
  n._props.GroupId = (extra && extra.groupId) || '';
  n.isGroup = () => false;
  n.isGroupMember = () => !!n._props.GroupId;
  (children || []).forEach(c => { n._children.push(c); c.getParent = () => n; });
  return n;
}
function flatten(n, out) { out.push(n); n.getChildren().forEach(c => flatten(c, out)); return out; }

// ---- canvas groups ---------------------------------------------------------------------------------------
function group(id, members, labeled) {
  const g = new Evented();
  g._props = { GroupId: '', LabeledComponent: !!labeled, Members: members.map(m => m.getId()) };
  g._id = id;
  g._members = members;
  g.getId = () => id;
  g.getType = () => 'Group';
  g.isGroup = () => true;
  g.isGroupMember = () => !!g._props.GroupId;
  g.getProperty = k => g._props[k];
  g.getMemberPeerNodes = () => g._members;
  g.getChildren = () => g._members.filter(m => m.isGroup());
  members.forEach(m => { m._props.GroupId = id; });
  return g;
}
function groupManager(topGroups) {
  const gm = new Evented();
  gm.getChildren = () => topGroups;
  gm.getDescendant = id => {
    const walk = list => { for (const g of list) { if (g.getId() === id) return g; const r = walk(g.getChildren()); if (r) return r; } return undefined; };
    return walk(topGroups);
  };
  return gm;
}

// ---- metadata ---------------------------------------------------------------------------------------------
function metaModel(values) {
  const m = new Evented();
  m._v = Object.assign({ Name: '', Author: '', Version: '', Summary: '', Description: '' }, values || {});
  m.getProperty = k => m._v[k];
  m.getMetaData = () => Object.assign({}, m._v);
  m.setProperty = (k, v) => { const old = m._v[k]; m._v[k] = v; m.emit(k + 'Change', { oldValue: old, newValue: v }); };
  return m;
}

// ---- code model -----------------------------------------------------------------------------------------
// o: makeCodeModel options + { fig, meta, groups, singleton, startup, inputParameters, callbacks, custom }
function appModel(o) {
  const cm = makeCodeModel(Object.assign({ figureCodeName: o.fig.getProperty('CodeName') }, o));
  cm._fileFormat = new TextFileFormat();
  cm._superClass = cm._config.createSuperClassStrategy(cm._fileFormat);
  cm.fig = o.fig;
  const all = flatten(o.fig, []);
  cm._componentProperties = all.map(n => ({ Id: n.getId(), Name: n.getProperty('CodeName'), ComponentType: n.getType(), Component: n }));
  cm._meta = metaModel(o.meta);
  cm.getMetaDataModel = () => cm._meta;
  const wdm = new Evented();
  const gm = groupManager(o.groups || []);
  wdm.getGroupManagerPeerNode = () => gm;
  cm.getWidgetDataManager = () => wdm;
  cm.getContextMenuForComponent = v => {
    const d = (v.getProperty('DirtyProps') || []).find(x => x.Name === 'ContextMenu');
    return all.find(n => d && n.getProperty('CodeName') === d.Value) || new Evented();
  };
  cm.getId = () => 'codemodel';
  cm.setSingletonMode = v => { const old = cm._singleton; cm._singleton = v; cm.emit('singletonChanged', { oldValue: old, newValue: v }); };
  if (o.custom) cm.getCustomCodeModel().loadCustomCode(o.custom);
  (o.callbacks || []).forEach(c => {
    const registry = loadRegistry();
    const model = new CallbackModel(Object.assign({ CallbackType: FunctionTypes.ComponentCallback, GeneratedName: c.name, Name: c.name, DefaultCode: [''], ComponentInfo: registry, Order: -1, Arguments: ['app', 'event'] }, c.model || {}));
    const target = all.find(n => n.getProperty('CodeName') === c.component);
    model.getBindings().addBinding(new CallbackBinding(target, model, c.prop));
    cm.getCallbackCollection().addCallback(model);
  });
  return cm;
}

function renderFile(cm, after) {
  const root = new GroupBuilder(cm, { behavior: cm.getCodeEngineConfig().get('AppFileBehavior', cm.fileFormat), tabDepth: 0, id: 'builder-root' });
  // The component property block learns the non-figure components the way CodeModel tells it on load.
  const rest = cm._componentProperties.slice(1).map(p => p.Component);
  if (rest.length) cm.emit('appPropertiesUpdated', { type: FigureUtil.componentOperationTypeEnum.INIT_EXISTING, componentViewOrder: rest });
  if (after) after(cm, root);
  const code = collectLines(root, []);
  const text = cm.getCodeFormatter().createAppCode(code);
  return { text, lines: text.split('\n'), meta: code.map(l => ({ tabDepth: l.tabDepth, readOnly: l.readOnly, isVisible: l.isVisible })) };
}
function appendixOf(lines) { const i = lines.indexOf('%[appendix]'); return i < 0 ? [] : lines.slice(i); }

const out = { cases: [] };
function addCase(name, input, build, opts) {
  let rec;
  try {
    const cm = build();
    const r = renderFile(cm, opts && opts.after);
    rec = { name, input, appendix: appendixOf(r.lines), lines: r.lines };
    if (opts && opts.withMeta) rec.lineMeta = r.meta;
  } catch (e) {
    rec = { name, input, error: e.message, stack: String(e.stack).split('\n').slice(0, 6) };
  }
  out.cases.push(rec);
}
const fig = (dirty, children, name) => vm(name || 'UIFigure', 'matlab.ui.Figure', dirty, children);
const figDirty = [P('Position', '[100 100 640 480]'), P('Name', "'MATLAB App'")];

// (1) a figure alone
addCase('1 figure alone', { DirtyProps: figDirty }, () => appModel({ fig: fig(figDirty) }), { withMeta: true });

// (2) a button with unsorted DirtyProps
addCase('2 button, unsorted DirtyProps', { button: [P('Text', "'Go'"), P('Position', '[20 20 100 22]'), P('ButtonPushedFcn', 'GoButtonPushed')] },
  () => appModel({ fig: fig(figDirty, [vm('GoButton', 'matlab.ui.control.Button', [P('Text', "'Go'"), P('Position', '[20 20 100 22]'), P('ButtonPushedFcn', 'GoButtonPushed')])]) }));

// (3) a grid with two children carrying Layout.Row/Layout.Column
addCase('3 grid children', null, () => appModel({ fig: fig(figDirty, [vm('GridLayout', 'matlab.ui.container.GridLayout', [P('ColumnWidth', "{'1x', '1x'}"), P('RowHeight', "{'1x'}")], [
  vm('Button', 'matlab.ui.control.Button', [P('Text', "'A'"), P('Layout.Row', '1'), P('Layout.Column', '1')]),
  vm('Button2', 'matlab.ui.control.Button', [P('Layout.Column', '2'), P('Layout.Row', '1'), P('Text', "'B'")]),
])]) }));

// (4) a labelled component (Label + EditField in a LabeledComponent group); the group is filtered out of
// the canvas groups section.
addCase('4 labelled component', null, () => {
  const label = vm('EditFieldLabel', 'matlab.ui.control.Label', [P('HorizontalAlignment', "'right'"), P('Position', '[20 400 55 22]'), P('Text', "'Edit Field'")]);
  const field = vm('EditField', 'matlab.ui.control.EditField', [P('Position', '[90 400 100 22]')]);
  const g = group('labeledGroup1', [label, field], true);
  return appModel({ fig: fig(figDirty, [label, field]), groups: [g] });
});
addCase('4b labelled component, field listed before label in the group', null, () => {
  const label = vm('EditFieldLabel', 'matlab.ui.control.Label', [P('Text', "'Edit Field'")]);
  const field = vm('EditField', 'matlab.ui.control.EditField', [P('Position', '[90 400 100 22]')]);
  const g = group('labeledGroup1', [field, label], true);
  return appModel({ fig: fig(figDirty, [field, label]), groups: [g] });
});

// (5) escaping: what XMLElementBuilder._escape does with each value string it is handed.
const escapeInputs = [
  "'a & b <c>'", "a & b <c>", "'line1\nline2'", "line1\nline2", "'it's'", "'it''s'", "'tip", "''tip'", "tip'", "'", "''", "'''",
  "{'a', 'b'}", "\"str\"", "'a]]>b'", "'&apos;'", "'tab\there'", "'cr\r\nlf'",
];
out.escape = escapeInputs.map(v => {
  const cm = makeCodeModel({});
  const b = new XMLElementBuilder(cm, { name: 'Value', tabDepth: 0 });
  b.value = v;
  return { input: v, line: cm.getCodeFormatter().createAppCode(b.getStartCode()) };
});
out.escapeEmptyAndArrays = [];
for (const v of ['', null, undefined, ['only'], ['a', 'b'], []]) {
  try {
    const cm = makeCodeModel({});
    const b = new XMLElementBuilder(cm, { name: 'X', tabDepth: 0 });
    b.value = v;
    out.escapeEmptyAndArrays.push({ input: v === undefined ? 'undefined' : v, start: b.getStartCode().map(l => l.text), end: b.getEndCode().map(l => l.text) });
  } catch (e) { out.escapeEmptyAndArrays.push({ input: v, error: e.message }); }
}
addCase('5 escaping in a component', null, () => appModel({ fig: fig(figDirty, [
  vm('Label', 'matlab.ui.control.Label', [P('Text', "'a & b <c>'"), P('Tooltip', "''tip'")]),
  vm('TextArea', 'matlab.ui.control.TextArea', [P('Value', "'first\nsecond'")]),
  vm('EditField', 'matlab.ui.control.EditField', [P('Value', "'it's'"), P('Tooltip', "'tip")]),
]) }));

// (6) a context menu first under the figure, a button with ContextMenu
addCase('6 context menu', null, () => {
  const menu = vm('Menu', 'matlab.ui.container.Menu', [P('Text', "'Menu'")]);
  const ctx = vm('ContextMenu', 'matlab.ui.container.ContextMenu', [], [menu]);
  const btn = vm('Button', 'matlab.ui.control.Button', [P('ContextMenu', 'ContextMenu'), P('Position', '[20 20 100 22]')]);
  return appModel({ fig: fig(figDirty, [ctx, btn]) });
});

// (7) nested containers: Panel > GridLayout > TabGroup > Tab > Button
addCase('7 nested containers', null, () => appModel({ fig: fig(figDirty, [
  vm('Panel', 'matlab.ui.container.Panel', [P('Title', "'Panel'"), P('Position', '[10 10 300 300]')], [
    vm('GridLayout', 'matlab.ui.container.GridLayout', [P('ColumnWidth', "{'1x'}"), P('RowHeight', "{'1x'}")], [
      vm('TabGroup', 'matlab.ui.container.TabGroup', [P('Layout.Row', '1'), P('Layout.Column', '1')], [
        vm('Tab', 'matlab.ui.container.Tab', [P('Title', "'Tab'")], [
          vm('Button', 'matlab.ui.control.Button', [P('Position', '[10 10 100 22]')]),
        ]),
      ]),
    ]),
  ]),
]) }), { withMeta: true });

// (8) a check box tree with nodes
addCase('8 tree with CheckedNodes', null, () => appModel({ fig: fig(figDirty, [
  vm('Tree', 'matlab.ui.container.CheckBoxTree', [P('Position', '[20 20 150 300]'), P('CheckedNodes', '[app.Node app.Node2]')], [
    vm('Node', 'matlab.ui.container.TreeNode', [P('Text', "'Node'")], [vm('Node3', 'matlab.ui.container.TreeNode', [P('Text', "'Node3'")])]),
    vm('Node2', 'matlab.ui.container.TreeNode', [P('Text', "'Node2'")]),
  ]),
]) }));

// (9) run configuration
addCase('9a run config: startup only', null, () => appModel({ fig: fig(figDirty), startup: { params: [] } }));
addCase('9b run config: startup with input args a, b', null, () => appModel({ fig: fig(figDirty), startup: { params: ['a', 'b'] }, inputParameters: 'a, b' }));
addCase('9c run config: singleton only', null, () => appModel({ fig: fig(figDirty), singleton: SingletonModeEnum.FOCUS }));
addCase('9d run config: startup and singleton both present when the engine is built', null, () => appModel({ fig: fig(figDirty), singleton: SingletonModeEnum.FOCUS, startup: { params: [] } }));
addCase('9e run config: startup added, then singleton set, after the engine is built', null, () => appModel({ fig: fig(figDirty) }), {
  after: (cm) => {
    const nls = load('dojo/i18n!appdesigner/resources/appdesigner/application_js/nls/CodeGeneration');
    cm.getCallbackCollection().addCallback(new CallbackModel({ Name: 'startupFcn', Comment: nls.startUpCallbackComment, CallbackType: FunctionTypes.StartupCallback, Arguments: ['app'], Order: 0, ComponentInfo: {} }));
    cm.setSingletonMode(SingletonModeEnum.FOCUS);
  },
});
addCase('9f run config: singleton set first, then startup added, after the engine is built', null, () => appModel({ fig: fig(figDirty) }), {
  after: (cm) => {
    cm.setSingletonMode(SingletonModeEnum.FOCUS);
    const nls = load('dojo/i18n!appdesigner/resources/appdesigner/application_js/nls/CodeGeneration');
    cm.getCallbackCollection().addCallback(new CallbackModel({ Name: 'startupFcn', Comment: nls.startUpCallbackComment, CallbackType: FunctionTypes.StartupCallback, Arguments: ['app'], Order: 0, ComponentInfo: {} }));
  },
});
addCase('9g run config: none', null, () => appModel({ fig: fig(figDirty) }));
out.singletonSerialization = { NONE: SingletonModeEnum.getValueForSerialization(SingletonModeEnum.NONE), FOCUS: SingletonModeEnum.getValueForSerialization(SingletonModeEnum.FOCUS) };

// (10) app details
addCase('10a app details: all set', null, () => appModel({ fig: fig(figDirty), meta: { Name: 'My App', Author: 'Ann & Bob', Version: '2.1', Summary: 'Short <summary>', Description: 'Line one\nLine two' } }));
addCase('10b app details: only Version 1.0', null, () => appModel({ fig: fig(figDirty), meta: { Version: '1.0' } }));
addCase('10c app details: none', null, () => appModel({ fig: fig(figDirty), meta: {} }));
addCase('10d app details: a new app as App Designer creates it (Name app1, Version 1.0)', null, () => appModel({ fig: fig(figDirty), meta: { Name: 'app1', Version: '1.0' } }));
addCase('10e app details: Summary set after the engine is built', null, () => appModel({ fig: fig(figDirty), meta: {} }), { after: cm => cm.getMetaDataModel().setProperty('Summary', 'Later') });

// (11) internal details
const internalBase = { AppType: 'Standard', MATLABRelease: 'R2025b', MinimumSupportedMATLABRelease: 'R2018a', Uuid: '1b2c3d4e-0000-1111-2222-333344445555' };
addCase('11a internal: scalars only', null, () => appModel({ fig: fig(figDirty), meta: Object.assign({}, internalBase) }));
addCase('11b internal: FileFormatVersion set (never set by R2025b)', null, () => appModel({ fig: fig(figDirty), meta: Object.assign({ FileFormatVersion: '1' }, internalBase) }));
addCase('11c internal: one-element lists at build time', null, () => appModel({ fig: fig(figDirty), meta: Object.assign({ RequiredProducts: ['Signal Processing Toolbox'], UserComponents: ['SpinnerGauge'], ImageRelativePaths: ['images/logo.png'] }, internalBase) }));
addCase('11d internal: two-element lists at build time', null, () => appModel({ fig: fig(figDirty), meta: Object.assign({ RequiredProducts: ['Signal Processing Toolbox', 'Image Processing Toolbox'], UserComponents: ['SpinnerGauge', 'pkg.Thing'] }, internalBase) }));
addCase('11e internal: lists set after the engine is built (the Change-event path)', null, () => appModel({ fig: fig(figDirty), meta: Object.assign({}, internalBase) }), {
  after: cm => {
    cm.getMetaDataModel().setProperty('RequiredProducts', ['Signal Processing Toolbox']);
    cm.getMetaDataModel().setProperty('UserComponents', ['SpinnerGauge', 'pkg.Thing']);
    cm.getMetaDataModel().setProperty('ImageRelativePaths', ['images/logo.png', 'icons/a.svg']);
  },
});
addCase('11f internal: only AppType', null, () => appModel({ fig: fig(figDirty), meta: { AppType: 'Standard' } }));

// (12) canvas groups: a plain group of two members, a group holding a nested group and a component, and a
// group holding a labelled component.
addCase('12 canvas groups', null, () => {
  const b1 = vm('Button', 'matlab.ui.control.Button', [P('Position', '[10 10 100 22]')]);
  const b2 = vm('Button2', 'matlab.ui.control.Button', [P('Position', '[10 40 100 22]')]);
  const b3 = vm('Button3', 'matlab.ui.control.Button', [P('Position', '[200 10 100 22]')]);
  const b4 = vm('Button4', 'matlab.ui.control.Button', [P('Position', '[200 40 100 22]')]);
  const b5 = vm('Button5', 'matlab.ui.control.Button', [P('Position', '[200 70 100 22]')]);
  const lab = vm('EditFieldLabel', 'matlab.ui.control.Label', [P('Text', "'Edit Field'")]);
  const ef = vm('EditField', 'matlab.ui.control.EditField', [P('Position', '[90 400 100 22]')]);
  const b6 = vm('Button6', 'matlab.ui.control.Button', [P('Position', '[300 70 100 22]')]);
  const g1 = group('group1', [b1, b2]);
  const g3 = group('group3', [b3, b4]);
  const g2 = group('group2', [g3, b5]);
  const lg = group('labeled1', [lab, ef], true);
  const g4 = group('group4', [lg, b6]);
  return appModel({ fig: fig(figDirty, [b1, b2, b3, b4, b5, lab, ef, b6]), groups: [g1, g2, g4] });
});

// (13) the whole file for a small app
const custom = ['    ', '    properties (Access = private)', '        Property % Description', '    end', '    '];
addCase('13 whole file Plain1', { className: 'Plain1', help: 'A plain app', callback: 'GoButtonPushed', body: ['x = 1;'], startup: 'startupFcn(app, a, b)', singleton: 'FOCUS' }, () => appModel({
  className: 'Plain1', help: { summary: 'A plain app', description: '' }, singleton: SingletonModeEnum.FOCUS, startup: { params: ['a', 'b'] }, inputParameters: 'a, b',
  fig: fig(figDirty, [vm('GoButton', 'matlab.ui.control.Button', [P('ButtonPushedFcn', 'GoButtonPushed'), P('Position', '[20 20 100 22]'), P('Text', "'Go'")])]),
  meta: { Name: 'Plain1', Version: '1.0', AppType: 'Standard', MATLABRelease: 'R2025b', MinimumSupportedMATLABRelease: 'R2025a', Uuid: 'abc' },
  custom,
  callbacks: [{ name: 'GoButtonPushed', component: 'GoButton', prop: 'ButtonPushedFcn', model: { LoadedFromDisk: true, Code: ['            x = 1;'] } }],
}), { withMeta: true });
addCase('13b whole file Plain1, IndentSize 2', null, () => appModel({
  indentSize: 2, className: 'Plain1', help: { summary: 'A plain app', description: '' }, singleton: SingletonModeEnum.FOCUS, startup: { params: ['a', 'b'] }, inputParameters: 'a, b',
  fig: fig(figDirty, [vm('Panel', 'matlab.ui.container.Panel', [P('Position', '[10 10 300 300]')], [vm('GoButton', 'matlab.ui.control.Button', [P('ButtonPushedFcn', 'GoButtonPushed'), P('Text', "'Go'")])])]),
  meta: { Name: 'Plain1', Version: '1.0' },
  callbacks: [{ name: 'GoButtonPushed', component: 'GoButton', prop: 'ButtonPushedFcn', model: { LoadedFromDisk: true, Code: ['        x = 1;'] } }],
}));
addCase('13c whole file, figure only, no metadata, no callbacks', null, () => appModel({ className: 'app1', fig: fig(figDirty) }));

// (14) responsive app: a figure with a responsive grid (AppTypeProperties.IsResponsiveContainer) and panels -
// the writer reads only CodeName/GroupId/DirtyProps/children, so nothing marks them.
addCase('14 responsive 2-panel shape', null, () => {
  const left = vm('LeftPanel', 'matlab.ui.container.Panel', [P('Layout.Row', '1'), P('Layout.Column', '1')]);
  const right = vm('RightPanel', 'matlab.ui.container.Panel', [P('Layout.Row', '1'), P('Layout.Column', '2')]);
  left._props.AppTypeProperties = { IsResponsiveContainer: true };
  right._props.AppTypeProperties = { IsResponsiveContainer: true };
  const grid = vm('GridLayout', 'matlab.ui.container.GridLayout', [P('ColumnWidth', "{220, '1x'}"), P('RowHeight', "{'1x'}"), P('ColumnSpacing', '0'), P('RowSpacing', '0'), P('Padding', '[0 0 0 0]'), P('Scrollable', "'on'")], [left, right]);
  grid._props.AppTypeProperties = { IsResponsiveContainer: true };
  return appModel({ fig: fig([P('AutoResizeChildren', "'off'"), P('Position', '[100 100 640 480]'), P('Name', "'MATLAB App'"), P('SizeChangedFcn', 'updateAppLayout')], [grid]) });
});

// Tab indentation (InsertSpaces false) - the XML shares the class's indentation settings.
addCase('15 InsertSpaces false (tabs)', null, () => {
  const cm = appModel({ fig: fig(figDirty, [vm('Button', 'matlab.ui.control.Button', [P('Text', "'Go'")])]) });
  cm._formatter._activeSettings.InsertSpaces = false;
  return cm;
});

// (16) a component with no dirty properties and no children; DirtyProps handed as one struct (not an array),
// as MATLAB's jsonencode sends a 1x1 struct array; a Value given as a one-element array.
addCase('16 empty DirtyProps, single-struct DirtyProps, one-element array value', null, () => {
  const single = vm('Lamp', 'matlab.ui.control.Lamp', []);
  single._props.DirtyProps = { Name: 'Position', Value: '[10 10 20 20]' };
  return appModel({ fig: fig(figDirty, [vm('Button', 'matlab.ui.control.Button', []), single, vm('Label', 'matlab.ui.control.Label', [P('Text', ["'One'"])])]) });
});

// (17) the MATLAB side: writePlainTextAppFile appends AppThumbnailUtils.createThumbnailAppendixSectionStr's text.
// Transcribed (not run): sprintf(strjoin({...}, newline), autoCapture, thumbnailURI), appended as
// append(fileContent, newline, newline, section).
function thumbnailSection(autoCapture, uri) {
  return ['%---', '%[app:thumbnail]', '%{',
    '<!-- Thumbnail is used by file previewers. To change how the thumbnail is captured or stored, use the App Details dialog box in App Designer. -->',
    "<?xml version='1.0' encoding='UTF-8' standalone='yes' ?>",
    `<Thumbnail autoCapture='${autoCapture}'>${uri}</Thumbnail>`, '%}'].join('\n');
}
const whole = out.cases.find(c => c.name === '13 whole file Plain1');
out.savedFiles = [
  { screenshotMode: 'auto (new file, no thumbnail yet) / otherwise', autoCapture: 'true', uri: '' },
  { screenshotMode: 'none', autoCapture: 'false', uri: '' },
  { screenshotMode: 'manual', autoCapture: 'false', uri: 'data:image/jpg;base64,/9j/4AAQ' },
].map(s => ({ screenshotMode: s.screenshotMode, text: whole.lines.join('\n') + '\n' + '\n' + thumbnailSection(s.autoCapture, s.uri) }));

out.modules = report();
fs.writeFileSync(path.join(__dirname, 'appendix.json'), JSON.stringify(out, null, 1));
console.log('appendix.json:', out.cases.length, 'cases;', out.cases.filter(c => c.error).map(c => c.name + ': ' + c.error).join(' | '));
