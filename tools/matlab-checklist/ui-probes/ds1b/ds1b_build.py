"""DS1b (designer plan): writes a component tree from scratch as a level-5 MAT-file holding the
variable `components` (a struct whose field UIFigure is the figure), the reference for JGraph's
writer. An object is a class name and an ordered list of stored names, each holding a word (a name
from the table), a literal, a value (a ds1b_mcos node) or references to other objects; plus its
dynamic properties (meta.DynamicProperty objects). Every class's saved-time defaults are written
empty, so what loads is only what is stored."""
import struct

from ds1b_mcos import MI_INT8, MI_INT32, MI_UINT32, Node, emit_matrix, uint8_node

REF_MARK = 0xDD000000
# a class saved under another name as well (the per-class int32 column)
ALIASES = {}


class Obj:
    def __init__(self, cls, entries=None, dynamic=None):
        self.cls = cls
        self.entries = list(entries or [])  # (name, kind, payload): kind 'word' | 'literal' | 'value' | 'refs'
        self.dynamic = list(dynamic or [])

    def word(self, name, word):
        self._put(name, 'word', word)
        return self

    def literal(self, name, number):
        self._put(name, 'literal', number)
        return self

    def value(self, name, node):
        self._put(name, 'value', node)
        return self

    def refs(self, name, objs, cls=None):
        self._put(name, 'refs', (list(objs), cls))
        return self

    def _put(self, name, kind, payload):
        for i, e in enumerate(self.entries):
            if e[0] == name:
                self.entries[i] = (name, kind, payload)
                return
        self.entries.append((name, kind, payload))


def struct_node(fields, dims=(1, 1)):
    """A struct with the given fields (name -> node) in order; dims (1,1) unless given."""
    names = list(fields)
    width = max([len(n) for n in names] + [0]) + 1
    packed = b''.join(n.encode() + b'\0' * (width - len(n)) for n in names)
    return Node(kind='struct', flags=(MI_UINT32, struct.pack('<II', 2, 0)),
                dims=(MI_INT32, struct.pack('<ii', *dims)), name=(MI_INT8, b''),
                width=(MI_INT32, struct.pack('<i', width)), names=(MI_INT8, packed),
                items=list(fields.values()))


def int32_column(values):
    return Node(kind='plain', flags=(MI_UINT32, struct.pack('<II', 12, 0)),
                dims=(MI_INT32, struct.pack('<ii', len(values), 1)), name=(MI_INT8, b''),
                rest=[(MI_INT32, struct.pack(f'<{len(values)}i', *values))])


def uint32_column(values):
    return Node(kind='plain', flags=(MI_UINT32, struct.pack('<II', 13, 0)),
                dims=(MI_INT32, struct.pack('<ii', len(values), 1)), name=(MI_INT8, b''),
                rest=[(MI_UINT32, struct.pack(f'<{len(values)}I', *values))])


def empty_node():
    return Node(kind='empty')


class Writer:
    def __init__(self):
        self.names = ['']
        self.classes = [(0, 0)]
        self.class_ids = {}
        self.cells = []

    def name(self, text):
        if text not in self.names:
            self.names.append(text)
        return self.names.index(text)

    def class_id(self, cls):
        if cls not in self.class_ids:
            package, _, short = cls.rpartition('.')
            self.classes.append((self.name(package) if package else 0, self.name(short)))
            self.class_ids[cls] = len(self.classes) - 1
        return self.class_ids[cls]

    def class_names(self):
        by_id = {v: k for k, v in self.class_ids.items()}
        return [by_id.get(i, '') for i in range(len(self.classes))]

    def ref(self, objs, cls, ids):
        cid = self.class_id(cls if cls else objs[0].cls)
        n = len(objs)
        dims = (1, n) if n else (0, 0)
        return uint32_column([REF_MARK, 2, dims[0], dims[1]] + [ids[id(o)] for o in objs] + [cid])

    def write(self, path, root, field='UIFigure', variable='components'):
        # objects, depth first from the root
        order, ids = [], {}

        def visit(o):
            if id(o) in ids:
                return
            ids[id(o)] = len(order) + 1
            order.append(o)
            for _, kind, payload in o.entries:
                if kind == 'refs':
                    for x in payload[0]:
                        visit(x)
            for d in o.dynamic:
                visit(d)

        visit(root)
        plain = [[]]
        for o in order:
            self.class_id(o.cls)
        for o in order:
            lst = []
            for name, kind, payload in o.entries:
                if kind == 'word':
                    lst.append((self.name(name), 0, self.name(payload)))
                elif kind == 'literal':
                    lst.append((self.name(name), 2, payload))
                elif kind == 'value':
                    self.cells.append(payload)
                    lst.append((self.name(name), 1, len(self.cells) - 1))
                else:
                    self.cells.append(self.ref(payload[0], payload[1], ids))
                    lst.append((self.name(name), 1, len(self.cells) - 1))
            plain.append(lst)
        objects = [(0, 0, 0, 0, 0, 0)] + [(self.class_id(o.cls), 0, 0, 0, i, i) for i, o in enumerate(order, 1)]
        dynamic = [[]] + [[ids[id(d)] for d in o.dynamic] for o in order]
        nclasses = len(self.classes)
        empty_struct = lambda: struct_node({}, dims=(1, 0))
        aliases = [self.name(ALIASES[c]) if c in ALIASES else 0 for c in self.class_names()]
        table = self.table(objects, plain, dynamic)
        cells = ([uint8_node(table), empty_node()] + self.cells
                 + [cell_column([empty_struct() for _ in range(nclasses)]),
                    int32_column(aliases),
                    cell_column([empty_struct() for _ in range(nclasses)])])
        wrapper = Node(kind='opaque', flags=(MI_UINT32, struct.pack('<II', 17, 0)), name=(MI_INT8, b''),
                       ts=(MI_INT8, b'MCOS'), cn=(MI_INT8, b'FileWrapper__'), content=cell_column(cells))
        subsystem = struct_node({'MCOS': wrapper})
        blob = b'\x00\x01IM\x00\x00\x00\x00' + emit_matrix(subsystem) + TRAILER
        figure = Node(kind='opaque', flags=(MI_UINT32, struct.pack('<II', 17, 0)), name=(MI_INT8, b''),
                      ts=(MI_INT8, b'MCOS'), cn=(MI_INT8, root.cls.encode()),
                      content=uint32_column([REF_MARK, 2, 1, 1, 1, self.class_id(root.cls)]))
        var = struct_node({field: figure})
        var.name = (MI_INT8, variable.encode())
        body = emit_matrix(var)
        header = (b'MATLAB 5.0 MAT-file, Platform: PCWIN64, Created on: Sat Oct 18 00:00:00 2026'
                  .ljust(116, b' ') + struct.pack('<Q', 128 + len(body)) + struct.pack('<H', 0x0100) + b'IM')
        with open(path, 'wb') as f:
            f.write(header + body + emit_matrix(uint8_node(blob, row=True)))

    def table(self, objects, plain, dynamic):
        names = b''.join(n.encode() + b'\0' for n in self.names[1:])
        names += b'\0' * ((-(40 + len(names))) % 8)
        segs = [b''.join(struct.pack('<4I', p, n, 0, 0) for p, n in self.classes), b'',
                b''.join(struct.pack('<6I', *o) for o in objects), lists(plain),
                id_lists(dynamic), b'', struct.pack('<2I', 0, 0)]
        offs, at = [], 40 + len(names)
        for s in segs:
            offs.append(at)
            at += len(s)
        offs.append(at)
        head = struct.pack('<II', 4, len(self.names) - 1) + struct.pack('<8I', *offs)
        return head + names + b''.join(segs) + struct.pack('<4I', 1, offs[7] + 8, 0, 0)


def lists(lsts):
    out = b''
    for lst in lsts:
        e = struct.pack('<I', len(lst)) + b''.join(struct.pack('<3I', *t) for t in lst)
        e += b'\0' * ((-len(e)) % 8)
        out += e
    return out


def id_lists(lsts):
    out = b''
    for ids in lsts:
        e = struct.pack(f'<{1 + len(ids)}I', len(ids), *ids)
        e += b'\0' * ((-len(e)) % 8)
        out += e
    return out


def cell_column(items):
    return Node(kind='cell', flags=(MI_UINT32, struct.pack('<II', 1, 0)),
                dims=(MI_INT32, struct.pack('<ii', len(items), 1)), name=(MI_INT8, b''), items=items)


def _trailer():
    """The second element of every subsystem MATLAB writes: a uint8 stream holding a struct whose MCOS field is empty."""
    inner = struct_node({'MCOS': empty_node()})
    stream = b'\x00\x01IM\x00\x00\x00\x00' + emit_matrix(inner)
    return emit_matrix(uint8_node(stream, row=True))


TRAILER = _trailer()
