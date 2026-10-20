// lifecycle.json: App Designer's own lifecycle block, createComponents frame and classdef lines, rendered by
// the real builders/behaviors over a stub CodeModel. Run: node lifecycle.js
'use strict';
const fs = require('fs');
const path = require('path');
const { makeCodeModel, render, load } = require('./stubs');
const { report } = require('./amd');

const GroupBuilder = load('appdesigner/codegen/builders/GroupBuilder');
const out = { lifecycle: [], createComponentsFrame: [], classdef: [], wholeClass: [] };

const startups = [
  { key: 'none', startup: null, inputParameters: '' },
  { key: 'startupFcn', startup: { params: [] }, inputParameters: '' },
  { key: 'startupFcn(a, b)', startup: { params: ['a', 'b'] }, inputParameters: 'a, b' },
];
for (const indentSize of [4, 2])
  for (const singleton of ['', 'FOCUS'])
    for (const su of startups)
      for (const figureCodeName of ['UIFigure', 'MyFig'])
        for (const className of ['app1', 'ReportDialog']) {
          const cm = makeCodeModel({ indentSize, singleton, startup: su.startup, inputParameters: su.inputParameters, figureCodeName, className });
          // AppLifecycleGroupBehavior = the "mlapp-lifecycle" group of MlappClassBehavior (tabDepth 0 -> block at 1).
          const g = new GroupBuilder(cm, { id: 'mlapp-lifecycle', behavior: cm.getCodeEngineConfig().get('AppLifecycleGroupBehavior') });
          out.lifecycle.push({ indentSize, singleton: singleton || 'NONE', startup: su.key, figureCodeName, className, lines: render(cm, g) });
        }

for (const indentSize of [4, 2])
  for (const figureCodeName of ['UIFigure', 'MyFig']) {
    const cm = makeCodeModel({ indentSize, figureCodeName });
    // AppMethodsGroupBehavior = the "mlapp-methods" group: createComponents block + trailing blank line.
    const g = new GroupBuilder(cm, { id: 'mlapp-methods', behavior: cm.getCodeEngineConfig().get('AppMethodsGroupBehavior') });
    out.createComponentsFrame.push({ indentSize, figureCodeName, lines: render(cm, g) });
  }

const helps = [
  { key: 'none', help: null },
  { key: 'summary only', help: { summary: 'My app', description: '' } },
  { key: 'summary+description', help: { summary: 'My app', description: 'It does things and this description is long enough to be wrapped by the comment wrapper at the right hand limit of seventy five characters' } },
  { key: 'description only', help: { summary: '', description: 'Only a description' } },
  { key: 'long summary', help: { summary: 'A summary that is itself long enough that the first help line must wrap onto a second line of comment', description: '' } },
  { key: 'description with newline', help: { summary: 'S', description: 'First paragraph line\nSecond paragraph line' } },
];
for (const indentSize of [4, 2])
  for (const className of ['app1', 'ReportDialog'])
    for (const h of helps) {
      const cm = makeCodeModel({ indentSize, className, help: h.help });
      const ClassBuilder = load('appdesigner/codegen/builders/ClassBuilder');
      // Exactly what MlappFileContentGroupBehavior does (note its `tabdepth` key is a typo, so tabDepth stays 0).
      const cb = new ClassBuilder(cm, { behavior: cm.getCodeEngineConfig().get('ClassBehavior', cm.fileFormat), tabdepth: 0, id: 'classdef-root' });
      out.classdef.push({ indentSize, className, help: h.key, startLines: cm.getCodeFormatter().createAppCode(cb.getStartCode()).split('\n'), endLines: cm.getCodeFormatter().createAppCode(cb.getEndCode()).split('\n') });
    }

// Whole-class renders through the engine's root group ("AppFileBehavior" -> MlappFileContentGroupBehavior ->
// ClassBuilder -> MlappClassBehavior's five groups), for a figure-only app.
for (const c of [
  { indentSize: 4, className: 'app1', singleton: '', startup: null, inputParameters: '' },
  { indentSize: 4, className: 'app1', singleton: 'FOCUS', startup: { params: ['a', 'b'] }, inputParameters: 'a, b', help: { summary: 'My app', description: 'Does things' } },
  { indentSize: 2, className: 'ReportDialog', singleton: '', startup: { params: [] }, inputParameters: '', figureCodeName: 'MyFig' },
]) {
  const cm = makeCodeModel(c);
  const root = new GroupBuilder(cm, { behavior: cm.getCodeEngineConfig().get('AppFileBehavior', cm.fileFormat), tabDepth: 0, id: 'builder-root' });
  out.wholeClass.push(Object.assign({}, c, { singleton: c.singleton || 'NONE', startup: c.startup ? 'startupFcn(' + c.startup.params.join(', ') + ')' : 'none', lines: render(cm, root) }));
}

out.modules = report();
fs.writeFileSync(path.join(__dirname, '..', 'lifecycle.json'), JSON.stringify(out, null, 1));
console.log('lifecycle.json written:', out.lifecycle.length, 'lifecycle cases,', out.createComponentsFrame.length, 'frames,', out.classdef.length, 'classdef cases,', out.wholeClass.length, 'whole classes');
