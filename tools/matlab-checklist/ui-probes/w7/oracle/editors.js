// What R2025b's App Designer's inspector editors do, from their own code run unmodified - for the App Designer window
// plan's stage W7c, whose DesignEditors transcribes it:
//   node editors.js > ../../../../../tests/JGraph.Tests/Scripting/Designer/DesignEditorsOracle.json
//
// Loaded whole, over stand-ins for the data store and for MATLAB: rendererseditors/utils/UniqueNameGenerator, ItemsEditor/ItemsModel,
// TicksEditor/TickDialogTableModel, ScaleColorsEditor/ScaleColorsModel, DisplayFormatEditor/DisplayFormatMode,
// UITableColumnEditor/UITableColumnEditorModel and ...TableModel, DateTableEditor/DateModel, and
// appdesigner_interface/command/PropertyCommandFactory.
//
// Cut out of the bundle as they are: ItemsEditor._handleModelUpdate, _PSTableController's button states, _PSTable's rows to select,
// TicksDialogEditor._handleModelUpdate and _flagInvalidNumericTicks, UITableColumnEditor._updateBeforeSendToServer and what it calls,
// PropertyFieldsEditor._isValidationError, and AbstractDesignTimeController.setComponentToPreferredSize with what it calls.
//
// A scene's random choices come from a seeded generator, so the file is the same each time.
'use strict';
const path = require('path');
const { load, stub, stubWhere, moduleText } = require(path.join(__dirname, '..', '..', 'w0', 'oracle', 'amd.js'));

class Evented {
  on(type, fn) { (this._on = this._on || {}); (this._on[type] = this._on[type] || []).push(fn); return { remove() {} }; }
  emit(type, data) { for (const f of ((this._on || {})[type] || [])) f(data); }
}
function declare(bases, props) {
  if (typeof bases === 'string') { [bases, props] = [props, arguments[2]]; }
  const list = Array.isArray(bases) ? bases : bases ? [bases] : [];
  const C = function (...a) { if (Object.prototype.hasOwnProperty.call(props, 'constructor')) props.constructor.apply(this, a); };
  for (const b of list) for (const k of Object.getOwnPropertyNames(b.prototype)) if (k !== 'constructor') C.prototype[k] = b.prototype[k];
  for (const k of Object.keys(props)) if (k !== 'constructor') C.prototype[k] = props[k];
  return C;
}
stub('dojo/_base/declare', declare);
stub('dojo/Evented', Evented);
const on = (t, type, fn) => t.on(type, fn);
on.pausable = (t, type, fn) => { const h = { live: true, pause() { this.live = false; }, resume() { this.live = true; }, remove() {} }; t.on(type, (d) => { if (h.live) fn(d); }); return h; };
stub('dojo/on', on);
stub('mw-data-model/enums/dataStoreTypes', { TABLE: 'table' });
class Store extends Evented {
  constructor(d) { super(); this.data = d; }
  replaceAll(d) { this.data = d; }
  get dataSize() { return { rows: { end: this.data.length - 1 } }; }
}
stub('mw-data-model/DataStore', { create: (d) => new Store(d) });
// MATLAB's ticksEditorEval making nothing of what it is given: the text stays, and is marked.
stub('mw-facade/MatlabFacade', class { feval(o) { o.fevalCallback([{ results: [[], ''] }]); } });
stub('datatoolsservices/DataToolsUtils', { getLogicalValue: (v, d) => (v === undefined ? d : v === true || v === 'true') });
stub('appdesigner_interface/command/PropertySetCommand', class { constructor(a) { captured.push(a); this.result = { then() {} }; } execute() {} });
stub('appdesigner_interface/command/CompositablePropertySetCommand', class { constructor(a) { captured.push(a); this.result = { then() {} }; } execute() {} });
stub('appdesigner_interface/command/CompositePropertySetCommand', class { execute() {} });
stub('appdesigner_interface/command/AddComponentCommand', class {});
stub('appdesigner_interface/command/DeleteComponentCommand', class {});
stub('appdesigner/model/codenamegenerator/CodeNameGeneratorFactory', class { makeModifyCodeNameGenerator(peers) { return { generateCodeNameData: () => peers.map((p) => ({ PeerNodeId: p.id, PropertyValues: {} })) }; } });
stub('appdesigner/util/FigureUtil', { offScreenProcess: (a) => a.targetFunction() });
stubWhere((n) => n.startsWith('dojo/text!'), () => '<div></div>');
const captured = [];

const Names = load('rendererseditors/utils/UniqueNameGenerator');
const ItemsModel = load('rendererseditors/editors/ItemsEditor/ItemsModel');
const TicksModel = load('rendererseditors/editors/TicksEditor/TickDialogTableModel');
const ScaleModel = load('rendererseditors/editors/ScaleColorsEditor/ScaleColorsModel');
const FormatMode = load('rendererseditors/editors/DisplayFormatEditor/DisplayFormatMode');
const ColumnsModel = load('rendererseditors/editors/UITableColumnEditor/UITableColumnEditorModel');
const ColumnsTable = load('rendererseditors/editors/UITableColumnEditor/UITableColumnEditorTableModel');
const DateModel = load('rendererseditors/editors/DateTableEditor/DateModel');
const Factory = load('appdesigner_interface/command/PropertyCommandFactory');
const nls = load('rendererseditors/l10n/nls/app').root;

// A method of a module, as `function(args){body}` closed over the module's own names for its imports.
function method(module, name, closure) {
  const text = moduleText(module);
  const re = new RegExp('[,{}]' + name + '(?::function(?: \\w+)?)?\\(([\\w,]*)\\)\\{');
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
  const names = Object.keys(closure);
  // eslint-disable-next-line no-new-func
  const make = new Function(...names, 'return function(' + m[1] + '){' + text.slice(start + 1, i) + '}');
  return make(...names.map((k) => closure[k]));
}

function rng(seed) { let a = seed >>> 0; return () => { a = (a + 0x6D2B79F5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
const pickFrom = (r, list) => list[Math.floor(r() * list.length)];
const clone = (v) => JSON.parse(JSON.stringify(v));

// --- names ---------------------------------------------------------------------------------------------
function namesScenes() {
  const r = rng(71001);
  const scenes = [];
  const words = ['Item', 'State', 'Option', 'Item 1', 'Item 2', 'Item 12', 'State 3', 'Option 2', 'Option 10', 'Item  4', 'Items', 'Item5', 'x', 'Itemize', 'My Item 3', 'Item 99', 'State', 'Off', 'On', '', 'Item 123456789012345678', 'Option 1 2', 'Item|3'];
  for (let i = 0; i < 400; i++) {
    const items = [];
    const n = Math.floor(r() * 6);
    for (let k = 0; k < n; k++) items.push(pickFrom(r, words));
    const key = pickFrom(r, ['defaultItemName', 'defaultStateName', 'defaultOptionName']);
    scenes.push({ items, key, name: new Names(true, ' ').generateVariableName(items.slice(), nls[key]) });
  }
  return scenes;
}

// --- items ---------------------------------------------------------------------------------------------
const itemsHandle = method('rendererseditors/editors/ItemsEditor/ItemsEditor', '_handleModelUpdate', {});
const postAdd = (g, C) => C - 1;
const postDelete = (g, C) => (C === 0 ? -1 : g > 0 ? g - 1 : 0);
function itemsScenes() {
  const r = rng(71002);
  const kinds = [{ minNumber: 0, defaultNameKey: 'defaultItemName' }, { minNumber: 0, defaultNameKey: 'defaultOptionName' }, { minNumber: 2, defaultNameKey: 'defaultStateName' }, { minNumber: 2, maxNumber: 2 }];
  const scenes = [];
  for (let i = 0; i < 300; i++) {
    const info = pickFrom(r, kinds);
    const count = info.maxNumber ? 2 : Math.floor(r() * 5) + (info.minNumber || 0);
    const items = [];
    for (let k = 0; k < count; k++) items.push(pickFrom(r, ['Item', 'Option', 'State', 'A', 'B']) + ' ' + (k + 1));
    const selected = count === 0 ? -1 : 1 + Math.floor(r() * count);
    const model = new ItemsModel({ items: items.slice(), selectedIndex: selected }, info);
    const commits = [];
    const editor = { _commitValue: (p, b, a) => commits.push([p, clone(a)]) };
    model.on('modelUpdated', (e) => itemsHandle.call(editor, e));
    const ops = [];
    for (let k = 0; k < 10; k++) {
      commits.length = 0;
      const n = model.getItems().length;
      const row = n === 0 ? -1 : Math.floor(r() * (n + 1)) - (r() < 0.1 ? 1 : 0);
      const safe = Math.min(row, n - 1);
      const op = pickFrom(r, ['add', 'add', 'delete', 'edit', 'select']);
      const did = { op, row: safe };
      if (op === 'add') {
        model.addRow(safe);
        did.toSelect = postAdd(safe, model.getItems().length);
      } else if (op === 'delete') {
        if (safe < 0) continue;
        model.deleteRow(safe);
        did.toSelect = postDelete(safe, model.getItems().length);
      } else if (op === 'edit') {
        if (safe < 0) continue;
        did.text = pickFrom(r, ['Item 7', 'Renamed', '', 'A 1', 'State 9']);
        model.updateItems(did.text, safe);
      } else {
        if (safe < 0) continue;
        model.updateSelectedIndex(safe);
      }
      did.items = clone(model.getItems());
      did.selected = model.getSelectedIndex();
      did.commits = clone(commits);
      did.canAdd = model.getItems().length < model.maximumStates;
      did.canDelete = model.getItems().length > model.minimumStates;
      ops.push(did);
    }
    scenes.push({ info, items, selected, ops });
  }
  return scenes;
}

// --- ticks ---------------------------------------------------------------------------------------------
const ticksClosure = { d: nls, S: 'error', y: 'primary' };
const ticksHandle = method('rendererseditors/editors/TicksEditor/TicksDialogEditor', '_handleModelUpdate', ticksClosure);
const ticksFlag = method('rendererseditors/editors/TicksEditor/TicksDialogEditor', '_flagInvalidNumericTicks', ticksClosure);
function ticksScenes() {
  const r = rng(71003);
  const scenes = [];
  for (let i = 0; i < 400; i++) {
    const lim = pickFrom(r, [[0, 100], [0, 1], [-10, 10], [5, 50], [0, 1000]]);
    const step = (lim[1] - lim[0]) / pickFrom(r, [2, 4, 5, 10]);
    const ticks = [];
    for (let t = lim[0]; t <= lim[1] + 1e-9; t = t + step) ticks.push(Number(t.toFixed(5)));
    const manual = r() < 0.4;
    const labels = manual ? ticks.map((t, k) => 'L' + k) : ticks.map(String);
    const fewer = manual && r() < 0.15;
    const info = {
      ticksPropName: 'MajorTicks', labelsPropName: 'MajorTickLabels', ticksMode: 'auto', labelMode: manual ? 'manual' : 'auto', Limits: lim, isNumeric: true,
      ticksValue: ticks.slice(), labelsValue: fewer ? labels.slice(0, 2) : labels.slice(),
    };
    const model = new TicksModel(info);
    const commits = [];
    const editor = {
      _ticksPropName: 'MajorTicks', _labelsPropName: 'MajorTickLabels', _tickLabelsModePropName: 'MajorTickLabelsMode', _ticksModePropName: 'MajorTicksMode',
      _props: { isNumeric: true, MajorTicks: ticks.slice(), MajorTickLabels: labels.slice(), MajorTicksMode: 'auto', MajorTickLabelsMode: info.labelMode },
      _ticksTableModel: model, _errorState: false, _errorPlaceHolder: { innerText: '' }, _plusButton: { set() {} },
      // The component's mode comes back manual once a value is set (_updateEditor's setTickMode, setTickLabelMode).
      _commitValue: (p, b, a) => { commits.push([p, clone(a)]); if (p === 'MajorTicks') model.setTickMode('manual'); else model.setTickLabelMode('manual'); },
      _selectNextRow() {}, _styleCellInColor(b, c) { if (c === 'error') marked.push(b); }, _flagInvalidNumericTicks: ticksFlag,
    };
    let marked = [];
    let toSelect = -1;
    // A row's mark stays until the ticks are checked again; a label's change does not check them.
    model.on('tableModelUpdated', (e) => { if (e.MajorTicks) marked = []; ticksHandle.call(editor, e); });
    model.on('sizeChanged', (b) => { toSelect = b.rowDeleted ? b.rowDeleted - 1 : b.rowAdded ? b.rowAdded : 0; });
    model.on('maxrowsexceeded', () => { editor._errorPlaceHolder.innerText = nls.ticksEditorStebByError; });
    const ops = [];
    for (let k = 0; k < 8; k++) {
      commits.length = 0; toSelect = -1;
      const n = model.getTableData().length;
      const row = n === 0 ? -1 : Math.floor(r() * n);
      const op = pickFrom(r, ['add', 'add', 'delete', 'tick', 'tick', 'label', 'interval']);
      const did = { op, row };
      if (op === 'add') {
        const end = n - 1;
        const at = row >= 0 && row < end ? row : end;
        did.interval = model.getCurrentInterval();
        const offered = at >= 0 && marked.includes(at);
        if (offered) continue;
        model.addRow(at + 1);
      } else if (op === 'delete') {
        if (row < 0) continue;
        model.deleteRow(row);
      } else if (op === 'tick') {
        if (row < 0) continue;
        did.text = pickFrom(r, [String(lim[0] - 1), String(lim[1] + 1), String((lim[0] + lim[1]) / 2), String(lim[0]), 'abc', '', String(lim[1]), '7']);
        // Typing a tick's own number again leaves R2025b's table holding the text, which it marks at the next change; not kept.
        if (Number(did.text) === model._oldTableData[row][0]) continue;
        model._tableData[row][0] = did.text;
        model._handleDataChange({ rangeList: [{ rows: { start: row, end: row }, columns: { start: 0, end: 0, count: 1 } }] });
      } else if (op === 'label') {
        if (row < 0) continue;
        did.text = pickFrom(r, ['Low', 'High', '', 'x']);
        model._tableData[row][1] = did.text;
        model._handleDataChange({ rangeList: [{ rows: { start: row, end: row }, columns: { start: 1, end: 1, count: 1 } }] });
      } else {
        did.step = pickFrom(r, [step, step * 2, (lim[1] - lim[0]) / 1000, 0.5, -1]);
        model.updateInterval(did.step);
      }
      did.rows = clone(model.getTableData());
      did.commits = clone(commits);
      did.error = editor._errorPlaceHolder.innerText;
      did.interval2 = model.getCurrentInterval();
      did.toSelect = toSelect;
      ops.push(did);
    }
    scenes.push({ limits: lim, ticks: info.ticksValue, labels: info.labelsValue, labelMode: info.labelMode, ops });
  }
  return scenes;
}

// --- scale colours ---------------------------------------------------------------------------------------
function scaleScenes() {
  const r = rng(71004);
  const scenes = [];
  const color = () => [r(), r(), r()].map((c) => Math.round(c * 1e6) / 1e6);
  for (let i = 0; i < 250; i++) {
    const range = pickFrom(r, [[0, 100], [0, 1], [-50, 50], ['-Inf', 100], [0, 'Inf'], ['-Inf', 'Inf']]);
    const count = Math.floor(r() * 4);
    const colors = [], limits = [];
    for (let k = 0; k < count; k++) { colors.push(color()); limits.push([k * 10, (k + 1) * 10]); }
    const model = new ScaleModel({ scaleColors: clone(colors), scaleColorLimits: clone(limits), colorsPropName: 'ScaleColors', colorLimitsPropName: 'ScaleColorLimits', range });
    const commits = [];
    model.on('modelUpdated', (y) => { for (const P of ['ScaleColors', 'ScaleColorLimits']) { const e = y.eventData[P]; if (JSON.stringify(e.newValue) !== JSON.stringify(e.oldValue)) commits.push([P, clone(e.newValue)]); } });
    const ops = [];
    for (let k = 0; k < 8; k++) {
      commits.length = 0;
      const n = model.getTableData().length;
      const row = n === 0 ? -1 : Math.floor(r() * n);
      const op = pickFrom(r, ['add', 'add', 'delete', 'color', 'start', 'end']);
      const did = { op, row };
      try {
        if (op === 'add') {
          const at = row === -1 ? model.getTableData().length : row + 1;
          model.setTableData(model.getTableData());
          model.addRow(at);
        } else if (op === 'delete') {
          if (row < 0) continue;
          model.setTableData(model.getTableData());
          model.deleteRow(row);
        } else if (op === 'color') {
          if (row < 0) continue;
          did.color = color();
          model.setTableData(model.getTableData());
          model.updateScaleColor(did.color.map((c) => Math.round(c * 1e4) / 1e4), row);
        } else {
          if (row < 0) continue;
          did.text = pickFrom(r, ['5', '12.5', 'abc', '', '1e2', '-3', '100']);
          model.setTableData(model.getTableData());
          if (op === 'start') model.updateStartValue(did.text, row); else model.updateEndValue(did.text, row);
        }
      } catch (e) { did.threw = e.message; }
      did.colors = clone(model.getScaleColors());
      did.limits = clone(model.getScaleColorLimits());
      did.commits = clone(commits);
      ops.push(did);
    }
    scenes.push({ range, colors, limits, ops });
  }
  return scenes;
}

// --- view format -----------------------------------------------------------------------------------------
function formatScenes() {
  const reads = ['%11.4g', '%.0f', '%.2f', '%.10f', '%e', '%d', '%f', '%5.3f', '%g dB', 'abc', '', '%.f', '%x2f', 'f%.3', '% .4f', '$%.2f', '%.0fm', '%.2e', '%11.4G'];
  const writes = [['DEFAULT', null], ['INTEGER', null], ['SCIENTIFIC', null], ['DECIMAL', '2'], ['DECIMAL', '0'], ['DECIMAL', '10'], ['DECIMAL', '-1'], ['DECIMAL', '1.5'], ['DECIMAL', ''], ['DECIMAL', 'a'], ['CUSTOM', '%d'], ['CUSTOM', 'abc'], ['CUSTOM', '']];
  const out = { reads: [], writes: [] };
  for (const t of reads) { const m = new FormatMode(); m.refresh(t); out.reads.push({ text: t, mode: m.getModeName(), value: m.getModeValue() }); }
  for (const [mode, value] of writes) {
    const m = new FormatMode(mode, value);
    let format = null, threw = null;
    try { format = m.getFormatString(); } catch (e) { threw = e.message; }
    out.writes.push({ mode, value, format, threw });
  }
  return out;
}

// --- columns ----------------------------------------------------------------------------------------------
const colClosure = {};
const editorMethods = {};
for (const n of ['_updateBeforeSendToServer', '_isNumeric', '_allSameValue', '_convertToValidWidth']) editorMethods[n] = method('rendererseditors/editors/UITableColumnEditor/UITableColumnEditor', n, colClosure);
const colHandle = method('rendererseditors/editors/UITableColumnEditor/UITableColumnEditor', '_handleEditorValueChange', colClosure);
const colUpdateTable = method('rendererseditors/editors/UITableColumnEditor/UITableColumnEditor', '_updateTableModelFromEditorModel', colClosure);
function columnScenes() {
  const r = rng(71005);
  const scenes = [];
  for (let i = 0; i < 250; i++) {
    const count = Math.floor(r() * 5);
    const names = [], widths = [], editable = [], sortable = [];
    for (let k = 0; k < count; k++) { names.push('Column ' + (k + 1)); widths.push(pickFrom(r, ['auto', 'auto', 75, 120, '1x', 'fit'])); editable.push(r() < 0.3); sortable.push(r() < 0.3); }
    const withSort = r() < 0.7;
    const info = { ColumnName: names.slice(), ColumnWidth: widths.slice(), ColumnEditable: editable.slice() };
    if (withSort) info.ColumnSortable = sortable.slice();
    const table = new ColumnsTable(clone(info));
    const model = new ColumnsModel(clone(info));
    const commits = [];
    const editor = { _allowedStrings: ['auto', 'fit'], _uitableColumnInfo: clone(info), _commitValue: (p, b, a) => commits.push([p, clone(a)]), _uiTableColumnEditorTableModel: table, ...editorMethods };
    model.on('editorModelUpdated', (e) => colHandle.call(editor, e));
    model.on('editorModelUpdated', (e) => colUpdateTable.call(editor, e));
    table.on('tableModelUpdated', (e) => model.setColumnValues(e.eventData));
    const ops = [];
    for (let k = 0; k < 8; k++) {
      commits.length = 0;
      const n = model.getNameColumn().length;
      const row = n === 0 ? -1 : Math.floor(r() * n);
      const op = pickFrom(r, ['add', 'delete', 'name', 'width', 'auto', 'editable', 'sortable']);
      const did = { op, row };
      const end = n - 1;
      const edit = (col, set) => {
        const data = table.getTableData();
        set(data[row]);
        table._handleDataChange({ rangeList: [{ columns: { start: col }, rows: { start: row } }] });
      };
      if (op === 'add') {
        const at = (row >= 0 && row < end ? row : end) + 1;
        model.addRow(at);
        did.toSelect = at;
      } else if (op === 'delete') {
        if (row < 0) continue;
        model.deleteRow(row);
        did.toSelect = row - 1;
      } else if (op === 'name') {
        if (row < 0) continue;
        did.text = pickFrom(r, ['Voltage', ' Spaced ', '', 'Column 1']);
        edit(0, (d) => { d[0] = did.text; });
      } else if (op === 'width') {
        if (row < 0) continue;
        did.text = pickFrom(r, ['100', '2x', 'fit', 'auto', '', ' 80 ', '12.5px']);
        edit(1, (d) => { d[1] = did.text; });
      } else if (op === 'auto') {
        if (row < 0) continue;
        did.on = r() < 0.5;
        edit(2, (d) => { d[2] = { checked: did.on }; });
      } else if (op === 'editable') {
        if (row < 0) continue;
        did.on = r() < 0.5;
        edit(3, (d) => { d[3] = { checked: did.on }; });
      } else {
        if (row < 0 || !withSort) continue;
        did.on = r() < 0.5;
        edit(4, (d) => { d[4] = { checked: did.on }; });
      }
      did.names = clone(model.getNameColumn());
      did.widths = clone(model.getWidthColumn());
      did.editable = clone(model.getEditableColumn());
      did.sortable = clone(model.getSortableColumn() || []);
      did.cells = clone(table.getTableData().map((c) => [c[1], c[2].checked]));
      did.commits = clone(commits);
      ops.push(did);
    }
    scenes.push({ names, widths, editable, sortable: withSort ? sortable : null, ops });
  }
  return scenes;
}

// --- dates ------------------------------------------------------------------------------------------------
function dateScenes() {
  const r = rng(71006);
  const scenes = [];
  for (let i = 0; i < 150; i++) {
    const dates = [];
    const count = 1 + Math.floor(r() * 4);
    let day = pickFrom(r, [new Date(2024, 1, 27), new Date(2023, 11, 30), new Date(2025, 0, 1), new Date(2025, 5, 29)]);
    for (let k = 0; k < count; k++) { dates.push({ Year: day.getFullYear(), Month: day.getMonth() + 1, Day: day.getDate() }); day = new Date(day.getFullYear(), day.getMonth(), day.getDate() + (r() < 0.6 ? 1 : 5)); }
    const model = new DateModel(clone(dates));
    const ops = [];
    for (let k = 0; k < 6; k++) {
      const n = model.getDisabledDates().length;
      if (n === 0) break;
      const row = Math.floor(r() * n);
      if (r() < 0.7) {
        const to = model.addRow(row);
        ops.push({ op: 'add', row, toSelect: to, dates: clone(model.getDisabledDates()) });
      } else {
        model.deleteRow(row);
        ops.push({ op: 'delete', row, dates: clone(model.getDisabledDates()) });
      }
    }
    scenes.push({ dates, ops });
  }
  return scenes;
}

// --- a mode beside its property -------------------------------------------------------------------------------
function modeScenes() {
  const r = rng(71007);
  const scenes = [];
  const bases = [12, 0, 1.005, 100.125, [0, 100], [0, 20, 40, 60, 80, 100], [0.96, 0.96, 0.96], [0.123456, 2, 3], 'auto', 'Helvetica', '', [], ['0', '20', '40']];
  for (let i = 0; i < 300; i++) {
    const base = pickFrom(r, bases);
    const candidates = [clone(base)];
    if (typeof base === 'number') candidates.push(base + 0.001, base + 0.004, base + 0.006, base + 1);
    else if (Array.isArray(base) && base.length && base.every((x) => typeof x === 'number')) candidates.push(base.map((x) => x + 0.001), base.map((x) => x + 0.006), base.slice(1));
    else if (typeof base === 'string') candidates.push(base + 'x');
    const value = pickFrom(r, candidates);
    const withMode = r() < 0.85;
    const d = { Foo: base };
    if (withMode) d.FooMode = 'auto';
    captured.length = 0;
    const peer = { id: 'p', getType: () => 'T', ComponentAdapter: { getDefaultValues: () => d, DesignGestures: { SkipOffScreen: () => true }, MassEditProperties: [] }, getChildren: () => [] };
    const app = { getPeerNodeById: () => peer, getUndoRedoManager: () => ({ addCommand() {}, on() { return { remove() {} }; } }), getViewState: () => ({ updateCanvas() {} }) };
    new Factory([peer], app).executePropertyEditorCommand({ Foo: value });
    const got = (captured[0] && captured[0].modePropertyValues) || {};
    scenes.push({ base, value, withMode, mode: got.FooMode === undefined ? null : got.FooMode });
  }
  return scenes;
}

// --- sized to its text ------------------------------------------------------------------------------------------
const ctrl = 'appdesigner_interface/controller/AbstractDesignTimeController';
const preferredFit = method(ctrl, 'setComponentToPreferredSize', {});
const preserve = method(ctrl, '_computeSizeToPreserve', {});
const widthBuffer = method('visualcomponents/widgetview/standardcomponents/AbstractStandardComponent', '_getWidthBuffer', {});
const locationFor = method('visualcomponents/widgetview/standardcomponents/AbstractStandardComponent', '_getLocationBasedOnNewSize', {});
function preferredScenes() {
  const r = rng(71008);
  const rules = [[false, false], [true, false], [false, true]];
  const scenes = [];
  for (let i = 0; i < 500; i++) {
    const [preserveWidth, preserveMinimum] = pickFrom(r, rules);
    const minimum = pickFrom(r, [[15, 22], [20, 20], [10, 10], [40, 22]]);
    const made = pickFrom(r, [[100, 22], [84, 22], [31, 22]]);
    const preferred = { width: Math.floor(r() * 200) + 5, height: pickFrom(r, [15, 22, 30, 61]) };
    const hasPrevious = r() < 0.8;
    const previous = hasPrevious ? { width: Math.floor(r() * 200) + 5, height: pickFrom(r, [15, 22, 30]) } : undefined;
    // A component either as its last preferred size left it, or sized by hand.
    let shown;
    if (hasPrevious && r() < 0.5) {
      const w = Math.max(Math.max(previous.width + widthBuffer(previous.width), minimum[0]), preserveMinimum ? made[0] : 0);
      shown = { width: w, height: Math.max(previous.height, minimum[1]) };
    } else {
      shown = { width: Math.floor(r() * 300) + 5, height: pickFrom(r, [22, 30, 80]) };
    }
    const location = [Math.floor(r() * 400), Math.floor(r() * 400)];
    const alignment = pickFrom(r, ['left', 'right', 'center']);
    const wrap = r() < 0.1;
    let set = null;
    const me = {
      widget: { get: (k) => (k === 'preferredSize' ? preferred : shown[k]) },
      _getWidthBuffer: widthBuffer, _getLocationBasedOnNewSize: locationFor, _computeSizeToPreserve: preserve,
      _ViewState: { isViewScaled: () => false },
      _previousPreferredSize: previous ? { ...previous } : undefined,
      WidgetData: { getProperty: (k) => (k === 'WordWrap' ? wrap : k === 'HorizontalAlignment' ? alignment : undefined), ComponentAdapter: { MinimumSize: minimum, PreferredSizing: { AutoFitShrinkWidthMode: { isPreserveWidth: preserveWidth, isPreserveMinimumDefaultWidth: preserveMinimum } }, getDefaultValues: () => ({ Size: made }) } },
      getInnerSizeInPixels: () => [shown.width, shown.height],
      getInnerLocationInPixels: () => location,
      setPositionPropertiesInPixels: (p) => { set = p; },
    };
    preferredFit.call(me);
    scenes.push({ preserveWidth, preserveMinimum, minimum, made, preferred, previous: previous || null, shown, location, alignment, wrap, size: set.Size, at: set.Location });
  }
  return scenes;
}

// --- fields --------------------------------------------------------------------------------------------------------
const fieldsRefused = method('inspector_client/editors/PropertyFieldsEditor', '_isValidationError', { o: {} });
function fieldScenes() {
  const r = rng(71009);
  const out = [];
  for (let i = 0; i < 100; i++) {
    const min = pickFrom(r, [0, 1, 5, -3, 10]);
    const max = pickFrom(r, [0, 1, 5, -3, 10, Infinity]);
    const me = { _validation: ['maxGreaterThanMin'], _showValidation() {} };
    out.push({ min, max: max === Infinity ? 'Inf' : max, refused: fieldsRefused.call(me, 'min', { min, max }, {}) });
  }
  return out;
}

const out = {
  names: namesScenes(),
  items: itemsScenes(),
  ticks: ticksScenes(),
  scale: scaleScenes(),
  format: formatScenes(),
  columns: columnScenes(),
  dates: dateScenes(),
  modes: modeScenes(),
  preferred: preferredScenes(),
  fields: fieldScenes(),
};
process.stdout.write(JSON.stringify(out));
