// What R2025b's toolstrip does when a tab's width changes, from its own Tab.resize run unmodified
// (MW/toolstrip/widgets/Tab), over seeded-random tabs of model sections - for the App Designer
// window plan's stage W1, whose ToolstripResizer transcribes it:
//   node resize.js > ../../../../../tests/JGraph.Tests/Scripting/Toolstrip/ToolstripResizeOracle.json
// A model section has three widths - with its labels, without them, collapsed to its drop-down - and a
// collapse priority; Section.collapse/expand/toggleDescendantLabels are modelled by those widths (the
// DOM work they do is not the resize's). Each scenario starts with every section expanded and
// labelled and resizes once per width of a random walk; the states after each resize are recorded.
'use strict';
const { load, stub } = require('./amd');

stub('dojo/_base/declare', (bases, props) => props);
stub('dojo/dom-construct', {});
stub('dijit/layout/ContentPane', {});
stub('dijit/registry', {});
stub('dojo/query', () => []);
stub('dojo/dom-geometry', { position: node => ({ w: node.w }) });
stub('MW/toolstrip/widgets/_WidgetMixin', {});
stub('MW/toolstrip/mixins/property/RemovableMixin', {});
const Tab = load('MW/toolstrip/widgets/Tab');

// A seeded generator, so the oracle is the same on every run.
let seed = 20261028;
function rand() {
  seed = (seed * 1103515245 + 12345) % 2147483648;
  return seed / 2147483648;
}
const int = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));

function section(spec) {
  const s = {
    spec,
    collapsePriority: spec.priority,
    collapsed: false,
    labelsHidden: false,
    collapse() {
      if (this.collapsePriority === -1) { this._isCollapsed = true; return true; }
      const changed = !this.collapsed;
      this.collapsed = true;
      return changed;
    },
    expand() {
      if (this.collapsePriority === -1) return true;
      const changed = this.collapsed;
      this.collapsed = false;
      return changed;
    },
    toggleDescendantLabels(show) { this.labelsHidden = !show; },
  };
  s.domNode = { get w() { return s.collapsed ? spec.collapsed : s.labelsHidden ? spec.bare : spec.full; } };
  return s;
}

const scenarios = [];
for (let k = 0; k < 400; k++) {
  const n = int(1, 7);
  const specs = [];
  for (let i = 0; i < n; i++) {
    const full = int(40, 260);
    const bare = rand() < 0.3 ? full : full - int(1, Math.floor(full * 0.4));
    const r = rand();
    specs.push({ full, bare, collapsed: int(36, 72), priority: r < 0.75 ? 0 : r < 0.85 ? int(1, 3) : r < 0.93 ? -1 : int(-1, 2) });
  }
  const sections = specs.map(section);
  const tab = Object.assign(Object.create(Tab), {
    domNode: { w: 0 },
    getParent: () => ({ getParent: () => ({ _startupComplete: true, _ignoreResize: false }) }),
    inherited() {},
    getChildren: () => sections.slice(),
    _isStaticDisplayState: () => false,
    _isCompactDisplayMode: () => false,
  });
  const total = specs.reduce((t, s) => t + s.full, 0) + 20;
  const steps = [];
  let width = total + int(-40, 40);
  for (let j = 0; j < 12; j++) {
    width = Math.max(30, Math.min(total + 120, width + int(-Math.ceil(total / 3), Math.ceil(total / 3))));
    if (j % 4 === 3) width = int(30, total + 120);
    tab.domNode.w = width;
    tab.resize();
    steps.push({
      available: width,
      states: sections.map(s => (s.collapsed ? 'c' : 'e') + (s.collapsed ? '' : s.labelsHidden ? '-' : '+')),
    });
  }
  scenarios.push({ sections: specs, steps });
}

process.stdout.write(JSON.stringify({
  source: 'R2025b bundle.mwBundle.appdesigner.js MW/toolstrip/widgets/Tab.resize, run under node by tools/matlab-checklist/ui-probes/w0/oracle/resize.js',
  collapseButtonWidth: 20,
  scenarios,
}) + '\n');
