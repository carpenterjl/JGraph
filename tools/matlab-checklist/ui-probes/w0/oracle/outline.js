// Prints the resolved toolstrip as an indented outline: node outline.js ../w0_toolstrip.json
'use strict';
const d = JSON.parse(require('fs').readFileSync(process.argv[2], 'utf8'));
const out = [];
function walk(n, ind) {
  if (!n || typeof n !== 'object') return;
  const bits = [n.type, n.tag ? '#' + n.tag : '', n.title ? 'title=' + JSON.stringify(n.title) : '',
    n.text ? JSON.stringify(n.text) : '', n.actionName || '', n.shortcut ? '[' + n.shortcut + ']' : '',
    n.icon ? '<' + n.icon + '>' : '', n.popupTag ? '->' + n.popupTag : '', n.actionMissing ? 'ACTION MISSING' : ''].filter(Boolean);
  out.push(' '.repeat(ind) + bits.join(' '));
  for (const c of n.children || []) walk(c, ind + 2);
  for (const c of n.popups || []) walk(c, ind + 2);
}
for (const dt of d.documentTypes) {
  out.push('=== ' + dt.config + ' ' + JSON.stringify(dt.properties.contexts));
  for (const t of dt.tabs) {
    out.push('--- tab ' + t.tabTag + (t.isContextual ? ' (contextual)' : ''));
    walk(t.resolved.layout, 2);
    if (t.resolved.popups) walk(t.resolved.popups, 2);
  }
}
out.push('=== QAB');
for (const q of d.quickAccessBar) walk(q, 2);
console.log(out.join('\n'));
