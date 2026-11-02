// What R2025b's editor does with Find and Replace, bookmarks and Go To's line, from its own code run unmodified - for the App
// Designer window plan's stage W9a, whose TextSearch, FindHistory, BookmarkRules and GoToLineRules transcribe it:
//   node find.js > ../../../../../tests/JGraph.Tests/Scripting/Editing/EditorToolsOracle.json
//
// The searches: rtc/plugins/find/FindUtilities loaded whole (with FindConfiguration, BinarySearchUtility, FindResult and
// FindPreference) over a stand-in document of lines, through 700 searches with every option, each stepped on from its
// result forward and backward with wrap-around on and off. The replacements: FindReplaceActions._processCaptureGroupsInRegexReplaceText
// cut out as it is. The history and the seed: FindPreference's own. The bookmarks: Bookmarks._toggleForNode and _jumpToNextBookmark
// cut out over stand-in line nodes. Go To's line: GotoDialog._validateInput's test on Number().
'use strict';
const path = require('path');
const { load, method } = require(path.join(__dirname, 'amd.js'));

const FindUtilities = load('rtc/plugins/find/FindUtilities');
const FindPreference = load('rtc/plugins/find/FindPreference');

function rng(seed) { let s = seed >>> 0; return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; }; }
const random = rng(20261101);
const pick = a => a[Math.floor(random() * a.length)];
const chance = p => random() < p;

const pool = [
  "classdef W8bApp < matlab.apps.AppBase",
  "    % Properties that correspond to app components",
  "    properties (Access = public)",
  "        UIFigure  matlab.ui.Figure",
  "    end",
  "",
  "        function startupFcn(app, n)",
  "            app.UIFigure.Name = sprintf('n is %d', n);",
  "            app.UIFigure = uifigure('Visible', 'off');",
  "            app.UIFigure.Position = [100 100 640 480];",
  "            app.UIFigure.Name = 'MATLAB App';",
  "    x = app.Value + app.value + App.VALUE;",
  "% Comment: Value VALUE value values",
  "    y = x.*x - foo(x, bar) + foo_bar - foo.bar(fooBar);",
  "if n > 1 && n < 10 || n == 100",
  "    disp(sprintf('n is %d', n)); % n",
  "    [a, b] = size(app.Table.Data);",
  "app.Slider.Value = 5; app.Slider.Limits = [0 10];",
  "\tz = 'app'; % app app",
  "aaa aa a aaaa  a",
  "x1 = x2 + x10 - 2*x1;",
  "    end",
  "end",
  "        createComponents(app)",
  "            registerApp(app, app.UIFigure)",
  "    name = \"an app\"; path = 'C:\\apps\\app.m';",
  "(a) (b) ((c)) [d] {e}",
  "Aa aA AA aa",
];
const queries = [
  'app', 'App', 'APP', 'a', 'aa', 'Value', 'value', 'n', 'x', 'foo', 'bar', 'foo.bar', 'foo_bar', 'end', '=', ' ', '%', '(', ')', 'app.',
  '.Value', 'x1', 'UIFigure', 'is', "'", 'ab', 'aaa', 'Aa', 'n is', 'sprintf(', '\\', ',', '10', '100',
];
const regexes = [
  '\\d+', 'a+', '[a-z]+\\.', '(foo|bar)', 'app\\.(\\w+)', '^\\s*%', 'x$', 'ap*', '.', 'n\\b', '\\bapp\\b', 'a{2}', '[A-Z]\\w*', '(x)(\\d)', '\\s+', '^end',
  '(', '\\', '[', 'a**', '(?<n>a)', 'a\\nb', '\\r', '', 'app|App', '[(]', 'V.lue',
];
const replacements = ['X', '', 'app', '$1', '$2$1', '<$1>', '$&', '$$', 'a\\\\b', '$0', '$3', '\\$1'];

function document(lines) {
  return { getMaxLineNumber: () => lines.length, getLineNode: i => ({ getTextForPositioning: () => lines[i] }) };
}
function occurrences(result) {
  return result.getAllOccurrences().map(o => ({ line: o.line, start: o.startColumn, end: o.endColumn, searchIndex: o.searchIndex === undefined ? -1 : o.searchIndex }));
}

// --- searches -----------------------------------------------------------------------------------------------------
const searches = [];
for (let n = 0; n < 700; n++) {
  const count = 3 + Math.floor(random() * 10);
  const lines = [];
  for (let k = 0; k < count; k++) lines.push(pick(pool));
  const regular = chance(0.35);
  const text = regular ? pick(regexes) : pick(queries);
  const options = {
    startAt: { line: Math.floor(random() * count), column: Math.floor(random() * 30) },
    backward: chance(0.3),
    caseSensitive: chance(0.4),
    wholeWords: chance(0.3),
    regularExpression: regular,
    deadEnd: 19999,
  };
  if (chance(0.25)) {
    const a = Math.floor(random() * count), b = Math.floor(random() * count);
    options.findInSelection = true;
    options.findInSelectionStartLine = Math.min(a, b);
    options.findInSelectionEndLine = Math.max(a, b);
  }
  const wrap = chance(0.7);
  FindPreference.setWrapAroundState(wrap);
  let result;
  try {
    result = FindUtilities.find(document(lines), text, options, null, undefined, null);
  } catch (e) {
    searches.push({ lines, text, options, wrap, error: true });
    continue;
  }
  const entry = { lines, text, options, wrap, found: occurrences(result), current: result.getCurrentOccurrenceIndex() };
  // Stepped on from this result, as Find Next and Find Previous do from the cache, with wrap-around on and off.
  if (result.hasCurrentOccurrence()) {
    entry.steps = [];
    for (const backward of [false, true]) {
      for (const w of [true, false]) {
        FindPreference.setWrapAroundState(w);
        const next = FindUtilities.find(document(lines), text, Object.assign({}, options, { backward }), result, undefined, null);
        entry.steps.push({ backward, wrap: w, current: next.getCurrentOccurrenceIndex() });
      }
    }
  }
  searches.push(entry);
}

// --- replacements with capture groups -----------------------------------------------------------------------------
const processCaptures = method('rtc/plugins/find/FindReplaceActions', '_processCaptureGroupsInRegexReplaceText', [])();
const replaces = [];
for (let n = 0; n < 300; n++) {
  const line = pick(pool);
  const pattern = pick(regexes.filter(r => !['(', '\\', '[', 'a**', 'a\\nb', '\\r', ''].includes(r)));
  const caseSensitive = chance(0.5);
  FindPreference.setWrapAroundState(true);
  let result;
  try {
    result = FindUtilities.find(document([line]), pattern, { startAt: { line: 0, column: 0 }, regularExpression: true, caseSensitive, deadEnd: 19999 }, null, undefined, null);
  } catch (e) { continue; }
  const all = result.getAllOccurrences();
  if (!all.length) continue;
  const occurrence = Object.assign({}, pick(all));
  const replaceWith = pick(replacements);
  const regex = new RegExp(pattern, caseSensitive ? '' : 'i');
  let text;
  try {
    text = processCaptures.call({ _document: { getLineText: () => line } }, regex, occurrence, replaceWith);
  } catch (e) { continue; }
  replaces.push({ line, pattern, caseSensitive, occurrence: { line: 0, start: occurrence.startColumn, end: occurrence.endColumn, searchIndex: occurrence.searchIndex }, replaceWith, text });
}

// --- the history and the seed --------------------------------------------------------------------------------------
const histories = [];
for (let n = 0; n < 200; n++) {
  FindPreference.cleanFindHistory();
  const steps = [];
  const count = 1 + Math.floor(random() * 30);
  for (let k = 0; k < count; k++) {
    const text = chance(0.1) ? '' : pick(queries.concat(['q' + Math.floor(random() * 30)]));
    FindPreference.setFindHistory(text);
    steps.push({ text, history: FindPreference.getFindHistory().slice(), latest: FindPreference.getFindText(), menu: FindPreference.getFindMenuItems().map(i => i.value) });
  }
  histories.push(steps);
}
const seeds = [];
for (const selected of [null, '', 'app', '\napp', '\n\napp\nx', 'app\nx', '  app  ', '\n', '\n\n', ' \nx', 'a\n', '\n \n']) {
  seeds.push({ selected, seed: FindPreference.getFirstNonBlankText(selected) });
}

// --- bookmarks ----------------------------------------------------------------------------------------------------
const Bookmarks = 'rtc/plugins/bookmarks/Bookmarks';
const toggleForNode = method(Bookmarks, '_toggleForNode', [])();
const jump = method(Bookmarks, '_jumpToNextBookmark', [])();
const inRange = method(Bookmarks, '_findNextBookmarkInLineRange', [])();
const isIn = method(Bookmarks, '_isNodeInBookmarks', [])();
const bookmarks = [];
for (let n = 0; n < 300; n++) {
  const count = 2 + Math.floor(random() * 12);
  const blank = [];
  const nodes = [];
  for (let k = 0; k < count; k++) { const b = chance(0.2); blank.push(b); nodes.push({ firstChild: () => !b }); }
  let cursor = Math.floor(random() * count);
  const start = cursor;
  let moved = null;
  const self = {
    _bookmarks: [],
    _internalDocument: { root: { children: nodes } },
    _cursor: { getNode: () => ({ rootContainer: () => nodes[cursor] }), collapseSelection() {}, setPosition(g) { moved = nodes.indexOf(g); } },
    _rtcView: { onLayoutChange() {} },
    _updateActions() {}, _toogleBookmarksMargin() {}, _saveBookmarks() {}, onNavigation() {},
    _findNextBookmarkInLineRange: inRange, _isNodeInBookmarks: isIn,
  };
  const ops = [];
  const steps = 1 + Math.floor(random() * 12);
  for (let k = 0; k < steps; k++) {
    const kind = pick(['toggle', 'toggle', 'next', 'previous', 'move']);
    if (kind === 'toggle') {
      const at = Math.floor(random() * count);
      toggleForNode.call(self, nodes[at]);
      ops.push({ op: 'toggle', at, marked: self._bookmarks.map(b => nodes.indexOf(b)).sort((a, b) => a - b) });
    } else if (kind === 'move') {
      cursor = Math.floor(random() * count);
      ops.push({ op: 'move', at: cursor });
    } else {
      moved = null;
      if (self._bookmarks.length !== 0) jump.call(self, kind === 'previous');
      if (moved !== null) cursor = moved;
      ops.push({ op: kind, to: moved });
    }
  }
  bookmarks.push({ blank, start, ops });
}

// --- Go To's line ---------------------------------------------------------------------------------------------------
const gotoLines = [];
for (const text of ['1', '0', '-1', '7', '3.0', '3.5', ' 5 ', '5e0', '1e2', '0x5', '0b11', '0o7', '', ' ', 'abc', '12abc', 'Infinity', '1,000', '+4', '4.', '.5', '1e-1', '007', '9999999999', '1_000', '0.0', '\t6\n']) {
  const value = Number(text);
  gotoLines.push({ text, valid: value > 0 && Number.isInteger(value), value: Number.isFinite(value) ? value : null });
}

const out = { searches, replaces, histories, seeds, bookmarks, gotoLines };
console.log(JSON.stringify(out));
console.error(`searches ${searches.length} (${searches.filter(s => s.error).length} refused), replaces ${replaces.length}, histories ${histories.length}, bookmarks ${bookmarks.length}, gotoLines ${gotoLines.length}`);
