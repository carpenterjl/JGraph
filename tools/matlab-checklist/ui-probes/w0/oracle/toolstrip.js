// App Designer's toolstrip as R2025b's bundle builds it: node toolstrip.js > ../w0_toolstrip.json
// Each document type (an app, a custom UI component) names its tabs; each tab's _generateTabLayoutConfig and
// _generateStaticPopupsConfig are run unmodified. Items that carry only an actionId take their text,
// description, icon and shortcut from the action of that id (what the toolstrip framework does), so the
// resolved tree is written next to the raw one.
'use strict';
const { makeAppDesignerModel, anything, load } = require('./host');
const { report } = require('./amd');

const ActionIds = load('appdesigner/action/ActionIds');
const ActionManager = load('appdesigner/action/ActionManager');
const appModel = makeAppDesignerModel();
const ads = new ActionManager(appModel).getActionDataService();
const idName = Object.fromEntries(Object.entries(ActionIds).map(([k, v]) => [v, k]));

function plain(v) {
  if (v === null || v === undefined) return v;
  if (typeof v === 'function') return undefined;
  if (Array.isArray(v)) return v.map(plain).filter(x => x !== undefined);
  if (typeof v === 'object') {
    const o = {};
    for (const k of Object.keys(v)) { const p = plain(v[k]); if (p !== undefined) o[k] = p; }
    return o;
  }
  return v;
}

// The toolstrip framework fills an item's missing text, description, icon and shortcut from its action.
function resolve(node) {
  if (Array.isArray(node)) return node.map(resolve);
  if (!node || typeof node !== 'object') return node;
  const r = {};
  for (const k of Object.keys(node)) r[k] = k === 'children' || k === 'popups' ? resolve(node[k]) : node[k];
  if (node.actionId) {
    const a = ads.getAction(node.actionId);
    r.actionName = idName[node.actionId] || null;
    if (!a) r.actionMissing = true;
    else {
      for (const k of ['text', 'description', 'icon', 'shortcut', 'quickAccessIcon', 'selected']) {
        if (r[k] === undefined && a[k] !== undefined) r[k] = a[k];
      }
      if (node.textOverride !== undefined) r.text = node.textOverride;
      if (node.iconOverride !== undefined) r.icon = node.iconOverride;
      r.enabledWithNoApp = a.enabled;
    }
  }
  return r;
}

const uiBuilder = anything('uiBuilder');
const documentTypes = [];
for (const name of ['AppDocumentTypeConfig', 'UserComponentDocumentTypeConfig']) {
  const C = load('appdesigner/view/documenttypeconfigs/' + name);
  const c = new C();
  const tabs = c.getToolstripConfig(uiBuilder, appModel).map(({ tab, isContextual }) => {
    const cfg = tab.getConfig();
    const [layout, popups] = Array.isArray(cfg) ? cfg : [cfg, null];
    return {
      tabTag: tab.getTabTag(),
      isContextual,
      commonOptions: tab.getCommonToolstripOptions(),
      hasRunSection: !!tab._hasRunSection,
      layout: plain(layout),
      popups: plain(popups),
      resolved: { layout: resolve(plain(layout)), popups: resolve(plain(popups)) },
    };
  });
  documentTypes.push({
    config: name,
    documentType: c.getDocumentType(),
    properties: plain(c.getDocumentTypeProperties()),
    panels: plain(c.getPanelChildProperties()),
    tabs,
  });
}

// The quick access bar, built through a builder that records each create.
const qab = [];
const recorder = {
  get: () => 'QABId',
  create: spec => { qab.push(plain(spec)); return 'w' + qab.length; },
};
load('appdesigner/view/toolstrip/QuickAccessBar').buildQAB({}, recorder);

const constants = {
  ToolstripConstants: plain(load('appdesigner/view/toolstrip/ToolstripConstants')),
  ExecutionControlsTags: plain(load('appdesigner/view/editor/plugins/toolstrip/AppDesignerExecutionControlsToolstripTags')),
  UIContainerConstants: plain(load('appdesigner/view/UIContainerConstants')),
};

process.stdout.write(JSON.stringify({
  source: 'R2025b bundle.mwBundle.appdesigner.js, run under node by tools/matlab-checklist/ui-probes/w0/oracle/toolstrip.js',
  documentTypes,
  quickAccessBar: resolve(qab),
  constants,
  modules: report(),
}, null, 1), () => process.exit(0));
