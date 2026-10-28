// A tiny loader that runs App Designer's own AMD modules from R2025b's bundle under node.
// Modules are taken raw from the bundle (never the line-broken copies, which change template strings).
// Extends ds4notes/responsive/oracle/amd.js with:
//   - both module-head forms: `"name":(h,v,t)=>{` and `"name":function(h,v,t){` (7 modules use the second,
//     among them uicomponents_appdesigner_plugin/model/_PanelModel);
//   - a generic `dojo/i18n!<nls>` rule: load(<nls>).root (the bundle's nls modules export {root:{...}});
//   - stubWhere(predicate, factory) for whole families of UI modules the model mixins pull in.
'use strict';
const fs = require('fs');

const BUNDLE = 'C:/Program Files/MATLAB/R2025b/toolbox/matlab/appdesigner/web/release/bundle.mwBundle.appdesigner.js';
const src = fs.readFileSync(BUNDLE, 'utf8');
const headRe = /"([A-Za-z0-9_\/\-\.!]+)":(?:\(\w,\w,\w\)=>|function\(\w,\w,\w\))\{/g;
const heads = [];
let m;
while ((m = headRe.exec(src)) !== null) {
  heads.push({ name: m[1], at: m.index, bodyAt: m.index + 1 + m[1].length + 2 });
}
const byName = new Map();
for (let i = 0; i < heads.length; i++) {
  const end = i + 1 < heads.length ? heads[i + 1].at : src.length;
  byName.set(heads[i].name, { start: heads[i].bodyAt, end });
}

const stubs = new Map();
const stubRules = [];
const cache = new Map();
const loadedReal = new Set();
const stubbedUsed = new Set();
const chain = [];
const fellBack = new Set();
let fallback = null;
// A module matching test that fails to evaluate or to load is replaced by make(name) and reported.
function setFallback(test, make) { fallback = { test, make }; }

function moduleText(name) {
  const r = byName.get(name);
  if (!r) return null;
  let text = src.slice(r.start, r.end).trimEnd();
  if (text.endsWith(',')) text = text.slice(0, -1);
  return text;
}

function load(name) {
  if (stubs.has(name)) { stubbedUsed.add(name); return stubs.get(name); }
  if (cache.has(name)) return cache.get(name);
  if (name.startsWith('dojo/i18n!')) {
    const nls = load(name.slice('dojo/i18n!'.length));
    const v = nls && nls.root ? nls.root : nls;
    cache.set(name, v);
    return v;
  }
  for (const rule of stubRules) {
    if (rule.test(name)) {
      const v = rule.make(name);
      stubs.set(name, v);
      stubbedUsed.add(name);
      return v;
    }
  }
  const text = moduleText(name);
  if (text === null) throw new Error('module not in bundle and not stubbed: ' + name);
  let fn;
  try { fn = (0, eval)('(' + text + ')'); }
  catch (e) { if (fallback && fallback.test(name)) { const v = fallback.make(name); stubs.set(name, v); fellBack.add(name); return v; } throw e; }
  const mod = { exports: {} };
  const t = function (dep) { return load(dep); };
  t.dj = {
    r: { absMids: new Proxy({}, { get: (_, k) => k }) },
    d: function (deps, factory) { return factory.apply(null, deps); },
    c: function () { return {}; },
  };
  cache.set(name, undefined);
  chain.push(name);
  try {
    fn(mod, mod.exports, t);
  } catch (e) {
    if (fallback && fallback.test(name) && !e.amdChain) { chain.pop(); const v = fallback.make(name); stubs.set(name, v); cache.delete(name); fellBack.add(name); chain.push(name); return v; }
    if (!e.amdChain) { e.amdChain = chain.slice(); e.message += ' | while loading: ' + chain.join(' -> '); }
    throw e;
  } finally { chain.pop(); }
  cache.set(name, mod.exports);
  loadedReal.add(name);
  return mod.exports;
}

function stub(name, value) { stubs.set(name, value); }
function stubWhere(test, make) { stubRules.push({ test, make }); }
function report() { return { real: [...loadedReal].sort(), stubbed: [...stubbedUsed].sort(), fellBack: [...fellBack].sort() }; }

module.exports = { load, stub, stubWhere, setFallback, moduleText, report };
