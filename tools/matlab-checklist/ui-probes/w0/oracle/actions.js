// Every action App Designer's ActionManager registers, as R2025b's bundle defines it: node actions.js > out.json
'use strict';
const { makeAppDesignerModel, settingsRead, load } = require('./host');
const { report } = require('./amd');

const ActionIds = load('appdesigner/action/ActionIds');
const ActionManager = load('appdesigner/action/ActionManager');
const am = new ActionManager(makeAppDesignerModel());
const ads = am.getActionDataService();
const idName = Object.fromEntries(Object.entries(ActionIds).map(([k, v]) => [v, k]));

function plain(v) {
  if (v === null || v === undefined) return v;
  if (typeof v === 'function') return undefined;
  if (Array.isArray(v)) return v.map(plain);
  if (typeof v === 'object') {
    const o = {};
    for (const k of Object.keys(v)) { const p = plain(v[k]); if (p !== undefined) o[k] = p; }
    return o;
  }
  return v;
}
const actions = ads.getAllActions().map(a => Object.assign({ name: idName[a.id] || null }, plain(a)));
const unregistered = Object.entries(ActionIds).filter(([, v]) => !ads.actions.has(v)).map(([k, v]) => ({ name: k, id: v }));
process.stdout.write(JSON.stringify({ actions, unregistered, settingsRead, modules: report() }, null, 1), () => process.exit(0));
