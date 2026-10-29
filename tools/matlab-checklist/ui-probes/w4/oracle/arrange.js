// What R2025b's App Designer does to the selection's positions when it aligns, sizes and spaces
// components, from its own modules run unmodified (appdesigner/command/Alignment, SameSize and
// Spacing, which need nothing but the peer nodes they are given) over seeded-random scenes - for the
// App Designer window plan's stage W4, whose DesignArrange transcribes them:
//   node arrange.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignArrangeOracle.json
// A peer node here holds what the three read and write: Location and Size (MATLAB's pixels, y up),
// IsSizeFixed, AspectRatioLimits, its adapter's MinimumSize, and its index among its parent's
// children; move() is DesignerWidgetData.move, setProperties sets what it is given.
'use strict';
const { load } = require('../../w0/oracle/amd');

const Alignment = load('appdesigner/command/Alignment');
const SameSize = load('appdesigner/command/SameSize');
const Spacing = load('appdesigner/command/Spacing');

let seed = 20261029;
function rand() {
  seed = (seed * 1103515245 + 12345) % 2147483648;
  return seed / 2147483648;
}
const int = (lo, hi) => lo + Math.floor(rand() * (hi - lo + 1));
const pick = list => list[Math.floor(rand() * list.length)];

function node(spec, parent) {
  const n = {
    props: { Location: [spec.x, spec.y], Size: [spec.w, spec.h], IsSizeFixed: spec.fixed, AspectRatioLimits: spec.ratio ? [1, 1] : [0, 'Inf'] },
    ComponentAdapter: { MinimumSize: spec.min },
    getProperty(name) { return this.props[name]; },
    setProperties(values) { Object.assign(this.props, values); },
    move(dx, dy) { const l = this.props.Location; this.props.Location = [l[0] + dx, l[1] + dy]; },
    getParent() { return parent; },
  };
  return n;
}

function scene(count, sizing) {
  const parent = { children: [], getChildren() { return this.children; } };
  const specs = [];
  for (let i = 0; i < count; i++) {
    // Positions sometimes shared, so the sorts' ties are reached.
    const x = rand() < 0.2 && specs.length ? specs[int(0, specs.length - 1)].x : int(1, 500);
    const y = rand() < 0.2 && specs.length ? specs[int(0, specs.length - 1)].y : int(1, 400);
    const odd = rand() < 0.3;
    specs.push({
      x, y,
      w: odd ? int(1, 300) + 0.5 : int(1, 300),
      h: odd ? int(1, 200) : int(1, 200),
      fixed: sizing ? pick([[false, false], [false, false], [false, false], [true, false], [false, true], [true, true]]) : [false, false],
      ratio: sizing && rand() < 0.25,
      min: [int(5, 60), int(5, 40)],
    });
  }
  const nodes = specs.map(s => node(s, parent));
  // Their order among the parent's children, shuffled: the stack order the spacing's ties fall back on.
  parent.children = nodes.slice().sort(() => rand() - 0.5);
  return { specs, nodes, z: nodes.map(n => parent.children.indexOf(n)) };
}

const boxes = nodes => nodes.map(n => [n.props.Location[0], n.props.Location[1], n.props.Size[0], n.props.Size[1]]);
const cases = [];

for (let i = 0; i < 1200; i++) {
  const { specs, nodes } = scene(int(1, 6), false);
  const kind = int(0, 6); // 6: not an alignment
  const anchor = rand() < 0.15 ? -1 : int(0, nodes.length - 1);
  const before = boxes(nodes);
  const done = Alignment.alignWidgets(kind, nodes, anchor < 0 ? undefined : nodes[anchor]);
  cases.push({ op: 'align', kind, anchor, boxes: before, done, after: boxes(nodes) });
}

for (let i = 0; i < 1200; i++) {
  const { specs, nodes } = scene(int(1, 6), true);
  const kind = pick(['size', 'width', 'height', 'SIZE', 'other']);
  const anchor = rand() < 0.15 ? -1 : int(0, nodes.length - 1);
  const before = boxes(nodes);
  const done = SameSize.makeSameSize(kind, nodes, anchor < 0 ? undefined : nodes[anchor]);
  cases.push({
    op: 'samesize', kind, anchor, boxes: before,
    fixed: specs.map(s => s.fixed), ratio: specs.map(s => s.ratio), min: specs.map(s => s.min),
    done, after: boxes(nodes),
  });
}

for (let i = 0; i < 1200; i++) {
  const { nodes, z } = scene(int(1, 7), false);
  const kind = pick(['horiz-auto', 'vert-auto', 'horiz-manual', 'vert-manual', 'other']);
  const gap = pick([0, 5, 20, 20, 33, -4]);
  const before = boxes(nodes);
  const changed = Spacing.spaceWidgets(kind, gap, nodes);
  cases.push({ op: 'space', kind, gap, z, boxes: before, changed, after: boxes(nodes) });
}

process.stdout.write(JSON.stringify({
  source: 'R2025b appdesigner/command/Alignment, SameSize, Spacing, run by ui-probes/w4/oracle/arrange.js',
  cases,
}) + '\n');
