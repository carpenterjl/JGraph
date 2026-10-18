"""DS1b (designer plan): rewrites MATLAB-saved component trees from scratch with ds1b_build, keeping
only the components (the classes in ds1_catalog.json), each component's stored names that hold
plain values (less the plumbing every component saves), the references that link a component to
its children, its Layout options, its position stores and its dynamic properties. Every other helper
object, and every class's saved-time defaults, is left out; a colour's Mode is stated where the tree
stores the colour without one; an enumeration member inside a value becomes its name. ds1b_compare then loads each beside the tree it came from.
Usage: python ds1b_transcribe.py <out subfolder> <file.mat> [...]  (each written as <name>__x.mat
beside a copy <name>__ref.mat)"""
import copy
import json
import os
import shutil
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ds1b_build import REF_MARK, Obj, Writer  # noqa: E402
from ds1b_mcos import MatFile  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
CATALOG = json.load(open(os.path.join(HERE, '..', 'ds1', 'ds1_catalog.json'), encoding='utf-8'))
COMPONENTS = {c['type'] for c in CATALOG['classes']}
MODED = {c['type']: {p['name'] for p in c['properties'] if p['mode']} for c in CATALOG['classes']}
PLUMBING = {'Internal', 'SerializableApplicationData', 'Copyable', 'CustomThemePropertyMappings',
            'appdesservices.internal.interfaces.model.AbstractModel.DirtyPropertyStrategy',
            'appdesservices.internal.interfaces.model.AbstractModel.DirtyProperties',
            'matlab.graphics.internal.GraphicsCoreProperties.TopLevelSerializedObject', 'BeingDeleted'}
KEPT_HELPERS = {'matlab.ui.layout.GridLayoutOptions', 'meta.DynamicProperty', 'matlab.ui.internal.UnitPos',
                'matlab.ui.internal.UnitPosReadOnly'}


def refs_of(node):
    """Object ids and class id of a reference node, or None."""
    if node.kind != 'plain' or not node.rest or node.rest[0][0] != 6:
        return None
    data = node.rest[0][1]
    words = struct.unpack(f'<{len(data) // 4}I', data)
    if len(words) < 4 or words[0] != REF_MARK:
        return None
    nd = words[1]
    count = 1
    for d in words[2:2 + nd]:
        count *= d
    return list(words[2 + nd:2 + nd + count]), words[-1]


def plain_copy(node, t):
    """A copy of a stored value with each enumeration member (a struct MATLAB writes with the fields
    EnumerationInstanceTag, ClassName, ValueNames, ...) replaced by its member's name as char."""
    from ds1b_mcos import char_node
    node = copy.deepcopy(node)
    if node.kind == 'struct':
        w = struct.unpack('<i', node.width[1])[0]
        names = [node.names[1][i:i + w].split(b'\0')[0].decode() for i in range(0, len(node.names[1]), w)] if w else []
        if names and names[0] == 'EnumerationInstanceTag':
            word = struct.unpack('<I', node.items[names.index('ValueNames')].rest[0][1][:4])[0]
            return char_node(t.names[word])
        node.items = [plain_copy(x, t) for x in node.items]
    elif node.kind == 'cell':
        node.items = [plain_copy(x, t) for x in node.items]
    return node


def transcribe(m):
    t = m.table
    made = {}

    def make(oid):
        if oid in made:
            return made[oid]
        cls = t.class_name(t.objects[oid][0])
        o = Obj(cls)
        made[oid] = o
        stored = set()
        for n, kind, v in m.props(oid):
            name = t.names[n]
            stored.add(name)
            if name in PLUMBING:
                continue
            if kind == 0:
                o.word(name, t.names[v])
            elif kind == 2:
                o.literal(name, v)
            else:
                node = m.cells[v + 2]
                r = refs_of(node)
                if r is None:
                    o.value(name, plain_copy(node, t))
                    continue
                ids, _ = r
                kept = [i for i in ids if t.class_name(t.objects[i][0]) in COMPONENTS | KEPT_HELPERS]
                if kept and len(kept) == len(ids):
                    o.refs(name, [make(i) for i in kept])
        for colour in MODED.get(cls, ()):
            if colour in stored and colour + 'Mode' not in stored:
                o.word(colour + 'Mode', 'auto')
        dep = t.objects[oid][5]
        if dep < len(t.dyn):
            o.dynamic = [make(i) for i in t.dyn[dep]]
        return o

    root = [i for i, o in enumerate(t.objects) if i and t.class_name(o[0]) == 'matlab.ui.Figure'][0]
    return make(root)


def main():
    sub = sys.argv[1]
    out = os.path.join(HERE, 'rt', sub)
    os.makedirs(out, exist_ok=True)
    for path in sys.argv[2:]:
        name = os.path.basename(path).replace('__ref.mat', '').replace('.mat', '')
        shutil.copy(path, os.path.join(out, f'{name}__ref.mat'))
        Writer().write(os.path.join(out, f'{name}__x.mat'), transcribe(MatFile(path)))
        print(name, end=' ')
    print()


if __name__ == '__main__':
    main()
