// templates.json: UserEditableBlockBehavior's Add Property / Add Function output over a real CodeBuilder, and
// CallbackCodeBehavior._getModelCode's indentation of a callback's default code. Run: node templates.js
'use strict';
const fs = require('fs');
const path = require('path');
const { makeCodeModel, makePeerNode, render, loadRegistry, load } = require('./stubs');
const { report } = require('./amd');

const registry = loadRegistry();
const CodeBuilder = load('appdesigner/codegen/builders/CodeBuilder');
const CallbackModel = load('appdesigner/model/codegen/CallbackModel');
const CallbackBinding = load('appdesigner/model/codegen/CallbackBinding');
const FunctionTypes = load('appdesigner/model/codegen/FunctionTypes');
const out = { editable: [], callbackCode: [] };

function customBuilder(cm) {
  // CustomCodeGroupBehavior: new CodeBuilder(cm, {tabDepth:0, id:'custom', readOnly:false, behavior: CustomCodeBehavior})
  return new CodeBuilder(cm, { tabDepth: 0, id: 'custom', readOnly: false, behavior: cm.getCodeEngineConfig().get('CustomCodeBehavior') });
}
// CodeController.insertEditableCode emits these events (Name from _getNextEditable*Name, Access from the toolstrip).
const addProperty = (cm, Name, Access) => cm.emit('userPropertyAdded', { Name, ItemType: 'Property', Type: 'PROPERTIES', Access });
const addMethod = (cm, Name, Access) => cm.emit('userMethodAdded', { Name, ItemType: 'Method', Type: 'METHODS', Access });
const snapshot = (cm, b) => ({ codeLines: b.getCode().map(l => l.text), rendered: render(cm, b), customCodeModel: cm.getCustomCodeModel().getCustomCode().slice() });

// An editable section as App Designer saves it (indentation is literal text; whitespace-only lines kept).
const existing4 = [
  '    ',
  '    properties (Access = private)',
  '        Property % Description',
  '    end',
  '    ',
  '    methods (Access = private)',
  '        ',
  '        function results = func(app)',
  '            ',
  '        end',
  '    end',
  '    ',
];
const existingPublic4 = [
  '    ',
  '    properties (Access = public)',
  '        Property % Description',
  '    end',
  '    ',
  '    methods (Access = public)',
  '        ',
  '        function results = func(app)',
  '            ',
  '        end',
  '    end',
  '    ',
];
const existingBareMethods = ['    ', '    methods', '        ', '        function results = func(app)', '            ', '        end', '    end', '    '];
const existingNoTrailingBlank = ['    ', '    properties (Access = private)', '        Property % Description', '    end'];
const existingNestedEnds = [
  '    ',
  '    methods (Access = private)',
  '        ',
  '        function results = func(app)',
  '            if true',
  '                x = 1; % end',
  '            end',
  "            s = 'end';",
  '            %{',
  '            end',
  '            %}',
  '        end',
  '    end',
  '    ',
];

function run(key, indentSize, existing, steps) {
  const cm = makeCodeModel({ indentSize });
  if (existing) cm.getCustomCodeModel().loadCustomCode(existing);
  const b = customBuilder(cm);
  const rec = { key, indentSize, existing: existing || [], steps: [] };
  rec.initial = snapshot(cm, b);
  for (const s of steps) {
    if (s.kind === 'property') addProperty(cm, s.name, s.access); else addMethod(cm, s.name, s.access);
    rec.steps.push(Object.assign({ step: s }, snapshot(cm, b)));
  }
  out.editable.push(rec);
}
for (const indentSize of [4, 2]) {
  run('empty: add private property', indentSize, null, [{ kind: 'property', name: 'Property', access: 'private' }]);
  run('empty: add public property', indentSize, null, [{ kind: 'property', name: 'Property', access: 'public' }]);
  run('empty: add private function', indentSize, null, [{ kind: 'method', name: 'func', access: 'private' }]);
  run('empty: add public function', indentSize, null, [{ kind: 'method', name: 'func', access: 'public' }]);
  run('empty: private property, then private function, then second of each', indentSize, null, [
    { kind: 'property', name: 'Property', access: 'private' }, { kind: 'method', name: 'func', access: 'private' },
    { kind: 'property', name: 'Property2', access: 'private' }, { kind: 'method', name: 'func2', access: 'private' }]);
  run('empty: private property then public property then public function then private function', indentSize, null, [
    { kind: 'property', name: 'Property', access: 'private' }, { kind: 'property', name: 'Property2', access: 'public' },
    { kind: 'method', name: 'func', access: 'public' }, { kind: 'method', name: 'func2', access: 'private' }]);
  run('existing private blocks: add private property and private function', indentSize, existing4, [
    { kind: 'property', name: 'Property2', access: 'private' }, { kind: 'method', name: 'func2', access: 'private' }]);
  run('existing private blocks: add public property and public function', indentSize, existing4, [
    { kind: 'property', name: 'Property2', access: 'public' }, { kind: 'method', name: 'func2', access: 'public' }]);
  run('existing public blocks: add private property and private function', indentSize, existingPublic4, [
    { kind: 'property', name: 'Property2', access: 'private' }, { kind: 'method', name: 'func2', access: 'private' }]);
  run('existing bare methods: add public function then private function', indentSize, existingBareMethods, [
    { kind: 'method', name: 'func2', access: 'public' }, { kind: 'method', name: 'func3', access: 'private' }]);
  run('existing without trailing blank: add private function', indentSize, existingNoTrailingBlank, [{ kind: 'method', name: 'func', access: 'private' }]);
  run('existing nested ends and comments: add private function', indentSize, existingNestedEnds, [{ kind: 'method', name: 'func2', access: 'private' }]);
}

// CallbackCodeBehavior._getModelCode: a fresh callback's default code gets the function-content indentation
// written into the model; a callback loaded from disk keeps its lines as they are.
function codeCase(key, indentSize, modelProps, binding) {
  const cm = makeCodeModel({ indentSize });
  const model = new CallbackModel(Object.assign({ CallbackType: FunctionTypes.ComponentCallback, GeneratedName: 'cb', DefaultCode: [''], ComponentInfo: registry, Order: -1, Arguments: ['app', 'event'] }, modelProps));
  const before = { code: model.getProperty('Code').slice(), defaultCode: model.getProperty('DefaultCode').slice() };
  if (binding) model.getBindings().addBinding(new CallbackBinding(makePeerNode(binding.codeName, binding.type, registry[binding.type]), model, binding.prop));
  const afterBinding = { code: model.getProperty('Code').slice(), defaultCode: model.getProperty('DefaultCode').slice() };
  cm.getCallbackCollection().addCallback(model);
  const depth = cm.getCodeFormatter().getFunctionContentIndentDepth(2);
  // CallbackBehavior: new CodeBuilder(cm, {tabDepth: functionContentDepth(method depth 2), behavior: CallbackCodeBehavior(model), autoformatTabDepth: IsReadOnlyCode, readOnly: IsReadOnlyCode})
  const b = new CodeBuilder(cm, { tabDepth: depth, behavior: cm.getCodeEngineConfig().get('CallbackCodeBehavior', model), autoformatTabDepth: false, readOnly: false });
  out.callbackCode.push({ key, indentSize, modelProps, binding: binding || null, before, afterBinding, builderTabDepth: depth, builderLines: b.getCode().map(l => ({ text: l.text, tabDepth: l.tabDepth, postRender: l.postRender ? Object.assign({}, l.postRender, { timestamp: undefined }) : undefined })), rendered: render(cm, b), modelCodeAfter: model.getProperty('Code').slice(), loadedFromDiskAfter: model.getProperty('LoadedFromDisk') === undefined ? null : model.getProperty('LoadedFromDisk') });
}
for (const indentSize of [4, 2]) {
  codeCase('fresh, Button.ButtonPushedFcn', indentSize, {}, { codeName: 'Button', type: 'matlab.ui.control.Button', prop: 'ButtonPushedFcn' });
  codeCase('fresh, UIFigure.CloseRequestFcn', indentSize, {}, { codeName: 'UIFigure', type: 'matlab.ui.Figure', prop: 'CloseRequestFcn' });
  codeCase('fresh, UITable.CellEditCallback', indentSize, {}, { codeName: 'UITable', type: 'matlab.ui.control.Table', prop: 'CellEditCallback' });
  codeCase('fresh, EditField.ValueChangedFcn', indentSize, {}, { codeName: 'EditField', type: 'matlab.ui.control.EditField', prop: 'ValueChangedFcn' });
  codeCase('fresh, TabGroup.SelectionChangedFcn', indentSize, {}, { codeName: 'TabGroup', type: 'matlab.ui.container.TabGroup', prop: 'SelectionChangedFcn' });
  codeCase('fresh, no binding', indentSize, {}, null);
  codeCase('fresh, startup', indentSize, { CallbackType: FunctionTypes.StartupCallback, GeneratedName: 'startupFcn', Arguments: ['app'] }, null);
  codeCase('loaded from disk, Code []', indentSize, { LoadedFromDisk: true, Code: [], Name: 'ButtonPushed' }, { codeName: 'Button', type: 'matlab.ui.control.Button', prop: 'ButtonPushedFcn' });
  codeCase('loaded from disk, Code [""]', indentSize, { LoadedFromDisk: true, Code: [''], Name: 'ButtonPushed' }, { codeName: 'Button', type: 'matlab.ui.control.Button', prop: 'ButtonPushedFcn' });
  codeCase('loaded from disk, Code ["            x = 1;"]', indentSize, { LoadedFromDisk: true, Code: ['            x = 1;'], Name: 'ButtonPushed' }, { codeName: 'Button', type: 'matlab.ui.control.Button', prop: 'ButtonPushedFcn' });
  codeCase('loaded from disk, CloseRequestFcn Code []', indentSize, { LoadedFromDisk: true, Code: [], Name: 'UIFigureCloseRequest' }, { codeName: 'UIFigure', type: 'matlab.ui.Figure', prop: 'CloseRequestFcn' });
  codeCase('fresh, user already typed (Code differs from default)', indentSize, { Code: ['            y = 2;'] }, { codeName: 'Button', type: 'matlab.ui.control.Button', prop: 'ButtonPushedFcn' });
}

out.modules = report();
fs.writeFileSync(path.join(__dirname, '..', 'templates.json'), JSON.stringify(out, null, 1));
console.log('templates.json written:', out.editable.length, 'editable cases,', out.callbackCode.length, 'callback code cases');
