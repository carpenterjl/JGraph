"""DS1b (designer plan): records each component class's base - what a fresh component of the class
stores in a saved tree, as ds1b_fresh.m saved them (rt/fresh/<Class>__ref.mat) - into
ds1b_base.json, which JGraph's tree writer embeds. A base is the component's stored names in
order, each a word, a literal or a value, and the helper objects it refers to that hold its place
(UnitPos, UnitPosReadOnly), recorded the same way. Left out: what every component saves that is no
part of its state (the plumbing), its children, its Layout options, a tab group's selected tab, and
references to any other object (an axes' rulers and camera, a tree's strategies), which a component
loaded without them makes again. An enumeration member inside a value is recorded as its name. A
stored colour whose Mode the component does not store gets the Mode 'auto' stated, because a tree
written without saved-time defaults would otherwise load the colour as set by hand.
Usage: python ds1b_base.py"""
import glob
import json
import math
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ds1b_mcos import MatFile  # noqa: E402
from ds1b_transcribe import PLUMBING, MODED, refs_of  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
HELPERS = {'matlab.ui.internal.UnitPos', 'matlab.ui.internal.UnitPosReadOnly', 'meta.DynamicProperty'}
STRUCTURE = {'matlab.ui.container.Container.SerializableChildren',
             'matlab.graphics.primitive.world.Group.SerializableChildren', 'Layout', 'SerializableSelectedTab'}
CLASSES = {1: 'cell', 2: 'struct', 4: 'char', 6: 'double', 7: 'single', 8: 'int8', 9: 'uint8', 10: 'int16',
           11: 'uint16', 12: 'int32', 13: 'uint32', 14: 'int64', 15: 'uint64'}
FORMATS = {1: 'b', 2: 'B', 3: 'h', 4: 'H', 5: 'i', 6: 'I', 7: 'f', 9: 'd', 12: 'q', 13: 'Q'}


def number(x):
    if isinstance(x, float):
        if math.isnan(x):
            return 'NaN'
        if math.isinf(x):
            return 'Inf' if x > 0 else '-Inf'
        if x == int(x) and abs(x) < 2 ** 53:
            return int(x)
    return x


def encode(node, t):
    """A stored value as JSON: {c: class, s: size, v: data}; struct fields under f."""
    if node.kind == 'empty':
        return {'c': 'double', 's': [0, 0], 'v': []}
    flags = struct.unpack_from('<I', node.flags[1])[0]
    cls = flags & 0xFF
    size = list(struct.unpack(f'<{len(node.dims[1]) // 4}i', node.dims[1]))
    if node.kind == 'cell':
        return {'c': 'cell', 's': size, 'v': [encode(x, t) for x in node.items]}
    if node.kind == 'struct':
        w = struct.unpack('<i', node.width[1])[0]
        names = [node.names[1][i:i + w].split(b'\0')[0].decode() for i in range(0, len(node.names[1]), w)] if w else []
        if names and names[0] == 'EnumerationInstanceTag':
            word = struct.unpack('<I', node.items[names.index('ValueNames')].rest[0][1][:4])[0]
            text = t.names[word]
            return {'c': 'char', 's': [1, len(text)], 'v': text}
        n = len(names)
        elements = [[encode(x, t) for x in node.items[i:i + n]] for i in range(0, len(node.items), n)] if n else []
        return {'c': 'struct', 's': size, 'f': names, 'v': elements}
    if node.kind != 'plain' or cls not in CLASSES:
        raise ValueError(f'unrecorded value kind {node.kind} class {cls}')
    if flags & 0x800:
        raise ValueError('complex value')
    data = node.rest[0][1] if node.rest else b''
    dtype = node.rest[0][0] if node.rest else 9
    if cls == 4:
        text = data.decode('utf-8') if dtype == 16 else data.decode('utf-16-le')
        return {'c': 'char', 's': size, 'v': text}
    fmt = FORMATS[dtype]
    values = [number(float(v)) if fmt in 'fd' else v for v in struct.unpack(f'<{len(data) // struct.calcsize(fmt)}{fmt}', data)]
    name = 'logical' if flags & 0x200 else CLASSES[cls]
    return {'c': name, 's': size, 'v': values}


def record(m, oid):
    t = m.table
    cls = t.class_name(t.objects[oid][0])
    entries, stored = [], set()
    for n, kind, v in m.props(oid):
        name = t.names[n]
        stored.add(name)
        if name in PLUMBING or name in STRUCTURE:
            continue
        if kind == 0:
            entries.append({'n': name, 'word': t.names[v]})
        elif kind == 2:
            entries.append({'n': name, 'literal': v})
        else:
            node = m.cells[v + 2]
            r = refs_of(node)
            if r is None:
                entries.append({'n': name, 'value': encode(node, t)})
                continue
            ids, _ = r
            if ids and all(t.class_name(t.objects[i][0]) in HELPERS for i in ids):
                entries.append({'n': name, 'refs': [record(m, i) for i in ids]})
    for colour in sorted(MODED.get(cls, ())):
        if colour in stored and colour + 'Mode' not in stored:
            entries.append({'n': colour + 'Mode', 'word': 'auto'})
    out = {'class': cls, 'entries': entries}
    dep = t.objects[oid][5]
    if dep < len(t.dyn) and t.dyn[dep]:
        out['dynamic'] = [record(m, i) for i in t.dyn[dep]]
    return out


# A uifigure gives the containers and tables made in it the values of its default-property map
# (the figure's dynamic property InternalDefaultPropMap) as they are made, with their modes left
# 'auto'. A component loaded from a tree takes them only when the figure is its parent as it loads,
# so the base states them in their _I form (which leaves the mode alone), unless the component
# stores them already. Units and Position are the design's.
FIGURE_DEFAULTS = {'matlab.ui.container.Panel': 'Uipanel', 'matlab.ui.container.ButtonGroup': 'Uibuttongroup',
                   'matlab.ui.container.TabGroup': 'Uitabgroup', 'matlab.ui.container.Tab': 'Uitab',
                   'matlab.ui.control.Table': 'Uitable'}

# Modes a component's stored position turns to 'manual' as it loads without saved-time defaults,
# which a fresh one has 'auto'.
POSITION_MODES = {'matlab.ui.control.UIAxes': ('PositionMode', 'PositionConstraintMode')}

# What a fresh component stores that it worked out from its default size, and works out again when
# it is not stored: an axes' camera and plot box, which would be wrong for any other size.
WORKED_OUT = {'matlab.ui.control.UIAxes': {'CameraPosition_I', 'CameraTarget_I', 'CameraViewAngle_I', 'PlotBoxAspectRatio_I'}}

# A fresh radio or toggle button, alone in its group, is the group's selection. A tree's buttons
# join their group as it loads, each selection taking over the last, so the base leaves a button
# unselected: the generated code sets Value true on the one selected.
UNSELECTED = ('matlab.ui.control.RadioButton', 'matlab.ui.control.ToggleButton')


def figure_defaults(base, figure):
    prefix = 'default' + FIGURE_DEFAULTS[base['class']]
    stored = {e['n'].split('.')[-1] for e in base['entries']}
    mapped = [d for d in figure['dynamic'] if d['entries'][0]['value']['v'] == 'InternalDefaultPropMap'][0]
    value = [e for e in mapped['entries'] if e['n'] == 'DynamicValue_'][0]['value']
    known = ('BorderType', 'FontName', 'FontUnits', 'FontSize', 'AutoResizeChildren', 'HighlightColor', 'EnableLegacyPadding')
    for name, v in zip(value['f'], value['v'][0]):
        prop = name[len(prefix):]
        if not name.startswith(prefix) or prop not in known or prop in stored or prop + '_I' in stored:
            continue
        base['entries'].append({'n': prop + '_I', 'value': v})


# The parts of an axes App Designer's label properties write (TitleString, XLabelString, ...): the
# title and subtitle text objects and the three rulers with their label text, recorded whole from
# an axes given all five (rt/fresh/UIAxesParts.mat), less what an axes works out again. The writer
# puts its own text in each String_I.
AXES_PARTS = ('Title_IS', 'Subtitle_IS', 'XRuler_I', 'YRuler_I', 'ZRuler_I')
# What an axes works out again: the rulers' tick values, and a text's cached position and size in
# other units (which would be the recorded text's, not the design's).
RULER_TICKS = {'NumericTickValues_I', 'TickValues_I', 'TickLabels_I', 'PositionStore', 'FontSizeStore'}


def record_part(m, oid):
    t = m.table
    entries = []
    for n, kind, v in m.props(oid):
        name = t.names[n]
        if name in RULER_TICKS:
            continue
        if kind == 0:
            entries.append({'n': name, 'word': t.names[v]})
        elif kind == 2:
            entries.append({'n': name, 'literal': v})
        else:
            node = m.cells[v + 2]
            r = refs_of(node)
            if r is None:
                entries.append({'n': name, 'value': encode(node, t)})
            elif r[0] and all(t.class_name(t.objects[i][0]) == 'matlab.graphics.primitive.Text' for i in r[0]):
                entries.append({'n': name, 'refs': [record_part(m, i) for i in r[0]]})
    return {'class': t.class_name(t.objects[oid][0]), 'entries': entries}


def axes_parts():
    m = MatFile(os.path.join(HERE, 'rt', 'fresh', 'UIAxesParts.mat'))
    t = m.table
    oid = m.objects_of('matlab.ui.control.UIAxes')[0]
    parts = {}
    for n, kind, v in m.props(oid):
        name = t.names[n]
        if name in AXES_PARTS:
            parts[name] = record_part(m, refs_of(m.cells[v + 2])[0][0])
    return parts


def main():
    bases = []
    for path in sorted(glob.glob(os.path.join(HERE, 'rt', 'fresh', '*__ref.mat'))):
        short = os.path.basename(path).split('__')[0]
        m = MatFile(path)
        t = m.table
        oid = [i for i, o in enumerate(t.objects) if i and t.class_name(o[0]).split('.')[-1] == short][-1]
        bases.append(record(m, oid))
    figure = [b for b in bases if b['class'] == 'matlab.ui.Figure'][0]
    for b in bases:
        if b['class'] in FIGURE_DEFAULTS:
            figure_defaults(b, figure)
        for mode in POSITION_MODES.get(b['class'], ()):
            b['entries'].append({'n': mode, 'word': 'auto'})
        b['entries'] = [e for e in b['entries'] if e['n'] not in WORKED_OUT.get(b['class'], set())]
        if b['class'] == 'matlab.ui.control.UIAxes':
            b['parts'] = axes_parts()
        if b['class'] in UNSELECTED:
            for e in b['entries']:
                if e['n'] == 'PrivateValue':
                    e['value'] = {'c': 'logical', 's': [1, 1], 'v': [0]}
    bases.sort(key=lambda b: b['class'])
    with open(os.path.join(HERE, 'ds1b_base.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'release': '2025b', 'classes': bases}, f, separators=(',', ':'))
    print(len(bases), 'classes,', os.path.getsize(os.path.join(HERE, 'ds1b_base.json')), 'bytes')


if __name__ == '__main__':
    main()
