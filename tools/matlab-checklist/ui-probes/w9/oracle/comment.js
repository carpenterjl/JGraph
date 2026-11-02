// What R2025b's editor does for Comment, Uncomment and Wrap Comments, from its own code run unmodified - for the App Designer
// window plan's stage W9b, whose CommentRules transcribe it:
//   node comment.js > ../../../../../tests/JGraph.Tests/Scripting/Editing/CommentOracle.json
//
// Comment and Uncomment: rtc/plugins/language/common/CommentCode's _determineCommentLevel, _commentLines, _uncommentLines and their
// helpers cut out as they are, over a stand-in document of lines and the editor tab service's own tab arithmetic (cut out of
// rtc/plugins/editortabservice/EditorTabService), through 500 line ranges. Wrap Comments: rtc/plugins/language/matlab/commentCode's
// LineCommentSection and BlockCommentSection loaded whole with their events, and WrapComments' _createCommentSections, the caret's
// run of comment lines and _wrapCommentSectionsIfNotEmpty cut out, over stand-in line nodes, through 500 documents.
'use strict';
const path = require('path');
const { load, stub, method } = require(path.join(__dirname, 'amd.js'));

// dojo's declare, as much of it as the sections use: one base, constructors chained base first, this.inherited(arguments).
stub('dojo/_base/declare', function declare(bases, props) {
  if (props === undefined) { props = bases; bases = []; }
  const base = (Array.isArray(bases) ? bases[0] : bases) || null;
  const own = props && props.constructor !== Object.prototype.constructor ? props.constructor : null;
  const C = function (...a) {
    if (base) base.apply(this, a);
    if (own) own.apply(this, a);
  };
  C.prototype = Object.create(base ? base.prototype : Object.prototype);
  C.prototype.constructor = C;
  for (const k of Object.keys(props || {})) {
    if (k === 'constructor') continue;
    const v = props[k];
    C.prototype[k] = typeof v === 'function'
      ? function (...a) { const prev = this.__inherited; this.__inherited = { name: k, base }; try { return v.apply(this, a); } finally { this.__inherited = prev; } }
      : v;
  }
  C.prototype.inherited = function (args) {
    const { name, base: b } = this.__inherited;
    let proto = b && b.prototype;
    while (proto && !Object.prototype.hasOwnProperty.call(proto, name)) proto = Object.getPrototypeOf(proto);
    return proto ? proto[name].apply(this, args) : undefined;
  };
  return C;
});

function rng(seed) { let s = seed >>> 0; return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; }; }
const random = rng(20261102);
const pick = a => a[Math.floor(random() * a.length)];
const chance = p => random() < p;

// The editor tab service: four spaces a tab, spaces inserted.
const Svc = 'rtc/plugins/editortabservice/EditorTabService';
const tabs = { getTabSize: () => 4, getTabCharacter: () => '\t', getSpaceCharacter: () => ' ', isTabKeyInsertSpaces: () => true, getIndentSize: () => 4 };
tabs.getLengthCompensatingTabs = method(Svc, 'getLengthCompensatingTabs', [])().bind(tabs);
tabs.getIndexForLengthCompensatingTabs = method(Svc, 'getIndexForLengthCompensatingTabs', [])().bind(tabs);
tabs.getWhitespaceStringOfLength = method(Svc, 'getWhitespaceStringOfLength', ['l'])({ rep: (c, n) => c.repeat(Math.max(0, Math.floor(n))) }).bind(tabs);

// --- Comment and Uncomment --------------------------------------------------------------------------------------------
const Code = 'rtc/plugins/language/common/CommentCode';
// The module's closure names, in order: m aspect, l StringUtils, o Utils, u AbstractPlugin, d LanguageConstants, r DeletionEvent,
// i CodeUtilities, e DocumentConstants, n coalescing, s event utilities, c Position, g TextNodeUtilities, p, E, C.
const P = ['m', 'l', 'o', 'u', 'd', 'r', 'i', 'e', 'n', 's', 'c', 'g', 'p', 'E', 'C'];
function Position(node, offset) { this.node = node; this.offset = offset; }
const scope = [null, { leftTrim: s => s.replace(/^\s+/, '') }, { isWhitespace: s => /^\s*$/.test(s) }, null, null, { create: () => ({}) },
  { isCodeLine: () => true }, null, null, null, Position, { createTextNodeForPosition: text => ({ text }) }, null, null, null];
const cut = name => method(Code, name, P)(...scope);
const commentFns = {
  _determineCommentLevel: cut('_determineCommentLevel'),
  _commentLines: cut('_commentLines'),
  _uncommentLines: cut('_uncommentLines'),
  _getStringToRemove: cut('_getStringToRemove'),
  _isWhitespaceNode: cut('_isWhitespaceNode'),
  _getLeadingText: cut('_getLeadingText'),
  getCommentAnnotation: cut('getCommentAnnotation'),
  _getCommentString: () => '%',
};

function linesDocument(lines) {
  const self = Object.assign({}, commentFns);
  const node = i => ({ index: i, getCodeText: () => lines[i], children: [{ isOpaqueNode: () => false, getCodeText: () => lines[i] }] });
  self._editorTabService = tabs;
  self._internalDocument = {
    getLineNode: i => node(i),
    deleteTextRange(n, length, offset) { const t = lines[n.index]; lines[n.index] = t.slice(0, offset) + t.slice(offset + length); },
    insertAtPosition(textNode, position) { const t = lines[position.node.index]; lines[position.node.index] = t.slice(0, position.offset) + textNode.text + t.slice(position.offset); return { events: [] }; },
    emitDocumentEvents() {},
  };
  self._document = { getLineText: n => lines[n - 1] };
  return self;
}

const codePool = ['x = 1;', '    y = x + 1;', '        disp(x)', '% a comment', '  % two spaces', '%no space', '    %  wide', '%% section', '%{', '%}', '', '   ', '\t', '\tz = 2;', ' \t w = 3;', '      % deep comment', '%', ' %', 'if x', 'end', '        %% deep section', '  %   spaced'];
const comments = [];
for (let n = 0; n < 500; n++) {
  const count = 1 + Math.floor(random() * 7);
  const original = [];
  for (let k = 0; k < count; k++) original.push(pick(codePool));
  const a = 1 + Math.floor(random() * count), b = 1 + Math.floor(random() * count);
  const startLine = Math.min(a, b), endLine = Math.max(a, b);
  const edit = { appendUndoEvents() {}, appendRedoEvents() {} };
  const forComment = original.slice();
  const c = linesDocument(forComment);
  const level = c._determineCommentLevel(startLine, endLine);
  c._commentLines(startLine, endLine, level, edit);
  const forUncomment = original.slice();
  const u = linesDocument(forUncomment);
  u._uncommentLines(startLine, endLine, edit);
  comments.push({ lines: original, startLine, endLine, level, commented: forComment, uncommented: forUncomment });
}

// --- Wrap Comments ------------------------------------------------------------------------------------------------------
const Utilities = load('rtc/plugins/language/matlab/commentCode/CommentUtilities');
const LineSection = load('rtc/plugins/language/matlab/commentCode/LineCommentSection');
const BlockSection = load('rtc/plugins/language/matlab/commentCode/BlockCommentSection');
const Wrap = 'rtc/plugins/language/matlab/commentCode/WrapComments';
// The module's closure names: m declare, l lang, o nls, u aspect, d AbstractPlugin, r CodeUtilities, i DocumentConstants, e Range,
// n RangeUtilities, s groups, c types, g BlockCommentSection, p CommentUtilities, E LineCommentSection, C MatlabTokenUtilities, v enum.
const WP = ['m', 'l', 'o', 'u', 'd', 'r', 'i', 'e', 'n', 's', 'c', 'g', 'p', 'E', 'C', 'v'];
function Range(start, end) { this.start = start; this.end = end; }
const wrapScope = [null, null, null, null, null, { isCodeContainer: () => true }, null, Range,
  { paragraphNodesInRange: range => range.start.node.all.slice(range.start.node.index, range.end.node.index + 1) }, null, null, BlockSection, Utilities, LineSection, null, null];
const wcut = name => method(Wrap, name, WP)(...wrapScope);
const wrapFns = {
  _createCommentSections: wcut('_createCommentSections'),
  _wrapCommentSectionsIfNotEmpty: wcut('_wrapCommentSectionsIfNotEmpty'),
  _getTargetNodesFromCaret: wcut('_getTargetNodesFromCaret'),
  _isNonEmptyLineComment: wcut('_isNonEmptyLineComment'),
  _findLineCommentStart: wcut('_findLineCommentStart'),
  _findLineCommentEnd: wcut('_findLineCommentEnd'),
  _getLineCommentPrefix: wcut('_getLineCommentPrefix'),
  _findBlockCommentStart: wcut('_findBlockCommentStart'),
  _findBlockCommentEnd: wcut('_findBlockCommentEnd'),
  _updateSelection() {},
};

// Which lines are line comments and which stand in a block comment, as R2025b's tokens would say.
function classify(lines) {
  const kinds = [];
  let inBlock = 0;
  for (const text of lines) {
    const trimmed = text.trim();
    if (inBlock === 0 && /^%\{$/.test(trimmed)) { kinds.push('block'); inBlock = 1; continue; }
    if (inBlock > 0) {
      kinds.push('block');
      if (/^%\{$/.test(trimmed)) inBlock++;
      else if (/^%\}$/.test(trimmed)) inBlock--;
      continue;
    }
    kinds.push(trimmed.startsWith('%') ? 'line' : 'code');
  }
  return kinds;
}

function wrapDocument(lines, caretLine, selection, maxLength, fromCommentStart) {
  const kinds = classify(lines);
  const all = [];
  // R2025b's nodes are live: a section's start line is read after the sections before it were wrapped, so each line
  // remembers its node and a node's line is where its text now stands.
  const owners = [];
  const lineOf = node => { const at = owners.indexOf(node); return (at < 0 ? node.index : at) + 1; };
  for (let i = 0; i < lines.length; i++) {
    const node = {
      index: i, all,
      children: [{ getText: () => lines[i], isOpaqueNode: () => false }],
      getText: () => lines[i],
      startPosition: () => ({ getLine: () => lineOf(node), node }),
      endPosition: () => ({ getLine: () => lineOf(node), node }),
      previousSibling: () => i > 0 ? all[i - 1] : null,
      nextSibling: () => i + 1 < lines.length ? all[i + 1] : null,
      rootContainer: () => node,
    };
    all.push(node);
    owners.push(node);
  }
  const self = Object.assign({}, wrapFns);
  self._editorTabService = tabs;
  self._wrapFromStartOfLineSetting = !fromCommentStart;
  self._maxLengthSetting = maxLength;
  self._view = null;
  self._viewMeasurementService = null;
  self._cursorHasSelection = selection !== null;
  self._isLineComment = node => kinds[node.index] === 'line';
  self._isBlockCommentNode = node => kinds[node.index] === 'block';
  self._cursor = { getNode: () => all[caretLine], getSelection: () => selection ? new Range(all[selection[0]].startPosition(), all[selection[1]].endPosition()) : null, hasSelection: () => selection !== null };
  self._document = {
    startBlockEdit: () => ({}), endBlockEdit() {}, isReadOnly: () => false,
    getLineLength: l => lines[l - 1].length,
    replaceText(text, sl, sc, el, ec) {
      const before = lines[sl - 1].slice(0, sc - 1), after = lines[el - 1].slice(ec - 1);
      const made = (before + text + after).split('\n');
      lines.splice(sl - 1, el - sl + 1, ...made);
      owners.splice(sl - 1, el - sl + 1, ...made.map((_, k) => (k === 0 ? owners[sl - 1] : null)));
    },
    deleteText(sl, sc, el, ec) { this.replaceText('', sl, sc, el, ec); },
    insertText(text, l, c) { this.replaceText(text, l, c, l, c); },
  };
  const target = selection ? wrapScope[8].paragraphNodesInRange(self._cursor.getSelection()) : self._getTargetNodesFromCaret();
  const sections = self._createCommentSections(target);
  self._wrapCommentSectionsIfNotEmpty(sections);
  return sections.length;
}

const words = ['the', 'quick', 'brown', 'fox', 'jumps', 'over', 'a', 'lazy', 'dog', 'MATLAB', 'app', 'component', 'uifigure', 'x', 'callback', 'startupFcn', 'returns', 'value', 'twenty-one', 'supercalifragilisticexpialidociousandthensomemoreletters'];
function sentence(min, max) { const n = min + Math.floor(random() * (max - min + 1)); const out = []; for (let i = 0; i < n; i++) out.push(pick(words)); return out.join(chance(0.1) ? '  ' : ' '); }
const wraps = [];
for (let n = 0; n < 500; n++) {
  const lines = [];
  const count = 1 + Math.floor(random() * 8);
  const prefix = pick(['% ', '% ', '%', '  % ', '%% ', '    %  ', '\t% ']);
  for (let k = 0; k < count; k++) {
    const r = random();
    if (r < 0.5) lines.push(prefix + sentence(1, 30) + (chance(0.15) ? '   ' : ''));
    else if (r < 0.6) lines.push(pick(['% ', '  % ', '%']) + sentence(1, 20));
    else if (r < 0.68) lines.push(prefix + '* ' + sentence(1, 12));
    else if (r < 0.74) lines.push(prefix.trimEnd());
    else if (r < 0.8) lines.push('');
    else if (r < 0.88) lines.push(pick(['x = 1;', '    y = 2; % trailing', 'disp(x)']));
    else { lines.push('%{'); lines.push(sentence(5, 30)); if (chance(0.5)) lines.push('   ' + sentence(1, 20)); lines.push('%}'); }
  }
  const caretLine = Math.floor(random() * lines.length);
  let selection = null;
  if (chance(0.3)) { const a = Math.floor(random() * lines.length), b = Math.floor(random() * lines.length); selection = [Math.min(a, b), Math.max(a, b)]; }
  const maxLength = pick([75, 75, 75, 40, 60, 20]);
  const fromCommentStart = chance(0.25);
  const before = lines.slice();
  let sections, error = null;
  try { sections = wrapDocument(lines, caretLine, selection, maxLength, fromCommentStart); }
  catch (e) { error = String(e.message).slice(0, 80); }
  wraps.push({ lines: before, caretLine, selection, maxLength, fromCommentStart, sections, wrapped: error ? null : lines, error });
}

const out = { comments, wraps };
console.log(JSON.stringify(out));
console.error(`comments ${comments.length}, wraps ${wraps.length} (${wraps.filter(w => w.error).length} threw: ${[...new Set(wraps.filter(w => w.error).map(w => w.error))].join(' | ')})`);
