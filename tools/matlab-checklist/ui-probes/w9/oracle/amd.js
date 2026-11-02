// A tiny loader that runs the rich text component's own AMD modules from R2025b's bundle under node, for the App
// Designer window plan's stage W9 (the editor's tools). The same loader as ../../w0/oracle/amd.js over another bundle:
// the editor's modules live in toolbox/matlab/rich_text_component/release/bundle.mwBundle.rtc_R2025b.js, not in App
// Designer's. W9_BUNDLE names another bundle.
'use strict';
const fs = require('fs');

const BUNDLE = process.env.W9_BUNDLE || 'C:/Program Files/MATLAB/R2025b/toolbox/matlab/rich_text_component/release/bundle.mwBundle.rtc_R2025b.js';
const src = fs.readFileSync(BUNDLE, 'utf8');
const headRe = /"([A-Za-z0-9_\/\-\.!@]+)":(?:\(\w,\w,\w\)=>|function\(\w,\w,\w\))\{/g;
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
  const fn = (0, eval)('(' + text + ')');
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
    if (!e.amdChain) { e.amdChain = chain.slice(); e.message += ' | while loading: ' + chain.join(' -> '); }
    throw e;
  } finally { chain.pop(); }
  cache.set(name, mod.exports);
  loadedReal.add(name);
  return mod.exports;
}

function stub(name, value) { stubs.set(name, value); }
function stubWhere(test, make) { stubRules.push({ test, make }); }
function report() { return { real: [...loadedReal].sort(), stubbed: [...stubbedUsed].sort() }; }

// A method cut out of a module's text as it is, its free names given as parameters: method(module, name, ['a','b'])(valueOfA, valueOfB) is the function.
function method(module, name, parameters) {
  const text = moduleText(module);
  const found = new RegExp(name + '(:function)?\\([^()]*\\)\\{').exec(text);
  if (!found) throw new Error('no ' + name + ' in ' + module);
  const head = found.index;
  const open = text.indexOf('{', text.indexOf(')', head));
  let depth = 0; let i = open; let quote = null;
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

module.exports = { load, stub, stubWhere, moduleText, method, report };
