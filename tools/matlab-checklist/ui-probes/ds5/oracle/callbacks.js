// callbacks.json: per-component callback data, generated comments through the real CallbackModel, default
// callback names, UniqueNameGenerator and CodeEditUtil results. Run: node callbacks.js
'use strict';
const fs = require('fs');
const path = require('path');
const { makeCodeModel, makePeerNode, render, loadRegistry, load } = require('./stubs');
const { report } = require('./amd');

const registry = loadRegistry();
const CallbackModel = load('appdesigner/model/codegen/CallbackModel');
const CallbackBinding = load('appdesigner/model/codegen/CallbackBinding');
const FunctionTypes = load('appdesigner/model/codegen/FunctionTypes');
const MethodBuilder = load('appdesigner/codegen/builders/MethodBuilder');
const UniqueNameGenerator = load('hmishared/util/UniqueNameGenerator');
const CodeEditUtil = load('appdesigner/util/CodeEditUtil');
const CallbackUtil = load('appdesigner/util/CallbackUtil');
const AppTypeMethodEnum = load('appdesigner/model/codegen/AppTypeMethodEnum');
const AppNodeModel = load('appdesigner/model/inspectableapp/InspectableAppNodeModel');
const nlsCG = load('dojo/i18n!appdesigner/resources/appdesigner/application_js/nls/CodeGeneration');

const out = {};

// (a) per component model
out.components = Object.keys(registry).map(type => {
  const model = registry[type];
  const groups = (model.CallbackFcnData || []).map(g => {
    const name = Object.keys(g)[0];
    const arr = g[name];
    return { group: name, title: arr.Title || null, entries: arr.map(x => ({ Label: x.Label, RequiresEventArgument: x.RequiresEventArgument })) };
  });
  const props = [].concat(...groups.map(g => g.entries.map(e => e.Label)));
  const comments = {}, defaultCode = {};
  props.forEach(p => {
    comments[p] = (model.CodeGeneration && model.CodeGeneration.CallbackComments && model.CodeGeneration.CallbackComments[p]) || null;
    defaultCode[p] = model.CodeGeneration.getDefaultCallbackCode(p, type);
  });
  return {
    type, name: model.Name, defaultCodeName: model.CodeNameGeneration.DefaultCodeName, defaultText: model.CodeNameGeneration.DefaultText,
    defaultTextPropertyName: model.CodeNameGeneration.DefaultTextPropertyName === undefined ? null : model.CodeNameGeneration.DefaultTextPropertyName,
    callbackFcnData: groups, callbackComments: (model.CodeGeneration && model.CodeGeneration.CallbackComments) || {}, resolvedComments: comments, defaultCode,
    defaultCallbackType: model.getDefaultCallbackType(),
  };
});
out.appNode = {
  callbackFcnData: AppNodeModel.CallbackFcnData,
  defaultCallbackName: AppNodeModel.getDefaultCallbackName('app', 'StartupFcn'),
  defaultCallbackType: AppNodeModel.getDefaultCallbackType(),
  defaultCode: AppNodeModel.CodeGeneration.getDefaultCallbackCode('StartupFcn'),
  startUpCallbackComment: nlsCG.startUpCallbackComment,
};
out.reservedMethodNames = { Standard: AppTypeMethodEnum.getReservedMethodNames('Standard'), CustomUIComponent: AppTypeMethodEnum.getReservedMethodNames('CustomUIComponent') };
out.nls = nlsCG;

// (b) generated comments through the real CallbackModel + CallbackBindingCollection/CallbackBinding
function callbackCase(cm, name, bindings, extra) {
  const props = Object.assign({ CallbackType: FunctionTypes.ComponentCallback, GeneratedName: name, DefaultCode: [''], ComponentInfo: registry, Order: -1, Arguments: ['app', 'event'] }, extra || {});
  const model = new CallbackModel(props);
  bindings.forEach(b => {
    const node = makePeerNode(b.codeName, b.type, registry[b.type]);
    model.getBindings().addBinding(new CallbackBinding(node, model, b.prop));
  });
  cm.getCallbackCollection().addCallback(model);
  const mb = new MethodBuilder(cm, { behavior: cm.getCodeEngineConfig().get('CallbackMethodBehavior', model), tabDepth: 2, id: model.getProperty('Id') });
  return {
    comment: model.getProperty('Comment'),
    generatedComment: model.getProperty('GeneratedComment'),
    defaultCode: model.getProperty('DefaultCode'),
    code: model.getProperty('Code'),
    methodStartLines: cm.getCodeFormatter().createAppCode(mb.getStartCode()).split('\n'),
    method: render(cm, mb),
  };
}
const B = (codeName, prop, type) => ({ codeName, prop, type: type || 'matlab.ui.control.Button' });
const N = (prefix, len) => (prefix + 'x'.repeat(Math.max(0, len))).slice(0, len);
const cases = [
  { key: 'Button.ButtonPushedFcn', bindings: [B('Button', 'ButtonPushedFcn')] },
  { key: 'UIFigure.CloseRequestFcn', bindings: [B('UIFigure', 'CloseRequestFcn', 'matlab.ui.Figure')] },
  { key: 'UIFigure.KeyPressFcn', bindings: [B('UIFigure', 'KeyPressFcn', 'matlab.ui.Figure')] },
  { key: 'UIFigure.WindowScrollWheelFcn', bindings: [B('UIFigure', 'WindowScrollWheelFcn', 'matlab.ui.Figure')] },
  { key: 'UIFigure.SizeChangedFcn', bindings: [B('UIFigure', 'SizeChangedFcn', 'matlab.ui.Figure')] },
  { key: 'EditField.ValueChangedFcn', bindings: [B('EditField', 'ValueChangedFcn', 'matlab.ui.control.EditField')] },
  { key: 'EditField.ValueChangingFcn', bindings: [B('EditField', 'ValueChangingFcn', 'matlab.ui.control.EditField')] },
  { key: 'UITable.CellEditCallback', bindings: [B('UITable', 'CellEditCallback', 'matlab.ui.control.Table')] },
  { key: 'UITable.SelectionChangedFcn', bindings: [B('UITable', 'SelectionChangedFcn', 'matlab.ui.control.Table')] },
  { key: 'Tab.SizeChangedFcn', bindings: [B('Tab', 'SizeChangedFcn', 'matlab.ui.container.Tab')] },
  { key: 'TabGroup.SelectionChangedFcn', bindings: [B('TabGroup', 'SelectionChangedFcn', 'matlab.ui.container.TabGroup')] },
  { key: 'ButtonGroup.SelectionChangedFcn', bindings: [B('ButtonGroup', 'SelectionChangedFcn', 'matlab.ui.container.ButtonGroup')] },
  { key: 'Menu.MenuSelectedFcn', bindings: [B('Menu', 'MenuSelectedFcn', 'matlab.ui.container.Menu')] },
  { key: 'PushTool.ClickedCallback', bindings: [B('PushTool', 'ClickedCallback', 'matlab.ui.container.toolbar.PushTool')] },
  { key: 'HTML.HTMLEventReceivedFcn', bindings: [B('HTML', 'HTMLEventReceivedFcn', 'matlab.ui.control.HTML')] },
  { key: 'HTML.DataChangedFcn', bindings: [B('HTML', 'DataChangedFcn', 'matlab.ui.control.HTML')] },
  { key: 'Slider.ValueChangingFcn', bindings: [B('Slider', 'ValueChangingFcn', 'matlab.ui.control.Slider')] },
  { key: 'DropDown.ValueChangedFcn', bindings: [B('DropDown', 'ValueChangedFcn', 'matlab.ui.control.DropDown')] },
  { key: 'Image.ImageClickedFcn', bindings: [B('Image', 'ImageClickedFcn', 'matlab.ui.control.Image')] },
  { key: 'Tree.NodeExpandedFcn', bindings: [B('Tree', 'NodeExpandedFcn', 'matlab.ui.container.Tree')] },
  { key: 'ContextMenu.ContextMenuOpeningFcn', bindings: [B('ContextMenu', 'ContextMenuOpeningFcn', 'matlab.ui.container.ContextMenu')] },
  { key: 'two same property', bindings: [B('Button', 'ButtonPushedFcn'), B('Button2', 'ButtonPushedFcn')] },
  { key: 'two different properties', bindings: [B('Button', 'ButtonPushedFcn'), B('EditField', 'ValueChangedFcn', 'matlab.ui.control.EditField')] },
  { key: 'same property different classes', bindings: [B('UIFigure', 'SizeChangedFcn', 'matlab.ui.Figure'), B('Panel', 'SizeChangedFcn', 'matlab.ui.container.Panel')] },
  { key: 'one component two properties', bindings: [B('EditField', 'ValueChangedFcn', 'matlab.ui.control.EditField'), B('EditField', 'ValueChangingFcn', 'matlab.ui.control.EditField')] },
  { key: 'three short', bindings: ['Button', 'Button2', 'Button3'].map(n => B(n, 'ButtonPushedFcn')) },
  { key: 'five moderate', bindings: ['ComputeResultsButton', 'ExportDataToExcelButton', 'LoadConfigurationButton', 'ResetAllFieldsButton', 'StartSimulationButton'].map(n => B(n, 'ButtonPushedFcn')) },
  { key: 'eight short', bindings: [1, 2, 3, 4, 5, 6, 7, 8].map(i => B('Button' + (i === 1 ? '' : i), 'ButtonPushedFcn')) },
  { key: 'eight unsorted insertion', bindings: ['Button8', 'Button3', 'Button', 'Button5', 'Button2', 'Button7', 'Button4', 'Button6'].map(n => B(n, 'ButtonPushedFcn')) },
  { key: 'first name fits exactly (39 chars + comma)', bindings: [B(N('A', 39), 'ButtonPushedFcn'), B('Button2', 'ButtonPushedFcn')] },
  { key: 'first name one too long (40 chars + comma)', bindings: [B(N('A', 40), 'ButtonPushedFcn'), B('Button2', 'ButtonPushedFcn')] },
  { key: 'single name fits exactly (41 chars, no comma)', bindings: [B(N('A', 41), 'ButtonPushedFcn')] },
  { key: 'single name one too long (42 chars)', bindings: [B(N('A', 42), 'ButtonPushedFcn')] },
  { key: 'first name too long for a whole line (80)', bindings: [B(N('A', 80), 'ButtonPushedFcn'), B('Button2', 'ButtonPushedFcn')] },
  { key: 'second name too long after first', bindings: [B('Button', 'ButtonPushedFcn'), B(N('B', 70), 'ButtonPushedFcn'), B('Button3', 'ButtonPushedFcn')] },
  { key: 'second line fills then truncates', bindings: [B('Button', 'ButtonPushedFcn'), B(N('B', 50), 'ButtonPushedFcn'), B(N('C', 30), 'ButtonPushedFcn'), B('Button4', 'ButtonPushedFcn')] },
  { key: 'ordinal sort alpha Beta gamma Delta', bindings: ['alpha', 'Beta', 'gamma', 'Delta'].map(n => B(n, 'ButtonPushedFcn')) },
  { key: 'ordinal sort underscore digits', bindings: ['b_1', 'b1', 'B2', 'a10', 'a9'].map(n => B(n, 'ButtonPushedFcn')) },
  { key: 'mixed comment long (no % in base string)', bindings: [B(N('A', 45), 'ButtonPushedFcn'), B('EditField', 'ValueChangedFcn', 'matlab.ui.control.EditField')] },
  { key: 'mixed comment 66 chars', bindings: [B(N('A', 47), 'ButtonPushedFcn'), B('E', 'ValueChangedFcn', 'matlab.ui.control.EditField')] },
  { key: 'mixed comment 67 chars', bindings: [B(N('A', 48), 'ButtonPushedFcn'), B('E', 'ValueChangedFcn', 'matlab.ui.control.EditField')] },
  { key: 'zero bindings', bindings: [] },
  { key: 'zero bindings custom name', bindings: [], extra: { GeneratedName: 'helperCallback' } },
  { key: 'UseCustomComment', bindings: [B('Button', 'ButtonPushedFcn')], extra: { UseCustomComment: true, Comment: ['My own comment'] } },
];
out.comments = [];
for (const indentSize of [4, 2]) {
  const cm = makeCodeModel({ indentSize });
  for (const c of cases) out.comments.push(Object.assign({ indentSize, key: c.key, bindings: c.bindings }, callbackCase(cm, c.key.replace(/\W+/g, '_'), c.bindings, c.extra)));
}
// The startup callback's method lines (no bindings, Comment is a string, Arguments without event).
for (const indentSize of [4, 2]) for (const params of [[], ['a', 'b']]) {
  const cm = makeCodeModel({ indentSize, startup: { params } });
  const model = cm.getCallbackCollection().all()[0];
  const mb = new MethodBuilder(cm, { behavior: cm.getCodeEngineConfig().get('CallbackMethodBehavior', model), tabDepth: 2, id: model.getProperty('Id') });
  out.comments.push({ indentSize, key: 'startupFcn(' + params.join(', ') + ')', bindings: [], comment: model.getProperty('Comment'), code: model.getProperty('Code'), methodStartLines: cm.getCodeFormatter().createAppCode(mb.getStartCode()).split('\n'), method: render(cm, mb) });
}
// A callback-methods block with two callbacks, to show the block comment/access and blank-line placement.
for (const indentSize of [4, 2]) {
  const cm = makeCodeModel({ indentSize, startup: { params: [] } });
  const m1 = new CallbackModel({ CallbackType: FunctionTypes.ComponentCallback, GeneratedName: 'ButtonPushed', DefaultCode: [''], ComponentInfo: registry, Order: -1, Arguments: ['app', 'event'] });
  m1.getBindings().addBinding(new CallbackBinding(makePeerNode('Button', 'matlab.ui.control.Button', registry['matlab.ui.control.Button']), m1, 'ButtonPushedFcn'));
  cm.getCallbackCollection().addCallback(m1);
  const m2 = new CallbackModel({ CallbackType: FunctionTypes.ComponentCallback, GeneratedName: 'UIFigureCloseRequest', DefaultCode: [''], ComponentInfo: registry, Order: -1, Arguments: ['app', 'event'] });
  m2.getBindings().addBinding(new CallbackBinding(cm.fig, m2, 'CloseRequestFcn'));
  cm.getCallbackCollection().addCallback(m2);
  const GroupBuilder = load('appdesigner/codegen/builders/GroupBuilder');
  const g = new GroupBuilder(cm, { id: 'callback-group', behavior: cm.getCodeEngineConfig().get('CallbackGroupBehavior') });
  out.comments.push({ indentSize, key: 'callback group: startupFcn + ButtonPushed + UIFigureCloseRequest', orders: cm.getCallbackCollection().all(true).map(m => [m.getProperty('Name'), m.getProperty('Order')]), method: render(cm, g) });
}

// (c) default callback names and unique names
const nameCases = [
  ['Button', 'ButtonPushedFcn', 'matlab.ui.control.Button'], ['StartButton', 'ButtonPushedFcn', 'matlab.ui.control.Button'],
  ['UIFigure', 'CloseRequestFcn', 'matlab.ui.Figure'], ['EditField', 'ValueChangedFcn', 'matlab.ui.control.EditField'],
  ['NameEditField', 'ValueChangingFcn', 'matlab.ui.control.EditField'], ['Table', 'CellEditCallback', 'matlab.ui.control.Table'],
  ['UITable', 'CellEditCallback', 'matlab.ui.control.Table'], ['PushTool', 'ClickedCallback', 'matlab.ui.container.toolbar.PushTool'],
  ['Tab', 'SizeChangedFcn', 'matlab.ui.container.Tab'], ['Menu', 'MenuSelectedFcn', 'matlab.ui.container.Menu'],
  ['Button2', 'ButtonPushedFcn', 'matlab.ui.control.Button'], ['UIFigure', 'WindowButtonDownFcn', 'matlab.ui.Figure'],
  ['MyUIFigure2', 'KeyPressFcn', 'matlab.ui.Figure'], ['Slider', 'ValueChangedFcn', 'matlab.ui.control.Slider'],
  ['DropDown', 'DropDownOpeningFcn', 'matlab.ui.control.DropDown'], ['Image', 'ImageClickedFcn', 'matlab.ui.control.Image'],
  ['ToggleTool', 'OnCallback', 'matlab.ui.container.toolbar.ToggleTool'], ['HTML', 'HTMLEventReceivedFcn', 'matlab.ui.control.HTML'],
  ['TabGroup', 'SelectionChangedFcn', 'matlab.ui.container.TabGroup'], ['ButtonGroup', 'SelectionChangedFcn', 'matlab.ui.container.ButtonGroup'],
  ['ContextMenu', 'ContextMenuOpeningFcn', 'matlab.ui.container.ContextMenu'], ['CheckBox', 'ValueChangedFcn', 'matlab.ui.control.CheckBox'],
];
out.defaultCallbackNames = nameCases.map(([codeName, prop, type]) => ({ codeName, prop, type, name: registry[type].getDefaultCallbackName(codeName, prop) }));
const uniqueCases = [[], ['ButtonPushed'], ['ButtonPushed', 'ButtonPushed2'], ['ButtonPushed3'], ['ButtonPushedX'], ['ButtonPushed 1'], ['ButtonPushed', 'ButtonPushed3'], ['ButtonPushed2'], ['ButtonPushed_2'], ['buttonpushed'], ['XButtonPushed'], ['ButtonPushed10']];
out.uniqueNames = uniqueCases.map(existing => ({ existing, base: 'ButtonPushed', name: new UniqueNameGenerator(false, '').generateVariableName(existing, 'ButtonPushed', false) }));
out.uniqueNames.push(...[[], ['func'], ['func', 'func2'], ['Property'], ['Property', 'Property2']].map(existing => ({ existing, base: existing[0] && existing[0].startsWith('P') ? 'Property' : 'func', name: new UniqueNameGenerator(false, '').generateVariableName(existing, existing[0] && existing[0].startsWith('P') ? 'Property' : 'func', false) })));
out.uniqueNames.push({ existing: ['Button'], base: 'Button', note: 'exact=true (code names)', name: new UniqueNameGenerator(false, '').generateVariableName(['Button'], 'Button', true) });

// (d) CodeEditUtil
const tryMsg = fn => { try { const r = fn(); return { ok: true, result: r === undefined ? null : r }; } catch (e) { return { ok: false, message: e.message }; } };
const varNames = ['Button', 'if', 'end', 'properties', 'methods', '1abc', 'a-b', 'a_b', '_a', 'a b', '', 'x'.repeat(63), 'x'.repeat(64), 'abc$', 'résumé', 'startupFcn', 'ButtonPushed'];
out.isVarName = varNames.map(n => ({ input: n, max63: tryMsg(() => CodeEditUtil.isVarName(n, 63)), defaultMax: tryMsg(() => CodeEditUtil.isVarName(n)) }));
const argTexts = ['', 'a', 'a, b', 'a,b', 'a,,b', 'a b', 'a, a', ', a,', 'a,', ' a , b ', 'a, 1b', 'a, if', 'a, ' + 'x'.repeat(64), 'a;b', 'varargin', 'app', ','];
out.validateInputArguments = argTexts.map(t => { const e = CodeEditUtil.validateInputArguments(t, 63); return { input: t, error: e ? e.message : null, sanitized: CodeEditUtil.sanitizeInputArgumentText(t) }; });
out.isNameUniqueInArray = [[['a', 'b'], 'a'], [['a', 'a'], 'a'], [['a', ' a'], 'a'], [['a', 'b'], 'c']].map(([arr, n]) => ({ array: arr, name: n, result: tryMsg(() => CodeEditUtil.isNameUniqueInArray(arr, n)) }));
out.isAppAttributeNameUnique = [
  ['Button', '', { Button: 'AppComponentProperty' }], ['startupFcn', '', { startupFcn: 'AppStartupFunction' }], ['ButtonPushed', '', { ButtonPushed: 'AppCallbackFunction' }],
  ['app1', '', { app1: 'AppConstructorFunction' }], ['createComponents', '', { createComponents: 'AppInitializationFunction' }], ['delete', '', { delete: 'AppBaseMethod' }],
  ['Property', '', { Property: 'AppAuthorProperty' }], ['func', '', { func: 'AppAuthorFunction' }], ['Button', 'Button', { Button: 'AppComponentProperty' }], ['Free', '', { Button: 'AppComponentProperty' }],
].map(([n, old, map]) => ({ name: n, oldName: old, hashmap: map, result: tryMsg(() => CodeEditUtil.isAppAttributeNameUnique(n, old, map, '')) }));
out.createEventName = ['Name', 'Code', 'GeneratedComment', 'Arguments'].map(p => [p, CodeEditUtil.createEventName(p)]);
out.isStartupCallbackByName = ['StartupFcn', 'startupFcn', 'PostSetupFcn', 'ButtonPushedFcn'].map(n => [n, CallbackUtil.isStartupCallbackByName(n)]);
out.formatComment = ['Description', '% already', '%no space', '%% two'].map(s => [s, makeCodeModel({}).getCodeFormatter().formatComment(s)]);

out.modules = report();
fs.writeFileSync(path.join(__dirname, '..', 'callbacks.json'), JSON.stringify(out, null, 1));
console.log('callbacks.json written:', out.components.length, 'components,', out.comments.length, 'comment cases');
