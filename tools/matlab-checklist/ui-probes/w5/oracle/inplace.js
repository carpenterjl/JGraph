// Runs R2025b's own in-place hotspot rules - each multi-hotspot controller's _getEditableHotspotIndex
// and getEditorOptions, cut out of App Designer's client bundle as they are - over every orientation,
// scale direction and way in (a key, Tab and Shift+Tab from each place), and writes what each chose:
// the place, the property it edits and the text's alignment. DesignInPlaceOracleTests replays them.
//
//   node inplace.js <out.json>
//
// The functions read only the peer node's properties and the hotspots' rectangles, which are given
// here: so many rectangles side by side (a discrete knob's per item, a table's per column, inside its
// box).
const fs = require('fs');
const path = require('path');
const { moduleText } = require(path.join(__dirname, '..', '..', 'w0', 'oracle', 'amd.js'));

// The function named `name` in a module, as `function(args){body}`: its parameter list and its
// balanced body, whichever form the class wrote it in (`name(a,b){` or `name:function(a,b){`).
function method(module, name) {
  const text = moduleText(module);
  const re = new RegExp('[,{}]' + name + '(?::function)?\\(([\\w,]*)\\)\\{');
  const m = re.exec(text);
  if (!m) throw new Error(module + ' has no ' + name);
  let i = m.index + m[0].length - 1, d = 0, q = null;
  const start = i;
  for (; i < text.length; i++) {
    const c = text[i];
    if (q) { if (c === '\\') { i++; continue; } if (c === q) q = null; continue; }
    if (c === '"' || c === "'" || c === '`') { q = c; continue; }
    if (c === '{') d++;
    else if (c === '}') { d--; if (d === 0) break; }
  }
  // eslint-disable-next-line no-new-func
  return new Function(...m[1].split(',').filter(Boolean), text.slice(start + 1, i));
}

const P = 'uicomponents_appdesigner_plugin/controller/';
const KINDS = [
  { type: 'matlab.ui.control.Slider', ctl: 'SliderController', props: { Orientation: ['horizontal', 'vertical'] }, n: 2 },
  { type: 'matlab.ui.control.Knob', ctl: 'ContinuousKnobController', props: {}, n: 2 },
  { type: 'matlab.ui.control.DiscreteKnob', ctl: 'DiscreteKnobController', props: {}, n: [2, 3, 4, 5] },
  { type: 'matlab.ui.control.Gauge', ctl: 'CircularGaugeController', props: { ScaleDirection: ['clockwise', 'counterclockwise'] }, n: 2 },
  { type: 'matlab.ui.control.LinearGauge', ctl: 'LinearGaugeController', props: { Orientation: ['horizontal', 'vertical'] }, n: 2 },
  { type: 'matlab.ui.control.NinetyDegreeGauge', ctl: 'NinetyDegreeGaugeController',
    props: { Orientation: ['northwest', 'northeast', 'southwest', 'southeast'], ScaleDirection: ['clockwise', 'counterclockwise'] }, n: 2 },
  { type: 'matlab.ui.control.SemicircularGauge', ctl: 'SemicircularGaugeController',
    props: { Orientation: ['north', 'south', 'east', 'west'], ScaleDirection: ['clockwise', 'counterclockwise'] }, n: 2 },
  { type: 'matlab.ui.control.Switch', ctl: 'AbstractSwitchController', props: { Orientation: ['horizontal', 'vertical'] }, n: 2 },
  { type: 'matlab.ui.control.Table', ctl: 'TableController', props: {}, n: [1, 3, 4] },
];

function combos(props) {
  let out = [{}];
  for (const [k, vs] of Object.entries(props)) {
    out = out.flatMap(o => vs.map(v => ({ ...o, [k]: v })));
  }
  return out;
}

const scenes = [];
for (const kind of KINDS) {
  const index = method(P + kind.ctl, '_getEditableHotspotIndex');
  const options = method(P + kind.ctl, 'getEditorOptions');
  for (const n of [].concat(kind.n)) {
    // The places side by side, 40 by 20, inside a box that holds them all.
    const rects = Array.from({ length: n }, (_, i) => ({ left: i * 40, top: 0, width: 40, height: 20, right: i * 40 + 40, bottom: 20 }));
    for (const values of combos(kind.props)) {
      const all = { Limits: [0, 100], Items: Array.from({ length: n }, (_, i) => 'I' + i), ColumnName: Array.from({ length: n }, (_, i) => 'C' + i), ...values };
      const self = {
        peernode: { getProperty: k => all[k] },
        getEditableHotspotPositions: () => rects,
        getDesignTimeContainerDomNode: () => ({ getBoundingClientRect: () => ({ left: 0, top: 0, width: n * 40, height: 20 }) }),
      };
      self._getEditableHotspotIndex = index;
      const peer = { getProperty: k => all[k] };
      const ways = [{ way: 'key', initiator: { EventType: 'KeyboardEvent' } }];
      for (let from = 0; from < n; from++) {
        for (const back of [false, true]) {
          ways.push({ way: back ? 'previous' : 'next', from, initiator: { Type: 'StartEditingNextHotspotOnClose', Index: back ? from - 1 : from + 1 } });
        }
      }

      for (const w of ways) {
        const o = options.call(self, peer, w.initiator);
        scenes.push({
          type: kind.type, hotspots: n, orientation: values.Orientation || '', scaleDirection: values.ScaleDirection || '',
          way: w.way, from: w.from === undefined ? -1 : w.from,
          hotspot: o ? o.HotspotIndex === undefined ? 0 : o.HotspotIndex : -1,
          property: o ? o.PropertyName : null, alignment: o ? o.HorizontalAlignment : null,
        });
      }
    }
  }
}

fs.writeFileSync(process.argv[2], JSON.stringify(scenes, null, 1));
console.log(scenes.length + ' scenes');
